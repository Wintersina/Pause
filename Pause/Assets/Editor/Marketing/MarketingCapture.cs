using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEditor.Recorder.Input;
using UnityEngine;
using UnityEngine.SceneManagement;

// Marketing capture: store screenshots and gameplay video (with game audio) for
// the four launch worlds (Space, Frost, Verdant, Ember). Editor only; nothing
// here reaches a player build (the only runtime-side hook is the UNITY_EDITOR
// override block in TouchInput).
//
//   Tools/Marketing/Capture All           from the editor menu
//   Unity -projectPath Pause -executeMethod MarketingCapture.CaptureAll
//       (NOT -batchmode: the Recorder needs a rendering editor). See
//       scripts/marketing-capture.sh and the README the run writes.
//
// A capture is a list of JOBS, one Play session each, because the game view
// (and so the whole layout) changes size per job:
//   job 0  1080x1920 (Google Play): menus, then per world a gameplay clip, a boss
//          clip and screenshots
//   job 1  1320x2868 (App Store 6.9"): the same walk, screenshots only
// State between Play sessions (domain reloads) lives in SessionState.
//
// Options (environment variables):
//   MC_OUT      output root (default /Users/sina/Developer/HapticGate-brand/marketing/pause)
//   MC_WORLDS   comma list of world indexes (default 0,1,2,3)
//   MC_JOBS     comma list of job indexes (default 0,1)
//   MC_PLAY_SECONDS / MC_BOSS_SECONDS   clip lengths (default 40 / 28)
//   MC_NO_VIDEO=1   skip the recorder (screenshots only)
[InitializeOnLoad]
public static class MarketingCapture
{
    const string ActiveKey = "mc.active", JobKey = "mc.job", JobsKey = "mc.jobs";

    
    public static string OutRoot { get { return MarketingEnv.OutRoot; } }

    public static MarketingJob[] AllJobs()
    {
        return new[]
        {
            new MarketingJob { w = 1080, h = 1920, video = Environment.GetEnvironmentVariable("MC_NO_VIDEO") != "1", shotDir = "screenshots/play_1080x1920" },
            new MarketingJob { w = 1320, h = 2868, video = false, shotDir = "screenshots/ios_1320x2868" },
        };
    }

    public static int[] ParseList(string env, int[] fallback) { return MarketingEnv.ParseList(env, fallback); }

    static MarketingCapture()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
        // domain reload while a capture is in flight (entering Play): pick the job back up
        if (SessionState.GetBool(ActiveKey, false) && EditorApplication.isPlayingOrWillChangePlaymode == false &&
            !EditorApplication.isPlaying)
            EditorApplication.delayCall += ContinueAfterReload;
        MarketingRunner.StartVideoHook = StartVideo;
        MarketingRunner.StopVideoHook = StopVideo;
    }

    [MenuItem("Tools/Marketing/Capture All")]
    public static void CaptureAll()
    {
        Directory.CreateDirectory(OutRoot);
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetInt(JobKey, 0);
        var jobs = ParseList("MC_JOBS", new[] { 0, 1 });
        SessionState.SetString(JobsKey, string.Join(",", jobs));
        EditorApplication.delayCall += StartNextJob;
    }

    static void ContinueAfterReload()
    {
        if (!SessionState.GetBool(ActiveKey, false)) return;
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
        StartNextJob();
    }

    static int[] JobList()
    {
        var parts = SessionState.GetString(JobsKey, "0,1").Split(',');
        var r = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++) r[i] = int.Parse(parts[i]);
        return r;
    }

    static void StartNextJob()
    {
        int n = SessionState.GetInt(JobKey, 0);
        var list = JobList();
        if (n >= list.Length)
        {
            SessionState.SetBool(ActiveKey, false);
            Debug.Log("[MarketingCapture] ALL DONE");
            if (Application.isBatchMode || Environment.GetEnvironmentVariable("MC_EXIT") == "1") EditorApplication.Exit(0);
            return;
        }
        var job = AllJobs()[list[n]];
        GameViewSize.Set(job.w, job.h);
        Debug.Log("[MarketingCapture] job " + n + ": " + job.w + "x" + job.h);
        EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
    }

    static void OnPlayMode(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(ActiveKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var list = JobList();
            int n = SessionState.GetInt(JobKey, 0);
            var go = new GameObject("~MarketingRunner") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<MarketingRunner>().Begin(AllJobs()[list[n]]);
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetInt(JobKey, SessionState.GetInt(JobKey, 0) + 1);
            EditorApplication.delayCall += StartNextJob;
        }
    }
    // ---- Unity Recorder hooks (the Play-mode runner lives in the runtime assembly) ----
    static RecorderController recorder;

    static void StartVideo(string path, int w, int h)
    {
        var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = Path.GetFileName(path);
        movie.Enabled = true;
        movie.EncoderSettings = new CoreEncoderSettings
        {
            Codec = CoreEncoderSettings.OutputCodec.MP4,
            EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
        };
        movie.CaptureAlpha = false;
        movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = w, OutputHeight = h };
        movie.AudioInputSettings.PreserveAudio = true;
        movie.OutputFile = path;
        settings.AddRecorderSettings(movie);
        settings.SetRecordModeToManual();
        settings.FrameRate = 60f;
        settings.CapFrameRate = true;
        recorder = new RecorderController(settings);
        recorder.PrepareRecording();
        recorder.StartRecording();
    }

    static void StopVideo()
    {
        if (recorder == null) return;
        recorder.StopRecording();
        recorder = null;
    }
}

// Sets the Game view to a fixed pixel size through the editor's own internals.
static class GameViewSize
{
    public static void Set(int w, int h)
    {
        var asm = typeof(Editor).Assembly;
        var sizesType = asm.GetType("UnityEditor.GameViewSizes");
        var single = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        object instance = single.GetProperty("instance").GetValue(null, null);
        object group = sizesType.GetProperty("currentGroup").GetValue(instance, null);
        var groupType = group.GetType();
        var sizeType = asm.GetType("UnityEditor.GameViewSize");
        var sizeKind = asm.GetType("UnityEditor.GameViewSizeType");
        string name = "MC " + w + "x" + h;

        int total = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null);
        int index = -1;
        var getSize = groupType.GetMethod("GetGameViewSize");
        for (int i = 0; i < total; i++)
        {
            object s = getSize.Invoke(group, new object[] { i });
            if ((string)sizeType.GetProperty("baseText").GetValue(s, null) == name) { index = i; break; }
        }
        if (index < 0)
        {
            object size = Activator.CreateInstance(sizeType, Enum.ToObject(sizeKind, 1), w, h, name);
            groupType.GetMethod("AddCustomSize").Invoke(group, new[] { size });
            index = total;
        }
        var gvType = asm.GetType("UnityEditor.GameView");
        var gv = EditorWindow.GetWindow(gvType);
        var prop = gvType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        prop.SetValue(gv, index, null);
        // render 1:1 where the window allows; the render target keeps its size either way
        gv.Repaint();
    }
}
