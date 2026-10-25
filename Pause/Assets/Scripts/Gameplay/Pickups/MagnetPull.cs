using UnityEngine;

// The Magnetic Atom (prefab "magnetAtom", PickupKind.Magnet). Collecting it
// (collisionDetection) calls Begin; this component, added to the ship, then
// runs the pull for PullSeconds on the game clock:
//
//   * ALWAYS: every star dust on screen (smStar1 / LargeStar1) is hauled
//     into the hull, faster the longer the pull has run. Reaching the hull
//     collects it the ordinary way (collisionDetection), so it pays, fills
//     the secret meter and counts for achievements exactly as if flown into.
//
//   * ONLY if the shield was up when the atom was caught: the hazards on
//     screen are dragged in too, and the shield gains ShieldBonusSeconds
//     (collisionDetection.ExtendShield, done by the caller). The enemy rule
//     (Rule) is deliberately conservative:
//       - plain enemies (tag Enimey)            -> Drag: pulled to the hull at
//         EnemySpeed; the shield destroys what touches it, as ever.
//       - unarmoured elites that are in play    -> Nudge: their velocity is
//         pushed toward the hull at ElitePull x ShieldShockwave.EliteResist
//         (the same share the shield's shockwave and the Time Bubble push
//         them with); their own steering still works, so they are tugged,
//         never snatched.
//       - bosses, armoured elites, enemy shots, rail-mounted mines, asteroids
//         and backdrop scenery                  -> None: left alone.
//     Without the shield only the dust moves; no enemy is touched.
//
//   The pull lasts PullSeconds (6 s). A shield caught while it runs adds
//   ExtendSeconds (cap MaxSeconds) and turns the enemy pull on; the enemy
//   pull ends when the shield does. Death ends the pull.
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
    public const float EnemySpeed = 5f;                           // u/s
    public const float ElitePull = 12f;                           // u/s^2 toward the hull, before EliteResist
    public const float StopDistance = .3f;                        // a dragged enemy hands over to the collision at this range

    public enum Effect { None, Drag, Nudge }

    // What the pull does to a hazard. Pure, so the rule is testable.
    public static Effect Rule(bool shielded, bool isEnemy, bool isBoss, bool isHostileShot, bool railMounted,
                              bool isElite, bool eliteInPlay, bool eliteArmoured)
    {
        if (!shielded || !isEnemy || isBoss || isHostileShot || railMounted) return Effect.None;
        if (isElite) return eliteInPlay && !eliteArmoured ? Effect.Nudge : Effect.None;
        return Effect.Drag;
    }

    public static float DustSpeed(float elapsed)
    {
        return Mathf.Lerp(DustSpeedStart, DustSpeedEnd, Mathf.Clamp01(elapsed / DustRampSeconds));
    }

    public static bool IsDust(GameObject go) { return PickupArt.IsStarDust(go); }

    // ---- state ----
    public static MagnetPull Active { get; private set; }
    public static int Pulls { get; private set; }               // atoms collected this session (tests)
    public bool Shielded { get; private set; }
    public float Elapsed { get; private set; }
    public float Duration { get; private set; } = PullSeconds;
    public bool Running => Elapsed < Duration;
    public float Remaining => Mathf.Max(0f, Duration - Elapsed);
    public int DraggedLast { get; private set; }
    public int NudgedLast { get; private set; }
    public int DustLast { get; private set; }

    const int MaxLocked = 96;
    readonly Behaviour[] locked = new Behaviour[MaxLocked];
    int lockedCount;
    readonly System.Collections.Generic.List<ClearTarget> scratch = new System.Collections.Generic.List<ClearTarget>(64);

    // Starts (or re-arms) the pull on `ship`. `shielded` = the shield was up.
    public static MagnetPull Begin(GameObject ship, bool shielded)
    {
        if (ship == null) return null;
        var pull = ship.GetComponent<MagnetPull>();
        if (pull == null) pull = ship.AddComponent<MagnetPull>();
        pull.Elapsed = 0f;
        pull.Duration = PullSeconds;
        pull.Shielded = shielded;
        Active = pull;
        Pulls++;
        return pull;
    }

    // A shield caught while the pull runs: +ExtendSeconds (capped at
    // MaxSeconds in total) and the enemy pull switches on for as long as the
    // shield stays up. False if no pull is running (nothing to extend).
    public static bool OnShieldCaught()
    {
        var pull = Active;
        if (pull == null || !pull.isActiveAndEnabled || !pull.Running) return false;
        pull.Duration = Mathf.Min(pull.Duration + ExtendSeconds, MaxSeconds);
        pull.Shielded = true;
        return true;
    }

    // Ends the pull at once (death, world change).
    public void End()
    {
        Elapsed = Duration;
        Release();
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
        DraggedLast = NudgedLast = DustLast = 0;

        Rect view = ShipTargets.View();
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (t == null || !t.isActiveAndEnabled) continue;
            var go = t.gameObject;
            if (ShipTargets.IsPickup(t))
            {
                if (!IsDust(go) || !view.Contains(go.transform.position)) continue;
                Lock(go);
                go.transform.position = Vector3.MoveTowards(go.transform.position, at, speed * dt);
                DustLast++;
            }
        }

        // enemies are pulled only while the shield and the magnet are both up
        if (Shielded && !collisionDetection.atomCheck) Shielded = false;
        if (Shielded) PullEnemies(at, dt);
        if (!Running) Release();
    }

    void PullEnemies(Vector3 at, float dt)
    {
        ShipTargets.Collect(scratch);
        for (int i = 0; i < scratch.Count; i++)
        {
            var t = scratch[i];
            if (t == null || !t.isActiveAndEnabled || t.Scenery) continue;
            var go = t.gameObject;
            EliteShip elite;
            bool isElite = go.TryGetComponent(out elite);
            var effect = Rule(true, go.CompareTag("Enimey"),
                              go.TryGetComponent(out BossTarget _) || go.TryGetComponent(out BossActor _) || t.GetComponent<IShipAttackTarget>() != null,
                              RunScore.IsHostileShot(go), go.TryGetComponent(out RailMineMount _),
                              isElite, isElite && elite.InPlay, isElite && elite.Def != null && elite.Def.armored);
            if (effect == Effect.None) continue;
            Vector2 d = at - go.transform.position;
            float dist = d.magnitude;
            if (dist <= StopDistance) continue;
            if (effect == Effect.Nudge)
            {
                elite.Velocity += d / dist * (ElitePull * dt * ShieldShockwave.EliteResist);
                NudgedLast++;
                continue;
            }
            Lock(go);
            go.transform.position = Vector3.MoveTowards(go.transform.position, at, EnemySpeed * dt);
            DraggedLast++;
        }
    }

    void Lock(GameObject go)
    {
        if (lockedCount < MaxLocked) lockedCount = HazardMovers.Disable(go, locked, lockedCount);
    }

    void Release()
    {
        for (int i = 0; i < lockedCount; i++)
        {
            if (locked[i] != null) locked[i].enabled = true;
            locked[i] = null;
        }
        lockedCount = 0;
    }

    void OnDisable() { Release(); }
    void OnDestroy() { Release(); if (Active == this) Active = null; }
}
