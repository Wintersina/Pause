using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// COVER BOULDERS (docs/cover-boulders.md; Scripts/Gameplay/Cover).
//
//   TABLE      one entry per world, sane numbers, no pink outside Space, art loads where wired
//   SAFETY     the ship is blocked but never trapped: for every lane shape, boulder width, side, position and turn the band that keeps
//              one side free holds; a grid of fingers always resolves to a point outside every circle and inside the reach; a boulder
//              falling through the lane onto a still finger never throws the ship (continuous), never pins it
//   WEAPONS    every player weapon (each ship's main attack): stopped / truncated / line-of-sight blocked; the boulder never changes
//   HOSTILE    hostile shots (real elite shots and the registry), beams and area hazards by line of sight; bodies solid; shoved bodies crash
//   BOSS       arrival destroys boulders (the sequence, drawn and procedural), none spawn during a fight
//   RULES      spawn rules and rarity per world, the first 20 s, elites' landing sites, tutorial, death, world change
//   COST       no allocation, pooled; ScreenFit: every device shape keeps one side free and the boulder inside the rails
public static class CoverBoulderTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[COVER] PASS  " : "[COVER] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Table();
            Art();
            WorldArt();
            BackdropRules();
            Landforms();
            Emerge();
            Lanes();
            Safety();
            DynamicSafety();
            HostileShotsBlocked();
            PlayerWeapons();
            ShootThroughFlags();
            Bodies();
            Shoves();
            LineOfSight();
            BossArrival();
            Rules();
            Rarity();
            Cost();
            Screens();
        }
        finally
        {
            CoverBoulders.Disabled = false;
            CoverBoulders.TestAnyWorld = false;
            CoverBoulderDirector.WorldOverride = -1;
            CoverBoulderDirector.BackdropVariantOverride = -1;
            CoverBoulders.Dispose();
            EliteSystem.PlayerOverride = null;
            EliteSystem.Clear();
            HostileShots.ResetCounters();
            ShoveCrash.RollOverride = null;
        }
        Debug.Log("[COVER] failures: " + fails);
        return fails;
    }

    // ---- fixtures ----

    static Camera cam;

    static void Fresh()
    {
        CoverBoulders.Dispose();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EliteSystem.Clear();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 6.6f;
        cam.aspect = 9f / 16f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .2f;
        Time.timeScale = 1f;
        collisionDetection.cloakTimer = 0f;
        collisionDetection.atomCheck = false;
        collisionDetection.lifeCounter = 0;
        BossEncounter.ResetRun();
        BossRails.Reset();
        WorldTimeFx.Reset();
        CoverBoulders.Disabled = false;
        CoverBoulders.TestAnyWorld = true;
        CoverBoulderDirector.WorldOverride = -1;
        ShoveCrash.RollOverride = null;
        HostileShots.ResetCounters();
        Random.InitState(4242);
    }

    // A boulder (Space art, variant v) put exactly at `at`, held still.
    static CoverBoulder Place(int variant, Vector2 at, float rot = 0f, int world = 0)
    {
        var b = CoverBoulders.Spawn(world, variant);
        b.Hold(at, rot);
        return b;
    }

    static float Top(CoverBoulder b) { float m = float.MinValue; for (int i = 0; i < b.Count; i++) m = Mathf.Max(m, b.CircleCentre(i).y + b.CircleRadius(i)); return m; }
    static float Bottom(CoverBoulder b) { float m = float.MaxValue; for (int i = 0; i < b.Count; i++) m = Mathf.Min(m, b.CircleCentre(i).y - b.CircleRadius(i)); return m; }

    static GameObject Hazard(float x, float y, string tag = "Enimey", float radius = .3f)
    {
        var go = new GameObject("hazard");
        go.tag = tag;
        go.transform.position = new Vector3(x, y, 0f);
        ClearTarget.Ensure(go).SetRadius(radius);
        return go;
    }

    // ---- table ----

    static void Table()
    {
        string bad = CoverBoulderTable.Validate();
        Check("the table checks out (" + (bad ?? "ok") + ")", bad == null);
        Check("an entry for each of the " + WorldManager.Worlds.Length + " worlds", CoverBoulderTable.Entries.Length == WorldManager.Worlds.Length && WorldManager.Worlds.Length == 7);
        bool names = true, three = true, notPink = true;
        for (int w = 0; w < CoverBoulderTable.Entries.Length; w++)
        {
            var e = CoverBoulderTable.Entries[w];
            names &= e.world == WorldManager.Worlds[w].displayName && !string.IsNullOrEmpty(e.concept);
            three &= e.Variants == 3;
            if (w > 0) notPink &= !WorldCue.IsPink(e.crack) && !WorldCue.IsPink(e.debris) && !WorldCue.IsPink(e.chip2);
        }
        Check("each entry is its world's, with a concept and three variants", names && three);
        Check("no pink in any world's crack light or debris except Space's", notPink && WorldCue.IsPink(CoverBoulderTable.Entries[0].crack));
        Check("Space is wired; a world is enabled only with its art in Resources/Boulders",
              CoverBoulderTable.Enabled(0) && !CoverBoulderTable.Enabled(-1) && !CoverBoulderTable.Enabled(99));
        for (int w = 0; w < CoverBoulderTable.Entries.Length; w++)
            if (CoverBoulderTable.Entries[w].enabled)
                Check(CoverBoulderTable.Entries[w].world + ": an enabled world's art loads", CoverBoulderArt.Has(w));
        // the drift styles are per world: the rock spins, a mountain and a mesa never turn
        var space = CoverBoulderTable.Entries[0]; var frost = CoverBoulderTable.Entries[1]; var desert = CoverBoulderTable.Entries[6];
        Check("Space tumbles slowly; Frost and Desert slide without turning; the rest sway, rock, bob or hover",
              space.drift == CoverDrift.Tumble && space.spin.y <= 10f && frost.drift == CoverDrift.Slide && desert.drift == CoverDrift.Slide &&
              CoverBoulderTable.Entries[2].drift == CoverDrift.Sway && CoverBoulderTable.Entries[3].drift == CoverDrift.Rock &&
              CoverBoulderTable.Entries[4].drift == CoverDrift.Bob && CoverBoulderTable.Entries[5].drift == CoverDrift.Hover);
    }

    // ---- art ----

    static void Art()
    {
        Fresh();
        var v = CoverBoulderArt.For(0);
        Check("Space's art loads: three variants with a sprite, 9 fracture tiles and 1-4 circles each",
              v != null && v.Length == 3 && System.Array.TrueForAll(v, x => x.intact != null && x.pieces.Length == 9 && x.circles.Length >= 1 && x.circles.Length <= CoverBoulder.MaxCircles));
        if (v == null) return;
        bool inside = true;
        foreach (var x in v)
            foreach (var c in x.circles)
                inside &= new Vector2(c.x, c.y).magnitude + c.r <= x.cellPx * .5f * 1.05f && c.r > 8f;
        Check("every hit circle lies inside its cell", inside);
        Check("Space's approved destruction strip is wired: 5 drawn frames per variant", System.Array.TrueForAll(v, x => x.frames != null && x.frames.Length == CoverBoulderArt.FramesPerStrip));
        string keep = CoverBoulderTable.Entries[3].folder;
        CoverBoulderTable.Entries[3].folder = "Boulders/Nowhere";   // (every world has art now: pretend one has not)
        var w = CoverBoulderArt.For(3);
        CoverBoulderTable.Entries[3].folder = keep;
        Check("a world with no art borrows Space's for the tests", w != null && w[0] == v[0]);
    }

    // ---- every wired world's art: complete, real resolution, never clamped, pale-edged, no pink outside Space ----

    // Share of opaque texels equal to their k x k block's first texel at the best grid phase (>= .97 is a block upscale).
    static float BlockScore(Color32[] px, int W, int H, int k)
    {
        float best = 0f;
        for (int oy = 0; oy < k; oy++)
            for (int ox = 0; ox < k; ox++)
            {
                long eq = 0, n = 0;
                for (int y = oy; y + k <= H; y++)
                {
                    int by = oy + (y - oy) / k * k;
                    for (int x = ox; x + k <= W; x++)
                    {
                        var c = px[y * W + x];
                        if (c.a <= 8) continue;
                        n++;
                        var f = px[by * W + ox + (x - ox) / k * k];
                        if (c.r == f.r && c.g == f.g && c.b == f.b && c.a == f.a) eq++;
                    }
                }
                if (n > 300) best = Mathf.Max(best, eq / (float)n);
            }
        return best;
    }

    static Color32[] ReadPng(string path, out int w, out int h)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(System.IO.File.ReadAllBytes(path));
        w = tex.width; h = tex.height;
        var px = tex.GetPixels32();
        Object.DestroyImmediate(tex);
        return px;
    }

    static void WorldArt()
    {
        Fresh();
        float spaceDensity = 0f;
        var sp = CoverBoulderArt.For(0);
        for (int i = 0; i < sp.Length; i++)
            spaceDensity += sp[i].artWidth / CoverBoulderTable.Entries[0].widths[i] / sp.Length;
        for (int w = 0; w < CoverBoulderTable.Entries.Length; w++)
        {
            var e = CoverBoulderTable.Entries[w];
            if (!e.enabled) continue;
            string name = e.world;
            var v = CoverBoulderArt.Load(e.folder);
            Check(name + ": manifest + art load, 3 variants, 1-4 hit circles each, five drawn break frames",
                  v != null && v.Length == 3 && System.Array.TrueForAll(v, x => x.intact != null && x.circles.Length >= 1 && x.circles.Length <= CoverBoulder.MaxCircles &&
                                                                           x.frames != null && x.frames.Length == CoverBoulderArt.FramesPerStrip));
            if (v == null) continue;
            float cell = v[0].cellPx;
            // texel density: the art's own pixels per world unit never below Space's (no downscale to save memory)
            for (int i = 0; i < v.Length && w > 0; i++)
                Check(name + " v" + i + ": texel density " + (v[i].artWidth / e.widths[i]).ToString("0") + " px/u >= Space's " + spaceDensity.ToString("0"),
                      v[i].artWidth / e.widths[i] >= spaceDensity * 1.05f);
            bool inside = true, pivot = true;
            foreach (var x in v)
            {
                // collider sanity: circles inside the cell and inside the art's own width, the centre circle near the cell centre
                foreach (var c in x.circles) inside &= new Vector2(c.x, c.y).magnitude + c.r <= cell * .5f * 1.05f && c.r > 8f * cell / 512f && Mathf.Abs(c.x) < x.artWidth * .5f;
                pivot &= x.intact.pivot.x * 2f == x.intact.rect.width && Mathf.Approximately(x.intact.rect.width, cell) && Mathf.Approximately(x.frames[0].rect.width, cell);
            }
            Check(name + ": hit circles inside the " + cell + " px cell, pivot at the cell centre (art and break frames share one cell)", inside && pivot);

            if (w == 0) continue;   // Space: the user's in-place asteroids (no sheets), the benchmark of the others
            string dir = "Assets/Art/Resources/" + e.folder + "/";
            foreach (var f in new[] { "boulder.png", "break.png" })
            {
                int W, H;
                var px = ReadPng(dir + f, out W, out H);
                bool ok = f == "boulder.png" ? W == (int)cell * 3 && H == (int)cell : W == (int)cell * 5 && H == (int)cell * 3;
                var imp = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(dir + f);
                if (imp != null) Debug.Log("[COVER] import " + f + ": def " + imp.GetDefaultPlatformTextureSettings().maxTextureSize + " android " + imp.GetPlatformTextureSettings("Android").maxTextureSize + "/" + imp.GetPlatformTextureSettings("Android").format + " iphone " + imp.GetPlatformTextureSettings("iPhone").format + " mips " + imp.mipmapEnabled + " ok " + ok);
                Check(name + "/" + f + ": " + W + "x" + H + " is the full sheet; import not clamped, no mipmaps, ASTC 4x4 on phones",
                      ok && imp != null && imp.GetDefaultPlatformTextureSettings().maxTextureSize >= W && imp.GetPlatformTextureSettings("Android").maxTextureSize >= W && !imp.mipmapEnabled &&
                      imp.GetPlatformTextureSettings("Android").format == UnityEditor.TextureImporterFormat.ASTC_4x4 &&
                      imp.GetPlatformTextureSettings("iPhone").format == UnityEditor.TextureImporterFormat.ASTC_4x4);
                float worst = Mathf.Max(BlockScore(px, W, H, 2), Mathf.Max(BlockScore(px, W, H, 3), BlockScore(px, W, H, 4)));
                Check(name + "/" + f + ": real resolution, not a block upscale (block score " + worst.ToString("0.000") + " < .97)", worst < .97f);
                int pinks = 0, opaque = 0;
                for (int i = 0; i < px.Length; i += 3)
                    if (px[i].a > 8) { opaque++; if (w > 0 && WorldCue.IsPink(new Color32(px[i].r, px[i].g, px[i].b, 255))) pinks++; }
                bool pinkFree = pinks <= opaque * .002f;
                if (f == "boulder.png")
                {
                    // the rim: solid pixels with a clear pixel 4 px away; Storm has a pale one, every other world none
                    double rim = 0, body = 0; long nr = 0, nb = 0;
                    for (int y = 4; y < H - 4; y += 2)
                        for (int x = 4; x < W - 4; x += 2)
                        {
                            var c = px[y * W + x];
                            if (c.a <= 240) continue;
                            double l = .299 * c.r + .587 * c.g + .114 * c.b;
                            bool edge = px[y * W + x - 4].a < 128 || px[y * W + x + 4].a < 128 || px[(y - 4) * W + x].a < 128 || px[(y + 4) * W + x].a < 128;
                            if (edge) { rim += l; nr++; } else { body += l; nb++; }
                        }
                    double dl = nr > 0 && nb > 0 ? rim / nr - body / nb : 0;
                    Check(name + "/boulder.png: " + (e.outline ? "has its pale one-sided outline" : "has no outline or halo") + " (rim " + dl.ToString("+0;-0") + " luma vs body)",
                          e.outline ? dl >= 20 : dl <= 14);
                }
                Check(name + "/" + f + ": no pink outside Space", pinkFree);
            }
        }
    }

    // ---- BACKDROP RULES: a boulder only spawns where it blends into the backdrop variant in play (docs 13) ----

    static void BackdropRules()
    {
        Fresh();
        CoverBoulders.TestAnyWorld = false;
        bool all = true, notes = true, noneBlocked = true, onlyAllowed = true, determinism = true, someWhere = true;
        int noneCount = 0;
        for (int w = 0; w < CoverBoulderTable.Entries.Length; w++)
        {
            var e = CoverBoulderTable.Entries[w];
            if (!e.enabled) continue;
            someWhere &= e.Mask(0) != 0;
            for (int b = 1; b <= 4; b++)
            {
                int mask = e.Mask(b);
                notes &= !string.IsNullOrEmpty(e.notes[b - 1]);
                CoverBoulderDirector.BackdropVariantOverride = b;
                CoverBoulders.Clear();
                string why = CoverBoulderDirector.Blocked(w, 90f);
                if (mask == 0) { noneCount++; noneBlocked &= why == "backdrop"; }
                else
                {
                    if (why != null) { Debug.Log("[COVER] backdrop rule: " + CoverBoulderTable.Entries[w].world + " v" + b + " blocked by " + why); }
                    all &= why == null;
                    CoverBoulderDirector.WorldOverride = w;
                    var dir = new GameObject("~dir").AddComponent<CoverBoulderDirector>();
                    dir.Begin();
                    var seen = new List<int>();
                    for (int i = 0; i < 24; i++)
                    {
                        CoverBoulders.Clear();
                        var bd = dir.SpawnOne(w);
                        if (bd == null) { onlyAllowed = false; break; }
                        onlyAllowed &= (mask & (1 << bd.Variant)) != 0;
                        seen.Add(bd.Variant);
                    }
                    // determinism: the same seed gives the same sequence
                    var again = new List<int>();
                    Random.InitState(4242);
                    var dir2 = new GameObject("~dir2").AddComponent<CoverBoulderDirector>();
                    dir2.Begin();
                    Random.InitState(99); var first = new List<int>();
                    for (int i = 0; i < 8; i++) { CoverBoulders.Clear(); var bd = dir2.SpawnOne(w); first.Add(bd != null ? bd.Variant : -1); }
                    Object.DestroyImmediate(dir2.gameObject);
                    var dir3 = new GameObject("~dir3").AddComponent<CoverBoulderDirector>();
                    dir3.Begin();
                    Random.InitState(99);
                    for (int i = 0; i < 8; i++) { CoverBoulders.Clear(); var bd = dir3.SpawnOne(w); again.Add(bd != null ? bd.Variant : -1); }
                    Object.DestroyImmediate(dir3.gameObject);
                    determinism &= first.Count == again.Count;
                    for (int i = 0; i < first.Count && i < again.Count; i++) determinism &= first[i] == again[i];
                    Object.DestroyImmediate(dir.gameObject);
                    CoverBoulderDirector.WorldOverride = -1;
                }
            }
            // with no known backdrop variant (0: menus, tests) the union applies
            CoverBoulderDirector.BackdropVariantOverride = 0;
            CoverBoulders.Clear();
            string why0 = CoverBoulderDirector.Blocked(w, 90f);
            if (why0 != null) Debug.Log("[COVER] backdrop rule: " + e.world + " union blocked by " + why0);
            all &= why0 == null;
        }
        CoverBoulderDirector.BackdropVariantOverride = -1;
        CoverBoulders.Clear();
        CoverBoulders.TestAnyWorld = true;
        Check("every wired world allows at least one shape over at least one backdrop variant", someWhere);
        Check("every backdrop variant of a wired world either has allowed shapes (spawns, nothing else blocks) or is explicitly 'none' (" + noneCount + " none)", all);
        Check("a 'none' backdrop never spawns a boulder (Blocked == backdrop)", noneBlocked);
        Check("every wired world documents each backdrop variant (notes)", notes);
        Check("spawns only ever pick an allowed shape for the backdrop variant", onlyAllowed);
        Check("the choice of shape is deterministic for a seed", determinism);
    }

    // ---- EMERGE / RECEDE: out of the backdrop into the play layer with no pop ----

    static void Emerge()
    {
        Fresh();
        var b = CoverBoulders.Spawn(0, 1);
        var sr = b != null ? b.GetComponentOnBody() : null;
        Check("a fresh boulder starts fully faded and small (presence " + (b != null ? b.Presence.ToString("0.00") : "-") + ")", b != null && b.Presence < .02f);
        if (b == null) return;
        float prev = b.Presence, maxStep = 0f, prevScale = b.VisualScale, maxScaleStep = 0f, last = 0f;
        bool monotone = true;
        for (int i = 0; i < 160; i++)
        {
            b.Hold(new Vector2(0f, 0f), 0f, false);   // keeps it clear of the view bottom: only the emerge runs
            CoverBoulders.Step(Dt);
            b.Hold(new Vector2(0f, 0f), 0f, false);
            maxStep = Mathf.Max(maxStep, Mathf.Abs(b.Presence - prev));
            maxScaleStep = Mathf.Max(maxScaleStep, Mathf.Abs(b.VisualScale - prevScale));
            monotone &= b.Presence >= prev - 1e-4f;
            prev = b.Presence; prevScale = b.VisualScale; last = b.Presence;
        }
        Check("the emerge is continuous: opacity steps <= " + maxStep.ToString("0.000") + ", scale steps <= " + maxScaleStep.ToString("0.000") + " per frame, rising", maxStep < .03f && maxScaleStep < .02f && monotone);
        Check("after " + CoverBoulder.EmergeSeconds + " s it is fully in the play layer (presence " + last.ToString("0.00") + ", scale " + prevScale.ToString("0.00") + ")", last > .98f && Mathf.Abs(prevScale - 1f) < .02f);
        // recede: a boulder low in the view sinks back smoothly (no jump from the last frame in view)
        float vb = CameraFit.ViewBottom;
        float pp = -1f, maxRec = 0f;
        for (float y = vb + 6f; y > vb - .5f; y -= .02f)
        {
            b.Hold(new Vector2(0f, y), 0f, false);
            CoverBoulders.Step(.0001f);
            b.Hold(new Vector2(0f, y), 0f, false);
            if (pp >= 0f) maxRec = Mathf.Max(maxRec, Mathf.Abs(b.Presence - pp));
            pp = b.Presence;
        }
        Check("the recede is smooth (max step " + maxRec.ToString("0.000") + " per .02 u) and ends faint (" + pp.ToString("0.00") + ")", maxRec < .03f && pp < .5f);
        CoverBoulders.Clear();
    }

    // ---- LANDFORMS: every world but Space draws its boulder as DECORATIVE backdrop art that affects nothing ----

    static void Landforms()
    {
        Fresh();
        var T = CoverBoulderTable.Entries;
        bool flags = T[0].interactive;
        for (int w = 1; w < T.Length; w++) flags &= !T[w].interactive;
        Check("only Space is interactive cover; every other world's boulder is a decorative landform (one flag per world)", flags);
        // no built-up city, rig or industrial complex gets a landform (the per-variant table of docs section 13)
        bool cities = T[1].Mask(2) == 0 && T[1].Mask(3) == 0 && T[2].Mask(3) == 0 && T[3].Mask(2) == 0 && T[4].Mask(3) == 0 && T[6].Mask(2) == 0 && T[6].Mask(3) == 0;
        Check("none on the cities / industrial variants: Frost v2 v3, Verdant v3, Ember v2, Tide v2 v3 (oil rigs, sunken city), Desert v2 v3", cities);

        for (int w = 1; w < T.Length; w++)
        {
            Fresh();
            var b = CoverBoulders.Spawn(w, 1);
            string nm = T[w].world;
            if (b == null) { Check(nm + ": a landform spawns", false); continue; }
            b.Hold(new Vector2(.4f, 2f), 0f);
            var body = Hazard(.4f, 2f, "Enimey", .3f);
            var at = body.transform.position;
            var shot = Fake(new Vector2(.4f, 3.5f));
            Vector2 hit;
            bool inert = b.Alive && b.Decorative && !b.Solid && !CoverBoulders.Any && CoverBoulders.SolidCount == 0 && CoverBoulders.Alive == 0 && !CoverBoulders.All.GetEnumerator().MoveNext();
            inert &= !CoverBoulders.Inside(new Vector2(.4f, 2f), .3f) && !CoverBoulders.StopsShot(new Vector2(.4f, 2f), .2f);
            inert &= !CoverBoulders.StopsPlayerShot(new Vector2(.4f, -3f), new Vector2(.4f, 6f), .2f, out hit);
            inert &= !CoverBoulders.Blocked(new Vector2(.4f, 6f), new Vector2(.4f, -3f)) && Mathf.Approximately(CoverBoulders.ClipUp(new Vector2(.4f, -3f), 8f, .3f), 8f);
            inert &= (CoverBoulders.ResolveShip(new Vector2(.4f, 2f)) - new Vector2(.4f, 2f)).sqrMagnitude < 1e-6f;
            inert &= !CoverBoulders.SiteCovered(new Vector2(.4f, 2f)) && !CoverBoulders.ShoveMeet(body.transform, .3f, Vector2.down * .1f, Dt);
            CoverBoulders.PushBodies();
            inert &= (body.transform.position - at).sqrMagnitude < 1e-8f;
            Check(nm + ": the landform blocks nothing (shots, beams, line of sight, ship, bodies, shoves, landing sites, hazard cover query) and pushes nothing", inert);
            b.Hold(new Vector2(.4f, 2f), 0f);
            Check(nm + ": it is bigger than a cover boulder and inside the lane (" + b.Width.ToString("0.0") + " u of " + (CoverBoulders.Edge * 2f).ToString("0.0") + ")", b.Width > T[w].widths[1] * 1.2f - .01f && b.Width <= CoverBoulders.Edge * 2f * CoverBoulders.DecorMaxShare + .001f);
            Check(nm + ": it has no motion of its own (the ground carries it; " + b.Vel + " u/s)", b.Vel == Vector2.zero && b.Rotation == 0f);
        }

        // a boss starting: a landform sinks out (no rumble, no crumble, nothing destroyed); Space's cover boulder is destroyed as before
        Fresh();
        int before = CoverBoulders.Destroyed;
        var f = CoverBoulders.Spawn(1, 0);
        f.Hold(new Vector2(0f, 1f), 0f);
        int n = CoverBoulders.OnBossBegin();
        Check("a boss starting fades the landform out (state " + f.State + ", destroyed " + (CoverBoulders.Destroyed - before) + ")", n == 0 && f.State == CoverBoulder.Phase.Fade && CoverBoulders.Destroyed == before);
        float tt = 0f;
        while (f.Alive && tt < 4f) { CoverBoulders.Step(Dt); tt += Dt; }
        Check("... and is gone, effects included, in " + tt.ToString("0.0") + " s", !f.Alive && CoverBoulders.AliveAny == 0);
        var sp = CoverBoulders.Spawn(0, 0);
        sp.Hold(new Vector2(0f, 1f), 0f);
        Check("Space's cover boulder still blocks and is still destroyed by a boss", CoverBoulders.Any && CoverBoulders.OnBossBegin() == 1 && sp.State == CoverBoulder.Phase.Rumble);

        // the Storm effects layer: pooled, no allocations, follows the boulder, hidden the moment it fades or is released, no pink
        Fresh();
        var st = CoverBoulders.Spawn(5, 1);
        st.Hold(new Vector2(0f, 2f), 0f);
        for (int i = 0; i < 30; i++) CoverBoulders.Step(Dt);
        var fxRoot = st.StormFxRoot;
        Check("Storm: the effects layer exists (" + (fxRoot != null ? fxRoot.GetComponentsInChildren<SpriteRenderer>(true).Length.ToString() : "-") + " pooled renderers) and is showing", fxRoot != null && fxRoot.gameObject.activeSelf && fxRoot.GetComponentsInChildren<SpriteRenderer>(true).Length == 31);
        System.Action work = () => { for (int i = 0; i < 120; i++) CoverBoulders.Step(Dt); };
        work();
        long bytes = TestHarness.AllocatedBytes(work);
        Check("Storm: 120 frames with every wisp, streak, pellet and arc moving allocate " + bytes + " bytes", bytes == 0);
        CoverBoulders.OnBossBegin();
        for (int i = 0; i < 60 * 2.2f; i++) CoverBoulders.Step(Dt);
        Check("Storm: no wisp, streak or arc is left after the boss fade", !st.Alive && !fxRoot.gameObject.activeSelf);
        var st2 = CoverBoulders.Spawn(5, 0);
        for (int i = 0; i < 90; i++) CoverBoulders.Step(Dt);
        CoverBoulders.Clear();
        Check("Storm: released with its effects (a despawn leaves nothing)", !st2.Alive && !st2.StormFxRoot.gameObject.activeSelf);
        int W, H;
        var fxpx = ReadPng("Assets/Art/Resources/Boulders/Storm/fx.png", out W, out H);
        int pk = 0, op = 0;
        for (int i = 0; i < fxpx.Length; i += 2) if (fxpx[i].a > 8) { op++; if (WorldCue.IsPink(new Color32(fxpx[i].r, fxpx[i].g, fxpx[i].b, 255))) pk++; }
        Check("Storm: the effects atlas is " + W + "x" + H + ", real resolution, no pink (" + pk + " of " + op + ")", W == 1024 && H == 1024 && pk == 0 && BlockScore(fxpx, W, H, 2) < .97f);
    }

    // ---- lanes: the free-reach band, every shape ----

    // (reach h of the ship's centre, rail face)
    static readonly Vector2[] LaneShapes =
    {
        new Vector2(2.0f, 2.45f), new Vector2(2.21f, 2.61f), new Vector2(2.32f, 2.75f), new Vector2(2.45f, 2.9f), new Vector2(2.6f, 3.2f),
    };

    static void Lanes()
    {
        Fresh();
        var art = CoverBoulderArt.For(0);
        float hull = CoverBoulders.HullRadius;
        int bandBad = 0, freeBad = 0, railBad = 0, wideBad = 0, n = 0;
        foreach (var lane in LaneShapes)
        {
            float h = lane.x, edge = lane.y;
            foreach (var e in CoverBoulderTable.Entries)
                for (int v = 0; v < 3; v++)
                {
                    var cv = art[v];
                    float boundPx = 0f;
                    foreach (var c in cv.circles) boundPx = Mathf.Max(boundPx, new Vector2(c.x, c.y).magnitude + c.r);
                    float width = e.widths[v];
                    width *= CoverBoulders.FitScale(width, boundPx / cv.artWidth, h, hull, edge);
                    float bound = boundPx * width / cv.artWidth;
                    if (width > edge * 2f * CoverBoulders.MaxScreenShare + 1e-3f) wideBad++;
                    foreach (int side in new[] { -1, 1 })
                    {
                        float lo, hi;
                        CoverBoulders.Band(side, bound, h, hull, edge, out lo, out hi);
                        n++;
                        if (lo > hi + 1e-4f) bandBad++;
                        for (int s = 0; s <= 4; s++)
                        {
                            float cx = Mathf.Lerp(lo, hi, s / 4f);
                            // both sides keep SideGap to the rail face, rotation or not (bound is rotation-proof): a hull fits beside it
                            float gap = edge - (Mathf.Abs(cx) + bound);
                            if (gap < CoverBoulders.SideGap - .005f) freeBad++;
                            if (gap < 2f * hull - .005f) railBad++;
                        }
                    }
                }
        }
        Check("every world's every boulder, on six lane shapes (reach 1.5-2.6): the band is never empty (" + bandBad + " of " + n + " bad)", bandBad == 0);
        Check("... both sides keep " + CoverBoulders.SideGap + " u between the boulder and the rail face (" + freeBad + " bad)", freeBad == 0);
        Check("... which always fits a hull (" + (2f * hull).ToString("F2") + " u) at the rail: nothing is ever squeezed against a rail (" + railBad + " bad)", railBad == 0);
        Check("... and the art is at most " + CoverBoulders.MaxScreenShare + " of the rail-to-rail width (" + wideBad + " bad)", wideBad == 0);
        // the widest table entry is not wider than the brief's 3-5 enemies at 0.7 u
        float widest = 0f;
        foreach (var e in CoverBoulderTable.Entries) foreach (float x in e.widths) widest = Mathf.Max(widest, x);
        Check("boulders are 2.0-3.0 u wide (3-5 enemies; the widest " + widest.ToString("F1") + ")", widest <= 3.0f && widest >= 2.8f);
    }

    // ---- the ship is blocked, never trapped ----

    static void Safety()
    {
        Fresh();
        float hull = CoverBoulders.HullRadius;
        int bad = 0, tried = 0, outOfBox = 0, notFree = 0;
        foreach (var lane in LaneShapes)
        {
            float h = lane.x, edge = lane.y;
            float yb = -4.2f, yt = 4.6f;
            for (int variant = 0; variant < 3; variant++)
                foreach (int side in new[] { -1, 1 })
                    foreach (float rot in new[] { 0f, 37f, 90f, 143f, 200f, 281f })
                    {
                        CoverBoulders.Clear();
                        var b = Place(variant, Vector2.zero, rot);
                        // the boulder at its two band extremes and the middle, at three heights (low: against the floor; mid; high: at the ceiling)
                        float lo, hi;
                        float bound = b.Bound;
                        CoverBoulders.Band(side, bound, h, hull, edge, out lo, out hi);
                        foreach (float cx in new[] { lo, (lo + hi) * .5f, hi })
                            foreach (float cy in new[] { -3.6f, 0f, 3.8f })
                            {
                                b.Hold(new Vector2(cx, cy), rot);
                                for (float fx = -h; fx <= h + 1e-3f; fx += .2f)
                                    for (float fy = yb; fy <= yt + 1e-3f; fy += .45f)
                                    {
                                        tried++;
                                        // (the previous ship position is the finger's: no hint)
                                        var p = CoverBoulders.Resolve(new Vector2(fx, fy), hull, -h, h, yb, yt, new Vector2(fx, fy));
                                        if (p.x < -h - 1e-3f || p.x > h + 1e-3f || p.y < yb - 1e-3f || p.y > yt + 1e-3f) outOfBox++;
                                        bool inside = false;
                                        for (int i = 0; i < b.Count; i++)
                                            if ((p - b.CircleCentre(i)).magnitude < b.CircleRadius(i) + hull - .01f) inside = true;
                                        if (inside) bad++;
                                        // a finger already clear of the boulder is never moved
                                        bool clear = true;
                                        for (int i = 0; i < b.Count; i++)
                                            if ((new Vector2(fx, fy) - b.CircleCentre(i)).magnitude < b.CircleRadius(i) + hull + .001f) clear = false;
                                        if (clear && (p - new Vector2(fx, fy)).sqrMagnitude > 1e-6f) notFree++;
                                    }
                            }
                    }
        }
        Check("a grid of " + tried + " fingers x boulders (3 variants, 6 turns, both sides, 3 spots, 6 lanes): every ship position is outside every circle (" + bad + " inside)", bad == 0);
        Check("... and inside the ship's reach (" + outOfBox + " outside)", outOfBox == 0);
        Check("... and a finger already clear of the boulder is not touched (" + notFree + " moved)", notFree == 0);

        // slide: a finger dragged straight up the boulder's face moves the ship along the face, continuously, never through
        Fresh();
        var bd = Place(2, new Vector2(.4f, 0f));
        float maxStep = 0f, minDist = float.MaxValue;
        Vector2 prev = new Vector2(.4f, -4f);
        for (float y = -4f; y <= 4f; y += .02f)
        {
            var p = CoverBoulders.ResolveStep(new Vector2(.4f, y), prev, new Vector2(.4f, y - .02f), hull, -2.3f, 2.3f, -4.2f, 4.6f, Dt);
            maxStep = Mathf.Max(maxStep, (p - prev).magnitude);
            for (int i = 0; i < bd.Count; i++) minDist = Mathf.Min(minDist, (p - bd.CircleCentre(i)).magnitude - bd.CircleRadius(i));
            prev = p;
        }
        Check("dragging the finger up through the boulder's middle slides the hull round it at <= " + CoverBoulders.SlideSpeed + " u/s: steps <= " + maxStep.ToString("F2") + " u a frame, never inside (" + minDist.ToString("F2") + ")",
              maxStep <= CoverBoulders.SlideSpeed * Dt + .03f && minDist >= hull - .01f);
        // no damage: the ship never takes a hit from touching (a boulder is not a hazard)
        var b0 = bd;
        Check("a boulder is no hazard body: no collider, no hazard tag, not a ClearTarget, no hit interface",
              b0 != null && !HasHazardParts(CoverBoulders.Root));
    }

    static bool HasHazardParts(Transform root)
    {
        foreach (var c in root.GetComponentsInChildren<Collider2D>(true)) return true;
        foreach (var c in root.GetComponentsInChildren<ClearTarget>(true)) return true;
        foreach (var m in root.GetComponentsInChildren<MonoBehaviour>(true)) if (m is IShipAttackTarget) return true;
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.CompareTag("Enimey") || t.CompareTag("Astr")) return true;
        return false;
    }

    // A boulder falls through the lane (hugging each side) onto a still finger: the ship never leaves the safe set, never jumps, is never pinned.
    static void DynamicSafety()
    {
        float hull = CoverBoulders.HullRadius;
        int worst = 0; float maxJump = 0f; string worstInfo = ""; int invalid = 0, runs = 0, pinned = 0;
        foreach (var lane in new[] { LaneShapes[0], LaneShapes[1], LaneShapes[3] })
        {
            float h = lane.x, edge = lane.y;
            for (int variant = 0; variant < 3; variant++)
                foreach (int side in new[] { -1, 1 })
                    foreach (float fingerX in new[] { -h, -1f, 0f, .7f, h })
                        foreach (float fingerY in new[] { -4.2f, -2f, 1f })
                        {
                            Fresh();
                            var b = Place(variant, Vector2.zero);
                            float lo, hi;
                            CoverBoulders.Band(side, b.Bound, h, hull, edge, out lo, out hi);
                            float cx = side > 0 ? hi : lo;   // hugging a rail (the narrowest gap)
                            Vector2 finger = new Vector2(fingerX, fingerY);
                            Vector2 prev = finger;
                            float y = 7f;
                            bool first = true;
                            runs++;
                            while (y > -7.5f)
                            {
                                y -= 1.1f * Dt;
                                b.Hold(new Vector2(cx, y));
                                var p = CoverBoulders.ResolveStep(finger, prev, finger, hull, -h, h, -4.2f, 4.6f, Dt);
                                bool inside = false;
                                for (int i = 0; i < b.Count; i++) if ((p - b.CircleCentre(i)).magnitude < b.CircleRadius(i) + hull - .01f) inside = true;
                                if (inside || p.x < -h - 1e-3f || p.x > h + 1e-3f) { invalid++; worst++; }
                                if (!first) { float j = (p - prev).magnitude; if (j > maxJump) { maxJump = j; worstInfo = "lane " + h + " v" + variant + " side " + side + " finger " + finger + " boulder y " + y.ToString("F2") + " cx " + cx.ToString("F2") + " from " + prev + " to " + p; } }
                                first = false;
                                prev = p;
                            }
                            // when it has gone by the finger is not displaced any more
                            if ((prev - finger).sqrMagnitude > 1e-6f) pinned++;
                        }
        }
        Check("a boulder falling at 1.1 u/s onto a still finger (" + runs + " runs: 3 lanes, 3 variants, both sides, 15 finger spots): the ship is always out of it and in reach (" + invalid + " bad frames)", invalid == 0);
        Check("... it is moved smoothly, never thrown: the largest step in a frame is " + maxJump.ToString("F2") + " u (limit .6: the chord a boulder opens on a ship squeezed against the floor grows like a square root at first touch) " + worstInfo, maxJump <= .6f);
        Check("... and when the boulder has passed the ship is back under the finger (" + pinned + " left displaced)", pinned == 0);
    }

    // ---- hostile shots (elite / boss pools) ----

    sealed class FakeShot : MonoBehaviour, IHostileShot
    {
        public Vector2 pos; public float radius = .1f; public int mass = HostileShots.Light; public bool popped;
        public bool ShotCollidable => !popped;
        public Vector2 ShotPosition => pos;
        public float ShotRadius => radius;
        public int ShotOwner => 7;
        public float ShotAge => 1f;
        public int ShotMass => mass;
        public Color ShotTint => Color.white;
        public void ShotPop(Vector2 at) { popped = true; }
        public bool ShotShovable => true;
        public void ShotShove(Vector2 by) { pos += by; }
    }

    static FakeShot Fake(Vector2 at, int mass = HostileShots.Light)
    {
        var go = new GameObject("~fakeShot");
        var f = go.AddComponent<FakeShot>();
        f.pos = at; f.mass = mass;
        HostileShots.Register(f);
        return f;
    }

    static void HostileShotsBlocked()
    {
        Fresh();
        var b = Place(0, new Vector2(0f, 1f));
        Vector2 inside = b.CircleCentre(0);
        var light = Fake(inside); var heavy = Fake(inside + Vector2.right * .1f, HostileShots.Heavy); var fixedPool = Fake(inside + Vector2.left * .1f, HostileShots.Fixed);
        var outside = Fake(new Vector2(0f, Top(b) + 1f));
        int before = CoverBoulders.ShotsBlocked;
        HostileShots.Resolve();
        Check("light, heavy and fixed hostile shots touching a boulder are all popped (" + (CoverBoulders.ShotsBlocked - before) + ")", light.popped && heavy.popped && fixedPool.popped);
        Check("a shot clear of the boulder is untouched", !outside.popped);
        Check("the boulder is not harmed", b.State == CoverBoulder.Phase.Live && b.Count > 0);

        // the real thing: an elite's bolt fired at a pilot hiding behind a boulder
        Fresh();
        var pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -3f, 0f);
        EliteSystem.PlayerOverride = pilot;
        EliteDef def = null;
        foreach (var d in EliteCatalog.All) { def = d; break; }
        var e = EliteShip.CreateInPlay(def, new Vector2(0f, 4f));
        e.AttackCooldown = 99f;
        Place(0, new Vector2(0f, 0f));
        var shot = EliteSystem.Shots.Fire(e, e.Def, EliteShots.Kind.Bolt, new Vector2(0f, 2.4f), Vector2.down * 6f);
        float lowest = 9f;
        for (int i = 0; i < 90; i++) { EliteSystem.Step(Dt); if (shot.Active) lowest = Mathf.Min(lowest, shot.transform.position.y); }
        Check("an elite bolt fired down the lane at a pilot behind a boulder dies on its face (lowest " + lowest.ToString("F2") + " vs the boulder's top " + Top(CoverBoulders.First).ToString("F2") + ")",
              !shot.Active && lowest > Bottom(CoverBoulders.First));
        CoverBoulders.Clear();
        var shot2 = EliteSystem.Shots.Fire(e, e.Def, EliteShots.Kind.Bolt, new Vector2(0f, 2.4f), Vector2.down * 6f);
        float low2 = 9f;
        for (int i = 0; i < 90; i++) { EliteSystem.Step(Dt); if (shot2.Active) low2 = Mathf.Min(low2, shot2.transform.position.y); }
        Check("(control: with no boulder the same bolt flies on down the lane to y " + low2.ToString("F2") + ")", low2 < -1.5f || !shot2.Active && low2 < 0f);
    }

    // ---- the player's weapons ----

    static ShipPowerController Ship(int id, float y = -3f)
    {
        PlayerPrefs.SetInt("spawnShip", id);
        var go = new GameObject("ship" + id, typeof(SpriteRenderer));
        go.transform.position = new Vector3(0f, y, 0f);
        var c = go.AddComponent<ShipPowerController>();
        c.SendMessage("Awake");
        c.SendMessage("Start");
        var gun = go.GetComponentInChildren<UltimateGun>();
        if (gun != null) gun.SendMessage("Awake");
        return c;
    }

    static void Teardown(ShipPowerController c)
    {
        if (c == null) return;
        c.SendMessage("OnDestroy");
        if (c.Runner != null) c.Runner.SendMessage("OnDestroy");
        if (c.Secret != null) c.Secret.SendMessage("OnDestroy");
        Object.DestroyImmediate(c.gameObject);
        AttackPool.StopAll();
    }

    static void Steps(ShipAttackRunner r, float seconds, float dt = .02f)
    {
        for (float t = 0f; t < seconds; t += dt) r.Step(dt);
    }

    // Fires ship `id`'s main attack at a hazard hidden behind a boulder (and, as the control, with no boulder). True if the hazard died.
    static bool Fired(int id, bool withBoulder, out CoverBoulder boulder, out Vector2 posBefore)
    {
        Fresh();
        WorldTimeFx.Reset();
        var l = ShipLoadoutTable.For(id);
        var c = Ship(id);
        Vector2 nose = c.Runner.Nose;
        GameObject hz;
        boulder = null; posBefore = Vector2.zero;
        if (l.attack == ShipAttack.SkyJavelins)
        {
            // they fall from above: the boulder is over the hazard
            if (withBoulder) { boulder = Place(0, new Vector2(0f, 1.2f)); posBefore = boulder.Pos; }
            hz = Hazard(0f, withBoulder ? Bottom(boulder) - .45f : -.8f);
        }
        else if (l.attack == ShipAttack.OrbitDisc)
        {
            // the disc starts straight above the ship at its orbit radius
            Vector2 start = new Vector2(0f, -3f + l.width);
            if (withBoulder) { boulder = Place(0, start); posBefore = boulder.Pos; }
            hz = Hazard(start.x, start.y);
        }
        else
        {
            if (withBoulder)
            {
                boulder = Place(0, Vector2.zero);
                boulder.Hold(new Vector2(0f, nose.y + .4f - Bottom(boulder)));
                posBefore = boulder.Pos;
            }
            float top = withBoulder ? Top(boulder) : nose.y + 2.3f;
            hz = Hazard(0f, top + .45f);
        }
        c.Runner.Fire();
        Steps(c.Runner, 3f);
        bool died = hz == null;
        if (hz != null) Object.DestroyImmediate(hz);
        Teardown(c);
        return died;
    }

    static void PlayerWeapons()
    {
        int tested = 0;
        foreach (int id in ShipId.All)
        {
            var l = ShipLoadoutTable.For(id);
            if (l.attack == ShipAttack.ScreenClear) continue;   // the cinematic volley passes (ShootThroughFlags)
            CoverBoulder b; Vector2 p0;
            bool control = Fired(id, false, out b, out p0);
            bool blockedDied = Fired(id, true, out b, out p0);
            bool unchanged = b != null && b.State == CoverBoulder.Phase.Live && (b.Pos - p0).sqrMagnitude < 1e-6f && b.Count > 0;
            Check("ship " + id + " " + l.attack + ": the control (no boulder) kills the hazard; behind a boulder it survives, and the boulder is unchanged",
                  control && !blockedDied && unchanged);
            tested++;
        }
        Check("every ship's main attack but the three cinematic ones was tried (" + tested + ")", tested == ShipId.Count - ShipLoadoutTable.TopTierCount);

        // beams are cut at the face (not only blocked): a hazard in front of the boulder, in the beam's line, still dies
        Fresh();
        var rail = Ship(4);
        Vector2 nose = rail.Runner.Nose;
        var bd = Place(0, Vector2.zero);
        bd.Hold(new Vector2(0f, nose.y + 3.4f - Bottom(bd)));
        var front = Hazard(0f, nose.y + 1.4f);
        var behind = Hazard(0f, Top(bd) + .6f);
        rail.Runner.Fire();
        Check("the rail gun is cut at the boulder: a hazard before the face still dies, one behind it does not", front == null && behind != null);
        if (behind != null) Object.DestroyImmediate(behind);
        Teardown(rail);
    }

    // Decision record: the cinematic ultimate and the secret powers pass over cover (docs section 4); nothing in them consults CoverBoulders.
    static void ShootThroughFlags()
    {
        string root = "Assets/Scripts/Gameplay/Weapons/";
        bool ultimate = !System.IO.File.ReadAllText(root + "UltimateGun.cs").Contains("CoverBoulders") &&
                        !System.IO.File.ReadAllText(root + "SecretPowers.cs").Contains("CoverBoulders") &&
                        !System.IO.File.ReadAllText(root + "ShipPowerController.cs").Contains("CoverBoulders");
        Check("the ultimate and the secret powers pass over cover (documented): they never consult CoverBoulders", ultimate);
        // and the shot-down / clear helpers of the shield pulse and EMP leave a boulder alone
        Fresh();
        var b = Place(1, new Vector2(.5f, 0f));
        var s = Fake(new Vector2(.5f, Top(b) + .6f));
        HostileShots.ClearWithin(s.pos, 3f);
        HostileShots.ClearIn(new Rect(-5f, -5f, 10f, 10f));
        Check("clearing every shot in view (shield pulse, EMP) does not touch the boulder", b.State == CoverBoulder.Phase.Live && s.popped);
        var sw = ShieldShockwave.Release(new Vector2(.5f, Bottom(b) - .9f), .3f);
        Check("the shield-burst shockwave does not touch it either", b.State == CoverBoulder.Phase.Live && b.Count > 0);
    }

    // ---- bodies ----

    static void Bodies()
    {
        Fresh();
        var b = Place(2, new Vector2(.3f, 0f));
        var rock = Hazard(b.CircleCentre(0).x + .1f, b.CircleCentre(0).y, "Astr", .4f);
        var ship = Hazard(-.2f, b.CircleCentre(0).y - .2f, "Enimey", .35f);
        var pick = Hazard(.5f, 1.1f, "pickUp", .15f);
        var far = Hazard(-2.2f, 3.5f);
        Vector3 farBefore = far.transform.position;
        CoverBoulders.PushBodies();
        bool out1 = !Inside(b, rock.transform.position, .4f * CoverBoulders.BodyShare - .01f) &&
                    !Inside(b, ship.transform.position, .35f * CoverBoulders.BodyShare - .01f) &&
                    !Inside(b, pick.transform.position, .15f * CoverBoulders.BodyShare - .01f);
        Check("an enemy, a rock and an atom inside a boulder are pushed out of it", out1);
        Check("a body clear of it is not moved", far.transform.position == farBefore);

        // a falling body slides round it (its mover keeps pulling it down; the push keeps it out)
        var faller = Hazard(b.CircleCentre(0).x + .05f, Top(b) + 1.5f, "Enimey", .35f);
        float minD = float.MaxValue; float lastX = faller.transform.position.x;
        for (int i = 0; i < 300; i++)
        {
            faller.transform.position += Vector3.down * 8f * Dt;
            CoverBoulders.PushBodies();
            minD = Mathf.Min(minD, DistToShape(b, faller.transform.position));
        }
        Check("a body falling at 8 u/s down onto it slides round it and never goes through (closest " + minD.ToString("F2") + " u of the face)", minD >= .35f * CoverBoulders.BodyShare - .02f);
        Check("... and ends up below the boulder, to a side", faller.transform.position.y < Bottom(b));

        // things that are not pushed: a parked elite, a rail mine's mount, the boss
        Fresh();
        var b2 = Place(2, new Vector2(.3f, 0f));
        var mine = Hazard(b2.CircleCentre(0).x, b2.CircleCentre(0).y, "Enimey");
        mine.AddComponent<RailMineMount>();
        CoverBoulders.PushBodies();
        Check("a rail mine (it rides its rail, which no boulder covers) is left alone", mine.transform.position == new Vector3(b2.CircleCentre(0).x, b2.CircleCentre(0).y, 0f));
        var hb = Hazard(b2.CircleCentre(0).x, b2.CircleCentre(0).y, "Enimey");
        hb.AddComponent<BossTarget>();
        CoverBoulders.PushBodies();
        Check("the boss's body is left alone", hb.transform.position == new Vector3(b2.CircleCentre(0).x, b2.CircleCentre(0).y, 0f));
        Object.DestroyImmediate(mine); Object.DestroyImmediate(hb);
        EliteDef def = null;
        foreach (var d in EliteCatalog.All) { def = d; break; }
        var elite = EliteShip.CreateInPlay(def, b2.CircleCentre(0));
        CoverBoulders.PushBodies();
        Check("an elite in play is pushed out like any body", !Inside(b2, elite.transform.position, def.hullRadius * .5f));
    }

    static bool Inside(CoverBoulder b, Vector2 p, float r) { return b.Touches(p, r); }

    static float DistToShape(CoverBoulder b, Vector2 p)
    {
        float m = float.MaxValue;
        for (int i = 0; i < b.Count; i++) m = Mathf.Min(m, (p - b.CircleCentre(i)).magnitude - b.CircleRadius(i));
        return m;
    }

    // ---- shoved bodies ----

    static void Shoves()
    {
        for (int pass = 0; pass < 2; pass++)
        {
            Fresh();
            EnemyShove.Clear();
            ShoveCrash.RollOverride = () => pass == 0 ? 0f : 1f;   // pass 0: every roll succeeds; pass 1: every roll fails
            var b = Place(2, new Vector2(.3f, 0f));
            var body = Hazard(-2f, 0f, "Enimey", .35f);
            body.AddComponent<EnemyIdentity>();
            int kills = ShoveCrash.RailKills, crash = CoverBoulders.CrashKills;
            // flung hard at the boulder: 3.2 u in .28 s is ~15 u/s at the start of the ease-out
            EnemyShove.Add(body.transform, null, new Vector2(3.4f, 0f), .35f, ShieldShockwave.PushSeconds);
            for (int i = 0; i < 40; i++) EnemyShove.Step(Dt);
            if (pass == 0)
                Check("a body shoved hard into a boulder crashes there by the rail-crash roll: dead, the pilot's kill (" + (CoverBoulders.CrashKills - crash) + ")",
                      body == null && CoverBoulders.CrashKills == crash + 1 && ShoveCrash.RailKills == kills + 1);
            else
                Check("a survivor of the roll scrapes the face and is pushed out, alive and outside the boulder",
                      body != null && CoverBoulders.CrashKills == crash && !Inside(b, body.transform.position, .35f * CoverBoulders.BodyShare - .01f));
            if (body != null) Object.DestroyImmediate(body);
        }
        ShoveCrash.RollOverride = null;
        // the wave itself is blocked by a boulder between the ship and the body
        Fresh();
        EnemyShove.Clear();
        var bd = Place(2, new Vector2(0f, -.3f));
        var hidden = Hazard(0f, Top(bd) + .4f, "Enimey", .3f);
        var open = Hazard(1.6f, -3.5f, "Enimey", .3f);
        Vector2 ship = new Vector2(0f, Bottom(bd) - .5f);
        Vector3 hiddenAt = hidden.transform.position;
        ShieldShockwave.Release(ship, .3f);
        Check("the shield-burst shockwave does not reach a body hidden behind a boulder (" + EnemyShove.Active + " shoved)", hidden.transform.position == hiddenAt && !EnemyShove.IsShoved(hidden.transform));
        EnemyShove.Clear();
    }

    // ---- line of sight: beams and area hazards ----

    static void LineOfSight()
    {
        Fresh();
        var rig = new DeathCrashTest.Rig(1);
        try
        {
            var ship = rig.ship.transform;
            collisionDetection.MAXLIFE = 5;
            EliteSystem.PlayerOverride = ship;
            ship.position = new Vector3(0f, -3f, 0f);
            // a strike column in the ship's lane, shot from above (the shooter is at y 4): with a boulder between, the hit is ignored
            var shooter = new GameObject("~shooter").transform;
            shooter.position = new Vector3(0f, 4f, 0f);
            var b = Place(0, new Vector2(0f, 0f));
            var s = AttackStrike.Arm(StrikeSpec.Standard(1), 0f, -2.5f, 1f, shooter.gameObject);
            s.Ignite();
            for (int i = 0; i < 3; i++) AttackPools.StepAll(Dt);
            collisionDetection.lifeCounter = 0; PlayerInvuln.Reset();
            int los = CoverBoulders.LosBlocked;
            Check("the hazard's hitbox origin is its shooter", CoverBoulders.ShadowsHit(s.Hitbox, ship.position) && CoverBoulders.LosBlocked == los + 1);
            rig.Hit(s.Hitbox);
            Check("a strike whose shooter is hidden from the ship by a boulder does not land (hearts lost " + collisionDetection.lifeCounter + ")", collisionDetection.lifeCounter == 0);
            CoverBoulders.Clear();
            rig.Hit(s.Hitbox);
            Check("(control: the same hit with no boulder costs a heart: " + collisionDetection.lifeCounter + ")", collisionDetection.lifeCounter == 1);
            s.Cancel();
            Object.DestroyImmediate(shooter.gameObject);
        }
        finally { rig.Dispose(); }
        // pure line tests
        Fresh();
        var bd = Place(0, new Vector2(0f, 0f));
        Check("a line through a boulder is blocked, one beside it is not",
              CoverBoulders.Blocked(new Vector2(0f, 3f), new Vector2(0f, -3f)) && !CoverBoulders.Blocked(new Vector2(2.2f, 3f), new Vector2(2.2f, -3f)));
        Check("a line that ends before the boulder is not blocked", !CoverBoulders.Blocked(new Vector2(0f, 3f), new Vector2(0f, Top(bd) + .05f)));
    }

    // ---- a boss arrives ----

    static readonly Dictionary<CoverVariant[], Sprite[][]> realFrames = new Dictionary<CoverVariant[], Sprite[][]>();

    static void BossArrival()
    {
        foreach (bool drawn in new[] { false, true })
        {
            Fresh();
            var art = CoverBoulderArt.For(0);
            // the approved strip is wired: the drawn case plays it; the procedural case (a world with no strip) is forced by taking it away
            if (!realFrames.ContainsKey(art)) { realFrames[art] = new Sprite[3][]; for (int i = 0; i < 3; i++) realFrames[art][i] = art[i].frames; }
            for (int i = 0; i < 3; i++)
                art[i].frames = drawn ? (realFrames[art][i] ?? new[] { art[i].intact, art[i].intact, art[i].intact, art[i].intact, art[i].intact }) : null;
            var b = Place(1, new Vector2(.4f, 1f));
            bool wasDrawn = b.DrawnFracture;
            int destroyed = CoverBoulders.Destroyed;
            Check((drawn ? "drawn" : "procedural") + ": the boulder is solid and live before the boss (" + b.State + ")", b.State == CoverBoulder.Phase.Live && b.Solid);
            bool began = BossEncounter.Begin(3, null);
            Check("a boss beginning (BossEncounter.Setup) starts the destruction of every boulder (" + began + ")",
                  began && b.State == CoverBoulder.Phase.Rumble && CoverBoulders.Destroyed == destroyed + 1);
            Check("... which is still solid through the rumble and crack, not a hazard, and plays " + (wasDrawn ? "the drawn frames" : "the procedural fracture"), b.Solid && b.DrawnFracture == wasDrawn);
            float t = 0f; bool burstSeen = false, solidAtBurst = false; int fxMax = 0; var seen = new HashSet<CoverBoulder.Phase>();
            while (b.Alive && t < 5f)
            {
                CoverBoulders.Step(Dt); t += Dt;
                seen.Add(b.State);
                if (b.State == CoverBoulder.Phase.Burst) { burstSeen = true; solidAtBurst |= b.Solid; fxMax = Mathf.Max(fxMax, CoverBoulders.Fx.Live); }
            }
            Check("the sequence runs rumble -> crack -> burst -> gone in " + t.ToString("F2") + " s (about 1.4 s)",
                  burstSeen && seen.Contains(CoverBoulder.Phase.Rumble) && seen.Contains(CoverBoulder.Phase.Crack) && !b.Alive &&
                  t > CoverBoulder.RumbleSeconds + CoverBoulder.CrackSeconds + CoverBoulder.BurstSeconds - .1f && t < 1.7f);
            Check("the burst is not solid and throws world-coloured debris (" + fxMax + " pixels)", !solidAtBurst && fxMax >= 10);
            Check("with the boulder gone nothing blocks any more", !CoverBoulders.Any && CoverBoulders.Alive == 0);
            Check("no boulder spawns while a boss is up", CoverBoulderDirector.Blocked(0, 90f) == "boss");
            var dir = new GameObject("~dir").AddComponent<CoverBoulderDirector>();
            dir.Begin();
            CoverBoulderDirector.WorldOverride = 0;
            for (int i = 0; i < 6000; i++) { dir.Tick(.1f); CoverBoulders.Step(.1f); }
            Check("... not in 10 minutes of a fight (" + CoverBoulders.Alive + " alive)", CoverBoulders.Alive == 0);
            Object.DestroyImmediate(dir.gameObject);
            if (BossEncounter.Instance != null) Object.DestroyImmediate(BossEncounter.Instance.gameObject);
            BossEncounter.ResetRun();
        }
        // a Dismiss (world change, death) is a quiet fade, not the destruction
        Fresh();
        var q = Place(0, new Vector2(.4f, 1f));
        int d0 = CoverBoulders.Destroyed;
        CoverBoulders.DismissAll();
        Check("a world change fades the boulder out (not solid at once) without the destruction sequence", q.State == CoverBoulder.Phase.Fade && !q.Solid && CoverBoulders.Destroyed == d0);
        for (int i = 0; i < 60; i++) CoverBoulders.Step(Dt);
        Check("... and it is gone in under a second", !q.Alive);
    }

    // ---- spawn rules ----

    static LandingSite Site(Vector2 at, int id)
    {
        var anchor = new GameObject("~Pad" + id).transform;
        anchor.position = at;
        return new LandingSite { anchor = anchor, local = Vector3.zero, scale = .3f, order = -420, id = id };
    }

    static void Rules()
    {
        Fresh();
        CoverBoulders.TestAnyWorld = false;
        bool wasOn = CoverBoulderTable.Entries[1].enabled;
        CoverBoulderTable.Entries[1].enabled = false;
        Check("a world whose art is not wired spawns nothing (Frost, switched off: " + CoverBoulderDirector.Blocked(1, 90f) + ")", CoverBoulderDirector.Blocked(1, 90f) == "no art");
        CoverBoulderTable.Entries[1].enabled = wasOn;
        Check("Space spawns after its first 20 s", CoverBoulderDirector.Blocked(0, 90f) == null);
        CoverBoulders.TestAnyWorld = true;
        Check("none in the first 20 s of a world (the 8 s calm start inside it)", CoverBoulderDirector.Blocked(0, 5f) == "too early" && CoverBoulderDirector.Blocked(0, 19f) == "too early" && CoverBoulderDirector.Blocked(0, 21f) == null);
        startMenu.youAreInTutorial = true;
        Check("none in the tutorial", CoverBoulderDirector.Blocked(0, 90f) == "tutorial");
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = true;
        Check("none after a death", CoverBoulderDirector.Blocked(0, 90f) == "dead");
        buttonClicks.playerDied = false;
        Check("none before a world is known", CoverBoulderDirector.Blocked(-1, 90f) == "no world");
        CoverBoulders.Disabled = true;
        Check("none when disabled", CoverBoulderDirector.Blocked(0, 90f) == "disabled");
        CoverBoulders.Disabled = false;
        Place(0, new Vector2(.5f, 3f));
        Check("at most one at a time", CoverBoulderDirector.Blocked(0, 90f) == "alive" && CoverBoulders.Spawn(0, 1) != null && CoverBoulders.Alive == 2 && CoverBoulders.Spawn(0, 1) == null);
        CoverBoulders.Clear();

        // elites: none while one is parked or lifting off; a site under a boulder is not used
        Fresh();
        var pilot = new GameObject("~Pilot").transform; pilot.position = new Vector3(0f, -3f, 0f);
        EliteSystem.PlayerOverride = pilot;
        EliteDef def = null;
        foreach (var d in EliteCatalog.All) { def = d; break; }
        var parked = EliteShip.Create(def, Site(new Vector2(2f, 3f), 5), 2.5f, new Vector2(-1.5f, 1.5f));
        Check("none while an elite is parked on a landing site (" + parked.State + ")", parked.State == EliteState.Parked && CoverBoulderDirector.Blocked(0, 90f) == "elite");
        EliteSystem.Clear();
        EliteSystem.PlayerOverride = pilot;
        var bd = Place(0, new Vector2(-.4f, 3f));
        Check("a landing site under a boulder is covered, one beside it is not", CoverBoulders.SiteCovered(new Vector2(-.4f, 3f)) && !CoverBoulders.SiteCovered(new Vector2(2.3f, -3f)));
        var pads = new List<LandingSite> { Site(new Vector2(-.4f, 3f), 11), Site(new Vector2(-.3f, 3.1f), 12) };
        LandingSites.Override = list => list.AddRange(pads);
        var ed = new GameObject("~EliteDir").AddComponent<EliteDirector>();
        int made = ed.SpawnGroup(3, 2);
        Check("the elite director leaves landing sites under a boulder alone (" + made + " parked)", made == 0);
        CoverBoulders.Clear();
        made = ed.SpawnGroup(3, 2);
        Check("(control: with no boulder both sites are used: " + made + ")", made == 2);
        Object.DestroyImmediate(ed.gameObject);
        LandingSites.Override = null;
        EliteSystem.Clear();
    }

    // ---- rarity per world ----

    static void Rarity()
    {
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            Fresh();
            var e = CoverBoulderTable.For(w);
            CoverBoulderDirector.WorldOverride = w;
            var dir = new GameObject("~dir").AddComponent<CoverBoulderDirector>();
            dir.Begin();
            float total = 1800f, dt = .1f;
            var times = new List<float>();
            int spawnedBefore = CoverBoulders.Spawned, maxAlive = 0;
            bool onScreenShort = true;
            float aliveSince = -1f;
            for (float t = 0f; t < total; t += dt)
            {
                int n = CoverBoulders.Spawned;
                dir.Tick(dt);
                if (CoverBoulders.Spawned != n) times.Add(t);
                CoverBoulders.Step(dt);
                maxAlive = Mathf.Max(maxAlive, CoverBoulders.AliveAny);
                if (CoverBoulders.AliveAny > 0 && aliveSince < 0f) aliveSince = t;
                if (CoverBoulders.AliveAny == 0 && aliveSince >= 0f) { if (t - aliveSince > (e.interactive ? 45f : 140f)) onScreenShort = false; aliveSince = -1f; }
            }
            float first = times.Count > 0 ? times[0] : -1f;
            float minGap = float.MaxValue, maxGap = 0f;
            for (int i = 1; i < times.Count; i++) { float g = times[i] - times[i - 1]; minGap = Mathf.Min(minGap, g); maxGap = Mathf.Max(maxGap, g); }
            Check(e.world + ": 30 min -> " + times.Count + " boulders, first at " + first.ToString("F0") + " s, gaps " + minGap.ToString("F0") + "-" + maxGap.ToString("F0") + " s (table " + e.gap.x + "-" + e.gap.y + ")",
                  times.Count >= 1800f / (e.gap.y + 3f) * .85f && times.Count <= 1800f / e.gap.x + 1 &&
                  first >= CoverBoulders.FirstSeconds && first <= CoverBoulders.FirstSeconds + 16f &&
                  (times.Count < 2 || (minGap >= e.gap.x - .25f && maxGap <= e.gap.y + 3f)));
            Check(e.world + ": never more than one at a time, and each is on screen under " + (e.interactive ? 45 : 140) + " s (max " + maxAlive + ")", maxAlive == 1 && onScreenShort);
            Check(e.world + ": none in the first 8 s calm start", times.Count == 0 || times[0] > 8f);
            // a world change takes the boulder away
            CoverBoulders.Clear();
            Place(0, new Vector2(.5f, 2f));
            CoverBoulderDirector.WorldOverride = (w + 1) % WorldManager.Worlds.Length;
            dir.Tick(dt);
            Check(e.world + ": changing world dismisses it", CoverBoulders.First == null || CoverBoulders.First.State == CoverBoulder.Phase.Fade);
            Object.DestroyImmediate(dir.gameObject);
        }
        CoverBoulderDirector.WorldOverride = -1;
    }

    // ---- cost ----

    static void Cost()
    {
        Fresh();
        long ctl;
        Check("the allocation meter sees allocations (control)", TestHarness.AllocMeterWorks(out ctl));
        CoverBoulders.Prewarm();
        var b = CoverBoulders.Spawn(0, 1);
        var rock = Hazard(b.Pos.x + .1f, b.Pos.y, "Astr", .4f);
        var other = Hazard(-1.5f, 3f);
        var shot = Fake(new Vector2(.5f, 3f));
        Vector2 q = Vector2.zero; Vector2 n = Vector2.zero;
        int frames = 0;
        System.Action work = () =>
        {
            for (int i = 0; i < 120; i++)
            {
                CoverBoulders.Step(Dt);
                CoverBoulders.PushBodies();
                CoverBoulders.ResolveShip(new Vector2(0f, -1f));
                CoverBoulders.StopsShot(b.Visual, .1f);
                CoverBoulders.StopsPlayerShot(new Vector2(0f, -3f), new Vector2(0f, 3f), .2f, out q);
                CoverBoulders.Blocked(new Vector2(0f, 4f), new Vector2(0f, -3f));
                CoverBoulders.ClipUp(new Vector2(0f, -3f), 8f, .3f);
                HostileShots.Resolve();
                frames++;
            }
        };
        work();   // warm: JIT, first-use caches
        long bytes = TestHarness.AllocatedBytes(work);
        Check("a boulder alive, bodies pushed, shots tested, the ship resolved: 120 frames allocate " + bytes + " bytes", bytes == 0);
        Check("the pool is two boulders and one chip pool, built once", CoverBoulders.Slots == 2 && CoverBoulders.Fx.Live >= 0);
        // spawning itself, after the pool exists
        CoverBoulders.Clear();
        System.Action spawn = () => { for (int i = 0; i < 20; i++) { CoverBoulders.Spawn(0, i % 3); CoverBoulders.Clear(); } };
        spawn();
        long sb = TestHarness.AllocatedBytes(spawn);
        Check("spawning a boulder allocates nothing once its art is loaded (" + sb + " bytes)", sb == 0);
        // drawn cost: a boulder and its overlay and tiles are the only renderers
        int renderers = CoverBoulders.Root.GetComponentsInChildren<SpriteRenderer>(true).Length;
        Check("the whole system is " + renderers + " sprite renderers (2 boulders x 11, " + CoverFx.Capacity + " chips)", renderers == 2 * 11 + CoverFx.Capacity);
        Check("nothing moves while the world is frozen", FrozenStays());
    }

    static bool FrozenStays()
    {
        CoverBoulders.Clear();
        var b = CoverBoulders.Spawn(0, 0);
        Vector2 p = b.Pos;
        CoverBoulders.Step(0f);
        return b.Pos == p;
    }

    // ---- every device shape ----

    static void Screens()
    {
        int checkedN = 0, bad = 0;
        foreach (string id in new[] { "and-1080x1920", "and-1080x2400-gesture", "flip7-1080x2520", "and-1080x2640", "iphone-15", "iphone-se", "fold-1812x2176", "ipad-9" })
        {
            Fresh();
            var d = FitDevice.Find(id);
            cam.aspect = d.Aspect;
            cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, d.w, d.h);
            using (ScreenInfo.Override(d.w, d.h, d.Safe, d.Cutouts, d.ReportedDpi, d.ios))
            {
                PlayField.Reset();
                float h = ShipReach.HalfWidth, edge = BossRails.DrawnInnerEdge;
                int worst = 0;
                for (int w = 0; w < CoverBoulderTable.Entries.Length; w++)
                    for (int v = 0; v < 3; v++)
                        for (int rep = 0; rep < 4; rep++)
                        {
                            CoverBoulders.Clear();
                            var b = CoverBoulders.Spawn(w, v);
                            checkedN++;
                            float width = b.Width;
                            // a landform is backdrop art: huge but inside the view's width, no lane to keep free, nothing solid
                            bool ok = b.Decorative
                                ? width <= edge * 2f * CoverBoulders.DecorMaxShare + .01f && !b.Solid && b.Pos.y > CameraFit.ViewTop
                                : CoverBoulders.SideGapOf(b) >= CoverBoulders.SideGap - .01f &&
                                  width <= edge * 2f * CoverBoulders.MaxScreenShare + .01f &&
                                  b.Pos.y > CameraFit.ViewTop;   // it enters above the top
                            // and it keeps that all the way down (drift a minute)
                            for (int i = 0; i < 1500 && ok && b.Alive; i++)
                            {
                                CoverBoulders.Step(.05f);
                                if (!b.Alive) break;
                                ok &= b.Decorative ? !b.Solid : CoverBoulders.SideGapOf(b) >= CoverBoulders.SideGap - .01f;
                            }
                            if (!ok) { bad++; worst++; }
                        }
                Check(id + " (reach " + h.ToString("F2") + ", rails " + edge.ToString("F2") + ", view " + (CameraFit.ViewTop - CameraFit.ViewBottom).ToString("F1") + " u): Space's cover boulder keeps SideGap to both rails and every landform stays inside the view width, in and out of the view (" + worst + " bad)", worst == 0);
            }
            PlayField.Reset();
        }
        Check("checked " + checkedN + " boulders across the device shapes", checkedN > 0 && bad == 0);
    }
}
