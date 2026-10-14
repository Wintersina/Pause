using System.IO;
using UnityEngine;

// Renders the PLACEHOLDER look of every world hazard (warning frame + live frame) and each world's ambient weather
// into one contact sheet, for the PR: docs/world-weather-hazards-preview.png.
//   Unity -batchmode -quit -projectPath Pause -executeMethod WorldHazardsPreview.Run
public static class WorldHazardsPreview
{
    const int W = 270, H = 540;

    // Every approved hazard (Storm tornado + gust, Tide whirlpool + surge) composited over EVERY variant of its world's backdrop, at in-game scale,
    // with a few bullet-like dots behind the body: /tmp/hazard_backdrops.png (columns = variants, rows = hazards).
    public static void RunBackdrops()
    {
        const int PW = 270, PH = 540;
        var camGo = new GameObject("cam", typeof(Camera)); camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 6.6f; cam.aspect = PW / (float)PH;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        camGo.transform.position = new Vector3(0, 0, -10);
        var rt = new RenderTexture(PW, PH, 24); cam.targetTexture = rt;
        var mgrGo = new GameObject("m"); var m = mgrGo.AddComponent<WorldHazards>();
        var ply = new GameObject("p"); ply.transform.position = new Vector3(0, -4f); EliteSystem.PlayerOverride = ply.transform;
        string[] ids = { "tornado", "gust_front", "whirlpool", "current_surge" };
        int[] worlds = { 5, 5, 4, 4 };
        var sheet = new Texture2D(PW * 4, PH * ids.Length, TextureFormat.RGB24, false);
        var read = new Texture2D(PW, PH, TextureFormat.RGB24, false);
        for (int h = 0; h < ids.Length; h++)
            for (int v = 1; v <= 4; v++)
            {
                var root = new GameObject("root").transform;
                var set = new BackdropSet(WorldManager.Worlds[worlds[h]].displayName, root, 3.3f, 6.6f, new BackdropSet.MenuOptions { variant = v, dim = 1f, withDirector = false });
                for (int i = 0; i < 100; i++) set.Tick(.05f, 1f);
                WorldHazards.ClearAll();
                HazardDef def = null;
                foreach (var e in WorldWeatherTable.Entries) foreach (var d in e.hazards) if (d.id == ids[h]) def = d;
                m.Begin(def);
                var f = WorldHazards.Fields[0]; f.side = 1f;
                if (def.shape == HazardShape.Swirl) { f.centre = new Vector2(0f, 1.5f); f.vel = Vector2.zero; }
                for (int i = 0; i < (int)((def.warning + (def.shape == HazardShape.Front ? 1.6f : 2.5f)) * 60f); i++) { m.StepFields(1f / 60f); if (def.shape == HazardShape.Swirl) f.centre = new Vector2(0f, 1.5f); }
                // bullet-like dots BEHIND the body (white-violet, a few around the hazard)
                var dots = new System.Collections.Generic.List<GameObject>();
                for (int k = 0; k < 9; k++)
                {
                    var g = new GameObject("dot", typeof(SpriteRenderer)); var r = g.GetComponent<SpriteRenderer>();
                    r.sprite = WeatherSprites.Get(AmbientShape.Dot); r.sortingOrder = -9; r.color = new Color(.95f, .95f, 1f, 1f);
                    float ang = k * 0.7f; float rad = .5f + k * .22f; Vector2 c = def.shape == HazardShape.Swirl ? f.centre : new Vector2(0f, f.frontY);
                    g.transform.position = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad; g.transform.localScale = Vector3.one * .22f; dots.Add(g);
                }
                m.Visuals.Draw(WorldHazards.Fields, true);
                cam.Render();
                RenderTexture.active = rt; read.ReadPixels(new Rect(0, 0, PW, PH), 0, 0); RenderTexture.active = null;
                sheet.SetPixels((v - 1) * PW, (ids.Length - 1 - h) * PH, PW, PH, read.GetPixels());
                foreach (var g in dots) Object.DestroyImmediate(g);
                WorldHazards.ClearAll(); m.Visuals.Draw(WorldHazards.Fields, true);
                set.Destroy(); Object.DestroyImmediate(root.gameObject);
            }
        sheet.Apply();
        File.WriteAllBytes("/tmp/hazard_backdrops.png", sheet.EncodeToPNG());
        Debug.Log("[PREVIEW] wrote /tmp/hazard_backdrops.png");
    }

