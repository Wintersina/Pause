using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Renders ~15 s of the home screen (startS4) for review: the traffic with an
// ultimate going off and bringing a ship down into the wall, a ship diving
// into the PAUSE logo, one into a menu button, and a finger swiping ships
// around (drawn as a ring). Phone-shaped (1080x2340); the menu canvas is
// drawn in the camera for the capture only (the scene is not saved).
//
//   home/NNN.png   20 fps frames for a GIF
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod HomeTrafficPreview.Run
//   (writes to $HOME_PREVIEW_DIR, else Builds/HomeTrafficPreview)
public static class HomeTrafficPreview
{
    const float Fps = 20f;
    const float Seconds = 15f;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("HOME_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/HomeTrafficPreview";
        string frames = Path.Combine(dir, "home");
        if (Directory.Exists(frames)) Directory.Delete(frames, true);
        Directory.CreateDirectory(frames);
        using (var sandbox = new TestHarness.Sandbox())
        {
            Clip(frames);
        }
        EditorApplication.Exit(0);
    }

    static void Clip(string frames)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/startS4.unity", OpenSceneMode.Single);
        Random.InitState(2026);
        var cam = Camera.main;
        cam.aspect = 1080f / 2340f;
        cam.orthographicSize = CameraFit.ComputeSize(cam.orthographicSize, 2.85f, 1080, 2340);
        float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;

