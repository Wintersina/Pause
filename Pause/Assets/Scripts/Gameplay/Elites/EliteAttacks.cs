using UnityEngine;

// What an elite does when it attacks: the tell (EliteArt.Tell drawing for
// TellSeconds, locking its aim) then the action (EliteArt.Action drawing
// while the attack plays). Each elite names one in its def (`attack`) with
// its own numbers (dashSpeed, shotCount, shotSpeed, shotSpread, shotSize,
// shotInterval, actionSeconds, blinkDistance, shotKind), and every shot
// leaves one of its measured muzzles (EliteDef.muzzles) -- never the
// ship's centre.
//
//   lance_dash    Sunstoke: aims, then rams straight down the locked line
//                 at dashSpeed; as it brakes, a fan of heat needles leaves
//                 the lance tip
//   broadside     Coalrunner: holds its lane and strafes -- bolts out of
//                 both side cannons, volley after volley
//   claw_dive     Brass Vulture: locks the pilot's spot, dives through it
//                 claw first, flinging scrap shards at the bottom
//   slag_drop     Kilnback: dumps molten slag blobs from its thrusters
//                 that sink down the lane it blocks
//   blink_shards  Ash Wraith: blinks sideways out of its spot, then fans
//                 shards at the pilot from its beak
//   siege_cannon  Cauterizer: a long charge with a sight line down its
//                 lane, then one piercing shell straight down it
//   ice_ram       Rimebreaker: locks the lane below it, then ploughs
//                 straight down it, breaking through rocks (no heart lost);
//                 frost shards fly off the prow at the launch and off every
//                 rock it breaks, glancing off the rails
//   resin_mortar  Resin Warden: plants itself, then lobs resin globs in
//                 high arcs onto a row of marked spots across the lane
//                 ahead of the pilot; each lands as a sticky pool that
//                 rides the board for a few seconds
public abstract class EliteAttack
{
    protected EliteShip ship;
    protected EliteDef def;
    protected Vector2 aim;          // locked target point
    protected Vector2 dir;          // locked direction
    protected float t;
    public string Id { get; protected set; }

    // Locked at the tell (tests).
    public Vector2 Aim => aim;
    public Vector2 Dir => dir;
    public int Fired { get; protected set; }

    public void Bind(EliteShip s) { ship = s; def = s.Def; }

    public virtual float TellSeconds => def.tellSeconds;
    public virtual bool HoldsDuringTell => false;
    public virtual bool DrivesMovement => false;
    public virtual float? FaceDeg => null;
    // Breaks through rocks while acting (EliteShip.CrashInto): no heart lost.
    public virtual bool Ploughs => false;
    public virtual void OnPlough(Vector2 at) { }

    public virtual void BeginTell(Vector2 seen)
    {
        aim = seen;
        Vector2 d = seen - ship.Position;
        dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.down;
        t = 0f;
    }

    public virtual void StepTell(float dt) { }
    public virtual void BeginAction() { t = 0f; }
    // True when the action is over.
    public abstract bool StepAction(float dt);
    public virtual void End() { }

    protected EliteShot Fire(int muzzle, float deg, float speed)
    {
        Vector2 at = ship.MuzzleWorld(muzzle);
        float r = deg * Mathf.Deg2Rad;
        Fired++;
        ship.OnFired(muzzle, deg);
        return EliteSystem.Shots.Fire(ship, def, EliteShots.KindOf(def.shotKind), at, new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * speed);
    }

    protected static float Deg(Vector2 v) => Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
}

public static class EliteAttacks
{
    public static readonly string[] Ids = { "lance_dash", "broadside", "claw_dive", "slag_drop", "blink_shards", "siege_cannon", "ice_ram", "resin_mortar" };

    public static EliteAttack Create(string id)
    {
        switch (id)
        {
            case "broadside": return new BroadsideAttack();
            case "claw_dive": return new ClawDiveAttack();
            case "slag_drop": return new SlagDropAttack();
            case "blink_shards": return new BlinkShardsAttack();
            case "siege_cannon": return new SiegeCannonAttack();
            case "ice_ram": return new IceRamAttack();
            case "resin_mortar": return new ResinMortarAttack();
            default: return new LanceDashAttack();
        }
    }
}

public class LanceDashAttack : EliteAttack
{
    public LanceDashAttack() { Id = "lance_dash"; }
    public override bool HoldsDuringTell => true;
    public override bool DrivesMovement => true;
    public override float? FaceDeg => Deg(dir);

    public override void StepTell(float dt)
    {
        // keeps re-aiming through the first half of the wind-up
        t += dt;
        if (t < TellSeconds * .5f) BeginTellAim();
    }

    void BeginTellAim()
    {
        Vector2 d = ship.Seen - ship.Position;
        if (d.sqrMagnitude > 1e-4f) { dir = d.normalized; aim = ship.Seen; }
    }

    bool needles;

