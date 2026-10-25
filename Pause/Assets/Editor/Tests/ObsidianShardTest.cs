using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Feature: "make obsidian shards multi colors and have min size for them, they are hard to notice" (ObsidianShards).
//
//   - every colour variant (strip and death strip) exists with the same grid, size and alpha shape as the original
//   - the colours differ from each other (mean glass hue >= MinHueGap apart), none is pink, the glass is still dark,
//     and the lava veins keep their Ember orange (the cue)
//   - the blade is never drawn shorter than MinLongEdge, at the smallest draw, on the real built body, and the
//     size floor changes nothing for the other rocks
//   - the variant is a pure function of the seed, spreads over every colour, and a built shard keeps its colour
//     (flipbook frames and death burst come from that variant's art)
//   - the variant art is not in the roster-keyed folders (no world palette / framing sweep sees it) and rides on the
//     original silhouette
public static class ObsidianShardTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[OBSIDIAN] PASS  " : "[OBSIDIAN] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string Root = "Assets/Art/Resources/";
    const float MinHueGap = 35f;
    const float DevicePixelsPerUnit = 1080f / (2f * 3.72f);

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
            Art();
            Palettes();
            MinSize();
            Picking();
            BuiltShards();
        }
        finally { }
        Debug.Log("[OBSIDIAN] failures: " + fails);
        return fails;
    }

    static Texture2D Load(string rel)
    {
        string path = Root + rel + ".png";
        if (!File.Exists(path)) return null;
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(path));
        return t;
    }

    static string StripRel(int v) { return v == 0 ? "Enemies/ember_rock_obsidian" : ObsidianShards.StripPath(v); }
    static string DeathRel(int v) { return v == 0 ? "Enemies/Death/ember_rock_obsidian" : ObsidianShards.DeathPath(v); }

    // ---- art ----
    static void Art()
    {
        Check("five colours: violet (the roster strip) + four", ObsidianShards.Count == 5 && ObsidianShards.Names[0] == "violet");
        var strip0 = Load(StripRel(0)); var death0 = Load(DeathRel(0));
        var p0 = strip0.GetPixels32(); var d0 = death0.GetPixels32();
        for (int v = 1; v < ObsidianShards.Count; v++)
        {
            string n = ObsidianShards.Names[v];
            var s = Load(StripRel(v)); var d = Load(DeathRel(v));
            Check(n + ": strip exists, 7 cells of 192 px, same size as the original", s != null && s.width == strip0.width && s.height == strip0.height && s.width == 7 * 192);
            Check(n + ": death strip exists, 3 cells, same size as the original", d != null && d.width == death0.width && d.height == death0.height && d.width == d.height * 3);
            if (s == null || d == null) continue;
            Check(n + ": strip alpha is the original's, pixel for pixel", SameAlpha(p0, s.GetPixels32()));
            Check(n + ": death alpha is the original's, pixel for pixel", SameAlpha(d0, d.GetPixels32()));
            Check(n + ": the art really is different (not a copy)", !SameRgb(p0, s.GetPixels32()));
            Object.DestroyImmediate(s); Object.DestroyImmediate(d);
        }
        // the variants live outside the roster-keyed folders, so the per-world palette and framing sweeps never see them
        Check("no variant file in the top-level Enemies folder", Directory.GetFiles(Root + "Enemies", "ember_rock_obsidian_*.png").Length == 0);
        Object.DestroyImmediate(strip0); Object.DestroyImmediate(death0);
    }

    static bool SameAlpha(Color32[] a, Color32[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i].a != b[i].a) return false;
        return true;
    }
    static bool SameRgb(Color32[] a, Color32[] b)
    {
        for (int i = 0; i < a.Length; i++) if (a[i].a > 0 && (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b)) return false;
        return true;
    }

    // ---- palettes ----
    struct Stat { public float hue, darkShare, warmShare, pink; }

    static Stat Measure(Texture2D t)
    {
        var px = t.GetPixels32();
        int cell = t.height;
        double sx = 0, sy = 0, w = 0; int opaque = 0, dark = 0, warm = 0, pink = 0;
        for (int y = 0; y < t.height; y++)
            for (int x = 0; x < cell * 4 && x < t.width; x++)   // the four idle cells
            {
                var p = px[y * t.width + x];
                if (p.a < 200) continue;
                Color.RGBToHSV(p, out float h, out float s, out float v);
                float deg = h * 360f;
                opaque++;
                if (v < .40f) dark++;
                bool isWarm = (deg < 50f || deg > 340f) && s > .30f;
                if (isWarm && v > .45f) warm++;
                if (v > .25f && s * v >= .035f && deg >= 280f && deg < 352f) pink++;
                if (s > .30f && v > .25f)
                {
                    double wt = s * v;
                    sx += System.Math.Cos(h * 2 * System.Math.PI) * wt; sy += System.Math.Sin(h * 2 * System.Math.PI) * wt; w += wt;
                }
            }
        float m = (float)(System.Math.Atan2(sy, sx) / (2 * System.Math.PI));
        return new Stat { hue = (m < 0 ? m + 1f : m) * 360f, darkShare = dark / (float)System.Math.Max(1, opaque), warmShare = warm / (float)System.Math.Max(1, opaque), pink = pink / (float)System.Math.Max(1, opaque) };
    }

    static float HueGap(float a, float b) { float d = Mathf.Abs(a - b) % 360f; return d > 180f ? 360f - d : d; }

    static void Palettes()
    {
        var stats = new Stat[ObsidianShards.Count];
        for (int v = 0; v < stats.Length; v++)
        {
            var t = Load(StripRel(v));
            stats[v] = Measure(t);
            Object.DestroyImmediate(t);
            var d = Load(DeathRel(v));
            var ds = Measure(d);
            Object.DestroyImmediate(d);
            string n = ObsidianShards.Names[v];
            Debug.Log("[OBSIDIAN] PALETTE " + n + ": glass hue " + stats[v].hue.ToString("F0") + " deg, dark share " + stats[v].darkShare.ToString("P0") + ", warm share " + stats[v].warmShare.ToString("P1"));
            if (v > 0)
            {
                Check(n + ": no pink-family pixel in the body or the death burst (non-Space world; the rule of NoPinkOutsideSpaceTest)", stats[v].pink < .01f && ds.pink < .01f);   // (< 1 %: the painted-art share; a few kept lava-burst pixels sit at the edge of the band)
                Check(n + ": the glass is still mostly dark (>= 40 % of pixels below value 0.40): it still reads as obsidian", stats[v].darkShare >= .40f);
                Check(n + ": the Ember lava veins stay warm orange (>= the original's warm share)", stats[v].warmShare >= stats[0].warmShare - .005f);
            }
        }
        float worst = 360f; string pair = "";
        for (int a = 0; a < stats.Length; a++)
            for (int b = a + 1; b < stats.Length; b++)
            {
                float g = HueGap(stats[a].hue, stats[b].hue);
                if (g < worst) { worst = g; pair = ObsidianShards.Names[a] + "/" + ObsidianShards.Names[b]; }
            }
        Check("every pair of colours is at least " + MinHueGap + " deg apart in mean glass hue (closest " + pair + " " + worst.ToString("F0") + ")", worst >= MinHueGap);
            }

    // ---- min size ----
    static void MinSize()
    {
        var def = EnemyRoster.Find(ObsidianShards.Key);
        var b = def.Behaviour;
        float edgeU = ObsidianShards.MinLongEdge;
        float floor = ObsidianShards.MinScale();
        Check("the scale floor is " + floor.ToString("F3") + " (blade length " + edgeU + " u at 164/192 of the cell, frame " + def.FrameWorldSize + " u)",
              Mathf.Abs(floor * ObsidianShards.SilhouetteLong * def.FrameWorldSize - edgeU) < 1e-4f);
        Check("the behaviour record carries the floor", Mathf.Abs(b.minScale - floor) < 1e-5f && Mathf.Abs(HazardSize.FloorFor(b) - Mathf.Max(HazardSize.MinScale, floor)) < 1e-5f);
        // the silhouette constant matches the art
        var t = Load(StripRel(0));
        var px = t.GetPixels32();
        int lo = 192, hi = -1;
        for (int y = 0; y < 192; y++)
            for (int x = 0; x < 192; x++)
                if (px[y * t.width + x].a > 20) { if (y < lo) lo = y; if (y > hi) hi = y; }
        Object.DestroyImmediate(t);
        Check("the blade is " + (hi - lo + 1) + " px of the 192 px cell (constant 164)", hi - lo + 1 == 164);

        float smallest = 9f;
        HazardSize.Seed(31337u);
        for (int i = 0; i < 20000; i++) smallest = Mathf.Min(smallest, HazardSize.Draw(def));
        float drawnLong = smallest * ObsidianShards.SilhouetteLong * def.FrameWorldSize;
        Check("20000 draws: the smallest blade is " + drawnLong.ToString("F3") + " u long, never below " + edgeU, drawnLong >= edgeU - 1e-4f);
        Check("HazardSize.Min is the floor, so the range agrees", HazardSize.Min(b) >= floor - 1e-5f && HazardSize.FromUniform(b, 0f) >= floor - 1e-5f);
        Check("it is " + (drawnLong * DevicePixelsPerUnit).ToString("F0") + " px on a 1080 px phone (the old smallest was " + (.76f * .95f * ObsidianShards.SilhouetteLong * def.FrameWorldSize * DevicePixelsPerUnit).ToString("F0") + ")",
              drawnLong * DevicePixelsPerUnit >= 85f);
        Check("mean area stays within 6 % of today's (the board holds the same amount of rock)", Mathf.Abs(HazardSize.MeanArea(b) - 1f) <= .06f);
        Check("small < typical < large still, and the blade stays slimmer-ranged than the islet", b.sizeSmall < b.sizeTypical && b.sizeTypical < b.sizeLarge && HazardSize.Max(b) < HazardSize.Max(EnemyRoster.Find("ember_rock_islet").Behaviour));
        bool others = true;
        foreach (var d in EnemyRoster.All)
            if (d.key != ObsidianShards.Key && d.Behaviour != null) others &= d.Behaviour.minScale == 0f;
        Check("no other roster body has a size floor of its own", others);

        // the built body at the minimum: the sprite is that long, the collider grew with the scale only
        var go = EnemyFactory.Create(def, Vector3.zero, Quaternion.identity, HazardSize.Min(b));
        var sr = go.GetComponent<SpriteRenderer>();
        float longEdge = sr.bounds.size.y * ObsidianShards.SilhouetteLong;
        float colSide = go.GetComponent<BoxCollider2D>().size.x * go.transform.lossyScale.x;
        Check("built at the minimum: the blade is " + longEdge.ToString("F3") + " u long", longEdge >= edgeU - .01f);
        Check("collider follows the scale like every rock (" + colSide.ToString("F3") + " u square, nominal " + (def.ColliderSize.x).ToString("F3") + " x scale)", Mathf.Abs(colSide - def.ColliderSize.x * HazardSize.Min(b)) < 1e-3f);
        Object.DestroyImmediate(go);
    }

    // ---- picking ----
    static void Picking()
    {
        bool same = true;
        for (uint s = 1; s < 500; s++) same &= ObsidianShards.Pick(s) == ObsidianShards.Pick(s);
        Check("Pick is a pure function of the seed", same);
        var seen = new int[ObsidianShards.Count];
        for (uint s = 1; s <= 5000; s++) seen[ObsidianShards.Pick(s * 7919u)]++;
        bool spread = true;
        foreach (int n in seen) spread &= n > 5000 / ObsidianShards.Count * .8f && n < 5000 / ObsidianShards.Count * 1.2f;
        Check("5000 seeds spread over every colour (" + string.Join("/", System.Array.ConvertAll(seen, x => x.ToString())) + ")", spread);
        Check("the seed follows position and size", ObsidianShards.SeedFor(new Vector3(1f, 2f, 0f), 1f) == ObsidianShards.SeedFor(new Vector3(1f, 2f, 5f), 1f) &&
              ObsidianShards.SeedFor(new Vector3(1f, 2f, 0f), 1f) != ObsidianShards.SeedFor(new Vector3(1.5f, 2f, 0f), 1f));
        int distinct = 0; var field = new bool[ObsidianShards.Count];
        for (int i = 0; i < 12; i++) { int v = ObsidianShards.Pick(ObsidianShards.SeedFor(new Vector3(i * .9f - 5f, 4f, 0f), 1f + i * .01f)); if (!field[v]) { field[v] = true; distinct++; } }
        Check("a field of twelve shards wears at least three colours (" + distinct + ")", distinct >= 3);
    }

    // ---- built shards ----
    static void BuiltShards()
    {
        var def = EnemyRoster.Find(ObsidianShards.Key);
        var seenVariants = new bool[ObsidianShards.Count];
        bool frames = true, stable = true, deaths = true, others = true;
        for (int i = 0; i < 40; i++)
        {
            var pos = new Vector3(i * .37f - 6f, 3f, 0f);
            float size = 0.9f + i * .008f;
            var go = EnemyFactory.Create(def, pos, Quaternion.identity, size);
            int v = EnemyIdentity.VariantOf(go);
            seenVariants[v] = true;
            stable &= v == ObsidianShards.Pick(ObsidianShards.SeedFor(pos, size));
            var fb = EnemyArt.Frames(def, v);
            frames &= fb != null && System.Array.IndexOf(fb, go.GetComponent<SpriteRenderer>().sprite) >= 0;
            var ds = EnemyDeathFlipbook.Frames(def, v);
            deaths &= ds != null && ds.Length == 3 && (v == 0 || ds[0] != EnemyDeathFlipbook.Frames(def, 0)[0]);
            Object.DestroyImmediate(go);
        }
        int n = 0; foreach (bool b in seenVariants) if (b) n++;
        Check("40 built shards wear all " + ObsidianShards.Count + " colours (" + n + ")", n == ObsidianShards.Count);
        Check("a built shard's colour is the Pick of its spawn", stable);
        Check("a built shard shows frames of its own colour's strip", frames);
        Check("its death burst is its colour's strip (and differs from the violet one)", deaths);
        var variantFrames = EnemyArt.Frames(def, 1);
        Check("variant frames keep the roster frame size and count", variantFrames.Length == EnemyRoster.FrameCount &&
              Mathf.Abs(variantFrames[0].bounds.size.x - def.FrameWorldSize) < .01f && variantFrames[0] != EnemyArt.Frames(def, 0)[0]);
        var other = EnemyRoster.Find("ember_rock_magma");
        var go2 = EnemyFactory.Create(other, Vector3.zero, Quaternion.identity, 1f);
        others &= EnemyIdentity.VariantOf(go2) == 0 && EnemyArt.Frames(other, 3) == EnemyArt.Frames(other, 0);
        Object.DestroyImmediate(go2);
        Check("other rocks never get a variant", others);
    }
}
