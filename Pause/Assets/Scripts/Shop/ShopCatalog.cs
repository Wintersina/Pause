using System.Collections.Generic;
using UnityEngine;

// What the Shop screen sells and what each row says, as pure data (no UI): the Remove Ads / Paragon /
// star dust / booster sections, their state from the billing classes, and the one place a buy tap is decided.
//
// A row is never buyable unless its product is Available (Play returned a price and the billing flow is
// connected), so on iOS, in the editor, or with no price nothing looks buyable: the row reads UNAVAILABLE.
// The Shop entry is on the home screen on every platform (nothing here is platform-specific).
public enum ShopRowKind { RemoveAds, Paragon, Pack, DustBooster, HazardClear, HomingAtoms, WatchAd, QuickAd }

public enum ShopRowState
{
    Unavailable,   // no store / no price: shown, never buyable
    Buyable,       // price known, tap opens Play's sheet
    Buying,        // the sheet is open or the store has not answered yet
    Owned,         // a one-time product already bought
    Locked,        // Paragon only: own more ships first (ShipUnlocks)
    Short,         // booster rows only: not enough star dust, or already at the limit (shown, never buyable)
}

public sealed class ShopRow
{
    public ShopRowKind kind;
    public SupportPurchase.Pack pack;   // Pack rows only
    public Boosters.Kind booster;       // booster rows only
    public bool IsBooster { get { return kind == ShopRowKind.DustBooster || kind == ShopRowKind.HazardClear || kind == ShopRowKind.HomingAtoms; } }
    public ShopRowState state;
    public string title, subtitle, button;
    // Booster rows only: what is held, as one line ("ON  4:32 left", "OFF  x3", "NOT OWNED"), and the on/off switch.
    public string status;
    public bool owned;        // time left / a charge held
    public bool switchedOn;   // the player's switch (Boosters.IsOn)
    public bool HasSwitch { get { return IsBooster && owned; } }
    public bool Tappable { get { return state == ShopRowState.Buyable; } }
}

public sealed class ShopSection
{
    public readonly string label;
    public readonly List<ShopRow> rows = new List<ShopRow>();
    public ShopSection(string label, params ShopRow[] rows) { this.label = label; this.rows.AddRange(rows); }
}

public static class ShopCatalog
{
    public const string Title = "SHOP";
    public const string RemoveAdsTitle = "REMOVE ADS";
    public const string RemoveAdsText = "Support the devs by removing the ads";
    public const string ParagonTitle = "PARAGON";
    public const string ParagonText = "The cheat ship. Bought here, it joins your Space Dock.";
    public const string ParagonOwnedText = "In your Space Dock now.";
    public const string UnavailableLabel = "UNAVAILABLE";
    public const string OwnedLabel = "OWNED";
    public const string BuyingLabel = "...";
    public const string StoreDownNote = "The store is not reachable right now. Check your connection and open the Shop again.";
    // Where no store exists yet (iOS, the editor) a connection is not the problem.
    public const string NoStoreNote = "Purchases are not available on this device yet.";
    public static string StoreDownText
    {
        get { return Application.platform == RuntimePlatform.Android ? StoreDownNote : NoStoreNote; }
    }

    // True while any product's purchase sheet is open. One purchase flow at a time: a second launch before
    // Play's sheet takes focus would leave two products in flight and a flow error could land on the wrong one.
    public static bool AnyBuying
    {
        get { return RemoveAdsPurchase.Buying || ParagonPurchase.Buying || SupportPurchase.Buying != null; }
    }
    public const string RestoreNote = "Bought before? Purchases come back by themselves when the game starts.";

    // The Shop is offered on every platform; there is no platform-specific UI. Where a store has no products
    // (iOS until a StoreKit layer sets prices and launch flows, the editor) every row reads UNAVAILABLE through
    // the same Available rule, so a later StoreKit layer only has to set Price / LaunchFlow.
    public static bool EntryVisible() { return true; }

    // The Shop's sections, top to bottom. A new section (Boosters, later) is one more entry here, one more row
    // kind and its row factory; the panel lays sections out from this list.
    public static List<ShopSection> Sections()
    {
        var packs = new ShopSection("STAR DUST");
        foreach (var pack in SupportPurchase.Packs) packs.rows.Add(PackRow(pack));
        return new List<ShopSection>
        {
            new ShopSection("ADS", RemoveAdsRow()),
            new ShopSection("SHIP", ParagonRow()),
            packs,
            new ShopSection("BOOSTERS", BoosterRow(Boosters.Kind.DustMultiplier), BoosterRow(Boosters.Kind.HazardClear),
                            BoosterRow(Boosters.Kind.HomingAtoms)),
            new ShopSection("FREE STAR DUST", WatchAdRow(), QuickAdRow()),
        };
    }

