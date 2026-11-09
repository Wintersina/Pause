using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The Shop screen (ShopCatalog rows + ShopPanel): rows for owned / not owned / unavailable / buying / gated, the
// sections, the buy tap, the panel's scroll, Back and star dust read-out, the home Shop row, and the dock no
// longer selling packs or Paragon.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShopScreenTest.Run
public static class ShopScreenTest
{
    static int failures;

    static void Check(string label, bool condition)
    {
        if (!condition) failures++;
        Debug.Log("[SHOPSCR] " + (condition ? "PASS " : "FAIL ") + label);
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const int P = PremiumShips.Paragon;

    static void Reset()
    {
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.DeleteKey(AdFree.Key);
        for (int id = 1; id <= 16; id++) PlayerPrefs.DeleteKey(ShipId.OwnedKey(id));
        PlayerPrefs.SetString(ShipId.OwnedKey(1), "True");
        RemoveAdsPurchase.ResetSession();
        ParagonPurchase.ResetSession();
        SupportPurchase.ResetSession();
        foreach (string k in BoosterKeys) PlayerPrefs.DeleteKey(k);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 0f);
        Boosters.Reload();
    }

    static readonly string[] BoosterKeys = Boosters.AllKeys;

    static void OwnShips(int n)
    {
        for (int id = 2; id <= n; id++) PlayerPrefs.SetString(ShipId.OwnedKey(id), "True");
    }

    static void Connect()
    {
        RemoveAdsPurchase.LaunchFlow = () => { };
        ParagonPurchase.LaunchFlow = () => { };
        foreach (var p in SupportPurchase.Packs) p.LaunchFlow = () => { };
    }

    public static int Execute()
    {
        failures = 0;
        using var sandbox = new TestHarness.Sandbox();
        Reset();
        try
        {
            Rows();
            RowIcons();
            Gate();
            Taps();
            BoosterRows();
            Panel();
            Dock();
            Home();
        }
        finally
        {
            Reset();
            BackNavigator.ResetHooks();
            ShopCatalog.OpenOnHome = false;
        }
        return failures;
    }

    // every priced row (Paragon keeps its own art; Remove Ads, packs and boosters each get a sprite of their own)
    static void RowIcons()
    {
        var seen = new HashSet<Sprite>();
        bool allFound = true, distinct = true, pixel = true;
        int count = 0;
        foreach (var r in ShopCatalog.Rows())
        {
            if (r.kind == ShopRowKind.Paragon || r.kind == ShopRowKind.QuickAd) continue;   // the quick ad shares the +100 row's icon for now
            count++;
            var sprite = ShopIconArt.For(r);
            if (sprite == null) { allFound = false; continue; }
            if (!seen.Add(sprite)) distinct = false;
            pixel &= sprite.texture.filterMode == FilterMode.Point && sprite.texture.mipmapCount == 1;
        }
        Check("Remove Ads, three packs, three boosters and the watch-ad row: " + count + " rows", count == 8);
        Check("every Remove Ads, pack, booster and watch-ad row resolves to a sprite", allFound);
        Check("each of them has its own sprite", distinct && seen.Count == 8);
        Check("shop icons are Point filtered with no mipmaps (crisp pixel art)", pixel);
        var small = ShopIconArt.NameFor(new ShopRow { kind = ShopRowKind.Pack, pack = SupportPurchase.Small });
        var huge = ShopIconArt.NameFor(new ShopRow { kind = ShopRowKind.Pack, pack = SupportPurchase.Huge });
        Check("Remove Ads maps to its icon", ShopIconArt.NameFor(ShopCatalog.RemoveAdsRow()) == "remove_ads");
        Check("the watch-ad row has its own icon", ShopIconArt.NameFor(ShopCatalog.WatchAdRow()) == "ad_dust");
        Check("the quick-ad row shares it for now", ShopIconArt.NameFor(ShopCatalog.QuickAdRow()) == "ad_dust");
        Check("packs map by size", small == "pack_850" && huge == "pack_6000");
    }

