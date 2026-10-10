using System.Collections.Generic;
using UnityEngine;

// THE SHARED BODY OF THE THEMED AREA HAZARDS (plan phases 1c-1e; docs/world-attacks-design.md 0.1-0.2).
//
// A blast ring, a lane strike (and later a jet, a band, a lash) is a pooled
// component with one life:
//
//   Off --Arm--> Tell --Ignite--> Live --(its shape ends)--> After --> Off (back to its pool)
//
//   Tell   harmless. The footprint is drawn (AttackPreview, FR2) from the SAME AttackShape
//          the hit test uses; at least MinTellSeconds (.7 s, FR1) and the preview shows
//          >= AttackPreview.MinLead (.4 s) before Live. The caller's windup (EnemyBrain)
//          lasts as long, so Ignite() is its Release; the hazard also ignites itself
//          when its own tell has run out (an elite or a boss that never calls it).
//   Live   harmful: a trigger collider tagged "Enimey" (the way every hostile hurts:
//          collisionDetection's heart / shield / blink rules) built from the polygons of
//          the shape, refilled every live frame, and IHostileZone (burns the light and
//          heavy hostile shots crossing it, bar its own volley's). Friendly fire hurts
//          other hazards once per pulse (FR9), never the shooter.
//   After  harmless: the burst / fade-out.
//
// TIME. Everything advances through Step(dt), which AttackPools.StepAll calls on the
// world's running frames (EliteSystem.Step), so a frozen world freezes every hazard
// and its preview (FR8). Pooled (AttackPools), nothing allocates after the first take.
//
// HITBOX HYGIENE. Polygons of one hazard must not overlap (a PolygonCollider2D with
// several paths fills them even-odd). Shield or Cloak: the hit is absorbed and the
// pulse ends (EraseHitbox); a blink dash erases it only where the hull lands on it
// (BlinkStrike); the fatal hit destroys the hitbox object and the hazard ends.
public abstract class AttackHazard : MonoBehaviour, IHostileZone, IAttackStep, IAttackReleased
{
    public enum Phase { Off, Tell, Live, After }

    public const string HitboxName = "AttackHazardHit";
    public const float MinTellSeconds = .7f;   // FR1: an instant-hit hazard is told at least this long
    public const float BlinkHullReach = .33f;  // the hull's reach when a blink lands (as RailMineLaser)
    public const int MaxHits = 24;             // friendly-fire targets per pulse
    public static bool HurtsOtherEnemies = true;

    static readonly List<AttackHazard> registry = new List<AttackHazard>(16);
    public static IReadOnlyList<AttackHazard> All => registry;

    public Phase State { get; private set; }
    public bool Active => State != Phase.Off;
    public bool Live => State == Phase.Live;
    public float PhaseTime => t;                // seconds in the current phase
    public float Age => age;                    // seconds since it went live
    public float TellSeconds => tellTotal;
    public float TellLeft => State == Phase.Tell ? Mathf.Max(0f, tellTotal - t) : 0f;
    public GameObject Shooter => shooter;
    public GameObject Hitbox => hitbox;
    public PolygonCollider2D HitCollider => poly;
    public AttackShape Footprint => shape;      // the hit geometry AND the preview geometry
    public AttackPreview Preview => preview;
    public int HitsThisPulse => hitCount;
    public int World => world;
    // how much of the roster's shot budget this hazard counts while live (FR7)
    public abstract float ThreatWeight { get; }

    protected readonly AttackShape shape = new AttackShape();
    protected int world;
    protected GameObject shooter;
    protected int ownerId;
    protected float t, age, tellTotal;
    GameObject hitbox;
    PolygonCollider2D poly;
    AttackPreview preview;
    readonly int[] hits = new int[MaxHits];
    int hitCount;
    Vector2[][] buffers;   // [poly * 9 + length] -> path buffer, made once

    // ---- the subclass's part ----

