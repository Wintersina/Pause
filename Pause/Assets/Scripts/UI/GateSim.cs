using System;
using UnityEngine;

// The HapticGate splash as a pure, deterministic simulation: no GameObjects, no
// Time, no scene loading. splashScene feeds it real (unscaled) frame time and
// player taps; the editor tests and the preview filmstrips feed it a fake clock.
//
// The intro (no taps): the two steel leaves rattle on their latch with a rising
// amplitude, steam pressure builds, then the leaves are FORCED open in jerky
// steps -- each step snaps, overshoots and recoils, and fires an Impact (flash,
// camera kick, debris, sparks, steam gust). The logo is revealed behind them.
//
// Taps. The intro keeps playing (it is never frozen: a player who taps once and
// walks away still sees the card finish by itself). Every discrete tap queues
// one stage; stages are applied at least TapSpacing apart so even a triple tap
// inside one frame shows crack 1, crack 2, then the smash, and no tap is lost:
//   tap 1 -> crack stage 1 (sharp kick, a few sparks)
//   tap 2 -> crack stage 2 (more cracks, the leaves bulge, steam leaks, bigger kick)
//   tap 3 -> crack stage 3 for one beat, then SMASH: the leaves burst, hard shake,
//            flash; the card is over SmashLeaveDelay later.
// Taps in the first SkipLockout seconds are dropped (a stray tap carried over
// from a previous screen), taps after the smash are ignored.
public sealed class GateSim
{
    public const float IntroSeconds = 2.3f;       // natural length (splashScene.holdSeconds)
    public const float SkipLockout = 0.15f;
    public const float TapSpacing = 0.12f;        // min sim time between applied stages
    public const float SmashLeaveDelay = 0.45f;   // smash -> scene change
    public const int TapsToSmash = 3;

    // Forced-open steps: start time, open fraction reached, snap duration.
    public const int StepCount = 7;
    static readonly float[] StepTime = { 0.70f, 0.92f, 1.10f, 1.22f, 1.40f, 1.58f, 1.76f };
    static readonly float[] StepOpen = { 0.08f, 0.20f, 0.31f, 0.52f, 0.66f, 0.86f, 1.00f };
    static readonly float[] StepDur  = { 0.050f, 0.050f, 0.045f, 0.050f, 0.050f, 0.055f, 0.110f };
    public const float RattleEnd = 0.70f;

    // ---- outputs, read by the view --------------------------------------
    public float time;                 // sim seconds since the card appeared
    public float open;                 // 0 shut .. 1 fully open (stepped, not smooth)
    public float amp;                  // leaf rattle amplitude, 0..1
    public float kick;                 // impact/tap shake impulse, decays fast (0..~1.5)
    public float pressure;             // steam pressure 0..1
    public int crackStage;             // 0..3
    public bool smashed;
    public float smashTime;            // seconds since the smash (0 before it)
    public bool finished;              // true once the scene should change
    public bool smashFinish;           // finished because of the smash (not the natural end)
    public float naturalSeconds = IntroSeconds;   // splashScene.holdSeconds
    public int stepsDone;
    public int pendingTaps;
    public int tapsApplied;

    // Per-leaf jitter, gate-local units / degrees (+x is the leaf's own outward direction).
    public Vector2 leftJitter, rightJitter;
    public float leftRot, rightRot;
    // Shared shake offset in gate-local units (applies to the whole card + words).
    public Vector2 shake;
    // How far each leaf bulges outward / in scale at crack stage 2+ (gate-local, 0..1).
    public float bulge;

    // ---- events (audio / effects hooks) -----------------------------------
    public Action<float, int> Impact;   // strength 0..1, step index (-1 = not a step)
    public Action<int> Cracked;         // crack stage 1..3
    public Action Smashed;
    public Action Done;                 // fires once, when finished becomes true

    float sinceTap = 99f;
    const uint seed = 0x9E3779B9u;
    uint rng = 0x1234ABCDu;
    bool doneFired;

    public GateSim() { Reset(); }

    public void Reset()
    {
        time = open = amp = kick = pressure = 0f;
        crackStage = 0; smashed = false; smashTime = 0f; finished = false; smashFinish = false;
        stepsDone = 0; pendingTaps = 0; tapsApplied = 0; sinceTap = 99f; doneFired = false; rng = 0x1234ABCDu;
        leftJitter = rightJitter = shake = Vector2.zero; leftRot = rightRot = 0f; bulge = 0f;
    }

    // Queue one tap (a discrete press). Returns false if it was dropped.
    public bool Tap()
    {
        if (finished || smashed || time < SkipLockout) return false;
        if (pendingTaps + crackStage >= TapsToSmash) return false;   // enough queued already
        pendingTaps++;
        return true;
    }

