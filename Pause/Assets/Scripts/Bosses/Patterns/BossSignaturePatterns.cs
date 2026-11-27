using UnityEngine;

// THE BULLET-PATTERN LIBRARY'S EMITTERS (partial of BossPatternRunner): the signature streams' maths. Every shot comes out
// of the boss's pool through host.Fire (prewarmed at the boss's spawn) and its path is a PatternMotion on the shot, so there is
// no per-bullet logic and nothing is allocated. Every length is in lane units (host.RailEdge / 2.6 = 1 on a phone), so a tall
// 1080x2520 cover or a wide screen draws the same pattern with the same gaps.
//
// The fair-dodge rules every stream keeps: a guaranteed gap (a wall's, a ring's, a rose's, an arm's spacing), a telegraph at
// the start (Plan: the lane / gap / aim line in the world's cue colour), a slow start for anything aimed, and no shot ever
// starts anywhere but on a muzzle.
public sealed partial class BossPatternRunner
{
    IMusicPatternHost mh;

    // ---- the guaranteed lane ----
    // Every signature stream (but the wave wall, which carries its own gap) keeps one SAFE LANE clear: a corridor of
    // 2 * corrHalf u that drifts slowly (corrA u about corrC0, one swing in CorrPeriod seconds, so never faster than ~.7 u/s).
    // Before a shot leaves its muzzle the runner flies it forward on the same maths the game uses (PatternMotion.Apply) and
    // drops it when it would cross the corridor inside the ship's rows, at the corridor's place at THAT time. So however dense
    // the pattern or the loop, a free run of 2 * corrHalf always exists in the ship's rows, and it is the telegraphed lane.
    public const float CorrPeriod = 7.5f, CorrDt = .04f, CorrR = .14f;
    float corrC0, corrA, corrHalf, sigTime;

    void InitCorridor()
    {
        corrHalf = .5f + BossPatternScaling.GapBonus;
        float lim = Mathf.Max(0f, host.ShipHalfWidth - corrHalf - .2f);
        corrA = Mathf.Min(.8f, lim * .5f);
        var p = host.Player;
        float away = p.x > 0f ? -1f : 1f;    // the lane is a step away from the ship: it has to move to it
        corrC0 = Mathf.Clamp(p.x + away * 1.5f, -(lim - corrA), lim - corrA);
    }

    float CorrX(float t) => corrC0 + corrA * Mathf.Sin(t * 6.2831853f / CorrPeriod);

    // Does a shot launched at `at` with velocity `v` (already as the host will fly it) keep out of the lane while it is in the ship's rows?
    bool Fits(Vector2 at, Vector2 v, PMotion m)
    {
        float yLo = host.ZoneBottom - CorrR - .25f, yHi = host.ZoneTop + CorrR + .25f;
        float half = corrHalf + CorrR + .06f;
        var p = at; float age = 0f;
        for (int n = 0; n < 220; n++)
        {
            age += CorrDt;
            p += v * CorrDt;
            if (m.kind != MotionKind.None) PatternMotion.Apply(ref m, ref p, ref v, age, CorrDt);
            if (p.y < yLo - 1f) break;
            if (p.y <= yHi && p.y >= yLo && Mathf.Abs(p.x - CorrX(sigTime + age)) < half) return false;
        }
        return true;
    }

    // Loop 2+: a volley is held back while the pool is nearly full (BossSignatureTuning.MaxLive); loop 1 never is. A volley of a
    // ring, a rose or a wall may add up to Big shots, any other up to Small.
    const int Big = 32, Small = 16;
    bool Room(PatternStream s)
    {
        if (!BossPatternScaling.Scaled || mh == null) return true;
        bool big = s.kind == StreamKind.Rose || s.kind == StreamKind.Halo || s.kind == StreamKind.WaveWall;
        return mh.ActiveMusicShots + (big ? Big : Small) <= BossSignatureTuning.MaxLive;
    }

    // Seconds a stream may not volley faster than: a wave wall keeps one wall's height clear of the next (the ship's rows, the bob
    // and a margin), so two walls' gaps are never in the ship's rows together.
    float FloorSeconds(PatternStream s)
    {
        if (s.kind != StreamKind.WaveWall) return 0f;
        float sep = host.ZoneTop - host.ZoneBottom + 2f * s.amp * Lane + .8f;
        return sep / Mathf.Max(.5f, s.speed * BossPatternScaling.Speed * Mathf.Max(.01f, host.ShotSpeedScale));
    }

    float Lane => Mathf.Max(.5f, host.RailEdge / 2.6f);

    void FireM(int i, Vector2 at, Vector2 v, PMotion m)
    {
        v *= emitSpeed;
        if (!Fits(at, v * host.ShotSpeedScale, m)) return;
        var sh = shot[i];
        sh.motion = m;
        host.Fire(at, v, ref sh);
        Fired++;
    }

