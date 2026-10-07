using UnityEditor;

// Import settings for the generated Akira hull sheets
// (Art/Resources/ShipArt/Hulls/src~/build.py) and the per-ship exhaust atlas
// (exhaust.py). The sheets are 2304x768, past the default 2048 cap, and are shown well below their
// authored size (a 256 px cell as a ~110 px ship), so they keep mipmaps and
// bilinear filtering. Code slices them with Sprite.Create (ShipHullArt,
// ShipExhaustStyle), so the importer only has to deliver an unclipped texture.
public class ShipArtImporter : AssetPostprocessor
{
    const string Hulls = "Assets/Art/Resources/ShipArt/Hulls/";
    const string Exhaust = "Assets/Art/Resources/ShipArt/Exhaust/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Hulls) && !assetPath.StartsWith(Exhaust)) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.filterMode = UnityEngine.FilterMode.Bilinear;
        importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 4096;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.isReadable = false;
    }
}
