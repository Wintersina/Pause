using UnityEngine;

// BOSS BULLET WAVES (docs/boss-bullet-waves.md): every boss attack is a WAVE of bullets with a GAP the ship can escape
// through, spawned on the music's beat grid. Pure geometry + rhythm; no engine objects beyond Vector2 / Mathf, so the
// same code runs in the game (BossActor host) and in the tests (PatternSim host).
//
//   WaveGaps     the fairness core: picks where a wave's gap goes so the ship can always reach it in time
//   WaveRhythm   which sixteenths of a bar spawn a wave, from the section kind and the music's intensity
//   WaveMoves    each world's menu of wave shapes, unlocked as the intensity rises
//   BossWaveSources  which body part of each boss fires each shape (its measured muzzles, the pose that shows it)
//   WaveDeploy   the fan-out: a row's bullets leave the boss's muzzles and spread into the row just under the boss
//   BossWaveVoice  turns one move into bullets (walls, slanted walls, rings, fans), speeds quantised so a wave REACHES
//                  the ship on a beat as well as leaving the boss on one
//
// EVERY bullet starts at one of the boss's own muzzles, in the drawing on screen: nothing comes in from a screen edge.

public enum WaveKind
{
    Wall,    // a flat row of bullets, sprayed out of the muzzles into a line under the boss, then falling; one gap
    Slant,   // the same row tilted (drift degrees): one end leads; crossing waves are slants of opposite sign
    Ring,    // a radial ring from a muzzle with one gap (only the downward half fires)
    Fan,     // an arc of bullets from a muzzle with one gap (a ring with a limited arc)
}

public struct WaveMove
{
    public WaveKind kind;
    public bool sweep;          // the gap travels steadily one way (reversing at the lane's end) instead of hopping
    public float drift;         // slant: tilt of the row in degrees (sign = which end leads)
    public float arc;           // fan: half arc in degrees each side of the gap
    public int count;           // ring: bullets round the full circle
    public int minTier;         // intensity tier (0..3) that unlocks it
    public int rows;            // wall / slant: rows deep (a thicker wave)

    public static WaveMove Wall(int tier, bool sweep = false, int rows = 1) => new WaveMove { kind = WaveKind.Wall, minTier = tier, sweep = sweep, rows = rows };
    public static WaveMove Slant(int tier, float drift, bool sweep = false, int rows = 1) => new WaveMove { kind = WaveKind.Slant, minTier = tier, drift = drift, sweep = sweep, rows = rows };
    public static WaveMove Ring(int tier, int count = 24, bool sweep = false) => new WaveMove { kind = WaveKind.Ring, minTier = tier, count = count, sweep = sweep };
    public static WaveMove Fan(int tier, float arc, int count = 24, bool sweep = false) => new WaveMove { kind = WaveKind.Fan, minTier = tier, arc = arc, count = count, sweep = sweep };
}

// What a world's waves look like beyond their shape: bullet style, speed, spacing.
public sealed class BossWaveStyle
{
    public BossShotStyle style = BossShotStyle.Shard;
    public int art;                     // the world's wave drawing: its boss's attack variant (BossArt.AttackShot), 0 = the shared pair
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

    public void Reset() { has = false; Count = 0; shadows = 0; }

    // ---- ricochet shadows (BossBounce): x ranges (ship-centre x, already padded) a bounced bullet sweeps through the
    // ship's band in a game-time window. Every gap is planned so the ship never has to cross a live shadow, and a bullet
    // is only made bouncy when its shadow misses the x range the ship uses for the wave it belongs to (Certify).
    public const int MaxShadows = 48;
    readonly float[] shT0 = new float[MaxShadows], shT1 = new float[MaxShadows], shX0 = new float[MaxShadows], shX1 = new float[MaxShadows];
    int shadows;
    public int Shadows => shadows;
    public int PeakShadows { get; private set; }
    // The x range (ship centres) the last Choose let the ship use: where it could start, the way to the gap, the gap.
    public float PathLo { get; private set; }
    public float PathHi { get; private set; }

    // Drops the shadows that have ended by game time `now`.
    public void ExpireShadows(float now)
    {
        for (int i = shadows - 1; i >= 0; i--)
            if (shT1[i] < now)
            {
                shadows--;
                shT0[i] = shT0[shadows]; shT1[i] = shT1[shadows]; shX0[i] = shX0[shadows]; shX1[i] = shX1[shadows];
            }
    }

