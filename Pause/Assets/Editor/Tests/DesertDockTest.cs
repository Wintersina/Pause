using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Desert's elite hull LIGHTS and damaged BURN LOOPS, and the four new launch SITES (Space, Frost, Ember, Tide and Storm did the same job:
// SpaceDockTest, FrostDockTest, DesertDockTest, DesertDockTest):
//
//   lights   each Desert elite's <key>_lights strip (an additive overlay, installed beside the hull strip) loads, is cell-aligned with the hull
//            strip, is drawn by EliteShip over the hull wearing the SAME cell every frame, and keeps going while damaged
//   damaged  each elite's <key>_damaged companion strip (six more cells: every engine's flames drawn weak and ragged on the damaged hull) and its
//            <key>_lights_damaged continue the hull / lights strips as cells 9..14 (EliteCells.damagedLoop); once hurt the ship plays them
//            (EliteShip.DamagedLoops: no procedural plume over them), and every engine's patch changes from cell to cell
//   sites    the buried bunker, crawler hangar, mirror-array silo and canyon dock (DesertTuning.SiteNames 5..8, docks atlas) each have their
//            6-cell launch strip, a pad point on the drawing, a LandingKind of their own, and play idle / opening / open / hold / closing through
//            the launch (the ship's shared launch light is the tell: never a blink cell, flash, flare, halo or ring); Codex's launch_v2 strips (closed,
//            lamp, opening x2, open, closing; dust in the opening bay) are baked whole (build_launch_atlas.py)
//   dust     while a strip is in its open cells Desert's existing ambient smoke_b flipbook (DesertAmbientCatalog) plays over the bay, above the site and
//            below the elite, fading in over the opening and out through the closing; gone once it has closed
//   homes    each Desert elite launches from one of its own kinds (the new ones first) when one is free, any free site when none is
//   budget   the docks atlas is small (WorldBackdropTest measures every Desert PNG against the Desert texture budget, docks included)
//
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites DesertDockTest
public static class DesertDockTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DDK] PASS  " : "[DDK] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    const string AtlasPng = "Assets/Art/Backgrounds/Resources/Worlds/Desert/Backdrop3/docks.png";
    const string AtlasJson = "Assets/Art/Backgrounds/Resources/Worlds/Desert/Backdrop3/docks.json";
    const string Master = "Assets/Art/Enemies/Elite/Desert/new_docks_lights~/";
    const string Installed = "Assets/Art/Resources/Elites/Desert/";
    static Transform pilot;

    // key, launchFrom, its first (preferred) kind
    static readonly (string key, string launchFrom, LandingKind home)[] Elites =
    {
        ("desert_elite_dune_stalker", "canyondock,wormburrow", LandingKind.DesertCanyonDock),
        ("desert_elite_caravan_hauler", "crawlerhangar,caravanbay", LandingKind.DesertCrawlerHangar),
        ("desert_elite_lens_matriarch", "mirrorsilo,glasspad", LandingKind.DesertMirrorSilo),
        ("desert_elite_glass_cannon", "mirrorsilo,buriedsilo", LandingKind.DesertMirrorSilo),
        ("desert_elite_scorpion_walker", "buriedbunker,ruindock", LandingKind.DesertBuriedBunker),
        ("desert_elite_mirror_bastion", "buriedbunker,ruindock", LandingKind.DesertBuriedBunker),
    };

    // DesertTuning site index, its atlas tag, its LandingKind and its launchFrom word
    static readonly (int site, string tag, LandingKind kind, string word)[] Sites =
    {
        (5, "buriedbunker", LandingKind.DesertBuriedBunker, "buriedbunker"),
        (6, "crawlerhangar", LandingKind.DesertCrawlerHangar, "crawlerhangar"),
        (7, "mirrorsilo", LandingKind.DesertMirrorSilo, "mirrorsilo"),
        (8, "canyondock", LandingKind.DesertCanyonDock, "canyondock"),
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EliteCatalog.Reload();
            LightStrips();
            DamagedLoops();
            DamagedRuntime();
            SiteArt();
            SitesAndLaunch();
            Homes();
            Budget();
        }
        finally
        {
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            LandingSites.Override = null;
            RunScore.EndRun(RunScore.RunId);
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
            startMenu.youAreInTutorial = false;
        }
        Debug.Log("[DDK] failures: " + fails);
        return fails;
    }

    // ---- fixtures ------------------------------------------------------------------------

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

    static void WaitBack(EliteShip e, float seconds = 20f)
    {
        for (float t = 0f; t < seconds && Alive(e) && e.Hidden; t += Dt) EliteSystem.Step(Dt);
    }

    static Color32[] Load(string path, out int w, out int h)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(path));
        w = t.width; h = t.height;
        var px = t.GetPixels32();
        Object.DestroyImmediate(t);
        return px;
    }

    static bool SameBytes(string a, string b)
    {
        if (!File.Exists(a) || !File.Exists(b)) return false;
        var x = File.ReadAllBytes(a);
        var y = File.ReadAllBytes(b);
        if (x.Length != y.Length) return false;
        for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
        return true;
    }

    // ---- lights ----------------------------------------------------------------------------

    static void LightStrips()
    {
        foreach (var x in Elites)
        {
            var d = EliteCatalog.Find(x.key);
            var hull = EliteArt.Frames(d);
            var lights = EliteArt.ExtraFrames(d, EliteArt.Extra.Lights);
            Check(x.key + ": the _lights strip is installed and loads (" + (lights != null ? lights.Length : 0) + " cells)", lights != null && lights.Length > 0 && EliteArt.HasExtra(d, EliteArt.Extra.Lights));
            Check(x.key + ": the masters are kept in new_docks_lights~ and the installed copies are byte for byte the masters",
                  SameBytes(Master + x.key + "_lights.png", Installed + x.key + "_lights.png") &&
                  SameBytes(Master + "damaged/" + x.key + "_damaged.png", Installed + x.key + "_damaged.png") &&
                  SameBytes(Master + "damaged/" + x.key + "_damaged_lights.png", Installed + x.key + "_lights_damaged.png"));
            if (lights == null || hull == null) continue;
            bool aligned = lights.Length == hull.Length;
            for (int i = 0; i < Mathf.Min(lights.Length, hull.Length); i++)
                aligned &= Mathf.Approximately(lights[i].rect.width, hull[i].rect.width) && Mathf.Approximately(lights[i].rect.height, hull[i].rect.height) &&
                           Mathf.Approximately(lights[i].pixelsPerUnit, hull[i].pixelsPerUnit) && lights[i].pivot == hull[i].pivot;
            Check(x.key + ": cell-aligned with the hull strip (" + lights.Length + " vs " + hull.Length + " cells of " + lights[0].rect.width + " px: 9 flight cells + 6 burn-loop ones)", aligned && lights.Length == 15);
            Check(x.key + ": its lights are bright small points over a clear strip (not a second hull)", Coverage(Installed + x.key + "_lights.png", 3) > 0f && Coverage(Installed + x.key + "_lights.png", 3) < .1f);
        }
    }

    // Share of a cell's pixels with any alpha.
    static float Coverage(string path, int cell)
    {
        int w, h;
        var px = Load(path, out w, out h);
        int lit = 0;
        for (int y = 0; y < h; y++)
            for (int x = cell * h; x < (cell + 1) * h; x++) if (px[y * w + x].a > 0) lit++;
        return lit / (float)(h * h);
    }

    // ---- the damaged burn loop: strips ---------------------------------------------------------

    static void DamagedLoops()
    {
        foreach (var x in Elites)
        {
            var d = EliteCatalog.Find(x.key);
            var c = d.cells;
            Check(x.key + ": cells.damagedLoop = 9..14 on the damaged cell 6 (the loop is the companion strip's six cells)",
                  c.damaged == 6 && c.HasDamagedLoop && c.damagedLoop.Length == 6 && c.damagedLoop[0] == 9 && c.damagedLoop[5] == 14 && c.Count == 15);
            var hull = EliteArt.Frames(d);
            Check(x.key + ": the hull strip continues with the _damaged companion (" + (hull != null ? hull.Length : 0) + " cells of " + (hull != null ? hull[0].rect.width : 0) + " px)",
                  hull != null && hull.Length == 15 && Mathf.Approximately(hull[14].rect.width, d.cellPixels) && Mathf.Approximately(hull[14].rect.height, d.cellPixels));
            // every engine's patch (a window under each nozzle) differs across the six loop cells: the engines animate
            int w, h;
            var px = Load(Installed + x.key + "_damaged.png", out w, out h);
            bool animated = true;
            string bad = "";
            foreach (var n in d.nozzles)
            {
                var seen = new HashSet<long>();
                for (int cell = 0; cell < 6; cell++)
                {
                    long hash = 17;
                    for (int dy = 0; dy < 56; dy++)
                        for (int dx = -24; dx <= 24; dx++)
                        {
                            int px0 = cell * h + Mathf.Clamp(Mathf.RoundToInt(n.x) + dx, 0, h - 1);
                            int py = h - 1 - Mathf.Clamp(Mathf.RoundToInt(n.y) + dy, 0, h - 1);
                            var p = px[py * w + px0];
                            hash = hash * 31 + (p.a > 8 ? p.r * 7 + p.g * 3 + p.b : -1);
                        }
                    seen.Add(hash);
                }
                if (seen.Count < 3) { animated = false; bad += n.name + "(" + seen.Count + ") "; }
            }
            Check(x.key + ": every engine's flames change across the burn loop (>= 3 distinct drawings each) " + bad, animated && d.nozzles.Length > 0);
        }
    }

    // ---- the damaged burn loop: in play ---------------------------------------------------------

    static void DamagedRuntime()
    {
        foreach (var x in Elites)
        {
            Fresh(.05f);
            var d = EliteCatalog.Find(x.key);
            var e = InPlay(d, new Vector2(0f, 1.5f));
            Step(1.2f);
            bool light = e.HasLights;
            var healthy = new HashSet<int>();
            Step(1f, () => { pilot.position = new Vector3(Mathf.Sin(Time.frameCount * .1f), -2.5f, 0f); healthy.Add(e.CurrentFrame); });
            Check(x.key + ": whole, it draws its lights over the flight cells (" + string.Join(",", healthy) + "), no burn loop yet",
                  light && !e.Damaged && !e.DamagedLoops && healthy.Count > 0 && !healthy.Contains(9));
            // (a roamer outside the board is untouchable: wait for it to come back on)
            for (float t = 0f; t < 20f && Alive(e) && e.Offstage; t += Dt) EliteSystem.Step(Dt);
            e.TakeHit(EliteDamage.Teleport, e.Position, 1);
            Step(.1f);
            if (e.Hidden) WaitBack(e);
            Step(EliteShip.GraceSeconds + .1f);
            var cells = new HashSet<int>();
            bool lightsMatch = true;
            Step(3f, () =>
            {
                cells.Add(e.CurrentFrame);
                lightsMatch &= e.LightCell == e.CurrentFrame || e.LightCell < 0;
            });
            Check(x.key + ": hurt, it plays the six-cell burn loop (" + string.Join(",", cells) + "), every cell of it",
                  e.Damaged && e.DamagedLoops && cells.SetEquals(new[] { 9, 10, 11, 12, 13, 14 }));
            Check(x.key + ": its lights wear the same burn-loop cell as the hull and keep going (" + e.LightCell + ")", lightsMatch && e.HasLights && e.Lights.enabled);
            Check(x.key + ": no procedural damaged plume over the painted loop, no hover bob (DamagedLoops)", e.DamagedLoops);
        }
        EliteSystem.Clear();
    }

    // ---- the site art ---------------------------------------------------------------------------

    static void SiteArt()
    {
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(AtlasPng));
        var px = tex.GetPixels32();
        int W = tex.width, H = tex.height;
        Object.DestroyImmediate(tex);
        var json = JsonUtility.FromJson<Atlas>(File.ReadAllText(AtlasJson));
        Check("the docks atlas is " + W + " x " + H + " and its manifest says so (" + json.sheetW + " x " + json.sheetH + ")", json.sheetW == W && json.sheetH == H && json.sprites.Length == 24);
        foreach (var s in Sites)
        {
            string k = s.tag + ": ";
            var rects = new Dictionary<string, Rect2>();
            foreach (var r in json.sprites) rects[r.n] = r;
            bool six = true;
            var distinct = new HashSet<string>();
            for (int i = 0; i < 6; i++)
            {
                Rect2 r;
                six &= rects.TryGetValue(s.tag + "_" + i.ToString("00"), out r) && r.x >= 0 && r.y >= 0 && r.x + r.w <= W && r.y + r.h <= H;
                if (rects.TryGetValue(s.tag + "_" + i.ToString("00"), out r)) distinct.Add(r.x + "," + r.y);
            }
            Check(k + "six launch cells in the manifest, inside the sheet, six DISTINCT drawings (closed, lamp, opening x2, open, closing) -- no baked duplicates (" + distinct.Count + " distinct)", six && distinct.Count == 6);
            Rect2 r0 = rects[s.tag + "_00"], r2 = rects[s.tag + "_02"], r4 = rects[s.tag + "_04"], r5 = rects[s.tag + "_05"];
            // the pad point (the launch opening) sits on the drawing
            Vector2 pad = DesertTuning.SitePads[s.site];
            int cx = r4.x + Mathf.RoundToInt((.5f + pad.x) * r4.w), cy = r4.y + Mathf.RoundToInt((.5f + pad.y) * r4.h);   // (y from the bottom, as Unity's rects)
            Check(k + "the pad point (" + pad.x.ToString("0.000") + ", " + pad.y.ToString("0.000") + ") is on the drawing (alpha " + px[cy * W + cx].a + ")", px[cy * W + cx].a > 200);
            // the doors part cell by cell: the picture differs from the closed cell more and more (2 <= 3 <= 4), half shut is in between, and no cell
            // is a flat black box
            var rl = new Rect2[6];
            for (int i = 0; i < 6; i++) rl[i] = rects[s.tag + "_" + i.ToString("00")];
            float[] diff = new float[6];
            bool boxFree = true;
            for (int i = 0; i < 6; i++)
            {
                int opaque = 0, dark = 0;
                long sum = 0;
                for (int y = 0; y < rl[i].h; y++)
                    for (int x = 0; x < rl[i].w; x++)
                    {
                        var p = px[(rl[i].y + y) * W + rl[i].x + x];
                        var q = px[(rl[0].y + y) * W + rl[0].x + x];
                        sum += Mathf.Abs(p.r - q.r) + Mathf.Abs(p.g - q.g) + Mathf.Abs(p.b - q.b) + Mathf.Abs(p.a - q.a);
                        if (p.a < 200) continue;
                        opaque++;
                        if (p.r + p.g + p.b < 60) dark++;
                    }
                diff[i] = sum / (float)(rl[i].w * rl[i].h);
                boxFree &= opaque > 4000 && dark / (float)Mathf.Max(1, opaque) < .6f;
            }
            Check(k + "the cells play closed 0 < opening " + diff[2].ToString("0.0") + " <= " + diff[3].ToString("0.0") + " <= open " + diff[4].ToString("0.0") + " (the doors part), half-shut " + diff[5].ToString("0.0") + " between; none is a black box",
                  boxFree && diff[2] > 0f && diff[2] <= diff[3] + .01f && diff[3] <= diff[4] + .01f && diff[5] > diff[0] && diff[5] < diff[4]);
            Check(k + "its LandingKind and launchFrom word parse (" + s.word + ")", LandingSite.KindOf(s.word) == s.kind && LandingSite.Accepts(s.word, s.kind) && !LandingSite.Accepts(s.word, LandingKind.Gantry) &&
                  DesertTuning.SiteKinds[s.site] == s.kind && DesertTuning.SiteNames[s.site] == s.tag && DesertTuning.HasLaunchStrip(s.site));
        }
        Check("the launch cells by phase (idle through the tell's first share, opening, open, hold: no blink cell)",
              DesertTuning.LaunchCellAt(.05f) == DesertTuning.LaunchIdle && DesertTuning.LaunchCellAt(.6f) == DesertTuning.LaunchOpening &&
              DesertTuning.LaunchCellAt(1.05f) == DesertTuning.LaunchOpen && DesertTuning.LaunchCellAt(1.5f) == DesertTuning.LaunchHold);
        bool never = true;
        for (float ph = .01f; ph < 2f; ph += .01f) never &= DesertTuning.LaunchCellAt(ph) != DesertTuning.LaunchBlink;
        Check("... and the strip's blink cell is never picked by any phase", never);
    }

    [System.Serializable] class Rect2 { public string n; public int x, y, w, h; }
    [System.Serializable] class Atlas { public int sheetW, sheetH; public Rect2[] sprites; }

    // ---- the sites on the ground, and the launch through them ------------------------------------------

    static DesertDirector NewDesert(out WorldBackdrop wb)
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        Time.timeScale = 1f;
        moveBackGround.speed = .25f;
        var sel = BackdropVariants.For("Desert");
        sel.Reset();
        sel.Seed(11);
        sel.Force = 1;
        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = 1080f / 2400f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 1080, 2400);
        wb = WorldBackdrop.Create("Desert");
        wb.Show("Desert", false);
        return wb.Current != null ? wb.Current.Director as DesertDirector : null;
    }

    static int CellOf(DesertDirector ed, int kind, Sprite s)
    {
        var strip = ed.LaunchStrip(kind);
        for (int i = 0; strip != null && i < strip.Length; i++) if (strip[i] == s) return i;
        return -1;
    }

    static void SitesAndLaunch()
    {
        foreach (var s in Sites)
        {
            Fresh(.25f);
            WorldBackdrop wb;
            var ed = NewDesert(out wb);
            string k = s.tag + " site: ";
            Check(k + "Desert's backdrop is up", ed != null);
            if (ed == null) continue;
            for (int i = 0; i < 30 * 8; i++) wb.Step(Dt);
            bool placed = false;
            for (int i = 0; i < 60 && !placed; i++) { placed = ed.SpawnSite(float.NaN, s.site); if (!placed) wb.Step(.5f); }
            Check(k + "one is spawned on the ground (" + placed + ")", placed);
            if (!placed) { Object.DestroyImmediate(wb.gameObject); continue; }
            LandingSite site = default;
            BackdropPiece piece = null;
            var list = new List<LandingSite>();
            for (int i = 0; i < 30 * 12 && piece == null; i++)
            {
                wb.Step(Dt);
                list.Clear();
                ed.LandingSites(list);
                foreach (var l in list)
                    if (l.kind == s.kind)
                        foreach (var p in ed.Sites.items) if (p.active && p.root == l.anchor) { site = l; piece = p; }
            }
            Check(k + "it offers a " + s.kind + " site, an emerge site inside the drawing, behind the gameplay, small",
                  piece != null && site.emerge && site.Valid && site.order < EliteShip.PlayOrder - 2 && site.scale > .05f && site.scale < .5f && piece.kind == s.site);
            if (piece == null) { Object.DestroyImmediate(wb.gameObject); continue; }
            Bounds b = piece.sr.sprite.bounds;
            Vector2 pad = DesertTuning.SitePads[s.site];
            Vector3 want = piece.root.TransformPoint(new Vector3(b.center.x + pad.x * b.size.x, b.center.y + pad.y * b.size.y, 0f));
            Check(k + "the site is the pad point (" + Vector3.Distance(want, site.Position).ToString("0.000") + " u off)", Vector3.Distance(want, site.Position) < .005f);
            float width = b.size.x * piece.root.lossyScale.x;
            Check(k + "drawn " + width.ToString("0.00") + " u wide (the usual " + DesertTuning.SiteMin + ".." + DesertTuning.SiteMax + " x " + DesertTuning.SiteWidthK[s.site] + ")",
                  width >= DesertTuning.SiteMin * DesertTuning.SiteWidthK[s.site] - .02f && width <= DesertTuning.SiteMax * DesertTuning.SiteWidthK[s.site] + .02f && width >= DesertTuning.SiteMinSize);
            Check(k + "idle until a launch, its old lamp row off", CellOf(ed, s.site, piece.sr.sprite) == DesertTuning.LaunchIdle &&
                  (piece.body.Find("lamps") == null || !piece.body.Find("lamps").GetComponent<SpriteRenderer>().enabled));

            // the launch: an elite parks in it; the strip plays through the tell and the lift-off
            EliteDef d = null;
            foreach (var x in Elites) if (x.home == s.kind) { d = EliteCatalog.Find(x.key); break; }
            var e = EliteShip.Create(d, site, 2.4f, EliteDirector.JoinPoint(d, EliteBrains.Create(d.brain).JoinFrom, site.Position));
            var order = new List<int>();
            var phaseOfCell = new Dictionary<int, (float lo, float hi)>();
            int lastCell = -1;
            bool rides = true, lit = false;
            float afterJoin = 0f;
            int slot = ed.Sites.items.IndexOf(piece);
            bool mistEarly = false, mistOpen = false, opened = false;
            float maxFade = 0f;
            for (int i = 0; i < 30 * 16; i++)
            {
                wb.Step(Dt);
                EliteSystem.Step(Dt);
                int cell = CellOf(ed, s.site, piece.sr.sprite);
                float ph = Alive(e) ? e.LaunchPhase : 2f;
                lit |= e.LaunchLightOn;
                if (e.State == EliteState.Parked) rides &= Vector2.Distance(e.transform.position, site.Position) < .02f || !site.Valid;
                if (cell != lastCell) { order.Add(cell); lastCell = cell; }
                bool shownMist = ed.MistShowing(slot);
                if (cell == DesertTuning.LaunchIdle && !opened) mistEarly |= shownMist;
                if (cell >= DesertTuning.LaunchOpening && cell <= DesertTuning.LaunchHold) { opened = true; mistOpen |= shownMist; maxFade = Mathf.Max(maxFade, ed.MistFade(slot)); }
                if (!phaseOfCell.ContainsKey(cell)) phaseOfCell[cell] = (ph, ph);
                else { var v = phaseOfCell[cell]; phaseOfCell[cell] = (Mathf.Min(v.lo, ph), Mathf.Max(v.hi, ph)); }
                if (e.State != EliteState.Parked && e.State != EliteState.LiftOff) { afterJoin += Dt; if (afterJoin > DesertTuning.SiteCloseDelay + 1f) break; }
                if (!piece.active) break;
            }
            bool idleFirst = order.Count > 0 && order[0] == DesertTuning.LaunchIdle;
            bool closed = order.Count > 0 && order[order.Count - 1] == DesertTuning.LaunchIdle && order.Contains(DesertTuning.LaunchClosing);
            int lastIdx = -1;
            bool inOrder = true;
            foreach (int c in new[] { DesertTuning.LaunchOpening, DesertTuning.LaunchOpen, DesertTuning.LaunchHold, DesertTuning.LaunchClosing })
            {
                int idx = order.IndexOf(c);
                if (idx < 0 || idx < lastIdx) { inOrder = false; break; }
                lastIdx = idx;
            }
            Check(k + "its strip plays idle, opening, open, hold, closing, idle, never the blink cell (" + string.Join(">", order) + ")",
                  idleFirst && inOrder && closed && !order.Contains(DesertTuning.LaunchBlink) && !order.Contains(-1));
            Check(k + "the doors open in the launch tell, open as the elite emerges; the ship's one small launch light is the tell",
                  lit && phaseOfCell.ContainsKey(DesertTuning.LaunchOpening) && phaseOfCell[DesertTuning.LaunchOpening].lo < 1f &&
                  phaseOfCell.ContainsKey(DesertTuning.LaunchOpen) && phaseOfCell[DesertTuning.LaunchOpen].lo >= 1f);
            var mistSr = ed.MistRenderer(slot);
            Check(k + "the launch dust (the ambient smoke_b flipbook) plays only in the open cells: never while closed before the launch (" + mistEarly + "), up while opening / open (" + mistOpen +
                  ", fade " + maxFade.ToString("0.00") + "), gone after the closing (" + ed.MistShowing(slot) + ")",
                  !mistEarly && mistOpen && maxFade > .9f && !ed.MistShowing(slot));
            Check(k + "... drawn above the site and below the elite emerging from it (order " + (mistSr != null ? mistSr.sortingOrder : -1) + " vs site " + piece.sr.sortingOrder + " / elite " + site.order + ")",
                  mistSr != null && mistSr.sortingOrder > piece.sr.sortingOrder && mistSr.sortingOrder < site.order);
            Check(k + "the elite rode the site and came out into play (" + e.State + ")", rides && e.InPlay);
            Object.DestroyImmediate(wb.gameObject);
            EliteSystem.Clear();
        }
    }

    // ---- homes ---------------------------------------------------------------------------------------

    static void Homes()
    {
        Fresh(.05f);
        var all = DesertTuning.SiteKinds;
        var pads = new List<LandingSite>();
        for (int i = 0; i < all.Length; i++)
        {
            var anchor = new GameObject("~Body" + i).transform;
            anchor.position = new Vector2(-2f + i * .5f, 3f + i * .1f);
            pads.Add(new LandingSite { anchor = anchor, local = Vector3.zero, scale = .3f, order = -300, id = i + 1, kind = all[i], emerge = true });
        }
        LandingSites.Override = list => list.AddRange(pads);
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        var perKey = new Dictionary<string, int>();
        bool own = true;
        string bad = "";
        for (int i = 0; i < 90; i++)
        {
            EliteSystem.Clear();
            dir.SpawnGroup(6, 1);
            foreach (var e in EliteShip.Live)
            {
                perKey[e.Def.key] = perKey.ContainsKey(e.Def.key) ? perKey[e.Def.key] + 1 : 1;
                bool ok = LandingSite.Accepts(e.Def.launchFrom, e.Site.kind);
                own &= ok;
                if (!ok) bad += e.Def.key + "@" + e.Site.kind + " ";
            }
        }
        Check("each Desert elite launches from one of its own two kinds of site (" + perKey.Count + " kinds of elite, " + bad + ")", own && perKey.Count == 6);
        foreach (var x in Elites)
        {
            var d = EliteCatalog.Find(x.key);
            Check(x.key + ": launchFrom '" + d.launchFrom + "' = " + x.launchFrom + ", first kind " + x.home, d.launchFrom == x.launchFrom && LandingSite.KindOf(d.launchFrom) == x.home);
        }
        Object.DestroyImmediate(dir.gameObject);
        LandingSites.Override = null;
        EliteSystem.Clear();
    }

    // ---- budget --------------------------------------------------------------------------------------

    static void Budget()
    {
        var fi = new FileInfo(AtlasPng);
        Check("the docks atlas is small (" + (fi.Length / 1024) + " KB on disk)", fi.Exists && fi.Length < 800 * 1024);
    }
}
