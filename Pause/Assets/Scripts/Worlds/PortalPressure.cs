using System;
using UnityEngine;

// What happens while a portal is open and the pilot has not flown through it.
//
// The portal waits for ever (Portal). So that waiting is never the smart
// move, the board presses harder the longer it lasts: gently at first, then
// without limit, until the pilot enters or dies. Every number is here; the
// spawner, the pilots' airspace, the shot budget, the chasers and the lane
// guard each read their one dial. (docs/speed-and-loops.md has the table.)
//
//   Seconds   flight seconds since the portal opened. WorldManager.Tick
//             feeds it, so a paused or frozen world adds nothing.
//   Level     max(0, Seconds - GraceSeconds) / LevelSeconds: continuous,
//             monotonic, unbounded.
//
//   grace     the board is thinner than usual (GraceDensity), no pilot is
//             admitted, everything earns as normal: a breath after the boss
//   then      spawn rate, the threat ceiling, pilot load, chasers, the shot
//             budget and the volley cadence all climb with Level. The ones
//             that put bodies on screen stop at their caps (60 fps)
//   overdrive past OverdriveLevel the dials that cost nothing to draw keep
//             going with no ceiling: shots and chasers get faster, and the
//             ship-wide gap the lane guard promises in every row shrinks to
//             nothing. Staying is eventually fatal.
//
// ANTI-FARM. Once the grace is over the run earns nothing until the portal
// is flown (EarningsClosed): no points from flight, kills, elites, absorbs,
// pickups or teleports, no kill chain, no death combo, no star dust, and no
// star clusters are released. Boss and world bonuses are untouched (the
// world's is paid on entering).
//
// The natural speed cap (SpeedRamp.Cap) applies throughout.
public static class PortalPressure
{
    // ---- tunables: the clock ----
    public static float GraceSeconds = 8f;
    public static float LevelSeconds = 10f;

    // ---- tunables: what climbs with Level ----
    // Spawn rate (every spawn timer): thinner in the grace, then 1 + this per Level.
    public static float GraceDensity = .6f;
    public static float DensityPerLevel = .35f;
    // Threat ceiling (bodies + weighted shots, authored 10 u view) ...
    public static float ThreatsPerLevel = 1.5f;
    // ... up to this many in the authored view, and never more than
    // BodyCapAbsolute however tall the view (60 fps).
    public static float BodyCap = 24f;
    public static float BodyCapAbsolute = 36f;
    public static float PilotLoadPerLevel = .5f, PilotLoadBonusCap = 4f;
    public static float ChasersPerLevel = .5f;
    public static int ChaserBonusCap = 3;
    public static float ShotsPerLevel = 2f;
    public static int ShotCap = 30;
    public static float VolleyGapPerLevel = .3f;

    // ---- tunables: overdrive (no ceiling) ----
    public static float OverdriveLevel = 6f;
    public static float ShotSpeedPerOverdrive = .08f;
    public static float ChaserSpeedPerOverdrive = .10f;
    public static float GapShrinkPerOverdrive = .08f;

    // ---- text ----
    public const string OpenBanner = "PORTAL OPEN";
    public const string UrgeBanner = "ENTER THE PORTAL";
    public const string ChipPrefix = "PORTAL  DANGER ";

    public enum Signal
    {
        Open,       // the portal appeared
        GraceOver,  // the pressure starts; earnings close
        LevelUp,    // each whole Level
        Entered,    // the pilot flew through
        Closed,     // gone without being flown (a reset, developer jump)
    }

    // Sound, HUD: subscribe once.
    public static event Action<Signal> Beat;

    // ---- the curve (pure: tests read it at any time) ----

    public static float LevelAt(float seconds)
    {
        return Mathf.Max(0f, seconds - GraceSeconds) / Mathf.Max(.01f, LevelSeconds);
    }

    public static float OverdriveAt(float seconds) { return Mathf.Max(0f, LevelAt(seconds) - OverdriveLevel); }

    public static float DensityAt(float seconds)
    {
        return seconds < GraceSeconds ? GraceDensity : 1f + DensityPerLevel * LevelAt(seconds);
    }

    public static float ThreatBonusAt(float seconds) { return ThreatsPerLevel * LevelAt(seconds); }
    public static float PilotLoadBonusAt(float seconds)
    {
        return Mathf.Min(PilotLoadBonusCap, PilotLoadPerLevel * LevelAt(seconds));
    }
    public static int ChaserBonusAt(float seconds)
    {
        return Mathf.Min(ChaserBonusCap, Mathf.FloorToInt(ChasersPerLevel * LevelAt(seconds)));
    }
    public static int ShotBonusAt(float seconds) { return Mathf.FloorToInt(ShotsPerLevel * LevelAt(seconds)); }
    public static float VolleyGapScaleAt(float seconds) { return 1f / (1f + VolleyGapPerLevel * LevelAt(seconds)); }
    public static float ShotSpeedScaleAt(float seconds) { return 1f + ShotSpeedPerOverdrive * OverdriveAt(seconds); }
    public static float ChaserSpeedScaleAt(float seconds) { return 1f + ChaserSpeedPerOverdrive * OverdriveAt(seconds); }
    public static float ShipGapScaleAt(float seconds)
    {
        return Mathf.Max(0f, 1f - GapShrinkPerOverdrive * OverdriveAt(seconds));
    }

