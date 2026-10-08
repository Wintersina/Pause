using UnityEngine;

// An unshielded, non-fatal ram (collisionDetection): the pilot loses a heart
// and what was rammed dies like any other kill instead of silently
// vanishing -- its own blast through the shared path (TargetExplosion.Spawn:
// a rail mine flashes its burst frame and queues its friendly-fire blast,
// RailBombAnimator.Burst; anything may split into spinning pieces at the
// configured rate, EnemySplit). Unpaid, as before: a crash that costs a
// heart is not a kill (AwardDestroyedTarget is for weapons, powers, blinks
// and shielded rams). Not for:
//   * an elite -- EliteShip.Rammed (a heart, knocked away);
//   * a hostile shot's hitbox (boss shot / laser, elite shot) or the boss
//     itself -- the hull just absorbs it, no hazard blast;
//   * the fatal hit -- DeathCrash owns that one.
public static class RamKill
{
    public static int Blasts;   // tests

    // Plays the rammed target's death blast (the caller destroys it). False
    // when there is nothing to blow up.
    public static bool Blast(GameObject target, int ship)
    {
        if (target == null || NotAHazardBody(target) || ShipAttackHits.AlreadyHit(target)) return false;
        TargetExplosion.Spawn(target, ship);
        Blasts++;
        return true;
    }

    // Shot hitboxes, lasers and the boss: tagged "Enimey" so they hurt, but
    // not hazards that blow up when hit.
    public static bool NotAHazardBody(GameObject go)
    {
        if (go.TryGetComponent(out EliteShotHitbox _) || go.TryGetComponent(out BossTarget _) ||
            go.TryGetComponent(out RailMineLaserHitbox _)) return true;
        var parent = go.transform.parent;
        return parent != null && (parent.TryGetComponent(out BossProjectile _) || parent.TryGetComponent(out BossBeam _));
    }
}
