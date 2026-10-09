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
//
// Frost's ground sites (FrostDirector) are emerge sites too, each its own
// kind: an ice-shelf hangar, a rig's lift bay, a relay pad ring, a crawler
// garage, a silo hatch -- drawn shut until the launch tell opens them.
//
// Verdant's (VerdantDirector) are emerge sites too: a root-braced hangar in
// a giant tree, a river bay on stilts, a petal pod pad, a tower bay and a
// hatch under the roots (Hatch, shared with Frost's silo hatch).
public enum LandingKind { Ground, Station, Planet, Asteroid, Hangar, RigBay, PadRing, CrawlerBay, Hatch, RootHangar, RiverBay, PodPad, TowerBay }

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

    // "station" / "planet" / "asteroid" / "ground" / "hangar" / "rigbay" /
    // "padring" / "crawlerbay" / "hatch" / "roothangar" / "riverbay" / "podpad" /
    // "towerbay" (EliteDef.launchFrom); anything else:
    // null (any pad). A launchFrom may list several, comma separated
    // ("hangar,crawlerbay"): KindOf is the first, Accepts takes any of them.
    public static LandingKind? KindOf(string s)
    {
        if (s != null && s.IndexOf(',') >= 0) s = s.Substring(0, s.IndexOf(',')).Trim();
        switch (s)
        {
            case "hangar": return LandingKind.Hangar;
            case "rigbay": return LandingKind.RigBay;
            case "padring": return LandingKind.PadRing;
            case "crawlerbay": return LandingKind.CrawlerBay;
            case "hatch": return LandingKind.Hatch;
            case "roothangar": return LandingKind.RootHangar;
            case "riverbay": return LandingKind.RiverBay;
            case "podpad": return LandingKind.PodPad;
            case "towerbay": return LandingKind.TowerBay;
            case "station": return LandingKind.Station;
            case "planet": return LandingKind.Planet;
            case "asteroid": return LandingKind.Asteroid;
            case "ground": return LandingKind.Ground;
            default: return null;
        }
    }

    // Does an elite that launches from `launchFrom` want a site of `kind`?
    // (Empty / unknown: it takes any.) Allocates only for a listed launchFrom.
    public static bool Accepts(string launchFrom, LandingKind kind)
    {
        if (string.IsNullOrEmpty(launchFrom)) return true;
        if (launchFrom.IndexOf(',') < 0)
        {
            var k = KindOf(launchFrom);
            return k == null || k.Value == kind;
        }
        bool known = false;
        foreach (string part in launchFrom.Split(','))
        {
            var k = KindOf(part.Trim());
            if (k == null) continue;
            known = true;
            if (k.Value == kind) return true;
        }
        return !known;
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
