using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Filmstrip of each boss's real in-fight death (BossActor outro: strip cells + blasts)
// over its world backdrop: 8 frames, 2 rows of 4, one PNG per boss (bossdeath-fight-<key>.png).
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod BossDeathWiredPreview.Run
//   (writes to $BOSSDEATH_PREVIEW_DIR, else Builds/BossDeathWiredPreview)
public static class BossDeathWiredPreview
{
    const int Width = 1080, Height = 2340;
    const float Dt = 1f / 60f;
    const float Half = 3.0f;
    static readonly float[] At = { 0f, .12f, .24f, .36f, .48f, .6f, .72f, .9f };

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("BOSSDEATH_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/BossDeathWiredPreview";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        {
            try { for (int w = 0; w < BossCatalog.All.Length; w++) Render(w, dir); }
            finally { BossEncounter.ResetRun(); }
        }
        EditorApplication.Exit(0);
    }

    static void Render(int world, string dir)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun(); BossRails.Reset(); PlayField.Reset();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.aspect = Width / (float)Height;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, Width, Height);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false; score.pauseCounter = 0; Time.timeScale = 1f;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        var backdrop = new GameObject("~Backdrop").AddComponent<WorldBackdrop>();
        backdrop.Show(WorldManager.Worlds[world].displayName, false);
        for (int i = 0; i < 600; i++) backdrop.Step(Dt);

        BossEncounter.Begin(world, null);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);
        for (int i = 0; i < 20 && e.State == BossEncounter.Phase.Fight; i++) { e.OnShipAttackHit(1f); e.Step(Dt, 1f); }
        var actor = e.Actor;
        string key = actor.Boss.artKey;

        var rt = new RenderTexture(Width, Height, 24);
        cam.targetTexture = rt;
        var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        float ppu = Height / (cam.orthographicSize * 2f);
        int side = Mathf.RoundToInt(Half * 2f * ppu);
        var sheet = new Texture2D(side * 4, side * 2, TextureFormat.RGB24, false);
        float t0 = 0f;
        for (int t = 0; t < At.Length; t++)
        {
            while (t0 < At[t]) { e.Step(Dt, 1f); t0 += Dt; }
            cam.Render();
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            shot.Apply();
            RenderTexture.active = old;
            Vector3 c = cam.WorldToScreenPoint(actor.transform.position);
            int x0 = Mathf.Clamp((int)c.x - side / 2, 0, Width - side);
            int y0 = Mathf.Clamp((int)c.y - side / 2, 0, Height - side);
            sheet.SetPixels((t % 4) * side, (1 - t / 4) * side, side, side, shot.GetPixels(x0, y0, side, side));
            Debug.Log("[BOSSDEATH-WIRED] " + key + " t=" + At[t] + " state=" + actor.State + " sprite=" + (actor.GetComponentInChildren<SpriteRenderer>() != null ? actor.GetComponentInChildren<SpriteRenderer>().sprite.name : "-"));
        }
        sheet.Apply();
        string path = Path.Combine(dir, "bossdeath-fight-" + key.ToLowerInvariant() + ".png");
        File.WriteAllBytes(path, sheet.EncodeToPNG());
        cam.targetTexture = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(shot); Object.DestroyImmediate(sheet);
        Debug.Log("[BOSSDEATH-WIRED] " + path);
    }
}
