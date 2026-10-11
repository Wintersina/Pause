using UnityEngine;

// Verdant's four new elites (Codex's flight strips, Art/Enemies/Elite/Verdant/new/manifest_new_elites.md), each its own brain and
// attack, joining the Resin Warden. They fly out of Verdant's own pads, every attack is the world's own material (a trunk, a vine,
// spores, a leaf blade) built from the attack cores already in the game (ShotMotion Roll / Burst / Slash, AttackLash), and every one
// draws its footprint from the first frame of its tell (AttackPreview: the same dotted pink-white outline the themed hazards use).
//
//   Timber Hauler  logger  / log_roll     (riverbay)   lobs trunks onto ringed spots; each lands, rolls down the board on a diagonal
//   Thornlash      thorn   / vine_lash    (towerbay)   a thorned vine whip swept across the lane (AttackLash), aimed once at the tell
//   Sporebloom     drifter / spore_burst  (podpad)     pods that burst into a ring of six spores; the burst rings are drawn first
//   Leafblade      diver   / leaf_dive    (roothangar) circles, then dives down a drawn lane and leaves a crescent across its row
//
// See docs/enemy-behaviours.md ("Verdant elites") for the design.

// ---- the telegraph -------------------------------------------------------------------------------------------------

// A dotted outline of what an attack is about to cover: rings (a landing spot, a burst), lanes (a roll path, a dive, a slash row).
// Built once per attack, shown from the first frame of its tell for the whole tell, riding the board when asked to.
public sealed class EliteTelegraph
{
    public const int RingPoints = 16;
    readonly AttackShape shape = new AttackShape();
    AttackPreview preview;
    float seconds;
    public bool Showing => preview != null && preview.Active;
    public int Loops => shape.LoopCount;
    public AttackShape Shape => shape;

    public void Clear() { End(); shape.Clear(); }

    public void Ring(Vector2 c, float r)
    {
        if (shape.LoopCount >= AttackShape.MaxLoops) return;
        shape.BeginLoop();
        for (int i = 0; i < RingPoints; i++)
        {
            float a = i * Mathf.PI * 2f / RingPoints;
            shape.LoopPoint(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
        }
        shape.EndLoop();
    }

    // A lane: the rectangle `half` either side of a -> b.
    public void Lane(Vector2 a, Vector2 b, float half)
    {
        if (shape.LoopCount >= AttackShape.MaxLoops) return;
        Vector2 d = b - a;
        float m = d.magnitude;
        if (m < 1e-4f) return;
        Vector2 n = new Vector2(-d.y, d.x) / m * half;
        shape.BeginLoop();
        shape.LoopPoint(a + n); shape.LoopPoint(b + n); shape.LoopPoint(b - n); shape.LoopPoint(a - n);
        shape.EndLoop();
    }

    public void Show(float untilLive, Color shot)
    {
        End();
        seconds = untilLive;
        preview = AttackPreview.Show(shape, untilLive, HostileShotPalette.Body(shot), true);   // (bold: Verdant's canopy is bright)
    }

    public void Step(float dt) { if (preview != null) preview.Step(dt); }
    public void Follow(Vector2 delta) { if (preview != null) preview.Follow(delta); }
    public void GoLive() { if (preview != null) { preview.GoLive(); preview = null; } }
    public void End() { if (preview != null) { preview.End(); preview = null; } }
}

// ---- brains --------------------------------------------------------------------------------------------------------

// Timber Hauler: a lumbering log barge, a lane wall like the Kilnback but slower and a wider reach: it throws from anywhere over the pilot's lane.
public class LoggerBrain : HaulerBrain
{
    public LoggerBrain() { Id = "logger"; }
    public override float SpeedScale => .7f;
    public override bool WantsAttack(Vector2 seen) => Pos.y > seen.y + MinAttackAbove && Mathf.Abs(Pos.x - seen.x) < 1.7f;
}

// Thornlash: holds a lane beside the pilot like a gunship but lets its whip reach: any time it is within a body and a half of the pilot's height.
public class ThornBrain : GunshipBrain
{
    public ThornBrain() { Id = "thorn"; }
    public override bool WantsAttack(Vector2 seen) => Mathf.Abs(Pos.y - seen.y) < .95f && Mathf.Abs(Pos.x - Lane) < .7f;
}

// Sporebloom: a slow drifting platform high over the board, swaying side to side on top of the bastion's crawl.
public class DrifterBrain : BastionBrain
{
    public DrifterBrain() { Id = "drifter"; }
    public override Vector2 Goal(Vector2 seen, float dt)
    {
        Vector2 g = base.Goal(seen, dt);
        g.x += Mathf.Sin(Clock * .6f) * .55f;
        return g;
    }
    public override float SpeedScale => .65f;
}

// Leafblade: circles the pilot like a striker, quicker and tighter, then dives.
public class DiverBrain : StrikerBrain
{
    public DiverBrain() { Id = "diver"; }
    public override Vector2 JoinFrom => new Vector2(-1f, .3f);
}

// ---- attacks -------------------------------------------------------------------------------------------------------

// Timber Hauler: it plants itself, a row of ringed spots and the line each trunk will roll along is drawn at once (the whole
// wind-up), then it lobs shotCount trunks onto the spots, one after another out of its grabbers. A trunk lands, rolls down the
// board on a diagonal toward the nearer rail at 1.4 u/s (relative to the ground), glances off a rail once and is gone after five
// seconds. The spots are locked on screen when the tell begins (the lobs come down from above onto them as the board carries them); a pilot who stands on one or on a roll line is hit.
public class LogRollAttack : EliteAttack
{
    public const float LaneLength = 3.6f, PathHalf = .16f, SpotRing = .45f, MinSpotX = .32f;
    readonly EliteTelegraph telegraph = new EliteTelegraph();
    Vector2 rowCentre;   // the row where the trunks LAND (on screen: the tell draws it there, locked)
    Vector2 liveRow;     // the lob's aim: that row plus the board's scroll over the flight, riding the board down onto it
    float next;
    int lobbed;
    bool fromLeft;
    public LogRollAttack() { Id = "log_roll"; }
    public override bool HoldsDuringTell => true;
    public Vector2 RowCentre => rowCentre;
    public int Lobbed => lobbed;
    public EliteTelegraph Telegraph => telegraph;

