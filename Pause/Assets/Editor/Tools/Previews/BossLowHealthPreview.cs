using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders one boss at a given heart count through a live fight (a manual beat clock drives the per-wave tell / fire
// cadence) and saves a contact sheet of every distinct drawing it showed (idle, tells, fire, flash), so an overlaid
// frame is visible. Also logs each distinct set of enabled boss renderers.
//
//   BOSSLOW_BOSS=Frost BOSSLOW_HEARTS=2 BOSSLOW_OUT=/path/sheet.png Unity -batchmode -quit -projectPath Pause -executeMethod BossLowHealthPreview.Run
public static class BossLowHealthPreview
{
    const int Width = 1080, Height = 2340;
    const float Half = 2.4f;

    sealed class Manual : IMusicBeatSource
    {
        public bool Active { get; set; } = true;
        public double BeatNow { get; set; }
        public double SecondsPerBeat => .4;
        public int BeatsPerBar => 4;
        public int DownbeatBeat => 0;
        public MusicMoment Moment = new MusicMoment { energy = .7f, bass = .7f, accent = .7f, kind = "drive" };
        public MusicMoment MusicNow => Moment;
        public MusicMoment MusicAhead(double beats) => Moment;
    }

    static string Env(string k, string d) { var v = System.Environment.GetEnvironmentVariable(k); return string.IsNullOrEmpty(v) ? d : v; }

    public static void Run()
    {
        string key = Env("BOSSLOW_BOSS", "Frost");
        int hearts = int.Parse(Env("BOSSLOW_HEARTS", "2"));
        string outPath = Env("BOSSLOW_OUT", "Builds/BossLowHealth/" + key + "-" + hearts + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
        using (new TestHarness.Sandbox())
        {
            try { Render(key, hearts, outPath); }
            finally { BossEncounter.ResetRun(); }
        }
        EditorApplication.Exit(0);
    }

    static void Render(string key, int hearts, string outPath)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun(); BossRails.Reset(); PlayField.Reset();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.aspect = Width / (float)Height;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, Width, Height);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(.05f, .1f, .25f);
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        Time.timeScale = 1f;

        BossDef boss = BossCatalog.All[0];
        foreach (var b in BossCatalog.All) if (b.artKey == key) boss = b;
        var actor = BossActor.Spawn(boss);
        var pool = new BossProjectilePool(BossConfig.ProjectilePoolMax, BossConfig.BeamPoolMax);
        var music = new Manual();
        actor.BeginFight();
        actor.BeatOverride = music;
        actor.SetHearts(hearts, actor.transform.position);
        var player = new Vector3(0f, -3.2f, 0f);

        var rt = new RenderTexture(Width, Height, 24);
        cam.targetTexture = rt;
        var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        float ppu = Height / (cam.orthographicSize * 2f);
        int side = Mathf.RoundToInt(Half * 2f * ppu);
        var seen = new HashSet<string>();
        var tiles = new List<Color[]>();
        float dt = 1f / 60f;
        for (int f = 0; f < 60 * 30; f++)
        {
            music.BeatNow = f * (double)dt / .4;
            actor.StepFight(dt, dt, player, .8f, pool);
            if (f % 240 == 100) actor.Flash(1f);
            var on = new List<string>();
            foreach (var sr in actor.GetComponentsInChildren<SpriteRenderer>())
                if (sr.enabled && sr.sprite != null && sr.color.a > 0f && (sr.name == "Body" || sr.name == "HitFlash" || sr.name.StartsWith("Damage")))
                    on.Add(sr.name + ":" + sr.sprite.name + "@" + sr.color.a.ToString("0.00"));
            string sig = actor.BodyFrame + "|" + string.Join(",", on);
            if (!seen.Add(sig)) continue;
            Debug.Log("[BOSSLOW] f" + f + " frame " + actor.BodyFrame + " stage " + actor.DamageStage + " : " + string.Join(" ", on));
            cam.Render();
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            shot.Apply();
            RenderTexture.active = old;
            Vector3 c = cam.WorldToScreenPoint(actor.transform.position);
            int x0 = Mathf.Clamp((int)c.x - side / 2, 0, Width - side);
            int y0 = Mathf.Clamp((int)c.y - side / 2, 0, Height - side);
            tiles.Add(shot.GetPixels(x0, y0, side, side));
            if (tiles.Count >= 24) break;
        }
        int cols = Mathf.Min(6, Mathf.Max(1, tiles.Count)), rows = Mathf.Max(1, (tiles.Count + cols - 1) / cols);
        var sheet = new Texture2D(side * cols, side * rows, TextureFormat.RGB24, false);
        for (int i = 0; i < tiles.Count; i++)
            sheet.SetPixels((i % cols) * side, (rows - 1 - i / cols) * side, side, side, tiles[i]);
        sheet.Apply();
        File.WriteAllBytes(outPath, sheet.EncodeToPNG());
        Debug.Log("[BOSSLOW] wrote " + outPath + " (" + tiles.Count + " tiles)");
        cam.targetTexture = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(shot); Object.DestroyImmediate(sheet);
        Object.DestroyImmediate(actor.gameObject);
    }
}
