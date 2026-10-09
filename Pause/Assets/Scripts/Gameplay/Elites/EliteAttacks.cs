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
//   ward_curtain  Eventide Bastion: a curtain of slow bolts out of both
//                 wing pods onto a row of spots across the pilot's height,
//                 centred on it -- with one spot beside it left out, the
//                 gap to slip into
//   crescent_volley Orbit Reaver: races round its orbit while the claws
//                 take turns firing shards at the spot it locked, crossing
//                 there from a new angle each time
//   rift_rail     Rift Lancer: a blinking sight line at the pilot that
//                 locks, then a rail of fast bolts straight down it and a
//                 recoil; then it dashes to its other flank
//   floe_cast     Floe Harrower: a staggered row of drifting ice slabs
//                 across the board with one gap, then a lance down the gap
//   frost_bloom   Cryo Siren: a slow cryo orb that bursts into a ring of
//                 shards; every third attack a telegraphed beam sweep
//   drone_deploy  Glacier Tender: tethered ice drones; shielded while two live
//   armour_shatter Whiteout Sentinel: ice plates soak hits and spray shards;
//                 stripped, it charges
// (the Frost four: FrostElites.cs)
//   gravity_sling Singularity Hauler: flings shots sideways out of both tow
//                 claws that its core's gravity whips round, in curves
//                 that close on a ringed well ahead of the pilot
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

    public void Bind(EliteShip s) { ship = s; def = s.Def; OnBind(); }
    protected virtual void OnBind() { }

    // ---- hooks for attacks that own things on the board (the Frost four) ----
    // Every play step, attacking or not (after the ship moved).
    public virtual void Passive(float dt) { }
    // A hit it soaks (a shield, an armour plate): no heart lost. Asked
    // before the heart goes, grace or not aside.
    public virtual bool Absorbs(EliteDamage cause, Vector3 at) => false;
    // The ship died: tidy what it left on the board (before End).
    public virtual void OnDeath() { }
    // Shows the damaged drawing even with every heart (stripped armour).
    public virtual bool ShowsDamaged => false;
    // Its action fires shots out of its muzzles (drone_deploy releases drones instead).
    public virtual bool Shoots => true;

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
    // Opens with a blink (the ship wants a safe spot to land on first).
    public virtual bool Blinks => false;
    // The wind-up was abandoned (EliteShip.BreakOff): nothing was fired.
    public virtual void Cancel() { }

    // A dash's line (DrivesMovement attacks): the way it would go and how far
    // the ship checks it before committing -- to the pilot and a little past.
    // `locked`: the wind-up is over, the aim is the one it locked.
    public virtual void DashLine(Vector2 seen, bool locked, out Vector2 d, out float reach)
    {
        Vector2 to = (locked ? aim : seen) - ship.Position;
        reach = to.magnitude + DashPast;
        d = locked && dir.sqrMagnitude > 1e-4f ? dir : to.sqrMagnitude > 1e-4f ? to.normalized : Vector2.down;
    }

    public const float DashPast = .4f;

    // Would its shots, fired now, cross another elite? (Checked when it wants
    // to attack and again as the wind-up ends; it holds fire if so.)
    public virtual bool FriendlyInLine(Vector2 seen) => false;

    // Another elite in play inside the corridor `halfWidth` either side of
    // the line from `from` along `d` (unit) for `reach`.
    protected bool EliteOnLine(Vector2 from, Vector2 d, float reach, float halfWidth)
    {
        var live = EliteShip.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var e = live[i];
            if (e == null || e == ship || !e.InPlay) continue;
            Vector2 rel = e.Position - from;
            float along = Vector2.Dot(rel, d);
            if (along < 0f || along > reach) continue;
            float off = Mathf.Abs(rel.x * d.y - rel.y * d.x);
            if (off < halfWidth + e.Def.hullRadius + .15f) return true;
        }
        return false;
    }

    protected static Vector2 Unit(float deg)
    {
        float r = deg * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
    }

    protected EliteShot Fire(int muzzle, float deg, float speed)
    {
        Vector2 at = ship.MuzzleWorld(muzzle);
        float r = deg * Mathf.Deg2Rad;
        Fired++;
        ship.OnFired(muzzle, deg);
        return EliteSystem.Shots.Fire(ship, def, EliteShots.KindOf(def.shotKind), at, new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * speed);
    }

    // A shot of a given kind (not the def's shotKind): slabs, orbs.
    protected EliteShot Fire(int muzzle, float deg, float speed, EliteShots.Kind kind)
    {
        Vector2 at = ship.MuzzleWorld(muzzle);
        float r = deg * Mathf.Deg2Rad;
        Fired++;
        ship.OnFired(muzzle, deg);
        return EliteSystem.Shots.Fire(ship, def, kind, at, new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * speed);
    }

    protected static float Deg(Vector2 v) => Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
}

