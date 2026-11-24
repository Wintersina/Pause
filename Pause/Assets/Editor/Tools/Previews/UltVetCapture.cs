using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Renders every ship's ULTIMATE mid-fire with the drones shooting (shot atoms on) as numbered PNG frames, for the owner's review of the
// gun-during-ultimate rule. One folder per job; Paragon also with 2 drones (tier 0) and 6 drones (tier 4).
//
//   scripts/unity-batch.sh -projectPath Pause -executeMethod UltVetCapture.Run
//   ($ULTVET_DIR: output folder, default Builds/UltVet; $ULTVET_SHIPS: comma separated ids, default all)
public static class UltVetCapture
{
    const float Dt = 1f / 60f;
    const int Px = 405, Py = 720;
    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        string root = System.Environment.GetEnvironmentVariable("ULTVET_DIR");
        if (string.IsNullOrEmpty(root)) root = "Builds/UltVet";
        string ships = System.Environment.GetEnvironmentVariable("ULTVET_SHIPS");
        var ids = new System.Collections.Generic.List<int>();
        if (string.IsNullOrEmpty(ships)) foreach (int id in ShipId.All) ids.Add(id);
        else foreach (var s in ships.Split(',')) ids.Add(int.Parse(s.Trim()));
        using (var sandbox = new TestHarness.Sandbox())
        {
            foreach (int id in ids)
            {
                Job(root, id, ShipWeaponUpgrades.MaxLevel, PremiumShips.IsPremium(id) ? "6drones" : "");
                if (PremiumShips.IsPremium(id)) Job(root, id, 0, "2drones");
            }
        }
        EditorApplication.Exit(0);
    }

    static void Job(string root, int id, int level, string tag)
    {
        string dir = Path.Combine(root, id.ToString("00") + "-" + ShipId.NameOf(id).Replace(' ', '_') + (tag != "" ? "-" + tag : ""));
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
        ShotFeelTest.FreshScene();
        for (int a = 0; a < ShotAtomTuning.PerDroneAtoms; a++) ShotAtoms.SetLevel((ShotAtom)a, 2);
        ShotAtoms.SetLevel(ShotAtom.Twin, 3);
        ShotAtoms.SetLevel(ShotAtom.Rapid, 3);

        PlayerPrefs.SetInt("spawnShip", id);
        var hullSprite = ShipHullArt.StockRest(id);
        var ship = new GameObject("ship" + id, typeof(SpriteRenderer));
        var sr = ship.GetComponent<SpriteRenderer>();
        sr.sprite = hullSprite;
        sr.sortingOrder = 10;
        if (hullSprite != null) ship.transform.localScale = Vector3.one * (1.15f / Mathf.Max(.01f, hullSprite.bounds.size.y));
        ship.transform.position = new Vector3(0f, -3f, 0f);
        var power = ship.AddComponent<ShipPowerController>();
        power.SendMessage("Awake");
        power.SendMessage("Start");
        var gun = ship.GetComponentInChildren<UltimateGun>();
        if (gun != null) gun.SendMessage("Awake");
        var battery = ship.GetComponent<FloatieBattery>();
        if (battery != null) battery.SetLevel(level);

        var enemies = new System.Collections.Generic.List<GameObject>();
        float[] xs = { -1.6f, -.8f, 0f, .8f, 1.6f };
        foreach (float x in xs)
        {
            foreach (var pos in new[] { new Vector3(x, -.3f, 0f), new Vector3(x * .7f, 1.8f, 0f) })
            {
                var e = EnemyFactory.Create(EnemyRoster.One(3, EnemyRole.Fighter), pos, Quaternion.identity);
                ClearTarget.Ensure(e);
                enemies.Add(e);
            }
        }

        var camGo = new GameObject("~UltCam", typeof(Camera));
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(.04f, .05f, .11f);
        cam.transform.position = new Vector3(0f, 0f, -10f);
        cam.aspect = Px / (float)Py;
        var rt = new RenderTexture(Px, Py, 24);
        cam.targetTexture = rt;
        var label = Label(new Vector3(0f, 4.7f, 0f));
        label.text = ShipId.NameOf(id) + "  " + ShipLoadoutTable.For(id).attackName + (battery != null ? "  (" + battery.Count + " drones)" : "");

        int n = 0;
        battery.ResetTimers();
        for (float t = 0f; t < 1.2f; t += Dt) { battery.Step(Dt); }
        typeof(ShipPowerController).GetMethod("Fire", Inst).Invoke(power, null);
        for (float t = 0f; t < 2.4f; t += Dt)
        {
            power.Runner.Step(Dt);
            if (gun != null) gun.Step(1f, Dt, Dt);
            battery.Step(Dt);
            if (Mathf.RoundToInt(t / Dt) % 3 == 0) Shoot(cam, rt, Path.Combine(dir, "f" + (n++).ToString("0000") + ".png"));
        }
        Debug.Log("[ULTVET-CAP] " + dir + " frames " + n + " shots " + string.Join("/", battery.Shots));
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(label.gameObject);
        if (ShipPowerController.CinematicClearActive) { var m = typeof(ShipPowerController).GetMethod("FinishCinematic", Inst); if (m != null) m.Invoke(power, null); }
        ShotFeelTest.Teardown(new ShotFeelTest.Rig { ship = ship, power = power, gun = gun, battery = battery });
        foreach (var e in enemies) if (e != null) Object.DestroyImmediate(e);
        ShotAtoms.BeginRun();
    }

    static TextMesh Label(Vector3 at)
    {
        var go = new GameObject("~Label");
        var tm = go.AddComponent<TextMesh>();
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        go.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
        tm.characterSize = .05f;
        tm.fontSize = 48;
        tm.anchor = TextAnchor.UpperCenter;
        tm.color = new Color(.85f, .9f, 1f);
        go.transform.position = new Vector3(at.x, at.y, -2f);
        return tm;
    }

    static void Shoot(Camera cam, RenderTexture rt, string path)
    {
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var png = new Texture2D(Px, Py, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, Px, Py), 0, 0);
        png.Apply();
        File.WriteAllBytes(path, png.EncodeToPNG());
        RenderTexture.active = old;
        Object.DestroyImmediate(png);
    }
}
