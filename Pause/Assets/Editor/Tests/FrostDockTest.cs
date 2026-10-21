using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Frost's elite hull LIGHTS and the four new launch SITES (Space did the same job: SpaceDockTest):
//
//   lights   each Frost elite's <key>_lights strip (an additive overlay, installed beside the hull strip) loads, is cell-aligned with
//            the hull strip (ten 384 px cells), is drawn by EliteShip over the hull wearing the SAME cell every frame (burn cycle, the
//            damaged cell 9, lift-off), still on while damaged / hiding, off parked engines-off and on with the pilot lights
//   sites    the ice-locked offshore rig, cryo silo, glacier cave hangar and icebreaker deck (FrostTuning.SiteNames 5..8, docks atlas)
//            each have their 6-cell launch strip, a pad point on the drawing, a LandingKind of their own, a ground rule on the right
//            ground (rig / icebreaker on water, silo on flats, cave on ridges), play idle / opening / open / hold / closing
//            through the launch (the ship's shared launch light is the tell: never a blink cell, flash, flare, halo or ring), launch the right elite
//   homes    each Frost elite launches from one of its own kinds when one is free (icebreaker / hangar, rig / crawlerbay, cave / rigbay,
//            silo / hatch, silo / padring), any free site when none is, and the director brings the new kinds into view
//   budget   the docks atlas is small, and the Frost texture budget holds (WorldBackdropTest measures every Frost PNG, docks included)
//
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites FrostDockTest
public static class FrostDockTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[FDK] PASS  " : "[FDK] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    const string AtlasPng = "Assets/Art/Backgrounds/Resources/Worlds/Frost/Backdrop3/docks.png";
    static Transform pilot;

    // key, launchFrom, its first (preferred) kind, the FrostTuning site index of it
    static readonly (string key, string launchFrom, LandingKind home, int site)[] Elites =
    {
        ("frost_elite_rimebreaker", "icebreaker,hangar", LandingKind.Icebreaker, 8),
        ("frost_elite_floe_harrower", "rig,crawlerbay", LandingKind.Rig, 5),
        ("frost_elite_cryo_siren", "cave,rigbay", LandingKind.Cave, 7),
        ("frost_elite_glacier_tender", "silo,hatch", LandingKind.Silo, 6),
        ("frost_elite_whiteout_sentinel", "silo,padring", LandingKind.Silo, 6),
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EliteCatalog.Reload();
            LightStrips();
            LightsFollowHull();
            LightsParkedAndLaunch();
            LightsDamagedAndHiding();
            DamagedLoops();
            LaunchOrder();
            SiteArt();
            Rules();
            SitesAndLaunch();
            HomesSynthetic();
            HomesBroughtIntoView();
            Budget();
        }
        finally
        {
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            LandingSites.Override = null;
            EliteDirector.WorldOverride = -1;
            RunScore.EndRun(RunScore.RunId);
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
            startMenu.youAreInTutorial = false;
        }
        Debug.Log("[FDK] failures: " + fails);
        return fails;
    }

    // ---- fixtures (as FrostEliteTest's) -------------------------------------------------

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
        EliteDirector.WorldOverride = -1;
        Random.InitState(2468);
    }

    static EliteShip InPlay(EliteDef def, Vector2 at)
    {
        var e = EliteShip.CreateInPlay(def, at);
        e.AttackCooldown = 999f;
        return e;
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

    static LandingSite Body(Vector2 at, LandingKind kind, int id, bool emerge = true)
    {
        var anchor = new GameObject("~Body" + id).transform;
        anchor.position = at;
        return new LandingSite { anchor = anchor, local = Vector3.zero, scale = .3f, order = -300, id = id, kind = kind, emerge = emerge };
    }

    // ---- lights ---------------------------------------------------------------------------

    static void LightStrips()
    {
        foreach (var x in Elites)
        {
            var d = EliteCatalog.Find(x.key);
            var hull = EliteArt.Frames(d);
            var lights = EliteArt.ExtraFrames(d, EliteArt.Extra.Lights);
            Check(x.key + ": the _lights strip is installed and loads (" + (lights != null ? lights.Length : 0) + " cells)",
                  lights != null && lights.Length > 0 && EliteArt.HasExtra(d, EliteArt.Extra.Lights));
            Check(x.key + ": its master is kept in new_docks_lights~ and the installed copy is byte for byte the master",
                  File.Exists("Assets/Art/Enemies/Elite/Frost/new_docks_lights~/" + x.key + "_lights.png") &&
                  File.ReadAllBytes("Assets/Art/Enemies/Elite/Frost/new_docks_lights~/" + x.key + "_lights.png").Length == File.ReadAllBytes("Assets/Art/Resources/Elites/Frost/" + x.key + "_lights.png").Length);
            if (lights == null || hull == null) continue;
            bool aligned = lights.Length == hull.Length && lights.Length == 15 && lights[0].texture.width == hull[0].texture.width && lights[0].texture.height == hull[0].texture.height;
            for (int i = 0; i < Mathf.Min(lights.Length, hull.Length); i++)
                aligned &= Mathf.Approximately(lights[i].rect.width, hull[i].rect.width) && Mathf.Approximately(lights[i].rect.height, hull[i].rect.height) &&
                           Mathf.Approximately(lights[i].textureRect.x, hull[i].textureRect.x) && Mathf.Approximately(lights[i].textureRect.y, hull[i].textureRect.y) &&
                           Mathf.Approximately(lights[i].pixelsPerUnit, hull[i].pixelsPerUnit) && lights[i].pivot == hull[i].pivot;
            Check(x.key + ": cell-aligned with the hull strip (" + lights.Length + " vs " + hull.Length + " cells of " + lights[0].rect.width + " px, " +
                  lights[0].texture.width + "x" + lights[0].texture.height + " vs " + hull[0].texture.width + "x" + hull[0].texture.height + ")", aligned);
            Check(x.key + ": its lights are bright small points over a clear strip (not a second hull)", Coverage(lights[3]) > 0f && Coverage(lights[3]) < .08f);
            Check(x.key + ": every cell carries lights, and they sit on or beside the hull of the SAME cell (cell " + 9 + " is the damaged hull's)", LightsOnHull(x.key, d.cellPixels));
        }
    }

    // Share of a cell's pixels with any alpha (read from the PNG: the texture is not readable).
    static float Coverage(Sprite s)
    {
        string path = "Assets/Art/Resources/Elites/Frost/" + s.texture.name + ".png";
        if (!File.Exists(path)) return 0f;
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(path));
        Rect r = s.textureRect;
        var px = tex.GetPixels32();
        int w = tex.width, lit = 0, n = 0;
        for (int y = (int)r.y; y < (int)(r.y + r.height); y++)
            for (int x = (int)r.x; x < (int)(r.x + r.width); x++) { n++; if (px[y * w + x].a > 0) lit++; }
        Object.DestroyImmediate(tex);
        return n > 0 ? lit / (float)n : 0f;
    }

    // Per cell: some lights, and most of them (>= 80%) over an opaque hull pixel of the same cell of the hull strip (the glow's
    // one or two hard pixels may spill past the hull's edge).
    static bool LightsOnHull(string key, int cell)
    {
        var hull = new Texture2D(2, 2);
        hull.LoadImage(File.ReadAllBytes("Assets/Art/Resources/Elites/Frost/" + key + ".png"));
        var lit = new Texture2D(2, 2);
        lit.LoadImage(File.ReadAllBytes("Assets/Art/Resources/Elites/Frost/" + key + "_lights.png"));
        bool ok = hull.width == lit.width && hull.height == lit.height;
        if (ok)
        {
            var h = hull.GetPixels32();
            var l = lit.GetPixels32();
            int cells = hull.width / cell;
            for (int c = 0; c < cells && ok; c++)
            {
                int any = 0, on = 0;
                for (int y = 0; y < cell; y++)
                    for (int x = c * cell; x < (c + 1) * cell; x++)
                    {
                        int i = y * hull.width + x;
                        if (l[i].a == 0) continue;
                        any++;
                        if (h[i].a >= 128) on++;
                    }
                ok &= any > 0 && on >= any * .8f;
            }
        }
        Object.DestroyImmediate(hull);
        Object.DestroyImmediate(lit);
        return ok;
    }

    static void LightsFollowHull()
    {
        foreach (var x in Elites)
        {
            Fresh(.05f);
            var d = EliteCatalog.Find(x.key);
            var hull = EliteArt.Frames(d);
            var lights = EliteArt.ExtraFrames(d, EliteArt.Extra.Lights);
            var e = InPlay(d, new Vector2(0f, 1f));
            Check(x.key + ": the ship draws its lights on a child layer of the hull with the additive shader",
                  e.HasLights && e.Lights.transform.parent == e.HullTransform && e.Lights.sharedMaterial != null && e.Lights.sharedMaterial.shader.name == "Pause/BackdropAdditive" &&
                  e.Lights.sortingOrder > e.Hull.sortingOrder);
            var cells = new HashSet<int>();
            bool same = true, sprites = true, transform = true, shown = true;
            string bad = "";
            for (int i = 0; i < 4 * 30; i++)
            {
                pilot.position = new Vector3(Mathf.Sin(i * .05f) * 1.6f, -2.5f, 0f);
                EliteSystem.Step(Dt);
                if (!Alive(e)) break;
                int cell = e.CurrentFrame;
                cells.Add(cell);
                same &= e.LightCell == cell;
                sprites &= e.Lights.sprite == lights[cell] && e.Hull.sprite == hull[cell];
                transform &= e.Lights.transform.lossyScale == e.HullTransform.lossyScale && e.Lights.transform.rotation == e.HullTransform.rotation &&
                             e.Lights.transform.position == e.HullTransform.position && e.Lights.flipX == e.Hull.flipX && e.Lights.flipY == e.Hull.flipY;
                shown &= e.Lights.enabled;
                if ((!same || !sprites || !transform || !shown) && bad.Length < 120) bad += "[cell " + cell + " lit " + e.LightCell + "] ";
            }
            Check(x.key + ": the lights wear the hull's cell every frame (" + cells.Count + " cells seen " + bad + ")", Alive(e) && cells.Count >= 3 && same && sprites);
            Check(x.key + ": ... with the hull's own transform, flip and bank, and always on in flight", transform && shown);
            e.TakeHit(EliteDamage.PlayerWeapon, e.Position + Vector2.left * .3f);
            Step(.2f);
            Check(x.key + ": ... still on its cell after a hit (" + e.LightCell + " = " + e.CurrentFrame + ")", !Alive(e) || e.LightCell == e.CurrentFrame);
            EliteSystem.Clear();
        }
    }

    static void LightsParkedAndLaunch()
    {
        foreach (var x in Elites)
        {
            Fresh(.05f);
            var d = EliteCatalog.Find(x.key);
            var lights = EliteArt.ExtraFrames(d, EliteArt.Extra.Lights);
            var site = Body(new Vector2(.8f, 3f), LandingKind.Station, 1, false);
            var e = EliteShip.Create(d, site, 2.5f, new Vector2(-1.4f, 1.4f));
            bool offEarly = true, sawPilot = false, offBlink = false, lateOnWithEngine = true;
            int guard = 0;
            while (e.State == EliteState.Parked && guard++ < 400)
            {
                EliteSystem.Step(Dt);
                if (e.State != EliteState.Parked) break;
                bool tell = e.StateTime >= e.ParkSeconds - EliteShip.EngineTellSeconds;
                if (!tell && e.StateTime > Dt * 2) offEarly &= e.LightCell == -1 && !e.Lights.enabled;
                if (tell)
                {
                    if (e.EngineLightsOn) { sawPilot |= e.LightCell == d.cells.parkedIdle && e.Lights.enabled && e.Lights.sprite == lights[d.cells.parkedIdle]; lateOnWithEngine &= e.LightCell == d.cells.parkedIdle; }
                    else offBlink |= e.LightCell == -1;
                }
            }
            Check(x.key + ": parked with its engines off the lights are off", offEarly);
            Check(x.key + ": the pilot lights come on in the launch tell with the pilot-light cell's lights (" + d.cells.parkedIdle + ")", sawPilot && lateOnWithEngine);
            Check(x.key + ": ... and blink off again with them", offBlink);
            bool lift = true;
            int liftFrames = 0;
            guard = 0;
            while (e.State == EliteState.LiftOff && guard++ < 300)
            {
                EliteSystem.Step(Dt);
                if (e.State != EliteState.LiftOff) break;
                liftFrames++;
                lift &= e.LightCell == e.CurrentFrame && e.Lights.enabled;
            }
            Check(x.key + ": through the lift-off the lights wear the hull's cell (" + liftFrames + " frames)", liftFrames > 5 && lift);
            EliteSystem.Clear();
        }
    }

    static void LightsDamagedAndHiding()
    {
        foreach (var x in Elites)
        {
            Fresh(.05f);
            var d = EliteCatalog.Find(x.key);
            var lights = EliteArt.ExtraFrames(d, EliteArt.Extra.Lights);
            var e = InPlay(d, new Vector2(0f, 1f));
            Step(.5f);
            // (the armoured ones take more than one hit to show damage)
            for (int h = 0; h < 6 && Alive(e) && !e.Damaged; h++)
            {
                e.TakeHit(EliteDamage.PlayerWeapon, e.Position + Vector2.left * .3f);
                Step(.35f);
            }
            if (!Alive(e) || !e.Damaged) { Check(x.key + ": shows damage after hits", false); EliteSystem.Clear(); continue; }
            var alphas = new HashSet<float>();
            bool follows = true, on = true, sawDamaged = false;
            int n = 0;
            float minA = 9f, maxA = 0f;
            Step(3f, () =>
            {
                n++;
                if (n < 3 || !Alive(e)) return;
                follows &= e.LightCell == e.CurrentFrame && e.Lights.sprite == lights[e.CurrentFrame];
                sawDamaged |= System.Array.IndexOf(d.cells.damagedLoop, e.CurrentFrame) >= 0 && e.Lights.sprite == lights[e.CurrentFrame];
                on &= e.Lights.enabled;
                float a = e.Lights.color.a;
                alphas.Add(a);
                minA = Mathf.Min(minA, a); maxA = Mathf.Max(maxA, a);
            });
            Check(x.key + ": damaged" + (e.Hidden ? " and hiding" : "") + ": the lights follow the hull cell, wear the damaged cell (" + d.cells.damaged + ") and stay on",
                  follows && sawDamaged && on);
            Check(x.key + ": ... and keep animating over the damaged hull: they stutter (" + alphas.Count + " brightness steps " + minA.ToString("0.00") + ".." + maxA.ToString("0.00") + ")",
                  alphas.Count >= 3 && minA > .1f && maxA >= .99f);
            EliteSystem.Clear();
        }
    }


    // ---- the damaged burn loop (EliteCells.damagedLoop; companion _damaged strips) ----------------------------------------

    static void DamagedLoops()
    {
        foreach (var x in Elites)
        {
            var d = EliteCatalog.Find(x.key);
            var c = d.cells;
            var hull = EliteArt.Frames(d);
            var lights = EliteArt.ExtraFrames(d, EliteArt.Extra.Lights);
            Check(x.key + ": a 6-drawing damaged loop starting at the damaged cell (" + c.damaged + ") and continuing in the companion strip (cells 10..14)",
                  c.HasDamagedLoop && c.damagedLoop.Length == 6 && c.damagedLoop[0] == c.damaged && c.damagedLoop[5] == 14 && c.Count == 15);
            Check(x.key + ": hull and lights strips both load all 15 cells (" + (hull != null ? hull.Length : 0) + " / " + (lights != null ? lights.Length : 0) + ")",
                  hull != null && lights != null && hull.Length == 15 && lights.Length == 15 && hull[14] != null && lights[14] != null &&
                  Mathf.Approximately(hull[14].rect.width, d.cellPixels) && Mathf.Approximately(lights[14].rect.width, d.cellPixels));
            // the loop plays while damaged: several distinct loop cells, no procedural bob over it
            Fresh(.05f);
            var e = InPlay(d, new Vector2(0f, 1f));
            Step(.5f);
            for (int h = 0; h < 6 && Alive(e) && !e.Damaged; h++) { e.TakeHit(EliteDamage.PlayerWeapon, e.Position + Vector2.left * .3f); Step(.35f); }
            if (!Alive(e) || !e.Damaged) { Check(x.key + ": shows damage after hits", false); EliteSystem.Clear(); continue; }
            var seen = new HashSet<int>();
            Step(3f, () => { if (Alive(e)) seen.Add(e.CurrentFrame); });
            bool inLoop = true;
            foreach (int f in seen) inLoop &= System.Array.IndexOf(c.damagedLoop, f) >= 0;
            Check(x.key + ": damaged, it plays its burn loop (" + seen.Count + " distinct drawings, all in the loop)", seen.Count >= 4 && inLoop && e.DamagedLoops);
            EliteSystem.Clear();
        }
    }

    // ---- the launch strips are in launch order -----------------------------------------------------------------------------

    static void LaunchOrder()
    {
        LoadAtlasPixels();
        // played idle -> open hold -> closing: the closing cell (half shut) is closer to the idle cell than the fully open hold is
        foreach (var tag in new[] { "rig", "silo", "cave", "icebreaker" })
            Check(tag + ": the strip is in launch order (idle vs hold " + Diff(tag, 0, 4) + " px, idle vs closing " + Diff(tag, 0, 5) + " px)",
                  atlasPx != null && Diff(tag, 0, 4) > 500 && Diff(tag, 0, 5) < Diff(tag, 0, 4));
    }

    static int Diff(string tag, int a, int b)
    {
        var ra = AtlasRect(tag + "_0" + a); var rb = AtlasRect(tag + "_0" + b);
        int n = 0, w = atlasTex.width;
        for (int y = 0; y < (int)ra.height; y++)
            for (int x = 0; x < (int)ra.width; x++)
            {
                var p = atlasPx[((int)ra.y + y) * w + (int)ra.x + x];
                var q = atlasPx[((int)rb.y + y) * w + (int)rb.x + x];
                if (Mathf.Abs(p.r - q.r) + Mathf.Abs(p.g - q.g) + Mathf.Abs(p.b - q.b) + Mathf.Abs(p.a - q.a) > 40) n++;
            }
        return n;
    }

    [System.Serializable] class AtlasSprite { public string n; public int x, y, w, h; }
    [System.Serializable] class AtlasManifest { public AtlasSprite[] sprites; }

    // a cell's pixel rect in the atlas (the manifest's y runs from the bottom, like GetPixels32 rows)
    static RectInt AtlasRect(string name)
    {
        var m = JsonUtility.FromJson<AtlasManifest>(File.ReadAllText("Assets/Art/Backgrounds/Resources/Worlds/Frost/Backdrop3/docks.json"));
        foreach (var s in m.sprites) if (s.n == name) return new RectInt(s.x, s.y, s.w, s.h);
        return default;
    }

    // ---- the sites --------------------------------------------------------------------------

    static Texture2D atlasTex;
    static Color32[] atlasPx;

    static void LoadAtlasPixels()
    {
        if (atlasPx != null) return;
        atlasTex = new Texture2D(2, 2);
        atlasTex.LoadImage(File.ReadAllBytes(AtlasPng));
        atlasPx = atlasTex.GetPixels32();
    }

    static FrostDirector NewFrost(out WorldBackdrop wb)
    {
        FrostBackdropSelection.Reset();
        FrostBackdropSelection.Seed(11);
        FrostBackdropSelection.Force = 1;
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        Time.timeScale = 1f;
        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = 1080f / 2400f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 1080, 2400);
        wb = WorldBackdrop.Create("Frost");
        wb.Show("Frost", false);
        return wb.Current != null ? wb.Current.Director as FrostDirector : null;
    }

    static void SiteArt()
    {
        Fresh(.25f);
        WorldBackdrop wb;
        var fd = NewFrost(out wb);
        Check("Frost builds for the site check", fd != null);
        if (fd == null) return;
        LoadAtlasPixels();
        Check("the docks atlas is the PNG on disk (" + atlasTex.width + "x" + atlasTex.height + ")", atlasTex.width > 1000 && atlasTex.height > 800);
        var atlas = wb.Current.Atlas(FrostTuning.DocksAtlas);
        var kinds = new HashSet<LandingKind>();
        for (int k = FrostTuning.FirstLaunchSite; k < FrostTuning.SiteNames.Length; k++)
        {
            string tag = FrostTuning.SiteNames[k];
            var cells = atlas.Frames(tag);
            Check(tag + ": its " + FrostTuning.LaunchCells + "-cell launch strip loads (" + cells.Length + ")", cells.Length == FrostTuning.LaunchCells);
            if (cells.Length != FrostTuning.LaunchCells) continue;
            bool equal = true;
            for (int i = 1; i < cells.Length; i++) equal &= Mathf.Approximately(cells[i].rect.width, cells[0].rect.width) && Mathf.Approximately(cells[i].rect.height, cells[0].rect.height);
            Check(tag + ": every cell the same size (" + cells[0].rect.width + "x" + cells[0].rect.height + ")", equal);
            Check(tag + ": idle and open are the cells the old two-drawing API names (" + FrostTuning.SiteClosed[k] + ", " + FrostTuning.SiteOpen[k] + ")",
                  cells[FrostTuning.LaunchIdle].name == FrostTuning.SiteClosed[k] && cells[FrostTuning.LaunchOpen].name == FrostTuning.SiteOpen[k]);
            Vector2 pad = FrostTuning.SitePads[k];
            Check(tag + ": its pad point (" + pad.x.ToString("0.000") + ", " + pad.y.ToString("0.000") + ") is well inside the cell", Mathf.Abs(pad.x) < .38f && Mathf.Abs(pad.y) < .38f);
            // the pad is on the drawing: opaque in every cell, and opaque all round it
            bool onDrawing = true;
            string bad = "";
            for (int c = 0; c < cells.Length; c++)
            {
                Rect r = cells[c].textureRect;
                int px = Mathf.RoundToInt(r.x + (pad.x + .5f) * r.width), py = Mathf.RoundToInt(r.y + (pad.y + .5f) * r.height);
                int opaque = 0, ring = 0;
                for (int dy = -4; dy <= 4; dy++)
                    for (int dx = -4; dx <= 4; dx++) { ring++; opaque += atlasPx[(py + dy) * atlasTex.width + px + dx].a >= 200 ? 1 : 0; }
                bool ok = atlasPx[py * atlasTex.width + px].a >= 200 && opaque >= ring * .9f;
                onDrawing &= ok;
                if (!ok) bad += c + " ";
            }
            Check(tag + ": the pad pixel and its neighbourhood are opaque drawing in all six cells " + bad, onDrawing);
            kinds.Add(FrostTuning.SiteKinds[k]);
            Check(tag + ": a LandingKind of its own that its launchFrom word reaches (" + FrostTuning.SiteKinds[k] + ")",
                  LandingSite.KindOf(tag) == FrostTuning.SiteKinds[k] && LandingSite.Accepts(tag, FrostTuning.SiteKinds[k]) && !LandingSite.Accepts(tag, LandingKind.Hangar) &&
                  !LandingSite.Accepts(tag, LandingKind.Station));
            // no flash: no cell is brighter than the idle one round the pad (a flare / halo / ring would be)
            Check(tag + ": no flash, flare or halo in any cell (peak brightness " + PeakBright(cells).ToString("0.00") + " <= idle's " + PeakBright(new[] { cells[0] }).ToString("0.00") + " + .02)",
                  PeakBright(cells) <= PeakBright(new[] { cells[0] }) + .02f);
        }
        Check("four distinct new site kinds, the old five still there (" + FrostTuning.SiteKinds.Length + " in all)", kinds.Count == 4 && FrostTuning.SiteKinds.Length == 9 &&
              FrostTuning.SiteNames.Length == 9 && FrostTuning.SiteClosed.Length == 9 && FrostTuning.SiteOpen.Length == 9 && FrostTuning.SiteWeights.Length == 9 &&
              FrostTuning.SitePads.Length == 9 && FrostTuning.SiteWidthK.Length == 9);
        Object.DestroyImmediate(wb.gameObject);
        Object.DestroyImmediate(atlasTex);
        atlasTex = null; atlasPx = null;
    }

    // The brightest opaque pixel (luminance 0..1) over the given cells' interiors, ignoring the pale ice rim: cells' very brightest.
    static float PeakBright(Sprite[] cells)
    {
        float peak = 0f;
        foreach (var s in cells)
        {
            Rect r = s.textureRect;
            for (int y = (int)r.y; y < (int)(r.y + r.height); y++)
                for (int x = (int)r.x; x < (int)(r.x + r.width); x++)
                {
                    var c = atlasPx[y * atlasTex.width + x];
                    if (c.a < 200) continue;
                    float l = (c.r * .299f + c.g * .587f + c.b * .114f) / 255f;
                    if (l > peak) peak = l;
                }
        }
        return peak;
    }

    static void Rules()
    {
        // which ground each new site stands on (GroundPlanner classes: W water / leads, O snow flats, C sheet / ridge)
        var rig = FrostTuning.Rule("rig_00");
        var ice = FrostTuning.Rule("icebreaker_00");
        var silo = FrostTuning.Rule("silo_00");
        var cave = FrostTuning.Rule("cave_00");
        Check("the offshore rig stands in the water (leads)", rig.on == GroundClass.Water);
        Check("the icebreaker deck floats on open water", ice.on == GroundClass.Water);
        Check("the cryo silo stands on snow flats", silo.on == GroundClass.Open);
        Check("the glacier cave hangar is cut into the ridge / pack ice", cave.on == GroundClass.Canopy);
        Check("on the glacier (no sea) they all take the plateau, never over a crevasse",
              FrostTuning.Rule("rig_00", 4).on == FrostTuning.Rule("silo_00", 4).on && FrostTuning.Rule("cave_00", 4).on == FrostTuning.Rule("icebreaker_00", 4).on &&
              (FrostTuning.Rule("rig_00", 4).on & GroundClass.Water) == 0);
        Check("the rig and icebreaker never come to the glacier (no sea there); everything else does",
              !FrostTuning.SiteFits(5, 4) && !FrostTuning.SiteFits(8, 4) && FrostTuning.SiteFits(6, 4) && FrostTuning.SiteFits(7, 4) && FrostTuning.SiteFits(0, 4) &&
              FrostTuning.SiteFits(5, 1) && FrostTuning.SiteFits(8, 1));
        Check("the old sites keep their rules (rig bay offshore, hangar an island)", FrostTuning.Rule("rigbay_closed").on == GroundClass.Water &&
              (FrostTuning.Rule("hangar_closed").on & GroundClass.Open) != 0);
    }

    static int CellOf(Sprite s) { return s == null ? -1 : int.Parse(s.name.Substring(s.name.Length - 2)); }

    static bool PlaceSite(WorldBackdrop wb, FrostDirector fd, int kind)
    {
        for (int i = 0; i < 60; i++)
        {
            if (fd.SpawnSite(2.2f + (i % 5) * .5f, kind)) return true;
            wb.Step(.5f);
        }
        return false;
    }

    static bool WaitForSite(WorldBackdrop wb, FrostDirector fd, LandingKind kind, out LandingSite site, out BackdropPiece piece, float maxSeconds = 40f)
    {
        var sites = new List<LandingSite>();
        site = default;
        piece = null;
        for (float t = 0f; t < maxSeconds; t += 1f / 15f)
        {
            LandingSites.Collect(sites);
            foreach (var s in sites)
                if (s.kind == kind)
                {
                    site = s;
                    foreach (var p in fd.Sites.items) if (p.active && p.root == s.anchor) piece = p;
                    if (piece != null) return true;
                }
            wb.Step(1f / 15f);
        }
        return false;
    }

    static void SitesAndLaunch()
    {
        var seen = new HashSet<int>();
        foreach (var x in Elites)
        {
            if (!seen.Add(x.site) && x.site != 6) continue;
            if (x.key == "frost_elite_whiteout_sentinel") continue;      // (the silo is the Tender's; the Sentinel shares it, see HomesSynthetic)
            Fresh(.25f);
            WorldBackdrop wb;
            var fd = NewFrost(out wb);
            var d = EliteCatalog.Find(x.key);
            string tag = FrostTuning.SiteNames[x.site];
            string k = tag + " site: ";
            for (int i = 0; i < 30 * 8; i++) wb.Step(Dt);        // (the opening ground is in place)
            bool placed = PlaceSite(wb, fd, x.site);
            Check(k + "a " + tag + " is spawned on the ground (" + placed + ")", placed);
            LandingSite site;
            BackdropPiece piece;
            bool offered = placed && WaitForSite(wb, fd, x.home, out site, out piece);
            if (!offered) { Check(k + "it offers a " + x.home + " site", false); Object.DestroyImmediate(wb.gameObject); continue; }
            WaitForSite(wb, fd, x.home, out site, out piece);
            Check(k + "it offers a " + x.home + " site, an emerge site inside the drawing, behind the gameplay, small",
                  site.emerge && site.Valid && site.order < EliteShip.PlayOrder - 2 && site.scale > .05f && site.scale < .5f && site.kind == x.home && piece.kind == x.site);
            Bounds b = piece.sr.sprite.bounds;
            Vector2 pad = FrostTuning.SitePads[x.site];
            Vector3 want = piece.root.TransformPoint(new Vector3(b.center.x + pad.x * b.size.x, b.center.y + pad.y * b.size.y, 0f));
            Check(k + "the site is the pad point (" + Vector3.Distance(want, site.Position).ToString("0.000") + " u off)", Vector3.Distance(want, site.Position) < .005f);
            float width = b.size.x * piece.root.lossyScale.x;
            Check(k + "drawn " + width.ToString("0.00") + " u wide (the usual " + FrostTuning.SiteMin + ".." + FrostTuning.SiteMax + " x " + FrostTuning.SiteWidthK[x.site] + "), behind the play layer",
                  width >= FrostTuning.SiteMin * FrostTuning.SiteWidthK[x.site] - .02f && width <= FrostTuning.SiteMax * FrostTuning.SiteWidthK[x.site] + .02f &&
                  width >= FrostTuning.SiteMinSize && piece.sr.sortingOrder < EliteShip.PlayOrder - 2);
            Check(k + "idle (closed) until a launch, its old lamp row off (a strip site draws its own lamp)", CellOf(piece.sr.sprite) == FrostTuning.LaunchIdle &&
                  (piece.body.Find("lamps") == null || !piece.body.Find("lamps").GetComponent<SpriteRenderer>().enabled));

            // the launch: its elite parks in it; the strip plays through the tell and the lift-off
            var e = EliteShip.Create(d, site, 2.4f, EliteDirector.JoinPoint(d, EliteBrains.Create(d.brain).JoinFrom, site.Position));
            var order = new List<int>();
            var phaseOfCell = new Dictionary<int, (float lo, float hi)>();
            int lastCell = -1;
            bool rides = true;
            float afterJoin = 0f;
            for (int i = 0; i < 30 * 16; i++)
            {
                wb.Step(Dt);
                EliteSystem.Step(Dt);
                int cell = CellOf(piece.sr.sprite);
                float ph = Alive(e) ? e.LaunchPhase : 2f;
                if (e.State == EliteState.Parked) rides &= Vector2.Distance(e.transform.position, site.Position) < .02f || !site.Valid;
                if (cell != lastCell) { order.Add(cell); lastCell = cell; }
                if (!phaseOfCell.ContainsKey(cell)) phaseOfCell[cell] = (ph, ph);
                else { var v = phaseOfCell[cell]; phaseOfCell[cell] = (Mathf.Min(v.lo, ph), Mathf.Max(v.hi, ph)); }
                if (e.State != EliteState.Parked && e.State != EliteState.LiftOff) { afterJoin += Dt; if (afterJoin > FrostTuning.SiteCloseDelay + 1f) break; }
                if (!piece.active) break;
            }
            bool idleFirst = order.Count > 0 && order[0] == FrostTuning.LaunchIdle;
            bool closed = order.Count > 0 && order[order.Count - 1] == FrostTuning.LaunchIdle && order.Contains(FrostTuning.LaunchClosing);
            int lastIdx = -1;
            bool inOrder = true;
            foreach (int c in new[] { FrostTuning.LaunchOpening, FrostTuning.LaunchOpen, FrostTuning.LaunchHold, FrostTuning.LaunchClosing })
            {
                int idx = order.IndexOf(c);
                if (idx < 0 || idx < lastIdx) { inOrder = false; break; }
                lastIdx = idx;
            }
            Check(k + "its strip plays idle, opening, open, hold, closing, idle, never the blink cell (" + string.Join(">", order) + ")", idleFirst && inOrder && closed && !order.Contains(FrostTuning.LaunchBlink));
            Check(k + "the doors open in the launch tell, open as the elite emerges (opening " + Range(phaseOfCell, FrostTuning.LaunchOpening) + ", open " + Range(phaseOfCell, FrostTuning.LaunchOpen) + ")",
                  phaseOfCell.ContainsKey(FrostTuning.LaunchIdle) &&
                  phaseOfCell.ContainsKey(FrostTuning.LaunchOpening) && phaseOfCell[FrostTuning.LaunchOpening].lo < 1f &&
                  phaseOfCell.ContainsKey(FrostTuning.LaunchOpen) && phaseOfCell[FrostTuning.LaunchOpen].lo >= 1f);
            Check(k + "the elite rode the site and came out into play (" + e.State + ")", rides && e.InPlay);
            Object.DestroyImmediate(wb.gameObject);
            EliteSystem.Clear();
        }
        Check("the launch cells by phase (idle through the tell's first share, opening, open, hold: no blink cell)",
              FrostTuning.LaunchCellAt(.05f) == FrostTuning.LaunchIdle && FrostTuning.LaunchCellAt(.6f) == FrostTuning.LaunchOpening &&
              FrostTuning.LaunchCellAt(1.05f) == FrostTuning.LaunchOpen && FrostTuning.LaunchCellAt(1.5f) == FrostTuning.LaunchHold);
        bool never = true;
        for (float ph = .01f; ph < 2f; ph += .01f) never &= FrostTuning.LaunchCellAt(ph) != FrostTuning.LaunchBlink;
        Check("... and the strip's blink cell is never picked by any phase", never);
        // the shared launch light: the elite parked at a strip site blinks its own small light through the tell
        Fresh(.05f);
        var def = EliteCatalog.Find(Elites[0].key);
        var e2 = EliteShip.Create(def, Body(new Vector2(.8f, 3f), LandingKind.Icebreaker, 77), 2.5f, new Vector2(-1.4f, 1.4f));
        bool lit = false;
        for (int i = 0; i < 400 && e2.State == EliteState.Parked; i++) { EliteSystem.Step(Dt); lit |= e2.LaunchLightOn; }
        Check("a parked elite blinks the shared launch light (EliteShip.LaunchLightOn) in its tell", lit);
        EliteSystem.Clear();
    }

    static string Range(Dictionary<int, (float lo, float hi)> m, int cell)
    {
        return m.ContainsKey(cell) ? m[cell].lo.ToString("0.00") + ".." + m[cell].hi.ToString("0.00") : "-";
    }

    // ---- homes -------------------------------------------------------------------------------------

    static void HomesSynthetic()
    {
        // one pad of every Frost kind: each elite takes one of its own two kinds
        Fresh(.05f);
        var all = FrostTuning.SiteKinds;
        var pads = new List<LandingSite>();
        for (int i = 0; i < all.Length; i++) pads.Add(Body(new Vector2(-2f + i * .5f, 3f + i * .1f), all[i], i + 1));
        LandingSites.Override = list => list.AddRange(pads);
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        var perKey = new Dictionary<string, int>();
        bool own = true;
        string bad = "";
        for (int i = 0; i < 60; i++)
        {
            EliteSystem.Clear();
            if (dir.SpawnGroup(1, 1) != 1) { own = false; continue; }
            var e = EliteShip.Live[0];
            perKey[e.Def.key] = perKey.ContainsKey(e.Def.key) ? perKey[e.Def.key] + 1 : 1;
            bool pref = LandingSite.Accepts(e.Def.launchFrom, e.Site.kind);
            if (!pref && bad.Length < 120) bad += e.Def.key + " from " + e.Site.kind + "; ";
            own &= pref;
        }
        Check("each Frost elite launches from one of its own kinds when every kind is free (" + perKey.Count + " elites, " + bad + ")", own && perKey.Count == 5);
        foreach (var x in Elites)
            Check(x.key + ": launched at least once, from " + x.launchFrom, perKey.ContainsKey(x.key) && perKey[x.key] > 0);

        // among the bodies it does not launch from plus one of its kinds: that one; then none of its kinds in view: any free site
        foreach (var x in Elites)
        {
            Fresh(.05f);
            var d = EliteCatalog.Find(x.key);
            Check(x.key + ": launchFrom " + d.launchFrom + " (a new kind first, then an old one)",
                  d.launchFrom == x.launchFrom && LandingSite.KindOf(d.launchFrom) == x.home);
            foreach (string part in d.launchFrom.Split(','))
                Check(x.key + ": '" + part + "' is a Frost site kind", System.Array.IndexOf(FrostTuning.SiteKinds, LandingSite.KindOf(part).Value) >= 0);
            foreach (var wantKind in new[] { x.home, LandingSite.KindOf(d.launchFrom.Split(',')[1]).Value })
            {
                var only = new List<LandingSite>();
                for (int i = 0; i < all.Length; i++)
                    if (!LandingSite.Accepts(d.launchFrom, all[i]) || all[i] == wantKind) only.Add(Body(new Vector2(-2f + i * .5f, 3f + i * .1f), all[i], i + 1));
                LandingSites.Override = list => list.AddRange(only);
                dir = new GameObject("~Dir").AddComponent<EliteDirector>();
                int mine = 0;
                bool home = true;
                for (int i = 0; i < 60 && mine < 3; i++)
                {
                    EliteSystem.Clear();
                    if (dir.SpawnGroup(1, 1) != 1) continue;
                    var live = EliteShip.Live[0];
                    if (live.Def.key != d.key) continue;
                    mine++;
                    home &= live.Site.kind == wantKind;
                }
                Check(x.key + ": among " + only.Count + " sites with only " + wantKind + " of its kinds it takes it (" + mine + " launches)", mine >= 1 && home);
                Object.DestroyImmediate(dir.gameObject);
            }
            var rest = new List<LandingSite>();
            for (int i = 0; i < all.Length; i++) if (!LandingSite.Accepts(d.launchFrom, all[i])) rest.Add(Body(new Vector2(-2f + i * .5f, 3f + i * .1f), all[i], i + 1));
            LandingSites.Override = list => list.AddRange(rest);
            dir = new GameObject("~Dir").AddComponent<EliteDirector>();
            bool any = true, saw = false;
            for (int i = 0; i < 60 && !saw; i++)
            {
                EliteSystem.Clear();
                any &= dir.SpawnGroup(1, 1) == 1;
                saw |= EliteShip.Live.Count > 0 && EliteShip.Live[0].Def == d;
            }
            Check(x.key + ": with none of its kinds in view it takes any free site (" + rest.Count + " left, saw it " + saw + ")", any && saw);
            Object.DestroyImmediate(dir.gameObject);
        }
        LandingSites.Override = null;
        EliteSystem.Clear();
    }

    static void HomesBroughtIntoView()
    {
        // (a fresh backdrop for each: the site pool holds three, and a launch site stays in the pool until it scrolls off)
        foreach (var x in Elites)
        {
            Fresh(.25f);
            WorldBackdrop wb;
            var fd = NewFrost(out wb);
            if (fd == null) { Check("Frost builds for the home check", false); return; }
            for (int i = 0; i < 30 * 8; i++) wb.Step(Dt);
            int second = System.Array.IndexOf(FrostTuning.SiteKinds, LandingSite.KindOf(x.launchFrom.Split(',')[1]).Value);
            bool ok = false;
            for (int i = 0; i < 60 && !ok; i++) { ok = fd.BringHome(x.launchFrom); if (!ok) wb.Step(.5f); }
            Check(x.key + ": BringHome(" + x.launchFrom + ") brings one of its kinds into view or on its way (" + CountKind(fd, x.site) + " " + FrostTuning.SiteNames[x.site] + ", " + CountKind(fd, second) + " other)",
                  ok && CountKind(fd, x.site) + CountKind(fd, second) >= 1);
            int spawned = fd.SitesSpawned;
            Check("... asked again it spawns no second one", fd.BringHome(x.launchFrom) && fd.SitesSpawned == spawned);
            Object.DestroyImmediate(wb.gameObject);
            EliteSystem.Clear();
        }
        Fresh(.25f);
        WorldBackdrop wb2;
        var fd2 = NewFrost(out wb2);
        if (fd2 == null) return;
        for (int i = 0; i < 30 * 8; i++) wb2.Step(Dt);
        Check("... LandingSites.BringHome reaches the new kinds through the world backdrop", LandingSites.BringHome("icebreaker,hangar"));
        Check("... a launchFrom of no Frost site is not conjured", !fd2.BringHome("gate") && !fd2.BringHome(""));
        Object.DestroyImmediate(wb2.gameObject);
        EliteSystem.Clear();
    }

    static int CountKind(FrostDirector fd, int kind)
    {
        int n = 0;
        foreach (var p in fd.Sites.items) if (p.active && p.kind == kind) n++;
        return n;
    }

    // ---- texture budget ------------------------------------------------------------------------------

    static void Budget()
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPng);
        var imp = AssetImporter.GetAtPath(AtlasPng) as TextureImporter;
        Check("docks.png imports compressed, no mipmaps, not clamped below its size",
              tex != null && imp != null && !imp.mipmapEnabled && imp.textureCompression != TextureImporterCompression.Uncompressed && imp.maxTextureSize >= Mathf.Max(tex.width, tex.height));
        long astc6 = tex != null ? ((tex.width + 5) / 6) * ((tex.height + 5) / 6) * 16L : 0;
        Check("the docks atlas is " + (astc6 / 1024) + " KB on a phone (ASTC 6x6): under 1 MB of the Frost budget", tex != null && astc6 > 0 && astc6 <= 900L * 1024);
        long full = 4L * 6 * 768 * 768 * 4;
        Check("... against ~" + (full / 1024 / 1024) + " MB uncompressed for the full-size masters (shrunk 3x)", tex != null && tex.width <= 1600 && astc6 < full / 20);
        Check("the Frost texture budget itself is WorldBackdropTest's (FrostTextureBudgetBytes " + (WorldBackdropTest.FrostTextureBudgetBytes / 1024) + " KB, measured over every Frost PNG, docks included)",
              WorldBackdropTest.FrostTextureBudgetBytes > astc6);
    }
}
