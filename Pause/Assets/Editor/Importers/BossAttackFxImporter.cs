using UnityEditor;

// Import settings for the boss attacks' FX sheet
// (Art/BossAttacks/src~/build_boss_attack_fx.py -> Art/Resources/BossAttackFx):
// neon pixel art sliced at runtime by BossAttackFx, so point-filtered,
// uncompressed, mip-free and never rescaled (that would shift the cells).
public class BossAttackFxImporter : AssetPostprocessor
{
    public const string Folder = "Assets/Art/Resources/BossAttackFx/";

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
        importer.textureCompression = TextureImporterCompression.Uncompressed;
    }
}
