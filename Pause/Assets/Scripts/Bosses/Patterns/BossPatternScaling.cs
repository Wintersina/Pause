using UnityEngine;

// THE LOOP SEAM of the signature bullet patterns: on loop 2..n everything gets harder, exponentially. LoopFactor is 1 on loop 1
// (and every number below is then exactly the authored one) and the coordinator wires it to the run's difficulty
// (LoopDifficulty.Current: Base^(loop-1)). The patterns answer with a SUBLINEAR, hard-capped response so loop 6+ is brutal but
// fair and still 60 fps:
//
//   Density       x min(MaxDensity, f^.5)    the firing rate (more volleys a second), never an interval under MinInterval
//   Speed         x min(MaxSpeed, f^.2)      the shots' speed (every gap is across the lane, so more speed never closes one)
//   ExtraArms     min(MaxExtraArms, floor(log2(f) / 2))   more arms on a tornado, arms or orbit; more shots in a fan or ring
//   RingCap       the most shots a ring / rose / fan is built from
//   Capacity      BossSignatureTuning.MaxLive: past it the runner holds a volley back (BossPatternRunner.Room)
//
// The gaps are never scaled: a wall's gap, a ring's gap in degrees, a fan's pitch, a rose's gap all stay as authored, so the free
// run BossSignatureTest measures holds at any factor (it checks LoopFactor = 20).
public static class BossPatternScaling
{
    public static float LoopFactor = 1f;

    public const float MaxDensity = 1.8f, MaxSpeed = 1.4f;
    public const int MaxExtraArms = 2, RingCap = 44;
    public const float MinInterval = .11f;     // seconds between two volleys of one stream, however dense

    static float F => Mathf.Max(1f, LoopFactor);

    public static bool Scaled => LoopFactor > 1.0001f;
    public static float Density => Mathf.Min(MaxDensity, Mathf.Sqrt(F));
    public static float Speed => Mathf.Min(MaxSpeed, Mathf.Pow(F, .2f));
    // the lane and a wall's gap WIDEN with the loop (never shrink): +.1 u of lane half width per extra arm
    public static float GapBonus => .1f * ExtraArms;
    public static int ExtraArms => Mathf.Min(MaxExtraArms, Mathf.FloorToInt(Mathf.Log(F, 2f) * .5f));

    // a count of shots grown with the factor, capped
    public static int Count(int authored) => ExtraArms == 0 ? authored : Mathf.Min(RingCap, Mathf.RoundToInt(authored * (1f + .2f * ExtraArms)));
}