    public void Advance(float dt)
    {
        if (dt <= 0f || finished) return;
        time += dt;
        sinceTap += dt;
        kick *= Mathf.Exp(-dt * 7f);
        if (kick < 0.001f) kick = 0f;

        ApplyTaps();
        if (smashed) smashTime += dt;

        // Rattle amplitude: rises through the build, stays high while forcing
        // the leaves, drops once they are through.
        if (time < RattleEnd) { float r = time / RattleEnd; amp = 0.10f + 0.90f * r * r; }
        else amp = Mathf.Lerp(0.85f, 0.25f, Mathf.Clamp01((time - 1.76f) / 0.5f));
        if (smashed) amp = 0f;

        pressure = Mathf.Clamp01(time / 1.0f);

        // Forced-open steps.
        float o = 0f;
        for (int i = 0; i < StepCount; i++)
        {
            if (time < StepTime[i]) break;
            if (i >= stepsDone)
            {
                stepsDone = i + 1;
                float s = (i + 1f) / StepCount;
                kick = Mathf.Max(kick, 0.55f + 0.5f * s);
                if (Impact != null) Impact(0.45f + 0.55f * s, i);
            }
            float prev = i == 0 ? 0f : StepOpen[i - 1];
            float k = Mathf.Clamp01((time - StepTime[i]) / StepDur[i]);
            float ease = 1f - (1f - k) * (1f - k) * (1f - k);
            // overshoot + recoil on the last step; a small recoil on the others
            float recoil = Mathf.Sin(k * Mathf.PI) * (i == StepCount - 1 ? 0.035f : 0.012f);
            o = prev + (StepOpen[i] - prev) * ease + (k < 1f ? recoil : 0f);
        }
        if (time >= StepTime[StepCount - 1] + StepDur[StepCount - 1])
        {
            // dead-stop shudder after the last step
            float since = time - (StepTime[StepCount - 1] + StepDur[StepCount - 1]);
            o = 1f + Mathf.Sin(since * 38f) * Mathf.Exp(-since * 9f) * 0.02f;
        }
        open = o;

        bulge = crackStage >= 2 ? (crackStage >= 3 ? 1f : 0.6f) : 0f;

        // Jitter. Hash noise re-rolled ~50x a second reads as violent rattling;
        // a faster sine on top keeps it moving between rolls.
        float a = amp + kick * 0.9f;
        int roll = (int)(time * 50f);
        float tt = time * 61f;
        leftJitter  = new Vector2(Noise(roll, 1) * 0.0085f + Mathf.Sin(tt) * 0.0025f,
                                  Noise(roll, 2) * 0.0065f) * a;
        rightJitter = new Vector2(Noise(roll, 3) * 0.0085f + Mathf.Sin(tt + 2f) * 0.0025f,
                                  Noise(roll, 4) * 0.0065f) * a;
        leftRot  = Noise(roll, 5) * 0.9f * a;
        rightRot = Noise(roll, 6) * 0.9f * a;
        // Shake of the whole card. Stronger with kicks; always a little during the rattle.
        float sh = amp * 0.012f + kick * 0.045f;
        shake = new Vector2(Noise(roll, 7), Noise(roll, 8)) * sh;

        if (smashed)
        {
            if (smashTime >= SmashLeaveDelay) Finish(true);
        }
        else if (time >= naturalSeconds) Finish(false);
    }

    void ApplyTaps()
    {
        while (pendingTaps > 0 && !smashed && sinceTap >= TapSpacing)
        {
            pendingTaps--;
            sinceTap = 0f;
            tapsApplied++;
            crackStage++;
            if (crackStage >= TapsToSmash)
            {
                // Beat of "stage 3" cracks (visible for the impact frame), then
                // everything bursts.
                kick = 1.6f;
                if (Cracked != null) Cracked(3);
                smashed = true;
                smashTime = 0f;
                pendingTaps = 0;
                if (Smashed != null) Smashed();
                if (Impact != null) Impact(1f, -1);
            }
            else
            {
                kick = Mathf.Max(kick, crackStage == 1 ? 0.8f : 1.15f);
                if (Cracked != null) Cracked(crackStage);
                if (Impact != null) Impact(crackStage == 1 ? 0.35f : 0.6f, -1);
            }
        }
    }

    void Finish(bool bySmash)
    {
        finished = true;
        smashFinish = bySmash;
        if (!doneFired) { doneFired = true; if (Done != null) Done(); }
    }

    // Deterministic noise in -1..1 from (roll, channel).
    float Noise(int roll, int channel)
    {
        uint h = (uint)(roll * 73856093) ^ (uint)(channel * 19349663) ^ seed;
        h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
        return (h & 0xFFFFu) / 32767.5f - 1f;
    }

    // Deterministic particle randomness (view uses this so previews repeat).
    public float Rand()
    {
        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
        return (rng & 0xFFFFFFu) / 16777216f;
    }
}