    static void Rows()
    {
        var sections = ShopCatalog.Sections();
        Check("five sections: ads, ship, star dust, boosters, free star dust (one list the panel lays out from)",
              sections.Count == 5 && sections[4].label == "FREE STAR DUST" && sections[4].rows.Count == 2 && sections[4].rows[0].kind == ShopRowKind.WatchAd && sections[4].rows[1].kind == ShopRowKind.QuickAd && sections[0].label == "ADS" && sections[1].label == "SHIP" && sections[2].label == "STAR DUST" &&
              sections[3].label == "BOOSTERS" && sections[3].rows.Count == 3);
        var rows = ShopCatalog.Rows();
        Check("rows: Remove Ads, Paragon, 850 / 2,500 / 6,000, then Dust x2, Hazard Clear and Homing Atoms, then the two ad rows", rows.Count == 10 &&
              rows[8].title == ShopCatalog.WatchAdTitle && rows[9].title == ShopCatalog.QuickAdTitle && ShopCatalog.WatchAdDust == 100f && ShopCatalog.QuickAdDust == 40f &&
              rows[5].kind == ShopRowKind.DustBooster && rows[6].kind == ShopRowKind.HazardClear && rows[7].kind == ShopRowKind.HomingAtoms &&
              rows[7].IsBooster &&
              rows[0].kind == ShopRowKind.RemoveAds && rows[1].kind == ShopRowKind.Paragon &&
              rows[2].pack.dust == 850 && rows[3].pack.dust == 2500 && rows[4].pack.dust == 6000);
        Check("Remove Ads row says what it does", rows[0].subtitle == "Support the devs by removing the ads");

        // unavailable (iOS, the editor, no Play price): shown, never buyable (the gate is open: 5 ships owned)
        OwnShips(5);
        rows = ShopCatalog.Rows();
        bool any = false;
        foreach (var r in rows) any |= r.Tappable;
        Check("no store: every row reads UNAVAILABLE and none is buyable",
              !any && rows[0].state == ShopRowState.Unavailable && rows[1].state == ShopRowState.Unavailable &&
              rows[2].button == "UNAVAILABLE" && rows[4].state == ShopRowState.Unavailable);
        Check("no store: the screen says so (StoreDown)", ShopCatalog.StoreDown);
        Check("the Shop is offered on every platform", ShopCatalog.EntryVisible());

        // price but no billing flow (a price without a connection) is still unavailable
        RemoveAdsPurchase.SetPrice("$3.99");
        Check("a price without a billing flow is not buyable", ShopCatalog.RemoveAdsRow().state == ShopRowState.Unavailable);

        // priced and connected: buyable with Play's price
        Connect();
        RemoveAdsPurchase.SetPrice("$3.99");
        ParagonPurchase.SetPrice("$4.99");
        SupportPurchase.Small.SetPrice("$4.00");
        SupportPurchase.Large.SetPrice("$10.00");
        SupportPurchase.Huge.SetPrice("$20.00");
        rows = ShopCatalog.Rows();
        Check("priced: every row shows Play's price and is buyable",
              rows[0].button == "$3.99" && rows[1].button == "$4.99" && rows[2].button == "$4.00" &&
              rows[3].button == "$10.00" && rows[4].button == "$20.00" && rows[0].Tappable && rows[1].Tappable && rows[4].Tappable);
        Check("priced: the store is up", !ShopCatalog.StoreDown);

        // owned
        PlayerPrefs.SetInt(AdFree.Key, 1);
        Check("Remove Ads owned: reads OWNED and cannot be bought again",
              ShopCatalog.RemoveAdsRow().state == ShopRowState.Owned && ShopCatalog.RemoveAdsRow().button == "OWNED" &&
              !ShopCatalog.Tap(ShopCatalog.RemoveAdsRow()));
        PremiumShips.Grant(P);
        Check("Paragon owned: reads OWNED and is not buyable",
              ShopCatalog.ParagonRow().state == ShopRowState.Owned && !ShopCatalog.ParagonRow().Tappable && !ParagonPurchase.RequestBuy());
        Check("packs are repeatable: still buyable after other purchases", ShopCatalog.PackRow(SupportPurchase.Small).Tappable);
        Check("dust pack row text uses the pack label", ShopCatalog.PackRow(SupportPurchase.Huge).title == "+6,000 STAR DUST");
        Reset();
    }

