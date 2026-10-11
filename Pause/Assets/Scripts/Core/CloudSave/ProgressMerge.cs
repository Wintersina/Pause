using System;
using System.Collections.Generic;

// How a device's progress and its account's cloud save combine. This is the
// one place to change the rule.
//
//   bought ships                     union
//   HighestSpeed, BestScore,         max
//   highestWorld,
//   achievement/kill counters
//   HasDoneTut                       true if either side has it
//   owned hull skins                 union
//   codex discoveries               union
//   codex NEW markers                union of both sides' NEW, minus any entry
//                                    the other side already cleared (known there
//                                    and not NEW); acknowledged on either side
//                                    stays acknowledged
//   currency, spawnShip,             from whichever side has the newer
//   currentWorld, startWorld,        savedAtUtc (local wins a tie)
//   equipped skins
//
// The result is stamped with the newer of the two timestamps.
public static class ProgressMerge
{
    public static ProgressSnapshot Merge(ProgressSnapshot local, ProgressSnapshot cloud)
    {
        if (local == null) return cloud;
        if (cloud == null) return local;

        bool cloudNewer = cloud.savedAtUtc > local.savedAtUtc;
        var newer = cloudNewer ? cloud : local;

        var ships = new SortedSet<int>(local.boughtShips ?? new int[0]);
        ships.UnionWith(cloud.boughtShips ?? new int[0]);

        var skins = new SortedSet<int>(local.ownedSkins ?? new int[0]);
        skins.UnionWith(cloud.ownedSkins ?? new int[0]);

        var counters = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var side in new[] { local, cloud })
            foreach (var c in side.counters ?? new ProgressSnapshot.Counter[0])
            {
                if (string.IsNullOrEmpty(c.key)) continue;
                int existing;
                counters[c.key] = counters.TryGetValue(c.key, out existing) ? Math.Max(existing, c.value) : c.value;
            }
        var mergedCounters = new List<ProgressSnapshot.Counter>();
        foreach (var pair in counters) mergedCounters.Add(new ProgressSnapshot.Counter(pair.Key, pair.Value));

        string[] seen, fresh, acked;
        MergeCodex(local, cloud, out seen, out fresh, out acked);

        int highestWorld = Math.Max(local.highestWorld, cloud.highestWorld);
        var result = new ProgressSnapshot
        {
            schemaVersion = ProgressSnapshot.CurrentSchemaVersion,
            savedAtUtc = Math.Max(local.savedAtUtc, cloud.savedAtUtc),
            currency = newer.currency,
            spawnShip = newer.spawnShip,
            currentWorld = Math.Min(newer.currentWorld, highestWorld),
            startWorld = newer.startWorld,
            boughtShips = new List<int>(ships).ToArray(),
            highestSpeed = Math.Max(local.highestSpeed, cloud.highestSpeed),
            bestScore = Math.Max(local.bestScore, cloud.bestScore),
            highestWorld = highestWorld,
            hasDoneTut = local.hasDoneTut || cloud.hasDoneTut,
            counters = mergedCounters.ToArray(),
            ownedSkins = new List<int>(skins).ToArray(),
            equippedSkins = (int[])(newer.equippedSkins ?? new int[0]).Clone(),
            codexSeen = seen,
            codexNew = fresh,
            codexNewAck = acked,
        };
        return result;
    }

    // Codex rules. An entry is CLEARED on a side when that side knows it
    // (discovered) and does not list it as NEW: the player opened it there (or
    // it predates the NEW markers), so it never comes back as NEW. Restored
    // discoveries are therefore not NEW: only entries a side itself lists as NEW
    // (and the other side has not cleared) stay NEW.
    public static void MergeCodex(ProgressSnapshot a, ProgressSnapshot b,
        out string[] seen, out string[] fresh, out string[] acked)
    {
        var seenA = new HashSet<string>(a.codexSeen ?? new string[0]);
        var seenB = new HashSet<string>(b.codexSeen ?? new string[0]);
        var newA = new HashSet<string>(a.codexNew ?? new string[0]);
        var newB = new HashSet<string>(b.codexNew ?? new string[0]);

        var allSeen = new HashSet<string>(seenA);
        allSeen.UnionWith(seenB);

        var allNew = new HashSet<string>(newA);
        allNew.UnionWith(newB);
        allNew.RemoveWhere(id =>
            (seenA.Contains(id) && !newA.Contains(id)) || (seenB.Contains(id) && !newB.Contains(id)));

        var allAck = new HashSet<string>(a.codexNewAck ?? new string[0]);
        allAck.UnionWith(b.codexNewAck ?? new string[0]);
        // Entry acks only mean something while the entry is NEW; "ach:" acks stand alone.
        allAck.RemoveWhere(id => !id.StartsWith(Codex.AchievementAckPrefix, StringComparison.Ordinal) && !allNew.Contains(id));

        seen = ProgressSnapshot.IdList(allSeen);
        fresh = ProgressSnapshot.IdList(allNew);
        acked = ProgressSnapshot.IdList(allAck);
    }
}
