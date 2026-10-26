using UnityEngine;

// The Magnetic Atom (prefab "magnetAtom", PickupKind.Magnet). Collecting it
// (collisionDetection) calls Begin; this component, added to the ship, then
// runs the pull for PullSeconds on the game clock. What it moves is decided
// every frame by MagnetCombo (the rules and the tunables live there):
//
//   * ALWAYS: every goodie on screen -- star dust and the other atoms -- is
//     hauled into the hull, faster the longer the pull has run. Reaching the
//     hull collects it the ordinary way (collisionDetection / ShipHitbox), so
//     it pays, fills the secret meter and counts for achievements exactly as
//     if flown into.
//
//   * WHILE THE BLUE-ATOM SHIELD IS ALSO UP (MagnetCombo.EnemyPullActive,
//     read live from collisionDetection.atomCheck, so the order the two were
//     caught in never matters): every plain enemy on screen is sucked in
//     toward the hull, ramping from a slide to a rush (MagnetCombo.EnemySpeed);
//     the shield destroys what touches it, by the ordinary shield kill. The
//     instant either power ends, the pulled enemies are let go (their movers
//     switched back on) and fly on.
//
//     Never pulled: bosses, elites, what elites fly (drones, boulders), hostile
//     shots and beams, rail mines, rocks / asteroids, backdrop scenery, and
//     nothing at all in the tutorial.
//
//   Catching the magnet with a shield up tops the shield up by
//   ShieldBonusSeconds (collisionDetection.ExtendShield, done by the caller);
//   a shield caught while the pull runs adds ExtendSeconds to the pull (cap
//   MaxSeconds). Death ends the pull.
//
// Per frame it walks ClearTarget's live registry once: nothing allocates.
public class MagnetPull : MonoBehaviour
{
    public const string ObjectName = "magnetAtom";
    public const string Word = "MAGNET";
    public const string ShieldedWord = "MAGNET +2s SHIELD";

    public const float ShieldBonusSeconds = 2f;
    public const string ShieldExtendedWord = "SHIELD + MAGNET +2s";
    public const float PullSeconds = 6f;                          // was 1.4 s
    public const float ExtendSeconds = 2f;                        // a shield caught mid-pull adds this
    public const float MaxSeconds = 10f;                          // the pull never runs longer than this in total
    public const float DustRampSeconds = 1.4f;
    public const float DustSpeedStart = 8f, DustSpeedEnd = 24f;   // u/s, ramping over DustRampSeconds
    public const float StopDistance = .3f;                        // a pulled enemy hands over to the shield's collision at this range

    public static float DustSpeed(float elapsed)
    {
        return Mathf.Lerp(DustSpeedStart, DustSpeedEnd, Mathf.Clamp01(elapsed / DustRampSeconds));
    }

    public static bool IsDust(GameObject go) { return PickupArt.IsStarDust(go); }

    // What the magnet sees in `t` (MagnetCombo.Classify over its tags and parts).
    public static MagnetCombo.Kind KindOf(ClearTarget t)
    {
        var go = t.gameObject;
        bool pickup = go.CompareTag("pickUp");
        bool enemy = !pickup && go.CompareTag("Enimey");
        bool elite = enemy && go.TryGetComponent(out EliteShip _);
        bool boss = enemy && !elite && (go.TryGetComponent(out BossTarget _) || go.TryGetComponent(out BossActor _) ||
                                        go.TryGetComponent(out IShipAttackTarget _));
        return MagnetCombo.Classify(t.Scenery, pickup, pickup && IsDust(go), !pickup && go.CompareTag("Astr"), enemy,
                                    boss, elite, enemy && t.Mother != null,
                                    enemy && (t.IsShotHitbox || RunScore.IsHostileShot(go)),
                                    enemy && go.TryGetComponent(out RailMineMount _));
    }

    // The blue-atom shield, as the combo reads it.
    static bool ShieldUp => collisionDetection.atomCheck && !buttonClicks.playerDied;
    static bool Tutorial => startMenu.youAreInTutorial;

    // ---- state ----
    public static MagnetPull Active { get; private set; }
    public static int Pulls { get; private set; }               // atoms collected this session (tests)
    // The enemy pull is on (magnet running AND shield up), as of the last step.
    public bool Shielded { get; private set; }
    public float ComboSeconds { get; private set; }             // how long the enemy pull has been on (its ramp)
    public float Elapsed { get; private set; }
    public float Duration { get; private set; } = PullSeconds;
    public bool Running => Elapsed < Duration;
    public float Remaining => Mathf.Max(0f, Duration - Elapsed);
    public int DraggedLast { get; private set; }
    public int DustLast { get; private set; }
    public int AtomsLast { get; private set; }
    public int EnemiesLocked => enemyCount;

