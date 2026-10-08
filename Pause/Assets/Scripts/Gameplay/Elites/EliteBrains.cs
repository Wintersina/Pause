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
//   bastion      (Space) a slow shield platform: holds high over the
//                board's middle, its lane following the pilot's at a crawl
//                but never more than laneOffset off centre; attacks when the
//                pilot is under its wings
//   reaver       (Space) circles on a flattened orbit lifted wholly above
//                the pilot and turns round at the end of every lap (and
//                after every volley); speeds round while it fires
//   lancer       (Space) holds high on one flank of the pilot, nose on it;
//                after each shot it dashes across to the other flank
//   tug          (Space) a slow armoured salvage tug: hangs ahead of the
//                pilot a lane to one side, swapping sides after each sling
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
    // How far above the pilot it has to be before it will attack (0: from
    // anywhere). With the ship too close under the reach cap for that, the
    // elite climbs back to its own goal for the attack (EliteShip.ReachGoal).
    public virtual float MinAttackAbove => 0f;
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
    public static readonly string[] Ids = { "interceptor", "gunship", "striker", "hauler", "skirmisher", "siege", "breaker", "warden",
                                            "bastion", "reaver", "lancer", "tug" };

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
            case "bastion": return new BastionBrain();
            case "reaver": return new ReaverBrain();
            case "lancer": return new LancerBrain();
            case "tug": return new TugBrain();
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

    public override float MinAttackAbove => .5f;
    public override bool WantsAttack(Vector2 seen) => Pos.y > seen.y + MinAttackAbove && Mathf.Abs(Pos.x - seen.x) < 1.1f;
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

    // Near the top of the view -- no higher than the player's reach (EliteShip.ReachY).
    public float Height => ship.ReachY(EliteSystem.ViewTop - def.topMargin);

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        if (!init) { trackX = Pos.x; init = true; }
        trackX = Mathf.MoveTowards(trackX, seen.x, def.speed * .6f * dt);
        return new Vector2(trackX, Height);
    }

    public override float MinAttackAbove => 1.5f;
    public override bool WantsAttack(Vector2 seen) => Mathf.Abs(Pos.x - seen.x) < .7f && Pos.y > seen.y + MinAttackAbove;
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

    public override float MinAttackAbove => def.followDistance * .6f;
    public override bool WantsAttack(Vector2 seen) =>
        Mathf.Abs(Pos.x - seen.x) < .45f && Pos.y > seen.y + MinAttackAbove;

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
            // (high up -- no higher than the player's reach: EliteShip.ReachY)
            return new Vector2(Mathf.Clamp(side * def.laneOffset, -edge, edge), ship.ReachY(EliteSystem.ViewTop - def.topMargin));
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
    public override float MinAttackAbove => 1.5f;
    public override bool WantsAttack(Vector2 seen) => settled && Pos.y > seen.y + MinAttackAbove;
    public void Cross() { side = -side; settled = false; }
    public override Vector2 JoinFrom => new Vector2(side, 1f);
}

// Eventide Bastion: a slow shield platform. Holds followDistance above the
// pilot over the board's middle -- its lane follows the pilot's at a crawl
// but never strays more than laneOffset off centre -- bobbing a little.
// Attacks when the pilot is under its wings.
public class BastionBrain : EliteBrain
{
    float trackX;
    bool init;
    public float TrackX => trackX;

    public BastionBrain() { Id = "bastion"; }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        if (!init) { trackX = Mathf.Clamp(Pos.x, -def.laneOffset, def.laneOffset); init = true; }
        trackX = Mathf.MoveTowards(trackX, Mathf.Clamp(seen.x, -def.laneOffset, def.laneOffset), def.speed * .45f * dt);
        return new Vector2(trackX, seen.y + def.followDistance + Mathf.Sin(Clock * 1.1f) * .12f);
    }

    public override float SpeedScale => .8f;
    public override float MinAttackAbove => 1.4f;
    public override bool WantsAttack(Vector2 seen) => Pos.y > seen.y + MinAttackAbove && Mathf.Abs(Pos.x - seen.x) < 1.8f;
    public override Vector2 JoinFrom => Vector2.up;
}

// Orbit Reaver: a fast raider on a flattened orbit (circleRadius across,
// Squash of that up and down) centred Lift x circleRadius above the pilot,
// so the whole orbit stays above it -- it swings to and fro over the pilot
// and turns round at the end of every lap. Attacks after at least
// AttackArc of a lap; it speeds round while it fires (ActScale) and turns
// round again after the volley.
public class ReaverBrain : EliteBrain
{
    public const float Squash = .7f, Lift = 1.3f, ActScale = 1.5f, AttackArc = Mathf.PI * .75f;
    float angle, lapped, swept, spin = 1f;
    bool started;
    public float Lapped => lapped;
    public float Spin => spin;
    public int Turns { get; private set; }

    public ReaverBrain() { Id = "reaver"; }

    public override void OnJoin() { started = false; lapped = 0f; swept = 0f; }

