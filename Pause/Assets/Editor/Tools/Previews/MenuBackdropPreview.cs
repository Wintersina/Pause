using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Home-screen stills, one per unlocked world (and a second variant each): the
// real menu scene with MenuBackdrop behind the title traffic and the menu.
//
//   MENU_PREVIEW_DIR=<dir> Unity -batchmode -quit -projectPath Pause -executeMethod MenuBackdropPreview.Run
public static class MenuBackdropPreview
{
    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("MENU_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/MenuBackdropPreview";
        Directory.CreateDirectory(dir);
        using (var sandbox = new TestHarness.Sandbox())
        {
            PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, WorldManager.Worlds.Length - 1);
            EditorSceneManager.OpenScene("Assets/Scenes/startS4.unity", OpenSceneMode.Single);
            Random.InitState(2026);
            var cam = Camera.main;
            cam.aspect = 1080f / 2340f;
            cam.orthographicSize = CameraFit.ComputeSize(cam.orthographicSize, 2.85f, 1080, 2340);
            float halfH = cam.orthographicSize;

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

            var tgo = new GameObject("~TitleScreenTraffic");
            var traffic = tgo.AddComponent<TitleScreenTraffic>();
            traffic.plungeInterval = new Vector2(1e6f, 1e6f);
            traffic.Init();
            for (int i = 0; i < 30 * 12; i++) traffic.Step(1f / 30f);

            var bgo = new GameObject("~MenuBackdrop");
            var mb = bgo.AddComponent<MenuBackdrop>();
            var picks = new List<MenuBackdropSelection.Pick>();
            MenuBackdropSelection.Candidates(WorldManager.Worlds.Length - 1, picks);
            var shown = new Dictionary<string, int>();
            foreach (var p in picks)
            {
                int n;
                shown.TryGetValue(p.world, out n);
                if (n >= 2) continue;
                shown[p.world] = n + 1;
                if (!mb.Build(p)) continue;
                for (int i = 0; i < 30 * 6; i++)
                {
                    mb.Step(1f / 30f);
                    traffic.Step(1f / 30f);
                    foreach (var book in tgo.GetComponentsInChildren<ShipFlameFlipbook>()) book.Step(1f / 30f);
                }
                Shoot(cam, Path.Combine(dir, "home_" + p.world.ToLower() + (p.variant > 0 ? "_v" + p.variant : "") + ".png"));
            }
            traffic.Shutdown();
        }
        EditorApplication.Exit(0);
    }

    static void Shoot(Camera cam, string path)
    {
        const int px = 540;
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
