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
//   RANGE: everything inside MagnetCombo.PullRect (90% of the visible rect's
//   area, from the live camera), for the goodies and the enemies alike.
//
//   SYNC: catching the magnet with the shield up, or the shield while the
//   magnet runs, restarts BOTH to MagnetCombo.SyncSeconds (collisionDetection
//   calls Sync); from then on the pull ends the frame the shield does, so the
//   two end together. Every further catch of either restarts both again
//   (chaining, by design without limit). Death ends the pull.
//
// Per frame it walks ClearTarget's live registry once: nothing allocates.
public class MagnetPull : MonoBehaviour
{
    public const string ObjectName = "magnetAtom";
    public const string Word = "MAGNET";

    public const float PullSeconds = 6f;                          // was 1.4 s
    public const float DustRampSeconds = 1.4f;
    public const float DustSpeedStart = 8f, DustSpeedEnd = 24f;   // u/s, ramping over DustRampSeconds
    public const float SyncSlack = .05f;                          // s: a synced pull follows the shield's clock within this
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
    // Restarted together with the shield (Sync): the pull ends with the shield.
    public bool Synced { get; private set; }
    // Catches in the current unbroken combo (1 = it just started; MagnetCombo.NextChain).
    public static int Chain { get; private set; }
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
        pull.Synced = false;
        Active = pull;
        Pulls++;
        return pull;
    }

    // Is a magnet pull running right now?
    public static bool MagnetRunning
    {
        get { var p = Active; return p != null && p.isActiveAndEnabled && p.Running; }
    }

    // Magnet running and the blue-atom shield up (the timers; the enemy pull
    // also stays off in the tutorial).
    public static bool ComboOn => MagnetRunning && ShieldUp;

    // Restarts the running pull to `seconds` in step with the shield (the
    // caller sets the shield's clock to the same) and counts the chain.
    // `comboBefore`: both were already up before this catch. False if no
    // pull is running.
    public static bool Sync(float seconds, bool comboBefore)
    {
        var pull = Active;
        if (pull == null || !pull.isActiveAndEnabled || !pull.Running) return false;
        pull.Elapsed = 0f;
        pull.Duration = seconds;
        pull.Synced = true;
        Chain = MagnetCombo.NextChain(Chain, comboBefore);
        return true;
    }

    // Ends the pull at once (death, world change).
    public void End()
    {
        Elapsed = Duration;
        Shielded = false;
        Synced = false;
        Chain = 0;
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
        if (Synced)
        {
            // restarted with the shield: never outlasts it, ends the frame it does
            if (!shield) { Elapsed = Duration; Shielded = false; Synced = false; Chain = 0; ComboSeconds = 0f; ReleaseAll(); return; }
            // the two clocks run the same world time: the pull reads the
            // shield's, so they cannot drift apart by a frame. (Only a shield
            // topped up well past it -- a premium Aegis shield -- is left to
            // run on alone after the pull.)
            float left = Mathf.Max(0f, collisionDetection.invTimer);
            if (left <= Remaining + SyncSlack) Elapsed = Duration - left;
            if (!Running) { Shielded = false; Synced = false; Chain = 0; ComboSeconds = 0f; ReleaseAll(); return; }
        }
        bool combo = MagnetCombo.EnemyPullActive(true, shield, tutorial);
        if (combo) ComboSeconds += dt;
        else
        {
            ComboSeconds = 0f;
            if (enemyCount > 0) Release(enemies, ref enemyCount);   // either power ended: let them go now
            if (!shield) Chain = 0;
        }
        Shielded = combo;

        Rect view = ShipTargets.View();
        float enemySpeed = MagnetCombo.EnemySpeed(ComboSeconds, view.height);
        Rect field = MagnetCombo.PullRect(view);
        // LIFELINE (magnet + heal, AtomFusions): the goodies come from the
        // whole view and just above it; the enemy pull keeps its own field
        bool lifeline = AtomFusions.LifelineActive;
        Rect goodyField = lifeline ? AtomFusions.LifelineField(view) : field;
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (t == null || !t.isActiveAndEnabled) continue;
            var go = t.gameObject;
            Vector3 p = go.transform.position;
            bool inField = field.Contains(p);
            if (!inField && !(lifeline && goodyField.Contains(p))) continue;
            if (!combo && !go.CompareTag("pickUp")) continue;   // magnet alone: only goodies, skip the lookups
            var kind = KindOf(t);
            if (!MagnetCombo.Pulls(kind, true, shield, tutorial)) continue;
            if (kind == MagnetCombo.Kind.Enemy)
            {
                if (!inField) continue;
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

        if (!Running) { Shielded = false; Synced = false; Chain = 0; ComboSeconds = 0f; ReleaseAll(); }
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