    static void Gate()
    {
        Connect();
        ParagonPurchase.SetPrice("$4.99");
        int launches = 0;
        ParagonPurchase.LaunchFlow = () => launches++;
        // Reset leaves only the starter owned
        var row = ShopCatalog.ParagonRow();
        Check("no Paragon gate: only the starter owned -> row is Buyable with Play's price",
              ShipUnlocks.OwnedCount() == 1 && row.state == ShopRowState.Buyable && row.button == "$4.99" && row.Tappable);
        Check("no Paragon gate: RequestBuy is not refused", ParagonPurchase.RequestBuy() && launches == 1 && ParagonPurchase.Buying);
        ParagonPurchase.EndBuy();
        OwnShips(3);
        Check("no Paragon gate: still buyable with 3 ships owned", ShopCatalog.ParagonRow().state == ShopRowState.Buyable);
        Check("the gate is one switch (ShipUnlocks.GatesParagon), off today", !ShipUnlocks.GatesParagon);
        var g = ShipUnlocks.GatedIds;
        Reset();
        Check("the other two gated ships stay locked with only the starter owned",
              ShipUnlocks.IsLocked(g[0]) && ShipUnlocks.IsLocked(g[1]) && !ShipUnlocks.IsLocked(g[2]) &&
              ShipUnlocks.RequiredOwned(g[0]) == 4 && ShipUnlocks.RequiredOwned(g[1]) == 4 && g[2] == PremiumShips.Paragon);
        Reset();
    }

    static void Taps()
    {
        int launches = 0, packLaunches = 0;
        RemoveAdsPurchase.LaunchFlow = () => launches++;
        SupportPurchase.Large.LaunchFlow = () => packLaunches++;
        RemoveAdsPurchase.SetPrice("$3.99");
        SupportPurchase.Large.SetPrice("$10.00");
        Check("tap on Remove Ads opens the sheet once", ShopCatalog.Tap(ShopCatalog.RemoveAdsRow()) && launches == 1);
        Check("while the sheet is open the row reads ... and a second tap does nothing",
              ShopCatalog.RemoveAdsRow().state == ShopRowState.Buying && !ShopCatalog.Tap(ShopCatalog.RemoveAdsRow()) && launches == 1);
        // one purchase flow at a time: a second product's tap (or direct request) while a sheet is open does nothing,
        // so a flow error can never land on the wrong product
        Check("while Remove Ads' sheet is open a pack tap does nothing", ShopCatalog.AnyBuying &&
              !ShopCatalog.Tap(ShopCatalog.PackRow(SupportPurchase.Large)) && !SupportPurchase.Large.RequestBuy() && packLaunches == 0 &&
              SupportPurchase.Buying == null);
        RemoveAdsPurchase.EndBuy();
        Check("once that sheet closes the pack tap opens its own sheet",
              ShopCatalog.Tap(ShopCatalog.PackRow(SupportPurchase.Large)) && packLaunches == 1 && SupportPurchase.Buying == SupportPurchase.Large);
        Check("with a pack open Remove Ads cannot start", !RemoveAdsPurchase.RequestBuy() && !ShopCatalog.Tap(ShopCatalog.RemoveAdsRow()) && launches == 1);
        // a billing disconnect answers no sheet: every open buy ends, no row is left on "..."
        PlayBilling.EndAllBuys();
        Check("a disconnect ends every open buy", !ShopCatalog.AnyBuying &&
              ShopCatalog.PackRow(SupportPurchase.Large).state == ShopRowState.Buyable);
        RemoveAdsPurchase.SetPrice(null);
        SupportPurchase.Large.SetPrice(null);
        Check("and the rows then read UNAVAILABLE, not ...", ShopCatalog.RemoveAdsRow().state == ShopRowState.Unavailable &&
              ShopCatalog.PackRow(SupportPurchase.Large).state == ShopRowState.Unavailable);
        Check("an unavailable pack tap does nothing", !ShopCatalog.Tap(ShopCatalog.PackRow(SupportPurchase.Small)));
        Reset();
    }

    static void BoosterRows()
    {
        Reset();   // no billing connected, 0 dust
        var dustRow = ShopCatalog.BoosterRow(Boosters.Kind.DustMultiplier);
        var clearRow = ShopCatalog.BoosterRow(Boosters.Kind.HazardClear);
        Check("booster rows carry the Boosters name and description",
              dustRow.title == Boosters.Name(Boosters.Kind.DustMultiplier) && clearRow.title == Boosters.Name(Boosters.Kind.HazardClear) &&
              dustRow.subtitle == Boosters.Description(Boosters.Kind.DustMultiplier));
        Check("no dust: both rows show the price and are not buyable",
              dustRow.state == ShopRowState.Short && dustRow.button == "600 DUST" && !dustRow.Tappable &&
              clearRow.state == ShopRowState.Short && clearRow.button == "200 DUST" && !ShopCatalog.Tap(clearRow));
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 1000f);
        Check("with dust and NO billing connection the rows are still buyable",
              ShopCatalog.StoreDown && ShopCatalog.BoosterRow(Boosters.Kind.DustMultiplier).Tappable &&
              ShopCatalog.BoosterRow(Boosters.Kind.HazardClear).Tappable);

