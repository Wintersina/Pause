using UnityEditor;
using UnityEngine;

// Keep the reinforced rails crisp and preserve their transparent silhouettes.
public class WorldRailImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Art/Resources/Worlds/") ||
            !System.IO.Path.GetFileName(assetPath).StartsWith("rail_") ||
            !assetPath.EndsWith(".png")) return;

        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Default;
        ti.mipmapEnabled = false;
        ti.filterMode = FilterMode.Point;
        ti.wrapModeU = TextureWrapMode.Clamp;
        ti.wrapModeV = TextureWrapMode.Repeat;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.alphaIsTransparency = true;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.maxTextureSize = 4096;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.isReadable = false;
    }
}
