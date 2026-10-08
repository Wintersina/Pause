using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// The Frost v3 backdrop (FrostDirector, FrostBackdropSelection,
// FrostAmbientCatalog / AmbientEmitters): one of four ground sets per
// landing, never the same twice running; the cloud ceiling thick at the
// start and gone by ~35 s; blizzard gusts that come round and stay
// translucent; every layer behind gameplay; the ambient loops riding their
// landmarks (and nothing, without an error, when their atlas is missing);
// the elites' launch sites -- offered in the upper view, opening for the
// launch tell and the lift-off, shutting behind -- with the Rimebreaker
// launching from one; and no per-frame allocation.
// (The tiles' seams / darkness / contrast are WorldBackdropTest's.)
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod FrostBackdropTest.Run
public static class FrostBackdropTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[FBD] PASS  " : "[FBD] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        float savedSpeed = moveBackGround.speed;
        try
        {
            Catalog();
            Selection();
            Weather();
            Ambient();
            Sites();
            Allocation();
        }
        finally
        {
            moveBackGround.speed = savedSpeed;
            Time.timeScale = 1f;
            FrostBackdropSelection.Reset();
            LandingSites.Override = null;
            EliteSystem.PlayerOverride = null;
            EliteSystem.Clear();
            if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
        }
        Debug.Log("[FBD] failures: " + fails);
        return fails;
    }

    static WorldBackdrop Fresh(int variant = 0, float speed = .25f)
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        Time.timeScale = 1f;
        moveBackGround.speed = speed;
        FrostBackdropSelection.Reset();
        FrostBackdropSelection.Seed(11);
        FrostBackdropSelection.Force = variant;
        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = 1080f / 2400f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 1080, 2400);
        var wb = WorldBackdrop.Create("Frost");
        wb.Show("Frost", false);
        return wb;
    }

    static FrostDirector Director(WorldBackdrop wb) { return wb.Current != null ? wb.Current.Director as FrostDirector : null; }

    static void Run(WorldBackdrop wb, float seconds, System.Action each = null)
    {
        for (float t = 0f; t < seconds; t += Dt) { wb.Step(Dt); if (each != null) each(); }
    }

    // ---- the layer stack -----------------------------------------------------

    static void Catalog()
    {
        var spec = BackdropCatalog.For("Frost");
        Check("Frost has its own v3 folder and four variant sets",
              spec.folder == "Worlds/Frost/Backdrop3/" && spec.variantSets == FrostBackdropSelection.MaxVariants);
        string[] want = { "sky", "far", "mid", "flow", "aurora", "landmarks", "mist", "wisps", "ceiling",
                          "snow_far", "blizzard", "snow_mid", "snow_near" };
        bool all = true;
        foreach (string n in want) all &= spec.Has(n);
        Check("Frost layers: ground tiles + flow, aurora, landmarks, mist, wisps, ceiling, snow x3, blizzard", all);
        bool rising = true;
        for (int i = 1; i < spec.layers.Length; i++) rising &= spec.layers[i].rate > spec.layers[i - 1].rate;
        Check("Frost rates strictly increase far -> near", rising);
        Check("Frost landmarks ride the ground's far parallax (" + spec.Rate("landmarks") + " <= " + BackdropCatalog.MaxGroundRate + ")",
              spec.Rate("landmarks") <= BackdropCatalog.MaxGroundRate && spec.Find("landmarks").role == BackdropCatalog.Role.Landmark);
        Check("Frost's ceiling and wisps are cloud layers, snow / blizzard / mist atmosphere",
              spec.Find("ceiling").role == BackdropCatalog.Role.Cloud && spec.Find("wisps").role == BackdropCatalog.Role.Cloud &&
              spec.Find("blizzard").role == BackdropCatalog.Role.Atmosphere && spec.Find("snow_near").role == BackdropCatalog.Role.Atmosphere);
        Check("landmark drawings stay under the size limit (" + FrostTuning.LandmarkMax + ", sites " + FrostTuning.SiteMax + " <= " +
              BackdropCatalog.MaxLandmarkSize + ")",
              FrostTuning.LandmarkMax <= BackdropCatalog.MaxLandmarkSize && FrostTuning.SiteMax <= BackdropCatalog.MaxLandmarkSize);
        bool loops = true;
        foreach (var b in FrostAmbientCatalog.Bindings)
        {
            loops &= b.emitters.Length <= FrostAmbientCatalog.MaxPerPiece;
            foreach (var e in b.emitters) loops &= FrostAmbientCatalog.LoopIndex(e.loop) >= 0;
        }
        Check("every ambient emitter names a known loop and no piece carries more than " + FrostAmbientCatalog.MaxPerPiece, loops);
    }

    // ---- four ground sets, random per landing ----------------------------------

    static void Selection()
    {
        FrostBackdropSelection.Reset();
        int installed = FrostBackdropSelection.InstalledCount("Frost", 4);
        Check("all four Frost ground sets are installed (" + installed + ")", installed == 4);
        Check("a fifth variant is not", !FrostBackdropSelection.Installed("Frost", 5));

        FrostBackdropSelection.Seed(3);
        var seen = new HashSet<int>();
        int prev = 0, repeats = 0, bad = 0;
        for (int i = 0; i < 400; i++)
        {
            int v = FrostBackdropSelection.Pick("Frost", 4);
            if (v == prev) repeats++;
            if (!FrostBackdropSelection.Installed("Frost", v)) bad++;
            seen.Add(v);
            prev = v;
        }
        Check("400 landings pick only installed sets, all four of them (" + string.Join(",", seen) + ")", bad == 0 && seen.Count == 4);
        Check("... never the same set twice in a row (" + repeats + ")", repeats == 0);

        FrostBackdropSelection.Reset();
        FrostBackdropSelection.Seed(9);
        var a = new List<int>();
        for (int i = 0; i < 12; i++) a.Add(FrostBackdropSelection.Pick("Frost", 4));
        FrostBackdropSelection.Reset();
        FrostBackdropSelection.Seed(9);
        bool same = true;
        for (int i = 0; i < 12; i++) same &= FrostBackdropSelection.Pick("Frost", 4) == a[i];
        Check("a seed replays the same sequence", same);
        FrostBackdropSelection.Force = 3;
        Check("Force picks that set", FrostBackdropSelection.Pick("Frost", 4) == 3 && FrostBackdropSelection.Pick("Frost", 4) == 3);
        FrostBackdropSelection.Reset();

        // each entry into Frost loads its own set's tiles
        var wb = Fresh();
        int last = wb.Current.Variant, changes = 0, loaded = 0;
        for (int i = 0; i < 6; i++)
        {
            wb.Show("Space", false);
            wb.Show("Frost", false);
            var set = wb.Current;
            if (set.Variant != last) changes++;
            last = set.Variant;
            var sky = Resources.Load<Sprite>(BackdropCatalog.TileFolder("Frost", set.Variant) + "sky");
            if (sky != null && set.Textures.Contains(sky.texture) && set.Complete) loaded++;
        }
        Check("every landing in Frost flies a different set from the last, from its own folder (" + changes + "/6, " + loaded + "/6)",
              changes == 6 && loaded == 6);
    }

    // ---- ceiling, blizzard, layers behind gameplay ---------------------------------

    static void Weather()
    {
        var wb = Fresh(1);
        var d = Director(wb);
        Check("Frost builds its director on the v3 art", d != null && wb.Current.Complete);
        if (d == null) return;
        Run(wb, .5f);
        float c0 = d.CeilingCover;
        Run(wb, 14.5f);
        float c15 = d.CeilingCover;
        Run(wb, 20f);
        float c35 = d.CeilingCover;
        int live35 = d.Ceiling.ActiveCount;
        Check("the cloud ceiling is thick at the start (" + c0.ToString("F2") + " of the view >= .35)", c0 >= .35f);
        Check("... thinning by 15 s (" + c15.ToString("F2") + ")", c15 < c0 * .8f);
        Check("... and gone by 35 s (" + c35.ToString("F3") + ", " + live35 + " banks)", c35 < .02f && live35 == 0);
        Check("ceiling density: 1 at the start, 0 by " + FrostTuning.CeilingClearAt + " s",
              FrostDirector.CeilingDensity(0f) == 1f && FrostDirector.CeilingDensity(FrostTuning.CeilingClearAt) == 0f &&
              FrostDirector.CeilingDensity(18f) > 0f && FrostDirector.CeilingDensity(18f) < 1f);

        // blizzard gusts: periodic, translucent, behind gameplay
        int g0 = d.Gusts;
        float maxAlpha = 0f;
        int sheetFrames = 0;
        bool behind = true;
        var renderers = wb.Current.Root.GetComponentsInChildren<SpriteRenderer>(true);
        Run(wb, 150f, () =>
        {
            foreach (var p in d.Blizzard.items)
                if (p.active && p.sr.enabled) { sheetFrames++; maxAlpha = Mathf.Max(maxAlpha, p.sr.color.a); }
        });
        foreach (var r in renderers) behind &= r.sortingOrder < 0;
        int gusts = d.Gusts - g0;
        Check("blizzard gusts come round every " + FrostTuning.BlizzardEveryMin + "-" + FrostTuning.BlizzardEveryMax + " s (" + gusts +
              " in 150 s)", gusts >= 6 && gusts <= 12 && sheetFrames > 0);
        Check("... and stay translucent (peak alpha " + maxAlpha.ToString("F2") + " <= " + FrostTuning.BlizzardMaxAlpha + ")",
              maxAlpha > .05f && maxAlpha <= FrostTuning.BlizzardMaxAlpha + 1e-3f);
        // the sheets' own pixels are translucent (draw alpha x art alpha)
        float artMax = 0f;
        var png = new Texture2D(2, 2);
        png.LoadImage(System.IO.File.ReadAllBytes("Assets/Art/Backgrounds/Resources/Worlds/Frost/Backdrop3/weather.png"));
        var weather = new BackdropAtlas(Resources.Load<Texture2D>("Worlds/Frost/Backdrop3/weather"), Resources.Load<TextAsset>("Worlds/Frost/Backdrop3/weather"));
        foreach (var sp in weather.Frames("blizzard"))
        {
            var r = sp.rect;
            var px = png.GetPixels((int)r.x, (int)r.y, (int)r.width, (int)r.height);
            foreach (var c in px) artMax = Mathf.Max(artMax, c.a);
        }
        weather.Destroy();
        Object.DestroyImmediate(png);
        Check("... the gust never covers more than " + (maxAlpha * artMax * 100f).ToString("F0") + "% of what is under it (<= 30%)",
              artMax > 0f && maxAlpha * artMax <= .3f);
        Check("every Frost backdrop renderer sorts behind gameplay (" + renderers.Length + ")", behind);
        int landmarks = d.Ground.ActiveCount;
        Check("landmarks keep scrolling by (" + landmarks + " in view)", landmarks >= 2);

        // no two ground pieces ever overlap much
        float worst = 0f;
        Run(wb, 120f, () =>
        {
            foreach (var pool in d.Landmarks)
                foreach (var p in pool.items)
                {
                    if (!p.active) continue;
                    foreach (var pool2 in d.Landmarks)
                        foreach (var q in pool2.items)
                        {
                            if (!q.active || q == p) continue;
                            float dx = Mathf.Abs(p.x - q.x), dy = Mathf.Abs(p.y - q.y);
                            float r = (p.size + q.size) * .5f;
                            float ov = Mathf.Max(0f, r * .8f - dx) * Mathf.Max(0f, r * .8f - dy) / (r * r * .64f);
                            worst = Mathf.Max(worst, ov);
                        }
                }
        });
        Check("ground pieces never stack on each other (worst overlap " + worst.ToString("F2") + " of the art's core)", worst < .2f);
    }

    // ---- ambient loops -----------------------------------------------------------------

    static void Ambient()
    {
        var wb = Fresh(4);
        var d = Director(wb);
        if (d == null) { Check("Frost director", false); return; }
        bool all = true;
        foreach (var l in FrostAmbientCatalog.Loops) all &= d.Ambient.Has(l.name);
        Check("every run C loop is installed (" + FrostAmbientCatalog.Loops.Length + ")", all);
        int peak = 0;
        bool advanced = false;
        Sprite seenSprite = null;
        Run(wb, 90f, () =>
        {
            peak = Mathf.Max(peak, d.Ambient.LiveCount);
            foreach (var p in d.Ground.items)
            {
                if (!p.active) continue;
                var sr = p.body.Find("ambient0") != null ? p.body.Find("ambient0").GetComponent<SpriteRenderer>() : null;
                if (sr == null || !sr.enabled) continue;
                if (seenSprite != null && sr.sprite != seenSprite) advanced = true;
                seenSprite = sr.sprite;
            }
        });
        Check("landmarks carry their smoke / flares / beacons / lights (peak " + peak + " live loops)", peak >= 4);
        Check("... and the loops animate", advanced);
        int auroras = 0;
        Run(wb, 40f, () => { auroras = Mathf.Max(auroras, d.Aurora.ActiveCount); });
        Check("the glacier night (v4) shows its aurora", auroras > 0);

        // Missing atlases: Verdant's folder has none of the loop sheets.
        var root = new GameObject("~AmbientMissing").transform;
        var verdant = new BackdropSet("Verdant", root, 3f, 6f);
        var amb = new AmbientEmitters(verdant);
        var pool = new BackdropPool(root, "test", 2, -400, 0f);
        amb.Rig(pool);
        var piece = pool.Spawn();
        int shown = -1;
        bool quiet = true;
        try { shown = amb.Attach(piece, "refinery_00", new System.Random(1)); amb.Step(1f); }
        catch (System.Exception e) { quiet = false; Debug.LogException(e); }
        Check("without the loop atlases the emitters quietly show nothing (" + shown + ")", quiet && shown == 0 && !amb.Has("smoke_a"));
        verdant.Destroy();
        Object.DestroyImmediate(root.gameObject);

        // ... and with them, a refinery smokes from its stacks
        var root2 = new GameObject("~AmbientPresent").transform;
        var frost = new BackdropSet("Frost", root2, 3f, 6f);
        var amb2 = new AmbientEmitters(frost);
        var pool2 = new BackdropPool(root2, "test", 1, -400, 0f);
        amb2.Rig(pool2);
        var p2 = pool2.Spawn();
        int shown2 = amb2.Attach(p2, "refinery_00", new System.Random(1));
        amb2.Step(1f);
        Check("with them, a refinery gets its smoke plumes and windows (" + shown2 + " loops, " + amb2.LiveCount + " live)",
              shown2 == FrostAmbientCatalog.For("refinery_00").Length && amb2.LiveCount == shown2);
        frost.Destroy();
        Object.DestroyImmediate(root2.gameObject);
    }

    // ---- elite launch sites --------------------------------------------------------------

    static void Sites()
    {
        var wb = Fresh(2);
        var d = Director(wb);
        if (d == null) { Check("Frost director", false); return; }
        var sites = new List<LandingSite>();
        var kinds = new HashSet<LandingKind>();
        bool shape = true;
        string bad = "";
        int samples = 0;
        Run(wb, 240f, () =>
        {
            if (LandingSites.Collect(sites) == 0) return;
            samples++;
            foreach (var s in sites)
            {
                kinds.Add(s.kind);
                Vector3 p = s.Position - wb.transform.position;
                bool ok = s.emerge && s.Valid && s.order < EliteShip.PlayOrder - 2 && s.scale > .05f && s.scale < .5f &&
                          p.y > -CameraFit.ViewTop * .5f && s.kind >= LandingKind.Hangar;
                if (!ok && bad.Length < 200) bad += s.kind + " " + p.ToString("0.0") + " o" + s.order + " s" + s.scale.ToString("0.00") + "; ";
                shape &= ok;
            }
        });
        Check("Frost offers ground launch sites while flying (" + samples + " samples, " + d.SitesSpawned + " sites, kinds " +
              string.Join(",", kinds) + ")", samples > 200 && kinds.Count >= 4);
        Check("... each an emerge site of a Frost kind, behind gameplay, small, in the upper view " + bad, shape);

        // the Rimebreaker launches from one
        var defs = new List<EliteDef>();
        EliteCatalog.ForWorld(1, defs);
        EliteDef rime = defs.Find(x => x.key == "frost_elite_rimebreaker");
        Check("the Rimebreaker launches from a hangar or a crawler bay",
              rime != null && LandingSite.Accepts(rime.launchFrom, LandingKind.Hangar) &&
              LandingSite.Accepts(rime.launchFrom, LandingKind.CrawlerBay) && !LandingSite.Accepts(rime.launchFrom, LandingKind.Station));
        Check("Space elites keep their own kinds", LandingSite.KindOf("station") == LandingKind.Station &&
              LandingSite.Accepts("planet", LandingKind.Planet) && !LandingSite.Accepts("planet", LandingKind.Hangar));
        if (rime == null) return;

        // wait for a hangar or crawler bay in the upper view
        var pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -2.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        bool found = false;
        for (int i = 0; i < 30 * 120 && !found; i++)
        {
            wb.Step(Dt);
            LandingSites.Collect(sites);
            foreach (var s in sites) if (s.kind == LandingKind.Hangar || s.kind == LandingKind.CrawlerBay) found = true;
            if (!found && i % 300 == 299) d.SpawnSite(float.NaN, 0);
        }
        Check("a hangar or crawler bay comes into view", found);
        if (!found) return;
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        Random.InitState(5);
        int made = dir.SpawnGroup(1, 1);
        var e = EliteShip.Live.Count > 0 ? EliteShip.Live[0] : null;
        Check("the director docks the Rimebreaker in its own kind of site (" + made + ", " + (e != null ? e.Site.kind.ToString() : "-") + ")",
              e != null && (e.Site.kind == LandingKind.Hangar || e.Site.kind == LandingKind.CrawlerBay) && e.IsDocked);
        if (e == null) return;
        BackdropPiece site = null;
        foreach (var p in d.Sites.items) if (p.active && p.root == e.Site.anchor) site = p;
        Check("... inside a Frost site piece", site != null);
        if (site == null) return;
        Sprite closed = site.sr.sprite;
        bool shutWhileParked = true, opened = false, lit = false;
        var lamp = site.body.Find("lamps") != null ? site.body.Find("lamps").GetComponent<SpriteRenderer>() : null;
        for (int i = 0; i < 30 * 12 && e != null && e.State == EliteState.Parked; i++)
        {
            bool tell = e.StateTime >= e.ParkSeconds - EliteShip.EngineTellSeconds;
            wb.Step(Dt);
            EliteSystem.Step(Dt);
            if (!tell && d.SiteOpen(site)) shutWhileParked = false;
            if (tell && d.SiteOpen(site)) opened = true;
            if (lamp != null && lamp.sprite != null && lamp.sprite.name == "lights_on") lit = true;
        }
        Check("the site stays shut while the elite waits, opens in the launch tell (" + shutWhileParked + ", " + opened + ")",
              shutWhileParked && opened && site.sr.sprite != closed);
        Check("... its lamp row lights up", lit);
        bool openLift = true;
        for (int i = 0; i < 30 * 4 && e != null && e.State == EliteState.LiftOff; i++)
        {
            wb.Step(Dt);
            EliteSystem.Step(Dt);
            openLift &= d.SiteOpen(site);
        }
        Check("... stays open while it emerges and it reaches the play layer", openLift && e != null && e.InPlay);
        Run(wb, FrostTuning.SiteCloseDelay + 1f, () => EliteSystem.Step(Dt));
        Check("... then shuts again behind it", !d.SiteOpen(site) && site.sr.sprite == closed);
        Object.DestroyImmediate(dir.gameObject);
        EliteSystem.Clear();
    }

    // ---- 60 fps hygiene --------------------------------------------------------------------

    static void Allocation()
    {
        var wb = Fresh(3);
        var d = Director(wb);
        if (d == null) return;
        Run(wb, 60f);
        long control;
        bool meter = TestHarness.AllocMeterWorks(out control);
        Check("allocation meter sees a control allocation (" + control + " bytes)", meter);
        d.ForceGust();
        long used = TestHarness.AllocatedBytes(() => Run(wb, 120f));
        Check("Frost backdrop allocates nothing over 2 minutes of frames, spawns, gusts and loops (" + used + " bytes)",
              meter && used >= 0 && used <= 256);
        int transforms = wb.GetComponentsInChildren<Transform>(true).Length;
        Run(wb, 300f);
        Check("pools never grow over a long run", wb.GetComponentsInChildren<Transform>(true).Length == transforms);
    }
}