public static class EliteAttacks
{
    public static readonly string[] Ids = { "lance_dash", "broadside", "claw_dive", "slag_drop", "blink_shards", "siege_cannon", "ice_ram", "resin_mortar",
                                            "ward_curtain", "crescent_volley", "rift_rail", "gravity_sling",
                                            "floe_cast", "frost_bloom", "drone_deploy", "armour_shatter" };

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
            case "ward_curtain": return new WardCurtainAttack();
            case "crescent_volley": return new CrescentVolleyAttack();
            case "rift_rail": return new RiftRailAttack();
            case "gravity_sling": return new GravitySlingAttack();
            case "floe_cast": return new FloeCastAttack();
            case "frost_bloom": return new FrostBloomAttack();
            case "drone_deploy": return new DroneDeployAttack();
            case "armour_shatter": return new ArmourShatterAttack();
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

    public override bool FriendlyInLine(Vector2 seen)
    {
        for (int m = 0; m < def.muzzles.Length; m++)
            if (EliteOnLine(ship.MuzzleWorld(m), Unit(ship.MuzzleDeg(m)), 6f, def.shotSize * .5f)) return true;
        return false;
    }

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

    // (it dives at the spot it locked, from wherever it has circled to)
    public override void DashLine(Vector2 seen, bool locked, out Vector2 d, out float reach)
    {
        Vector2 to = (locked ? aim : seen) - ship.Position;
        reach = to.magnitude + DashPast;
        d = to.sqrMagnitude > 1e-4f ? to.normalized : Vector2.down;
    }

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

    public override bool FriendlyInLine(Vector2 seen) =>
        EliteOnLine(ship.Position, Vector2.down, 3.5f, def.shotSize * .5f + .1f);

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
    public override bool Blinks => true;
    public override float? FaceDeg => Deg(ship.Seen - ship.Position);

    public override bool FriendlyInLine(Vector2 seen)
    {
        Vector2 to = seen - ship.Position;
        float dist = to.magnitude;
        return dist > 1e-3f && EliteOnLine(ship.Position, to / dist, dist + 2f, .35f);
    }

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

    public override bool FriendlyInLine(Vector2 seen)
    {
        // (its lane: the muzzle swings under the hull as it turns nose-down for the shot)
        Vector2 p = ship.Position;
        return EliteOnLine(p, Vector2.down, p.y - EliteSystem.ViewBottom, def.shotSize * .4f);
    }

    public override void Cancel() { ship.ShowSight(Vector2.zero, 0f, 0f, false); }

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

    // (straight down its lane, as far as the pilot's height and a little past)
    public override void DashLine(Vector2 seen, bool locked, out Vector2 d, out float reach)
    {
        d = Vector2.down;
        reach = Mathf.Max(1f, ship.Position.y - seen.y + DashPast);
    }
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

// Eventide Bastion: a shield curtain. The tell holds it still while its wing
// pods charge; the action fires shotCount slow bolts, alternating pods,
// shotInterval apart, each aimed at its own spot on a row lobSpacing apart
// across the pilot's height, centred on where it sees the pilot -- except
// the spot beside the pilot on the side toward the middle of the board,
// which is left out: the gap to slip into. Standing still is a hit; one
// short sidestep (or the open board past the curtain's ends) is safe.
public class WardCurtainAttack : EliteAttack
{
    float next;
    int slot, gap;
    Vector2 row;
    public WardCurtainAttack() { Id = "ward_curtain"; }
    public override bool HoldsDuringTell => true;
    public int Gap => gap;
    public Vector2 Row => row;
    public int Count => Mathf.Max(3, def.shotCount);
    public int Centre => Count / 2;

