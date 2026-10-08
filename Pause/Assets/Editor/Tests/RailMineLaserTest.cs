using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// "Have the rail mines shoot a laser."
//
//   TIMING    every world's mine: its windup (the tell it always had) with
//             the blinking aim line over its last AimSeconds, then
//             BeamSeconds of beam, then CoolSeconds of flicker, then gone;
//             the mine holds still while the beam burns
//   SPAN      the beam lies at the mine's y, from its own rail's inner face
//             to the opposite rail's, follows the mine as the board carries
//             it, and spans exactly rail to rail at 9:16, 9:19.5, 9:21 and
//             3:4 in every world (the painted rails, RailInset included)
//   DAMAGE    the hitbox exists in the Beam phase only (the aim line and the
//             flicker are harmless); unshielded it costs a heart through
//             collisionDetection; under a shield it is absorbed and the beam
//             ends with no heart lost; a blink erases it
//   FRIENDLY  other hazards on its row take the standard hostile hit, once
//   FIRE      per pulse each, never its own mine; off (HurtsOtherEnemies
//             false) it touches nothing
//   FAIR      the beam is HitThickness thick: with the 1.35x ship's hitbox,
//             the band the ship must clear, and the room to clear it in the
//             aim line's warning, are reported and bounded
//   PAUSE     a frozen world freezes the aim line, the beam and its timers
//   60 FPS    a full fire (aim, beam, flicker) allocates nothing
//   SPRITES   the boss laser art: no player red
public static class RailMineLaserTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[MINELASER] PASS  " : "[MINELASER] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    static float clock;
    static Transform ship;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Timing();
            Follows();
            Damage();
            FriendlyFireOnce();
            Fairness();
            PauseFreezes();
            NoAllocations();
            SpansEveryAspect();
        }
        finally
        {
            EnemyThreat.ForceShooting = false;
            EnemyThreat.Reset();
            RailMineLaser.HurtsOtherEnemies = true;
            SpawnSpace.ClockOverride = null;
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            ScreenInfo.ClearOverride();
            BossRails.Reset();
            buttonClicks.playerDied = false;
            collisionDetection.lifeCounter = 0;
            collisionDetection.atomCheck = false;
        }
        Debug.Log("[MINELASER] failures: " + fails);
        return fails;
    }

    // ---- fixtures ----------------------------------------------------------

    static void Fresh()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EliteSystem.Clear();
        EnemyThreat.Reset();
        EnemyThreat.ForceShooting = true;
        BossRails.Reset();
        ScreenInfo.ClearOverride();
        FriendlyFire.ResetCounters();
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        startMenu.youAreInTutorial = false;
        moveBackGround.speed = 0f;
        Time.timeScale = 1f;
        clock = 100f;
        SpawnSpace.ClockOverride = clock;
        ship = new GameObject("~LaserTestShip").transform;
        ship.position = new Vector3(-.5f, 1f, 0f);
        EliteSystem.PlayerOverride = ship;
    }

    static EnemyBrain Mine(int world, bool right, float y, out GameObject rail)
    {
        var def = EnemyRoster.One(world, EnemyRole.Mine);
        float x = enmiesOnBoard.WorldRailX(!right);
        rail = new GameObject("RailMineLane");
        rail.transform.position = new Vector3(x, y, 0f);
        rail.AddComponent<RailLaneScroller>();
        var go = EnemyFactory.Create(def, new Vector3(x, y, 0f), Quaternion.identity);
        var brain = go.GetComponent<EnemyBrain>();
        var mount = go.AddComponent<RailMineMount>();
        mount.MountTo(rail.transform);
        mount.brain = brain;
        brain.TargetOverride = ship;
        return brain;
    }

    // One running frame: the brain (and its laser), the mount, the flipbook.
    static void Step(EnemyBrain brain, float dt = Dt)
    {
        clock += dt;
        SpawnSpace.ClockOverride = clock;
        brain.Step(dt);
        var mount = brain.GetComponent<RailMineMount>();
        if (mount != null) TestHarness.Send(mount, "LateUpdate");
        var fb = brain.GetComponent<EnemyFlipbook>();
        if (fb != null) fb.Advance(dt);
        var l = brain.Laser;
        if (l != null) l.Place();
    }

    static string F(float v) => v.ToString("F3");

    // Runs a mine until its laser fires, timing each phase.
    struct Run1
    {
        public float windup, sightFirst, beam, cool, hitboxOutsideBeam, touchOutsideBeam, touchInBeam;
        public bool fired, sightBeforeWindowOk, heldStill, ended;
    }

    static Run1 Fire(EnemyBrain brain)
    {
        var r = new Run1 { sightFirst = -1f };
        RailMineLaser seen = null;
        float windupAt = -1f, time = 0f, minePosY = 0f;
        bool held = true;
        for (int i = 0; i < 60 * 10; i++)
        {
            Step(brain);
            time += Dt;
            var l = brain.Laser ?? seen;
            if (brain.State == EnemyBrain.Phase.Windup && windupAt < 0f) windupAt = time;
            if (l != null) seen = l;
            if (seen == null) continue;
            switch (seen.State)
            {
                case RailMineLaser.Phase.Aim:
                    r.windup += Dt;
                    if (seen.SightShown && r.sightFirst < 0f) r.sightFirst = r.windup;
                    break;
                case RailMineLaser.Phase.Beam:
                    if (r.beam == 0f) minePosY = brain.transform.position.y;
                    r.beam += Dt;
                    r.fired = true;
                    held &= Mathf.Abs(brain.transform.position.y - minePosY) < 1e-4f;
                    break;
                case RailMineLaser.Phase.Cool:
                    r.cool += Dt;
                    break;
            }
            bool beam = seen.State == RailMineLaser.Phase.Beam;
            bool box = seen.Hitbox != null && seen.Hitbox.GetComponent<Collider2D>().enabled;
            bool touch = seen.Touches(new Vector2(ship.position.x, seen.Y), .3f, .33f);
            if (!beam && box) r.hitboxOutsideBeam += Dt;
            if (!beam && touch) r.touchOutsideBeam += Dt;
            if (beam && touch) r.touchInBeam += Dt;
            if (r.fired && !seen.Active) { r.ended = true; break; }
        }
        r.heldStill = held;
        return r;
    }

    // ---- 1. timing -----------------------------------------------------------

    static void Timing()
    {
        for (int w = 0; w < 4; w++)
        {
            Fresh();
            var brain = Mine(w, true, 1f, out var rail);
            var b = brain.Behaviour;
            float tell = Mathf.Max(EnemyBrain.TellFloorSeconds, b.tell);
            Check(EnemyRoster.WorldKeys[w] + " mine fires a laser (" + b.attack + "), its tell " + b.tell + " s, cooldown " + b.cooldown +
                  " s, " + b.maxVolleys + " volleys", b.attack == EnemyAttack.Laser && b.Shoots && b.shotCount == 1);
            var r = Fire(brain);
            string tag = EnemyRoster.WorldKeys[w] + ": ";
            Check(tag + "windup " + F(r.windup) + " s (its tell " + F(tell) + "), the aim line from " + F(r.sightFirst) + " s (" +
                  F(r.windup - r.sightFirst) + " s of blinking line), beam " + F(r.beam) + " s, flicker " + F(r.cool) + " s, then gone",
                  r.fired && r.ended && Mathf.Abs(r.windup - tell) <= 2.5f * Dt &&
                  Mathf.Abs((r.windup - r.sightFirst) - RailMineLaser.AimSeconds) <= 2.5f * Dt &&
                  Mathf.Abs(r.beam - RailMineLaser.BeamSeconds) <= 2.5f * Dt &&
                  Mathf.Abs(r.cool - RailMineLaser.CoolSeconds) <= 2.5f * Dt);
            Check(tag + "the hitbox exists in the beam only (" + F(r.hitboxOutsideBeam) + " s outside), the ship level with it is touched " +
                  F(r.touchInBeam) + " s in the beam and " + F(r.touchOutsideBeam) + " s outside",
                  r.hitboxOutsideBeam == 0f && r.touchOutsideBeam == 0f && r.touchInBeam > RailMineLaser.BeamSeconds - 3f * Dt);
            Check(tag + "the mine holds still on its rail while the beam burns", r.heldStill);
            Check(tag + "the brain counts the fire (" + brain.ShotsFired + ")", brain.ShotsFired == 1);
            Object.DestroyImmediate(brain.gameObject);
            Object.DestroyImmediate(rail);
        }
        Check("the aim line shows " + RailMineLaser.AimSeconds + " s, the beam burns " + RailMineLaser.BeamSeconds + " s, the flicker " +
              RailMineLaser.CoolSeconds + " s (asked: 0.6-0.8, 0.35-0.5, brief)",
              RailMineLaser.AimSeconds >= .6f && RailMineLaser.AimSeconds <= .8f &&
              RailMineLaser.BeamSeconds >= .35f && RailMineLaser.BeamSeconds <= .5f && RailMineLaser.CoolSeconds <= .3f);
    }

    // ---- 2. it lies on the mine's row and follows it -------------------------

    static void Follows()
    {
        Fresh();
        foreach (bool right in new[] { true, false })
        {
            var brain = Mine(0, right, 1f, out var rail);
            ship.position = new Vector3(0f, 1f, 0f);
            RailMineLaser l = null;
            for (int i = 0; i < 600 && (l == null || l.State != RailMineLaser.Phase.Beam); i++) { Step(brain); l = brain.Laser; }
            if (l == null) { Check("a laser fired", false); continue; }
            float edge = BossRails.DrawnInnerEdge, s = right ? 1f : -1f;
            float y0 = l.Y;
            Check((right ? "right" : "left") + " mine: the beam runs from its own rail's face " + F(l.From.x) + " to the opposite rail's " +
                  F(l.To.x) + " (+/-" + F(edge) + "), level with the mine (" + F(l.Y) + " vs " + F(brain.transform.position.y) + ")",
                  Mathf.Abs(l.From.x - s * edge) < 1e-4f && Mathf.Abs(l.To.x + s * edge) < 1e-4f &&
                  Mathf.Abs(l.Y - brain.transform.position.y) < 1e-4f && Mathf.Abs(l.From.y - l.To.y) < 1e-5f &&
                  Mathf.Abs(l.Length - 2f * edge) < 1e-4f);
            var br = l.BeamRenderer.bounds;
            Check("... drawn across exactly that span (" + F(br.min.x) + " .. " + F(br.max.x) + "), " + F(br.size.y) + " u thick",
                  Mathf.Abs(br.min.x + edge) < .01f && Mathf.Abs(br.max.x - edge) < .01f && br.size.y <= RailMineLaser.DrawWidth + 1e-3f);
            var box = l.Hitbox.GetComponent<BoxCollider2D>();
            Physics2D.SyncTransforms();
            var bb = box.bounds;
            Check("... its hitbox too, " + F(bb.size.y) + " u thick (" + RailMineLaser.HitThickness + ")",
                  Mathf.Abs(bb.min.x + edge) < .01f && Mathf.Abs(bb.max.x - edge) < .01f &&
                  Mathf.Abs(bb.size.y - RailMineLaser.HitThickness) < .01f && box.isTrigger && box.CompareTag("Enimey"));
            // the board carries the mine down: the beam goes with it
            rail.transform.position += Vector3.down * .5f;
            Step(brain, 1e-5f);
            Check("... and rides the board with its mine (" + F(y0) + " -> " + F(l.Y) + ", mine " + F(brain.transform.position.y) + ")",
                  Mathf.Abs(l.Y - brain.transform.position.y) < 1e-4f && l.Y < y0 - .45f);
            Object.DestroyImmediate(brain.gameObject);
            Object.DestroyImmediate(rail);
            // (in play the brain's OnDisable cancels it; with neither, the laser's own frame notices)
            TestHarness.Send(l, "LateUpdate");
            Check("... and goes with the mine", !l.Active);
        }
        // the art: the world boss's laser cells, none of it the player's red
        bool art = true;
        string tints = "";
        for (int w = 0; w < 4; w++)
        {
            var a = MineLaserArt.For(w);
            foreach (var s in new[] { a.beam0, a.beam1, a.sight })
            {
                art &= s != null;
                if (s == null) continue;
                var px = ShieldContour.ReadPixels(s, out int pw, out int ph);
                int red = 0, lit = 0, hot = 0;
                for (int i = 0; px != null && i < px.Length; i++)
                {
                    if (px[i].a < 128) continue;
                    lit++;
                    if (HostileGlow.IsPlayerRed(px[i])) red++;
                    Color.RGBToHSV(px[i], out float hh, out float ss, out float vv);
                    if (ss > .5f && vv > .8f) hot++;
                }
                if (s == a.beam0) tints += " " + EnemyRoster.WorldKeys[w] + " " + red + " red / " + hot + " neon of " + lit;
                art &= lit > 0 && red == 0 && (s == a.sight || hot > lit / 5);
            }
        }
        Check("the beam is the world boss's laser art in neon (magenta, Verdant lime), none of it the player's red (" + tints + ")", art);
    }

    // ---- 3. damage -----------------------------------------------------------

    static RailMineLaser LiveLaser(EnemyBrain brain)
    {
        for (int i = 0; i < 600; i++)
        {
            Step(brain);
            var l = brain.Laser;
            if (l != null && l.State == RailMineLaser.Phase.Beam) return l;
        }
        return null;
    }

    static void Damage()
    {
        Fresh();
        Object.DestroyImmediate(ship.gameObject);
        var rig = new DeathCrashTest.Rig(FirstShip);
        try
        {
            ship = rig.ship.transform;
            EliteSystem.PlayerOverride = ship;
            ship.position = new Vector3(-.5f, 1f, 0f);
            collisionDetection.lifeCounter = 0;
            PlayerInvuln.Reset();

            // during the aim line: the ship sits in the row and nothing hurts
            var brain = Mine(1, true, 1f, out var rail);
            RailMineLaser aiming = null;
            for (int i = 0; i < 600 && aiming == null; i++) { Step(brain); if (brain.Laser != null && brain.Laser.SightShown) aiming = brain.Laser; }
            Physics2D.SyncTransforms();
            var hits = Physics2D.OverlapPointAll(ship.position);
            bool boxHere = false;
            foreach (var h in hits) boxHere |= h.TryGetComponent(out RailMineLaserHitbox _);
            Check("while the aim line blinks there is no hitbox under the ship", aiming != null && !boxHere);

            // the beam: unshielded, a heart
            var l = LiveLaser(brain);
            Physics2D.SyncTransforms();
            hits = Physics2D.OverlapPointAll(ship.position);
            foreach (var h in hits) boxHere |= h.TryGetComponent(out RailMineLaserHitbox _);
            Check("the live beam's hitbox lies under the ship in its row", l != null && boxHere);
            if (l != null)
            {
                Check("the beam's hitbox is a hostile beam, not a hazard body that blows up when rammed (RamKill), and a projectile " +
                      "to the death crash", RamKill.NotAHazardBody(l.Hitbox) && DeathCrash.Classify(l.Hitbox) == DeathCrash.KillerKind.Projectile);
                long paid = RunScore.Total;
                rig.Hit(l.Hitbox);
                Check("unshielded, the beam costs a heart (" + collisionDetection.lifeCounter + ") and pays nothing",
                      collisionDetection.lifeCounter == 1 && !buttonClicks.playerDied && RunScore.Total == paid);
            }
            Object.DestroyImmediate(brain.gameObject);
            Object.DestroyImmediate(rail);

            // under the shield: absorbed, the beam ends, no heart
            collisionDetection.lifeCounter = 0;
            PlayerInvuln.Reset();
            collisionDetection.atomCheck = true;
            RunScore.OnShieldRaised();
            brain = Mine(1, true, 1f, out rail);
            l = LiveLaser(brain);
            if (l != null)
            {
                rig.Hit(l.Hitbox);
                Check("under the shield the beam is absorbed: it ends at once (" + l.State + "), no heart lost (" +
                      collisionDetection.lifeCounter + ")",
                      l.State == RailMineLaser.Phase.Cool && collisionDetection.lifeCounter == 0 && !l.Live &&
                      !l.Hitbox.GetComponent<Collider2D>().enabled);
            }
            else Check("a beam to absorb", false);
            collisionDetection.atomCheck = false;
            Object.DestroyImmediate(brain.gameObject);
            Object.DestroyImmediate(rail);

            // a blink landing on it erases it
            brain = Mine(1, true, 1f, out rail);
            l = LiveLaser(brain);
            bool erased = l != null && EliteShip.TeleportStrike(l.Hitbox, ship.position) && l.State == RailMineLaser.Phase.Cool;
            Check("a blink onto the beam erases it", erased);
            Object.DestroyImmediate(brain.gameObject);
            Object.DestroyImmediate(rail);
        }
        finally
        {
            rig.Dispose();
            collisionDetection.lifeCounter = 0;
            PlayerInvuln.Reset();
        }
    }

    static int FirstShip { get { foreach (int id in ShipId.All) return id; return 0; } }

    // ---- 4. friendly fire ----------------------------------------------------

    static void FriendlyFireOnce()
    {
        // ALL hostile fire is friendly fire: the beam hurts what it crosses
        foreach (bool on in new[] { true })
        {
            Fresh();
            Check("the mine laser is friendly fire (RailMineLaser.HurtsOtherEnemies)", RailMineLaser.HurtsOtherEnemies);
            // the ship level with the mine lets it fire; the hazards go on the beam's row once it burns
            ship.position = new Vector3(0f, 1f, 0f);
            var brain = Mine(3, true, 1f, out var rail);
            var l = LiveLaser(brain);
            float row = l != null ? l.Y : 1f;
            var rock = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Rock), new Vector3(-1f, row + .05f, 0f), Quaternion.identity);
            ClearTarget.Ensure(rock);
            var fighter = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Fighter), new Vector3(1f, row - .05f, 0f), Quaternion.identity);
            ClearTarget.Ensure(fighter);
            var fb = fighter.GetComponent<EnemyBrain>();
            if (fb != null) fb.enabled = false;   // held in the row
            var far = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Rock), new Vector3(0f, row + 2f, 0f), Quaternion.identity);
            ClearTarget.Ensure(far);
            int kills = FriendlyFire.Kills;
            for (int i = 0; i < 30 && l != null && l.State == RailMineLaser.Phase.Beam; i++) Step(brain);
            kills = FriendlyFire.Kills;
            if (on)
                Check("friendly fire (rock " + (rock == null ? "gone" : FriendlyFire.CanHit(rock.GetComponent<ClearTarget>()) + " at " + rock.transform.position) +
                      ", fighter " + (fighter == null ? "gone" : FriendlyFire.CanHit(fighter.GetComponent<ClearTarget>()) + " at " + fighter.transform.position + " r " + fighter.GetComponent<ClearTarget>().Radius) +
                      ", beam y " + (l != null ? F(l.Y) : "-") + "): a rock and an enemy fighter in the beam's row each take the standard hostile hit, once (" + kills +
                      " kills, " + (l != null ? l.HitsThisPulse : -1) + " hits this pulse); a rock off the row and its own mine do not",
                      kills == 2 && far != null && brain != null && l != null && l.HitsThisPulse == 2);
            if (rock != null) Object.DestroyImmediate(rock);
            if (fighter != null) Object.DestroyImmediate(fighter);
            if (far != null) Object.DestroyImmediate(far);
            if (brain != null) Object.DestroyImmediate(brain.gameObject);
            Object.DestroyImmediate(rail);
        }
        RailMineLaser.HurtsOtherEnemies = true;
    }

    // ---- 5. dodge room -------------------------------------------------------

    static void Fairness()
    {
        // the 1.35x ship's hull hitbox (RailsShipSizeTest: ~0.66 u tall)
        Fresh();
        var shipGo = new GameObject("~LaserSpawner").AddComponent<spawnShips>().Spawn(ShipId.Starter);
        float tall = .66f;
        var hb = ShipHitbox.Of(shipGo);
        // (outside gameS1 the hull is the normalised size; in the game it is ShipScale's 1.35x)
        if (hb != null && hb.Hull != null) { Physics2D.SyncTransforms(); tall = hb.Hull.bounds.size.y * 1.35f; }
        Object.DestroyImmediate(shipGo);
        float band = RailMineLaser.HitThickness + tall;          // the ship's centre must leave this band
        float move = band * .5f;                                  // from the worst spot (dead centre of the row)
        float view = 13.2f;                                       // a 1080x1920 view, the smallest
        Debug.Log("[MINELASER] INFO  dodge room: beam " + RailMineLaser.HitThickness + " u + ship hitbox " + F(tall) + " u = a " + F(band) +
                  " u band the ship's centre must clear (" + (100f * band / view).ToString("F1") + "% of a 13.2 u view); from the row's centre " +
                  F(move) + " u up or down, with " + RailMineLaser.AimSeconds + " s of aim line (at least " +
                  F(move / RailMineLaser.AimSeconds) + " u/s) plus the rest of the mine's tell");
        Check("the beam is " + RailMineLaser.HitThickness + " u thick (asked 0.25-0.35) and the band to clear (" + F(band) +
              " u) is under a tenth of the smallest view",
              RailMineLaser.HitThickness >= .25f && RailMineLaser.HitThickness <= .35f && band < view * .1f &&
              Mathf.Abs(RailMineLaser.HitThickness - RailMineLaser.DrawWidth * BossConfig.BeamHitFraction) < 1e-4f);
        // the budget: a burning laser counts as a shot
        Check("a burning laser counts against the roster shot budget (EnemyThreat.LiveShots)", CountsInBudget());
    }

    static bool CountsInBudget()
    {
        Fresh();
        var brain = Mine(0, true, 1f, out var rail);
        int before = EnemyThreat.LiveShots;
        var l = LiveLaser(brain);
        int during = EnemyThreat.LiveShots;
        Object.DestroyImmediate(brain.gameObject);
        Object.DestroyImmediate(rail);
        return l != null && during == before + 1;
    }

    // ---- 6. pause ------------------------------------------------------------

    static void PauseFreezes()
    {
        Fresh();
        var brain = Mine(2, true, 1f, out var rail);
        RailMineLaser l = null;
        for (int i = 0; i < 600; i++) { Step(brain); l = brain.Laser; if (l != null && l.State == RailMineLaser.Phase.Beam && l.PhaseTime > .1f) break; }
        if (l == null) { Check("a beam to freeze", false); return; }
        float t = l.PhaseTime, y = l.Y;
        bool shown = l.BeamShown;
        score.pauseCounter = 3;   // finger up with pauses left: the world is frozen
        for (int i = 0; i < 300; i++)
        {
            TestHarness.Send(brain, "LateUpdate");   // what Unity calls on a frozen frame
            TestHarness.Send(l, "LateUpdate");
        }
        Check("300 frozen frames: the beam holds (" + l.State + ", " + F(t) + " -> " + F(l.PhaseTime) + " s, still drawn " + l.BeamShown + ")",
              l.State == RailMineLaser.Phase.Beam && Mathf.Approximately(l.PhaseTime, t) && l.Y == y && l.BeamShown == shown);
        score.pauseCounter = 0;
        for (int i = 0; i < 120 && l.Active; i++) Step(brain);
        Check("... and finishes on resume", !l.Active);
        Object.DestroyImmediate(brain.gameObject);
        Object.DestroyImmediate(rail);
        Check("no laser in the tutorial: roster enemies never shoot there",
              !WithTutorial());
    }

    static bool WithTutorial()
    {
        EnemyThreat.ForceShooting = false;
        bool was = startMenu.youAreInTutorial;
        startMenu.youAreInTutorial = true;
        bool allowed = EnemyThreat.ShootingAllowed;
        startMenu.youAreInTutorial = was;
        EnemyThreat.ForceShooting = true;
        return allowed;
    }

    // ---- 7. allocations ------------------------------------------------------

    static void NoAllocations()
    {
        Fresh();
        var brain = Mine(3, true, 1f, out var rail);
        // warm up: one whole fire (pool, sprites, hitbox)
        for (int i = 0; i < 60 * 8 && brain.Volleys < 1; i++) Step(brain);
        for (int i = 0; i < 60; i++) Step(brain);
        bool meter = TestHarness.AllocMeterWorks(out long control);
        int volleys = brain.Volleys;
        var fbk = brain.GetComponent<EnemyFlipbook>();
        // (the Ember mine stays put: no mount step needed; TestHarness.Send itself allocates)
        long used = TestHarness.AllocatedBytes(() =>
        {
            for (int i = 0; i < 60 * 6; i++)
            {
                clock += Dt;
                SpawnSpace.ClockOverride = clock;
                brain.Step(Dt);
                if (fbk != null) fbk.Advance(Dt);
                var l = brain.Laser;
                if (l != null) l.Place();
            }
        });
        Check("a mine's second fire -- aim line, beam, flicker -- allocates nothing (" + used + " bytes over " + (brain.Volleys - volleys) +
              " fire; the meter saw " + control + " for its control)", meter && used == 0 && brain.Volleys > volleys);
        Check("the lasers are pooled (" + RailMineLasers.Created + " built for " + brain.Volleys + " fires)",
              RailMineLasers.Created == RailMineLasers.Max);
        Object.DestroyImmediate(brain.gameObject);
        Object.DestroyImmediate(rail);
    }

    // ---- 8. every aspect: rail face to rail face -----------------------------

    static readonly string[] Aspects = { "and-1080x1920", "iphone-13", "flip7-1080x2520", "ipad-9" };

    static void SpansEveryAspect()
    {
        EditorSceneLoader.Open("gameS1");
        var bad = new List<string>();
        int n = 0;
        foreach (var id in Aspects)
        {
            var d = FitDevice.Find(id);
            foreach (var theme in WorldManager.Worlds)
            {
                ScreenInfo.ClearOverride();
                ScreenInfo.Override(d.w, d.h, d.Safe, d.Cutouts, d.ReportedDpi, d.ios);
                var cam = Camera.main;
                cam.aspect = d.Aspect;
                cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, d.w, d.h);
                BossRails.Reset();
                PlayField.Reset();
                WorldPainter.Apply(theme);
                WorldPainter.VisibleRailEdges(GameObject.Find("leftPipe"), out float inL, out float outL);
                WorldPainter.VisibleRailEdges(GameObject.Find("rightPipe"), out float inR, out float outR);
                float halfW = cam.orthographicSize * cam.aspect;
                foreach (bool right in new[] { true, false })
                {
                    n++;
                    float mx = enmiesOnBoard.WorldRailX(!right);
                    RailMineLaser.SpanFor(mx, out float from, out float to);
                    float own = right ? inR : -inL, other = right ? -inL : inR;
                    if (Mathf.Abs(from - own) > .005f || Mathf.Abs(to - other) > .005f || Mathf.Abs(from) > halfW || Mathf.Abs(to) > halfW)
                        bad.Add(id + " " + theme.displayName + (right ? " R" : " L") + " " + F(from) + ".." + F(to) + " rails " + F(-inL) + "/" + F(inR));
                }
            }
        }
        Check("at 9:16, 9:19.5, 9:21 and 3:4 in every world a mine's beam spans exactly from its own rail's drawn inner face to the " +
              "opposite one's, on screen (" + n + " cases; " + string.Join(", ", bad) + ")", bad.Count == 0 && n == 32);
        ScreenInfo.ClearOverride();
    }
}
