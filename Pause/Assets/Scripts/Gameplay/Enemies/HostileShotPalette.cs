using UnityEngine;

// ONE hue family for every elite and roster enemy shot: magenta to hot pink
// (HueMin..HueMax degrees), so no shot ever wears an atom's colour.
//
// The worlds used to fire in their element -- Frost cyan (#6EF2EE, the blue
// shield atom's very own light tone), Verdant lime (the green heal atom),
// Ember amber (star dust) -- and several elites in violet (the capacitor
// atom) or with cyan cores. Players kept grabbing bullets for atoms. Now:
//
//   atoms         cyan 178, green 82, red 7 / 357, violet 259, amber 37 deg
//   hostile shots 312 .. 326 deg, saturated, a pink-white core
//
// The band keeps clear of the player's red (HostileGlow.RedBandDeg: past
// 332) and of the violet capacitor (259). A source colour keeps a little of
// its identity: its hue picks a spot inside the band, so two elites still
// differ slightly.
public static class HostileShotPalette
{
    public const float HueMin = 312f, HueMax = 326f;
    public const float MinSaturation = .62f, MaxSaturation = .8f, MinValue = 1f;
    public const float CoreWhite = .75f;   // the core: the body this far towards white
    public const float TraceWhite = .3f;  // the outline's light trace (ShotOutline): this far towards white

    public static Color Body(Color source)
    {
        Color.RGBToHSV(source, out float h, out float s, out float v);
        float deg = HueMin + (HueMax - HueMin) * Mathf.Repeat(h * 360f, 60f) / 60f;
        var c = Color.HSVToRGB(deg / 360f, Mathf.Clamp(s, MinSaturation, MaxSaturation), Mathf.Max(v, MinValue));
        c.a = 1f;
        return c;
    }

    public static Color Core(Color body)
    {
        var c = Color.Lerp(body, Color.white, CoreWhite);
        c.a = 1f;
        return c;
    }

    public static bool InFamily(Color c, float slack = 1f)
    {
        Color.RGBToHSV(c, out float h, out float s, out _);
        float deg = h * 360f;
        return s > .3f && deg >= HueMin - slack && deg <= HueMax + slack;
    }

    // The core's stepped flicker (EliteShot): a hard on/off between the core
    // and white-hot, FlickerHz times a second on the world's clock -- a
    // danger tell, never the atoms' smooth breath.
    public const float FlickerHz = 5f;
    public static bool FlickerHot(float age) => (Mathf.FloorToInt(age * FlickerHz * 2f) & 1) == 1;
}
