using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Contact sheets for review of the target explosions (Explosions v2 + the
// TargetExplosion.Intensity loudness): per world backdrop and per pair of
// materials, a small / medium / large burst of each, with a player ship for
// scale, at five moments of the blast side by side (phone portrait frames).
// "before" sheets draw the v1 atlas (Art/Weapons/ExplosionsV1~) at the v1
// sizes for comparison.
//
//   scripts/unity-batch.sh -executeMethod ExplosionPreview.Run
//   (writes to $EXPLOSION_PREVIEW_DIR, else Builds/ExplosionPreview)
public static class ExplosionPreview
{
    const int Width = 405, Height = 720;
    static readonly string[] Worlds = { "Space", "Frost", "Verdant", "Ember" };
    static readonly float[] Moments = { .04f, .12f, .25f, .42f, .62f };
    static readonly TargetExplosion.Kind[][] Pairs =
    {
        new[] { TargetExplosion.Kind.Metal, TargetExplosion.Kind.Rock },
        new[] { TargetExplosion.Kind.Mine, TargetExplosion.Kind.Ice },
        new[] { TargetExplosion.Kind.Spore, TargetExplosion.Kind.Magma },
    };

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("EXPLOSION_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/ExplosionPreview";
        Directory.CreateDirectory(dir);
        var cache = typeof(WeaponArt).GetField("explosionSheet", BindingFlags.NonPublic | BindingFlags.Static);
        using (new TestHarness.Sandbox())
        {
            try
            {
                for (int w = 0; w < Worlds.Length; w++)
                    for (int p = 0; p < Pairs.Length; p++)
                        Sheet(Path.Combine(dir, "after-" + Worlds[w].ToLower() + "-" + p + ".png"), w, Pairs[p]);
                // before: the v1 atlas at the v1 sizes (Intensity 1/SizeK)
                var v1 = new Texture2D(2, 2);
                if (cache != null && v1.LoadImage(File.ReadAllBytes("Assets/Art/Weapons/ExplosionsV1~/Explosions.png")))
                {
                    cache.SetValue(null, V1Sheet(v1));
                    TargetExplosion.Intensity = 1f / TargetExplosion.SizeK;
                    for (int w = 0; w < Worlds.Length; w += 3)
                        for (int p = 0; p < Pairs.Length; p++)
                            Sheet(Path.Combine(dir, "before-" + Worlds[w].ToLower() + "-" + p + ".png"), w, Pairs[p]);
                }
            }
            finally
            {
                TargetExplosion.Intensity = 1f;
                if (cache != null) cache.SetValue(null, null);
            }
        }
        EditorApplication.Exit(0);
    }

    // The v1 atlas, sliced exactly as WeaponArt.Explosions slices the live one.
    static Sprite[] V1Sheet(Texture2D tex)
    {
        int kinds = WeaponArt.ExplosionKinds, frames = WeaponArt.ExplosionFrames;
        var s = new Sprite[kinds * frames + 3];
        float w = tex.width / 16f, h = tex.height / (float)kinds;
        System.Func<int, int, Sprite> cell = (c, r) =>
            Sprite.Create(tex, new Rect(c * w, (kinds - 1 - r) * h, w, h), new Vector2(.5f, .5f), w);
        for (int r = 0; r < kinds; r++)
            for (int i = 0; i < frames; i++) s[r * frames + i] = cell(i, r);
        s[kinds * frames] = cell(10, 0);
        s[kinds * frames + 1] = cell(11, 0);
        s[kinds * frames + 2] = cell(12, 0);
        return s;
    }

    static Camera cam;
    static RenderTexture rt;
    static Texture2D frame, sheet;

    static void Scene(int world)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.aspect = Width / (float)Height;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, Width, Height);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = 0f;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        var backdrop = new GameObject("~Backdrop").AddComponent<WorldBackdrop>();
        backdrop.Show(Worlds[world], false);
        for (int i = 0; i < 600; i++) backdrop.Step(1f / 60f);
        int id = ShipId.Equipped();
        var ship = new GameObject("~Ship", typeof(SpriteRenderer));
        ship.transform.position = new Vector3(0f, -3.4f, 0f);
        var hull = ship.GetComponent<SpriteRenderer>();
        hull.sprite = ShipHullArt.Rest(id, 2);
        float scale = shopingShips.NormalizedHullScale(hull.sprite);
        ship.transform.localScale = new Vector3(scale, scale, 1f);
        hull.sortingOrder = 10;
        if (rt == null) rt = new RenderTexture(Width, Height, 24);
        if (frame == null) frame = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        if (sheet == null) sheet = new Texture2D(Width * Moments.Length, Height, TextureFormat.RGB24, false);
        cam.targetTexture = rt;
    }

    static void Sheet(string path, int world, TargetExplosion.Kind[] pair)
    {
        Scene(world);
        float[] ys = { 2.3f, .5f, -1.5f };
        var sizes = new[] { TargetExplosion.Size.Small, TargetExplosion.Size.Medium, TargetExplosion.Size.Large };
        for (int c = 0; c < pair.Length; c++)
            for (int r = 0; r < 3; r++)
                TargetExplosion.Spawn(new Vector3(c == 0 ? -1.1f : 1.1f, ys[r], 0f), pair[c], sizes[r], ShipId.Equipped());
        float clock = 0f;
        const float Dt = 1f / 60f;
        for (int m = 0; m < Moments.Length; m++)
        {
            while (clock + Dt * .5f < Moments[m])
            {
                foreach (var fx in Object.FindObjectsByType<FlipbookFx>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    fx.Tick(Dt);
                clock += Dt;
            }
            cam.Render();
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            frame.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            frame.Apply();
            RenderTexture.active = old;
            sheet.SetPixels(m * Width, 0, Width, Height, frame.GetPixels());
        }
        sheet.Apply();
        File.WriteAllBytes(path, sheet.EncodeToPNG());
        Debug.Log("[EXPLOSION-PREVIEW] " + path);
    }
}
