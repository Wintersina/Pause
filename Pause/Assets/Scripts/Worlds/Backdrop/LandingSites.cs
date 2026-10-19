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
// surface, an asteroid's hollow): hidden while parked -- only one small
// light blinks at the dock in the launch tell (EliteLaunchTell) -- it flies out
// without a flash and fades in as it rises (EliteShip).
//
// Frost's ground sites (FrostDirector) are emerge sites too, each its own
// kind: an ice-shelf hangar, a rig's lift bay, a relay pad ring, a crawler
// garage, a silo hatch -- drawn shut until the launch tell opens them.
//
// Frost's four launch sites with a 6-cell launch strip (docks atlas, FrostTuning.SiteKinds): an ice-locked offshore rig (Rig, on water), a
// cryo silo (Silo, on snow flats), a glacier cave hangar (Cave, on ridges) and an icebreaker deck (Icebreaker, on water). Their doors open
// through the launch tell -- a small blinking lamp, no flash -- like Space's docks do.
//
// Verdant's (VerdantDirector) are emerge sites too: a root-braced hangar in
// a giant tree, a river bay on stilts, a petal pod pad, a tower bay and a
// hatch under the roots (Hatch, shared with Frost's silo hatch).
//
// Tide's (TideDirector) are emerge sites too: a trench hatch in the sea floor
// shoals, a rig deck bay, a reef dock, a vent stack and a wreck bay -- shut
// until the launch tell opens them.
//
// Desert's (DesertDirector) are emerge sites too: a worm burrow hatch in the dunes, a
// crawler bay, a fused-glass pad, a buried silo and a ruin dock -- shut until the launch
// tell opens them.
//
// Space's backdrop docks (SpaceDocks.cs) are emerge sites too: an orbital shipyard gantry, a carrier hangar, a ring gate and a derelict
// dock -- drifting set pieces whose doors / iris open through the launch tell. Gantry / Carrier / Gate / Derelict.
public enum LandingKind { Ground, Station, Planet, Asteroid, Hangar, RigBay, PadRing, CrawlerBay, Hatch, RootHangar, RiverBay, PodPad, TowerBay,
                         FoundryHangar, MagmaBay, SlagPad, FurnaceBay,
                         TideTrenchHatch, TideRigDeck, TideReefDock, TideVentStack, TideWreckBay,
                         DesertWormBurrow, DesertCrawlerBay, DesertGlassPad, DesertBuriedSilo, DesertRuinDock,
                         Gantry, Carrier, Gate, Derelict,
                         Rig, Silo, Cave, Icebreaker }

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
    // "towerbay" / "foundryhangar" / "magmabay" / "slagpad" / "furnacebay" / "trenchhatch" / "rigdeck" /
    // "reefdock" / "ventstack" / "wreckbay" / "wormburrow" / "caravanbay" / "glasspad" / "buriedsilo" / "ruindock" / Space's "gantry" / "carrier" / "gate" / "derelict" / Frost's "rig" / "silo" / "cave" / "icebreaker" (EliteDef.launchFrom); anything else:
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
            case "foundryhangar": return LandingKind.FoundryHangar;
            case "magmabay": return LandingKind.MagmaBay;
            case "slagpad": return LandingKind.SlagPad;
            case "furnacebay": return LandingKind.FurnaceBay;
            case "trenchhatch": return LandingKind.TideTrenchHatch;
            case "rigdeck": return LandingKind.TideRigDeck;
            case "reefdock": return LandingKind.TideReefDock;
            case "ventstack": return LandingKind.TideVentStack;
            case "wreckbay": return LandingKind.TideWreckBay;
            case "wormburrow": return LandingKind.DesertWormBurrow;
            case "caravanbay": return LandingKind.DesertCrawlerBay;
            case "glasspad": return LandingKind.DesertGlassPad;
            case "buriedsilo": return LandingKind.DesertBuriedSilo;
            case "ruindock": return LandingKind.DesertRuinDock;
            case "gantry": return LandingKind.Gantry;
            case "carrier": return LandingKind.Carrier;
            case "gate": return LandingKind.Gate;
            case "derelict": return LandingKind.Derelict;
            case "rig": return LandingKind.Rig;
            case "silo": return LandingKind.Silo;
            case "cave": return LandingKind.Cave;
            case "icebreaker": return LandingKind.Icebreaker;
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

    // An elite is due soon and none of the kinds it launches from (`launchFrom`, see LandingSite.Accepts) is on screen: ask the
    // backdrop to bring one of its home bodies into view (BackdropDirector.BringHome: Space's drifting docks). True when one is
    // there or on its way. Nothing for a fixed Override list or a world that cannot (the others' landmarks are fixed in the ground).
    public static bool BringHome(string launchFrom)
    {
        HomeRequests++;
        if (Override != null) return false;
        var wb = WorldBackdrop.Instance;
        var set = wb != null ? wb.Current : null;
        if (set == null || set.Director == null || !set.Complete) return false;
        return set.Director.BringHome(launchFrom);
    }

    // Requests made (tests).
    public static int HomeRequests;

    public static int Gather(List<LandingSite> into)
    {
        if (Override != null) { into.Clear(); Override(into); return into.Count; }
        return Collect(into);
    }
}
