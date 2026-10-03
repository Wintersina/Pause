using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class DockPreview
{
    public static void Run()
    {
        foreach (var size in new[] { new Vector2Int(1100, 800), new Vector2Int(1080, 2400) })
        {
            EditorSceneLoader.Open("shopS6");
            ShopSceneExtender.Build();
            var shop = Object.FindFirstObjectByType<shopingShips>();
            shop.SendMessage("Start");
            foreach (var thruster in Object.FindObjectsByType<ShipThruster>(FindObjectsSortMode.None)) thruster.SendMessage("Start");
            foreach (var art in Object.FindObjectsByType<DockCardArt>(FindObjectsSortMode.None)) art.SendMessage("LateUpdate");
            foreach (var label in Object.FindObjectsByType<ShopButtonAligner>(FindObjectsSortMode.None)) label.SendMessage("LateUpdate");
            var camera = Camera.main;
            var target = new RenderTexture(size.x, size.y, 24);
            camera.targetTexture = target;
            camera.orthographicSize = CameraFit.ComputeSize(5, 2.85f, size.x, size.y);
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                var scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null) scaler.enabled = false;
                canvas.scaleFactor = Mathf.Sqrt((size.x / 720f) * (size.y / 960f));
            }
            Canvas.ForceUpdateCanvases();
            Object.FindFirstObjectByType<DockScrollView>().RefreshLayout();
            Canvas.ForceUpdateCanvases();
            camera.Render();
            var old = RenderTexture.active;
            RenderTexture.active = target;
            var png = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            png.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
            png.Apply();
            File.WriteAllBytes("/private/tmp/pause-dock-" + size.x + "x" + size.y + ".png", png.EncodeToPNG());
            RenderTexture.active = old;
            camera.targetTexture = null;
            Object.DestroyImmediate(png);
            Object.DestroyImmediate(target);
        }
        EditorApplication.Exit(0);
    }
}
