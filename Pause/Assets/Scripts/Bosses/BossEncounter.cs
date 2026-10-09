using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// The end-of-level boss fight.
//
// WorldManager calls Begin() when a world's level clock runs out, where the
// portal used to open; the callback it passes opens the portal once the
// encounter is over. All tuning is in BossConfig, the bosses in BossCatalog.
//
//   Pending  waits out an ultimate cinematic already running
//   Intro    BossConfig.IntroSeconds of real time. The world is frozen
//            (moveBackGround asks ScriptedFreeze) even with a finger down,
//            and it is scripted, so it never spends a pause (score asks
//            FreePress). Warning flash, on-screen hazards blown away, the
//            boss warps in, its name card; speed drains to zero.
//   Fight    speed held at FightSpeed: moveBackGround skips its ramp and the
//            blue atom's +0.05 is filtered (SpeedLocked). Normal enemy
//            spawning is off (SuspendsSpawning). Pausing, teleporting and
//            damage all work as usual.
//            Its health shows as BossConfig.Hearts hearts spinning round it
//            (BossHearts): each one an equal share (HeartWeight) of its hit
//            points, gone when that share is spent; the attack phases follow
//            the hearts as well as the clock (PhaseProgress01).
//   Outro    the boss explodes (its hit points ran out) or retreats (the
//            fight clock ran out first) -- whichever came first (BossEndRule).
//   Done     everything above is released and the portal opens.
//   Aborted  the player died; the normal death panel takes over.
//
// Shared code only ever reads the static hooks below, so a scene with no
// encounter (the tutorial, the menus) behaves exactly as before.
[DefaultExecutionOrder(-200)]
public class BossEncounter : MonoBehaviour
{
    public enum Phase { Idle, Pending, Intro, Fight, Outro, Done, Aborted }

    public static BossEncounter Instance { get; private set; }

    // The world whose boss has been dealt with this run (one per world visit).
    static int doneWorld = -1;

    Phase state = Phase.Idle;
    BossDef boss;
    int world = -1;
    Action onFinished;

    float introClock, preSpeed;
    bool warned, arrived, carded;
    float remaining, fightClock;
    int hits, hp;
    float damage;       // every hit's weight, summed (the hearts are read off it)
    int heartsLeft;
    bool freePress;
    float outroClock;
    float rushClock;
    // The fight's free-shot atoms (BossFreeShotAtoms): when each is due.
    readonly float[] freeAtomTimes = new float[BossFreeShotAtoms.Count];
    int freeAtomCount, freeAtomNext;

    BossActor actor;
    BossProjectilePool pool;
    BossIntroUI ui;
    Transform player;

    // ---- hooks for shared code --------------------------------------------

    public static bool Running => Instance != null && Instance.IsRunning;
    // moveBackGround: hold timeScale at 0 regardless of the finger -- the
    // intro.
    public static bool ScriptedFreeze => Instance != null && Instance.state == Phase.Intro;
    // moveBackGround (ramp) and collisionDetection (atom boost): hands off speed.
    public static bool SpeedLocked => Instance != null &&
        (Instance.state == Phase.Intro || Instance.state == Phase.Fight || Instance.state == Phase.Outro);
    // enmiesOnBoard: no normal enemy spawns during the encounter.
    public static bool SuspendsSpawning => Running;
    // score: a press during the intro, or the first one after it, is not a
    // spent pause -- the freeze was the boss's, not the player's.
    public static bool FreePress => Instance != null && Instance.freePress;

    // spawnGoodStuff: true when a fight free-shot atom is due now (and the
    // boss is still there to shoot at); the caller then releases one. Never
    // true outside the fight, nor once the boss is destroyed or out of clock.
    public static bool FreeAtomDue => Instance != null && Instance.IsFreeAtomDue;
    public static void FreeAtomReleased() { if (Instance != null) Instance.freeAtomNext++; }
    bool IsFreeAtomDue => state == Phase.Fight && hp > 0 && remaining > 0f &&
        freeAtomNext < freeAtomCount && fightClock >= freeAtomTimes[freeAtomNext];
    public int FreeAtomsPlanned => freeAtomCount;
    public int FreeAtomsReleased => freeAtomNext;
    public float FightClock => fightClock;

    public static bool DoneInWorld(int worldIndex) => doneWorld == worldIndex;

    // A loop: every boss comes round again on the next pass.
    public static void ForgetDone() { doneWorld = -1; }