    // One number for "how deadly": the product of everything that has no
    // ceiling. Monotonic and unbounded in `seconds` (tests).
    public static float LethalityAt(float seconds)
    {
        return DensityAt(seconds) / Mathf.Max(1e-3f, VolleyGapScaleAt(seconds)) *
               ShotSpeedScaleAt(seconds) * ChaserSpeedScaleAt(seconds);
    }

    // The threat ceiling with a portal waiting: `baseCeiling` (authored
    // view) plus the bonus, up to BodyCap, scaled to the view, never past
    // BodyCapAbsolute.
    public static float CeilingAt(float seconds, float baseCeiling, float viewScale)
    {
        float authored = Mathf.Min(Mathf.Max(baseCeiling, BodyCap), baseCeiling + ThreatBonusAt(seconds));
        return Mathf.Min(Mathf.Max(BodyCapAbsolute, baseCeiling * viewScale), authored * viewScale);
    }

    // ---- live state ----

    public static bool Active { get; private set; }
    // Flight seconds since the portal opened (0 when none is).
    public static float Seconds { get; private set; }
    // The world the open portal leads to (-1: none).
    public static int Destination { get; private set; } = -1;

    public static float Level { get { return Active ? LevelAt(Seconds) : 0f; } }
    public static bool InGrace { get { return Active && Seconds < GraceSeconds; } }
    // The pressure has started: pilots are admitted again, and nothing earns.
    public static bool Pressing { get { return Active && Seconds >= GraceSeconds; } }
    public static bool EarningsClosed { get { return Pressing; } }
    public static bool AdmitsPilots { get { return Pressing; } }
    // "DANGER 1" as the pressure starts, +1 each Level.
    public static int DangerNumber { get { return Pressing ? 1 + Mathf.FloorToInt(Level) : 0; } }

    // The dials, live. Neutral (x1 / +0) whenever no portal is waiting.
    // Plain fields set by Refresh: each is read on hot paths.
    public static float DensityScale = 1f;
    public static float ThreatBonus;
    public static float PilotLoadBonus;
    public static int ChaserBonus;
    public static int ShotBonus;
    public static float VolleyGapScale = 1f;
    public static float ShotSpeedScale = 1f;
    public static float ChaserSpeedScale = 1f;
    public static float ShipGapScale = 1f;

    public static float Ceiling(float baseCeiling, float viewScale)
    {
        return Active ? CeilingAt(Seconds, baseCeiling, viewScale) : baseCeiling * viewScale;
    }

    // WorldManager: a portal opened, leading to `destination`.
    public static void Open(int destination)
    {
        Active = true;
        Seconds = 0f;
        Destination = destination;
        Refresh();
        Raise(Signal.Open);
    }

    // WorldManager: one flying frame with the portal open.
    public static void Tick(float dt)
    {
        if (!Active || dt <= 0f) return;
        float before = Seconds;
        Seconds += dt;
        Refresh();
        if (before < GraceSeconds && Seconds >= GraceSeconds) Raise(Signal.GraceOver);
        else if (Mathf.FloorToInt(LevelAt(Seconds)) > Mathf.FloorToInt(LevelAt(before))) Raise(Signal.LevelUp);
    }

    // The portal is gone: flown through (`entered`) or removed.
    public static void Close(bool entered)
    {
        if (!Active) return;
        Clear();
        Raise(entered ? Signal.Entered : Signal.Closed);
    }

    // A new scene / run: silently back to neutral.
    public static void Reset() { Clear(); }

    static void Clear()
    {
        Active = false;
        Seconds = 0f;
        Destination = -1;
        Refresh();
    }

    static void Refresh()
    {
        if (!Active)
        {
            DensityScale = VolleyGapScale = ShotSpeedScale = ChaserSpeedScale = ShipGapScale = 1f;
            ThreatBonus = PilotLoadBonus = 0f;
            ChaserBonus = ShotBonus = 0;
            return;
        }
        float s = Seconds;
        DensityScale = DensityAt(s);
        ThreatBonus = ThreatBonusAt(s);
        PilotLoadBonus = PilotLoadBonusAt(s);
        ChaserBonus = ChaserBonusAt(s);
        ShotBonus = ShotBonusAt(s);
        VolleyGapScale = VolleyGapScaleAt(s);
        ShotSpeedScale = ShotSpeedScaleAt(s);
        ChaserSpeedScale = ChaserSpeedScaleAt(s);
        ShipGapScale = ShipGapScaleAt(s);
    }

    static void Raise(Signal signal)
    {
        var handler = Beat;
        if (handler == null) return;
        try { handler(signal); }
        catch (Exception e) { Debug.LogException(e); }   // a HUD or sound problem never stops the portal
    }
}
