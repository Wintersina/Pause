using System.Collections.Generic;
using UnityEngine;

// WHO A SHIP ATTACK MAY CHASE OR CHAIN TO (Volt Viper's spark and every hop of its lightning).
//
// The ClearTarget registry holds everything that can hurt the pilot or that a weapon can strike: enemies,
// but also enemy bullets (EliteShotHitbox, BossProjectile hitboxes), boss lasers, mine lasers, the themed
// area hazards (AttackHazardHitbox), rail mines, asteroids / rocks (tag "Astr") and pickups. A chain that
// "jumps between hazards" would happily hop onto a bullet. IsEnemy is the explicit allow-list:
//   * tagged "Enimey" (so never "Astr" rocks, "pickUp" pickups or untagged scenery);
//   * not a Scenery target (backdrop rocks);
//   * not a hostile shot hitbox: EliteShotHitbox / ClearTarget.IsShotHitbox, RunScore.IsHostileShot (boss
//     shots), or anything that is (or sits under) an IHostileShot, a BossProjectile or an IHostileZone;
//   * not a mine laser (RailMineLaserHitbox) or a themed area hazard's hitbox (AttackHazardHitbox);
//   * not a rail mine (RailMineMount, RailBombAnimator, the "mine" role);
//   * an elite only while it is in play (InPlay: not parked / lifting off / hidden / dead);
//   * the boss body (BossTarget) counts, and takes the hit under its own 0.25 s hit window.
// The player's own objects (Saboteur / Warden mines and sentries, projectiles) are never ClearTargets.
public static class ShipAttackTargets
{
    public static bool IsEnemy(ClearTarget t)
    {
        if (t == null || !t.isActiveAndEnabled) return false;
        var go = t.gameObject;
        if (!go.CompareTag("Enimey")) return false;
        if (t.Scenery || t.IsShotHitbox) return false;
        if (RunScore.IsHostileShot(go)) return false;
        if (go.TryGetComponent(out EliteShotHitbox _) || go.TryGetComponent(out RailMineLaserHitbox _) ||
            go.TryGetComponent(out AttackHazardHitbox _) || go.TryGetComponent(out RailMineMount _) ||
            go.TryGetComponent(out RailBombAnimator _) || go.TryGetComponent(out BossProjectile _)) return false;
        if (go.GetComponentInParent<IHostileShot>() != null || go.GetComponentInParent<BossProjectile>() != null ||
            go.GetComponentInParent<IHostileZone>() != null) return false;
        if (go.TryGetComponent(out EliteShip elite) || (elite = t.Elite) != null)
            return elite.State != EliteState.Dead && elite.InPlay;
        return true;
    }

    // A valid, visible enemy the spark may chase or a hop may jump to. With a boss up the spark chases only the boss
    // (the rule every homing player shot follows, ShipTargets.CanHome); hops take any enemy.
    public static bool CanTarget(ClearTarget t, bool homing)
    {
        if (!IsEnemy(t) || !ShipTargets.InHomingView(t.transform.position)) return false;
        return !homing || !ShipTargets.BossAlive() || ShipTargets.IsBossTarget(t);
    }

    // The nearest enemy to `from` within `maxDistance` (<= 0: any), skipping `exclude`. `ahead`: something behind
    // `from` counts as farther (a homing spark prefers what is in front of it).
    public static ClearTarget Nearest(Vector3 from, float maxDistance, List<GameObject> exclude, bool homing, bool ahead = false)
    {
        var live = ClearTarget.Live;
        ClearTarget best = null;
        float bestScore = maxDistance > 0f ? maxDistance * maxDistance : float.MaxValue;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (!CanTarget(t, homing)) continue;
            if (exclude != null && exclude.Contains(t.gameObject)) continue;
            Vector3 p = t.transform.position;
            float sq = ((Vector2)(p - from)).sqrMagnitude;
            if (sq > (maxDistance > 0f ? maxDistance * maxDistance : float.MaxValue)) continue;
            float score = ahead && p.y < from.y - .3f ? sq + 36f : sq;
            if (score < bestScore) { bestScore = score; best = t; }
        }
        return best;
    }
}

// The flight state and the drawing of one Volt Viper spark: one pooled SpriteRenderer playing the ship's
// "attack_shot" strip (Powers2/VoltViper_attack_shot.png, cells 0-7: the spark with its own crackle tail,
// drawn pointing up) turned to the flight heading. A plain renderer, so the kill cam records it. Built once
// per run slot, reused; nothing allocates per frame.
public sealed class SparkBolt
{
    public bool flying, arrived, fizzled;
    public Vector2 pos, dir;
    public float age;
    public ClearTarget target;
    public int steps;   // frames flown (tests)