    // Every row, top to bottom.
    public static List<ShopRow> Rows()
    {
        var rows = new List<ShopRow>();
        foreach (var section in Sections()) rows.AddRange(section.rows);
        return rows;
    }

    public static ShopRow RemoveAdsRow()
    {
        var row = new ShopRow { kind = ShopRowKind.RemoveAds, title = RemoveAdsTitle, subtitle = RemoveAdsText };
        if (AdFree.IsActive) Set(row, ShopRowState.Owned, OwnedLabel);
        else if (RemoveAdsPurchase.Buying) Set(row, ShopRowState.Buying, BuyingLabel);
        else if (RemoveAdsPurchase.Available) Set(row, ShopRowState.Buyable, RemoveAdsPurchase.Price);
        else Set(row, ShopRowState.Unavailable, UnavailableLabel);
        return row;
    }

    public static ShopRow ParagonRow()
    {
        int id = PremiumShips.Paragon;
        var row = new ShopRow { kind = ShopRowKind.Paragon, title = ParagonTitle, subtitle = ParagonText };
        if (ShipId.IsOwned(id)) { row.subtitle = ParagonOwnedText; Set(row, ShopRowState.Owned, OwnedLabel); }
        else if (ShipUnlocks.IsLocked(id))
        {
            // Only while ShipUnlocks.GatesParagon is on (off today): shown, and the purchase refused, even when Play has a price.
            row.subtitle = ShipUnlocks.LockedMessage(id);
            Set(row, ShopRowState.Locked, ShipUnlocks.LockedButtonText(id));
        }
        else if (ParagonPurchase.Buying) Set(row, ShopRowState.Buying, BuyingLabel);
        else if (ParagonPurchase.Available) Set(row, ShopRowState.Buyable, ParagonPurchase.Price);
        else Set(row, ShopRowState.Unavailable, UnavailableLabel);
        return row;
    }

    public static ShopRow PackRow(SupportPurchase.Pack pack)
    {
        var row = new ShopRow { kind = ShopRowKind.Pack, pack = pack, title = pack.label };
        if (pack.Buying) Set(row, ShopRowState.Buying, BuyingLabel);
        else if (pack.Available) Set(row, ShopRowState.Buyable, pack.Price);
        else Set(row, ShopRowState.Unavailable, UnavailableLabel);
        return row;
    }

    public const string WatchAdTitle = "WATCH AD  +100";
    public const string QuickAdTitle = "QUICK AD  +40";
    public const string WatchAdText = "Watch an ad for +100 star dust. As often as you like, even with Remove Ads.";
    public const string QuickAdText = "A quick ad for +40 star dust. As often as you like, even with Remove Ads.";
    public const string WatchAdLoadingLabel = "LOADING";
    public const string WatchAdButton = "+100 DUST";
    public const string QuickAdButton = "+40 DUST";
    public const float WatchAdDust = AdController.ShopAdDust;
    public const float QuickAdDust = AdController.QuickAdDust;

    // "Watch ad: +100 Star Dust" and "Quick ad: +40 Star Dust" (a rewarded interstitial): ALWAYS on offer (Remove Ads and Paragon owners too), repeatable. Buyable only while a
    // rewarded ad is loaded; otherwise a disabled LOADING state (UNAVAILABLE where ads are off altogether). The dust is
    // credited only by the SDK's reward callback (AdController.TryShowShopReward -> StarDustLedger.Grant).
    // Raised (main thread) once the ad's star dust is granted and saved; the open Shop plays its burst and count-up.
    public static event System.Action<float> AdRewarded;
    public static void RaiseAdRewarded(float amount) { var h = AdRewarded; if (h != null) h(amount); }

    public static ShopRow WatchAdRow()
    {
        var row = new ShopRow { kind = ShopRowKind.WatchAd, title = WatchAdTitle, subtitle = WatchAdText };
        Fill(row, AdManager.ShopAdReady, WatchAdButton);
        return row;
    }

    public static ShopRow QuickAdRow()
    {
        var row = new ShopRow { kind = ShopRowKind.QuickAd, title = QuickAdTitle, subtitle = QuickAdText };
        Fill(row, AdManager.ShopQuickReady, QuickAdButton);
        return row;
    }

