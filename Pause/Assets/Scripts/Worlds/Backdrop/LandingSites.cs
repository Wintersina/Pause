using System.Collections.Generic;
using UnityEngine;

// A spot on a world's background terrain where an elite ship sits parked
// before it lifts off (EliteShip). The backdrop director that owns the
// terrain reports them (BackdropDirector.LandingSites); a world with no
// elites reports none.
//
// `anchor` is the set piece the site sits on (it scrolls with the world's
// parallax), `local` the pad's offset in the anchor's local space, so a
// parked ship rides along with its volcano / ridge / wreck. `scale` is how
// big a play-size ship looks down there (its depth), `order` the sorting
// order just above the piece, `id` keeps two ships off one pad.
//
// `kind` says what the pad is (a ground pad on a planet world's terrain, or
// Space's station / planet / big asteroid) so an elite can ask for the kind
// it launches from (EliteDef.launchFrom). `emerge`: the ship is not sitting
// on top of the piece but inside it (a station's hangar, a planet's
// surface, an asteroid's hollow): hidden while parked -- only its engine
// lights blink at the dock in the launch tell -- it flies out with a flare
// and fades in as it rises (EliteShip).
public enum LandingKind { Ground, Station, Planet, Asteroid }

public struct LandingSite
{
    public Transform anchor;
    public Vector3 local;
    public float scale;
    public int order;
    public int id;
    public LandingKind kind;
    public bool emerge;

    public bool Valid => anchor != null && anchor.gameObject.activeInHierarchy;
    public Vector3 Position => anchor != null ? anchor.TransformPoint(local) : Vector3.zero;

    // "station" / "planet" / "asteroid" / "ground" (EliteDef.launchFrom); anything else: null (any pad).
    public static LandingKind? KindOf(string s)
    {
        switch (s)
        {
            case "station": return LandingKind.Station;
            case "planet": return LandingKind.Planet;
            case "asteroid": return LandingKind.Asteroid;
            case "ground": return LandingKind.Ground;
            default: return null;
        }
    }
}

public static class LandingSites
{
    // The current world backdrop's sites right now (none without one).
    public static int Collect(List<LandingSite> into)
    {
        into.Clear();
        var wb = WorldBackdrop.Instance;
        var set = wb != null ? wb.Current : null;
        if (set == null || set.Director == null || !set.Complete) return 0;
        set.Director.LandingSites(into);
        return into.Count;
    }

    // Test / preview hook: a fixed list instead of the backdrop's.
    public static System.Action<List<LandingSite>> Override;

    public static int Gather(List<LandingSite> into)
    {
        if (Override != null) { into.Clear(); Override(into); return into.Count; }
        return Collect(into);
    }
}
