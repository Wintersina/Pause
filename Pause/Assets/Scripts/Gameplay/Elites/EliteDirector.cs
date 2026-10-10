using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// When and where elite ships turn up (gameS1 only), and the one place the
// elite world is stepped from.
//
// RULES
//   * at most MaxAlive elites at once (parked ones count)
//   * groups of 1-3 (never past MaxAlive) at random times: GapSeconds
//     between groups, a little shorter on later loops (LoopRules ->
//     RunLoop.Index, GapLoopScale)
//   * none in the first FirstSeconds of flight in a world, none during a
//     boss (BossEncounter.Running) or in its run-up (SecondsLeftInWorld <
//     BossLeadSeconds), none while a portal waits, none in the tutorial,
//     none in a world without elite defs or landing sites
//   * each one parks on a landing site of the world's backdrop
//     (LandingSites) -- of the kind its def launches from (launchFrom:
//     a Space elite's station / planet / big asteroid) when one is free,
//     else any free one -- at least a few seconds, and its lift-off ends at a
//     join point at least EliteShip.MinJoinDistance from the pilot, on the
//     side its brain likes (JoinFrom), and inside the rails
//   * a boss arriving clears the parked ones quietly (the ones in play are
//     blown away with the rest of the board by BossEncounter)
//
// Stepped on running frames only (moveBackGround's freeze sets timeScale 0
// and the same "flying" rule every hazard mover uses).
public class EliteDirector : MonoBehaviour
{
    public const int MaxAlive = 3;
    public const float FirstSeconds = 20f;
    public const float BossLeadSeconds = 12f;
    public static readonly Vector2 GapSeconds = new Vector2(16f, 32f);
    public const float GapLoopScale = .12f;   // each loop: gaps x 1 / (1 + this * loop), at most x 1/1.5
    public static readonly Vector2 ParkSeconds = new Vector2(2.5f, 5f);

    public static EliteDirector Instance { get; private set; }

    // Off by default in a scene; the bootstrap adds it to gameS1.
    public static bool Disabled;

    float worldSeconds;
    int world = -1;
    float untilNext;
    readonly List<LandingSite> sites = new List<LandingSite>(16);
    readonly List<EliteDef> defs = new List<EliteDef>(8);
    readonly List<int> usedSites = new List<int>(8);

    public float WorldSeconds => worldSeconds;
    public float UntilNext => untilNext;
    public int Groups { get; private set; }
    public int Spawned { get; private set; }

    void Awake()
    {
        Instance = this;
        untilNext = FirstSeconds + Random.Range(0f, 8f);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        EliteSystem.Clear();
    }

    static bool Flying => !buttonClicks.playerDied && (TouchInput.IsPressed || score.pauseCounter <= 0);

    void Update()
    {
        // a player death: the themed area hazards vanish (the crash domino owns the board)
        if (buttonClicks.playerDied && AttackHazard.ActiveCount > 0) AttackPools.ClearAll();
        if (!Flying || WorldEntry.Active) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        Tick(dt);
        EliteSystem.Step(dt);
    }

    // ---- scheduling (dt explicit for tests) ----------------------------------

    public static int CurrentWorld => WorldManager.Instance != null ? WorldManager.CurrentIndex : -1;

    // Why nothing may spawn right now, or null.
    public static string Blocked(int worldIndex, float secondsInWorld)
    {
        if (Disabled) return "disabled";
        if (startMenu.youAreInTutorial) return "tutorial";
        if (worldIndex < 0) return "no world";
        if (BossEncounter.Running) return "boss";
        var wm = WorldManager.Instance;
        if (wm != null && wm.DistanceLeft > 0f && wm.SecondsLeftInWorld < BossLeadSeconds) return "boss soon";
        // (for the whole wait, pressure or not: an elite pays dust and
        // takes seconds to lift off; the wait must never be worth farming)
        if (wm != null && wm.PortalIsOpen) return "portal";
        if (secondsInWorld < FirstSeconds) return "too early";
        if (!EliteCatalog.WorldHasElites(worldIndex)) return "no elites";
        return null;
    }

    public void Tick(float dt)
    {
        int w = CurrentWorld;
        if (w != world)
        {
            world = w; worldSeconds = 0f; untilNext = Mathf.Max(untilNext, FirstSeconds);
            // an elite of another world does not stay on (a dev jump, a way on that left it)
            var left = EliteShip.Live;
            for (int i = left.Count - 1; i >= 0; i--)
                if (w >= 0 && left[i] != null && left[i].Def != null && left[i].Def.WorldIndex != w) BossUtil.Kill(left[i].gameObject);
        }
        worldSeconds += dt;

        // a boss clears the background of parked / rising elites
        if (BossEncounter.Running) { DismissParked(); return; }

        untilNext -= dt;
        if (untilNext > 0f) return;
        if (Blocked(world, worldSeconds) != null) { untilNext = 1f; return; }
        int spawned = SpawnGroup(world, Random.Range(1, 4));
        untilNext = spawned > 0 ? NextGap() : 1.5f;
    }

