using UnityEditor;
using UnityEngine;

// Import settings for the cover boulders' sheets (Art/Resources/Boulders/<World>/boulder.png and break.png, staged in
// Art/Boulders~/<World>/ until the user has vetted them; docs/cover-boulders.md section 6). The runtime cuts the
// cells itself (CoverBoulderArt; 768 px cells for the high-res worlds, manifest `cell`), so the textures import as plain sprites: no mipmaps (it is a 2D scroller, drawn
// near 1:1), bilinear (a boulder turns slowly and is drawn a little above its authored size), clamped, compressed,
// never readable, up to 4096 so the 3840 px destruction strip is not downscaled; ASTC 4x4 on Android / iOS.
public class BoulderArtImporter : AssetPostprocessor
{
    public const string Folder = "Assets/Art/Resources/Boulders/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder)) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.maxTextureSize = 4096;
        importer.isReadable = false;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        importer.SetTextureSettings(settings);
        // phones: ASTC 4x4 (the art is painted continuous-tone), never clamped below its own size (a clamp is a hidden downscale)
        foreach (string platform in new[] { "Android", "iPhone" })
        {
            var ps = importer.GetPlatformTextureSettings(platform);
            ps.overridden = true;
            ps.maxTextureSize = 4096;
            ps.format = TextureImporterFormat.ASTC_4x4;
            ps.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(ps);
        }
    }
}
