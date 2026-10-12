using System;
using UnityEngine;

// The HapticGate splash as a pure, deterministic simulation: no GameObjects, no
// Time, no scene loading. splashScene feeds it real (unscaled) frame time and
// player taps; the editor tests and the preview filmstrips feed it a fake clock.
//
// The picture is the approved gate-opening film (GateFilm), played on the sim
// clock. The sim adds what the film cannot: the card shake (a rattle that rises
// while the shut gate strains, a kick on each of the film's opening shoves) and
// the player's taps.
//
// Taps. The film keeps playing (it is never frozen: a player who taps once and
// walks away still sees the card finish by itself). Every discrete tap queues
// one stage; stages are applied at least TapSpacing apart so even a triple tap
// inside one frame shows crack 1, crack 2, then the smash, and no tap is lost:
//   tap 1 -> crack stage 1 (sharp kick, sparks and chips off a leaf)
//   tap 2 -> crack stage 2 (more, steam leaks, bigger kick)
//   tap 3 -> SMASH: white flash, debris burst, hard shake, the film jumps to its
//            final frame (gate open, wordmark landed); the card is over
//            SmashLeaveDelay later.
// Taps in the first SkipLockout seconds are dropped (a stray tap carried over
// from a previous screen), taps after the smash are ignored.
public sealed class GateSim
{
    // Natural length (splashScene.holdSeconds): the film (3.33 s, wordmark landed at 3.30 s) plus a short hold.
    public const float HoldAfterFilm = 4f - GateFilm.Length;   // 0.667 s
    public const float IntroSeconds = GateFilm.Length + HoldAfterFilm;   // 4.0 s
    public const float SkipLockout = 0.15f;
    public const float TapSpacing = 0.12f;        // min sim time between applied stages
    public const float SmashLeaveDelay = 0.45f;   // smash -> scene change
    public const int TapsToSmash = 3;

    // The film's opening shoves (it pauses at frame 43-44 and shoves again at 45): each is an impact
    // (card kick + gate_step cue, which the opening clip replaces in a natural run).
    public static readonly float[] StepTime = { GateFilm.FirstSplit, 45f / GateFilm.Fps };
    public static int StepCount { get { return StepTime.Length; } }
    public const float RattleEnd = GateFilm.FirstSplit;

    // ---- outputs, read by the view --------------------------------------
    public float time;                 // sim seconds since the card appeared
    public float open;                 // 0 shut .. 1 fully open (the film's leaves, GateFilm.OpenAt)
    public float amp;                  // leaf rattle amplitude, 0..1
    public float kick;                 // impact/tap shake impulse, decays fast (0..~1.5)
    public float pressure;             // steam pressure 0..1
    public int crackStage;             // 0..3
    public bool smashed;
    public float smashTime;            // seconds since the smash (0 before it)
    public float smashAt;              // sim time of the smash
    public bool finished;              // true once the scene should change
    public bool smashFinish;           // finished because of the smash (not the natural end)
    public float naturalSeconds = IntroSeconds;   // splashScene.holdSeconds
    public int stepsDone;
    public int pendingTaps;
    public int tapsApplied;

    // Shake offset of the whole card, in film widths.
    public Vector2 shake;

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
        crackStage = 0; smashed = false; smashTime = 0f; smashAt = 0f; finished = false; smashFinish = false;
        stepsDone = 0; pendingTaps = 0; tapsApplied = 0; sinceTap = 99f; doneFired = false; rng = 0x1234ABCDu;
        shake = Vector2.zero;
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

        // Rattle amplitude: rises while the shut gate strains, drops once it is through.
        if (time < RattleEnd) { float r = time / RattleEnd; amp = 0.10f + 0.90f * r * r; }
        else amp = Mathf.Lerp(0.85f, 0f, Mathf.Clamp01((time - RattleEnd) / 0.8f));
        if (smashed) amp = 0f;

        pressure = Mathf.Clamp01(time / RattleEnd);

        // The film's shoves.
        for (int i = stepsDone; i < StepCount; i++)
        {
            if (time < StepTime[i] || smashed) break;
            stepsDone = i + 1;
            float s = (i + 1f) / StepCount;
            kick = Mathf.Max(kick, 0.55f + 0.5f * s);
            if (Impact != null) Impact(0.45f + 0.55f * s, i);
        }
        open = smashed ? 1f : GateFilm.OpenAt(time);

        // Shake of the whole card (film widths). Hash noise re-rolled ~50x a second reads as
        // violent rattling; stronger with kicks; always a little while the gate strains.
        int roll = (int)(time * 50f);
        float sh = amp * 0.005f + kick * 0.022f;
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
                smashAt = time;
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