    static float Wrap(float deg) => Mathf.Clamp(deg, -165f, -15f);

    PMotion Sway(PatternStream s, float phaseDeg, float delay = 0f, bool bob = false) => new PMotion
    {
        kind = bob ? MotionKind.Bob : MotionKind.Sway, amp = s.amp * Lane, hz = s.hz, phase = phaseDeg * Mathf.Deg2Rad, delay = delay,
    };

    // The heading a Halo's gap points at on its k-th ring: sweeping from side to side of straight down.
    public static float HaloGap(PatternStream s, int k) => -90f + s.spread * Mathf.Sin(k * s.rotStep * Mathf.Deg2Rad);

    void PlanSignature(int i, PatternStream s, float tell)
    {
        var from = Part(s, 0);
        float lane = Lane;
        switch (s.kind)
        {
            case StreamKind.Sine:
            case StreamKind.Braid:
                host.Mark(from, from + Heading(Wrap(s.baseDeg)) * 6f, s.amp * lane + .15f, tell);
                break;
            case StreamKind.WaveWall:
            {
                float lim = Mathf.Max(0f, host.ShipHalfWidth - .3f);
                var p = host.Player;
                gapDir[i] = p.x >= 0f ? -1f : 1f;
                gapNow[i] = Mathf.Clamp(p.x + gapDir[i] * s.gapStep * .7f, -lim, lim);
                host.Mark(new Vector2(gapNow[i], host.SpawnTop), new Vector2(gapNow[i], host.ZoneBottom - .5f), Mathf.Max(.1f, s.gap * .5f - .35f), tell);
                break;
            }
            case StreamKind.Halo:
                host.Mark(from, from + Heading(HaloGap(s, 0)) * 5f, .07f, tell);
                break;
            case StreamKind.Rose:
            {
                float g = GapHeading(from, host.Player, s, 0);
                host.Mark(from, from + Heading(g) * 5f, .07f, tell);
                break;
            }
            case StreamKind.AccelFan:
            {
                float aim = AimDeg(from, host.Player);
                host.Mark(from, from + Heading(aim) * 7f, .09f, tell);
                break;
            }
            default:   // tornado, arms, orbit: the muzzle itself winds up
                host.Mark(from, from, .3f, tell);
                break;
        }
        // the safe lane, where it starts (every stream but the wall, whose gap is marked above)
        if (s.kind != StreamKind.WaveWall) host.Mark(new Vector2(CorrX(0f), host.SpawnTop), new Vector2(CorrX(0f), host.ZoneBottom - .5f), corrHalf, tell);
    }

    void EmitSignature(int i, PatternStream s, float ts, int k)
    {
        if (!Room(s)) return;
        sigTime = s.start + ts;
        float saved = emitSpeed;
        emitSpeed *= BossPatternScaling.Speed;
        EmitSignatureShots(i, s, ts, k);
        emitSpeed = saved;
    }

