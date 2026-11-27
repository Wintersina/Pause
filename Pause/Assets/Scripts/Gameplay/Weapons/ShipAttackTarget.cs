using System.Collections.Generic;
using UnityEngine;

// Something a ship's attack can hit without simply destroying it -- the
// end-of-level boss. Put it on (or beside) a component of the hit object
// and register that object as a hazard target: tag it "Enimey" and call
// ClearTarget.Ensure(go) (optionally ClearTarget.SetRadius for its size), so
// the directional attacks and the cinematic volley both find it.
//
// (Ordinary enemies and hazards are not special targets: enemies carry EnemyHearts and take one heart per hit; hazards die in one hit,
// see Toughness.)
// Every attack routes its hits through ShipAttackHits.Hit, which calls
// TakeShipAttack instead of destroying the object when it finds one.
//
// `weight` is the share of one full ultimate hit this contact is worth: a
// single homing shot of the screen-clear volley, a rail slug or a fireball
// burst are 1; a gatling shell, a beam tick or a disc pass are a fraction.
// Other targets (elites) get one budget per firing, shared by its contacts.
// The BOSS has no per-firing, per-barrage or per-ram cap (BossTarget is
// IShipAttackTargetUncapped): every contact counts, and the only limit is the
// boss's short hit window after a lost hit point (BossEncounter.TakePlayerHit,
// BossConfig.HitInvulnerableSeconds). Weights bank into whole hits.
public interface IShipAttackTarget
{
    void TakeShipAttack(int ship, float weight, Vector3 at);
}

// A target that takes every hit in full, outside the firing's allowance: the boss (which limits itself with
// its own hit window) and ordinary small things that happen to need more than one hit (Space's field rocks,
// FieldAsteroid, and its backdrop rocks), so one volley can break several of them.
public interface IShipAttackTargetUncapped : IShipAttackTarget { }

// A boss target reports whether a hit counted during an active fight.
public interface IBossAttackTarget : IShipAttackTargetUncapped
{
    // True when the hit counted or there is nothing left to hit.
    bool TryStrike(int ship, float weight, Vector3 at);
}

// Marks a hit body as scenery (the drifting backdrop rocks): it takes hits from the weapons that sweep an area, but the
// weapons that PICK a mark (the Gold Warden's sentries) never choose it. ClearTarget.IsScenery reads this as well as
// the Scenery flag, so a pooled / reset flag can never turn a backdrop body into a target.
public interface ISceneryTarget { }

// What a player hit comes from. An ULTIMATE hit (the ship's charged main attack and its red-atom free shot: everything the ShipAttackRunner
// fires -- ion laser, frost nova, sentries, nuke, rail, spark, eye, heat wave, star, mines, abduct, gale, blowtorch, disc) kills an ordinary
// enemy outright whatever hearts it has left. Drone hits carry actual quarter-heart damage.
// Other normal hits (secret powers, fusions) remove one ordinary-enemy heart.
public enum HitKind { Normal, Ultimate, Drone, Power }   // Power: a secret power's strike (as Normal, but the boss takes it like an ultimate)

public static class ShipAttackHits
{
    // Share of its damage an ULTIMATE (and a secret power's strike) deals to the end-of-level BOSS. 0.75: scaled back a quarter
    // (user, 2026-11-27: "a little"). Bosses only: ordinary enemies, elites, drone fire and rams are untouched, and the
    // boss's own hit rules stay as they were (no per-firing cap; weights bank into hearts). The attack still reports its
    // nominal weight; the real boss (BossTarget.Strike) takes BossScale of it.
    public const float UltimateBossDamageScale = 0.75f;

    // The share of a hit's weight the boss takes right now: UltimateBossDamageScale while an ultimate / power hit is being
    // handed over (Hit, HitBoss), else 1 (rams, drones, the tests' direct strikes).
    public static float BossScale = 1f;

    public static float ScaleFor(HitKind kind) { return kind == HitKind.Ultimate || kind == HitKind.Power ? UltimateBossDamageScale : 1f; }

    // Counters for tests and previews.
    public static int Kills;
    public static int AttackTargetHits;
    public static float AttackTargetWeight;

    // Where the blow came from, when the caller knows (a floatie bullet's position): "away from the hit" for a heart-pop's chips.
    // Null: the player's ship (Toughness.Origin).
    public static Vector3? HitFrom;

