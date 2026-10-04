using System.Collections.Generic;
using UnityEngine;

// Something a ship's attack can hit without simply destroying it -- the
// end-of-level boss. Put it on (or beside) a component of the hit object
// and register that object as a hazard target: tag it "Enimey" and call
// ClearTarget.Ensure(go) (optionally ClearTarget.SetRadius for its size), so
// the directional attacks and the cinematic volley both find it.
//
// Every attack routes its hits through ShipAttackHits.Hit, which calls
// TakeShipAttack instead of destroying the object when it finds one.
//
// `weight` is the share of one full ultimate hit this contact is worth: a
// single homing shot of the screen-clear volley, a rail slug or a fireball
// burst are 1; a gatling shell, a beam tick or a disc pass are a fraction.
// One firing of any attack never deals more than 1 in total to a single
// boss (each attack carries a budget), so rapid-fire weapons can't end a
// fight on their own. A boss using "ultimate hits shorten the fight" can
// take seconds * weight off the clock, or bank weights into whole hits.
public interface IShipAttackTarget
{
    void TakeShipAttack(int ship, float weight, Vector3 at);
}

public static class ShipAttackHits
{
    // Counters for tests and previews.
    public static int Kills;
    public static int AttackTargetHits;
    public static float AttackTargetWeight;

    // Destroys a hazard the way every player weapon does (explosion in the
    // weapon's colour, sound, star dust, codex, secret-meter credit), or hands
    // the hit to an IShipAttackTarget. Returns true when the target was
    // destroyed. `budget` is what is left of this firing's boss allowance.
    public static bool Hit(GameObject target, int ship, float weight, ref float budget)
    {
        if (target == null) return false;
        var special = target.GetComponent<IShipAttackTarget>();
        if (special != null)
        {
            float w = Mathf.Min(weight, budget);
            if (w <= 0f) return false;
            budget -= w;
            AttackTargetHits++;
            AttackTargetWeight += w;
            special.TakeShipAttack(ship, w, target.transform.position);
            return false;
        }
        Kill(target, ship);
        return true;
    }

    // Already destroyed by a weapon this frame (Destroy() is deferred, so
    // the object is still there): its registry entry was released.
    public static bool AlreadyHit(GameObject target)
    {
        if (target == null) return true;
        var t = target.GetComponent<ClearTarget>();
        return t != null && !t.enabled;
    }

    public static bool Hit(GameObject target, int ship)
    {
        float budget = 1f;
        return Hit(target, ship, 1f, ref budget);
    }

    static void Kill(GameObject target, int ship)
    {
        Kills++;
        TargetExplosion.Spawn(target, ship);
        collisionDetection.PlayExplosion();
        collisionDetection.AwardDestroyedTarget(target);
        ClearTarget.Release(target);
        if (Application.isPlaying) Object.Destroy(target);
        else Object.DestroyImmediate(target);
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

    public static bool IsPickup(ClearTarget t)
    {
        return t != null && t.isActiveAndEnabled && t.CompareTag("pickUp");
    }

    // The visible play area in world units (plus `margin` all round). No
    // camera: an unbounded rect, so everything counts.
    public static Rect View(float margin = 0f)
    {
        var cam = Camera.main;
        if (cam == null || !cam.orthographic)
            return new Rect(-1e5f, -1e5f, 2e5f, 2e5f);
        float h = cam.orthographicSize, w = h * cam.aspect;
        Vector3 c = cam.transform.position;
        return new Rect(c.x - w - margin, c.y - h - margin, (w + margin) * 2f, (h + margin) * 2f);
    }

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

    public static int CountOnScreen(bool enemiesOnly)
    {
        Rect view = View();
        var live = ClearTarget.Live;
        int n = 0;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (!IsHazard(t)) continue;
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
            if (!IsHazard(t)) continue;
            if (enemiesOnly && !t.CompareTag("Enimey")) continue;
            Vector2 d = t.transform.position - at;
            float r = radius + t.Radius;
            if (d.sqrMagnitude <= r * r) n++;
        }
        return n;
    }

    // Nearest hazard to `from` (inside the view), skipping anything in
    // `exclude`; `maxDistance` <= 0 means any distance.
    public static ClearTarget Nearest(Vector3 from, float maxDistance, List<GameObject> exclude)
    {
        Rect view = View();
        view.yMax += .4f;
        var live = ClearTarget.Live;
        ClearTarget best = null;
        float bestSq = maxDistance > 0f ? maxDistance * maxDistance : float.MaxValue;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (!IsHazard(t)) continue;
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
            if (!IsHazard(t)) continue;
            Vector3 p = t.transform.position;
            float r = t.Radius + hullRadius;
            float dx = p.x - ship.x, dy = p.y - ship.y;
            if (dx * dx + dy * dy <= (r + .12f) * (r + .12f)) return t.gameObject;
            if (Mathf.Abs(dx) < r * .9f && dy > 0f && dy <= r + fall * lookahead) return t.gameObject;
        }
        return null;
    }
}
