using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Renders the space dock to PNGs without a build, for quick visual checks:
//
//   Unity -batchmode -projectPath Pause -executeMethod DockPreview.Run
//
// Writes /private/tmp/pause-dock-<w>x<h>[-selected].png: the idle dock and
// the dock with Paranoid selected (powered up, popup open).
public static class DockPreview
{
    public static void Run()
    {
        foreach (var size in new[] { new Vector2Int(1080, 1920), new Vector2Int(1080, 2400), new Vector2Int(1100, 800) })
            foreach (int selected in new[] { 0, 10 })
                Render(size, selected);
        EditorApplication.Exit(0);
    }

    static void Render(Vector2Int size, int selected)
    {
        EditorSceneLoader.Open("shopS6");
        ShopSceneExtender.Build();
        var shop = Object.FindFirstObjectByType<shopingShips>();
        shop.SendMessage("Start");
        var camera = Camera.main;
        var target = new RenderTexture(size.x, size.y, 24);
        camera.targetTexture = target;
        camera.aspect = size.x / (float)size.y;
        camera.orthographicSize = CameraFit.ComputeSize(5, 2.85f, size.x, size.y);
        foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (canvas.renderMode == RenderMode.WorldSpace) continue;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null) scaler.enabled = false;
            canvas.scaleFactor = Mathf.Sqrt((size.x / 720f) * (size.y / 960f));
        }
        Canvas.ForceUpdateCanvases();
        var dock = SpaceDock.Instance;
        dock.Relayout();
        foreach (var thruster in Object.FindObjectsByType<ShipThruster>(FindObjectsSortMode.None)) thruster.SendMessage("Start");
        if (selected > 0)
        {
            dock.Select(selected);
            dock.bays[selected].SnapPower();
            dock.popup.SkipAppear();
            dock.popup.SendMessage("LateUpdate");
        }
        for (int k = 0; k < 20; k++)
            foreach (var thruster in Object.FindObjectsByType<ShipThruster>(FindObjectsSortMode.None)) thruster.SendMessage("LateUpdate");
        Canvas.ForceUpdateCanvases();
        camera.Render();
        var old = RenderTexture.active;
        RenderTexture.active = target;
        var png = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
        png.Apply();
        File.WriteAllBytes("/private/tmp/pause-dock-" + size.x + "x" + size.y + (selected > 0 ? "-selected" : "") + ".png", png.EncodeToPNG());
        RenderTexture.active = old;
        camera.targetTexture = null;
        Object.DestroyImmediate(png);
        Object.DestroyImmediate(target);
    }
}
