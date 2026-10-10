using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders the shield's release shockwave (ShieldShockwave) a few frames after it fires, for review:
// a shielded ship, hazards around and above it, frames at 0, 0.1, 0.2 and 0.4 s.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod ShockwavePreview.Run
//   (frames to $SHOCKWAVE_PREVIEW_DIR/shockwave-<t>.png, else Builds/ShockwavePreview)
public static class ShockwavePreview
{
    const int Width = 432;
    const float HalfH = 6.4f, HalfW = 2.95f, Dt = 1f / 60f;
    static Camera cam;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("SHOCKWAVE_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/ShockwavePreview";
        Directory.CreateDirectory(dir);
        using (var sandbox = new TestHarness.Sandbox())
        {
            try { Clip(dir); }
            catch (System.Exception e) { Debug.LogError("[ShockwavePreview] " + e); }
            EnemyShove.Clear();
        }
        EditorApplication.Exit(0);
    }

    static void Clip(string dir)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        startMenu.youAreInTutorial = false;
        moveBackGround.speed = .2f;
        ShieldShockwave.ResetGuards();
        EnemyShove.Clear();

        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(.06f, .05f, .14f);
        cam.orthographicSize = HalfH;
        cam.aspect = HalfW / HalfH;
        cam.transform.position = new Vector3(0f, 0f, -10f);

        var ship = new GameObject(ShipId.ObjectName(ShipId.Starter) + "(Clone)", typeof(SpriteRenderer));
        var sr = ship.GetComponent<SpriteRenderer>();
        sr.sprite = ShipHullArt.Rest(ShipId.Starter);
        sr.sortingOrder = 6;
        float s = shopingShips.NormalizedHullScale(sr.sprite);
        ship.transform.localScale = new Vector3(s, s, 1f);
        ship.transform.position = new Vector3(0f, -3f, 0f);

        // pink hazards: beside the ship, in its column, beyond the ring, behind
        Hazard(new Vector2(1.2f, -2.3f), .5f);
        Hazard(new Vector2(-1.7f, -3.3f), .5f);
        Hazard(new Vector2(0f, -.6f), .5f);
        Hazard(new Vector2(.05f, 2.2f), .5f);
        Hazard(new Vector2(-2.0f, 0f), .5f);      // outside both: stays
        Hazard(new Vector2(1.9f, -5.3f), .5f);    // behind, inside the ring

        var shield = ShipShield.For(ship);
        shield.remainingOverride = 5f;
        shield.Show();
        for (int i = 0; i < 40; i++) shield.Tick(Dt);
        Shoot(Path.Combine(dir, "shockwave-before.png"));
        shield.Hide();   // the 5.8 s ran out: ShipShield.Hide -> ShieldShockwave.TryRelease

        float t = 0f;
        foreach (float at in new[] { 0f, .1f, .2f, .4f })
        {
            while (t < at - 1e-4f)
            {
                ShieldShockwaveFx.Instance.Tick(Dt);
                ShieldShards.Instance.Tick(Dt);
                t += Dt;
            }
            Shoot(Path.Combine(dir, "shockwave-" + at.ToString("0.0") + ".png"));
        }
    }

    static Sprite white;
    static void Hazard(Vector2 at, float size)
    {
        if (white == null)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            white = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1f);
        }
        var go = new GameObject("~hazard");
        go.tag = "Enimey";
        go.transform.position = new Vector3(at.x, at.y, 0f);
        go.transform.localScale = new Vector3(size, size, 1f);
        var r = go.AddComponent<SpriteRenderer>();
        r.sprite = white;
        r.color = new Color(1f, .25f, .6f);
        r.sortingOrder = 5;
        var box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        ClearTarget.Ensure(go).SetRadius(size * .5f);
        SpawnFootprint.Attach(go, Vector2.one * size * .5f);
        go.transform.localScale = new Vector3(size, size, 1f);
    }

    static void Shoot(string path)
    {
        int px = Width, py = Mathf.RoundToInt(Width / cam.aspect);
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
