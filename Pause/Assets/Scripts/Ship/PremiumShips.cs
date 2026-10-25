using UnityEngine;

// Premium ships: hulls that are NOT part of the star-dust economy.
//
// Today there is one: Paragon, the "cheat ship" (id 16). It is meant to be
// sold for real money (App Store / Google Play in-app purchase), so:
//   * it has no dust price and the dock shows it locked as "COMING SOON";
//   * nothing in normal play can own it (shopingShips.TryPurchase refuses it);
//   * developer mode owns it like every other ship (DeveloperUnlocks writes
//     boughtship<id> for the whole roster), which is the only way to test it;
//   * its skins are bought with dust (50,000 each) once the ship is owned;
//   * it does not count toward the "own every ship" / "every skin" sets, so
//     the free-to-play progression stays completable without it.
//
// TODO(IAP): sell Paragon as a real-money in-app purchase on both stores,
// product id ParagonProductId (placeholder, create it in App Store Connect
// and Google Play Console, non-consumable). On a verified purchase or a
// restore call PremiumShips.Grant(id) -- it writes the same
// "boughtship<id>" key the roster always used, so ProgressSnapshot (cloud
// save), the dock and the spawner pick it up unchanged. The ownership gate
// is ShipId.IsOwned; the dust-purchase gate is shopingShips.TryPurchase;
// the dock's "COMING SOON" state is DockPopup / DockBay / SpaceDock.Buy.
public struct PremiumTier
{
    public int hearts;          // the hull's own lives at this tier
    public int startSpeedHud;   // the run's starting HUD speed
    public float missileEvery;  // seconds between mini-warhead volleys
    public int missiles;        // mini-warheads per volley
    public float shieldEvery;   // seconds between shields
    public float shieldSeconds; // how long each shield holds
}

public static class PremiumShips
{
    public const int Paragon = 16;

    // Placeholder store product id (see the TODO(IAP) above).
    public const string ParagonProductId = "me.hapticgate.pause.ship_paragon";

    // Every skin of a premium ship costs this much star dust (100,000 for the first two).
    public const float SkinPrice = 50000f;

    // Whether Paragon exists for this player: its feature flag ("paragon_ship",
    // default ON, a locked COMING SOON berth) or developer mode. Flag off, ShipId.Count is 15 and Paragon is not
    // a valid id anywhere (dock, codex, saves, achievements, selection).
    public static bool Visible
    {
        get { return DeveloperUnlocks.Enabled || FeatureFlags.IsOn(FeatureFlags.ParagonShip); }
    }

    public static bool IsPremium(int id) { return id == Paragon; }

    // Roster ships that live in the dust economy (the 15 originals).
    public static int StandardCount { get { return shopingShips.RosterTotal - 2; } }

    // The roster without premium ships: the dust economy's ships.
    public static System.Collections.Generic.IEnumerable<int> StandardIds
    {
        get { foreach (int id in ShipId.All) if (!IsPremium(id)) yield return id; }
    }

    // Whether star dust can buy the hull itself. Never for a premium ship.
    public static bool DustPurchasable(int id) { return ShipId.IsValid(id) && !IsPremium(id); }

    // Locked and not buyable yet: the dock reads "COMING SOON".
    public static bool ComingSoon(int id) { return Visible && IsPremium(id) && !ShipId.IsOwned(id); }

    public const string ComingSoonLabel = "COMING SOON";

    // The warning every place that meets a premium ship shows (dock card, codex,
    // the offer before any purchase, the results screen says it shorter): a Paragon run never
    // reaches a leaderboard (RunScore.Unranked, LeaderboardService). The IAP flow
    // MUST show this line before the player pays.
    public const string LeaderboardNotice = "CHEAT SHIP: NOT ON LEADERBOARDS";
    public const string UnrankedRunLabel = "CHEAT SHIP - UNRANKED";

    // The only code path that may mark a premium ship owned: the (future) IAP
    // flow. Developer mode writes the key itself.
    public static void Grant(int id)
    {
        if (!IsPremium(id)) return;
        PlayerPrefs.SetString(ShipId.OwnedKey(id), "True");
        PrefsSaver.SaveNow();
    }

    // ---------------------------------------------------------------- tiers
    // One row per skin tier: 0 = hull only, then one per skin owned. The one
    // table the game, the dock and the tests read.
    static readonly PremiumTier[] paragon =
    {
        new PremiumTier { hearts = 10, startSpeedHud = 20, missileEvery = 10f,  missiles = 2, shieldEvery = 8f,   shieldSeconds = 2.5f },
        new PremiumTier { hearts = 11, startSpeedHud = 22, missileEvery = 8.5f, missiles = 3, shieldEvery = 7.5f, shieldSeconds = 3f },
        new PremiumTier { hearts = 12, startSpeedHud = 24, missileEvery = 7f,   missiles = 3, shieldEvery = 7f,   shieldSeconds = 3.5f },
        new PremiumTier { hearts = 13, startSpeedHud = 26, missileEvery = 6f,   missiles = 4, shieldEvery = 6f,   shieldSeconds = 3.5f },
        new PremiumTier { hearts = 14, startSpeedHud = 28, missileEvery = 5f,   missiles = 5, shieldEvery = 5f,   shieldSeconds = 4f },
    };

    public static int TierCount(int id) { return IsPremium(id) ? paragon.Length : 0; }

    public static PremiumTier Tier(int id, int tier)
    {
        if (!IsPremium(id)) return default(PremiumTier);
        return paragon[Mathf.Clamp(tier, 0, paragon.Length - 1)];
    }

    // The tier the ship flies at: how many of its skins are owned.
    public static int TierOf(int id) { return Mathf.Min(ShipWeaponUpgrades.Level(id), TierCount(id) - 1); }

    public static PremiumTier Current(int id) { return Tier(id, TierOf(id)); }

    // The most hearts any premium hull can fly with (the heart orbit's room).
    public static int MostHearts { get { return paragon[paragon.Length - 1].hearts; } }
}
