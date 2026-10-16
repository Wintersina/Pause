using UnityEngine;

// THE WIND (docs/weather-overhaul.md): the always-on, per-world weather force that sits UNDER the rare hazard events.
//
// A hazard event (tornado, flare, sandstorm) is a big shape that crosses the screen once a minute. The wind is what
// the weather does the rest of the time, and it is what makes bullets CURVE: every world has a steady breeze (so
// every enemy bullet bends a little, always), a shear (the wind differs by height, or by column, so bullets snake and
// layers of air visibly flow in opposite directions), turbulence, and scheduled GUSTS. A gust is telegraphed first
// (the edge it will come from glows and streaks start to blow in, `warn` seconds), then it ramps in, holds and
// ramps out. It pushes bullets, enemy bodies, elites and (a little, capped) the ship, by the same `TargetMix`
// gains the events use. All of it is one pure function of (world profile, gust state, position, time): the tests walk
// it, the drawn wind streaks are advected by the very same function, and the hazard scheduler does not start a gust
// while an event is running.
//
// Boss fights: a boss wave's gap is only reachable if its bullets move together, so while a boss encounter runs every
// hostile shot gets only the RIGID, slew-limited, capped sideways wind (BossShotWind): the same sideways speed for
// every bullet on screen, never above BossShotWindCap. Elite and ordinary shots outside a boss fight feel the whole field.
public sealed class WindProfile
{
    public string name;
    public Vector2 breeze;            // steady wind (u/s): x sideways (+ right), y up-screen
    public float wobble = .4f;        // 0..1: the breeze swells and ebbs by this share (9 s period)
    public float gust;                // peak extra speed of a gust (u/s)
    public Vector2 gustDir = Vector2.right;   // its direction (x is mirrored by the gust's side, y is up-screen)
    public Vector2 every = new Vector2(8f, 13f);   // calm seconds between gusts
    public float warn = 1.7f, live = 3.4f;         // telegraph / hold seconds of a gust
    public float shear;               // modulation depth by height (or by column): 0 none, 1 stalls in the troughs, > 1 reverses
    public float shearLen = 6f, shearRate = .8f;   // the shear's wavelength (u) and how fast its bands travel (u/s)
    public bool shearByX;             // modulate by x (vertical thermals) instead of y (horizontal layers)
    public float turb = .15f;         // fast flutter, as a share of the local speed
    public TargetMix player = new TargetMix(.22f), body = new TargetMix(.55f), elite = new TargetMix(.25f), hostile = new TargetMix(1f), mine = new TargetMix(.5f);

    public TargetMix MixOf(HazardTarget t)
    {
        switch (t)
        {
            case HazardTarget.Player: return player;
            case HazardTarget.Body: return body;
            case HazardTarget.Elite: return elite;
            case HazardTarget.Hostile: return hostile;
            default: return mine;
        }
    }
}

public static class WeatherWind
{
    public enum Phase { Calm, Warning, Gust }

    // ---- global tunables ----
    public const float BossShotWindCap = .12f;     // u/s: the most sideways speed a boss-fight bullet ever gets from weather
    public const float BossShotWindSlew = .12f;    // u/s per s: how fast it may change (a gust never reverses a wave's drift abruptly)
    public const float FadeIn = 2.5f;              // s: the wind eases in after the calm start / a world change
    public const float GustRampIn = .7f, GustRampOut = 1.0f;
    public const float FirstGustAfter = 10f;       // s into a world

    // ---- state (one wind at a time; tests drive it by hand) ----
    public static WindProfile Profile;
    public static Phase PhaseNow;
    public static float PhaseT;            // seconds into the phase
    public static float Side = 1f;         // the gust's direction (+1 right, -1 left)
    public static float Clock;             // seconds the wind has run
    public static float Fade;              // 0..1 how much of the wind is on (calm start, transitions, boss intros)
    static float untilGust = 12f;
    static float bossWind;                 // the slew-limited rigid wind boss-fight shots feel (u/s)
    static System.Random rng = new System.Random(7);

    public static float BossWind => bossWind;
    public static bool Warning => PhaseNow == Phase.Warning && Fade > .05f;
    public static float WarnProgress => PhaseNow == Phase.Warning && Profile != null ? Mathf.Clamp01(PhaseT / Mathf.Max(.1f, Profile.warn)) : 0f;
    public static float Strength => Fade;

    public static void Reset(WindProfile p, int seed = -1)
    {
        Profile = p;
        PhaseNow = Phase.Calm; PhaseT = 0f; Clock = 0f; Fade = 0f; bossWind = 0f;
        if (seed >= 0) rng = new System.Random(seed);
        Side = rng.Next(2) == 0 ? -1f : 1f;
        untilGust = p != null ? FirstGustAfter + Rand(0f, 4f) : 99f;
    }

