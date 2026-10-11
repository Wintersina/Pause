using System.IO;
using UnityEditor;
using UnityEngine;

// Before / after previews for the Android ASTC diet. "Before" is the source PNG
// decoded straight from disk, "after" is the imported (ASTC) texture blitted
// through the GPU. Writes side-by-side PNGs (source | ASTC | difference x6) at 3x
// zoom and at game scale (0.45x, bilinear), and logs PSNR and the worst errors.
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod TextureDietPreview.Run
//   (RunThenBuild: apply the diet, write previews, then BuildScript.BuildAndroidRelease)
public static class TextureDietPreview
{
    static readonly string[] Targets =
    {
        "ShipArt/Hulls/Ninja", "ShipArt/Hulls/UFO", "ShipArt/Hulls/CrimsonHalo",
        "Elites/Frost/frost_elite_rimebreaker", "Bosses/Space", "Worlds/Space/rail_space_wide_v1",
        "ShipArt/Exhaust/exhaust_atlas", "Pickups/heal_atom_green", "HapticGate/industrial_gate_v3", "HapticGate/gate_cracks_1",
        "Bosses/Frost_shots", "Enemies/frost_alien", "Weapons/Explosions", "Weapons/AttackFx", "Vfx/teleport_portal_atlas",
        "Bosses/Tide_damage_fx", "Bosses/Ember", "Worlds/Ember/Backdrop3/v1/mid", "Worlds/Frost/Backdrop3/v1/sky", "Worlds/Ember/Backdrop3/v1/far", "Elites/Tide/tide_elite_riptide", "HapticGate/gate_steam",
        "Assets/Art/UI/Title/pause_title_2.png", "Assets/Art/UI/Splash/HapticGate_title_red.png",
    };

    public static void Run()
    {
        Previews();
        EditorApplication.Exit(0);
    }