        // the overlay menu, placed in the camera's view for the capture
        MenuStyler.StyleScene();
        foreach (var cb in Object.FindObjectsByType<CodexHomeButton>(FindObjectsSortMode.None)) cb.Build();
        var panel = GameObject.Find("UIPanel");
        var canvas = panel.GetComponentInParent<Canvas>().rootCanvas;
        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler != null) scaler.enabled = false;
        canvas.renderMode = RenderMode.WorldSpace;
        cam.cullingMask |= 1 << canvas.gameObject.layer;
        canvas.sortingOrder = 1000;
        var crt = (RectTransform)canvas.transform;
        float w = 800f, h = w * 2340f / 1080f;
        crt.sizeDelta = new Vector2(w, h);
        float k = 2f * halfH / h;
        crt.localScale = new Vector3(k, k, 1f);
        crt.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)panel.transform);
        Canvas.ForceUpdateCanvases();

        var go = new GameObject("~TitleScreenTraffic");
        var t = go.AddComponent<TitleScreenTraffic>();
        t.plungeInterval = new Vector2(1e6f, 1e6f);   // the clip times its own dives
        t.Init();

        // the finger
        var finger = new GameObject("~finger", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
        finger.sprite = WeaponArt.ExplosionRing();
        finger.sortingOrder = 999;
        finger.color = new Color(1f, 1f, 1f, .55f);
        finger.enabled = false;

        float dt = 1f / Fps;
        // warm: the wardrobe decoded, a populated sky
        for (int i = 0; i < 30 * 14; i++) t.Step(1f / 30f);

        int frame = 0;
        bool ult = false, logoDive = false, buttonDive = false, fingerOn = false;
        Vector2 fingerAt = Vector2.zero;
        for (float s = 0f; s < Seconds; s += dt)
        {
            if (!ult && s >= .8f) ult = ForceUltimate(t);
            if (!logoDive && s >= 5.5f) logoDive = Dive(t, -1);
            if (!buttonDive && s >= 9f) buttonDive = Dive(t, 1);

            // 11.5-14.5 s: a finger chases ships across the sky, shoving
            // them (it never starts on a button)
            bool down = s >= 11.5f && s < 14.5f;
            if (down)
            {
                if (!fingerOn) { fingerAt = new Vector2(-halfW * .7f, cam.transform.position.y - halfH * .45f); fingerOn = true; }
                TitleScreenTraffic.Flyer near = null;
                float best = float.MaxValue;
                foreach (var f in t.Pool)
                {
                    if (!f.active || f.layer == TitleScreenTraffic.Depth.Back || f.state != TitleScreenTraffic.State.Cruise) continue;
                    if (t.MenuRect.width > 0f && t.MenuRect.Contains(f.pos)) continue;
                    float d = (f.pos - fingerAt).sqrMagnitude;
                    if (d < best) { best = d; near = f; }
                }
                Vector2 goal = near != null ? near.pos - (near.pos - fingerAt).normalized * .45f : fingerAt;
                fingerAt = Vector2.MoveTowards(fingerAt, goal, 5.5f * dt);
            }
            else fingerOn = false;
            Vector2 world = fingerAt;
            Vector2 screen = new Vector2((world.x - t.View.x) / t.View.width * Screen.width,
                                         (world.y - t.View.y) / t.View.height * Screen.height);
            t.FeedPointer(0, 0, down, screen, dt);
            finger.enabled = down;
            finger.transform.position = new Vector3(world.x, world.y, 0f);
            finger.transform.localScale = Vector3.one * .9f;

            int logo0 = t.LogoCrashes, button0 = t.ButtonCrashes, down0 = t.ShootDowns, ult0 = t.UltsFired;
            t.Step(dt);
            if (t.LogoCrashes != logo0) Debug.Log("[HP] logo crash at frame " + frame + " logo " + t.LogoRect + " letters " + t.LogoLetters + " at " + t.LastImpactPoint + " cam " + cam.transform.position + " size " + cam.orthographicSize);
            if (t.ButtonCrashes != button0) Debug.Log("[HP] button crash at frame " + frame);
            if (t.ShootDowns != down0) Debug.Log("[HP] shoot-down at frame " + frame);
            if (t.UltsFired != ult0) Debug.Log("[HP] ultimate at frame " + frame);
            foreach (var book in go.GetComponentsInChildren<ShipFlameFlipbook>()) book.Step(dt);
            Shoot(cam, Path.Combine(frames, (frame++).ToString("000") + ".png"));
        }
        Debug.Log("[HP] frames " + frame + ", ults " + t.UltsFired + ", shoot-downs " + t.ShootDowns + ", wall hits " +
                  t.WallImpacts + ", logo " + t.LogoCrashes + ", buttons " + t.ButtonCrashes + ", shoves " + t.Shoves);
        t.Shutdown();
        Object.DestroyImmediate(go);
    }

    // A ship with a mark on its layer lets its ultimate go.
    static bool ForceUltimate(TitleScreenTraffic t)
    {
        TitleScreenTraffic.Flyer best = null;
        float bestD = float.MaxValue;
        foreach (var a in t.Pool)
        {
            if (!a.active || a.layer == TitleScreenTraffic.Depth.Back || a.state != TitleScreenTraffic.State.Cruise) continue;
            if (!t.Safe.Contains(a.pos)) continue;
            foreach (var b in t.Pool)
            {
                if (b == a || !b.active || b.layer != a.layer || !t.View.Contains(b.pos)) continue;
                float d = (a.pos - b.pos).sqrMagnitude;
                if (d < bestD && d > .5f) { bestD = d; best = a; }
            }
        }
        if (best == null) return false;
        best.ultAt = t.Now;
        return true;
    }

    static bool Dive(TitleScreenTraffic t, int into)
    {
        foreach (var f in t.Pool)
        {
            if (!f.active || f.layer == TitleScreenTraffic.Depth.Back || f.state != TitleScreenTraffic.State.Cruise || f.ulting) continue;
            if (!t.Safe.Contains(f.pos)) continue;
            int target = into;
            if (into >= 0) target = Mathf.Min(into, t.ButtonCount - 1);
            if (t.StartPlunge(f, false, target)) return true;
        }
        return false;
    }

    static void Shoot(Camera cam, string path)
    {
        const int px = 360;
        int py = Mathf.RoundToInt(px / cam.aspect);
        var rt = new RenderTexture(px, py, 24);
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var png = new Texture2D(px, py, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, px, py), 0, 0);
        png.Apply();
        File.WriteAllBytes(path, png.EncodeToPNG());
        RenderTexture.active = old;
        cam.targetTexture = null;
        Object.DestroyImmediate(png);
        Object.DestroyImmediate(rt);
    }
}