    // The Storm tornado, frame by frame (the 2 s warning, then 2 s live) into /tmp/tornado_frames/f####.png at 15 fps.
    public static void RunTornado()
    {
        const int PW = 360, PH = 540;
        var camGo = new GameObject("cam", typeof(Camera)); camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 4.2f; cam.aspect = PW / (float)PH;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.04f, .04f, .1f);
        camGo.transform.position = new Vector3(0, 0, -10);
        var rt = new RenderTexture(PW, PH, 24); cam.targetTexture = rt;
        var mgrGo = new GameObject("m"); var m = mgrGo.AddComponent<WorldHazards>();
        var ply = new GameObject("p"); ply.transform.position = new Vector3(0, -3f); EliteSystem.PlayerOverride = ply.transform;
        System.IO.Directory.CreateDirectory("/tmp/tornado_frames");
        var layer = new WeatherLayer();
        WorldHazards.ClearAll();
        foreach (var e in WorldWeatherTable.Entries) foreach (var h in e.hazards) if (h.id == "tornado") m.Begin(h);
        var f = WorldHazards.Fields[0]; f.centre = new Vector2(0f, 0f); f.side = 1f; f.vel = Vector2.zero;
        var read = new Texture2D(PW, PH, TextureFormat.RGB24, false);
        for (int i = 0; i < 60; i++)
        {
            for (int k = 0; k < 4; k++) { m.StepFields(1f / 60f); WorldHazards.Apply(1f / 60f); layer.Step(1f / 60f, true, 5, WorldHazards.Fields); }
            f.centre = new Vector2(0f, 0f);
            m.Visuals.Draw(WorldHazards.Fields, true);
            cam.Render();
            RenderTexture.active = rt; read.ReadPixels(new Rect(0, 0, PW, PH), 0, 0); RenderTexture.active = null;
            File.WriteAllBytes(string.Format("/tmp/tornado_frames/f{0:0000}.png", i), read.EncodeToPNG());
        }
        Debug.Log("[PREVIEW] wrote tornado frames");
    }

    public static void Run()
    {
        var camGo = new GameObject("cam", typeof(Camera)); camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 6f; cam.aspect = W / (float)H;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.04f, .04f, .1f);
        camGo.transform.position = new Vector3(0, 0, -10);
        var rt = new RenderTexture(W, H, 24); cam.targetTexture = rt;
        var mgrGo = new GameObject("m"); var m = mgrGo.AddComponent<WorldHazards>();
        var ply = new GameObject("p"); ply.transform.position = new Vector3(0, -4f); EliteSystem.PlayerOverride = ply.transform;
        int hz = 0; foreach (var e in WorldWeatherTable.Entries) hz += e.hazards.Length;
        int cols = 7, rows = 5;   // per world: (warning, live) x 2 hazards, ambient only
        var sheet = new Texture2D(cols * W, rows * H, TextureFormat.RGB24, false);
        var read = new Texture2D(W, H, TextureFormat.RGB24, false);
        for (int w = 0; w < WorldWeatherTable.Entries.Length; w++)
        {
            var e = WorldWeatherTable.Entries[w];
            for (int k = 0; k < 5; k++)
            {
                WorldHazards.ClearAll();
                var layer = new WeatherLayer();
                if (k < 4 && k / 2 < e.hazards.Length)
                {
                    var d = e.hazards[k / 2];
                    m.Begin(d);
                    float until = (k % 2 == 0 ? d.warning * .6f : d.warning + (d.shape == HazardShape.Front ? 2f : 4f));
                    for (float t = 0; t < until; t += 1f / 30f) { m.StepFields(1f / 30f); WorldHazards.Apply(1f / 30f); layer.Step(1f / 30f, true, w, WorldHazards.Fields); }
                }
                else for (int i = 0; i < 300; i++) layer.Step(1f / 30f, true, w, WorldHazards.Fields);
                m.Visuals.Draw(WorldHazards.Fields, true);
                cam.Render();
                RenderTexture.active = rt; read.ReadPixels(new Rect(0, 0, W, H), 0, 0); RenderTexture.active = null;
                int col = w;   // columns = worlds; rows: A warning, A live, B warning, B live, ambient
                sheet.SetPixels(col * W, (rows - 1 - (k)) * H, W, H, read.GetPixels());
                layer.Destroy();
            }
        }
        sheet.Apply();
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/world-weather-hazards-preview.png"));
        File.WriteAllBytes(path, sheet.EncodeToPNG());
        Debug.Log("[PREVIEW] wrote " + path);
        Object.DestroyImmediate(camGo); Object.DestroyImmediate(mgrGo); Object.DestroyImmediate(ply);
        EliteSystem.PlayerOverride = null;
    }
}
