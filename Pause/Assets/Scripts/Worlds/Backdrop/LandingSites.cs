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
public struct LandingSite
{
    public Transform anchor;
    public Vector3 local;
    public float scale;
    public int order;
    public int id;

    public bool Valid => anchor != null && anchor.gameObject.activeInHierarchy;
    public Vector3 Position => anchor != null ? anchor.TransformPoint(local) : Vector3.zero;
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
