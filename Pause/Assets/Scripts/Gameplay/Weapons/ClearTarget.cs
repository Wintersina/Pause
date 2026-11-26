using System.Collections.Generic;
using UnityEngine;

// Live registry of everything the ultimate's cinematic clear could be aiming
// at, so ShipPowerController can tell the instant the screen is empty and end
// the slow-motion early without a FindObjectsByType scan every frame.
//
// The hazard movers (moveEnimes, moveItemEnmInStrightLine) call Ensure() from
// Awake, which covers every enemy, asteroid, alien, chaser and rail mine
// prefab; CinematicClear also Ensure()s each target it snapshots, so anything
// tagged that slipped past a mover still counts. Registration follows the
// component's enabled state, and OnDisable also runs on Destroy, so the list
// never holds a dead object for longer than the frame it died in.
//
// [ExecuteAlways] only so edit-mode tests see the same OnEnable/OnDisable
// bookkeeping the game does; there is no per-frame work here.
[ExecuteAlways]
[DisallowMultipleComponent]
public class ClearTarget : MonoBehaviour, IPoolReset
{
    static readonly List<ClearTarget> live = new List<ClearTarget>();

    // Registered entries, hazard-tagged or not (movers sit on pickups too).
    public static int Count => live.Count;

    // The registry itself, for the ship attacks and secret powers to query
    // without a scene scan. Read it, don't hold on to it: a hit that
    // Release()s a target removes it from this list, so callers copy what
    // they need into their own buffer before acting (ShipTargets.Collect).
    public static List<ClearTarget> Live => live;

    float radius = -1f;

    // Scenery that still takes a hit (a hit-taking backdrop body): the ship's weapons
    // and ultimate see it like any hazard, but nothing else treats it as a body -- not the elites' sensor,
    // friendly fire, the weather, the secret powers' threat counts nor the ultimate's "screen cleared" check.
    [System.NonSerialized] public bool Scenery;

    // ---- the elites' threat sensor (EliteEvasion.Sense) ----
    // Where it was the last time the sensor looked and how fast it was
    // moving (measured, so a weave, a lunge and a chaser all read true), and
    // what it is, looked up once per object -- never per frame.
    [System.NonSerialized] public Vector2 SensedAt, SensedVelocity;
    [System.NonSerialized] public int SensedStep = -1;
    [System.NonSerialized] public bool SensedMeasured;   // false: first sight, SensedVelocity is a guess
    [System.NonSerialized] public EliteShip Elite;   // set by the elite itself when it joins the play
    [System.NonSerialized] public EliteShip Mother;  // an elite's own drone (drone_deploy): it never crashes into or dodges it
    // Seconds it has been inside the playfield (view, between the rails):
    // hostile fire's spawn-in protection (FriendlyFire.TrackPlayfield).
    [System.NonSerialized] public float PlayfieldSeconds;
    EnemyBrain brain;
    bool classified, shotHitbox, sceneryBody;

    void Classify()
    {
        classified = true;
        TryGetComponent(out brain);
        shotHitbox = TryGetComponent(out EliteShotHitbox _);
        sceneryBody = TryGetComponent(out ISceneryTarget _);
    }

    // A pooled body starts over: nothing sensed, no mother, not scenery. (Its measured radius and
    // classification stay: the instance is the same body.)
    public void OnPoolAcquire()
    {
        Scenery = false;
        SensedAt = SensedVelocity = Vector2.zero;
        SensedStep = -1;
        SensedMeasured = false;
        Elite = null;
        Mother = null;
        PlayfieldSeconds = 0f;
        enabled = true;
        InvalidateBody();
    }

    // ---- the weather's per-body lookups (WorldHazards.PushBodies / StunBand), resolved once per body, not per frame ----
    // The components a hazard field asks about on every body every frame while it is active. They are all added in the
    // body's construction (the same call that makes it), so the first frame it is looked at sees them; a pooled body
    // forgets them on reuse (OnPoolAcquire) and a body that went away takes the cache with it.
    bool bodyResolved, bodyExempt;
    RailMineMount bodyMount;
    SpawnFootprint bodyFootprint;
    FrozenState bodyFrost;

