using UnityEngine;

// What the pilot has found of the ATOMIC FUSIONS (the codex's COMBOS page):
// per fusion, whether it has ever fired, how many times, the best chain and
// the day it was first seen. One PlayerPrefs string (PrefsKey):
//
//   "bloom:12:3:20261026,vacuum:4:2:20261025"   key:count:bestChain:yyyymmdd
//
// Parse / Serialize are pure (the tests round-trip them); unknown keys in a
// save are kept as they are, so an older build never drops a newer combo.
// Written only when something changes (a fusion fires: a rare event), never
// per frame. `Unseen` marks discoveries the COMBOS tab hasn't shown yet (its
// NEW dot); opening the tab clears it.
public static class FusionDiscoveryStore
{
    public const string PrefsKey = "fusionLog";
    public const string UnseenKey = "fusionUnseen";

    public struct Record
    {
        public int count;
        public int bestChain;
        public int firstDay;   // yyyymmdd, 0 = never
        public bool Discovered => count > 0;
    }

    static Record[] records;
    static string extra = "";   // entries this build doesn't know (kept verbatim)
    static bool unseen;
    static bool loaded;

    public static void Reload() { loaded = false; Ensure(); }

    static void Ensure()
    {
        if (loaded) return;
        loaded = true;
        records = new Record[AtomFusionTable.Count];
        extra = Parse(PlayerPrefs.GetString(PrefsKey, ""), records);
        unseen = PlayerPrefs.GetInt(UnseenKey, 0) != 0;
    }

    public static Record Get(FusionId id) { Ensure(); return records[(int)id]; }
    public static bool IsDiscovered(FusionId id) { return Get(id).Discovered; }

    public static int DiscoveredCount
    {
        get
        {
            Ensure();
            int n = 0;
            for (int i = 0; i < records.Length; i++) if (records[i].Discovered) n++;
            return n;
        }
    }

    public static int Total => AtomFusionTable.Count;
    public static bool HasUnseen { get { Ensure(); return unseen; } }

    public static void MarkSeen()
    {
        Ensure();
        if (!unseen) return;
        unseen = false;
        PlayerPrefs.SetInt(UnseenKey, 0);
        PrefsSaver.MarkDirty();
    }

    // A fusion fired with chain `chain` on day `day` (yyyymmdd). True on its
    // first ever discovery.
    public static bool RecordFiring(FusionId id, int chain, int day)
    {
        Ensure();
        var r = records[(int)id];
        bool first = !r.Discovered;
        if (first) r.firstDay = day;
        // a chain re-trigger is a firing too
        r.count++;
        r.bestChain = Mathf.Max(r.bestChain, Mathf.Max(1, chain));
        records[(int)id] = r;
        if (first) { unseen = true; PlayerPrefs.SetInt(UnseenKey, 1); }
        PlayerPrefs.SetString(PrefsKey, Serialize(records, extra));
        PrefsSaver.MarkDirty();
        return first;
    }

    public static int Today()
    {
        var d = System.DateTime.Now;
        return d.Year * 10000 + d.Month * 100 + d.Day;
    }

    static readonly string[] Months = { "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC" };

    // 20261026 -> "26 OCT 2026" ("" for 0).
    public static string FormatDay(int day)
    {
        if (day <= 0) return "";
        int y = day / 10000, m = day / 100 % 100, d = day % 100;
        if (m < 1 || m > 12) return y.ToString();
        return d + " " + Months[m - 1] + " " + y;
    }

    // ---- pure (de)serialisation ----

    // Fills `into` (indexed by FusionId) and returns the entries it didn't know.
    public static string Parse(string raw, Record[] into)
    {
        for (int i = 0; i < into.Length; i++) into[i] = default;
        if (string.IsNullOrEmpty(raw)) return "";
        var kept = new System.Text.StringBuilder();
        foreach (var entry in raw.Split(','))
        {
            if (string.IsNullOrEmpty(entry)) continue;
            var parts = entry.Split(':');
            var def = AtomFusionTable.FindKey(parts[0]);
            if (def == null)
            {
                if (kept.Length > 0) kept.Append(',');
                kept.Append(entry);
                continue;
            }
            int count, best, day;
            int.TryParse(parts.Length > 1 ? parts[1] : "0", out count);
            int.TryParse(parts.Length > 2 ? parts[2] : "0", out best);
            int.TryParse(parts.Length > 3 ? parts[3] : "0", out day);
            var r = into[(int)def.id];
            r.count = Mathf.Max(r.count, Mathf.Max(0, count));
            r.bestChain = Mathf.Max(r.bestChain, Mathf.Max(0, best));
            if (day > 0 && (r.firstDay == 0 || day < r.firstDay)) r.firstDay = day;
            into[(int)def.id] = r;
        }
        return kept.ToString();
    }

    public static string Serialize(Record[] records, string extra)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < records.Length && i < AtomFusionTable.Count; i++)
        {
            var r = records[i];
            if (!r.Discovered) continue;
            if (sb.Length > 0) sb.Append(',');
            sb.Append(AtomFusionTable.All[i].key).Append(':').Append(r.count).Append(':').Append(r.bestChain).Append(':').Append(r.firstDay);
        }
        if (!string.IsNullOrEmpty(extra))
        {
            if (sb.Length > 0) sb.Append(',');
            sb.Append(extra);
        }
        return sb.ToString();
    }
}
