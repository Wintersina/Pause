using UnityEngine;

// Frost's four new elites (Codex's flight strips, Art/Enemies/Elite/Frost/
// manifest_new_elites.md), each its own brain and attack. All four are
// icy industrial craft launching from Frost's ground sites, all telegraph
// before anything can hurt, and everything they put on the board rides it.
//
//   Floe Harrower     herder / floe_cast       (hangar, crawlerbay)
//   Cryo Siren        kiter / frost_bloom      (rigbay)
//   Glacier Tender    tender / drone_deploy    (crawlerbay)
//   Whiteout Sentinel ironclad / armour_shatter (padring, hatch)
//
// See docs/enemy-behaviours.md ("Frost elites") for the design.

// ---- brains ----------------------------------------------------------------------

// Floe Harrower: a patient ice barge. Holds followDistance ahead of the
// pilot and drifts -- slowly -- into the lane the pilot is heading for (its
// sideways drift, read over the last second, times Lead, at most laneOffset
// ahead of it): it cuts off the lane before the pilot gets there.
public class HerderBrain : EliteBrain
{
    public const float Lead = 1.1f, LaneShare = .7f;
    float laneX, drift;
    Vector2 last;
    bool init;
    public float LaneX => laneX;
    public float PilotDrift => drift;
    public float Want { get; private set; }

    public HerderBrain() { Id = "herder"; }

    public override void OnJoin() { init = false; }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        if (!init) { laneX = Pos.x; last = seen; drift = 0f; init = true; }
        if (dt > 0f) drift = Mathf.Lerp(drift, (seen.x - last.x) / dt, 1f - Mathf.Exp(-2.5f * dt));
        last = seen;
        float edge = EliteSystem.RailEdge - def.hullRadius - .3f;
        Want = Mathf.Clamp(seen.x + Mathf.Clamp(drift * Lead, -def.laneOffset, def.laneOffset), -edge, edge);
        laneX = Mathf.MoveTowards(laneX, Want, def.speed * LaneShare * dt);
        return new Vector2(laneX, seen.y + def.followDistance + Mathf.Sin(Clock * .8f) * .08f);
    }

    public override float SpeedScale => .85f;
    public override float MinAttackAbove => 1.6f;
    public override bool WantsAttack(Vector2 seen) => Pos.y > seen.y + MinAttackAbove && Mathf.Abs(Pos.x - seen.x) < def.laneOffset + .6f;
    public override Vector2 JoinFrom => Vector2.up;
}

// Cryo Siren: a fragile kiter. Keeps keepDistance from the pilot on a
// bearing within Arc of straight above it, a nervous little jitter on top;
// crowded (closer than Crowded x that) it backpedals in stutter steps -- a
// burst straight away, a pause, a burst -- until it is back at 95% of its
// range, and holds fire meanwhile.
// Its avoidance is the highest of any elite: it flees what comes at it.
public class KiterBrain : EliteBrain
{
    public const float Arc = 40f, Crowded = .75f, Stutter = .44f, Burst = 1.5f, Pause = .35f;
    bool backing;
    float range;
    public bool Backing => backing;
    // The range it keeps: keepDistance, or less when the player's reach caps how high it may
    // hold (HostileReach: a pilot parked at the top) -- it never flees out of reach for good.
    public float Range => range > 0f ? range : def.keepDistance;
    public int Backpedals { get; private set; }

    public KiterBrain() { Id = "kiter"; }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        Vector2 d = Pos - seen;
        float dist = d.magnitude;
        d = dist > 1e-3f ? d / dist : Vector2.up;
        float bearing = Mathf.Clamp(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, 90f - Arc, 90f + Arc) * Mathf.Deg2Rad;
        float room = ship.ReachCap - seen.y;
        // (the cap only limits its height: on the slant of its arc it can still keep most of its range)
        range = Mathf.Clamp(room / Mathf.Sin((90f - Arc) * Mathf.Deg2Rad), MinAttackAbove + .4f, def.keepDistance);
        bool was = backing;
        backing = dist < range * (was ? .95f : Crowded);
        if (backing && !was) Backpedals++;
        if (backing)
        {
            // the stutter step: a burst straight away from the pilot, then a pause where it is
            if (Mathf.Repeat(Clock, Stutter) >= Stutter * .5f) return Pos;
            return Pos + new Vector2(Mathf.Cos(bearing), Mathf.Sin(bearing)) * 1.6f;
        }
        return seen + new Vector2(Mathf.Cos(bearing), Mathf.Sin(bearing)) * range + new Vector2(Mathf.Sin(Clock * 4.7f) * .12f, Mathf.Sin(Clock * 3.1f) * .06f);
    }

    // the stutter step: a burst, then a pause
    public override float SpeedScale => backing ? (Mathf.Repeat(Clock, Stutter) < Stutter * .5f ? Burst : Pause) : 1f;
    public override float MinAttackAbove => 1.2f;

    public override bool WantsAttack(Vector2 seen)
    {
        float d = (seen - Pos).magnitude;
        return !backing && Pos.y > seen.y + MinAttackAbove && d > Range * .7f && d < Range * 1.5f;
    }

    public override Vector2 JoinFrom => new Vector2(.5f, 1f);
}

