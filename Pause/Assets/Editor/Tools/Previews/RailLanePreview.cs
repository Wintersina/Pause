using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Evidence renders for the dark rail edges, the rails moved out where the
// screen has room (RailInset) and the main game's 1.35x ship (ShipScale):
// gameS1 painted as each world, drawn by the game camera at a phone's size,
// before (no edge treatment, no inset, the normalised ship) and after.
//
//   RAIL_LANE_OUT=<dir> Unity -batchmode -quit -projectPath Pause
//       -executeMethod RailLanePreview.Run
//
// Writes <world>-<w>x<h>-<before|after>.png: the backdrop, both rails, a
// rail mine on each rail, star dust (small and large) and the ship at its
// start, plus a second ship clamped against the right rail. The UI is not
// drawn (it is a screen-space overlay).
public static class RailLanePreview
{
    const float Dt = 1f / 60f;

    public static void Run()
    {
        string outDir = System.Environment.GetEnvironmentVariable("RAIL_LANE_OUT");
        if (string.IsNullOrEmpty(outDir)) outDir = "Logs/rail-lane";
        Directory.CreateDirectory(outDir);
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            foreach (var id in new[] { "and-1080x1920", "iphone-13" })
            {
                var d = FitDevice.Find(id);
                for (int w = 0; w < WorldManager.Worlds.Length; w++)
                    foreach (bool after in new[] { false, true })
                        Shot(d, w, after, outDir + "/" + WorldManager.Worlds[w].displayName.ToLowerInvariant() + "-" + d.w + "x" + d.h + "-" + (after ? "after" : "before") + ".png");
            }
        }
        finally
        {
            RailInset.Enabled = true; RailInset.WidenLane = true;
            BossRails.Reset();
            PlayField.Reset();
            ScreenInfo.ClearOverride();
        }
    }

    static void Shot(FitDevice d, int world, bool after, string file)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity");
        BossRails.Reset();
        RailInset.Enabled = true; RailInset.WidenLane = after;
        var cam = Camera.main;
        cam.aspect = d.Aspect;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, d.w, d.h);
        ScreenInfo.ClearOverride();
        ScreenInfo.Override(d.w, d.h, d.Safe, d.Cutouts, d.ReportedDpi, d.ios);
        PlayField.Reset();
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        var theme = WorldManager.Worlds[world];
        var wb = new GameObject("~RsBackdrop").AddComponent<WorldBackdrop>();
        WorldPainter.Apply(theme);
        foreach (var name in new[] { "leftPipe", "rightPipe" })
        {
            var wall = GameObject.Find(name);
            var s = wall.transform.localScale;
            s.y = cam.orthographicSize * 2f * 1.085f / wall.GetComponent<MeshFilter>().sharedMesh.bounds.size.y;   // RailFit
            wall.transform.localScale = s;
            RailFit.RefreshTextureTiling(wall);
            if (!after)
            {
                var m = wall.GetComponent<Renderer>().sharedMaterial;
                m.SetFloat("_OuterDark", 0f);
            }
        }
        BossRails.Measure();
        wb.Show(theme.displayName, false);
        for (int i = 0; i < 30; i++) wb.Step(Dt);

        float top = CameraFit.ViewTop, h = cam.orthographicSize;
        var root = new GameObject("~RsStage");
        var mineDef = EnemyRoster.One(world, EnemyRole.Mine);
        Mine(root, mineDef, enmiesOnBoard.WorldRailX(true), top - h * .6f);
        Mine(root, mineDef, enmiesOnBoard.WorldRailX(false), top - h * 1.1f);

        // star dust as the game spawns it (x1.25 after)
        var spawner = Object.FindFirstObjectByType<spawnGoodStuff>();
        if (spawner != null)
        {
            var small = (GameObject)typeof(spawnGoodStuff).GetField("smStar").GetValue(spawner);
            var large = (GameObject)typeof(spawnGoodStuff).GetField("midStar").GetValue(spawner);
            Dust(root, small, new Vector3(-1.2f, top - h * .9f, 0f), true);
            Dust(root, small, new Vector3(-.8f, top - h * .95f, 0f), true);
            Dust(root, large, new Vector3(1.0f, top - h * .75f, 0f), true);
            Dust(root, large, new Vector3(.3f, top - h * .55f, 0f), after);
        }

        // the ship at its start, and one clamped against the right rail
        var ship = new GameObject("~RsSpawner").AddComponent<spawnShips>().Spawn(ShipId.Starter);
        
        ship.transform.position = new Vector3(0f, ShipReach.StartY, 0f);
        var ship2 = new GameObject("~RsSpawner2").AddComponent<spawnShips>().Spawn(ShipId.Starter);
        
        float reach = ShipReach.HalfWidth;
        ship2.transform.position = new Vector3(reach, ShipReach.StartY + 2.2f, 0f);
        foreach (var go in new[] { ship, ship2 })
        {
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr != null) sr.sortingOrder = 40;
        }

        float inner = BossRails.InnerEdge;
        var hull = ship.GetComponent<SpriteRenderer>().bounds.size;
        Debug.Log(string.Format("[RSP] {0} {1}x{2} {3}: rail inner edge +/-{4:F3} ({5:F1}% of the width each side is rail), ship {6:F3} x {7:F3} u = {8:F0} x {9:F0} px, " +
                                "reach +/-{10:F3} (hull edge {11:F3}), mine x {12:F3}",
                                theme.displayName, d.w, d.h, after ? "after" : "before", inner,
                                100f * (cam.orthographicSize * cam.aspect - inner) / (2f * cam.orthographicSize * cam.aspect),
                                hull.x, hull.y, hull.x / (2f * cam.orthographicSize * cam.aspect) * d.w, hull.y / (2f * cam.orthographicSize) * d.h,
                                reach, reach + hull.x * .5f, enmiesOnBoard.WorldRailX(false)));

        Capture(cam, d.w, d.h, file);
        Object.DestroyImmediate(root);
        RailInset.Enabled = true;
    }

    static void Dust(GameObject root, GameObject prefab, Vector3 at, bool after)
    {
        if (prefab == null) return;
        var go = Object.Instantiate(prefab, at, Quaternion.identity);
        go.transform.SetParent(root.transform, true);
        if (after) PickupArt.ApplyInGameScale(go, prefab);
    }

    static void Mine(GameObject root, EnemyDef def, float x, float y)
    {
        var rail = new GameObject("RailMineLane");
        rail.transform.SetParent(root.transform, false);
        rail.transform.position = new Vector3(x, y, 0f);
        var go = EnemyFactory.Create(def, new Vector3(x, y, 0f), Quaternion.identity);
        go.transform.SetParent(root.transform, true);
        go.GetComponent<SpriteRenderer>().flipX = x > 0f;
        var mount = go.AddComponent<RailMineMount>();
        mount.MountTo(rail.transform);
        mount.SendMessage("LateUpdate");
    }

    static void Capture(Camera cam, int width, int height, string file)
    {
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var png = new Texture2D(width, height, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        png.Apply();
        File.WriteAllBytes(file, png.EncodeToPNG());
        RenderTexture.active = old;
        cam.targetTexture = previous;
        Object.DestroyImmediate(png);
        Object.DestroyImmediate(rt);
    }
}
