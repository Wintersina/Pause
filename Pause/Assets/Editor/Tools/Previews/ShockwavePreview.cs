using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders the shield's release shockwave (ShieldShockwave) over real backdrops, for review:
// a shielded ship with real enemy sprites around it, frames at 0, .1, .2, .3 and .4 s over Space,
// Frost, Verdant and Ember. Per world: shockwave-<world>-strip.png (cropped around the ship, the
// five frames side by side) and shockwave-<world>-full-0.2.png (the whole screen).
//
//   SHOCKWAVE_PREVIEW_DIR=<dir> Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod ShockwavePreview.Run
public static class ShockwavePreview
{
    const int W = 1080, H = 2400, Crop = 760;
    const float Dt = 1f / 60f;
    static readonly string[] Worlds = { "Space", "Frost", "Verdant", "Ember" };

    public static void Run()
    {
        string dir = System.Environment.GetEnvironmentVariable("SHOCKWAVE_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/ShockwavePreview";
        Directory.CreateDirectory(dir);
        int failures = 0;
        using (new TestHarness.Sandbox())
        {
            for (int w = 0; w < Worlds.Length; w++)
            {
                try { One(dir, w); }
                catch (System.Exception e) { Debug.LogException(e); failures++; }
            }
            EnemyShove.Clear();
            EliteSystem.PlayerOverride = null;
            EliteSystem.Clear();
            PlayField.Reset();
            ScreenInfo.ClearOverride();
        }
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    // SHOCKWAVE_PREVIEW_DIR=<dir> ... -executeMethod ShockwavePreview.RunCrash
    // Three heavy rocks shoved into a pack of fighters: frames at 0, .15, .3 and .5 s (shockwave-crash-strip.png,
    // the whole screen at .3 s as shockwave-crash-full-0.3.png).
    public static void RunCrash()
    {
        string dir = System.Environment.GetEnvironmentVariable("SHOCKWAVE_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/ShockwavePreview";
        Directory.CreateDirectory(dir);
        int failures = 0;
        using (new TestHarness.Sandbox())
        {
            try { One(dir, 0, true); }
            catch (System.Exception e) { Debug.LogException(e); failures++; }
            EnemyShove.Clear();
            EliteSystem.PlayerOverride = null;
            EliteSystem.Clear();
            PlayField.Reset();
            ScreenInfo.ClearOverride();
        }
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    static void One(string dir, int world, bool crash = false)
    {
        string name = Worlds[world];
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity");
        BossEncounter.ResetRun();
        BossRails.Reset();
        PortalPressure.Reset();
        RunLoop.Reset();
        var cam = Camera.main;
        cam.aspect = W / (float)H;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, W, H);
        PlayField.Reset();
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        score.pauseCounter = 0;
        Time.timeScale = 1f;
        moveBackGround.speed = .25f;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        Random.InitState(7);
        var paused = SceneUtil.FindAny("paused");
        if (paused != null) paused.SetActive(false);
        WorldPainter.Apply(WorldManager.Worlds[world]);
        foreach (var n in new[] { "leftPipe", "rightPipe" })
        {
            var wall = GameObject.Find(n);
            if (wall == null) continue;
            var sc = wall.transform.localScale;
            sc.y = cam.orthographicSize * 2f * 1.085f / wall.GetComponent<MeshFilter>().sharedMesh.bounds.size.y;
            wall.transform.localScale = sc;
            RailFit.RefreshTextureTiling(wall);
        }
        BossRails.Measure();
        ShieldShockwave.ResetGuards();
        EnemyShove.Clear();

        var wb = WorldBackdrop.Create(name);
        if (name != "Space") wb.Show("Space", false);
        wb.Show(name, false);
        for (int i = 0; i < (name == "Space" ? 240 : 60 * 14); i++) wb.Step(Dt);

        var ship = new GameObject("~PfSpawner").AddComponent<spawnShips>().Spawn(ShipId.Starter);
        ship.transform.position = new Vector3(0f, -1.6f, 0f);
        var shield = ShipShield.For(ship);
        shield.remainingOverride = 5f;
        shield.Show();
        for (int i = 0; i < 40; i++) shield.Tick(Dt);

        if (crash) { CrashScene(dir, name, cam, ship); return; }
        // real enemies: around the ship, in its column, beyond the ring
        Vector2[] spots = { new Vector2(1.3f, -1.2f), new Vector2(-1.4f, -2.3f), new Vector2(.1f, .3f), new Vector2(-.9f, 1.3f), new Vector2(1.5f, 2.2f), new Vector2(-2.2f, -.4f) };
        int placed = 0;
        foreach (var def in EnemyRoster.All)
        {
            if (placed >= spots.Length) break;
            if (def.role == EnemyRole.Chaser || def.role == EnemyRole.Mine) continue;
            var go = EnemyFactory.Create(def, new Vector3(spots[placed].x, spots[placed].y, 0f), Quaternion.identity);
            var brain = go.GetComponent<EnemyBrain>();
            if (brain != null) brain.TargetOverride = ship.transform;
            placed++;
        }
        shield.Hide();   // the 5.8 s ran out: ShipShield.Hide -> ShieldShockwave.TryRelease

        Vector3 vp = cam.WorldToViewportPoint(ship.transform.position);
        Vector3 sp = new Vector3(vp.x * W, vp.y * H, 0f);
        int cx = Mathf.Clamp(Mathf.RoundToInt(sp.x) - Crop / 2, 0, W - Crop), cy = Mathf.Clamp(Mathf.RoundToInt(sp.y) - Crop / 2, 0, H - Crop);
        var times = new[] { 0f, .1f, .2f, .3f, .4f };
        var strip = new Texture2D(Crop * times.Length, Crop, TextureFormat.RGB24, false);
        float t = 0f;
        for (int k = 0; k < times.Length; k++)
        {
            while (t < times[k] - 1e-4f)
            {
                ShieldShockwaveFx.Instance.Tick(Dt);
                if (ShieldShards.Instance != null) ShieldShards.Instance.Tick(Dt);
                t += Dt;
            }
            var full = Render(cam);
            strip.SetPixels(Crop * k, 0, Crop, Crop, full.GetPixels(cx, cy, Crop, Crop));
            if (k == 2) File.WriteAllBytes(Path.Combine(dir, "shockwave-" + name + "-full-0.2.png"), full.EncodeToPNG());
            Object.DestroyImmediate(full);
        }
        File.WriteAllBytes(Path.Combine(dir, "shockwave-" + name + "-strip.png"), strip.EncodeToPNG());
        Object.DestroyImmediate(strip);
        Object.DestroyImmediate(wb.gameObject);
    }

    static void CrashScene(string dir, string name, Camera cam, GameObject ship)
    {
        ShoveCrash.Clear();
        ShoveCrash.ResetCounters();
        EliteSystem.PlayerOverride = ship.transform;
        Vector2 at = ship.transform.position;
        // three heavy rocks hugging the ship (left, right, above), a pack of fighters just beyond each
        var rockDef = EnemyRoster.One(0, EnemyRole.Rock);
        var fightDef = EnemyRoster.One(0, EnemyRole.Fighter);
        Vector2[] rocks = { new Vector2(.85f, .15f), new Vector2(-.85f, -.1f), new Vector2(.1f, .95f) };
        Vector2[] dirs = { new Vector2(1f, .15f).normalized, new Vector2(-1f, -.1f).normalized, new Vector2(.1f, 1f).normalized };
        for (int k = 0; k < 3; k++)
        {
            var rock = EnemyFactory.Create(rockDef, at + rocks[k], Quaternion.identity);
            if (rock.TryGetComponent(out EnemyIdentity id)) id.SetScale(1.4f);
            Vector2 perp = new Vector2(-dirs[k].y, dirs[k].x);
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = at + rocks[k] + dirs[k] * (.5f + (i / 2) * .45f) + perp * ((i % 2 == 0 ? -1 : 1) * .18f);
                EnemyFactory.Create(fightDef, new Vector3(p.x, p.y, 0f), Quaternion.identity);
            }
        }
        var shield = ShipShield.For(ship);
        shield.Hide();
        Vector3 vp = cam.WorldToViewportPoint(ship.transform.position);
        Vector3 sp = new Vector3(vp.x * W, vp.y * H, 0f);
        int cx = Mathf.Clamp(Mathf.RoundToInt(sp.x) - Crop / 2, 0, W - Crop), cy = Mathf.Clamp(Mathf.RoundToInt(sp.y) - Crop / 2, 0, H - Crop);
        var times = new[] { 0f, .15f, .3f, .5f };
        var strip = new Texture2D(Crop * times.Length, Crop, TextureFormat.RGB24, false);
        float t = 0f;
        for (int k = 0; k < times.Length; k++)
        {
            while (t < times[k] - 1e-4f)
            {
                ShieldShockwaveFx.Instance.Tick(Dt);   // the shoves and the crashes
                EliteSystem.Step(Dt);                  // the sparks
                if (ShieldShards.Instance != null) ShieldShards.Instance.Tick(Dt);
                t += Dt;
            }
            var full = Render(cam);
            strip.SetPixels(Crop * k, 0, Crop, Crop, full.GetPixels(cx, cy, Crop, Crop));
            if (k == 2) File.WriteAllBytes(Path.Combine(dir, "shockwave-crash-full-0.3.png"), full.EncodeToPNG());
            Object.DestroyImmediate(full);
        }
        File.WriteAllBytes(Path.Combine(dir, "shockwave-crash-strip.png"), strip.EncodeToPNG());
        Object.DestroyImmediate(strip);
        Debug.Log("[CRASHPREVIEW] hits " + ShoveCrash.Hits + ", kills " + ShoveCrash.Kills + ", recoils " + ShoveCrash.Recoils);
    }

    static Texture2D Render(Camera cam)
    {
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        var png = new Texture2D(W, H, TextureFormat.RGB24, false);
        png.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        png.Apply();
        RenderTexture.active = old;
        cam.targetTexture = previous;
        Object.DestroyImmediate(rt);
        return png;
    }
}