    public override void BeginAction()
    {
        base.BeginAction();
        needles = false;
        ship.Drive(dir * def.dashSpeed);
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        // committed: no dodging, no turning -- the bait
        bool braking = t >= def.actionSeconds * .8f;
        ship.Drive(dir * def.dashSpeed * (braking ? .5f : 1f));
        if (braking && !needles)
        {
            // the parting shot: heat needles off the lance tip as it brakes
            // (after the ram, so they never clear its path for it)
            needles = true;
            float baseDeg = Deg(dir);
            int n = Mathf.Max(1, def.shotCount);
            for (int i = 0; i < n; i++)
                Fire(0, baseDeg + (i - (n - 1) * .5f) * def.shotSpread, def.shotSpeed + def.dashSpeed * .5f);
        }
        return t >= def.actionSeconds;
    }
}

public class BroadsideAttack : EliteAttack
{
    float next;
    int volleys;
    public BroadsideAttack() { Id = "broadside"; }
    public int Volleys => volleys;

    public override void BeginAction()
    {
        base.BeginAction();
        next = 0f;
        volleys = 0;
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        while (t >= next && volleys < Mathf.Max(1, def.shotCount))
        {
            for (int m = 0; m < def.muzzles.Length; m++)
                Fire(m, ship.MuzzleDeg(m), def.shotSpeed);
            volleys++;
            next += def.shotInterval;
        }
        return t >= def.actionSeconds && volleys >= Mathf.Max(1, def.shotCount);
    }
}

public class ClawDiveAttack : EliteAttack
{
    bool flung;
    public ClawDiveAttack() { Id = "claw_dive"; }
    public override bool DrivesMovement => true;
    public override float? FaceDeg => Deg(dir);

    public override void BeginTell(Vector2 seen)
    {
        // leads the pilot a little: aims where it is drifting
        base.BeginTell(seen);
    }

    public override void BeginAction()
    {
        base.BeginAction();
        flung = false;
        Vector2 d = aim - ship.Position;
        dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.down;
        ship.Drive(dir * def.dashSpeed);
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        Vector2 to = aim - ship.Position;
        bool through = Vector2.Dot(to, dir) <= 0f;
        ship.Drive(dir * def.dashSpeed * (through ? .45f : 1f));
        if ((through || t >= def.actionSeconds) && !flung)
        {
            flung = true;
            int n = Mathf.Max(1, def.shotCount);
            float baseDeg = Deg(dir);
            for (int i = 0; i < n; i++)
                Fire(0, baseDeg + (i - (n - 1) * .5f) * def.shotSpread, def.shotSpeed);
        }
        return t >= def.actionSeconds;
    }

    public override void End()
    {
        var s = ship.Brain as StrikerBrain;
        if (s != null) s.ResetLap();
    }
}

public class SlagDropAttack : EliteAttack
{
    float next;
    int dropped;
    public SlagDropAttack() { Id = "slag_drop"; }

    public override void BeginAction()
    {
        base.BeginAction();
        next = 0f;
        dropped = 0;
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        int n = Mathf.Max(1, def.shotCount);
        while (t >= next && dropped < n)
        {
            int m = def.muzzles.Length > 0 ? dropped % def.muzzles.Length : 0;
            Fire(m, -90f + Random.Range(-def.shotSpread, def.shotSpread), def.shotSpeed);
            dropped++;
            next += def.shotInterval;
        }
        return t >= def.actionSeconds && dropped >= n;
    }
}

public class BlinkShardsAttack : EliteAttack
{
    bool fired;
    public BlinkShardsAttack() { Id = "blink_shards"; }
    public override float? FaceDeg => Deg(ship.Seen - ship.Position);

    public override void BeginAction()
    {
        base.BeginAction();
        fired = false;
        ship.Blink(ship.BlinkSpot(), true);
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        if (!fired && t >= def.actionSeconds * .35f)
        {
            fired = true;
            Vector2 d = ship.Seen - ship.MuzzleWorld(0);
            float baseDeg = Deg(d);
            int n = Mathf.Max(1, def.shotCount);
            for (int i = 0; i < n; i++)
                Fire(0, baseDeg + (i - (n - 1) * .5f) * def.shotSpread, def.shotSpeed);
        }
        return t >= def.actionSeconds;
    }
}

public class SiegeCannonAttack : EliteAttack
{
    bool fired;
    public SiegeCannonAttack() { Id = "siege_cannon"; }
    public override bool HoldsDuringTell => true;
    public override float? FaceDeg => -90f;

    public override void StepTell(float dt)
    {
        t += dt;
        Vector2 m = ship.MuzzleWorld(0);
        // the sight line down the lane, blinking on twos, faster near the end
        float period = t > TellSeconds * .7f ? 2f : 4f;
        bool on = Mathf.FloorToInt(t / (period * EliteArt.Tick)) % 2 == 0;
        ship.ShowSight(m, -90f, Mathf.Max(.5f, m.y - EliteSystem.ViewBottom + .5f), on);
    }

