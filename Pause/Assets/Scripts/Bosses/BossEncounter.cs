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
//   Outro    the boss explodes (hit by the ultimate) or retreats.
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
    bool freePress;
    float outroClock;
    float rushClock;

    BossActor actor;
    BossProjectilePool pool;
    BossIntroUI ui;
    Transform player;

    // ---- hooks for shared code --------------------------------------------

    public static bool Running => Instance != null && Instance.IsRunning;
    // moveBackGround: hold timeScale at 0 regardless of the finger.
    public static bool ScriptedFreeze => Instance != null && Instance.state == Phase.Intro;
    // moveBackGround (ramp) and collisionDetection (atom boost): hands off speed.
    public static bool SpeedLocked => Instance != null &&
        (Instance.state == Phase.Intro || Instance.state == Phase.Fight || Instance.state == Phase.Outro);
    // enmiesOnBoard: no normal enemy spawns during the encounter.
    public static bool SuspendsSpawning => Running;
    // score: a press during the intro, or the first one after it, is not a
    // spent pause -- the freeze was the boss's, not the player's.
    public static bool FreePress => Instance != null && Instance.freePress;

    public static float FilterSpeedChange(float delta) => SpeedLocked ? 0f : delta;
    public static bool DoneInWorld(int worldIndex) => doneWorld == worldIndex;

    // ---- state ------------------------------------------------------------

    public Phase State => state;
    public BossDef Boss => boss;
    public BossActor Actor => actor;
    public BossProjectilePool Pool => pool;
    public float Remaining => remaining;
    public int Hits => hits;
    public int HitPointsLeft => hp;
    public bool IsRunning => state == Phase.Pending || state == Phase.Intro ||
                             state == Phase.Fight || state == Phase.Outro;
    public float Progress01 => BossConfig.FightSeconds <= 0f ? 1f
        : Mathf.Clamp01(1f - remaining / BossConfig.FightSeconds);

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
        remaining = BossConfig.FightSeconds;
        fightClock = 0f;
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
            case Phase.Idle: TickDevRush(dt); break;
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
        preSpeed = moveBackGround.speed;
        freePress = true;
        actor = BossActor.Spawn(boss);
        pool = new BossProjectilePool(BossConfig.ProjectilePoolMax, BossConfig.BeamPoolMax);
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
        fightClock = 0f;
        actor.BeginFight();
        if (TouchInput.IsPressed) freePress = false;
    }

    void TickFight(float dt, float realDt)
    {
        moveBackGround.speed = BossConfig.FightSpeed;
        remaining -= dt;
        fightClock += dt;
        actor.StepFight(dt, realDt, PlayerPosition(), Progress01, pool);

        bool over = BossConfig.EndRule == BossEndRule.Survival
            ? remaining <= 0f
            : hp <= 0 || remaining <= 0f;
        if (over) BeginOutro();
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
    // share of one full ultimate hit (one firing never totals more than 1):
    // Survival takes UltimateHitSeconds * weight off the clock; HitPoints
    // banks weights and loses a point per whole hit.
    float hitBank;
    public void OnShipAttackHit(float weight)
    {
        if (state != Phase.Fight || weight <= 0f) return;
        hits++;
        if (BossConfig.EndRule == BossEndRule.Survival) remaining -= BossConfig.UltimateHitSeconds * weight;
        else
        {
            hitBank += weight;
            while (hitBank >= 1f - 1e-4f) { hitBank -= 1f; hp--; }
        }
        if (actor != null) actor.Flash();
        if (ui != null) ui.HitFlash(boss.flash);
    }

    // ---- outro ------------------------------------------------------------

    void BeginOutro()
    {
        state = Phase.Outro;
        outroClock = 0f;
        moveBackGround.speed = BossConfig.FightSpeed;
        if (pool != null) pool.RecycleAll();
        // It explodes if the pilot actually landed blows; otherwise it gives
        // up and warps away.
        bool explode = BossConfig.EndRule == BossEndRule.Survival ? hits > 0 : hp <= 0;
        RunScore.OnBoss(explode, remaining, BossConfig.EndRule == BossEndRule.HitPoints,
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
    void TickDevRush(float dt)
    {
        if (!BossDev.RushEnabled || WorldManager.Instance == null) return;
        if (DoneInWorld(WorldManager.CurrentIndex)) return;
        rushClock += dt;
        if (rushClock >= BossConfig.DevRushAfterSeconds)
        {
            rushClock = 0f;
            BossDev.TriggerNow();
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
        if (scene.name != "gameS1") return;
        Ensure();
    }

    // A fresh run (tests): nothing beaten yet.
    public static void ResetRun()
    {
        doneWorld = -1;
        if (Instance != null) BossUtil.Kill(Instance.gameObject);
        Instance = null;
    }
}
