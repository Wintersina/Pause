using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders the life hearts orbiting their ships, for review:
//   orbit-grid.png       every ship at one moment, its own lives
//   orbit-<ship>/NNN.png  a few seconds of a ship's orbit and a hit taken
//                        (a rock flies in, a heart shields against it and
//                        crumbles, the rest re-space) -- frames for a GIF
// with the thumb drawn where it sits.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod HeartsPreview.Run
//   (writes to $HEARTS_PREVIEW_DIR, else Builds/HeartsPreview)
public static class HeartsPreview
{
    const float CellW = 1.9f, CellH = 2.3f;
    const int Cols = 5;
    const float Fps = 30f;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("HEARTS_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/HeartsPreview";
        Directory.CreateDirectory(dir);
        using (var sandbox = new TestHarness.Sandbox())
        {
            Grid(Path.Combine(dir, "orbit-grid.png"));
            Clip(dir, 7, 5, new Vector3(.35f, 1.6f, 0f));    // Gold Warden: a crown of five, hit from ahead
            Clip(dir, 11, 0, new Vector3(-1.6f, .5f, 0f));   // Ninja: the spinning ring, hit from the left
            Clip(dir, 1, 3, new Vector3(1.5f, -.6f, 0f));    // Neon Comet: the train, hit from the right
            Clip(dir, 8, 4, new Vector3(-.5f, 1.6f, 0f));    // Lightning: crossing atom orbits
            ShipUiSlots.ScreenOverride = null;
            collisionDetection.lifeCounter = 0;
        }
        EditorApplication.Exit(0);
    }

