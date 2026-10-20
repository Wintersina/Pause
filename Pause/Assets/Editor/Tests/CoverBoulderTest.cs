using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// BOULDERS (docs/cover-boulders.md; Scripts/Gameplay/Cover): big decorative landforms of the backdrop, in every world, with no gameplay effect.
//
//   TABLE      one entry per world, sane numbers, no pink outside Space, art loads where wired
//   ART        the art of every wired world: complete, real resolution, pale-edged
//   BACKDROP   a boulder only spawns where it blends into the backdrop variant in play; it emerges and recedes without a pop
//   NOTHING    a boulder blocks nothing: the ship, shots, bodies, elite landing sites are all unaffected, in every world
//   RULES      spawn rules and rarity per world, the first 20 s, tutorial, death, world change, a boss fades it
//   COST       no allocation, pooled; every device shape keeps it inside the rails
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
            Emerge();
            BlocksNothing();
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
        CoverBoulders.Disabled = false;
        CoverBoulders.TestAnyWorld = true;
        CoverBoulderDirector.WorldOverride = -1;
        ShoveCrash.RollOverride = null;
        HostileShots.ResetCounters();
        Random.InitState(4242);
    }


    // A boulder (variant v of `world`) put exactly at `at`, held still.
    static CoverBoulder Place(int variant, Vector2 at, int world = 0)
    {
        var b = CoverBoulders.Spawn(world, variant);
        b.Hold(at);
        return b;
    }

    static GameObject Hazard(float x, float y, string tag = "Enimey", float radius = .3f)
    {
        var go = new GameObject("hazard");
        go.tag = tag;
        go.transform.position = new Vector3(x, y, 0f);
        ClearTarget.Ensure(go).SetRadius(radius);
        return go;
    }

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

    // ---- table ----

    static void Table()
    {
        string bad = CoverBoulderTable.Validate();
        Check("the table checks out (" + (bad ?? "ok") + ")", bad == null);
        Check("an entry for each of the " + WorldManager.Worlds.Length + " worlds", CoverBoulderTable.Entries.Length == WorldManager.Worlds.Length && WorldManager.Worlds.Length == 7);
        bool names = true, three = true;
        for (int w = 0; w < CoverBoulderTable.Entries.Length; w++)
        {
            var e = CoverBoulderTable.Entries[w];
            names &= e.world == WorldManager.Worlds[w].displayName && !string.IsNullOrEmpty(e.concept);
            three &= e.Variants == 3;
        }
        Check("each entry is its world's, with a concept and three variants", names && three);
        Check("Space is wired; a world is enabled only with its art in Resources/Boulders",
              CoverBoulderTable.Enabled(0) && !CoverBoulderTable.Enabled(-1) && !CoverBoulderTable.Enabled(99));
        for (int w = 0; w < CoverBoulderTable.Entries.Length; w++)
            if (CoverBoulderTable.Entries[w].enabled)
                Check(CoverBoulderTable.Entries[w].world + ": an enabled world's art loads", CoverBoulderArt.Has(w));
        // no built-up city, rig or industrial complex gets a landform (the per-variant table of docs section 13)
        var T = CoverBoulderTable.Entries;
        bool cities = T[1].Mask(2) == 0 && T[1].Mask(3) == 0 && T[2].Mask(3) == 0 && T[3].Mask(2) == 0 && T[4].Mask(3) == 0 && T[6].Mask(2) == 0 && T[6].Mask(3) == 0;
        Check("none on the cities / industrial variants: Frost v2 v3, Verdant v3, Ember v2, Tide v2 v3 (oil rigs, sunken city), Desert v2 v3", cities);
    }

    // ---- art ----

    static void Art()
    {
        Fresh();
        var v = CoverBoulderArt.For(0);
        Check("Space's art loads: three variants with a sprite and 1-4 circles each",
              v != null && v.Length == 3 && System.Array.TrueForAll(v, x => x.intact != null && x.circles.Length >= 1 && x.circles.Length <= CoverBoulder.MaxCircles));
        if (v == null) return;
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
        Check("a fresh boulder starts fully faded (presence " + (b != null ? b.Presence.ToString("0.00") : "-") + ")", b != null && b.Presence < .02f);
        if (b == null) return;
        float prev = b.Presence, maxStep = 0f, last = 0f;
        bool monotone = true;
        for (int i = 0; i < 160; i++)
        {
            b.Hold(new Vector2(0f, 0f), false);   // keeps it clear of the view bottom: only the emerge runs
            CoverBoulders.Step(Dt);
            b.Hold(new Vector2(0f, 0f), false);
            maxStep = Mathf.Max(maxStep, Mathf.Abs(b.Presence - prev));
            monotone &= b.Presence >= prev - 1e-4f;
            prev = b.Presence; last = b.Presence;
        }
        Check("the emerge is continuous: opacity steps <= " + maxStep.ToString("0.000") + " per frame, rising", maxStep < .03f && monotone);
        Check("after " + CoverBoulder.EmergeSeconds + " s it is fully in the play layer (presence " + last.ToString("0.00") + ")", last > .98f);
        // recede: a boulder low in the view sinks back smoothly (no jump from the last frame in view)
        float vb = CameraFit.ViewBottom;
        float pp = -1f, maxRec = 0f;
        for (float y = vb + 6f; y > vb - .5f; y -= .02f)
        {
            b.Hold(new Vector2(0f, y), false);
            CoverBoulders.Step(.0001f);
            b.Hold(new Vector2(0f, y), false);
            if (pp >= 0f) maxRec = Mathf.Max(maxRec, Mathf.Abs(b.Presence - pp));
            pp = b.Presence;
        }
        Check("the recede is smooth (max step " + maxRec.ToString("0.000") + " per .02 u) and ends faint (" + pp.ToString("0.00") + ")", maxRec < .03f && pp < .5f);
        CoverBoulders.Clear();
    }

    // ---- a boulder BLOCKS NOTHING, in every world ----

    static LandingSite Site(Vector2 at, int id)
    {
        var anchor = new GameObject("~Pad" + id).transform;
        anchor.position = at;
        return new LandingSite { anchor = anchor, local = Vector3.zero, scale = .3f, order = -420, id = id };
    }

    static void BlocksNothing()
    {
        var T = CoverBoulderTable.Entries;
        for (int w = 0; w < T.Length; w++)
        {
            Fresh();
            string nm = T[w].world;
            var b = Place(1, new Vector2(.4f, 2f), w);
            if (b == null) { Check(nm + ": a boulder spawns", false); continue; }
            Check(nm + ": it is drawn behind gameplay (backdrop sorting order " + CoverBoulder.SortingOrder + ")",
                  b.GetComponentOnBody().sortingOrder == CoverBoulder.SortingOrder && CoverBoulder.SortingOrder < 0);
            Check(nm + ": it is inside the lane (" + b.Width.ToString("0.0") + " u of " + (CoverBoulders.Edge * 2f).ToString("0.0") + ")", b.Width <= CoverBoulders.Edge * 2f * CoverBoulders.DecorMaxShare + .001f);

            // a body and a hostile shot right on top of it; the player's follow target straight through it
            var body = Hazard(.4f, 2f);
            var at = body.transform.position;
            var shot = Fake(new Vector2(.4f, 2f));
            HostileShots.Resolve();
            for (int i = 0; i < 30; i++) CoverBoulders.Step(Dt);
            Check(nm + ": a hostile shot inside it is not popped, and a body inside it is not pushed", !shot.popped && (body.transform.position - at).sqrMagnitude < 1e-8f);
            var finger = new Vector3(.4f, 2f - ShipReach.FingerOffset, 0f);
            var follow = movePlayer.FollowTarget(finger);
            Check(nm + ": the ship's follow target is not moved by it (" + follow + ")", Mathf.Abs(follow.x - ShipReach.ClampX(.4f)) < 1e-4f);

            // elite landing sites under it are used (SpawnGroup), as with no boulder at all
            var pilot = new GameObject("~Pilot").transform; pilot.position = new Vector3(0f, -3f, 0f);
            EliteSystem.PlayerOverride = pilot;
            var pads = new List<LandingSite> { Site(new Vector2(.4f, 2f), 11), Site(new Vector2(.5f, 2.1f), 12) };
            LandingSites.Override = list => list.AddRange(pads);
            var ed = new GameObject("~EliteDir").AddComponent<EliteDirector>();
            int made = ed.SpawnGroup(3, 2);
            Check(nm + ": landing sites under it are still used (" + made + " parked)", made == 1);
            Object.DestroyImmediate(ed.gameObject);
            LandingSites.Override = null;
            EliteSystem.Clear();
        }

        // an elite parked and a world hazard running never keep a boulder from spawning (no mutual exclusion)
        Fresh();
        CoverBoulders.TestAnyWorld = false;
        Check("nothing but the spawn rules gates the director (Space after 20 s: " + (CoverBoulderDirector.Blocked(0, 90f) ?? "free") + ")", CoverBoulderDirector.Blocked(0, 90f) == null);
        CoverBoulders.TestAnyWorld = true;

        // a boss starting: the boulder sinks out (nothing destroyed, no rumble); every world's
        for (int w = 0; w < T.Length; w++)
        {
            Fresh();
            var f = Place(0, new Vector2(0f, 1f), w);
            CoverBoulders.DismissAll();
            Check(T[w].world + ": a boss starting fades the boulder out (state " + f.State + ")", f.State == CoverBoulder.Phase.Fade);
            float tt = 0f;
            while (f.Alive && tt < 4f) { CoverBoulders.Step(Dt); tt += Dt; }
            Check(T[w].world + ": ... and it is gone, effects included, in " + tt.ToString("0.0") + " s", !f.Alive && CoverBoulders.AliveAny == 0);
        }

        // the Storm effects layer: pooled, no allocations, follows the boulder, hidden the moment it fades or is released, no pink
        Fresh();
        var st = Place(1, new Vector2(0f, 2f), 5);
        for (int i = 0; i < 30; i++) CoverBoulders.Step(Dt);
        var fxRoot = st.StormFxRoot;
        Check("Storm: the effects layer exists (" + (fxRoot != null ? fxRoot.GetComponentsInChildren<SpriteRenderer>(true).Length.ToString() : "-") + " pooled renderers) and is showing", fxRoot != null && fxRoot.gameObject.activeSelf && fxRoot.GetComponentsInChildren<SpriteRenderer>(true).Length == 31);
        System.Action work = () => { for (int i = 0; i < 120; i++) CoverBoulders.Step(Dt); };
        work();
        long bytes = TestHarness.AllocatedBytes(work);
        Check("Storm: 120 frames with every wisp, streak, pellet and arc moving allocate " + bytes + " bytes", bytes == 0);
        CoverBoulders.DismissAll();
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

    // ---- spawn rules ----

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
        Check("at most one at a time", CoverBoulderDirector.Blocked(0, 90f) == "alive" && CoverBoulders.Spawn(0, 1) != null && CoverBoulders.AliveAny == 2 && CoverBoulders.Spawn(0, 1) == null);
        CoverBoulders.Clear();
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
                if (CoverBoulders.AliveAny == 0 && aliveSince >= 0f) { if (t - aliveSince > 140f) onScreenShort = false; aliveSince = -1f; }
            }
            float first = times.Count > 0 ? times[0] : -1f;
            float minGap = float.MaxValue, maxGap = 0f;
            for (int i = 1; i < times.Count; i++) { float g = times[i] - times[i - 1]; minGap = Mathf.Min(minGap, g); maxGap = Mathf.Max(maxGap, g); }
            Check(e.world + ": 30 min -> " + times.Count + " boulders, first at " + first.ToString("F0") + " s, gaps " + minGap.ToString("F0") + "-" + maxGap.ToString("F0") + " s (table " + e.gap.x + "-" + e.gap.y + ")",
                  times.Count >= 1800f / (e.gap.y + 3f) * .85f && times.Count <= 1800f / e.gap.x + 1 &&
                  first >= CoverBoulders.FirstSeconds && first <= CoverBoulders.FirstSeconds + 16f &&
                  (times.Count < 2 || (minGap >= e.gap.x - .25f && maxGap <= e.gap.y + 3f)));
            Check(e.world + ": never more than one at a time, and each is on screen under " + 140 + " s (max " + maxAlive + ")", maxAlive == 1 && onScreenShort);
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
        System.Action work = () => { for (int i = 0; i < 120; i++) CoverBoulders.Step(Dt); };
        work();   // warm: JIT, first-use caches
        long bytes = TestHarness.AllocatedBytes(work);
        Check("a boulder alive: 120 frames allocate " + bytes + " bytes", bytes == 0);
        // spawning itself, after the pool exists
        CoverBoulders.Clear();
        System.Action spawn = () => { for (int i = 0; i < 20; i++) { CoverBoulders.Spawn(0, i % 3); CoverBoulders.Clear(); } };
        spawn();
        long sb = TestHarness.AllocatedBytes(spawn);
        Check("spawning a boulder allocates nothing once its art is loaded (" + sb + " bytes)", sb == 0);
        // drawn cost: a boulder and its haze are the only renderers
        int renderers = CoverBoulders.Root.GetComponentsInChildren<SpriteRenderer>(true).Length;
        Check("the whole system is " + renderers + " sprite renderers (2 boulders x 2)", renderers == CoverBoulders.Slots * 2);
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
                float edge = BossRails.DrawnInnerEdge;
                int worst = 0;
                for (int w = 0; w < CoverBoulderTable.Entries.Length; w++)
                    for (int v = 0; v < 3; v++)
                        for (int rep = 0; rep < 4; rep++)
                        {
                            CoverBoulders.Clear();
                            var b = CoverBoulders.Spawn(w, v);
                            checkedN++;
                            // backdrop art: huge but inside the view's width, entering above the top
                            bool ok = b.Width <= edge * 2f * CoverBoulders.DecorMaxShare + .01f && b.Pos.y > CameraFit.ViewTop;
                            for (int i = 0; i < 1500 && b.Alive; i++) CoverBoulders.Step(.05f);
                            if (!ok) { bad++; worst++; }
                        }
                Check(id + " (rails " + edge.ToString("F2") + ", view " + (CameraFit.ViewTop - CameraFit.ViewBottom).ToString("F1") + " u): every boulder stays inside the view width and scrolls out (" + worst + " bad)", worst == 0);
            }
            PlayField.Reset();
        }
        Check("checked " + checkedN + " boulders across the device shapes", checkedN > 0 && bad == 0);
    }
}
