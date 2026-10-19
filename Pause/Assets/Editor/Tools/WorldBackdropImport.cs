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
//   <World>/Backdrop3/ (Frost, Verdant, Ember, Tide) the same rules for the v1..v4 tile sets and atlases
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

    // Space's sun sheets (SpaceSunLayer): never clamped (4096), no mip maps; the disc is hard pixel art (point, RGBA32 on phones),
    // the corona and the flare flipbook are soft additive layers (bilinear, ASTC 4x4 on phones).
    public static bool IsSunSheet(string path)
    {
        if (!path.Contains("/Worlds/Space/Backdrop/")) return false;
        string f = System.IO.Path.GetFileNameWithoutExtension(path);
        return f.StartsWith("sun_", System.StringComparison.Ordinal) || f.StartsWith("flares_", System.StringComparison.Ordinal);
    }
    // The approved weather sheets of every world (wx_hazard / wx_ambient: HazardArt / WeatherArt): the same treatment as the sun's soft layers.
    public static bool IsTornadoSheet(string path)
    {
        if (!path.StartsWith("Assets/Art/Backgrounds/Resources/Worlds/") || !(path.Contains("/Backdrop3/") || path.Contains("/Backdrop/"))) return false;
        string f = System.IO.Path.GetFileNameWithoutExtension(path);
        return f.StartsWith("wx_", System.StringComparison.Ordinal);   // the approved Storm / Tide weather sheets
    }
    public static bool IsSunDisc(string path)
    {
        string f = System.IO.Path.GetFileNameWithoutExtension(path);
        return IsSunSheet(path) && !f.Contains("corona") && !f.StartsWith("flares_", System.StringComparison.Ordinal);
    }

    static bool IsBackdrop(string path)
    {
        return path.StartsWith("Assets/Art/Backgrounds/Resources/Worlds/") &&
               (path.Contains("/Backdrop/") || IsBackdrop3(path)) && path.EndsWith(".png");
    }

    // Frost's, Verdant's, Ember's and Tide's v3 backdrops (Spec.folder "Worlds/<World>/Backdrop3/"):
    // variant tile sets in v1..v4/, shared atlases beside them.
    // Frost's launch-site strips (Backdrop3/docks: four 6-cell strips of 256 px cells, 1578 x 1054): wider than the 1024 atlases, so never clamped
    // (a 1024 cap would shrink the cells by a third and make the cut rects drift).
    public static bool IsFrostLaunchSites(string path) { return path.Contains("/Worlds/Frost/Backdrop3/") && path.EndsWith("/docks.png"); }

    public static bool IsBackdrop3(string path) { return path.Contains("/Backdrop3/"); }

    // Backdrop3 art is pixel art drawn near 1:1 (landmarks, sites, lights)
    // or ~2x (tiles): point-sampled so it stays crisp. Its soft atlases
    // (clouds, mist, snow sheets, smoke, steam, aurora) are drawn several
    // times their pixel size and keep bilinear filtering.
    public static bool IsSoftAtlas(string file)
    {
        return file == "weather" || file == "smoke" || file == "steam" || file == "aurora" || file == "firesmoke" || file == "eruption" || file == "surf";
    }

    // Tide's and Desert's run C loops (smoke, surf, dust, worms ...) are nearest-neighbour pixel art: point-sampled,
    // unlike Ember's and Verdant's soft sheets of the same name. Only its weather stays soft.
    public static bool IsTidePixelLoop(string path, string file)
    {
        return (path.Contains("/Worlds/Tide/Backdrop3/") || path.Contains("/Worlds/Desert/Backdrop3/")) && file != "weather";
    }

    // Desert's worm sheets (worm, worm_giant): 2048 px sheets of 256 / 512 px cells that are drawn up to
    // ~1.5x their pixel size, so they keep every pixel (the other atlases cap at 1024).
    // Storm's and Desert's painted art is continuous-tone (thousands of colours: smooth cloud / dune gradients), not
    // the small palettes of Frost / Verdant / Ember: ASTC 6x6 bands and blocks it on phones, so these two worlds
    // take ASTC 4x4 (8 bpp). Their sources are never clamped (see maxTextureSize above).
    public static bool IsContinuousTone(string path)
    {
        return path.Contains("/Worlds/Storm/Backdrop3/") || path.Contains("/Worlds/Desert/Backdrop3/");
    }

    public static bool IsDesertWormSheet(string path, string file)
    {
        return path.Contains("/Worlds/Desert/Backdrop3/") && (file == "worm" || file == "worm_giant");
    }

    // Storm's v1 / v2 ground tiles are 1024x2048 (2x the other worlds' 512x1024, "HD neon pixel art"): they keep every pixel
    // (max size 2048 on every platform), draw 170.67 px per unit (a tile is still 6 units wide), take ASTC 6x6 on phones
    // (0.93 MB each; 4x4 would be 8 bpp = 2 MB each) and the sparse flow strips a plain quad.
    public static bool IsStormHdTile(string path, bool tile)
    {
        return tile && path.Contains("/Worlds/Storm/Backdrop3/");
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
               file == "reference_planet" || file == "station_ring_v2";
    }

    public static void ReimportSpaceStandaloneSprites()
    {
        const string folder = "Assets/Art/Backgrounds/Resources/Worlds/Space/Backdrop/";
        for (int i = 0; i < 3; i++)
            AssetDatabase.ImportAsset(folder + "asteroid_" + i.ToString("00") + ".png",
                                      ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(folder + "reference_planet.png", ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(folder + "station_ring_v2.png", ImportAssetOptions.ForceUpdate);
        foreach (string atlas in new[] { "extras", "neon_frames", "asteroid_fx" })
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
        bool sun = IsSunSheet(assetPath) || IsTornadoSheet(assetPath), sunDisc = IsSunDisc(assetPath);
        ti.mipmapEnabled = planetSheet;
        if (planetSheet) ti.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
        bool crisp = sharpSpaceSky || (IsBackdrop3(assetPath) && (!IsSoftAtlas(file) || IsTidePixelLoop(assetPath, file)));
        ti.filterMode = sun ? (sunDisc ? FilterMode.Point : FilterMode.Bilinear) : crisp ? FilterMode.Point : planetSheet ? FilterMode.Trilinear : FilterMode.Bilinear;
        ti.alphaIsTransparency = true;
        ti.isReadable = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        bool stormHd = IsStormHdTile(assetPath, tile);
        ti.maxTextureSize = sun ? PlanetSheetMaxSize : planetSheet ? PlanetSheetMaxSize : file == "station_ring_v2" ? 1024 : file == "reference_planet" ? 1024 :
            sharpSpaceSky || stormHd || (!tile && assetPath.Contains("/Worlds/Space/")) || IsDesertWormSheet(assetPath, file) || IsFrostLaunchSites(assetPath) || file == "docks" ? 2048 : 1024;   // (docks: the launch-strip sites atlas, 1578 px wide, keeps every pixel)
        ti.textureCompression = sunDisc ? TextureImporterCompression.Uncompressed : TextureImporterCompression.Compressed;
        ti.sRGBTexture = true;

        if (tile || IsStandaloneSprite(file))
        {
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = stormHd ? 1024f / 6f : tile ? TilePixelsPerUnit : BackdropAtlas.PixelsPerUnit;
            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            settings.spriteMeshType = file == "sky" || sharpSpaceSky || stormHd ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
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
            defaults.maxTextureSize = sun ? PlanetSheetMaxSize : planetSheet ? PlanetSheetMaxSize : file == "station_ring_v2" ? 1024 : file == "reference_planet" ? 1024 : 2048;
            ti.SetPlatformTextureSettings(defaults);
        }

        foreach (string platform in new[] { "Android", "iPhone" })
        {
            var ps = ti.GetPlatformTextureSettings(platform);
            ps.overridden = true;
            ps.maxTextureSize = sun ? PlanetSheetMaxSize : planetSheet ? PlanetSheetMaxSize : file == "station_ring_v2" ? 1024 : file == "reference_planet" ? 1024 :
                sharpSpaceSky || stormHd || (!tile && assetPath.Contains("/Worlds/Space/")) || IsDesertWormSheet(assetPath, file) || IsFrostLaunchSites(assetPath) || file == "docks" ? 2048 : 1024;   // (docks: the launch-strip sites atlas, 1578 px wide, keeps every pixel)
            ps.format = sunDisc ? TextureImporterFormat.RGBA32 : stormHd ? TextureImporterFormat.ASTC_6x6 : sun || planetSheet || IsContinuousTone(assetPath) ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6;
            ps.textureCompression = sunDisc ? TextureImporterCompression.Uncompressed : TextureImporterCompression.Compressed;
            ti.SetPlatformTextureSettings(ps);
        }
    }
}