    public Vector2 Spot(int i) => SpotAt(rowCentre, i);

    Vector2 SpotAt(Vector2 centre, int i)
    {
        int n = Mathf.Max(1, def.shotCount);
        float x = centre.x + (i - (n - 1) * .5f) * def.lobSpacing;
        float edge = Mathf.Max(MinSpotX, EliteSystem.RailEdge - def.shotSize - .1f);
        x = Mathf.Clamp(x, -edge, edge);
        // (a spot on the middle line would roll either way: it is nudged to a side, so its roll can be drawn)
        if (Mathf.Abs(x) < MinSpotX) x = x >= 0f ? MinSpotX : -MinSpotX;
        return new Vector2(x, centre.y);
    }

    // The way a trunk rolls on from a spot (board frame), the same rule EliteShot uses.
    public static Vector2 RollDir(float spotX)
    {
        float dx = spotX > .25f ? 1f : -1f;
        return new Vector2(dx * ShotMotions.RollSpeed * .65f, -ShotMotions.RollSpeed * .76f).normalized;
    }

    // The roll's path from a spot: out along the diagonal, bouncing off the rail once (board frame), as the dotted lanes.
    void DrawRoll(Vector2 spot)
    {
        Vector2 p = spot, d = RollDir(spot.x);
        float left = LaneLength, edge = EliteSystem.RailEdge;
        for (int leg = 0; leg < 2 && left > .05f; leg++)
        {
            float toRail = d.x > 0f ? (edge - p.x) / d.x : (-edge - p.x) / d.x;
            float run = Mathf.Min(left, Mathf.Max(0f, toRail));
            Vector2 q = p + d * run;
            telegraph.Lane(p, q, PathHalf + def.shotSize * .4f);
            left -= run;
            p = q;
            d = new Vector2(-d.x, d.y);
        }
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        // where the trunks will come down: lobAhead of the pilot, on screen, drawn there for the whole tell
        rowCentre = seen + Vector2.up * def.lobAhead;
        telegraph.Clear();
        int n = Mathf.Max(1, def.shotCount);
        for (int i = 0; i < n; i++)
        {
            Vector2 s = Spot(i);
            telegraph.Ring(s, SpotRing);
            DrawRoll(s);
        }
        telegraph.Show(TellSeconds, def.ShotColor);
    }

    public override void StepTell(float dt)
    {
        t += dt;
        telegraph.Step(dt);
    }

    public override void Cancel() { telegraph.End(); }
    public override void OnDeath() { telegraph.End(); }

    public override void BeginAction()
    {
        base.BeginAction();
        telegraph.GoLive();
        next = 0f;
        lobbed = 0;
        fromLeft = ship.Position.x < rowCentre.x;
        liveRow = rowCentre + Vector2.up * (EliteSystem.Scroll * def.lobSeconds);
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        liveRow.y -= EliteSystem.Scroll * dt;   // (the aim rides the board down onto the row)
        int n = Mathf.Max(1, def.shotCount);
        while (t >= next && lobbed < n)
        {
            int spot = fromLeft ? lobbed : n - 1 - lobbed;
            int m = def.muzzles.Length > 0 ? lobbed % def.muzzles.Length : 0;
            Vector2 to = SpotAt(liveRow, spot);
            var shot = Fire(m, Deg(to - ship.MuzzleWorld(m)), 0f);
            if (shot != null) shot.Lob(to, def.lobSeconds - next);   // (the later trunks come down with the first: every spot lands on time)
            lobbed++;
            next += def.shotInterval;
        }
        return t >= def.actionSeconds && lobbed >= n;
    }

