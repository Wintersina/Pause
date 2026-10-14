using UnityEngine;
using UnityEngine.SceneManagement;

// When a cover boulder turns up (gameS1 only), and the one place the boulders are stepped from: docs/cover-boulders.md section 5.
//
// RULES (Blocked says which one stops a spawn):
//   * at most one on screen; the next one comes a random world-table gap after the last arrived
//   * none in the first FirstSeconds of a world (inside it: the 8 s calm start), none before the world's art is wired
//   * none during a boss (BossEncounter.Running), in its run-up (BossLeadSeconds), a planetfall / lift-off / portal arrival /
//     world entry, while a portal or planet waits, in the tutorial, after a death, in a cinematic ultimate
//   * none while an elite is parked or lifting off (their landing sites and entrances stay clear)
//   * a world change, a death or the scene ending removes the boulder at once (a quiet fade: not the boss destruction)
//
// Stepped on running frames only (the same "flying" rule every hazard mover uses), so a frozen world freezes boulders.
public class CoverBoulderDirector : MonoBehaviour
{
    public static CoverBoulderDirector Instance { get; private set; }

    float worldSeconds, untilNext;
    int world = -1;
    int lastVariant = -1;

    public float WorldSeconds => worldSeconds;
    public float UntilNext => untilNext;

    void Awake() { Begin(); }

    // (edit-mode AddComponent skips Awake: tests call this)
    public void Begin()
    {
        Instance = this;
        untilNext = CoverBoulders.FirstSeconds + Random.Range(0f, 15f);
        CoverBoulders.Prewarm();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        CoverBoulders.Dispose();
    }

    static bool Flying => !buttonClicks.playerDied && (TouchInput.IsPressed || score.pauseCounter <= 0);

    void Update()
    {
        if (buttonClicks.playerDied && CoverBoulders.Alive > 0) CoverBoulders.DismissAll();
        if (!Flying || WorldEntry.Active) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        Tick(dt);
        CoverBoulders.Step(dt);
    }

    // After every mover has run: bodies and atoms are pushed out of the boulder.
    void LateUpdate()
    {
        if (CoverBoulders.Any) CoverBoulders.PushBodies();
    }

    // The backdrop variant (1..4) the boulders must blend into: tests set BackdropVariantOverride (> 0); in play it is the live
    // backdrop set's own variant, and only while that set is this world's (a cross-fade in progress counts as no match: 0).
    public static int BackdropVariantOverride = -1;
    public static int BackdropVariant(int worldIndex)
    {
        if (BackdropVariantOverride >= 0) return BackdropVariantOverride;
        var wb = WorldBackdrop.Instance;
        if (wb == null || wb.Current == null || worldIndex < 0 || worldIndex >= WorldManager.Worlds.Length) return 0;
        return wb.Current.Spec.world == WorldManager.Worlds[worldIndex].displayName ? wb.Current.Variant : 0;
    }

    public static int WorldOverride = -1;   // tests: the world the director believes it is in (>= 0)
    public static int CurrentWorld => WorldOverride >= 0 ? WorldOverride : WorldManager.Instance != null ? WorldManager.SpawnWorld : -1;

    // Why no boulder may spawn right now, or null.
    public static string Blocked(int worldIndex, float secondsInWorld)
    {
        if (CoverBoulders.Disabled) return "disabled";
        if (startMenu.youAreInTutorial) return "tutorial";
        if (worldIndex < 0) return "no world";
        if (buttonClicks.playerDied) return "dead";
        if (!CoverBoulderTable.Enabled(worldIndex) && !CoverBoulders.TestAnyWorld) return "no art";
        if (CoverBoulderTable.For(worldIndex).Mask(BackdropVariant(worldIndex)) == 0) return "backdrop";   // nothing blends into this backdrop
        if (BossEncounter.Running) return "boss";
        if (Planetfall.SuspendsSpawning || Liftoff.SuspendsSpawning || PortalArrival.SuspendsSpawning || WorldEntry.Active) return "transition";
        var wm = WorldManager.Instance;
        if (wm != null && wm.DistanceLeft > 0f && wm.SecondsLeftInWorld < CoverBoulders.BossLeadSeconds) return "boss soon";
        if (wm != null && wm.PortalIsOpen) return "portal";
        if (ShipPowerController.CinematicClearActive) return "cinematic";
        if (secondsInWorld < CoverBoulders.FirstSeconds) return "too early";
        if (ElitesParked()) return "elite";
        if (CoverBoulders.Alive >= CoverBoulders.MaxAlive) return "alive";
        return null;
    }

    static bool ElitesParked()
    {
        var live = EliteShip.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var e = live[i];
            if (e != null && (e.State == EliteState.Parked || e.State == EliteState.LiftOff)) return true;
        }
        return false;
    }

    // One running frame of scheduling (dt explicit for tests).
    public void Tick(float dt)
    {
        int w = CurrentWorld;
        if (w != world)
        {
            // a new world: nothing of the old one stays
            world = w; worldSeconds = 0f;
            untilNext = Mathf.Max(untilNext, CoverBoulders.FirstSeconds);
            CoverBoulders.DismissAll();
        }
        worldSeconds += dt;
        if (BossEncounter.Running || Planetfall.SuspendsSpawning || Liftoff.SuspendsSpawning || PortalArrival.SuspendsSpawning)
        {
            // a boss: the destruction sequence (OnBossBegin) has the boulder; a transition: it fades
            if (!BossEncounter.Running && CoverBoulders.Alive > 0) CoverBoulders.DismissAll();
            return;
        }
        untilNext -= dt;
        if (untilNext > 0f) return;
        if (Blocked(world, worldSeconds) != null) { untilNext = 1f; return; }
        if (SpawnOne(world) != null) untilNext = NextGap(world);
        else untilNext = 5f;
    }

    public static float NextGap(int worldIndex)
    {
        var e = CoverBoulderTable.For(worldIndex);
        return Random.Range(e.gap.x, e.gap.y);
    }

    static int BitCount(int m) { int n = 0; while (m != 0) { n += m & 1; m >>= 1; } return n; }

    public CoverBoulder SpawnOne(int worldIndex)
    {
        var e = CoverBoulderTable.For(worldIndex);
        int mask = e.Mask(BackdropVariant(worldIndex));
        if (mask == 0) return null;
        // a shape that blends into this backdrop, never the same silhouette twice running when another fits
        int n = 0, pick = -1;
        for (int i = 0; i < e.Variants; i++)
            if ((mask & (1 << i)) != 0 && (i != lastVariant || BitCount(mask) == 1))
                if (Random.Range(0, ++n) == 0) pick = i;
        if (pick < 0) return null;
        lastVariant = pick;
        return CoverBoulders.Spawn(worldIndex, pick);
    }
}

public static class CoverBoulderDirectorBootstrap
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
        if (CoverBoulderDirector.Instance != null) return;
        CoverBoulders.Dispose();   // (a stale pool of the last run)
        new GameObject("~CoverBoulderDirector").AddComponent<CoverBoulderDirector>();
    }
}
