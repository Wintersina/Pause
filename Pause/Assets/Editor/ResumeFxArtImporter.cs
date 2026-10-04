using UnityEditor;
using UnityEngine;

// Import settings for the resume slow-mo indicator's sprites
// (Art/UI/ResumeFx/src~/build_resume_fx.py -> Art/Resources/ResumeFx):
// tiny neon pixel-art textures stretched across the view, so point-filtered,
// uncompressed and mip-free. The streak pivots on its head (bottom) so
// stretching it only lengthens the tail.
public class ResumeFxArtImporter : AssetPostprocessor
{
    public const string Folder = "Assets/Art/Resources/ResumeFx/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder)) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.isReadable = false;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteAlignment = assetPath.EndsWith("resume_streak.png")
            ? (int)SpriteAlignment.BottomCenter
            : (int)SpriteAlignment.Center;
        importer.SetTextureSettings(settings);
    }
}