    public Vector2 Spot(int i) => new Vector2(row.x + (i - (Count - 1) * .5f) * def.lobSpacing, row.y);

    public override bool FriendlyInLine(Vector2 seen)
    {
        Vector2 to = seen - ship.Position;
        float dist = to.magnitude;
        return dist > 1e-3f && EliteOnLine(ship.Position, to / dist, dist + 1f, Count * def.lobSpacing * .5f);
    }

    public override void BeginAction()
    {
        base.BeginAction();
        next = 0f;
        slot = 0;
        // the row: across the pilot's height, the centre spot on the pilot
        // (shifted so that spot lands exactly on it when the count is even)
        row = ship.Seen + new Vector2(((Count - 1) * .5f - Centre) * def.lobSpacing, 0f);
        gap = Centre + (ship.Seen.x > 0f ? -1 : 1);
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        int n = Count;
        while (t >= next && slot < n)
        {
            if (slot != gap)
            {
                int m = def.muzzles.Length > 0 ? slot % def.muzzles.Length : 0;
                Fire(m, Deg(Spot(slot) - ship.MuzzleWorld(m)), def.shotSpeed);
            }
            slot++;
            next += def.shotInterval;
        }
        return t >= def.actionSeconds && slot >= n;
    }
}

// Orbit Reaver: a crescent volley. The tell locks the spot where it sees
// the pilot (no hold: it keeps circling, slower); the action races it round
// its orbit (ReaverBrain.ActScale) while the claws take turns, shotInterval
// apart, firing shards at that locked spot -- from a new angle each time,
// so they cross on where the pilot WAS. Keep moving. Then it turns round.
public class CrescentVolleyAttack : EliteAttack
{
    float next;
    int fired;
    public CrescentVolleyAttack() { Id = "crescent_volley"; }
    public int Volley => fired;

    public override bool FriendlyInLine(Vector2 seen)
    {
        Vector2 to = seen - ship.Position;
        float dist = to.magnitude;
        return dist > 1e-3f && EliteOnLine(ship.Position, to / dist, dist + 1f, .3f);
    }

    public override void BeginAction()
    {
        base.BeginAction();
        next = 0f;
        fired = 0;
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        int n = Mathf.Max(1, def.shotCount);
        while (t >= next && fired < n)
        {
            int m = def.muzzles.Length > 0 ? fired % def.muzzles.Length : 0;
            Fire(m, Deg(aim - ship.MuzzleWorld(m)), def.shotSpeed);
            fired++;
            next += def.shotInterval;
        }
        return t >= def.actionSeconds && fired >= n;
    }

    public override void End()
    {
        var r = ship.Brain as ReaverBrain;
        if (r != null) r.Turn(true);
    }
}

// Rift Lancer: a rift rail. The tell plants it nose-on to the pilot and
// draws a blinking sight line from its prong along its aim, tracking the
// pilot for the first TrackShare of the wind-up, then locked (blinking
// faster); the action fires shotCount fast bolts one after another,
// shotInterval apart, straight down that locked line -- a rail of light --
// and the first one kicks it back up the line. Then it dashes across to
// its other flank (LancerBrain.Cross).
public class RiftRailAttack : EliteAttack
{
    public const float TrackShare = .6f, SightLength = 9f, Recoil = 1.8f;
    float next;
    int fired;
    public RiftRailAttack() { Id = "rift_rail"; }
    public override bool HoldsDuringTell => true;
    public override float? FaceDeg => Deg(dir);
    public bool Locked => t >= TellSeconds * TrackShare;

