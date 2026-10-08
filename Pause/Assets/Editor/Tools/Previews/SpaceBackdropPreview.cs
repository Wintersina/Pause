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
//   $BACKDROP_PX / _PY     frame size in pixels (default 360 x 780)
//   $BACKDROP_SKIP         seconds simulated before the first frame (default 0)
// Space also writes stations.csv next to the frames: per frame, every
// visible station's centre and drawn size in frame pixels (top-left
// origin), for cropping close-ups of the stations and their lamps, whether
// it is an edge peeker or an in-frame station, and its live steam puffs.
// It also writes asteroids.csv: per frame, every drifting asteroid's centre,
// drawn size (frame px), heading, tumble, crack level and live puffs.
public static class SpaceBackdropPreview
{
    const float Dt = 1f / 60f;
    static int Px = 360, Py = 780;

    public static void Run()
    {
        string dir = Env("BACKDROP_PREVIEW_DIR", "Builds/BackdropPreview");
        string world = Env("BACKDROP_WORLD", "Space");
        float seconds = float.Parse(Env("BACKDROP_SECONDS", "10"), System.Globalization.CultureInfo.InvariantCulture);
        Px = int.Parse(Env("BACKDROP_PX", "360"));
        Py = int.Parse(Env("BACKDROP_PY", "780"));
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
            float skip = float.Parse(Env("BACKDROP_SKIP", "0"), System.Globalization.CultureInfo.InvariantCulture);
            for (int i = 0, k = Mathf.RoundToInt(skip / Dt); i < k; i++) wb.Step(Dt);
            int n = 0, steps = Mathf.RoundToInt(seconds / Dt);
            var track = new System.Text.StringBuilder("frame,station,x,y,size,cell,kind,puffs\n");
            var rocks = new System.Text.StringBuilder("frame,rock,x,y,size,heading,spin,tier,crack,puffs\n");
            for (int i = 0; i < steps; i++)
            {
                // A run speeds up a little over the clip.
                moveBackGround.speed = Mathf.Lerp(0.15f, 0.35f, i / (float)steps);
                wb.Step(Dt);
                if (i % 4 != 0) continue;
                Track(wb, cam, n, track);
                TrackAsteroids(wb, cam, n, rocks);
                Shoot(cam, rt, Path.Combine(dir, "f" + (n++).ToString("0000") + ".png"));
            }
            File.WriteAllText(Path.Combine(dir, "stations.csv"), track.ToString());
            File.WriteAllText(Path.Combine(dir, "asteroids.csv"), rocks.ToString());
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

    static void Track(WorldBackdrop wb, Camera cam, int frame, System.Text.StringBuilder into)
    {
        var sd = wb.Current != null ? wb.Current.Director as SpaceDirector : null;
        if (sd == null || sd.StationLights == null) return;
        foreach (var r in sd.StationLights.Rigs)
        {
            var p = r.piece;
            if (!p.active || r.cell == null) continue;
            Vector3 c = cam.WorldToScreenPoint(p.root.position);
            float px = p.size / (cam.orthographicSize * 2f) * Py;
            into.Append(frame).Append(',').Append(sd.StationLights.Rigs.IndexOf(r)).Append(',')
                .Append(Mathf.RoundToInt(c.x)).Append(',').Append(Mathf.RoundToInt(Py - c.y)).Append(',')
                .Append(Mathf.RoundToInt(px)).Append(',').Append(r.cell).Append(',')
                .Append(p.edge ? "peeker" : "inframe").Append(',').Append(LivePuffs(sd, p)).Append('\n');
        }
    }

    static void TrackAsteroids(WorldBackdrop wb, Camera cam, int frame, System.Text.StringBuilder into)
    {
        var sd = wb.Current != null ? wb.Current.Director as SpaceDirector : null;
        var drift = sd != null ? sd.AsteroidDrift : null;
        if (drift == null) return;
        for (int k = 0; k < drift.Rocks.Count; k++)
        {
            var r = drift.Rocks[k];
            if (!r.active) continue;
            Vector3 c = cam.WorldToScreenPoint(r.piece.root.position);
            float px = r.piece.size / (cam.orthographicSize * 2f) * Py;
            int puffs = 0;
            for (int i = 0; i < drift.PuffOwner.Length; i++) if (drift.PuffOwner[i] == k) puffs++;
            into.Append(frame).Append(',').Append(k).Append(',').Append(Mathf.RoundToInt(c.x)).Append(',')
                .Append(Mathf.RoundToInt(Py - c.y)).Append(',').Append(Mathf.RoundToInt(px)).Append(',')
                .Append(Mathf.RoundToInt(Mathf.Repeat(r.heading, 360f))).Append(',').Append(r.spin.ToString("F1")).Append(',')
                .Append(r.piece.tier).Append(',').Append(r.crackLevel.ToString("F2")).Append(',').Append(puffs).Append('\n');
        }
    }

    static int LivePuffs(SpaceDirector sd, BackdropPiece p)
    {
        if (sd.StationPuffs == null) return 0;
        foreach (var r in sd.StationPuffs.Rigs) if (r.piece == p) return sd.StationPuffs.LivePuffs(r);
        return 0;
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
