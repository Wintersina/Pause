using System;
using UnityEngine;

// The single owner of the difficulty ramp on moveBackGround.speed.
//
// moveBackGround sits on both side walls (leftPipe and rightPipe in gameS1 and
// tutorialS5). Each instance used to add speedRampPerSecond * dt to the shared
// static speed on its own, so the ramp ran once per wall per frame: twice the
// intended rate. Every instance still calls Tick on its running frames, and
// the frame guard makes sure only the first call in a frame counts. The ramp
// therefore runs exactly once per frame, however many walls exist.
// WorldManager.ApplyDifficulty gives every instance the same ramp, so it
// doesn't matter which one goes first.
//
// THE CAP AND THE EASE (2026-10, second pass). Natural speed never passes
// Cap (HUD 35) in any world, on any loop: 35 is the hard-but-playable final
// speed. The ramp keeps the world's rate up to EaseKnee and eases into the
// cap from there: the rate falls as the square root of what is left, so
// speed follows a parabola that reaches the cap with zero slope in a finite
// time (2 x (cap - knee) / rate after the knee). No wall and no asymptote.
// Tick, SpeedAfter, DistanceOver and SecondsToCover all follow that curve.
//
// THE LIMIT BREAK. moveBackGround.speed is natural + Boost. Boost is the
// blue atom's shield boost: the only way the effective speed passes the
// cap. It rises to its target while the shield is up and settles back to 0
// when it ends; the ramp keeps running on the natural speed underneath.
// Distance, score and the level clock read the effective speed, so a boost
// really does get the ship ahead. (docs/speed-and-loops.md)
public static class SpeedRamp
{
    // ---- tunables: the curve ----
    // The natural speed limit (moveBackGround.speed; HUD 35) ...
    public static float Cap = .35f;
    // ... and where the ramp starts easing into it (HUD 25).
    public static float EaseKnee = .25f;

    // ---- tunables: the limit break ----
    // Each blue atom caught while shielded adds this to the boost ...
    public static float BoostPerAtom = .05f;
    // ... up to this much over natural speed (HUD 45 at the cap).
    public static float MaxBoost = .10f;
    // The boost comes on / eases off at these rates (per second of flight).
    public static float BoostRisePerSecond = .25f;
    public static float BoostSettlePerSecond = .05f;

    // The cap as the HUD shows it (35).
    public static int CapHud { get { return Mathf.RoundToInt(Cap * 100f); } }

    // ---- the limit break ----

    static float boost, boostTarget;

    // What the boost adds to the natural speed right now.
    public static float Boost { get { return boost; } }
    public static float BoostTarget { get { return boostTarget; } }
    // The ramp's own speed: moveBackGround.speed without the boost.
    public static float Natural { get { return Mathf.Max(0f, moveBackGround.speed - boost); } }
    // Past the cap on the boost (the HUD read-out is above the cap's).
    public static bool LimitBroken
    {
        get { return boost > 0f && Mathf.RoundToInt(moveBackGround.speed * 100f) > CapHud; }
    }

    // A blue atom was caught (collisionDetection). Nothing while a boss
    // holds the speed (BossEncounter.SpeedLocked).
    public static void AddBoost()
    {
        if (BossEncounter.SpeedLocked) return;
        boostTarget = Mathf.Min(MaxBoost, boostTarget + BoostPerAtom);
    }

    // The shield ended: the boost eases off (Tick).
    public static void EndBoost() { boostTarget = 0f; }

    // Something else takes the speed over (a boss): the boost is gone at
    // once and the speed is left at its natural value.
    public static void CancelBoost()
    {
        moveBackGround.speed = Natural;
        boost = boostTarget = 0f;
    }

    // A new scene / run: no boost, the speed untouched.
    public static void ResetBoost() { boost = boostTarget = 0f; }

    // Sets the natural speed (a run's start, a portal's arrival), never past
    // the cap; a boost in progress rides on top of it.
    public static void SetNatural(float natural)
    {
        boost = Mathf.Min(boost, Mathf.Max(0f, moveBackGround.speed));
        moveBackGround.speed = Mathf.Clamp(natural, 0f, Cap) + boost;
    }

    static float StepBoost(float dt)
    {
        if (boost < boostTarget) return Mathf.Min(boostTarget, boost + BoostRisePerSecond * dt);
        if (boost > boostTarget) return Mathf.Max(boostTarget, boost - BoostSettlePerSecond * dt);
        return boost;
    }

    // ---- the curve ----

    // A world's cap: never past Cap, whatever a wall was given.
    static float CapOf(float max) { return Mathf.Min(max, Cap); }
    // Where the ease into `cap` starts.
    static float KneeOf(float cap) { return Mathf.Max(0f, cap - Mathf.Max(0f, Cap - EaseKnee)); }

    // The ramp rate at `speed` for a world whose base rate is `rate`.
    public static float RateAt(float speed, float rate)
    {
        float cap = Cap, knee = KneeOf(cap);
        if (speed >= cap) return 0f;
        if (speed <= knee || cap <= knee) return rate;
        return rate * Mathf.Sqrt((cap - speed) / (cap - knee));
    }

    // Seconds the ease takes from the knee to the cap ...
    static float EaseSeconds(float rate, float cap, float knee) { return 2f * (cap - knee) / rate; }
    // ... how far into it `v` is ...
    static float EaseClock(float v, float rate, float cap, float knee)
    {
        return EaseSeconds(rate, cap, knee) * (1f - Mathf.Sqrt(Mathf.Clamp01((cap - v) / (cap - knee))));
    }
    // ... the speed at a reading of that clock ...
    static float EaseSpeed(float clock, float rate, float cap, float knee)
    {
        float k = 1f - Mathf.Clamp01(clock / EaseSeconds(rate, cap, knee));
        return cap - (cap - knee) * k * k;
    }
    // ... and the distance flown between two readings.
    static float EaseDistance(float from, float to, float rate, float cap, float knee)
    {
        float T = EaseSeconds(rate, cap, knee);
        float a = 1f - Mathf.Clamp01(from / T), b = 1f - Mathf.Clamp01(to / T);
        return cap * (to - from) - (cap - knee) * T / 3f * (a * a * a - b * b * b);
    }

