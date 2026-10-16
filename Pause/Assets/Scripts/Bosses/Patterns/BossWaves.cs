using UnityEngine;

// BOSS BULLET WAVES (docs/boss-bullet-waves.md): every boss attack is a WAVE of bullets with a GAP the ship can escape
// through, spawned on the music's beat grid. Pure geometry + rhythm; no engine objects beyond Vector2 / Mathf, so the
// same code runs in the game (BossActor host) and in the tests (PatternSim host).
//
//   WaveGaps     the fairness core: picks where a wave's gap goes so the ship can always reach it in time
//   WaveRhythm   which sixteenths of a bar spawn a wave, from the section kind and the music's intensity
//   WaveMoves    each world's menu of wave shapes, unlocked as the intensity rises
//   BossWaveVoice  turns one move into bullets (walls, slanted walls, rings, fans), speeds quantised so a wave REACHES
//                  the ship on a beat as well as leaving the boss on one

public enum WaveKind
{
    Wall,    // a flat row of bullets falling from the top, one gap
    Slant,   // the same row drifting sideways (drift degrees off vertical): crossing waves are slants of opposite sign
    Ring,    // a radial ring from a muzzle with one gap (only the downward half fires)
    Fan,     // an arc of bullets from a muzzle with one gap (a ring with a limited arc)
}

public struct WaveMove
{
    public WaveKind kind;
    public bool sweep;          // the gap travels steadily one way (reversing at the lane's end) instead of hopping
    public float drift;         // slant: degrees off straight down (sign = direction)
    public float arc;           // fan: half arc in degrees each side of the gap
    public int count;           // ring: bullets round the full circle
    public int minTier;         // intensity tier (0..3) that unlocks it
    public int rows;            // wall / slant: rows deep (a thicker wave)
    public int part;            // muzzle index into the boss's part list for ring / fan

    public static WaveMove Wall(int tier, bool sweep = false, int rows = 1) => new WaveMove { kind = WaveKind.Wall, minTier = tier, sweep = sweep, rows = rows };
    public static WaveMove Slant(int tier, float drift, bool sweep = false, int rows = 1) => new WaveMove { kind = WaveKind.Slant, minTier = tier, drift = drift, sweep = sweep, rows = rows };
    public static WaveMove Ring(int tier, int count = 24, bool sweep = false, int part = 0) => new WaveMove { kind = WaveKind.Ring, minTier = tier, count = count, sweep = sweep, part = part };
    public static WaveMove Fan(int tier, float arc, int count = 24, bool sweep = false, int part = 0) => new WaveMove { kind = WaveKind.Fan, minTier = tier, arc = arc, count = count, sweep = sweep, part = part };
}

// What a world's waves look like beyond their shape: bullet style, speed, spacing.
public sealed class BossWaveStyle
{
    public BossShotStyle style = BossShotStyle.Shard;
    public float speed = 3.3f;          // nominal fall speed (authored view); quantised to the beat
    public float pitch = .55f;          // spacing between bullets of a wall
    public float rowSep = .5f;          // spacing between rows of a thick wave
    public WaveMove[] moves;
}

// ---------------------------------------------------------------- the gap generator
// The ship moves at about 5 u/s (the solver's speed); the generator plans with 4 so a human has headroom. For each
// new wave it knows when the wave reaches the ship's row and where the previous wave's gap was: the ship can be
// anywhere in that gap when it passes, so the new gap's centre is limited to what the ship can cover in the time
// between the two waves (less the time the previous wave takes to pass the ship and a reaction margin), plus the free
// room inside both gaps. The first wave after a quiet spell is judged from the ship's own position. Time is GAME time
// (pauses and slow motion shrink or stop it together with the bullets and the ship, so a squeezed gap pins in place).
public sealed class WaveGaps
{
    public const float PlanSpeed = 4f;       // ship speed the generator assumes (the solver tests with 5)
    public const float Reaction = .14f;      // seconds lost to seeing the gap
    public const float ShipWidth = 2f * PatternSim.ShipRadius + .1f;   // ship plus a hair
    public const float Slack = .14f;         // minimum overlap (u) between where the ship can be and the next gap
    public const float MinMultiple = 1.7f;   // never narrower than this many ship widths of clear space

