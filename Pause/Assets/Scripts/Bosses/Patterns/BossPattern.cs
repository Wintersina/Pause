using UnityEngine;

// BOSS BULLET-HELL PATTERNS: the data (this file) and the runner that turns it into shots (BossPatternRunner).
//
// A BossPattern is a few STREAMS that start at their own times and may overlap (a spiral under an aimed fan, a mine
// field with a closing pincer). A stream is one vocabulary item (StreamKind) with its numbers; every number a boss
// is tuned by lives in the stream, and every stream of every boss lives in BossPatternTables -- one place.
//
// The pattern core uses no engine objects beyond Vector2 / Mathf: the live game plays it through BossActor (the host
// fires pooled BossProjectiles), the tests and BossLadder play it through BossPatternSim (a flat flight model with the
// same rail rules), so the ladder's numbers and the fairness solver are measured on exactly the shots the game fires.
public enum StreamKind
{
    Aimed,     // `count` bolts at the ship from the part(s), `spread` degrees across; one part per volley when alternate
    Fan,       // `count` shots in `spread` degrees round `baseDeg` (default straight down), turning `amp` between volleys
    Spiral,    // rotating (spin deg/s, only the downward half fires) or pendulum (amp deg, period seconds) arms; mirror: the second part turns the other way
    Ring,      // a radial ring of `count` shots with a gap of `gap` degrees aimed `spread` degrees beside the ship; each ring turns `rotStep`
    Curtain,   // a wall of shots across the lane with a gap of `gap` u that steps up to `gapStep` u per wall
    Rain,      // shots dropped from the top into random lanes, always leaving lanes free
    Lattice,   // staggered rows of shots drifting `angle` degrees off straight down: a grid to thread (drifts the other way every `count` rows)
    Bomb,      // `count` slow bombs thrown at spots round the ship that burst after `fuse` seconds into a ring of `burst`
    Seeds,     // slow seeds that home on the ship for `homeSeconds` (turning `homeDeg` deg/s) and then fly straight
    Mines,     // `count` stationary mines on marked spots (never near the ship, `minSep` apart) that go after `fuse` seconds
    Pincer,    // two jaws (the part pair) aimed at either side of the ship, closing by `gapStep` u per volley
}

// One shot's extras on top of its position and velocity (BossProjectile.Launch).
public struct PShot
{
    public BossShotStyle style;
    public BossRailMode rail;
    public int bounces;
    public float homeDeg, homeSeconds;   // seeds: turn rate and how long it homes
    public float fuse;                   // > 0: it ends (or bursts) after this long
    public int burst;                    // > 0: it bursts into a ring of this many shots when its fuse is up
    public float burstSpeed;
    public float burstRot;               // degrees the burst ring is turned by
}

public sealed class PatternStream
{
    public StreamKind kind;
    public string[] emitters;
    public int[] parts;                 // resolved by BossCatalog
    public float start, duration, interval = .5f;
    public float beats;                 // beat-synced: beats between volleys (0 = the interval, rounded to the beat grid); .5 = every off-beat, 2 = every other beat
    public int count = 1;
    public float spread;                // aimed / fan: arc; ring: how far beside the ship the gap is aimed
    public float baseDeg = -90f;
    public float speed = 3f, speedJitter;
    public float spin, amp, period = 4f;   // spiral
    public float armDeg = 180f;             // spiral: degrees between arms
    public bool mirror, alternate;
    public float gap, gapStep;          // curtain: gap (centre to centre of the shots either side), step per wall; ring: gap degrees; pincer: opening u, closing u per volley; mines: clear radius round the ship
    public float pitch = .5f;           // curtain / lattice: spacing; rain: lane width
    public float angle;                 // lattice drift off straight down (degrees)
    public float rotStep;               // ring / bomb burst: turned this much each time
    public int bounces;
    public BossRailMode rail = BossRailMode.Absorb;
    public BossShotStyle style;
    public float homeDeg = 70f, homeSeconds = 1.6f;
    public float fuse = 1.6f;
    public int burst = 8;
    public float burstSpeed = 2.4f;
    public float minSep = 1.9f;         // mines: apart; bombs: apart across the ship
    public int free = 2;                // rain: lanes left empty each drop row
    public float side = 1.4f;           // pincer: how far either side of the part the two jaws sit (one part given)

