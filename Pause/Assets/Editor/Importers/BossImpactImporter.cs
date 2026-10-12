using UnityEditor;

// Import settings for Art/Resources/BossImpact (BossImpact / BossArt):
//   attack/<key>_<pose>_stage<N>.png  damaged tell / fire frames, registered to the body
//                            atlas cells: the same bilinear + HQ-compressed
//                            import as the body and damage atlases, so a
//                            damaged idle and a damaged tell match exactly;
//   crack_*.png, boss_hit_burst*.png  hard-edged pixel art: Point filter, never
//                            compressed or rescaled (compression would smear
//                            the 1-3 px crack cores and the burst's sparks).
// The full-screen crack overlays are mostly transparent but 1080 x 2400, so
// they stay HQ-compressed (a smooth 4:1 ASTC keeps their white cores); the
// strips are small and stay raw.
public class BossImpactImporter : AssetPostprocessor
{
    const string Folder = "Assets/Art/Resources/BossImpact/";

    public override uint GetVersion() { return 3; }

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
        importer.maxTextureSize = 8192;   // boss_hit_burst.png is 4608 px wide
        string name = System.IO.Path.GetFileName(assetPath);
        bool attack = assetPath.Contains("/BossImpact/attack/");
        bool overlay = name.StartsWith("crack_0");
        importer.filterMode = attack ? UnityEngine.FilterMode.Bilinear : UnityEngine.FilterMode.Point;
        importer.textureCompression = attack || overlay ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed;
    }
}