// Glacier Tender: a slow support tug. Hovers topMargin under the top of the
// view, hanging back: its lane drifts at a crawl toward halfway between the
// pilot's and the middle. When the pilot comes within keepDistance it flees
// -- sideways, away, fast -- until the pilot is 1.4x that away again.
// Attacks (releases drones) only while it has room for more.
public class TenderBrain : EliteBrain
{
    public const float FleeScale = 1.7f, Release = 1.4f;
    bool fleeing, init;
    float trackX, fleeSide = 1f;
    public bool Fleeing => fleeing;
    public int Flights { get; private set; }

    public TenderBrain() { Id = "tender"; }

    public float Height => ship.ReachY(EliteSystem.ViewTop - def.topMargin);

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Clock += dt;
        float edge = EliteSystem.RailEdge - def.hullRadius - .4f;
        if (!init) { trackX = Pos.x; init = true; }
        float dist = (Pos - seen).magnitude;
        bool was = fleeing;
        fleeing = dist < def.keepDistance * (was ? Release : 1f);
        if (fleeing && !was)
        {
            Flights++;
            // away from the pilot, mostly sideways (the top of the view is close); a rail that way: the other
            fleeSide = Pos.x >= seen.x ? 1f : -1f;
            if (edge - fleeSide * Pos.x < .8f) fleeSide = -fleeSide;
        }
        if (fleeing)
        {
            trackX = Mathf.Clamp(Pos.x + fleeSide * 1.6f, -edge, edge);
            return new Vector2(trackX, Mathf.Max(Pos.y, seen.y + def.keepDistance) + .4f);
        }
        trackX = Mathf.MoveTowards(trackX, Mathf.Clamp(seen.x * .5f, -edge, edge), def.speed * .35f * dt);
        return new Vector2(trackX, Height + Mathf.Sin(Clock * .9f) * .1f);
    }

    public override float SpeedScale => fleeing ? FleeScale : .7f;
    public override float MinAttackAbove => 1.8f;

    public override bool WantsAttack(Vector2 seen)
    {
        var dd = ship.Attack as DroneDeployAttack;
        return !fleeing && Pos.y > seen.y + MinAttackAbove && (dd == null || dd.Live < dd.Cap);
    }

    public override Vector2 JoinFrom => Vector2.up;
}

// Whiteout Sentinel: a slow ironclad. Flies straight legs -- every
// LegSeconds it fixes a point followDistance above where it sees the pilot
// and goes there in a straight line -- at a steady, slow pace, never
// dodging anything (Steadfast), its prow (and its plates) turned on the
// pilot. After an enraged charge it limps (RecoverScale) a while.
public class IroncladBrain : EliteBrain
{
    public const float LegSeconds = 1.6f, Pace = .6f, RecoverScale = .3f;
    Vector2 leg;
    float legLeft;
    public Vector2 Leg => leg;
    public int Legs { get; private set; }

    public IroncladBrain() { Id = "ironclad"; }

    public override void OnJoin() { legLeft = 0f; }

    public override Vector2 Goal(Vector2 seen, float dt)
    {
        legLeft -= dt;
        if (legLeft <= 0f)
        {
            leg = seen + Vector2.up * def.followDistance;
            legLeft = LegSeconds;
            Legs++;
        }
        return leg;
    }

    public override bool Steadfast => true;
    public override float SpeedScale
    {
        get
        {
            var a = ship != null ? ship.Attack as ArmourShatterAttack : null;
            return a != null && a.Recovering ? RecoverScale : Pace;
        }
    }
    public override float MinAttackAbove => .8f;
    public override bool WantsAttack(Vector2 seen) => Pos.y > seen.y + MinAttackAbove && (seen - Pos).magnitude < 4f;
    public override float? FaceDeg(Vector2 seen) => FaceTowards(seen);
    public override Vector2 JoinFrom => Vector2.up;
}

// ---- attacks ---------------------------------------------------------------------

