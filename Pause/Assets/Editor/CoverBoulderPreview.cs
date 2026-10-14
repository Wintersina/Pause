using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders the real runtime cover boulder / landform over a world's backdrop layers, frame by frame, for the PR previews (docs/cover-boulders.md section 13):
//
//   Unity -batchmode -projectPath <abs>/Pause -executeMethod CoverBoulderPreview.Run -world Storm -out /tmp/bd/stormfx
//
// Writes <out>/v<N>_<frame>.png for every backdrop variant N of the world (30 fps, 5 s: the emerge, the drift, the effects).  Scripts compose the GIF.
public static class CoverBoulderPreview
{
    public static void Run()
    {
        string world = Arg("-world", "Storm"), outDir = Arg("-out", "/tmp/bd/boulderfx"), variants = Arg("-variants", "1,2,3,4");
        int shape = int.Parse(Arg("-shape", "1"));
        float seconds = float.Parse(Arg("-seconds", "5.5"));
        Directory.CreateDirectory(outDir);
        int w = -1;
        for (int i = 0; i < CoverBoulderTable.Entries.Length; i++) if (CoverBoulderTable.Entries[i].world == world) w = i;
        foreach (string vs in variants.Split(','))
        {
            int v = int.Parse(vs);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CoverBoulders.Dispose();
            var camGo = new GameObject("Main Camera", typeof(Camera));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = 6.6f; cam.aspect = 9f / 16f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            int order = -30;
            foreach (string layer in new[] { "sky", "far", "mid", "flow" })
            {
                var tex = Resources.Load<Texture2D>("Worlds/" + world + "/Backdrop3/v" + v + "/" + layer);
                if (tex == null) continue;
                var go = new GameObject(layer);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
                sr.sortingOrder = order++;
                float k = 7.43f / (tex.width / 100f);
                go.transform.localScale = new Vector3(k, k, 1f);
            }
            buttonClicks.playerDied = false; startMenu.youAreInTutorial = false; score.pauseCounter = 0; Time.timeScale = 1f;
            CoverBoulders.Disabled = false; CoverBoulders.TestAnyWorld = true;
            Random.InitState(77);
            var b = CoverBoulders.Spawn(w, shape);
            b.Pos = new Vector2(.3f, CameraFit.ViewTop + 1.5f);
            b.Vel = new Vector2(0f, -1.1f);
            var rt = new RenderTexture(540, 960, 24, RenderTextureFormat.ARGB32);
            var tx = new Texture2D(540, 960, TextureFormat.RGB24, false);
            int frames = Mathf.RoundToInt(seconds * 30f);
            for (int f = 0; f < frames; f++)
            {
                for (int s = 0; s < 2; s++) CoverBoulders.Step(1f / 60f);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tx.ReadPixels(new Rect(0, 0, 540, 960), 0, 0);
                tx.Apply();
                File.WriteAllBytes(Path.Combine(outDir, "v" + v + "_" + f.ToString("000") + ".png"), tx.EncodeToPNG());
                RenderTexture.active = null;
                if (f == Mathf.RoundToInt(frames * .8f) && Arg("-boss", "0") == "1") CoverBoulders.OnBossBegin();
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tx);
            CoverBoulders.Dispose();
        }
        CoverBoulders.TestAnyWorld = false;
        EditorApplication.Exit(0);
    }

    static string Arg(string name, string fallback)
    {
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return fallback;
    }
}
