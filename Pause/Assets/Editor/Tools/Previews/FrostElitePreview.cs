using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Frames of each of Frost's four newer elites' attack moments over the
// Frost backdrop at phone portrait, for review (FrostElites.cs):
//   Floe Harrower     the chutes' tell, the slab row and its gap, the sight, the lance
//   Cryo Siren        the orb on its fuse and its burst ring; then the painted beam sweep
//   Glacier Tender    drones out on tethers, the shield link soaking a shot
//   Whiteout Sentinel plates breaking into telegraphed sprays, then the charge
//
//   scripts/unity-batch.sh -executeMethod FrostElitePreview.Run
//   (frames to $FROST_ELITE_PREVIEW_DIR/<key>/NNN.png, else Builds/FrostElitePreview)
public static class FrostElitePreview
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
        string root = System.Environment.GetEnvironmentVariable("FROST_ELITE_PREVIEW_DIR");
        if (string.IsNullOrEmpty(root)) root = "Builds/FrostElitePreview";
        Directory.CreateDirectory(root);
        using (var sandbox = new TestHarness.Sandbox())
        {
            EliteCatalog.Reload();
            foreach (var key in new[] { "frost_elite_floe_harrower", "frost_elite_cryo_siren", "frost_elite_glacier_tender", "frost_elite_whiteout_sentinel" })
            {
                try { Clip(root, EliteCatalog.Find(key)); }
                catch (System.Exception ex) { Debug.LogError("[FrostElitePreview] " + key + ": " + ex); }
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
        var wb = WorldBackdrop.Create("Frost");
        wb.Show("Frost", false);
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
        label.color = new Color(.75f, .95f, 1f);
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
        switch (def.attack)
        {
            case "floe_cast":
                Attack();
                Tick(2.2f);
                break;
            case "frost_bloom":
                Attack();
                Tick(1.4f);
                ((FrostBloomAttack)e.Attack).NextIsBeam();
                Attack();
                Tick(.6f);
                break;
            case "drone_deploy":
                Attack();
                Tick(1.4f);
                e.TakeShipAttack(0, 1f, e.Position + Vector2.down * .3f);
                Tick(.8f);
                break;
            case "armour_shatter":
                Tick(1f);
                for (int i = 0; i < 3; i++) { e.TakeShipAttack(0, 1f, e.Position + Vector2.down * .3f); Tick(.7f); }
                e.AttackCooldown = 0f;
                for (int g = 0; g < 30 && !e.Telling; g++) Tick(1f / Fps);
                if (!e.Telling) e.ForceAttack();
                for (int g = 0; g < 60 && e.State == EliteState.Attack; g++) Tick(1f / Fps);
                Tick(.5f);
                break;
        }
        e.AttackCooldown = 99f;
        Debug.Log("[FrostElitePreview] " + def.key + ": " + frame + " frames -> " + dir);
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
