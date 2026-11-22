using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;

// Play-mode performance soak: `Unity -batchmode -executeMethod PerfSoak.Run` (NO -quit: the editor has to
// enter Play; scripts/perf-soak.sh does that through the batch queue) flies real runs with the marketing
// autopilot and logs "[PERFSOAK]" lines (see Assets/Marketing/PerfSoakRunner.cs for what and the PS_*
// options). The bounded-growth gate that CI runs is PerfSoakTest; this is the tool for finding WHAT grows.
//
// PS_PROFILE=1 also reads every frame back from the editor profiler and logs, every PS_PROFILE_FRAMES frames
// (default 1800 = 30 simulated seconds): the markers that allocated the most managed memory (a GC.Alloc
// sample is charged to its parent marker) and the ones with the most self time; and, for every frame slower
// than PS_SPIKE_MS (default 40), that frame's own top markers.
[InitializeOnLoad]
public static class PerfSoak
{
    const string ActiveKey = "perfsoak.active";

    static PerfSoak()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    [MenuItem("Tools/Perf/Soak Run")]
    public static void Run()
    {
        SessionState.SetBool(ActiveKey, true);
        EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
    }

    static void OnPlayMode(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(ActiveKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var go = new GameObject("~PerfSoakRunner") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            go.AddComponent<PerfSoakRunner>().Begin();
            if (System.Environment.GetEnvironmentVariable("PS_PROFILE") == "1") StartProfile();
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(ActiveKey, false);
            Debug.Log("[PERFSOAK] exit");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }

    // ---- profiler read-back ----------------------------------------------------------------------------

    sealed class Stat { public double selfMs; public long gc; public int calls; }
    static readonly Dictionary<string, Stat> window = new Dictionary<string, Stat>();
    static readonly Dictionary<string, Stat> frame = new Dictionary<string, Stat>();
    static readonly List<int> children = new List<int>();
    static int lastSeen = -1, windowFrames, windowEvery = 1800;
    static float spikeMs = 40f;

    static void StartProfile()
    {
        int n; float f;
        if (int.TryParse(System.Environment.GetEnvironmentVariable("PS_PROFILE_FRAMES"), out n) && n > 0) windowEvery = n;
        if (float.TryParse(System.Environment.GetEnvironmentVariable("PS_SPIKE_MS"), out f) && f > 0f) spikeMs = f;
        ProfilerDriver.ClearAllFrames();
        ProfilerDriver.profileEditor = false;
        Profiler.enabled = true;
        ProfilerDriver.enabled = true;
        lastSeen = -1;
        EditorApplication.update -= ReadFrames;
        EditorApplication.update += ReadFrames;
        Debug.Log("[PERFSOAK] profiler read-back on");
    }

    static void ReadFrames()
    {
        if (!EditorApplication.isPlaying) { EditorApplication.update -= ReadFrames; return; }
        int last = ProfilerDriver.lastFrameIndex;
        if (last < 0) return;
        int from = Mathf.Max(lastSeen + 1, ProfilerDriver.firstFrameIndex);
        for (int f = from; f <= last; f++) ReadFrame(f);
        lastSeen = last;
    }

    static void ReadFrame(int index)
    {
        using (var v = ProfilerDriver.GetHierarchyFrameDataView(index, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnDontSort, false))
        {
            if (v == null || !v.valid) return;
            frame.Clear();
            Walk(v, v.GetRootItemID(), "", "", 0);
            float ms = v.frameTimeMs;
            foreach (var kv in frame)
            {
                Stat s;
                if (!window.TryGetValue(kv.Key, out s)) window[kv.Key] = s = new Stat();
                s.selfMs += kv.Value.selfMs; s.gc += kv.Value.gc; s.calls += kv.Value.calls;
            }
            if (ms >= spikeMs) Debug.Log("[PERFSOAK] spike " + ms.ToString("F1") + " ms t=" + Time.time.ToString("F1") + ":" + Top(frame, false, 10, 1) + " || gc:" + Top(frame, true, 6, 1));
            if (++windowFrames >= windowEvery)
            {
                Debug.Log("[PERFSOAK] profile t=" + Time.time.ToString("F0") + " gc B/frame:" + Top(window, true, 30, windowFrames));
                Debug.Log("[PERFSOAK] profile t=" + Time.time.ToString("F0") + " self ms/frame:" + Top(window, false, 30, windowFrames));
                window.Clear();
                windowFrames = 0;
            }
        }
    }

    // Engine work that matters by WHO asked for it (the nearest script method above it).
    static readonly HashSet<string> Charged = new HashSet<string>
    {
        "Texture2D.GetPixels32", "Texture2D.GetPixels", "SpriteMeshGenerator.TraceShape", "SpriteMeshGenerator.Simplify", "File.Read",
        "GetComponentNullErrorMessage", "Gfx.ReadbackImage", "EngineJob", "Font.CacheFontForText", "GameObject.AddComponent", "Instantiate.Produce",
        "Physics2D.OverlapBoxAll", "Loading.ReadObject", "Resources.Load",
    };

    static void Walk(HierarchyFrameDataView v, int id, string parent, string owner, int depth)
    {
        string name = v.GetItemName(id);
        if (depth > 0)
        {
            float self = v.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);
            bool alloc = name == "GC.Alloc";
            string key = alloc ? parent : name;
            if (Charged.Contains(key) && owner.Length > 0) key = key + " < " + owner;
            Stat s;
            if (!frame.TryGetValue(key, out s)) frame[key] = s = new Stat();
            s.selfMs += self;
            if (alloc) s.gc += (long)v.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnGcMemory);
            else s.calls += (int)v.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnCalls);
        }
        if (!v.HasItemChildren(id)) return;
        if (name.IndexOf("()", System.StringComparison.Ordinal) >= 0) owner = name;
        var kids = new List<int>();
        v.GetItemChildren(id, kids);
        for (int i = 0; i < kids.Count; i++) Walk(v, kids[i], name, owner, depth + 1);
    }

    static string Top(Dictionary<string, Stat> from, bool byGc, int n, int frames)
    {
        var list = new List<KeyValuePair<string, Stat>>(from);
        if (byGc) list.Sort((a, b) => b.Value.gc.CompareTo(a.Value.gc));
        else list.Sort((a, b) => b.Value.selfMs.CompareTo(a.Value.selfMs));
        var sb = new StringBuilder();
        for (int i = 0; i < list.Count && i < n; i++)
        {
            var s = list[i].Value;
            if (byGc && s.gc <= 0) break;
            sb.Append(" | ").Append(list[i].Key).Append('=');
            if (byGc) sb.Append(s.gc / frames);
            else sb.Append((s.selfMs / frames).ToString("F3"));
        }
        return sb.ToString();
    }
}