    bool has;
    public float prevCenter, prevWidth, prevArrival;   // clear width of the previous gap, game-clock arrival at the ship row
    float reachLo, reachHi;                            // where the ship can be when the previous wave passes (inside its gap)
    public float lastShift, lastMaxShift;
    public int Count { get; private set; }
    public float ReachLo => reachLo;
    public float ReachHi => reachHi;

    public void Reset() { has = false; Count = 0; }

    // Picks the centre of a new wave's gap (clear width `width`) that reaches the ship's row at game time `arrival`.
    // `thick` is how long the previous wave takes to pass the ship. `want` in -1..1 is where the pattern would like the
    // gap to go within what is allowed (-1 = as far left as is fair, 1 = as far right); the result is always fair.
    //
    // It tracks the interval of x the ship can be at when the previous wave passes (its gap, narrowed by what the ship
    // could actually reach from the wave before it). That interval grows by PlanSpeed * the time between the waves
    // (less the previous wave's passing time and a reaction margin) and the new gap must overlap it by at least a ship
    // width. A wave after a quiet spell (the previous one has already passed) is judged from the ship's own position.
    public float Choose(float playerX, float now, float arrival, float width, float thick, float lim, float want)
    {
        float lo, hi, travel;
        if (has && prevArrival >= now - .02f)
        {
            lo = reachLo; hi = reachHi;
            travel = arrival - prevArrival - thick - Reaction;
        }
        else
        {
            lo = hi = Mathf.Clamp(playerX, -lim, lim);
            travel = arrival - now - Reaction;
        }
        float e = PlanSpeed * Mathf.Max(0f, travel);
        float el = Mathf.Max(-lim, lo - e), eh = Mathf.Min(lim, hi + e);
        if (el > eh) el = eh = Mathf.Clamp(.5f * (lo + hi), -lim, lim);
        float room = Mathf.Max(0f, (width - ShipWidth) * .5f);
        // leave some slack: the ship must be able to overlap the new gap by a few hair widths, not by a single point
        float slack = Mathf.Min(room, Slack);
        float cl = Mathf.Max(-lim, el - room + slack), ch = Mathf.Min(lim, eh + room - slack);
        if (cl > ch) cl = ch = Mathf.Clamp(.5f * (el + eh), -lim, lim);
        float c = Mathf.Lerp(cl, ch, (Mathf.Clamp(want, -1f, 1f) + 1f) * .5f);
        lastMaxShift = (ch - cl) * .5f;
        lastShift = c - .5f * (lo + hi);
        float nl = Mathf.Max(el, c - room), nh = Mathf.Min(eh, c + room);
        if (nl > nh) nl = nh = Mathf.Clamp(c, el, eh);
        if (!has || arrival >= prevArrival)
        {
            prevCenter = c; prevWidth = width; prevArrival = arrival;
            reachLo = nl; reachHi = nh;
        }
        has = true;
        Count++;
        return c;
    }
}

// ---------------------------------------------------------------- the rhythm
public static class WaveRhythm
{
    public const int TicksPerBar = 16;

    // Bit k set = a wave spawns on sixteenth k of the bar. Calm music keeps one wave a bar, drive adds a syncopated
    // 3+3+2 feel, a build or peak lands on every beat, and a drop doubles up on the off-beat. A wave never spawns
    // between sixteenths, so every wave leaves the boss exactly on the grid.
    public static int Mask(string kind, float intensity)
    {
        bool peak = kind == "drop" || kind == "peak";
        if (peak && intensity > .75f) return Bits(0, 4, 6, 8, 12, 14);
        if (peak || kind == "build" && intensity > .55f) return Bits(0, 4, 8, 12);
        if (intensity >= .62f || kind == "build") return Bits(0, 4, 8, 12);
        if (intensity >= .45f) return Bits(0, 6, 12);
        if (intensity >= .25f) return Bits(0, 8);
        return Bits(0);
    }

    static int Bits(params int[] ticks)
    {
        int m = 0;
        for (int i = 0; i < ticks.Length; i++) m |= 1 << ticks[i];
        return m;
    }

    public static int Tier(float intensity) => intensity < .3f ? 0 : intensity < .55f ? 1 : intensity < .78f ? 2 : 3;

    // The fraction of the gap table between "relaxed" (0) and "tightest" (1) for this intensity.
    public static float Squeeze(float intensity) => Mathf.Clamp01((intensity - .2f) / .7f);
}

// ---------------------------------------------------------------- the per-world menus
public static class WaveMoves
{
    static BossWaveStyle[] all;