    // May a bounced bullet sweep [x0, x1] (ship centres) of the band? Not where the ship goes for the last wave, and
    // only while there is room to remember it.
    public bool Certify(float x0, float x1) => shadows < MaxShadows && (x1 < PathLo || x0 > PathHi);

    public void AddShadow(float t0, float t1, float x0, float x1)
    {
        if (shadows >= MaxShadows) return;
        shT0[shadows] = t0; shT1[shadows] = t1; shX0[shadows] = x0; shX1[shadows] = x1;
        shadows++;
        if (shadows > PeakShadows) PeakShadows = shadows;
    }

    public void ShadowAt(int i, out float t0, out float t1, out float x0, out float x1) { t0 = shT0[i]; t1 = shT1[i]; x0 = shX0[i]; x1 = shX1[i]; }

    // Shrinks the ship's reach [el, eh] so it stops short of every live shadow beside where it starts ([lo, hi]).
    void ClipToShadows(float lo, float hi, ref float el, ref float eh)
    {
        for (int i = 0; i < shadows; i++)
        {
            if (shX0[i] > hi) eh = Mathf.Min(eh, shX0[i]);
            else if (shX1[i] < lo) el = Mathf.Max(el, shX1[i]);
        }
        if (el > eh) el = eh = Mathf.Clamp(.5f * (lo + hi), el, eh);
    }

    // Picks the centre of a new wave's gap (clear width `width`) that reaches the ship's row at game time `arrival`.
    // `thick` is how long the previous wave takes to pass the ship. `want` in -1..1 is where the pattern would like the
    // gap to go within what is allowed (-1 = as far left as is fair, 1 = as far right); the result is always fair.
    //
    // It tracks the interval of x the ship can be at when the previous wave passes (its gap, narrowed by what the ship
    // could actually reach from the wave before it). That interval grows by PlanSpeed * the time between the waves
    // (less the previous wave's passing time and a reaction margin) and the new gap must overlap it by at least a ship
    // width. A wave after a quiet spell (the previous one has already passed) is judged from the ship's own position.
    //
    // `front` says how the wave meets the ship's row away from its gap: a flat wall everywhere at once, a ring from a
    // muzzle off to one side reaches the row under that muzzle FIRST, a tilted row reaches one side first. Where the
    // ship's path from the last gap to the new one meets the wave early, the ship has that much less time.
    public float Choose(float playerX, float now, float arrival, float width, float thick, float lim, float want, WaveFront front = default)
    {
        ExpireShadows(now);
        float c0 = Pick(playerX, now, arrival, width, thick, lim, want, 0f, out _, out _, out _, out _);
        float early = front.Early(Lo(playerX, now, lim), Hi(playerX, now, lim), c0);
        float c = c0;
        float el = 0f, eh = 0f, lo = 0f, hi = 0f;
        for (int i = 0; i < 3; i++)
        {
            c = Pick(playerX, now, arrival, width, thick, lim, want, early, out el, out eh, out lo, out hi);
            float e2 = front.Early(lo, hi, c);
            if (e2 <= early + .005f) break;
            early = e2;
        }
        LastEarly = early;
        float room = Mathf.Max(0f, (width - ShipWidth) * .5f);
        lastShift = c - .5f * (lo + hi);
        float nl = Mathf.Max(el, c - room), nh = Mathf.Min(eh, c + room);
        if (nl > nh) nl = nh = Mathf.Clamp(c, el, eh);
        PathLo = Mathf.Min(Mathf.Min(lo, nl), c); PathHi = Mathf.Max(Mathf.Max(hi, nh), c);
        if (!has || arrival >= prevArrival)
        {
            prevCenter = c; prevWidth = width; prevArrival = arrival;
            reachLo = nl; reachHi = nh;
        }
        has = true;
        Count++;
        return c;
    }

    public float LastEarly { get; private set; }

    bool Chained(float now) => has && prevArrival >= now - .02f;
    float Lo(float playerX, float now, float lim) => Chained(now) ? reachLo : Mathf.Clamp(playerX, -lim, lim);
    float Hi(float playerX, float now, float lim) => Chained(now) ? reachHi : Mathf.Clamp(playerX, -lim, lim);

