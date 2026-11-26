using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class DockProgressionReview
{
    public static void Export()
    {
        using var sandbox = new TestHarness.Sandbox();
        const string folder = "/private/tmp/pause-ship-progression-review";
        foreach (string id in new[] { "and-480x854", "flip7-cover-948x1048", "and-1080x2400-gesture" })
        {
            var device = Array.Find(FitDevice.All, d => d.id == id);
            for (int scene = 0; scene < 6; scene++)
            {
                int state = scene;
                var screen = new FitScreen { id = "progression-" + state, scene = state < 2 ? "shopS6" : "gameS1", gameplayView = state >= 2,
                    stage = rig => { if (state < 2) Dock(rig, state); else Flight(rig, state == 2 ? 1 : state == 3 || state == 5 ? 3 : 12, state == 5); } };
                var shot = ScreenFitRunner.Run(screen, device, folder + "/" + id + "-" + state + ".png", 1200);
                if (shot.error != null) throw new Exception(shot.error);
            }
        }
        Debug.Log("[DOCK-REVIEW] exported " + folder);
        EditorApplication.Exit(0);
    }
    static void Dock(ScreenFitRig rig, int state)
    {
        ScreenFitScreens.Dock(rig, 0, false);
        foreach (int ship in ShipId.All)
        {
            PlayerPrefs.SetString(ShipId.OwnedKey(ship), "True");
            int level = ship % 3 == 0 ? 4 : ship % 3 == 1 ? 0 : 3;
            for (int skin = 1; skin < ShipSkins.CountFor(ship); skin++) PlayerPrefs.SetInt(ShipSkins.OwnedKey(ship, skin), skin <= level ? 1 : 0);
        }
        var dock = SpaceDock.Instance; dock.RefreshStatuses(); dock.Relayout();
        if (state == 1) { dock.Select(8); dock.popup.SkipAppear(); dock.SendMessage("LateUpdate"); }
        rig.Sync();
    }
    static void Flight(ScreenFitRig rig, int slots, bool upgradeCard = false)
    {
        ScreenFitScreens.WorldBase(rig, 0, false, false);
        ShotAtoms.BeginRun(Mathf.Min(3, slots));
        var ship = new GameObject("ship1", typeof(SpriteRenderer)); spawnShips.ApplyHull(ship, 1);
        ship.transform.position = new Vector3(0f, -2f, 0f);
        var power = ship.AddComponent<ShipPowerController>(); power.SendMessage("Awake"); power.SendMessage("Start");
        DroneSquad.CollectCore(DroneMode.Aimed);
        for (int d = 1; d < DroneSquad.Slots; d++) DroneSquad.Activate(d);
        while (DroneSquad.Slots < slots) DroneSquad.AddBossDrone();
        var battery = ship.GetComponent<FloatieBattery>(); battery.Refresh();
        for (int drone = 0; drone < DroneSquad.Slots; drone++)
        for (int atom = 0; atom < ShotAtomTuning.PerDroneAtoms; atom++) if ((drone + atom) % 3 == 0) ShotAtoms.SetLevel(drone, (ShotAtom)atom, (drone + atom) % 10 + 1);
        ShotAtoms.SetLevel(ShotAtom.Rapid, 3);
        battery.SendMessage("LateUpdate");
        HealBank.Reset(); HealBank.OnCatch(false, false); HealBank.OnCatch(false, false);
        HealBankIcon.Attach(ship).Refresh();
        score.pauseCounter = 3; TouchInput.PressedOverride = () => false;
        var hud = new GameObject(ShotAtomHud.HostName).AddComponent<ShotAtomHud>(); hud.SendMessage("OnEnable"); hud.SendMessage("Update"); hud.AdvancePausedReview(2f);
        if (slots == 3 && !upgradeCard)
        {
            var scoreHud = UnityEngine.Object.FindFirstObjectByType<ScoreHud>();
            scoreHud.ShowAtomUpgrade("TWIN SHOT -> DRONE 1", "2 SHOTS → 3 SHOTS", ShotAtoms.Colour(ShotAtom.Twin), new Vector2(float.NaN, float.NaN), "LV 2");
            scoreHud.StepPopups(.2f);
            if (scoreHud.AtomUpgradeText.enabled || scoreHud.AtomUpgradeCard.gameObject.activeSelf) throw new Exception("Upgrade card obscures paused review");
            score.pauseCounter = 0; TouchInput.PressedOverride = () => true; hud.SendMessage("Update"); scoreHud.StepPopups(0f);
            if (!scoreHud.AtomUpgradeText.enabled || !scoreHud.AtomUpgradeCard.gameObject.activeSelf) throw new Exception("Upgrade card did not return after resume");
            score.pauseCounter = 3; TouchInput.PressedOverride = () => false; hud.SendMessage("Update"); hud.AdvancePausedReview(2f); scoreHud.StepPopups(0f);
        }
        if (upgradeCard)
        {
            score.pauseCounter = 0; TouchInput.PressedOverride = () => true; hud.SendMessage("Update");
            hud.SendMessage("OnEnable"); rig.Sync();
            if (!ShotAtomHud.PopupAnchor(out Vector2 at)) throw new Exception("Missing upgrade anchor");
            var scoreHud = ScoreHud.Current ?? UnityEngine.Object.FindFirstObjectByType<ScoreHud>();
            if (scoreHud == null) throw new Exception("Missing score HUD");
            scoreHud.ShowAtomUpgrade("TWIN SHOT -> DRONE 1", "2 SHOTS → 3 SHOTS", ShotAtoms.Colour(ShotAtom.Twin), at, "LV 2");
            scoreHud.StepPopups(.2f);
        }
        rig.Sync();
    }
}