    public override void End() { telegraph.End(); }
}

// Thornlash: it holds still while its coiled whip root swells, and the whole swept area -- the start line, the arc of the tip and the
// end line -- is drawn from the first frame of the tell (AttackLash's own preview). The sweep is aimed once, at the pilot's spot when the
// tell begins, from the root nearer to it; it sweeps across that spot in AttackLash's fair way (a corridor on the far side stays open).
public class VineLashAttack : EliteAttack
{
    AttackLash lash;
    bool lit;
    public VineLashAttack() { Id = "vine_lash"; }
    public override bool HoldsDuringTell => true;
    public override bool Shoots => false;   // (a whip, not shots)
    public AttackLash Lash => lash;
    public bool Lit => lit;

    int Root(Vector2 seen)
    {
        int best = 0;
        float bd = float.MaxValue;
        for (int m = 0; m < def.muzzles.Length; m++)
        {
            float d = ((Vector2)ship.MuzzleWorld(m) - seen).sqrMagnitude;
            if (d < bd) { bd = d; best = m; }
        }
        return best;
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        lit = false;
        int m = Root(seen);
        var spec = LashSpec.Standard(Mathf.Max(0, def.WorldIndex));
        if (def.hazardSeconds > .01f) spec.sweepSeconds = def.hazardSeconds;
        if (def.hazardSize >= AttackLash.MinLength) spec.length = def.hazardSize;   // (the whip's reach: the pilot's range + 1.6 u when longer)
        Vector2 at = ship.MuzzleWorld(m);
        lash = AttackLash.Arm(spec, at, seen, TellSeconds, ship.gameObject);
        if (lash != null) lash.Follow(ship.transform, at - ship.Position);
        rootMuzzle = m;
    }

    int rootMuzzle;

    public override void Cancel() { Drop(); }
    public override void OnDeath() { Drop(); }

    void Drop()
    {
        if (lash != null && lash.State == AttackHazard.Phase.Tell) lash.Cancel();
        lash = null;
    }

    public override void BeginAction()
    {
        base.BeginAction();
        if (lash != null)
        {
            lash.Ignite();
            lit = true;
            Fired++;
            ship.OnFired(rootMuzzle, Deg(lash.Root - ship.Position));
        }
    }

    public override bool StepAction(float dt)
    {
        t += dt;
        return t >= def.actionSeconds;
    }

    public override void End() { lash = null; }
}

// Sporebloom: it holds while its pods swell. The burst points (a ring as wide as the spores reach) and the line each pod flies are drawn
// from the first frame of the tell. Then shotCount pods leave its side vents, one after another, each flying to its own burst point
// in ShotMotions.BurstSeconds and opening there into six spores in a ring, drifting out at 1.6 u/s. The burst points are set either
// side of the pilot's spot, so the cloud of spores (not the pods, which are slow) is what must be read.
public class SporeBurstAttack : EliteAttack
{
    public const float Reach = ShotMotions.SporeSpeed * ShotMotions.SporeSeconds;   // how far a spore flies
    readonly EliteTelegraph telegraph = new EliteTelegraph();
    Vector2[] burst = new Vector2[4];
    float next;
    int sent;
    public SporeBurstAttack() { Id = "spore_burst"; }
    public override bool HoldsDuringTell => true;
    public int Sent => sent;
    public EliteTelegraph Telegraph => telegraph;
    public Vector2 Burst(int i) => burst[Mathf.Clamp(i, 0, burst.Length - 1)];

    int Count => Mathf.Clamp(def.shotCount, 1, burst.Length);

    // Where pod i bursts: spread across the pilot's spot by lobSpacing, a little above it (the spores drift out and down onto it).
    Vector2 Place(Vector2 seen, int i)
    {
        int n = Count;
        float x = seen.x + (i - (n - 1) * .5f) * def.lobSpacing;
        float edge = Mathf.Max(.2f, EliteSystem.RailEdge - .35f);
        return new Vector2(Mathf.Clamp(x, -edge, edge), seen.y + def.lobAhead);
    }

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        telegraph.Clear();
        for (int i = 0; i < Count; i++)
        {
            burst[i] = Place(seen, i);
            Vector2 from = ship.MuzzleWorld(i % Mathf.Max(1, def.muzzles.Length));
            telegraph.Lane(from, burst[i], def.shotSize * .5f + .06f);
            telegraph.Ring(burst[i], Reach + .2f);
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
            int m = sent % Mathf.Max(1, def.muzzles.Length);
            Vector2 from = ship.MuzzleWorld(m);
            Vector2 d = burst[sent] - from;
            float speed = d.magnitude / ShotMotions.BurstSeconds;
            Fire(m, Deg(d), speed);
            sent++;
            next += def.shotInterval;
        }
        return t >= def.actionSeconds && sent >= Count;
    }