        // a store sheet is open: dust purchases still go through and do not touch the guard
        RemoveAdsPurchase.LaunchFlow = () => { };
        RemoveAdsPurchase.SetPrice("$3.99");
        RemoveAdsPurchase.RequestBuy();
        Check("a store sheet is open", ShopCatalog.AnyBuying);
        Check("Dust x2 can be bought while a sheet is open: pays 600 and starts the clock",
              ShopCatalog.Tap(ShopCatalog.BoosterRow(Boosters.Kind.DustMultiplier)) && Boosters.DustActive &&
              Mathf.Approximately(StarDustLedger.Saved, 400f));
        Check("the purchase guard is unchanged: still the one sheet", ShopCatalog.AnyBuying && RemoveAdsPurchase.Buying &&
              SupportPurchase.Buying == null && !ShopCatalog.Tap(ShopCatalog.RemoveAdsRow()));
        RemoveAdsPurchase.EndBuy();
        Check("a booster tap never sets a Buying flag", !ShopCatalog.AnyBuying);

        // hazard clear: 200 each, max 9
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 5000f);
        var heldRow = ShopCatalog.BoosterRow(Boosters.Kind.DustMultiplier);
        Check("Dust x2 now reads ON and its time left on the row's held line", heldRow.status == "ON  5:00 left" && heldRow.owned && heldRow.switchedOn &&
              heldRow.HasSwitch && heldRow.title == Boosters.Name(Boosters.Kind.DustMultiplier));
        Check("the switch turns it off: the held line reads OFF with the time frozen",
              ShopCatalog.TapSwitch(heldRow) && !Boosters.IsOn(Boosters.Kind.DustMultiplier) &&
              ShopCatalog.BoosterRow(Boosters.Kind.DustMultiplier).status == "OFF  5:00 left" && !Boosters.AnyActive);
        Check("... and on again", ShopCatalog.TapSwitch(ShopCatalog.BoosterRow(Boosters.Kind.DustMultiplier)) && Boosters.IsOn(Boosters.Kind.DustMultiplier) &&
              ShopCatalog.BoosterRow(Boosters.Kind.DustMultiplier).status.StartsWith("ON"));
        Check("a row with nothing held reads NOT OWNED and has no switch",
              ShopCatalog.BoosterRow(Boosters.Kind.HazardClear).status == ShopCatalog.NotOwnedLabel && !ShopCatalog.BoosterRow(Boosters.Kind.HazardClear).HasSwitch &&
              !ShopCatalog.TapSwitch(ShopCatalog.BoosterRow(Boosters.Kind.HazardClear)));
        int bought = 0;
        while (ShopCatalog.Tap(ShopCatalog.BoosterRow(Boosters.Kind.HazardClear)) && bought < 20) bought++;
        var full = ShopCatalog.BoosterRow(Boosters.Kind.HazardClear);
        Check("hazard clear sells up to 9 charges, then reads MAX and is dead",
              bought == Boosters.MaxClearCharges && Boosters.ClearCharges == 9 && full.state == ShopRowState.Short && full.button == "MAX" &&
              !full.Tappable && full.status == "ON  x9");
        Check("hazard clear can be switched off so it stops auto-firing", ShopCatalog.TapSwitch(full) &&
              ShopCatalog.BoosterRow(Boosters.Kind.HazardClear).status == "OFF  x9" && !Boosters.ClearAvailable);
        Boosters.SetOn(Boosters.Kind.HazardClear, true);
        Check("a refused tap spent nothing", Mathf.Approximately(StarDustLedger.Saved, 5000f - 9f * 200f));

        // homing atoms: 1,000 a purchase, 5 minutes each, the same one-hour cap
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 999f);
        var homingRow = ShopCatalog.BoosterRow(Boosters.Kind.HomingAtoms);
        Check("homing atoms: 999 dust is short of the 1,000 price",
              homingRow.state == ShopRowState.Short && homingRow.button == "1,000 DUST" && !ShopCatalog.Tap(homingRow) &&
              homingRow.title == Boosters.Name(Boosters.Kind.HomingAtoms) && homingRow.status == ShopCatalog.NotOwnedLabel);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 1500f);
        Check("homing atoms: a tap pays 1,000 and starts 5 minutes",
              ShopCatalog.Tap(ShopCatalog.BoosterRow(Boosters.Kind.HomingAtoms)) && Mathf.Approximately(StarDustLedger.Saved, 500f) &&
              Boosters.HomingActive && Mathf.Approximately(Boosters.HomingRemainingSeconds, Boosters.HomingSeconds) &&
              ShopCatalog.BoosterRow(Boosters.Kind.HomingAtoms).status == "ON  5:00 left");
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100000f);
        int homingBought = 1;
        while (ShopCatalog.Tap(ShopCatalog.BoosterRow(Boosters.Kind.HomingAtoms)) && homingBought < 20) homingBought++;
        Check("homing atoms stop at the one-hour stored cap (12 purchases) and then read MAX",
              homingBought == 12 && ShopCatalog.BoosterRow(Boosters.Kind.HomingAtoms).button == "MAX");

        // dust x2 stops at the stored-time cap
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100000f);
        int dustBought = 0;
        while (ShopCatalog.Tap(ShopCatalog.BoosterRow(Boosters.Kind.DustMultiplier)) && dustBought < 20) dustBought++;
        Check("Dust x2 stops at the one-hour stored cap and then reads MAX",
              Boosters.RemainingSeconds <= Boosters.MaxStoredSeconds + .01f && ShopCatalog.BoosterRow(Boosters.Kind.DustMultiplier).button == "MAX");
        Reset();
    }

    static void RunFx(ShopPanel panel) { for (int n = 0; n < 400 && panel.FxActive; n++) panel.StepFx(1f / 60f); }

    static void Panel()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/startS4.unity", OpenSceneMode.Single);
        BackNavigator.ResetHooks();
        OwnShips(5);
        Connect();
        RemoveAdsPurchase.SetPrice("$3.99");
        ParagonPurchase.SetPrice("$4.99");
        foreach (var p in SupportPurchase.Packs) p.SetPrice("$4.00");
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 1234f);
        var panel = ShopPanel.Open();
        Check("the panel opens and owns Back", panel != null && ShopPanel.IsOpen && BackNavigator.Top == (object)panel);
        Check("it has the ten rows and scrolls vertically only",
              panel.RowCount == 10 && panel.Scroll.vertical && !panel.Scroll.horizontal);
        Check("the star dust balance is shown", panel.DustText.text == "1,234");
        Check("booster rows read their dust price and are live with 1,234 dust",
              panel.RowButtonText(5).text == "600 DUST" && panel.RowButton(5).interactable &&
              panel.RowButtonText(6).text == "200 DUST" && panel.RowButton(6).interactable &&
              panel.RowButtonText(7).text == "1,000 DUST" && panel.RowButton(7).interactable);
        Check("Remove Ads price on its button", panel.RowButtonText(0).text == "$3.99" && panel.RowButton(0).interactable);
        Check("Paragon carries the leaderboard warning before payment", panel.RowNotice(1).gameObject.activeSelf &&
              panel.RowNotice(1).text == PremiumShips.LeaderboardNotice);

        // booster rows: held line and the switch
        Check("booster rows show NOT OWNED and no switch while nothing is held",
              panel.RowStatus(5).text == ShopCatalog.NotOwnedLabel && !panel.RowSwitch(5).gameObject.activeSelf &&
              panel.RowStatus(6).text == ShopCatalog.NotOwnedLabel && panel.RowStatus(7).text == ShopCatalog.NotOwnedLabel);
        panel.RowButton(5).onClick.Invoke();   // buy Dust x2 (600 of 1,234)
        Check("after buying Dust x2 the row reads ON 5:00 left and shows its switch, knob to the right",
              panel.RowStatus(5).text == "ON  5:00 left" && panel.RowSwitch(5).gameObject.activeSelf && panel.RowSwitch(5).interactable &&
              panel.RowSwitchLabel(5).text == ShopCatalog.OnLabel && panel.RowSwitchKnob(5).anchoredPosition.x > 30f);
        float knobOn = panel.RowSwitchKnob(5).anchoredPosition.x;
        panel.RowSwitch(5).onClick.Invoke();
        Check("tapping the switch turns it off: OFF, time frozen, knob slides left",
              !Boosters.IsOn(Boosters.Kind.DustMultiplier) && panel.RowStatus(5).text == "OFF  5:00 left" &&
              panel.RowSwitchLabel(5).text == ShopCatalog.OffLabel && panel.RowSwitchKnob(5).anchoredPosition.x < knobOn);
        panel.RowSwitch(5).onClick.Invoke();
        Check("tapping again turns it back on", Boosters.IsOn(Boosters.Kind.DustMultiplier) && panel.RowStatus(5).text.StartsWith("ON"));
        PlayerPrefs.SetInt(Boosters.DustUsedMsKey, 28000);   // 28 s of play spent elsewhere (a run)
        Boosters.Reload();
        Check("the open Shop follows a change without a tap (Changed / poll)", panel.RowStatus(5).text == "ON  4:32 left");
        var sw = (RectTransform)panel.RowSwitch(5).transform;
        var buy = (RectTransform)panel.RowButton(5).transform;
        Check("the switch's tap strip is finger-sized and sits under the buy plate inside the row",
              sw.sizeDelta.y >= ShopPanel.ButtonHeight - .5f && sw.anchoredPosition.y <= buy.anchoredPosition.y - buy.sizeDelta.y + .5f &&
              -sw.anchoredPosition.y + sw.sizeDelta.y <= ShopPanel.BoosterRowHeight + .5f);
        Check("the held line is readable (min size >= 15) and does not overlap the description",
              panel.RowStatus(5).resizeTextMinSize >= 15 && panel.RowSwitchLabel(5).resizeTextMinSize >= 15 &&
              panel.RowStatus(5).rectTransform.anchoredPosition.y - panel.RowStatus(5).rectTransform.sizeDelta.y >= panel.RowSubtitle(5).rectTransform.anchoredPosition.y - .5f);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 1234f);
        foreach (string k in BoosterKeys) PlayerPrefs.DeleteKey(k);
        Boosters.Reload();
        panel.Refresh();

        // a pack paid while open: the balance follows
        SupportPurchase.Handle(new PlayPurchase { productId = SupportPurchase.Small.productId, token = "shop-t1", state = PlayPurchaseState.Purchased },
                               (t, done) => done(true));
        Check("a pack grant starts the burst; the shown total begins at the old balance", panel.FxActive && panel.DustText.text == "1,234" && Mathf.FloorToInt(StarDustLedger.Saved) == 2084);
        RunFx(panel);
        Check("after a pack is granted the balance updates (count-up ends at the true balance)", !panel.FxActive && panel.DustText.text == "2,084");

        // a pending / ignored purchase and a cancelled one play nothing
        SupportPurchase.Handle(new PlayPurchase { productId = SupportPurchase.Small.productId, token = "shop-pend", state = PlayPurchaseState.Pending }, (t, done) => done(true));
        SupportPurchase.Handle(new PlayPurchase { productId = SupportPurchase.Small.productId, token = "", state = PlayPurchaseState.Purchased }, (t, done) => done(true));
        Check("pending / cancelled purchase: no animation, no dust", !panel.FxActive && panel.DustText.text == "2,084" && Mathf.FloorToInt(StarDustLedger.Saved) == 2084);
        SupportPurchase.Handle(new PlayPurchase { productId = SupportPurchase.Small.productId, token = "shop-t1", state = PlayPurchaseState.Purchased }, (t, done) => done(true));
        Check("the same purchase delivered again: no second animation", !panel.FxActive && Mathf.FloorToInt(StarDustLedger.Saved) == 2084);

        // the watch-ad reward: +50 one at a time, ends at the saved balance; an early leave and a second reward are safe
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 2184f);   // the ad's grant (already saved)
        ShopCatalog.RaiseAdRewarded(100f);
        Check("ad reward +100: burst on, total starts at 2,084, 50 particles stepping 2", panel.FxActive && panel.DustText.text == "2,084" && panel.Fx.Count == 50);
        var seen = new List<int>();
        for (int n = 0; n < 400 && panel.FxActive; n++) { panel.StepFx(1f / 60f); seen.Add(panel.DustShown); }
        bool steps = true; for (int i = 1; i < seen.Count; i++) if (seen[i] - seen[i - 1] > 4 || seen[i] < seen[i - 1]) steps = false;
        Check("ad reward: small steps and ends at 2,184", steps && !panel.FxActive && panel.DustText.text == "2,184" && seen.Count >= 72 && seen.Count <= 108);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 2284f);
        ShopCatalog.RaiseAdRewarded(100f);
        for (int n = 0; n < 40; n++) panel.StepFx(1f / 60f);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 2384f);
        ShopCatalog.RaiseAdRewarded(100f);   // another reward lands mid-flight
        RunFx(panel);
        Check("a second reward mid-animation does not double count: ends at 2,384", panel.DustText.text == "2,384");
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 2484f);
        ShopCatalog.RaiseAdRewarded(100f);
        for (int n = 0; n < 20; n++) panel.StepFx(1f / 60f);
        bool threw = false;
        try { panel.Close(); panel.StepFx(.5f); } catch (System.Exception) { threw = true; }
        Check("leaving the Shop mid-animation does not throw and snaps to the true balance", !threw && !panel.FxActive && panel.DustText.text == "2,484");
        ShopPanel.Open();

        // the quick ad (+40): 40 particles, +1 each, from its own row, ends at the true balance
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 2524f);
        ShopCatalog.RaiseAdRewarded(40f);
        Check("quick ad +40: burst on, total starts at 2,484, 40 particles", panel.FxActive && panel.DustText.text == "2,484" && panel.Fx.Count == 40);
        var seenQ = new List<int>();
        for (int n = 0; n < 400 && panel.FxActive; n++) { panel.StepFx(1f / 60f); seenQ.Add(panel.DustShown); }
        bool stepsQ = true; for (int i = 1; i < seenQ.Count; i++) if (seenQ[i] - seenQ[i - 1] > 2 || seenQ[i] < seenQ[i - 1]) stepsQ = false;
        Check("quick ad: +1 at a time and ends at 2,524 in about 1.5 s", stepsQ && !panel.FxActive && panel.DustText.text == "2,524" && seenQ.Count >= 70 && seenQ.Count <= 108);
        Check("the two ad rows load independently (not loaded -> LOADING each, tap does nothing)",
              ShopCatalog.WatchAdRow().kind == ShopRowKind.WatchAd && ShopCatalog.QuickAdRow().kind == ShopRowKind.QuickAd &&
              ShopCatalog.WatchAdRow().state != ShopRowState.Buyable && ShopCatalog.QuickAdRow().state != ShopRowState.Buyable);

        // the timeline itself: 50, and a 6,000 pack, take about the same time and land exactly
        foreach (int amount in new[] { 40, 50, 100, 850, 2500, 6000 })
        {
            var m = new ShopDustFx(null);
            m.Begin(Vector2.zero, new Vector2(100f, 100f), 1000, 1000 + amount, null);
            float t = 0f; int last = 1000; bool mono = true;
            while (m.Active && t < 5f) { m.Step(1f / 60f); t += 1f / 60f; if (m.Displayed < last) mono = false; last = m.Displayed; }
            Check("fx timeline " + amount + ": ends at the true balance in 1.2-1.8 s", mono && !m.Active && m.Displayed == 1000 + amount && t >= 1.2f && t <= 1.8f);
        }
        var mid = new ShopDustFx(null);
        mid.Begin(Vector2.zero, Vector2.one, 10, 60, null);
        mid.Step(.5f); mid.Finish(); mid.Finish();
        Check("fx Finish mid-way snaps to the end and is repeatable", !mid.Active && mid.Displayed == 60 && mid.Pulse == 1f);

        // a store sheet open: the packs grey out, the dust-bought booster rows stay live
        RemoveAdsPurchase.LaunchFlow = () => { };
        RemoveAdsPurchase.SetPrice("$3.99");
        RemoveAdsPurchase.RequestBuy();
        panel.Refresh();
        Check("a store sheet open greys the packs but not the booster rows",
              ShopCatalog.AnyBuying && !panel.RowButton(2).interactable && panel.RowButton(5).interactable && panel.RowButton(6).interactable);
        RemoveAdsPurchase.EndBuy();

        // owned Remove Ads
        PlayerPrefs.SetInt(AdFree.Key, 1);
        panel.Refresh();
        Check("owned: the button reads OWNED and is dead", panel.RowButtonText(0).text == "OWNED" && !panel.RowButton(0).interactable);

        // unavailable
        RemoveAdsPurchase.SetPrice(null);
        ParagonPurchase.SetPrice(null);
        foreach (var p in SupportPurchase.Packs) p.SetPrice(null);
        panel.Refresh();
        Check("no store: the booster rows stay live (dust, not Play billing)", panel.RowButton(5).interactable && panel.RowButton(6).interactable);
        Check("no store: no row is interactive and the note shows (not an empty screen)",
              !panel.RowButton(1).interactable && !panel.RowButton(2).interactable && panel.StoreNote.gameObject.activeSelf &&
              panel.RowButtonText(2).text == "UNAVAILABLE");

        // sized for a tall phone and a tablet: content fits the width and the rows do not overlap
        foreach (var size in new[] { new Vector2Int(1080, 2520), new Vector2Int(1080, 1920), new Vector2Int(1536, 2048) })
        {
            using (ScreenInfo.Override(size.x, size.y, new Rect(0f, 0f, size.x, size.y), null, 420f))
            {
                panel.Layout();
                var vp = panel.Content.parent as RectTransform;
                Check(size.x + "x" + size.y + ": content is no wider than its viewport and has height",
                      panel.Content.rect.width <= vp.rect.width + .5f && panel.Content.rect.height > 400f);
                bool ordered = true;
                float last = float.MaxValue;
                for (int i = 0; i < panel.RowCount; i++)
                {
                    var rt = (RectTransform)panel.RowButton(i).transform.parent;
                    float top = rt.anchoredPosition.y;
                    ordered &= top < last;
                    last = top - rt.sizeDelta.y + .01f;
                }
                Check(size.x + "x" + size.y + ": rows stack top to bottom without overlapping", ordered);
            }
        }

        // Back closes the Shop (one level), not the app
        BackNavigator.Back();
        Check("Back closes the Shop and hands Back on", !ShopPanel.IsOpen && BackNavigator.Top != (object)panel);
        Object.DestroyImmediate(panel.gameObject);
    }

    static void Dock()
    {
        Reset();
        PlayerPrefs.SetString("boughtship1", "True");
        EditorSceneManager.OpenScene("Assets/Scenes/shopS6.unity", OpenSceneMode.Single);
        ShopSceneExtender.Build();
        var dock = SpaceDock.Instance;
        Check("the dock was built", dock != null);
        if (dock == null) return;
        Check("the dock has no star dust pack tiles", dock.transform.Find("~DockRack").Find("~DockSupport_support850") == null &&
              dock.transform.Find("~DockRack").Find("~DockSupport_support2500") == null);
        Check("the 16 ships fill the rack: 3 columns, 6 rows", dock.layout.count == 16 && dock.layout.columns == 3 && dock.layout.rows == 6);
        var last = dock.layout.BayCenter(15);
        Check("the lone last berth (Paragon) is centred, not left with a gap", Mathf.Abs(last.x) < .001f);
        Check("a full row stays left-aligned to columns", dock.layout.BayCenter(0).x < -.1f && dock.layout.BayCenter(2).x > .1f);
        Object.DestroyImmediate(dock.gameObject);
    }

    static void Home()
    {
        Reset();
        EditorSceneManager.OpenScene("Assets/Scenes/startS4.unity", OpenSceneMode.Single);
        var menu = Object.FindFirstObjectByType<startMenu>();
        menu.LayoutHome();
        var entry = SceneUtil.FindAny(ShopHomeEntry.ButtonName);
        Check("the home menu has a SHOP row under the Space dock", entry != null && entry.activeSelf &&
              entry.transform.GetSiblingIndex() == SceneUtil.FindAny("shopButton").transform.GetSiblingIndex() + 1);
        if (entry == null) return;
        Check("it reads Shop", entry.GetComponentInChildren<Text>(true).text == "Shop");
        menu.LayoutHome();
        Check("laying out twice keeps one row", Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0 &&
              GameObject.Find("UIPanel").transform.Find(ShopHomeEntry.ButtonName) != null);
        entry.GetComponent<Button>().onClick.Invoke();
        Check("pressing it opens the Shop over home", ShopPanel.IsOpen);
        BackNavigator.Back();
        Check("Back leaves the Shop", !ShopPanel.IsOpen);
        if (ShopPanel.Current != null) Object.DestroyImmediate(ShopPanel.Current.gameObject);
    }
}
