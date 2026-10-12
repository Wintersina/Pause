using System;
using UnityEditor;

// Import settings for the HapticGate splash film frames
// (Resources/HapticGate/Film/gate_film_NNN.jpg, see GateFilm.cs):
//   * Default texture (GateFilm makes its own sprites), never readable,
//   * no mipmaps: shown at about 1:1 (810 px across a 1080 px phone, ~1.3x),
//     never minified, and mips would cost a third more memory,
//   * Non-Power-of-2: None (810x828 stays 810x828; ASTC needs no power of two),
//   * clamp, bilinear (painted film, not pixel art), sRGB, no alpha,
//   * max size 1024 (never shrinks the 810x828 frame),
//   * desktop / editor: high quality compression; Android / iOS: ASTC 6x6 from
//     AndroidTextureDiet (runs after this one, order 1000), like the rest of the
//     soft painted splash / title art.
public class GateFilmImporter : AssetPostprocessor
{
    public const string Folder = "Assets/Resources/HapticGate/Film/";

    public static bool IsFilmFrame(string path)
    {
        return path.StartsWith(Folder, StringComparison.Ordinal) &&
               (path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
    }

    public override int GetPostprocessOrder() { return 10; }

    void OnPreprocessTexture()
    {
        if (!IsFilmFrame(assetPath)) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Default;
        ti.mipmapEnabled = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.isReadable = false;
        ti.sRGBTexture = true;
        ti.alphaSource = TextureImporterAlphaSource.None;
        ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        ti.filterMode = UnityEngine.FilterMode.Bilinear;
        ti.maxTextureSize = 1024;
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.streamingMipmaps = false;
    }
}