    static void Fresh()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        collisionDetection.lifeCounter = 0;
        ShipUiSlots.ScreenOverride = () => Rect.MinMaxRect(-100f, -100f, 100f, 100f);
    }

    static void Grid(string path)
    {
        Fresh();
        int i = 0;
        foreach (int id in ShipId.All)
        {
            var at = Cell(i++);
            var rig = HeartsPlacementTest.Build(id, at, true, 0);
            AddFlame(rig);
            HeartsPlacementTest.Pose(rig, 20f, 0f, 1.3f, 1f, ShipUiSlots.Spins(id) ? 25f : 0f, 1.7f + i * .37f);
            Finger(at);
            Label(at + Vector3.up * .1f, ShipId.NameOf(id) + "  " + rig.hearts.Hearts.Length + "  " + rig.hearts.Style);
        }
        var cam = MakeCamera(new Vector3(0f, .3f, -10f), 3 * CellH * .5f + .2f, Cols * CellW * .5f);
        Label(new Vector3(0f, cam.transform.position.y + cam.orthographicSize - .85f, 0f), "every ship's own lives, orbiting");
        Shoot(cam, path, 1500);
        Object.DestroyImmediate(cam.gameObject);
    }

    // ~5.5 seconds: orbiting, a rock comes in, the heart shields and
    // crumbles, the hearts left re-space.
    static void Clip(string dir, int id, int count, Vector3 from)
    {
        Fresh();
        string name = "orbit-" + ShipId.KeyOf(id);
        string frames = Path.Combine(dir, name);
        if (Directory.Exists(frames)) Directory.Delete(frames, true);
        Directory.CreateDirectory(frames);

        var rig = HeartsPlacementTest.Build(id, Vector3.zero, true, count);
        AddFlame(rig);
        Finger(Vector3.zero);
        Label(new Vector3(0f, 1.32f, 0f), ShipId.NameOf(id) + "  " + rig.hearts.Hearts.Length + " hearts  " + rig.hearts.Style);
        var rock = Rock();
        var cam = MakeCamera(new Vector3(0f, -.15f, -10f), 1.6f, 1.6f);
        bool spins = ShipUiSlots.Spins(id);

        const float total = 5.5f, hitAt = 2.6f, flight = .45f;
        int n = Mathf.RoundToInt(total * Fps);
        float dt = 1f / Fps;
        Vector3 impact = from.normalized * .55f;
        bool hit = false;
        for (int f = 0; f < n; f++)
        {
            float t = f * dt;
            if (spins) rig.ship.transform.Rotate(0f, 0f, 360f * 1.2f * dt);
            // the gun hovers on its own clock
            HeartsPlacementTest.Pose(rig, 20f, 0f, t, 1f, rig.ship.transform.eulerAngles.z);
            rig.hearts.SendMessage("Update");
            rig.hearts.Place(dt, dt);
            rig.hearts.StepBreaks(dt);

            float k = (t - (hitAt - flight)) / flight;
            rock.SetActive(k >= 0f && k < 1f);
            if (rock.activeSelf) rock.transform.position = Vector3.Lerp(from, impact, k) + Vector3.back * .5f;
            if (!hit && t >= hitAt)
            {
                hit = true;
                ShipLivesIndicator.Impact(impact);
                collisionDetection.lifeCounter = 1;
            }
            Shoot(cam, Path.Combine(frames, f.ToString("000") + ".png"), 480);
        }
        collisionDetection.lifeCounter = 0;
        Object.DestroyImmediate(cam.gameObject);
        Debug.Log("[PREVIEW] " + frames + " (" + n + " frames)");
    }

    static Vector3 Cell(int i)
    {
        int col = i % Cols, row = i / Cols;
        return new Vector3((col - (Cols - 1) * .5f) * CellW, (1 - row) * CellH + .3f, 0f);
    }

    static void AddFlame(HeartsPlacementTest.Rig rig)
    {
        var thruster = rig.ship.AddComponent<ShipThruster>();
        thruster.SendMessage("Start");
        thruster.SendMessage("LateUpdate");
    }

    static Sprite disc;

    static Sprite Disc()
    {
        if (disc != null) return disc;
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = new Vector2(x - n * .5f + .5f, y - n * .5f + .5f).magnitude / (n * .5f);
                tex.SetPixel(x, y, d <= 1f ? new Color(1f, 1f, 1f, d > .93f ? .9f : .45f) : Color.clear);
            }
        tex.Apply();
        disc = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), n * .5f);
        return disc;
    }

    // The thumb: a pad of radius FingerRadius, FingerBelow under the ship.
    static void Finger(Vector3 ship)
    {
        var c = ship + Vector3.down * HeartsPlacementTest.FingerBelow;
        var pad = new GameObject("~Thumb", typeof(SpriteRenderer));
        pad.transform.position = new Vector3(c.x, c.y, -1f);
        pad.transform.localScale = Vector3.one * HeartsPlacementTest.FingerRadius;
        var sr = pad.GetComponent<SpriteRenderer>();
        sr.sprite = Disc();
        sr.color = new Color(1f, .82f, .7f, .6f);
        sr.sortingOrder = 100;
    }

    static GameObject Rock()
    {
        var go = new GameObject("~Rock", typeof(SpriteRenderer));
        go.transform.localScale = Vector3.one * .16f;
        var sr = go.GetComponent<SpriteRenderer>();
        sr.sprite = Disc();
        sr.color = new Color(.75f, .55f, .45f, 1f);
        sr.sortingOrder = 50;
        go.SetActive(false);
        return go;
    }

    static void Label(Vector3 at, string text)
    {
        var go = new GameObject("~Label");
        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        go.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
        tm.characterSize = .028f;
        tm.fontSize = 48;
        tm.anchor = TextAnchor.UpperCenter;
        tm.color = new Color(.85f, .9f, 1f);
        go.transform.position = new Vector3(at.x, at.y + .75f, -2f);
    }

    static Camera MakeCamera(Vector3 at, float halfHeight, float halfWidth)
    {
        var camGo = new GameObject("~PreviewCamera", typeof(Camera));
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(.05f, .06f, .12f);
        cam.orthographicSize = halfHeight;
        cam.aspect = halfWidth / halfHeight;
        cam.transform.position = at;
        return cam;
    }

    static void Shoot(Camera cam, string path, int px)
    {
        int py = Mathf.RoundToInt(px / cam.aspect);
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
