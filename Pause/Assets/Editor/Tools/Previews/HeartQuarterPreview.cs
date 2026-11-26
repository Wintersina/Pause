using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Deterministic export of the native heart's quarter states and break-off frames. Run after compile:
// scripts/unity-batch.sh -executeMethod HeartQuarterPreview.Run
public static class HeartQuarterPreview
{
    public const string DirectoryPath = "/private/tmp/pause-quarter-heart-review";
    public static void Run()
    {
        Export();
        EditorApplication.Exit(0);
    }
    public static void Export()
    {
        Directory.CreateDirectory(DirectoryPath);
        var source = Resources.Load<Sprite>(ShipLivesIndicator.HeartSpritePath);
        var art = HeartQuarterFrames.For(source);
        if (art == null) throw new System.InvalidOperationException("The source heart could not be read");
        File.WriteAllBytes(Path.Combine(DirectoryPath, "heart-quarter-atlas.png"), art.Atlas.EncodeToPNG());
        for (int remain = 0; remain <= 4; remain++) Save(art.States[remain], "heart-" + remain * 25 + ".png");
        for (int loss = 0; loss < 4; loss++)
            for (int frame = 0; frame < HeartQuarterFrames.Frames; frame++)
            {
                Save(art.BreakFrames[loss][frame], "loss-" + loss + "-frame-" + frame.ToString("D2") + ".png");
                Save(art.LossFrames[loss][frame], "piece-" + loss + "-frame-" + frame.ToString("D2") + ".png");
            }
        // A readable preview strip, enlarged by copying source pixels (no interpolation/repaint).
        var strip = new Texture2D(art.Width * 8 * 4, art.Height * 4 * 4, TextureFormat.RGBA32, false);
        var atlas = art.Atlas.GetPixels32(); int stride = art.Atlas.width;
        var output = new Color32[strip.width * strip.height];
        for (int loss = 0; loss < 4; loss++)
            for (int frame = 0; frame < HeartQuarterFrames.Frames; frame++)
            {
                var rect = art.BreakFrames[loss][frame].rect;
                for (int y = 0; y < art.Height; y++)
                    for (int x = 0; x < art.Width; x++)
                    {
                        Color32 c = atlas[((int)rect.y + y) * stride + (int)rect.x + x];
                        for (int sy = 0; sy < 4; sy++)
                            for (int sx = 0; sx < 4; sx++)
                            {
                                int dx = frame * art.Width * 4 + x * 4 + sx;
                                int dy = (3 - loss) * art.Height * 4 + y * 4 + sy;
                                output[dy * strip.width + dx] = c;
                            }
                    }
            }
        strip.SetPixels32(output); strip.Apply();
        File.WriteAllBytes(Path.Combine(DirectoryPath, "heart-quarter-preview-4x.png"), strip.EncodeToPNG());
        Object.DestroyImmediate(strip);
        File.WriteAllText(Path.Combine(DirectoryPath, "frames.txt"),
            "Loss order: top-right, bottom-right, bottom-left, top-left.\n" +
            "States index remaining quarters 0..4. Each loss has 8 frames at .07 seconds/frame.\n" +
            "Atlas: 8 columns x 10 rows, source-size cells, rows top to bottom.\n" +
            "Row 0 states 0%,25%,50%,75%,100%; row 1 original quarter pieces; rows 2..5 combined breaks; rows 6..9 detached overlays.\n" +
            "All cells keep the original pivot and pixels-per-unit.\n");
        Debug.Log("[HEARTQ] Preview exported to " + DirectoryPath);
    }
    // Actual health/damage/render path at phone resolution, including a killing hit.
    public static void ExportCombat()
    {
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
        cam.tag = "MainCamera"; cam.orthographic = true; cam.orthographicSize = 5f;
        cam.aspect = 480f / 854f; cam.transform.position = new Vector3(0f, 0f, -10f);
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.015f, .02f, .05f);
        var rt = new RenderTexture(480, 854, 24);
        var image = new Texture2D(480, 854, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        cam.targetTexture = rt;
        var dir = Path.Combine(DirectoryPath, "combat-quarter"); Directory.CreateDirectory(dir);
        var enemy = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Fighter), Vector3.zero, Quaternion.identity, 1f, true);
        ClearTarget.Ensure(enemy);
        var view = enemy.GetComponent<EnemyHearts>().View;
        for (int i = 0; i < 40; i++) view.Place(.02f, .02f);
        float budget = 1f;
        try
        {
            ShipAttackHits.Hit(enemy, 1, .25f, ref budget, HitKind.Drone);
            for (int phase = 0; phase < 2; phase++)
            {
                if (phase == 1)
                    for (int hit = 0; hit < 7; hit++) ShipAttackHits.Hit(enemy, 1, .25f, ref budget, HitKind.Drone);
                for (int frame = 0; frame < 6; frame++)
                {
                    if (frame > 0)
                    {
                        if (enemy != null) { view.Place(.11f, .11f); view.StepBreaks(.11f); }
                        foreach (var remnant in Object.FindObjectsByType<HeartQuarterRemnant>(FindObjectsSortMode.None)) remnant.Step(.11f);
                    }
                    cam.Render(); RenderTexture.active = rt;
                    image.ReadPixels(new Rect(0, 0, 480, 854), 0, 0); image.Apply();
                    File.WriteAllBytes(Path.Combine(dir, (phase == 0 ? "hit" : "kill") + "-" + frame.ToString("D2") + ".png"), image.EncodeToPNG());
                }
            }
        }
        finally
        {
            cam.targetTexture = null; RenderTexture.active = previous;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(image);
        }
        Debug.Log("[HEARTQ] Live combat frames exported to " + dir);
    }

    static void Save(Sprite sprite, string name)
    {
        var rect = sprite.rect;
        var texture = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
        int width, height;
        texture.SetPixels32(HeartQuarterFrames.ReadFullCell(sprite, out width, out height));
        texture.Apply();
        File.WriteAllBytes(Path.Combine(DirectoryPath, name), texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
    }
}