    static float Rand(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

    // 0..1: how hard the current gust blows (0 outside a gust).
    public static float GustEnvelope
    {
        get
        {
            if (Profile == null || PhaseNow != Phase.Gust) return 0f;
            float inK = Mathf.Clamp01(PhaseT / GustRampIn); inK = inK * inK * (3f - 2f * inK);
            float outK = Mathf.Clamp01((Profile.live - PhaseT) / GustRampOut); outK = outK * outK * (3f - 2f * outK);
            return inK * outK;
        }
    }

    // One frame. `on`: the wind may blow (a clean, running board); `allowGust`: a new gust may be scheduled (no event is running).
    public static void Step(float dt, bool on, bool allowGust)
    {
        var p = Profile;
        Fade = Mathf.MoveTowards(Fade, on && p != null ? 1f : 0f, dt / FadeIn * (on ? 1f : 3f));
        if (p == null) { PhaseNow = Phase.Calm; return; }
        Clock += dt;
        PhaseT += dt;
        switch (PhaseNow)
        {
            case Phase.Calm:
                if (on && allowGust) untilGust -= dt;
                if (untilGust <= 0f)
                {
                    PhaseNow = Phase.Warning; PhaseT = 0f;
                    Side = rng.Next(2) == 0 ? -1f : 1f;
                }
                break;
            case Phase.Warning:
                if (!on || !allowGust) { PhaseNow = Phase.Calm; PhaseT = 0f; untilGust = 3f; }
                else if (PhaseT >= p.warn) { PhaseNow = Phase.Gust; PhaseT = 0f; }
                break;
            default:
                if (PhaseT >= p.live || !on) { PhaseNow = Phase.Calm; PhaseT = 0f; untilGust = Rand(p.every.x, p.every.y); }
                break;
        }
    }

    // The wind's velocity (u/s) at `at` for a kind of target, with the profile and the state given (the tests pass their own).
    public static Vector2 Sample(WindProfile p, Vector2 at, HazardTarget target, float clock, float gustEnv, float side, float fade)
    {
        if (p == null || fade <= 0f) return Vector2.zero;
        TargetMix mix = p.MixOf(target);
        if (mix.gain == 0f) return Vector2.zero;
        Vector2 v = p.breeze * (1f + p.wobble * Mathf.Sin(clock * 2f * Mathf.PI / 9f));
        if (gustEnv > 0f) v += new Vector2(p.gustDir.x * side, p.gustDir.y) * (p.gust * gustEnv);
        float m = 1f;
        if (p.shear > 0f)
        {
            float coord = p.shearByX ? at.x : at.y;
            m += p.shear * Mathf.Sin(2f * Mathf.PI * (coord - p.shearRate * clock) / Mathf.Max(.5f, p.shearLen));
        }
        v *= m;
        if (p.turb > 0f)
        {
            float mag = v.magnitude;
            v += new Vector2(Mathf.Sin(clock * 3.1f + at.y * 1.7f + at.x * .9f), Mathf.Sin(clock * 2.3f + at.x * 1.9f - at.y * 1.1f)) * (p.turb * mag);
        }
        return v * (mix.gain * fade);
    }

    public static Vector2 Sample(Vector2 at, HazardTarget target)
    {
        return Sample(Profile, at, target, Clock, GustEnvelope, Side, Fade);
    }

    // The RIGID part of the wind, one sideways number for the whole board (no shear, no flutter): what boss-fight shots feel.
    public static float RigidX(HazardTarget target)
    {
        var p = Profile;
        if (p == null || Fade <= 0f) return 0f;
        float x = p.breeze.x * (1f + p.wobble * Mathf.Sin(Clock * 2f * Mathf.PI / 9f)) + p.gustDir.x * Side * p.gust * GustEnvelope;
        return x * p.MixOf(target).gain * Fade;
    }

    // One frame of the boss-fight shots' wind: the rigid sideways push of the wind and of every front / sweep, capped, slew-limited.
    // `fieldRigid` is the sum of the live events' RigidX already scaled by their bossShotScale.
    public static float StepBossWind(float dt, float fieldRigid)
    {
        float target = Mathf.Clamp(RigidX(HazardTarget.Hostile) * .2f + fieldRigid, -BossShotWindCap, BossShotWindCap);
        bossWind = Mathf.MoveTowards(bossWind, target, BossShotWindSlew * dt);
        return bossWind;
    }

    public static void ResetBossWind() { bossWind = 0f; }
}
