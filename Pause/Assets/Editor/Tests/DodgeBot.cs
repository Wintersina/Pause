using System;
using System.Collections.Generic;
using UnityEngine;

// THE DODGE BOT (docs/world-attacks-design.md FR6; AttackBudgetTest).
//
// A deterministic, headless stand-in for a decent human: it sees the world
// REACTION seconds late (a ring of past snapshots), extrapolates every
// hazard linearly from the two snapshots it last saw (a shot's speed, a
// beam's sweep), knows when a telegraphed hazard goes live (a laser's aim
// line, a lunge's tell, a pool's landing), and flies at Speed to the NEAREST
// spot whose whole path is clear of everything it predicts for the next
// Horizon seconds. It re-plans the moment its plan stops being clear. It
// never sees an attack before the attack shows itself (a shot is invisible
// until it leaves the muzzle; the aim line shows only for its last .7 s).
// Two human frailties keep it from being a perfect dodger (a perfect one
// scores zero against everything and pins nothing): it misjudges where each
// hazard is by up to PerceptionError (a fixed error per hazard per roll), and
// the ship takes time to get up to speed (Accel).
//
// A scenario (one attack, one fixture) is rolled `rolls` times from seeded
// positions; a roll is a HIT when the bot's ship touched a live hazard at
// the end of any frame (circles are swept). A ghost ship that never moves
// is scored in the same rolls (the "stand still" rate, information only).
//
//   hit rate = rolls hit / rolls in which the attack showed itself at all
//
// A themed attack replaces an old one only if its hit rate stays within
// AttackBudgetTest.BudgetFactor of the old one's pinned rate.
public static class DodgeBot
{
    public const float Reaction = .25f;     // seconds late the bot sees the world
    public const float Speed = 7f;          // u/s, the ship's top speed (BossAttackTest.MaxShipSpeed)
    public const float ShipRadius = .28f;   // BossAttackTest / ShipReachTest
    public const float Margin = .05f;       // clearance the bot asks for on top of the radii
    public const float Accel = 40f;         // u/s^2: the ship does not reach top speed at once (.175 s to 7 u/s)
    public const float PerceptionError = .12f;   // u: half-width of the (triangular) misjudgement of where each hazard is, constant per hazard per roll
    public const float Dt = .025f;          // simulation step (Reaction is exactly DelayFrames of it)
    public const int DelayFrames = 10;
    public const float Horizon = 1.5f;      // seconds the bot plans ahead
    public const float PlanStep = .05f;
    public const float MinX = -2.3f, MaxX = 2.3f, MinY = -3.9f, MaxY = -.7f;   // where the ship may fly
    public const float StartY0 = -3.6f, StartY1 = -2f, StartX = 2.1f;
    public const int MaxCandidates = 520;
    public const float TellStep = 1.2f;     // u the bot sidesteps when it sees a windup begin (before it knows the line)

    // A hazard as the bot can see it: a circle (a shot, a pool, a body), or a
    // thick segment (a beam, a jet, a lunge path). liveIn/liveFor are in
    // seconds from the snapshot: harmful from liveIn for liveFor.
    public struct Hz
    {
        public int id;
        public bool seg;
        public Vector2 a, b;           // circle: a = centre; segment: a -> b
        public float r;                // radius / half thickness
        public float liveIn, liveFor;  // 0 / Mathf.Infinity for a shot in flight
        public Vector2 va, vb;         // filled by the bot from two snapshots, unless the scenario knows them (hasV)
        public bool hasV;              // the scenario supplies va / vb (a telegraphed beam's known sweep)
        public Vector2 vbExtra;        // (hasV) extra velocity of the far end b only, from moveFrom on (a beam's swing once it is live)
        public float moveFrom;         // seconds from the snapshot at which vbExtra starts
        public Func<float, Vector4> segAt;   // a segment that moves in a way the bot knows (a sweep drawn in the tell): its ends (a.xy, b.xy) `since` seconds after the snapshot; replaces a / b / va / vb
        public bool planOnly;          // the bot knows it (the preview draws it: a sweep's whole sector) but it is not where the hazard is: never scored as a hit

