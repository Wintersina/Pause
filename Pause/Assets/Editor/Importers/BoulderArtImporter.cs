using UnityEditor;
using UnityEngine;

// Import settings for the cover boulders' sheets (Art/Resources/Boulders/<World>/boulder.png and break.png, staged in
// Art/Boulders~/<World>/ until the user has vetted them; docs/cover-boulders.md section 6). The runtime cuts the
// 512 px cells itself (CoverBoulderArt), so the textures import as plain sprites: no mipmaps (it is a 2D scroller, drawn
// near 1:1), bilinear (a boulder turns slowly and is drawn a little above its authored size), clamped, compressed,
// never readable, up to 4096 so the 2560 px destruction strip is not downscaled.
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
    }
}
