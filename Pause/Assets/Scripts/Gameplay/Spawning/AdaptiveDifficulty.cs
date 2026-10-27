using UnityEngine;

// Adaptive spawn density (docs/adaptive-difficulty.md). The PerformanceMeter's
// flow value becomes bounded multipliers on how OFTEN ordinary hazards and
// pickups spawn. It never touches enemy hit points, damage, attacks, hitboxes,
// world speed, or the authored elite and boss beats.
public static class AdaptiveDifficulty
{
    // ---- named bounds (the whole feature lives inside these) ----
    public const float EnemyMin = .75f, EnemyMax = 1.25f;       // ordinary hazard / enemy spawn rate
    public const float DustMin = .85f, DustMax = 1.35f;         // star dust cluster rate (never below .85: the economy)
    public const float RecoveryMax = 1.4f;                      // red/green atom timers run up to this fast when struggling
    public const float LaneFollowMax = .5f;                     // share of a star's x pulled toward the ship when doing well
    public const float LogStep = .05f;                          // [FLOW] line when a multiplier moves this much

    public static readonly PerformanceMeter Meter = new PerformanceMeter();

    // Per-run gate, decided at run start: off in the tutorial/practice and for
    // the premium Paragon ship. Tests may set it directly.
    public static bool RunActive;

    // Tests: stand in for the live authored-beat state (null = ask the game).
    public static bool? HeldOverride;

    static float lastLoggedEnemy = 1f, lastLoggedDust = 1f;
    static int lastWorld = -1;

    public static void BeginRun(bool scoresForReal, bool premiumShip)
    {
        Meter.Reset();
        RunActive = scoresForReal && !premiumShip;
        lastLoggedEnemy = lastLoggedDust = 1f;
        lastWorld = -1;
#if UNITY_EDITOR || PAUSE_DEV
        if (RunActive && Application.isPlaying && DeveloperUnlocks.Enabled) AdaptiveFlowOverlay.Create();
#endif
    }

    public static void Reset() { Meter.Reset(); RunActive = false; HeldOverride = null; lastLoggedEnemy = lastLoggedDust = 1f; lastWorld = -1; }

    public static bool On { get { return RunActive; } }

    // An authored beat (boss, elite fight, planetfall, lift-off, portal wait,
    // a world's entry) is running: the meter stands still and the multipliers
    // read neutral, so the beat is the same for everyone.
    public static bool Held
    {
        get
        {
            if (HeldOverride.HasValue) return HeldOverride.Value;
            return BossEncounter.Running || BossEncounter.ScriptedEntrance || EliteFight.Active ||
                   Planetfall.SuspendsSpawning || Liftoff.SuspendsSpawning || PortalArrival.SuspendsSpawning ||
                   WorldEntry.Active || PortalPressure.Active || buttonClicks.playerDied;
        }
    }

    public static float Flow { get { return On ? Meter.Flow : 0f; } }

    // Multiplier on ordinary hazard / enemy spawn RATE (interval divided by it).
    public static float EnemyScale { get { return On ? EnemyScaleFor(Meter.Flow, Held) : 1f; } }
    // Multiplier on star dust cluster rate.
    public static float DustScale { get { return On ? DustScaleFor(Meter.Flow, Held) : 1f; } }
    // Struggling only: speed-up of the red (pause) and green (heal) atom timers. Their per-world budgets are unchanged.
    public static float RecoveryScale { get { return On ? RecoveryScaleFor(Meter.Flow, Held) : 1f; } }
    // Doing well only: how far a star's x is pulled toward the ship.
    public static float LaneFollow { get { if (!On) return 0f; float f = Meter.Flow; return f <= 0f || Held ? 0f : f * LaneFollowMax; } }

    public static float EnemyScaleFor(float flow, bool held)
    {
        if (held) return 1f;
        return Mathf.Clamp(1f + (EnemyMax - 1f) * Mathf.Clamp(flow, 0f, 1f) + (1f - EnemyMin) * Mathf.Clamp(flow, -1f, 0f), EnemyMin, EnemyMax);
    }

    public static float DustScaleFor(float flow, bool held)
    {
        if (held) return 1f;
        return Mathf.Clamp(1f + (DustMax - 1f) * Mathf.Clamp(flow, 0f, 1f) + (1f - DustMin) * Mathf.Clamp(flow, -1f, 0f), DustMin, DustMax);
    }

    public static float RecoveryScaleFor(float flow, bool held)
    {
        if (held || flow >= 0f) return 1f;
        return Mathf.Clamp(1f + (RecoveryMax - 1f) * -flow, 1f, RecoveryMax);
    }

    // ---- events ----
    public static void OnKill() { if (RunActive) Meter.OnKill(); }
    public static void OnHeartTaken() { if (RunActive) Meter.OnHit(); }
    public static void OnRevive() { if (RunActive) Meter.OnRevive(); }

    // A running frame (score.StepRunning).
    public static void Tick(float dt, int chain, int lifeCounter, int maxLife)
    {
        if (!RunActive) return;
        int world = WorldManager.Instance != null ? WorldManager.CurrentIndex : 0;
        if (lastWorld < 0) lastWorld = world;
        else if (world != lastWorld) { lastWorld = world; Meter.OnWorldChange(); }
        Meter.Frozen = Held;
        Meter.Tick(dt, chain, maxLife - lifeCounter, maxLife);
        float e = EnemyScale, d = DustScale;
        if (Mathf.Abs(e - lastLoggedEnemy) > LogStep || Mathf.Abs(d - lastLoggedDust) > LogStep)
        {
            lastLoggedEnemy = e; lastLoggedDust = d;
            Debug.Log("[FLOW] flow=" + Meter.Flow.ToString("0.00") + " enemies x" + e.ToString("0.00") +
                      " dust x" + d.ToString("0.00") + " recovery x" + RecoveryScale.ToString("0.00"));
        }
    }
}