    public static void RunThenBuild()
    {
        AndroidTextureDiet.ApplyAll();
        // Force the sprite sheets through the importer again and report what they became.
        foreach (string g in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art/Resources/ShipArt" }))
        {
            string ap = AssetDatabase.GUIDToAssetPath(g);
            if (!AndroidTextureDiet.Eligible(ap)) continue;
            AssetDatabase.ImportAsset(ap, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }
        var ninja = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Resources/ShipArt/Hulls/Ninja.png");
        var nti = (TextureImporter)AssetImporter.GetAtPath("Assets/Art/Resources/ShipArt/Hulls/Ninja.png");
        var nps = nti.GetPlatformTextureSettings("Android");
        Debug.Log("[DIETPREV] Ninja after force: tex.format=" + ninja.format + " graphicsFormat=" + ninja.graphicsFormat + " override=" + nps.overridden + " fmt=" + nps.format + " readable=" + nti.isReadable + " target=" + EditorUserBuildSettings.activeBuildTarget);
        try { Previews(); } catch (System.Exception e) { Debug.LogError("[DIETPREV] " + e); }
        BuildScript.BuildAndroidRelease();
    }

    static void Previews()
    {
        string outDir = System.Environment.GetEnvironmentVariable("PAUSE_PREVIEW_DIR");
        if (string.IsNullOrEmpty(outDir)) outDir = "/private/tmp/claude-501/previews";
        Directory.CreateDirectory(outDir);
        foreach (string res0 in Targets)
        {
            string res = res0;
            string assetPath = null;
            Texture2D asset;
            if (res.StartsWith("Assets/")) { assetPath = res; asset = AssetDatabase.LoadAssetAtPath<Texture2D>(res); res = Path.GetFileNameWithoutExtension(res); }
            else
            {
                asset = Resources.Load<Texture2D>(res);
                foreach (string g in AssetDatabase.FindAssets(Path.GetFileName(res) + " t:Texture2D"))
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    if (p.Contains("/Resources/" + res + ".")) assetPath = p;
                }
            }
            if (asset == null || assetPath == null) { Debug.LogWarning("[DIETPREV] missing " + res); continue; }
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            src.LoadImage(File.ReadAllBytes(assetPath));
            int w = src.width, h = src.height;
            var a = Read(src, w, h);
            var b = Read(asset, w, h);
            double se = 0; int maxA = 0, maxC = 0;
            var diff = new Color32[w * h];
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].a == 0 && b[i].a == 0) continue;     // RGB under fully transparent pixels is invisible
                int dr = Mathf.Abs(a[i].r - b[i].r), dg = Mathf.Abs(a[i].g - b[i].g), db = Mathf.Abs(a[i].b - b[i].b), da = Mathf.Abs(a[i].a - b[i].a);
                se += dr * dr + dg * dg + db * db + da * da;
                if (da > maxA) maxA = da;
                int m = Mathf.Max(dr, Mathf.Max(dg, db)); if (m > maxC) maxC = m;
                int d = Mathf.Min(255, Mathf.Max(m, da) * 6);
                diff[i] = new Color32((byte)d, (byte)d, (byte)d, 255);
            }
            double mse = se / (a.Length * 4.0);   // (all pixels in the denominator: comparable across sheets)
            double psnr = mse <= 0 ? 99 : 10 * System.Math.Log10(255.0 * 255.0 / mse);
            string name = res.Replace('/', '_');
            Debug.Log("[DIETPREV] " + res + " " + w + "x" + h + " format=" + asset.format + " PSNR=" + psnr.ToString("F1") + " dB maxAlphaErr=" + maxA + " maxColourErr=" + maxC);
            int cw = Mathf.Min(w, 384), ch = Mathf.Min(h, 256), cx = (w - cw) / 2, cy = (h - ch) / 2;
            Save(outDir, name + "_zoom.png", Triple(a, b, diff, w, cx, cy, cw, ch, 3));
            Bg = new Color32(235, 235, 220, 255);
            Save(outDir, name + "_zoom_bright.png", Triple(a, b, diff, w, cx, cy, cw, ch, 3));
            Bg = new Color32(40, 40, 52, 255);
            int gw, gh;
            var ga = Area(a, w, h, 0.45f, out gw, out gh);   // before: what a mip chain would give (area average)
            var gb = Down(b, w, h, 0.45f, out gw, out gh);
            Save(outDir, name + "_game.png", Triple(ga, gb, null, gw, 0, 0, Mathf.Min(gw, 300), Mathf.Min(gh, 200), 2));
            Object.DestroyImmediate(src);
        }
    }

    static Color32[] Area(Color32[] s, int w, int h, float k, out int nw, out int nh)
    {
        nw = Mathf.Max(1, Mathf.RoundToInt(w * k)); nh = Mathf.Max(1, Mathf.RoundToInt(h * k));
        var o = new Color32[nw * nh];
        for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++)
            {
                int x0 = Mathf.FloorToInt(x / k), x1 = Mathf.Min(w, Mathf.Max(x0 + 1, Mathf.CeilToInt((x + 1) / k)));
                int y0 = Mathf.FloorToInt(y / k), y1 = Mathf.Min(h, Mathf.Max(y0 + 1, Mathf.CeilToInt((y + 1) / k)));
                float r = 0, g = 0, bl = 0, al = 0; int n = 0;
                for (int yy = y0; yy < y1; yy++)
                    for (int xx = x0; xx < x1; xx++)
                    {
                        var c = s[yy * w + xx]; float wa = c.a / 255f;
                        r += c.r * wa; g += c.g * wa; bl += c.b * wa; al += wa; n++;
                    }
                o[y * nw + x] = al <= 0 ? new Color32(0, 0, 0, 0)
                    : new Color32((byte)(r / al), (byte)(g / al), (byte)(bl / al), (byte)Mathf.Clamp(al / n * 255f, 0, 255));
            }
        return o;
    }

    static Color32[] Read(Texture tex, int w, int h)
    {
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prev = RenderTexture.active;
        Graphics.Blit(tex, rt);
        RenderTexture.active = rt;
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
        t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var px = t.GetPixels32();
        Object.DestroyImmediate(t);
        return px;
    }

    static Color32[] Down(Color32[] s, int w, int h, float k, out int nw, out int nh)
    {
        nw = Mathf.Max(1, Mathf.RoundToInt(w * k)); nh = Mathf.Max(1, Mathf.RoundToInt(h * k));
        var o = new Color32[nw * nh];
        for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++)
            {
                float fx = (x + 0.5f) / k - 0.5f, fy = (y + 0.5f) / k - 0.5f;
                int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, w - 1), y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, h - 1);
                int x1 = Mathf.Min(x0 + 1, w - 1), y1 = Mathf.Min(y0 + 1, h - 1);
                float tx = Mathf.Clamp01(fx - x0), ty = Mathf.Clamp01(fy - y0);
                o[y * nw + x] = Color32.Lerp(Color32.Lerp(s[y0 * w + x0], s[y0 * w + x1], tx), Color32.Lerp(s[y1 * w + x0], s[y1 * w + x1], tx), ty);
            }
        return o;
    }

    static Color32 Bg = new Color32(40, 40, 52, 255);
    static Texture2D Triple(Color32[] a, Color32[] b, Color32[] d, int w, int cx, int cy, int cw, int ch, int zoom)
    {
        int panels = d == null ? 2 : 3;
        var t = new Texture2D(cw * zoom * panels + 8 * (panels - 1), ch * zoom, TextureFormat.RGBA32, false);
        var fill = new Color32[t.width * t.height];
        for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(255, 0, 255, 255);
        t.SetPixels32(fill);
        for (int p = 0; p < panels; p++)
        {
            var src = p == 0 ? a : p == 1 ? b : d;
            for (int y = 0; y < ch * zoom; y++)
                for (int x = 0; x < cw * zoom; x++)
                {
                    var c = src[(cy + y / zoom) * w + cx + x / zoom];
                    if (p < 2)
                    {
                        float al = c.a / 255f;
                        c = new Color32((byte)(c.r * al + Bg.r * (1 - al)), (byte)(c.g * al + Bg.g * (1 - al)), (byte)(c.b * al + Bg.b * (1 - al)), 255);
                    }
                    t.SetPixel(p * (cw * zoom + 8) + x, y, c);
                }
        }
        t.Apply();
        return t;
    }

    static void Save(string dir, string file, Texture2D t)
    {
        File.WriteAllBytes(Path.Combine(dir, file), t.EncodeToPNG());
        Object.DestroyImmediate(t);
    }
}