// Floe Harrower: floe cast. The tell plants it while its slab chutes charge;
// the action casts shotCount ice slabs (EliteShots.Kind.Slab) one after
// another, SlabGap apart, out of alternate chutes, each gliding out
// (hazardSeconds) onto its own spot in a staggered row across the whole
// board, led by the scroll so it is lobAhead in front of the pilot when the
// lance comes -- shotCount + 1
// slots between the rails, one left open: the gap, the slot beside the
// pilot's toward the middle. The slabs ride the board, drifting hazardSpeed
// sideways together, block shots (hazardArmour hits) and hurt on touch.
// The gap is chosen at the tell, and from SightFrom of it a sight line
// blinks from its keel down the gap's lane to the pilot's height; right
// after the last slab (LanceDelay) one fast lance (a bolt) goes down it:
// keep out of the gap's lane until the lance has passed, then slip into it
// before the row arrives (under a second at the top scroll speeds).
public class FloeCastAttack : EliteAttack
{
    public const float SightWidth = .18f, SlabGap = .06f, Stagger = .2f, SightFrom = .4f, LanceDelay = .15f, SightLength = 9f;
    public const int Keel = 2;   // muzzle: the lance; 0 / 1: the chutes
    readonly EliteShot[] slabs = new EliteShot[6];
    float next, slot, sideways, settleAt;
    int cast, slotsDone, gap;
    Vector2 row;
    bool lanced;

    public FloeCastAttack() { Id = "floe_cast"; }
    public override bool HoldsDuringTell => true;
    public int Count => Mathf.Clamp(def.shotCount, 3, 5);
    public int Gap => gap;
    public Vector2 Row => row;
    public float SlotWidth => slot;
    public bool Lanced => lanced;
    // (the lance comes right after the last slab: at the top scroll speeds the row is on the pilot within a second)
    public float LanceAt => (Count - 1) * SlabGap + LanceDelay;
    public float SettleAt => settleAt;
    public int Cast => cast;
    public EliteShot Slab(int i) => slabs[i];

    // Spot i of the row now (the row rides the board, the slabs drift with it).
    public Vector2 Spot(int i)
    {
        float edge = EliteSystem.RailEdge;
        return new Vector2(-edge + slot * (i + .5f), row.y + (i % 2 == 0 ? Stagger : -Stagger));
    }

    // Where the gap is now: its spot, drifted with the slabs since they settled.
    public Vector2 GapNow => Spot(gap) + Vector2.right * sideways * Mathf.Max(0f, t - settleAt);

    // Where the lance goes: down the gap's lane, at the pilot's height (wherever the row is).
    public Vector2 LanceAim => new Vector2(GapNow.x, Mathf.Min(ship.Seen.y, ship.Position.y - 1f));

    public override bool FriendlyInLine(Vector2 seen) =>
        EliteOnLine(ship.Position, Vector2.down, Mathf.Max(1f, ship.Position.y - seen.y), 1f);

    public override void Cancel() { ship.ShowSight(Vector2.zero, 0f, 0f, false); }