    public float End => start + duration;
}

public sealed class BossPattern
{
    public string name;
    public PatternStream[] streams;

    public float Duration
    {
        get
        {
            float d = 0f;
            for (int i = 0; i < streams.Length; i++) d = Mathf.Max(d, streams[i].End);
            return d;
        }
    }

    public int MaxConcurrentStreams
    {
        get
        {
            int best = 0;
            for (int i = 0; i < streams.Length; i++)
            {
                int n = 0;
                float at = streams[i].start + .001f;
                for (int j = 0; j < streams.Length; j++) if (streams[j].start <= at && streams[j].End > at) n++;
                best = Mathf.Max(best, n);
            }
            return best;
        }
    }
}

// What the runner needs of whoever plays it (BossActor in the game, PatternSim in tests).
public interface IPatternHost
{
    Vector2 Emitter(int part);              // a muzzle, in world space, now
    Vector2 Player { get; }
    float RailEdge { get; }                 // where a shot's centre meets the rail
    float ShipHalfWidth { get; }            // the ship's reach sideways
    float SpawnTop { get; }                 // the y rain / curtains / lattices come in at
    float ZoneBottom { get; }               // the ship's reach vertically
    float ZoneTop { get; }
    void Fire(Vector2 at, Vector2 velocity, ref PShot shot);
    void Emitted();                         // the boss shows its fire pose
    void Mark(Vector2 a, Vector2 b, float halfWidth, float seconds);   // a telegraph (a == b: a spot)
}

public sealed class BossPatternRunner
{
    public const int MaxStreams = 4;
    public const int MaxSpots = 12;
    public const float TopLoss = 0f;

    BossPattern pat;
    IPatternHost host;
    System.Random rng;
    float clock, rate = 1f;
    readonly float[] next = new float[MaxStreams];
    readonly int[] emitted = new int[MaxStreams];
    readonly float[] gapNow = new float[MaxStreams];
    readonly float[] gapDir = new float[MaxStreams];
    readonly PShot[] shot = new PShot[MaxStreams];
    readonly Vector2[] spots = new Vector2[MaxStreams * MaxSpots];   // planned mine / bomb spots, per stream
    readonly int[] spotCount = new int[MaxStreams];
    readonly int[] lanes = new int[64];

    // Beat sync (docs: BossBeatTuning): when the boss track's clock is running, a stream's volleys fall on the music's
    // beat grid instead of on its own cooldown; without a clock (no map, music not playing) every stream keeps the
    // timer above, exactly as before.
    public const double GridBeats = .25;       // the finest subdivision a volley lands on (a 16th)
    const double StartSlack = .12;             // beats: a pattern that starts just past a grid line still uses it
    IBeatSource beat;
    BeatTuning tune;
    bool started;
    double startBeat;
    readonly bool[] beatMode = new bool[MaxStreams];
    readonly double[] nextBeat = new double[MaxStreams];
    float emitSpeed = 1f;

    public bool BeatSynced => beat != null;
    public double LastScheduledBeat { get; private set; }
    public double LastEmitLateBeats { get; private set; }

    public BossPattern Pattern => pat;
    public float Clock => clock;
    public int Fired { get; private set; }
    public int Emissions { get; private set; }
    public bool Finished => pat == null || clock >= pat.Duration;