    // Developer (BossDev.TriggerFinal): the next fight lasts DevShortFightSeconds.
    public static bool DevShortFight;
    public const float DevShortFightSeconds = 1.5f;

    // ---- state ------------------------------------------------------------

    public Phase State => state;
    public BossDef Boss => boss;
    public BossActor Actor => actor;
    public BossProjectilePool Pool => pool;
    public float Remaining => remaining;
    public int Hits => hits;
    public int HitPointsLeft => hp;
    // Hearts left (BossConfig.Hearts at the start; 0 exactly when destroyed).
    public int HeartsLeft => heartsLeft;
    public float Damage => damage;
    // The boss's hit points ran out (DESTROYED), as opposed to the fight
    // clock (SURVIVED). Meaningful from the outro on.
    public bool Destroyed => hp <= 0;
    public bool IsRunning => state == Phase.Pending || state == Phase.Intro ||
                             state == Phase.Fight || state == Phase.Outro;
    public float Progress01 => BossConfig.FightSeconds <= 0f ? 1f
        : Mathf.Clamp01(1f - remaining / BossConfig.FightSeconds);
    // The hearts' share of the fight: the share of them lost.
    public float HeartProgress01 => BossConfig.Hearts <= 0 ? 0f
        : Mathf.Clamp01((BossConfig.Hearts - heartsLeft) / (float)BossConfig.Hearts);
    // What the attack phases go by (BossCatalog.UnlockedAttacks, FinalPhase:
    // thirds): the clock or the hearts, whichever is further on. 5-4 hearts
    // phase 1, 3-2 phase 2, the last one phase 3 -- or later, by the clock.
    public float PhaseProgress01 => Mathf.Max(Progress01, HeartProgress01);
    // 1, 2 or 3 (before any later loop's head start, LoopRules).
    public int FightPhase => PhaseProgress01 >= 2f / 3f ? 3 : PhaseProgress01 >= 1f / 3f ? 2 : 1;

    // Starts the world's boss. False when there is nothing to start: it was
    // already beaten (or survived) on this visit, or one is running.
    public static bool Begin(int worldIndex, Action finished)
    {
        if (DoneInWorld(worldIndex)) return false;
        var e = Ensure();
        if (e.IsRunning) return false;
        e.Setup(worldIndex, finished);
        return true;
    }