    // The plan is made at the tell (the gap: the slot beside the pilot's, toward the middle) ...
    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        float edge = EliteSystem.RailEdge;
        int n = Count;
        slot = 2f * edge / (n + 1);
        row = new Vector2(0f, seen.y + def.lobAhead);
        sideways = 0f;
        settleAt = 0f;
        int mine = Mathf.Clamp(Mathf.FloorToInt((seen.x + edge) / slot), 0, n);
        gap = Mathf.Clamp(mine + (seen.x > 0f ? -1 : 1), 0, n);
        if (gap == mine) gap = mine == 0 ? 1 : mine - 1;
    }

    // ... and shown: from SightFrom of the wind-up a sight blinks down the gap's lane
    public override void StepTell(float dt)
    {
        t += dt;
        if (t < TellSeconds * SightFrom) return;
        Vector2 keel = ship.MuzzleWorld(Keel);
        bool on = Mathf.FloorToInt(t / (4f * EliteArt.Tick)) % 2 == 0;
        ship.ShowSight(keel, Deg(LanceAim - keel), SightLength, on, SightWidth);
    }

    public override void BeginAction()
    {
        base.BeginAction();
        next = 0f;
        cast = 0;
        slotsDone = 0;
        lanced = false;
        for (int i = 0; i < slabs.Length; i++) slabs[i] = null;
        int n = Count;
        settleAt = (n - 1) * SlabGap + def.hazardSeconds;
        // the row: lobAhead in front of the pilot once the slabs are out (it rides the board meanwhile)
        row.y = Mathf.Min(ship.Seen.y + def.lobAhead + EliteSystem.Scroll * settleAt, EliteSystem.ViewTop - .6f);
        // they all drift the same way: toward the middle from the gap's side
        sideways = (Spot(gap).x > 0f ? -1f : 1f) * def.hazardSpeed;
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        row.y -= EliteSystem.Scroll * dt;   // the row is on the board: it rides it
        int n = Count;
        while (t >= next && slotsDone <= n)
        {
            if (slotsDone != gap)
            {
                int m = cast % 2;
                Vector2 to = Spot(slotsDone);
                var s = Fire(m, Deg(to - ship.MuzzleWorld(m)), 0f, EliteShots.Kind.Slab);
                if (s != null) { s.Glide(to, def.hazardSeconds, sideways); slabs[cast] = s; }
                cast++;
                next += SlabGap;
            }
            slotsDone++;
        }
        Vector2 keel = ship.MuzzleWorld(Keel);
        float deg = Deg(LanceAim - keel);
        if (!lanced)
        {
            // the sight down the gap's lane, blinking fast now: the lance is coming
            bool on = Mathf.FloorToInt(t / (2f * EliteArt.Tick)) % 2 == 0;
            ship.ShowSight(keel, deg, SightLength, on, SightWidth);
        }
        if (!lanced && t >= LanceAt)
        {
            lanced = true;
            ship.ShowSight(Vector2.zero, 0f, 0f, false);
            Fire(Keel, deg, def.shotSpeed);
        }
        return lanced && t >= Mathf.Max(def.actionSeconds, LanceAt + .1f);
    }

    public override void End() { ship.ShowSight(Vector2.zero, 0f, 0f, false); }

    // Its slabs break up with it.
    public override void OnDeath()
    {
        for (int i = 0; i < slabs.Length; i++)
        {
            var s = slabs[i];
            slabs[i] = null;
            if (s == null || !s.Active || s.Owner != ship || s.Kind != EliteShots.Kind.Slab) continue;
            s.Struck();   // (until it breaks)
            if (s.Active) { EliteSystem.Fx.Sparks(s.transform.position, def.ShotColor, 6); s.Recycle(); }
        }
    }
}

// Cryo Siren: frost bloom. The tell charges the dish; the action sends one
// slow cryo orb (EliteShots.Kind.Orb, shotSpeed x OrbShare) at the pilot,
// its fuse ring blinking; hazardSeconds later it bursts into a ring of
// hazardCount shards (hazardSpeed). Shoot the orb first and it just pops.
// Every BeamEvery-th attack is a beam sweep instead: a longer tell
// (BeamTell) in which a sight line paints the arc it will sweep -- BeamArc
// either side of the pilot -- then locks on its start; the action sweeps a
// stream of fast bolts (shotSpeed) across that arc. Get out of the arc.
public class FrostBloomAttack : EliteAttack
{
    public const int BeamEvery = 3;
    public const float SightWidth = .2f, OrbShare = .3f, BeamArc = 16f, BeamTell = 1.6f, BeamInterval = .035f, BeamLength = 9f, LockShare = .7f;
    bool beam;
    int casts, beamShots;
    float a0, a1, next;
    public bool Beam => beam;
    public float BeamFrom => a0;
    public float BeamTo => a1;
    public int BeamShots => beamShots;
    public EliteShot Orb { get; private set; }

    public FrostBloomAttack() { Id = "frost_bloom"; }

    public override float TellSeconds => beam ? def.tellSeconds * BeamTell : def.tellSeconds;
    public override bool HoldsDuringTell => beam;
    public bool Locked => beam && t >= TellSeconds * LockShare;

    public override bool FriendlyInLine(Vector2 seen)
    {
        Vector2 to = seen - ship.Position;
        float dist = to.magnitude;
        return dist > 1e-3f && EliteOnLine(ship.Position, to / dist, dist + 1f, .45f);
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        casts++;
        beam = casts % BeamEvery == 0;
        float c = Deg(seen - ship.MuzzleWorld(0));
        float s = seen.x >= ship.Position.x ? 1f : -1f;
        // sweeps from past the pilot's outer side back across it
        a0 = c + s * BeamArc;
        a1 = c - s * BeamArc;
    }

    // Forces the next attack to be a beam (tests, previews).
    public void NextIsBeam() { casts = (casts / BeamEvery + 1) * BeamEvery - 1; }

    public override void Cancel() { ship.ShowSight(Vector2.zero, 0f, 0f, false); }

    public override void StepTell(float dt)
    {
        t += dt;
        if (!beam) return;
        // the arc painted: the sight swings across it, then holds on the start, blinking faster
        float deg = a0;
        if (!Locked)
        {
            float k = Mathf.PingPong(t * 2f / (TellSeconds * LockShare), 1f);
            deg = Mathf.Lerp(a0, a1, k);
        }
        // (painting: steady; locked: blinking fast)
        bool on = !Locked || Mathf.FloorToInt(t / (2f * EliteArt.Tick)) % 2 == 0;
        ship.ShowSight(ship.MuzzleWorld(0), deg, BeamLength, on, SightWidth);
    }

