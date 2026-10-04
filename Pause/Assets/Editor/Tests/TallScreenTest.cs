using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// Very tall screens (9:22, 9:24, Galaxy Z Fold covers ~2.45-2.56:1) run full
// screen with no letterbox: CameraFit keeps the play field's 2.85 half-width
// and extends the view's height (half-height up to ~7.6). Everything that
// enters or leaves "just off screen" must use the real view edge:
//   - the enemy / pickup spawn point sits above the visible top
//   - the boss warps in from, and retreats to, fully above it
//   - portals spawn above it and are missed only below the bottom
//   - boss beams reach past the bottom, boss shots live until past the edge
//   - the rails and every full-width backdrop layer cover the whole height
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod TallScreenTest.Run
public static class TallScreenTest
{
    static int fails;

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TS] PASS  " : "[TS] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static readonly (string name, int w, int h)[] Screens =
    {
        ("9:16 1080x1920", 1080, 1920),
        ("9:21 1080x2520", 1080, 2520),
        ("9:22 1080x2640", 1080, 2640),
        ("9:24 1080x2880", 1080, 2880),
        ("Z Fold6 cover 968x2376", 968, 2376),
        ("Z Fold5 cover 904x2316", 904, 2316),
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        foreach (var scene in new[] { "gameS1", "tutorialS5" })
        {
            EditorSceneManager.OpenScene("Assets/Scenes/" + scene + ".unity", OpenSceneMode.Single);
            var cam = Camera.main;
            Check(scene + ": has an orthographic main camera", cam != null && cam.orthographic);
            if (cam == null) continue;
            float baseSize = cam.orthographicSize;
            foreach (var s in Screens)
            {
                cam.orthographicSize = CameraFit.ComputeSize(baseSize, 2.85f, s.w, s.h);
                cam.aspect = (float)s.w / s.h;
                SpawnAndRails(scene + " " + s.name, cam);
            }
            cam.orthographicSize = baseSize;
        }
        BossesAndPortals();
        Backdrop();
        Debug.Log("[TS] failures: " + fails);
        return fails;
    }

    static float Top(Camera cam) { return cam.transform.position.y + cam.orthographicSize; }
    static float Bottom(Camera cam) { return cam.transform.position.y - cam.orthographicSize; }

    static void SpawnAndRails(string who, Camera cam)
    {
        var spawn = SceneUtil.FindAny("Enemey_Item_Position");
        if (spawn != null)
        {
            var comp = spawn.GetComponent<SpawnAboveCamera>() ?? spawn.AddComponent<SpawnAboveCamera>();
            comp.SendMessage("Reposition");
            Check(who + ": spawn point above the visible top (" + spawn.transform.position.y.ToString("F2") +
                  " > " + Top(cam).ToString("F2") + ")", spawn.transform.position.y > Top(cam));
        }
        else Check(who + ": spawn point exists", false);

        foreach (var name in new[] { "leftPipe", "rightPipe" })
        {
            var go = SceneUtil.FindAny(name);
            if (go == null) { Check(who + ": " + name + " exists", false); continue; }
            var rail = go.GetComponent<RailFit>() ?? go.AddComponent<RailFit>();
            rail.SendMessage("Start");
            var r = go.GetComponent<Renderer>();
            Check(who + ": " + name + " covers the full view height",
                  r != null && r.bounds.min.y <= Bottom(cam) && r.bounds.max.y >= Top(cam));
        }
        Check(who + ": the play field's width is kept (half-width " + (cam.orthographicSize * cam.aspect).ToString("F3") + ")",
              cam.orthographicSize * cam.aspect >= 2.85f - .001f);
    }

    static Camera FreshCamera(float size, float aspect)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = new GameObject("Main Camera", typeof(Camera));
        go.tag = "MainCamera";
        var cam = go.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = size;
        cam.aspect = aspect;
        go.transform.position = new Vector3(0f, 0f, -10f);
        return cam;
    }

    static void BossesAndPortals()
    {
        foreach (var s in Screens)
        {
            float size = CameraFit.ComputeSize(5f, 2.85f, s.w, s.h);
            var cam = FreshCamera(size, (float)s.w / s.h);
            Check(s.name + ": CameraFit.ViewTop/ViewBottom are the camera's edges",
                  Mathf.Abs(CameraFit.ViewTop - size) < 1e-4f && Mathf.Abs(CameraFit.ViewBottom + size) < 1e-4f);
            float bossBottom = BossActor.ArrivalY - BossConfig.BossWorldSize * .5f;
            Check(s.name + ": the boss warps in from fully above the view (bottom " + bossBottom.ToString("F2") +
                  " > top " + size.ToString("F2") + ")", bossBottom > size);
            Check(s.name + ": the boss still starts from the authored 7.6 or above", BossActor.ArrivalY >= 7.6f);
            Check(s.name + ": a portal spawns above the view (" + Portal.SpawnY.ToString("F2") + ")",
                  Portal.SpawnY - .9f > size);

            // A laser burning straight down grows past the bottom of the view.
            var root = new GameObject("~beams");
            var beam = BossBeam.Create(root.transform);
            beam.Begin(BossCatalog.All[0], null, -1, new Vector3(0f, 3f, 0f), -90f, 0f, .2f, 1f, .6f);
            for (int i = 0; i < 20; i++) beam.Step(.05f);   // its tell, then grown out to full length
            float lowest = beam.Origin.y + beam.Direction.y * beam.Length;
            Check(s.name + ": a boss laser reaches past the bottom of the view (" + lowest.ToString("F2") + ")",
                  beam.Live && lowest < -size);
            Object.DestroyImmediate(root);
        }
    }

    // Every full-width backdrop tile layer covers the whole view, at every
    // tall aspect, for every world.
    static void Backdrop()
    {
        foreach (var s in Screens)
        {
            float size = CameraFit.ComputeSize(5f, 2.85f, s.w, s.h);
            var cam = FreshCamera(size, (float)s.w / s.h);
            float halfW = size * cam.aspect;
            var go = new GameObject("~TallBackdrop");
            var wb = go.AddComponent<WorldBackdrop>();
            var gaps = new List<string>();
            foreach (var spec in BackdropCatalog.All)
            {
                wb.Show(spec.world, false);
                if (wb.Current == null) { gaps.Add(spec.world + " (none)"); continue; }
                for (int k = 0; k < 40; k++)
                {
                    wb.Step(1f / 30f);
                    foreach (var tile in wb.Current.Tiles)
                    {
                        if (tile.layer.kind != BackdropCatalog.Kind.Tile) continue;
                        if (!Covers(go.transform, "Tile_" + tile.layer.name, -halfW, halfW, -size, size))
                        { gaps.Add(spec.world + "/" + tile.layer.name); goto nextWorld; }
                    }
                }
                nextWorld:;
            }
            Check(s.name + ": every full-width backdrop layer covers the whole view" +
                  (gaps.Count > 0 ? " -- gaps: " + string.Join(", ", gaps) : ""), gaps.Count == 0);
            Object.DestroyImmediate(go);
        }
    }

    // The enabled copies under `layerName` cover the rect (x by each copy,
    // y by their union).
    static bool Covers(Transform root, string layerName, float xMin, float xMax, float yMin, float yMax)
    {
        Transform layer = null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == layerName) { layer = t; break; }
        if (layer == null) return false;
        var spans = new List<Vector2>();
        foreach (var r in layer.GetComponentsInChildren<SpriteRenderer>(false))
        {
            if (!r.enabled || r.sprite == null) continue;
            var b = r.bounds;
            if (b.min.x > xMin + .001f || b.max.x < xMax - .001f) return false;
            spans.Add(new Vector2(b.min.y, b.max.y));
        }
        spans.Sort((a, b) => a.x.CompareTo(b.x));
        float reached = yMin;
        foreach (var sp in spans)
        {
            if (sp.x > reached + .002f) break;
            reached = Mathf.Max(reached, sp.y);
        }
        return reached >= yMax - .002f;
    }
}
