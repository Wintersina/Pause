using System;
using System.Collections.Generic;
using UnityEngine;

// The second "first-use hitch" sweep. Things the game used to load, slice, decode or build on the frame it first
// needed them -- the first shot's clip, the ship's weapon sheet, the first pause's sprites, the first death panel's
// button copy, the first liftoff's shader, the fusion strips -- are done here ahead of time, ONE STEP A FRAME, during
// the menu idle and the run's calm start (the same idea as ArtWarm / WorldArtPreload / BoostJet.Prewarm).
//
// A step is an optional asynchronous Resources.LoadAsync (the file read + decode happens off the main thread; the
// asset is HELD here so a later Resources.Load of the same path answers from memory) and an optional `post` that
// runs on the main thread once the asset is resident (slice the sprites, create the AudioSource, force a clip
// decode, copy a texture). Budget: at most ONE step per frame, and each post is the same small piece of work the
// first use used to do (a Sprite.Create of a sheet, a 100 x 40 texture copy, a clip decode kick), never several
// stacked on one frame; with nothing queued Pump costs one comparison. Steps are keyed: asking again is a no-op.
//
// Out of play (edit-mode suites) Pump loads synchronously, one step per call, so a test can count steps.
public static class FirstUseWarm
{
    // A step's budget: the number of steps one Pump runs. The queue is drained one a frame.
    public const int StepsPerFrame = 1;
    // Seconds after the scene starts before the first step (the first frames are for the scene's own start).
    public const float StartDelay = 1.5f;

    struct Step { public string key; public string path; public Type type; public Action post; }

    static readonly Queue<Step> queue = new Queue<Step>(64);
    static readonly HashSet<string> asked = new HashSet<string>();
    static readonly List<UnityEngine.Object> held = new List<UnityEngine.Object>(128);
    static ResourceRequest inFlight;
    static Step flying;
    static bool flightLive;

    public static int Queued { get { return queue.Count; } }
    public static int Ran { get; private set; }
    public static int HeldCount { get { return held.Count; } }
    public static bool Done { get { return queue.Count == 0 && !flightLive; } }
    public static bool Asked(string key) { return asked.Contains(key); }
    public static bool Holds(UnityEngine.Object o) { return o != null && held.Contains(o); }

    // Test seam: replaces the load of a step (type, path) -> object.
    public static Func<Type, string, UnityEngine.Object> SyncLoader;

    // Queues one step once per key. `path` may be null (a pure main-thread step).
    public static bool Add(string key, string path, Type type, Action post)
    {
        if (string.IsNullOrEmpty(key) || !asked.Add(key)) return false;
        queue.Enqueue(new Step { key = key, path = path, type = type, post = post });
        if (Application.isPlaying) FirstUseWarmRunner.Ensure();
        return true;
    }

    public static bool Add(string key, Action post) { return Add(key, null, null, post); }

    // One frame of warm-up. Returns true while there is more to do.
    public static bool Pump()
    {
        if (flightLive)
        {
            if (inFlight != null && !inFlight.isDone) return true;
            if (inFlight != null && inFlight.asset != null) held.Add(inFlight.asset);
            var s = flying;
            flightLive = false;
            inFlight = null;
            Run(s);
            return !Done;
        }
        if (queue.Count == 0) return false;
        var step = queue.Dequeue();
        bool sync = !Application.isPlaying || SyncLoader != null;
        if (step.path != null && step.type != null)
        {
            if (sync)
            {
                var o = SyncLoader != null ? SyncLoader(step.type, step.path) : Resources.Load(step.path, step.type);
                if (o != null) held.Add(o);
            }
            else
            {
                inFlight = Resources.LoadAsync(step.path, step.type);
                flying = step;
                flightLive = true;
                return true;
            }
        }
        Run(step);
        return !Done;
    }

    static void Run(Step s)
    {
        Ran++;
        if (s.post == null) return;
        try { s.post(); }
        catch (Exception e) { Debug.LogWarning("[FirstUseWarm] " + s.key + ": " + e.Message); }
    }

    // Tests: forget everything (the held assets are released to the next asset sweep).
    public static void Clear()
    {
        queue.Clear();
        asked.Clear();
        held.Clear();
        inFlight = null;
        flightLive = false;
        Ran = 0;
    }

    // ---- what is warmed, in the order it is wanted ----------------------------------------------------------------

    // Everything a session will use whatever ship flies: the first pause / death panel / toast, the fusion strips, the
    // liftoff shader and the always-on sounds. Called at app start (menu idle) and again at each run start (a no-op).
    public static void QueueSession()
    {
        PewSound.QueueWarm();
        WeaponArt.QueueWarmExplosions();
        FirstUseWarmSteps.Pause();
        FirstUseWarmSteps.DeathPanel();
        FirstUseWarmSteps.Fusion();
        FirstUseWarmSteps.Liftoff();
        FirstUseWarmSteps.Achievements();
        FirstUseWarmSteps.Cues();
    }

    // The flown ship's own art: weapon sheet, attack and power strips, rotors, the secret-power badge, the ultimate gun.
    public static void QueueShip(int ship)
    {
        if (!ShipId.IsValid(ship)) return;
        WeaponArt.QueueWarm(ship);
        ShipFxArt.QueueWarm(ship);
        ShipRotors.QueueWarm(ship);
        UltimateGun.QueueWarm(ship);
    }

    // A world begins (ArtWarm.Begin): the death panel's frame for it.
    public static void QueueWorld(int world)
    {
        FirstUseWarmSteps.DeathFrame(world);
    }

    // Menu idle: the session items and the equipped ship's.
    public static void QueueAtStart()
    {
        if (PerfTier.LowEnd) return;   // low-end phones hold nothing in the menu: the run start queues the same steps (PerfTier is the one low-end decision)
        QueueSession();
        QueueShip(ShipId.Equipped());
    }
}

// Pumps the warm-up each frame while something is queued; lives for the session.
public sealed class FirstUseWarmRunner : MonoBehaviour
{
    static FirstUseWarmRunner live;
    float startAt;

    public static void Ensure()
    {
        if (live != null) { live.enabled = true; return; }
        var go = new GameObject("~FirstUseWarm");
        go.hideFlags = HideFlags.HideAndDontSave;
        DontDestroyOnLoad(go);
        live = go.AddComponent<FirstUseWarmRunner>();
        live.startAt = Time.unscaledTime + FirstUseWarm.StartDelay;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        FirstUseWarm.QueueAtStart();
    }

    void Update()
    {
        if (Time.unscaledTime < startAt) return;
        if (!FirstUseWarm.Pump() && FirstUseWarm.Done) enabled = false;
    }
}