    void EmitSignatureShots(int i, PatternStream s, float ts, int k)
    {
        int extra = BossPatternScaling.ExtraArms;
        var player = host.Player;
        float lane = Lane;
        switch (s.kind)
        {
            case StreamKind.Sine:
            case StreamKind.Braid:
            {
                var from = Part(s, s.alternate ? k : 0);
                float h = Wrap(s.baseDeg + s.spin * ts);
                int c = Mathf.Max(1, s.count);
                for (int j = 0; j < c; j++)
                {
                    float off = c == 1 ? 0f : -s.spread * .5f + s.spread * j / (c - 1);
                    var v = Heading(h + off) * s.speed;
                    FireM(i, from, v, Sway(s, s.phaseDeg));
                    if (s.kind == StreamKind.Braid) FireM(i, from, v, Sway(s, s.phaseDeg + 180f));
                }
                break;
            }
            case StreamKind.WaveWall:
            {
                float gc = gapNow[i];
                float edge = host.RailEdge - .15f;
                float y = WaveDeploy.FormY(host, WaveDeploy.LowestY(host, PartsOf(s))), t = Spray(s);
                float gap = s.gap + BossPatternScaling.GapBonus;
                for (float x = gc - gap * .5f; x > -edge; x -= s.pitch) WallShot(i, s, x, y, t);
                for (float x = gc + gap * .5f; x < edge; x += s.pitch) WallShot(i, s, x, y, t);
                float lim = Mathf.Max(0f, host.ShipHalfWidth - .3f);
                float nx = gc + gapDir[i] * s.gapStep;
                if (nx > lim || nx < -lim) { gapDir[i] = -gapDir[i]; nx = Mathf.Clamp(gc + gapDir[i] * s.gapStep, -lim, lim); }
                gapNow[i] = nx;
                break;
            }
            case StreamKind.Tornado:
            {
                var from = Part(s, 0);
                float theta = s.baseDeg + s.spin * ts;
                var m = new PMotion { kind = MotionKind.Curve, a = s.turnDeg, sec = s.turnSeconds, b = s.gain, delay = .12f };
                int arms = Mathf.Min(5, Mathf.Max(1, s.count) + extra);
                float armDeg = extra > 0 ? 360f / arms : s.armDeg;
                for (int a = 0; a < arms; a++)
                {
                    float h = theta + a * armDeg;
                    if (Mathf.Sin(h * Mathf.Deg2Rad) > -.15f) continue;
                    FireM(i, from, Heading(h) * s.speed, m);
                }
                break;
            }
            case StreamKind.Arms:
            {
                var from = Part(s, 0);
                float spin = s.spin * ts;
                if (s.reverseAt > 0f && ts > s.reverseAt) spin = s.spin * (2f * s.reverseAt - ts);   // the arms wind back the way they came
                float theta = s.baseDeg + spin;
                int arms = Mathf.Clamp(s.count + extra, 2, 5);
                for (int a = 0; a < arms; a++)
                {
                    float h = theta + a * 360f / arms;
                    if (Mathf.Sin(h * Mathf.Deg2Rad) > -.15f) continue;
                    FireM(i, from, Heading(h) * s.speed, default);
                }
                break;
            }
            case StreamKind.Rose:
            {
                var from = Part(s, 0);
                float g = GapHeading(from, player, s, k);
                int c = Mathf.Min(BossPatternScaling.RingCap, BossPatternScaling.Count(Mathf.Max(8, s.count)));
                float turn = k * s.rotStep;
                for (int j = 0; j < c; j++)
                {
                    float h = -165f + 150f * j / (c - 1);
                    if (Mathf.Abs(Mathf.DeltaAngle(h, g)) < s.gap * .5f) continue;
                    float petal = Mathf.Abs(Mathf.Cos(s.petals * (h + 90f + turn) * Mathf.Deg2Rad));
                    FireM(i, from, Heading(h) * (s.speed * (.38f + .62f * petal)), default);
                }
                break;
            }
            case StreamKind.Halo:
            {
                var from = Part(s, 0);
                float g = HaloGap(s, k);
                int c = Mathf.Min(BossPatternScaling.RingCap, BossPatternScaling.Count(Mathf.Max(8, s.count)));
                for (int j = 0; j < c; j++)
                {
                    float h = -90f + 360f * j / c;
                    if (Mathf.Sin(h * Mathf.Deg2Rad) > -.1f) continue;
                    if (Mathf.Abs(Mathf.DeltaAngle(h, g)) < s.gap * .5f) continue;
                    FireM(i, from, Heading(h) * s.speed, default);
                }
                break;
            }
            case StreamKind.Orbit:
            {
                var from = Part(s, k);
                int c = Mathf.Min(10, Mathf.Max(2, s.count) + extra);
                float R = s.orbitR * lane;
                for (int j = 0; j < c; j++)
                {
                    var m = new PMotion
                    {
                        kind = MotionKind.Orbit, amp = R, hz = s.hz, sec = s.holdSeconds, a = s.speed * emitSpeed * host.ShotSpeedScale,
                        b = .55f, anchor = from, phase = (s.phaseDeg + 360f * j / c) * Mathf.Deg2Rad,
                    };
                    FireM(i, from, Vector2.zero, m);
                }
                break;
            }
            case StreamKind.AccelFan:
            {
                var from = Part(s, k);
                float aim = AimDeg(from, player);
                int c0 = Mathf.Max(2, s.count);
                int c = Mathf.Min(10, c0 + extra * 2);
                float spreadNow = s.spread * (c - 1) / (c0 - 1);   // the same pitch between shots, so the fan only widens
                var m = new PMotion { kind = MotionKind.Accel, a = Mathf.Max(1f, s.gain), sec = s.turnSeconds };
                float start = s.speed / Mathf.Max(1f, s.gain);
                for (int j = 0; j < c; j++)
                {
                    float off = -spreadNow * .5f + spreadNow * j / (c - 1);
                    FireM(i, from, Heading(aim + off) * start, m);
                }
                break;
            }
        }
    }

    // A wave wall's shot: it deploys like a curtain's, then bobs in a sine that travels along the row (spread deg per u).
    void WallShot(int i, PatternStream s, float x, float y, float turn)
    {
        var from = WaveDeploy.Nearest(host, PartsOf(s), x);
        var sh = shot[i];
        float k = Mathf.Max(.01f, host.ShotSpeedScale);
        sh.turnAt = turn;
        sh.then = new Vector2(0f, -s.speed) * emitSpeed;
        sh.motion = Sway(s, s.phaseDeg + x * s.spread / Lane, turn, true);
        var to = new Vector2(x, y);
        host.Fire(from, (to - from) / turn / k, ref sh);
        Fired++;
    }
}
