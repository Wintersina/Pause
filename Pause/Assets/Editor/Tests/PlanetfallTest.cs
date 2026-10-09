using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The planetfall (Planetfall): the Space -> Frost and Frost -> Verdant
// descents that replace the portal there (Frost's after its lift-off,
// LiftoffTest; here the lift-off is switched off so Frost's gateway opens
// straight away).
//
//   1  which world changes are planetfalls: Space -> Frost and Frost ->
//      Verdant; Verdant -> Ember and the loop back keep the portal; missing
//      art falls back to the portal
//   2  the approach is the open portal's stage: pressure, a reserved column,
//      in the ship's reach, frozen by a pause
//   3  the commit: the ship is held and shielded, nothing spawns, a press is
//      free, the pressure stops, the ship is lifted over the clouds; a dead
//      pilot can't commit
//   4  the descent: the world switches exactly once, while the clouds cover
//      the whole view; score and hearts carry through; a pause freezes it;
//      control returns with every hook off and the ship put back
//   5  no per-frame allocation after warm-up
//   6  the art loads at full size with the importer's settings
public static class PlanetfallTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PLANETFALL] PASS  " : "[PLANETFALL] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags Stat = BindingFlags.NonPublic | BindingFlags.Static;
    const float Dt = 1f / 60f;
    static int frame;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            WhichTransitions();
            Approach();
            DescentFlow(0);
            DescentFlow(1);
            ShroudFits(0);
            ShroudFits(1);
            NoAllocations(0);
            NoAllocations(1);
            ArtLoads(PlanetfallCatalog.Frost, 1024f - 536f);
            ArtLoads(PlanetfallCatalog.Verdant, 1024f - 512.5f);
        }
        finally
        {
            if (Planetfall.Live != null) Object.DestroyImmediate(Planetfall.Live.gameObject);
            LiftoffCatalog.Enabled = true;
            PlanetfallCatalog.Enabled = true;
            PlanetfallCatalog.Defs = PlanetfallCatalog.All;
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
            moveBackGround.speed = 0f;
        }
        Debug.Log("[PLANETFALL] failures: " + fails);
        return fails;
    }

    // ---- fixtures (OpenPortalTest's) ----------------------------------------

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

    // The level flown and the boss dealt with: the real EndLevel path.
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

    // A ship with a hull and one child renderer (an exhaust, a heart).
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
    static void Fly(WorldManager wm, Planetfall p)
    {
        frame++;
        if (!WorldManager.Flying) return;
        if (wm != null) wm.Tick(Dt);
        if (p != null && p.State != Planetfall.Stage.Done) p.Step(Dt);
    }

    // ---- 1. which transitions --------------------------------------------------

    static void WhichTransitions()
    {
        Check("the catalogue: Space -> Frost and Frost -> Verdant are planetfalls; Verdant -> Ember and the loop are not",
              PlanetfallCatalog.For(0, 1, false) == PlanetfallCatalog.Frost && PlanetfallCatalog.For(1, 2, false) == PlanetfallCatalog.Verdant &&
              PlanetfallCatalog.For(2, 3, false) == null && PlanetfallCatalog.For(3, 0, true) == null &&
              PlanetfallCatalog.For(3, 1, true) == null);

        for (int world = 0; world < WorldManager.Worlds.Length; world++)
        {
            FreshScene(world);
            var wm = World();
            FinishLevel(wm);
            bool fall = world == 0;
            string name = WorldManager.Worlds[world].displayName;
            if (LiftoffCatalog.For(world, WorldManager.PortalDestination, !WorldManager.HasNext) != null)
                // Frost and Verdant lift off first (LiftoffTest), then their gateway
                Check(name + "'s end: the lift-off, no planetfall (stage Portal)",
                      wm.Stage == WorldManager.LevelStage.Portal && Liftoff.Live != null && Planetfall.Live == null);
            else
                Check(name + "'s end: " + (fall ? "the planetfall, no portal" : "the portal, no planetfall") +
                      " (stage Portal, pressure on)",
                      wm.Stage == WorldManager.LevelStage.Portal && PortalPressure.Active &&
                      (fall ? Planetfall.Live != null && Portal.Live == null : Planetfall.Live == null && Portal.Live != null));
            if (fall)
                Check("... the planetfall's own words for the pressure (" + PortalPressure.Urge + " / " + PortalPressure.Chip.Trim() + ")",
                      PortalPressure.Urge == PlanetfallCatalog.Frost.urgeBanner && PortalPressure.Chip == PlanetfallCatalog.Frost.chipPrefix &&
                      PortalPressureHud.ChipLabel(2) == PlanetfallCatalog.Frost.chipPrefix + 2);
            Gone();
            Object.DestroyImmediate(wm.gameObject);
        }
        Check("with the portal back, the HUD's words are the portal's again",
              PortalPressureHud.ChipLabel(2) == PortalPressure.ChipPrefix + 2 || PortalPressure.Chip == PortalPressure.ChipPrefix);
        LiftoffCatalog.Enabled = false;
        FreshScene(1);
        var wv = World();
        FinishLevel(wv);
        var verdant = PlanetfallCatalog.Verdant;
        Check("Frost's gateway (lift-off skipped): Verdant's planet, no portal, its words (" + PortalPressure.Urge + " / " +
              PortalPressure.Chip.Trim() + ")",
              Planetfall.Live != null && Planetfall.Live.Def == verdant && Portal.Live == null && PortalPressure.Active &&
              PortalPressure.Destination == 2 && PortalPressure.Urge == verdant.urgeBanner && PortalPressure.Chip == verdant.chipPrefix &&
              verdant.openBanner == "LAND ON VERDANT");
        Gone();
        Object.DestroyImmediate(wv.gameObject);
        LiftoffCatalog.Enabled = true;


        // missing art: the portal, as before
        string folder = PlanetfallCatalog.Frost.folder;
        try
        {
            PlanetfallCatalog.Frost.folder = "Worlds/Nowhere/";
            FreshScene(0);
            var wm = World();
            FinishLevel(wm);
            Check("Frost's art missing: Space opens the portal instead", Planetfall.Live == null && Portal.Live != null);
            Gone();
            Object.DestroyImmediate(wm.gameObject);
        }
        finally { PlanetfallCatalog.Frost.folder = folder; }

        PlanetfallCatalog.Enabled = false;
        FreshScene(0);
        var w2 = World();
        FinishLevel(w2);
        Check("planetfalls switched off: Space opens the portal", Planetfall.Live == null && Portal.Live != null);
        Gone();
        Object.DestroyImmediate(w2.gameObject);
        PlanetfallCatalog.Enabled = true;
    }

    // ---- 2. the approach ----------------------------------------------------------

    static void Approach()
    {
        FreshScene(0);
        var wm = World();
        FinishLevel(wm);
        var p = Planetfall.Live;
        float arrive = -1f;
        bool inReach = true, inView = true;
        for (int i = 0; i < (int)(20f / Dt); i++)
        {
            Fly(wm, p);
            if (arrive < 0f && p.OnStation) arrive = i * Dt;
            if (!p.OnStation) continue;
            Vector3 at = p.transform.position;
            inReach &= Mathf.Abs(at.x) <= ShipReach.HalfWidth && at.y >= ShipReach.Bottom && at.y - p.ZoneRadius * .5f <= ShipReach.Top;
            inView &= at.y + p.Radius <= CameraFit.ViewTop && at.y - p.Radius >= CameraFit.ViewBottom &&
                      Mathf.Abs(at.x) + p.Radius < CameraFit.GameplayHalfWidth;
        }
        Check("the planet arrives in " + arrive.ToString("F1") + " s and holds station", arrive > 0f && arrive <= Planetfall.ArriveSeconds + .1f);
        Check("on station its zone is where the ship can fly", inReach);
        Check("... and the whole planet stays in view", inView);
        Check("the wait is the portal's: the pressure ticks on flight time (" + PortalPressure.Seconds.ToString("F1") + " s)",
              Mathf.Abs(PortalPressure.Seconds - 20f) < .1f && wm.Stage == WorldManager.LevelStage.Portal);
        float far = p.HomeX - Mathf.Sign(p.HomeX) * (Planetfall.ColumnHalf + .2f);
        Check("its column is closed to pilots, the far side (x " + far.ToString("F2") + ") open",
              Planetfall.Reserves(p.HomeX - .1f, p.HomeX + .1f) && !Planetfall.Reserves(far - .05f, far + .05f) &&
              Mathf.Abs(far) < ShipReach.HalfWidth);
        Check("the cue shows: the glow and the reticle, the planet drawn in the backdrop under the board",
              p.RimRenderer.enabled && p.ReticleRenderer.enabled && p.PlanetRenderer.enabled &&
              p.PlanetRenderer.sortingOrder < 0 && p.ReticleRenderer.sortingOrder > 0);
        Check("no hooks while it waits: the pilot flies", !Planetfall.HoldsShip && !Planetfall.ShieldsShip && !Planetfall.SuspendsSpawning);

        float s = p.Seconds, ps = PortalPressure.Seconds;
        Vector3 at0 = p.transform.position;
        score.pauseCounter = 3;   // no touch in batch mode: paused
        for (int i = 0; i < 120; i++) Fly(wm, p);
        Check("paused: the planet and the pressure hold still", p.Seconds == s && PortalPressure.Seconds == ps && p.transform.position == at0);
        score.pauseCounter = 0;
        Check("the source: Update steps only when Flying",
              File.ReadAllText("Assets/Scripts/Worlds/Planetfall/Planetfall.cs").Contains("if (WorldManager.Flying) Step(Time.deltaTime);"));

        buttonClicks.playerDied = true;
        var ship = Ship();
        Check("a dead pilot can't commit", !p.Commit(ship) && p.State == Planetfall.Stage.Approach);
        buttonClicks.playerDied = false;
        Gone();
        Object.DestroyImmediate(ship.gameObject);
        Object.DestroyImmediate(wm.gameObject);
    }

    // ---- 3, 4. the commit and the descent ------------------------------------------

    // `from`: Space (onto Frost) or Frost (onto Verdant; its lift-off skipped).
    static void DescentFlow(int from)
    {
        int to = from + 1;
        string onto = WorldManager.Worlds[to].displayName;
        LiftoffCatalog.Enabled = false;
        FreshScene(from);
        RunScore.BeginRun(true, true);
        RunScore.Tick(10f, .3f);
        var wm = World();
        FinishLevel(wm);
        var p = Planetfall.Live;
        for (int i = 0; i < 300; i++) Fly(wm, p);
        var ship = Ship();
        var hull = ship.GetComponent<SpriteRenderer>();
        var plume = ship.GetChild(0).GetComponent<SpriteRenderer>();
        collisionDetection.lifeCounter = 1;
        long before = RunScore.Total;

        Check(onto + ": the commit is taken", p != null && p.Def.world == to && p.Commit(ship));
        Check("... the ship is held and shielded, nothing spawns, a press is free",
              Planetfall.HoldsShip && Planetfall.ShieldsShip && Planetfall.SuspendsSpawning && Planetfall.FreePress);
        Check("... the pressure is over, the stage still Portal until the switch, the column released",
              !PortalPressure.Active && wm.Stage == WorldManager.LevelStage.Portal && !Planetfall.Reserves(-9f, 9f));
        Check("... the ship's renderers are lifted over the clouds (hull " + hull.sortingOrder + ", plume " + plume.sortingOrder + ")",
              hull.sortingOrder == Planetfall.Raise && plume.sortingOrder == Planetfall.Raise - 1 &&
              hull.sortingOrder > Planetfall.BurstOrder && hull.sortingOrder < Planetfall.FlashOrder);
        Check("a second touch does nothing", !p.Commit(ship));

        int switches = 0;
        float switchedAt = -1f, coverAt = -1f, darkAlphaAt = -1f;
        bool darkOnAt = false, shipHeld = true, moved = false;
        bool pausedHeld = true;
        Vector3 shipWas = ship.position;
        int world = WorldManager.CurrentIndex;
        for (int i = 0; i < (int)(10f / Dt) && p != null && p.State != Planetfall.Stage.Done; i++)
        {
            // a lifted finger halfway through: everything holds
            if (i == 150)
            {
                score.pauseCounter = 3;
                float s0 = p.Seconds;
                Vector3 at = ship.position;
                for (int k = 0; k < 90; k++) Fly(wm, p);
                pausedHeld = p.Seconds == s0 && ship.position == at && Planetfall.HoldsShip;
                score.pauseCounter = 0;
            }
            float tBefore = p.Seconds;
            Fly(wm, p);
            if (WorldManager.CurrentIndex != world)
            {
                switches++;
                world = WorldManager.CurrentIndex;
                switchedAt = p.Seconds;
                coverAt = PlanetfallTimeline.Cover(p.Seconds);
                darkOnAt = p.DarkDeckRenderer.enabled;
                darkAlphaAt = p.DarkDeckRenderer.color.a;
            }
            if (p.State != Planetfall.Stage.Done)
            {
                shipHeld &= Planetfall.HoldsShip;
                moved |= (ship.position - shipWas).sqrMagnitude > 1e-6f;
            }
        }
        Check("the world switched exactly once, to " + onto + " (" + switches + ", at " + switchedAt.ToString("F2") + " s)",
              switches == 1 && WorldManager.CurrentIndex == to);
        Check("... while the clouds covered the whole view (cover " + coverAt.ToString("F3") + ", deep deck alpha " +
              darkAlphaAt.ToString("F3") + ")", coverAt >= .999f && darkOnAt && darkAlphaAt >= .999f &&
              switchedAt >= PlanetfallTimeline.SwitchAt && switchedAt < PlanetfallTimeline.SwitchAt + .05f);
        Check("... and the cover is total from well before to after it",
              PlanetfallTimeline.Cover(PlanetfallTimeline.SwitchAt - .5f) >= .999f &&
              PlanetfallTimeline.Cover(PlanetfallTimeline.SwitchAt + 1.2f) >= .999f);   // the backdrop's 1.1 s cross-fade is hidden too
        Check("a lifted finger mid-descent froze it (clock, ship)", pausedHeld);
        Check("the ship was flown by the descent the whole time", shipHeld && moved);
        Check("score carried through, plus the world bonus (" + before + " -> " + RunScore.Total + "); hearts untouched",
              RunScore.Total == before + ScoreRules.WorldClearedPoints(from) && collisionDetection.lifeCounter == 1);
        Check(onto + "'s level begins: stage Level, a full world ahead, the arrival speed",
              wm.Stage == WorldManager.LevelStage.Level && !wm.PortalIsOpen &&
              Mathf.Approximately(wm.DistanceLeft, WorldManager.WorldDistanceFor(to)) &&
              Mathf.Approximately(SpeedRamp.Natural, WorldManager.ArrivalSpeed(RunLoop.Index)));
        Check("control returns: the planetfall is gone and every hook is off",
              Planetfall.Live == null && !Planetfall.HoldsShip && !Planetfall.ShieldsShip && !Planetfall.SuspendsSpawning &&
              !Planetfall.FreePress && WorldBackdrop.ScrollBoost == 1f);
        Check("... the ship's renderers are back (hull " + hull.sortingOrder + ", plume " + plume.sortingOrder + ")",
              hull.sortingOrder == 0 && plume.sortingOrder == -1);
        Check("... the ship at its start on the centre line (no finger): " + ship.position,
              Mathf.Abs(ship.position.x) < 1e-3f && Mathf.Abs(ship.position.y - ShipReach.StartY) < 1e-3f);
        Check("... nothing of it is left in the scene",
              GameObject.Find("~PlanetfallStage") == null && GameObject.Find("~Planetfall") == null);
        Check("the timeline: about seven seconds, switch before break before release",
              PlanetfallTimeline.Seconds >= 6f && PlanetfallTimeline.Seconds <= 8f &&
              PlanetfallTimeline.SwitchAt < PlanetfallTimeline.BreakAt && PlanetfallTimeline.BreakAt < PlanetfallTimeline.Seconds &&
              PlanetfallTimeline.Cover(PlanetfallTimeline.Seconds) <= .001f);
        Check("the hooks are wired: movePlayer, collisionDetection, the spawners, score",
              File.ReadAllText("Assets/Scripts/Ship/movePlayer.cs").Contains("Planetfall.HoldsShip") &&
              File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs").Contains("Planetfall.ShieldsShip") &&
              File.ReadAllText("Assets/Scripts/Gameplay/Spawning/enmiesOnBoard.cs").Contains("Planetfall.SuspendsSpawning") &&
              File.ReadAllText("Assets/Scripts/Gameplay/Pickups/spawnGoodStuff.cs").Contains("Planetfall.SuspendsSpawning") &&
              File.ReadAllText("Assets/Scripts/Core/score.cs").Contains("Planetfall.FreePress"));
        Object.DestroyImmediate(ship.gameObject);
        Object.DestroyImmediate(wm.gameObject);
        LiftoffCatalog.Enabled = true;
    }

    // ---- 5. no per-frame allocation ---------------------------------------------------

    static void NoAllocations(int from)
    {
        LiftoffCatalog.Enabled = false;
        // warm-up: one whole planetfall (JIT, caches), then measure a second
        for (int pass = 0; pass < 2; pass++)
        {
            FreshScene(from);
            var wm = World();
            FinishLevel(wm);
            var p = Planetfall.Live;
            var ship = Ship();
            for (int i = 0; i < 120; i++) Fly(wm, p);
            long approach = 0, early = 0, late = 0;
            if (pass == 1) approach = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 120; i++) Fly(wm, p); });
            else for (int i = 0; i < 120; i++) Fly(wm, p);
            p.Commit(ship);
            for (int i = 0; i < 12; i++) Fly(wm, p);
            // to just before the switch, then from after it to before the banner
            int toSwitch = Mathf.FloorToInt((PlanetfallTimeline.SwitchAt - .1f - p.Seconds) / Dt);
            if (pass == 1) early = TestHarness.AllocatedBytes(() => { for (int i = 0; i < toSwitch; i++) Fly(wm, p); });
            else for (int i = 0; i < toSwitch; i++) Fly(wm, p);
            while (p.Seconds < PlanetfallTimeline.SwitchAt + .1f) Fly(wm, p);
            int toBanner = Mathf.FloorToInt((PlanetfallTimeline.BannerAt - .1f - p.Seconds) / Dt);
            if (pass == 1) late = TestHarness.AllocatedBytes(() => { for (int i = 0; i < toBanner; i++) Fly(wm, p); });
            else for (int i = 0; i < toBanner; i++) Fly(wm, p);
            if (pass == 1)
            {
                long control;
                bool meter = TestHarness.AllocMeterWorks(out control);
                // A small margin: the recorder has been seen to catch a stray
                // editor allocation in an otherwise clean window.
                const long Margin = 512;
                Check("the allocation meter works (" + control + " bytes for the control)", meter);
                Check(WorldManager.Worlds[from + 1].displayName + ": no allocation per frame: 120 approach frames " + approach + " B, " + toSwitch + " descent frames " + early +
                      " B, " + toBanner + " cloud / break frames " + late + " B (margin " + Margin + ")",
                      meter && approach >= 0 && approach <= Margin && early >= 0 && early <= Margin && late >= 0 && late <= Margin);
            }
            for (int i = 0; i < 600 && Planetfall.Live != null; i++) Fly(wm, p);
            Gone();
            Object.DestroyImmediate(ship.gameObject);
            Object.DestroyImmediate(wm.gameObject);
        }
        LiftoffCatalog.Enabled = true;
    }

    // ---- 6. the art --------------------------------------------------------------------

    // Clear (alpha < 40) pixels from (x, y) stepping (dx, dy), at most 200.
    static int Clear(Color32[] px, int w, int x, int y, int dx, int dy)
    {
        int h = px.Length / w, n = 0;
        while (n < 200)
        {
            int nx = x + dx * (n + 1), ny = y + dy * (n + 1);
            if (nx < 0 || ny < 0 || nx >= w || ny >= h || px[ny * w + nx].a >= 40) break;
            n++;
        }
        return n;
    }

    // ---- the entry shroud fits the ship and lives ---------------------------
    // The hull here is a known 0.8 x 1.0 unit drawing whose middle sits 0.1
    // above the ship's position: the shroud's opening must be HoleFit times
    // its width and centred on that middle; the loop steps through all six
    // cells at about ShroudFps and wraps 5 -> 0; the additive copy rides
    // between the shroud and the hull, one cell behind.
    static void ShroudFits(int fromWorld)
    {
        LiftoffCatalog.Enabled = false;
        FreshScene(fromWorld);
        RunScore.BeginRun(true, true);
        var wm = World();
        FinishLevel(wm);
        var p = Planetfall.Live;
        for (int i = 0; i < 300; i++) Fly(wm, p);
        var ship = Ship();
        var hull = ship.GetComponent<SpriteRenderer>();
        var tex = new Texture2D(80, 100, TextureFormat.RGBA32, false);
        hull.sprite = Sprite.Create(tex, new Rect(0, 0, 80, 100), new Vector2(.5f, .4f), 100f, 0, SpriteMeshType.FullRect);
        collisionDetection.lifeCounter = 1;
        Check(WorldManager.Worlds[fromWorld + 1].displayName + " shroud fit: the commit is taken", p != null && p.Def.world == fromWorld + 1 && p.Commit(ship));
        while (p.State == Planetfall.Stage.Descent && p.Seconds < PlanetfallTimeline.ShroudInTo + .05f) Fly(wm, p);

        var art = p.Art;
        float hole = p.ShroudScale * p.Def.entryHolePx / art.EntryCellPx;
        Check("the shroud is fitted to the hull: opening " + hole.ToString("F3") + " = " + Planetfall.HoleFit +
              " x hull width " + p.ShipSpan.ToString("F3") + ", middle " + p.ShipMid.ToString("F3"),
              Mathf.Abs(p.ShipSpan - .8f) < .001f && Mathf.Abs(hole - Planetfall.HoleFit * .8f) < .01f &&
              Mathf.Abs(p.ShipMid.y - .1f) < .001f && Mathf.Abs(p.ShipMid.x) < .001f);

        var sh = p.ShroudRenderer;
        var glow = p.ShroudGlowRenderer;
        float off = (sh.transform.position - (ship.position + p.ShipMid + Vector3.down * Planetfall.HoleDrop * p.ShipSpan)).magnitude;
        Check("... it rides on the hull's middle (" + off.ToString("F3") + " off), the hot copy between it and the hull",
              sh.enabled && glow.enabled && off < .06f &&
              sh.sortingOrder < glow.sortingOrder && glow.sortingOrder < hull.sortingOrder && glow.sortingOrder < hull.sortingOrder - 1);

        int changes = 0, wraps = 0, seen = 0, behind = 0, frames = 0;
        float minScale = 99f, maxScale = 0f;
        Sprite last = sh.sprite;
        float from = p.Seconds;
        while (p.State == Planetfall.Stage.Descent && p.Seconds < from + 1f)
        {
            Fly(wm, p);
            frames++;
            int a = System.Array.IndexOf(art.Entry, sh.sprite), b = System.Array.IndexOf(art.Entry, last);
            if (sh.sprite != last) { changes++; if (b == art.Entry.Length - 1 && a == 0) wraps++; }
            seen |= 1 << a;
            if (System.Array.IndexOf(art.Entry, glow.sprite) == (a + art.Entry.Length - 1) % art.Entry.Length) behind++;
            minScale = Mathf.Min(minScale, sh.transform.localScale.x);
            maxScale = Mathf.Max(maxScale, sh.transform.localScale.x);
            last = sh.sprite;
        }
        Check("... the loop animates: " + changes + " cell steps in 1 s (12..16), all six cells, wraps 5 -> 0 (" + wraps + ")",
              changes >= 12 && changes <= 16 && seen == 63 && wraps >= 2);
        Check("... the hot copy runs a cell behind every frame (" + behind + "/" + frames + "), the size flickers (" +
              minScale.ToString("F3") + " .. " + maxScale.ToString("F3") + ")",
              behind == frames && maxScale - minScale > .02f * p.ShroudScale);
        Object.DestroyImmediate(tex);
        Gone();
        LiftoffCatalog.Enabled = true;
    }

    // `planetPivotY`: the globe's centre, texture rows up from the bottom.
    static void ArtLoads(PlanetfallDef def, float planetPivotY)
    {
        string name = WorldManager.Worlds[def.world].displayName;
        var art = PlanetfallArt.Load(def);
        Check(name + "'s planetfall art loads complete", art.Complete);
        if (!art.Complete) return;
        bool sizes = Size(art.PlanetTex, 1024, 1024) & Size(art.LimbTex, 2048, 1024) & Size(art.DeckTex, 2048, 1024) &
                     Size(art.DeckDarkTex, 2048, 1024) & Size(art.EntryTex, 3072, 1024) & Size(art.BurstTex, 5120, 1024) &
                     Size(art.StreaksTex, 1024, 2048);
        Check("... every texture at its full size, no mipmaps", sizes);
        Check("... the globe's pivot on its measured centre (" + art.Planet.pivot + ")",
              Mathf.Abs(art.Planet.pivot.x - def.planetCentrePx.x) < .5f && Mathf.Abs(art.Planet.pivot.y - planetPivotY) < .5f);
        Check("wrap: Repeat for the decks and streaks, Clamp for the rest",
              art.DeckTex.wrapMode == TextureWrapMode.Repeat && art.DeckDarkTex.wrapMode == TextureWrapMode.Repeat &&
              art.StreaksTex.wrapMode == TextureWrapMode.Repeat && art.PlanetTex.wrapMode == TextureWrapMode.Clamp &&
              art.LimbTex.wrapMode == TextureWrapMode.Clamp && art.EntryTex.wrapMode == TextureWrapMode.Clamp &&
              art.BurstTex.wrapMode == TextureWrapMode.Clamp);
        Check("the strips cut into cells: " + art.Entry.Length + " entry (512x1024), " + art.Burst.Length + " burst (1024x1024)",
              art.Entry.Length == 6 && art.Burst.Length == 5 && art.Entry[5].rect.width == 512f && art.Entry[0].rect.height == 1024f &&
              art.Burst[4].rect.width == 1024f && art.Burst[4].rect.x == 4096f);
        // Each cell's pivot is its own opening's centre (the hole wanders
        // 251 .. 268 px through the loop): measured from the PNG, the clear
        // run from the pivot reaches the plasma about equally far each way.
        bool pivots = art.Entry.Length == def.entryHoleX.Length, centred = pivots;
        var png = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        png.LoadImage(File.ReadAllBytes("Assets/Art/Backgrounds/Resources/" + def.folder + def.entryFx + ".png"));
        var px = png.GetPixels32();
        string spans = "";
        for (int i = 0; pivots && i < art.Entry.Length; i++)
        {
            Vector2 pv = art.Entry[i].pivot;
            pivots &= Mathf.Abs(pv.x - def.entryHoleX[i]) < .5f && Mathf.Abs(pv.y - (1024f - def.entryShipPx.y)) < .5f;
            int cx = i * 512 + Mathf.RoundToInt(pv.x), cy = Mathf.RoundToInt(pv.y);   // texture rows are y up
            int l = Clear(px, png.width, cx, cy, -1, 0), r = Clear(px, png.width, cx, cy, 1, 0);
            int u = Clear(px, png.width, cx, cy, 0, 1), d = Clear(px, png.width, cx, cy, 0, -1);
            spans += " " + l + "/" + r + "," + u + "/" + d;
            centred &= Mathf.Abs(l - r) <= 12 && Mathf.Abs(l + r - def.entryHolePx) <= 14 && Mathf.Abs(u - d) <= 14;
        }
        Object.DestroyImmediate(png);
        Check("each entry cell's pivot is the centre of its opening (clear px left/right, up/down:" + spans + ")",
              pivots && centred);
        Check("the limb's horizon circle: radius " + def.LimbArcPx(2048f).ToString("F0") + " px (apex 355, edges 722)",
              Mathf.Abs(def.LimbArcPx(2048f) - 1612f) < 2f);
        bool importer = true;
        foreach (string f in new[] { def.planet, def.limb, def.deck, def.deckDark, def.entryFx, def.burst, def.streaks })
        {
            var ti = AssetImporter.GetAtPath("Assets/Art/Backgrounds/Resources/" + def.folder + f + ".png") as TextureImporter;
            importer &= ti != null && !ti.mipmapEnabled && ti.npotScale == TextureImporterNPOTScale.None &&
                        ti.maxTextureSize >= 5120 && ti.GetPlatformTextureSettings("Android").maxTextureSize >= 5120 &&
                        ti.filterMode == FilterMode.Bilinear;
        }
        Check("the importer: no mipmaps, never NPOT-scaled, no size cap under the art, bilinear (PlanetfallArtImporter)", importer);
        Check("the layer shader loads", Resources.Load<Shader>(Planetfall.ShaderPath) != null);
        Check("the sources stay out of the build (src~)",
              Directory.Exists("Assets/Art/Worlds/" + name + "/descent/src~") && !Directory.Exists("Assets/Art/Backgrounds/Resources/" + def.folder + "src~") &&
              Directory.GetFiles("Assets/Art/Worlds/" + name + "/descent", "*.png").Length == 0);
        art.Release();
        Check("... and released after: no textures or sprites held, no longer complete",
              !art.Complete && art.PlanetTex == null && art.EntryTex == null && art.Entry == null && art.Planet == null);
    }

    static bool Size(Texture2D t, int w, int h)
    {
        bool ok = t != null && t.width == w && t.height == h && t.mipmapCount == 1;
        if (!ok) Debug.Log("[PLANETFALL] size " + (t != null ? t.name + " " + t.width + "x" + t.height + " mips " + t.mipmapCount : "null"));
        return ok;
    }
}
