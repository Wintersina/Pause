using System.IO;
using UnityEditor;
using UnityEngine;

// No Space backdrop atlas cell may cut its art off. For every cell of every
// Space backdrop atlas (the planets, rocks, stations, asteroids and fx the
// directors draw): the rect lies inside the sheet, and the one-pixel ring
// just OUTSIDE the rect holds no opaque pixel (an opaque pixel there means
// the rect slices through a drawing: a planet's lower limb, a rock's side,
// "the rock was cut off in space world"). The neon_frames sheet shipped
// with a json for an older 1024 px layout over a 1536 x 1024 sheet, so its
// planet cells were windows through planet bottoms and the tops of the rocks
// below; BackdropDirectors drew them (45% of the planets) as flat-cut grey
// rocks. build_neon_frames.py now writes the json from the art.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod BackdropCellClipTest.Run
public static class BackdropCellClipTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[CLIP] PASS  " : "[CLIP] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string Dir = "Assets/Art/Backgrounds/Resources/Worlds/Space/Backdrop/";
    const byte Opaque = 40;
    static readonly string[] Atlases =
    {
        "anim", "anim_hires", "fx", "neon_frames", "asteroid_drift", "asteroid_drift_fx", "asteroid_fx", "comet_frames_v1",
    };
    // Cells whose art abuts a neighbour's by design / is never drawn.
    static bool Exempt(string n) { return n.StartsWith("neon_asteroid_"); }

    [System.Serializable] class R { public string n; public int x, y, w, h; }
    [System.Serializable] class M { public R[] sprites; public int sheetW, sheetH; }

    public static int Execute()
    {
        fails = 0;
        foreach (string atlas in Atlases) Atlas(atlas);
        NeonPlanets();
        Debug.Log("[CLIP] failures: " + fails);
        return fails;
    }

    static void Atlas(string name)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(Dir + name + ".png"));
        var m = JsonUtility.FromJson<M>(File.ReadAllText(Dir + name + ".json"));
        int W = tex.width, H = tex.height;
        float fx = m.sheetW > 0 ? W / (float)m.sheetW : 1f, fy = m.sheetH > 0 ? H / (float)m.sheetH : 1f;
        var px = tex.GetPixels32();
        int clipped = 0, outside = 0;
        string first = "";
        foreach (var r in m.sprites)
        {
            if (Exempt(r.n)) continue;
            int x0 = Mathf.RoundToInt(r.x * fx), y0 = Mathf.RoundToInt(r.y * fy);
            int w = Mathf.RoundToInt(r.w * fx), h = Mathf.RoundToInt(r.h * fy);
            if (x0 < 0 || y0 < 0 || x0 + w > W || y0 + h > H) { outside++; if (first == "") first = r.n + " outside"; continue; }
            int hit = 0;
            for (int y = y0 - 1; y <= y0 + h; y++)
                for (int x = x0 - 1; x <= x0 + w; x++)
                {
                    bool ring = x == x0 - 1 || x == x0 + w || y == y0 - 1 || y == y0 + h;
                    if (!ring || x < 0 || y < 0 || x >= W || y >= H) continue;
                    if (px[y * W + x].a > Opaque) hit++;
                }
            if (hit > 0) { clipped++; if (first == "") first = r.n + " (" + hit + " opaque px on its border ring)"; }
        }
        Object.DestroyImmediate(tex);
        Check(name + ": no cell is cut off by its rect" + (first == "" ? "" : " -- " + first), clipped == 0 && outside == 0);
    }

    // The planet cells the sphere shader turns are cut on the disc: square.
    static void NeonPlanets()
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(Dir + "neon_frames.png"));
        var m = JsonUtility.FromJson<M>(File.ReadAllText(Dir + "neon_frames.json"));
        var px = tex.GetPixels32();
        int W = tex.width, n = 0;
        foreach (var r in m.sprites)
        {
            if (!r.n.StartsWith("neon_planet_")) continue;
            n++;
            Check(r.n + " is a square cell", r.w == r.h);
            // the whole disc is inside: the inscribed circle's centre column / row are opaque
            Check(r.n + " has its centre opaque", px[(r.y + r.h / 2) * W + r.x + r.w / 2].a > Opaque);
        }
        Check("four neon planet cells", n == 4);
        Object.DestroyImmediate(tex);
        // the imported texture is not shrunk below the sheet the rects describe
        var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "neon_frames.png");
        Check("neon_frames imports at its full " + W + " px width", imported != null && imported.width == W);
    }
}
