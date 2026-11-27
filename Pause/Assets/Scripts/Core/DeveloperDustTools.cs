using UnityEngine;

// Developer-only test helpers on the Options screen's developer rows: +1000 STARDUST and
// WIPE DUST + COLLECTION (a fresh-player state to test from). Every entry point is a no-op
// unless developer mode is on (DeveloperUnlocks.Enabled, which is itself false in a release
// build), so a player build can never reach them.
//
// The wipe resets what dust or play earned and keeps what real money or the account owns:
//
//   WIPED   star dust (to 0); every owned ship except Paragon (back to the starter, selected);
//           every owned / equipped hull skin (the ship upgrades); world progress (highest and
//           current world, the START WORLD pick); Codex discoveries, NEW dots, collected-reward
//           marks, killed / hurt tallies, atomic combo discoveries and the last-stand replay;
//           boosters (bought / used counters and their on-off switches); the dust-offer
//           streak; achievement unlocks, claims, counters and boss / elite flags.
//   KEPT    Remove Ads (AdFree) and Paragon (real-money entitlements); best score, top speed,
//           leaderboard bookkeeping and the shared-board settings; play time; the tutorial flag;
//           audio, vibration, perch, offer-toggle and other settings; developer mode and its
//           world pick; sign-in and account link; the Codex back-pay marker (so it cannot pay again).
//
// Cloud saves: the wipe is local. Once the session's cloud save is Ready, the changed progress
// uploads like any change (so it overwrites the account's save and is not merged back). A
// session that never reached Ready (offline, signed out, read-only) leaves the account's old
// save in the cloud, and the next successful sync merges it (max / union) back in: sign out in
// Options first for a fresh state that stays.
public static class DeveloperDustTools
{
    public const float Bonus = 1000f;
    public const float ConfirmSeconds = 4f;

    // Adds 1000 star dust through the ledger's own grant, then saves. Returns the new balance
    // (or -1 when developer mode is off and nothing was done).
    public static float AddDust()
    {
        if (!DeveloperUnlocks.Enabled) return -1f;
        StarDustLedger.Grant(Bonus);
        PrefsSaver.SaveNow();
        return StarDustLedger.Saved;
    }

    // Two-tap confirm without any dialog: the first tap arms (returns false), a second tap within
    // ConfirmSeconds confirms (returns true and disarms). `armedAt` < 0 means not armed.
    public static bool Confirm(ref float armedAt, float now)
    {
        if (armedAt >= 0f && now - armedAt <= ConfirmSeconds) { armedAt = -1f; return true; }
        armedAt = now;
        return false;
    }

    // Wipes dust and collection (see the header). Returns false, touching nothing, when
    // developer mode is off.
    public static bool Wipe()
    {
        if (!DeveloperUnlocks.Enabled) return false;

        // Developer mode parks the real ownership / world progress behind its all-unlocked
        // override: switch it off (restoring the real values), wipe those, switch it back on
        // (snapshotting the wiped state and re-granting the override).
        DeveloperUnlocks.SetEnabled(false);
        WipeRealProgress();
        DeveloperUnlocks.SetEnabled(true);

        PrefsSaver.SaveNow();
        return true;
    }

    static void WipeRealProgress()
    {
        // Star dust. An open run's ledger is told too, so its next absolute write keeps the zero.
        float before = PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey, 0f);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 0f);
        StarDustLedger.Spend(before);

        // Ships: only the starter stays (selected); Paragon is a real-money entitlement.
        for (int id = 1; id < shopingShips.RosterTotal; id++)
        {
            if (PremiumShips.IsPremium(id)) continue;
            if (id == ShipId.Starter) PlayerPrefs.SetString(ShipId.OwnedKey(id), "True");
            else PlayerPrefs.DeleteKey(ShipId.OwnedKey(id));
        }
        PlayerPrefs.SetInt(ShipId.SelectedKey, ShipId.Starter);

        // Skins (the ship upgrades), every hull including Paragon's (those cost dust).
        for (int id = 0; id <= shopingShips.RosterTotal; id++)
        {
            PlayerPrefs.DeleteKey(ShipSkins.EquippedKey(id));
            PlayerPrefs.DeleteKey(ShipSkins.DeveloperEquippedKey(id));
            for (int n = 0; n < ShipSkins.PerShip; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(id, n));
        }

        // World progress.
        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);
        PlayerPrefs.DeleteKey(WorldManager.PrefsCurrentWorld);
        PlayerPrefs.DeleteKey(PlayerStartWorld.Key);

        // Codex and combo discoveries.
        PlayerPrefs.DeleteKey(Codex.PrefsKey);
        PlayerPrefs.DeleteKey(Codex.NewKey);
        PlayerPrefs.DeleteKey(Codex.AckKey);
        PlayerPrefs.DeleteKey(Codex.PaidKey);
        PlayerPrefs.DeleteKey(DamageLog.KilledKey);
        PlayerPrefs.DeleteKey(DamageLog.HurtKey);
        PlayerPrefs.DeleteKey(FusionDiscoveryStore.PrefsKey);
        PlayerPrefs.DeleteKey(FusionDiscoveryStore.UnseenKey);
        KillCamArchive.Clear();

        // Boosters and the dust-offer streak.
        foreach (string key in Boosters.AllKeys) PlayerPrefs.DeleteKey(key);
        PlayerPrefs.DeleteKey(UpgradeOfferStore.StreakKey);
        PlayerPrefs.DeleteKey(UpgradeOfferStore.DeclinedKey);
        PlayerPrefs.DeleteKey(UpgradeOfferStore.LastDiedKey);

        // Achievement progress (the store-side schema / sync marks stay).
        foreach (string key in AchievementStore.SyncKeys()) PlayerPrefs.DeleteKey(key);
        foreach (string key in AchievementMigration.LegacyKeys()) PlayerPrefs.DeleteKey(key);

        // Drop the in-memory copies so open screens and the next write see the wiped state.
        Codex.OnExternalChange();
        FusionDiscoveryStore.Reload();
        Boosters.Reload();
    }
}