    // Plans the pattern (the places mines and bombs will be, the first wall's gap: all marked as telegraphs lasting
    // `tell` seconds); Step then plays it from time 0.
    public void Begin(BossPattern pattern, IPatternHost h, float rateScale, int seed, float tell, IBeatSource beatSource = null, BeatTuning tuning = null, bool prepare = true)
    {
        beat = beatSource != null && beatSource.Active ? beatSource : null;
        tune = tuning ?? BeatTuning.Default;
        started = false;
        startBeat = double.NaN;
        for (int i = 0; i < MaxStreams; i++) beatMode[i] = false;
        pat = pattern;
        host = h;
        rng = new System.Random(seed);
        rate = Mathf.Max(.2f, rateScale);
        clock = 0f;
        Fired = Emissions = 0;
        for (int i = 0; i < MaxStreams; i++) { next[i] = i < pat.streams.Length ? pat.streams[i].start : 1e9f; emitted[i] = 0; spotCount[i] = 0; }
        if (prepare) for (int i = 0; i < pat.streams.Length && i < MaxStreams; i++) Plan(i, pat.streams[i], tell);
    }

    // The musical director owns scheduling. These entry points reuse projectile geometry
    // and telegraph placement without consulting stream starts, durations, or cooldowns.
    public void PrepareMusical(float warningSeconds)
    {
        for (int i = 0; i < pat.streams.Length; i++)
        {
            emitted[i] = 0; // bombs must use the announced placement, not re-aim at emission
            Plan(i, pat.streams[i], warningSeconds);
        }
    }

    public void ResetMusicalPhrase()
    {
        for (int i = 0; i < MaxStreams; i++) emitted[i] = 0;
    }

    public void EmitMusical(float musicalPosition)
    {
        for (int i = 0; i < pat.streams.Length; i++)
        {
            Emit(i, pat.streams[i], musicalPosition);
            emitted[i]++;
            Emissions++;
            host.Emitted();
        }
    }

    public void Stop() { pat = null; host = null; beat = null; }

    void Plan(int i, PatternStream s, float tell)
    {
        var p = host.Player;
        shot[i] = new PShot
        {
            style = s.style, rail = s.rail, bounces = s.bounces,
            homeDeg = s.kind == StreamKind.Seeds ? s.homeDeg : 0f, homeSeconds = s.kind == StreamKind.Seeds ? s.homeSeconds : 0f,
            fuse = 0f, burst = 0, burstSpeed = s.burstSpeed,
        };
        float half = host.ShipHalfWidth;
        switch (s.kind)
        {
            case StreamKind.Curtain:
            {
                float lim = Mathf.Max(0f, half - .3f);
                gapDir[i] = p.x >= 0f ? -1f : 1f;
                gapNow[i] = Mathf.Clamp(p.x + gapDir[i] * s.gapStep * .7f, -lim, lim);
                // the first gap, a lane down the field
                host.Mark(new Vector2(gapNow[i], host.SpawnTop), new Vector2(gapNow[i], host.ZoneBottom - .5f), Mathf.Max(.1f, s.gap * .5f - .35f), tell);
                break;
            }
            case StreamKind.Ring:
            {
                var from = host.Emitter(s.parts != null && s.parts.Length > 0 ? s.parts[0] : 0);
                float g = GapHeading(from, p, s, 0);
                host.Mark(from, from + Heading(g) * 5f, .07f, tell);
                break;
            }
            case StreamKind.Mines:
                PlaceMines(i, s, p, tell);
                break;
            case StreamKind.Bomb:
                PlaceBombs(i, s, p, tell);
                break;
        }
    }

    static Vector2 Heading(float deg)
    {
        float r = deg * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
    }

    float Rand() => (float)rng.NextDouble();

    static float AimDeg(Vector2 from, Vector2 to)
    {
        var d = to - from;
        float deg = d.sqrMagnitude < 1e-4f ? -90f : Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        return Mathf.Clamp(deg, -170f, -10f);
    }

