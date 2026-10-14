using UnityEngine;

// Tide's five elites (Codex's flight strips, Art/Enemies/Elite/Tide/manifest_new_elites.md), each its own brain and attack, in the
// ocean's own material (a surf dash, depth charges, nets, thunder, pearls) built from the cores already in the game (EliteTelegraph,
// the Orb fuse, the Glob lob and pool, AttackStrike, the ward curtain). Every attack draws its footprint from the first frame of its
// tell. Their shots wear the procedural skins until Codex delivers Tide's attack art (ShotSkins.Enable(4)).
//
//   Riptide      surfer   / surf_dash      (trenchhatch) a lane is drawn, then a committed ram on it; spout needles at the brake
//   Bathyscaphe  abyss    / depth_charge   (rigdeck)     fused charges fly out of the claws, each bursts into a ring of pearls
//   Trawler      netter   / net_cast       (reefdock)    nets lobbed across the pilot's row, each lands as a pool; one gap stays open
//   Thunderfin   stormray / thunder_strike (ventstack)   lightning columns on the lanes beside and on the pilot, glyph first
//   Pearl Warden pearler  / pearl_curtain  (wreckbay)    a curtain of slow pearls with one gap

// ---- brains --------------------------------------------------------------------------------------------------------

// Riptide: a lean interceptor, a touch faster than Sunstoke.
public class SurferBrain : InterceptorBrain
{
    public SurferBrain() { Id = "surfer"; }
    public override float SpeedScale => 1.1f;
}

// Bathyscaphe: a siege platform that wallows slowly side to side.
public class AbyssBrain : SiegeBrain
{
    public AbyssBrain() { Id = "abyss"; }
    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Vector2 g = base.Goal(seen, dt);
        g.x += Mathf.Sin(Clock * .5f) * .5f;
        return g;
    }
    public override float SpeedScale => .8f;
}

// Trawler: a hauler that blocks the lane, slow and wide.
public class NetterBrain : HaulerBrain
{
    public NetterBrain() { Id = "netter"; }
    public override float SpeedScale => .75f;
}

// Thunderfin: a skirmisher that keeps its range and blinks clear.
public class StormrayBrain : SkirmisherBrain
{
    public StormrayBrain() { Id = "stormray"; }
}

// Pearl Warden: a bastion platform that breathes sideways.
public class PearlerBrain : BastionBrain
{
    public PearlerBrain() { Id = "pearler"; }
    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Vector2 g = base.Goal(seen, dt);
        g.x += Mathf.Sin(Clock * .45f) * .4f;
        return g;
    }
    public override float SpeedScale => .7f;
}

// ---- attacks -------------------------------------------------------------------------------------------------------

// Riptide: the lance dash with its lane drawn. It re-aims through the first half of the tell, then locks and the dotted lane of the
// ram is shown for the rest of it; the dash is committed (bait it), spout needles leave the lance tip as it brakes.
public class SurfDashAttack : LanceDashAttack
{
    readonly EliteTelegraph telegraph = new EliteTelegraph();
    bool shown;
    public EliteTelegraph Telegraph => telegraph;
    public bool Shown => shown;
    public SurfDashAttack() { Id = "surf_dash"; }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        shown = false;
        telegraph.Clear();
    }

    public override void StepTell(float dt)
    {
        base.StepTell(dt);
        if (!shown && t >= TellSeconds * def.aimCommit)
        {
            shown = true;
            Vector2 from = ship.Position;
            float reach = def.dashSpeed * def.actionSeconds * .9f;
            telegraph.Clear();
            telegraph.Lane(from, from + dir * reach, def.hullRadius + .12f);
            telegraph.Show(Mathf.Max(.1f, TellSeconds - t), def.ShotColor);
        }
        telegraph.Step(dt);
    }

    public override void Cancel() { telegraph.End(); shown = false; }
    public override void OnDeath() { telegraph.End(); }
    public override void BeginAction() { base.BeginAction(); telegraph.GoLive(); }
    public override void End() { telegraph.End(); }
}

