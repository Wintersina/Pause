using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders a world's animated backdrop (WorldBackdrop) as numbered PNG frames
// for review, in an empty scene through a phone-shaped orthographic camera.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod SpaceBackdropPreview.Run
//   $BACKDROP_PREVIEW_DIR  output folder (default Builds/BackdropPreview)
//   $BACKDROP_WORLD        world display name (default Space)
//   $BACKDROP_SECONDS      length (default 10), captured at 15 fps
public static class SpaceBackdropPreview
{
    const float Dt = 1f / 60f;
    const int Px = 360, Py = 780;

    public static void Run()
    {
        string dir = Env("BACKDROP_PREVIEW_DIR", "Builds/BackdropPreview");
        string world = Env("BACKDROP_WORLD", "Space");
        float seconds = float.Parse(Env("BACKDROP_SECONDS", "10"), System.Globalization.CultureInfo.InvariantCulture);
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(dir, "*.png")) File.Delete(f);

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        float savedSpeed = moveBackGround.speed;
        var camGo = new GameObject("~PreviewCam", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 6.2f;
        cam.aspect = Px / (float)Py;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        var rt = new RenderTexture(Px, Py, 24);
        cam.targetTexture = rt;

        var go = new GameObject("~WorldBackdrop");
        var wb = go.AddComponent<WorldBackdrop>();
        try
        {
            Time.timeScale = 1f;
            moveBackGround.speed = 0.15f;
            wb.Show(world, false);
            int n = 0, steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                // A run speeds up a little over the clip.
                moveBackGround.speed = Mathf.Lerp(0.15f, 0.35f, i / (float)steps);
                wb.Step(Dt);
                if (i % 4 == 0) Shoot(cam, rt, Path.Combine(dir, "f" + (n++).ToString("0000") + ".png"));
            }
            Debug.Log("[BGPREVIEW] " + world + " frames " + n + " -> " + dir);
        }
        finally
        {
            moveBackGround.speed = savedSpeed;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(camGo);
        }
        EditorApplication.Exit(0);
    }

    static string Env(string key, string fallback)
    {
        string v = System.Environment.GetEnvironmentVariable(key);
        return string.IsNullOrEmpty(v) ? fallback : v;
    }

    static void Shoot(Camera cam, RenderTexture rt, string path)
    {
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var png = new Texture2D(Px, Py, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, Px, Py), 0, 0);
        png.Apply();
        File.WriteAllBytes(path, png.EncodeToPNG());
        RenderTexture.active = old;
        Object.DestroyImmediate(png);
    }
}