    // The heading of a ring's gap: beside the ship (alternating sides each ring), so the ship has to step into it.
    static float GapHeading(Vector2 from, Vector2 player, PatternStream s, int ring)
    {
        float aim = AimDeg(from, player);
        float side = (ring % 2 == 0) ? 1f : -1f;
        if (player.x < from.x) side = -side;
        return Mathf.Clamp(aim + side * s.spread, -165f, -15f);
    }

    // Preserve the beat chosen by the actor's tell. The first Step may arrive a frame late;
    // deriving a fresh origin there shifts the entire stream onto a different subdivision.
    public void StartOnBeat(double target) { if (!started) startBeat = target; }

    public bool Step(float dt, float realDt = -1f)
    {
        if (pat == null) return true;
        clock += dt;
        if (beat != null && !started) BeginBeats();
        for (int i = 0; i < pat.streams.Length && i < MaxStreams; i++)
        {
            var s = pat.streams[i];
            if (beatMode[i])
            {
                if (beat != null && beat.Active) { StepBeats(i, s, realDt >= 0f ? realDt : dt); continue; }
                // the music stopped under the pattern: this stream goes back to its own timer from now on
                beatMode[i] = false;
                next[i] = clock + Mathf.Max(.02f, s.interval / rate);
                continue;
            }
            float step = Mathf.Max(.02f, s.interval / rate);
            while (next[i] <= clock && (next[i] < s.End - 1e-4f || emitted[i] == 0))
            {
                Emit(i, s, next[i]);
                emitted[i]++;
                next[i] += step;
                Emissions++;
                host.Emitted();
                if (s.kind == StreamKind.Mines) { next[i] = 1e9f; break; }
            }
        }
        return clock >= pat.Duration;
    }

    // The pattern's first frame: each stream's first volley is the first grid line at or after its start time.
    void BeginBeats()
    {
        started = true;
        if (double.IsNaN(startBeat)) startBeat = beat.BeatNow;
        double perSec = 1.0 / beat.SecondsPerBeat;
        for (int i = 0; i < pat.streams.Length && i < MaxStreams; i++)
        {
            var s = pat.streams[i];
            double at = startBeat + s.start * perSec;
            nextBeat[i] = System.Math.Ceiling((at - StartSlack) / GridBeats) * GridBeats;
            beatMode[i] = true;
        }
    }

    // Beats between one stream's volleys: its own `beats`, or its interval in beats snapped to a musical division
    // (a 16th, 8th, beat, beat and a half, two, three, four), then the boss's tuning and the ladder's pace.
    static readonly float[] Divisions = { .25f, .5f, 1f, 1.5f, 2f, 3f, 4f };

    public float VolleyBeats(PatternStream s)
    {
        double spb = beat != null ? beat.SecondsPerBeat : .4;
        if (s.beats > 0f)
        {
            double e = s.beats * tune.beatsScale / rate;
            return (float)System.Math.Max(GridBeats, System.Math.Round(e / GridBeats) * GridBeats);
        }
        return Snap(Snap(s.interval / spb) * tune.beatsScale / rate);
    }

    static float Snap(double b)
    {
        float best = Divisions[0];
        double bestErr = double.MaxValue;
        for (int i = 0; i < Divisions.Length; i++)
        {
            double err = System.Math.Abs(System.Math.Log(Divisions[i] / System.Math.Max(.01, b)));
            if (err < bestErr) { bestErr = err; best = Divisions[i]; }
        }
        return best;
    }

