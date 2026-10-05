using UnityEditor;
using UnityEngine;

// Import settings for the generated world backgrounds under
// Assets/Art/Resources/Worlds/<World>/Backdrop/, applied on every (re)import
// so a re-render can't drift from them.
//
//   sky/far/mid/flow, and any other texture a BackdropCatalog tile layer
//   names (Layer.WithTexture)
//                     Sprite (single). Seamless vertical tiles: wrap V =
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
    public const float TileUnits = 6f;                  // a tile is 6 units wide, whatever its pixel width
    public const float TilePixelsPerUnit = 512f / TileUnits;

    static bool IsBackdrop(string path)
    {
        return path.StartsWith("Assets/Art/Resources/Worlds/") && path.Contains("/Backdrop/") &&
               path.EndsWith(".png");
    }

    public static bool IsTile(string path)
    {
        string f = System.IO.Path.GetFileNameWithoutExtension(path);
        if (f == "sky" || f == "far" || f == "mid" || f == "flow") return true;
        // A tile layer may name its own texture. Without this it would be
        // imported as a plain texture, the layer's Resources.Load<Sprite>
        // would find nothing, and the whole world's backdrop would be dropped
        // as incomplete.
        foreach (var spec in BackdropCatalog.All)
        {
            if (!path.Contains("/Worlds/" + spec.world + "/Backdrop/")) continue;
            foreach (var layer in spec.layers)
                if (layer.kind != BackdropCatalog.Kind.Pieces && layer.texture == f) return true;
        }
        return false;
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
        ti.maxTextureSize = 1024;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.sRGBTexture = true;

        if (tile)
        {
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            int srcW, srcH;
            ti.GetSourceTextureWidthAndHeight(out srcW, out srcH);
            ti.spritePixelsPerUnit = srcW > 0 ? srcW / TileUnits : TilePixelsPerUnit;
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
