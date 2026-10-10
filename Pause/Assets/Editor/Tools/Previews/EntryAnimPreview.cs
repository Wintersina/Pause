using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders a run's ENTRY (WorldEntry) in gameS1 as the game camera sees it on
// a phone, for review: the real run start (WorldManager.Start with a ship in
// the scene), stepped at 60 fps, a frame every 0.3 s of the entry and a
// little after:
//
//   ENTRY_KIND=space      Space: the portal arrival (default)
//   ENTRY_KIND=frost      Frost: its planetfall as the run's entry
//   ENTRY_KIND=verdant | ember | tide   likewise
//   ENTRY_KIND=tutorial   the first run after the tutorial (the prefs the
//                         tutorial leaves, TutorialSkip.FinishBySkipping): the
//                         same portal arrival into Space
//
//   ENTRY_PREVIEW_DIR=<dir> Unity -batchmode -quit -projectPath Pause
//       -executeMethod EntryAnimPreview.Run
//   (ENTRY_DEVICE=flip7-1080x2520 picks the screen. The UI is not drawn: it is
//    a screen overlay.)
public static class EntryAnimPreview
{
    const float Dt = 1f / 60f;

    public static void Run()
    {
        string kind = (System.Environment.GetEnvironmentVariable("ENTRY_KIND") ?? "space").ToLowerInvariant();
        string dir = System.Environment.GetEnvironmentVariable("ENTRY_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/EntryPreview";
        string device = System.Environment.GetEnvironmentVariable("ENTRY_DEVICE");
        if (string.IsNullOrEmpty(device)) device = "flip7-1080x2520";
        int failures = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                var d = FitDevice.Find(device);
                if (d == null) throw new System.Exception("no device " + device);
                Directory.CreateDirectory(dir);
                Render(d, dir, kind);
            }
            catch (System.Exception e) { Debug.LogException(e); failures++; }
            finally
            {
                BossEncounter.ResetRun();
                PortalPressure.Reset();
                BossRails.Reset();
                PlayField.Reset();
                ScreenInfo.ClearOverride();
                buttonClicks.playerDied = false;
                WorldManager.TideEnabled = false;
            }
        }
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    static void Render(FitDevice d, string dir, string kind)
    {
        int world = System.Array.IndexOf(new[] { "space", "frost", "verdant", "ember", "tide" }, kind);
        if (kind == "tutorial") world = 0;
        if (world < 0) throw new System.Exception("unknown ENTRY_KIND " + kind);
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity");
        BossEncounter.ResetRun();
        BossRails.Reset();
        PortalPressure.Reset();
        RunLoop.Reset();
        WorldManager.ClearReplayWorld();
        var cam = Camera.main;
        cam.aspect = d.Aspect;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, d.w, d.h);
        ScreenInfo.ClearOverride();
        ScreenInfo.Override(d.w, d.h, d.Safe, d.Cutouts, d.ReportedDpi, d.ios);
        PlayField.Reset();
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        score.pauseCounter = 0;
        Time.timeScale = 1f;
        PlayerPrefs.DeleteAll();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        if (kind == "tutorial") TutorialSkip.FinishBySkipping();
        else PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, world);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        WorldManager.TideEnabled = world >= 4;
        Random.InitState(7);

        var paused = SceneUtil.FindAny("paused");
        if (paused != null) paused.SetActive(false);
        foreach (var name in new[] { "leftPipe", "rightPipe" })
        {
            var wall = GameObject.Find(name);
            if (wall == null) continue;
            var s = wall.transform.localScale;
            s.y = cam.orthographicSize * 2f * 1.085f / wall.GetComponent<MeshFilter>().sharedMesh.bounds.size.y;   // RailFit
            wall.transform.localScale = s;
            RailFit.RefreshTextureTiling(wall);
        }
        BossRails.Measure();

        var ship = new GameObject("~PfSpawner").AddComponent<spawnShips>().Spawn(ShipId.Starter);
        ship.transform.position = new Vector3(-1.1f, ShipReach.StartY, 0f);
        if (ship.GetComponentInChildren<movePlayer>() == null && ship.GetComponentInParent<movePlayer>() == null) ship.AddComponent<movePlayer>();

        // the run start, in the order Unity runs it
        var wm = new GameObject("~WorldManager").AddComponent<WorldManager>();
        wm.SendMessage("Awake");
        wm.HoldStartWorld();
        typeof(WorldManager).GetMethod("Start", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(wm, null);
        var wb = WorldBackdrop.Instance;
        for (int i = 0; i < 180; i++) wb.Step(Dt);   // the sky has been scrolling since the scene loaded
        Debug.Log("[ENTRY-PREVIEW] " + kind + ": entry " + wm.EntryPlayed + " on " + WorldManager.Current.displayName);

        int frame = 0, shot = 0;
        float clock = 0f, endAt = -1f;
        while (clock < 12f)
        {
            if (WorldManager.Flying) wm.Tick(Dt);
            var pf = Planetfall.Live;
            if (pf != null && pf.State != Planetfall.Stage.Done) pf.Step(Dt);
            var pa = PortalArrival.Live;
            if (pa != null && pa.State != PortalArrival.Stage.Done) pa.Step(Dt);
            wb.Step(Dt);
            clock += Dt;
            if (endAt < 0f && !WorldEntry.Active) { endAt = clock; Debug.Log("[ENTRY-PREVIEW] entry over at " + clock.ToString("F2") + " s"); }
            if (frame++ % 18 == 0)
                Capture(cam, d.w / 2, d.h / 2, Path.Combine(dir, kind + "_" + shot++.ToString("00") + "_" + clock.ToString("F1") + "s.png"));
            if (endAt >= 0f && clock > endAt + .8f) break;
        }
        Debug.Log("[ENTRY-PREVIEW] " + kind + ": " + shot + " frames, ship " + ship.transform.position + " scale " + ship.transform.localScale + " -> " + dir);
        if (wm != null) Object.DestroyImmediate(wm.gameObject);
    }

    static void Capture(Camera cam, int width, int height, string file)
    {
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var png = new Texture2D(width, height, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        png.Apply();
        File.WriteAllBytes(file, png.EncodeToPNG());
        RenderTexture.active = old;
        cam.targetTexture = previous;
        Object.DestroyImmediate(png);
        Object.DestroyImmediate(rt);
    }
}
