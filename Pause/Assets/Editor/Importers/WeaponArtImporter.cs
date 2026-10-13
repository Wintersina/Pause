using UnityEditor;

// Import settings for the weapon / explosion atlases rendered by
// Art/Weapons/src~/render.sh into Art/Resources/Weapons. Enforced here so a
// re-render can never drift: plain textures sliced at runtime by WeaponArt,
// no mipmaps (they are drawn near 1:1), bilinear, clamped, never resized to
// a power of two (that would shift the 128px cells), high-quality
// compression so the hard ink outlines stay crisp.
public class WeaponArtImporter : AssetPostprocessor
{
    const string Folder = "Assets/Art/Resources/Weapons/";
    // The 4096x256 neon pixel-art power / attack strips (256 px cells): Point and full size,
    // the 2048 cap below would halve them and bilinear would blur the pixels.
    const string StripFolder = Folder + "Powers2/";

    // Bump to force a re-import of the folder under the current rules.
    public override uint GetVersion() { return 2; }

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder)) return;
        var importer = (TextureImporter)assetImporter;
        bool strip = assetPath.StartsWith(StripFolder);
        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = strip ? UnityEngine.FilterMode.Point : UnityEngine.FilterMode.Bilinear;
        importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.isReadable = false;
        importer.maxTextureSize = strip ? 4096 : 2048;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
    }
}
