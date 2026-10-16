using UnityEngine;

// These measurements describe the legacy timer rotation, not BossMusicDirector.
// THE DIFFICULTY LADDER (docs/boss-attack-overhaul.md): boss 1 (Void Archon) easiest ... boss 7 (Dune-Eater) hardest,
// in the order the game visits the worlds. Defined by numbers that are MEASURED on the real tables
// (BossPatternTables played through PatternSim), so a tuning change that breaks the ladder fails BossLadderTest.
//
//   bullets / second     shots fired per second over a full phase-3 rotation (tells and cooldowns included), enrage on
//   max simultaneous     the most shots alive at once in that rotation (must fit BossConfig.ProjectilePoolMax)
//   speed range          slowest .. fastest shot
//   attacks              slots in the rotation (the boss's pattern count)
//   max overlap          the most streams running at once inside one attack
//   min tell             the shortest telegraph in the rotation
//   safe gap             the narrowest forced gap (a curtain's / ring's / lattice's opening) in ship-hitbox widths,
//                        a TARGET every stream is checked against (GapTarget) -- it shrinks up the ladder
//   enrage               the last third multiplies every stream's firing rate by this
public sealed class LadderRow
{
    public int world;
    public int attacks, patterns;
    public float bulletsPerSecond;
    public int maxSimultaneous, maxOverlap;
    public float minSpeed, maxSpeed;
    public float minTell;
    public float minGap;            // the narrowest measured forced gap, in hitbox widths (>= GapTarget[world])
    public float enrage;            // the last third's rate multiplier
    public float density;           // the boss's overall density knob
    public int dropped;             // shots the sim's pool had no room for (must be 0)
    public string worst;            // the slot that sets minGap
}

public static class BossLadder
{
    // ---- the tuning, per boss (index = world) ----
    public static readonly float[] Enrage = { 1.10f, 1.15f, 1.20f, 1.25f, 1.30f, 1.40f, 1.50f };
    // the narrowest gap any of the boss's walls / rings / lattices / mine fields may leave, in ship hitbox widths
    public static readonly float[] GapTarget = { 3.4f, 3.0f, 2.7f, 2.4f, 2.1f, 1.6f, 1.4f };
    // the shortest telegraph the boss may show (seconds)
    public static readonly float[] TellFloor = { .9f, .85f, .8f, .75f, .7f, .7f, .65f };
    // the fastest shot of the rotation (the aimed accent that makes the boss feel quicker up the ladder)
    public static readonly float[] SpeedFloor = { 4.4f, 4.8f, 5.1f, 5.4f, 5.7f, 6.0f, 6.4f };
    // the headline: shots per second over the phase-3 rotation, the ladder's floor and its pool
    // (Tempest 7.4 -> 7.1: its lattice rows used to be counted out to x = +/-4.1, behind the walls and off the screen;
    // they now form inside the rails under the boss -- the same grid in play, fewer bullets nobody could see)
    public static readonly float[] BulletsFloor = { 3.2f, 4.2f, 4.4f, 6.2f, 6.7f, 7.1f, 10.8f };

    // how much of each slot's cooldown is kept (pace: the later bosses leave less room between attacks)
    public static readonly float[] Cooldown = { 1.0f, 1.0f, .95f, .85f, .8f, .75f, .7f };

    public static float CooldownFor(int world) => Cooldown[Mathf.Clamp(world, 0, Cooldown.Length - 1)];
    // The overall density of the boss's streams (every stream's firing rate x this, all phases): the one fine-tuning knob
    // the greedy-bot ladder is calibrated with (BossLadderBotTest), so the tables themselves stay readable.
    public static float[] Density = { .75f, .62f, .64f, .62f, .60f, .50f, .66f };
    public static float DensityFor(int world) => Density[Mathf.Clamp(world, 0, Density.Length - 1)];

    // The firing-rate scale at a point of the fight: the density, times the enrage in the last third.
    public static float RateAt(int world, float progress01) => DensityFor(world) * (progress01 >= 2f / 3f ? EnrageFor(world) : 1f);
    public static float EnrageFor(int world) => Enrage[Mathf.Clamp(world, 0, Enrage.Length - 1)];

    public const float ShipR = PatternSim.ShipRadius;
    public const float Diameter = 2f * PatternSim.ShipRadius;
    public const float FinalCooldownScale = .75f;