    float Pick(float playerX, float now, float arrival, float width, float thick, float lim, float want, float early,
               out float el, out float eh, out float lo, out float hi)
    {
        float travel;
        if (Chained(now))
        {
            lo = reachLo; hi = reachHi;
            travel = arrival - early - prevArrival - thick - Reaction;
        }
        else
        {
            lo = hi = Mathf.Clamp(playerX, -lim, lim);
            travel = arrival - early - now - Reaction;
        }
        float e = PlanSpeed * Mathf.Max(0f, travel);
        el = Mathf.Max(-lim, lo - e); eh = Mathf.Min(lim, hi + e);
        if (el > eh) el = eh = Mathf.Clamp(.5f * (lo + hi), -lim, lim);
        if (shadows > 0) ClipToShadows(lo, hi, ref el, ref eh);
        float room = Mathf.Max(0f, (width - ShipWidth) * .5f);
        // leave some slack: the ship must be able to overlap the new gap by a few hair widths, not by a single point
        float slack = Mathf.Min(room, Slack);
        float cl = Mathf.Max(-lim, el - room + slack), ch = Mathf.Min(lim, eh + room - slack);
        if (cl > ch) cl = ch = Mathf.Clamp(.5f * (el + eh), -lim, lim);
        float c = Mathf.Lerp(cl, ch, (Mathf.Clamp(want, -1f, 1f) + 1f) * .5f);
        lastMaxShift = (ch - cl) * .5f;
        return c;
    }
}

// How a wave's front meets the ship's row away from its gap centre `c` (WaveGaps.Choose): relative arrival times.
public struct WaveFront
{
    public int kind;            // 0 flat (a wall: everywhere at once), 1 radial (a ring / fan from muzzle x `fx`), 2 tilted row
    public float fx, h, rs;     // radial: the muzzle's x, its height above the ship row, the ring's speed
    public float tilt, spd;     // tilted: tan of the tilt, the fall speed

    public static WaveFront Radial(float fx, float h, float rs) => new WaveFront { kind = 1, fx = fx, h = Mathf.Max(.5f, h), rs = Mathf.Max(.5f, rs) };
    public static WaveFront Tilted(float tilt, float spd) => new WaveFront { kind = tilt != 0f ? 2 : 0, tilt = tilt, spd = Mathf.Max(.5f, spd) };

    // seconds the wave reaches x before it reaches c
    public float Lead(float x, float c)
    {
        if (kind == 1) return (Dist(c) - Dist(x)) / rs;
        if (kind == 2) return -tilt * (x - c) / spd;
        return 0f;
    }

    float Dist(float x) { float d = x - fx; return Mathf.Sqrt(h * h + d * d); }

    // The most any point of the ship's path (from anywhere in [lo, hi] to c) meets the wave before c does.
    public float Early(float lo, float hi, float c)
    {
        if (kind == 0) return 0f;
        float a = Mathf.Min(lo, c), b = Mathf.Max(hi, c);
        float worst = Mathf.Max(Lead(a, c), Lead(b, c));
        if (kind == 1) worst = Mathf.Max(worst, Lead(Mathf.Clamp(fx, a, b), c));
        return Mathf.Max(0f, worst);
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
            new BossWaveStyle { style = BossShotStyle.Shard, art = 2, speed = 3.0f, pitch = .6f, moves = new[] {
                WaveMove.Ring(0, 24, sweep: true), WaveMove.Fan(1, 50f, 28, sweep: true), WaveMove.Wall(2, true), WaveMove.Slant(3, 14f), WaveMove.Slant(3, -14f) } },
            // 1 Hoarfrost Leviathan: cold-snap rings with a gap, icicle rows, glacier slabs crossing
            new BossWaveStyle { style = BossShotStyle.Shard, art = 3, speed = 2.9f, pitch = .55f, moves = new[] {
                WaveMove.Ring(0, 22), WaveMove.Wall(1), WaveMove.Ring(2, 26, sweep: true), WaveMove.Slant(3, 10f, rows: 2), WaveMove.Slant(3, -10f, rows: 2) } },
            // 2 Bloom Queen: vine fans and hedges of seeds with a path through them, the path sweeping like a vine
            new BossWaveStyle { style = BossShotStyle.Bolt, art = 1, speed = 3.1f, pitch = .6f, moves = new[] {
                WaveMove.Fan(0, 55f, 22), WaveMove.Wall(1, true), WaveMove.Fan(2, 70f, 30, sweep: true), WaveMove.Slant(3, 8f, true, 2), WaveMove.Slant(3, -8f, true, 2) } },
            // 3 Cinder Drake: flame fans and fire walls with a drifting gap
            new BossWaveStyle { style = BossShotStyle.Shard, art = 1, speed = 3.2f, pitch = .55f, moves = new[] {
                WaveMove.Fan(0, 45f, 26), WaveMove.Wall(1, true), WaveMove.Fan(2, 65f, 34, sweep: true), WaveMove.Slant(3, 12f, rows: 2), WaveMove.Slant(3, -12f, rows: 2) } },
            // 4 Iron Kraken: the TIDAL WALLS (sweeping gap), surf crossing at the peak, a swell of rings from the beak
            new BossWaveStyle { style = BossShotStyle.Shard, art = 2, speed = 3.3f, pitch = .5f, moves = new[] {
                WaveMove.Wall(0, true), WaveMove.Wall(1, true), WaveMove.Slant(2, 9f, true), WaveMove.Slant(2, -9f, true), WaveMove.Wall(3, true, 2), WaveMove.Ring(3, 26) } },
            // 5 Tempest Engine: rain curtains and lightning slants, and an arc discharge fanned from the core drum (its
            //   bolts are the only ones that reach the rails: the ricochets, BossBounce)
            new BossWaveStyle { style = BossShotStyle.Bolt, art = 1, speed = 3.7f, pitch = .5f, moves = new[] {
                WaveMove.Wall(0), WaveMove.Slant(1, 11f), WaveMove.Fan(1, 55f, 24), WaveMove.Wall(2, true), WaveMove.Slant(2, -11f), WaveMove.Wall(3, false, 2) } },
            // 6 Dune-Eater: closing pincers (two slants that close on the gap), sand walls
            new BossWaveStyle { style = BossShotStyle.Shard, art = 1, speed = 3.4f, pitch = .52f, moves = new[] {
                WaveMove.Slant(0, 12f), WaveMove.Slant(0, -12f), WaveMove.Wall(1, true), WaveMove.Ring(2, 26, sweep: true), WaveMove.Slant(3, 16f, rows: 2), WaveMove.Slant(3, -16f, rows: 2) } },
        };
    }
}