    public override void BeginAction()
    {
        base.BeginAction();
        fired = false;
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        if (!fired)
        {
            fired = true;
            int n = Mathf.Max(1, def.shotCount);
            for (int i = 0; i < n; i++)
                Fire(0, -90f + (i - (n - 1) * .5f) * def.shotSpread, def.shotSpeed);
            ship.Drive(Vector2.up * 1.6f);   // recoil
        }
        return t >= def.actionSeconds;
    }

    public override void End() { ship.ShowSight(Vector2.zero, 0f, 0f, false); }
}

// Rimebreaker: an icebreaker. The tell locks the lane straight below it
// (the prow glows, the hull squats); the action ploughs down that lane at
// dashSpeed -- committed, no steering, so a sidestep makes it miss -- and
// breaks through any rock in its way without losing a heart. As it launches
// it throws two frost shards off the prow, down and out to both sides;
// every rock it breaks throws two more, flatter. Shards glance off the
// rails (shotBounces), so they come back across the board.
public class IceRamAttack : EliteAttack
{
    public const int MaxPloughShards = 3;   // rocks per ram that throw shards
    int smashed;
    float laneX;
    public IceRamAttack() { Id = "ice_ram"; }
    public override bool HoldsDuringTell => true;
    public override bool DrivesMovement => true;
    public override bool Ploughs => true;
    public float LaneX => laneX;
    public int Smashed => smashed;

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        laneX = ship.Position.x;
        dir = Vector2.down;
        aim = new Vector2(laneX, seen.y);
    }

    public override void BeginAction()
    {
        base.BeginAction();
        smashed = 0;
        ship.Drive(Vector2.down * def.dashSpeed);
        // the launch: a shard down-left and one down-right off the prow
        float spread = Mathf.Max(10f, def.shotSpread);
        Fire(0, -90f - spread, def.shotSpeed);
        Fire(0, -90f + spread, def.shotSpeed);
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        bool braking = t >= def.actionSeconds * .8f;
        ship.Drive(Vector2.down * def.dashSpeed * (braking ? .35f : 1f));
        return t >= def.actionSeconds;
    }

    public override void OnPlough(Vector2 at)
    {
        if (smashed++ >= MaxPloughShards) return;
        // the broken rock's ice: two flat shards, out to both rails
        Fire(0, -90f - 70f, def.shotSpeed * .9f);
        Fire(0, -90f + 70f, def.shotSpeed * .9f);
    }
}

// Resin Warden: a bio-industrial mortar. The tell plants it (the resin
// chambers charge, the hull squats); the action lobs shotCount resin
// globs, one after another out of alternate pods, in high arcs onto a row
// of spots lobSpacing apart across the lane lobAhead in front of the pilot
// (centred on where it sees the pilot, kept inside the rails), sweeping
// from its own side across. Each spot is marked by a blinking ring while
// its glob is in the air; harmless in flight, a glob lands as a sticky
// resin pool that rides the board for poolSeconds -- a wall with gaps.
public class ResinMortarAttack : EliteAttack
{
    float next;
    int lobbed;
    Vector2 rowCentre;
    bool fromLeft;
    public ResinMortarAttack() { Id = "resin_mortar"; }
    public override bool HoldsDuringTell => true;
    public Vector2 RowCentre => rowCentre;
    public int Lobbed => lobbed;

    public Vector2 Spot(int i)
    {
        int n = Mathf.Max(1, def.shotCount);
        return new Vector2(rowCentre.x + (i - (n - 1) * .5f) * def.lobSpacing, rowCentre.y);
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        rowCentre = seen + Vector2.up * def.lobAhead;
    }

    public override void BeginAction()
    {
        base.BeginAction();
        next = 0f;
        lobbed = 0;
        // the row: lobAhead in front of the pilot when the globs come down
        // (the spots ride the board meanwhile), inside the rails
        rowCentre = ship.Seen + Vector2.up * (def.lobAhead + EliteSystem.Scroll * def.lobSeconds);
        float half = (Mathf.Max(1, def.shotCount) - 1) * .5f * def.lobSpacing;
        float edge = Mathf.Max(0f, EliteSystem.RailEdge - def.shotSize - half);
        rowCentre.x = Mathf.Clamp(rowCentre.x, -edge, edge);
        fromLeft = ship.Position.x < rowCentre.x;
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        rowCentre.y -= EliteSystem.Scroll * dt;   // the row is on the board: it rides it
        int n = Mathf.Max(1, def.shotCount);
        while (t >= next && lobbed < n)
        {
            int spot = fromLeft ? lobbed : n - 1 - lobbed;
            int m = def.muzzles.Length > 0 ? lobbed % def.muzzles.Length : 0;
            Vector2 to = Spot(spot);
            var shot = Fire(m, Deg(to - ship.MuzzleWorld(m)), 0f);
            if (shot != null) shot.Lob(to, def.lobSeconds);
            lobbed++;
            next += def.shotInterval;
        }
        return t >= def.actionSeconds && lobbed >= n;
    }

    // planted no more: it crosses to its other station
    public override void End()
    {
        var w = ship.Brain as WardenBrain;
        if (w != null) w.Cross();
    }
}