// Bathyscaphe: depth charges. The burst points (a little above the pilot, either side of its column) and the lane each charge flies
// are drawn from the first frame of the tell. Then shotCount fused orbs leave the claws, one after another, each flying to its own
// point and bursting there (hazardSeconds) into a ring of hazardCount pearls at hazardSpeed. Shoot a charge and it only pops.
public class DepthChargeAttack : EliteAttack
{
    public const float BurstRing = .55f;
    readonly EliteTelegraph telegraph = new EliteTelegraph();
    Vector2[] burst = new Vector2[4];
    float next;
    int sent;
    public DepthChargeAttack() { Id = "depth_charge"; }
    public override bool HoldsDuringTell => true;
    public int Sent => sent;
    public EliteTelegraph Telegraph => telegraph;
    public Vector2 Burst(int i) => burst[Mathf.Clamp(i, 0, burst.Length - 1)];
    int Count => Mathf.Clamp(def.shotCount, 1, burst.Length);
    int Claw(int i) => def.muzzles.Length >= 2 ? i % 2 : 0;

    public override bool FriendlyInLine(Vector2 seen)
    {
        Vector2 to = seen - ship.Position;
        float dist = to.magnitude;
        return dist > 1e-3f && EliteOnLine(ship.Position, to / dist, dist + 1f, Count * def.lobSpacing * .5f);
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        telegraph.Clear();
        float edge = Mathf.Max(.2f, EliteSystem.RailEdge - .35f);
        for (int i = 0; i < Count; i++)
        {
            float x = seen.x + (i - (Count - 1) * .5f) * def.lobSpacing;
            burst[i] = new Vector2(Mathf.Clamp(x, -edge, edge), seen.y + def.lobAhead);
            telegraph.Lane(ship.MuzzleWorld(Claw(i)), burst[i], def.shotSize * .5f + .06f);
            telegraph.Ring(burst[i], BurstRing);
        }
        telegraph.Show(TellSeconds, def.ShotColor);
    }

    public override void StepTell(float dt) { t += dt; telegraph.Step(dt); }
    public override void Cancel() { telegraph.End(); }
    public override void OnDeath() { telegraph.End(); }

    public override void BeginAction()
    {
        base.BeginAction();
        telegraph.GoLive();
        next = 0f;
        sent = 0;
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        while (t >= next && sent < Count)
        {
            int m = Claw(sent);
            Vector2 d = burst[sent] - (Vector2)ship.MuzzleWorld(m);
            float seconds = Mathf.Max(.2f, def.hazardSeconds);
            var orb = Fire(m, Deg(d), d.magnitude / seconds, EliteShots.Kind.Orb);
            if (orb != null) orb.Fuse(seconds);
            sent++;
            next += def.shotInterval;
        }
        return t >= def.actionSeconds && sent >= Count;
    }

    public override void End() { telegraph.End(); }
}

// Trawler: a net cast. A row of ringed spots across the pilot's height (centred on it) is drawn from the first frame of the tell; the
// spot beside the pilot on the side toward the board's middle is left out: the hole in the net. Then the booms lob a net onto every
// other spot (each lands as a pool riding the board for poolSeconds).
public class NetCastAttack : EliteAttack
{
    public const float SpotRing = .42f;
    readonly EliteTelegraph telegraph = new EliteTelegraph();
    Vector2 row, liveRow;
    int gap, cast;
    float next;
    public NetCastAttack() { Id = "net_cast"; }
    public override bool HoldsDuringTell => true;
    public int Count => Mathf.Max(3, def.shotCount);
    public int Centre => Count / 2;
    public int Gap => gap;
    public int Cast => cast;
    public Vector2 Row => row;
    public EliteTelegraph Telegraph => telegraph;

    public Vector2 Spot(int i) => SpotAt(row, i);

    Vector2 SpotAt(Vector2 centre, int i)
    {
        float x = centre.x + (i - (Count - 1) * .5f) * def.lobSpacing;
        float edge = Mathf.Max(.3f, EliteSystem.RailEdge - def.shotSize - .1f);
        return new Vector2(Mathf.Clamp(x, -edge, edge), centre.y);
    }

