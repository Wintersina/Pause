using System;
using UnityEditor;
using UnityEngine;

// Boss, weapon and attack atlases are sliced at runtime: keep their cell geometry
// intact (no mips or NPOT scaling), clamp edges and discard CPU-readable copies.
// Specific subfolders precede their parent rules. AndroidTextureDiet runs later
// and applies the existing mobile ASTC overrides.
public class RuntimeAtlasImporter : AssetPostprocessor
{
    const string Root = "Assets/Art/Resources/";

    static readonly (string folder, FilterMode filter, TextureImporterCompression compression, int maxSize)[] Rules =
    {
        // Power strips are 4096 px wide; point filtering preserves their pixel edges.
        ("Weapons/Powers2/", FilterMode.Point, TextureImporterCompression.CompressedHQ, 4096),
        ("Weapons/", FilterMode.Bilinear, TextureImporterCompression.CompressedHQ, 2048),
        // Boss bodies include a 4032 px sheet; damaged attack poses match the bodies.
        ("Bosses/", FilterMode.Bilinear, TextureImporterCompression.CompressedHQ, 4096),
        ("BossImpact/attack/", FilterMode.Bilinear, TextureImporterCompression.CompressedHQ, 4096),
        ("BossImpact/", FilterMode.Point, TextureImporterCompression.CompressedHQ, 4096),
        // Zero leaves the existing size cap untouched, as the boss FX importer did.
        ("BossAttackFx/", FilterMode.Point, TextureImporterCompression.Uncompressed, 0),
        ("Attacks/", FilterMode.Point, TextureImporterCompression.Uncompressed, 2048),
    };

    public override uint GetVersion() { return 1; }

    void OnPreprocessTexture()
    {
        foreach (var rule in Rules)
        {
            if (!assetPath.StartsWith(Root + rule.folder, StringComparison.Ordinal)) continue;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = rule.filter;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.isReadable = false;
            if (rule.maxSize > 0) importer.maxTextureSize = rule.maxSize;
            importer.textureCompression = rule.compression;
            return;
        }
    }
}
