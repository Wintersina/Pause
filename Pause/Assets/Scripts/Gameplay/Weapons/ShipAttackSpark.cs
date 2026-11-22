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

    public void Launch(Vector3 from) { Launch(from, Vector2.up); }

    public void Launch(Vector3 from, Vector2 heading)
    {
        if (root == null) Build();
        flying = true; arrived = fizzled = false;
        pos = from; dir = heading.normalized; age = 0f; steps = 0;
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

    // No enemy to chain to: a firing launches a volley of VolleyShots sparks, VolleyStagger apart, fanned a little either
    // side of straight up. The arc links the ship to shot 1, shot 1 to shot 2, shot 2 to shot 3 (firing order, so the
    // newest few; never more than VolleyShots links) while they fly, so the attack still reads as electric. The moment an
    // enemy can be homed on the volley ends and the sparks home and chain as normal (the switch back).
    //
    // Damage choice: a volley arc DOES hurt an enemy that crosses it (a spark and its arc are the same attack, and a
    // charge that kills nothing while an enemy flies through its lightning would read as broken), but gently: a normal
    // enemy dies like it would to any chain hop, an elite or the boss takes only VolleyArcWeight of a hit (elites share
    // the firing's budget, the boss keeps its 0.25 s hit window), and one enemy is struck at most once per
    // VolleyArcCooldown. A volley only exists when the screen has no homeable enemy, so it cannot out-damage the chain.
    public const int VolleyShots = 3;
    public const float VolleyStagger = .12f;      // seconds between launches
    public const float VolleyFanDegrees = 14f;    // shot 2 leans right, shot 3 left, by this much
    public const float VolleyArcWeight = .25f;
    public const float VolleyArcCooldown = .5f;
    public const float VolleyArcReach = .1f;      // arc half-width added to the enemy's radius

    void StartSpark(AttackRun run, Vector3 nose)
    {
        if (run.spark == null) run.spark = new SparkBolt();
        run.spark.Launch(nose);
        run.point = nose;
        run.fired = 0;   // 0: the spark is flying, 1: the chain is running
        run.launched = 1;
        run.volley = ShipAttackTargets.Nearest(nose, 0f, null, true) == null;
        for (int i = 0; i < run.links.Length; i++) { run.links[i] = null; run.linkGen[i] = -1; }
    }

    // Volley arcs held up right now, across the runs (tests).
    public int VolleyLinkCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < runs.Length; i++)
            {
                var r = runs[i];
                if (r == null || !r.active || !r.volley) continue;
                for (int k = 0; k < r.links.Length; k++) if (r.links[k] != null && r.links[k].Live && r.links[k].generation == r.linkGen[k]) n++;
            }
            return n;
        }
    }

    // The sparks of a firing in the order they were fired: [0] is shot 1.
    SparkBolt SparkAt(AttackRun run, int i)
    {
        if (i == 0) return run.spark;
        return run.extraSparks != null && i - 1 < run.extraSparks.Length ? run.extraSparks[i - 1] : null;
    }

    void StepSparks(AttackRun run, float dt)
    {
        if (run.volley)
        {
            while (run.launched < VolleyShots && run.t >= run.launched * VolleyStagger)
            {
                if (run.extraSparks == null) run.extraSparks = new SparkBolt[VolleyShots - 1];
                var extra = run.extraSparks[run.launched - 1] ?? (run.extraSparks[run.launched - 1] = new SparkBolt());
                float lean = (run.launched & 1) == 1 ? VolleyFanDegrees : -VolleyFanDegrees;
                extra.Launch(Nose, Quaternion.Euler(0f, 0f, -lean) * Vector3.up);
                run.launched++;
            }
        }
        for (int i = 0; i < run.launched && run.fired == 0; i++)
        {
            var s = SparkAt(run, i);
            if (s != null && s.flying) StepSpark(run, s, dt);
        }
        if (run.fired != 0) return;   // one spark arrived: the chain took over
        bool anyFlying = false;
        for (int i = 0; i < run.launched; i++) { var s = SparkAt(run, i); if (s != null && s.flying) anyFlying = true; }
        if (!anyFlying && !(run.volley && run.launched < VolleyShots)) { run.active = false; return; }
        if (run.volley) LinkVolley(run);
    }

    // The volley's arcs: ship -> shot 1 -> shot 2 -> shot 3, each pair that is still flying, held on while they fly.
    void LinkVolley(AttackRun run)
    {
        Vector3 from = Nose;
        int link = 0;
        for (int i = 0; i < run.launched && link < run.links.Length; i++)
        {
            var s = SparkAt(run, i);
            if (s == null || !s.flying) continue;
            Vector3 to = new Vector3(s.pos.x, s.pos.y, 0f);
            run.links[link] = VoltArcs.Hold(run.links[link], run.linkGen[link], from, to);
            run.linkGen[link] = VoltArcs.GenerationOf(run.links[link]);
            StrikeAlong(run, from, to);
            from = to;
            link++;
        }
    }

    // An enemy crossing a volley arc takes a light hit, once per VolleyArcCooldown.
    void StrikeAlong(AttackRun run, Vector3 a, Vector3 b)
    {
        ShipTargets.Collect(targets);
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || !ShipAttackTargets.IsEnemy(t) || !ShipTargets.InHomingView(t.transform.position)) continue;
            float along;
            if (!SegmentHits(a, b, t.transform.position, VolleyArcReach + t.Radius, out along)) continue;
            var go = t.gameObject;
            int id = go.GetInstanceID();
            int slot = -1;
            for (int k = 0; k < run.arcHitId.Length; k++)
            {
                if (run.arcHitUntil[k] > run.t && run.arcHitId[k] == id) { slot = -2; break; }
                if (slot == -1 && run.arcHitUntil[k] <= run.t) slot = k;
            }
            if (slot == -2) continue;
            if (slot >= 0) { run.arcHitId[slot] = id; run.arcHitUntil[slot] = run.t + VolleyArcCooldown; }
            Vector3 at = t.transform.position;
            ShipAttackHits.Hit(go, ship, VolleyArcWeight, ref run.budget);
            AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, at, Vector3.one * .3f, 0f,
                ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 71);
        }
    }

    void StepSpark(AttackRun run, SparkBolt s, float dt)
    {
        s.age += dt;
        Vector3 here = s.pos;
        if (s.target != null && !ShipAttackTargets.CanTarget(s.target, true)) s.target = null;   // died / left: retarget
        if (s.target == null) s.target = ShipAttackTargets.Nearest(here, 0f, null, true, true);
        if (s.target != null && run.volley)
        {
            run.volley = false;   // an enemy is in reach: back to chaining enemies, the volley arcs fade out
        }
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
                run.HideSparks();
                return;
            }
        }
        // out of road: lifetime up, or climbed out of the top with nothing to chase
        if (s.age >= SparkLifetime || s.pos.y > ShipTargets.View().yMax + .3f || s.pos.y < ShipTargets.View().yMin - 1f)
        {
            s.fizzled = true;
            AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, new Vector3(s.pos.x, Mathf.Min(s.pos.y, ShipTargets.View().yMax), 0f),
                Vector3.one * .25f, 0f, ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 71);
            s.Hide();   // the run ends in StepSparks once no spark is left flying
        }
    }
}
