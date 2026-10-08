using UnityEngine;

// When everything in a lift-off happens: every number of it, in seconds
// after the boss is over (WorldManager.OpenPortal), and the curves Liftoff
// draws from them. The landing's moments (PlanetfallTimeline) played
// backwards and upwards; pure functions of the clock, so the tests and the
// preview read the same moments the game plays.
//
//    0.0  BEAT       the boss's wreck settles; nothing new spawns
//    0.8  TAKE       LIFT OFF: the board is cleared, the ship is taken over
//                    and eased to the lane's centre; the thrusters flare,
//                    the rumble builds, the ground rushes (ScrollBoost)
//    1.9  CLIMB      the deep deck, then the bright one, roll down over
//                    everything but the ship; plasma, streaks, the grade
//    2.9  SWAP       the decks cover the whole view: the backdrop and rails
//                    become the interlude world's (Space), unseen
//    4.3  BREAK      flash and the burst at the ship; the decks fall away
//                    below, the planet's horizon shows beneath the ship
//    4.6  SETTLE     the ship eases to the pilot's finger (or its start);
//                    the horizon recedes into the whole globe
//    6.3  RELEASE    control returns: calm space, nothing spawns, the
//                    planet sinks away behind
//    9.6  GATEWAY    the way on opens (WorldManager.OpenGateway)
public static class LiftoffTimeline
{
    // ---- tunables (seconds after the boss is over) ----
    public static float TakeAt = .8f;
    public static float SteerTo = 1.9f;
    public static float RiseFrom = 1.9f;                    // the ship climbs a little through the clouds
    public static float PlumeInTo = 1.3f, PlumeOutFrom = 4.8f, PlumeOutTo = 5.8f;
    public static float StreaksInFrom = 1.2f, StreaksInTo = 1.9f;
    public static float TintInFrom = 1.7f, TintInTo = 2.5f, TintWarmFrom = 2.9f, TintWarmTo = 4.1f;
    public static float TintAlpha = .12f, VignetteAlpha = .7f;
    public static float ShroudInFrom = 2.4f, ShroudInTo = 3.1f, ShroudAlpha = .85f;
    public static float DarkInFrom = 1.9f, DarkInTo = 2.5f;
    public static float DeckInFrom = 2.4f, DeckInTo = 3.0f, DeckAlpha = .78f;
    public static float SwapAt = 2.9f;
    public static float BreakAt = 4.3f;
    public static float FlashRise = .07f, FlashFall = .65f, FlashPeak = .9f;
    public static float BurstFps = 9f;
    public static float CloudsOutTo = 5.3f, DeckOutTo = 4.9f, FxOutTo = 4.6f, GradeOutTo = 5.4f;
    // The planet beneath: its horizon shows from the break, recedes to the
    // horizon view, gives way to the globe, which sinks and fades.
    public static float RecedeTo = 6.4f, GlobeFrom = 5.7f, GlobeTo = 6.3f;
    public static float SinkTo = 9.4f, PlanetOutFrom = 8.6f, PlanetOutTo = 9.4f;
    public static float SettleFrom = 4.6f, SettleTo = 6.1f;
    public static float ReleaseAt = 6.3f;
    public static float GatewayAt = 9.6f;
    // The backdrop's scroll is multiplied up to this from the take to the
    // break (the ground rushes, then the stars), and eases back by release.
    public static float BackdropBoost = 5f, BoostFrom = .9f, BoostTo = 1.9f, BoostOutTo = 6.2f;
    // Rumble, world units: the climb's steady shake and the break's kick.
    public static float ShakeTo = 2.6f, ShakeClimb = .03f, ShakeBreak = .1f, ShakeDecay = 5f;

    public static float Ramp(float t, float a, float b) { return PlanetfallTimeline.Ramp(t, a, b); }

    // ---- the ship ----

    public static float Steer01(float t) { return Ramp(t, TakeAt, SteerTo); }
    public static float Rise01(float t) { return Ramp(t, RiseFrom, BreakAt); }
    public static float Settle01(float t) { return Ramp(t, SettleFrom, SettleTo); }
    public static float PlumeAlpha(float t) { return Ramp(t, TakeAt, PlumeInTo) * (1f - Ramp(t, PlumeOutFrom, PlumeOutTo)); }
    // The thrusters' flare: 0 idle, 1 full climb power.
    public static float Thrust(float t) { return Ramp(t, TakeAt, SteerTo) * (1f - .6f * Ramp(t, BreakAt, PlumeOutTo)); }

    // ---- the clouds ----

