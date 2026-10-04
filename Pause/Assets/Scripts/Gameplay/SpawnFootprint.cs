using UnityEngine;

// A live enemy's (or pickup's) reserved space on the board -- see SpawnSpace.
//
// Holds the body's half-extents and the movement pattern that says where the
// body is going (IMovementFootprint; none = a plain scroller). Registers with
// SpawnSpace while enabled, so placement queries never scan the scene.
//
//   SpawnFootprint.Attach(go, half, layer)   -- once, at spawn
//   SpawnFootprint.Bind(go, pattern)         -- when the pattern is attached
//                                               or swapped (a mine mounting
//                                               its rail, a future attack)
//
// [ExecuteAlways] only so edit-mode tests see the same registration the game
// does; there is no per-frame work here.
[ExecuteAlways]
[DisallowMultipleComponent]
public class SpawnFootprint : MonoBehaviour
{
    public SpawnLayer layer = SpawnLayer.Enemy;
    public Vector2 half = new Vector2(.3f, .3f);

    IMovementFootprint plan;
    Behaviour planBehaviour;     // the plan's component, when it is one
    internal int registryIndex = -1;
    bool registered;

    public IMovementFootprint Plan => Held ? null : plan;

    // The pattern's component has been switched off (an EMP stun, a black
    // hole's grip, the magnet): the body stops scrolling and simply holds its
    // place in the world, so in board space it rises with the scroll.
    public bool Held => planBehaviour != null && !planBehaviour.enabled;

    public Vector2 Center => transform.position;
    public Rect Body => SpawnSpace.BodyRect(transform.position, half);

    // Board-space sweep over [from, to] seconds of flight from now.
    public Rect Sweep(float from, float to)
    {
        if (Held)
        {
            // standing still in the world = rising through the board
            Rect b = Body;
            b.yMax += SpawnSpace.ScrollSpeed * to;
            return b;
        }
        return plan != null ? plan.SweptBounds(transform.position, half, from, to) : Body;
    }

    // What a self-steering pattern must keep clear of right now: a
    // board-locked footprint's near-future sweep (the whole band a weaving
    // rock crosses), another steerer's (or a held enemy's) body.
    public Rect Envelope()
    {
        if (Held || plan == null || plan.SelfSteering) return Body;
        return plan.SweptBounds(transform.position, half, 0f, SpawnSpace.SteerHorizon);
    }

    public void SetPlan(IMovementFootprint p)
    {
        plan = p;
        planBehaviour = p as Behaviour;
    }

    public static SpawnFootprint Attach(GameObject go, Vector2 half, SpawnLayer layer = SpawnLayer.Enemy)
    {
        if (go == null) return null;
        SpawnFootprint f;
        if (!go.TryGetComponent(out f))
        {
            // register only once layer/half are set (OnEnable fires inside
            // AddComponent on an active object)
            bool wasActive = go.activeSelf;
            f = go.AddComponent<SpawnFootprint>();
            if (f.registered) f.Unregister();
            f.layer = layer;
            f.half = half;
            if (wasActive && f.isActiveAndEnabled) f.Register();
            return f;
        }
        if (f.layer != layer && f.registered) { f.Unregister(); f.layer = layer; f.Register(); }
        f.layer = layer;
        f.half = half;
        return f;
    }

    public static void Bind(GameObject go, IMovementFootprint pattern)
    {
        SpawnFootprint f;
        if (go != null && go.TryGetComponent(out f)) f.SetPlan(pattern);
    }

    void Register()
    {
        if (registered) return;
        SpawnSpace.Register(this);
        registered = true;
    }

    void Unregister()
    {
        if (!registered) return;
        SpawnSpace.Unregister(this);
        registered = false;
    }

    void OnEnable() { Register(); }
    void OnDisable() { Unregister(); }
}