    public override void BeginAction()
    {
        base.BeginAction();
        next = 0f;
        beamShots = 0;
        if (beam) return;
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        var orb = Fire(0, Deg(ship.Seen - ship.MuzzleWorld(0)), def.shotSpeed * OrbShare, EliteShots.Kind.Orb);
        if (orb != null) orb.Fuse(def.hazardSeconds);
        Orb = orb;
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        if (!beam) return t >= def.actionSeconds;
        float k = Mathf.Clamp01(t / def.actionSeconds);
        float deg = Mathf.Lerp(a0, a1, k);
        ship.ShowSight(ship.MuzzleWorld(0), deg, BeamLength, true, SightWidth);
        while (t >= next && next <= def.actionSeconds)
        {
            Fire(0, Mathf.Lerp(a0, a1, Mathf.Clamp01(next / def.actionSeconds)), def.shotSpeed);
            beamShots++;
            next += BeamInterval;
        }
        return t >= def.actionSeconds;
    }

    public override void End() { ship.ShowSight(Vector2.zero, 0f, 0f, false); }

    // An orb still on its fuse dies with it (pops, no ring).
    public override void OnDeath()
    {
        var o = Orb;
        Orb = null;
        if (o != null && o.Active && o.Owner == ship && o.Kind == EliteShots.Kind.Orb) o.Struck();
    }
}

// Glacier Tender: drone deploy. The tell charges the drone pods; the action
// releases 2-3 ice drones (Frost's Flake fighter, hazardSize scale, its own
// movers off: EliteDrone) out of the pods, never more than Cap (hazardCount)
// alive, each on a glowing cyan tether. They fan out lobAhead below it
// (FanDeg apart) and sway there at hazardSpeed -- never lower than
// DroneFloor above the pilot -- a wall of targets and hazards. While two or
// more live, the Tender is shield-linked (a blinking cyan ring): no hit but
// a pause jump, a shielded ram or a rail costs it a heart. Kill the drones
// first. Killing the Tender scuttles every drone it still has.
public class DroneDeployAttack : EliteAttack
{
    public const string DroneKey = "frost_fighter_1";
    public const float FanDeg = 38f, DroneFloor = 1.1f, BlockFlash = .15f;
    public const int Link = 2;
    readonly GameObject[] drones = new GameObject[4];
    readonly int[] pod = new int[4];
    SpriteRenderer[] tethers;
    SpriteRenderer shield;
    float clock, flash;

    public DroneDeployAttack() { Id = "drone_deploy"; }
    public override bool Shoots => false;
    public int Cap => Mathf.Clamp(def.hazardCount, Link, drones.Length);
    public int Released { get; private set; }
    public int Blocked { get; private set; }
    public bool ShieldShown => shield != null && shield.enabled;
    public bool TetherShown(int i) => tethers != null && i < tethers.Length && tethers[i].enabled;

    public int Live
    {
        get
        {
            int n = 0;
            for (int i = 0; i < drones.Length; i++) if (Alive(drones[i])) n++;
            return n;
        }
    }

    public bool Linked => Live >= Link;
    public GameObject Drone(int i) => i >= 0 && i < drones.Length && Alive(drones[i]) ? drones[i] : null;

    static bool Alive(GameObject go) => go != null && go.activeInHierarchy;

    public override bool FriendlyInLine(Vector2 seen) =>
        EliteOnLine(ship.Position, Vector2.down, def.lobAhead + .5f, def.lobAhead * .7f);

    public override void BeginAction()
    {
        base.BeginAction();
        int want = Random.Range(2, 4);
        int pods = Mathf.Max(1, def.muzzles.Length);
        for (int i = 0; i < Cap && want > 0; i++)
        {
            if (Alive(drones[i])) continue;
            int m = Released % pods;
            var go = SpawnDrone(ship.MuzzleWorld(m), i);
            if (go == null) break;
            drones[i] = go;
            pod[i] = m;
            ship.OnFired(m, 270f);
            Fired++;
            Released++;
            want--;
        }
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        return t >= def.actionSeconds;
    }

