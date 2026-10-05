using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders each elite's whole life cycle over its world's real backdrop, for
// review: parked on a landing site the backdrop reports -> engine-light
// tell -> lift-off -> join -> follow -> attack (tell, action) -> a hit ->
// a crash that kills it (into an enemy craft dropped in its path) -> the debris.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod ElitePreview.Run
//   (frames to $ELITE_PREVIEW_DIR/elite-<key>/NNN.png, else Builds/ElitePreview;
//    ELITE_PREVIEW_ONLY=<key>[,<key>...] renders just those)
//
// then .claude/skills/add-elite-ship/scripts/make_preview_gif.py <dir> turns
// every frame folder into elite-<key>.gif + elite-<key>-sheet.png.
public static class ElitePreview
{
    const float Fps = 15f;
    const int Width = 432;
    const float HalfH = 6.4f, HalfW = 2.95f;

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("ELITE_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/ElitePreview";
        string only = System.Environment.GetEnvironmentVariable("ELITE_PREVIEW_ONLY");
        Directory.CreateDirectory(dir);
        using (var sandbox = new TestHarness.Sandbox())
        {
            EliteCatalog.Reload();
            foreach (var def in EliteCatalog.All)
            {
                if (!string.IsNullOrEmpty(only) && System.Array.IndexOf(only.Split(','), def.key) < 0) continue;
                try { Clip(dir, def); }
                catch (System.Exception e) { Debug.LogError("[ElitePreview] " + def.key + ": " + e); }
            }
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            LandingSites.Override = null;
        }
        EditorApplication.Exit(0);
    }

    static Camera cam;
    static Transform pilot;

    static void Fresh(EliteDef def)
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .3f;
        RunScore.BeginRun(true, false);
        Random.InitState(def.key.Length * 97);

        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(.05f, .04f, .11f);
        cam.orthographicSize = HalfH;
        cam.aspect = HalfW / HalfH;
        cam.transform.position = new Vector3(0f, 0f, -10f);

        string world = def.WorldIndex >= 0 && def.WorldIndex < WorldManager.Worlds.Length ? WorldManager.Worlds[def.WorldIndex].displayName : "Ember";
        var wb = WorldBackdrop.Create(world);
        wb.Show(world, false);