    public override bool FriendlyInLine(Vector2 seen)
    {
        Vector2 to = seen - ship.Position;
        float dist = to.magnitude;
        return dist > 1e-3f && EliteOnLine(ship.Position, to / dist, dist + 1f, Count * def.lobSpacing * .5f);
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        row = seen + new Vector2(((Count - 1) * .5f - Centre) * def.lobSpacing, def.lobAhead);
        gap = Centre + (seen.x > 0f ? -1 : 1);
        telegraph.Clear();
        for (int i = 0; i < Count; i++) if (i != gap) telegraph.Ring(Spot(i), SpotRing);
        telegraph.Show(TellSeconds, def.ShotColor);
    }

    public override void StepTell(float dt) { t += dt; telegraph.Step(dt); }
    public override void Cancel() { telegraph.End(); }
    public override void OnDeath() { telegraph.End(); }

    public override void BeginAction()
    {
        base.BeginAction();
        telegraph.GoLive();
        next = 0f;
        cast = 0;
        liveRow = row + Vector2.up * (EliteSystem.Scroll * def.lobSeconds);
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        liveRow.y -= EliteSystem.Scroll * dt;
        while (t >= next && cast < Count - 1)
        {
            int slot = cast >= gap ? cast + 1 : cast;   // (the k-th cast goes to spot k, skipping the gap)
            int m = def.muzzles.Length > 0 ? cast % def.muzzles.Length : 0;
            Vector2 to = SpotAt(liveRow, slot);
            var shot = Fire(m, Deg(to - (Vector2)ship.MuzzleWorld(m)), 0f);
            if (shot != null) shot.Lob(to, Mathf.Max(.15f, def.lobSeconds - next));
            cast++;
            next += def.shotInterval;
        }
        return t >= def.actionSeconds && cast >= Count - 1;
    }

    public override void End() { telegraph.End(); }
}

// Thunderfin: thunder strike. The lure coils pulse and a lane glyph is drawn under every column for the whole tell (>= .7 s), then
// shotCount lightning columns hit at once (AttackStrike: .25 s live), on the pilot's lane and the lanes beside it. Step off the glyph.
public class ThunderStrikeAttack : EliteAttack
{
    readonly AttackStrike[] armed = new AttackStrike[4];
    readonly float[] lanes = new float[4];
    int count;
    public ThunderStrikeAttack() { Id = "thunder_strike"; }
    public override bool HoldsDuringTell => true;
    public override bool Shoots => false;   // (lightning, not shots)
    public int Armed => count;
    public AttackStrike Strike(int i) => armed[i];

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        var spec = StrikeSpec.Standard(Mathf.Max(0, def.WorldIndex));
        count = StrikeLanes.Pick(seen.x, Mathf.Clamp(def.shotCount, 1, armed.Length), Mathf.Max(AttackStrike.MinLaneSpacing, def.lobSpacing),
                                 BossRails.DrawnInnerEdge, spec.hitHalf, lanes);
        for (int i = 0; i < count; i++) armed[i] = AttackStrike.Arm(spec, lanes[i], seen.y, TellSeconds, ship.gameObject);
    }

    public override void Cancel() { Drop(); }
    public override void OnDeath() { Drop(); }

    void Drop()
    {
        for (int i = 0; i < count; i++)
        {
            if (armed[i] != null && armed[i].State == AttackHazard.Phase.Tell) armed[i].Cancel();
            armed[i] = null;
        }
        count = 0;
    }

    public override void BeginAction()
    {
        base.BeginAction();
        for (int i = 0; i < count; i++) if (armed[i] != null) armed[i].Ignite();
        if (count > 0)
        {
            Fired++;
            ship.OnFired(0, -90f);
        }
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        return t >= def.actionSeconds;
    }

    public override void End() { for (int i = 0; i < armed.Length; i++) armed[i] = null; count = 0; }
}

// Pearl Warden: the ward curtain in pearls: a row of slow pearls across the pilot's height with
// the one spot beside the pilot left out.
public class PearlCurtainAttack : WardCurtainAttack
{
    public PearlCurtainAttack() { Id = "pearl_curtain"; }
}