    void ResolveBody()
    {
        bodyResolved = true;
        bodyExempt = TryGetComponent(out BossTarget _) || TryGetComponent(out BossActor _) || TryGetComponent(out EliteShip _);
        TryGetComponent(out bodyMount);
        TryGetComponent(out bodyFootprint);
        TryGetComponent(out bodyFrost);
    }

    public void InvalidateBody() { bodyResolved = false; bodyExempt = false; bodyMount = null; bodyFootprint = null; bodyFrost = null; }

    // A boss, a boss part or an elite (the weather treats those on their own).
    public bool IsBossOrElite { get { if (!bodyResolved) ResolveBody(); return bodyExempt; } }
    public RailMineMount BodyMount { get { if (!bodyResolved) ResolveBody(); return bodyMount; } }
    public SpawnFootprint BodyFootprint { get { if (!bodyResolved) ResolveBody(); return bodyFootprint; } }

    // The weather's stun: a frozen body is refreshed in place (the same failsafe reset Freeze does), anything else is frozen as before.
    public FrozenState Stun(float seconds)
    {
        if (!bodyResolved) ResolveBody();
        if (bodyFrost != null && bodyFrost.IsFrozen) { bodyFrost.Refresh(seconds); return bodyFrost; }
        bodyFrost = FrozenState.Freeze(gameObject, seconds);
        return bodyFrost;
    }

    public EnemyBrain Brain { get { if (!classified) Classify(); return brain; } }
    // Scenery by flag or by what it is (an ISceneryTarget body): never something a sentry picks as its mark.
    public bool IsScenery { get { if (!classified) Classify(); return Scenery || sceneryBody; } }
    public bool IsShotHitbox { get { if (!classified) Classify(); return shotHitbox; } }

    // Rough hit radius in world units, from the collider (or renderer) the
    // first time it's asked, so per-frame hit tests never call GetComponent.
    public bool HasRadius => radius >= 0f;

    public float Radius
    {
        get
        {
            if (radius < 0f) radius = MeasureRadius(gameObject);
            return radius;
        }
    }

    // Set it outright (the boss hitbox, test fixtures).
    public void SetRadius(float r) { radius = Mathf.Max(.05f, r); }

    public static float MeasureRadius(GameObject go)
    {
        Bounds b;
        var col = go.GetComponent<Collider2D>();
        if (col != null && col.enabled) { Physics2D.SyncTransforms(); b = col.bounds; }   // (auto sync is off: bounds follow the last sync)
        else
        {
            var r = go.GetComponentInChildren<Renderer>();
            if (r == null) return .3f;
            b = r.bounds;
        }
        float e = Mathf.Max(b.extents.x, b.extents.y);
        return Mathf.Clamp(e <= 0f ? .3f : e, .15f, 1.3f);
    }

    public static ClearTarget Ensure(GameObject go)
    {
        if (go == null) return null;
        var existing = go.GetComponent<ClearTarget>();
        return existing != null ? existing : go.AddComponent<ClearTarget>();
    }

    // Called by a hit that is about to Destroy() the object: Destroy is
    // deferred to the end of the frame, and the clear check should see the
    // target gone on the frame it was actually hit.
    public static void Release(GameObject go)
    {
        if (go == null) return;
        var t = go.GetComponent<ClearTarget>();
        if (t != null) t.enabled = false;
    }

    public static bool IsHazard(GameObject go)
    {
        return go != null && (go.CompareTag("Enimey") || go.CompareTag("Astr"));
    }

    // Hazards whose centre is inside the camera's view. Anything above the
    // top edge, scrolled out the bottom, or already destroyed doesn't count.
    // With no camera there is no "screen", so every hazard counts.
    public static int CountOnScreen(Camera cam)
    {
        int n = 0;
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var t = live[i];
            if (t == null) { live.RemoveAt(i); continue; }
            if (!IsHazard(t.gameObject) || t.Scenery) continue;
            if (cam == null || OnScreen(cam, t.transform.position)) n++;
        }
        return n;
    }

    static bool OnScreen(Camera cam, Vector3 world)
    {
        Vector3 v = cam.WorldToViewportPoint(world);
        return v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;
    }

    void OnEnable()
    {
        PlayfieldSeconds = 0f;
        if (!live.Contains(this)) live.Add(this);
    }

    void OnDisable()
    {
        live.Remove(this);
    }
}
