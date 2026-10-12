using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Frames of several ships' secret-power bursts and ultimates drawn from the powers-v2 strips,
// at phone aspect (1080 x 2400), for eyeballing size and clarity.
//
//   scripts/unity-batch.sh -executeMethod PowersPreview.Run
//   (writes to $POWERS_PREVIEW_DIR, else Builds/PowersPreview; <ship>-power-<n>.png / <ship>-ult-<n>.png)
public static class PowersPreview
{
    const int Width = 1080, Height = 2400;
    static readonly int[] Ships = { 1, 2, 3, 12, 15, 5, 6 };
    static readonly float[] Times = { .05f, .18f, .35f };

    static Camera cam;
    static RenderTexture rt;
    static Texture2D png;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("POWERS_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/PowersPreview";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        {
            try { foreach (int id in Ships) { One(dir, id, true); One(dir, id, false); } }
            finally { AttackPool.StopAll(); WorldTimeFx.Reset(); }
        }
        EditorApplication.Exit(0);
    }

    static void One(string dir, int id, bool power)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.aspect = Width / (float)Height;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(.05f, .05f, .12f);
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 3;
        collisionDetection.cloakTimer = 0f;
        Time.timeScale = 1f;
        WorldTimeFx.Reset();
        if (rt == null) rt = new RenderTexture(Width, Height, 24);
        if (png == null) png = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        cam.targetTexture = rt;

        PlayerPrefs.SetInt("spawnShip", id);
        var go = new GameObject("ship" + id, typeof(SpriteRenderer));
        go.transform.position = new Vector3(0f, -3f, 0f);
        var sr = go.GetComponent<SpriteRenderer>();
        sr.sprite = EliteFxArt.Plate;
        go.transform.localScale = new Vector3(.012f, .012f, 1f);
        go.transform.rotation = Quaternion.Euler(0f, 0f, 180f);
        sr.sortingOrder = 20;
        var c = go.AddComponent<ShipPowerController>();
        c.SendMessage("Awake");
        c.SendMessage("Start");
        var gun = go.GetComponentInChildren<UltimateGun>();
        if (gun != null) gun.SendMessage("Awake");

        for (int i = 0; i < 6; i++)
        {
            var e = new GameObject("enemy" + i);
            e.tag = "Enimey";
            e.transform.position = new Vector3(-1.8f + i * .72f, 1f + (i % 3) * 1.2f, 0f);
            var es = e.AddComponent<SpriteRenderer>();
            es.sprite = EliteFxArt.Plate;
            es.color = new Color(.6f, .6f, .6f);
            e.transform.localScale = new Vector3(.01f, .01f, 1f);
            ClearTarget.Ensure(e).SetRadius(.3f);
        }

        if (power) { c.Secret.SetMeter(SecretPowerController.Full); c.Secret.Fire(); }
        else if (c.Runner != null) c.Runner.Fire();

        float clock = 0f;
        foreach (float t in Times)
        {
            while (clock < t)
            {
                const float dt = 1f / 60f;
                clock += dt;
                c.Step(dt);
                PowerTrailSprite.StepAll(dt);
                foreach (var a in Object.FindObjectsByType<AttackSprite>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) a.Tick(dt);
            }
            cam.Render();
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            png.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            png.Apply();
            RenderTexture.active = old;
            string stem = ShipId.KeyOf(id) + (power ? "-power-" : "-ult-") + Mathf.RoundToInt(t * 1000f) + "ms";
            File.WriteAllBytes(Path.Combine(dir, stem + ".png"), png.EncodeToPNG());
            Debug.Log("[POWERS-PREVIEW] " + stem);
        }
        c.SendMessage("OnDestroy");
        if (c.Runner != null) c.Runner.SendMessage("OnDestroy");
        if (c.Secret != null) c.Secret.SendMessage("OnDestroy");
        AttackPool.StopAll();
    }
}
