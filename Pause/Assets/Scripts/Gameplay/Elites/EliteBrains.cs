using UnityEngine;

// How an elite flies while it isn't attacking: a personality on a shared
// base (EliteDef: speed, accel, turnRate, followDistance, aggression,
// tellSeconds, attackGap, avoidance ...). A brain only says where it wants
// to be (Goal), how fast (SpeedScale), which way to face and when it wants
// to attack; EliteShip does the flying -- arriving, dodging, turn and
// acceleration limits, the rails -- so every brain dodges the same way and
// can crash the same way.
//
// `seen` is where the elite thinks the pilot is (EliteShip.Perceive): it
// lags after a pause-teleport, so a brain keeps flying to the old spot.
//
//   interceptor  stalks from behind (below the pilot), weaving a little;
//                attacks when it has closed in
//   gunship      holds a lane beside the pilot, matching its height;
//                attacks when lined up across
//   striker      circles the pilot; attacks after at least half a lap
//   hauler       drifts slowly into the pilot's lane just ahead of it,
//                blocking it; attacks from above
//   skirmisher   keeps its distance, sliding round; blinks away from
//                anything about to hit it
//   siege        holds near the top of the view, tracking the pilot's lane
//                slowly; attacks when over it
//   breaker      prowls ahead of the pilot, sweeping side to side across
//                its lane like an icebreaker looking for a lead; attacks
//                the moment it is over the pilot's lane
//   warden       takes a station high on the side away from the pilot and
//                holds it, bobbing; after each attack it crosses to the
//                other side and plants again
public abstract class EliteBrain
{
    protected EliteShip ship;
    protected EliteDef def;
    public string Id { get; protected set; }

    public void Bind(EliteShip s)
    {
        ship = s;
        def = s.Def;
        OnBind();
    }

    protected virtual void OnBind() { }
    public virtual void OnJoin() { }

    public abstract Vector2 Goal(Vector2 seen, float dt);
    public virtual float SpeedScale => 1f;
    public virtual bool WantsAttack(Vector2 seen) => true;
    // Preferred facing (world degrees), or null: face the way it flies.
    public virtual float? FaceDeg(Vector2 seen) => null;
    public virtual bool DodgesByBlink => false;
    // Where the lift-off would best end, relative to the pilot (direction).
    public virtual Vector2 JoinFrom => Vector2.down;

    protected Vector2 Pos => ship.Position;
    protected float Aggro => Mathf.Clamp01(def.aggression);
    protected float Clock;

    protected float FaceTowards(Vector2 p)
    {
        Vector2 d = p - Pos;
        return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
    }
}

public static class EliteBrains
{
    public static readonly string[] Ids = { "interceptor", "gunship", "striker", "hauler", "skirmisher", "siege", "breaker", "warden" };

    public static EliteBrain Create(string id)
    {
        switch (id)
        {
            case "gunship": return new GunshipBrain();
            case "striker": return new StrikerBrain();
            case "hauler": return new HaulerBrain();
            case "skirmisher": return new SkirmisherBrain();
            case "siege": return new SiegeBrain();
            case "breaker": return new BreakerBrain();
            case "warden": return new WardenBrain();
            default: return new InterceptorBrain();
        }
    }
}

// Sunstoke: stalks from behind, closing as aggression rises.
public class InterceptorBrain : EliteBrain
{
    public InterceptorBrain() { Id = "interceptor"; }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        float back = def.followDistance * Mathf.Lerp(1.15f, .8f, Aggro);
        return seen + new Vector2(Mathf.Sin(Clock * 1.3f) * .45f, -back);
    }

    public override bool WantsAttack(Vector2 seen)
    {
        return (seen - Pos).magnitude < def.followDistance * 1.7f;
    }

    public override float? FaceDeg(Vector2 seen) => FaceTowards(seen);
    public override Vector2 JoinFrom => Vector2.down;
}

// Coalrunner: a lane beside the pilot, at its height.
public class GunshipBrain : EliteBrain
{
    float side = 1f;
    public float Lane { get; private set; }
    public float Side => side;

    public GunshipBrain() { Id = "gunship"; }

    protected override void OnBind() { side = Random.value < .5f ? -1f : 1f; }
    public override void OnJoin()
    {
        var p = EliteSystem.Player;
        side = p == null || Pos.x >= p.position.x ? 1f : -1f;
    }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        float edge = EliteSystem.RailEdge - def.hullRadius - .35f;
        float lane = seen.x + side * def.laneOffset;
        // no room on this side: switch to the other
        if (Mathf.Abs(lane) > edge && Mathf.Abs(seen.x - side * def.laneOffset) <= edge + .2f) side = -side;
        Lane = Mathf.Clamp(seen.x + side * def.laneOffset, -edge, edge);
        return new Vector2(Lane, seen.y + .1f);
    }

    public override bool WantsAttack(Vector2 seen)
    {
        return Mathf.Abs(Pos.y - seen.y) < .6f && Mathf.Abs(Pos.x - Lane) < .5f;
    }

    public override float SpeedScale => 1.1f;
    public override Vector2 JoinFrom => new Vector2(side, 0f);
}

// Brass Vulture: circles the pilot, then dives.
public class StrikerBrain : EliteBrain
{
    float angle, lapped;
    bool started;
    public float Lapped => lapped;