    // Each ad row shows its own loading state: an ad that is not loaded greys only its row.
    static void Fill(ShopRow row, bool ready, string button)
    {
        if (!AdManager.ShopAdEnabled) Set(row, ShopRowState.Unavailable, UnavailableLabel);
        else if (AdManager.ShowInProgress) Set(row, ShopRowState.Buying, BuyingLabel);
        else if (ready) Set(row, ShopRowState.Buyable, button);
        else Set(row, ShopRowState.Unavailable, WatchAdLoadingLabel);
    }

    // A booster row: bought with star dust (Boosters API), no store involved, so it does not depend on billing being
    // connected and is never held up by the purchase-flow guard (AnyBuying). It is buyable whenever Boosters.CanBuy.
    public static ShopRow BoosterRow(Boosters.Kind kind)
    {
        var row = new ShopRow
        {
            kind = RowKind(kind),
            booster = kind,
            title = Boosters.Name(kind),
            subtitle = Boosters.Description(kind),
            owned = Boosters.Owns(kind),
            switchedOn = Boosters.IsOn(kind),
        };
        row.status = BoosterStatus(kind);
        string price = Mathf.RoundToInt(Boosters.Price(kind)).ToString("N0") + " DUST";
        if (Boosters.CanBuy(kind)) Set(row, ShopRowState.Buyable, price);
        else if (Boosters.AtLimit(kind)) Set(row, ShopRowState.Short, MaxLabel);
        else Set(row, ShopRowState.Short, price);
        return row;
    }

    public const string MaxLabel = "MAX";
    public const string NotOwnedLabel = "NOT OWNED";
    public const string OnLabel = "ON";
    public const string OffLabel = "OFF";

    // The row's held line: "ON  4:32 left" / "OFF  4:32 left" (timed, frozen while off), "ON  x3" / "OFF  x3", or NOT OWNED.
    public static string BoosterStatus(Boosters.Kind kind)
    {
        if (!Boosters.Owns(kind)) return NotOwnedLabel;
        return (Boosters.IsOn(kind) ? OnLabel : OffLabel) + "  " + Boosters.Status(kind);
    }

    // A tap on a booster row's switch: flips it when something is held. True when it changed.
    public static bool TapSwitch(ShopRow row)
    {
        if (row == null || !row.HasSwitch) return false;
        bool want = !Boosters.IsOn(row.booster);
        return Boosters.SetOn(row.booster, want) && Boosters.IsOn(row.booster) == want;
    }

    static ShopRowKind RowKind(Boosters.Kind kind)
    {
        switch (kind)
        {
            case Boosters.Kind.DustMultiplier: return ShopRowKind.DustBooster;
            case Boosters.Kind.HomingAtoms: return ShopRowKind.HomingAtoms;
            default: return ShopRowKind.HazardClear;
        }
    }

    static void Set(ShopRow row, ShopRowState state, string button) { row.state = state; row.button = button; }

    // True while no product at all has a price (the store has not answered, or there is none): the screen says so
    // instead of looking empty.
    public static bool StoreDown
    {
        get
        {
            if (RemoveAdsPurchase.Available || ParagonPurchase.Available) return false;
            foreach (var pack in SupportPurchase.Packs) if (pack.Available) return false;
            return true;
        }
    }

    // A buy tap on a row. True when Play's sheet was opened. A refused Paragon (gate) or an unavailable row does
    // nothing here; the panel toasts the gate.
    public static bool Tap(ShopRow row)
    {
        if (row == null || !row.Tappable) return false;
        // Booster rows spend star dust on the spot: no sheet, so they neither wait for nor block a purchase flow.
        if (row.IsBooster) return Boosters.Buy(row.booster) == Boosters.BuyResult.Bought;
        // The ad row has no purchase flow either: it never waits on, or blocks, a store sheet.
        if (row.kind == ShopRowKind.WatchAd) return AdManager.TryShowShopReward(RaiseAdRewarded, null);
        if (row.kind == ShopRowKind.QuickAd) return AdManager.TryShowQuickReward(RaiseAdRewarded, null);
        if (AnyBuying) return false;
        switch (row.kind)
        {
            case ShopRowKind.RemoveAds: return RemoveAdsPurchase.RequestBuy();
            case ShopRowKind.Paragon: return ParagonPurchase.RequestBuy();
            default: return row.pack != null && row.pack.RequestBuy();
        }
    }

    // ---- reaching the Shop from elsewhere (the dock's Paragon berth) ----

    // Set by Open; the home screen's Shop row opens the panel once when it loads and clears it.
    public static bool OpenOnHome { get; set; }

    // Takes the player to the Shop from another screen: home, with the panel opening on arrival.
    public static bool Open()
    {
        if (!EntryVisible()) return false;
        OpenOnHome = true;
        BackNavigator.LoadScene(BackNavigator.HomeScene);
        return true;
    }
}