    public Vector2 Centre(Vector2 seen) => seen + Vector2.up * def.circleRadius * Lift;

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Vector2 c = Centre(seen);
        if (!started)
        {
            Vector2 d = Pos - c;
            angle = Mathf.Atan2(d.y / Squash, d.x);
            started = true;
        }
        float w = def.speed * SpeedScale / Mathf.Max(.5f, def.circleRadius);
        angle += spin * w * dt;
        lapped += w * dt;
        swept += w * dt;
        if (swept >= Mathf.PI * 2f) Turn(false);
        return c + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * Squash) * def.circleRadius;
    }

    public override float SpeedScale => ship != null && ship.Acting ? ActScale : 1f;
    public override bool WantsAttack(Vector2 seen) => lapped >= AttackArc;

    // Round the other way (after a lap, or a volley: then a new lap before the next one).
    public void Turn(bool volley)
    {
        spin = -spin;
        swept = 0f;
        if (volley) lapped = 0f;
        Turns++;
    }

    public override Vector2 JoinFrom => new Vector2(-1f, .6f);
}

// Rift Lancer: a flanker. Holds high on one side of the pilot (laneOffset
// across, followDistance up), nose on it, weaving a little; after each
// shot it dashes across to the other flank (DashScale x its speed until
// it gets there). Attacks once it is on a flank, in range.
public class LancerBrain : EliteBrain
{
    public const float DashScale = 1.8f;
    float side = 1f;
    bool crossing;
    public float Side => side;
    public bool Crossing => crossing;
    public int Crossings { get; private set; }

    public LancerBrain() { Id = "lancer"; }

    public override void OnJoin()
    {
        var p = EliteSystem.Player;
        side = p == null || Pos.x >= p.position.x ? 1f : -1f;
        crossing = false;
    }

    public Vector2 Flank(Vector2 seen)
    {
        float edge = EliteSystem.RailEdge - def.hullRadius - .45f;
        // no room on this side: the other
        if (Mathf.Abs(seen.x + side * def.laneOffset) > edge && Mathf.Abs(seen.x - side * def.laneOffset) <= edge) side = -side;
        // (no higher than the player's reach: EliteShip.ReachY -- the flank it arrives at is the one it holds)
        return new Vector2(Mathf.Clamp(seen.x + side * def.laneOffset, -edge, edge), ship.ReachY(seen.y + def.followDistance));
    }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        Vector2 f = Flank(seen);
        if (crossing && (Pos - f).sqrMagnitude < .45f * .45f) crossing = false;
        return f + new Vector2(0f, Mathf.Sin(Clock * 1.6f) * .2f);
    }

    public override float SpeedScale => crossing ? DashScale : 1f;
    // (above by .8, and 1.5 away from its flank laneOffset across)
    public override float MinAttackAbove => Mathf.Max(.8f, Mathf.Sqrt(Mathf.Max(0f, 1.6f * 1.6f - def.laneOffset * def.laneOffset)));

    public override bool WantsAttack(Vector2 seen)
    {
        float d = (seen - Pos).magnitude;
        return !crossing && Pos.y > seen.y + .8f && d > 1.5f && d < 4.6f;
    }

    public override float? FaceDeg(Vector2 seen) => FaceTowards(seen);

    // After a shot: across to the other flank, fast.
    public void Cross()
    {
        side = -side;
        crossing = true;
        Crossings++;
    }

    public override Vector2 JoinFrom => new Vector2(side, 1f);
}

// Singularity Hauler: a slow, armoured salvage tug. Hangs followDistance
// ahead of the pilot, a lane (laneOffset) to one side, that lane following
// the pilot's at a crawl; it swaps sides after each sling.
public class TugBrain : EliteBrain
{
    float laneX, side = 1f;
    bool init;
    public float Side => side;
    public float LaneX => laneX;

    public TugBrain() { Id = "tug"; }

    public override void OnJoin()
    {
        var p = EliteSystem.Player;
        side = p == null || Pos.x >= p.position.x ? 1f : -1f;
    }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        float edge = EliteSystem.RailEdge - def.hullRadius - .4f;
        if (Mathf.Abs(seen.x + side * def.laneOffset) > edge && Mathf.Abs(seen.x - side * def.laneOffset) <= edge) side = -side;
        float want = Mathf.Clamp(seen.x + side * def.laneOffset, -edge, edge);
        if (!init) { laneX = Pos.x; init = true; }
        laneX = Mathf.MoveTowards(laneX, want, def.speed * .5f * dt);
        return new Vector2(laneX, seen.y + def.followDistance + Mathf.Sin(Clock * .9f) * .1f);
    }

    public override float SpeedScale => .75f;
    public override float MinAttackAbove => 1.2f;
    public override bool WantsAttack(Vector2 seen) => Pos.y > seen.y + MinAttackAbove && Mathf.Abs(Pos.x - seen.x) < def.laneOffset + .8f;
    public void Swap() { side = -side; }
    public override Vector2 JoinFrom => Vector2.up;
}