        public static Hz Circle(int id, Vector2 c, float r, float liveIn = 0f, float liveFor = float.PositiveInfinity)
        { return new Hz { id = id, a = c, b = c, r = r, liveIn = liveIn, liveFor = liveFor }; }

        public static Hz Segment(int id, Vector2 a, Vector2 b, float halfThick, float liveIn = 0f, float liveFor = float.PositiveInfinity)
        { return new Hz { id = id, seg = true, a = a, b = b, r = halfThick, liveIn = liveIn, liveFor = liveFor }; }

        public bool HarmfulNow => liveIn <= 0f && liveFor > 0f;
    }

    // One attack in its fixture. The bot owns the ship; the scenario owns the world.
    public interface IScenario
    {
        string Name { get; }
        float MaxSeconds { get; }
        Transform Begin(System.Random rng, Vector2 shipStart);   // sets the world up, returns the ship transform the bot flies
        void Step(float dt);                 // one world frame
        void Collect(List<Hz> into);         // every hazard, now
        bool Telling { get; }                // an aimed attack's windup is showing (the bot sidesteps: "moving after the tell starts always dodges")
        bool Attacked { get; }               // did the attack show itself this roll
        bool Done { get; }                   // nothing left to come
        void End();
    }

    public struct Result
    {
        public string name;
        public int rolls, attacked, hits, ghostHits, frames;
        public float HitRate => attacked > 0 ? hits / (float)attacked : 0f;
        public float GhostRate => attacked > 0 ? ghostHits / (float)attacked : 0f;
        public float AttackedShare => rolls > 0 ? attacked / (float)rolls : 0f;
    }

    // Debugging: log every frame's hazards and the bot's place (AttackBudgetTest.Sweep -trace)
    public static bool Trace;

    // offsets from the ship, nearest first (built once)
    static Vector2[] offsets;

    static Vector2[] Offsets()
    {
        if (offsets != null) return offsets;
        var list = new List<Vector2>();
        const float grid = .3f;
        for (int ix = -18; ix <= 18; ix++)
            for (int iy = -10; iy <= 10; iy++)
                list.Add(new Vector2(ix * grid, iy * grid));
        list.Sort((p, q) => p.sqrMagnitude.CompareTo(q.sqrMagnitude));
        offsets = list.ToArray();
        return offsets;
    }