    public static BossEncounter Ensure()
    {
        if (Instance != null) return Instance;
        // Set here as well as in Awake: edit-mode AddComponent skips Awake.
        Instance = new GameObject("~BossEncounter").AddComponent<BossEncounter>();
        return Instance;
    }

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Teardown();
    }

    void Setup(int worldIndex, Action finished)
    {
        Teardown();
        world = worldIndex;
        boss = BossCatalog.ForWorld(worldIndex);
        onFinished = finished;
        state = Phase.Pending;
        introClock = 0f;
        warned = arrived = carded = false;
        hits = 0;
        hp = BossConfig.HitPoints;
        hitBank = 0f;
        damage = 0f;
        heartsLeft = Mathf.Max(1, BossConfig.Hearts);
        remaining = BossConfig.FightSeconds;
        fightClock = 0f;
        freeAtomCount = freeAtomNext = 0;
        freePress = false;
    }

    void Update()
    {
        Step(Mathf.Min(Time.unscaledDeltaTime, .1f), Time.timeScale);
    }

    // The first press after the intro has been seen by score this frame.
    void LateUpdate()
    {
        if (freePress && state != Phase.Intro && TouchInput.IsPressed) freePress = false;
    }

    // One frame. realDt is unscaled; the world's time is realDt * timeScale,
    // so a frozen world (timeScale 0) freezes the fight and its projectiles.
    public void Step(float realDt, float timeScale)
    {
        float dt = realDt * Mathf.Max(0f, timeScale);

        if (buttonClicks.playerDied && (state == Phase.Pending || state == Phase.Intro || state == Phase.Fight))
            Abort();

        switch (state)
        {
            // Done too: after a boss, the next world's rushes.
            case Phase.Idle: case Phase.Done: TickDevRush(dt); break;
            case Phase.Pending:
                if (!ShipPowerController.CinematicClearActive) StartIntro();
                break;
            case Phase.Intro: TickIntro(realDt); break;
            case Phase.Fight: TickFight(dt, realDt); break;
            case Phase.Outro: TickOutro(dt); break;
        }
        if (pool != null) pool.Step(dt);
    }

    // ---- intro ------------------------------------------------------------

    void StartIntro()
    {
        state = Phase.Intro;
        introClock = 0f;
        SpeedRamp.CancelBoost();   // the boss holds the speed: no limit break through its intro
        preSpeed = moveBackGround.speed;
        freePress = true;
        actor = BossActor.Spawn(boss);
        BossRails.Measure();   // the walls boss shots ricochet off or splash on
        pool = new BossProjectilePool(BossConfig.ProjectilePoolMax, BossConfig.BeamPoolMax);
        BossArt.ShotRim(boss, BossArt.Bolt0);   // build the shots' rims now, not at the first volley
        if (Application.isPlaying) ui = BossIntroUI.Play(boss);
        WorldMusic.BeginBoss(WorldManager.Worlds[Mathf.Clamp(world, 0, WorldManager.Worlds.Length - 1)]);
        // First sight unlocks the secret codex entry.
        Codex.Discover(boss.id);
    }

    void TickIntro(float realDt)
    {
        introClock += realDt;
        float drain = Mathf.Clamp01(introClock / Mathf.Max(.01f, BossConfig.SpeedDrainSeconds));
        moveBackGround.speed = Mathf.Lerp(preSpeed, 0f, drain);

        if (!warned && introClock >= BossConfig.WarningAt)
        {
            warned = true;
            ClearHazards();
        }
        if (!arrived && introClock >= BossConfig.BossArriveAt)
        {
            arrived = true;
            actor.BeginArrival();
        }
        if (arrived) actor.StepArrival(realDt);
        if (!carded && introClock >= BossConfig.NameCardAt) carded = true;

        if (introClock >= BossConfig.IntroSeconds) BeginFight();
    }

    // The boss's arrival blows everything already on the board away, so the
    // fight starts on a clean screen. The blasts hang frozen with the world
    // until the fight starts -- time really has stopped.
    void ClearHazards()
    {
        int ship = ShipId.Equipped();
        foreach (var t in FindObjectsByType<ClearTarget>(FindObjectsSortMode.None))
        {
            if (t == null || !ClearTarget.IsHazard(t.gameObject)) continue;
            var go = t.gameObject;
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 v = cam.WorldToViewportPoint(go.transform.position);
                if (v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f && Application.isPlaying)
                    TargetExplosion.Spawn(go, ship);
            }
            ClearTarget.Release(go);
            BossUtil.Kill(go);
        }
    }

    // ---- fight ------------------------------------------------------------

    public void BeginFight()
    {
        if (state != Phase.Intro && state != Phase.Pending) return;
        if (state == Phase.Pending) StartIntro();
        state = Phase.Fight;
        moveBackGround.speed = BossConfig.FightSpeed;
        remaining = BossConfig.FightSeconds;
        if (DevShortFight)
        {
            DevShortFight = false;
            remaining = Mathf.Min(remaining, DevShortFightSeconds);
        }
        fightClock = 0f;
        freeAtomCount = BossFreeShotAtoms.Roll(remaining, freeAtomTimes);
        freeAtomNext = 0;
        actor.BeginFight();
        if (TouchInput.IsPressed) freePress = false;
    }

    void TickFight(float dt, float realDt)
    {
        moveBackGround.speed = BossConfig.FightSpeed;
        remaining -= dt;
        fightClock += dt;
        actor.StepFight(dt, realDt, PlayerPosition(), PhaseProgress01, pool);

        // Whichever comes first: hit points (DESTROYED) or the clock
        // (SURVIVED). Both on the same frame: BeginOutro sees hp <= 0 and
        // the boss is destroyed.
        if (hp <= 0 || remaining <= 0f) BeginOutro();
    }

    Vector3 PlayerPosition()
    {
        if (player == null)
        {
            var mover = FindFirstObjectByType<movePlayer>();
            if (mover != null) player = mover.transform;
        }
        return player != null ? player.position : new Vector3(0f, -3.5f, 0f);
    }

    // A homing shot from the ultimate landed on the boss (BossTarget).
    public void OnUltimateHit() => OnShipAttackHit(1f);

    // Any ship attack landed (BossTarget.TakeShipAttack). `weight` is the
    // share of one full ultimate hit (one firing never totals more than 1).
    // Weights bank up and the boss loses a hit point per whole hit; the
    // fight clock is left alone (BossEndRule).
    // Each heart is HeartWeight of it (0.6): a heart goes the moment its
    // share is spent, the last with the last hit point.
    float hitBank;
    public void OnShipAttackHit(float weight)
    {
        OnShipAttackHit(weight, actor != null ? actor.transform.position + Vector3.down : Vector3.zero);
    }

    public void OnShipAttackHit(float weight, Vector3 at)
    {
        if (state != Phase.Fight || weight <= 0f) return;
        hits++;
        hitBank += weight;
        damage += weight;
        while (hitBank >= 1f - 1e-4f && hp > 0) { hitBank -= 1f; hp--; }
        int was = heartsLeft;
        heartsLeft = HeartsFor(damage, hp);
        if (actor != null)
        {
            if (heartsLeft != was) actor.SetHearts(heartsLeft, at);
            actor.Flash();
        }
        if (ui != null) ui.HitFlash(boss.flash);
    }

    // The hearts left after `damage` (weighted hits) with `hpLeft` hit
    // points: none once the hit points are gone, else at least one.
    public static int HeartsFor(float damage, int hpLeft)
    {
        int n = Mathf.Max(1, BossConfig.Hearts);
        if (hpLeft <= 0) return 0;
        int lost = Mathf.FloorToInt(damage / Mathf.Max(1e-4f, BossConfig.HeartWeight) + 1e-4f);
        return Mathf.Clamp(n - lost, 1, n);
    }

    // ---- outro ------------------------------------------------------------

    void BeginOutro()
    {
        state = Phase.Outro;
        outroClock = 0f;
        moveBackGround.speed = BossConfig.FightSpeed;
        if (pool != null) pool.RecycleAll();
        // Hit points gone: it explodes (DESTROYED). The clock ran out first:
        // it gives up and warps away (SURVIVED). Scoring is the flat
        // destroyed / survived pair, no time bonus.
        bool explode = Destroyed;
        RunScore.OnBoss(explode, remaining, false,
                        actor != null ? actor.transform.position : new Vector3(0f, BossConfig.BossY, 0f));
        actor.BeginOutro(explode);
    }

    void TickOutro(float dt)
    {
        moveBackGround.speed = BossConfig.FightSpeed;
        outroClock += dt;
        actor.StepOutro(dt, ShipId.Equipped());
        if (outroClock >= BossConfig.OutroSeconds) Finish();
    }

    void Finish()
    {
        state = Phase.Done;
        doneWorld = world;
        Teardown();
        WorldMusic.EndBoss();
        var done = onFinished;
        onFinished = null;
        if (done != null) done();
    }

    void Abort()
    {
        state = Phase.Aborted;
        freePress = false;
        onFinished = null;
    }

    void Teardown()
    {
        if (actor != null) BossUtil.Kill(actor.gameObject);
        actor = null;
        if (pool != null) pool.Dispose();
        pool = null;
        if (ui != null) ui.Close();
        ui = null;
    }

    // ---- developer ----------------------------------------------------------

    // Boss rush (Options > developer): the boss arrives a few seconds into a
    // run instead of at the end of the level.
    // BOSS RUSH FINAL: on the first pass, a few seconds in jumps straight to
    // the end of the final world's boss (its choice); once looping, it rushes
    // each boss like BOSS RUSH ON.
    void TickDevRush(float dt)
    {
        if (!BossDev.RushEnabled || WorldManager.Instance == null) return;
        if (WorldManager.Instance.PortalIsOpen) return;
        bool final = BossDev.FinalRushEnabled && RunLoop.Index == 0;
        if (!final && DoneInWorld(WorldManager.CurrentIndex)) return;
        if (final && DoneInWorld(WorldManager.Worlds.Length - 1)) return;
        rushClock += dt;
        if (rushClock >= BossConfig.DevRushAfterSeconds)
        {
            rushClock = 0f;
            if (final) BossDev.TriggerFinal();
            else BossDev.TriggerNow();
        }
    }

    // ---- lifecycle ------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        doneWorld = -1;
        DevShortFight = false;
        if (scene.name != "gameS1") return;
        Ensure();
    }

    // A fresh run (tests): nothing beaten yet.
    public static void ResetRun()
    {
        doneWorld = -1;
        DevShortFight = false;
        if (Instance != null) BossUtil.Kill(Instance.gameObject);
        Instance = null;
    }
}
