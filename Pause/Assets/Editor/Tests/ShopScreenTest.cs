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

    static readonly string[] BoosterKeys = { Boosters.DustBoughtKey, Boosters.DustUsedMsKey, Boosters.ClearBoughtKey, Boosters.ClearUsedKey, Boosters.HomingBoughtKey, Boosters.HomingUsedMsKey };

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
            if (r.kind == ShopRowKind.Paragon) continue;
            count++;
            var sprite = ShopIconArt.For(r);
            if (sprite == null) { allFound = false; continue; }
            if (!seen.Add(sprite)) distinct = false;
            pixel &= sprite.texture.filterMode == FilterMode.Point && sprite.texture.mipmapCount == 1;
        }
        Check("Remove Ads, three packs and three boosters: " + count + " rows", count == 7);
        Check("every Remove Ads, pack and booster row resolves to a sprite", allFound);
        Check("each of them has its own sprite", distinct && seen.Count == 7);
        Check("shop icons are Point filtered with no mipmaps (crisp pixel art)", pixel);
        var small = ShopIconArt.NameFor(new ShopRow { kind = ShopRowKind.Pack, pack = SupportPurchase.Small });
        var huge = ShopIconArt.NameFor(new ShopRow { kind = ShopRowKind.Pack, pack = SupportPurchase.Huge });
        Check("Remove Ads maps to its icon", ShopIconArt.NameFor(ShopCatalog.RemoveAdsRow()) == "remove_ads");
        Check("packs map by size", small == "pack_850" && huge == "pack_6000");
    }

    static void Rows()
    {
        var sections = ShopCatalog.Sections();
        Check("four sections: ads, ship, star dust, boosters (one list the panel lays out from)",
              sections.Count == 4 && sections[0].label == "ADS" && sections[1].label == "SHIP" && sections[2].label == "STAR DUST" &&
              sections[3].label == "BOOSTERS" && sections[3].rows.Count == 3);
        var rows = ShopCatalog.Rows();
        Check("rows: Remove Ads, Paragon, 850 / 2,500 / 6,000, then Dust x2, Hazard Clear and Homing Atoms", rows.Count == 8 &&
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
        Check("Dust x2 now reads its time left on the row", ShopCatalog.BoosterRow(Boosters.Kind.DustMultiplier).title.Contains("left"));
        int bought = 0;
        while (ShopCatalog.Tap(ShopCatalog.BoosterRow(Boosters.Kind.HazardClear)) && bought < 20) bought++;
        var full = ShopCatalog.BoosterRow(Boosters.Kind.HazardClear);
        Check("hazard clear sells up to 9 charges, then reads MAX and is dead",
              bought == Boosters.MaxClearCharges && Boosters.ClearCharges == 9 && full.state == ShopRowState.Short && full.button == "MAX" &&
              !full.Tappable && full.title.Contains("x9"));
        Check("a refused tap spent nothing", Mathf.Approximately(StarDustLedger.Saved, 5000f - 9f * 200f));

        // homing atoms: 1,000 a purchase, 10 minutes each, the same one-hour cap
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 999f);
        var homingRow = ShopCatalog.BoosterRow(Boosters.Kind.HomingAtoms);
        Check("homing atoms: 999 dust is short of the 1,000 price",
              homingRow.state == ShopRowState.Short && homingRow.button == "1,000 DUST" && !ShopCatalog.Tap(homingRow) &&
              homingRow.title == Boosters.Name(Boosters.Kind.HomingAtoms));
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 1500f);
        Check("homing atoms: a tap pays 1,000 and starts 10 minutes",
              ShopCatalog.Tap(ShopCatalog.BoosterRow(Boosters.Kind.HomingAtoms)) && Mathf.Approximately(StarDustLedger.Saved, 500f) &&
              Boosters.HomingActive && Mathf.Approximately(Boosters.HomingRemainingSeconds, Boosters.HomingSeconds) &&
              ShopCatalog.BoosterRow(Boosters.Kind.HomingAtoms).title.Contains("10:00 left"));
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100000f);
        int homingBought = 1;
        while (ShopCatalog.Tap(ShopCatalog.BoosterRow(Boosters.Kind.HomingAtoms)) && homingBought < 20) homingBought++;
        Check("homing atoms stop at the one-hour stored cap (6 purchases) and then read MAX",
              homingBought == 6 && ShopCatalog.BoosterRow(Boosters.Kind.HomingAtoms).button == "MAX");

        // dust x2 stops at the stored-time cap
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 100000f);
        int dustBought = 0;
        while (ShopCatalog.Tap(ShopCatalog.BoosterRow(Boosters.Kind.DustMultiplier)) && dustBought < 20) dustBought++;
        Check("Dust x2 stops at the one-hour stored cap and then reads MAX",
              Boosters.RemainingSeconds <= Boosters.MaxStoredSeconds + .01f && ShopCatalog.BoosterRow(Boosters.Kind.DustMultiplier).button == "MAX");
        Reset();
    }

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
        Check("it has the eight rows and scrolls vertically only",
              panel.RowCount == 8 && panel.Scroll.vertical && !panel.Scroll.horizontal);
        Check("the star dust balance is shown", panel.DustText.text == "1,234");
        Check("booster rows read their dust price and are live with 1,234 dust",
              panel.RowButtonText(5).text == "600 DUST" && panel.RowButton(5).interactable &&
              panel.RowButtonText(6).text == "200 DUST" && panel.RowButton(6).interactable &&
              panel.RowButtonText(7).text == "1,000 DUST" && panel.RowButton(7).interactable);
        Check("Remove Ads price on its button", panel.RowButtonText(0).text == "$3.99" && panel.RowButton(0).interactable);
        Check("Paragon carries the leaderboard warning before payment", panel.RowNotice(1).gameObject.activeSelf &&
              panel.RowNotice(1).text == PremiumShips.LeaderboardNotice);

        // a pack paid while open: the balance follows
        SupportPurchase.Handle(new PlayPurchase { productId = SupportPurchase.Small.productId, token = "shop-t1", state = PlayPurchaseState.Purchased },
                               (t, done) => done(true));
        Check("after a pack is granted the balance updates", panel.DustText.text == "2,084");

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