    public StrikerBrain() { Id = "striker"; }

    public override void OnJoin() { started = false; lapped = 0f; }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        if (!started)
        {
            Vector2 d = Pos - seen;
            angle = Mathf.Atan2(d.y, d.x);
            started = true;
        }
        float w = def.speed / Mathf.Max(.5f, def.circleRadius);
        angle += w * dt;
        lapped += w * dt;
        return seen + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * .85f) * def.circleRadius;
    }

    public override bool WantsAttack(Vector2 seen) => lapped >= Mathf.PI;

    // a new lap after each dive
    public void ResetLap() { lapped = 0f; started = false; }
    public override Vector2 JoinFrom => new Vector2(1f, .3f);
}

// Kilnback: slow, drifts into the pilot's lane just ahead of it.
public class HaulerBrain : EliteBrain
{
    float laneX;
    bool init;

    public HaulerBrain() { Id = "hauler"; }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        if (!init) { laneX = Pos.x; init = true; }
        // the lane follows the pilot slowly: it is a wall, not a hunter
        laneX = Mathf.MoveTowards(laneX, seen.x, def.speed * .55f * dt);
        return new Vector2(laneX, seen.y + def.followDistance);
    }

    public override bool WantsAttack(Vector2 seen) => Pos.y > seen.y + .5f && Mathf.Abs(Pos.x - seen.x) < 1.1f;
    public override float SpeedScale => .8f;
    public override Vector2 JoinFrom => Vector2.up;
}

// Ash Wraith: keeps its distance, sliding round the pilot; blinks out of
// trouble.
public class SkirmisherBrain : EliteBrain
{
    float drift = 1f;

    public SkirmisherBrain() { Id = "skirmisher"; }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        Vector2 d = Pos - seen;
        if (d.sqrMagnitude < 1e-4f) d = Vector2.up;
        float a = Mathf.Atan2(d.y, d.x) + drift * .35f * dt * 3f;
        if (Mathf.Repeat(Clock, 4f) < dt) drift = -drift;
        return seen + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * def.keepDistance;
    }

    public override bool WantsAttack(Vector2 seen)
    {
        float d = (seen - Pos).magnitude;
        return d > def.keepDistance * .6f && d < def.keepDistance * 1.6f;
    }

    public override float? FaceDeg(Vector2 seen) => FaceTowards(seen);
    public override bool DodgesByBlink => true;
    public override Vector2 JoinFrom => new Vector2(-1f, .5f);
}

// Cauterizer: holds high, tracking the pilot's lane slowly, nose down.
public class SiegeBrain : EliteBrain
{
    float trackX;
    bool init;

    public SiegeBrain() { Id = "siege"; }

    public float Height => EliteSystem.ViewTop - def.topMargin;

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        if (!init) { trackX = Pos.x; init = true; }
        trackX = Mathf.MoveTowards(trackX, seen.x, def.speed * .6f * dt);
        return new Vector2(trackX, Height);
    }

    public override bool WantsAttack(Vector2 seen) => Mathf.Abs(Pos.x - seen.x) < .7f && Pos.y > seen.y + 1.5f;
    public override float? FaceDeg(Vector2 seen) => -90f;
    public override Vector2 JoinFrom => Vector2.up;
}

// Rimebreaker: prowls followDistance ahead of the pilot, sweeping across
// its lane (laneOffset either side, a slow sine), so it keeps crossing over
// the pilot -- and rams down the lane the moment it is over it.
public class BreakerBrain : EliteBrain
{
    public BreakerBrain() { Id = "breaker"; }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        float sweep = Mathf.Sin(Clock * Mathf.Lerp(.9f, 1.4f, Aggro)) * def.laneOffset;
        return new Vector2(seen.x + sweep, seen.y + def.followDistance);
    }

    public override bool WantsAttack(Vector2 seen) =>
        Mathf.Abs(Pos.x - seen.x) < .45f && Pos.y > seen.y + def.followDistance * .6f;

    public override Vector2 JoinFrom => Vector2.up;
}

// Resin Warden: a station high up on the side away from the pilot
// (laneOffset off the middle, topMargin below the top), held with a slow
// bob; it attacks once settled there, then crosses to the other side.
public class WardenBrain : EliteBrain
{
    float side = 1f;
    bool settled;
    public float Side => side;
    public bool Settled => settled;

    public WardenBrain() { Id = "warden"; }

    public override void OnJoin()
    {
        var p = EliteSystem.Player;
        side = p == null || p.position.x <= 0f ? 1f : -1f;
    }

    public Vector2 Station
    {
        get
        {
            float edge = EliteSystem.RailEdge - def.hullRadius - .45f;
            return new Vector2(Mathf.Clamp(side * def.laneOffset, -edge, edge), EliteSystem.ViewTop - def.topMargin);
        }
    }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        Vector2 st = Station;
        settled = (Pos - st).sqrMagnitude < .35f * .35f;
        return st + new Vector2(0f, Mathf.Sin(Clock * 1.7f) * .12f);
    }

    public override float SpeedScale => settled ? .6f : 1f;
    public override bool WantsAttack(Vector2 seen) => settled && Pos.y > seen.y + 1.5f;
    public void Cross() { side = -side; settled = false; }
    public override Vector2 JoinFrom => new Vector2(side, 1f);
}