    public static Result Run(IScenario s, int rolls, uint seed)
    {
        var res = new Result { name = s.Name, rolls = rolls };
        var ring = new List<Hz>[DelayFrames + 1];
        for (int i = 0; i < ring.Length; i++) ring[i] = new List<Hz>(48);
        var prevA = new Vector2[8192];
        var prevB = new Vector2[8192];
        var prevFrame = new int[8192];
        var now = new List<Hz>(48);
        var tellRing = new bool[DelayFrames + 1];
        var offs = Offsets();
        int maxFrames = Mathf.CeilToInt(s.MaxSeconds / Dt);

        for (int roll = 0; roll < rolls; roll++)
        {
            var rng = new System.Random(unchecked((int)(seed * 7919u + (uint)roll * 104729u + 17u)));
            Vector2 start = new Vector2(Mathf.Lerp(-StartX, StartX, (float)rng.NextDouble()),
                                        Mathf.Lerp(StartY0, StartY1, (float)rng.NextDouble()));
            Vector2 pos = start, ghost = start, goal = start, vel = Vector2.zero;
            bool havePlan = false, hit = false, ghostHit = false, tellWas = false;
            for (int i = 0; i < tellRing.Length; i++) tellRing[i] = false;
            for (int i = 0; i < prevFrame.Length; i++) prevFrame[i] = -10;
            for (int i = 0; i < ring.Length; i++) ring[i].Clear();
            var ship = s.Begin(rng, start);
            ship.position = new Vector3(pos.x, pos.y, 0f);
            int f = 0;
            for (; f < maxFrames; f++)
            {
                s.Step(Dt);
                now.Clear();
                s.Collect(now);
                // velocities from the last frame's positions
                for (int i = 0; i < now.Count; i++)
                {
                    var h = now[i];
                    int id = h.id & 8191;
                    if (h.hasV) { }
                    else if (prevFrame[id] == f - 1) { h.va = (h.a - prevA[id]) / Dt; h.vb = (h.b - prevB[id]) / Dt; }
                    prevA[id] = h.a; prevB[id] = h.b; prevFrame[id] = f;
                    now[i] = h;
                }
                tellRing[f % tellRing.Length] = s.Telling;
                var slot = ring[f % ring.Length];
                slot.Clear();
                slot.AddRange(now);

                // ---- did anything touch the ships this frame (hazards at their end-of-frame places)
                pos = ship.position;
                for (int i = 0; i < now.Count && !(hit && ghostHit); i++)
                {
                    var h = now[i];
                    if (!h.HarmfulNow || h.planOnly) continue;
                    float reach = h.r + ShipRadius;
                    if (!hit && Touches(h, pos, reach)) hit = true;
                    if (!ghostHit && Touches(h, ghost, reach)) ghostHit = true;
                }

                if (Trace && now.Count > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append("[DODGE] f").Append(f).Append(" ship ").Append(pos.ToString("F2")).Append(hit ? " HIT" : "").Append(" plan ").Append(havePlan ? goal.ToString("F2") : "-");
                    for (int i = 0; i < now.Count; i++)
                    {
                        var h = now[i];
                        sb.Append("\n    ").Append(h.id).Append(h.seg ? " seg " : " circ ").Append(h.a.ToString("F2")).Append(h.seg ? "->" + h.b.ToString("F2") : "").Append(" r").Append(h.r.ToString("F2"))
                          .Append(" live ").Append(h.liveIn.ToString("F2")).Append("+").Append(h.liveFor.ToString("F2")).Append(" v ").Append(h.va.ToString("F2"));
                    }
                    Debug.Log(sb.ToString());
                }

                // ---- the bot: sees the frame DelayFrames ago
                if (f >= DelayFrames)
                {
                    var seen = ring[(f - DelayFrames) % ring.Length];
                    int salt = roll;
                    // a windup seen (a reaction ago): a quick sidestep, a coin flip which way, towards the room there is
                    bool tell = tellRing[(f - DelayFrames) % tellRing.Length];
                    if (tell && !tellWas)
                    {
                        float side = rng.NextDouble() < .5 ? -1f : 1f;
                        if (pos.x + side * TellStep < MinX || pos.x + side * TellStep > MaxX) side = -side;
                        goal = new Vector2(Mathf.Clamp(pos.x + side * TellStep, MinX, MaxX), pos.y);
                        havePlan = true;
                    }
                    tellWas = tell;
                    if (!havePlan || !PathClear(pos, goal, seen, salt))
                    {
                        goal = pos;
                        havePlan = true;
                        if (!PathClear(pos, pos, seen, salt))
                        {
                            int tried = 0;
                            for (int k = 1; k < offs.Length && tried < MaxCandidates; k++)
                            {
                                Vector2 g = pos + offs[k];
                                if (g.x < MinX || g.x > MaxX || g.y < MinY || g.y > MaxY) continue;
                                tried++;
                                if (PathClear(pos, g, seen, salt)) { goal = g; break; }
                            }
                        }
                    }
                    Vector2 to = goal - pos;
                    float away = to.magnitude;
                    float want = Mathf.Min(Speed, Mathf.Sqrt(2f * Accel * away));   // brakes into its goal
                    Vector2 wantVel = away > 1e-4f ? to / away * want : Vector2.zero;
                    vel = Vector2.MoveTowards(vel, wantVel, Accel * Dt);
                    pos += vel * Dt;
                    pos.x = Mathf.Clamp(pos.x, MinX - .2f, MaxX + .2f);
                    pos.y = Mathf.Clamp(pos.y, MinY - .2f, MaxY + .2f);
                    ship.position = new Vector3(pos.x, pos.y, 0f);
                }
                if (s.Done && f > DelayFrames) break;
            }
            res.frames += f;
            if (s.Attacked) { res.attacked++; if (hit) res.hits++; if (ghostHit) res.ghostHits++; }
            s.End();
        }
        return res;
    }