    void StepBeats(int i, PatternStream s, float realDt)
    {
        double now = beat.BeatNow;
        double spb = beat.SecondsPerBeat;
        double step = VolleyBeats(s);
        emitSpeed = tune.speedScale;
        // Gameplay can freeze while music keeps playing. Only a normal frame's lateness
        // is allowed, even for a stream that has not fired yet. Keep its original phase.
        double allowedLate = System.Math.Max(0, realDt) / spb + 1e-6;
        if (now - nextBeat[i] > allowedLate)
            nextBeat[i] += System.Math.Ceiling((now - nextBeat[i]) / step) * step;
        while (nextBeat[i] <= now)
        {
            float at = clock - (float)((now - nextBeat[i]) * spb);
            if (emitted[i] > 0 && at >= s.End - 1e-4f) { nextBeat[i] = 1e18; break; }
            LastScheduledBeat = nextBeat[i];
            LastEmitLateBeats = now - nextBeat[i];
            Emit(i, s, Mathf.Max(s.start, at));
            emitted[i]++;
            nextBeat[i] += step;
            Emissions++;
            host.Emitted();
            if (s.kind == StreamKind.Mines) { nextBeat[i] = 1e18; break; }
        }
        emitSpeed = 1f;
    }

    Vector2 Part(PatternStream s, int k)
    {
        int n = s.parts == null ? 0 : s.parts.Length;
        return n == 0 ? host.Emitter(0) : host.Emitter(s.parts[k % n]);
    }

    int PartCount(PatternStream s) => s.parts == null || s.parts.Length == 0 ? 1 : s.parts.Length;

    void Fire(int i, Vector2 at, float deg, float speed)
    {
        Fire(i, at, Heading(deg) * speed);
    }

    void Fire(int i, Vector2 at, Vector2 v)
    {
        v *= emitSpeed;
        var sh = shot[i];
        host.Fire(at, v, ref sh);
        Fired++;
    }

