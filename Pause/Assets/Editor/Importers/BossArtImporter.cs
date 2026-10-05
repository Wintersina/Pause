using UnityEditor;

// Import settings for the boss atlases rendered by Art/Bosses/src~/render.sh
// into Art/Resources/Bosses: plain textures sliced at runtime by BossArt (the
// same rules as WeaponArtImporter) -- no mipmaps, bilinear, clamped, never
// rescaled to a power of two (that would shift the cells), high-quality
// compression so the ink outlines stay crisp.
public class BossArtImporter : AssetPostprocessor
{
    const string Folder = "Assets/Art/Resources/Bosses/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder)) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = UnityEngine.FilterMode.Bilinear;
        importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.isReadable = false;
        // Space now carries three additional painted rows (1920 x 2688).
        // Never downscale it: runtime slicing is in fixed 384px cells.
        // The body atlases are 5 x 8 cells of 384 px (1920 x 3072): a 2048 cap
        // shrank them to 1280 x 2048 on import, 256 px a cell.
        importer.maxTextureSize = 4096;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
    }
}