    public override void End() { telegraph.End(); }
}

// Leafblade: it locks the pilot's spot, holds a breath while the blades spread (the lane it will dive down and the row its crescent will
// cross are drawn at once), then dives through the spot at dashSpeed -- committed, no steering -- and, as it passes through, a crescent
// leaves its blade along the row, crossing the lane lengthwise at 4.5 u/s. A sidestep dodges the dive; the row needs a step up or down.
public class LeafDiveAttack : EliteAttack
{
    public const float DiveHalfPad = .22f, SlashHalf = .18f;
    readonly EliteTelegraph telegraph = new EliteTelegraph();
    bool slashed;
    float slashDeg;
    Vector2 diveFrom;
    public LeafDiveAttack() { Id = "leaf_dive"; }
    public override bool HoldsDuringTell => true;
    public override bool DrivesMovement => true;
    public override float? FaceDeg => Deg(dir);
    public EliteTelegraph Telegraph => telegraph;
    public bool Slashed => slashed;
    public Vector2 DiveFrom => diveFrom;
    public float SlashDeg => slashDeg;
    public float SlashRowY => aim.y;

    // (it dives at the spot it locked, from where it held)
    public override void DashLine(Vector2 seen, bool locked, out Vector2 d, out float reach)
    {
        Vector2 to = aim - ship.Position;
        reach = to.magnitude + DashPast;
        d = to.sqrMagnitude > 1e-4f ? to.normalized : Vector2.down;
    }

    // Which way the crescent crosses the lane: toward the side with the most floor (away from the nearer rail).
    public static float SlashHeading(float aimX) => aimX >= 0f ? 180f : 0f;

    public override void BeginTell(Vector2 seen)
    {
        base.BeginTell(seen);
        slashed = false;
        slashDeg = SlashHeading(aim.x);
        // where it will have come to rest by the end of the tell (it brakes from its circling speed at its own accel): the lane is drawn from there
        Vector2 v = ship.Velocity;
        Vector2 from = ship.Position + v * v.magnitude / (2f * Mathf.Max(1f, def.accel));
        diveFrom = from;
        Vector2 to = aim - from;
        Vector2 d = to.sqrMagnitude > 1e-4f ? to.normalized : Vector2.down;
        telegraph.Clear();
        telegraph.Lane(from, aim + d * (.5f + .25f), def.hullRadius + DiveHalfPad);
        // the row the crescent crosses: from the end of the dive to the far rail
        float edge = EliteSystem.RailEdge;
        Vector2 rowFrom = new Vector2(aim.x, aim.y), rowTo = new Vector2(slashDeg > 90f ? -edge : edge, aim.y);
        telegraph.Lane(rowFrom, rowTo, SlashHalf);
        telegraph.Show(TellSeconds, def.ShotColor);
    }

    public override void StepTell(float dt) { t += dt; telegraph.Step(dt); }
    public override void Cancel() { telegraph.End(); }
    public override void OnDeath() { telegraph.End(); }

    public override void BeginAction()
    {
        base.BeginAction();
        telegraph.GoLive();
        slashed = false;
        Vector2 d = aim - ship.Position;
        dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector2.down;
        ship.Drive(dir * def.dashSpeed);
    }

    float slashT;

    public override bool StepAction(float dt)
    {
        t += dt;
        if (!slashed)
        {
            ship.Drive(dir * def.dashSpeed);
            Vector2 to = aim - ship.Position;
            bool through = Vector2.Dot(to, dir) <= 0f;
            if (through || t >= def.actionSeconds)
            {
                slashed = true;
                slashT = t;
                // out of the blade on the side the crescent leaves (its muzzle 1 = left blade, 2 = right: the blade on the side it heads for, so the whole crescent starts ahead of the spot), along the row it was drawn on
                int m = def.muzzles.Length > 2 ? (slashDeg > 90f ? 1 : 2) : 0;
                var shot = Fire(m, slashDeg, def.shotSpeed);
                if (shot != null)
                {
                    // from the row it drew, not where the hull has got to
                    Vector3 p = shot.transform.position;
                    shot.transform.position = new Vector3(p.x, aim.y, p.z);
                }
            }
            return false;
        }
        // the dive ends where it was drawn to end: a quick stop just past the locked spot, never a coast down the board
        ship.Drive(dir * def.dashSpeed * .12f);
        return t >= slashT + StopSeconds;
    }

    public const float StopSeconds = .3f;

    public override void End()
    {
        telegraph.End();
        var s = ship.Brain as StrikerBrain;
        if (s != null) s.ResetLap();
    }
}