    // `speed` after `dt` seconds of ramp toward the cap (a speed already at
    // or over it is left alone).
    public static float Advance(float speed, float rate, float dt)
    {
        if (dt <= 0f || rate <= 0f) return speed;
        return Mathf.Max(speed, SpeedAfter(speed, rate, Cap, dt));
    }

    // Test hooks: edit-mode tests can't advance Time.frameCount or set
    // Time.deltaTime.
    public static Func<int> FrameOverride;
    public static Func<float> DeltaOverride;

    static int lastFrame = int.MinValue;

    static int Frame => FrameOverride != null ? FrameOverride() : Time.frameCount;
    static float Delta => DeltaOverride != null ? DeltaOverride() : Time.deltaTime;

    // One frame of flight: the natural speed climbs the curve toward `max`
    // (never past Cap) and the boost moves toward its target. Returns false
    // when another caller already ticked this frame.
    public static bool Tick(float ratePerSecond, float max)
    {
        int frame = Frame;
        if (frame == lastFrame) return false;
        lastFrame = frame;

        float dt = Delta;
        // (someone zeroed the speed under a boost: the boost goes with it)
        boost = Mathf.Min(boost, Mathf.Max(0f, moveBackGround.speed));
        float natural = moveBackGround.speed - boost;
        float cap = CapOf(max);
        if (natural < cap && dt > 0f && ratePerSecond > 0f)
            natural = Mathf.Min(Mathf.Max(natural, SpeedAfter(natural, ratePerSecond, cap, dt)), cap);
        boost = StepBoost(dt);
        moveBackGround.speed = natural + boost;
        return true;
    }

    public static void ResetFrameGuard()
    {
        lastFrame = int.MinValue;
    }

    // ---- the ramp as a curve (pure; WorldManager measures worlds with it) ----
    // Speed starts at v0, climbs `rate` per second to the knee, eases into
    // the cap (min(max, Cap)) and holds. A start at or above the cap holds
    // where it is (the ramp never lowers a speed).

    // Speed after `seconds` on the curve.
    public static float SpeedAfter(float v0, float rate, float max, float seconds)
    {
        float v = Mathf.Max(0f, v0), cap = CapOf(max), knee = KneeOf(cap);
        if (seconds <= 0f || rate <= 0f || v >= cap) return v;
        if (v < knee)
        {
            float tk = (knee - v) / rate;
            if (seconds <= tk) return v + rate * seconds;
            v = knee;
            seconds -= tk;
        }
        if (cap <= knee) return cap;
        return EaseSpeed(EaseClock(v, rate, cap, knee) + seconds, rate, cap, knee);
    }

    // Distance (speed x seconds) flown in `seconds`.
    public static float DistanceOver(float v0, float rate, float max, float seconds)
    {
        if (seconds <= 0f) return 0f;
        float v = Mathf.Max(0f, v0), cap = CapOf(max), knee = KneeOf(cap), d = 0f;
        if (rate <= 0f || v >= cap) return v * seconds;
        if (v < knee)
        {
            float tk = (knee - v) / rate;
            if (seconds <= tk) return v * seconds + .5f * rate * seconds * seconds;
            d = v * tk + .5f * rate * tk * tk;
            v = knee;
            seconds -= tk;
        }
        if (cap <= knee) return d + cap * seconds;
        float T = EaseSeconds(rate, cap, knee), c0 = EaseClock(v, rate, cap, knee);
        float left = T - c0;
        if (seconds <= left) return d + EaseDistance(c0, c0 + seconds, rate, cap, knee);
        return d + EaseDistance(c0, T, rate, cap, knee) + cap * (seconds - left);
    }

    // Seconds needed to fly `distance` (the inverse of DistanceOver).
    // Infinity when the speed is and stays zero.
    public static float SecondsToCover(float v0, float rate, float max, float distance)
    {
        if (distance <= 0f) return 0f;
        float v = Mathf.Max(0f, v0), cap = CapOf(max), knee = KneeOf(cap), t = 0f;
        if (rate <= 0f || v >= cap) return v > 0f ? distance / v : float.PositiveInfinity;
        if (v < knee)
        {
            float tk = (knee - v) / rate;
            float dk = v * tk + .5f * rate * tk * tk;
            if (distance <= dk) return (-v + Mathf.Sqrt(v * v + 2f * rate * distance)) / rate;
            distance -= dk;
            t = tk;
            v = knee;
        }
        if (cap <= knee) return cap > 0f ? t + distance / cap : float.PositiveInfinity;
        float T = EaseSeconds(rate, cap, knee), c0 = EaseClock(v, rate, cap, knee);
        float whole = EaseDistance(c0, T, rate, cap, knee);
        if (distance >= whole) return t + (T - c0) + (distance - whole) / cap;
        // inside the ease: distance grows with the clock (bisection, no allocation)
        float lo = c0, hi = T;
        for (int i = 0; i < 40; i++)
        {
            float mid = .5f * (lo + hi);
            if (EaseDistance(c0, mid, rate, cap, knee) < distance) lo = mid; else hi = mid;
        }
        return t + .5f * (lo + hi) - c0;
    }
}
