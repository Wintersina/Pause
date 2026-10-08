using UnityEngine;

// When everything in a planetfall happens: every number of the descent, in
// seconds after the commit (the moment the ship touched the planet's zone),
// and the curves Planetfall draws from them. Pure functions of that clock,
// so the tests and the preview read the same moments the game plays.
//
//    0.0  COMMIT     the ship is taken over and eased to its dive spot; the
//                    planet swells, its atmosphere rim brightening; the
//                    backdrop's stars streak (WorldBackdrop.ScrollBoost)
//    1.0  LIMB       the curved close-approach horizon fades in over the
//                    swollen disc, exactly on its edge
//    1.5  DIVE       the horizon climbs off the top of the view; plasma
//                    shroud on the ship, the heat tint and vignette build,
//                    the rumble grows
//    2.0  CLOUDS     the deep deck, then the bright one, roll over
//                    everything but the ship; speed streaks at the edges
//    3.6  SWITCH     the decks cover the whole view: the world changes
//                    (WorldManager.Advance) unseen
//    5.0  BREAK      white-cyan flash and the burst at the ship; the decks
//                    rush past and thin out, revealing the new world
//    5.3  SETTLE     the ship eases to the pilot's finger (or its start)
//    7.0  RELEASE    control returns; the level's calm start begins
public static class PlanetfallTimeline
{
    // ---- tunables (seconds after the commit) ----
    public static float SteerSeconds = 1.1f;
    public static float SwellSeconds = 1.5f;
    public static float LimbInFrom = 1.0f, LimbInTo = 1.45f;
    public static float DiveTo = 3.1f;
    public static float SurfaceOutFrom = 2.75f, SurfaceOutTo = 3.0f;
    public static float RimFrom = 0f, RimTo = 1.0f, RimOutFrom = 1.05f, RimOutTo = 1.45f;
    public static float ShroudInFrom = 1.45f, ShroudInTo = 2.0f;
    public static float StreaksInFrom = 2.2f, StreaksInTo = 2.8f;   // with the clouds: never over the bare rails
    public static float DarkInFrom = 2.0f, DarkInTo = 2.7f;
    public static float DeckInFrom = 2.3f, DeckInTo = 3.0f;
    public static float DeckAlpha = .78f;
    public static float TintInFrom = 1.4f, TintInTo = 2.3f, TintCoolFrom = 2.8f, TintCoolTo = 4.4f;
    public static float TintAlpha = .12f, VignetteAlpha = .7f;
    public static float SwitchAt = 3.6f;
    public static float BreakAt = 5.0f;
    public static float FlashRise = .07f, FlashFall = .65f, FlashPeak = .9f;
    public static float BurstFps = 9f;
    public static float CloudsOutFrom = 5.0f, CloudsOutTo = 6.3f;
    public static float DeckOutTo = 5.9f;   // the bright deck drops away first
    public static float FxOutTo = 5.3f;      // shroud and streaks
    public static float GradeOutTo = 6.3f;   // tint and vignette
    public static float SettleFrom = 5.3f, SettleTo = 6.8f;
    public static float BannerAt = 5.7f;
    public static float Seconds = 7.0f;
    // The backdrop's scroll is multiplied up to this while the planet swells.
    public static float BackdropBoost = 4f, BoostFrom = .1f, BoostTo = 1.4f;
    // Rumble, world units: the entry's steady shake and the break's kick.
    public static float ShakeFrom = .6f, ShakeTo = 3.0f, ShakeEntry = .035f, ShakeBreak = .11f, ShakeDecay = 5f;

    // 0 before a, 1 after b, smooth between.
    public static float Ramp(float t, float a, float b)
    {
        if (b <= a) return t >= b ? 1f : 0f;
        float x = Mathf.Clamp01((t - a) / (b - a));
        return x * x * (3f - 2f * x);
    }

    // ---- the surface: planet disc, then its limb ----

    // How far the swell has got (0 commit, 1 the horizon view): accelerating,
    // as an approach does.
    public static float Swell01(float t)
    {
        float x = Mathf.Clamp01(t / Mathf.Max(.01f, SwellSeconds));
        return Mathf.Pow(x, 1.6f);
    }

    // How far the dive has got (0 the horizon view, 1 the horizon gone).
    public static float Dive01(float t)
    {
        float x = Mathf.Clamp01((t - SwellSeconds) / Mathf.Max(.01f, DiveTo - SwellSeconds));
        return x * (2f - x);
    }