    void Emit(int i, PatternStream s, float at)
    {
        int k = emitted[i];
        float ts = at - s.start;
        var player = host.Player;
        switch (s.kind)
        {
            case StreamKind.Aimed:
            {
                int n = s.alternate ? 1 : PartCount(s);
                for (int q = 0; q < n; q++)
                {
                    var from = Part(s, s.alternate ? k : q);
                    float aim = AimDeg(from, player);
                    int c = Mathf.Max(1, s.count);
                    for (int j = 0; j < c; j++)
                    {
                        float off = c == 1 ? 0f : -s.spread * .5f + s.spread * j / (c - 1);
                        Fire(i, from, aim + off, s.speed);
                    }
                }
                break;
            }
            case StreamKind.Fan:
            {
                int n = s.alternate ? 1 : PartCount(s);
                float rot = s.amp * ((k % 2 == 0) ? 1f : -1f);
                for (int q = 0; q < n; q++)
                {
                    var from = Part(s, s.alternate ? k : q);
                    int c = Mathf.Max(1, s.count);
                    for (int j = 0; j < c; j++)
                    {
                        float off = c == 1 ? 0f : -s.spread * .5f + s.spread * j / (c - 1);
                        Fire(i, from, s.baseDeg + off + rot, s.speed);
                    }
                }
                break;
            }
            case StreamKind.Spiral:
            {
                int n = s.mirror ? Mathf.Min(2, PartCount(s)) : 1;
                float theta = s.amp > 0f
                    ? s.baseDeg + s.amp * Mathf.Sin(ts / Mathf.Max(.1f, s.period) * Mathf.PI * 2f)
                    : s.baseDeg + s.spin * ts;
                for (int q = 0; q < n; q++)
                {
                    var from = Part(s, q);
                    for (int a = 0; a < Mathf.Max(1, s.count); a++)
                    {
                        float h = theta + a * s.armDeg;
                        if (q == 1) h = 180f - h;   // the mirror image
                        float sin = Mathf.Sin(h * Mathf.Deg2Rad);
                        if (sin > -.15f) continue;  // only the downward half fires
                        Fire(i, from, h, s.speed);
                    }
                }
                break;
            }
            case StreamKind.Ring:
            {
                var from = Part(s, 0);
                float g = GapHeading(from, player, s, k);
                int c = Mathf.Max(4, s.count);
                float a0 = -90f + k * s.rotStep;
                for (int j = 0; j < c; j++)
                {
                    float h = a0 + 360f * j / c;
                    if (Mathf.Sin(h * Mathf.Deg2Rad) > -.1f) continue;                     // the downward half only
                    if (Mathf.Abs(Mathf.DeltaAngle(h, g)) < s.gap * .5f) continue;        // the gap
                    Fire(i, from, h, s.speed);
                }
                break;
            }
            case StreamKind.Curtain:
            {
                float gc = gapNow[i];
                float edge = host.RailEdge - .15f;
                float y = host.SpawnTop;
                float sp = s.speed + Rand() * s.speedJitter;
                for (float x = gc - s.gap * .5f; x > -edge; x -= s.pitch) Fire(i, new Vector2(x, y), new Vector2(0f, -sp));
                for (float x = gc + s.gap * .5f; x < edge; x += s.pitch) Fire(i, new Vector2(x, y), new Vector2(0f, -sp));
                // the next wall's gap: a step to one side, turning at the end of the lane
                float lim = Mathf.Max(0f, host.ShipHalfWidth - .3f);
                float nx = gc + gapDir[i] * s.gapStep;
                if (nx > lim || nx < -lim) { gapDir[i] = -gapDir[i]; nx = Mathf.Clamp(gc + gapDir[i] * s.gapStep, -lim, lim); }
                gapNow[i] = nx;
                break;
            }
            case StreamKind.Rain:
            {
                int nl = Mathf.Clamp(Mathf.RoundToInt(host.RailEdge * 2f / Mathf.Max(.3f, s.pitch)), 3, lanes.Length);
                for (int l = 0; l < nl; l++) lanes[l] = l;
                for (int l = nl - 1; l > 0; l--) { int j = rng.Next(l + 1); int t = lanes[l]; lanes[l] = lanes[j]; lanes[j] = t; }
                int c = Mathf.Clamp(s.count, 1, nl - Mathf.Max(1, s.free));
                float w = host.RailEdge * 2f / nl;
                for (int d = 0; d < c; d++)
                {
                    float x = -host.RailEdge + w * (lanes[d] + .5f) + (Rand() - .5f) * w * .35f;
                    float sp = s.speed + Rand() * s.speedJitter;
                    Fire(i, new Vector2(x, host.SpawnTop + Rand() * .3f), new Vector2(0f, -sp));
                }
                break;
            }
            case StreamKind.Lattice:
            {
                int run = Mathf.Max(1, s.count);
                float dir = ((k / run) % 2 == 0) ? 1f : -1f;
                float ang = s.angle * Mathf.Deg2Rad;
                var v = new Vector2(dir * Mathf.Sin(ang), -Mathf.Cos(ang)) * s.speed;
                float fall = Mathf.Max(1f, host.SpawnTop - (host.ZoneBottom + host.ZoneTop) * .5f);
                float drift = Mathf.Tan(ang) * fall;
                float lo = Mathf.Max(-4.1f, -host.RailEdge - Mathf.Max(0f, -dir * drift) - .2f), hi = Mathf.Min(4.1f, host.RailEdge + Mathf.Max(0f, dir * drift) + .2f);
                float offs = (k % 2 == 0) ? 0f : s.pitch * .5f;
                for (float x = lo + offs; x < hi; x += s.pitch) Fire(i, new Vector2(x, host.SpawnTop), v);
                break;
            }
            case StreamKind.Bomb:
            {
                var from = Part(s, k);
                int c = spotCount[i];
                // the first volley lands on the marked spots; later ones are planned now (the ship is where it is)
                if (k > 0) PlaceBombs(i, s, player, 0f);
                c = spotCount[i];
                for (int j = 0; j < c; j++)
                {
                    var to = spots[i * MaxSpots + j];
                    var d = to - from;
                    var sh = shot[i];
                    sh.fuse = s.fuse;
                    sh.burst = s.burst;
                    sh.burstSpeed = s.burstSpeed;
                    sh.burstRot = k * s.rotStep;
                    host.Fire(from, d / Mathf.Max(.3f, s.fuse), ref sh);
                    Fired++;
                }
                break;
            }
            case StreamKind.Seeds:
            {
                var from = Part(s, k);
                float aim = AimDeg(from, player);
                int c = Mathf.Max(1, s.count);
                for (int j = 0; j < c; j++)
                {
                    float off = c == 1 ? 0f : -s.spread * .5f + s.spread * j / (c - 1);
                    Fire(i, from, aim + off, s.speed);
                }
                break;
            }
            case StreamKind.Mines:
            {
                int c = spotCount[i];
                for (int j = 0; j < c; j++)
                {
                    var sh = shot[i];
                    sh.fuse = s.fuse;
                    host.Fire(spots[i * MaxSpots + j], Vector2.zero, ref sh);
                    Fired++;
                }
                break;
            }
            case StreamKind.Pincer:
            {
                float off = Mathf.Max(0f, s.gap - s.gapStep * k);
                int n = 2;
                for (int q = 0; q < n; q++)
                {
                    var from = PartCount(s) >= 2 ? Part(s, q) : Part(s, 0) + new Vector2(q == 0 ? -s.side : s.side, 0f);
                    float side = from.x < player.x ? -1f : 1f;
                    var target = new Vector2(player.x + side * off, player.y);
                    float aim = AimDeg(from, target);
                    int c = Mathf.Max(1, s.count);
                    for (int j = 0; j < c; j++)
                    {
                        float o = c == 1 ? 0f : -s.spread * .5f + s.spread * j / (c - 1);
                        Fire(i, from, aim + o, s.speed);
                    }
                }
                break;
            }
        }
    }

