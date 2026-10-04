using System.Collections.Generic;
using System.IO;
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

        // Berths are packed edge to edge, without overlapping (slots 0, 1
        // and the one below 0 in the cheapest-first order).
        var b1 = dock.bays[SpaceDock.ShipAt(0)].transform.position;
        var b2 = dock.bays[SpaceDock.ShipAt(1)].transform.position;
        var below = dock.bays[SpaceDock.ShipAt(dock.layout.columns)].transform.position;
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

        BayOrder(dock);
        Silhouettes(dock);

        // Selection powers an owned ship up.
        PlayerPrefs.SetString("boughtship2", "True");
        dock.RefreshStatuses();
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
        // Ship 3 is not bought: the tap still lights its berth, but the hull
        // stays an ink shadow with a cold engine and no idle flipbook.
        dock.bays[3].SnapPower();
        dock.bays[3].SendMessage("Update");
        RunThrusters();
        Check("a selected unbought ship's berth lights come up", dock.bays[3].LightLevel > dock.bays[4].LightLevel + .3f);
        Check("a selected unbought ship stays a silhouette", dock.bays[3].Silhouetted);
        Check("a selected unbought ship has no exhaust", dock.bays[3].thruster != null && !dock.bays[3].thruster.IsBurning);
        Check("a selected unbought ship holds still on its rest drawing",
              dock.bays[3].hull.sprite == ShipHullArt.Get(3, dock.bays[3].Skin, 0, 0) &&
              Mathf.Abs(dock.bays[3].ship.localPosition.y - DockBay.ShipRest.y) < 1e-4f);
        Check("an unbought ship can't launch: LAUNCH falls through to BUY",
              LaunchUnbought(dock, 3));
        PlayerPrefs.DeleteKey("boughtship2");
        dock.RefreshStatuses();

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
        // (berth slots, mapped to the ShipIds parked there)
        int[] probe = { 0, 1, dock.layout.columns - 1, dock.layout.columns * 2, last - dock.layout.columns, last - 1 };
        foreach (int slot in probe)
        {
            int i = SpaceDock.ShipAt(slot);
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
        Check("a locked ship is a silhouette before buying", dock.bays[ship].Silhouetted);
        int saves = PrefsSaver.SaveCount;
        dock.popup.Press();
        // The reveal: ink fills with white, the art swaps in under the
        // white, the white fades into the colours.
        var bay = dock.bays[ship];
        Check("buying starts the reveal", bay.Revealing && bay.SilhouetteAmount > 0f);
        bay.SendMessage("StepReveal", .15f);
        Check("reveal: the ink fills with white first", bay.Silhouetted && bay.FlashAmount > .1f && bay.FlashAmount < 1f);
        bay.SendMessage("StepReveal", DockBay.RevealPeak);
        Check("reveal: at full white the art swaps in", !bay.Silhouetted && bay.FlashAmount > .99f);
        bay.SendMessage("StepReveal", .7f);
        Check("reveal: the white fades into the colours", !bay.Silhouetted && bay.FlashAmount > 0f && bay.FlashAmount < 1f);
        bay.SnapPower();
        RunThrusters();
        Check("after the reveal the bought ship shows in full colour, powered up and burning",
              !bay.Revealing && !bay.Silhouetted && bay.FlashAmount == 0f && bay.Powered &&
              bay.thruster != null && bay.thruster.IsBurning);
        var shown = RenderHull(bay);
        Check("the revealed ship renders its full art (" + shown.colours + " colours)", !shown.rendered || shown.colours > 8);
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
        bool noShadows = true;
        for (int i = 1; i < shopingShips.shipTotal; i++) noShadows &= !dock.bays[i].Silhouetted && !dock.bays[i].Revealing;
        Check("DeveloperUnlocks: no berth is a silhouette (and none replays the reveal)", noShadows);
        if (noShadows)
        {
            var r = RenderHull(dock.bays[9]);
            Check("DeveloperUnlocks: an unbought hull renders its art (" + r.colours + " colours)", !r.rendered || r.colours > 8);
        }
        DeveloperUnlocks.SetEnabled(false);
        Check("DeveloperUnlocks off restores locked berths", dock.bays[9].ShowsPrice);
        Check("DeveloperUnlocks off puts the silhouettes back", dock.bays[9].Silhouetted);
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


    static bool LaunchUnbought(SpaceDock dock, int id)
    {
        float dust = PlayerPrefs.GetFloat("PlayerCurrecny");
        int equipped = PlayerPrefs.GetInt("spawnShip");
        PlayerPrefs.SetFloat("PlayerCurrecny", 0f);
        dock.Launch(id);
        bool ok = !dock.Launching && !ShipId.IsOwned(id) && PlayerPrefs.GetInt("spawnShip") == equipped &&
                  dock.bays[id].Silhouetted && dock.LiftOffIndex() != id;
        PlayerPrefs.SetFloat("PlayerCurrecny", dust);
        return ok;
    }

    // ---- Berth order: cheapest first, display only ----

    static void BayOrder(SpaceDock dock)
    {
        var order = SpaceDock.BayOrder;
        var seen = new HashSet<int>();
        foreach (int id in order) seen.Add(id);
        Check("the bay order holds every ship exactly once", order.Length == ShipId.Count && seen.Count == ShipId.Count &&
              seen.IsSupersetOf(ShipId.All));
        Check("the starter (Neon Comet, free) is the first berth", order[0] == ShipId.Starter && shopingShips.CostFor(order[0]) == 0f);
        bool ascending = true;
        for (int k = 1; k < order.Length; k++)
        {
            float a = shopingShips.CostFor(order[k - 1]), b = shopingShips.CostFor(order[k]);
            ascending &= a < b || (a == b && order[k - 1] < order[k]);
        }
        var names = new List<string>();
        foreach (int id in order) names.Add(shopingShips.NameFor(id) + " " + shopingShips.CostFor(id));
        Debug.Log("[ST] bay order: " + string.Join(", ", names));
        Check("berths run from cheapest to most expensive (ties by ShipId)", ascending);
        Check("the most expensive ship (Gold Warden) is the last berth", order[order.Length - 1] == 7);

        bool placed = true, identity = true;
        for (int slot = 0; slot < order.Length; slot++)
        {
            int id = order[slot];
            var bay = dock.bays[id];
            placed &= bay != null && ((Vector2)bay.transform.localPosition - dock.layout.BayCenter(slot)).sqrMagnitude < 1e-8f &&
                      SpaceDock.SlotOf(id) == slot;
            identity &= bay != null && bay.index == id && bay.ship.name == ShipId.ObjectName(id) &&
                        shopingShips.ships[id] == bay.ship.gameObject && bay.NameLabel == shopingShips.NameFor(id).ToUpperInvariant();
        }
        Check("every ship sits in its price slot", placed);
        Check("berths are still indexed by ShipId (bays[id], ship<id>, name)", identity);

        // Tapping a slot's position selects the ship parked there.
        bool taps = true;
        for (int slot = 0; slot < order.Length; slot++)
        {
            Vector3 world = dock.rack.TransformPoint(dock.layout.BayCenter(slot));
            dock.Tap(world);
            taps &= dock.Selected == order[slot] && dock.popup.Visible && dock.popup.ShipIndex == order[slot];
        }
        dock.Deselect();
        Check("tapping each berth selects the ship in that slot, popup included", taps);
        Check("ShipIds are not renumbered (save keys / roster)", ShipId.KeyOf(7) == "GoldWarden" && ShipId.OwnedKey(7) == "boughtship7" &&
              shopingShips.NameFor(1) == "Neon Comet");

        AspectRatios(dock);
    }

    static void AspectRatios(SpaceDock dock)
    {
        var cam = Camera.main;
        var sizes = new[]
        {
            new Vector2Int(1080, 1920), new Vector2Int(1080, 2340), new Vector2Int(1080, 2400), new Vector2Int(1440, 3200),
            new Vector2Int(720, 1280), new Vector2Int(1536, 2048), new Vector2Int(1100, 800), new Vector2Int(1920, 1080),
        };
        float aspect = cam.aspect, ortho = cam.orthographicSize;
        foreach (var size in sizes)
        {
            cam.aspect = size.x / (float)size.y;
            cam.orthographicSize = CameraFit.ComputeSize(5f, 2.85f, size.x, size.y);
            dock.Relayout();
            float halfW = cam.orthographicSize * cam.aspect;
            float cx = cam.transform.position.x;
            float bayW = DockLayout.BaySize.x * dock.layout.scale;
            bool fits = true, ordered = true;
            for (int slot = 0; slot < ShipId.Count; slot++)
            {
                var bay = dock.bays[SpaceDock.ShipAt(slot)];
                Vector3 p = bay.transform.position;
                fits &= Mathf.Abs(p.x - cx) + bayW * .5f <= halfW + .001f;
                if (slot > 0)
                {
                    Vector3 prev = dock.bays[SpaceDock.ShipAt(slot - 1)].transform.position;
                    bool sameRow = slot % dock.layout.columns != 0;
                    // reading order: left to right, then the next row down
                    ordered &= sameRow ? p.x > prev.x + bayW - .001f && Mathf.Abs(p.y - prev.y) < 1e-4f
                                       : p.y < prev.y - DockLayout.BaySize.y * dock.layout.scale + .001f;
                }
            }
            string tag = size.x + "x" + size.y;
            Check(tag + ": three columns, every berth fits the width", dock.layout.columns == 3 && fits);
            Check(tag + ": berths read cheapest first, left to right, top to bottom, without overlapping", ordered);
        }
        cam.aspect = aspect;
        cam.orthographicSize = ortho;
        dock.Relayout();
    }

    // ---- Silhouettes ----

    static void Silhouettes(SpaceDock dock)
    {
        bool match = true;
        for (int i = 1; i < shopingShips.shipTotal; i++)
            match &= dock.bays[i].Silhouetted == !ShipId.IsOwned(i);
        Check("unbought ships (and only they) park as silhouettes", match);
        Check("the dock hull shader is supported", DockArt.ShipMaterial != null && DockArt.ShipMaterial.shader.isSupported &&
              dock.bays[2].hull.sharedMaterial == DockArt.ShipMaterial);

        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            Check("rendered silhouettes need a graphics device (run without -nographics)", false);
            return;
        }
        foreach (int id in new[] { 2, 7, 11, 13, 15 })
        {
            var bay = dock.bays[id];
            var parked = RenderHull(bay, Path.Combine(PreviewDir, "silhouette-" + id + "-parked.png"));
            string why;
            Check(Label(id) + ": a parked unbought hull renders one flat ink colour inside its alpha mask (" + Why(parked) + ")",
                  Flat(parked, out why));

            dock.Select(id);
            bay.SnapPower();
            bay.SendMessage("Update");
            RunThrusters();
            if (bay.drift != null) { bay.drift.SendMessage("Start"); bay.drift.Step(1f / 24f); }
            var selected = RenderHull(bay, Path.Combine(PreviewDir, "silhouette-" + id + "-selected.png"));
            Check(Label(id) + ": selected, it is still one flat ink colour (" + Why(selected) + ")", Flat(selected, out why));
            Check(Label(id) + ": selected, no exhaust or glow spills past the hull (" + parked.covered + " vs " + selected.covered + " px)",
                  Mathf.Abs(selected.covered - parked.covered) <= parked.covered / 50 + 4 &&
                  (bay.thruster == null || !bay.thruster.IsBurning) &&
                  (bay.drift == null || (!bay.drift.Ring.enabled && !bay.drift.Wake.enabled)));
        }
        dock.Deselect();

        // The same measurement sees the art of a bought hull.
        dock.Select(1);
        dock.bays[1].SnapPower();
        RunThrusters();
        var owned = RenderHull(dock.bays[1], Path.Combine(PreviewDir, "silhouette-1-owned.png"));
        Check("a bought hull renders its full art (" + Why(owned) + ")", owned.rendered && owned.colours > 8 && !dock.bays[1].Silhouetted);
        dock.Deselect();
        dock.bays[1].SnapPower();
    }

    static string Label(int id) { return "ship" + id + " (" + shopingShips.NameFor(id) + ")"; }

    static string PreviewDir
    {
        get
        {
            string dir = System.Environment.GetEnvironmentVariable("PAUSE_DOCK_PREVIEW_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Path.GetTempPath(), "pause-dock-silhouettes");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public struct HullRender
    {
        public int covered;      // pixels the ship (hull + engine) touches at all
        public int opaque;       // pixels it covers completely (same over black and white)
        public int colours;      // distinct colours among the opaque pixels
        public Color32 first;
        public bool rendered;
    }

    static string Why(HullRender r) { return r.opaque + "/" + r.covered + " px opaque, " + r.colours + " colour(s), first " + r.first; }

    static bool Flat(HullRender r, out string why)
    {
        Color32 ink = (Color)DockBay.SilhouetteInk;
        why = Why(r);
        return r.rendered && r.opaque >= 200 && r.colours == 1 &&
               Mathf.Abs(r.first.r - ink.r) <= 1 && Mathf.Abs(r.first.g - ink.g) <= 1 && Mathf.Abs(r.first.b - ink.b) <= 1;
    }

    // Renders only this berth's ship (hull plus every child: exhaust, spin
    // drift) once over black and once over white. A pixel that comes out the
    // same over both is fully inside the alpha mask.
    public static HullRender RenderHull(DockBay bay, string savePath = null)
    {
        var result = new HullRender();
        if (bay == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return result;
        const int Layer = 31, W = 256;
        var renderers = bay.ship.GetComponentsInChildren<Renderer>(false);
        var layers = new int[renderers.Length];
        for (int k = 0; k < renderers.Length; k++) { layers[k] = renderers[k].gameObject.layer; renderers[k].gameObject.layer = Layer; }

        var camGo = new GameObject("~HullCam");
        var cam = camGo.AddComponent<Camera>();
        Bounds b = bay.hull.bounds;
        camGo.transform.position = new Vector3(b.center.x, b.center.y, b.center.z - 10f);
        cam.orthographic = true;
        cam.orthographicSize = Mathf.Max(b.extents.x, b.extents.y) * 2.2f;
        cam.aspect = 1f;
        cam.cullingMask = 1 << Layer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.enabled = false;
        var rt = new RenderTexture(W, W, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;

        var shots = new Color32[2][];
        var backs = new[] { Color.black, Color.white };
        for (int k = 0; k < 2; k++)
        {
            cam.backgroundColor = backs[k];
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, W, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, W, W), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            shots[k] = tex.GetPixels32();
            if (k == 1 && savePath != null) File.WriteAllBytes(savePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo);
        for (int k = 0; k < renderers.Length; k++) renderers[k].gameObject.layer = layers[k];

        var seen = new HashSet<int>();
        for (int i = 0; i < shots[0].Length; i++)
        {
            Color32 bl = shots[0][i], wh = shots[1][i];
            bool onBlack = bl.r > 0 || bl.g > 0 || bl.b > 0;
            bool onWhite = wh.r < 255 || wh.g < 255 || wh.b < 255;
            if (!onBlack && !onWhite) continue;
            result.covered++;
            if (Mathf.Abs(bl.r - wh.r) > 1 || Mathf.Abs(bl.g - wh.g) > 1 || Mathf.Abs(bl.b - wh.b) > 1) continue;
            result.opaque++;
            if (seen.Add((bl.r << 16) | (bl.g << 8) | bl.b) && seen.Count == 1) result.first = bl;
        }
        result.colours = seen.Count;
        result.rendered = true;
        return result;
    }
}