    // One stream's forced gap in hitbox widths (the clear space between shots, over the ship's width); infinity when
    // the stream forces no gap (a plain aimed line the ship simply steps out of).
    public static float GapMultiple(PatternStream s, float rail = 2.6f)
    {
        float r = s.style == BossShotStyle.Bolt ? .13f : .12f;
        float clear;
        switch (s.kind)
        {
            case StreamKind.Curtain: clear = s.gap - 2f * r; break;
            case StreamKind.Ring: clear = 2f * 4.0f * Mathf.Sin(s.gap * .5f * Mathf.Deg2Rad) - 2f * r; break;
            case StreamKind.Lattice: clear = s.pitch * .5f - 2f * r; break;
            case StreamKind.Rain:
            {
                float lane = rail * 2f / Mathf.Max(3f, Mathf.Round(rail * 2f / Mathf.Max(.3f, s.pitch)));
                clear = Mathf.Max(1, s.free) * lane - 2f * r;
                break;
            }
            case StreamKind.Mines: clear = s.minSep - 2f * r; break;
            case StreamKind.Spiral:
                clear = s.count >= 2 ? 2f * 4.0f * Mathf.Sin(Mathf.Min(s.armDeg, 60f) * .5f * Mathf.Deg2Rad) - 2f * r : float.PositiveInfinity;
                break;
            default: return float.PositiveInfinity;
        }
        return clear / Diameter;
    }

    // How many shots the boss's pool builds up front (the rotation's peak and a margin), so a fight creates nothing mid-pattern.
    static readonly int[] warm = new int[7];

    public static int WarmShots(int world)
    {
        world = Mathf.Clamp(world, 0, warm.Length - 1);
        // Beat-synced volleys are denser than Measure's timer rotation. Build their
        // bounded pool during the entrance rather than allocating on musical accents.
        if (BossBeat.Enabled && BossBeatTuning.For(world).enabled) return BossConfig.ProjectilePoolMax;
        if (warm[world] == 0) warm[world] = Mathf.Min(BossConfig.ProjectilePoolMax, Measure(world).maxSimultaneous + 12);
        return warm[world];
    }

    // Plays the boss's whole phase-3 rotation twice through the sim and reads the second round.
    public static LadderRow Measure(int world, float rate = -1f)
    {
        var slots = BossPatternTables.For(world);
        if (rate < 0f) rate = EnrageFor(world) * DensityFor(world);
        var row = new LadderRow { world = world, attacks = slots.Length, enrage = EnrageFor(world), density = DensityFor(world), minTell = float.MaxValue, minGap = float.MaxValue };
        var sim = new PatternSim { cap = 256 };
        var runner = new BossPatternRunner();
        const float dt = .025f;
        float measuredFrom = 0f, measuredTime = 0f;
        int firedFrom = 0;
        row.minSpeed = float.MaxValue;
        for (int round = 0; round < 2; round++)
        {
            if (round == 1) { measuredFrom = sim.time; firedFrom = sim.fired; sim.maxLive = sim.live; sim.minSpeed = float.MaxValue; sim.maxSpeed = 0f; }
            for (int i = 0; i < slots.Length; i++)
            {
                var sl = slots[i];
                if (sl.pattern != null)
                {
                    if (round == 1) row.minTell = Mathf.Min(row.minTell, sl.tell);
                    runner.Begin(sl.pattern, sim, rate, 1000 * i + 7, sl.tell);
                    sim.player = new Vector2(i % 2 == 0 ? .8f : -.8f, -3.2f);
                    for (float t = 0f; t < sl.tell; t += dt) sim.Advance(dt);
                    while (!runner.Step(dt)) sim.Advance(dt);
                    sim.Advance(dt);
                    if (round == 1)
                        for (int q = 0; q < sl.pattern.streams.Length; q++)
                        {
                            var g = GapMultiple(sl.pattern.streams[q]);
                            if (g < row.minGap) { row.minGap = g; row.worst = sl.name; }
                        }
                }
                else
                {
                    if (round == 1) row.minTell = Mathf.Min(row.minTell, sl.tell);
                    for (float t = 0f; t < sl.tell + sl.est; t += dt) sim.Advance(dt);
                }
                for (float t = 0f; t < sl.cooldown * CooldownFor(world) * FinalCooldownScale; t += dt) sim.Advance(dt);
            }
        }
        measuredTime = sim.time - measuredFrom;
        row.bulletsPerSecond = (sim.fired - firedFrom) / Mathf.Max(.1f, measuredTime);
        row.maxSimultaneous = sim.maxLive;
        row.minSpeed = sim.minSpeed;
        row.maxSpeed = sim.maxSpeed;
        row.dropped = sim.dropped;
        int pats = 0, overlap = 1;
        foreach (var sl in slots) if (sl.pattern != null) { pats++; overlap = Mathf.Max(overlap, sl.pattern.MaxConcurrentStreams); }
        row.patterns = pats;
        row.maxOverlap = overlap;
        if (row.minGap == float.MaxValue) row.minGap = 99f;
        return row;
    }
}
