using UnityEngine;
using UnityEngine.SceneManagement;

// When a gameplay asteroid turns up (gameS1, Space only): SpaceAsteroidField's schedule.
//
// RULES (Blocked says which one stops a spawn):
//   * only in Space (the world proper or the interlude space leg), never in the tutorial
//   * at most MaxAlive rocks on screen, the next one a random Gap after the last arrived
//   * none in the first FirstSeconds of a world (inside it: the 8 s calm start), none in a boss's run-up
//   * none during a boss, a planetfall / lift-off / portal arrival / world entry, while a portal waits, after a death,
//     in a cinematic ultimate, nor while an elite is on the board (the elites own the lane)
//   * a world change, a death or a boss arriving clears the field
//
// Stepped on running frames only (the same "flying" rule every hazard mover uses), so a frozen world freezes the schedule.
public class SpaceAsteroidFieldDirector : MonoBehaviour
{
    public static SpaceAsteroidFieldDirector Instance { get; private set; }

    float worldSeconds, untilNext;
    int world = -1;
    System.Random rng;

    public float WorldSeconds => worldSeconds;
    public float UntilNext => untilNext;

    void Awake() { Begin(); }

    // (edit-mode AddComponent skips Awake: tests call this)
    public void Begin()
    {
        Instance = this;
        rng = new System.Random(SpaceAsteroidField.SeedOverride >= 0 ? SpaceAsteroidField.SeedOverride : System.Environment.TickCount);
        untilNext = SpaceAsteroidField.FirstSeconds + Next(0f, 10f);
        world = -1; worldSeconds = 0f;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SpaceAsteroidField.ClearAll();
    }

    float Next(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

    static bool Flying => !buttonClicks.playerDied && (TouchInput.IsPressed || score.pauseCounter <= 0);

    void Update()
    {
        if (buttonClicks.playerDied && SpaceAsteroidField.Live.Count > 0) SpaceAsteroidField.ClearAll();
        if (!Flying || WorldEntry.Active) return;
        float dt = Time.deltaTime;
        if (dt > 0f) Tick(dt);
    }

    public static int WorldOverride = -1;   // tests: the world the director believes it is in (>= 0)
    public static int CurrentWorld => WorldOverride >= 0 ? WorldOverride : WorldManager.Instance != null ? WorldManager.SpawnWorld : -1;

    // Why no rock may spawn right now, or null.
    public static string Blocked(int worldIndex, float secondsInWorld)
    {
        if (SpaceAsteroidField.Disabled) return "disabled";
        if (startMenu.youAreInTutorial) return "tutorial";
        if (worldIndex != SpaceAsteroidField.SpaceWorld) return "not space";
        if (buttonClicks.playerDied) return "dead";
        if (SpaceAsteroidField.Alive >= SpaceAsteroidField.MaxAlive) return "alive";
        if (BossEncounter.Running) return "boss";
        if (Planetfall.SuspendsSpawning || Liftoff.SuspendsSpawning || PortalArrival.SuspendsSpawning || WorldEntry.Active) return "transition";
        var wm = WorldManager.Instance;
        if (wm != null && wm.DistanceLeft > 0f && wm.SecondsLeftInWorld < SpaceAsteroidField.BossLeadSeconds) return "boss soon";
        if (wm != null && wm.PortalIsOpen) return "portal";
        if (ShipPowerController.CinematicClearActive) return "cinematic";
        if (EliteDirector.AliveCount > 0) return "elite";
        if (secondsInWorld < SpaceAsteroidField.FirstSeconds) return "too early";
        return null;
    }

    // One running frame of scheduling (dt explicit for tests).
    public void Tick(float dt)
    {
        if (rng == null) Begin();
        int w = CurrentWorld;
        if (w != world)
        {
            // a new world: nothing of the old one stays
            world = w; worldSeconds = 0f;
            untilNext = Mathf.Max(untilNext, SpaceAsteroidField.FirstSeconds);
            SpaceAsteroidField.ClearAll();
        }
        worldSeconds += dt;
        if (BossEncounter.Running || Planetfall.SuspendsSpawning || Liftoff.SuspendsSpawning || PortalArrival.SuspendsSpawning)
        {
            if (SpaceAsteroidField.Live.Count > 0) SpaceAsteroidField.ClearAll();
            return;
        }
        untilNext -= dt;
        if (untilNext > 0f) return;
        if (Blocked(world, worldSeconds) != null) { untilNext = 1f; return; }
        untilNext = SpawnOne() != null ? Next(SpaceAsteroidField.Gap.x, SpaceAsteroidField.Gap.y) : 2f;
    }

    // A rock from above the view: a size drawn small-leaning, a slow sideways heading, clear of other hazards.
    public FieldAsteroid SpawnOne()
    {
        if (rng == null) Begin();
        int tier = SpaceAsteroidField.TierFor((float)rng.NextDouble());
        float reach = SpaceAsteroidField.Width[tier];
        float lane = SpaceAsteroidField.Edge * SpaceAsteroidField.LaneShare;
        float x = Next(-lane, lane);
        float y = CameraFit.ViewTop + reach + SpaceAsteroidField.SpawnMargin;
        var at = new Vector2(x, y);
        if (!Clear(at)) return null;
        // heads toward the middle of the lane, so it crosses the view instead of leaving by a rail at once
        float speed = Next(SpaceAsteroidField.Sideways.x, SpaceAsteroidField.Sideways.y);
        float dir = x > lane * .5f ? -1f : x < -lane * .5f ? 1f : (rng.NextDouble() < .5 ? -1f : 1f);
        float spin = Next(SpaceAsteroidField.Spin.x, SpaceAsteroidField.Spin.y) * (rng.NextDouble() < .5 ? -1f : 1f);
        return SpaceAsteroidField.Spawn(tier, rng.Next(3), at, dir * speed, 0f, spin);
    }

    // No other hazard near the spawn point.
    static bool Clear(Vector2 at)
    {
        var live = ClearTarget.Live;
        float r2 = SpaceAsteroidField.ClearRadius * SpaceAsteroidField.ClearRadius;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (t == null || t.Scenery || !ClearTarget.IsHazard(t.gameObject)) continue;
            Vector2 d = (Vector2)t.transform.position - at;
            if (d.sqrMagnitude < r2) return false;
        }
        return true;
    }
}

public static class SpaceAsteroidFieldBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "gameS1") return;
        SpaceAsteroidField.Forget();   // (a stale list of the last run)
        if (SpaceAsteroidFieldDirector.Instance != null) return;
        new GameObject("~SpaceAsteroidFieldDirector").AddComponent<SpaceAsteroidFieldDirector>();
    }
}
