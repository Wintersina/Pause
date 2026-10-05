using UnityEditor;
using UnityEngine;

// Import settings for the generated world backgrounds under
// Assets/Art/Resources/Worlds/<World>/Backdrop/, applied on every (re)import
// so a re-render can't drift from them.
//
//   sky/far/mid/flow  Sprite (single). Seamless vertical tiles: wrap V =
//                     Repeat (bilinear at the seam samples the other edge,
//                     which is the continuation), wrap U = Clamp. sky is
//                     opaque (full-rect mesh); the alpha tiles use tight
//                     meshes so transparent areas aren't drawn (overdraw).
//   fx/anim atlases   Default texture; WorldBackdrop cuts sprites at
//                     runtime from the JSON manifest beside each atlas.
//
// All: no mipmaps (fixed ortho scale), bilinear, alpha-is-transparency
// (dilates colour under transparent pixels so soft edges don't fringe dark),
// compressed. Mobile: ASTC 6x6 (~0.9 bpp); desktop: DXT1/DXT5 automatic.
public class WorldBackdropImport : AssetPostprocessor
{
    public const float TilePixelsPerUnit = 512f / 6f;   // a tile is 6 units wide

    static bool IsBackdrop(string path)
    {
        return path.StartsWith("Assets/Art/Resources/Worlds/") && path.Contains("/Backdrop/") &&
               path.EndsWith(".png");
    }

    public static bool IsTile(string path)
    {
        string f = System.IO.Path.GetFileNameWithoutExtension(path);
        return f == "sky" || f == "far" || f == "mid" || f == "flow" ||
               f.StartsWith("forest_industrial_center_v");
    }

    void OnPreprocessTexture()
    {
        if (!IsBackdrop(assetPath)) return;
        var ti = (TextureImporter)assetImporter;
        string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        bool tile = IsTile(assetPath);

        ti.mipmapEnabled = false;
        ti.filterMode = FilterMode.Bilinear;
        ti.alphaIsTransparency = true;
        ti.isReadable = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.maxTextureSize = file.StartsWith("forest_industrial_center_v") ? 512 : 1024;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.sRGBTexture = true;

        if (tile)
        {
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = TilePixelsPerUnit;
            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            settings.spriteMeshType = file == "sky" ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteExtrude = 1;
            settings.wrapModeU = TextureWrapMode.Clamp;
            settings.wrapModeV = TextureWrapMode.Repeat;
            ti.SetTextureSettings(settings);
            ti.alphaSource = file == "sky" ? TextureImporterAlphaSource.None
                                           : TextureImporterAlphaSource.FromInput;
        }
        else
        {
            ti.textureType = TextureImporterType.Default;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
        }

        foreach (string platform in new[] { "Android", "iPhone" })
        {
            var ps = ti.GetPlatformTextureSettings(platform);
            ps.overridden = true;
            ps.maxTextureSize = 1024;
            ps.format = TextureImporterFormat.ASTC_6x6;
            ps.textureCompression = TextureImporterCompression.Compressed;
            ti.SetPlatformTextureSettings(ps);
        }
    }
}
