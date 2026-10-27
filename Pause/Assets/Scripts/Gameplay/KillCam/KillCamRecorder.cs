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
// (finger up) or the replay itself records nothing, so the tape is always the
// last seconds of actual flight. At most one frame per MinGap of game time.
//
// THE DEATH. The fatal hit (DeathCrash.Play -> MarkImpact) is recorded at once
// -- the frame of the collision itself, the ship still whole against what hit
// it -- and the tape rolls on through the death on the crash's own clock
// (DeathCrash.FrameDt: the world is stopped, the crash is not): the hit flash,
// the hull breaking up, the blasts, the killer's tumble, for up to
// KillCamClip.DeathTail, until the kill cam takes over (FinishTail). The clip
// then carries where the hit is (KillCamClip.impact): the replay slows into it
// and plays on through the death.
//
// Cheap and GC-free: the rings (frames, entries, mesh entries) are allocated
// once; the walk reuses its lists (GetRootGameObjects / GetComponentsInChildren
// into lists); entries are structs. When a ring is full the oldest frames go.
[DefaultExecutionOrder(31000)]   // after everything that moves or animates a sprite
public sealed class KillCamRecorder : MonoBehaviour
{
    // 24 frames a game second: every frame of the 24 fps art, and the replay blends between them
    // (it was 40; the art never changes faster than 24, and a capture is the costly part of the tape).
    public const float MinGap = 1f / 24f;
    // The walk is cached: the renderers (inactive pooled ones too) are gathered once, then every
    // RefreshEvery captures (~.17 s of play), and again the moment a spawner calls Invalidate().
    // The gathering happens on the frame before a capture is due, so no frame carries walk + capture.
    public const int RefreshEvery = 4;
    // Phones with less RAM than this, or a GPU budget below LowGraphicsMemoryMb, tape at LowEndMinGap (20 Hz).
    public const int LowSystemMemoryMb = 3600, LowGraphicsMemoryMb = 1024;
    public const float LowEndMinGap = 1f / 20f;
    public const int FrameCap = 128;           // 3.2 s at MinGap (the replay needs LiveWindow + DeathTail, 2.3 s)
    // ~1070 sprites a frame over the 2.3 s the replay needs: a busy board, the
    // elites' full-height dotted telegraphs (a hail wall's lanes are ~1000 dots) included.
    public const int EntryCap = 98304;
    public const int MaxPerFrame = 2048;
    public const int MaxMeshPerFrame = 8;
    public const int MeshCap = FrameCap * MaxMeshPerFrame;

    public static KillCamRecorder Instance { get; private set; }
    // Tests / previews: switch the tape off.
    public static bool Enabled = true;
    // Only what a camera drew last frame is recorded (edit-mode tests, where nothing renders, turn this off).
    public static bool RequireVisible = true;

    // The tape's gap on this device: MinGap, or LowEndMinGap on a low-end phone (documented thresholds above).
    public static float Gap(int systemMemoryMb, int graphicsMemoryMb)
    {
        bool low = (systemMemoryMb > 0 && systemMemoryMb < LowSystemMemoryMb) || (graphicsMemoryMb > 0 && graphicsMemoryMb < LowGraphicsMemoryMb);
        return low ? LowEndMinGap : MinGap;
    }
    public static float DeviceGap = Gap(SystemInfo.systemMemorySize, SystemInfo.graphicsMemorySize);
    float gap { get { return DeviceGap; } }

    // Spawners that Instantiate (not pool) a new sprite call this; the next capture gathers afresh.
    public static void Invalidate() { var r = Instance; if (r != null) r.cacheValid = false; }
    // Diagnostics for tests: how many full walks have run.
    public int Gathers { get; private set; }
    bool cacheValid, pregathered;
    int capturesSinceGather;

    // The blast renderers (TargetExplosion's pooled flipbooks, LegacyExplosionFx):
    // their entries carry KillCamEntry.Blast so the replay can keep the
    // collision beat clear of the death's explosion (KillCamStage.ContactRule).
    // Keyed by renderer instance id: a lookup per sprite, no allocation.
    static readonly HashSet<int> blasts = new HashSet<int>();

    public static void TagBlast(SpriteRenderer sr, bool on)
    {
        if (sr == null) return;
        if (on) { blasts.Add(sr.GetInstanceID()); Invalidate(); }   // (a blast just switched on or was built: the next capture walks afresh)
        else blasts.Remove(sr.GetInstanceID());
    }

