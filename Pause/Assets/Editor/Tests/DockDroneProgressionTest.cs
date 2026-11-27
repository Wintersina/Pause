using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.UI;

public static class DockDroneProgressionTest
{
    static int fails;
    static void Check(string text, bool ok) { Debug.Log((ok ? "[DOCK-DRONE] PASS " : "[DOCK-DRONE] FAIL ") + text); if (!ok) fails++; }
    public static void Run() { TestHarness.Exit(Execute()); }
    static void Level(int ship, int level)
    {
        PlayerPrefs.SetString(ShipId.OwnedKey(ship), "True");
        for (int skin = 1; skin < ShipSkins.CountFor(ship); skin++) PlayerPrefs.SetInt(ShipSkins.OwnedKey(ship, skin), skin <= level ? 1 : 0);
        PlayerPrefs.SetInt("spawnShip", ship);
    }
    public static int Execute()
    {
        fails = 0; using var sandbox = new TestHarness.Sandbox();
        DeveloperUnlocks.SetEnabled(false); startMenu.youAreInTutorial = false;
        int[] expected = { 1, 1, 1, 2, 3 };
        foreach (int ship in ShipId.All)
        for (int tier = 0; tier <= 4; tier++)
        {
            Level(ship, tier); ShotAtoms.BeginRun();
            bool count = DroneSquad.StartingFor(ship) == expected[tier] && ShotAtoms.DroneCount == expected[tier] && DroneSquad.Slots == expected[tier];
            DroneSquad.CollectCore(DroneMode.Aimed);
            for (int coin = 0; coin < 8; coin++) ShotAtoms.Collect(ShotAtom.Twin);
            Check(ShipId.NameOf(ship) + " tier " + tier + ": coins activate only dock-unlocked slots", count && DroneSquad.ActiveCount == expected[tier] && DroneSquad.Slots == expected[tier]);
            int recruited = DroneSquad.AddBossDrone();
            Check("boss recruit adds one slot beyond the dock allowance", recruited == expected[tier] && DroneSquad.Slots == expected[tier] + 1);
        }
        Check("starter hull prices and upgrades remain accessible", shopingShips.CostFor(1) == 0f && shopingShips.CostFor(2) == 300f && ShipSkins.PriceOf(1, 1) == 50f && ShipSkins.PriceOf(2, 1) == 50f);
        Check("ship 15 is the 15k price anchor", shopingShips.CostFor(15) == 15000f && shopingShips.CostFor(7) > shopingShips.CostFor(15));
        Offscreen(); Paused(); DockBadges();
        return fails;
    }
    static void Offscreen()
    {
        ShotFeelTest.FreshScene();
        var camera = Camera.main;
        foreach (float halfHeight in new[] { 5f, 8.7f, 11f })
        {
            camera.orthographicSize = halfHeight; camera.transform.position = new Vector3(0f, 2f, -10f);
            var modifier = ShotAtomPickup.Spawn(ShotAtom.Twin, Vector3.zero);
            var core = DroneCorePickup.Spawn(DroneMode.Aimed, Vector3.zero);
            DroneCorePickup.SpawnRandom(Vector3.zero, .5f);
            bool hidden = modifier.GetComponent<SpriteRenderer>().bounds.min.y > halfHeight + 2f;
            foreach (var pickup in Object.FindObjectsByType<DroneCorePickup>(FindObjectsSortMode.None)) hidden &= pickup.GetComponent<SpriteRenderer>().bounds.min.y > halfHeight + 2f;
            Check("all modifier/mode coins enter above camera top " + halfHeight, hidden);
            foreach (var pickup in Object.FindObjectsByType<DroneCorePickup>(FindObjectsSortMode.None)) Object.DestroyImmediate(pickup.gameObject);
            Object.DestroyImmediate(modifier);
        }
        Bounds hull = new Bounds(Vector3.zero, new Vector3(.7f, 1f, 0f));
        Vector3 bank = HealBankIcon.Place(hull, false, default, new Rect(-3f, -5f, 6f, 10f), .25f, true);
        Check("a banked heal sits directly under the hull", Mathf.Abs(bank.x) < .001f && bank.y < hull.min.y && !ShipUiSlots.Overlaps(HealBankIcon.FootprintAt(bank, .25f, true), hull));
    }
    static void Paused()
    {
        EditorSceneLoader.Open("gameS1"); ShotAtoms.BeginRun(3);
        var rig = ShotFeelTest.MakeShip(1, 4, activate: true);
        while (DroneSquad.Slots < 12) DroneSquad.AddBossDrone(); rig.battery.Refresh();
        for (int drone = 0; drone < 12; drone++) ShotAtoms.SetLevel(drone, ShotAtom.Twin, drone % 10 + 1);
        var hud = new GameObject(ShotAtomHud.HostName).AddComponent<ShotAtomHud>();
        TouchInput.PressedOverride = () => false; score.pauseCounter = 5;
        hud.SendMessage("Update"); hud.AdvancePausedReview(2f);
        bool levels = true;
        for (int drone = 0; drone < 12; drone++) levels &= hud.PausedLevelLabel(drone, ShotAtom.Twin) == "x" + (drone % 10 + 1);
        Check("pause expands all twelve individual drone modifier rows", hud.PauseExpanded && hud.ExpandedRowsVisible == 12 && levels);
        bool separated = true;
        for (int drone = 1; drone < 12; drone++) separated &= hud.PausedRowRect(drone).yMax <= hud.PausedRowRect(drone - 1).yMin;
        Check("all expanded rows remain distinct and inside the screen", separated && hud.PausedRowRect(11).yMin >= ScreenInfo.SafeArea.yMin);
        foreach (var image in hud.GetComponentsInChildren<Image>(true)) levels &= !image.raycastTarget;
        ShotAtoms.SetLevel(0, ShotAtom.Twin, 0);
        ShotAtoms.SetLevel(1, ShotAtom.Arc, 2);
        hud.SendMessage("Update");
        Check("each paused row omits uncollected modifiers and all zero labels",
            hud.PausedLevelLabel(0, ShotAtom.Twin) == null && hud.PausedUpgradeCount(0) == 0 &&
            hud.PausedUpgradeCount(1) == 2 && hud.PausedLevelLabel(1, ShotAtom.Arc) == "x2");
        ShotAtoms.SetLevel(0, ShotAtom.Arc, 1); ShotAtoms.SetLevel(0, ShotAtom.Twin, 1);
        hud.SendMessage("Update");
        Check("newly collected modifiers insert in first-catch order without empty columns",
            hud.PausedUpgradeCount(0) == 2 && hud.PausedColumn(0, ShotAtom.Twin) == 1 && hud.PausedColumn(0, ShotAtom.Arc) == 2);
        Check("expanded rows never intercept the resume press", levels);
        TouchInput.PressedOverride = () => true; hud.SendMessage("Update");
        Check("flight collapses back to the selected drone", !hud.PauseExpanded);
        ShotFeelTest.Teardown(rig); TouchInput.PressedOverride = null;
    }
    static void DockBadges()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Level(3, 3);
        var root = new GameObject("Dock"); var bay = DockBay.Create(root.transform, 3, CodexUi.FindFont());
        bay.RefreshStatus(true, false, shopingShips.CostFor(3));
        Check("owned berth shows current health and two unlocked drones", bay.ShowsLoadout && bay.DroneCountText == "2" && bay.HealthCountText == ShipLives.Max(3).ToString());
        foreach (Facing facing in System.Enum.GetValues(typeof(Facing)))
        {
            bay.SetFacing(facing);
            bay.SetLabelScale(1f);
            Check("wall loadout stays below its larger number: " + facing,
                bay.LoadoutPosition.y < bay.NumberRect.yMin && Mathf.Abs(bay.LoadoutPosition.x - bay.NumberRect.center.x) < .001f);
        }
        Level(3, 4); bay.RefreshStatus(true, true, shopingShips.CostFor(3));
        Check("berth badge updates immediately after the final upgrade", bay.DroneCountText == "3");
        bay.RefreshStatus(false, false, shopingShips.CostFor(3));
        Check("locked silhouette does not expose owned loadout badges", !bay.ShowsLoadout);
    }
}
