using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders each boss with its five spinning hearts over its own world's
// backdrop (whole view, and after two hearts are lost), and the elite and
// boss hearts close up with their outline off (before) and on (after), for
// review.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod BossHeartsPreview.Run
//   (writes to $BOSSHEARTS_PREVIEW_DIR, else Builds/BossHeartsPreview)
public static class BossHeartsPreview
{
    const int Width = 1080, Height = 2340;   // a 9:19.5 phone
    const float Dt = 1f / 60f;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("BOSSHEARTS_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/BossHeartsPreview";
        Directory.CreateDirectory(dir);
        string[] worlds = { "Space", "Frost", "Verdant", "Ember" };
        using (new TestHarness.Sandbox())
        {
            try
            {
                for (int w = 0; w < worlds.Length; w++) World(dir, w, worlds[w]);
            }
            finally
            {
                BossEncounter.ResetRun();
                EliteSystem.PlayerOverride = null;
                EliteSystem.Clear();
            }
        }
        EditorApplication.Exit(0);
    }

    static void World(string dir, int w, string world)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        BossRails.Reset();
        PlayField.Reset();
        EliteSystem.Clear();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.aspect = Width / (float)Height;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, Width, Height);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, w);

        var backdrop = new GameObject("~Backdrop").AddComponent<WorldBackdrop>();
        backdrop.Show(world, false);
        for (int i = 0; i < 600; i++) backdrop.Step(Dt);

        BossEncounter.Begin(w, null);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);
        e.Actor.ForcedAttack = 0;
        for (int i = 0; i < 45; i++) e.Step(Dt, 1f);   // the pop-in done, a little way round

        var pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        var all = EliteCatalog.All;
        var elites = new EliteHearts[2];
        for (int k = 0; k < elites.Length; k++)
        {
            var ship = EliteShip.CreateInPlay(all[(w * 2 + k) % all.Length], new Vector2(-1.2f + 2.4f * k, -1.6f));
            ship.AttackCooldown = 99f;
            elites[k] = ship.GetComponent<EliteHearts>();
            for (int i = 0; i < 40; i++) elites[k].Place(Dt, Dt);
        }

        var rt = new RenderTexture(Width, Height, 24);
        cam.targetTexture = rt;
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        string stem = Path.Combine(dir, "bosshearts-" + world.ToLowerInvariant());

        Grab(cam, rt, tex);
        File.WriteAllBytes(stem + "-full.png", tex.EncodeToPNG());
        Vector3 bossAt = e.Actor.transform.position;
        Crop(cam, tex, bossAt, 2.6f, stem + "-boss-after.png");
        foreach (var eh in elites) Crop(cam, tex, eh.transform.position, .55f, stem + "-elite" + System.Array.IndexOf(elites, eh) + "-after.png");

        SetOutlines(e.Actor.Hearts, false);
        foreach (var eh in elites) SetOutlines(eh, false);
        Grab(cam, rt, tex);
        Crop(cam, tex, bossAt, 2.6f, stem + "-boss-before.png");
        foreach (var eh in elites) Crop(cam, tex, eh.transform.position, .55f, stem + "-elite" + System.Array.IndexOf(elites, eh) + "-before.png");
        SetOutlines(e.Actor.Hearts, true);
        foreach (var eh in elites) SetOutlines(eh, true);

        // two hearts lost (phase 2), mid-crumble, then settled
        e.OnShipAttackHit(BossConfig.HeartWeight * 2f, bossAt + new Vector3(1.2f, -1.4f, 0f));
        for (int i = 0; i < 12; i++) e.Step(Dt, 1f);
        Grab(cam, rt, tex);
        Crop(cam, tex, e.Actor.transform.position, 2.6f, stem + "-boss-hit.png");
        for (int i = 0; i < 60; i++) e.Step(Dt, 1f);
        Grab(cam, rt, tex);
        File.WriteAllBytes(stem + "-3hearts-full.png", tex.EncodeToPNG());

        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        BossEncounter.ResetRun();
        EliteSystem.Clear();
        Debug.Log("[BOSSHEARTS-PREVIEW] " + stem);
    }

    static void SetOutlines(HeartOrbit h, bool on)
    {
        for (int i = 0; i < h.Hearts.Length; i++)
        {
            var r = h.HeartOutlineRenderer(i);
            if (r != null) r.enabled = on;
        }
    }

    static void Grab(Camera cam, RenderTexture rt, Texture2D tex)
    {
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        tex.Apply();
        RenderTexture.active = old;
    }

    // A square crop `half` world units round `at`, scaled up to at least 400 px.
    static void Crop(Camera cam, Texture2D src, Vector3 at, float half, string path)
    {
        Vector3 c = cam.WorldToScreenPoint(at);
        float ppu = Height / (cam.orthographicSize * 2f);
        int r = Mathf.RoundToInt(half * ppu);
        int x0 = Mathf.Clamp((int)c.x - r, 0, Width - 1), y0 = Mathf.Clamp((int)c.y - r, 0, Height - 1);
        int x1 = Mathf.Clamp((int)c.x + r, 0, Width), y1 = Mathf.Clamp((int)c.y + r, 0, Height);
        int w = x1 - x0, h = y1 - y0;
        if (w <= 0 || h <= 0) return;
        int k = Mathf.Max(1, Mathf.CeilToInt(400f / Mathf.Max(w, h)));
        var px = src.GetPixels(x0, y0, w, h);
        var outTex = new Texture2D(w * k, h * k, TextureFormat.RGB24, false);
        var big = new Color[w * k * h * k];
        for (int y = 0; y < h * k; y++)
            for (int x = 0; x < w * k; x++) big[y * w * k + x] = px[(y / k) * w + x / k];   // nearest: the pixels as they are
        outTex.SetPixels(big);
        outTex.Apply();
        File.WriteAllBytes(path, outTex.EncodeToPNG());
        Object.DestroyImmediate(outTex);
    }
}
