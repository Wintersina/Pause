using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// The kill cam's tape: a rolling record of what the board drew, kept the
// whole run so that the moment the ship dies the last couple of seconds can
// be replayed (KillCam) -- and, for a high-score death, kept (KillCamArchive).
//
// Every LateUpdate of a running frame (the world moving: scaled time
// advancing, the ship alive) it walks the loaded scenes' renderers and notes
// each sprite on screen as a KillCamEntry: sprite, material, world matrix,
// colour, sorting, flips; and each scrolling quad (the rails: a MeshRenderer
// whose look is its material's texture offset) as its offset. A frozen pause
// (finger up), a death or the replay itself records nothing, so the tape is
// always the last seconds of actual flight. At most one frame per MinGap of
// game time (a 120 Hz screen records every other frame).
//
// Cheap and GC-free: the rings (frames, entries, mesh entries) are allocated
// once; the walk reuses its lists (GetRootGameObjects / GetComponentsInChildren
// into lists); entries are structs. When a ring is full the oldest frames go.
[DefaultExecutionOrder(31000)]   // after everything that moves or animates a sprite
public sealed class KillCamRecorder : MonoBehaviour
{
    public const int FrameCap = 160;           // 2.6 s at the 62 Hz cap
    public const int EntryCap = 57600;         // ~360 sprites a frame over the full ring
    public const int MaxPerFrame = 900;
    public const int MaxMeshPerFrame = 8;
    public const int MeshCap = FrameCap * MaxMeshPerFrame;
    public const float MinGap = 1f / 62f;

    public static KillCamRecorder Instance { get; private set; }
    // Tests / previews: switch the tape off.
    public static bool Enabled = true;
    // Only what a camera drew last frame is recorded (edit-mode tests, where nothing renders, turn this off).
    public static bool RequireVisible = true;

    readonly List<GameObject> roots = new List<GameObject>(64);
    readonly List<Renderer> scratch = new List<Renderer>(512);
    readonly List<Renderer> found = new List<Renderer>(1024);
    readonly Dictionary<int, bool> scrollQuad = new Dictionary<int, bool>(32);

    readonly KillCamEntry[] ring = new KillCamEntry[EntryCap];
    readonly KillCamMeshEntry[] meshRing = new KillCamMeshEntry[MeshCap];
    readonly float[] fTime = new float[FrameCap];
    readonly Vector2[] fCam = new Vector2[FrameCap];
    readonly int[] fFirst = new int[FrameCap], fCount = new int[FrameCap];
    readonly int[] fMeshFirst = new int[FrameCap], fMeshCount = new int[FrameCap];
    int head, frames, entryWrite, entriesLive, meshWrite, meshLive;
    float clock, lastAt = -1f;
    bool recording;
    Camera cam;
    KillCamClip view;

    public bool Recording { get { return recording; } }
    public int Frames { get { return frames; } }
    public float Clock { get { return clock; } }
    public int LastFrameEntries { get { return frames > 0 ? fCount[(head + frames - 1) % FrameCap] : 0; } }

