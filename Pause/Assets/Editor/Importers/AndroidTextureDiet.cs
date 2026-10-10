using System.Collections.Generic;
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Android texture size diet. Most shipping textures had no Android override
// and their "Default" tab said uncompressed (or the automatic format fell
// back to RGBA32), so a 2304x768 hull sheet shipped as 9.4 MB of raw pixels.
// This gives every sizeable, non-readable, shipping texture an explicit
// ASTC override for Android (and iPhone, same hardware class):
//
//   ASTC 4x4  (8 bpp)    anything with crisp pixel outlines: ships, guns,
//                        exhaust, enemies, bosses, elites, pickups, weapons, VFX, UI
//   ASTC 6x6  (3.56 bpp) soft painted art: backdrops, planetfall surfaces,
//                        the star field
//
// Other importers (WorldBackdropImport, PlanetfallArtImporter, ...) that
// already set an ASTC override keep theirs. Textures that are readable (code
// calls GetPixels on them) are left alone, as are small ones (< 128 px),
// textures in ~ folders (not imported at all) and Editor/Tests art.
// Runs after the other postprocessors (callbackOrder), on every (re)import;
// "Pause > Textures > Apply Android ASTC to all" re-imports what is stale.
public class AndroidTextureDiet : AssetPostprocessor
{
    public const int MinSide = 128;

    public override uint GetPostprocessOrder() { return 1000; }

    public static bool Eligible(string path)
    {
        if (!path.StartsWith("Assets/", StringComparison.Ordinal)) return false;
        if (path.Contains("~/") || path.Contains("/Editor/") || path.Contains("/Tests/")) return false;
        if (path.Contains("/Staging/") || path.Contains("/Plugins/")) return false;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga";
    }

    // 6x6 for soft painted art, 4x4 for everything else.
    public static TextureImporterFormat FormatFor(string path)
    {
        if (path.Contains("/Backgrounds/") || path.Contains("/Planetfall/")) return TextureImporterFormat.ASTC_6x6;
        return TextureImporterFormat.ASTC_4x4;
    }

    // True if the importer was changed.
    public static bool Apply(TextureImporter ti, string path)
    {
        if (!Eligible(path) || ti == null) return false;
        if (ti.isReadable) return false;
        if (ti.textureType != TextureImporterType.Default && ti.textureType != TextureImporterType.Sprite &&
            ti.textureType != TextureImporterType.GUI) return false;
        ti.GetSourceTextureWidthAndHeight(out int w, out int h);
        if (w < MinSide && h < MinSide) return false;

        bool changed = false;
        foreach (string platform in new[] { "Android", "iPhone" })
        {
            var ps = ti.GetPlatformTextureSettings(platform);
            if (ps.overridden && IsAstc(ps.format)) continue;      // another importer already chose
            ps.overridden = true;
            ps.format = FormatFor(path);
            ps.textureCompression = TextureImporterCompression.Compressed;
            ps.compressionQuality = 100;
            ps.maxTextureSize = ti.GetDefaultPlatformTextureSettings().maxTextureSize;   // never shrink
            ti.SetPlatformTextureSettings(ps);
            changed = true;
        }
        return changed;
    }

    public static bool IsAstc(TextureImporterFormat f)
    {
        return f >= TextureImporterFormat.ASTC_4x4 && f <= TextureImporterFormat.ASTC_12x12;
    }

    void OnPreprocessTexture()
    {
        Apply((TextureImporter)assetImporter, assetPath);
    }

    [MenuItem("Pause/Textures/Apply Android ASTC to all")]
    public static void ApplyAll()
    {
        var stale = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!Eligible(path)) continue;
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) continue;
            var before = ti.GetPlatformTextureSettings("Android");
            if (before.overridden && IsAstc(before.format)) continue;
            stale.Add(path);
        }
        Debug.Log("[DIET] " + stale.Count + " textures to re-import");
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (string p in stale) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
        }
        finally { AssetDatabase.StopAssetEditing(); }
        AssetDatabase.SaveAssets();
    }

    public static void ApplyAllAndExit()
    {
        ApplyAll();
        EditorApplication.Exit(0);
    }
}
