using UnityEngine;

// The small seam between world hazards and COVER BOULDERS (feat/cover-boulders): the two never share the screen.
//
//   * The boulder system implements ICoverQuery (CoverHazardQuery, the default of HazardCoverSeam.Cover). Hazards refuse to START while Cover.AnyOnScreen, and an event already
//     running fades out quickly when one appears.
//   * The boulder director asks HazardCoverSeam.HazardBusy before spawning (true from the first frame of a
//     hazard's warning to the end of its fade-out) and keeps its next gap running instead.
//
// Both directions are wired: CoverBoulderDirector.Blocked says "hazard" while WorldHazards.AnyActive.
public interface ICoverQuery
{
    bool AnyOnScreen { get; }                       // a cover boulder is in view (or about to enter)
    bool AnyWithin(Rect world);                     // a boulder's bounds overlap this world rectangle
}

public static class HazardCoverSeam
{
    // The real cover boulders (Gameplay/Cover/CoverHazardQuery); tests may set a stand-in or null (no boulder system).
    public static ICoverQuery Cover = CoverHazardQuery.Instance;

    public static bool CoverOnScreen { get { return Cover != null && Cover.AnyOnScreen; } }

    // A hazard is warning, acting or fading.
    public static bool HazardBusy { get { return WorldHazards.AnyActive; } }
}