// ---------------------------------------------------------------- where each wave leaves the boss
// Every bullet of a wave is fired FROM THE BOSS: from the measured muzzles (BossEmitterTable) of the body part that
// visibly does it, in the drawing on screen. One source per wave shape and boss, chosen to match the boss's own art
// and attacks (docs/boss-bullet-waves.md has the table): the pose a source is told / fired in is the tell of the
// boss attack that already fires from that part (BossCatalog), so the drawing on screen always matches what comes out.
public struct WaveSource
{
    public int[] parts;      // emitter indices (BossEmitterTable), never empty
    public int pose;         // the tell pose (0..2) whose drawings show this part working
    public float holdBeats;  // beats the fire drawing is held (a flame breath is a sustained stream, not a blink)
    public string label;     // what it is, for the docs / tests
}

public sealed class BossWaveSources
{
    public WaveSource wall, wallAlt, ring, fan, accent;
    public bool hasAlt;
    public int[] all = new int[0];   // every part any source uses (the boss's flash / charge slots)

    // world: wall, wall (alternate side, or null), ring, fan, accent -- body-part names, first found wins per list
    static readonly string[][][] Names =
    {
        // 0 Void Archon: pod nozzles spray the curtains, the reactor core pulses rings, the chin cannon fans
        new[] { new[] { "PodL", "PodR" }, null, new[] { "Core" }, new[] { "Chin" }, new[] { "Chin" } },
        // 1 Hoarfrost Leviathan: the icicle jaw breathes the rows, fans and cold-snap rings (rings from the blowhole
        //   crown, at the top of its head, sweep down through its body and arrive so unevenly across the ship's row
        //   that the gap chain could not be proved: BossWaveTest found dead ends)
        new[] { new[] { "Jaw" }, null, new[] { "Jaw" }, new[] { "Jaw" }, new[] { "Jaw" } },
        // 2 Bloom Queen: the acid cannons lay the seed hedges, the stinger whips the vine fans, the bulb the spore rings
        new[] { new[] { "CannonL", "CannonR" }, null, new[] { "Bulb" }, new[] { "Stinger" }, new[] { "Stinger" } },
        // 3 Cinder Drake: the twin brass shoulder cannons and the chest furnace fire the slug walls, the maw breathes
        //   the flame fans (a held fire-breath pose), the furnace hurls the bombs
        new[] { new[] { "CannonL", "CannonR", "Furnace" }, null, new[] { "Furnace" }, new[] { "Jaw" }, new[] { "Furnace" } },
        // 4 Iron Kraken: the tidal walls come off one cluster of cannon arms, then the other; the beak jets rings / fans
        new[] { new[] { "LeftA", "LeftB" }, new[] { "RightA", "RightB" }, new[] { "Beak" }, new[] { "Beak" }, new[] { "Beak" } },
        // 5 Tempest Engine: the Tesla coils rain the curtains, the core drum flares the rings / fans
        new[] { new[] { "CoilL", "CoilR" }, null, new[] { "Core" }, new[] { "Core" }, new[] { "Core" } },
        // 6 Dune-Eater: the maw blasts the sand walls and pincers, the boom crane drops the mines
        new[] { new[] { "Maw" }, null, new[] { "Maw" }, new[] { "Maw" }, new[] { "Boom" } },
    };