    // Destroys a hazard the way every player weapon does (explosion in the
    // weapon's colour, sound, star dust, codex, secret-meter credit), or hands
    // the hit to an IShipAttackTarget. Returns true when the target was
    // destroyed. `budget` is what is left of this firing's allowance for capped
    // targets (elites); uncapped ones (the boss, backdrop rocks) ignore it.
    public static bool Hit(GameObject target, int ship, float weight, ref float budget, HitKind kind = HitKind.Normal)
    {
        if (target == null) return false;
        IShipAttackTarget special;
        if (target.TryGetComponent(out special))
        {
            if (kind == HitKind.Drone && special is EliteShip elite)
            {
                AttackTargetHits++;
                AttackTargetWeight += weight;
                elite.TakeDroneAttack(ship, weight, HitFrom ?? target.transform.position);
                return false;
            }
            if (special is IShipAttackTargetUncapped)
            {
                AttackTargetHits++;
                AttackTargetWeight += weight;
                float was = BossScale;
                BossScale = ScaleFor(kind);
                try { special.TakeShipAttack(ship, weight, target.transform.position); }
                finally { BossScale = was; }
                return false;
            }
            float w = Mathf.Min(weight, budget);
            if (w <= 0f) return false;
            budget -= w;
            AttackTargetHits++;
            AttackTargetWeight += w;
            special.TakeShipAttack(ship, w, target.transform.position);
            return false;
        }
        // A tough enemy (2 hearts) takes ONE heart per hit, whatever the hit's weight; only the hit that takes the last one falls through
        // to the ordinary kill. Hazards and plain bodies (and the tests' bare targets) die in one hit.
        // An ultimate hit skips the hearts: it kills the enemy outright (paid once, in Kill).
        if (kind != HitKind.Ultimate && Toughness.Absorb(target, kind == HitKind.Drone ? weight : 1f)) return false;
        DroneScoring.InProgress = kind == HitKind.Drone;   // (a squad's kill pays less: DroneScoring)
        try { Kill(target, ship); }
        finally { DroneScoring.InProgress = false; }
        return true;
    }

    // One heavy hit on a boss (the nuke). Counted like any uncapped contact once it lands. False: the boss's hit
    // window blocked it, so the caller keeps it pending and calls again.
    public static bool HitBoss(IBossAttackTarget boss, int ship, float weight, Vector3 at)
    {
        float was = BossScale;
        BossScale = UltimateBossDamageScale;
        bool landed;
        try { landed = boss.TryStrike(ship, weight, at); }
        finally { BossScale = was; }
        if (!landed) return false;
        AttackTargetHits++;
        AttackTargetWeight += weight;
        return true;
    }

    // Already destroyed by a weapon this frame (Destroy() is deferred, so
    // the object is still there): its registry entry was released.
    public static bool AlreadyHit(GameObject target)
    {
        if (target == null) return true;
        ClearTarget t;
        return target.TryGetComponent(out t) && !t.enabled;
    }

    public static bool Hit(GameObject target, int ship, HitKind kind = HitKind.Normal)
    {
        float budget = 1f;
        return Hit(target, ship, 1f, ref budget, kind);
    }

    static void Kill(GameObject target, int ship)
    {
        Kills++;
        TargetExplosion.Spawn(target, ship);
        EnemyDeathAudio.Play(target);
        collisionDetection.AwardDestroyedTarget(target);
        ClearTarget.Release(target);
        Pooled.Remove(target);   // (a pooled enemy goes back to its pool)
    }
}

// Read-only queries over the ClearTarget registry: the hazards the ship's
// weapons and powers can see. Callers pass their own preallocated list, so
// nothing here allocates.
public static class ShipTargets
{
    public static bool IsHazard(ClearTarget t)
    {
        return t != null && t.isActiveAndEnabled && ClearTarget.IsHazard(t.gameObject);
    }

    // A hazard that is a real body in the lane: not scenery (ClearTarget.Scenery).
    public static bool IsThreat(ClearTarget t)
    {
        return IsHazard(t) && !t.IsScenery;
    }

    public static bool IsPickup(ClearTarget t)
    {
        return t != null && t.isActiveAndEnabled && t.CompareTag("pickUp");
    }

    // The visible play area in world units (plus `margin` all round). No
    // camera: an unbounded rect, so everything counts.
    public static Rect View(float margin = 0f)
    {
        // the camera's centre and half extents are read once per frame (every shot asks for the view every tick:
        // a native camera read each). Edit mode (no real frames) always reads fresh unless a test switches the cache on.
        if (!(viewStamp == Time.frameCount && (Application.isPlaying || ForceViewCache)))
        {
            var cam = CameraFit.Main;
            if (cam == null || !cam.orthographic) viewOpen = true;
            else
            {
                viewOpen = false;
                float h = cam.orthographicSize;
                Vector3 c = cam.transform.position;
                viewHalfH = h; viewHalfW = h * cam.aspect; viewX = c.x; viewY = c.y;
            }
            viewStamp = Time.frameCount;
            ViewReads++;
        }
        if (viewOpen) return new Rect(-1e5f, -1e5f, 2e5f, 2e5f);
        float w = viewHalfW, hh = viewHalfH;
        return new Rect(viewX - w - margin, viewY - hh - margin, (w + margin) * 2f, (hh + margin) * 2f);
    }

    static int viewStamp = -1;
    static bool viewOpen;
    static float viewX, viewY, viewHalfW, viewHalfH;
    public static bool ForceViewCache;   // tests: cache by frame in edit mode too
    public static int ViewReads;         // tests: camera reads so far
    public static void ResetViewCache() { viewStamp = -1; }