    public static BossWaveStyle For(int world)
    {
        if (all == null) Build();
        return all[Mathf.Clamp(world, 0, all.Length - 1)];
    }

    static void Build()
    {
        all = new[]
        {
            // 0 Void Archon: the pulsar's rotating comb of rings, mirrored sweeping fans, crossing walls at the peak
            new BossWaveStyle { style = BossShotStyle.Shard, speed = 3.0f, pitch = .6f, moves = new[] {
                WaveMove.Ring(0, 24, sweep: true), WaveMove.Fan(1, 50f, 28, sweep: true), WaveMove.Wall(2, true), WaveMove.Slant(3, 14f), WaveMove.Slant(3, -14f) } },
            // 1 Hoarfrost Leviathan: cold-snap rings with a gap, icicle rows, glacier slabs crossing
            new BossWaveStyle { style = BossShotStyle.Shard, speed = 2.9f, pitch = .55f, moves = new[] {
                WaveMove.Ring(0, 22), WaveMove.Wall(1), WaveMove.Ring(2, 26, sweep: true), WaveMove.Slant(3, 10f, rows: 2), WaveMove.Slant(3, -10f, rows: 2) } },
            // 2 Bloom Queen: vine fans and hedges of seeds with a path through them, the path sweeping like a vine
            new BossWaveStyle { style = BossShotStyle.Bolt, speed = 3.1f, pitch = .6f, moves = new[] {
                WaveMove.Fan(0, 55f, 22), WaveMove.Wall(1, true), WaveMove.Fan(2, 70f, 30, sweep: true), WaveMove.Slant(3, 8f, true, 2), WaveMove.Slant(3, -8f, true, 2) } },
            // 3 Cinder Drake: flame fans and fire walls with a drifting gap
            new BossWaveStyle { style = BossShotStyle.Shard, speed = 3.2f, pitch = .55f, moves = new[] {
                WaveMove.Fan(0, 45f, 26), WaveMove.Wall(1, true), WaveMove.Fan(2, 65f, 34, sweep: true), WaveMove.Slant(3, 12f, rows: 2), WaveMove.Slant(3, -12f, rows: 2) } },
            // 4 Iron Kraken: the TIDAL WALLS (sweeping gap), surf crossing at the peak, a swell of rings from the beak
            new BossWaveStyle { style = BossShotStyle.Shard, speed = 3.3f, pitch = .5f, moves = new[] {
                WaveMove.Wall(0, true), WaveMove.Wall(1, true), WaveMove.Slant(2, 9f, true), WaveMove.Slant(2, -9f, true), WaveMove.Wall(3, true, 2), WaveMove.Ring(3, 26) } },
            // 5 Tempest Engine: rain curtains and lightning slants
            new BossWaveStyle { style = BossShotStyle.Bolt, speed = 3.7f, pitch = .5f, moves = new[] {
                WaveMove.Wall(0), WaveMove.Slant(1, 11f), WaveMove.Wall(2, true), WaveMove.Slant(2, -11f), WaveMove.Wall(3, false, 2) } },
            // 6 Dune-Eater: closing pincers (two slants that close on the gap), sand walls
            new BossWaveStyle { style = BossShotStyle.Shard, speed = 3.4f, pitch = .52f, moves = new[] {
                WaveMove.Slant(0, 12f), WaveMove.Slant(0, -12f), WaveMove.Wall(1, true), WaveMove.Ring(2, 26, sweep: true), WaveMove.Slant(3, 16f, rows: 2), WaveMove.Slant(3, -16f, rows: 2) } },
        };
    }
}

// ---------------------------------------------------------------- the voice
public sealed class BossWaveVoice
{
    readonly IPatternHost host;
    readonly BossWaveStyle style;
    readonly int[] parts;
    readonly int world;
    public readonly WaveGaps gaps = new WaveGaps();
    int index, sweepDir = 1;
    public int Waves { get; private set; }
    public int Skipped { get; private set; }
    public float LastGapCenter { get; private set; }
    public float LastArrival { get; private set; }
    public float LastClearWidth { get; private set; }
    public float LastSpeed { get; private set; }
    public int LastBullets { get; private set; }
    public WaveKind LastKind { get; private set; }
    public float LastSpawnGame { get; private set; }
    public int LastTravelBeats { get; private set; }

    public BossWaveVoice(int world, IPatternHost host, int[] parts)
    {
        this.world = world;
        this.host = host;
        this.parts = parts != null && parts.Length > 0 ? parts : new[] { 0 };
        style = WaveMoves.For(world);
    }

