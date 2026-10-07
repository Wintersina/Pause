using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Codex delivers elite art to Art/Enemies/Elite/<World>/ (art-only: not
// under Resources). The game loads it from Art/Resources/Elites/<World>/
// (EliteArt), so this copies every final strip across -- byte for byte, the
// originals are never touched -- whenever one is imported or changed, and
// gives the copies their import settings (a sprite, no mips, uncompressed so
// the pixel art stays crisp, alpha as transparency).
//
// Final strips are <world>_elite_<name>.png plus the optional
// <world>_elite_<name>_parked / _liftoff / _death.png; anything ending in
// _candidate or _concept (Codex's working files) is skipped. A strip
// delivered under its bare name (Frost/rimebreaker.png) is copied under the
// convention name (Frost/frost_elite_rimebreaker.png): the def's key.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod EliteArtSync.SyncAllAndExit
public class EliteArtSync : AssetPostprocessor
{
    public const string SourceRoot = "Assets/Art/Enemies/Elite";
    public const string TargetRoot = "Assets/Art/Resources/Elites";

    static readonly Regex Final = new Regex(@"^[a-z]+_elite_[a-z0-9_]+\.png$");
    static readonly Regex Bare = new Regex(@"^[a-z][a-z0-9_]*\.png$");
    static readonly string[] WorkingSuffixes = { "_candidate", "_concept", "_wip", "_draft", "_old" };

    public static bool IsFinalStrip(string fileName)
    {
        if (!Final.IsMatch(fileName) && !Bare.IsMatch(fileName)) return false;
        string stem = Path.GetFileNameWithoutExtension(fileName);
        foreach (string w in WorkingSuffixes) if (stem.EndsWith(w)) return false;
        return true;
    }

    // The name the game loads it by: <world>_elite_<name>.png.
    public static string TargetName(string world, string fileName)
    {
        string prefix = world.ToLowerInvariant() + "_elite_";
        return Final.IsMatch(fileName) ? fileName : prefix + fileName;
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        bool any = false;
        foreach (string path in imported)
            if (path.StartsWith(SourceRoot + "/") && path.EndsWith(".png")) any |= Copy(path);
        if (any) AssetDatabase.Refresh();
    }

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(TargetRoot + "/")) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.filterMode = FilterMode.Bilinear;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.maxTextureSize = 2048;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.wrapMode = TextureWrapMode.Clamp;
    }

    // The strips come out of Codex's pipeline with their solid body at alpha
    // 250-254 and a faint 1-7 alpha haze round it. Imported (the PNG bytes
    // stay as delivered), the body is snapped to fully opaque and the haze
    // to clear, so the hull reads solid in play and the codex's locked
    // silhouette is one flat ink colour (CodexTest's readable-card check).
    public const byte SolidAlpha = 240, HazeAlpha = 8;

    void OnPostprocessTexture(Texture2D texture)
    {
        if (!assetPath.StartsWith(TargetRoot + "/")) return;
        var px = texture.GetPixels32();
        for (int i = 0; i < px.Length; i++)
        {
            byte a = px[i].a;
            if (a >= SolidAlpha) px[i].a = 255;
            else if (a < HazeAlpha) px[i] = new Color32(0, 0, 0, 0);
        }
        texture.SetPixels32(px);
        texture.Apply(false);
    }

    // Copies one source strip if it is a final one and differs; true if copied.
    static bool Copy(string source)
    {
        string file = Path.GetFileName(source);
        if (!IsFinalStrip(file)) return false;
        string world = Path.GetFileName(Path.GetDirectoryName(source));
        string dir = TargetRoot + "/" + world;
        string target = dir + "/" + TargetName(world, file);
        if (File.Exists(target) && Same(source, target)) return false;
        Directory.CreateDirectory(dir);
        File.Copy(source, target, true);
        Debug.Log("[EliteArt] " + source + " -> " + target);
        return true;
    }

    static bool Same(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (fa.Length != fb.Length) return false;
        return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }

    [MenuItem("Pause/Elites/Sync Elite Art")]
    public static void SyncAll()
    {
        int n = 0;
        if (Directory.Exists(SourceRoot))
            foreach (string path in Directory.GetFiles(SourceRoot, "*.png", SearchOption.AllDirectories))
                if (Copy(path.Replace('\\', '/'))) n++;
        AssetDatabase.Refresh();
        // re-import every copy so the import fix-ups above always apply
        if (Directory.Exists(TargetRoot))
            foreach (string path in Directory.GetFiles(TargetRoot, "*.png", SearchOption.AllDirectories))
                AssetDatabase.ImportAsset(path.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
        Debug.Log("[EliteArt] synced " + n + " strip(s)");
    }

    public static void SyncAllAndExit()
    {
        SyncAll();
        EditorApplication.Exit(0);
    }
}
