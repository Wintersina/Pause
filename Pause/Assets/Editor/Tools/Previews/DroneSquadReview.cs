using System.IO;
using UnityEditor;
using UnityEngine;

// Review the real gameplay art, HUD and pickup previews at phone and cover-screen sizes.
public static class DroneSquadReview
{
    public static void Export()
    {
        using var sandbox = new TestHarness.Sandbox();
        string dir = HeartQuarterPreview.DirectoryPath + "/drone-squad";
        Directory.CreateDirectory(dir);
        foreach (string device in new[] { "and-480x854", "flip7-cover-948x1048" })
        {
            var size = System.Array.Find(FitDevice.All, d => d.id == device);
            for (int state = 0; state < 6; state++)
            {
                int snapshot = state;
                var screen = new FitScreen { id = "drone-review-" + state, scene = "gameS1", gameplayView = true,
                    stage = rig => Stage(rig, snapshot) };
                var result = ScreenFitRunner.Run(screen, size, Path.Combine(dir, device + "-" + state + ".png"), 1048);
                if (result.error != null) throw new System.Exception(result.error);
            }
        }
        HeartQuarterPreview.ExportCombat();
        Debug.Log("[SQUAD-REVIEW] exported " + dir);
        EditorApplication.Exit(0);
    }
    static void Stage(ScreenFitRig rig, int state)
    {
        ShotAtoms.BeginRun();
        ScreenFitScreens.WorldBase(rig, state == 5 ? 3 : 0, false, false);
        var ship = new ShotFeelTest.Rig();
        ship.ship = new GameObject("ship1", typeof(SpriteRenderer));
        spawnShips.ApplyHull(ship.ship, 1);
        ship.power = ship.ship.AddComponent<ShipPowerController>();
        ship.power.SendMessage("Awake"); ship.power.SendMessage("Start");
        ship.gun = ship.ship.GetComponentInChildren<UltimateGun>();
        if (ship.gun != null) ship.gun.SendMessage("Awake");
        ship.battery = ship.ship.GetComponent<FloatieBattery>();
        ship.battery.SetLevel(0);
        ship.ship.transform.position = new Vector3(0f, -2f, 0f);
        if (state == 0) DroneCorePickup.Build(DroneMode.Aimed, ship.ship.transform.position + Vector3.up * 1.25f);
        if (state >= 1) DroneSquad.CollectCore(DroneMode.Aimed);
        if (state >= 2) ShotAtoms.Collect(ShotAtom.Twin);
        if (state >= 3) { ShotAtoms.Collect(ShotAtom.Twin); ShotAtoms.Collect(ShotAtom.Arc); }
        if (state == 1 || state == 2) ShotAtomPickup.Build(ShotAtom.Twin, ship.ship.transform.position + Vector3.up * 1.8f);
        if (state == 3) DroneCorePickup.SpawnChoice(ship.ship.transform.position + Vector3.up * 1.8f);
        if (state == 4)
        {
            int recruit = DroneSquad.AddBossDrone(); ship.battery.Refresh();
            ship.battery.JoinDrone(recruit, ship.ship.transform.position + Vector3.up * 1.8f);
        }
        if (state == 5)
        {
            while (DroneSquad.Slots < 12) DroneSquad.AddBossDrone();
            for (int atom = 0; atom < ShotAtomTuning.Count; atom++) ShotAtoms.SetLevel((ShotAtom)atom, 6);
        }
        ship.battery.Refresh();
        for (int i = 0; i < ship.battery.Count - 1; i++) ship.battery.Extra(i).Place();
        ship.battery.SendMessage("LateUpdate");
        if (ship.gun != null) ship.gun.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
        foreach (var preview in Object.FindObjectsByType<DronePickupPreview>(FindObjectsSortMode.None))
        { preview.Build(); preview.SendMessage("LateUpdate"); }
        if (state > 0)
        {
            EnemyDef definition = EnemyRoster.One(0, EnemyRole.Fighter);
            if (state == 5)
            {
                var manager = new GameObject("~ReviewClock").AddComponent<WorldManager>(); manager.SendMessage("Awake");
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                WorldManager.CurrentIndex = 3;
                typeof(WorldManager).GetField("levelBegun", flags).SetValue(manager, true);
                typeof(WorldManager).GetField("distanceLeft", flags).SetValue(manager, manager.WorldDistance * .4f);
                foreach (var def in EnemyRoster.All) if (def.role == EnemyRole.Fighter && def.tier == 4) { definition = def; break; }
            }
            var enemy = EnemyFactory.Create(definition, new Vector3(.6f, 1.5f, 0f), Quaternion.identity, 1f, true);
            ClearTarget.Ensure(enemy);
            foreach (var hearts in Object.FindObjectsByType<EnemyHeartsView>(FindObjectsSortMode.None))
                for (int t = 0; t < 30; t++) hearts.Place(.02f, .02f);
            ship.battery.Step(.01f);
            for (int i = 0; i < ship.battery.Count; i++) ship.battery.Fire(i);
            for (int t = 0; t < 12; t++) ship.battery.Step(1f / 60f);
        }
        var hud = new GameObject(ShotAtomHud.HostName).AddComponent<ShotAtomHud>();
        hud.SendMessage("Start", SendMessageOptions.DontRequireReceiver); hud.SendMessage("Update");
        rig.Sync();
    }
}
