using System;
using System.Collections.Generic;

// How a device's progress and its account's cloud save combine. This is the
// one place to change the rule.
//
//   bought ships                     union
//   HighestSpeed, highestWorld,      max
//   achievement/kill counters
//   HasDoneTut                       true if either side has it
//   owned hull skins                 union
//   currency, spawnShip,             from whichever side has the newer
//   currentWorld, equipped skins     savedAtUtc (local wins a tie)
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

        int highestWorld = Math.Max(local.highestWorld, cloud.highestWorld);
        var result = new ProgressSnapshot
        {
            schemaVersion = ProgressSnapshot.CurrentSchemaVersion,
            savedAtUtc = Math.Max(local.savedAtUtc, cloud.savedAtUtc),
            currency = newer.currency,
            spawnShip = newer.spawnShip,
            currentWorld = Math.Min(newer.currentWorld, highestWorld),
            boughtShips = new List<int>(ships).ToArray(),
            highestSpeed = Math.Max(local.highestSpeed, cloud.highestSpeed),
            highestWorld = highestWorld,
            hasDoneTut = local.hasDoneTut || cloud.hasDoneTut,
            counters = mergedCounters.ToArray(),
            ownedSkins = new List<int>(skins).ToArray(),
            equippedSkins = (int[])(newer.equippedSkins ?? new int[0]).Clone(),
        };
        return result;
    }
}
