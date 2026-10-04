using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders the companion drone fighting a ship's fires (ShipDamageFx +
// UltimateGun) as numbered PNG frames for review: intact, a hit (the drone
// jolts and blinks), it flies in and sprays, the foam calms the spot; a
// second hit to the last life (more often); then healed. One folder per ship.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod FloatiePreview.Run
//   (writes to $FLOATIE_PREVIEW_DIR, else Builds/FloatiePreview;
//    $FLOATIE_SHIPS: comma separated ids, default 3,7,11,13)
public static class FloatiePreview
{
    const float Dt = 1f / 60f;
    const int Px = 420, Py = 420;
    static readonly Vector3 Far = new Vector3(500f, 500f, 0f);

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("FLOATIE_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/FloatiePreview";
        string ships = System.Environment.GetEnvironmentVariable("FLOATIE_SHIPS");
        if (string.IsNullOrEmpty(ships)) ships = "3,7,11,13";
        using (var sandbox = new TestHarness.Sandbox())
        {
            EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
            buttonClicks.playerDied = false;
            score.pauseCounter = 0;
            ShipUiSlots.ScreenOverride = () => Rect.MinMaxRect(Far.x - 3f, Far.y - 5f, Far.x + 3f, Far.y + 5f);
            foreach (var s in ships.Split(','))
                Ship(int.Parse(s.Trim()), dir);
            ShipUiSlots.ScreenOverride = null;
            collisionDetection.lifeCounter = 0;
        }
        EditorApplication.Exit(0);
    }

    static void Ship(int id, string root)
    {
        string dir = Path.Combine(root, id.ToString("00") + "-" + ShipId.NameOf(id).Replace(' ', '_'));
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);
        int max = ShipLives.Max(id);
        collisionDetection.MAXLIFE = max;
        collisionDetection.lifeCounter = 0;
        PlayerPrefs.SetInt("spawnShip", id);

        var go = new GameObject(ShipId.ObjectName(id) + "(Clone)", typeof(SpriteRenderer));
        go.GetComponent<SpriteRenderer>().sortingOrder = 10;
        go.transform.position = Far;
        var life = go.AddComponent<lifeControler>();
        life.SendMessage("Start");
        var fx = go.GetComponent<ShipDamageFx>();
        fx.Build();
        var power = go.AddComponent<ShipPowerController>();
        power.SendMessage("Awake");
        power.SendMessage("Start");
        typeof(ShipPowerController).GetField("timer", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(power, 999f);
        var gun = go.GetComponentInChildren<UltimateGun>();
        if (gun != null && gun.transform.childCount == 0) gun.SendMessage("Awake");
        var thruster = go.AddComponent<ShipThruster>();
        thruster.SendMessage("Start");
        var hearts = go.AddComponent<ShipLivesIndicator>();
        hearts.BuildHearts(max);

        var camGo = new GameObject("~FloatieCam", typeof(Camera));
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = .72f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(.05f, .06f, .12f);
        cam.transform.position = Far + new Vector3(0f, -.05f, -10f);
        cam.aspect = Px / (float)Py;
        var rt = new RenderTexture(Px, Py, 24);
        cam.targetTexture = rt;
        var label = Label(Far + new Vector3(0f, .65f, 0f));

        float spin = ShipUiSlots.Spins(id) ? 240f : 0f;
        int n = 0;
        float t = 0f;
        // (seconds, lives lost, caption)
        var script = new (float, int, string)[]
        {
            (.6f, 0, "intact: the drone hovers"),
            (4.5f, max > 2 ? 1 : max - 1, max > 2 ? "hit! damaged: drone jolts, flies in, sprays" : "hit! last life: drone jolts, flies in, sprays"),
            (4.5f, max - 1, "hit! last life: sprays more often"),
            (1.6f, 0, "healed: back to its hover"),
        };
        foreach (var (seconds, lost, caption) in script)
        {
            collisionDetection.lifeCounter = lost;
            label.text = ShipId.NameOf(id) + "  -  " + caption;
            for (float s = 0f; s < seconds; s += Dt, t += Dt)
            {
                life.SendMessage("Update");
                if (spin != 0f) go.transform.rotation *= Quaternion.Euler(0f, 0f, spin * Dt);
                gun.Step(0f, Dt, Dt);
                hearts.SendMessage("Update");
                hearts.Place(Dt, Dt);
                hearts.StepBreaks(Dt);
                thruster.SendMessage("LateUpdate");
                fx.Tick(Dt);
                if (Mathf.RoundToInt(t / Dt) % 2 == 0) Shoot(cam, rt, Path.Combine(dir, "f" + (n++).ToString("0000") + ".png"));
            }
        }
        Debug.Log("[FLOATIE] " + dir + " frames " + n + " sprays " + fx.Sprays + " hits " + fx.HitsSeen);
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(label.gameObject);
        power.SendMessage("OnDestroy");
        Object.DestroyImmediate(go);
        collisionDetection.lifeCounter = 0;
    }

    static TextMesh Label(Vector3 at)
    {
        var go = new GameObject("~Label");
        var tm = go.AddComponent<TextMesh>();
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        go.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
        tm.characterSize = .011f;
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