    GameObject SpawnDrone(Vector2 at, int slot)
    {
        var d = EnemyRoster.Find(DroneKey);
        if (d == null) return null;
        var go = EnemyFactory.Create(d, new Vector3(at.x, at.y, 0f), Quaternion.identity);
        if (go == null) return null;
        go.transform.SetParent(EliteSystem.Root, true);   // (gone with the elites on a clear)
        go.transform.localScale *= Mathf.Max(.2f, def.hazardSize);
        // the Tender flies it on its tether: its own movers off
        var mover = go.GetComponent<moveItemEnmInStrightLine>();
        if (mover != null) mover.enabled = false;
        var brain = go.GetComponent<EnemyBrain>();
        if (brain != null) brain.enabled = false;
        var drone = go.AddComponent<EliteDrone>();
        drone.mother = ship;
        drone.slot = slot;
        SpawnFootprint.Bind(go, drone);
        var ct = ClearTarget.Ensure(go);
        if (ct != null) ct.Mother = ship;
        return go;
    }

    // Where drone slot i hangs: fanned out below the Tender, swaying.
    public Vector2 SlotSpot(int i)
    {
        float deg = -90f + (i - (Cap - 1) * .5f) * FanDeg;
        Vector2 p = ship.Position + Unit(deg) * def.lobAhead + new Vector2(Mathf.Sin(clock * 1.9f + i * 2.1f) * .18f, Mathf.Sin(clock * 1.3f + i) * .08f);
        float edge = EliteSystem.RailEdge - .35f;
        p.x = Mathf.Clamp(p.x, -edge, edge);
        p.y = Mathf.Max(p.y, ship.Seen.y + DroneFloor);
        return p;
    }

    public override void Passive(float dt)
    {
        clock += dt;
        if (flash > 0f) flash -= dt;
        if (tethers == null) Build();
        for (int i = 0; i < drones.Length; i++)
        {
            var go = drones[i];
            if (!Alive(go))
            {
                drones[i] = null;
                if (i < tethers.Length) tethers[i].enabled = false;
                continue;
            }
            Vector2 p = go.transform.position;
            p = Vector2.MoveTowards(p, SlotSpot(i), Mathf.Max(.5f, def.hazardSpeed) * dt);
            go.transform.position = new Vector3(p.x, p.y, 0f);
            // the tether: pod to drone, glowing in hard steps
            var line = tethers[i];
            Vector2 from = ship.MuzzleWorld(pod[i]);
            Vector2 to = p - from;
            line.enabled = true;
            line.transform.position = new Vector3(from.x, from.y, 0f);
            line.transform.rotation = Quaternion.Euler(0f, 0f, Deg(to) - 90f);
            line.transform.localScale = new Vector3(.08f, to.magnitude, 1f);
            Color c = def.ShotColor;
            c.a = Mathf.FloorToInt((clock + i * .1f) / (5f * EliteArt.Tick)) % 2 == 0 ? .85f : .5f;
            line.color = c;
        }
        bool linked = Linked;
        shield.enabled = linked;
        if (linked)
        {
            Color c = flash > 0f ? def.ShotCore : def.ShotColor;
            c.a = flash > 0f ? 1f : Mathf.FloorToInt(clock / (6f * EliteArt.Tick)) % 2 == 0 ? .75f : .45f;
            shield.color = c;
        }
    }

    void Build()
    {
        tethers = new SpriteRenderer[drones.Length];
        for (int i = 0; i < tethers.Length; i++)
        {
            var go = new GameObject("Tether" + i);
            go.transform.SetParent(ship.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = EliteFxArt.Sight;
            sr.sortingOrder = EliteShip.PlayOrder - 1;
            sr.enabled = false;
            tethers[i] = sr;
        }
        var sg = new GameObject("ShieldLink");
        sg.transform.SetParent(ship.transform, false);
        shield = sg.AddComponent<SpriteRenderer>();
        shield.sprite = EliteFxArt.Ring;
        shield.sortingOrder = EliteShip.PlayOrder + 3;
        sg.transform.localScale = Vector3.one * def.hullRadius * 2.9f / Mathf.Max(.01f, EliteFxArt.Ring.bounds.size.x);
        shield.enabled = false;
    }

    // The shield link: two drones up, only a pause jump, a shielded ram or a rail gets through.
    public override bool Absorbs(EliteDamage cause, Vector3 at)
    {
        if (!Linked || cause == EliteDamage.Teleport || cause == EliteDamage.ShieldRam || cause == EliteDamage.Domino || cause == EliteDamage.Rail) return false;
        Blocked++;
        flash = BlockFlash;
        EliteSystem.Fx.Sparks(at, def.ShotCore, 6);
        return true;
    }

    // Its drones go down with it (a blast each, nothing paid).
    public override void OnDeath()
    {
        for (int i = 0; i < drones.Length; i++)
        {
            var go = drones[i];
            drones[i] = null;
            if (Alive(go)) EliteDrone.Scuttle(go);
        }
        if (tethers != null) foreach (var l in tethers) if (l != null) l.enabled = false;
        if (shield != null) shield.enabled = false;
    }
}

// A Glacier Tender's drone: marks the Frost fighter as its mother's (it is
// flown by DroneDeployAttack.Passive) and gives SpawnSpace its footprint.
public class EliteDrone : MonoBehaviour, IMovementFootprint
{
    public EliteShip mother;
    public int slot;

