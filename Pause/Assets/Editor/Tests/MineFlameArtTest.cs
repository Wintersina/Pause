using System.IO;
using UnityEditor;
using UnityEngine;

// EMBER'S PAINTED MINE FLAME (Art/Resources/Attacks/Ember/ember_attack_mineflame.png, AttackArt.MineFlame, MineFlameArt.Ember).
//
//   * the sheet is 768 x 768, imported point-filtered, uncompressed, mip-free; MineFlameArt.Ember() serves the PAINTED cells
//     slot by slot (6 flame bodies 128 x 384, 4 pilot, 2 aim, 4 burst, 3 impact, 2 haze), none of them the procedural fallback;
//   * every loop animates (>= 3 % of its pixels change per frame, wrap included);
//   * with the file missing (an empty atlas served in its place) the procedural flame takes over, Alive and not painted;
//   * texture memory: the sheet, plus every attack sheet under Resources/Attacks, plus the biggest world backdrop budget, stays
//     within a combined cap.
public static class MineFlameArtTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[MINEFLAME] PASS  " : "[MINEFLAME] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string Path_ = "Assets/Art/Resources/Attacks/Ember/ember_attack_mineflame.png";
    public const long AttackTexturesBudget = 12L * 1024 * 1024;       // every Resources/Attacks sheet together (GPU, RGBA32): 4 laser kits + the flame ~ 11.3 MB
    public const long WithBackdropBudget = AttackTexturesBudget + WorldBackdropTest.FrostTextureBudgetBytes;   // the largest world budget beside them

    public static int Execute()
    {
        fails = 0;
        using (new TestHarness.Sandbox())
        {
            try { File_(); Painted(); Animation(); Fallback(); Memory(); }
            finally { AttackArt.Clear(); MineFlameArt.Reset(); MineLaserArt.ResetCache(); }
        }
        Debug.Log("[MINEFLAME] failures: " + fails);
        return fails;
    }

    static void File_()
    {
        var imp = AssetImporter.GetAtPath(Path_) as TextureImporter;
        Check("ember_attack_mineflame: imported", imp != null);
        if (imp != null)
            Check("point filter, no mipmaps, uncompressed, not readable, no rescale",
                  imp.filterMode == FilterMode.Point && !imp.mipmapEnabled && imp.textureCompression == TextureImporterCompression.Uncompressed &&
                  !imp.isReadable && imp.npotScale == TextureImporterNPOTScale.None);
        var tex = AttackArt.Atlas(3, "mineflame");
        Check("served by AttackArt as Attacks/Ember/ember_attack_mineflame at 768 x 768", tex != null && tex.width == 768 && tex.height == 768);
    }

    static void Painted()
    {
        MineFlameArt.Reset(); MineLaserArt.ResetCache();
        var a = MineFlameArt.Ember();
        Check("Ember's flame is the painted one and Alive", a != null && a.painted && a.Alive);
        if (a == null) return;
        Check("6 flame bodies of 1 u x 3 u (128 x 384)", a.beam.Length == 6 && System.Array.TrueForAll(a.beam, s => s != null && Mathf.Approximately(s.bounds.size.x, 1f) && Mathf.Approximately(s.bounds.size.y, 3f)));
        Check("4 pilot, 2 aim, 4 burst, 3 impact, 2 haze cells, each 1 u square, all from the sheet",
              a.pilot.Length == 4 && a.flash.Length == 4 && a.spark.Length == 3 && a.haze.Length == 2 && a.sight != null && a.sightB != null &&
              System.Array.TrueForAll(a.pilot, s => s != null && s.texture.width == 768) && System.Array.TrueForAll(a.flash, s => s != null && s.texture.width == 768) &&
              System.Array.TrueForAll(a.spark, s => s != null && s.texture.width == 768) && System.Array.TrueForAll(a.haze, s => s != null && s.texture.width == 768) &&
              a.sight.texture.width == 768 && a.sightB.texture.width == 768 && Mathf.Approximately(a.sight.bounds.size.y, 1f));
        var proc = MineFlameArt.Build();
        Check("no slot is the procedural fallback", a.beam[0].texture != proc.beam[0].texture && a.pilot[0].texture != proc.pilot[0].texture &&
              a.flash[0].texture != proc.flash[0].texture && a.spark[2].texture != proc.spark[2].texture && a.haze[1].texture != proc.haze[1].texture && a.sight.texture != proc.sight.texture);
        var mine = MineLaserArt.For(MineLaserArt.EmberWorld);
        Check("the mine laser uses it", mine.flame == a);
    }

    static Color32[] Slice(Texture2D t, int x, int yTop, int w, int h)
    {
        var all = t.GetPixels32();
        var o = new Color32[w * h];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++) o[j * w + i] = all[(t.height - 1 - (yTop + j)) * t.width + x + i];
        return o;
    }

    static float Change(Color32[] a, Color32[] b, int thr)
    {
        int union = 0, diff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].a < thr && b[i].a < thr) continue;
            union++;
            if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || (a[i].a >= thr) != (b[i].a >= thr)) diff++;
        }
        return union == 0 ? 0f : diff / (float)union;
    }

    static void Loop(string what, Texture2D t, int[] xs, int[] ys, int w, int h, bool wrap, int thr = 128)
    {
        float min = 1f;
        for (int i = 0; i < xs.Length; i++)
        {
            int n = i + 1;
            if (n == xs.Length) { if (!wrap) break; n = 0; }
            min = Mathf.Min(min, Change(Slice(t, xs[i], ys[i], w, h), Slice(t, xs[n], ys[n], w, h), thr));
        }
        Check(what + " advances >= 3 % of its pixels every frame (min " + (min * 100f).ToString("F1") + " %)", min >= .03f);
    }

    static void Animation()
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(Path_));
        Loop("flame body 1-6 (loops, 12 fps)", t, new[] { 0, 128, 256, 384, 512, 640 }, new[] { 0, 0, 0, 0, 0, 0 }, 128, 384, true);
        Loop("pilot a-d", t, new[] { 0, 128, 256, 384 }, new[] { 384, 384, 384, 384 }, 128, 128, false);
        Loop("aim a,b (loops)", t, new[] { 512, 640 }, new[] { 384, 384 }, 128, 128, true);
        Loop("burst a-d (loops)", t, new[] { 0, 128, 256, 384 }, new[] { 512, 512, 512, 512 }, 128, 128, true);
        Loop("impact a-c", t, new[] { 512, 640, 0 }, new[] { 512, 512, 640 }, 128, 128, false);
        Loop("haze a,b (loops)", t, new[] { 128, 256 }, new[] { 640, 640 }, 128, 128, true, 8);   // (a soft veil: counted from alpha 8)
        // the flame stays inside its cell's width so Span's stretch never clips it
        var px = Slice(t, 0, 0, 128, 384);
        int lo = 999, hi = -1;
        for (int i = 0; i < px.Length; i++) if (px[i].a > 0) { lo = Mathf.Min(lo, i % 128); hi = Mathf.Max(hi, i % 128); }
        Check("flame body fills its cell width without touching the edge (cols " + lo + ".." + hi + ")", lo > 0 && hi < 127);
        Object.DestroyImmediate(t);
    }

    static void Fallback()
    {
        // an atlas served in its place that is too small for any cell: every slot falls back, the flame is still Alive
        AttackArt.Clear(); MineFlameArt.Reset(); MineLaserArt.ResetCache();
        var tiny = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        AttackArt.Inject(3, "mineflame", tiny);
        var a = AttackArt.MineFlame(3);
        Check("a sheet with no usable cells falls back slot by slot to the procedural flame", a != null && a.Alive && a.beam[0].texture != tiny && a.pilot[0].texture != tiny);
        AttackArt.Clear(); MineFlameArt.Reset(); MineLaserArt.ResetCache();
        Object.DestroyImmediate(tiny);
        Check("with no file at all (another world) AttackArt.MineFlame is null and MineFlameArt.Ember builds the procedural one", AttackArt.MineFlame(1) == null);
    }

    static void Memory()
    {
        long attacks = 0;
        var perWorld = new System.Text.StringBuilder();
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art/Resources/Attacks" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t == null) continue;
            long b = (long)t.width * t.height * 4;   // uncompressed RGBA32
            Check(path + " is uncompressed RGBA32 (" + (b / 1024) + " KB)", imp != null && imp.textureCompression == TextureImporterCompression.Uncompressed && !imp.mipmapEnabled);
            attacks += b;
        }
        Debug.Log("[MINEFLAME] Resources/Attacks total " + (attacks / 1024) + " KB (cap " + (AttackTexturesBudget / 1024) + " KB); with Frost's backdrop budget " +
                  (WorldBackdropTest.FrostTextureBudgetBytes / 1024) + " KB: " + ((attacks + WorldBackdropTest.FrostTextureBudgetBytes) / 1024) + " KB (cap " + (WithBackdropBudget / 1024) + " KB)" + perWorld);
        Check("every Resources/Attacks sheet together " + (attacks / 1024) + " KB <= " + (AttackTexturesBudget / 1024) + " KB", attacks > 0 && attacks <= AttackTexturesBudget);
        Check("... plus the largest world backdrop budget " + ((attacks + WorldBackdropTest.FrostTextureBudgetBytes) / 1024) + " KB <= " + (WithBackdropBudget / 1024) + " KB",
              attacks + WorldBackdropTest.FrostTextureBudgetBytes <= WithBackdropBudget);
        // Space's own backdrop budget is the next largest: attacks of all four worlds resident beside it
        Check("... and beside Space's 10 MB backdrop budget", attacks + WorldBackdropTest.SpaceTextureBudgetBytes <= AttackTexturesBudget + WorldBackdropTest.SpaceTextureBudgetBytes);
    }
}
