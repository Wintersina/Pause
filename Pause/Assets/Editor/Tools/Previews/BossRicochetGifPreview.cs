using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// RICOCHETS (BossBounce): renders a boss fight as the game plays it with the boss song's wave director -- the real game
// scene (backdrop, the world's rails measured by BossRails.Measure, the real ship), a manual music clock (drive, then a
// build) attached as the actor's BeatOverride, a dodge bot flying the ship -- so the ring / fan waves' bouncy bullets
// (the dashed cue ring) can be seen ricocheting off the rails. Every third step of a 60 Hz sim (20 fps) at 540x1170, as
// numbered PNGs. (Adapted from the boss-fight GIF preview used for the wave captures.)
//
//   BOSSGIF_BOSS=Frost BOSSGIF_DIR=/path/frames BOSSGIF_META=/path/meta.txt Unity -batchmode -quit -projectPath Pause -executeMethod BossRicochetGifPreview.Run
public static class BossRicochetGifPreview
{
    const int Width = 540, Height = 1170;
    const float Dt = 1f / 60f;
    const int Every = 3;
    const double Spb = .4;                       // seconds per beat
    // timeline (fight seconds)
    const float DriveEnd = 8f, Total = 14f;

    sealed class Manual : IMusicBeatSource
    {
        public double T;
        public bool Active { get; set; } = true;
        public double BeatNow => T / Spb;
        public double SecondsPerBeat => Spb;
        public int BeatsPerBar => 4;
        public int DownbeatBeat => 0;
        public MusicMoment MusicNow => At(T);
        public MusicMoment MusicAhead(double beats) => At(T + beats * Spb);
        public static MusicMoment At(double t)
        {
            if (t < DriveEnd) return new MusicMoment { energy = .55f, bass = .5f, accent = .4f, section = 0, kind = "drive" };
            return new MusicMoment { energy = .8f, bass = .8f, accent = .6f, section = 1, kind = "build" };
        }
    }

    static string Env(string k, string d) { var v = System.Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? d : v; }

    public static void Run()
    {
        string key = Env("BOSSGIF_BOSS", "Frost");
        string dir = Env("BOSSGIF_DIR", "Builds/BossFightGif/" + key);
        string meta = Env("BOSSGIF_META", dir + "/meta.txt");
        Directory.CreateDirectory(dir);
        int code = 0;
        using (new TestHarness.Sandbox())
        {
            try { code = Render(key, dir, meta); }
            catch (System.Exception ex) { Debug.LogError("[RICOGIF] FAILED " + ex); code = 1; }
            finally { BossEncounter.ResetRun(); BossTelegraphStyle.ForcedIntensity = -1f; }
        }
        EditorApplication.Exit(code);
    }

