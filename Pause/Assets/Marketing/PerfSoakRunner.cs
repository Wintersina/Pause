#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// The Play-mode half of PerfSoak (Assets/Editor/Tools/PerfSoak.cs): flies REAL runs of gameS1 with the
// marketing autopilot (a bot that dodges and never dies) at a fixed 60 simulated frames a second, as fast
// as the machine goes, and logs every few simulated seconds what is alive: objects, renderers, materials,
// textures, meshes, managed heap, GC allocation per frame and CPU time per frame. Whatever grows over a
// level is a leak; the name histograms say which objects they are.
//
//   PS_RUNS      "ship:world,ship:world,..."   (default "2:0")
//   PS_SECONDS   simulated seconds per run     (default 240: a level is ~120 s, then its boss)
//   PS_SAMPLE    simulated seconds per sample  (default 10)
//
// Editor only; nothing here reaches a player build.
public class PerfSoakRunner : MonoBehaviour
{
    MarketingAutopilot pilot;
    ProfilerRecorder gcRec;
    readonly Stopwatch wall = new Stopwatch();
    long gcBytes, gcFrames, gcMax;
    double cpuMs, cpuMax;
    int frames;

    public void Begin() { StartCoroutine(Run()); }

    static float EnvFloat(string key, float fallback)
    {
        float v;
        return float.TryParse(Environment.GetEnvironmentVariable(key), System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out v) ? v : fallback;
    }

    IEnumerator Run()
    {
        yield return null;
        pilot = gameObject.AddComponent<MarketingAutopilot>();
        gcRec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        // every pixel read (a GPU sync when it goes through the GPU), with who and when: none may land in a fight
        PerfProbe.OnPixelRead = (site, what, pixels, gpu) =>
            Debug.Log("[PERFSOAK] pixelread t=" + runT.ToString("F1") + " " + site + " '" + (what != null ? what.name : "?") + "' px=" + pixels +
                      (gpu ? " GPU" : " cpu") + " boss=" + (BossEncounter.Running ? 1 : 0));
        string runs = Environment.GetEnvironmentVariable("PS_RUNS");
        if (string.IsNullOrEmpty(runs)) runs = "2:0";
        float seconds = EnvFloat("PS_SECONDS", 240f), every = EnvFloat("PS_SAMPLE", 10f);
        foreach (string run in runs.Split(','))
        {
            var p = run.Split(':');
            yield return OneRun(int.Parse(p[0]), int.Parse(p[1]), seconds, every);
        }
        pilot.Enabled = false;
        DeveloperUnlocks.SetEnabled(false);   // puts the real progress snapshot back
        PlayerPrefs.Save();
        Debug.Log("[PERFSOAK] finished");
        PerfProbe.OnPixelRead = null;
        gcRec.Dispose();
        EditorApplication.isPlaying = false;
    }

