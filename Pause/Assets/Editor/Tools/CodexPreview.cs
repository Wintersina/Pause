using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Codex contact sheets without a build, for reviewing the whole catalogue:
//
//   Unity -batchmode -projectPath <abs>/Pause -executeMethod CodexPreview.Run
//
// Writes into $PAUSE_CODEX_PREVIEW_DIR (default /private/tmp):
//   codex-<tab>-<state>.png         the tab's whole scrolling list in one
//                                   tall image (the panel laid out tall
//                                   enough to hold every card)
//   codex-detail-<tab>-<state>.png  every entry's detail view of that tab,
//                                   as a grid of small renders
// for <state> locked (a fresh profile: only the Pilot's Log and the starter
// ship, bosses unlisted) and unlocked (developer mode: everything, bosses
// included). PlayerPrefs are restored afterwards (TestHarness.Sandbox).
public static class CodexPreview
{
    const int Width = 800;
    const int DetailHeight = 1280;
    const int TileColumns = 6;
    const int TileScale = 2;   // detail tiles at 1/2 size
    static readonly Color Backdrop = new Color(.03f, .03f, .06f, 1f);

    public static void Run()
    {
        string dir = Environment.GetEnvironmentVariable("PAUSE_CODEX_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "/private/tmp";
        Directory.CreateDirectory(dir);
        int code = 0;
        try
        {
            using (new TestHarness.Sandbox())
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                foreach (bool unlocked in new[] { false, true })
                {
                    DeveloperUnlocks.SetEnabled(unlocked);
                    if (!unlocked)
                    {
                        PlayerPrefs.DeleteKey(Codex.PrefsKey);
                        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);
                        for (int i = 0; i <= shopingShips.shipTotal; i++) PlayerPrefs.DeleteKey(ShipId.OwnedKey(i));
                    }
                    Codex.Reload();
                    var panel = CodexPanel.Open(null);
                    panel.Refresh();
                    panel.SkipAnimations();
                    string state = unlocked ? "unlocked" : "locked";
                    foreach (var tab in CodexPanel.Tabs)
                    {
                        string label = CodexPanel.CategoryLabel(tab).ToLowerInvariant();
                        TabSheet(panel, tab, Path.Combine(dir, "codex-" + label + "-" + state + ".png"));
                        DetailSheet(panel, tab, Path.Combine(dir, "codex-detail-" + label + "-" + state + ".png"));
                    }
                    panel.Close();
                    panel.SkipAnimations();
                }
                DeveloperUnlocks.SetEnabled(false);
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    // The whole list of a tab in one image: the panel laid out just tall
    // enough for every card row to sit inside the list unscrolled.
    static void TabSheet(CodexPanel panel, CodexCategory tab, string path)
    {
        panel.ShowGrid();
        panel.ApplyLayout(new Rect(-Width * .5f, -DetailHeight * .5f, Width, DetailHeight));
        panel.ShowCategory(tab);
        panel.SkipAnimations();
        var l = panel.CurrentLayout;
        float list = panel.Sectioned ? l.list.height : l.body.height;
        int height = Mathf.Clamp(Mathf.CeilToInt(DetailHeight - list + panel.Content.sizeDelta.y + 8f), DetailHeight, 8000);
        var tex = Capture(panel, Width, height, () =>
        {
            panel.ShowCategory(tab);
            panel.SetScrollY(0f);
            panel.SkipAnimations();
        });
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        Debug.Log("[CODEX-PREVIEW] " + path + " (" + panel.VisibleCards + " cards)");
    }

    // Every entry's detail view in a grid of half-size tiles.
    static void DetailSheet(CodexPanel panel, CodexCategory tab, string path)
    {
        panel.ShowGrid();
        panel.ShowCategory(tab);
        panel.SkipAnimations();
        int n = panel.VisibleCards;
        if (n == 0) return;
        var entries = new CodexEntry[n];
        for (int i = 0; i < n; i++) entries[i] = panel.CardEntry(i);

        int tw = Width / TileScale, th = DetailHeight / TileScale;
        int cols = Mathf.Min(TileColumns, n), rows = (n + cols - 1) / cols;
        var sheet = new Texture2D(cols * tw, rows * th, TextureFormat.RGBA32, false);
        var fill = new Color[sheet.width * sheet.height];
        for (int i = 0; i < fill.Length; i++) fill[i] = Backdrop;
        sheet.SetPixels(fill);

        for (int i = 0; i < n; i++)
        {
            var e = entries[i];
            var tile = Capture(panel, Width, DetailHeight, () =>
            {
                panel.ShowDetail(e);
                panel.SkipAnimations();
                // a beat into its loop, so the art isn't always on frame 0
                for (int k = 0; k < 12; k++) panel.TickAnimations(1f / 30f);
            }, TileScale);
            int col = i % cols, row = i / cols;
            sheet.SetPixels(col * tw, (rows - 1 - row) * th, tw, th, tile.GetPixels());
            UnityEngine.Object.DestroyImmediate(tile);
            panel.ShowGrid();
            panel.SkipAnimations();
        }
        sheet.Apply();
        File.WriteAllBytes(path, sheet.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(sheet);
        Debug.Log("[CODEX-PREVIEW] " + path + " (" + n + " entries)");
    }

    // Renders the panel's canvas at w x h canvas units (1 unit = 1 px),
    // laid out for that whole area, after `setup`; downscaled by `scale`.
    static Texture2D Capture(CodexPanel panel, int w, int h, Action setup, int scale = 1)
    {
        var canvas = panel.GetComponent<Canvas>();
        var scaler = panel.GetComponent<CanvasScaler>();
        bool scalerOn = scaler != null && scaler.enabled;
        if (scaler != null) scaler.enabled = false;
        var mode = canvas.renderMode;
        var oldCam = canvas.worldCamera;
        float plane = canvas.planeDistance;
        float factor = canvas.scaleFactor;

        var camGo = new GameObject("~CodexPreviewCam");
        camGo.transform.position = new Vector3(9000f, 9000f, -10f);
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Backdrop;
        cam.enabled = false;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 1f;
        canvas.scaleFactor = 1f;
        Canvas.ForceUpdateCanvases();
        panel.ApplyLayout(new Rect(-w * .5f, -h * .5f, w, h));
        setup();
        Canvas.ForceUpdateCanvases();
        cam.Render();

        int ow = w / scale, oh = h / scale;
        var src = rt;
        RenderTexture small = null;
        if (scale > 1)
        {
            small = new RenderTexture(ow, oh, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(rt, small);
            src = small;
        }
        var prev = RenderTexture.active;
        RenderTexture.active = src;
        var tex = new Texture2D(ow, oh, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, ow, oh), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        canvas.renderMode = mode;
        canvas.worldCamera = oldCam;
        canvas.planeDistance = plane;
        canvas.scaleFactor = factor;
        if (scaler != null) scaler.enabled = scalerOn;
        cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(rt);
        if (small != null) UnityEngine.Object.DestroyImmediate(small);
        UnityEngine.Object.DestroyImmediate(camGo);
        Canvas.ForceUpdateCanvases();
        return tex;
    }
}
