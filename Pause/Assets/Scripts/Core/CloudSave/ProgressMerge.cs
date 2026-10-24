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
//   currentWorld, startWorld,        savedAtUtc (local wins a tie). A side with
//   equipped skins                   no progress at all (HasProgress false: a
//                                    fresh install) counts as savedAtUtc 0, so
//                                    it never beats a real save.
//   currency, economy                the newer side's balance, MINUS the price of
//                                    every ship / skin owned only on the OTHER
//                                    side. Purchases are unioned, so without
//                                    this a ship bought on phone B would come
//                                    back with its dust refunded by newer phone
//                                    A's old balance. (Dust earned on the older
//                                    side after the newer side's stamp is still
//                                    superseded; the newer balance is the truth.)
//
// The result is stamped with the newer of the two timestamps.
public static class ProgressMerge
{
    public static ProgressSnapshot Merge(ProgressSnapshot local, ProgressSnapshot cloud)
    {
        if (local == null) return cloud;
        if (cloud == null) return local;

        long localAt = local.HasProgress() ? local.savedAtUtc : 0;
        long cloudAt = cloud.HasProgress() ? cloud.savedAtUtc : 0;
        bool cloudNewer = cloudAt > localAt;
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

        var other = cloudNewer ? local : cloud;
        float currency = newer.currency - UnpaidPurchases(newer, other);
        if (currency < 0f) currency = 0f;

        int highestWorld = Math.Max(local.highestWorld, cloud.highestWorld);
        var result = new ProgressSnapshot
        {
            schemaVersion = ProgressSnapshot.CurrentSchemaVersion,
            savedAtUtc = Math.Max(local.savedAtUtc, cloud.savedAtUtc),
            currency = currency,
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
            codexPaid = MergePaid(local, cloud),
            codexKilled = DamageLog.MergeMax(local.codexKilled, cloud.codexKilled),
            codexHurt = DamageLog.MergeMax(local.codexHurt, cloud.codexHurt),
        };
        return result;
    }

    // Star dust price of what `other` owns (ships and skins) that `newer` does
    // not: those purchases are kept by the union, so they must be paid for out
    // of newer's balance. Premium (real-money) ships have no dust price.
    public static float UnpaidPurchases(ProgressSnapshot newer, ProgressSnapshot other)
    {
        float total = 0f;
        var haveShips = new HashSet<int>(newer.boughtShips ?? new int[0]);
        foreach (int id in other.boughtShips ?? new int[0])
            if (!haveShips.Contains(id)) total += ShipDustPrice(id);
        var haveSkins = new HashSet<int>(newer.ownedSkins ?? new int[0]);
        foreach (int code in other.ownedSkins ?? new int[0])
            if (!haveSkins.Contains(code))
            {
                int id = code / ProgressSnapshot.SkinCode, skin = code % ProgressSnapshot.SkinCode;
                total += ShipSkins.PriceOf(id, skin);
            }
        return total;
    }

    static float ShipDustPrice(int id)
    {
        if (id <= shopingShips.StarterShip || id >= shopingShips.Prices.Length) return 0f;
        if (PremiumShips.IsPremium(id)) return 0f;
        return shopingShips.Prices[id];
    }

    // A collected unlock reward stays collected on every device: union.
    public static string[] MergePaid(ProgressSnapshot a, ProgressSnapshot b)
    {
        var all = new HashSet<string>(a.codexPaid ?? new string[0]);
        all.UnionWith(b.codexPaid ?? new string[0]);
        return ProgressSnapshot.IdList(all);
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