    // Does hazard `h` (as of now) touch a ship at p (reach = both radii)?
    static bool Touches(Hz h, Vector2 p, float reach)
    {
        if (!h.seg) return (h.a - p).sqrMagnitude < reach * reach;
        return HostileShots.SegmentDistanceSq(h.a, h.b, p) < reach * reach;
    }

    // Is the straight flight from `from` to `to` (at Speed, then holding)
    // clear of everything `seen` (a snapshot Reaction ago) predicts?
    static bool PathClear(Vector2 from, Vector2 to, List<Hz> seen, int salt)
    {
        if (seen.Count == 0) return true;
        Vector2 d = to - from;
        float dist = d.magnitude;
        Vector2 dir = dist > 1e-4f ? d / dist : Vector2.zero;
        for (float tau = 0f; tau <= Horizon + 1e-4f; tau += PlanStep)
        {
            Vector2 p = from + dir * Mathf.Min(Travelled(tau), dist);
            float since = Reaction + tau;   // snapshot -> that moment
            for (int i = 0; i < seen.Count; i++)
            {
                var h = seen[i];
                if (since < h.liveIn || since > h.liveIn + h.liveFor) continue;
                float reach = h.r + ShipRadius + Margin;
                Vector2 err = Misjudged(h.id, salt);
                if (!h.seg)
                {
                    Vector2 c = h.a + err + h.va * since;
                    if ((c - p).sqrMagnitude < reach * reach) return false;
                }
                else if (h.segAt != null)
                {
                    // (a fast sweep passes a spot between two plan steps: look at it four times a step)
                    for (int sub = 0; sub < 4; sub++)
                    {
                        float at = since + sub * PlanStep * .25f;
                        if (at < h.liveIn || at > h.liveIn + h.liveFor) continue;
                        Vector4 e = h.segAt(at);
                        if (HostileShots.SegmentDistanceSq(new Vector2(e.x, e.y) + err, new Vector2(e.z, e.w) + err, p) < reach * reach) return false;
                    }
                }
                else
                {
                    Vector2 a = h.a + err + h.va * since, b = h.b + err + h.vb * since + h.vbExtra * Mathf.Max(0f, since - h.moveFrom);
                    if (HostileShots.SegmentDistanceSq(a, b, p) < reach * reach) return false;
                }
            }
        }
        return true;
    }

    // how far the ship has flown `tau` seconds after setting off (ramps up at Accel, then Speed)
    static float Travelled(float tau)
    {
        float ramp = Speed / Accel;
        return tau < ramp ? .5f * Accel * tau * tau : .5f * Speed * ramp + Speed * (tau - ramp);
    }

    // the bot's constant misjudgement of hazard `id` in roll `salt`: a triangular error in x and y
    static Vector2 Misjudged(int id, int salt)
    {
        uint h = (uint)id * 2654435761u ^ (uint)(salt + 1) * 40503u ^ 0x9E3779B9u;
        float U() { h ^= h << 13; h ^= h >> 17; h ^= h << 5; return (h & 0xFFFF) / 65535f; }
        return new Vector2((U() + U() - 1f) * PerceptionError, (U() + U() - 1f) * PerceptionError);
    }
}
