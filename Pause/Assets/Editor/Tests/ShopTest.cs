using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The ship-select space dock (shopS6): every roster ship parked in its own
// berth, powered down until selected, a small popup floating above the
// selected ship, purchases deducting once and saving at once, and launch
// continuing to the same scene the PLAY button always led to.
public static class ShopTest
{
    static int fails;

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ST] PASS  " : "[ST] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        EditorSceneLoader.Open("shopS6", OpenSceneMode.Single);

        // The old dialog and its per-ship buttons are gone from the scene.
        Check("the old yes/no popup canvas was removed", SceneUtil.FindAny("PopUpCanvas") == null);
        Check("the old Yes button was removed", SceneUtil.FindAny("Yes Button") == null);
        Check("the old per-ship card buttons were removed", SceneUtil.FindAny("Button1") == null);
        Check("BACK is still in the scene", SceneUtil.FindAny("BackButton") != null);
        Check("LIFT-OFF is still in the scene", SceneUtil.FindAny("PlayButton") != null);

        // Seed a known state: starter owned, everything else locked.
        for (int i = 2; i < shopingShips.shipTotal; i++) PlayerPrefs.DeleteKey("boughtship" + i);
        PlayerPrefs.SetString("boughtship1", "True");
        PlayerPrefs.SetInt("spawnShip", 1);
        PlayerPrefs.SetFloat("PlayerCurrecny", 0f);

        ShopSceneExtender.Build();
        var dock = SpaceDock.Instance;
        Check("the space dock was built", dock != null);
        if (dock == null) { Debug.Log("[ST] failures: " + fails); return fails; }

        // Lay the dock out for a 9:16 phone, as CameraFit would.
        var cam = Camera.main;
        cam.aspect = 1080f / 1920f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, 2.85f, 1080, 1920);
        dock.Relayout();

        int total = shopingShips.shipTotal;
        Check("roster includes retro and original ships", total == shopingShips.Roster.Length && total > 8);
        Check("portrait phones get a three-column rack", dock.layout.columns == 3);

        float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
        Vector3 c = cam.transform.position;
        for (int i = 1; i < total; i++)
        {
            var bay = dock.bays[i];
            var ship = SceneUtil.FindAny("ship" + i);
            string name = shopingShips.NameFor(i);
            Check("ship" + i + " (" + name + ") is parked in its own berth",
                  bay != null && ship != null && ship.transform.parent == bay.transform);
            if (bay == null || ship == null) continue;
            var sr = ship.GetComponent<SpriteRenderer>();
            Check("ship" + i + " has art", sr != null && sr.sprite != null);
            Check("ship" + i + " is registered with the shop", shopingShips.ships[i] == ship);
            float h = sr.bounds.size.y;
            Check("ship" + i + " uses the compact dock size", h > 0.16f && h < 0.80f);
            Check("Boost" + i + " exists for lift-off", SceneUtil.FindAny("Boost" + i) != null);
            Check("ship" + i + " carries no live-gameplay components",
                  ship.GetComponent<collisionDetection>() == null && ship.GetComponent<movePlayer>() == null);

            Vector3 p = bay.transform.position;
            Check("berth " + i + " is on screen on a 9:16 phone",
                  Mathf.Abs(p.x - c.x) + DockLayout.BaySize.x * .5f * dock.layout.scale <= halfW + .001f &&
                  Mathf.Abs(p.y - c.y) < halfH);

            // Labels: the name on every berth; owned = a quiet icon, locked =
            // a compact price tag.
            Check("berth " + i + " shows the ship's name", bay.NameLabel == name.ToUpperInvariant());
            bool owned = i == 1;
            Check("berth " + i + (owned ? " marks ownership with an icon" : " shows no owned marker"),
                  bay.ShowsStatusIcon == owned);
            Check("berth " + i + (owned ? " has no price tag" : " shows its price"),
                  bay.ShowsPrice == !owned &&
                  (owned || bay.PriceLabel == Mathf.RoundToInt(shopingShips.CostFor(i)).ToString("N0")));
        }

        // Berths are packed edge to edge, without overlapping.
        var b1 = dock.bays[1].transform.position;
        var b2 = dock.bays[2].transform.position;
        var below = dock.bays[1 + dock.layout.columns].transform.position;
        Check("neighbouring berths are packed close (" + (b2.x - b1.x).ToString("F2") + "u apart)",
              b2.x - b1.x < 1.9f && b2.x - b1.x >= DockLayout.BaySize.x * dock.layout.scale - .001f);
        Check("berth rows are packed close (" + (b1.y - below.y).ToString("F2") + "u apart)",
              b1.y - below.y < 1.6f && b1.y - below.y >= DockLayout.BaySize.y * dock.layout.scale - .001f);

        // Powered down until selected: no flame, dim hull, standby lights.
        foreach (var thruster in Object.FindObjectsByType<ShipThruster>(FindObjectsSortMode.None))
            thruster.SendMessage("Start", SendMessageOptions.DontRequireReceiver);
        RunThrusters();
        for (int i = 1; i < total; i++)
        {
            var bay = dock.bays[i];
            Check("ship" + i + " starts powered down", !bay.Powered && bay.Power == 0f);
            if (bay.thruster != null) Check("ship" + i + " has no active exhaust while parked", !bay.thruster.IsBurning);
            Check("ship" + i + " hull is dimmed while parked", bay.hull.color.r < .7f);
        }

        dock.Select(2);
        dock.bays[2].SnapPower();
        RunThrusters();
        Check("selecting a ship powers it up", dock.Selected == 2 && dock.bays[2].Powered);
        Check("the selected ship's engine ignites", dock.bays[2].thruster != null && dock.bays[2].thruster.IsBurning);
        Check("the selected ship's hull is at full colour", dock.bays[2].hull.color.r > .99f);
        Check("the selected berth's lights come up", dock.bays[2].LightLevel > dock.bays[3].LightLevel + .3f);
        for (int i = 1; i < total; i++)
            if (i != 2 && dock.bays[i].thruster != null)
                Check("unselected ship" + i + " stays powered down", !dock.bays[i].Powered && !dock.bays[i].thruster.IsBurning);
        dock.Select(3);
        dock.bays[2].SnapPower();
        RunThrusters();
        Check("selecting another ship powers the first back down",
              !dock.bays[2].Powered && !dock.bays[2].thruster.IsBurning && dock.bays[3].Powered);

        PopupAnchoring(dock, cam);
        Purchases(dock);
        Dismissal(dock);
        Unlocks(dock);
        LaunchFlow(dock);

        Debug.Log("[ST] failures: " + fails);
        return fails;
    }

    static void RunThrusters()
    {
        foreach (var thruster in Object.FindObjectsByType<ShipThruster>(FindObjectsSortMode.None))
            thruster.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
    }

    static void PopupAnchoring(SpaceDock dock, Camera cam)
    {
        float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
        Vector3 c = cam.transform.position;
        var screen = new Rect(c.x - halfW, c.y - halfH, halfW * 2f, halfH * 2f);
        int last = shopingShips.shipTotal - 1;
        // Corners and middle of the rack: top row must flip or clamp, side
        // columns must clamp horizontally.
        int[] probe = { 1, 2, dock.layout.columns, 1 + dock.layout.columns * 2, last - dock.layout.columns + 1, last };
        foreach (int i in probe)
        {
            dock.Select(i);
            var popup = dock.popup;
            popup.SendMessage("LateUpdate");
            Vector3 ship = dock.bays[i].ship.position;
            Rect r = popup.WorldRect;
            Check("popup for ship" + i + " is shown for that ship", popup.Visible && popup.ShipIndex == i);
            Check("popup for ship" + i + " stays inside the dock's safe view",
                  r.xMin >= popup.safeView.xMin - .001f && r.xMax <= popup.safeView.xMax + .001f &&
                  r.yMin >= popup.safeView.yMin - .001f && r.yMax <= popup.safeView.yMax + .001f);
            Check("popup for ship" + i + " is on screen",
                  r.xMin >= screen.xMin && r.xMax <= screen.xMax && r.yMin >= screen.yMin && r.yMax <= screen.yMax);
            Check("popup for ship" + i + " is anchored over the ship horizontally",
                  ship.x >= r.xMin && ship.x <= r.xMax);
            Check("popup for ship" + i + " floats " + (popup.Flipped ? "just below" : "just above") + " the ship",
                  popup.Flipped ? r.yMax < ship.y && ship.y - r.yMax < .7f
                                : r.yMin > ship.y && r.yMin - ship.y < .6f);
            Check("popup is small (not a full-screen dialog)",
                  r.width < screen.width * .6f && r.height < screen.height * .1f);
        }

        // The anchor follows the ship: move the rack and the popup moves too.
        dock.Select(5);
        dock.popup.SendMessage("LateUpdate");
        float before = dock.popup.transform.position.y;
        dock.rack.position += new Vector3(0f, -.3f, 0f);
        dock.popup.SendMessage("LateUpdate");
        Check("popup stays anchored to the ship as it moves",
              Mathf.Abs(dock.popup.transform.position.y - before + .3f) < .001f);
        dock.rack.position += new Vector3(0f, .3f, 0f);

        bool flipped;
        var view = new Rect(-2.8f, -4f, 5.6f, 8f);
        var top = DockPopup.Place(new Vector2(-2.6f, 3.9f), .3f, .5f, new Vector2(DockPopup.Width, DockPopup.Height), view, out flipped);
        Check("a popup with no room above flips below the ship", flipped && top.y < 3.9f);
        Check("a popup near the left edge is pushed back on screen", top.x - DockPopup.Width * .5f >= view.xMin - .001f);
    }

    static void Purchases(SpaceDock dock)
    {
        int ship = 4;
        float price = shopingShips.CostFor(ship);
        PlayerPrefs.DeleteKey("boughtship" + ship);
        PlayerPrefs.SetFloat("PlayerCurrecny", price + 500f);
        dock.Select(ship);
        Check("a locked ship's popup offers BUY", dock.popup.CurrentMode == DockPopup.Mode.Buy);
        int saves = PrefsSaver.SaveCount;
        dock.popup.Press();
        Check("buying deducts the price exactly once",
              Mathf.Approximately(PlayerPrefs.GetFloat("PlayerCurrecny"), 500f));
        Check("buying marks the ship bought", PlayerPrefs.GetString("boughtship" + ship) == "True");
        Check("buying equips the ship", PlayerPrefs.GetInt("spawnShip") == ship);
        Check("buying saves immediately", PrefsSaver.SaveCount > saves);
        Check("after buying, the popup offers LAUNCH", dock.popup.CurrentMode == DockPopup.Mode.Launch);
        Check("after buying, the berth shows the owned marker", dock.bays[ship].ShowsStatusIcon && !dock.bays[ship].ShowsPrice);
        dock.Buy(ship);
        dock.Buy(ship);
        Check("buying an owned ship again never charges twice",
              Mathf.Approximately(PlayerPrefs.GetFloat("PlayerCurrecny"), 500f));

        int poor = 7;
        PlayerPrefs.DeleteKey("boughtship" + poor);
        PlayerPrefs.SetFloat("PlayerCurrecny", 100f);
        dock.Select(poor);
        dock.popup.Press();
        Check("an unaffordable ship is not bought", PlayerPrefs.GetString("boughtship" + poor) != "True");
        Check("an unaffordable purchase takes no star dust", Mathf.Approximately(PlayerPrefs.GetFloat("PlayerCurrecny"), 100f));
        Check("an unaffordable ship keeps offering BUY", dock.popup.CurrentMode == DockPopup.Mode.Buy);
        var message = dock.popup.transform.Find("Panel/Message").GetComponent<Text>();
        Check("the player is told they can't afford it",
              message.enabled && message.text.Contains("NEED") && message.text.Contains((shopingShips.CostFor(poor) - 100f).ToString("N0")));
    }

    static void Dismissal(SpaceDock dock)
    {
        dock.Select(6);
        dock.Tap(new Vector3(500f, 500f, 0f));
        Check("tapping elsewhere dismisses the popup", !dock.popup.Visible && dock.Selected == 0);
        Check("tapping elsewhere powers the ship back down", !dock.bays[6].Powered);
        dock.Tap(dock.bays[6].transform.position);
        Check("tapping a berth selects its ship", dock.Selected == 6 && dock.popup.ShipIndex == 6);
        new GameObject("~ShopProxy").AddComponent<shopingShips>().SelectShip(8);
        Check("shopingShips.SelectShip routes to the dock", dock.Selected == 8);
    }

    static void Unlocks(SpaceDock dock)
    {
        // The editor shares PlayerPrefs with the Mac build; developer mode may
        // already be on there. Start from off so turning it on snapshots.
        PlayerPrefs.DeleteKey(DeveloperUnlocks.EnabledKey);
        DeveloperUnlocks.SetEnabled(true);
        bool allOwned = true;
        for (int i = 1; i < shopingShips.shipTotal; i++) allOwned &= dock.bays[i].ShowsStatusIcon && !dock.bays[i].ShowsPrice;
        Check("DeveloperUnlocks: every berth shows as owned at once", allOwned);
        DeveloperUnlocks.SetEnabled(false);
        Check("DeveloperUnlocks off restores locked berths", dock.bays[9].ShowsPrice);
    }

    static void LaunchFlow(SpaceDock dock)
    {
        PlayerPrefs.SetString("HasDoneTut", "true");
        Check("launch continues to the game, like PLAY", SpaceDock.Destination == "gameS1");
        PlayerPrefs.SetString("HasDoneTut", "false");
        Check("launch goes to the tutorial first, like PLAY", SpaceDock.Destination == "tutorialS5");

        int saves = PrefsSaver.SaveCount;
        dock.Select(1);
        dock.popup.Press();
        Check("LAUNCH starts the undock", dock.Launching);
        Check("LAUNCH equips the ship and saves", PlayerPrefs.GetInt("spawnShip") == 1 && PrefsSaver.SaveCount > saves);
        Check("the popup is gone during the launch", !dock.popup.Visible);
        Check("the launching ship is powered", dock.bays[1].Powered);
    }
}