    const int MaxGoodies = 96, MaxEnemies = 96;
    readonly Behaviour[] goodies = new Behaviour[MaxGoodies];
    readonly Behaviour[] enemies = new Behaviour[MaxEnemies];
    int goodyCount, enemyCount;

    // Starts (or re-arms) the pull on `ship`. Whether enemies come too is
    // read live each frame (MagnetCombo), not decided here.
    public static MagnetPull Begin(GameObject ship)
    {
        if (ship == null) return null;
        var pull = ship.GetComponent<MagnetPull>();
        if (pull == null) pull = ship.AddComponent<MagnetPull>();
        pull.Elapsed = 0f;
        pull.Duration = PullSeconds;
        Active = pull;
        Pulls++;
        return pull;
    }

    // A shield caught while the pull runs: +ExtendSeconds (capped at
    // MaxSeconds in total). False if no pull is running (nothing to extend).
    // (The enemy pull itself needs nothing from here: the shield being up is
    // enough, MagnetCombo.)
    public static bool OnShieldCaught()
    {
        var pull = Active;
        if (pull == null || !pull.isActiveAndEnabled || !pull.Running) return false;
        pull.Duration = Mathf.Min(pull.Duration + ExtendSeconds, MaxSeconds);
        return true;
    }

    // Ends the pull at once (death, world change).
    public void End()
    {
        Elapsed = Duration;
        Shielded = false;
        ComboSeconds = 0f;
        ReleaseAll();
    }

    void Update()
    {
        if (buttonClicks.playerDied) { End(); return; }
        if (!Running) return;
        Step(Time.deltaTime);
    }

    // One step of the pull (public so a headless test can drive it).
    public void Step(float dt)
    {
        if (dt <= 0f || !Running) return;
        Vector3 at = transform.position;
        float speed = DustSpeed(Elapsed);
        Elapsed += dt;
        DraggedLast = DustLast = AtomsLast = 0;

        bool shield = ShieldUp, tutorial = Tutorial;
        bool combo = MagnetCombo.EnemyPullActive(true, shield, tutorial);
        if (combo) ComboSeconds += dt;
        else
        {
            ComboSeconds = 0f;
            if (enemyCount > 0) Release(enemies, ref enemyCount);   // either power ended: let them go now
        }
        Shielded = combo;

        Rect view = ShipTargets.View();
        float enemySpeed = MagnetCombo.EnemySpeed(ComboSeconds, view.height);
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (t == null || !t.isActiveAndEnabled) continue;
            var go = t.gameObject;
            Vector3 p = go.transform.position;
            if (!view.Contains(p)) continue;
            if (!combo && !go.CompareTag("pickUp")) continue;   // magnet alone: only goodies, skip the lookups
            var kind = KindOf(t);
            if (!MagnetCombo.Pulls(kind, true, shield, tutorial)) continue;
            if (kind == MagnetCombo.Kind.Enemy)
            {
                Vector2 d = at - p;
                if (d.sqrMagnitude <= StopDistance * StopDistance) continue;
                Lock(go, enemies, ref enemyCount);
                go.transform.position = Vector3.MoveTowards(p, at, enemySpeed * dt);
                DraggedLast++;
                continue;
            }
            Lock(go, goodies, ref goodyCount);
            go.transform.position = Vector3.MoveTowards(p, at, speed * dt);
            if (kind == MagnetCombo.Kind.Dust) DustLast++; else AtomsLast++;
        }

        if (!Running) { Shielded = false; ComboSeconds = 0f; ReleaseAll(); }
    }

    static void Lock(GameObject go, Behaviour[] into, ref int count)
    {
        if (count >= into.Length) count = Compact(into, count);   // collected / destroyed bodies free their slots
        if (count < into.Length) count = HazardMovers.Disable(go, into, count);
    }

    static int Compact(Behaviour[] list, int count)
    {
        int n = 0;
        for (int i = 0; i < count; i++)
        {
            if (list[i] != null) list[n++] = list[i];
        }
        for (int i = n; i < count; i++) list[i] = null;
        return n;
    }

    static void Release(Behaviour[] list, ref int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (list[i] != null) list[i].enabled = true;
            list[i] = null;
        }
        count = 0;
    }

    void ReleaseAll()
    {
        Release(goodies, ref goodyCount);
        Release(enemies, ref enemyCount);
    }

    void OnDisable() { ReleaseAll(); }
    void OnDestroy() { ReleaseAll(); if (Active == this) Active = null; }
}