    public static float NextGap()
    {
        float loopScale = 1f / Mathf.Min(1.5f, 1f + GapLoopScale * Mathf.Max(0, RunLoop.Index));
        return Random.Range(GapSeconds.x, GapSeconds.y) * loopScale;
    }

    public static int AliveCount
    {
        get
        {
            int n = 0;
            var live = EliteShip.Live;
            for (int i = 0; i < live.Count; i++) if (live[i] != null && live[i].State != EliteState.Dead) n++;
            return n;
        }
    }

    // Parks up to `size` elites of `worldIndex` on free landing sites (never
    // past MaxAlive). Returns how many.
    public int SpawnGroup(int worldIndex, int size)
    {
        int room = MaxAlive - AliveCount;
        size = Mathf.Min(size, room);
        if (size <= 0) return 0;
        if (EliteCatalog.ForWorld(worldIndex, defs) == 0) return 0;
        if (LandingSites.Gather(sites) == 0) return 0;

        usedSites.Clear();
        var live = EliteShip.Live;
        for (int i = 0; i < live.Count; i++)
            if (live[i] != null && live[i].State == EliteState.Parked) usedSites.Add(live[i].Site.id);

        int made = 0;
        float stagger = 0f;
        for (int k = 0; k < size; k++)
        {
            var def = defs[Random.Range(0, defs.Count)];
            int s = PickSite(def.launchFrom);
            if (s < 0) break;
            usedSites.Add(sites[s].id);
            float park = Random.Range(ParkSeconds.x, ParkSeconds.y) + stagger;
            stagger += Random.Range(.8f, 1.6f);
            Spawn(def, sites[s], park);
            made++;
        }
        if (made > 0) Groups++;
        return made;
    }

    // One elite of `def` parked on `site` for `park` seconds.
    public EliteShip Spawn(EliteDef def, LandingSite site, float park)
    {
        var brain = EliteBrains.Create(def.brain);
        Spawned++;
        return EliteShip.Create(def, site, park, JoinPoint(def, brain.JoinFrom, site.Position));
    }

    // A free site, of the kind the elite launches from when one is free
    // (a Space elite: its station / planet / asteroid), else any free one.
    int PickSite(string launchFrom)
    {
        bool prefer = !string.IsNullOrEmpty(launchFrom) && LandingSite.KindOf(launchFrom) != null;
        int start = Random.Range(0, Mathf.Max(1, sites.Count));
        int any = -1;
        for (int k = 0; k < sites.Count; k++)
        {
            int i = (start + k) % sites.Count;
            if (!sites[i].Valid || usedSites.Contains(sites[i].id)) continue;
            if (!prefer || LandingSite.Accepts(launchFrom, sites[i].kind)) return i;
            if (any < 0) any = i;
        }
        return any;
    }

    // Where a lift-off ends: on the brain's preferred side of the pilot,
    // at least MinJoinDistance away, inside the rails and the view.
    public static Vector2 JoinPoint(EliteDef def, Vector2 from, Vector3 site)
    {
        var p = EliteSystem.Player;
        Vector2 pilot = p != null ? (Vector2)p.position : new Vector2(0f, -2.5f);
        float edge = EliteSystem.RailEdge - def.hullRadius - .4f;
        float top = EliteSystem.ViewTop - def.hullRadius - .5f, bottom = EliteSystem.ViewBottom + def.hullRadius + .6f;
        Vector2 best = Vector2.zero;
        float bestScore = float.MinValue;
        float prefer = Mathf.Atan2(from.y, from.x);
        // all the way round, nearest the preferred side first
        for (int k = 0; k < 32; k++)
        {
            float turn = (k / 2) * (Mathf.PI / 8f) * (k % 2 == 0 ? 1f : -1f);
            float a = prefer + turn;
            for (int ring = 0; ring < 2; ring++)
            {
                float r = EliteShip.MinJoinDistance + .4f + ring * .8f;
                Vector2 c = pilot + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                c.x = Mathf.Clamp(c.x, -edge, edge);
                c.y = Mathf.Clamp(c.y, bottom, top);
                float dist = (c - pilot).magnitude;
                float score = (dist >= EliteShip.MinJoinDistance ? 10f : dist) - Mathf.Abs(turn) * .5f
                              - Mathf.Abs(c.x - site.x) * .05f;
                if (score > bestScore) { bestScore = score; best = c; }
            }
        }
        return best;
    }

    void DismissParked()
    {
        var live = EliteShip.Live;
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var e = live[i];
            if (e != null && (e.State == EliteState.Parked || e.State == EliteState.LiftOff))
                BossUtil.Kill(e.gameObject);
        }
    }
}

public static class EliteDirectorBootstrap
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
        if (EliteDirector.Instance != null) return;
        new GameObject("~EliteDirector").AddComponent<EliteDirector>();
    }
}
