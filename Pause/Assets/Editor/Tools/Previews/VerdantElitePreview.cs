using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Frames of each of Verdant's four new elites' attack moments over the Verdant backdrop at phone portrait, for review (VerdantElites.cs):
//   Timber Hauler  the ringed spots and roll lanes in the tell, the trunks lobbed, landing and rolling
//   Thornlash      the whip's swept area in the tell, the sweep, the retract
//   Sporebloom     the burst rings and pod lines in the tell, the pods, the rings of spores
//   Leafblade      the dive lane and crescent row in the tell, the dive, the crescent
// Each frame is logged "[VEP] <key> <frame> <state>" so a contact sheet can pick tell / live frames.
//
//   scripts/unity-batch.sh -executeMethod VerdantElitePreview.Run
//   (frames to $VERDANT_ELITE_PREVIEW_DIR/<key>/NNN.png, else Builds/VerdantElitePreview)
public static class VerdantElitePreview
{
    const float Fps = 15f, HalfH = 6.4f, HalfW = 2.95f;
    const int Width = 432;
    static Camera cam;
    static Transform pilot;
    static TextMesh label;
    static string dir;
    static int frame;
    static EliteShip e;

    public static void Run()
    {
        string root = System.Environment.GetEnvironmentVariable("VERDANT_ELITE_PREVIEW_DIR");
        if (string.IsNullOrEmpty(root)) root = "Builds/VerdantElitePreview";
        Directory.CreateDirectory(root);
        using (var sandbox = new TestHarness.Sandbox())
        {
            EliteCatalog.Reload();
            foreach (var key in new[] { "verdant_elite_timber_hauler", "verdant_elite_thornlash", "verdant_elite_sporebloom", "verdant_elite_leafblade" })
            {
                try { Clip(root, EliteCatalog.Find(key)); }
                catch (System.Exception ex) { Debug.LogError("[VerdantElitePreview] " + key + ": " + ex); }
            }
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
        }
        EditorApplication.Exit(0);
    }

    static void Fresh()
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .3f;
        RunScore.BeginRun(true, false);
        Random.InitState(1357);
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(.05f, .04f, .11f);
        cam.orthographicSize = HalfH;
        cam.aspect = HalfW / HalfH;
        cam.transform.position = new Vector3(0f, 0f, -10f);
        var wb = WorldBackdrop.Create("Verdant");
        wb.Show("Verdant", false);
        for (int i = 0; i < 30; i++) wb.Step(1f / Fps);
        pilot = new GameObject("~Pilot").transform;
        var sr = pilot.gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = shopingShips.SpriteFor(1, 0);
        sr.sortingOrder = 6;
        float s = shopingShips.NormalizedHullScale(sr.sprite);
        pilot.localScale = new Vector3(s, s, 1f);
        pilot.position = new Vector3(.6f, -3f, 0f);
        EliteSystem.PlayerOverride = pilot;
        var go = new GameObject("~Label");
        label = go.AddComponent<TextMesh>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        go.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
        go.GetComponent<MeshRenderer>().sortingOrder = 100;
        label.characterSize = .05f;
        label.fontSize = 48;
        label.anchor = TextAnchor.UpperCenter;
        label.color = new Color(.85f, 1f, .8f);
        go.transform.position = new Vector3(0f, HalfH - .2f, -2f);
    }

    static void Tick(float seconds, bool shoot = true)
    {
        float dt = 1f / Fps;
        for (float t = 0f; t < seconds - 1e-4f; t += dt)
        {
            WorldBackdrop.Instance.Step(dt);
            EliteSystem.Step(dt);
            if (e != null) { var h = e.GetComponent<EliteHearts>(); if (h != null) { h.Place(dt, dt); h.StepBreaks(dt); } }
            string st = e == null || e.State == EliteState.Dead ? "DOWN" : e.Telling ? "TELL" : e.Acting ? "ACTION" : e.State.ToString().ToUpperInvariant();
            label.text = (e != null ? e.Def.displayName.ToUpperInvariant() : "") + "  " + st;
            if (shoot) Debug.Log("[VEP] " + e.Def.key + " " + frame + " " + st);
            if (shoot) Shoot(Path.Combine(dir, frame.ToString("000") + ".png"));
            frame++;
        }
    }

    static void Attack()
    {
        if (e == null) return;
        e.ForceAttack();
        for (int g = 0; g < 60 && e != null && e.State == EliteState.Attack; g++) Tick(1f / Fps);
    }

    static void Clip(string root, EliteDef def)
    {
        if (def == null) return;
        Fresh();
        dir = Path.Combine(root, def.key);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        frame = 0;
        e = EliteShip.CreateInPlay(def, new Vector2(-.3f, 1.4f));
        e.AttackCooldown = 99f;
        Tick(1.6f, false);
        Attack();
        Tick(2.4f);
        e.AttackCooldown = 99f;
        Debug.Log("[VerdantElitePreview] " + def.key + ": " + frame + " frames -> " + dir);
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
