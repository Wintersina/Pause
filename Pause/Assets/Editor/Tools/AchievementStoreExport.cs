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