        Rails();
        pilot = new GameObject("~Pilot").transform;
        var sr = pilot.gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = shopingShips.SpriteFor(1, 0);
        sr.sortingOrder = 6;
        float s = shopingShips.NormalizedHullScale(sr.sprite);
        pilot.localScale = new Vector3(s, s, 1f);
        pilot.position = new Vector3(0f, -3f, 0f);
        EliteSystem.PlayerOverride = pilot;
    }

    static Sprite white;
    static Sprite White()
    {
        if (white != null) return white;
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, Color.white);
        t.Apply();
        white = Sprite.Create(t, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1f);
        return white;
    }

    // Rusty rails with pink lamps and cyan tubes, at the rails' inner faces.
    static void Rails()
    {
        float edge = EliteSystem.RailEdge;
        for (int side = -1; side <= 1; side += 2)
        {
            var go = new GameObject("~Rail");
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = White();
            r.color = new Color(.22f, .16f, .2f);
            r.sortingOrder = 0;
            go.transform.position = new Vector3(side * (edge + .5f), 0f, 0f);
            go.transform.localScale = new Vector3(1f, HalfH * 2f, 1f);
            for (int i = 0; i < 9; i++)
            {
                var lamp = new GameObject("~Lamp").AddComponent<SpriteRenderer>();
                lamp.sprite = White();
                lamp.color = i % 3 == 0 ? new Color(.4f, .95f, 1f) : new Color(1f, .3f, .55f);
                lamp.sortingOrder = 1;
                lamp.transform.position = new Vector3(side * (edge + .12f), -HalfH + 1.4f * i + .3f, 0f);
                lamp.transform.localScale = new Vector3(.08f, i % 3 == 0 ? .5f : .18f, 1f);
            }
        }
    }

    static void Clip(string root, EliteDef def)
    {
        Fresh(def);
        string dir = Path.Combine(root, "elite-" + def.key);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        int frame = 0;
        float dt = 1f / Fps;

        // let the backdrop bring a landmark (volcano, glacier, ruin) into the landing band
        var sites = new List<LandingSite>();
        for (int i = 0; i < 400 && LandingSites.Collect(sites) == 0; i++) WorldBackdrop.Instance.Step(dt);
        LandingSite site;
        if (sites.Count > 0) site = sites[0];
        else
        {
            var pad = new GameObject("~Pad").transform;
            pad.position = new Vector3(1f, 2.5f, 0f);
            site = new LandingSite { anchor = pad, scale = .3f, order = -420, id = 1 };
        }
        var e = EliteShip.Create(def, site, 2.2f, EliteDirector.JoinPoint(def, EliteBrains.Create(def.brain).JoinFrom, site.Position));
        var label = Label(def.displayName.ToUpperInvariant() + "  " + def.role);
        var state = Label("");
        state.transform.position = new Vector3(0f, HalfH - .75f, -2f);

        System.Action<float> tick = seconds =>
        {
            for (float t = 0f; t < seconds - 1e-4f; t += dt)
            {
                pilot.position = new Vector3(Mathf.Sin(frame * .04f) * .9f, -3f + Mathf.Sin(frame * .023f) * .4f, 0f);
                WorldBackdrop.Instance.Step(dt);
                EliteSystem.Step(dt);
                var h = e != null ? e.GetComponent<EliteHearts>() : null;
                if (h != null) { h.Place(dt, dt); h.StepBreaks(dt); }
                state.text = e == null ? "DOWN" : e.State == EliteState.Attack ? (e.Telling ? "ATTACK: TELL" : "ATTACK: ACTION") :
                             e.State.ToString().ToUpperInvariant() + (e.State == EliteState.Parked && e.EngineLightsOn ? " (ENGINES)" : "");
                Shoot(Path.Combine(dir, frame.ToString("000") + ".png"));
                frame++;
            }
        };

        tick(2.4f);                                   // parked, the tell
        for (int g = 0; g < 200 && e != null && e.State != EliteState.Follow; g++) tick(dt);
        if (e != null) e.AttackCooldown = 99f;
        tick(1.6f);                                   // follow
        if (e != null) e.ForceAttack();
        for (int g = 0; g < 200 && e != null && e.State == EliteState.Attack; g++) tick(dt);
        tick(.6f);
        if (e != null) e.TakeHit(EliteDamage.PlayerWeapon, e.transform.position + Vector3.left * .5f);
        tick(.9f);                                    // the heart darts and crumbles
        if (e != null)
        {
            // the crash: an enemy craft in its path (armoured haulers shrug off rocks)
            Vector2 at = e.Position + (e.Velocity.sqrMagnitude > .01f ? e.Velocity.normalized * .25f : Vector2.zero);
            var rock = EnemyFactory.Create(EnemyRoster.Fighter(3, 2), at, Quaternion.identity);
            ClearTarget.Ensure(rock);
        }
        tick(1.8f);                                   // the debris
        Object.DestroyImmediate(label);
        Debug.Log("[ElitePreview] " + def.key + ": " + frame + " frames -> " + dir);
    }

    static TextMesh Label(string text)
    {
        var go = new GameObject("~Label");
        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        go.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
        go.GetComponent<MeshRenderer>().sortingOrder = 100;
        tm.characterSize = .05f;
        tm.fontSize = 48;
        tm.anchor = TextAnchor.UpperCenter;
        tm.color = new Color(.75f, .95f, 1f);
        go.transform.position = new Vector3(0f, HalfH - .2f, -2f);
        return tm;
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