    public void ResetPhrase() { index = 0; }

    // Clear gap width (ship widths) for a world at an intensity: relaxed .. the ladder's own target for that boss,
    // never under WaveGaps.MinMultiple.
    public static float GapMultiple(int world, float intensity)
    {
        float target = Mathf.Max(WaveGaps.MinMultiple, BossLadder.GapTarget[Mathf.Clamp(world, 0, 6)]);
        return Mathf.Lerp(target + 1.2f, target, WaveRhythm.Squeeze(intensity));
    }

    // One wave spawning now (a grid sixteenth). `spb` music seconds per beat, `gameNow` the gameplay clock,
    // `telegraph` marks the gap lane. Returns the bullets fired (0 when the wave was skipped).
    public int Emit(float intensity, double spb, float gameNow, bool telegraph, float lateSeconds = 0f)
    {
        int tier = WaveRhythm.Tier(intensity);
        var moves = style.moves;
        WaveMove m = default;
        int tries = 0;
        for (; tries < moves.Length; tries++)
        {
            m = moves[index++ % moves.Length];
            if (m.minTier <= tier) break;
        }
        if (m.minTier > tier) { m = moves[0]; }
        return EmitMove(m, intensity, spb, gameNow, telegraph, lateSeconds);
    }

    // A specific move now (the accent voice uses it for a heavy wave); same fairness rules as Emit.
    public int EmitMove(WaveMove m, float intensity, double spb, float gameNow, bool telegraph, float lateSeconds = 0f)
    {
        var player = host.Player;
        float scale = host.ShotSpeedScale;
        float D = Mathf.Max(1f, host.SpawnTop - player.y);
        float nominal = style.speed * (1f + .12f * Mathf.Clamp01(intensity));
        // quantise: the wave reaches the ship row an exact number of beats after it leaves, so the danger lands on a beat
        int k = Mathf.Max(2, Mathf.RoundToInt((float)(D / (nominal * scale) / spb)));
        bool radial = m.kind == WaveKind.Ring || m.kind == WaveKind.Fan;
        if (radial)
        {
            // a ring's gap corridor reaches the ship row after its own whole number of beats
            var muzzle = host.Emitter(parts[Mathf.Clamp(m.part, 0, parts.Length - 1) % parts.Length]);
            float d0 = Mathf.Max(1f, Vector2.Distance(muzzle, new Vector2(0f, player.y)));
            k = Mathf.Max(2, Mathf.RoundToInt((float)(d0 / (nominal * scale) / spb)));
        }
        float spd = (float)(D / (k * spb));                      // game units/s the wave really travels (before ShotSpeedScale)
        float arrival = gameNow + (float)(k * spb) - lateSeconds;
        float mult = GapMultiple(world, intensity);
        float r = style.style == BossShotStyle.Bolt ? .13f : .12f;
        float clear = mult * 2f * PatternSim.ShipRadius;
        float width = clear;
        float lim = Mathf.Max(0f, host.ShipHalfWidth - .3f);
        float thick = (2f * PatternSim.ShipRadius + 2f * r + .3f) / Mathf.Max(.5f, spd);
        // where the pattern would like the gap: a steady sweep, or a hop to the far side of the ship
        float want;
        if (m.sweep)
        {
            want = sweepDir * .6f;
        }
        else
        {
            want = (Waves % 2 == 0 ? 1f : -1f) * (player.x >= 0f ? -1f : 1f) * .9f;
        }
        float c = gaps.Choose(player.x, gameNow, arrival, width, thick, lim, want);
        if (m.sweep && Mathf.Abs(c) >= lim - .02f) sweepDir = -sweepDir;
        else if (m.sweep && Mathf.Abs(gaps.lastShift) < .05f) sweepDir = -sweepDir;   // pinned: turn round
        Waves++;
        int fired;
        switch (m.kind)
        {
            case WaveKind.Ring:
            case WaveKind.Fan:
                fired = Radial(m, c, clear, r, k, (float)spb, telegraph);
                break;
            default:
                fired = Row(m, c, clear, r, spd, k, (float)spb, telegraph, gameNow);
                break;
        }
        LastSpawnGame = gameNow; LastTravelBeats = k;
        LastGapCenter = c; LastArrival = arrival; LastClearWidth = clear; LastSpeed = spd; LastBullets = fired; LastKind = m.kind;
        return fired;
    }