    public override bool FriendlyInLine(Vector2 seen)
    {
        Vector2 to = seen - ship.Position;
        float dist = to.magnitude;
        return dist > 1e-3f && EliteOnLine(ship.Position, to / dist, SightLength, def.shotSize * .5f);
    }

    public override void Cancel() { ship.ShowSight(Vector2.zero, 0f, 0f, false); }

    public override void StepTell(float dt)
    {
        t += dt;
        if (!Locked)
        {
            Vector2 d = ship.Seen - ship.Position;
            if (d.sqrMagnitude > 1e-4f) { dir = d.normalized; aim = ship.Seen; }
        }
        float period = Locked ? 2f : 4f;
        bool on = Mathf.FloorToInt(t / (period * EliteArt.Tick)) % 2 == 0;
        ship.ShowSight(ship.MuzzleWorld(0), Deg(dir), SightLength, on);
    }

    public override void BeginAction()
    {
        base.BeginAction();
        next = 0f;
        fired = 0;
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        int n = Mathf.Max(1, def.shotCount);
        while (t >= next && fired < n)
        {
            Fire(0, Deg(dir), def.shotSpeed);
            if (fired == 0) ship.Drive(-dir * Recoil);
            fired++;
            next += def.shotInterval;
        }
        return t >= def.actionSeconds && fired >= n;
    }

    public override void End()
    {
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        var l = ship.Brain as LancerBrain;
        if (l != null) l.Cross();
    }
}

// Singularity Hauler: a gravity sling. The tell holds it while the core
// charges; the action marks a well -- lobAhead in front of where it sees
// the pilot (led by the scroll over lobSeconds), inside the rails, riding
// the board -- and flings shotCount shots, alternating tow claws,
// shotInterval apart, out sideways; the core's pull whips each round a
// curve (EliteShot.Sling) that passes through the ringed well after
// lobSeconds, then flies on along it -- pincers closing on the ring from
// both sides and crossing there. Keep out of the ring (and off the line
// past it). Then the tug swaps sides.
public class GravitySlingAttack : EliteAttack
{
    public const float Bulge = 1.1f;    // how far out past its claw a shot swings
    float next;
    int fired;
    Vector2 well;
    public GravitySlingAttack() { Id = "gravity_sling"; }
    public override bool HoldsDuringTell => true;
    public Vector2 Well => well;

    public override bool FriendlyInLine(Vector2 seen)
    {
        Vector2 to = seen + Vector2.up * def.lobAhead - ship.Position;
        float dist = to.magnitude;
        return dist > 1e-3f && EliteOnLine(ship.Position, to / dist, dist, .6f);
    }

    public override void BeginAction()
    {
        base.BeginAction();
        next = 0f;
        fired = 0;
        well = ship.Seen + Vector2.up * (def.lobAhead + EliteSystem.Scroll * def.lobSeconds);
        float edge = Mathf.Max(0f, EliteSystem.RailEdge - def.shotSize - .3f);
        well.x = Mathf.Clamp(well.x, -edge, edge);
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        well.y -= EliteSystem.Scroll * dt;   // the well is on the board: it rides it
        int n = Mathf.Max(1, def.shotCount);
        while (t >= next && fired < n)
        {
            int m = def.muzzles.Length > 0 ? fired % def.muzzles.Length : 0;
            Vector2 from = ship.MuzzleWorld(m);
            float side = from.x >= ship.Position.x ? 1f : -1f;
            float edge = EliteSystem.RailEdge - def.shotSize - .25f;
            Vector2 bend = new Vector2(Mathf.Clamp(from.x + side * Bulge, -edge, edge), Mathf.Lerp(from.y, well.y, .3f));
            var shot = Fire(m, Deg(bend - from), def.shotSpeed);
            if (shot != null) shot.Sling(bend, well, def.lobSeconds);
            fired++;
            next += def.shotInterval;
        }
        return t >= def.actionSeconds && fired >= n;
    }

    public override void End()
    {
        var tug = ship.Brain as TugBrain;
        if (tug != null) tug.Swap();
    }
}
