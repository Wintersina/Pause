using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Pause > Achievements > Export store CSV
//
// Writes the files the owner uses to enter the 60 achievements in the stores
// into docs/achievements-export/ (generated from AchievementCatalog, so the
// catalogue is the one source of truth):
//   play-console.csv   Google Play Console > Play Games Services > Achievements
//   game-center.csv    App Store Connect > Game Center > Achievements
//   android-ids.csv    internal_id,android_id -- the template to paste the real
//                      CgkI... ids into (Assets/Resources/AchievementStoreIds.csv)
// Headless: -executeMethod AchievementStoreExport.Export
public static class AchievementStoreExport
{
    public const string Folder = "docs/achievements-export";

    [MenuItem("Pause/Achievements/Export store CSV")]
    public static void Export()
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", Folder));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "play-console.csv"), PlayConsoleCsv(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "game-center.csv"), GameCenterCsv(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "android-ids.csv"), AndroidIdTemplateCsv(), new UTF8Encoding(false));
        Debug.Log("[Achievements] exported store CSVs to " + dir);
    }

    // Pause > Achievements > Export store icons: the Google Play 512 x 512 PNGs (nearest-neighbour from the
    // 1024 masters in Art/Achievements/src~, so the pixel art stays crisp) into play-icons-512/<id>.png, and
    // the Game Center 1024 masters (RGB, no alpha) into gamecenter-1024/<id>.png. -executeMethod AchievementStoreExport.ExportIcons
    [MenuItem("Pause/Achievements/Export store icons")]
    public static void ExportIcons()
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", Folder));
        string src = Path.Combine(Application.dataPath, "Art", "Achievements", "src~");
        string play = Path.Combine(dir, "play-icons-512"), gc = Path.Combine(dir, "gamecenter-1024");
        Directory.CreateDirectory(play); Directory.CreateDirectory(gc);
        int n = 0;
        foreach (var d in AchievementCatalog.All)
        {
            string master = Path.Combine(src, d.id + "_1024.png");
            if (!File.Exists(master)) { Debug.LogError("[Achievements] missing master " + master); continue; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(master));
            File.WriteAllBytes(Path.Combine(play, d.id + ".png"), Downscale(tex, 512).EncodeToPNG());
            File.WriteAllBytes(Path.Combine(gc, d.id + ".png"), Downscale(tex, tex.width).EncodeToPNG());
            Object.DestroyImmediate(tex);
            n++;
        }
        Debug.Log("[Achievements] exported " + n + " store icons to " + dir);
    }

    // Nearest-neighbour resample to size x size, fully opaque (RGB: flattened onto near-black).
    static Texture2D Downscale(Texture2D s, int size)
    {
        var o = new Texture2D(size, size, TextureFormat.RGB24, false);
        var px = new Color32[size * size];
        var sp = s.GetPixels32();
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var c = sp[(y * s.height / size) * s.width + (x * s.width / size)];
                float a = c.a / 255f;
                px[y * size + x] = new Color32((byte)(c.r * a + 8 * (1 - a)), (byte)(c.g * a + 8 * (1 - a)), (byte)(c.b * a + 10 * (1 - a)), 255);
            }
        o.SetPixels32(px); o.Apply();
        return o;
    }

    public static string Field(string s)
    {
        if (s == null) return "";
        return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    static bool HiddenInStore(AchievementDef d) { return d.hidden || d.requiresTide; }

    public static string PlayConsoleCsv()
    {
        var sb = new StringBuilder();
        sb.Append("Order,Name,Description,Points,Incremental steps (blank = standard),Initial state,Icon file (512x512)\n");
        int n = 0;
        foreach (var d in AchievementCatalog.All)
        {
            n++;
            sb.Append(n).Append(',').Append(Field(d.title)).Append(',').Append(Field(d.description)).Append(',')
              .Append(d.points).Append(',').Append(d.storeSteps > 0 ? d.storeSteps.ToString() : "").Append(',')
              .Append(HiddenInStore(d) ? "Hidden" : "Revealed").Append(',').Append(d.id).Append(".png\n");
        }
        return sb.ToString();
    }

    public static string GameCenterCsv()
    {
        var sb = new StringBuilder();
        sb.Append("Reference name,Achievement ID,Point value,Hidden,Achievable more than once,Title,Pre-earned description,Earned description,Image\n");
        foreach (var d in AchievementCatalog.All)
        {
            sb.Append("Pause ").Append(d.id).Append(',').Append(AchievementIds.IosId(d)).Append(',').Append(d.points).Append(',')
              .Append(HiddenInStore(d) ? "Yes" : "No").Append(",No,").Append(Field(d.title)).Append(',')
              .Append(Field(d.description)).Append(',').Append(Field(d.description)).Append(',').Append(d.id).Append("_1024.png\n");
        }
        return sb.ToString();
    }

    public static string AndroidIdTemplateCsv()
    {
        var sb = new StringBuilder();
        sb.Append("internal_id,android_id\n");
        foreach (var d in AchievementCatalog.All) sb.Append(d.id).Append(',').Append(AchievementIds.AndroidId(d)).Append('\n');
        return sb.ToString();
    }
}