    public static bool IsBlast(SpriteRenderer sr) { return sr != null && blasts.Contains(sr.GetInstanceID()); }

    readonly List<GameObject> roots = new List<GameObject>(64);
    readonly List<Renderer> scratch = new List<Renderer>(512);
    readonly List<Renderer> found = new List<Renderer>(1024);
    readonly Dictionary<int, bool> scrollQuad = new Dictionary<int, bool>(32);

    // The rings are the one big allocation (EntryCap entries: ~8 MB on the
    // large-object heap): paid once, because the recorder outlives its scene
    // (Ensure). They stay per instance: a static entry ring made the capture
    // read as allocating under the GC's accounting (KillCamTest).
    readonly KillCamEntry[] ring = new KillCamEntry[EntryCap];
    readonly KillCamMeshEntry[] meshRing = new KillCamMeshEntry[MeshCap];
    readonly float[] fTime = new float[FrameCap];
    readonly Vector2[] fCam = new Vector2[FrameCap];
    readonly int[] fFirst = new int[FrameCap], fCount = new int[FrameCap];
    readonly int[] fMeshFirst = new int[FrameCap], fMeshCount = new int[FrameCap];
    int head, frames, entryWrite, entriesLive, meshWrite, meshLive;
    float clock, lastAt = -1f, impactAt = -1f;
    bool recording, tailing;
    Camera cam;
    KillCamClip view;

    public bool Recording { get { return recording; } }
    public int Frames { get { return frames; } }
    public float Clock { get { return clock; } }
    // The fatal hit on the tape's clock (-1: none yet) and whether the death is still being taped.
    public float ImpactAt { get { return impactAt; } }
    public bool Tailing { get { return tailing; } }
    public int LastFrameEntries { get { return frames > 0 ? fCount[(head + frames - 1) % FrameCap] : 0; } }

