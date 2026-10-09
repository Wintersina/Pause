using UnityEditor;

// Import settings for the achievement badges (Art/Resources/Achievements/<id>.png,
// 128 px, alpha): a single pixel-art sprite each, point-filtered, no mipmaps,
// never rescaled. The 1024 masters live in Art/Achievements/src~ (ignored by Unity).
public class AchievementArtImporter : AssetPostprocessor
{
    const string Folder = "Assets/Art/Resources/Achievements/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder)) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = UnityEngine.FilterMode.Point;
        importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.isReadable = false;
        importer.maxTextureSize = 256;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
    }
}
