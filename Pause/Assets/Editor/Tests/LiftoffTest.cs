using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The lift-off (Liftoff): Frost's, Verdant's and Ember's climb back to space
// after their boss, a calm interlude, then the way on: Verdant's planetfall
// after Frost, Ember's after Verdant; after Ember (the last world) no portal:
// the loop starts Space again at once (LiftoffDef.autoLoop,
// WorldManager.StartLoop), the loop portal only as its fallback.
//
//   1  which world ends lift off: Frost, Verdant and Ember. Space -> Frost
//      stays the planetfall; Ember's way on is the loop itself; switched off
//      or missing art: the gateway straight away
//   2  the flow: a beat (nothing spawns, the pilot flies), the take (held,
//      shielded, free presses, lifted over the clouds), the backdrop swapped
//      to Space while the clouds cover the view, control back at release,
//      a calm interlude, then the portal with its pressure; the world
//      changes exactly once, only through the portal; score and hearts
//      carry through; a pause freezes it; nothing of it is left. Ember's:
//      the same up to the interlude's end, then Space's level begins with
//      no portal ever, one loop on (exactly once), the backdrop untouched;
//      with autoLoop off, the loop portal as before
//   3  the gateway after Frost (Verdant's planetfall) and after Verdant
//      (Ember's), flown all the way down (the world changes once, under the
//      clouds, onto the new planet)
//   4  robustness: a dead pilot freezes it; the ship gone mid-climb still
//      finishes and opens the gateway
//   5  no per-frame allocation after warm-up
public static class LiftoffTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[LIFTOFF] PASS  " : "[LIFTOFF] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags Stat = BindingFlags.NonPublic | BindingFlags.Static;
    const float Dt = 1f / 60f;
    const int Frost = 1, Verdant = 2, Ember = 3;
    static int frame;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            WhichTransitions();
            // the flow's gateway is the portal: Frost's with no Verdant
            // planetfall listed (as before Verdant's art), Verdant's with no
            // Ember planetfall listed, Ember's (the loop) for real
            PlanetfallCatalog.Defs = new[] { PlanetfallCatalog.Frost };
            Flow(Frost);
            PlanetfallCatalog.Defs = new[] { PlanetfallCatalog.Frost, PlanetfallCatalog.Verdant };
            Flow(Verdant);
            PlanetfallCatalog.Defs = PlanetfallCatalog.All;
            Flow(Ember);   // the loop, straight away
            LiftoffCatalog.Ember.autoLoop = false;
            Flow(Ember);   // the fallback: the loop portal
            LiftoffCatalog.Ember.autoLoop = true;
            NextPlanetfall(Frost);
            NextPlanetfall(Verdant);
            Robust();
            NoAllocations(Frost);
            NoAllocations(Verdant);
            NoAllocations(Ember);
        }
        finally
        {
            Gone();
            PlanetfallCatalog.Defs = PlanetfallCatalog.All;
            PlanetfallCatalog.Enabled = true;
            LiftoffCatalog.Enabled = true;
            LiftoffCatalog.Ember.autoLoop = true;
            if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
            PortalPressure.Reset();
            ShipStartSpeed.EquippedHudOverride = null;
            SpeedRamp.FrameOverride = null;
            SpeedRamp.DeltaOverride = null;
            SpeedRamp.ResetFrameGuard();
            SpeedRamp.ResetBoost();
            ResumeSlowMo.ClockOverride = null;
            ResumeSlowMo.ResetRun();
            BossEncounter.ResetRun();
            RunLoop.Reset();
            buttonClicks.playerDied = false;
            score.pauseCounter = 0;
            moveBackGround.speed = 0f;
        }
        Debug.Log("[LIFTOFF] failures: " + fails);
        return fails;
    }

    // ---- fixtures (PlanetfallTest's) ------------------------------------------

    static void FreshScene(int world)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        RunLoop.Reset();
        PortalPressure.Reset();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.aspect = 1080f / 2340f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 1080, 2340);
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        score.pauseCounter = 0;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        ShipStartSpeed.EquippedHudOverride = () => ShipStartSpeed.StockHud;
        frame = 90000;
        SpeedRamp.FrameOverride = () => frame;
        SpeedRamp.DeltaOverride = () => Dt;
        SpeedRamp.ResetFrameGuard();
        SpeedRamp.ResetBoost();
        ResumeSlowMo.ClockOverride = () => frame * Dt;
        ResumeSlowMo.ResetRun();
    }

    static WorldManager World()
    {
        var wm = new GameObject("~WorldManager").AddComponent<WorldManager>();
        wm.SendMessage("Awake");
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(wm, wm.WorldDistance);
        typeof(WorldManager).GetField("levelBegun", Inst).SetValue(wm, true);
        return wm;
    }

    static void FinishLevel(WorldManager wm)
    {
        typeof(BossEncounter).GetField("doneWorld", Stat).SetValue(null, WorldManager.CurrentIndex);
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(wm, 0f);
        wm.EndLevel();
    }

    static void Gone()
    {
        if (Liftoff.Live != null) Object.DestroyImmediate(Liftoff.Live.gameObject);
        if (Planetfall.Live != null) Object.DestroyImmediate(Planetfall.Live.gameObject);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
    }

    static Transform Ship()
    {
        var go = new GameObject("~Ship");
        go.AddComponent<movePlayer>();
        go.AddComponent<SpriteRenderer>().sortingOrder = 0;
        var child = new GameObject("Plume");
        child.transform.SetParent(go.transform, false);
        child.AddComponent<SpriteRenderer>().sortingOrder = -1;
        go.transform.position = new Vector3(-1f, -3f, 0f);
        return go.transform;
    }

    // One frame the way the game runs it: only while Flying.
    static void Fly(WorldManager wm, Liftoff l)
    {
        frame++;
        if (!WorldManager.Flying) return;
        if (wm != null) wm.Tick(Dt);
        if (l != null && l.State != Liftoff.Stage.Done) l.Step(Dt);
    }

    // ---- 1. which transitions ---------------------------------------------------

    static void WhichTransitions()
    {
        Check("the catalogue: Frost, Verdant and Ember (on the loop) lift off; Space does not",
              LiftoffCatalog.For(1, 2, false) == LiftoffCatalog.Frost && LiftoffCatalog.For(0, 1, false) == null &&
              LiftoffCatalog.For(2, 3, false) == LiftoffCatalog.Verdant && LiftoffCatalog.For(3, 0, true) == LiftoffCatalog.Ember &&
              LiftoffCatalog.Frost.planet == PlanetfallCatalog.Frost && LiftoffCatalog.Frost.interludeWorld == 0 &&
              LiftoffCatalog.Verdant.planet == PlanetfallCatalog.Verdant && LiftoffCatalog.Verdant.interludeWorld == 0 &&
              LiftoffCatalog.Ember.planet == PlanetfallCatalog.Ember && LiftoffCatalog.Ember.interludeWorld == 0);
        Check("... the gateways: Frost's is Verdant's planetfall, Verdant's Ember's, Ember's the loop portal (never a planetfall)",
              PlanetfallCatalog.For(1, 2, false) == PlanetfallCatalog.Verdant && PlanetfallCatalog.For(2, 3, false) == PlanetfallCatalog.Ember &&
              PlanetfallCatalog.For(3, 0, true) == null && PlanetfallCatalog.For(3, 1, true) == null);

        for (int world = 0; world < WorldManager.Worlds.Length; world++)
        {
            FreshScene(world);
            var wm = World();
            FinishLevel(wm);
            string name = WorldManager.Worlds[world].displayName;
            bool stage = wm.Stage == WorldManager.LevelStage.Portal && wm.PortalIsOpen;
            if (world == 0)
                Check(name + "'s end: the planetfall (unchanged)", stage && Planetfall.Live != null && Liftoff.Live == null && Portal.Live == null);
            else if (world == Frost || world == Verdant || world == Ember)
                Check(name + "'s end: the lift-off; no portal, no pressure yet, nothing spawning",
                      stage && Liftoff.Live != null && Portal.Live == null && Planetfall.Live == null && !PortalPressure.Active &&
                      Liftoff.SuspendsSpawning && !Liftoff.HoldsShip && Liftoff.Live.State == Liftoff.Stage.Beat);
            else
                Check(name + "'s end: the portal (unchanged)", stage && Portal.Live != null && Liftoff.Live == null &&
                      Planetfall.Live == null && PortalPressure.Active);
            Gone();
            Object.DestroyImmediate(wm.gameObject);
        }

        LiftoffCatalog.Enabled = false;
        FreshScene(Frost);
        var w2 = World();
        FinishLevel(w2);
        Check("lift-offs switched off: Frost opens its gateway at once (Verdant's planet)",
              Liftoff.Live == null && Planetfall.Live != null && Planetfall.Live.Def == PlanetfallCatalog.Verdant && PortalPressure.Active);
        Gone();
        Object.DestroyImmediate(w2.gameObject);
        LiftoffCatalog.Enabled = true;

        string folder = PlanetfallCatalog.Frost.folder;
        try
        {
            PlanetfallCatalog.Frost.folder = "Worlds/Nowhere/";
            FreshScene(Frost);
            var wm = World();
            FinishLevel(wm);
            Check("Frost's art missing: no lift-off, the gateway straight away (Verdant's planet)",
                  Liftoff.Live == null && Planetfall.Live != null && Planetfall.Live.Def == PlanetfallCatalog.Verdant && PortalPressure.Active);
            Gone();
            Object.DestroyImmediate(wm.gameObject);
        }
        finally { PlanetfallCatalog.Frost.folder = folder; }
    }

    // ---- 2. the flow ---------------------------------------------------------------

    // `from` lifts off; the gateway after it must be the portal to the next
    // (after the last world: the loop's portal back to where the run began).
    static void Flow(int from)
    {
        bool loop = from == WorldManager.Worlds.Length - 1;
        int to = loop ? RunLoop.StartWorld : from + 1;
        int loopWas = RunLoop.Index;
        string name = WorldManager.Worlds[from].displayName, next = WorldManager.Worlds[to].displayName;
        FreshScene(from);
        RunScore.BeginRun(true, true);
        RunScore.Tick(10f, .3f);
        var ship = Ship();
        var hull = ship.GetComponent<SpriteRenderer>();
        var plume = ship.GetChild(0).GetComponent<SpriteRenderer>();
        collisionDetection.lifeCounter = 1;
        var wm = World();
        FinishLevel(wm);
        var l = Liftoff.Live;
        long before = RunScore.Total;
        Check(name + "'s lift-off flies its own planet's art", l != null && l.Def.world == from && l.Art.Def.world == from);
        var liftArt = l.Art;
        // the last world's: no portal, the loop begins at the interlude's end
        bool direct = l.Def.autoLoop && loop;
        // ... under the backdrop the interlude already shows (a real one here)
        WorldBackdrop backdrop = null;
        if (direct)
        {
            backdrop = WorldBackdrop.Create(name);
            backdrop.Show(name, false);
        }
        BackdropSet interludeSky = null;
        int loopBefore = RunLoop.Index;

        // the beat: the pilot still flies
        for (int i = 0; i < 10; i++) Fly(wm, l);
        Check("the beat: the pilot flies, nothing spawns, nothing is shielded",
              l.State == Liftoff.Stage.Beat && !Liftoff.HoldsShip && !Liftoff.ShieldsShip && !Liftoff.FreePress && Liftoff.SuspendsSpawning);

        while (l.State == Liftoff.Stage.Beat) Fly(wm, l);
        Check("the take: the ship is held and shielded, a press is free, still no pressure",
              Liftoff.HoldsShip && Liftoff.ShieldsShip && Liftoff.FreePress && Liftoff.SuspendsSpawning && !PortalPressure.Active &&
              l.Ship == ship && wm.Stage == WorldManager.LevelStage.Portal);
        Check("... the ship's renderers are lifted over the clouds (hull " + hull.sortingOrder + ", plume " + plume.sortingOrder + ")",
              hull.sortingOrder == Planetfall.Raise && plume.sortingOrder == Planetfall.Raise - 1);

        int changes = 0;
        float swappedAt = -1f, coverAtSwap = -1f, releasedAt = -1f, gatewayAt = -1f;
        bool pausedHeld = true, heldThrough = true, moved = false, calm = true, noPortalBefore = true;
        bool released = false, freeAfter = true, boostSeen = false, thrustSeen = false, planetSeen = false, globeSeen = false;
        Vector3 shipWas = ship.position;
        Vector3 releasedPos = Vector3.zero;
        int hullAtRelease = -1, plumeAtRelease = -1;
        for (int i = 0; i < (int)(15f / Dt) && l != null && l.State != Liftoff.Stage.Done; i++)
        {
            if (i == 100)
            {
                score.pauseCounter = 3;
                float s0 = l.Seconds;
                Vector3 at = ship.position;
                for (int k = 0; k < 90; k++) Fly(wm, l);
                pausedHeld = l.Seconds == s0 && ship.position == at && Liftoff.HoldsShip;
                score.pauseCounter = 0;
            }
            bool wasSwapped = l.Swapped;
            int world = WorldManager.CurrentIndex;
            Fly(wm, l);
            if (WorldManager.CurrentIndex != world) changes++;
            if (!wasSwapped && l.Swapped) { swappedAt = l.Seconds; coverAtSwap = LiftoffTimeline.Cover(l.Seconds); }
            boostSeen |= WorldBackdrop.ScrollBoost >= LiftoffTimeline.BackdropBoost - .01f;
            thrustSeen |= l.PlumeRenderer != null && l.PlumeRenderer.enabled;
            if (l.State == Liftoff.Stage.Rise)
            {
                heldThrough &= Liftoff.HoldsShip && Liftoff.ShieldsShip;
                moved |= (ship.position - shipWas).sqrMagnitude > 1e-6f;
                planetSeen |= l.LimbRenderer.enabled && l.Seconds > LiftoffTimeline.BreakAt;
            }
            if (l.State == Liftoff.Stage.Interlude)
            {
                if (!released)
                {
                    released = true;
                    releasedAt = l.Seconds;
                    releasedPos = ship.position;
                    hullAtRelease = hull.sortingOrder;
                    plumeAtRelease = plume.sortingOrder;
                }
                freeAfter &= !Liftoff.HoldsShip && !Liftoff.ShieldsShip && !Liftoff.FreePress;
                calm &= Liftoff.SuspendsSpawning && SpaceDirector.Quiet;
                globeSeen |= l.PlanetRenderer.enabled && !l.LimbRenderer.enabled;
                if (backdrop != null) interludeSky = backdrop.Current;
            }
            if (l.State != Liftoff.Stage.Done || direct) noPortalBefore &= Portal.Live == null && !PortalPressure.Active;
            else gatewayAt = l.Seconds;
        }
        Check("a lifted finger mid-climb froze it (clock, ship)", pausedHeld);
        Check("the ship was flown and shielded through the rise", heldThrough && moved);
        Check("the thrusters flared and the backdrop rushed (boost " + LiftoffTimeline.BackdropBoost + ")", thrustSeen && boostSeen);
        Check("the backdrop became Space while the clouds covered the view (at " + swappedAt.ToString("F2") + " s, cover " +
              coverAtSwap.ToString("F3") + ")", swappedAt >= LiftoffTimeline.SwapAt && swappedAt < LiftoffTimeline.SwapAt + .05f &&
              coverAtSwap >= .999f);
        Check("... and the cover is total from before the swap to the break",
              LiftoffTimeline.Cover(LiftoffTimeline.SwapAt - .35f) >= .999f && LiftoffTimeline.Cover(LiftoffTimeline.BreakAt) >= .999f);
        Check("the planet's horizon showed below after the break, the globe in the interlude", planetSeen && globeSeen);
        Check("control returned at release (" + releasedAt.ToString("F2") + " s): hooks off, renderers back (hull " + hullAtRelease +
              ", plume " + plumeAtRelease + "), the ship at its start " + releasedPos,
              released && freeAfter && hullAtRelease == 0 && plumeAtRelease == -1 &&
              Mathf.Abs(releasedPos.x) < 1e-3f && Mathf.Abs(releasedPos.y - ShipReach.StartY) < 1e-3f);
        Check("the interlude was calm: nothing spawning, Space's backdrop quiet", calm);
        if (direct)
        {
            Check("no portal and no pressure, ever; the world changed exactly once, at the interlude's end, to " + next + " (" + changes + ")",
                  noPortalBefore && Portal.Live == null && Object.FindFirstObjectByType<Portal>() == null && !PortalPressure.Active &&
                  changes == 1 && WorldManager.CurrentIndex == to && gatewayAt >= LiftoffTimeline.GatewayAt - .05f);
            Check(next + "'s level begins at once: stage Level, a full world ahead, the loop's arrival speed, the lift-off gone",
                  Liftoff.Live == null && Planetfall.Live == null && wm.Stage == WorldManager.LevelStage.Level && !wm.PortalIsOpen &&
                  Mathf.Approximately(wm.DistanceLeft, WorldManager.WorldDistanceFor(to)) &&
                  Mathf.Approximately(SpeedRamp.Natural, WorldManager.ArrivalSpeed(RunLoop.Index)) &&
                  !Liftoff.SuspendsSpawning && !SpaceDirector.Quiet && WorldBackdrop.ScrollBoost == 1f && !WorldTransition.InProgress);
            Check("... one loop on, exactly once (" + loopBefore + " -> " + RunLoop.Index + "); every boss to fight again",
                  RunLoop.Index == loopBefore + 1 && !BossEncounter.DoneInWorld(from));
            Check("... the world bonus paid once (" + before + " -> " + RunScore.Total + "), hearts untouched",
                  RunScore.Total == before + ScoreRules.WorldClearedPoints(from) && collisionDetection.lifeCounter == 1);
            Check("... seamless: the interlude's Space sky is the level's, not rebuilt or cross-faded",
                  interludeSky != null && backdrop.Current == interludeSky && interludeSky.Spec.world == BackdropCatalog.For(next).world);
            Check("... nothing of it is left in the scene, its art released",
                  GameObject.Find("~LiftoffStage") == null && GameObject.Find("~Liftoff") == null &&
                  !liftArt.Complete && liftArt.PlanetTex == null && liftArt.DeckTex == null && liftArt.Entry == null);
            for (int i = 0; i < 120; i++) wm.Tick(Dt);
            Check("... and no portal turns up after (2 s of " + next + ")", Portal.Live == null && !PortalPressure.Active && RunLoop.Index == loopBefore + 1);
            Object.DestroyImmediate(backdrop.gameObject);
            Gone();
            Object.DestroyImmediate(ship.gameObject);
            Object.DestroyImmediate(wm.gameObject);
            return;
        }
        Check("no portal and no pressure before the gateway; the world never changed on the way (" + changes + ")",
              noPortalBefore && changes == 0 && WorldManager.CurrentIndex == from);
        Check("the gateway (" + gatewayAt.ToString("F2") + " s): the portal to " + next + " with its pressure, the lift-off gone",
              Liftoff.Live == null && Portal.Live != null && Planetfall.Live == null && PortalPressure.Active && PortalPressure.Destination == to &&
              wm.Stage == WorldManager.LevelStage.Portal && !Liftoff.SuspendsSpawning && WorldBackdrop.ScrollBoost == 1f &&
              gatewayAt >= LiftoffTimeline.GatewayAt - .05f);
        Check("... nothing of it is left in the scene, its art released",
              GameObject.Find("~LiftoffStage") == null && GameObject.Find("~Liftoff") == null &&
              !liftArt.Complete && liftArt.PlanetTex == null && liftArt.DeckTex == null && liftArt.Entry == null);
        Check("score carried through (no world bonus yet: " + before + " -> " + RunScore.Total + "), hearts untouched",
              RunScore.Total == before && collisionDetection.lifeCounter == 1);

        // through the portal: the world change, once
        if (Portal.Live == null) { Gone(); Object.DestroyImmediate(ship.gameObject); Object.DestroyImmediate(wm.gameObject); return; }
        Portal.Live.Enter();
        Check("through the portal: " + next + ", stage Level, a full world ahead, the world bonus paid once",
              WorldManager.CurrentIndex == to && wm.Stage == WorldManager.LevelStage.Level && !PortalPressure.Active &&
              Mathf.Approximately(wm.DistanceLeft, WorldManager.WorldDistanceFor(to)) &&
              RunScore.Total == before + ScoreRules.WorldClearedPoints(from) && collisionDetection.lifeCounter == 1);
        Check("... the loop count: " + (loop ? "one loop on (" + loopWas + " -> " + RunLoop.Index + ")" : "unchanged (" + RunLoop.Index + ")"),
              RunLoop.Index == loopWas + (loop ? 1 : 0));
        Check("the timeline: take, swap, break, release, gateway in order; about nine and a half seconds",
              LiftoffTimeline.TakeAt < LiftoffTimeline.SwapAt && LiftoffTimeline.SwapAt < LiftoffTimeline.BreakAt &&
              LiftoffTimeline.BreakAt < LiftoffTimeline.ReleaseAt && LiftoffTimeline.ReleaseAt < LiftoffTimeline.GatewayAt &&
              LiftoffTimeline.GatewayAt >= 8f && LiftoffTimeline.GatewayAt <= 11f &&
              LiftoffTimeline.Cover(LiftoffTimeline.ReleaseAt) <= .001f && LiftoffTimeline.Shake(LiftoffTimeline.ReleaseAt) == 0f);
        Check("the hooks are wired: movePlayer, collisionDetection, the spawners, score, the backdrop",
              File.ReadAllText("Assets/Scripts/Ship/movePlayer.cs").Contains("Liftoff.HoldsShip") &&
              File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs").Contains("Liftoff.ShieldsShip") &&
              File.ReadAllText("Assets/Scripts/Gameplay/Spawning/enmiesOnBoard.cs").Contains("Liftoff.SuspendsSpawning") &&
              File.ReadAllText("Assets/Scripts/Gameplay/Pickups/spawnGoodStuff.cs").Contains("Liftoff.SuspendsSpawning") &&
              File.ReadAllText("Assets/Scripts/Core/score.cs").Contains("Liftoff.FreePress") &&
              File.ReadAllText("Assets/Scripts/Worlds/Backdrop/BackdropDirectors.cs").Contains("Liftoff.Live"));
        Gone();
        Object.DestroyImmediate(ship.gameObject);
        Object.DestroyImmediate(wm.gameObject);
    }

    // ---- 3. the next planet's planetfall takes the portal's place -------------------

    // `from` (Frost or Verdant) lifts off; its gateway is the next planet's
    // planetfall (Verdant's, Ember's), flown all the way down.
    static void NextPlanetfall(int from)
    {
        int to = from + 1;
        string name = WorldManager.Worlds[from].displayName, next = WorldManager.Worlds[to].displayName;
        var fall = System.Array.Find(PlanetfallCatalog.Defs, d => d.world == to);
        Check(name + " -> " + next + " is a planetfall", fall != null && PlanetfallCatalog.For(from, to, false) == fall);
        FreshScene(from);
        RunScore.BeginRun(true, true);
        RunScore.Tick(10f, .3f);
        var ship = Ship();
        var hull = ship.GetComponent<SpriteRenderer>();
        collisionDetection.lifeCounter = 1;
        var wm = World();
        FinishLevel(wm);
        var l = Liftoff.Live;
        long before = RunScore.Total;
        for (int i = 0; i < (int)(15f / Dt) && Liftoff.Live != null; i++) Fly(wm, l);
        var p = Planetfall.Live;
        Check("the lift-off ends on " + next + "'s planet approach, not the portal (" + PortalPressure.Urge + ")",
              Liftoff.Live == null && p != null && p.Def == fall && Portal.Live == null &&
              PortalPressure.Active && PortalPressure.Destination == to && PortalPressure.Urge == fall.urgeBanner &&
              WorldManager.CurrentIndex == from && wm.Stage == WorldManager.LevelStage.Portal);
        if (p == null) { Gone(); Object.DestroyImmediate(ship.gameObject); Object.DestroyImmediate(wm.gameObject); return; }
        Check("... in the interlude's quiet Space sky, with " + next + "'s own art loaded",
              SpaceDirector.Quiet && p.Art.Complete && p.Art.PlanetTex.name == fall.planet && p.Art.EntryTex.name == fall.entryFx);

        // the approach: the planet drifts in and holds station; the pilot flies
        int frames = 0;
        while (!p.OnStation && frames < 600) { FlyFall(wm, p); frames++; }
        Check("... it arrives (" + (frames * Dt).ToString("F1") + " s) and holds station, the pilot free",
              p.OnStation && !Planetfall.HoldsShip && p.Radius > Planetfall.FirstRadius && p.ReticleRenderer.enabled);

        // the descent: one world change, under the clouds, onto the new planet
        Check("the commit is taken", p.Commit(ship));
        var art = p.Art;
        int switches = 0, world = WorldManager.CurrentIndex;
        float coverAt = -1f;
        bool held = true, pausedHeld = true;
        for (int i = 0; i < (int)(10f / Dt) && p != null && p.State != Planetfall.Stage.Done; i++)
        {
            if (i == 150)
            {
                score.pauseCounter = 3;
                float s0 = p.Seconds;
                Vector3 at = ship.position;
                for (int k = 0; k < 90; k++) FlyFall(wm, p);
                pausedHeld = p.Seconds == s0 && ship.position == at && Planetfall.HoldsShip;
                score.pauseCounter = 0;
            }
            FlyFall(wm, p);
            if (WorldManager.CurrentIndex != world) { switches++; world = WorldManager.CurrentIndex; coverAt = PlanetfallTimeline.Cover(p.Seconds); }
            if (p.State != Planetfall.Stage.Done) held &= Planetfall.HoldsShip && Planetfall.ShieldsShip;
        }
        Check("the world changed exactly once, to " + next + ", while the clouds covered the view (" + switches + ", cover " +
              coverAt.ToString("F3") + ")", switches == 1 && WorldManager.CurrentIndex == to && coverAt >= .999f);
        Check("a lifted finger mid-descent froze it; the ship was held and shielded throughout", pausedHeld && held);
        Check("score carried through plus " + name + "'s world bonus once (" + before + " -> " + RunScore.Total + "), hearts untouched",
              RunScore.Total == before + ScoreRules.WorldClearedPoints(from) && collisionDetection.lifeCounter == 1);
        Check(next + "'s level begins: stage Level, a full world ahead",
              wm.Stage == WorldManager.LevelStage.Level && !wm.PortalIsOpen && !PortalPressure.Active &&
              Mathf.Approximately(wm.DistanceLeft, WorldManager.WorldDistanceFor(to)));
        Check("control returns: every hook off, the hull back (" + hull.sortingOrder + "), nothing left, the art released",
              Planetfall.Live == null && Liftoff.Live == null && !Planetfall.HoldsShip && !Planetfall.ShieldsShip &&
              !Planetfall.SuspendsSpawning && !Liftoff.SuspendsSpawning && !SpaceDirector.Quiet && hull.sortingOrder == 0 &&
              GameObject.Find("~PlanetfallStage") == null && !art.Complete && art.PlanetTex == null && art.Entry == null);
        Gone();
        Object.DestroyImmediate(ship.gameObject);
        Object.DestroyImmediate(wm.gameObject);
    }

    static void FlyFall(WorldManager wm, Planetfall p)
    {
        frame++;
        if (!WorldManager.Flying) return;
        if (wm != null) wm.Tick(Dt);
        if (p != null && p.State != Planetfall.Stage.Done) p.Step(Dt);
    }

    // ---- 4. robustness -------------------------------------------------------------

    static void Robust()
    {
        FreshScene(Frost);
        var ship = Ship();
        var wm = World();
        FinishLevel(wm);
        var l = Liftoff.Live;
        for (int i = 0; i < 20; i++) Fly(wm, l);
        buttonClicks.playerDied = true;
        float s0 = l.Seconds;
        for (int i = 0; i < 120; i++) Fly(wm, l);
        Check("a dead pilot freezes it in the beat (nothing taken)", l.Seconds == s0 && l.State == Liftoff.Stage.Beat && !Liftoff.HoldsShip);
        buttonClicks.playerDied = false;

        while (l.State == Liftoff.Stage.Beat) Fly(wm, l);
        while (l.Seconds < LiftoffTimeline.SwapAt - .5f) Fly(wm, l);
        Object.DestroyImmediate(ship.gameObject);
        for (int i = 0; i < (int)(15f / Dt) && Liftoff.Live != null; i++) Fly(wm, l);
        Check("the ship gone mid-climb: it still finishes and opens the gateway (Verdant's planet)",
              Liftoff.Live == null && Planetfall.Live != null && PortalPressure.Active && !Liftoff.HoldsShip);
        Gone();

        // a long hitch moves it at most MaxStep
        FinishLevelAgain(wm);
        l = Liftoff.Live;
        float b = l != null ? l.Seconds : -1f;
        if (l != null) l.Step(5f);
        Check("a 5 s hitch moves it at most " + Liftoff.MaxStep + " s", l != null && l.Seconds - b <= Liftoff.MaxStep + 1e-4f);
        Gone();
        Object.DestroyImmediate(wm.gameObject);
    }

    static void FinishLevelAgain(WorldManager wm)
    {
        typeof(WorldManager).GetField("portalOpen", Inst).SetValue(wm, false);
        PortalPressure.Reset();
        FinishLevel(wm);
    }

    // ---- 5. no per-frame allocation --------------------------------------------------

    static void NoAllocations(int world)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            FreshScene(world);
            var ship = Ship();
            var wm = World();
            FinishLevel(wm);
            var l = Liftoff.Live;
            long beat = 0, climb = 0, orbit = 0, interlude = 0;
            int nBeat = Mathf.FloorToInt((LiftoffTimeline.TakeAt - .1f) / Dt);
            beat = Measure(pass, nBeat, wm, l);
            while (l.Seconds < LiftoffTimeline.TakeAt + .1f) Fly(wm, l);
            int nClimb = Mathf.FloorToInt((LiftoffTimeline.SwapAt - .1f - l.Seconds) / Dt);
            climb = Measure(pass, nClimb, wm, l);
            while (l.Seconds < LiftoffTimeline.SwapAt + .1f) Fly(wm, l);
            int nOrbit = Mathf.FloorToInt((LiftoffTimeline.ReleaseAt - .1f - l.Seconds) / Dt);
            orbit = Measure(pass, nOrbit, wm, l);
            while (l.Seconds < LiftoffTimeline.ReleaseAt + .1f) Fly(wm, l);
            int nInter = Mathf.FloorToInt((LiftoffTimeline.GatewayAt - .1f - l.Seconds) / Dt);
            interlude = Measure(pass, nInter, wm, l);
            if (pass == 1)
            {
                long control;
                bool meter = TestHarness.AllocMeterWorks(out control);
                const long Margin = 512;
                Check("the allocation meter works (" + control + " bytes for the control)", meter);
                Check(WorldManager.Worlds[world].displayName + ": no allocation per frame: beat " + beat + " B, climb " + climb + " B, clouds / break / orbit " + orbit +
                      " B, interlude " + interlude + " B (margin " + Margin + ")",
                      meter && beat >= 0 && beat <= Margin && climb >= 0 && climb <= Margin && orbit >= 0 && orbit <= Margin &&
                      interlude >= 0 && interlude <= Margin);
            }
            for (int i = 0; i < 600 && Liftoff.Live != null; i++) Fly(wm, l);
            Gone();
            Object.DestroyImmediate(ship.gameObject);
            Object.DestroyImmediate(wm.gameObject);
        }
    }

    static long Measure(int pass, int frames, WorldManager wm, Liftoff l)
    {
        if (pass == 1) return TestHarness.AllocatedBytes(() => { for (int i = 0; i < frames; i++) Fly(wm, l); });
        for (int i = 0; i < frames; i++) Fly(wm, l);
        return 0;
    }
}
