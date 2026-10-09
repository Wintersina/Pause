using UnityEditor;
using UnityEngine;

// Import settings for the generated world backgrounds under
// Assets/Art/Backgrounds/Resources/Worlds/<World>/Backdrop/, applied on every (re)import
// so a re-render can't drift from them.
//
//   sky/far/mid/flow, and any other texture a BackdropCatalog tile layer
//   names (Layer.WithTexture)
//                     Sprite (single). Seamless vertical tiles: wrap V =
//                     Repeat (bilinear at the seam samples the other edge,
//                     which is the continuation), wrap U = Clamp. sky is
//                     opaque (full-rect mesh); the alpha tiles use tight
//                     meshes so transparent areas aren't drawn (overdraw).
//   <World>/Backdrop3/ (Frost, Verdant, Ember) the same rules for the v1..v4 tile sets and atlases
//                     (landmarks, sites, weather and the ambient loops),
//                     point-sampled except the soft atlases (IsSoftAtlas).
//   fx/anim atlases   Default texture; WorldBackdrop cuts sprites at
//                     runtime from the JSON manifest beside each atlas.
//
// All: no mipmaps (fixed ortho scale), bilinear, alpha-is-transparency
// (dilates colour under transparent pixels so soft edges don't fringe dark),
// compressed. Mobile: ASTC 6x6 (3.56 bpp); desktop: DXT1/DXT5 automatic.
//
// Except Space's planet sheet (anim, or its high-resolution re-render
// anim_hires, IsPlanetSheet): its cells are drawn from ~0.4x (deep giants)
// to ~2.8x (hero giants) and their surface slides under the BackdropPlanet
// shader, so it keeps mipmaps with trilinear filtering (no shimmer when
// small; the shader picks the level) and ASTC 4x4 on phones: 6x6 visibly
// drops the station's lamp pixels (PSNR 33 vs 39 dB on anim.png).
// anim_hires may be up to PlanetSheetMaxSize.
public class WorldBackdropImport : AssetPostprocessor
{
    public const float TilePixelsPerUnit = 512f / 6f;   // a tile is 6 units wide
    public const int PlanetSheetMaxSize = 4096;

    public static bool IsPlanetSheet(string path)
    {
        if (!path.Contains("/Worlds/Space/Backdrop/")) return false;
        string f = System.IO.Path.GetFileNameWithoutExtension(path);
        return f == BackdropCatalog.AtlasAnim || f == BackdropCatalog.AtlasAnimHires;
    }

    static bool IsBackdrop(string path)
    {
        return path.StartsWith("Assets/Art/Backgrounds/Resources/Worlds/") &&
               (path.Contains("/Backdrop/") || IsBackdrop3(path)) && path.EndsWith(".png");
    }

    // Frost's, Verdant's and Ember's v3 backdrops (Spec.folder "Worlds/<World>/Backdrop3/"):
    // variant tile sets in v1..v4/, shared atlases beside them.
    public static bool IsBackdrop3(string path) { return path.Contains("/Backdrop3/"); }

    // Backdrop3 art is pixel art drawn near 1:1 (landmarks, sites, lights)
    // or ~2x (tiles): point-sampled so it stays crisp. Its soft atlases
    // (clouds, mist, snow sheets, smoke, steam, aurora) are drawn several
    // times their pixel size and keep bilinear filtering.
    public static bool IsSoftAtlas(string file)
    {
        return file == "weather" || file == "smoke" || file == "steam" || file == "aurora" || file == "firesmoke" || file == "eruption";
    }

