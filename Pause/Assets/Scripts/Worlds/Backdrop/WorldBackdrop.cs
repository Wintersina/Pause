using UnityEngine;
using UnityEngine.SceneManagement;

// The animated, multi-layer parallax background behind gameS1 and the
// tutorial. Replaces the single scrolling star quad (whose renderer is hidden
// while a set is up; its moveStarsBackground component keeps running because
// it also owns the PAUSED glow).
//
// Speed coupling
//   Foreground hazards move at moveBackGround.speed * 30 units/s. Every layer
//   moves at rate * ScrollVelocity(speed), where ScrollVelocity adds a small
//   base so the world still drifts at speed 0 (each planet starts there).
//   Positions are *integrated* (offset += velocity * dt) rather than computed
//   as time * speed, so a speed change -- an atom's +0.05 kick, a world reset
//   to 0 -- changes how fast things move, never where they are.
//
// Freezing
//   Everything advances by scaled time only: dt = unscaled frame delta x
//   Time.timeScale (the same value as Time.deltaTime). moveBackGround sets
//   timeScale to 0 whenever the world is not running (finger lifted with
//   pauses left, death), so scroll, flipbooks, twinkles, spawn timers and
//   cross-fades all stop dead and the paused frame is a still image. The
//   cinematic clear's slow timeScale slows the background with everything
//   else. Nothing here uses unscaled time for animation.
public class WorldBackdrop : MonoBehaviour
{
    public const float BaseSpeed = 0.2f;        // added to moveBackGround.speed
    public const float UnitsPerSpeed = 30f;     // matches the hazards' speed * 30
    public const float CrossfadeSeconds = 1.1f;

    static readonly string[] SceneStarfields = { "starsBackground0", "starsBackground" };
    static readonly string[] Walls = { "leftPipe", "rightPipe" };

    public static WorldBackdrop Instance { get; private set; }

    public BackdropSet Current { get; private set; }
    BackdropSet outgoing;
    float fade = 1f;
    Renderer[] hiddenStarfields;
    Camera cam;
    string defaultWorld;

    public static float ScrollVelocity(float speed)
    {
        return UnitsPerSpeed * (BaseSpeed + Mathf.Max(0f, speed));
    }

    // From WorldManager (gameS1) on start and on every portal transition.
    public static void Apply(WorldTheme theme, bool crossfade)
    {
        if (theme == null) return;
        if (Instance == null) Create(theme.displayName);
        Instance.Show(theme.displayName, crossfade);
    }

    public static WorldBackdrop Create(string defaultWorldName)
    {
        if (Instance != null) return Instance;
        var go = new GameObject("~WorldBackdrop");
        var wb = go.AddComponent<WorldBackdrop>();
        wb.defaultWorld = defaultWorldName;
        Instance = wb;
        return wb;
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        if (Application.isPlaying) SpaceSkySelection.BeginRun();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (Current != null) Current.Destroy();
        if (outgoing != null) outgoing.Destroy();
        Current = outgoing = null;
    }

    public void Show(string world, bool crossfade)
    {
        if (Current != null && Current.Spec.world == BackdropCatalog.For(world).world) return;

        float hw, hh;
        ViewExtents(out hw, out hh);
        var next = new BackdropSet(world, transform, hw, hh);
        if (!next.Complete)
        {
            // Missing art: keep (or restore) the scene's own background.
            Debug.LogWarning("[Backdrop] incomplete art for " + world + "; keeping the scene background");
            next.Destroy();
            return;
        }

        if (outgoing != null) outgoing.Destroy();
        outgoing = null;
        if (crossfade && Current != null)
        {
            outgoing = Current;
            fade = 0f;
            next.Alpha = 0f;
        }
        else if (Current != null)
        {
            Current.Destroy();
        }
        Current = next;
        HideSceneStarfield();
        FixWallQueue();
        next.Tick(0f, ScrollVelocity(moveBackGround.speed));
    }

    void Update()
    {
        // Applied after every Start() has run, so WorldManager's choice of
        // world (dev picker, saved progress) wins over this default.
        if (Current == null && !string.IsNullOrEmpty(defaultWorld))
        {
            string w = defaultWorld;
            defaultWorld = null;
            Show(w, false);
        }
        Step(Time.unscaledDeltaTime);
    }

    // One frame. `unscaledDt` is scaled here, so a paused game passes 0 down.
    public void Step(float unscaledDt)
    {
        float dt = Mathf.Min(unscaledDt, Time.maximumDeltaTime) * Time.timeScale;
        FollowCamera();
        float v = ScrollVelocity(moveBackGround.speed);

        if (outgoing != null)
        {
            fade = Mathf.Min(1f, fade + dt / CrossfadeSeconds);
            outgoing.Alpha = 1f - fade;
            Current.Alpha = fade;
            outgoing.Tick(dt, v);
            if (fade >= 1f)
            {
                outgoing.Destroy();
                outgoing = null;
                if (Application.isPlaying) Resources.UnloadUnusedAssets();
            }
        }
        if (Current != null) Current.Tick(dt, v);
    }

    void FollowCamera()
    {
        if (cam == null) cam = Camera.main;
        if (cam != null)
        {
            Vector3 p = cam.transform.position;
            transform.position = new Vector3(p.x, p.y, 0f);
        }
        float hw, hh;
        ViewExtents(out hw, out hh);
        if (Current != null) Current.Layout(hw, hh);
        if (outgoing != null) outgoing.Layout(hw, hh);
    }

    void ViewExtents(out float halfW, out float halfH)
    {
        if (cam == null) cam = Camera.main;
        if (cam != null && cam.orthographic)
        {
            halfH = cam.orthographicSize;
            halfW = halfH * cam.aspect;
        }
        else
        {
            halfW = CameraFit.GameplayHalfWidth;   // the gameplay view's minimum half-width
            halfH = halfW * 19.5f / 9f;            // a 9:19.5 phone at that width
        }
    }

    void HideSceneStarfield()
    {
        if (hiddenStarfields != null) return;
        var list = new System.Collections.Generic.List<Renderer>();
        foreach (string n in SceneStarfields)
        {
            var go = GameObject.Find(n);
            var r = go != null ? go.GetComponent<Renderer>() : null;
            if (r == null) continue;
            r.enabled = false;
            list.Add(r);
        }
        hiddenStarfields = list.ToArray();
    }

    // The walls' materials sit in the opaque queue; move them to the
    // transparent queue so sorting order (0, above every backdrop layer)
    // decides, independent of whether their shader writes depth.
    static void FixWallQueue()
    {
        foreach (string n in Walls)
        {
            var go = GameObject.Find(n);
            var r = go != null ? go.GetComponent<Renderer>() : null;
            if (r == null) continue;
            var m = Application.isPlaying ? r.material : r.sharedMaterial;
            if (m != null && Application.isPlaying && m.renderQueue < 3000) m.renderQueue = 3000;
        }
    }
}

// gameS1 gets its world from WorldManager; the tutorial always flies Space.
public static class WorldBackdropBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "tutorialS5")
        {
            WorldBackdrop.Create(WorldManager.Worlds[0].displayName);
            // No WorldManager here to paint the walls: without this the
            // tutorial would keep the scene's legacy rail texture and layout
            // while the game shows Space's reinforced rail.
            WorldPainter.Apply(WorldManager.Worlds[0]);
        }
        else if (scene.name == "gameS1")
            WorldBackdrop.Create(WorldManager.Current.displayName);
    }
}
