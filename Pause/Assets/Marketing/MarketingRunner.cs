#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// The Play-mode half of MarketingCapture: walks the menus and the four launch
// worlds, steering the ship with MarketingAutopilot, recording clips with the
// Unity Recorder and grabbing screenshots at the Game view's exact size.
public struct MarketingJob { public int w, h; public bool video; public string shotDir; }

public class MarketingRunner : MonoBehaviour
{
    // Launch worlds only: Space, Frost, Verdant, Ember.
    static readonly string[] WorldKey = { "space", "frost", "verdant", "ember" };
    // A ship with several hearts per world (the autopilot also refills hearts, so a stray hit never ends a take).
    static readonly int[] WorldShip = { 5, 6, 7, 4 };

    MarketingJob job;
    string root;
    float playSeconds, bossSeconds;
    MarketingAutopilot pilot;
    readonly System.Collections.Generic.List<string> log = new System.Collections.Generic.List<string>();

    public void Begin(MarketingJob j)
    {
        job = j;
        root = MarketingEnv.OutRoot;
        playSeconds = EnvFloat("MC_PLAY_SECONDS", 40f);
        bossSeconds = EnvFloat("MC_BOSS_SECONDS", 28f);
        StartCoroutine(Run());
    }

    static float EnvFloat(string key, float fallback)
    {
        float v;
        return float.TryParse(Environment.GetEnvironmentVariable(key), System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out v) ? v : fallback;
    }

    IEnumerator Run()
    {
        yield return null;
        Debug.Log("[MarketingCapture] screen " + Screen.width + "x" + Screen.height);
        if (Screen.width != job.w || Screen.height != job.h)
            Debug.LogWarning("[MarketingCapture] Game view is " + Screen.width + "x" + Screen.height + ", wanted " + job.w + "x" + job.h);
        Directory.CreateDirectory(Path.Combine(root, job.shotDir));
        Directory.CreateDirectory(Path.Combine(root, "video/raw"));
        pilot = gameObject.AddComponent<MarketingAutopilot>();
        // the editor's own prefs may have sound muted: the footage needs full music and effects
        UnityEditor.EditorUtility.audioMasterMute = false;
        PlayerPrefs.DeleteKey(SoundSettings.Key);
        SoundSettings.SetSfxVolume(1f);
        SoundSettings.SetMusicVolume(1f);
        SoundSettings.SetMuted(false);

        yield return MenuShots();
        foreach (int w in MarketingEnv.ParseList("MC_WORLDS", new[] { 0, 1, 2, 3 }))
            if (w >= 0) yield return WorldRun(w);

        if (job.video)
            File.WriteAllText(Path.Combine(root, "video/raw/pause_moments.jsonl"), momentsJson.ToString());
        DeveloperUnlocks.SetEnabled(false);   // puts the real progress snapshot back
        PlayerPrefs.Save();
        Debug.Log("[MarketingCapture] job finished");
        EditorApplication.isPlaying = false;
    }

    // ---- menus -----------------------------------------------------------------

    IEnumerator MenuShots()
    {
        pilot.Enabled = false;
        // Title + codex run WITHOUT developer mode (it would list the worlds that are not in the launch loop):
        // every entry is simply marked discovered. Prefs are restored by scripts/marketing-capture.sh.
        DeveloperUnlocks.SetEnabled(false);
        Codex.Reload();
        foreach (var entry in Codex.Entries)
            { Codex.Discover(entry.id); Codex.MarkSeen(entry.id); }   // (MarkSeen clears the NEW dots)
        Codex.AckAchievements();
        PlayerPrefs.SetInt(ShipId.SelectedKey, 7);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 1250f);

        // title
        yield return Load("startS4");
        yield return Frames(240);
        yield return Shot("title");
        yield return Frames(90);
        yield return Shot("title_2");

        // codex (pilot's log)
        Codex.Reload();
        CodexPanel panel = null;
        try { panel = CodexPanel.Open(null); } catch (Exception e) { Debug.LogError("[MarketingCapture] codex: " + e); }
        if (panel != null)
        {
            yield return Frames(30);
            panel.ShowGrid();
            string[] names = { "codex_enemies", "codex_atoms", "codex_worlds", "codex_ships" };
            int[] tabs = { 1, 3, 4, 5 };
            for (int k = 0; k < tabs.Length; k++)
            {
                panel.ShowGrid();
                panel.ShowCategory(CodexPanel.Tabs[tabs[k]]);
                panel.SkipAnimations();
                yield return Frames(30);
                yield return Shot(names[k]);
                if (k == 0 && panel.VisibleCards > 3)
                {
                    var entry = panel.CardEntry(3);
                    if (entry != null) { panel.ShowDetail(entry); panel.SkipAnimations(); yield return Frames(30); yield return Shot("codex_entry"); }
                }
            }
        }

