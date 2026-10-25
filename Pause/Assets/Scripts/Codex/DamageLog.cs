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
    static readonly Dictionary<string, int> runLast = new Dictionary<string, int>();
    static int runHits;

    // A heart lost to something that resolves to no codex entry still counts in this run's
    // HURT BY (never in the saved tallies): the line must not claim "no other hits" then.
    public const string UnknownSource = "unknown_source";

    // The entry that landed this run's fatal hit (null while alive).
    public static string RunKiller { get; private set; }
    public static int RunHurtKinds { get { return runOrder.Count; } }

    public static void BeginRun()
    {
        runHurt.Clear();
        runOrder.Clear();
        runLast.Clear();
        runHits = 0;
        RunKiller = null;
    }

    // A heart was lost to `id` (null: unknown source, not counted). `fatal`
    // is the last heart. `persist` writes the lifetime counters too.
    public static void Record(string id, bool fatal, bool persist)
    {
        if (string.IsNullOrEmpty(id))
        {
            // unknown fatal blow: KILLED BY stays a dash; an unknown earlier hit is still a hit
            if (!fatal) NoteRunHit(UnknownSource);
            return;
        }
        if (fatal) RunKiller = id;
        else NoteRunHit(id);
        if (!persist) return;
        EnsureLoaded();
        var counts = fatal ? killed : hurt;
        Bump(counts, id);
        PlayerPrefs.SetString(fatal ? KilledKey : HurtKey, Format(counts));
        PrefsSaver.MarkDirty();
    }

    static void NoteRunHit(string id)
    {
        int n;
        if (!runHurt.TryGetValue(id, out n)) runOrder.Add(id);
        runHurt[id] = n + 1;
        runLast[id] = ++runHits;
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

    // Every death on record (all entries): the codex table's "of your deaths" share is out of this.
    public static int TotalKilled()
    {
        EnsureLoaded();
        long n = 0;
        foreach (var pair in killed) n += pair.Value;
        return (int)Math.Min(int.MaxValue, n);
    }

    // The share (0..100, rounded) of all recorded deaths that `id` dealt; -1 with no deaths on record.
    public static int ShareOfDeaths(string id)
    {
        int total = TotalKilled();
        if (total <= 0) return -1;
        return Mathf.RoundToInt(100f * KilledCount(id) / total);
    }

    public static string NameOf(string id)
    {
        var entry = id != null ? Codex.Find(id) : null;
        return entry != null ? entry.name : null;
    }

    // This run's hearts lost before the fatal one, most first and, among equals, the most recent
    // first: "Cinder Drone x2, Mine". Entries are added while the line stays within `maxChars`
    // (at least one always shows); the rest fold into "+N more". Null when nothing non-fatal
    // hurt the pilot. Names are the codex names (the VERSUS YOU table's).
    public static string RunHurtLine(int maxChars)
    {
        if (runOrder.Count == 0) return null;
        var order = new List<int>(runOrder.Count);
        for (int i = 0; i < runOrder.Count; i++) order.Add(i);
        order.Sort((x, y) =>
        {
            int c = runHurt[runOrder[y]].CompareTo(runHurt[runOrder[x]]);
            return c != 0 ? c : runLast[runOrder[y]].CompareTo(runLast[runOrder[x]]);
        });
        var sb = new StringBuilder(64);
        int shown = 0;
        foreach (int i in order)
        {
            string id = runOrder[i];
            string part = (NameOf(id) ?? (id == UnknownSource ? "Unknown hazard" : id)) + (runHurt[id] > 1 ? " x" + runHurt[id] : "");
            string moreTail = " +" + (order.Count - shown - 1) + " more";
            int add = (shown > 0 ? 2 : 0) + part.Length;
            // keep room for the "+N more" tail unless this is the last entry
            int tail = shown + 1 < order.Count ? moreTail.Length : 0;
            if (shown > 0 && sb.Length + add + tail > maxChars) break;
            if (shown > 0) sb.Append(", ");
            sb.Append(part);
            shown++;
        }
        if (order.Count > shown) sb.Append(" +").Append(order.Count - shown).Append(" more");
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
