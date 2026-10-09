using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// The 2026-10 retune's speed curve and the shielded-projectile score.
//
// SPEED ("things get too hard past speed 35"; second pass: the cap):
//   - one natural cap, HUD 35, every world and loop (was 38 ... 44, never past 50)
//   - from the knee (HUD 25) the ramp eases into the cap: the climb through
//     30 to 35 is gentle and arrives with zero slope; the early ramp is unchanged
//   - Tick, SpeedAfter, DistanceOver and SecondsToCover agree on the curve
//   - a stock level still takes 120 s; every score speed tier is reachable
//
// SHIELD: a hostile projectile (roster, elite, boss) absorbed by the
// blue-atom shield pays ScoreRules.ShieldedShot, flat, with a popup, at most
// ShieldedShotsPerShield per shield; the shot is erased and the shield and
// the hearts are untouched; Cloak alone pays nothing.
public static class DifficultyRetuneTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[RETUNE] PASS  " : "[RETUNE] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly MethodInfo Trigger = typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst);

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            SpeedCaps();
            SoftKnee();
            CurveFunctionsAgree();
            ThresholdsStayReachable();
            EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
            ShieldedShotsPay();
        }
        finally
        {
            SpeedRamp.FrameOverride = null;
            SpeedRamp.DeltaOverride = null;
            SpeedRamp.ResetFrameGuard();
            EliteSystem.Clear();
            collisionDetection.atomCheck = false;
            collisionDetection.cloakTimer = 0f;
            collisionDetection.lifeCounter = 0;
            PlayerInvuln.Reset();
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
        }
        Debug.Log("[RETUNE] failures: " + fails);
        return fails;
    }

    // ---- speed ---------------------------------------------------------------

    // (Second pass, the speed cap. Was: per-world caps 38 / 40 / 42 / 44,
    // never past 50 on a loop or KEEP FLYING, a soft knee at HUD 30 keeping
    // 40% of the rate. Now: one cap, HUD 35, eased into from HUD 25.)
    static void SpeedCaps()
    {
        var w = WorldManager.Worlds;
        Check("one natural speed cap, HUD 35, for every world (were 38 / 40 / 42 / 44)",
              Mathf.Approximately(SpeedRamp.Cap, .35f) && SpeedRamp.CapHud == 35);
        // Every world on every loop (the loop's ramp scale), ticked for 1000 s:
        // natural speed never passes the cap.
        float worst = 0f;
        int frame = 9000;
        const float dt = .05f;
        SpeedRamp.FrameOverride = () => frame;
        SpeedRamp.DeltaOverride = () => dt;
        SpeedRamp.ResetBoost();
        for (int i = 0; i < w.Length; i++)
            for (int loop = 0; loop <= 8; loop++)
            {
                SpeedRamp.ResetFrameGuard();
                moveBackGround.speed = LoopRules.ArrivalSpeed(loop);
                float rate = w[i].speedRampPerSecond * LoopRules.RampScale(loop);
                for (int f = 0; f < 20000; f++) { frame++; SpeedRamp.Tick(rate, 10f); worst = Mathf.Max(worst, moveBackGround.speed); }
            }
        SpeedRamp.FrameOverride = null;
        SpeedRamp.DeltaOverride = null;
        SpeedRamp.ResetFrameGuard();
        Check("nothing ever ramps past HUD 35, in any world, on any loop, even given a wall cap of 10 (worst " +
              (worst * 100f).ToString("F2") + "; was 50)", worst <= SpeedRamp.Cap + 1e-6f);
        Check("the early ramp is unchanged (HUD 0.315 / 0.330 / 0.345 / 0.365 a second)",
              Mathf.Approximately(w[0].speedRampPerSecond, .00315f) && Mathf.Approximately(w[3].speedRampPerSecond, .00365f));
    }

    // (Was SoftKnee: a knee at HUD 30 keeping 40% of the rate. Now the ease:
    // full rate to HUD 25, then the rate falls as the square root of what is
    // left, reaching the cap with zero slope.)
    static void SoftKnee()
    {
        Check("the ease starts at HUD 25", Mathf.Approximately(SpeedRamp.EaseKnee, .25f));
        for (int i = 0; i < WorldManager.LiveWorldCount; i++)
        {
            var t = WorldManager.Worlds[i];
            float below = SpeedRamp.RateAt(.25f, t.speedRampPerSecond) * 100f, above = SpeedRamp.RateAt(.33f, t.speedRampPerSecond) * 100f;
            float near = SpeedRamp.RateAt(.34f, t.speedRampPerSecond) * 100f, at = SpeedRamp.RateAt(SpeedRamp.Cap, t.speedRampPerSecond);
            float atBoss = SpeedRamp.SpeedAfter(0f, t.speedRampPerSecond, SpeedRamp.Cap, WorldManager.BaselineWorldSeconds) * 100f;
            float to35 = Seconds(t, .30f, SpeedRamp.Cap);
            Debug.Log(string.Format("[RETUNE] {0}: {1:F3} HUD/s at the knee, {2:F3} at 33, {3:F3} at 34, 0 at the cap; stock boss at HUD {4:F1}; 30->35 takes {5:F0}s",
                                    t.displayName, below, above, near, atBoss, to35));
            Check(t.displayName + ": the climb at HUD 33 is under half the climb at HUD 25 (" + above.ToString("F3") + " vs " + below.ToString("F3") + " HUD/s)",
                  above <= below * .5f && above > 0f);
            Check(t.displayName + ": the slope at 34 is under 0.15 HUD a second and 0 at the cap (" + near.ToString("F3") + ")",
                  near < .15f && near > 0f && at == 0f);
            Check(t.displayName + ": a stock run meets its boss between HUD 32 and 35 (" + atBoss.ToString("F1") + ")",
                  atBoss >= 32f && atBoss <= 35f + 1e-3f);
            Check(t.displayName + ": HUD 30 to 35 takes over half a minute (" + to35.ToString("F0") + " s)", to35 >= 30f);
        }
    }

    // Seconds the ramp takes from speed a to speed b in `t`.
    static float Seconds(WorldTheme t, float a, float b)
    {
        float s = a, time = 0f;
        const float dt = .05f;
        while (s < b - 1e-6f && time < 2000f) { s = Mathf.Min(b, SpeedRamp.Advance(s, t.speedRampPerSecond, dt)); time += dt; }
        return time;
    }

    static void CurveFunctionsAgree()
    {
        bool tick = true, distance = true, inverse = true, monotone = true;
        float cap = SpeedRamp.Cap;
        SpeedRamp.ResetBoost();
        foreach (var t in WorldManager.Worlds)
            foreach (float v0 in new[] { 0f, .1f, .24f, .25f, .30f, .34f, cap, cap + .05f })
            {
                // Tick, frame by frame, against the closed form
                int frame = 5000;
                const float dt = 1f / 60f;
                SpeedRamp.FrameOverride = () => frame;
                SpeedRamp.DeltaOverride = () => dt;
                SpeedRamp.ResetFrameGuard();
                moveBackGround.speed = v0;
                float flown = 0f, last = v0;
                for (int i = 0; i < 60 * 200; i++)
                {
                    frame++;
                    flown += moveBackGround.speed * dt;
                    SpeedRamp.Tick(t.speedRampPerSecond, cap);
                    monotone &= moveBackGround.speed >= last - 1e-7f;
                    last = moveBackGround.speed;
                    if (i % 600 != 599) continue;
                    float secs = (i + 1) * dt;
                    tick &= Mathf.Abs(moveBackGround.speed - SpeedRamp.SpeedAfter(v0, t.speedRampPerSecond, cap, secs)) < 2e-4f;
                    float d = SpeedRamp.DistanceOver(v0, t.speedRampPerSecond, cap, secs);
                    distance &= Mathf.Abs(d - flown) < .02f + flown * .002f;
                    if (d > 0f) inverse &= Mathf.Abs(SpeedRamp.SecondsToCover(v0, t.speedRampPerSecond, cap, d) - secs) < .05f;
                }
                if (v0 <= cap) tick &= moveBackGround.speed <= cap + 1e-6f;
            }
        SpeedRamp.FrameOverride = null;
        SpeedRamp.DeltaOverride = null;
        SpeedRamp.ResetFrameGuard();
        Check("Tick, frame by frame, follows SpeedAfter through the ease and holds at the cap (4 worlds x 8 starts, 200 s)", tick);
        Check("speed never falls on the ramp", monotone);
        Check("DistanceOver is the distance actually flown", distance);
        Check("SecondsToCover is DistanceOver's inverse", inverse);

        bool level = true;
        for (int w = 0; w < WorldManager.LiveWorldCount; w++)
        {
            var t = WorldManager.Worlds[w];
            float secs = SpeedRamp.SecondsToCover(0f, t.speedRampPerSecond, cap, WorldManager.WorldDistanceFor(w));
            level &= Mathf.Abs(secs - WorldManager.BaselineWorldSeconds) < .1f;
            float fast = SpeedRamp.SecondsToCover(.30f, t.speedRampPerSecond, cap, WorldManager.WorldDistanceFor(w));
            level &= fast < secs && fast > 30f;
        }
        Check("a stock level is still 120 s to the boss on the new curve, and a faster start still gets there sooner", level);
    }

    // (Was: tiers 20 / 30 / 40 / 46, x2 at a later world's cap, x2.5 only
    // inside the loop / KEEP FLYING ceiling. Now x2 is the cap itself and
    // x2.5 the limit break above it.)
    static void ThresholdsStayReachable()
    {
        var tiers = ScoreRules.SpeedTierHud;
        var w = WorldManager.Worlds;
        float cap = SpeedRamp.Cap;
        float stockSpaceBoss = SpeedRamp.SpeedAfter(0f, w[0].speedRampPerSecond, cap, WorldManager.BaselineWorldSeconds) * 100f;
        float stockEmberBoss = SpeedRamp.SpeedAfter(0f, w[3].speedRampPerSecond, cap, WorldManager.BaselineWorldSeconds) * 100f;
        Check("score speed tiers are 20 / 30 / 35 (were 20 / 30 / 40 / 46)",
              tiers.Length == 3 && tiers[0] == 20 && tiers[1] == 30 && tiers[2] == 35);
        Check("x1.25 and x1.5 are reached in a stock Space level (boss at HUD " + stockSpaceBoss.ToString("F1") + ")",
              tiers[0] < stockSpaceBoss && tiers[1] < stockSpaceBoss);
        Check("x2 is the cap itself, reached by natural flight on a first pass (stock Ember's boss at HUD " +
              stockEmberBoss.ToString("F1") + ")",
              tiers[2] == SpeedRamp.CapHud && Mathf.RoundToInt(stockEmberBoss) >= tiers[2] &&
              Mathf.Approximately(ScoreRules.SpeedMultiplierFor(cap), 2f));
        Check("x2.5 is only past the cap: the limit break (one blue atom at the cap)",
              Mathf.Approximately(ScoreRules.SpeedMultiplierFor(cap + SpeedRamp.BoostPerAtom), ScoreRules.LimitBreakMultiplier) &&
              Mathf.Approximately(ScoreRules.LimitBreakMultiplier, 2.5f) && ScoreRules.IsLimitBreak(cap + .01f) && !ScoreRules.IsLimitBreak(cap));
        Check("the resume slow-motion threshold (HUD " + ResumeSlowMo.MinHudSpeed + ") and the boss fight speed (HUD " +
              Mathf.RoundToInt(BossConfig.FightSpeed * 100f) + ") sit under the cap",
              ResumeSlowMo.MinHudSpeed < SpeedRamp.CapHud && BossConfig.FightSpeed < cap);
        bool starts = true;
        foreach (int id in ShipId.All)
            for (int skin = 0; skin < ShipSkins.CountFor(id); skin++) starts &= ShipStartSpeed.HudFor(id, skin) / 100f < cap;
        Check("every ship and colour's start speed is under the cap", starts);
        Check("loops still arrive below the ease (HUD " + (LoopRules.ArrivalSpeed(LoopRules.MaxScaledLoops) * 100f).ToString("F0") + ")",
              LoopRules.ArrivalSpeed(LoopRules.MaxScaledLoops) < SpeedRamp.EaseKnee);
    }

    // ---- shield --------------------------------------------------------------

    static int popups, popupPoints;
    static RunScore.Source popupSource;

    static void OnScored(int points, Vector3 at, RunScore.Source source)
    {
        if (source != RunScore.Source.Shield) return;
        popups++;
        popupPoints = points;
        popupSource = source;
    }

    static int FirstShip { get { foreach (int id in ShipId.All) return id; return 0; } }

    static void Ram(DeathCrashTest.Rig r, GameObject hitbox)
    {
        Trigger.Invoke(r.cd, new object[] { hitbox.GetComponent<Collider2D>() });
    }

    static EliteDef Style(int world)
    {
        foreach (var def in EnemyRoster.All)
            if (def.world == world && def.Behaviour != null && def.Behaviour.Shoots) return def.Behaviour.ShotStyle;
        return null;
    }

    static EliteShot RosterShot(Vector3 at)
    {
        var s = EliteSystem.Shots.Fire(null, Style(0), EliteShots.Kind.Bolt, at, Vector2.down);
        s.AsRosterShot(null, 1f);
        Physics2D.SyncTransforms();
        return s;
    }

    static void ShieldedShotsPay()
    {
        EliteSystem.Clear();
        var r = new DeathCrashTest.Rig(FirstShip);
        RunScore.Scored += OnScored;
        var pool = new BossProjectilePool(8, 1);
        try
        {
            RunScore.EndRun(RunScore.RunId);
            RunScore.BeginRun(true, false);
            // a limit break (the boost at its most over the cap): the top speed
            // tier is up and the reward must stay flat
            moveBackGround.speed = SpeedRamp.Cap + SpeedRamp.MaxBoost;
            collisionDetection.lifeCounter = 0;
            collisionDetection.cloakTimer = 0f;
            PlayerInvuln.Reset();
            Vector3 at = r.ship.transform.position + Vector3.up * .2f;

            // shield up (the blue atom's)
            collisionDetection.atomCheck = true;
            RunScore.OnShieldRaised();
            popups = 0;
            long before = RunScore.Total;
            var shot = RosterShot(at);
            Ram(r, shot.Hitbox);
            Check("a roster enemy's shot absorbed by the shield pays " + ScoreRules.ShieldedShot + " (got " + (RunScore.Total - before) + ")",
                  RunScore.Total - before == ScoreRules.ShieldedShot && ScoreRules.ShieldedShot == 5);
            Check("... flat: no speed multiplier in a limit break (HUD " + ScoreRules.HudSpeed(moveBackGround.speed) + "), and it does not start a kill chain", RunScore.Chain == 0);
            Check("... with its own popup (+" + popupPoints + " ABSORB)", popups == 1 && popupPoints == ScoreRules.ShieldedShot &&
                  popupSource == RunScore.Source.Shield && ScoreHud.StyleFor(RunScore.Source.Shield, false).suffix.Contains("ABSORB"));
            Check("... the shot is erased, the shield stays up and no heart is lost",
                  !shot.Active && collisionDetection.atomCheck && collisionDetection.lifeCounter == 0 && !buttonClicks.playerDied);
            Check("... counted on the run's breakdown", RunScore.Parts.shieldedShots == 1);

            // an elite's shot
            before = RunScore.Total;
            var eliteShot = EliteSystem.Shots.Fire(null, Style(1), EliteShots.Kind.Shard, at, Vector2.down);
            Physics2D.SyncTransforms();
            Ram(r, eliteShot.Hitbox);
            Check("an elite's shot pays the same (" + (RunScore.Total - before) + ")", RunScore.Total - before == ScoreRules.ShieldedShot && !eliteShot.Active);

            // a boss's shot: worth the same in total (its old 1 is part of it)
            before = RunScore.Total;
            var bossShot = pool.Fire(BossCatalog.ForWorld(0), BossShotStyle.Bolt, at, Vector2.down * .01f);
            Physics2D.SyncTransforms();
            Ram(r, bossShot.Hitbox);
            Check("a boss's shot pays the same in total, not 1 (" + (RunScore.Total - before) + ")",
                  RunScore.Total - before == ScoreRules.ShieldedShot);

            // the cap: only the first ShieldedShotsPerShield of a shield pay
            RunScore.OnShieldRaised();
            before = RunScore.Total;
            int cap = ScoreRules.ShieldedShotsPerShield;
            for (int i = 0; i < cap + 6; i++) Ram(r, RosterShot(at).Hitbox);
            Check("one shield pays for at most " + cap + " projectiles (" + (RunScore.Total - before) + " points from " + (cap + 6) + " absorbed)",
                  RunScore.Total - before == cap * ScoreRules.ShieldedShot && RunScore.ShieldedShotsThisShield == cap);
            before = RunScore.Total;
            var late = pool.Fire(BossCatalog.ForWorld(0), BossShotStyle.Bolt, at, Vector2.down * .01f);
            Physics2D.SyncTransforms();
            Ram(r, late.Hitbox);
            Check("past the cap a boss shot pays its usual " + ScoreRules.BossShot, RunScore.Total - before == ScoreRules.BossShot);
            RunScore.OnShieldRaised();
            before = RunScore.Total;
            Ram(r, RosterShot(at).Hitbox);
            Check("a new blue atom starts the allowance again", RunScore.Total - before == ScoreRules.ShieldedShot);

            // not a shield: Cloak alone absorbs the shot but pays nothing
            collisionDetection.atomCheck = false;
            collisionDetection.BeginCloak(5f);
            before = RunScore.Total;
            var cloaked = RosterShot(at);
            Ram(r, cloaked.Hitbox);
            Check("Cloak alone (no shield up) absorbs the shot but pays nothing", RunScore.Total == before && !cloaked.Active &&
                  collisionDetection.lifeCounter == 0);
            collisionDetection.cloakTimer = 0f;

            // no shield at all: the shot costs a heart and pays nothing
            before = RunScore.Total;
            var bare = RosterShot(at);
            Ram(r, bare.Hitbox);
            Check("unshielded, the same shot costs a heart and pays nothing", RunScore.Total == before && collisionDetection.lifeCounter == 1);

            // only projectiles: a rock rammed shielded is a kill as before, not an absorb
            collisionDetection.lifeCounter = 0;
            PlayerInvuln.Reset();
            collisionDetection.atomCheck = true;
            RunScore.OnShieldRaised();
            int absorbed = RunScore.Parts.shieldedShots;
            var rock = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Rock), at, Quaternion.identity);
            Physics2D.SyncTransforms();
            Ram(r, rock);
            Check("a rock rammed shielded is a kill as before, not an absorbed projectile",
                  RunScore.Parts.shieldedShots == absorbed && !RunScore.IsHostileShot(rock));
            if (rock != null) Object.DestroyImmediate(rock);

            // the tutorial (no scoring run) pays nothing
            RunScore.EndRun(RunScore.RunId);
            RunScore.BeginRun(false, false);
            RunScore.OnShieldRaised();
            before = RunScore.Total;
            Ram(r, RosterShot(at).Hitbox);
            Check("a practice run scores nothing for it", RunScore.Total == before);
        }
        finally
        {
            RunScore.Scored -= OnScored;
            RunScore.EndRun(RunScore.RunId);
            collisionDetection.atomCheck = false;
            moveBackGround.speed = 0f;
            foreach (var p in Object.FindObjectsByType<BossProjectile>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
            var root = GameObject.Find("~BossProjectiles");
            if (root != null) Object.DestroyImmediate(root);
            if (r.ship != null) Object.DestroyImmediate(r.ship);
        }
    }
}