    PShot Shot()
    {
        return new PShot { style = style.style, rail = BossRailMode.Absorb };
    }

    int Row(WaveMove m, float c, float clear, float r, float spd, int k, float spb, bool telegraph, float gameNow)
    {
        float scale = host.ShotSpeedScale;
        float T = k * spb;
        float driftRad = m.kind == WaveKind.Slant ? m.drift * Mathf.Deg2Rad : 0f;
        float vx = Mathf.Tan(driftRad) * spd;                    // speed units before ShotSpeedScale; same unit as `spd`
        // wave speed along y is `spd`; the gap centre at the spawn line is where the drift takes it from
        float x0 = c - vx * T;
        float centres = clear + 2f * r;                          // gap between the centres of the bullets either side
        float edge = host.RailEdge + .3f;
        float lo = -edge - vx * T, hi = edge - vx * T;           // spawn x range whose bullets land inside the rails
        int fired = 0;
        var sh = Shot();
        int rows = Mathf.Max(1, m.rows);
        float pitch = style.pitch;
        for (int row = 0; row < rows; row++)
        {
            float y = host.SpawnTop + row * style.rowSep;
            float ox = (row % 2) * pitch * .5f;                  // staggered rows close the diagonals a ship could squeeze through
            for (float x = x0 - centres * .5f - ox; x > lo; x -= pitch) { Put(new Vector2(x, y), new Vector2(vx, -spd), ref sh, scale); fired++; }
            for (float x = x0 + centres * .5f + ox; x < hi; x += pitch) { Put(new Vector2(x, y), new Vector2(vx, -spd), ref sh, scale); fired++; }
        }
        host.Emitted();
        if (telegraph) host.Mark(new Vector2(c, host.ZoneTop + .3f), new Vector2(c, host.ZoneBottom - .3f), Mathf.Max(.1f, clear * .5f - .12f), Mathf.Min(T, 1.6f));
        return fired;
    }

    // Ring / fan: the gap is a corridor along the heading from the muzzle to (c, ship row).
    int Radial(WaveMove m, float c, float clear, float r, int kk, float spb, bool telegraph)
    {
        float scale = host.ShotSpeedScale;
        var from = host.Emitter(parts[Mathf.Clamp(m.part, 0, parts.Length - 1) % parts.Length]);
        var player = host.Player;
        var target = new Vector2(c, player.y);
        float g = Mathf.Atan2(target.y - from.y, target.x - from.x) * Mathf.Rad2Deg;
        g = Mathf.Clamp(g, -165f, -15f);
        float dMin = Mathf.Max(2f, from.y - host.ZoneTop);       // the nearest the ship can be: the gap is narrowest there
        float need = clear + 2f * r;
        float theta = 2f * Mathf.Asin(Mathf.Clamp(need / (2f * dMin), .02f, .98f)) * Mathf.Rad2Deg;
        float step = 360f / Mathf.Max(8, m.count);
        step = Mathf.Min(step, 24f);
        // the speed that lands the gap corridor's centre at the ship row on a beat
        float dist = Mathf.Max(1f, Vector2.Distance(from, target));
        float rs = (float)(dist / (kk * spb));
        float arc = m.kind == WaveKind.Fan ? Mathf.Max(step, m.arc) : 180f;
        var sh = Shot();
        int fired = 0;
        for (int side = -1; side <= 1; side += 2)
            for (int j = 0; ; j++)
            {
                float off = theta * .5f + j * step;
                if (off > arc) break;
                float h = g + side * off;
                if (Mathf.Sin(h * Mathf.Deg2Rad) > -.1f) break;     // the downward half only
                Put(from, new Vector2(Mathf.Cos(h * Mathf.Deg2Rad), Mathf.Sin(h * Mathf.Deg2Rad)) * rs, ref sh, scale);
                fired++;
            }
        host.Emitted();
        LastSpeed = rs;
        if (telegraph) host.Mark(from + (target - from).normalized * 1.5f, target + (target - from).normalized * 1.2f, Mathf.Max(.08f, clear * .5f - .12f), Mathf.Min(kk * spb, 1.6f));
        return fired;
    }

    // `v` is in game units; the host multiplies by its ShotSpeedScale, so divide it out.
    void Put(Vector2 at, Vector2 v, ref PShot sh, float scale)
    {
        host.Fire(at, v / Mathf.Max(.01f, scale), ref sh);
    }
}
