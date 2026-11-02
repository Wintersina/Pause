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
    }

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
            Gate();
            Taps();
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

    static void Rows()
    {
        var sections = ShopCatalog.Sections();
        Check("three sections: ads, ship, star dust (one list the panel lays out from)",
              sections.Count == 3 && sections[0].label == "ADS" && sections[1].label == "SHIP" && sections[2].label == "STAR DUST");
        var rows = ShopCatalog.Rows();
        Check("rows: Remove Ads, Paragon, then 850 / 2,500 / 6,000", rows.Count == 5 &&
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
        OwnShips(3);
        var row = ShopCatalog.ParagonRow();
        Check("Paragon gate: 3 ships owned -> OWN 5 SHIPS, refused even with a price",
              row.state == ShopRowState.Locked && row.button == "OWN 5 SHIPS" && !row.Tappable && !ParagonPurchase.RequestBuy() && !ShopCatalog.Tap(row));
        OwnShips(5);
        Check("Paragon gate: 5 ships owned -> buyable", ShopCatalog.ParagonRow().state == ShopRowState.Buyable);
        Check("the gate is one switch (ShipUnlocks.GatesParagon), on today", ShipUnlocks.GatesParagon);
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
        Check("it has the five rows and scrolls vertically only",
              panel.RowCount == 5 && panel.Scroll.vertical && !panel.Scroll.horizontal);
        Check("the star dust balance is shown", panel.DustText.text == "1,234");
        Check("Remove Ads price on its button", panel.RowButtonText(0).text == "$3.99" && panel.RowButton(0).interactable);
        Check("Paragon carries the leaderboard warning before payment", panel.RowNotice(1).gameObject.activeSelf &&
              panel.RowNotice(1).text == PremiumShips.LeaderboardNotice);

        // a pack paid while open: the balance follows
        SupportPurchase.Handle(new PlayPurchase { productId = SupportPurchase.Small.productId, token = "shop-t1", state = PlayPurchaseState.Purchased },
                               (t, done) => done(true));
        Check("after a pack is granted the balance updates", panel.DustText.text == "2,084");

        // owned Remove Ads
        PlayerPrefs.SetInt(AdFree.Key, 1);
        panel.Refresh();
        Check("owned: the button reads OWNED and is dead", panel.RowButtonText(0).text == "OWNED" && !panel.RowButton(0).interactable);

        // unavailable
        RemoveAdsPurchase.SetPrice(null);
        ParagonPurchase.SetPrice(null);
        foreach (var p in SupportPurchase.Packs) p.SetPrice(null);
        panel.Refresh();
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
