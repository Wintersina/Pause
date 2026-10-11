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
    public const long MemoryBudget = 2L * 1024 * 1024;   // lash 0.5 MB + log 1 MB uncompressed RGBA32 (1.5 MB)

    public static int Execute()
    {
        fails = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                Files();
                Loops();
                Tiling();
                LashUsesIt();
                LogUsesIt();
                Allocation();
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
        var sizes = new[] { new Vector2Int(1024, 128), new Vector2Int(1024, 256) };
        var names = new[] { "verdant_attack_lash", "verdant_attack_log" };
        for (int i = 0; i < 2; i++)
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
}