    public static KillCamRecorder Ensure()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("~KillCamRecorder");
        Instance = go.AddComponent<KillCamRecorder>();
        return Instance;
    }

    // A run starts (collisionDetection.Start): an empty tape, rolling.
    public static void BeginRun()
    {
        var r = Ensure();
        r.Clear();
        r.recording = Enabled;
    }

    void Awake() { if (Instance == null) Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    public void Clear()
    {
        head = frames = entryWrite = entriesLive = meshWrite = meshLive = 0;
        clock = 0f;
        lastAt = -1f;
        scrollQuad.Clear();
        // drop the references so a finished run's sprites can unload
        System.Array.Clear(ring, 0, ring.Length);
        System.Array.Clear(meshRing, 0, meshRing.Length);
    }

    public void StopRecording() { recording = false; }

    void LateUpdate()
    {
        if (!recording || !Enabled) return;
        if (buttonClicks.playerDied || KillCam.Playing) return;
        float dt = Time.deltaTime;
        if (dt <= 0f || Time.timeScale <= 0f) return;
        clock += dt;
        if (lastAt >= 0f && clock - lastAt < MinGap) return;
        lastAt = clock;
        Capture(clock);
    }

    // ---- the walk ----

    void Gather()
    {
        found.Clear();
        int sceneCount = SceneManager.sceneCount;
        for (int s = 0; s < sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            scene.GetRootGameObjects(roots);
            for (int i = 0; i < roots.Count; i++)
            {
                var root = roots[i];
                if (root == null || !root.activeInHierarchy) continue;
                root.GetComponentsInChildren(false, scratch);
                found.AddRange(scratch);
            }
        }
        roots.Clear();
        scratch.Clear();
    }

    // A quad that scrolls its texture (the rails, the star field) rather than
    // moving: replayed by its offset. Anything on the ship (its shield) is not
    // one. Classified once per renderer.
    bool IsScrollQuad(Renderer r)
    {
        int id = r.GetInstanceID();
        bool yes;
        if (scrollQuad.TryGetValue(id, out yes)) return yes;
        var mat = r.sharedMaterial;
        yes = r is MeshRenderer && mat != null && mat.HasProperty("_MainTex") &&
              r.GetComponent<MeshFilter>() != null && r.GetComponentInParent<collisionDetection>() == null;
        scrollQuad[id] = yes;
        return yes;
    }

    // Records one frame at clip time `at` (public for tests and previews).
    public void Capture(float at)
    {
        if (cam == null) cam = Camera.main;
        Gather();
        if (frames == FrameCap) DropOldest();
        int slot = (head + frames) % FrameCap;
        int start = entryWrite, n = 0;
        int meshStart = meshWrite, mn = 0;
        for (int i = 0; i < found.Count; i++)
        {
            var r = found[i];
            if (r == null || !r.enabled || r.forceRenderingOff || (RequireVisible && !r.isVisible)) continue;
            var sr = r as SpriteRenderer;
            if (sr != null)
            {
                if (n >= MaxPerFrame) continue;
                var sprite = sr.sprite;
                if (sprite == null) continue;
                var c = sr.color;
                if (c.a <= .003f) continue;
                while (entriesLive + n + 1 > EntryCap && frames > 0) DropOldest();
                if (entriesLive + n + 1 > EntryCap) continue;
                ref var e = ref ring[entryWrite];
                var m = sr.transform.localToWorldMatrix;
                e.track = sr.GetInstanceID();
                e.sprite = sprite;
                e.material = sr.sharedMaterial;
                e.source = sr;
                e.pos = new Vector3(m.m03, m.m13, m.m23);
                e.m00 = m.m00; e.m01 = m.m01; e.m10 = m.m10; e.m11 = m.m11;
                e.color = c;
                e.layer = sr.sortingLayerID;
                e.order = (short)Mathf.Clamp(sr.sortingOrder, short.MinValue, short.MaxValue);
                var mode = sr.drawMode;
                e.flags = (byte)((sr.flipX ? KillCamEntry.FlipX : 0) | (sr.flipY ? KillCamEntry.FlipY : 0) |
                                 ((int)mode << KillCamEntry.DrawModeShift));
                e.size = mode != SpriteDrawMode.Simple ? sr.size : Vector2.zero;
                entryWrite = (entryWrite + 1) % EntryCap;
                n++;
            }
            else if (mn < MaxMeshPerFrame && IsScrollQuad(r))
            {
                while (meshLive + mn + 1 > MeshCap && frames > 0) DropOldest();
                if (meshLive + mn + 1 > MeshCap) continue;
                ref var me = ref meshRing[meshWrite];
                me.track = r.GetInstanceID();
                me.renderer = r;
                me.offset = r.sharedMaterial.mainTextureOffset;
                meshWrite = (meshWrite + 1) % MeshCap;
                mn++;
            }
        }
        found.Clear();
        fTime[slot] = at;
        fCam[slot] = cam != null ? (Vector2)cam.transform.position : Vector2.zero;
        fFirst[slot] = start;
        fCount[slot] = n;
        fMeshFirst[slot] = meshStart;
        fMeshCount[slot] = mn;
        frames++;
        entriesLive += n;
        meshLive += mn;
    }

    void DropOldest()
    {
        if (frames <= 0) return;
        entriesLive -= fCount[head];
        meshLive -= fMeshCount[head];
        head = (head + 1) % FrameCap;
        frames--;
    }

    // The tape's last `window` game seconds as a clip (in time order, its clock
    // starting at 0). The clip shares the rings: valid until recording resumes.
    // Null when there is not enough to play.
    public KillCamClip Clip(float window)
    {
        if (frames < 2) return null;
        if (view == null)
        {
            view = new KillCamClip(FrameCap, 1, 1);
            view.entries = ring;
            view.meshEntries = meshRing;
        }
        float end = fTime[(head + frames - 1) % FrameCap];
        int from = 0;
        while (from < frames - 2 && fTime[(head + from) % FrameCap] < end - window) from++;
        int n = frames - from;
        float t0 = fTime[(head + from) % FrameCap];
        for (int i = 0; i < n; i++)
        {
            int s = (head + from + i) % FrameCap;
            view.time[i] = fTime[s] - t0;
            view.cam[i] = fCam[s];
            view.first[i] = fFirst[s];
            view.count[i] = fCount[s];
            view.meshFirst[i] = fMeshFirst[s];
            view.meshCount[i] = fMeshCount[s];
        }
        view.frameCount = n;
        view.meshTracks = null;
        if (cam == null) cam = Camera.main;
        if (cam != null)
        {
            view.camHalfHeight = cam.orthographicSize;
            view.camAspect = cam.aspect;
            view.camZ = cam.transform.position.z;
            view.background = cam.backgroundColor;
        }
        return view.Playable ? view : null;
    }
}
