using UnityEngine;

// The fighting zone: the part of the view under the HUD's top band (the score read-out, the home / replay icons). No hostile --
// an elite, a roster pilot, a chaser -- perches, hovers, stands off, patrols or aims from above its top: it may fly through on
// the way in (a descent from off the top) or out (an escape), but anything it HOLDS is clamped here.
//
// The edge is read from the live HUD layout, never guessed: PlayField.Live.bandBottom (the lowest edge of the band, in world
// units, from TopBand / HudStyler), the same edge the player's own reach stops at (ShipReach.TopFor). The zone top for a body
// is that edge minus the body's drawn half-height and a small Margin, so its whole hull is clear of the band. With no band laid
// out (the editor, batch mode, a landscape window) the band is NoBandShare of the view's height.
//
// Elites route EVERY goal through EliteShip.ClampGoal (-> FightZone.TopFor), roamers' perch / ready / re-entry points through
// EliteShip.ZoneY; a new brain that picks a point it will hold must do the same.
public static class FightZone
{
    // Clear space between a hull's top and the band's bottom (world u).
    public const float Margin = .1f;
    // The band's depth as a share of the view's height when no band is laid out.
    public const float NoBandShare = .17f;
    // An attack may start this far over the zone top (a hull still settling onto its station).
    public const float AttackSlack = .35f;

    public static float BandBottomFor(PlayField.Frame f)
    {
        return f.hasBand ? f.bandBottom : f.top - f.Height * NoBandShare;
    }

    // The highest centre a body drawn `halfHeight` above its centre may hold in this view.
    public static float TopFor(PlayField.Frame f, float halfHeight)
    {
        return Mathf.Max(f.bottom + 1f, BandBottomFor(f) - Mathf.Max(0f, halfHeight) - Margin);
    }

    // ... in the live view.
    public static float Top(float halfHeight) { return TopFor(PlayField.Live, halfHeight); }

    public static float ClampY(float y, float halfHeight) { return Mathf.Min(y, Top(halfHeight)); }

    // The drawn half-height an elite is clamped by: its hull, or a third of its cell where the drawing is bigger.
    public static float HalfOf(EliteDef def)
    {
        return Mathf.Max(def.hullRadius, def.cellWorldSize * .32f);
    }
}