        // ship shop (the space dock): developer mode on so every hull is on show
        DeveloperUnlocks.SetEnabled(true);
        PlayerPrefs.SetInt(ShipId.SelectedKey, 7);
        yield return Load("shopS6");
        yield return Frames(240);
        yield return Shot("shop");
        var dock = SpaceDock.Instance;
        if (dock != null)
        {
            dock.Select(7);
            yield return Frames(90);
            yield return Shot("shop_ship");
        }
    }

    // ---- one world -------------------------------------------------------------

    IEnumerator WorldRun(int w)
    {
        string key = WorldKey[w];
        Debug.Log("[MarketingCapture] === world " + key);
        pilot.Enabled = false;
        BossEncounter.ResetRun();
        BossEncounter.ForgetDone();
        Time.timeScale = 1f;
        DeveloperUnlocks.SetEnabled(true);
        DeveloperUnlocks.SelectWorld(w);
        BossDev.SetRushMode(BossDev.RushOff);
        PlayerPrefs.SetInt(ShipId.SelectedKey, WorldShip[w]);
        yield return Load("gameS1");
        pilot.Enabled = true;

        // wait for the world to come up, then for the entry (planetfall / portal arrival) to hand the ship over
        float t = 0f;
        while ((WorldManager.Instance == null || WorldManager.CurrentIndex != w) && t < 20f) { t += Time.unscaledDeltaTime; yield return null; }
        yield return Frames(30);
        t = 0f;
        while (WorldEntry.Active && t < 45f) { t += Time.unscaledDeltaTime; yield return null; }
        // fly a while unrecorded so the speed and the board have built up (the first minute is sparse)
        yield return Frames(Mathf.RoundToInt(EnvFloat("MC_WARMUP_SECONDS", 50f) * 60f));
        Debug.Log("[MarketingCapture] world " + WorldManager.CurrentIndex + " flying, entry done after " + t.ToString("F1") + "s");

        // ---- gameplay clip: flying + freeze -> teleport moments (the game's whole point) ----
        yield return Segment(key + "_gameplay", playSeconds, w, true,
            new[] { Mv(.10f, MarketingAutopilot.Move.Escape), Mv(.30f, MarketingAutopilot.Move.OntoEnemy), Mv(.50f, MarketingAutopilot.Move.Escape), Mv(.66f, MarketingAutopilot.Move.OntoRed) },
            new[] { .66f }, true);

        // ---- pause showcase: approach danger, freeze, hold the frame, teleport, unfreeze, escape ----
        if (job.video)
        {
            yield return Frames(60);
            yield return Segment("pause_showcase_" + key, 28f, w, false,
                new[] { Mv(.04f, MarketingAutopilot.Move.Escape), Mv(.26f, MarketingAutopilot.Move.OntoEnemy), Mv(.60f, MarketingAutopilot.Move.OntoRed) },
                new[] { .60f }, false);
        }

        pilot.CancelPending();
        pilot.OnMoment = null;

        // ---- boss clip ----
        yield return Frames(20);
        StartVideo(key + "_boss");
        yield return Frames(30);
        bool started = BossDev.TriggerNow();
        Debug.Log("[MarketingCapture] boss trigger: " + started);
        int bossFrames = Mathf.RoundToInt(bossSeconds * 60f);
        for (int f = 0; f < bossFrames; f++)
        {
            if (f == Mathf.RoundToInt(bossFrames * .25f)) yield return Shot(key + "_boss_1");
            if (f == Mathf.RoundToInt(bossFrames * .55f)) yield return Shot(key + "_boss_2");
            if (f == Mathf.RoundToInt(bossFrames * .85f)) yield return Shot(key + "_boss_3");
            yield return null;
        }
        StopVideo();
        pilot.Enabled = false;
    }

    // ---- pause-move segments -----------------------------------------------------

    struct Mov { public float at; public MarketingAutopilot.Move move; }
    static Mov Mv(float at, MarketingAutopilot.Move m) { return new Mov { at = at, move = m }; }

    readonly System.Collections.Generic.Queue<string> shotQueue = new System.Collections.Generic.Queue<string>();
    readonly System.Text.StringBuilder momentsJson = new System.Text.StringBuilder();
    float clipClock;
    string clipName = "";
    bool gotFreezeShot, gotTeleportShot, gotCounterShot;

    // One recorded clip. `moves` are fractions of the clip's length at which a pause move is armed (it fires when its
    // approach is there: danger in the lane, an enemy / red atom on screen); `redAt` spawns a red atom then.
    IEnumerator Segment(string name, float seconds, int w, bool takeShots, Mov[] moves, float[] redAt, bool normalShots)
    {
        string key = WorldKey[w];
        clipName = name;
        clipClock = 0f;
        gotFreezeShot = gotTeleportShot = gotCounterShot = !takeShots;
        pilot.OnMoment = (what, mv) =>
        {
            momentsJson.Append("{\"clip\":\"" + name + "\",\"world\":\"" + key + "\",\"kind\":\"" + mv + "\",\"event\":\"" + what +
                               "\",\"t\":" + clipClock.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                               ",\"pauses\":" + score.pauseCounter + "},\n");
            if (what == "hold" && mv == MarketingAutopilot.Move.Escape && !gotFreezeShot) { gotFreezeShot = true; shotQueue.Enqueue(key + "_pause_freeze"); }
            if (what == "after" && mv == MarketingAutopilot.Move.OntoEnemy && !gotTeleportShot) { gotTeleportShot = true; shotQueue.Enqueue(key + "_pause_teleport"); }
            if (what == "after" && mv == MarketingAutopilot.Move.OntoRed && !gotCounterShot) { gotCounterShot = true; shotQueue.Enqueue(key + "_pause_counter"); }
        };
        StartVideo(name);
        bool[] armed = new bool[moves.Length];
        bool redDone = false;
        bool eliteDone = false;
        bool[] shot = new bool[4];
        while (clipClock < seconds)
        {
            clipClock += Mathf.Min(Time.unscaledDeltaTime, .1f);
            float frac = clipClock / seconds;
            for (int i = 0; i < moves.Length; i++)
                if (!armed[i] && frac >= moves[i].at) { armed[i] = true; pilot.QueueMove(moves[i].move); }
            if (!redDone && pilot.NextMoveIsRed)
            {
                redDone = true;   // the red atom comes in when its move is next up, so it is still on screen then
                var spawner = UnityEngine.Object.FindFirstObjectByType<spawnGoodStuff>();
                if (spawner != null && spawner.redAtom != null && Camera.main != null)
                {
                    // the game's own pause atom prefab, dressed like spawnRedAtom does, dropped in view above the ship
                    var at = Camera.main.ViewportToWorldPoint(new Vector3(UnityEngine.Random.Range(.3f, .7f), .72f, Mathf.Abs(Camera.main.transform.position.z)));
                    at.z = 0f;
                    var atom = (GameObject)Instantiate(spawner.redAtom, at, Quaternion.identity);
                    PickupArt.ApplyInGameScale(atom, spawner.redAtom);
                    AtomSpin.AddTo(atom);
                    Debug.Log("[MarketingCapture] red atom placed at " + at);
                }
            }
            if (!eliteDone && frac > .22f)
            {
                eliteDone = true;
                var dir = EliteDirector.Instance;
                if (dir != null) Debug.Log("[MarketingCapture] elites spawned: " + dir.SpawnGroup(w, 2));
            }
            if (normalShots)
            {
                float[] at = { .05f, .42f, .66f, .92f };
                for (int i = 0; i < 4; i++)
                    if (!shot[i] && frac >= at[i] && !pilot.PauseMovePending) { shot[i] = true; shotQueue.Enqueue(key + "_" + (i + 1)); }
            }
            while (shotQueue.Count > 0) yield return Shot(shotQueue.Dequeue());
            yield return null;
        }
        StopVideo();
        yield return Frames(30);
    }

    // ---- helpers ---------------------------------------------------------------

    IEnumerator Load(string scene)
    {
        SceneManager.LoadScene(scene);
        yield return null;
        yield return null;
    }

    IEnumerator Frames(int n)
    {
        for (int i = 0; i < n; i++) yield return null;
    }

    IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        // opaque RGB PNG at the Game view's exact size
        var rgb = new Texture2D(tex.width, tex.height, TextureFormat.RGB24, false);
        rgb.SetPixels32(tex.GetPixels32());
        rgb.Apply();
        string stem = job.w == 1080 ? "pause" : "pause";
        string path = Path.Combine(root, job.shotDir, name + ".png");
        File.WriteAllBytes(path, rgb.EncodeToPNG());
        Debug.Log("[MarketingCapture] shot " + path + " " + tex.width + "x" + tex.height);
        Destroy(tex);
        Destroy(rgb);
    }

    // The Recorder lives in the Editor assembly (it cannot be referenced from here); it fills these in.
    public static Action<string, int, int> StartVideoHook;
    public static Action StopVideoHook;

    void StartVideo(string name)
    {
        if (!job.video || StartVideoHook == null) return;
        StartVideoHook(Path.Combine(root, "video/raw", name), job.w, job.h);
        Debug.Log("[MarketingCapture] recording " + name);
    }

    void StopVideo()
    {
        if (StopVideoHook != null) StopVideoHook();
    }
}

public static class MarketingEnv
{
    public static string OutRoot
    {
        get
        {
            string v = System.Environment.GetEnvironmentVariable("MC_OUT");
            return string.IsNullOrEmpty(v) ? "/Users/sina/Developer/HapticGate-brand/marketing/pause" : v;
        }
    }

    public static int[] ParseList(string env, int[] fallback)
    {
        string v = System.Environment.GetEnvironmentVariable(env);
        if (string.IsNullOrEmpty(v)) return fallback;
        var parts = v.Split(',');
        var r = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++) r[i] = int.Parse(parts[i].Trim());
        return r;
    }
}
#endif
