using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// Evidence renders for the rails vetting: every world at two phone shapes
// with rail mines clamped to both rails (at rest, mid-slide, charging and
// just fired), a pilot on station, a heavy and an elite, drawn by the game
// camera. Nothing here is gameplay.
//
//   RAILS_VETTING_OUT=<dir> Unity -batchmode -quit -projectPath Pause
//       -executeMethod RailsVettingRender.Run
//
// "-guides" images add thin lines at the enemy lane (SpawnLane.LaneHalf, cyan),
// the ship's clamp (2.4, white) and the measured rail edge (BossRails, yellow).
public static class RailsVettingRender
{
    const float Dt = 1f / 60f;

    public static void Run()
    {
        string outDir = System.Environment.GetEnvironmentVariable("RAILS_VETTING_OUT");
        if (string.IsNullOrEmpty(outDir)) outDir = "Logs/rails-vetting";
        Directory.CreateDirectory(outDir);
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity");
        var cam = Camera.main;
        var walls = new[] { GameObject.Find("leftPipe"), GameObject.Find("rightPipe") };
        var railX = typeof(enmiesOnBoard).GetMethod("WorldRailX", BindingFlags.NonPublic | BindingFlags.Static);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        EnemyThreat.ForceShooting = true;

        foreach (var shape in new[] { new Vector2Int(1080, 2520), new Vector2Int(1080, 1920) })
        {
            cam.aspect = shape.x / (float)shape.y;
            cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, shape.x, shape.y);
            string shapeTag = shape.x + "x" + shape.y;
            for (int w = 0; w < WorldManager.Worlds.Length; w++)
            {
                var theme = WorldManager.Worlds[w];
                PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, w);
                moveBackGround.speed = .2f;
                var wbgo = new GameObject("~VetBackdrop");
                var wb = wbgo.AddComponent<WorldBackdrop>();
                WorldPainter.Apply(theme);
                foreach (var wall in walls)
                {
                    var s = wall.transform.localScale;
                    var mesh = wall.GetComponent<MeshFilter>().sharedMesh;
                    s.y = cam.orthographicSize * 2f * 1.085f / mesh.bounds.size.y;   // RailFit
                    wall.transform.localScale = s;
                    RailFit.RefreshTextureTiling(wall);
                }
                BossRails.Measure();
                wb.Show(theme.displayName, false);
                for (int i = 0; i < 30; i++) wb.Step(Dt);

                float left = (float)railX.Invoke(null, new object[] { true }), right = (float)railX.Invoke(null, new object[] { false });
                float inL, outL, inR, outR;
                WorldPainter.VisibleRailEdges(walls[0], out inL, out outL);
                WorldPainter.VisibleRailEdges(walls[1], out inR, out outR);
                Debug.Log(string.Format("[VET] {0} {1}: ortho {2:F3} view top {3:F2} half width {4:F2} | rail art inner {5:F3}/{6:F3} outer {7:F3} | " +
                                        "mine x {8:F3}/{9:F3} | BossRails.InnerEdge {10:F3}",
                                        shapeTag, theme.displayName, cam.orthographicSize, CameraFit.ViewTop, cam.orthographicSize * cam.aspect,
                                        inL, inR, outL, left, right, BossRails.InnerEdge));

                var ship = new GameObject("~VetShip").transform;
                ship.position = new Vector3(.4f, CameraFit.ViewBottom + cam.orthographicSize * .45f, 0f);
                var root = new GameObject("~VetStage");
                var mineDef = EnemyRoster.One(w, EnemyRole.Mine);
                float top = CameraFit.ViewTop, h = cam.orthographicSize;
                // left rail: at rest, and one that charges and fires; right rail: mid-slide, at rest
                Mine(root, mineDef, left, top - h * .55f, ship, 0, 0f);
                Mine(root, mineDef, left, top - h * 1.05f, ship, mineDef.Behaviour.Shoots ? 2 : 1, 0f);
                Mine(root, mineDef, right, top - h * .8f, ship, 0, mineDef.Behaviour.vertical != EnemyVertical.None ? .6f : 0f);
                Mine(root, mineDef, right, top - h * 1.35f, ship, 1, 0f);

                // a pilot on station, a heavy, an elite
                Fly(root, EnemyRoster.Fighter(w, 4), -.9f, ship);
                Fly(root, EnemyRoster.One(w, EnemyRole.Big), .9f, ship);
                var defs = new System.Collections.Generic.List<EliteDef>();
                if (EliteCatalog.ForWorld(w, defs) > 0)
                {
                    var e = new GameObject("~VetElite");
                    e.transform.SetParent(root.transform, false);
                    e.transform.position = new Vector3(-1.1f, top - h * 1.3f, 0f);
                    var sr = e.AddComponent<SpriteRenderer>();
                    sr.sprite = EliteArt.Frame(defs[0], defs[0].cells.flight != null && defs[0].cells.flight.Length > 0 ? defs[0].cells.flight[0] : 0);
                    sr.sortingOrder = 5;
                }
                EliteSystem.Step(Dt);

                string name = outDir + "/" + theme.displayName.ToLowerInvariant() + "-" + shapeTag;
                Shot(cam, shape.x, shape.y, name + ".png");
                var guides = Guides(root, top, CameraFit.ViewBottom);
                Shot(cam, shape.x, shape.y, name + "-guides.png");
                Object.DestroyImmediate(guides);

                Object.DestroyImmediate(root);
                foreach (var id in Object.FindObjectsByType<EnemyIdentity>(FindObjectsSortMode.None)) Object.DestroyImmediate(id.gameObject);
                foreach (var r in Object.FindObjectsByType<RailLaneScroller>(FindObjectsSortMode.None)) Object.DestroyImmediate(r.gameObject);
                EliteSystem.Clear();
                PilotAirspace.Clear();
                EnemyThreat.Reset();
                Object.DestroyImmediate(ship.gameObject);
                Object.DestroyImmediate(wbgo);
            }
        }
        EnemyThreat.ForceShooting = false;
    }

    // mode 0: at rest; 1: charging (the tell held); 2: charged and just fired.
    static void Mine(GameObject root, EnemyDef def, float x, float y, Transform ship, int mode, float slide)
    {
        var rail = new GameObject("RailMineLane");
        rail.transform.position = new Vector3(x, y, 0f);
        rail.AddComponent<RailLaneScroller>();
        var go = EnemyFactory.Create(def, new Vector3(x, y, 0f), Quaternion.identity);
        go.GetComponent<SpriteRenderer>().flipX = x > 0f;
        var mount = go.AddComponent<RailMineMount>();
        mount.MountTo(rail.transform);
        var brain = go.GetComponent<EnemyBrain>();
        mount.brain = brain;
        brain.TargetOverride = ship;
        var fb = go.GetComponent<EnemyFlipbook>();
        if (slide != 0f) { mount.Slide = slide; mount.SendMessage("LateUpdate"); return; }
        if (mode == 0) return;
        float clock = 500f + y;
        for (int i = 0; i < 600; i++)
        {
            clock += Dt;
            SpawnSpace.ClockOverride = clock;
            brain.Step(Dt);
            mount.SendMessage("LateUpdate");
            fb.Advance(Dt);
            EliteSystem.Step(Dt);
            if (mode == 1 && brain.State == EnemyBrain.Phase.Windup && brain.StateTime > .3f) break;
            if (mode == 2 && brain.ShotsFired > 0 && brain.State == EnemyBrain.Phase.Idle) { for (int k = 0; k < 14; k++) EliteSystem.Step(Dt); break; }
        }
        if (mode == 1 && brain.State != EnemyBrain.Phase.Windup) fb.Drive(EnemyFlipbook.DrivePhase.Windup);   // a mine that does not shoot: its arming loop
        SpawnSpace.ClockOverride = null;
    }

    static void Fly(GameObject root, EnemyDef def, float x, Transform ship)
    {
        var go = EnemyFactory.Create(def, new Vector3(x, CameraFit.ViewTop + .5f, 0f), Quaternion.identity);
        var brain = go.GetComponent<EnemyBrain>();
        brain.TargetOverride = ship;
        var fb = go.GetComponent<EnemyFlipbook>();
        float clock = 900f + x;
        for (int i = 0; i < 60 * 8 && brain.Stage != EnemyBrain.PilotStage.Engaging; i++) { clock += Dt; SpawnSpace.ClockOverride = clock; brain.Step(Dt); }
        for (int i = 0; i < 60 * 4 && brain.State != EnemyBrain.Phase.Windup; i++) { clock += Dt; SpawnSpace.ClockOverride = clock; brain.Step(Dt); fb.Advance(Dt); }
        for (int i = 0; i < 20; i++) { clock += Dt; SpawnSpace.ClockOverride = clock; brain.Step(Dt); fb.Advance(Dt); }
        SpawnSpace.ClockOverride = null;
    }

    static Texture2D white;

    static GameObject Guides(GameObject root, float top, float bottom)
    {
        if (white == null) { white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply(); }
        var sprite = Sprite.Create(white, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1f);
        var g = new GameObject("~VetGuides");
        void Line(float x, Color c)
        {
            foreach (float side in new[] { -1f, 1f })
            {
                var go = new GameObject("guide");
                go.transform.SetParent(g.transform, false);
                go.transform.position = new Vector3(side * x, (top + bottom) * .5f, 0f);
                go.transform.localScale = new Vector3(.025f, top - bottom, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.color = c;
                sr.sortingOrder = 200;
            }
        }
        Line(SpawnLane.LaneHalf, new Color(.2f, 1f, 1f, .9f));
        Line(2.4f, new Color(1f, 1f, 1f, .9f));
        Line(BossRails.InnerEdge, new Color(1f, .95f, .2f, .9f));
        return g;
    }

    static void Shot(Camera cam, int w, int h, string path)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
    }
}
