using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders the boss telegraphs for review: OLD (solid translucent bars and squares) | NEW calm | NEW at a drop, for a
// wave with a gap lane, an aim line and a few mine / bomb spots on screen at once. One PNG per world.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod BossTelegraphPreview.Run
//   (writes to $TELEGRAPH_PREVIEW_DIR, else Builds/BossTelegraphPreview)
public static class BossTelegraphPreview
{
    const int Width = 360, Height = 780;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("TELEGRAPH_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/BossTelegraphPreview";
        Directory.CreateDirectory(dir);
        using (new TestHarness.Sandbox())
        {
            foreach (int w in new[] { 0, 1, 3, 4 }) World(dir, w);
        }
        EditorApplication.Exit(0);
    }

    static Sprite White()
    {
        var t = Texture2D.whiteTexture;
        return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(.5f, .5f), t.width);
    }

    static Texture2D Shoot(Camera cam, RenderTexture rt)
    {
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var png = new Texture2D(Width, Height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
        png.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        png.Apply();
        RenderTexture.active = old;
        return png;
    }

    static void Scene(out Camera cam)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera", typeof(Camera));
        cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.aspect = Width / (float)Height;
        cam.orthographicSize = CameraFit.ComputeSize(5f, 2.85f, Width, Height);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(.07f, .06f, .16f);
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        var sprite = White();
        foreach (float side in new[] { -1f, 1f })
        {
            var wall = new GameObject("pipe").AddComponent<SpriteRenderer>();
            wall.sprite = sprite; wall.color = new Color(.16f, .17f, .26f); wall.sortingOrder = -10;
            wall.transform.position = new Vector3(side * 3.15f, 0f, 1f);
            wall.transform.localScale = new Vector3(1.43f, 30f, 1f);
        }
        // a fan of bullets arriving, so the telegraph is judged against something
        var bullet = new GameObject("bullets");
        for (int i = -6; i <= 6; i++)
        {
            if (Mathf.Abs(i * .38f - .5f) < 1.1f) continue;   // the gap
            var b = new GameObject("b").AddComponent<SpriteRenderer>();
            b.transform.SetParent(bullet.transform, false);
            b.sprite = sprite; b.color = new Color(.95f, .8f, .5f, .9f); b.sortingOrder = 30;
            b.transform.position = new Vector3(i * .38f, 2.2f - Mathf.Abs(i) * .12f, 0f);
            b.transform.localScale = new Vector3(.2f, .2f, 1f);
        }
    }

    static void Old(BossDef boss)
    {
        var tint = HostileGlow.Tint(boss.flash);
        var art = BossArt.Shot(boss, BossArt.Telegraph);
        void Bar(Vector2 a, Vector2 b, float half)
        {
            var sr = new GameObject("oldbar").AddComponent<SpriteRenderer>();
            sr.sprite = art; sr.sortingOrder = 24; sr.color = new Color(tint.r, tint.g, tint.b, .55f);
            var d = b - a;
            if (d.magnitude < .05f) { sr.transform.position = a; sr.transform.localScale = new Vector3(half * 2f, half * 2f, 1f); return; }
            sr.transform.position = (a + b) * .5f;
            sr.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f);
            sr.transform.localScale = new Vector3(Mathf.Max(.06f, half * 2f), d.magnitude, 1f);
        }
        Scenario(Bar);
    }

    static void Scenario(System.Action<Vector2, Vector2, float> add)
    {
        add(new Vector2(.5f, 4.7f), new Vector2(.5f, -4.7f), .9f);                 // the gap lane
        add(new Vector2(-1.4f, 4f), new Vector2(-1.4f, -4f), .08f);                // an aim line
        foreach (var p in new[] { new Vector2(-1.5f, 0f), new Vector2(1.9f, -1f), new Vector2(-.4f, -2.4f), new Vector2(1.6f, 1.6f) })
            add(p, p, .38f);
    }

    static Texture2D New(int world, float intensity)
    {
        var actor = BossActor.Spawn(BossCatalog.ForWorld(world));
        actor.BeginFight();
        var mk = actor.Markers;
        mk.Clear();
        mk.Intensity = intensity;
        Scenario((a, b, h) => mk.Add(a, b, h, 1.2f));
        for (int i = 0; i < 40; i++) mk.Step(1f / 60f);
        return null;
    }

    static void World(string dir, int world)
    {
        var rt = new RenderTexture(Width, Height, 24) { hideFlags = HideFlags.HideAndDontSave };
        var boss = BossCatalog.ForWorld(world);
        var sheet = new Texture2D(Width * 3 + 8, Height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
        var fill = new Color[sheet.width * sheet.height];
        for (int i = 0; i < fill.Length; i++) fill[i] = Color.white;
        sheet.SetPixels(fill);
        for (int panel = 0; panel < 3; panel++)
        {
            Camera cam;
            Scene(out cam);
            if (panel == 0) Old(boss);
            else New(world, panel == 1 ? 0f : 1f);
            var png = Shoot(cam, rt);
            Debug.Log("[TELEPREV] panel " + panel + " centre pixel " + png.GetPixel(Width / 2, Height / 2) + " cam " + cam.name + " rt " + rt.IsCreated());
            sheet.SetPixels(panel * (Width + 4), 0, Width, Height, png.GetPixels());
        }
        sheet.Apply();
        File.WriteAllBytes(Path.Combine(dir, "telegraph-" + boss.artKey.ToLower() + ".png"), sheet.EncodeToPNG());
    }
}
