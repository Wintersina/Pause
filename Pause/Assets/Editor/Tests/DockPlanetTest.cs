using System.Collections.Generic;
using UnityEngine;

// The Space Dock's destination, warp and backdrop (DockPlanet, DockBody, DockWarp, PlanetFlipbooks, MenuBackdrop's
// dock swap) and the Space sky's next-world planet:
//
//   1  the art: every world's 4x4 rotation atlas (16 frames, point filtered, no mipmaps, 4096 at most), the sun, the
//      asteroids, the warp strip and its curves; any packing of the frames is read from the texture's size
//   2  each world selection shows its own planet; Space shows the sun and at least 4 asteroids of several kinds
//   3  the warp plays (old planet out, streaks, new planet in, 0.6-0.8 s, no white flash) and is interruptible
//   4  memory: at most two planet atlases are ever loaded, one after the warp
//   5  placement: the sphere's size and offset follow the tunables (and stay on screen on any pane)
//   6  the dock always shows a Space backdrop, and leaving it restores the menu's pick un-rolled
//   7  after a boss the Space sky's planet is the next world's rotation (Frost after Space's boss), and Frost's
//      planetfall draws the same turning planet at its existing size
public static class DockPlanetTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DOCKPLANET] PASS  " : "[DOCKPLANET] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    static readonly string[] Stems = { null, "frost", "verdant", "ember", "tide", "storm", "desert" };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        var root = new GameObject("~DockPlanetTestRoot");
        PlanetFlipbooks.ResetForTests();
        try
        {
            Art();
            Selections(root.transform);
            Warp(root.transform);
            Interrupt(root.transform);
            Memory(root.transform);
            Placement();
            Backdrop(root.transform);
            AfterBoss(root.transform);
        }
        finally
        {
            Object.DestroyImmediate(root);
            PlanetFlipbooks.ResetForTests();
            SpaceDirector.ForcedWorldOverride = -2;
            MenuBackdropSelection.ResetForTests();
        }
        Debug.Log("[DOCKPLANET] failures: " + fails);
        return fails;
    }

    static DockPlanet NewPlanet(Transform parent, int world)
    {
        var p = DockPlanet.Create(parent, world);
        p.Place(new Vector2(.4f, 0f), 3.6f);
        p.SetView(new Rect(-3f, -6f, 6f, 12f));
        return p;
    }

    // ---- 1 ----
    static void Art()
    {
        bool all = true;
        for (int w = 1; w < Stems.Length; w++)
        {
            var tex = Resources.Load<Texture2D>(PlanetFlipbooks.PathOf(w));
            bool ok = tex != null && tex.width == tex.height && tex.width <= 4096 && tex.width % 4 == 0 &&
                      tex.filterMode == FilterMode.Point && tex.mipmapCount == 1;
            Check(Stems[w] + " atlas is a square 4x4 sheet, point filtered, no mipmaps (" + (tex != null ? tex.width + "px" : "missing") + ")", ok);
            all &= ok;
            var frames = PlanetFlipbooks.Acquire(w);
            Check(Stems[w] + ": 16 frames, one unit wide each", frames != null && frames.Length == 16 && Mathf.Abs(frames[0].bounds.size.x - 1f) < .001f &&
                  frames[15].rect.x == tex.width * .75f && frames[0].rect.y == tex.height * .75f);   // frame 1 top-left, frame 16 bottom-right
            PlanetFlipbooks.Release(w);
        }
        Check("nothing stays loaded after those", PlanetFlipbooks.LoadedCount == 0);

        // the layout is read from the size: a strip, a grid or a larger square are all fine
        var strip = FlipbookGrid.Find(8192, 512, 16);
        var grid = FlipbookGrid.Find(4096, 4096, 16);
        var warpStrip = FlipbookGrid.Find(10368, 1024, 18, DockWarp.Aspect);
        var warpGrid = FlipbookGrid.Find(3456, 3072, 18, DockWarp.Aspect);
        var sun = FlipbookGrid.Find(4096, 3072, 12);
        Check("layouts: 8192x512 strip, 4096 atlas, 10368x1024 and 3456x3072 warp, 4096x3072 sun grid",
              strip.cols == 16 && strip.rows == 1 && grid.cols == 4 && grid.rows == 4 &&
              warpStrip.cols == 18 && warpStrip.rows == 1 && warpGrid.cols == 6 && warpGrid.rows == 3 && sun.cols == 4 && sun.rows == 3);

        var warp = DockWarp.Load();
        Check("the warp's per-frame curves come from warp_scale.json", warp.FromFile && warp.outScale[0] == 1f && warp.inScale[17] == 1f && warp.outAlpha[7] == 0f && warp.inAlpha[0] == 0f && warp.inScale[14] > 1f);
        var art = DockSpaceArt.Load();
        Check("the sun (12 frames) and the game's eight existing Space rock kinds (4 blink frames each) are installed", art.Ready && art.sun.Length == 12 && art.rocks.Length == 8 && art.rocks[0].Length == 4 && art.rocks[7].Length == 4);
        Check("the old Codex asteroid sheet is gone", Resources.Load<Texture2D>("Worlds/Dock/space_asteroids") == null);
        art.Destroy();
        var tex2 = Resources.Load<Texture2D>(DockWarp.StreaksPath);
        Check("the 18-frame streak art is installed and point filtered", tex2 != null && FlipbookGrid.Find(tex2.width, tex2.height, 18, DockWarp.Aspect).Valid && tex2.filterMode == FilterMode.Point);
    }

    // ---- 2 ----
    static void Selections(Transform root)
    {
        var p = NewPlanet(root, 0);
        Check("Space shows the sun", p.IsSpace && p.World == 0 && p.Renderer != null && p.Renderer.sprite != null && p.Renderer.sprite.name.StartsWith("dock_sun"));
        int kinds = 0;
        var seenKinds = new HashSet<int>();
        var widths = new HashSet<float>();
        var periods = new HashSet<float>();
        foreach (var r in DockBody.Rocks) { seenKinds.Add(r.kind); widths.Add(r.width); periods.Add(r.period); }
        kinds = seenKinds.Count;
        Check("... with at least 4 asteroids (" + p.Front.ShownRocks + ") of several kinds (" + kinds + "), sizes (" + widths.Count + ") and speeds (" + periods.Count + ")",
              p.Front.ShownRocks >= 4 && kinds >= 4 && widths.Count >= 4 && periods.Count >= 4);
        float rockMin = float.MaxValue;
        foreach (var r in DockBody.Rocks) rockMin = Mathf.Min(rockMin, r.width);
        Check("the asteroids are bigger than the old hazard-rock pebbles (>= 10% of the disc)", rockMin >= .1f);
        bool distinct = true;
        var names = new HashSet<string>();
        for (int w = 1; w < Stems.Length; w++)
        {
            p.Show(w, false);
            bool own = p.World == w && !p.IsSpace && p.CellName != null && p.CellName.StartsWith(Stems[w] + "_rot") && p.Front.Showing;
            distinct &= own;
            names.Add(Stems[w]);
        }
        Check("each of the six worlds shows its own planet", distinct && names.Count == 6);
        Check("only the shown planet's atlas is loaded", PlanetFlipbooks.LoadedCount == 1);
        // it turns: a calm full turn in PlanetFlipbooks.TurnSeconds, the next frame faded over this one
        p.Show(1, false);
        p.Tick(0f);
        string first = p.Front.Main.sprite.name;
        for (int i = 0; i < Mathf.RoundToInt(PlanetFlipbooks.TurnSeconds / 16f / Dt) + 5; i++) p.Tick(Dt);
        Check("the planet turns (a frame every " + (PlanetFlipbooks.TurnSeconds / 16f).ToString("F1") + " s), a full turn in about 20 s", p.Front.Main.sprite.name != first && Mathf.Abs(PlanetFlipbooks.TurnSeconds - 20f) < 2f);
        // on UNSCALED time with the menu's timeScale 0, and the second layer fades smoothly in (no pulse)
        float ts0 = Time.timeScale;
        try
        {
            Time.timeScale = 0f;
            p.Show(1, false);
            p.Tick(0f);
            string f0 = p.Front.Main.sprite.name;
            float prevA = p.Front.Pb.color.a; bool smooth = true, wrapped = false, rose = false;
            for (int i = 0; i < Mathf.RoundToInt(PlanetFlipbooks.TurnSeconds / 16f / Dt) + 5 && !wrapped; i++)
            {
                p.Tick(Dt);   // unscaled dt, as Update passes it
                if (p.Front.Main.sprite.name != f0) { wrapped = true; break; }
                float a = p.Front.Pb.color.a;
                if (a < prevA - .001f || a - prevA > .03f) smooth = false;
                if (a > prevA) rose = true;
                prevA = a;
            }
            Check("with timeScale 0 the planet still advances (unscaled clock): the next frame arrives and the fade up to it was smooth (alpha " + prevA.ToString("F2") + ")", wrapped && smooth);
        }
        finally { Time.timeScale = ts0; }
        Check("the sun's frame time is the slower " + DockSpaceArt.SunFrameSeconds.ToString("F2") + " s (was .125 s)", DockSpaceArt.SunFrameSeconds >= .375f);
        p.Dispose();
        Object.DestroyImmediate(p.gameObject);
        Check("a destroyed dock lets every sheet go", PlanetFlipbooks.LoadedCount == 0);
    }

    // ---- 3 ----
    static void Warp(Transform root)
    {
        var p = NewPlanet(root, 2);
        p.Show(3);
        Check("a change of selection starts a warp (old world out)", p.Warping && p.World == 3 && p.OutWorld == 2 && p.WarpsStarted == 1);
        p.Tick(Dt * 3f);
        Check("the old planet is shrinking and fading, the streaks have started", p.Back.ScaleMul < 1f && p.Back.Alpha < 1f && p.Streaks.enabled);
        float t = Dt * 3f;
        bool whiteFlash = false, monotoneOut = true;
        float lastOut = p.Back.ScaleMul;
        int frames = 3;
        while (p.Warping && frames < 200)
        {
            p.Tick(Dt);
            frames++;
            t += Dt;
            foreach (var r in p.GetComponentsInChildren<SpriteRenderer>(false))
            {
                if (r == p.Streaks) continue;
                // bodies are dimmed (never brighter than Dim): nothing flashes white over the dock
                if (r.enabled && (r.color.r > DockPlanet.Dim + .001f || r.color.b > DockPlanet.Dim + .001f)) whiteFlash = true;
            }
            if (p.Back.World >= 0) { monotoneOut &= p.Back.ScaleMul <= lastOut + .001f; lastOut = p.Back.ScaleMul; }
        }
        Check("the warp lasts 0.6-0.8 s (" + t.ToString("F2") + " s)", t >= .6f && t <= .8f);
        Check("the old planet only shrinks, nothing flashes white", monotoneOut && !whiteFlash);
        Check("the new planet landed at rest: full size, full alpha, streaks and the old one gone",
              !p.Warping && p.World == 3 && Mathf.Approximately(p.Front.ScaleMul, 1f) && Mathf.Approximately(p.FrontAlpha, 1f) && !p.Streaks.enabled && p.Back.World < 0 && p.CellName.StartsWith("ember_rot"));
        // the new planet comes in from small
        p.Show(4);
        float smallest = 1f;
        bool grew = true;
        float last = 0f;
        for (int i = 0; i < 100 && p.Warping; i++)
        {
            p.Tick(Dt);
            if (p.Front.Showing && p.Front.Alpha > .01f)
            {
                smallest = Mathf.Min(smallest, p.Front.ScaleMul);
                grew &= p.Front.ScaleMul >= last - .06f;   // (the file's curve overshoots to 1.04 and settles)
                last = p.Front.ScaleMul;
            }
        }
        Check("the new planet zips in from small (" + smallest.ToString("F2") + ") to its resting size", smallest < .3f && grew && Mathf.Approximately(p.Front.ScaleMul, 1f));
        // the same selection again: no warp
        int started = p.WarpsStarted;
        p.Show(4);
        Check("showing what is already shown does not warp", !p.Warping && p.WarpsStarted == started);
        p.Dispose();
        Object.DestroyImmediate(p.gameObject);
    }

    // ---- 3b ----
    static void Interrupt(Transform root)
    {
        var p = NewPlanet(root, 1);
        p.Show(2);
        p.Tick(Dt * 4f);
        p.Show(3);
        p.Tick(Dt * 2f);
        p.Show(4);
        p.Tick(Dt * 2f);
        p.Show(5);
        p.Tick(Dt * 2f);
        p.Show(6);
        Check("a fast tap through the worlds is one warp, aimed at the latest", p.Warping && p.World == 6 && p.WarpsStarted == 1);
        float t = 0f;
        while (p.Warping && t < 3f) { p.Tick(Dt); t += Dt; }
        Check("... which lands within a warp's length of the last tap (" + t.ToString("F2") + " s), not a queue of them", !p.Warping && t <= .8f && p.World == 6 && p.CellName.StartsWith("desert_rot"));
        // a tap at the very end of a warp skips the part nobody saw and still lands
        p.Show(1);
        for (int i = 0; i < 38; i++) p.Tick(Dt);
        p.Show(2);
        float t2 = 0f;
        while (p.Warping && t2 < 3f) { p.Tick(Dt); t2 += Dt; }
        Check("a tap near the end of a warp lands on the latest in under a warp's length (" + t2.ToString("F2") + " s)", !p.Warping && t2 <= .8f && p.World == 2 && p.CellName.StartsWith("verdant_rot"));
        // to Space and back mid-warp
        p.Show(0);
        p.Tick(Dt * 3f);
        p.Show(2);
        while (p.Warping && t2 < 6f) { p.Tick(Dt); t2 += Dt; }
        Check("Space -> planet -> back to the first, mid-warp, ends on the last choice", !p.Warping && p.World == 2 && !p.IsSpace);
        // snapping (a first show / animate false) cancels a warp at once
        p.Show(3);
        p.Tick(Dt * 5f);
        p.Show(1, false);
        Check("an unanimated show cancels a warp at once", !p.Warping && p.World == 1 && p.Back.World < 0 && !p.Streaks.enabled && Mathf.Approximately(p.FrontAlpha, 1f));
        p.Dispose();
        Object.DestroyImmediate(p.gameObject);
    }

    // ---- 4 ----
    static void Memory(Transform root)
    {
        PlanetFlipbooks.ResetForTests();
        var p = NewPlanet(root, 1);
        int most = PlanetFlipbooks.LoadedCount;
        Check("one atlas loaded for the one planet on show", most == 1 && PlanetFlipbooks.Refs(1) == 1);
        for (int w = 2; w <= 6; w++)
        {
            p.Show(w);
            for (int i = 0; i < 12; i++) { p.Tick(Dt); most = Mathf.Max(most, PlanetFlipbooks.LoadedCount); }
        }
        while (p.Warping) { p.Tick(Dt); most = Mathf.Max(most, PlanetFlipbooks.LoadedCount); }
        Check("through every world at most " + PlanetFlipbooks.MaxLoaded + " atlases were ever loaded at once (" + most + ")", most <= PlanetFlipbooks.MaxLoaded);
        Check("afterwards only the shown planet's is loaded", PlanetFlipbooks.LoadedCount == 1 && PlanetFlipbooks.Refs(6) == 1 && PlanetFlipbooks.Refs(1) == 0);
        // mid-warp both are resident
        p.Show(2);
        for (int i = 0; i < 10; i++) p.Tick(Dt);
        Check("mid-warp the old and the new are the two", PlanetFlipbooks.LoadedCount <= 2);
        p.Show(0);
        while (p.Warping) p.Tick(Dt);
        Check("warping to Space (no planet atlas) leaves none loaded", PlanetFlipbooks.LoadedCount == 0);
        p.Dispose();
        Object.DestroyImmediate(p.gameObject);
        Check("destroying the dock leaves none loaded", PlanetFlipbooks.LoadedCount == 0);
    }

    // ---- 5 ----
    static void Placement()
    {
        float d0 = DockPlanet.DiameterShare, o0 = DockPlanet.OffsetShare;
        try
        {
            var panes = new[] { new Rect(-3f, -5f, 6f, 10f), new Rect(-2.5f, -6f, 5f, 12f), new Rect(-1f, -2f, 8f, 6f) };
            foreach (var pane in panes)
            {
                Vector2 c; float d;
                float bottom = pane.yMin, top = pane.yMax;
                DockPlanet.Fit(pane, bottom, top, out c, out d);
                float share = d / pane.width, off = (c.x - pane.center.x) / pane.width;
                bool tall = (top - bottom) * DockPlanet.MaxHeightShare >= pane.width * DockPlanet.DiameterShare;
                Check("pane " + pane.width.ToString("F1") + "x" + pane.height.ToString("F1") + ": size " + (share * 100f).ToString("F0") + "% of the width, centre " + (off * 100f).ToString("F0") + "% right of the middle",
                      (!tall || (share >= .55f && share <= .65f)) && off > -.001f && off <= .251f &&
                      c.x - d * .5f >= pane.xMin - .001f && c.x + d * .5f <= pane.xMax + .1f * pane.width && c.y - d * .5f >= bottom - .001f && c.y + d * .5f <= top + .001f);
            }
            DockPlanet.DiameterShare = .5f;
            DockPlanet.OffsetShare = .05f;
            Vector2 c2; float dd;
            DockPlanet.Fit(panes[0], panes[0].yMin, panes[0].yMax, out c2, out dd);
            Check("the numbers are tunables: size follows DiameterShare, offset OffsetShare", Mathf.Abs(dd - 3f) < .001f && Mathf.Abs(c2.x - .3f) < .001f);
            DockPlanet.DiameterShare = d0;
            DockPlanet.OffsetShare = o0;
            Check("the offset is .25 of the width right of the middle", Mathf.Abs(DockPlanet.OffsetShare - .25f) < .001f);
            Vector2 c4; float d4;
            DockPlanet.Fit(panes[0], panes[0].yMin, panes[0].yMax, out c4, out d4);
            Check("... the sphere crops at the RIGHT edge only (" + ((c4.x + d4 * .5f - panes[0].xMax) / panes[0].width * 100f).ToString("F0") + "% of the width past it), whole on the left",
                  c4.x + d4 * .5f > panes[0].xMax + .001f && c4.x - d4 * .5f >= panes[0].xMin);
            // a short lot space caps the diameter (the whole sphere always shows)
            Vector2 c3; float d3;
            DockPlanet.Fit(new Rect(-3f, 0f, 6f, 4f), 0f, 4f, out c3, out d3);
            Check("a short space caps the diameter so the whole sphere still shows", d3 <= 4f * DockPlanet.MaxHeightShare + .001f && c3.y - d3 * .5f >= -.001f && c3.y + d3 * .5f <= 4.001f);
        }
        finally { DockPlanet.DiameterShare = d0; DockPlanet.OffsetShare = o0; }
    }

    // The tile layers the catalog's Space spec has (its other layers are pieces a director spawns).
    static int SpaceTileLayers()
    {
        int n = 0;
        foreach (var l in BackdropCatalog.For("Space").layers) if (l.kind != BackdropCatalog.Kind.Pieces) n++;
        return n;
    }

    // ---- 6 ----
    static void Backdrop(Transform root)
    {
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 3);
        MenuBackdropSelection.ResetForTests();
        var rng = new System.Random(11);
        for (int i = 0; i < 40 && MenuBackdropSelection.Current.world != "Frost" && MenuBackdropSelection.Current.world != "Verdant" && MenuBackdropSelection.Current.world != "Ember"; i++)
            MenuBackdropSelection.Roll(rng);
        var menuPick = MenuBackdropSelection.Current;
        Check("the menu rolled a world that is not Space (" + menuPick + ")", menuPick.Valid && menuPick.world != "Space");
        int rolls = MenuBackdropSelection.Rolls;
        var go = new GameObject("~MenuBackdropTest");
        go.transform.SetParent(root, false);
        var mb = go.AddComponent<MenuBackdrop>();
        Check("the menu backdrop builds the pick", mb.Build(menuPick) && mb.Set != null && mb.ShownPick.Same(menuPick) && !mb.DockActive);

        mb.EnterDock();
        Check("entering the dock shows a Space backdrop whatever the menu rolled", mb.DockActive && mb.ShownSet != null && mb.ShownSet.Spec.world == "Space" && mb.ShownPick.world == "Space");
        Check("... the real in-game tiles, dimmed like the menus, and no second sun", mb.ShownSet.Tiles.Count == SpaceTileLayers() && mb.ShownSet.Tiles.Count >= 1 && mb.ShownSet.Sun == null && mb.ShownSet.Root.gameObject.activeSelf);
        Check("... the menu's own backdrop is parked, not destroyed", mb.Set != null && !mb.Set.Root.gameObject.activeSelf && mb.Pick.Same(menuPick));
        Check("... and the roll is untouched", MenuBackdropSelection.Current.Same(menuPick) && MenuBackdropSelection.Rolls == rolls);
        mb.Step(Dt);
        mb.EnterDock();
        Check("entering twice builds nothing more", mb.DockSet != null && GameObjectsNamed("Backdrop_Space") == 1);

        mb.ExitDock();
        Check("leaving the dock shows the menu's pick again, not re-rolled", !mb.DockActive && mb.DockSet == null && mb.ShownSet == mb.Set && mb.Set.Root.gameObject.activeSelf &&
              mb.ShownPick.Same(menuPick) && MenuBackdropSelection.Current.Same(menuPick) && MenuBackdropSelection.Rolls == rolls);
        Check("... and the dock's Space set is gone", GameObjectsNamed("Backdrop_Space") == 0);
        // the same Space sky on reopening
        mb.EnterDock();
        var again = mb.DockPick;
        mb.ExitDock();
        mb.EnterDock();
        Check("reopening the dock shows the same Space variant", mb.DockPick.Same(again) && mb.DockPick.halfTurn == again.halfTurn);
        mb.ExitDock();
        Object.DestroyImmediate(go);

        // a menu that already shows Space: the dock keeps it
        MenuBackdropSelection.ResetForTests();
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 0);
        MenuBackdropSelection.Roll(new System.Random(3));
        var space = MenuBackdropSelection.Current;
        go = new GameObject("~MenuBackdropTest2");
        go.transform.SetParent(root, false);
        mb = go.AddComponent<MenuBackdrop>();
        mb.Build(space);
        mb.EnterDock();
        Check("a menu already on Space keeps its own Space for the dock", space.world == "Space" && mb.DockActive && mb.ShownSet == mb.Set && mb.ShownPick.Same(space));
        mb.ExitDock();
        Check("... and leaving restores it", !mb.DockActive && mb.ShownSet == mb.Set);
        Object.DestroyImmediate(go);
    }

    static int GameObjectsNamed(string name)
    {
        int n = 0;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (t.name == name) n++;
        return n;
    }

    // ---- 7 ----
    static void AfterBoss(Transform root)
    {
        // forced: Space's sky shows Frost's turning planet only
        SpaceDirector.ForcedWorldOverride = 1;
        var set = new BackdropSet("Space", root, 3.72f, 6.6f);
        Check("the Space set is complete", set.Complete && set.Director != null);
        var dir = (SpaceDirector)set.Director;
        bool allFrost = true;
        int seen = 0;
        for (int i = 0; i < 1600; i++)
        {
            set.Tick(.1f, 6f);
            if (i % 40 != 0) continue;
            foreach (var pool in dir.Bodies)
                foreach (var piece in pool.items)
                {
                    if (!piece.active || piece.kind != SpaceDirector.Planet || piece.parent != null) continue;
                    seen++;
                    bool frost = piece.frames != null && piece.frames.Length == 16 && piece.frames[0].name.StartsWith("frost_rot");
                    // (the planets already in the sky when the boss ended may be random: only NEW ones are checked)
                    if (i > 600 && !frost) allFrost = false;
                }
        }
        Check("after the Space boss the planets the sky spawns are Frost's rotation (" + seen + " seen)", seen > 0 && allFrost);
        Check("... Frost's sheet is held once for the sky", PlanetFlipbooks.Refs(1) == 1 && PlanetFlipbooks.LoadedCount == 1);
        set.Destroy();
        Check("... and let go with the sky", PlanetFlipbooks.LoadedCount == 0);

        // not forced: the usual random giants
        SpaceDirector.ForcedWorldOverride = -1;
        set = new BackdropSet("Space", root, 3.72f, 6.6f);
        dir = (SpaceDirector)set.Director;
        bool none = true;
        for (int i = 0; i < 400; i++)
        {
            set.Tick(.1f, 6f);
            foreach (var pool in dir.Bodies) foreach (var piece in pool.items) if (piece.active && piece.kind == SpaceDirector.Planet && piece.frames != null) none = false;
        }
        Check("before a boss the sky's planets are the usual giants", none && PlanetFlipbooks.LoadedCount == 0);
        set.Destroy();
        SpaceDirector.ForcedWorldOverride = -2;

        // Frost's planetfall draws the same planet, same size
        var def = PlanetfallCatalog.Frost;
        var art = PlanetfallArt.Load(def);
        Check("Frost's planetfall art loads", art.Complete);
        float classic = art.PlanetScale(1f);
        art.AttachRotation();
        Check("the approach planet is the 16-frame rotation (frame 1 first)", art.Rotating && art.Rotation.Length == 16 && art.Rotation[0].name.StartsWith("frost_rot") && PlanetFlipbooks.Refs(1) == 1);
        // the disc keeps its world size: a radius r world units whatever the art (the classic scale puts the measured disc on r)
        float r = 1.55f;
        float s = art.PlanetScale(r);
        Check("... drawn so its disc has the same radius as before (scale " + s.ToString("F3") + ")", Mathf.Abs(s * PlanetFlipbooks.DiscShare * .5f - r) < .001f && classic > 0f);
        art.Release();
        // EVERY planetfall world lands on its new rotation: the old static planet art is not loaded or drawn
        bool all = true, noOld = true, cap = true;
        foreach (var d in PlanetfallCatalog.All)
        {
            var a = PlanetfallArt.Load(d, true);
            a.AttachRotation();
            all &= PlanetFlipbooks.PlanetfallRotates[d.world] && a.Complete && a.Rotating && a.Rotation[0].name.StartsWith(Stems[d.world] + "_rot");
            noOld &= a.Planet == null && a.PlanetTex == null;
            cap &= PlanetFlipbooks.LoadedCount <= PlanetFlipbooks.MaxLoaded && PlanetFlipbooks.Refs(d.world) == 1;
            a.Release();
        }
        Check("every planetfall world's approach planet (frame 1 of the descent too) is its new rotation sheet", all);
        Check("... no old planetfall planet sprite or texture is loaded or referenced", noOld);
        Check("... only the destination's atlas is resident, inside the cap, and all go back", cap && PlanetFlipbooks.LoadedCount == 0);
        Check("releasing the art lets the sheet go", PlanetFlipbooks.LoadedCount == 0);
        // the preload asks for the next world's sheet when the gateway opens
        Check("the preload names the sheet it holds for the next world", PlanetFlipbooks.PathOf(1) == "Worlds/Dock/frost_rot_bare" && PlanetFlipbooks.PathOf(0) == null);
    }
}
