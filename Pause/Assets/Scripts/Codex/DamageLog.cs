using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

// What hurt and what killed the pilot, by codex entry id (HurtBy resolves the
// id from whatever touched the hull).
//
//   * Lifetime counters, one per entry: "killed you N times" and "hurt you N
//     times" (a heart lost without dying). Each is one PlayerPrefs string,
//     "id:n,id:n" sorted by id, written through the batched PrefsSaver and
//     synced by the cloud save (ProgressSnapshot.codexKilled / codexHurt,
//     merged per id by the larger count). An older save has neither key: that
//     reads as no counts, nothing to migrate.
//   * This run's tally, for the Flight Complete panel: who took hearts, how
//     many each, and who landed the fatal hit. Cleared when a run starts.
//
// Tutorial, practice and developer runs keep the run tally (so the panel
// still reads) but never touch the saved counters.
public static class DamageLog
{
    public const string KilledKey = "codexKilledBy", HurtKey = "codexHurtBy";
    public const int MaxCount = 99999;

    static Dictionary<string, int> killed, hurt;
    static readonly Dictionary<string, int> runHurt = new Dictionary<string, int>();
    static readonly List<string> runOrder = new List<string>();

    // The entry that landed this run's fatal hit (null while alive).
    public static string RunKiller { get; private set; }
    public static int RunHurtKinds { get { return runOrder.Count; } }

    public static void BeginRun()
    {
        runHurt.Clear();
        runOrder.Clear();
        RunKiller = null;
    }

    // A heart was lost to `id` (null: unknown source, not counted). `fatal`
    // is the last heart. `persist` writes the lifetime counters too.
    public static void Record(string id, bool fatal, bool persist)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (fatal) RunKiller = id;
        else
        {
            int n;
            if (!runHurt.TryGetValue(id, out n)) runOrder.Add(id);
            runHurt[id] = n + 1;
        }
        if (!persist) return;
        EnsureLoaded();
        var counts = fatal ? killed : hurt;
        Bump(counts, id);
        PlayerPrefs.SetString(fatal ? KilledKey : HurtKey, Format(counts));
        PrefsSaver.MarkDirty();
    }

    public static int KilledCount(string id) { return Lookup(true, id); }
    public static int HurtCount(string id) { return Lookup(false, id); }

    static int Lookup(bool fatal, string id)
    {
        if (id == null) return 0;
        EnsureLoaded();
        int n;
        return (fatal ? killed : hurt).TryGetValue(id, out n) ? n : 0;
    }

    // The codex line for an entry, or null when it never hurt the pilot.
    public static string CodexLine(string id)
    {
        int k = KilledCount(id), h = HurtCount(id);
        if (k <= 0 && h <= 0) return null;
        var sb = new StringBuilder(48);
        if (k > 0) sb.Append("KILLED YOU ").Append(k).Append(k == 1 ? " TIME" : " TIMES");
        if (k > 0 && h > 0) sb.Append("   |   ");
        if (h > 0) sb.Append("HURT YOU ").Append(h).Append(h == 1 ? " TIME" : " TIMES");
        return sb.ToString();
    }

    public static string NameOf(string id)
    {
        var entry = id != null ? Codex.Find(id) : null;
        return entry != null ? entry.name : null;
    }

    // "You died by <name>" caption text (null when the killer is unknown).
    public static string DiedByLine(string id)
    {
        string name = NameOf(id);
        return name == null ? null : "YOU DIED BY " + name.ToUpperInvariant();
    }

    // This run's hearts lost, most first: "Cinder Drone x2, Mine". Names at
    // most `maxNames`; the rest fold into "+N more". Null when nothing
    // non-fatal hurt the pilot.
    public static string RunHurtLine(int maxNames)
    {
        if (runOrder.Count == 0) return null;
        var sb = new StringBuilder(64);
        var used = new bool[runOrder.Count];
        int shown = 0;
        while (shown < maxNames && shown < runOrder.Count)
        {
            int best = -1;
            for (int i = 0; i < runOrder.Count; i++)
                if (!used[i] && (best < 0 || runHurt[runOrder[i]] > runHurt[runOrder[best]])) best = i;
            used[best] = true;
            string id = runOrder[best];
            if (shown > 0) sb.Append(", ");
            sb.Append(NameOf(id) ?? id);
            if (runHurt[id] > 1) sb.Append(" x").Append(runHurt[id]);
            shown++;
        }
        if (runOrder.Count > shown) sb.Append(" +").Append(runOrder.Count - shown).Append(" more");
        return sb.ToString();
    }

    // ---- persistence ----

    static void EnsureLoaded()
    {
        if (killed != null) return;
        killed = Parse(PlayerPrefs.GetString(KilledKey, ""));
        hurt = Parse(PlayerPrefs.GetString(HurtKey, ""));
    }

    // Re-read after a cloud restore / test reset.
    public static void Reload() { killed = hurt = null; }

    static void Bump(Dictionary<string, int> map, string id)
    {
        int n;
        map.TryGetValue(id, out n);
        map[id] = Math.Min(MaxCount, n + 1);
    }

    // "id:n,id:n" -> counts. Malformed or non-positive entries are dropped
    // (a corrupt pref reads as fewer counts, never throws).
    public static Dictionary<string, int> Parse(string text)
    {
        var map = new Dictionary<string, int>();
        if (string.IsNullOrEmpty(text)) return map;
        foreach (string part in text.Split(','))
        {
            int colon = part.LastIndexOf(':');
            if (colon <= 0) continue;
            int n;
            if (!int.TryParse(part.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n <= 0) continue;
            map[part.Substring(0, colon).Trim()] = Math.Min(MaxCount, n);
        }
        return map;
    }

    // Sorted ordinal by id: equal counts give equal text (cloud change detection).
    public static string Format(Dictionary<string, int> map)
    {
        var ids = new List<string>(map.Keys);
        ids.Sort(StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (string id in ids)
        {
            if (map[id] <= 0) continue;
            if (sb.Length > 0) sb.Append(',');
            sb.Append(id).Append(':').Append(map[id].ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    // The larger count per id (a restore never lowers a tally).
    public static string MergeMax(string a, string b)
    {
        var merged = Parse(a);
        foreach (var pair in Parse(b))
        {
            int n;
            if (!merged.TryGetValue(pair.Key, out n) || pair.Value > n) merged[pair.Key] = pair.Value;
        }
        return Format(merged);
    }
}
