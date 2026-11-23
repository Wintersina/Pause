using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Bakes every elite's silhouette reach (EliteSilhouette) into its def: "silhouetteShare" in
// Art/Resources/Elites/Defs/<key>.json, the furthest opaque pixel of any drawing of its strips as a share of
// the cell. The game then reads no pixel when an elite spawns. Run it after an elite's strip changes (a new
// elite, a repaint); PerfHitchTest fails on a def with no bake and HeartsInArtTest on a stale one.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod EliteSilhouetteBaker.BakeAllAndExit
public static class EliteSilhouetteBaker
{
    public const string DefsFolder = "Assets/Art/Resources/Elites/Defs";
    static readonly Regex Field = new Regex("\\n(\\s*)\"silhouetteShare\"\\s*:\\s*[-0-9.eE]+\\s*,");
    static readonly Regex Anchor = new Regex("\\n(\\s*)\"hullRadius\"\\s*:\\s*[-0-9.eE]+\\s*,");

    [MenuItem("Pause/Elites/Bake Silhouettes")]
    public static void BakeAll()
    {
        int n = Bake();
        Debug.Log("[EliteSilhouetteBaker] " + n + " defs changed");
    }

    public static void BakeAllAndExit()
    {
        int n = -1;
        try { n = Bake(); }
        catch (System.Exception e) { Debug.LogError("[EliteSilhouetteBaker] " + e); }
        Debug.Log("[EliteSilhouetteBaker] " + n + " defs changed");
        EditorApplication.Exit(n < 0 ? 1 : 0);
    }

    // The value a def's file should hold (4 decimals: 0.02 px of a 384 px cell).
    public static float Measure(EliteDef def)
    {
        return Mathf.Round(EliteSilhouette.MeasureShare(def) * 10000f) / 10000f;
    }

    static int Bake()
    {
        EliteCatalog.Reload();
        EliteSilhouette.ClearCache();
        int changed = 0;
        foreach (var def in EliteCatalog.All)
        {
            string path = DefsFolder + "/" + def.key + ".json";
            if (!File.Exists(path)) { Debug.LogWarning("[EliteSilhouetteBaker] no file for " + def.key); continue; }
            float share = Measure(def);
            if (share <= 0f) { Debug.LogWarning("[EliteSilhouetteBaker] " + def.key + ": strip missing or unreadable, not baked"); continue; }
            string text = File.ReadAllText(path);
            string value = share.ToString("0.####", CultureInfo.InvariantCulture);
            string next;
            if (Field.IsMatch(text)) next = Field.Replace(text, m => "\n" + m.Groups[1].Value + "\"silhouetteShare\": " + value + ",", 1);
            else if (Anchor.IsMatch(text)) next = Anchor.Replace(text, m => m.Value + "\n" + m.Groups[1].Value + "\"silhouetteShare\": " + value + ",", 1);
            else { Debug.LogError("[EliteSilhouetteBaker] " + def.key + ": no hullRadius line to put the bake after"); continue; }
            if (next == text) continue;
            File.WriteAllText(path, next);
            changed++;
            Debug.Log("[EliteSilhouetteBaker] " + def.key + " silhouetteShare " + value);
        }
        if (changed > 0) AssetDatabase.Refresh();
        EliteCatalog.Reload();
        return changed;
    }
}
