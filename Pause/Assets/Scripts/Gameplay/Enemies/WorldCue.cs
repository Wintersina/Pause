using UnityEngine;

// THE HOSTILE CUE COLOUR OF EACH WORLD (user, 2026-10-11: "only Space is supposed to have pink").
//
// Every hostile thing -- roster / elite / boss shots, their trails and impacts, telegraph lines, hazard
// footprints, warning flashes -- carries one cue colour so the player reads "this hurts" at a glance.
// It used to be the same hot pink (HostileShotPalette 312..326) in every world; now each world has its own:
//
//   Space   hot pink (the only pink left)       Verdant  bright green-white (jade, clear of repair-green 82)
//   Frost   icy white-cyan                      Tide     aqua-white
//   Ember   amber / orange-white                Storm    electric white-violet (pale: clear of the capacitor violet)
//   Desert  sun-white / gold
//
// The hue / saturation cap / minimum value of the pale end are THE SAME numbers as MineCue and
// Art/Attacks/MineBeams~/recolor_nopink.py (the approved rail-mine lasers); the body is that hue a little
// more saturated so a tinted procedural sprite still has a colour. Shots stay pointed, stepped and
// flickering (the cue's shape contract), so the atoms (round, smooth, breathing) are never confused with them.
public static class WorldCue
{
    //                                         space  frost verdant ember  tide  storm desert
    public static readonly float[] Hue      = { 318f, 195f, 135f, 32f, 188f, 258f, 45f };
    public static readonly float[] BodySat  = { .70f, .45f, .60f, .85f, .45f, .38f, .33f };   // (Storm / Desert follow their pale elite families: Desert S <= .33 never amber)
    public static readonly float[] BodyVal  = { 1f,   1f,   1f,   1f,   1f,   1f,   1f };
    public const int Worlds = 7;

    public static int Slot(int world) => Mathf.Clamp(world, 0, Worlds - 1);
    public static bool IsSpace(int world) => world <= 0;

    // The saturated cue colour (the body of a shot / the line of a telegraph).
    public static Color Body(int world)
    {
        int k = Slot(world);
        var c = Color.HSVToRGB(Hue[k] / 360f, BodySat[k], BodyVal[k]);
        c.a = 1f;
        return c;
    }

    // The pale end (the outline's light trace, a flare, an aim line's dashes).
    public static Color Trace(int world) => HostileShotPalette.Trace(Body(world));
    public static Color Core(int world) => HostileShotPalette.Core(Body(world));

    // Is `c` in the world's cue family (hue within `slack` degrees of the cue hue, visibly coloured)?
    public static bool InFamily(Color c, int world, float slack = 14f)
    {
        Color.RGBToHSV(c, out float h, out float s, out _);
        if (s <= .2f) return false;
        float d = Mathf.Abs(Mathf.DeltaAngle(h * 360f, Hue[Slot(world)]));
        return d <= slack;
    }

    // Any pink at all (hue 285..352, visibly coloured)? The test of "no pink outside Space".
    public static bool IsPink(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        float d = h * 360f;
        return c.a > 0f && s > .25f && v > .3f && d >= 285f && d <= 352f;
    }
}