    // Hazards (enemies + asteroids) inside the view, plus `topMargin` above
    // it so something just coming in can be shot.
    public static int Collect(List<ClearTarget> into, float topMargin = .4f)
    {
        into.Clear();
        Rect view = View();
        view.yMax += topMargin;
        var live = ClearTarget.Live;
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var t = live[i];
            if (t == null) { live.RemoveAt(i); continue; }
            if (!IsHazard(t)) continue;
            if (view.Contains(t.transform.position)) into.Add(t);
        }
        return into.Count;
    }

    // ---- homing target selection --------------------------------------
    // Every homing player shot (ultimate volleys, seeker eyes, premium
    // missiles, ricochet, re-targeting in flight) picks its marks here:
    //   * only inside the visible camera bounds (never something that has
    //     scrolled off the bottom or sits above the top), and
    //   * while a boss is alive, the boss and nothing else.
    public static bool IsBossTarget(ClearTarget t)
    {
        return t != null && t.TryGetComponent<IBossAttackTarget>(out _);
    }

    public static bool BossAlive()
    {
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (IsHazard(t) && IsBossTarget(t)) return true;
        }
        return false;
    }

    // Visible area, plus `topMargin` above so something just coming in can be shot.
    public static bool InHomingView(Vector3 p, float topMargin = .4f)
    {
        Rect view = View();
        view.yMax += topMargin;
        return view.Contains(p);
    }

    // Can a homing shot lock on to (or keep chasing) this right now?
    public static bool CanHome(ClearTarget t)
    {
        if (!IsHazard(t) || !InHomingView(t.transform.position)) return false;
        return !BossAlive() || IsBossTarget(t);
    }

    public static int CollectHoming(List<ClearTarget> into, float topMargin = .4f)
    {
        Collect(into, topMargin);
        if (into.Count > 0 && BossAlive())
            for (int i = into.Count - 1; i >= 0; i--)
                if (!IsBossTarget(into[i])) into.RemoveAt(i);
        return into.Count;
    }

    public static ClearTarget NearestHoming(Vector3 from, float maxDistance, List<GameObject> exclude, bool skipScenery = false)
    {
        if (!BossAlive()) return Nearest(from, maxDistance, exclude, skipScenery);
        ClearTarget best = null;
        float bestSq = maxDistance > 0f ? maxDistance * maxDistance : float.MaxValue;
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (!IsHazard(t) || !IsBossTarget(t) || (skipScenery && t.IsScenery) || !InHomingView(t.transform.position)) continue;
            if (exclude != null && exclude.Contains(t.gameObject)) continue;
            float sq = ((Vector2)(t.transform.position - from)).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = t; }
        }
        return best;
    }

    public static int CountOnScreen(bool enemiesOnly)
    {
        Rect view = View();
        var live = ClearTarget.Live;
        int n = 0;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (!IsThreat(t)) continue;
            if (enemiesOnly && !t.CompareTag("Enimey")) continue;
            if (view.Contains(t.transform.position)) n++;
        }
        return n;
    }

    public static int CountPickupsOnScreen()
    {
        Rect view = View();
        var live = ClearTarget.Live;
        int n = 0;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (IsPickup(t) && view.Contains(t.transform.position)) n++;
        }
        return n;
    }

    public static int CountNear(Vector3 at, float radius, bool enemiesOnly = false)
    {
        var live = ClearTarget.Live;
        int n = 0;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (!IsThreat(t)) continue;
            if (enemiesOnly && !t.CompareTag("Enimey")) continue;
            Vector2 d = t.transform.position - at;
            float r = radius + t.Radius;
            if (d.sqrMagnitude <= r * r) n++;
        }
        return n;
    }

    // Nearest hazard to `from` (inside the view), skipping anything in
    // `exclude`; `maxDistance` <= 0 means any distance.
    public static ClearTarget Nearest(Vector3 from, float maxDistance, List<GameObject> exclude, bool skipScenery = false)
    {
        Rect view = View();
        view.yMax += .4f;
        var live = ClearTarget.Live;
        ClearTarget best = null;
        float bestSq = maxDistance > 0f ? maxDistance * maxDistance : float.MaxValue;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (!IsHazard(t) || (skipScenery && t.IsScenery)) continue;
            Vector3 p = t.transform.position;
            if (!view.Contains(p)) continue;
            if (exclude != null && exclude.Contains(t.gameObject)) continue;
            float sq = ((Vector2)(p - from)).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = t; }
        }
        return best;
    }

    // Is anything about to touch a hull of `hullRadius` at `ship` within
    // `lookahead` seconds? Hazards close in at the world scroll speed (they
    // fall straight down the screen); anything already overlapping, or a
    // chaser right on top of the ship, counts too.
    public static GameObject Imminent(Vector3 ship, float hullRadius, float lookahead)
    {
        float fall = Mathf.Max(0f, moveBackGround.speed) * 30f;
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (!IsThreat(t)) continue;
            Vector3 p = t.transform.position;
            float r = t.Radius + hullRadius;
            float dx = p.x - ship.x, dy = p.y - ship.y;
            if (dx * dx + dy * dy <= (r + .12f) * (r + .12f)) return t.gameObject;
            if (Mathf.Abs(dx) < r * .9f && dy > 0f && dy <= r + fall * lookahead) return t.gameObject;
        }
        return null;
    }
}
