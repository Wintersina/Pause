using UnityEditor;
using UnityEngine;

// Import settings for the planetfall art (Planetfall: the descent onto a
// planet that replaces a portal) under
// Assets/Art/Backgrounds/Resources/Worlds/<World>/Planetfall/, applied on
// every (re)import so a re-render can't drift from them. The sources and
// generator are in Assets/Art/Worlds/<World>/descent/src~.
//
//   *cloud_deck*, *_streaks   wrap Repeat: they tile, and PlanetfallLayer
//                             scrolls their uv across one quad (bilinear at
//                             the seam samples the continuation)
//   everything else           wrap Clamp
//
// All: Default texture type (PlanetfallArt cuts the sprites at runtime from
// PlanetfallDef's pixel numbers), FULL size -- the burst sheet is 5120 wide,
// so the cap is 8192, and nothing is ever NPOT-scaled (the sheets are 3072
// and 5120 wide) -- no mipmaps (fixed ortho scale), bilinear (the art is
// drawn from 0.5x to 3x), alpha-is-transparency, not readable. Mobile: ASTC
// 4x4 for the fine fx (plasma, burst, streak lines), 6x6 for the big
// painted surfaces.
public class PlanetfallArtImporter : AssetPostprocessor
{
    public const string Root = "Assets/Art/Backgrounds/Resources/Worlds/";
    public const string Folder = "/Planetfall/";
    public const int MaxSize = 8192;

    public static bool IsPlanetfall(string path)
    {
        return path.StartsWith(Root, System.StringComparison.Ordinal) && path.Contains(Folder) &&
               path.EndsWith(".png", System.StringComparison.Ordinal);
    }

    public static bool Tiles(string file)
    {
        return file.Contains("cloud_deck") || file.EndsWith("_streaks", System.StringComparison.Ordinal);
    }

    public static bool FineFx(string file)
    {
        return file.EndsWith("_entry_fx", System.StringComparison.Ordinal) ||
               file.EndsWith("_breakthrough", System.StringComparison.Ordinal) ||
               file.EndsWith("_streaks", System.StringComparison.Ordinal);
    }

    void OnPreprocessTexture()
    {
        if (!IsPlanetfall(assetPath)) return;
        var ti = (TextureImporter)assetImporter;
        string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);

        ti.textureType = TextureImporterType.Default;
        ti.mipmapEnabled = false;
        ti.filterMode = FilterMode.Bilinear;
        ti.wrapMode = Tiles(file) ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.alphaIsTransparency = true;
        ti.isReadable = false;
        ti.sRGBTexture = true;
        ti.maxTextureSize = MaxSize;
        ti.textureCompression = TextureImporterCompression.Compressed;

        var defaults = ti.GetPlatformTextureSettings("DefaultTexturePlatform");
        defaults.maxTextureSize = MaxSize;
        defaults.textureCompression = TextureImporterCompression.Compressed;
        ti.SetPlatformTextureSettings(defaults);

        foreach (string platform in new[] { "Android", "iPhone" })
        {
            var ps = ti.GetPlatformTextureSettings(platform);
            ps.overridden = true;
            ps.maxTextureSize = MaxSize;
            ps.format = FineFx(file) ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6;
            ps.textureCompression = TextureImporterCompression.Compressed;
            ti.SetPlatformTextureSettings(ps);
        }
    }
}