    public static float DarkDeckAlpha(float t) { return Ramp(t, DarkInFrom, DarkInTo) * (1f - Ramp(t, BreakAt, CloudsOutTo)); }
    public static float DeckAlphaAt(float t) { return DeckAlpha * Ramp(t, DeckInFrom, DeckInTo) * (1f - Ramp(t, BreakAt, DeckOutTo)); }

    // How much of the view the decks hide.
    public static float Cover(float t) { return 1f - (1f - DarkDeckAlpha(t)) * (1f - DeckAlphaAt(t)); }

    // Zoom of the decks: close as the ship enters them, opening out as it
    // climbs, and falling away fast below it at the break.
    public static float DeckZoom(float t)
    {
        float climb = 1.45f - .4f * Ramp(t, DarkInFrom, BreakAt);
        float fall = 1f - .55f * Ramp(t, BreakAt, CloudsOutTo);
        return climb * fall;
    }

    // How fast the decks stream down past the ship (x their base flow).
    public static float DeckRush(float t) { return 2f + 2.5f * Ramp(t, BreakAt, CloudsOutTo); }

    // ---- the climb's grade and effects ----

    public static float ShroudAt(float t) { return ShroudAlpha * Ramp(t, ShroudInFrom, ShroudInTo) * (1f - Ramp(t, BreakAt, FxOutTo)); }
    public static float StreakAlpha(float t) { return Ramp(t, StreaksInFrom, StreaksInTo) * (1f - Ramp(t, BreakAt, FxOutTo)); }
    public static float TintAt(float t) { return TintAlpha * Ramp(t, TintInFrom, TintInTo) * (1f - Ramp(t, BreakAt, GradeOutTo)); }
    // 1 the cold deep cloud, 0 the warm upper air (the landing's, reversed).
    public static float Coolness(float t) { return 1f - Ramp(t, TintWarmFrom, TintWarmTo); }
    public static float VignetteAt(float t) { return VignetteAlpha * Ramp(t, TintInFrom, TintInTo) * (1f - Ramp(t, BreakAt, GradeOutTo)); }

    // ---- the break ----

    public static float Flash(float t)
    {
        if (t < BreakAt - FlashRise) return 0f;
        if (t < BreakAt) return FlashPeak * Ramp(t, BreakAt - FlashRise, BreakAt);
        return FlashPeak * (1f - Ramp(t, BreakAt, BreakAt + FlashFall));
    }

    public static int BurstFrame(float t, int frames)
    {
        if (t < BreakAt || frames <= 0) return -1;
        int f = Mathf.FloorToInt((t - BreakAt) * BurstFps);
        return f < frames + 2 ? Mathf.Min(f, frames - 1) : -1;
    }

    public static float BurstAlpha(float t, int frames)
    {
        float end = BreakAt + (frames + 2) / Mathf.Max(1f, BurstFps);
        return t < BreakAt ? 0f : 1f - Ramp(t, end - 2f / Mathf.Max(1f, BurstFps), end);
    }

    // ---- the planet beneath ----

    // 0 at the break (the horizon close below), 1 at the horizon view.
    public static float Recede01(float t) { return Ramp(t, BreakAt, RecedeTo); }
    // 0 the horizon view, 1 the globe small and low (then gone).
    public static float Sink01(float t)
    {
        float x = Mathf.Clamp01((t - RecedeTo) / Mathf.Max(.01f, SinkTo - RecedeTo));
        return x * (2f - x);
    }
    public static float SurfaceAlpha(float t) { return t < BreakAt - .05f ? 0f : 1f - Ramp(t, PlanetOutFrom, PlanetOutTo); }
    public static float LimbAlpha(float t) { return SurfaceAlpha(t) * (1f - Ramp(t, GlobeFrom, GlobeTo)); }
    // The globe's atmosphere glow, once the globe is seen whole.
    public static float RimAlpha(float t) { return .45f * Ramp(t, GlobeFrom, GlobeTo) * SurfaceAlpha(t); }

    // ---- the backdrop, the rumble ----

    public static float Boost(float t)
    {
        return 1f + (BackdropBoost - 1f) * Ramp(t, BoostFrom, BoostTo) * (1f - Ramp(t, BreakAt, BoostOutTo));
    }

    public static float Shake(float t)
    {
        float climb = ShakeClimb * Ramp(t, TakeAt, ShakeTo) * (1f - Ramp(t, BreakAt, BreakAt + .8f));
        float kick = t >= BreakAt ? ShakeBreak * Mathf.Exp(-(t - BreakAt) * ShakeDecay) : 0f;
        return t >= ReleaseAt ? 0f : climb + kick;
    }
}