    public Rect SweptBounds(Vector2 center, Vector2 half, float from, float to)
    {
        float own = 3f * to;
        return Rect.MinMaxRect(center.x - half.x - own, center.y - half.y - own, center.x + half.x + own, center.y + half.y + own);
    }

    public bool SelfSteering => true;

    // Its Tender went down: the drone goes too (a blast, nothing paid -- not a shot, a body).
    public static void Scuttle(GameObject go) { EliteShip.FriendlyKill(go); }
}

// Whiteout Sentinel: armour shatter. hazardCount ice plates (rims drawn on
// its prow, outermost first) soak every hit but a pause jump, a shielded
// ram or a rail: each costs a plate instead of a heart (then PlateGrace of
// no more plate losses). The broken plate's ice comes back at you: two
// cone edges blink from its eye for SprayDelay, then shotCount shards fan
// out along them (shotSpread apart). Its own attack while plated: a shard
// from each side emitter at the pilot. Stripped (the damaged drawing), it
// is enraged: its next attacks are charges -- a long tell planted, a
// sight line tracking the pilot then locked, a committed dash at dashSpeed
// -- each followed by RecoverSeconds of limping.
public class ArmourShatterAttack : EliteAttack
{
    public const float SprayDelay = .4f, PlateGrace = .3f, RecoverSeconds = 1.6f, ChargeTell = 1.5f, ConeLength = 2.4f, SightLength = 6f;
    int plates;
    float plateGrace, sprayIn = -1f, recover, clock;
    SpriteRenderer[] plateArt;
    SpriteRenderer coneL, coneR;
    public int Plates => plates;
    // Every plate gone at once (tests: the hearts underneath).
    public void Strip()
    {
        plates = 0;
        if (plateArt != null) foreach (var p in plateArt) p.enabled = false;
    }
    public bool Stripped => plates <= 0;
    public bool Recovering => recover > 0f;
    public int Shattered { get; private set; }
    public int Sprays { get; private set; }
    public int Charges { get; private set; }
    public bool SprayTelegraphed => sprayIn >= 0f;
    public bool PlateShown(int i) => plateArt != null && i < plateArt.Length && plateArt[i].enabled;

    public ArmourShatterAttack() { Id = "armour_shatter"; }

    protected override void OnBind() { plates = Mathf.Max(1, def.hazardCount); }

    public override bool DrivesMovement => Stripped;
    public override bool HoldsDuringTell => Stripped;
    public override float TellSeconds => Stripped ? def.tellSeconds * ChargeTell : def.tellSeconds;
    public override float? FaceDeg => Stripped ? Deg(dir) : (float?)null;
    public override bool ShowsDamaged => Stripped;

    public float ConeHalf => Mathf.Max(8f, def.shotSpread * (Mathf.Max(2, def.shotCount) - 1) * .5f);

    public override bool FriendlyInLine(Vector2 seen)
    {
        Vector2 to = seen - ship.Position;
        float dist = to.magnitude;
        return dist > 1e-3f && EliteOnLine(ship.Position, to / dist, dist + .5f, .5f);
    }

    public override void Cancel() { ship.ShowSight(Vector2.zero, 0f, 0f, false); }

    public override void StepTell(float dt)
    {
        t += dt;
        if (!Stripped) return;
        // the charge's sight: tracks the pilot for the first half, then locks, blinking faster
        bool locked = t >= TellSeconds * .5f;
        if (!locked)
        {
            Vector2 d = ship.Seen - ship.Position;
            if (d.sqrMagnitude > 1e-4f) { dir = d.normalized; aim = ship.Seen; }
        }
        bool on = Mathf.FloorToInt(t / ((locked ? 2f : 4f) * EliteArt.Tick)) % 2 == 0;
        ship.ShowSight(ship.Position, Deg(dir), SightLength, on, FrostBloomAttack.SightWidth);
    }