    public static KillCamRecorder Ensure()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("~KillCamRecorder");
        Instance = go.AddComponent<KillCamRecorder>();
        // One recorder for the whole session (play mode): its ~8 MB rings used
        // to be allocated again by every gameS1 load, and left to the
        // collector with the old scene. A scene change stops the tape and
        // drops its references (OnSceneLoaded), so it never tapes a menu or
        // pins a dead scene's sprites; the next run's BeginRun clears again.
        if (Application.isPlaying)
        {
            DontDestroyOnLoad(go);
            if (!sceneHooked) { sceneHooked = true; SceneManager.sceneLoaded += OnSceneLoaded; }
        }
        return Instance;
    }

    static bool sceneHooked;
    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var r = Instance;
        if (r == null) return;
        r.recording = false;
        r.Clear();
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
        lastAt = impactAt = -1f;
        tailing = false;
        scrollQuad.Clear();
        cacheValid = pregathered = false;
        found.Clear();
        // drop the references so a finished run's sprites can unload
        System.Array.Clear(ring, 0, ring.Length);
        System.Array.Clear(meshRing, 0, meshRing.Length);
    }

    public void StopRecording() { recording = false; }

    void LateUpdate()
    {
        if (!recording || !Enabled || KillCam.Playing) return;
        if (buttonClicks.playerDied)
        {
            // the death: on the crash's clock, until the tail is long enough
            if (!tailing) return;
            Tail(DeathCrash.FrameDt);
            return;
        }
        if (Time.timeScale <= 0f) return;
        Flight(Time.deltaTime);
    }

    // One frame of flight, `dt` of game time (tests drive it by hand).
    public void Flight(float dt)
    {
        if (dt <= 0f) return;
        clock += dt;
        if (lastAt >= 0f && clock - lastAt < gap)
        {
            // not due: if the next capture will need a fresh walk, do it now, so the capture frame stays light
            if (!pregathered && (!cacheValid || capturesSinceGather + 1 >= RefreshEvery) && clock - lastAt >= gap * .5f) { Gather(true); pregathered = true; }
            return;
        }
        lastAt = clock - lastAt < 2f * gap && lastAt >= 0f ? lastAt + gap : clock;   // (paced on the gap, not on the frame grid: 24 Hz on average at 60 fps)
        CaptureCached(clock);
    }

    // One step of the death tail: `dt` of the crash's clock (0: held, nothing).
    public void Tail(float dt)
    {
        if (!tailing || dt <= 0f) return;
        clock += dt;
        if (clock - impactAt >= KillCamClip.DeathTail) tailing = false;   // (this last frame is still kept)
        if (tailing && lastAt >= 0f && clock - lastAt < gap) return;
        lastAt = clock;
        CaptureCached(clock);
    }

    // The fatal hit (DeathCrash.Play, before the hull is hidden): the collision
    // frame itself goes on the tape and the death after it is taped too.
    public static void MarkImpact()
    {
        var r = Instance;
        if (r == null || !r.recording || !Enabled || KillCam.Playing) return;
        r.Impact(Mathf.Clamp(Time.deltaTime, 0f, .1f));
    }

    // (tests: `dt` of flight since the last frame)
    public void Impact(float dt)
    {
        if (!recording) return;
        clock += Mathf.Max(dt, 1e-4f);
        lastAt = impactAt = clock;
        tailing = true;
        Capture(clock);
    }

    // The kill cam takes over (KillCam.TryBegin): the death as it stands this
    // frame (the crash has stepped, `dt` of its clock) is the tape's last frame.
    public void FinishTail(float dt)
    {
        if (!tailing) return;
        clock += Mathf.Max(dt, 1e-4f);
        lastAt = clock;
        tailing = false;
        Capture(clock);
    }

    // A revive takes the death back: flight resumes on the same tape.
    public static void CancelImpact()
    {
        var r = Instance;
        if (r == null) return;
        r.Uncrash();
    }

    // Takes the death's frames off the end of the tape (the next death's lead-in is flight only).
    public void Uncrash()
    {
        if (impactAt >= 0f)
            while (frames > 0)
            {
                int last = (head + frames - 1) % FrameCap;
                if (fTime[last] < impactAt - 1e-5f) break;
                entriesLive -= fCount[last];
                meshLive -= fMeshCount[last];
                entryWrite = fFirst[last];
                meshWrite = fMeshFirst[last];
                frames--;
            }
        lastAt = frames > 0 ? fTime[(head + frames - 1) % FrameCap] : -1f;
        tailing = false;
        impactAt = -1f;
    }

    // ---- the walk ----

    void Gather(bool inactiveToo = false)
    {
        Gathers++;
        cacheValid = inactiveToo;   // (only the runtime walk, which takes inactive renderers too, is kept)
        capturesSinceGather = 0;
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
                if (root == null) continue;
                root.GetComponentsInChildren(inactiveToo, scratch);   // (the cache takes inactive ones too: pooled sprites are in it before they switch on)
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
        Gather();
        CaptureFrom(at);
    }

    // The runtime capture: reuses the gathered renderers unless the cache is stale.
    void CaptureCached(float at)
    {
        if (!cacheValid || (!pregathered && capturesSinceGather + 1 >= RefreshEvery)) Gather(true);
        if (pregathered) pregathered = false;   // (this capture is the first on the fresh walk)
        else capturesSinceGather++;
        CaptureFrom(at);
    }

    void CaptureFrom(float at)
    {
        if (cam == null) cam = Camera.main;
        if (frames == FrameCap) DropOldest();
        int slot = (head + frames) % FrameCap;
        int start = entryWrite, n = 0;
        int meshStart = meshWrite, mn = 0;
        for (int i = 0; i < found.Count; i++)
        {
            var r = found[i];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.forceRenderingOff || (RequireVisible && !r.isVisible)) continue;
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
                                 ((int)mode << KillCamEntry.DrawModeShift) |
                                 (blasts.Count > 0 && blasts.Contains(e.track) ? KillCamEntry.Blast : 0));
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
    // starting at 0) -- with a death on the tape, the last `window` seconds
    // before the fatal hit and the whole death after it (the clip's impact).
    // The clip shares the rings: valid until recording resumes. Null when there
    // is not enough to play.
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
        float anchor = impactAt >= 0f ? Mathf.Min(impactAt, end) : end;
        int from = 0;
        while (from < frames - 2 && fTime[(head + from) % FrameCap] < anchor - window) from++;
        int n = frames - from;
        float t0 = fTime[(head + from) % FrameCap];
        view.impact = impactAt >= 0f ? Mathf.Max(0f, anchor - t0) : -1f;
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
