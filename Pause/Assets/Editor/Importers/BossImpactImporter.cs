using UnityEditor;

// Import settings for Art/Resources/BossImpact (BossImpact / BossArt):
//   attack/<key>_<pose>_stage<N>.png  damaged tell / fire frames, registered to the body
//                            atlas cells: the same bilinear + HQ-compressed
//                            import as the body and damage atlases, so a
//                            damaged idle and a damaged tell match exactly;
//   boss_impact_<key>.png    the per-boss hit strips (3072 x 256, 12 cells): Point
//                            filter, HQ-compressed in the editor (the Android /
//                            iPhone diet makes them ASTC 4x4: this folder is on
//                            its hard-outline list, 6x6 would block the 1 px edges).
public class BossImpactImporter : AssetPostprocessor
{
    const string Folder = "Assets/Art/Resources/BossImpact/";

    public override uint GetVersion() { return 4; }

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder)) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.isReadable = false;
        importer.maxTextureSize = 4096;
        bool attack = assetPath.Contains("/BossImpact/attack/");
        importer.filterMode = attack ? UnityEngine.FilterMode.Bilinear : UnityEngine.FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
    }
}