    // `count` mines on marked spots: inside the ship's zone, `clear` from the ship, `minSep` from each other.
    void PlaceMines(int i, PatternStream s, Vector2 player, float tell)
    {
        int want = Mathf.Min(MaxSpots, Mathf.Max(1, s.count));
        int n = 0;
        float half = host.ShipHalfWidth + .1f;
        float clear2 = s.gap * s.gap, sep2 = s.minSep * s.minSep;
        for (int tries = 0; tries < 80 && n < want; tries++)
        {
            var q = new Vector2((Rand() * 2f - 1f) * half, host.ZoneBottom + Rand() * (host.ZoneTop - host.ZoneBottom));
            if ((q - player).sqrMagnitude < clear2) continue;
            bool ok = true;
            for (int j = 0; j < n && ok; j++) if ((spots[i * MaxSpots + j] - q).sqrMagnitude < sep2) ok = false;
            if (!ok) continue;
            spots[i * MaxSpots + n++] = q;
        }
        spotCount[i] = n;
        for (int j = 0; j < n; j++) host.Mark(spots[i * MaxSpots + j], spots[i * MaxSpots + j], .38f, tell);
    }

    // `count` landing spots in a row across the ship's place, `minSep` apart.
    void PlaceBombs(int i, PatternStream s, Vector2 player, float tell)
    {
        int c = Mathf.Min(MaxSpots, Mathf.Max(1, s.count));
        float half = host.ShipHalfWidth;
        for (int j = 0; j < c; j++)
        {
            // bombs bracket the ship (never one right on it): alternate sides, each a little further out
            float side = (j % 2 == 0) ? -1f : 1f;
            float x = Mathf.Clamp(player.x + side * (s.minSep * .6f + (j / 2) * s.minSep), -half, half);
            float y = Mathf.Clamp(player.y + (Rand() - .5f) * 1.2f, host.ZoneBottom, host.ZoneTop);
            spots[i * MaxSpots + j] = new Vector2(x, y);
        }
        spotCount[i] = c;
        if (tell > 0f) for (int j = 0; j < c; j++) host.Mark(spots[i * MaxSpots + j], spots[i * MaxSpots + j], .3f, tell);
    }
}