    // Sustained fire (beats) for the breath fans: the Drake's flame and the Leviathan's frost breath.
    static readonly float[] FanHold = { 0f, .5f, 0f, 1f, 0f, 0f, 0f };

    public static BossWaveSources For(int world, BossDef boss, int fallbackPart)
    {
        world = Mathf.Clamp(world, 0, Names.Length - 1);
        var n = Names[world];
        var s = new BossWaveSources();
        s.wall = Make(boss, n[0], fallbackPart, 0f, "wall");
        s.hasAlt = n[1] != null;
        s.wallAlt = s.hasAlt ? Make(boss, n[1], fallbackPart, 0f, "wall") : s.wall;
        s.ring = Make(boss, n[2], fallbackPart, 0f, "ring");
        s.fan = Make(boss, n[3], fallbackPart, FanHold[world], "fan");
        s.accent = Make(boss, n[4], fallbackPart, 0f, "accent");
        var all = new System.Collections.Generic.List<int>();
        foreach (var src in new[] { s.wall, s.wallAlt, s.ring, s.fan, s.accent })
            foreach (int p in src.parts) if (!all.Contains(p)) all.Add(p);
        s.all = all.ToArray();
        return s;
    }

    static WaveSource Make(BossDef boss, string[] names, int fallbackPart, float hold, string label)
    {
        var parts = new System.Collections.Generic.List<int>();
        foreach (var name in names)
        {
            int p = BossEmitters.Part(boss, name);
            if (p >= 0 && !parts.Contains(p)) parts.Add(p);
        }
        if (parts.Count == 0) parts.Add(Mathf.Max(0, fallbackPart));
        var src = new WaveSource { parts = parts.ToArray(), holdBeats = hold, label = label + ":" + string.Join("+", names) };
        src.pose = PoseOf(boss, parts[0]);
        return src;
    }

    // The tell of the boss attack that fires from `part` (its drawings show that part at work); 0 when none does.
    public static int PoseOf(BossDef boss, int part)
    {
        var attacks = boss != null ? boss.DefaultAttacks : null;
        if (attacks == null) return 0;
        string name = BossEmitters.PartName(boss, part);
        for (int i = 0; i < attacks.Length; i++)
        {
            var e = attacks[i].emitters;
            if (e == null) continue;
            for (int j = 0; j < e.Length; j++) if (e[j] == name) return attacks[i].tell;
        }
        return 0;
    }
}

// ---------------------------------------------------------------- the deploy (fan-out) geometry
// A wall, a curtain, a rain drop or a lattice row is not dropped in from the top of the screen: each bullet leaves a
// muzzle on the boss, flies out to its place in a FORMATION LINE just under the boss (PShot.turnAt), and there turns
// down its lane. The formation line sits between the lowest muzzle and the ship's ceiling (clear of the ship's hull,
// so the spray never touches it); below it the wave falls exactly like the old full-width wall, so the gap the ship
// threads is the same shape it always was.
public static class WaveDeploy
{
    public const float HullClear = PatternSim.ShipRadius + .14f + .12f;   // ship radius + bullet radius + a margin

    // The formation line for muzzles whose lowest point is at `emY`.
    public static float FormY(IPatternHost host, float emY)
    {
        float top = host.ZoneTop + HullClear;
        float y = Mathf.Lerp(host.ZoneTop, emY, .45f);
        return Mathf.Min(emY - .08f, Mathf.Max(top, y));
    }

    // The fan-out time for a spray reaching `dist` from its muzzle: quick, but never a teleport.
    public static float FanOut(float dist) => Mathf.Clamp(dist / 8f, .25f, .55f);

