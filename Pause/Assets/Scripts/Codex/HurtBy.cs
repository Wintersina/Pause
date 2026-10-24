using UnityEngine;

// Which codex entry a hostile collider belongs to, for the damage log
// (DamageLog). The hull is only ever hurt through collisionDetection, whose
// `hit` is one of:
//   * a body: a roster enemy, rock or mine, an elite, a boss part -- the
//     object itself says which (Codex.IdFor, EliteShip, BossActor);
//   * a projectile / beam / hazard hitbox -- a child that knows its owner:
//     an elite's or roster enemy's shot (EliteShot.Owner / Shooter), a
//     themed area hazard (AttackHazard.Shooter), a rail mine's laser
//     (RailMineLaser.Owner), a boss shot or beam (BossProjectile / BossBeam).
// The shot is credited to the thing that fired it. Runs only on a hit, never
// per frame, so the component lookups here are fine.
public static class HurtBy
{
    // The codex id of what `hit` is or came from; null when unknown.
    public static string Resolve(GameObject hit)
    {
        if (hit == null) return null;

        EliteShotHitbox shotBox;
        if (hit.TryGetComponent(out shotBox) && shotBox.shot != null)
            return shotBox.shot.SourceId ?? OwnerOf(shotBox.shot.Owner != null ? shotBox.shot.Owner.gameObject : shotBox.shot.Shooter);

        AttackHazardHitbox hazardBox;
        if (hit.TryGetComponent(out hazardBox) && hazardBox.hazard != null)
            return hazardBox.hazard.SourceId ?? OwnerOf(hazardBox.hazard.Shooter);

        RailMineLaserHitbox laserBox;
        if (hit.TryGetComponent(out laserBox) && laserBox.laser != null)
            return laserBox.laser.SourceId ?? (laserBox.laser.Owner != null ? Codex.IdFor(laserBox.laser.Owner.gameObject) : null);

        var bossShot = hit.GetComponentInParent<BossProjectile>();
        if (bossShot != null) return bossShot.Def != null ? bossShot.Def.id : null;
        var bossBeam = hit.GetComponentInParent<BossBeam>();
        if (bossBeam != null) return bossBeam.Owner != null && bossBeam.Owner.Boss != null ? bossBeam.Owner.Boss.id : null;

        return OwnerOf(hit);
    }

    // A shooter or body to its entry: an elite, a boss, else the roster / name lookup.
    public static string OwnerOf(GameObject go)
    {
        if (go == null) return null;
        var elite = go.GetComponentInParent<EliteShip>();
        if (elite != null && elite.Def != null) return elite.Def.codexId;
        var boss = go.GetComponentInParent<BossActor>();
        if (boss != null && boss.Boss != null) return boss.Boss.id;
        return Codex.IdFor(go);
    }
}
