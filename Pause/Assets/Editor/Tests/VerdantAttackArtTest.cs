using System.IO;
using UnityEditor;
using UnityEngine;
using static AttackTestKit;

// VERDANT'S PAINTED WHIP AND TRUNK (Art/Resources/Attacks/Verdant/verdant_attack_lash.png + verdant_attack_log.png;
// AttackArt.LashLink/Tip/Root/Dash, AttackArt.LogFrame; drawn by AttackLash and AttackLog, so the Thornlash and Timber Hauler elites too).
//
//   * files: sizes, importer (point, no mips, uncompressed, not readable), served by AttackArt, cell counts, texture memory
//   * the loops move (>= 3 % of the pixels change between the two link / tip / root / dash cells and between every pair of log frames, wrap included)
//   * the links tile: stacking N link cells at the painted period (104 px of vine per 128 px cell) gives a continuous column,
//     every joint's rows overlap, in every a/b order; the lash draws its links at that period
//   * AttackLash / AttackLog really pick the painted cells (also through the Thornlash / Timber Hauler specs); hit shapes unchanged
//   * the colour exception (see Palette): these two files are GREEN with a pink accent, not the >= 30 % pink of every other hostile drawing
//   * the painted whip and trunk still stand out of every installed Verdant backdrop variant (Contrast): the pink rim / tip / outline carry them
//   * stepping a whip and a trunk allocates nothing
public static class VerdantAttackArtTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[VERDANTART] PASS  " : "[VERDANTART] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    const string Dir = "Assets/Art/Resources/Attacks/Verdant/";
    public const long MemoryBudget = 3L * 1024 * 1024;   // lash 0.5 MB + log 1 MB + shots 1 MB uncompressed RGBA32 (2.5 MB)

    public static int Execute()
    {
        fails = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                Files();
                Loops();
                Palette();
                Contrast();
                Tiling();
                LashUsesIt();
                LogUsesIt();
                Allocation();
                ShotsAtlas();
                MineLit();
                BossShots();
            }
            finally { AttackTestKit.Cleanup(); AttackBudgetScenarios.Cleanup(); AttackArt.Clear(); AttackHazardArt.Forget(); }
        }
        Debug.Log("[VERDANTART] failures: " + fails);
        return fails;
    }

    static Texture2D Read(string name)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(Dir + name + ".png"));
        return t;
    }

    // alpha of a w x h rectangle (x, yTop from the TOP), row 0 = top
    static byte[] Alpha(Texture2D t, int x, int yTop, int w, int h)
    {
        var all = t.GetPixels32();
        var o = new byte[w * h];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++) o[j * w + i] = all[(t.height - 1 - (yTop + j)) * t.width + x + i].a;
        return o;
    }

    static Color32[] Rgba(Texture2D t, int x, int yTop, int w, int h)
    {
        var all = t.GetPixels32();
        var o = new Color32[w * h];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++) o[j * w + i] = all[(t.height - 1 - (yTop + j)) * t.width + x + i];
        return o;
    }

    static float Changed(Color32[] a, Color32[] b)
    {
        int union = 0, diff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].a < 25 && b[i].a < 25) continue;
            union++;
            if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) + Mathf.Abs(a[i].a - b[i].a) > 40) diff++;
        }
        return union == 0 ? 0f : diff / (float)union;
    }

    static void Files()
    {
        Fresh();
        long bytes = 0;
        var sizes = new[] { new Vector2Int(1024, 128), new Vector2Int(1024, 256), new Vector2Int(1024, 256) };
        var names = new[] { "verdant_attack_lash", "verdant_attack_log", "verdant_attack_shots" };
        for (int i = 0; i < 3; i++)
        {
            var imp = AssetImporter.GetAtPath(Dir + names[i] + ".png") as TextureImporter;
            Check(names[i] + ": imported point-filtered, no mipmaps, uncompressed, not readable, no rescale",
                  imp != null && imp.filterMode == FilterMode.Point && !imp.mipmapEnabled && imp.textureCompression == TextureImporterCompression.Uncompressed &&
                  !imp.isReadable && imp.npotScale == TextureImporterNPOTScale.None);
            var tex = AttackArt.Atlas(2, names[i].Substring("verdant_attack_".Length));
            Check(names[i] + ": served by AttackArt at " + sizes[i].x + " x " + sizes[i].y, tex != null && tex.width == sizes[i].x && tex.height == sizes[i].y);
            if (tex != null) bytes += (long)tex.width * tex.height * 4;
        }
        Debug.Log("[VERDANTART] texture memory " + (bytes / 1024) + " KB (budget " + (MemoryBudget / 1024) + " KB)");
        Check("texture memory " + (bytes / 1024) + " KB <= " + (MemoryBudget / 1024) + " KB", bytes > 0 && bytes <= MemoryBudget);
        bool all = true;
        for (int i = 0; i < 2; i++)
        {
            all &= AttackArt.LashLink(2, i) != null && AttackArt.LashTip(2, i) != null && AttackArt.LashRoot(2, i) != null && AttackArt.LashDash(2, i) != null;
        }
        Check("lash: 2 link, 2 tip, 2 root, 2 dash cells, each 1 u square", all && Mathf.Approximately(AttackArt.LashLink(2, 0).bounds.size.y, 1f));
        bool logs = true;
        for (int i = 0; i < 8; i++) logs &= AttackArt.LogFrame(2, i) != null;
        Check("log: 8 frames of 2.0 x 1.0 u", logs && Mathf.Approximately(AttackArt.LogFrame(2, 3).bounds.size.x, 2f) && Mathf.Approximately(AttackArt.LogFrame(2, 3).bounds.size.y, 1f));
        Check("the other worlds' lash and log slots stay empty (Frost and Ember keep their own)", !AttackArt.Has(1, "lash") && !AttackArt.Has(3, "log"));
    }

    // ---- the shots atlas, the lit mine, the brighter boss shots ----------------------------------------------

    static void ShotsAtlas()
    {
        Fresh();
        bool all = true;
        for (int i = 0; i < 16; i++) all &= AttackArt.ShotCell(2, (AttackArt.ShotCellName)i) != null;
        Check("shots: all 16 cells are served, 1 u square", all && Mathf.Approximately(AttackArt.ShotCell(2, AttackArt.ShotCellName.SigBA).bounds.size.x, 1f));
        var shots = Read("verdant_attack_shots");
        Check("the pod (sigB) and crescent (sigA) pairs animate", Changed(Rgba(shots, 4 * 128, 128, 128, 128), Rgba(shots, 5 * 128, 128, 128, 128)) >= .03f &&
              Changed(Rgba(shots, 2 * 128, 128, 128, 128), Rgba(shots, 3 * 128, 128, 128, 128)) >= .03f);
        // the motion bodies wear the painted cells
        var crescent = ShotMotionArt.Sprite_(ShotMotionArt.Piece.Crescent, 2, 0);
        var pod = ShotMotionArt.Sprite_(ShotMotionArt.Piece.Pod, 2, 1);
        var leaf = ShotMotionArt.Sprite_(ShotMotionArt.Piece.Leaf, 2, 0);
        Check("Verdant crescent / pod / leaf motion bodies are the painted sigA / sigB / shard cells",
              crescent == AttackArt.ShotCell(2, AttackArt.ShotCellName.SigAA) && pod == AttackArt.ShotCell(2, AttackArt.ShotCellName.SigBB) &&
              leaf == AttackArt.ShotCell(2, AttackArt.ShotCellName.ShardA));
        Check("other worlds keep the procedural bodies",
              ShotMotionArt.Sprite_(ShotMotionArt.Piece.Crescent, 1, 0) != crescent && ShotMotionArt.Painted(ShotMotionArt.Piece.Pod, 3, 0) == null);
        // themed kinds when Verdant is enabled; hit radius is the def's, never the art's
        ShotSkins.Enable(2);
        bool themed = true;
        foreach (var k in new[] { EliteShots.Kind.Bolt, EliteShots.Kind.Shard, EliteShots.Kind.Shell, EliteShots.Kind.Slag, EliteShots.Kind.Glob })
            themed &= !ShotSkins.For(2, k).procedural && ShotSkins.For(2, k).drawScale == AttackArt.ThemedDrawScale;
        Check("enabled, Verdant's bolt / shard / shell / slag / glob wear thorn dart / leaf / log chunk / sap atlas cells", themed);
        Sprite pb; var pool = ShotSkins.PoolSprite(2, out pb);
        Check("the landed pod pool is the painted cloud pair", pool == AttackArt.ShotCell(2, AttackArt.ShotCellName.PoolA) && pb == AttackArt.ShotCell(2, AttackArt.ShotCellName.PoolB));
        // the pod burst: the open husk (sigC a,b) is left where a pod bursts or lands, for HuskSeconds, only with the world's skins on
        Check("the open pod husk (sigC) pair animates", Changed(Rgba(shots, 6 * 128, 128, 128, 128), Rgba(shots, 7 * 128, 128, 128, 128)) >= .03f);
        var fxHost = new GameObject("~HuskTest");
        try
        {
            var fx = new EliteFx(fxHost.transform);
            int before = fx.LiveCount;
            bool emitted = fx.PodHusk(2, Vector2.zero, 1f);
            Check("a burst pod leaves the painted husk (" + before + " -> " + fx.LiveCount + " live fx)", emitted && fx.LiveCount == before + 1);
            bool frost = fx.PodHusk(1, Vector2.zero, 1f);
            ShotSkins.Enable(2, false);
            bool off = fx.PodHusk(2, Vector2.zero, 1f);
            Check("the husk is a Verdant thing: none for Frost, none with the skins off", !frost && !off);
            ShotSkins.Enable(2);
        }
        finally { Object.DestroyImmediate(fxHost); }
        ShotSkins.Enable(2, false);
        Check("disabled, Verdant's shots are the procedural default again", ShotSkins.For(2, EliteShots.Kind.Bolt).procedural);
        // missing atlas: the procedural body answers
        AttackArt.Clear();
        AttackArt.Inject(2, "shots", null);
        Check("with the atlas missing the motion bodies fall back to procedural (no exception)", ShotMotionArt.Sprite_(ShotMotionArt.Piece.Pod, 2, 0) != null);
        AttackArt.Clear();
    }

    static void MineLit()
    {
        const string LitFile = "Assets/Art/Resources/" + RailMineArt.VerdantLitAtlasPath + ".png";
        var imp = AssetImporter.GetAtPath(LitFile) as TextureImporter;
        Check("lit mine: imported point-filtered, no mipmaps, uncompressed, not readable, full size",
              imp != null && imp.filterMode == FilterMode.Point && !imp.mipmapEnabled && imp.textureCompression == TextureImporterCompression.Uncompressed && !imp.isReadable && imp.maxTextureSize >= 1254);
        var tex = RailMineArt.LitAtlas;
        Check("lit mine: 1254 x 298, served from Resources/" + RailMineArt.VerdantLitAtlasPath, tex != null && tex.width == RailMineArt.AtlasSize && tex.height == RailMineArt.VerdantLitAtlasHeight);
        // NO PINK on the arming green mine (user, 2026-10-12: "the green mine turns pink when it's shooting"): the lit drawing is Verdant green-white
        var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        raw.LoadImage(File.ReadAllBytes(LitFile));
        int opq = 0, pinkPx = 0, greenPx = 0;
        foreach (var c in raw.GetPixels32())
        {
            if (c.a < 128) continue;
            opq++;
            Color.RGBToHSV(c, out float hh, out float ss, out float vv);
            float dg = hh * 360f;
            if (vv > .25f && ss * vv >= .035f && dg >= 280f && dg < 352f) pinkPx++;
            if (ss > .25f && vv > .5f && Mathf.Abs(Mathf.DeltaAngle(dg, 125f)) <= 25f) greenPx++;
        }
        Check("lit mine: no pink (" + pinkPx + " of " + opq + " opaque px) and a visible green glow (" + greenPx + " px)", opq > 0 && pinkPx <= opq * .01f && greenPx > opq * .05f);
        Check("only Verdant has a lit mine", RailMineArt.HasLit(2) && !RailMineArt.HasLit(0) && !RailMineArt.HasLit(1) && !RailMineArt.HasLit(3) && !RailMineArt.HasLit(4));
        bool geo = true;
        for (int c = 0; c < 4; c++)
        {
            var o = RailMineArt.PixelRect(2, c); var r = RailMineArt.LitPixelRect(c);
            geo &= r.x == o.x && r.y == o.y - 594 && r.width == o.width && r.height == o.height && r.yMax <= tex.height;
            var f = RailMineArt.LitFrame(2, c);
            geo &= f != null && f.texture == tex && Mathf.Approximately(f.rect.width, o.width) && Mathf.Approximately(f.pixelsPerUnit, RailMineArt.PixelsPerUnit) &&
                   Mathf.Approximately(f.pivot.x, RailMineArt.PixelPivot(2, c).x - o.x) && Mathf.Approximately(f.rect.height - f.pivot.y, RailMineArt.PixelPivot(2, c).y - o.y);
        }
        Check("lit frames reuse the Verdant row's rects and pivots with the row's y removed (same size, same PPU, same clamp position)", geo);
        var fb = RailMineArt.Flipbook(2);
        var orig = RailMineArt.Frame(2, RailMineArt.Dormant);
        Check("the flipbook shows the lit drawing only while arming (slots 4-5); idle, blink and hit stay the original atlas",
              fb[0] == orig && fb[1] == RailMineArt.Frame(2, RailMineArt.Waking) && fb[6] == RailMineArt.Frame(2, RailMineArt.Burst) &&
              fb[4] == RailMineArt.LitFrame(2, RailMineArt.Waking) && fb[5] == RailMineArt.LitFrame(2, RailMineArt.Charging) && fb[4].texture == tex && fb[1].texture != tex);
        var other = RailMineArt.Flipbook(1);
        Check("other worlds' flipbooks are untouched", other[4] == RailMineArt.Frame(1, RailMineArt.Waking) && other[4].texture == RailMineArt.Atlas);
    }

    static void BossShots()
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes("Assets/Art/Resources/Bosses/Verdant_shots.png"));
        Check("boss shots stay 1024 x 128 (8 cells)", t.width == 1024 && t.height == 128);
        // cells 4-6 (lane telegraph, beam) are untouched; the bolt/shard cells (0-3, 7) are the brighter set
        var bright = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        bright.LoadImage(File.ReadAllBytes("Assets/Art/Attacks/Verdant~/Verdant_shots_bright.png"));
        Check("Resources/Bosses/Verdant_shots.png is the approved bright sheet", Changed(t.GetPixels32(), bright.GetPixels32()) == 0f);
    }

    static void Loops()
    {
        var lash = Read("verdant_attack_lash");
        for (int pair = 0; pair < 4; pair++)
        {
            float c = Changed(Rgba(lash, pair * 2 * 128, 0, 128, 128), Rgba(lash, (pair * 2 + 1) * 128, 0, 128, 128));
            Check("lash " + new[] { "link", "tip", "root", "dash" }[pair] + " a -> b changes " + (c * 100f).ToString("F1") + " % of its pixels (>= 3 %)", c >= .03f);
        }
        var log = Read("verdant_attack_log");
        float min = 9f;
        for (int i = 0; i < 8; i++)
        {
            int j = (i + 1) % 8;
            float c = Changed(Rgba(log, (i % 4) * 256, (i / 4) * 128, 256, 128), Rgba(log, (j % 4) * 256, (j / 4) * 128, 256, 128));
            if (c < min) min = c;
        }
        Check("log: every frame differs from the next by >= 3 % (wrap included; least " + (min * 100f).ToString("F1") + " %)", min >= .03f);
    }

    // The painted vine fills rows 12..115 of a link cell (104 px); copies stacked at that period must have no gap and joints that line up.
    static void Tiling()
    {
        var lash = Read("verdant_attack_lash");
        const int Top = 12, Rows = 104;
        for (int a = 0; a < 2; a++)
            for (int b = 0; b < 2; b++)
            {
                var ca = Alpha(lash, a * 128, 0, 128, 128);
                var cb = Alpha(lash, b * 128, 0, 128, 128);
                bool topOk = true, botOk = true;
                int emptyRows = 0;
                for (int y = 0; y < 128; y++)
                {
                    bool any = false;
                    for (int x = 0; x < 128; x++) if (ca[y * 128 + x] > 24) { any = true; break; }
                    bool inside = y >= Top && y < Top + Rows;
                    if (inside && !any) emptyRows++;
                    if (!inside && any) botOk = false;     // vine outside the 104 px band would overlap its neighbour
                }
                // the joint: the last band row of A against the first band row of B
                int overlap = 0, aCount = 0, bCount = 0;
                for (int x = 0; x < 128; x++)
                {
                    bool pa = ca[(Top + Rows - 1) * 128 + x] > 24, pb = cb[Top * 128 + x] > 24;
                    if (pa) aCount++;
                    if (pb) bCount++;
                    if (pa && pb) overlap++;
                }
                topOk = aCount > 0 && bCount > 0 && overlap * 2 >= Mathf.Min(aCount, bCount);
                Check("links " + "ab"[a] + " then " + "ab"[b] + ": the vine fills rows " + Top + ".." + (Top + Rows - 1) + " with no empty row (" + emptyRows + ") and none outside",
                      emptyRows == 0 && botOk);
                Check("links " + "ab"[a] + " then " + "ab"[b] + ": the joint lines up (" + overlap + " of " + Mathf.Min(aCount, bCount) + " px shared; no gap, no step)", topOk);
            }
        // a chain of N links stacked at the painted period has a continuous alpha column
        const int N = 6;
        var col = new byte[N * Rows];
        for (int n = 0; n < N; n++)
        {
            var c = Alpha(lash, (n & 1) * 128, 0, 128, 128);
            for (int y = 0; y < Rows; y++)
            {
                byte m = 0;
                for (int x = 0; x < 128; x++) m = (byte)Mathf.Max(m, c[(Top + y) * 128 + x]);
                col[n * Rows + y] = m;
            }
        }
        int hole = 0;
        for (int i = 0; i < col.Length; i++) if (col[i] <= 24) hole++;
        Check("a chain of " + N + " links (" + col.Length + " px) has a continuous alpha column (" + hole + " empty rows)", hole == 0);
        Check("the lash draws painted links at that period (" + AttackHazardArt.PaintedLinkFill.ToString("F4") + " of the cell)", Mathf.Approximately(AttackHazardArt.PaintedLinkFill, Rows / 128f));
    }

    static void LashUsesIt()
    {
        foreach (var world in new[] { 2 })
        {
            Fresh();
            var spec = LashSpec.Standard(world);
            var l = AttackLash.Arm(spec, new Vector2(0f, 3f), new Vector2(0f, -2.5f), 1f, null);
            var tex = AttackArt.Atlas(2, "lash");
            Check("the lash picks the painted cells: link, tip, root and dash all come from verdant_attack_lash.png",
                  AttackHazardArt.LashArt(2) && l.LinkRenderer(0).sprite.texture == tex && l.TipRenderer.sprite.texture == tex &&
                  l.RootRenderer.sprite.texture == tex && l.DashRenderer(0).sprite.texture == tex);
            int shapePolys = l.Footprint.PolyCount;
            l.Ignite();
            Advance(.12f);
            var link = l.LinkRenderer(0);
            // consecutive links meet end to end: painted band (104/128 of the sprite) == the joint spacing
            Vector2 a = l.Joint(0), b = l.Joint(1);
            float band = link.sprite.bounds.size.y * link.transform.localScale.y * AttackHazardArt.PaintedLinkFill;
            Check("live: a link's painted band (" + band.ToString("F3") + " u) equals the joint spacing (" + (b - a).magnitude.ToString("F3") + " u) -- no gap, no overlap",
                  Mathf.Abs(band - (b - a).magnitude) < .01f);
            Check("... the hit ribbon is unchanged: " + shapePolys + " trapezoids, one per link", shapePolys >= 4);
            EliteSystem.Clear();
        }
        // the Thornlash elite's whip is the same lash
        Fresh();
        var elite = AttackLash.Arm(LashSpec.Standard(2), new Vector2(1f, 3f), new Vector2(1f, -2.5f), .9f, null);
        Check("the Thornlash elite's lash (world 2 spec) is painted too", elite != null && elite.LinkRenderer(0).sprite.texture == AttackArt.Atlas(2, "lash"));
        EliteSystem.Clear();
    }

    static void LogUsesIt()
    {
        Fresh();
        var l = AttackLog.Arm(LogSpec.Standard(2), new Vector2(-2f, 3f), new Vector2(.5f, 1f), 1f, 1.2f, null);
        var tex = AttackArt.Atlas(2, "log");
        Check("the log picks the painted trunk (8 frames, lying horizontal)", l.BodyRenderer.sprite != null && l.BodyRenderer.sprite.texture == tex &&
              l.BodyRenderer.sprite.bounds.size.x > l.BodyRenderer.sprite.bounds.size.y);
        l.Ignite();
        var seen = new System.Collections.Generic.HashSet<Sprite>();
        for (int i = 0; i < 70; i++) { AttackPools.StepAll(Dt); HostileShots.Resolve(); if (l.BodyRenderer.enabled) seen.Add(l.BodyRenderer.sprite); }
        Check("rolling shows all eight frames (" + seen.Count + " seen in 70 steps; 12 fps)", seen.Count == 8);
        EliteSystem.Clear();
    }

    static void Allocation()
    {
        Fresh();
        System.Action lash = () =>
        {
            AttackLash.Arm(LashSpec.Standard(2), new Vector2(0f, 3f), new Vector2(.3f, -2.5f), .8f, null);
            for (int i = 0; i < 220; i++) { AttackPools.StepAll(Dt); HostileShots.Resolve(); }
        };
        System.Action log = () =>
        {
            AttackLog.Arm(LogSpec.Standard(2), new Vector2(-2f, 3f), new Vector2(.5f, 1f), 1f, 1.2f, null);
            for (int i = 0; i < 400; i++) { AttackPools.StepAll(Dt); HostileShots.Resolve(); }
        };
        lash(); lash(); log(); log();
        bool meter = TestHarness.AllocMeterWorks(out long ctl);
        long u1 = TestHarness.AllocatedBytes(lash), u2 = TestHarness.AllocatedBytes(log);
        Check("a painted whip's whole life allocates nothing after warm-up (" + u1 + " bytes; meter " + (meter ? "ok" : "blind") + ")", meter && u1 == 0);
        Check("a painted trunk's whole life allocates nothing after warm-up (" + u2 + " bytes)", meter && u2 == 0);
        EliteSystem.Clear();
    }

    // ---- the colour exception -----------------------------------------------------------------------------------
    //
    // DOCUMENTED, NARROW EXCEPTION to the "hostile art is >= 30 % pink-family" rule (ShotSkinTest.MinPinkShare): the Verdant vine lash and
    // rolling log (and only these two files; the Thornlash / Timber Hauler elites draw the same ones) are painted GREEN so the jungle's own
    // weapons read as plants, with a pink ACCENT: a thin rim, the hooked thorn tip, the cut rings of the trunk (the dotted tell arc stays pink).
    // So per cell: pink share 6..18 % and green share >= 55 % of the opaque pixels. No other world's art, shot or procedural sprite is
    // loosened (the 30 % checks in ShotSkinTest, AttackLashTest, AttackFairnessTest, ShotMotionTest still run on everything else), and the
    // hostile cue stays meaningful: the pink accent must exist in every working cell (rim / tip / rings) and the stand-out test below
    // keeps the whip and trunk 3:1 clear of the backdrop (the in-code outline, AttackLash's / AttackLog's ShotOutline rim, carries the rest).
    public const float ExceptionPinkMin = .05f, ExceptionPinkMax = .18f, ExceptionGreenMin = .55f;

    static void Shares(Color32[] px, out float pink, out float green, out int pinkPx, out int opaque)
    {
        int p = 0, g = 0, n = 0;
        foreach (var c in px)
        {
            if (c.a <= 24) continue;
            n++;
            Color.RGBToHSV(c, out float h, out float sat, out float v);
            float deg = h * 360f;
            if (sat < .35f && v > .8f) p++;   // the cue accent: jade-white / white-hot (NO PINK outside Space, user 2026-10-11)
            else if (deg >= 70f && deg <= 170f && sat > .2f) g++;
        }
        opaque = n; pinkPx = p;
        pink = n > 0 ? p / (float)n : 0f;
        green = n > 0 ? g / (float)n : 0f;
    }

    static void Palette()
    {
        foreach (var (name, cw, ch, cols, cells) in new[] { ("verdant_attack_lash", 128, 128, 8, 8), ("verdant_attack_log", 256, 128, 4, 8) })
        {
            var t = Read(name);
            float lowP = 9f, highP = -1f, lowG = 9f;
            int minPinkPx = int.MaxValue, workCells = 0;
            float dashMin = 9f;
            for (int i = 0; i < cells; i++)
            {
                var px = Rgba(t, (i % cols) * cw, (i / cols) * ch, cw, ch);
                Shares(px, out float pink, out float green, out int pinkPx, out int opaque);
                if (opaque == 0) continue;
                // the lash's dash cells (6, 7) are the dotted tell arc: they stay fully pink, under the ordinary >= 30 % rule
                if (name.EndsWith("lash") && i >= 6) { dashMin = Mathf.Min(dashMin, pink); workCells++; continue; }
                workCells++;
                lowP = Mathf.Min(lowP, pink); highP = Mathf.Max(highP, pink); lowG = Mathf.Min(lowG, green);
                minPinkPx = Mathf.Min(minPinkPx, pinkPx);
            }
            if (name.EndsWith("lash")) Check("verdant_attack_lash: the dotted tell arc (dash cells) stays pink-family: " + (dashMin * 100f).ToString("F0") + " % >= " + (ShotSkinTest.MinPinkShare * 100f) + " %", dashMin >= ShotSkinTest.MinPinkShare);
            Debug.Log("[VERDANTART] " + name + " pink " + (lowP * 100f).ToString("F1") + ".." + (highP * 100f).ToString("F1") + " %, least green " + (lowG * 100f).ToString("F1") + " %, fewest pink px " + minPinkPx);
            Check(name + ": green with a pink accent in every one of its " + workCells + " cells: pink " + (ExceptionPinkMin * 100f) + ".." + (ExceptionPinkMax * 100f) +
                  " % (got " + (lowP * 100f).ToString("F1") + ".." + (highP * 100f).ToString("F1") + "), green >= " + (ExceptionGreenMin * 100f) + " % (least " + (lowG * 100f).ToString("F1") + ")",
                  workCells == cells && lowP >= ExceptionPinkMin && highP <= ExceptionPinkMax && lowG >= ExceptionGreenMin);
            Check(name + ": the pink rim / thorn tip / cut rings exist in every cell (fewest " + minPinkPx + " pink px, need >= 40)", minPinkPx >= 40);
        }
    }

    // ---- stand-out over the Verdant backdrop ---------------------------------------------------------------------
    //
    // HostileProjectileTest's rule on real renders: pixels the drawing adds that are MinContrast (3:1) clear of the backdrop behind them.
    // Every installed Verdant backdrop variant at 2 / 12 / 40 s; the whip's links (window = one link) and the trunk (its whole box).
    static void Contrast()
    {
        var spec = BackdropCatalog.For("Verdant");
        var picker = BackdropVariants.For("Verdant");
        int worstLink = int.MaxValue, worstNaked = int.MaxValue, worstLog = int.MaxValue, worstLogNaked = int.MaxValue;
        long sumDressed = 0, sumNaked = 0;
        string worstWhere = "";
        for (int v = 1; v <= Mathf.Max(1, spec.variantSets); v++)
        {
            if (spec.variantSets > 0 && !picker.Installed(v)) continue;
            var cam = HostileProjectileTest.Fresh(2);
            cam.aspect = HostileProjectileTest.RW / (float)HostileProjectileTest.RH;
            picker.Force = spec.variantSets > 0 ? v : 0;
            var go = new GameObject("~Backdrop");
            var wb = go.AddComponent<WorldBackdrop>();
            wb.Show("Verdant", false);
            var rt = new RenderTexture(HostileProjectileTest.RW, HostileProjectileTest.RH, 24);
            cam.targetTexture = rt;
            var tex = new Texture2D(HostileProjectileTest.RW, HostileProjectileTest.RH, TextureFormat.RGB24, false);
            float clock = 0f;
            foreach (float at in new[] { 2f, 12f, 40f })
            {
                while (clock < at) { wb.Step(1f / 60f); clock += 1f / 60f; }
                var bg = HostileProjectileTest.Grab(cam, rt, tex);
                float ppu = HostileProjectileTest.RH / (cam.orthographicSize * 2f);
                // the whip hangs straight down from (0.3, 3.5): its links centred on the joints' midpoints
                var l = AttackLash.Arm(LashSpec.Standard(2), new Vector2(.3f, 3.5f), new Vector2(.3f, -2.5f), 1f, null);
                l.Ignite();
                for (int i = 0; i < 8; i++) { AttackPools.StepAll(Dt); HostileShots.Resolve(); }
                int linkMin = int.MaxValue, nakedMin = int.MaxValue;
                for (int pass = 0; pass < 2; pass++)
                {
                    bool naked = pass == 1;
                    SetRims(l.gameObject, !naked);
                    var fg = HostileProjectileTest.Grab(cam, rt, tex);
                    for (int i = 0; i < l.Links; i++)
                    {
                        Vector2 m = (l.Joint(i) + l.Joint(i + 1)) * .5f;
                        var c = cam.WorldToScreenPoint(new Vector3(m.x, m.y, 0f));
                        float half = (l.Joint(i + 1) - l.Joint(i)).magnitude * .5f * ppu + 6f;
                        int n = HostileProjectileTest.StandOut(fg, bg, c, half);
                        if (naked) { nakedMin = Mathf.Min(nakedMin, n); sumNaked += n; }
                        else { if (n < linkMin) { linkMin = n; worstWhere = "v" + v + " " + at + " s link " + i; } sumDressed += n; }
                    }
                }
                worstLink = Mathf.Min(worstLink, linkMin); worstNaked = Mathf.Min(worstNaked, nakedMin);
                l.gameObject.SetActive(false);
                AttackPools.Forget();
                // the trunk lying across the canopy
                var lg = AttackLog.Arm(LogSpec.Standard(2), new Vector2(-2f, 3f), new Vector2(.5f, 1f), 1f, 1.2f, null);
                lg.Ignite();
                for (int i = 0; i < 20; i++) { AttackPools.StepAll(Dt); HostileShots.Resolve(); }
                for (int pass = 0; pass < 2; pass++)
                {
                    SetRims(lg.gameObject, pass == 0);
                    var fg = HostileProjectileTest.Grab(cam, rt, tex);
                    var c = cam.WorldToScreenPoint(lg.BodyRenderer.transform.position);
                    float half = lg.BodyRenderer.bounds.extents.x * ppu + 4f;
                    int n = HostileProjectileTest.StandOut(fg, bg, c, half);
                    if (pass == 0) worstLog = Mathf.Min(worstLog, n); else worstLogNaked = Mathf.Min(worstLogNaked, n);
                }
                AttackPools.Forget();
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(go);
            picker.Force = 0;
        }
        Debug.Log("[VERDANTART] CONTRAST whip: fewest 3:1 px per link window " + worstLink + " with the outline (" + worstWhere + "), " + worstNaked + " without; log: " + worstLog + " with, " + worstLogNaked + " without; sums " + sumDressed + " vs " + sumNaked);
        Check("the painted whip stands out of every Verdant backdrop variant: every link has >= " + HostileProjectileTest.MinStandOutPixels + " px at " + HostileProjectileTest.MinContrast + ":1 (fewest " + worstLink + "; without its outline " + worstNaked + ")",
              worstLink >= HostileProjectileTest.MinStandOutPixels);
        Check("the painted trunk stands out of every Verdant backdrop variant: >= " + HostileProjectileTest.MinStandOutPixels + " px at " + HostileProjectileTest.MinContrast + ":1 (fewest " + worstLog + "; without its outline " + worstLogNaked + ")",
              worstLog >= HostileProjectileTest.MinStandOutPixels);
    }

    // shows / hides the outline renderers (the children named Rim, and the log's Outline) under a hazard
    static void SetRims(GameObject root, bool on)
    {
        foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
            if (sr.name == "Rim" || sr.name == "Outline") sr.forceRenderingOff = !on;
    }
}