    // The muzzle (of `parts`) nearest along x to `x`.
    public static Vector2 Nearest(IPatternHost host, int[] parts, float x)
    {
        Vector2 best = host.Emitter(parts[0]);
        for (int i = 1; i < parts.Length; i++)
        {
            var e = host.Emitter(parts[i]);
            if (Mathf.Abs(e.x - x) < Mathf.Abs(best.x - x)) best = e;
        }
        return best;
    }

    public static float LowestY(IPatternHost host, int[] parts)
    {
        float y = float.MaxValue;
        for (int i = 0; i < parts.Length; i++) y = Mathf.Min(y, host.Emitter(parts[i]).y);
        return y;
    }
}

// ---------------------------------------------------------------- the voice
public sealed class BossWaveVoice
{
    readonly IPatternHost host;
    readonly BossWaveStyle style;
    readonly BossWaveSources sources;
    readonly int world;
    public readonly WaveGaps gaps = new WaveGaps();
    int index, sweepDir = 1;
    bool altWall;
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
    public WaveSource LastSource { get; private set; }
    public float LastFormY { get; private set; }
    public float LastHold { get; private set; }
    public BossWaveSources Sources => sources;

    public BossWaveVoice(int world, IPatternHost host, BossWaveSources sources)
    {
        this.world = world;
        this.host = host;
        this.sources = sources;
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

    WaveMove Pick(float intensity, ref int at)
    {
        int tier = WaveRhythm.Tier(intensity);
        var moves = style.moves;
        WaveMove m = default;
        for (int tries = 0; tries < moves.Length; tries++)
        {
            m = moves[at++ % moves.Length];
            if (m.minTier <= tier) return m;
        }
        return moves[0];
    }

    public WaveSource SourceFor(WaveMove m, bool alt) =>
        m.kind == WaveKind.Ring ? sources.ring : m.kind == WaveKind.Fan ? sources.fan : (alt && sources.hasAlt ? sources.wallAlt : sources.wall);

    // The source the NEXT wave will leave from (for its tell on the beat before), without using it up.
    public WaveSource Peek(float intensity)
    {
        int at = index;
        var m = Pick(intensity, ref at);
        return SourceFor(m, m.kind == WaveKind.Wall || m.kind == WaveKind.Slant ? altWall : false);
    }

    // One wave spawning now (a grid sixteenth). `spb` music seconds per beat, `gameNow` the gameplay clock,
    // `telegraph` marks the gap lane. Returns the bullets fired (0 when the wave was skipped).
    public int Emit(float intensity, double spb, float gameNow, bool telegraph, float lateSeconds = 0f, float holdCap = float.MaxValue)
    {
        var m = Pick(intensity, ref index);
        return EmitMove(m, intensity, spb, gameNow, telegraph, lateSeconds, holdCap);
    }

    // A specific move now (the accent voice uses it for a heavy wave); same fairness rules as Emit.
    // `lateSeconds`: how far past its sixteenth the wave is seen (the audio clock's step); its flight is that much
    // shorter, so it still reaches the ship's row on the beat.
    public int EmitMove(WaveMove m, float intensity, double spb, float gameNow, bool telegraph, float lateSeconds = 0f, float holdCap = float.MaxValue)
    {
        lateSeconds = Mathf.Clamp(lateSeconds, 0f, .1f);
        bool row = m.kind == WaveKind.Wall || m.kind == WaveKind.Slant;
        var src = SourceFor(m, row && altWall);
        if (row) altWall = !altWall;
        var player = host.Player;
        float scale = host.ShotSpeedScale;
        float nominal = style.speed * (1f + .12f * Mathf.Clamp01(intensity)) * scale;
        float r = style.style == BossShotStyle.Bolt ? .13f : .12f;
        float mult = GapMultiple(world, intensity);
        float clear = mult * 2f * PatternSim.ShipRadius;
        float lim = Mathf.Max(0f, host.ShipHalfWidth - .3f);
        float edge = host.RailEdge - r - .03f;
        int k;
        float spd, thick, fanOut = 0f, formY = 0f, tilt = 0f, lift = 0f;
        if (row)
        {
            // muzzles -> formation line -> straight down their lanes: the gap centre reaches the ship row k beats on
            float emY = WaveDeploy.LowestY(host, src.parts);
            formY = WaveDeploy.FormY(host, emY);
            float reach = 0f;
            for (int i = 0; i < src.parts.Length; i++)
            {
                var e = host.Emitter(src.parts[i]);
                reach = Mathf.Max(reach, Mathf.Max(Mathf.Abs(edge - e.x), Mathf.Abs(-edge - e.x)) / src.parts.Length);
            }
            tilt = m.kind == WaveKind.Slant ? Mathf.Tan(m.drift * Mathf.Deg2Rad) : 0f;
            lift = Mathf.Abs(tilt) * 2f * edge;                      // the tilted row's lead, in u of fall
            float D = Mathf.Max(1f, formY - player.y) + lift;
            // every row falls at the SAME speed (rows of different speeds would catch each other up and cross); the
            // beat lock is in the fan-out instead: the spray takes between its minimum and a beat more, so the gap
            // centre reaches the ship's row exactly k beats after the wave left
            spd = nominal;
            float flight = D / spd;
            float minFan = WaveDeploy.FanOut(reach);
            k = Mathf.Max(2, Mathf.CeilToInt((float)((minFan + flight + lateSeconds) / spb) - 1e-3f));
            fanOut = (float)(k * spb) - lateSeconds - flight;
            // passing the ship: one row, the tilt across the gap, and every row behind the first
            thick = (2f * PatternSim.ShipRadius + 2f * r + .3f + Mathf.Abs(tilt) * (clear + 2f * r) + (Mathf.Max(1, m.rows) - 1) * style.rowSep) / Mathf.Max(.5f, spd);
        }
        else
        {
            // a ring's gap corridor reaches the ship row after its own whole number of beats
            var muzzle = host.Emitter(src.parts[0]);
            float d0 = Mathf.Max(1f, Vector2.Distance(muzzle, new Vector2(0f, player.y)));
            k = Mathf.Max(2, Mathf.RoundToInt((float)(d0 / nominal / spb)));
            spd = (float)(d0 / (k * spb - lateSeconds));
            thick = (2f * PatternSim.ShipRadius + 2f * r + .3f) / Mathf.Max(.5f, spd);
        }
        float arrival = gameNow + (float)(k * spb) - lateSeconds;
        // where the pattern would like the gap: a steady sweep, or a hop to the far side of the ship
        float want = m.sweep ? sweepDir * .6f : (Waves % 2 == 0 ? 1f : -1f) * (player.x >= 0f ? -1f : 1f) * .9f;
        WaveFront front;
        if (row) front = WaveFront.Tilted(tilt, spd);
        else
        {
            var mz = host.Emitter(src.parts[0]);
            front = WaveFront.Radial(mz.x, mz.y - player.y, spd);
        }
        float c = gaps.Choose(player.x, gameNow, arrival, clear, thick, lim, want, front);
        if (m.sweep && Mathf.Abs(c) >= lim - .02f) sweepDir = -sweepDir;
        else if (m.sweep && Mathf.Abs(gaps.lastShift) < .05f) sweepDir = -sweepDir;   // pinned: turn round
        Waves++;
        WaveEligible = WaveBouncy = 0;
        int fired = row
            ? Row(m, src, c, clear, r, spd, k, (float)spb, fanOut, formY, tilt, lift, edge, telegraph)
            : Radial(m, src, c, clear, r, k, (float)spb, lateSeconds, telegraph, gameNow);
        // the muzzles that fired flash (a breath holds its fire drawing, never into the next wave)
        LastHold = Mathf.Min(src.holdBeats * (float)spb, holdCap);
        for (int i = 0; i < src.parts.Length; i++) host.EmittedFrom(src.parts[i], LastHold);
        LastSpawnGame = gameNow; LastTravelBeats = k; LastSource = src; LastFormY = formY;
        LastGapCenter = c; LastArrival = arrival; LastClearWidth = clear; LastBullets = fired; LastKind = m.kind;
        if (row) LastSpeed = spd;
        return fired;
    }

    PShot Shot()
    {
        return new PShot { style = style.style, art = style.art, rail = BossRailMode.Absorb };
    }

    // ---- ricochets (BossBounce) ----
    public BounceSelector bounce;                       // the running pick (tests read its counts)
    public float BounceFraction = -1f;                  // < 0: BossBounce.FractionFor(world)
    public int WaveEligible { get; private set; }       // the last wave's bullets that meet a rail
    public int WaveBouncy { get; private set; }         // ... and of them made bouncy

    // Makes `sh` bouncy or plain for a straight bullet leaving `from` at velocity `v` (world units / s, already scaled):
    // picked by the accumulator, kept only if its ricochet shadow is certified fair (WaveGaps.Certify).
    void Ricochet(ref PShot sh, Vector2 from, Vector2 v, float r, float gameNow)
    {
        sh.rail = BossRailMode.Absorb; sh.bounces = 0; sh.bouncy = false;
        float edge = host.RailEdge;
        float floor = host.ZoneBottom - 1f;
        if (!BossBounce.MeetsRail(from, v, r, edge, floor)) return;
        WaveEligible++;
        if (!bounce.Offer(BounceFraction >= 0f ? BounceFraction : BossBounce.FractionFor(world))) return;
        float R = PatternSim.ShipRadius + r + BossBounce.ShadowMargin;
        // the gap chain is planned on the ship's row (WaveGaps): the shadow is the sweep through that row's band
        float row = host.Player.y, band = R + BossBounce.RowSlack;
        bool comes = BossBounce.Trace(from, v, r, edge, Mathf.Max(host.ZoneBottom - R, row - band), Mathf.Min(host.ZoneTop + R, row + band), out float t0, out float t1, out float x0, out float x1);
        if (comes)
        {
            float pad = BossBounce.ShadowPad(r, t1);
            x0 -= pad; x1 += pad;
            if (!gaps.Certify(x0, x1)) { bounce.Veto(); return; }
            gaps.AddShadow(gameNow + t0, gameNow + t1, x0, x1);
        }
        bounce.Take();
        WaveBouncy++;
        sh.rail = BossRailMode.Bounce; sh.bounces = BossBounce.MaxBounces; sh.bouncy = true;
    }

    // Wall / slant: a row of bullets with one gap, fired from the source muzzles. Each bullet flies from its nearest
    // muzzle to (x, formY), turns there and falls at `spd`. A slant is the same row TILTED: the bullets on one side
    // turn later, so the row sweeps down at an angle; the gap centre still lands on the beat.
    int Row(WaveMove m, WaveSource src, float c, float clear, float r, float spd, int k, float spb, float fanOut, float formY, float tilt, float lift, float edge, bool telegraph)
    {
        float scale = host.ShotSpeedScale;
        float centres = clear + 2f * r;                          // gap between the centres of the bullets either side
        int fired = 0;
        var sh = Shot();
        int rows = Mathf.Max(1, m.rows);
        float pitch = style.pitch;
        float T = k * spb;
        for (int row = 0; row < rows; row++)
        {
            float ox = (row % 2) * pitch * .5f;                  // staggered rows close the diagonals a ship could squeeze through
            float behind = row * style.rowSep / Mathf.Max(.5f, spd);
            for (int side = -1; side <= 1; side += 2)
                for (float x = c + side * (centres * .5f + ox); side < 0 ? x >= -edge : x <= edge; x += side * pitch)
                {
                    // a tilted row's lead: lift + tilt * (x - c) u of extra wait before it turns down, always >= 0
                    float turn = Mathf.Max(.12f, fanOut + (lift + tilt * (x - c)) / Mathf.Max(.5f, spd) + behind);
                    var from = WaveDeploy.Nearest(host, src.parts, x);
                    var to = new Vector2(x, formY);
                    sh.turnAt = turn;
                    sh.then = new Vector2(0f, -spd) / Mathf.Max(.01f, scale);
                    host.Fire(from, (to - from) / turn / Mathf.Max(.01f, scale), ref sh);
                    fired++;
                }
        }
        if (telegraph) host.Mark(new Vector2(c, host.ZoneTop + .3f), new Vector2(c, host.ZoneBottom - .3f), Mathf.Max(.1f, clear * .5f - .12f), Mathf.Min(T, 1.6f));
        return fired;
    }

    // Ring / fan: from the source muzzle; the gap is a corridor along the heading from the muzzle to (c, ship row).
    int Radial(WaveMove m, WaveSource src, float c, float clear, float r, int kk, float spb, float late, bool telegraph, float gameNow)
    {
        float scale = host.ShotSpeedScale;
        var from = host.Emitter(src.parts[0]);
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
        float rs = (float)(dist / (kk * spb - late));
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
                var v = new Vector2(Mathf.Cos(h * Mathf.Deg2Rad), Mathf.Sin(h * Mathf.Deg2Rad)) * rs;
                Ricochet(ref sh, from, v, r, gameNow);
                host.Fire(from, v / Mathf.Max(.01f, scale), ref sh);
                fired++;
            }
        LastSpeed = rs;
        if (telegraph) host.Mark(from + (target - from).normalized * 1.5f, target + (target - from).normalized * 1.2f, Mathf.Max(.08f, clear * .5f - .12f), Mathf.Min(kk * spb, 1.6f));
        return fired;
    }
}
