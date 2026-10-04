using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders every ship with its life hearts (2..5 of them, each in its own
// formation, the spinners' shield ring) with the thumb drawn where it sits,
// plus a lost heart crumbling, to PNGs for review.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod HeartsPreview.Run
//   (writes to $HEARTS_PREVIEW_DIR, else Builds/HeartsPreview)
public static class HeartsPreview
{
    const float CellW = 1.7f, CellH = 2.2f;
    const int Cols = 5;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("HEARTS_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/HeartsPreview";
        Directory.CreateDirectory(dir);
        using (var sandbox = new TestHarness.Sandbox())
        {
            for (int count = ShipLives.Fewest; count <= ShipLives.Most; count++)
                Grid(count, Path.Combine(dir, "hearts-" + count + ".png"));
            Grid(0, Path.Combine(dir, "hearts-own.png"));
            Crumble(Path.Combine(dir, "hearts-crumble.png"));
            ShipUiSlots.ScreenOverride = null;
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

    // count 0: each ship's own lives.
    static void Grid(int count, string path)
    {
        Fresh();
        int i = 0;
        foreach (int id in ShipId.All)
        {
            var at = Cell(i++);
            var rig = HeartsPlacementTest.Build(id, at, true, count);
            AddFlame(rig);
            HeartsPlacementTest.Pose(rig, 20f, 0f, 1.3f, 1f, ShipUiSlots.Spins(id) ? 25f : 0f);
            for (int f = 0; f < 3; f++) rig.hearts.Place(1f / 60f, 1f / 60f);
            Finger(at);
            Label(at, ShipId.NameOf(id) + "  " + rig.hearts.Hearts.Length + "  " + rig.hearts.Style);
        }
        Shoot(path, 3, count == 0 ? "each ship's own lives" : count + " hearts");
    }

    // One ship per column, losing a heart: before, shaking loose, cracked,
    // shards falling, gone; then the heal pop.
    static void Crumble(string path)
    {
        Fresh();
        var ids = new[] { 1, 7, 13, 15, 5 };
        var steps = new[] { -1f, .06f, .17f, .4f, .65f };
        for (int c = 0; c < ids.Length; c++)
            for (int r = 0; r < steps.Length; r++)
            {
                collisionDetection.lifeCounter = 0;
                var at = new Vector3((c - 2f) * CellW, (1.5f - r) * CellH * .8f, 0f);
                var rig = HeartsPlacementTest.Build(ids[c], at, false, ShipLives.Max(ids[c]) == 2 ? 3 : ShipLives.Max(ids[c]));
                AddFlame(rig);
                rig.hearts.SendMessage("Update");
                rig.hearts.Place(0f, 1f / 60f);
                if (steps[r] >= 0f)
                {
                    collisionDetection.lifeCounter = 1;
                    rig.hearts.SendMessage("Update");
                    rig.hearts.Place(0f, 0f);
                    rig.hearts.StepBreaks(steps[r]);
                }
                // keep this rig's state: the next rig's Update reads the same static
                rig.hearts.enabled = false;
                if (r == 0) Label(at, ShipId.NameOf(ids[c]));
            }
        collisionDetection.lifeCounter = 0;
        Shoot(path, 5, "a hit: the heart breaks away, cracks and crumbles (rows: before, 0.06s, 0.17s, 0.4s, 0.65s)", 4f);
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

    // The thumb: a pad of radius FingerRadius, FingerBelow under the ship,
    // and the rest of the thumb running down from it.
    static void Finger(Vector3 ship)
    {
        if (disc == null)
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x - n * .5f + .5f, y - n * .5f + .5f).magnitude / (n * .5f);
                    tex.SetPixel(x, y, d <= 1f ? new Color(1f, .82f, .7f, d > .93f ? .9f : .45f) : Color.clear);
                }
            tex.Apply();
            disc = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), n * .5f);
        }
        var c = ship + Vector3.down * HeartsPlacementTest.FingerBelow;
        var pad = new GameObject("~Thumb", typeof(SpriteRenderer));
        pad.transform.position = new Vector3(c.x, c.y, -1f);
        pad.transform.localScale = Vector3.one * HeartsPlacementTest.FingerRadius;
        var sr = pad.GetComponent<SpriteRenderer>();
        sr.sprite = disc;
        sr.sortingOrder = 100;
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

    static void Shoot(string path, int rows, string title, float halfHeight = 0f)
    {
        var camGo = new GameObject("~PreviewCamera", typeof(Camera));
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(.05f, .06f, .12f);
        float h = halfHeight > 0f ? halfHeight : rows * CellH * .5f + .2f;
        cam.orthographicSize = h;
        float w = Cols * CellW * .5f;
        int px = 1500, py = Mathf.RoundToInt(px * h / w);
        cam.transform.position = new Vector3(0f, halfHeight > 0f ? .2f : .3f - (rows - 3) * CellH * .5f, -10f);
        cam.aspect = w / h;
        var rt = new RenderTexture(px, py, 24);
        cam.targetTexture = rt;
        Label(new Vector3(0f, cam.transform.position.y + h - .85f, 0f), title);
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
        Debug.Log("[PREVIEW] " + path);
    }
}
