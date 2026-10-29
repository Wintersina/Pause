using UnityEngine;

// Runner side of the signature effects of group 1 (ShipSignatures): Viper jolts, Halo core, Ion crossing beam.
// The Comet's pierce lives in AttackProjectile (ShipAttacks.cs).
public partial class ShipAttackRunner
{
    // ---- Volt Viper: a chain jump jolts the enemies around it -------------------------------------
    // Called after the chain hit `at`'s target `hitGo` (which may have died).
    void ViperJolt(AttackRun run, Vector3 at, GameObject hitGo)
    {
        float seconds = ShipSignatures.ViperStun(run.loadout);
        if (seconds <= 0f) return;
        float radius = ShipSignatures.ViperRadius(run.loadout);
        Rect view = ShipTargets.View();
        view.yMax += .4f;
        var live = ClearTarget.Live;
        int stunned = 0;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (t == null || !t.isActiveAndEnabled || t.Scenery || !ShipTargets.IsHazard(t)) continue;
            var go = t.gameObject;
            if (go == hitGo && !go.TryGetComponent(out EliteShip _)) continue;   // the one hit dies: only a surviving elite is stunned
            Vector3 p = go.transform.position;
            if (!view.Contains(p)) continue;
            float reach = radius + t.Radius;
            if (((Vector2)(p - at)).sqrMagnitude > reach * reach) continue;
            if (!ShipSignatures.Stun(go, seconds)) continue;
            stunned++;
            AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, p, Vector3.one * .3f, 0f,
                ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 71);
        }
        JoltStunnedTotal += stunned;
    }

    // Enemies stunned by chain jolts since load (tests).
    public int JoltStunnedTotal { get; private set; }

    // ---- Crimson Halo: the armour-piercing core ---------------------------------------------------
    // After the slug's hits: a lingering thin core down the line, hostile shots in the line deleted.
    void HaloPierce(Vector3 nose, float len, float halfWidth)
    {
        AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, nose + Vector3.up * len * .5f,
            new Vector3(Mathf.Max(.08f, halfWidth * .7f), len, 1f), 0f, ShipFxArt.LoopTicks, true,
            ShipSignatures.HaloCoreSeconds, null, Vector3.zero, 72);
        HostileShots.ShootDownAlong(nose, nose + Vector3.up * len, halfWidth);
    }

    // ---- Ion Lancer: the crossing beam ------------------------------------------------------------
    // The mirror of the main beam's angle this frame, thinner, hitting through the same per-pass list.
    void StepIonCross(AttackRun run, Vector3 nose, float mainAngle, float weight)
    {
        var l = run.loadout;
        float rad = -mainAngle * Mathf.Deg2Rad;
        Vector2 d = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        Rect view = ShipTargets.View();
        float len = Mathf.Min(l.range, RayExit(nose, d, view));
        ShipTargets.Collect(targets);
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || !t.isActiveAndEnabled || !ShipTargets.IsBossTarget(t)) continue;
            Vector2 to = t.transform.position - nose;
            float along = Vector2.Dot(to, d);
            if (along <= 0f) continue;
            float side = Mathf.Abs(to.x * d.y - to.y * d.x);
            if (side > ShipSignatures.IonCrossHalfWidth + t.Radius * .55f) continue;
            len = Mathf.Min(len, Mathf.Max(.6f, along));
        }
        Vector3 end = nose + (Vector3)(d * len);
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || !t.isActiveAndEnabled || run.hit.Contains(t.gameObject)) continue;
            float reach = ShipSignatures.IonCrossHalfWidth + t.Radius * .55f;
            if (HostileShots.SegmentDistanceSq(nose, end, t.transform.position) > reach * reach) continue;
            if (Vector2.Dot((Vector2)(t.transform.position - nose), d) > len + t.Radius) continue;
            AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, t.transform.position, Vector3.one * .4f, 0f,
                ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 71);
            HitOnce(run.hit, t.gameObject, weight);
        }
        IonCrossFrames++;

        // the second beam is drawn on the run's spare sprite slot (the laser never throws a disc)
        if (run.disc == null || !run.disc.Active || run.disc.Run != run)
            run.disc = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, nose, new Vector3(1f, .6f, 1f), 90f,
                ShipFxArt.LoopTicks, true, 0f, null, Vector3.zero, 69).Owned(run);
        var b = run.disc.Body;
        b.position = nose + (Vector3)(d * len * .5f);
        b.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        b.localScale = new Vector3(len / .6f, .9f * ShipSignatures.IonCrossThickness, 1f);
    }

    // Frames the crossing beam has swept since load (tests).
    public int IonCrossFrames { get; private set; }
}