    protected abstract void OnArmed();                  // build `shape` for the ignition footprint (+ guides) and the visuals
    protected abstract void OnTellStep(float dt);       // follow the owner, blink the glyph...
    protected abstract void OnIgnited();                // the hazard goes live
    protected abstract bool OnLiveStep(float dt);       // advance + rebuild `shape`; false when the live part is over
    protected abstract bool OnAfterStep(float dt);      // the burst; false when done
    protected abstract void OnEnded();                  // hide the visuals
    protected abstract void ReturnToPool();             // give this item back to its AttackPool
    protected virtual string Label => "attack";         // FriendlyFire "by" name
    protected virtual Color PreviewTint => HostileShotPalette.Body(EnemyBehaviours.SpaceShot);

    // ---- IHostileZone ----
    public bool ZoneLive => State == Phase.Live;
    public bool ZoneTouches(Vector2 p, float r) => State == Phase.Live && shape.Touches(p, r);
    public int ZoneOwner => ownerId;
    public float ZoneAge => age;

    // ---- construction (the subclass's Create calls this once) ----

    protected void BuildHitbox()
    {
        if (hitbox != null) return;
        hitbox = new GameObject(HitboxName);
        hitbox.tag = "Enimey";
        hitbox.transform.SetParent(transform, false);
        poly = hitbox.AddComponent<PolygonCollider2D>();
        poly.isTrigger = true;
        poly.enabled = false;
        hitbox.AddComponent<AttackHazardHitbox>().hazard = this;
        if (buffers == null) buffers = new Vector2[AttackShape.MaxPolys * 9][];
    }

    protected void Register()
    {
        BuildHitbox();
        HostileShots.Register(this);
        for (int i = registry.Count - 1; i >= 0; i--) if (registry[i] == null) registry.RemoveAt(i);   // (edit mode never runs OnDestroy)
        registry.Add(this);
    }

    void OnDestroy() { registry.Remove(this); HostileShots.Unregister(this); }

    // The pool took it back (AttackPool.Release / ReleaseAll): nothing of it stays, whatever Unity does with OnDisable.
    public void Released() { if (State != Phase.Off) Stop(); }

    void OnDisable()
    {
        // taken out of play (pool release, ClearAll, a scene change): nothing of it stays
        if (State == Phase.Off) return;
        Stop();
    }

    // ---- life ----

    // Starts the tell. `tellSeconds` is raised to MinTellSeconds. The subclass has set its data.
    protected void BeginTell(float tellSeconds, GameObject by, int worldIndex)
    {
        world = Mathf.Clamp(worldIndex, 0, ShotSkins.Worlds - 1);
        shooter = by;
        ownerId = by != null ? by.GetInstanceID() : 0;
        tellTotal = Mathf.Max(MinTellSeconds, tellSeconds);
        t = age = 0f;
        hitCount = 0;
        State = Phase.Tell;
        if (hitbox == null) BuildHitbox();   // the fatal hit destroyed it: a fresh one
        poly.enabled = false;
        gameObject.SetActive(true);
        shape.Clear();
        OnArmed();
        EndPreview();
        preview = AttackPreview.Show(shape, tellTotal, PreviewTint);
    }

    // The windup is over: the hazard goes live. (A no-op once it is.)
    public void Ignite()
    {
        if (State != Phase.Tell) return;
        State = Phase.Live;
        t = age = 0f;
        hitCount = 0;
        if (preview != null) { preview.GoLive(); preview = null; }
        OnIgnited();
        if (hitbox == null) { EndLive(); return; }
        SyncHitbox();
        poly.enabled = true;
    }

    // Gone at once (its shooter died in the tell, a world change).
    public void Cancel() { if (State != Phase.Off) Stop(); }

    // A shield absorbed it or a blink landed on it: the pulse is spent.
    public void Erase()
    {
        if (State != Phase.Live) return;
        EndLive();
    }

    void EndLive()
    {
        State = Phase.After;
        t = 0f;
        if (poly != null) poly.enabled = false;
    }

    void EndPreview() { if (preview != null) { preview.End(); preview = null; } }

    void Stop()
    {
        EndPreview();
        State = Phase.Off;
        if (poly != null) poly.enabled = false;
        OnEnded();
        shooter = null;
        ReturnToPool();
    }

