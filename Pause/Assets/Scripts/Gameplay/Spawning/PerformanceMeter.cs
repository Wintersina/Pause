using System.Collections.Generic;

// How well the pilot is doing right now, as a "flow" value in [-1, +1]
// (docs/adaptive-difficulty.md). Pure C# (no UnityEngine): deterministic and
// unit-testable. The caller feeds it events (OnKill, OnHit, OnStar) and a
// Tick per running frame; it never reads the clock or random numbers.
//
// The raw score is built from a rolling window of recent game time:
//   good  = chain level (0.45) + kills per second (0.25) + damage-free time (0.30,
//           only counted once something was killed in the window, so flying
//           peacefully past a calm stretch earns nothing)
//   bad   = each heart lost in the window (0.5 each, so two hits = -1) and
//           being down to the last heart (0.3)
//   raw   = good - bad, clamped to [-1, 1]
// The shown value chases raw through a low-pass (Tau) AND a slew limit
// (MaxRise / MaxFall per second), so spawn rates never lurch.
//
// While Frozen (authored beats: boss, elite, planetfall...) the clock, the
// window and the value all stand still, so a fight is the same for everyone.
public sealed class PerformanceMeter
{
    // ---- tunables ----
    public const float WindowSeconds = 25f;
    public const int ChainForFull = 8;           // a chain this long is "full" chain credit
    public const float KillsPerSecondForFull = .5f;
    public const float CleanSecondsForFull = 30f;
    public const float ChainWeight = .45f, KillRateWeight = .25f, CleanWeight = .30f;
    public const float HitPenalty = .5f, LastHeartPenalty = .3f;
    public const float Tau = 6f;                  // low-pass time constant
    public const float MaxRise = .04f;            // flow per second, going up (25 s for 0 -> 1)
    public const float MaxFall = .08f;            // going down is faster: recovery should start soon
    public const float WorldBlend = .5f;          // share of the flow kept across a world change

    readonly List<float> kills = new List<float>();
    readonly List<float> hits = new List<float>();
    float now;
    float lastHit = float.NegativeInfinity;
    float value;
    float raw;

    // Smoothed flow, [-1, 1]. 0 = neutral.
    public float Flow { get { return value; } }
    // The unsmoothed target of the last Tick.
    public float Raw { get { return raw; } }
    public float Clock { get { return now; } }

    // Frozen: no time passes inside the meter; events are ignored.
    public bool Frozen;

    // Tests: pin the smoothed value.
    public void Force(float flow) { value = Clamp(flow, -1f, 1f); }

    public void Reset()
    {
        kills.Clear(); hits.Clear();
        now = 0f; lastHit = float.NegativeInfinity; value = 0f; raw = 0f; Frozen = false;
    }

    // A revive / continue: the slate is wiped like a new run.
    public void OnRevive() { Reset(); }

    // A new world: the value blends back toward neutral and the window is forgotten.
    public void OnWorldChange()
    {
        kills.Clear(); hits.Clear();
        lastHit = float.NegativeInfinity;
        value *= WorldBlend;
        now = 0f;
    }

    public void OnKill() { if (!Frozen) kills.Add(now); }

    public void OnHit() { if (Frozen) return; hits.Add(now); lastHit = now; }

    // Advance by dt seconds of running game time. chain = current kill chain,
    // heartsLeft / maxHearts = the ship's hull.
    public void Tick(float dt, int chain, int heartsLeft, int maxHearts)
    {
        if (Frozen || dt <= 0f) return;
        now += dt;
        Prune(kills); Prune(hits);

        float good = 0f;
        float chainT = Clamp01((float)chain / ChainForFull);
        float killRate = kills.Count / WindowSeconds;
        float rateT = Clamp01(killRate / KillsPerSecondForFull);
        good += ChainWeight * chainT + KillRateWeight * rateT;
        if (kills.Count > 0)
        {
            float clean = float.IsNegativeInfinity(lastHit) ? CleanSecondsForFull : now - lastHit;
            good += CleanWeight * Clamp01(clean / CleanSecondsForFull);
        }

        float bad = HitPenalty * hits.Count;
        if (maxHearts > 1 && heartsLeft <= 1) bad += LastHeartPenalty;

        raw = Clamp(good - bad, -1f, 1f);

        // low-pass, then slew limit
        float k = 1f - (float)System.Math.Exp(-dt / Tau);
        float target = value + (raw - value) * k;
        float step = target - value;
        float limit = (step > 0f ? MaxRise : MaxFall) * dt;
        if (step > limit) step = limit;
        else if (step < -limit) step = -limit;
        value = Clamp(value + step, -1f, 1f);
    }

    void Prune(List<float> list)
    {
        int drop = 0;
        while (drop < list.Count && list[drop] < now - WindowSeconds) drop++;
        if (drop > 0) list.RemoveRange(0, drop);
    }

    static float Clamp01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }
    static float Clamp(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }
}
