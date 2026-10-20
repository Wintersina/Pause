using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// The fighting zone, the rails, the boss, the escape boost and the attack cadence (docs/enemy-behaviours.md, "Fighting zone"):
//   * FightZone sits under the HUD band's bottom edge (PlayField), clear of the player's own reach;
//   * every elite of every world, over a long simulated fight, holds under the zone top (a roamer may cross it on its way in or
//     out, never linger); every goal is clamped by EliteShip.ClampGoal;
//   * a roster pilot's hold row is clamped (EnemyBrain.ZoneHoldY);
//   * an elite sorts UNDER the side rails (hull, plumes, lights, glows, hearts);
//   * a boss starting destroys every elite for no score and no escape loss and blocks new ones;
//   * an escape spools its engines, then boosts past its top speed and leaves;
//   * EliteTuning.AttackRate shortens every gap between attacks.
//
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites EliteFightZoneTest
public static class EliteFightZoneTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[EFZ] PASS  " : "[EFZ] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    static Transform pilot;

    public static int Execute()
    {
        fails = 0;
        float rate = EliteTuning.AttackRate;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EliteCatalog.Reload();
            ZoneIsUnderTheBand();
            EveryBrainStaysInTheZone();
            RosterHoldRows();
            SortsUnderTheRails();
            BossClearsElites();
            EscapeBoost();
            AttackCadence();
        }
        finally
        {
            EliteTuning.AttackRate = rate;
            EliteDirector.WorldOverride = -1;
            BossEncounter.ResetRun();
            EliteFight.Reset();
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            LandingSites.Override = null;
            RunScore.EndRun(RunScore.RunId);
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
        }
        Debug.Log("[EFZ] failures: " + fails);
        return fails;
    }

    static void Fresh(float speed = .25f)
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = speed;
        collisionDetection.lifeCounter = 0;
        collisionDetection.atomCheck = false;
        collisionDetection.cloakTimer = 0f;
        PlayerInvuln.Reset();
        RunScore.BeginRun(true, true);
        pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -2.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        LandingSites.Override = null;
        EliteFight.Reset();
        Random.InitState(4242);
    }

    static void Step(float seconds, System.Action each = null)
    {
        for (float t = 0f; t < seconds - 1e-4f; t += Dt)
        {
            if (each != null) each();
            EliteSystem.Step(Dt);
        }
    }

    static bool Alive(EliteShip e) => e != null && e.State != EliteState.Dead;

    static LandingSite Body(Vector2 at, int id)
    {
        var anchor = new GameObject("~Body" + id).transform;
        anchor.position = at;
        return new LandingSite { anchor = anchor, local = Vector3.zero, scale = .3f, order = -300, id = id, kind = LandingKind.Station, emerge = true };
    }

    // ---- 1. the zone ------------------------------------------------------------

    static void ZoneIsUnderTheBand()
    {
        // a 1080 x 1920 phone: the camera shows 13.2 u, the band's bottom edge is 260 px under the top of the screen
        var screen = new Vector2(1080f, 1920f);
        var f = PlayField.For(-6.6f, 13.2f, screen, new Rect(0f, 0f, 1080f, 1920f), 1920f - 260f);
        float half = .6f;
        float top = FightZone.TopFor(f, half);
        Check("the band hangs from the top: its bottom edge at " + f.bandBottom.ToString("0.00") + " of a view topping out at " + f.top.ToString("0.00"), f.hasBand && f.bandBottom < f.top - 1f);
        Check("the zone top (" + top.ToString("0.00") + ") puts the whole hull under the band's bottom edge", top + half <= f.bandBottom - FightZone.Margin + 1e-4f);
        Check("... and under the player's own reach (" + ShipReach.TopFor(f).ToString("0.00") + ")", top < ShipReach.TopFor(f));
        Check("a bigger hull holds lower", FightZone.TopFor(f, 1.2f) < top);
        var none = PlayField.For(-5f, 10f, Vector2.zero, default(Rect), -1f);
        Check("with no band laid out it falls back to a share of the view (" + FightZone.TopFor(none, 0f).ToString("0.00") + ")", Mathf.Abs(FightZone.BandBottomFor(none) - (5f - 10f * FightZone.NoBandShare)) < .01f);
    }

    // ---- 2. every brain, every world --------------------------------------------

    static void EveryBrainStaysInTheZone()
    {
        int worlds = 0;
        var seen = new HashSet<int>();
        foreach (var def in EliteCatalog.All)
        {
            Fresh(.25f);
            if (seen.Add(def.WorldIndex)) worlds++;
            bool roams = EliteBrains.Create(def.brain).Roams;
            var e = EliteShip.CreateInPlay(def, new Vector2(0f, 0f));
            float zone = e.ZoneTop;
            Check(def.key + ": a goal far above the board is clamped under the zone top (" + zone.ToString("0.00") + ")",
                  e.ClampGoal(new Vector2(0f, 99f)).y <= zone + 1e-4f);
            float worst = 0f, above = 0f, held = 0f, total = 0f;
            string heldAt = "";
            for (int i = 0; i < 30 * 60 && Alive(e) && e.State != EliteState.Retreat; i++)
            {
                float t = i * Dt;
                // a pilot that sweeps the lane and the height of the board, now and then parked high
                pilot.position = new Vector3(Mathf.Sin(t * .7f) * 2f, Mathf.Lerp(-3f, 2.4f, .5f + .5f * Mathf.Sin(t * .31f)), 0f);
                EliteSystem.Step(Dt);
                if (!Alive(e)) break;
                if (e.Offstage || e.Hidden || e.State == EliteState.Hide) continue;
                total += Dt;
                float excess = e.Position.y - e.ZoneTop;
                if (excess > worst) worst = excess;
                if (excess > .3f)
                {
                    above += Dt;
                    if (e.Velocity.magnitude < 1.2f && e.Position.y < EliteSystem.ViewTop) { held += Dt; heldAt = e.Position + " " + e.State + " zone " + e.ZoneTop.ToString("0.00"); }   // (lingering in the band, not crossing it)
                }
            }
            if (roams)
                Check(def.key + ": (roams) crosses the zone top only in passing: " + held.ToString("0.0") + " s held there (last at " + heldAt + "), " + above.ToString("0.0") + " s above of " + total.ToString("0") + " s", held <= 2.5f);
            else
                Check(def.key + ": holds under the zone top: highest " + worst.ToString("0.00") + " over, " + above.ToString("0.0") + " s above of " + total.ToString("0") + " s", worst <= .6f && above <= total * .03f);
            EliteSystem.Clear();
        }
        Check("... across " + worlds + " worlds' elites", worlds >= 4);
    }

    // ---- 3. the roster ------------------------------------------------------------

    static void RosterHoldRows()
    {
        Fresh();
        var f = PlayField.For(-6.6f, 13.2f, new Vector2(1080f, 1920f), new Rect(0f, 0f, 1080f, 1920f), 1920f - 260f);
        int pilots = 0, bad = 0;
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def);
            if (b == null || b.presence != EnemyPresence.Pilot) continue;
            pilots++;
            float drawn = SpawnSpace.BodyHalf(def, 1f).y, up = EnemyBrain.PatternUp(b);
            float view = Mathf.Max(.5f, f.Height / EnemyBrain.AuthoredViewHeight);
            float row = EnemyBrain.ZoneHoldY(f.top - b.stationDepth * view, drawn, up);
            float row2 = EnemyBrain.ZoneHoldY(f.top + 3f, drawn, up);   // (a hold row asked for far above)
            // (the live zone is the no-band fallback here: compare against it)
            float band = FightZone.BandBottomFor(PlayField.Live);
            if (row + up + drawn > band + 1e-3f || row2 + up + drawn > band + 1e-3f) { bad++; Debug.Log("[EFZ] over: " + def.key); }
        }
        Check("every roster pilot's hold row (" + pilots + ") keeps its drawing and its pattern's reach under the band", pilots > 0 && bad == 0);
    }

    static bool UnderNamed(Transform t, string name)
    {
        for (var p = t.parent; p != null; p = p.parent) if (p.name == name) return true;
        return false;
    }

    // ---- 4. under the rails -------------------------------------------------------

    static void SortsUnderTheRails()
    {
        Check("the elite layer is below the rails (" + EliteShip.PlayOrder + " vs " + EliteShip.RailOrder + ") and above the backdrop's hazard layers", EliteShip.PlayOrder + 2 < EliteShip.RailOrder && EliteShip.PlayOrder > -7);
        foreach (var def in EliteCatalog.All)
        {
            Fresh();
            var e = EliteShip.CreateInPlay(def, new Vector2(0f, 0f));
            Step(.5f);
            int highest = int.MinValue;
            string which = "";
            foreach (var sr in e.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (UnderNamed(sr.transform, "Marks") || UnderNamed(sr.transform, "~HeartBreaks")) continue;   // (the attack's own ring marks, a lost heart's crumble)
                if (sr.sortingOrder > highest) { highest = sr.sortingOrder; which = sr.name; }
            }
            var hearts = e.GetComponent<EliteHearts>();
            if (hearts != null && hearts.Hearts != null)
                foreach (var h in hearts.Hearts)
                {
                    var sr = h != null ? h.GetComponent<SpriteRenderer>() : null;
                    if (sr != null && sr.sortingOrder > highest) { highest = sr.sortingOrder; which = "heart"; }
                }
            Check(def.key + ": hull, flames, lights, glows and hearts all under the rails (highest " + which + " " + highest + ")", highest < EliteShip.RailOrder);
            EliteSystem.Clear();
        }
    }

    // ---- 5. a boss ----------------------------------------------------------------

    static void BossClearsElites()
    {
        Fresh(.37f);
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        EliteDirector.WorldOverride = 1;
        var defs = new List<EliteDef>();
        EliteCatalog.ForWorld(1, defs);
        Check("setup: Frost has elites", defs.Count > 0);
        var def = defs[0];
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        RunScore.OnElite(Vector3.zero, 300);
        long before = RunScore.Total;
        var live = EliteShip.CreateInPlay(def, new Vector2(0f, 0f));
        var launching = dir.Spawn(def, Body(new Vector2(1.5f, 3f), 1), 2.6f);
        launching.ForceLiftOff();
        Step(1.2f);
        var parked = dir.Spawn(def, Body(new Vector2(-1.5f, 3f), 2), 30f);
        Check("setup: one in play, one launching, one parked", live.InPlay && launching.State == EliteState.LiftOff && parked.State == EliteState.Parked);
        int kills = EliteShip.Kills, escapes = EliteShip.Escapes, paid = EliteRewards.Paid, deaths = EliteDeath.Played, gone = EliteShip.Dismissed;
        float dust = EliteRewards.LastDust;
        Check("no boss yet: spawning is not blocked for 'boss'", EliteDirector.Blocked(1, 100f) != "boss");
        BossEncounter.Begin(1, null);
        Check("a boss is running", BossEncounter.Running && EliteDirector.Blocked(1, 100f) == "boss");
        dir.Tick(Dt);
        Check("every elite is gone (" + EliteShip.Live.Count + " left)", EliteShip.Live.Count == 0 && !Alive(live) && !Alive(launching) && !Alive(parked));
        Check("... three dismissed", EliteShip.Dismissed == gone + 3);
        Check("the ones on the board died through the normal death FX (" + (EliteDeath.Played - deaths) + ")", EliteDeath.Played - deaths >= 1);
        Check("no reward, no dust, no kill count, no escape, no score change",
              EliteRewards.Paid == paid && Mathf.Approximately(EliteRewards.LastDust, dust) && EliteShip.Kills == kills && EliteShip.Escapes == escapes && RunScore.Total == before);
        Check("none spawn while it lasts", dir.SpawnGroup(1, 1) >= 0 && EliteDirector.Blocked(1, 100f) == "boss");
        for (int i = 0; i < 90; i++) dir.Tick(Dt);
        Check("... however long the director ticks", EliteShip.Live.Count == 0);
        Object.DestroyImmediate(dir.gameObject);
        BossEncounter.ResetRun();
        EliteDirector.WorldOverride = -1;
    }

    // ---- 6. the escape boost --------------------------------------------------------

    static void EscapeBoost()
    {
        foreach (var key in new[] { "space_elite_eventide_bastion", "space_elite_rift_lancer" })
        {
            Fresh(.05f);
            var def = EliteCatalog.Find(key);
            RunScore.OnElite(Vector3.zero, 300);
            var e = EliteShip.CreateInPlay(def, new Vector2(0f, 1.5f));
            e.AttackCooldown = 999f;
            int escapes = EliteShip.Escapes;
            e.EngageLeft = 0f;
            float spoolMax = 0f, boostMax = 0f, thrustStart = -1f, thrustSpoolEnd = 0f, thrustBoost = 0f;
            bool spooled = false, boosted = false;
            float speedAtRetreat = -1f;
            for (int i = 0; i < 30 * 12 && Alive(e); i++)
            {
                EliteSystem.Step(Dt);
                if (!Alive(e)) break;
                if (e.EscapeSpooling)
                {
                    spooled = true;
                    if (thrustStart < 0f) { thrustStart = e.Thrust; speedAtRetreat = e.Velocity.magnitude; }
                    spoolMax = Mathf.Max(spoolMax, e.Velocity.magnitude);
                    thrustSpoolEnd = e.Thrust;
                }
                else if (e.EscapeBoosting)
                {
                    boosted = true;
                    boostMax = Mathf.Max(boostMax, e.Velocity.magnitude);
                    thrustBoost = Mathf.Max(thrustBoost, e.Thrust);
                }
            }
            string k = def.key + ": ";
            Check(k + "it spools its engines first (" + EliteShip.EscapeSpoolSeconds + " s)", spooled);
            Check(k + "... the hull nearly still (" + spoolMax.ToString("0.00") + " u/s, from " + speedAtRetreat.ToString("0.00") + ", top speed " + def.speed + ")", spoolMax <= Mathf.Max(speedAtRetreat, .01f) + .01f && spoolMax < def.speed * .9f);
            Check(k + "... the engines growing (" + thrustStart.ToString("0.00") + " -> " + thrustSpoolEnd.ToString("0.00") + ")", thrustSpoolEnd > thrustStart + .02f);
            Check(k + "then it boosts past its top speed (" + boostMax.ToString("0.0") + " vs " + def.speed + "), a long trail (thrust " + thrustBoost.ToString("0.0") + ")",
                  boosted && boostMax > def.speed * 1.3f && thrustBoost > 1.4f);
            Check(k + "and leaves the screen: an escape, no kill", !Alive(e) && EliteShip.Escapes == escapes + 1);
            EliteSystem.Clear();
        }
    }

    // ---- 7. the attack cadence -------------------------------------------------------

    static int AttacksIn(float seconds)
    {
        int total = 0;
        foreach (var def in EliteCatalog.All)
        {
            if (EliteBrains.Create(def.brain).Roams) continue;
            Fresh(.1f);
            var e = EliteShip.CreateInPlay(def, new Vector2(0f, 1f));
            int attacks = 0;
            // (shorter than the engagement timer: it would escape and be gone)
            for (float t = 0f; t < seconds && Alive(e); t += Dt)
            {
                EliteSystem.Step(Dt);
                if (e != null) attacks = e.Attacks;
            }
            total += attacks;
            Debug.Log("[EFZ] " + def.key + ": " + attacks + " attacks in " + seconds + " s");
            EliteSystem.Clear();
        }
        return total;
    }

    static void AttackCadence()
    {
        Check("the shared knob is on: x" + EliteTuning.AttackRate, EliteTuning.AttackRate >= 1.5f && EliteTuning.AttackRate <= 2f);
        Check("a gap is divided by it", Mathf.Abs(EliteTuning.Gap(10f) - 10f / EliteTuning.AttackRate) < 1e-4f);
        float rate = EliteTuning.AttackRate;
        // the same seed, the same elite: the first wait before an attack is exactly the multiplier shorter
        var def = EliteCatalog.All[0];
        Fresh();
        EliteTuning.AttackRate = 1f;
        Random.InitState(9);
        float slow = EliteShip.CreateInPlay(def, Vector2.zero).AttackCooldown;
        EliteSystem.Clear();
        EliteTuning.AttackRate = rate;
        Random.InitState(9);
        float fast = EliteShip.CreateInPlay(def, Vector2.zero).AttackCooldown;
        EliteSystem.Clear();
        Check("the wait before the first attack: " + slow.ToString("0.00") + " s -> " + fast.ToString("0.00") + " s (x" + (slow / Mathf.Max(.001f, fast)).ToString("0.00") + ")", Mathf.Abs(slow / fast - rate) < .01f);
        // and over simulated minutes, every elite that stays on the board
        EliteTuning.AttackRate = 1f;
        int before = AttacksIn(40f);
        EliteTuning.AttackRate = rate;
        int after = AttacksIn(40f);
        EliteTuning.AttackRate = rate;
        Check("attacks in 40 s across every non-roaming elite: " + before + " -> " + after + " (x" + (after / Mathf.Max(1f, before)).ToString("0.00") + ")", before > 0 && after >= before * 1.2f);
    }
}
