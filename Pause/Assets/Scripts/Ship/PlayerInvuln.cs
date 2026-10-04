using UnityEngine;

// Post-hit invulnerability ("i-frames"): losing a heart (a non-fatal hit)
// leaves the ship untouchable for PostHitInvulnSeconds while it blinks back
// into existence. The single clock for it lives here.
//
//   Damage   collisionDetection ignores every hazard (enemies, rocks, mines,
//            boss shots, elites) while Active: no heart is lost and nothing
//            is rammed or destroyed -- the ship passes through harmlessly, so
//            the window can't be farmed for kills. Pickups still collect.
//   Clock    world time: collisionDetection.turnTextsOff ticks it with
//            Time.deltaTime on running-world frames only, beside Cloak, so
//            the game's pause (timeScale 0) freezes it and the resume
//            slow-mo drains it in scaled time.
//   Stacking independent of the blue-atom shield (invTimer) and Cloak
//            (cloakTimer): the ship is safe while any of them runs, i.e. for
//            the longer of the two. Under a shield the shield takes the
//            hits (and still destroys what it touches); this clock neither
//            shortens nor stretches the shield, nor the shield it.
//   Visual   the hull's alpha (lifeControler reads HullAlpha): a fast
//            rematerialise flicker fading in, then a strobe that slows
//            toward the end and finishes on a ghost half-beat, so the snap
//            back to solid when it ends reads as a final flash. Hearts,
//            the floatie and the shield are other renderers and stay put.
//   Fatal    the last heart starts nothing: the death sequence is untouched.
public static class PlayerInvuln
{
    public const float PostHitInvulnSeconds = 2f;

    // Blink shape.
    public const float RematerialiseSeconds = .25f;
    public const float RematerialiseHz = 30f;
    public const float StartBlinkHz = 14f;   // strobe slows from this...
    public const float EndBlinkHz = 4f;      // ...to this at the end
    public const float GhostAlpha = .18f;

    static float remaining;

    // World seconds of post-hit invulnerability left (0 when off).
    public static float Remaining { get { return remaining; } }
    public static bool Active { get { return remaining > 0f; } }

    // A heart was lost (not the last): start, or refresh, the window.
    public static void BeginPostHit() { Begin(PostHitInvulnSeconds); }

    // Never shortens a window already running.
    public static void Begin(float seconds)
    {
        remaining = Mathf.Max(remaining, Mathf.Min(seconds, PostHitInvulnSeconds));
    }

    // Runs the clock down by world (scaled) time; clamps at zero.
    public static void Tick(float dt)
    {
        if (remaining > 0f && dt > 0f) remaining = Mathf.Max(0f, remaining - dt);
    }

    public static void Reset() { remaining = 0f; }

    // The hull's alpha this frame. Solid when off, and while the blue-atom
    // shield is up (the bubble already says "protected").
    public static float HullAlpha
    {
        get
        {
            if (!Active || collisionDetection.atomCheck) return 1f;
            return Alpha(PostHitInvulnSeconds - remaining);
        }
    }

    // Blink alpha `elapsed` world seconds into the window. Pure (tests).
    public static float Alpha(float elapsed)
    {
        const float D = PostHitInvulnSeconds;
        if (elapsed < 0f || elapsed >= D) return 1f;

        if (elapsed < RematerialiseSeconds)
        {
            // Blinking back into existence: a fast flicker fading in.
            float k = elapsed / RematerialiseSeconds;
            bool lit = Frac(elapsed * RematerialiseHz) < .5f;
            return lit ? k : GhostAlpha * k;
        }

        // Strobe whose frequency eases linearly from StartBlinkHz to
        // EndBlinkHz; phase is its integral, a whole number of cycles at D,
        // so the last half-beat is the ghost one and the end snaps solid.
        float phase = elapsed * (StartBlinkHz + (EndBlinkHz - StartBlinkHz) * elapsed / (2f * D));
        return Frac(phase) < .5f ? 1f : GhostAlpha;
    }

    // Strobe frequency at `elapsed` (for tests: it slows toward the end).
    public static float BlinkHz(float elapsed)
    {
        return Mathf.Lerp(StartBlinkHz, EndBlinkHz, Mathf.Clamp01(elapsed / PostHitInvulnSeconds));
    }

    static float Frac(float x) { return x - Mathf.Floor(x); }
}