    public static float LimbAlpha(float t)
    {
        return Ramp(t, LimbInFrom, LimbInTo) * (1f - Ramp(t, SurfaceOutFrom, SurfaceOutTo));
    }

    // The disc stays under the limb (it covers what is below the limb's
    // bottom edge) until the deep deck has covered both.
    public static float PlanetAlpha(float t) { return 1f - Ramp(t, SurfaceOutFrom, SurfaceOutTo); }

    // The atmosphere rim: brightens through the swell, gives way to the
    // limb's own rim.
    public static float RimGlow(float t) { return Ramp(t, RimFrom, RimTo) * (1f - Ramp(t, RimOutFrom, RimOutTo)); }

    // ---- the clouds ----

    public static float DarkDeckAlpha(float t)
    {
        return Ramp(t, DarkInFrom, DarkInTo) * (1f - Ramp(t, CloudsOutFrom, CloudsOutTo));
    }

    public static float DeckAlphaAt(float t)
    {
        return DeckAlpha * Ramp(t, DeckInFrom, DeckInTo) * (1f - Ramp(t, CloudsOutFrom, DeckOutTo));
    }

    // How much of the view the decks hide (both decks are opaque art).
    public static float Cover(float t)
    {
        return 1f - (1f - DarkDeckAlpha(t)) * (1f - DeckAlphaAt(t));
    }

    // Zoom of the decks: 1 as they arrive, growing as the ship descends
    // through them, and fast as they rush past at the break.
    public static float DeckZoom(float t)
    {
        float descend = 1f + .45f * Ramp(t, DarkInFrom, BreakAt);
        float rush = 1f + 1.6f * Ramp(t, BreakAt, CloudsOutTo);
        return descend * rush;
    }

    // ---- the entry ----

    public static float ShroudAlpha(float t) { return Ramp(t, ShroudInFrom, ShroudInTo) * (1f - Ramp(t, BreakAt, FxOutTo)); }
    public static float StreakAlpha(float t) { return Ramp(t, StreaksInFrom, StreaksInTo) * (1f - Ramp(t, BreakAt, FxOutTo)); }
    public static float TintAt(float t) { return TintAlpha * Ramp(t, TintInFrom, TintInTo) * (1f - Ramp(t, BreakAt, GradeOutTo)); }
    // 0 the warm entry glow, 1 the cold deep cloud.
    public static float Coolness(float t) { return Ramp(t, TintCoolFrom, TintCoolTo); }
    public static float VignetteAt(float t) { return VignetteAlpha * Ramp(t, TintInFrom, TintInTo) * (1f - Ramp(t, BreakAt, GradeOutTo)); }

    // ---- the break ----

    public static float Flash(float t)
    {
        if (t < BreakAt - FlashRise) return 0f;
        if (t < BreakAt) return FlashPeak * Ramp(t, BreakAt - FlashRise, BreakAt);
        return FlashPeak * (1f - Ramp(t, BreakAt, BreakAt + FlashFall));
    }

    // The burst's cell, or -1 when it is not showing.
    public static int BurstFrame(float t, int frames)
    {
        if (t < BreakAt || frames <= 0) return -1;
        int f = Mathf.FloorToInt((t - BreakAt) * BurstFps);
        return f < frames + 2 ? Mathf.Min(f, frames - 1) : -1;   // the last cell holds two ticks
    }

    public static float BurstAlpha(float t, int frames)
    {
        float end = BreakAt + (frames + 2) / Mathf.Max(1f, BurstFps);
        return t < BreakAt ? 0f : 1f - Ramp(t, end - 2f / Mathf.Max(1f, BurstFps), end);
    }

    // ---- the ship, the backdrop, the rumble ----

    public static float Steer01(float t) { return Ramp(t, 0f, SteerSeconds); }
    public static float Settle01(float t) { return Ramp(t, SettleFrom, SettleTo); }

    public static float Boost(float t)
    {
        if (t >= SwitchAt) return 1f;
        return 1f + (BackdropBoost - 1f) * Ramp(t, BoostFrom, BoostTo);
    }

    public static float Shake(float t)
    {
        float entry = ShakeEntry * Ramp(t, ShakeFrom, ShakeTo) * (1f - Ramp(t, BreakAt, BreakAt + .8f));
        float kick = t >= BreakAt ? ShakeBreak * Mathf.Exp(-(t - BreakAt) * ShakeDecay) : 0f;
        return t >= Seconds ? 0f : entry + kick;
    }
}
