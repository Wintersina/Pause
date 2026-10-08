using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Space's turning planets (the anim atlas's giant_NN / rocky_NN cells under
// the BackdropPlanet shader) are ready for a higher-resolution sheet:
//   - a body's world size comes from SpaceDirector.KindWidth, never from its
//     cell's pixel size: a 2x copy of the sheet draws every body the same
//     size (same sprite bounds, same disc in uv);
//   - cell sizes come from the manifest (json rects), including a sheet the
//     importer delivered smaller than authored (sheetW/sheetH);
//   - the sheet samples smoothly: trilinear mips, never point, ASTC 4x4 on
//     phones, maximum size room for a 4096 re-render;
//   - the shader spreads the art over the globe close to 1:1 (_Period), the
//     smear that made big planets read as low resolution;
//   - an installed anim_hires carries the same cells as anim.
// Rotation on scaled time, the pause freeze and zero allocation are covered
// by WorldBackdropTest.CheckSpaceMotion.
public static class SpacePlanetSheetTest
{
    const string Dir = "Assets/Art/Backgrounds/Resources/Worlds/Space/Backdrop/";
    static int failures;

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PLANETSHEET] PASS  " : "[PLANETSHEET] FAIL  ") + what);
        if (!ok) failures++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    [System.Serializable] class Rect { public string n; public int x, y, w, h; }
    [System.Serializable] class Manifest { public Rect[] sprites; public float pixelScale; public int sheetW, sheetH; }

    public static int Execute()
    {
        failures = 0;
        CheckJsonCells();
        CheckScaleIndependence();
        CheckImport();
        CheckShader();
        CheckHires();
        Debug.Log("[PLANETSHEET] failures: " + failures);
        return failures;
    }

    static Texture2D LoadPng(string path)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(path));
        return tex;
    }

    static TextAsset Json(string text) { return new TextAsset(text); }

    // Every cell is cut exactly where anim.json says, at the json's own size.
    static void CheckJsonCells()
    {
        var tex = LoadPng(Dir + "anim.png");
        string text = File.ReadAllText(Dir + "anim.json");
        var m = JsonUtility.FromJson<Manifest>(text);
        var atlas = new BackdropAtlas(tex, Json(text));
        bool exact = true;
        int n = 0;
        foreach (var r in m.sprites)
        {
            var s = atlas.Get(r.n);
            n++;
            if (s == null || s.rect != new UnityEngine.Rect(r.x, r.y, r.w, r.h)) exact = false;
        }
        Check("anim.json drives every cell's rect and size (" + n + " cells; giants 12, rocks 4)",
              exact && atlas.Frames("giant").Length == 12 && atlas.Frames("rocky").Length == 4);
        Check("a 1x sheet keeps 100 pixels per unit", Mathf.Approximately(atlas.PixelScale, 1f) &&
              Mathf.Approximately(atlas.Get("giant_00").pixelsPerUnit, BackdropAtlas.PixelsPerUnit));
        atlas.Destroy();
        Object.DestroyImmediate(tex);
    }

    // A 2x copy of the sheet (texture and manifest), and a manifest authored
    // for 2x on a texture the importer capped back to 1x, both give every
    // cell the 1x sprite's bounds and disc: same world size, just more texels.
    static void CheckScaleIndependence()
    {
        var tex1 = LoadPng(Dir + "anim.png");
        string text1 = File.ReadAllText(Dir + "anim.json");
        var m = JsonUtility.FromJson<Manifest>(text1);
        var tex2 = new Texture2D(tex1.width * 2, tex1.height * 2, TextureFormat.RGBA32, false);
        var src = tex1.GetPixels32();
        var dst = new Color32[tex2.width * tex2.height];
        for (int y = 0; y < tex2.height; y++)
            for (int x = 0; x < tex2.width; x++)
                dst[y * tex2.width + x] = src[(y / 2) * tex1.width + x / 2];
        tex2.SetPixels32(dst);
        tex2.Apply();
        var m2 = new Manifest { pixelScale = 2f, sheetW = tex2.width, sheetH = tex2.height, sprites = new Rect[m.sprites.Length] };
        for (int i = 0; i < m.sprites.Length; i++)
        {
            var r = m.sprites[i];
            m2.sprites[i] = new Rect { n = r.n, x = r.x * 2, y = r.y * 2, w = r.w * 2, h = r.h * 2 };
        }
        string text2 = JsonUtility.ToJson(m2);
        var a1 = new BackdropAtlas(tex1, Json(text1));
        var a2 = new BackdropAtlas(tex2, Json(text2));
        var capped = new BackdropAtlas(tex1, Json(text2));     // authored 2x, delivered 1x
        float worstBounds = 0f, worstDisc = 0f, worstCap = 0f;
        bool sharper = true;
        foreach (var r in m.sprites)
        {
            Sprite s1 = a1.Get(r.n), s2 = a2.Get(r.n), sc = capped.Get(r.n);
            if (s1 == null || s2 == null || sc == null) { worstBounds = 999f; continue; }
            worstBounds = Mathf.Max(worstBounds, (s1.bounds.size - s2.bounds.size).magnitude);
            worstCap = Mathf.Max(worstCap, (s1.bounds.size - sc.bounds.size).magnitude);
            worstCap = Mathf.Max(worstCap, (s1.rect.size - sc.rect.size).magnitude);
            if (SpaceDirector.IsSphere(s1))
                worstDisc = Mathf.Max(worstDisc, (SpaceDirector.DiscOf(s1) - SpaceDirector.DiscOf(s2)).magnitude);
            sharper &= Mathf.Approximately(s2.rect.width, s1.rect.width * 2f);
        }
        Check("a 2x sheet gives every cell the 1x sprite's world size (worst " + worstBounds.ToString("E1") + " u)",
              worstBounds < 1e-4f && sharper);
        Check("a 2x sheet keeps every sphere's disc (centre, radius) in uv (worst " + worstDisc.ToString("E1") + ")",
              worstDisc < 1e-4f);
        Check("a sheet capped below its authored sheetW/H still cuts the same cells (worst " + worstCap.ToString("E1") + ")",
              worstCap < 1e-3f);

        // Drawn size through the director's own path (SetSprite: width in
        // units / sprite bounds), for a hero giant: identical from both sheets.
        float hero = SpaceDirector.KindWidth(SpaceDirector.Planet) * SpaceDirector.Tiers[SpaceDirector.Tiers.Length - 1].scale;
        var g1 = a1.Get("giant_00");
        var g2 = a2.Get("giant_00");
        float w1 = g1.bounds.size.x * (hero / g1.bounds.size.x), w2 = g2.bounds.size.x * (hero / g2.bounds.size.x);
        float h1 = g1.bounds.size.y * (hero / g1.bounds.size.x), h2 = g2.bounds.size.y * (hero / g2.bounds.size.x);
        Check("a hero giant is " + w1.ToString("F2") + " x " + h1.ToString("F2") + " u from the 1x sheet and " +
              w2.ToString("F2") + " x " + h2.ToString("F2") + " u from the 2x sheet",
              Mathf.Abs(w1 - w2) < 1e-4f && Mathf.Abs(h1 - h2) < 1e-4f);
        a1.Destroy(); a2.Destroy(); capped.Destroy();
        Object.DestroyImmediate(tex1);
        Object.DestroyImmediate(tex2);
    }

    static void CheckImport()
    {
        foreach (string file in new[] { BackdropCatalog.AtlasAnim, BackdropCatalog.AtlasAnimHires })
        {
            string path = Dir + file + ".png";
            if (!File.Exists(path)) continue;
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Check(file + " is a planet sheet for the importer", WorldBackdropImport.IsPlanetSheet(path));
            if (imp == null || tex == null) { Check(file + " imports", false); continue; }
            Check(file + " samples smoothly: trilinear, mipmapped, never point",
                  imp.filterMode == FilterMode.Trilinear && imp.mipmapEnabled && tex.filterMode != FilterMode.Point &&
                  tex.mipmapCount > 1);
            Check(file + " is not resampled on import (no NPOT scale, max size " + imp.maxTextureSize + " >= " +
                  Mathf.Max(tex.width, tex.height) + ")",
                  imp.npotScale == TextureImporterNPOTScale.None && imp.maxTextureSize >= WorldBackdropImport.PlanetSheetMaxSize);
            foreach (string platform in new[] { "Android", "iPhone" })
            {
                var ps = imp.GetPlatformTextureSettings(platform);
                Check(file + " on " + platform + ": ASTC 4x4, up to " + WorldBackdropImport.PlanetSheetMaxSize + " px",
                      ps.overridden && ps.format == TextureImporterFormat.ASTC_4x4 &&
                      ps.maxTextureSize >= WorldBackdropImport.PlanetSheetMaxSize);
            }
            // Phone memory of the sheet as imported, mip chain included.
            long astc = ((tex.width + 3) / 4) * ((tex.height + 3) / 4) * 16L * 4 / 3;
            Debug.Log("[PLANETSHEET] " + file + " " + tex.width + "x" + tex.height + ": ~" + (astc / 1024) +
                      " KB ASTC 4x4 with mips (1024: 1365 KB, 2048: 5461 KB, 4096x2048: 10923 KB; RGBA32 x4)");
        }
    }

    static void CheckShader()
    {
        var shader = Resources.Load<Shader>(SpaceDirector.PlanetShader);
        Check("BackdropPlanet shader loads", shader != null);
        if (shader == null) return;
        var mat = new Material(shader);
        float band = mat.GetFloat("_Band"), seam = mat.GetFloat("_Seam"), period = mat.GetFloat("_Period");
        float k = 2f * band / (1f + seam);       // radians of art per repeat
        float stretch = period / k;               // art longitude -> globe longitude
        // No feature may show twice inside the turning part of the disc:
        // the repeat must cover the visible face out to the rim keep (_Rim).
        float face = 2f * Mathf.Asin(mat.GetFloat("_Rim"));
        Check("planet surface is spread close to 1:1 (stretch " + stretch.ToString("F2") + " <= 1.35; it was pi / 1.83 = 1.71 when the art repeated every half turn)", stretch <= 1.35f && stretch >= 1f);
        Check("planet surface repeat " + period.ToString("F2") + " rad covers the turning face " + face.ToString("F2") + " rad",
              period >= face);
        Object.DestroyImmediate(mat);
    }

    // An installed hi-res re-render must be a drop-in for anim.
    static void CheckHires()
    {
        string png = Dir + BackdropCatalog.AtlasAnimHires + ".png";
        if (!File.Exists(png))
        {
            Debug.Log("[PLANETSHEET] NOTE  no anim_hires installed; runtime uses anim (docs/art-production-queue.md)");
            return;
        }
        Check("anim_hires.json sits beside anim_hires.png", File.Exists(Dir + BackdropCatalog.AtlasAnimHires + ".json"));
        var lo = JsonUtility.FromJson<Manifest>(File.ReadAllText(Dir + "anim.json"));
        var hi = JsonUtility.FromJson<Manifest>(File.ReadAllText(Dir + BackdropCatalog.AtlasAnimHires + ".json"));
        bool same = hi.sprites != null && hi.sprites.Length == lo.sprites.Length;
        for (int i = 0; same && i < lo.sprites.Length; i++) same = hi.sprites[i].n == lo.sprites[i].n;
        Check("anim_hires has anim's cells, names and order (" + hi.sprites.Length + ")", same);
        Check("anim_hires declares pixelScale >= 2 (" + hi.pixelScale + ")", hi.pixelScale >= 2f);
        var tex = LoadPng(png);
        float worst = 0f;
        var loByName = new Dictionary<string, Rect>();
        foreach (var r in lo.sprites) loByName[r.n] = r;
        foreach (var r in hi.sprites)
        {
            Rect l;
            if (!loByName.TryGetValue(r.n, out l)) continue;
            worst = Mathf.Max(worst, Mathf.Abs(r.w / hi.pixelScale - l.w), Mathf.Abs(r.h / hi.pixelScale - l.h));
        }
        Check("anim_hires cells are anim's at pixelScale (worst " + worst.ToString("F1") + " px of 1x art)", worst <= 4f);
        var atlas = new BackdropAtlas(tex, Json(File.ReadAllText(Dir + BackdropCatalog.AtlasAnimHires + ".json")));
        var loaded = BackdropSet.LoadAnimAtlas(BackdropCatalog.Folder("Space"));
        Check("the runtime loads anim_hires when installed", loaded.texture != null && loaded.texture.name == BackdropCatalog.AtlasAnimHires);
        loaded.Destroy();
        // Each sphere's disc fills its centred cut (as WorldBackdropTest checks for anim).
        float off = 0f;
        foreach (var r in hi.sprites)
        {
            var s = atlas.Get(r.n);
            if (s == null || !SpaceDirector.IsSphere(s)) continue;
            Vector4 d = SpaceDirector.DiscOf(s);
            int x0 = int.MaxValue, x1 = -1;
            var px = tex.GetPixels(r.x, r.y, r.w, r.h);
            for (int y = 0; y < r.h; y++)
                for (int x = 0; x < r.w; x++)
                    if (px[y * r.w + x].a >= 0.5f) { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); }
            float got = (x1 - x0 + 1) / 2f, want = d.z * tex.width;
            off = Mathf.Max(off, x1 < 0 ? 999f : Mathf.Abs(got - want) / hi.pixelScale);
        }
        Check("anim_hires discs fill their centred cuts (worst " + off.ToString("F1") + " px of 1x art <= 2)", off <= 2f);
        atlas.Destroy();
        Object.DestroyImmediate(tex);
    }
}