    public static bool IsTile(string path)
    {
        string f = System.IO.Path.GetFileNameWithoutExtension(path);
        if (f == "sky" || f == "far" || f == "mid" || f == "flow") return true;
        // Space's per-run sky variants (sky_01..04, SpaceSkySelection).
        if (f.StartsWith("sky_", System.StringComparison.Ordinal)) return true;
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

    static bool IsStandaloneSprite(string file)
    {
        return file.StartsWith("asteroid_", System.StringComparison.Ordinal) ||
               file == "reference_planet" || file == "comet_v2" || file == "station_ring_v2";
    }

    public static void ReimportSpaceStandaloneSprites()
    {
        const string folder = "Assets/Art/Backgrounds/Resources/Worlds/Space/Backdrop/";
        for (int i = 0; i < 3; i++)
            AssetDatabase.ImportAsset(folder + "asteroid_" + i.ToString("00") + ".png",
                                      ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(folder + "reference_planet.png", ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(folder + "comet_v2.png", ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(folder + "station_ring_v2.png", ImportAssetOptions.ForceUpdate);
        foreach (string atlas in new[] { "extras", "neon_frames", "asteroid_fx", "comet_frames_v1" })
            AssetDatabase.ImportAsset(folder + atlas + ".png", ImportAssetOptions.ForceUpdate);
    }

    void OnPreprocessTexture()
    {
        if (!IsBackdrop(assetPath)) return;
        var ti = (TextureImporter)assetImporter;
        string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        bool tile = IsTile(assetPath);
        bool sharpSpaceSky = assetPath.Contains("/Worlds/Space/") &&
                             file.StartsWith("sky_", System.StringComparison.Ordinal);

        bool planetSheet = IsPlanetSheet(assetPath);
        ti.mipmapEnabled = planetSheet;
        if (planetSheet) ti.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
        bool crisp = sharpSpaceSky || (IsBackdrop3(assetPath) && !IsSoftAtlas(file));
        ti.filterMode = crisp ? FilterMode.Point : planetSheet ? FilterMode.Trilinear : FilterMode.Bilinear;
        ti.alphaIsTransparency = true;
        ti.isReadable = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.maxTextureSize = planetSheet ? PlanetSheetMaxSize : file == "comet_v2" || file == "comet_frames_v1" || file == "station_ring_v2" ? 512 : file == "reference_planet" ? 1024 :
            sharpSpaceSky || (!tile && assetPath.Contains("/Worlds/Space/")) ? 2048 : 1024;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.sRGBTexture = true;

        if (tile || IsStandaloneSprite(file))
        {
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = tile ? TilePixelsPerUnit : BackdropAtlas.PixelsPerUnit;
            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            settings.spriteMeshType = file == "sky" || sharpSpaceSky ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteExtrude = 1;
            settings.wrapModeU = TextureWrapMode.Clamp;
            settings.wrapModeV = tile ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            ti.SetTextureSettings(settings);
            ti.alphaSource = file == "sky" || sharpSpaceSky ? TextureImporterAlphaSource.None
                                           : TextureImporterAlphaSource.FromInput;
        }
        else
        {
            ti.textureType = TextureImporterType.Default;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
        }

        // (switching the texture type can turn mipmaps back on for a fresh import)
        ti.mipmapEnabled = planetSheet;

        if (!tile && assetPath.Contains("/Worlds/Space/"))
        {
            var defaults = ti.GetPlatformTextureSettings("DefaultTexturePlatform");
            defaults.maxTextureSize = planetSheet ? PlanetSheetMaxSize : file == "comet_v2" || file == "comet_frames_v1" || file == "station_ring_v2" ? 512 : file == "reference_planet" ? 1024 : 2048;
            ti.SetPlatformTextureSettings(defaults);
        }

        foreach (string platform in new[] { "Android", "iPhone" })
        {
            var ps = ti.GetPlatformTextureSettings(platform);
            ps.overridden = true;
            ps.maxTextureSize = planetSheet ? PlanetSheetMaxSize : file == "comet_v2" || file == "comet_frames_v1" || file == "station_ring_v2" ? 512 : file == "reference_planet" ? 1024 :
                sharpSpaceSky || (!tile && assetPath.Contains("/Worlds/Space/")) ? 2048 : 1024;
            ps.format = planetSheet ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6;
            ps.textureCompression = TextureImporterCompression.Compressed;
            ti.SetPlatformTextureSettings(ps);
        }
    }
}
