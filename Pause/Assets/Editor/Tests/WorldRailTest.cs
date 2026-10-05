using System.IO;
using UnityEditor;
using UnityEngine;

// Validates the reinforced rail textures that WorldPainter binds to planet
// walls. The runtime mirrors each texture for the right side.
public static class WorldRailTest
{
    public static int CheckArt(WorldTheme theme)
    {
        if (theme == null) return 0;
        string name = WorldPainter.RailTextureName(theme.displayName);
        if (string.IsNullOrEmpty(name)) return 0;

        string path = "Assets/Art/Resources/Worlds/" +
            (string.IsNullOrEmpty(theme.resourceFolder) ? theme.displayName : theme.resourceFolder) + "/" + name + ".png";
        int failures = 0;
        Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        Check(theme.displayName + " reinforced rail texture imports", imported != null, ref failures);
        if (imported == null) return failures;

        Check(theme.displayName + " reinforced rail is a tall strip",
              imported.height > imported.width * 2, ref failures);

        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        bool loaded = source.LoadImage(File.ReadAllBytes(path));
        bool hasCutout = false;
        if (loaded)
            foreach (var pixel in source.GetPixels32())
                if (pixel.a < 255) { hasCutout = true; break; }
        Check(theme.displayName + " reinforced rail preserves transparent cutouts",
              loaded && hasCutout, ref failures);
        Object.DestroyImmediate(source);
        return failures;
    }

    static void Check(string label, bool ok, ref int failures)
    {
        Debug.Log((ok ? "[WR] PASS  " : "[WR] FAIL  ") + label);
        if (!ok) failures++;
    }
}