    public void Step(float dt)
    {
        if (State == Phase.Off || dt <= 0f) return;
        t += dt;
        switch (State)
        {
            case Phase.Tell:
                if (preview != null) preview.Step(dt);
                OnTellStep(dt);
                if (t >= tellTotal) Ignite();
                break;
            case Phase.Live:
                age += dt;
                // the fatal hit destroyed the hitbox: the pulse is over
                if (hitbox == null) { EndLive(); break; }
                if (!OnLiveStep(dt)) { EndLive(); break; }
                SyncHitbox();
                Burn();
                break;
            case Phase.After:
                if (!OnAfterStep(dt)) Stop();
                break;
        }
    }

    // ---- the hit geometry as a collider ----

    // Copies the shape's polygons into the trigger collider (no allocation after a polygon length is first used).
    protected void SyncHitbox()
    {
        if (poly == null) return;
        int n = shape.PolyCount;
        if (poly.pathCount != n) poly.pathCount = n;
        for (int i = 0; i < n; i++)
        {
            int len = shape.PolyLength(i);
            int key = i * 9 + len;
            var buf = buffers[key];
            if (buf == null) buf = buffers[key] = new Vector2[len];
            for (int j = 0; j < len; j++) buf[j] = shape.PolyAt(i, j);
            poly.SetPath(i, buf);
        }
    }

    // ---- friendly fire (FR9): the one place a pulse picks its targets besides the pilot ----

    void Burn()
    {
        if (!HurtsOtherEnemies || !FriendlyFire.HostileFireAllowed) return;
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var c = live[i];
            if (c == null || !FriendlyFire.HostileFireCanHit(c, shooter)) continue;
            int id = c.gameObject.GetInstanceID();
            if (AlreadyHit(id)) continue;
            Vector2 p = c.transform.position;
            if (!shape.Touches(p, c.Radius * .8f)) continue;
            if (!FriendlyFire.HostileHit(c, new Vector3(p.x, p.y, 0f), Label)) return;   // capped this frame: not spent, next frame
            if (hitCount < hits.Length) hits[hitCount++] = id;
            return;   // one a frame: the registry changes under a kill
        }
    }

    bool AlreadyHit(int id)
    {
        for (int i = 0; i < hitCount; i++) if (hits[i] == id) return true;
        return false;
    }

    // ---- what the ship's code asks (collisionDetection, EliteShip) ----

    public static bool IsHitbox(GameObject go) => go != null && go.TryGetComponent(out AttackHazardHitbox _);

    // Shield or Cloak: the pulse is absorbed. False if `go` is not a hazard's hitbox.
    public static bool EraseHitbox(GameObject go)
    {
        if (go == null || !go.TryGetComponent(out AttackHazardHitbox hb)) return false;
        if (hb.hazard != null) hb.hazard.Erase();
        return true;
    }

    // A blink landed within the blast circle of this hitbox: erased only where the hull itself lies on the footprint.
    public static bool BlinkStrike(GameObject go, Vector3 at)
    {
        if (go == null || !go.TryGetComponent(out AttackHazardHitbox hb)) return false;
        if (hb.hazard != null && hb.hazard.LandsOn(at)) hb.hazard.Erase();
        return true;
    }

    public bool LandsOn(Vector2 at) => State == Phase.Live && shape.Touches(at, BlinkHullReach);

    // ---- the budget (FR7) ----

    // Roster shots' worth of every hazard that is burning right now (EnemyThreat.LiveShots).
    public static int LiveThreat
    {
        get
        {
            float w = 0f;
            for (int i = 0; i < registry.Count; i++)
                if (registry[i] != null && registry[i].State == Phase.Live) w += registry[i].ThreatWeight;
            return Mathf.CeilToInt(w - 1e-4f);
        }
    }

    public static int ActiveCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < registry.Count; i++) if (registry[i] != null && registry[i].Active) n++;
            return n;
        }
    }

    // Tests: forget the registry (the objects went with the scene).
    public static void ForgetAll() { registry.Clear(); }
}

// Marks a themed hazard's hitbox (AttackHazard.IsHitbox / EraseHitbox / BlinkStrike; RamKill, DeathCrash).
public class AttackHazardHitbox : MonoBehaviour
{
    public AttackHazard hazard;
}