    // World size of the strip's cell at scale 1 times this: the drawn spark is ~.46 units long.
    const float DrawScale = .7f;
    const float Alpha = .95f;
    const int VoltViperId = 2;   // the roster id (ShipId.KeyOf(2) == "VoltViper")

    GameObject root;
    SpriteRenderer head;

    void Build()
    {
        root = new GameObject("~viperSpark");
        head = root.AddComponent<SpriteRenderer>();
        head.sortingOrder = 72;
        head.color = new Color(1f, 1f, 1f, Alpha);
        root.SetActive(false);
    }

    public void Launch(Vector3 from)
    {
        if (root == null) Build();
        flying = true; arrived = fizzled = false;
        pos = from; dir = Vector2.up; age = 0f; steps = 0;
        target = null;
        root.SetActive(true);
        Draw();
    }

    public void Advance(float dt)
    {
        steps++;
        Draw();
    }

    void Draw()
    {
        if (root == null) return;
        head.sprite = ShipFxArt.Extra(VoltViperId, "attack_shot", Mathf.FloorToInt(age * 24f) % ShipFxArt.LoopFrames);
        head.transform.position = new Vector3(pos.x, pos.y, 0f);
        head.transform.up = new Vector3(dir.x, dir.y, 0f);
        head.transform.localScale = Vector3.one * DrawScale;
    }

    public void Hide()
    {
        flying = false;
        target = null;
        if (root != null) root.SetActive(false);
    }

    public bool Shown => root != null && root.activeSelf;

    public void Dispose()
    {
        if (root == null) return;
        if (Application.isPlaying) Object.Destroy(root); else Object.DestroyImmediate(root);
        root = null;
    }
}

public partial class ShipAttackRunner
{
    // Volt Viper's spark (ShipAttack.ChainLightning): leaves the nose, homes onto the nearest enemy and, on arrival,
    // starts the chain from that enemy.
    public const float SparkSpeed = 16f;          // world units per second
    public const float SparkTurnRate = 540f;      // degrees per second
    public const float SparkLifetime = 1.6f;      // seconds before it gives up
    public const float SparkArriveRadius = .3f;   // reaches its target within this (plus 0.6 of the target's radius)

    void StartSpark(AttackRun run, Vector3 nose)
    {
        if (run.spark == null) run.spark = new SparkBolt();
        run.spark.Launch(nose);
        run.point = nose;
        run.fired = 0;   // 0: the spark is flying, 1: the chain is running
    }

    void StepSpark(AttackRun run, float dt)
    {
        var s = run.spark;
        if (s == null || !s.flying) { run.active = false; return; }
        s.age += dt;
        Vector3 here = s.pos;
        if (s.target != null && !ShipAttackTargets.CanTarget(s.target, true)) s.target = null;   // died / left: retarget
        if (s.target == null) s.target = ShipAttackTargets.Nearest(here, 0f, null, true, true);
        Vector2 prev = s.pos;
        if (s.target != null)
        {
            Vector2 want = (Vector2)s.target.transform.position - s.pos;
            if (want.sqrMagnitude > 1e-6f)
            {
                float ang = Vector2.SignedAngle(s.dir, want);   // positive counter-clockwise, like the rotation below
                float turn = Mathf.Clamp(ang, -SparkTurnRate * dt, SparkTurnRate * dt);
                s.dir = (Quaternion.Euler(0f, 0f, turn) * s.dir).normalized;
            }
        }
        s.pos += s.dir * SparkSpeed * dt;
        s.Advance(dt);

        // arrival: the segment flown this frame passes within reach of the target
        if (s.target != null)
        {
            Vector2 c = s.target.transform.position;
            Vector2 ab = s.pos - prev;
            float len2 = ab.sqrMagnitude;
            float along = len2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(c - prev, ab) / len2) : 0f;
            float reach = SparkArriveRadius + s.target.Radius * .6f;
            if ((c - (prev + ab * along)).sqrMagnitude <= reach * reach)
            {
                s.arrived = true;
                run.point = c;
                run.next = run.t;
                run.fired = 1;
                AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, new Vector3(c.x, c.y, 0f), Vector3.one * .4f, 0f,
                    ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 71);
                s.Hide();
                return;
            }
        }
        // out of road: lifetime up, or climbed out of the top with nothing to chase
        if (s.age >= SparkLifetime || s.pos.y > ShipTargets.View().yMax + .3f || s.pos.y < ShipTargets.View().yMin - 1f)
        {
            s.fizzled = true;
            AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, new Vector3(s.pos.x, Mathf.Min(s.pos.y, ShipTargets.View().yMax), 0f),
                Vector3.one * .25f, 0f, ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 71);
            s.Hide();
            run.active = false;
        }
    }
}
