using UnityEngine;

// AN AREA HAZARD, AS THE SHOT-VS-SHOT RULES SEE IT (docs/world-attacks-design.md 0.2).
//
// The themed attacks add five shapes that are not a projectile and not a
// laser: a cone / column JET, a sweeping WAVE band with a gap, an expanding
// BLAST ring with a gap, a lane STRIKE column and a swept LASH arc (plan
// phases 1a-1e). Each is a pooled component that implements this interface and
// registers once with HostileShots next to BossBeam. While ZoneLive:
//
//   * it burns every light and heavy hostile shot that touches its shape
//     (bar its own volley's: same owner, fired less than VolleyGap apart --
//     a blast ring would otherwise erase its own bars at the muzzle), exactly
//     as a live laser does;
//   * it is never stopped by a shot, and the player's weapons do not shoot it
//     down (HostileShots.ShootDownAlong only walks IHostileShot);
//   * the zone's own pulse can hurt other hazards and the ship through the
//     hitbox it carries (FriendlyFire.HostileHit, once per live pulse) --
//     that part is each primitive's, not the registry's.
//
// ZoneTouches is the exact hit geometry (the AttackShape the preview is drawn
// from): a circle at p with radius r overlaps the live footprint.
public interface IHostileZone
{
    bool ZoneLive { get; }                          // harmful right now (never during its tell)
    bool ZoneTouches(Vector2 p, float r);           // does a circle of radius r at p touch the live footprint
    int ZoneOwner { get; }                          // who made it (the shooter's instance id; its volley's owner id)
    float ZoneAge { get; }                          // seconds since it went live
}
