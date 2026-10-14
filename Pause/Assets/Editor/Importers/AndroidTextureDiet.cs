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

    public override int GetPostprocessOrder() { return 1000; }

    public static bool Eligible(string path)
    {
        if (!path.StartsWith("Assets/", StringComparison.Ordinal)) return false;
        if (path.Contains("~/") || path.Contains("/Editor/") || path.Contains("/Tests/")) return false;
        if (path.Contains("/Staging/") || path.Contains("/Plugins/")) return false;
        if (path.StartsWith("Assets/Art/AppIcon/", StringComparison.Ordinal)) return false;   // launcher icons stay uncompressed
        if (WorldBackdropImport.IsSunDisc(path)) return false;   // the Space sun's disc is hard pixel art: RGBA32 on phones (WorldBackdropImport)
        if (path.StartsWith("Assets/Art/UI/", StringComparison.Ordinal) &&
            !path.StartsWith("Assets/Art/UI/Title/", StringComparison.Ordinal) &&
            !path.StartsWith("Assets/Art/UI/Splash/", StringComparison.Ordinal)) return false;   // UI keeps its (test-guarded) import settings; only the big title / splash art is diet material
        // The Options volume icons / slider sprites: tiny pixel art that must stay uncompressed (ASTC blurs the 2x2 pixels).
        if (path.StartsWith("Assets/Art/Resources/Options/", StringComparison.Ordinal)) return false;
        // Small crisp logo with text: stays uncompressed (a few KB; ASTC at 6x6 would smear the lettering).
        if (path == "Assets/Art/UI/Splash/HapticGate.png") return false;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga";
    }

    // 6x6 for soft painted art, 4x4 for everything else.
    public static TextureImporterFormat FormatFor(string path)
    {
        if (path.Contains("/Backgrounds/") || path.Contains("/Planetfall/")) return TextureImporterFormat.ASTC_6x6;
        if (path.StartsWith("Assets/Art/UI/", StringComparison.Ordinal)) return TextureImporterFormat.ASTC_6x6;
        // Hard 1 px outlines (6x6 visibly blocks them in previews): ship hulls, guns, skins, exhaust, damage fx, shield, rails, weapons, attack fx, boss shots, the boss hit bursts / cracks / damaged attack poses.
        if (path.Contains("/ShipArt/") || path.Contains("/Shield/") || path.Contains("/Worlds/rail_") ||
            path.Contains("/Resources/Worlds/") ||
            path.Contains("/Resources/Weapons/") || path.Contains("/Resources/Attacks/") || path.Contains("/Resources/BossAttackFx/") || path.Contains("/Resources/BossImpact/") || path.EndsWith("_shots.png", StringComparison.Ordinal))
            return TextureImporterFormat.ASTC_4x4;
        // Painted / glowing / soft art (previews clean at 6x6): bosses, enemies, elites, pickups, vfx, gate, title and splash.
        return TextureImporterFormat.ASTC_6x6;
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
        // Unity silently keeps NPOT + mipmaps as RGBA32; the big title / splash art is only ever minified ~2x.
        if (ti.mipmapEnabled && ti.npotScale == TextureImporterNPOTScale.None && (!Mathf.IsPowerOfTwo(w) || !Mathf.IsPowerOfTwo(h))) { ti.mipmapEnabled = false; changed = true; }
        foreach (string platform in new[] { "Android", "iPhone" })
        {
            var ps = ti.GetPlatformTextureSettings(platform);
            if (ps.overridden && IsAstc(ps.format) && OwnedByOtherImporter(path)) continue;   // that importer already chose
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

    // Folders whose own importer sets the ASTC override (WorldBackdropImport, PlanetfallArtImporter, ShipArtImporter).
    static bool OwnedByOtherImporter(string path)
    {
        return path.Contains("/Backgrounds/") || path.Contains("/Planetfall/");
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
            ti.GetSourceTextureWidthAndHeight(out int sw, out int sh);
            bool npotMips = ti.mipmapEnabled && ti.npotScale == TextureImporterNPOTScale.None && !ti.isReadable && (!Mathf.IsPowerOfTwo(sw) || !Mathf.IsPowerOfTwo(sh)) && (sw >= MinSide || sh >= MinSide);
            if (!npotMips && before.overridden && (before.format == FormatFor(path) || (OwnedByOtherImporter(path) && IsAstc(before.format)))) continue;
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
        // Anything that still is not block compressed (Unity silently keeps NPOT + mipmaps as RGBA32).
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null || !Eligible(path) || ti.isReadable) continue;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null && tex.width >= MinSide && !IsAstc2(tex.format))
                Debug.Log("[DIETFAIL] " + path + " format=" + tex.format + " " + tex.width + "x" + tex.height + " mips=" + tex.mipmapCount);
        }
    }

    static bool IsAstc2(TextureFormat f) { return f.ToString().StartsWith("ASTC"); }

    // Re-imports every backdrop / planetfall texture (their importers set the ASTC format,
    // which does not re-run on its own when that importer code changes).
    public static void ReimportBackgroundsAndExit()
    {
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art/Backgrounds" }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
        }
        finally { AssetDatabase.StopAssetEditing(); }
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }

    public static void ApplyAllAndExit()
    {
        ApplyAll();
        EditorApplication.Exit(0);
    }
}