    IEnumerator OneRun(int ship, int world, float seconds, float every)
    {
        string tag = shopingShips.NameFor(ship) + "/" + WorldManager.Worlds[world].displayName;
        Debug.Log("[PERFSOAK] === run " + tag + " (ship " + ship + ", world " + world + ")");
        pilot.Enabled = false;
        BossEncounter.ResetRun();
        BossEncounter.ForgetDone();
        Time.timeScale = 1f;
        DeveloperUnlocks.SetEnabled(true);
        DeveloperUnlocks.SelectWorld(world);
        BossDev.SetRushMode(BossDev.RushOff);
        PlayerPrefs.SetInt(ShipId.SelectedKey, ship);
        // PS_SCREEN=WxH: be a phone of that shape (the batch editor's own "screen" is a landscape window)
        string scr = Environment.GetEnvironmentVariable("PS_SCREEN");
        if (!string.IsNullOrEmpty(scr))
        {
            var wh = scr.Split('x');
            screenW = int.Parse(wh[0]); screenH = int.Parse(wh[1]);
            ScreenInfo.Override(screenW, screenH, new Rect(0, 0, screenW, screenH - 90), null, 420f, false);
        }
        SceneManager.LoadScene("gameS1");
        yield return null;
        yield return null;
        pilot.Enabled = true;
        Time.captureFramerate = 60;

        float fireEvery = EnvFloat("PS_FIRE", 0f), nextFire = 8f;
        Dictionary<string, int>[] baseline = null;
        float t = 0f, next = 0f;
        // the run's own numbers (the summary line): the worst frame in the boss's first seconds and in the rest of
        // its fight, the worst frame of the level, the biggest single-frame allocation, pixel reads, textures
        double bossStartMs = 0, bossWorstMs = 0, levelWorstMs = 0;
        long worstGc = 0;
        float bossAt = -1f;
        int reads0 = PerfProbe.PixelReads, gpu0 = PerfProbe.GpuReadbacks, readsAtCalm = -1, gpuAtCalm = -1;
        int tex0 = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
        runT = 0f;
        bool baselined = false;
        wall.Restart();
        while (t < seconds)
        {
            yield return null;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            double ms = wall.Elapsed.TotalMilliseconds;
            wall.Restart();
            long gc = gcRec.Valid ? gcRec.LastValue : 0;
            if (frames >= 0)
            {
                cpuMs += ms; if (ms > cpuMax) cpuMax = ms;
                gcBytes += gc; gcFrames++; if (gc > gcMax) gcMax = gc;
            }
            if (frames >= 0 && t > CalmSeconds)
            {
                bool boss = BossEncounter.Running;
                if (boss && bossAt < 0f) bossAt = t;
                if (boss && t - bossAt <= BossStartSeconds) { if (ms > bossStartMs) bossStartMs = ms; }
                else if (boss) { if (ms > bossWorstMs) bossWorstMs = ms; }
                else if (ms > levelWorstMs) levelWorstMs = ms;
                if (gc > worstGc) worstGc = gc;
            }
            if (readsAtCalm < 0 && t > CalmSeconds) { readsAtCalm = PerfProbe.PixelReads; gpuAtCalm = PerfProbe.GpuReadbacks; }
            frames++;
            t += Time.deltaTime > 0f ? Time.deltaTime : 1f / 60f;
            runT = t;
            if (!benched && t > 5f) { benched = true; Bench(); }
            RenderCameras();
            if (fireEvery > 0f && t >= nextFire)
            {
                nextFire = t + fireEvery;
                var runner = Object.FindFirstObjectByType<ShipAttackRunner>();
                if (runner != null && !WorldEntry.Active) { try { runner.Fire(); fired++; } catch (Exception e) { Debug.LogError("[PERFSOAK] fire: " + e); } }
            }
            if (t < next) continue;
            next += every;
            var hist = Sample(tag, t);
            if (t < every)
            {
                // what this run starts with that the last one did not: whatever a run leaves behind for good
                if (lastRunStart != null) Growers(tag + " (since the last run's start)", t, lastRunStart, hist);
                lastRunStart = hist;
            }
            if (!baselined && t >= 20f) { baseline = hist; baselined = true; }
            else if (baselined && (Mathf.RoundToInt(t / every) % 6 == 0 || t + every >= seconds)) Growers(tag, t, baseline, hist);
            frames = -2;   // the sample's own frame (and the one after) is not measured
            cpuMs = 0; cpuMax = 0; gcBytes = 0; gcFrames = 0; gcMax = 0;
            wall.Restart();
        }
        pilot.Enabled = false;
        if (readsAtCalm < 0) { readsAtCalm = PerfProbe.PixelReads; gpuAtCalm = PerfProbe.GpuReadbacks; }
        Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "[PERFSOAK] summary {0} | bossStartMs={1:F1} bossWorstMs={2:F1} levelWorstMs={3:F1} worstGcBytes={4} | pixelReadsAfterCalm={5} gpuReadbacksAfterCalm={6} (whole run {7}/{8}) | tex {9} -> {10} | unbakedElites={11} unbakedShields={12}",
            tag, bossStartMs, bossWorstMs, levelWorstMs, worstGc, PerfProbe.PixelReads - readsAtCalm, PerfProbe.GpuReadbacks - gpuAtCalm,
            PerfProbe.PixelReads - reads0, PerfProbe.GpuReadbacks - gpu0, tex0, Resources.FindObjectsOfTypeAll<Texture2D>().Length,
            EliteSilhouette.Unbaked, ShieldContour.UnbakedCount));
    }

    // The run's first seconds are its calm start (the warm-up's window): what is read or built there is not "in a fight".
    public const float CalmSeconds = 12f, BossStartSeconds = 6f;
    float runT;

    Dictionary<string, int>[] lastRunStart;
    bool benched;
    int fired;
    RenderTexture target;

    // A batch editor draws no Game view, so nothing is ever "visible" and no renderer builds its geometry. PS_RENDER=1
    // draws every camera into a phone-shaped texture each frame, the way a device does.
    int screenW, screenH;

    void RenderCameras()
    {
        if (screenW > 0) foreach (var cam in Camera.allCameras) if (cam != null) cam.aspect = screenW / (float)screenH;
        if (Environment.GetEnvironmentVariable("PS_RENDER") != "1") return;
        if (target == null) target = new RenderTexture(540, 1260, 24) { name = "~PerfSoakTarget", hideFlags = HideFlags.HideAndDontSave };
        foreach (var cam in Camera.allCameras)
        {
            if (cam == null || !cam.isActiveAndEnabled) continue;
            var was = cam.targetTexture;
            cam.targetTexture = target;
            cam.Render();
            cam.targetTexture = was;
        }
    }

    static long Rss()
    {
        try
        {
            var p = new System.Diagnostics.Process();
            p.StartInfo.FileName = "/bin/ps";
            p.StartInfo.Arguments = "-o rss= -p " + System.Diagnostics.Process.GetCurrentProcess().Id;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.Start();
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return long.Parse(o.Trim()) / 1024;
        }
        catch (Exception) { return -1; }
    }

    // What the per-frame HUD layout calls cost here (they have no profiler markers of their own).
    static void Bench()
    {
        var screen = new Vector2(Screen.width, Screen.height);
        Rect safe = Screen.safeArea;
        One("Screen.width", () => { int w = Screen.width; });
        One("Screen.safeArea", () => { Rect r = Screen.safeArea; });
        One("Screen.dpi", () => { float d = Screen.dpi; });
        One("ScreenInfo.Width", () => { int w = ScreenInfo.Width; });
        One("ScreenInfo.Dpi", () => { float d = ScreenInfo.Dpi; });
        One("UiScale.PxPerPoint", () => { float d = UiScale.PxPerPoint; });
        One("CanvasScaleFor", () => PauseQuickActions.CanvasScaleFor(screen));
        One("LaneMetrics.InnerEdge", () => { float e = LaneMetrics.InnerEdge; });
        One("CameraFit.GameplayViewHalfWidth", () => CameraFit.GameplayViewHalfWidth(screen));
        One("HudStyler.BoardGrowthPx", () => HudStyler.BoardGrowthPx(screen, -1));
        One("TopBand.FrameFor", () => TopBand.FrameFor(safe, screen));
        One("HudSkin.CurrentWorld", () => { int w = HudSkin.CurrentWorld; });
    }

    static void One(string what, Action a)
    {
        a();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 200; i++) a();
        Debug.Log("[PERFSOAK] bench " + what + " = " + (sw.Elapsed.TotalMilliseconds * 1000.0 / 200).ToString("F1") + " us");
    }

    static readonly Type[] HistTypes = { typeof(GameObject), typeof(Material), typeof(Texture2D), typeof(Mesh), typeof(Sprite), typeof(RenderTexture), typeof(MonoBehaviour) };

    Dictionary<string, int>[] Sample(string tag, float t)
    {
        var hist = new Dictionary<string, int>[HistTypes.Length];
        var counts = new int[HistTypes.Length];
        for (int i = 0; i < HistTypes.Length; i++)
        {
            var all = Resources.FindObjectsOfTypeAll(HistTypes[i]);
            counts[i] = all.Length;
            var h = hist[i] = new Dictionary<string, int>();
            bool byType = HistTypes[i] == typeof(MonoBehaviour);
            foreach (var o in all)
            {
                string k = byType ? o.GetType().Name : Normal(o.name);
                int n; h.TryGetValue(k, out n); h[k] = n + 1;
            }
        }
        int lines = Object.FindObjectsByType<LineRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        int sprites = Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        int spritesOn = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).Length;
        int masks = Object.FindObjectsByType<SpriteMask>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        int particles = Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        int colliders = Object.FindObjectsByType<Collider2D>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        int audio = Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        int roots = 0;
        for (int s = 0; s < SceneManager.sceneCount; s++) roots += SceneManager.GetSceneAt(s).rootCount;
        int f = Mathf.Max(1, (int)gcFrames);
        Debug.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "[PERFSOAK] {0} t={1:F0} ortho=" + (Camera.main != null ? Camera.main.orthographicSize.ToString("F2") : "?") + " world={2} boss={3} speed={4:F2} | GO={5} roots={6} MB={7} | SR={8} SRon={9} LR={10} mask={11} PS={12} col={13} audio={14} | mat={15} tex={16} mesh={17} sprite={18} RT={19} | heapMB={20:F1} unityMB={21:F1} texMB={22:F1} gfxMB={27:F1} reservedMB={28:F1} rssMB={29} fired={30} | gcB/f={23} gcMax={24} cpuMs/f={25:F2} cpuMax={26:F1}",
            tag, t, WorldManager.Instance != null ? WorldManager.CurrentIndex : -1, BossEncounter.Running ? 1 : 0, moveBackGround.speed,
            counts[0], roots, counts[6], sprites, spritesOn, lines, masks, particles, colliders, audio,
            counts[1], counts[2], counts[3], counts[4], counts[5],
            Profiler.GetMonoUsedSizeLong() / 1048576.0, Profiler.GetTotalAllocatedMemoryLong() / 1048576.0, Texture.currentTextureMemory / 1048576.0,
            gcBytes / f, gcMax, cpuMs / f, cpuMax,
            Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576.0, Profiler.GetTotalReservedMemoryLong() / 1048576.0, Rss(), fired));
        return hist;
    }

    static void Growers(string tag, float t, Dictionary<string, int>[] a, Dictionary<string, int>[] b)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < HistTypes.Length; i++)
        {
            var list = new List<KeyValuePair<string, int>>();
            foreach (var kv in b[i])
            {
                int was; a[i].TryGetValue(kv.Key, out was);
                if (kv.Value - was > 0) list.Add(new KeyValuePair<string, int>(kv.Key + "(" + was + "->" + kv.Value + ")", kv.Value - was));
            }
            list.Sort((x, y) => y.Value.CompareTo(x.Value));
            sb.Length = 0;
            for (int k = 0; k < list.Count && k < 14; k++) sb.Append(' ').Append(list[k].Key);
            Debug.Log("[PERFSOAK] " + tag + " t=" + t.ToString("F0") + " growers " + HistTypes[i].Name + ":" + sb);
        }
    }

    // "rock (3)(Clone)" / "shard_12" -> one bucket
    static string Normal(string name)
    {
        if (string.IsNullOrEmpty(name)) return "<unnamed>";
        var sb = new StringBuilder(name.Length);
        foreach (char c in name) if (!char.IsDigit(c)) sb.Append(c);
        return sb.ToString().Replace("(Clone)", "").Replace("()", "").Replace(" (Instance)", "~inst").Trim();
    }
}
#endif
