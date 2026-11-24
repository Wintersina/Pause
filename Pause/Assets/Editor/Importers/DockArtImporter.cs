using UnityEditor;
using UnityEngine;

// Import settings for the Space Dock's art under Assets/Art/Backgrounds/Resources/Worlds/Dock/, applied on every
// (re)import so a re-render can't drift from them:
//
//   <world>_rot_bare   the destination planets: a 4x4 atlas of 16 rotation frames (4096x4096 final, 1024 px frames;
//                      the first drop is 2048x2048 of 512 px frames, same layout)
//   space_sun_anim     the orange sun's flipbook (4096x3072, a 4x3 grid of 1024 px frames, row-major)
//   warp_streaks       the 18-frame star-streak art (576x1024 frames; a 6x3 grid, 3456x3072)
//
// All pixel art: POINT filter, no mipmaps (fixed ortho scale), Clamp, never NPOT-scaled, not readable, alpha-is-
// transparency. Max size 4096 for the planet atlases (never downscaled on a phone: 6x6 ASTC is 7.5 MB each, at most
// two are ever resident -- PlanetFlipbooks) and 16384 for the rest (a strip layout is wider than 8192); mobile: ASTC
// 6x6 for the planets (hard-alpha globes), ASTC 4x4 for the sun and the streak lines.
public class DockArtImporter : AssetPostprocessor
{
    public const string Folder = "Assets/Art/Backgrounds/Resources/Worlds/Dock/";
    public const int PlanetMaxSize = 4096, OtherMaxSize = 16384;

    public static bool IsDock(string path)
    {
        return path.StartsWith(Folder, System.StringComparison.Ordinal) && path.EndsWith(".png", System.StringComparison.Ordinal);
    }

    public static bool IsPlanet(string path)
    {
        return path.EndsWith("_rot_bare.png", System.StringComparison.Ordinal);
    }

    void OnPreprocessTexture()
    {
        if (!IsDock(assetPath)) return;
        Apply((TextureImporter)assetImporter, assetPath);
    }

    public static void Apply(TextureImporter ti, string path)
    {
        bool planet = IsPlanet(path);
        int max = planet ? PlanetMaxSize : OtherMaxSize;
        ti.textureType = TextureImporterType.Default;
        ti.mipmapEnabled = false;
        ti.filterMode = FilterMode.Point;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.alphaIsTransparency = true;
        ti.isReadable = false;
        ti.sRGBTexture = true;
        ti.maxTextureSize = max;
        ti.textureCompression = TextureImporterCompression.Compressed;

        var defaults = ti.GetPlatformTextureSettings("DefaultTexturePlatform");
        defaults.maxTextureSize = max;
        defaults.textureCompression = TextureImporterCompression.Compressed;
        ti.SetPlatformTextureSettings(defaults);

        foreach (string platform in new[] { "Android", "iPhone" })
        {
            var ps = ti.GetPlatformTextureSettings(platform);
            ps.overridden = true;
            ps.maxTextureSize = max;
            ps.textureCompression = TextureImporterCompression.Compressed;
            ps.format = planet ? TextureImporterFormat.ASTC_6x6 : TextureImporterFormat.ASTC_4x4;
            ti.SetPlatformTextureSettings(ps);
        }
    }
}
