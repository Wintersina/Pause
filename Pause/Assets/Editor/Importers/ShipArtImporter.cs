using System.IO;
using UnityEditor;

// Import settings for the generated hull sheets (ShipArt/<Key>/<Key>.png, built by
// ShipArt/src~/build_restyle.py) and the per-ship flame strips (ShipArt/<Key>/<Key>_flame.png).
// The sheets are 2304x768, past the default 2048 cap, and are shown well below their
// authored size (a 256 px cell as a ~110 px ship, 0.43x, which bilinear alone
// handles: mipmaps are off because an NPOT mipmapped sheet cannot be ASTC
// compressed, see AndroidTextureDiet). Code slices them with Sprite.Create (ShipHullArt,
// ShipExhaustStyle), so the importer only has to deliver an unclipped texture.
//
// A ship's folder is picked up by name alone: any PNG named <Folder>.png or <Folder>_flame.png
// directly inside Assets/Art/Resources/ShipArt/<Folder>/ -- adding a ship needs no edit here.
public class ShipArtImporter : AssetPostprocessor
{
    const string Root = "Assets/Art/Resources/ShipArt/";

    // True for ShipArt/<Key>/<Key>.png and ShipArt/<Key>/<Key>_flame.png.
    public static bool IsShipSheet(string path)
    {
        if (!path.StartsWith(Root) || !path.EndsWith(".png")) return false;
        string rest = path.Substring(Root.Length);
        int slash = rest.IndexOf('/');
        if (slash <= 0 || rest.IndexOf('/', slash + 1) >= 0) return false;
        string folder = rest.Substring(0, slash), file = Path.GetFileNameWithoutExtension(rest);
        return file == folder || file == folder + "_flame";
    }

    void OnPreprocessTexture()
    {
        if (!IsShipSheet(assetPath)) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;   // Unity will not compress an NPOT mipmapped texture (2304x768 stayed RGBA32, 9.4 MB); ASTC without mips is 1.8 MB
        importer.filterMode = UnityEngine.FilterMode.Bilinear;
        importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 4096;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.isReadable = false;
    }
}
