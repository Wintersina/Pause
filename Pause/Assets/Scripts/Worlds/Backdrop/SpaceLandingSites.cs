using System.Collections.Generic;
using UnityEngine;

// Space's elite launch sites. Space has no ground, so its elites don't sit
// on a pad: they come OUT of the scenery that already drifts through the
// backdrop -- a station's hangar, a planet's surface, a big asteroid's
// hollow (LandingSite.emerge: hidden while docked, engine lights blinking
// at the dock in the launch tell, then a flare and the ship flying out,
// growing from the body's depth into the play layer; EliteShip).
//
// Sites are only offered on bodies still in the upper part of the view (a
// launch has time to be seen), inside the view sideways, and big enough to
// plausibly hold an elite: every lone station, the planets drawn at least
// PlanetMinSize wide, and the planetoids (rocks) drawn at least
// AsteroidMinSize wide -- the big asteroids, not the pre-shrunk pebbles.
// Each site says what it is (LandingKind) so an elite launches from the
// kind its def names (EliteDef.launchFrom).
//
// Kept in its own file (a partial of SpaceDirector) so the backdrop's own
// code stays untouched by the elite wiring.
public partial class SpaceDirector
{
    // Pad offsets as fractions of the drawing's bounds from its centre (x
    // right, y up). A station launches from its hub (its hangar), a planet
    // from a point on the disc toward the middle of the board, a rock from
    // the hollow at its heart.
    public static readonly Vector2 StationPad = new Vector2(0f, -.06f);
    public static readonly Vector2 PlanetPad = new Vector2(.2f, .12f);    // x mirrored toward the board's middle
    public static readonly Vector2 AsteroidPad = new Vector2(.06f, 0f);
    public const float PlanetMinSize = .9f, AsteroidMinSize = .45f;
    // A docked ship starts this big (x play size) times the body's width, within these limits.
    public const float EmergeScalePerUnit = .45f, EmergeScaleMin = .14f, EmergeScaleMax = .34f;
    public const int StationIdBase = 200, PlanetIdBase = 300, AsteroidIdBase = 400;

    public override void LandingSites(List<LandingSite> into)
    {
        if (stations == null) return;
        Sites(stations, LandingKind.Station, StationPad, 0f, StationIdBase, into);
        Sites(planets, LandingKind.Planet, PlanetPad, PlanetMinSize, PlanetIdBase, into);
        Sites(planetoids, LandingKind.Asteroid, AsteroidPad, AsteroidMinSize, AsteroidIdBase, into);
    }

    void Sites(BackdropPool pool, LandingKind kind, Vector2 pad, float minSize, int idBase, List<LandingSite> into)
    {
        for (int i = 0; i < pool.items.Count; i++)
        {
            var p = pool.items[i];
            if (!p.active || p.parent != null || p.sr == null || p.sr.sprite == null) continue;
            if (p.size < minSize) continue;
            if (p.y < -HalfH * .15f || p.y > HalfH - p.size * .3f) continue;
            if (Mathf.Abs(p.x) > HalfW - .25f) continue;
            Bounds b = p.sr.sprite.bounds;
            float fx = kind == LandingKind.Planet ? (p.x > 0f ? -pad.x : pad.x) : pad.x;
            into.Add(new LandingSite
            {
                anchor = p.root,
                local = new Vector3(b.center.x + fx * b.size.x, b.center.y + pad.y * b.size.y, 0f),
                scale = Mathf.Clamp(p.size * EmergeScalePerUnit, EmergeScaleMin, EmergeScaleMax),
                order = p.sr.sortingOrder + 1,
                id = idBase + i,
                kind = kind,
                emerge = true,
            });
        }
    }
}