    public override void BeginAction()
    {
        base.BeginAction();
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        if (Stripped)
        {
            Charges++;
            ship.Drive(dir * def.dashSpeed);
            return;
        }
        // plated: a shard from each side emitter at the pilot
        for (int m = 1; m < def.muzzles.Length; m++)
            Fire(m, Deg(ship.Seen - ship.MuzzleWorld(m)), def.shotSpeed);
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        if (!Stripped) return t >= def.actionSeconds * .5f;
        bool braking = t >= def.actionSeconds * .75f;
        ship.Drive(dir * def.dashSpeed * (braking ? .35f : 1f));
        return t >= def.actionSeconds;
    }

    public override void End()
    {
        ship.ShowSight(Vector2.zero, 0f, 0f, false);
        if (Stripped && t > 0f && ship.State != EliteState.Dead) recover = RecoverSeconds;
    }

    public override bool Absorbs(EliteDamage cause, Vector3 at)
    {
        if (plates <= 0 || cause == EliteDamage.Teleport || cause == EliteDamage.ShieldRam || cause == EliteDamage.Domino || cause == EliteDamage.Rail) return false;
        if (plateGrace > 0f) return true;
        plates--;
        Shattered++;
        plateGrace = PlateGrace;
        if (sprayIn < 0f) sprayIn = SprayDelay;
        if (plateArt != null && plates < plateArt.Length)
        {
            plateArt[plates].enabled = false;
            EliteSystem.Fx.Sparks(plateArt[plates].transform.position, def.ShotCore, 10);
        }
        // stripped: enraged, it charges soon
        if (plates == 0 && ship.State == EliteState.Follow) ship.AttackCooldown = Mathf.Min(ship.AttackCooldown, .5f);
        return true;
    }

    public override void Passive(float dt)
    {
        clock += dt;
        if (plateArt == null) Build();
        if (plateGrace > 0f) plateGrace -= dt;
        if (recover > 0f) recover -= dt;
        for (int i = 0; i < plateArt.Length; i++)
        {
            if (!plateArt[i].enabled) continue;
            Color c = def.ShotCore;
            c.a = (i == plates - 1 && plateGrace > 0f) ? 1f : Mathf.FloorToInt((clock + i * .2f) / (8f * EliteArt.Tick)) % 2 == 0 ? .8f : .6f;
            plateArt[i].color = c;
        }
        if (sprayIn < 0f) return;
        sprayIn -= dt;
        Vector2 eye = ship.MuzzleWorld(0);
        float face = ship.MuzzleDeg(0);
        if (sprayIn >= 0f)
        {
            // the telegraph: the cone's two edges blink from its eye
            bool on = Mathf.FloorToInt(sprayIn / (2f * EliteArt.Tick)) % 2 == 0;
            Edge(coneL, eye, face + ConeHalf, on);
            Edge(coneR, eye, face - ConeHalf, on);
            return;
        }
        coneL.enabled = coneR.enabled = false;
        sprayIn = -1f;
        Sprays++;
        int n = Mathf.Max(2, def.shotCount);
        for (int i = 0; i < n; i++)
            Fire(0, face + (i - (n - 1) * .5f) * def.shotSpread, def.shotSpeed);
    }

    void Edge(SpriteRenderer sr, Vector2 from, float deg, bool on)
    {
        sr.enabled = on;
        if (!on) return;
        sr.transform.position = new Vector3(from.x, from.y, 0f);
        sr.transform.rotation = Quaternion.Euler(0f, 0f, deg - 90f);
        sr.transform.localScale = new Vector3(.1f, ConeLength, 1f);
        Color c = def.ShotColor;
        c.a = .8f;
        sr.color = c;
    }

    void Build()
    {
        int n = Mathf.Max(1, def.hazardCount);
        plateArt = new SpriteRenderer[n];
        // rims ahead of its prow, innermost (index 0, the last to go) to outermost
        Vector2 tip = def.muzzles.Length > 0 ? def.PixelToLocal(def.muzzles[0].x, def.muzzles[0].y - 36f) : Vector2.up * .3f;
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("Plate" + i);
            go.transform.SetParent(ship.HullTransform, false);
            go.transform.localPosition = tip + Vector2.up * (.035f + i * .07f);
            go.transform.localScale = Vector3.one * def.cellWorldSize * (.42f + i * .1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = EliteFxArt.Plate;
            sr.sortingOrder = EliteShip.PlayOrder + 1;
            sr.enabled = i < plates;
            plateArt[i] = sr;
        }
        coneL = Line("ConeL");
        coneR = Line("ConeR");
    }

    SpriteRenderer Line(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(ship.transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = EliteFxArt.Sight;
        sr.sortingOrder = EliteShip.PlayOrder - 1;
        sr.enabled = false;
        return sr;
    }

    public override void OnDeath()
    {
        sprayIn = -1f;
        if (coneL != null) coneL.enabled = coneR.enabled = false;
    }
}
