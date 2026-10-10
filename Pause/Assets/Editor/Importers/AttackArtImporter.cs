using UnityEditor;

// Import settings for the themed attack art (Art/Resources/Attacks/<World>/<w>_attack_<file>.png, read by AttackArt):
// neon pixel-art sheets sliced at runtime into 128 px cells, so point-filtered, uncompressed RGBA32, mip-free, never
// rescaled (that would shift the cells), and not readable (nothing samples them on the CPU).
public class AttackArtImporter : AssetPostprocessor
{
    public const string Folder = "Assets/Art/Resources/Attacks/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder)) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = UnityEngine.FilterMode.Point;
        importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.isReadable = false;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
    }
}