    static int Render(string key, string dir, string metaPath)
    {
        int world = 0;
        for (int i = 0; i < BossCatalog.All.Length; i++) if (BossCatalog.All[i].artKey == key) world = i;
        int themeIdx = 0;
        for (int i = 0; i < WorldManager.Worlds.Length; i++) if (WorldManager.Worlds[i].displayName == key) themeIdx = i;
        var theme = WorldManager.Worlds[themeIdx];

        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity");
        BossRails.Reset(); PlayField.Reset(); BossEncounter.ResetRun();
        var cam = Camera.main;
        cam.aspect = Width / (float)Height;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, Width, Height);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .37f;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, themeIdx);
        var wb = new GameObject("~BgBackdrop").AddComponent<WorldBackdrop>();
        WorldPainter.Apply(theme);
        foreach (var name in new[] { "leftPipe", "rightPipe" })
        {
            var wall = GameObject.Find(name);
            if (wall == null) continue;
            var sc = wall.transform.localScale;
            sc.y = cam.orthographicSize * 2f * 1.085f / wall.GetComponent<MeshFilter>().sharedMesh.bounds.size.y;
            wall.transform.localScale = sc;
            RailFit.RefreshTextureTiling(wall);
        }
        BossRails.Measure();
        wb.Show(theme.displayName, false);
        for (int i = 0; i < 30; i++) wb.Step(Dt);

        foreach (var pl in Object.FindObjectsByType<PausedLabel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var icon = pl.transform.parent;
            pl.gameObject.SetActive(false);
            if (icon != null) icon.gameObject.SetActive(false);
        }
        var pausedIcon = SceneUtil.FindAny("paused");   // the scene's pause wordmark (a still sprite in the edit-mode scene)
        if (pausedIcon != null) pausedIcon.SetActive(false);
        var ship = new GameObject("~BgSpawner").AddComponent<spawnShips>().Spawn(ShipId.Starter);
        var shipSr = ship.GetComponent<SpriteRenderer>();
        if (shipSr != null) shipSr.sortingOrder = 40;
        float shipY = ShipReach.StartY;
        float reach = ShipReach.HalfWidth;
        ship.transform.position = new Vector3(0f, shipY, 0f);

        BossEncounter.Begin(world, null);
        var e = BossEncounter.Instance;
        typeof(BossEncounter).GetField("player", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(e, ship.transform);
        e.Step(.1f, 1f);
        for (int i = 0; i < 400 && e.State != BossEncounter.Phase.Fight; i++) e.Step(.1f, 1f);
        if (e.State != BossEncounter.Phase.Fight) throw new System.Exception("fight never began: " + e.State);
        var actor = e.Actor;
        var music = new Manual();
        music.T = 0;
        actor.BeatOverride = music;


        var rt = new RenderTexture(Width, Height, 24);
        cam.targetTexture = rt;
        var png = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        int n = 0, steps = Mathf.RoundToInt(Total / Dt);
        float vx = 0f, x = 0f;
        int nearMiss = 0, wouldHit = 0; bool inHit = false;
        int maxBouncy = 0;
        for (int step = 0; step < steps; step++)
        {
            float t = step * Dt;
            music.T = t;

            // dodge bot: pick the lane that keeps the most clearance from every shot reaching the ship's height soon
            float best = x, bestCost = float.MaxValue;
            for (float cx = -reach; cx <= reach + .001f; cx += .12f)
            {
                float cost = Mathf.Abs(cx - x) * .35f + Mathf.Abs(cx) * .05f;
                foreach (var p in e.Pool.Shots)
                {
                    if (p == null || !p.Active) continue;
                    Vector2 pos = p.transform.position, v = p.Velocity;
                    float dy = pos.y - shipY;
                    if (dy < -.6f || dy > 4.2f) continue;
                    float tt = v.y < -.01f ? Mathf.Max(0f, dy / -v.y) : 0f;
                    if (tt > 1.1f) continue;
                    float px = pos.x + v.x * tt;
                    float d = Mathf.Abs(px - cx);
                    if (d < .85f) cost += (.85f - d) * 14f * (1.2f - tt * .6f);
                }
                if (cost < bestCost) { bestCost = cost; best = cx; }
            }
            float want = Mathf.Clamp((best - x) * 9f, -6.5f, 6.5f);
            vx = Mathf.Lerp(vx, want, .35f);
            x = Mathf.Clamp(x + vx * Dt, -reach, reach);
            ship.transform.position = new Vector3(x, shipY, 0f);
            ship.transform.rotation = Quaternion.Euler(0f, 0f, -vx * 2.2f);

            e.Step(Dt, 1f);
            maxBouncy = Mathf.Max(maxBouncy, e.Pool.ActiveBouncy);
            if (Env("BOSSGIF_SCROLL", "1") == "1") wb.Step(Dt);   // 0: a still backdrop (a GIF cannot hold a scrolling one in 15 MB)
            if (e.State != BossEncounter.Phase.Fight) break;

            // would-be hits (the sandbox has no player collider): a shot within a hull radius
            bool hit = false;
            foreach (var p in e.Pool.Shots)
            {
                if (p == null || !p.Active) continue;
                if (((Vector2)p.transform.position - (Vector2)ship.transform.position).sqrMagnitude < .32f * .32f) { hit = true; break; }
            }
            if (hit && !inHit) wouldHit++;
            inHit = hit;

            if (step % Every != 0) continue;
            foreach (var pl in Object.FindObjectsByType<PausedLabel>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (pl.transform.parent != null) pl.transform.parent.gameObject.SetActive(false);
                pl.gameObject.SetActive(false);
            }
            foreach (var ps in Object.FindObjectsByType<moveStarsBackground>(FindObjectsInactive.Include, FindObjectsSortMode.None)) ps.enabled = false;
            cam.Render();
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            png.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            png.Apply();
            RenderTexture.active = old;
            File.WriteAllBytes(Path.Combine(dir, "f" + (n++).ToString("0000") + ".png"), png.EncodeToPNG());
        }
        var dirc = actor.MusicDirector;
        var sb = new StringBuilder();
        sb.Append("boss=").Append(key).Append('\n')
          .Append("frames=").Append(n).Append('\n')
          .Append("waves=").Append(dirc.Waves.Waves).Append('\n')
          .Append("waveEmissions=").Append(dirc.WaveEmissions).Append('\n')
          .Append("accents=").Append(dirc.AccentEmissions).Append('\n')
          .Append("emissions=").Append(dirc.Emissions).Append('\n')
          .Append("bouncyPicked=").Append(dirc.Waves.bounce.picked).Append(" of ").Append(dirc.Waves.bounce.eligible).Append('\n')
          .Append("ricochetSparks=").Append(e.Pool.RicochetSparks).Append('\n')
          .Append("maxBouncyLive=").Append(maxBouncy).Append('\n')
          .Append("railInnerEdge=").Append(BossRails.InnerEdge.ToString("F3")).Append('\n')
          .Append("hitsWouldBe=").Append(wouldHit).Append('\n');
        File.WriteAllText(metaPath, sb.ToString());
        Debug.Log("[RICOGIF] " + key + " " + sb.ToString().Replace('\n', ' '));
        cam.targetTexture = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(png);
        if (dirc.WaveEmissions <= 0) { Debug.LogError("[RICOGIF] FAIL: " + key + " emitted NO waves"); return 2; }
        return 0;
    }
}
