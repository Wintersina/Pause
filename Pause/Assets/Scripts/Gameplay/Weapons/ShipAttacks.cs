using System.Collections.Generic;
using UnityEngine;

// The directional and limited main attacks (every ship below the top price
// tier; see ShipLoadoutTable) and the top tier's cinematic ones.
// ShipPowerController.Fire() calls Fire() here when the charge meter fills
// (the top tier wraps it in slow motion). The newer verbs (laser sweep,
// frost nova, sentries, nuke) live in ShipAttacksV3.cs.
//
// Rules:
//  * Gameplay time. Everything steps on scaled time, so a paused world
//    (timeScale 0) freezes every shot, beam, cone and orbit mid-flight, and
//    there is no slow motion and no hit-stop: these play at normal speed.
//  * Pooled. Projectiles and drawn effects come from AttackPool, capped,
//    reused; runs are preallocated; target queries fill a list owned here.
//    Nothing allocates per frame.
//  * Every hit goes through ShipAttackHits.Hit, so an IShipAttackTarget (the
//    boss) takes a weighted hit instead of being destroyed. Elites share one
//    budget per firing; the boss has no cap at all (only its hit window,
//    BossEncounter.TakePlayerHit).
public partial class ShipAttackRunner : MonoBehaviour
{
    public const int MaxRuns = 4;

    int ship;
    ShipLoadout loadout;
    SpriteRenderer hull;
    float noseOffset = -1f;
    readonly AttackRun[] runs = new AttackRun[MaxRuns];
    readonly List<ClearTarget> targets = new List<ClearTarget>(64);

    public int Ship => ship;
    public ShipLoadout Loadout => loadout;
    public int FireCount { get; private set; }

    public int ActiveRuns
    {
        get
        {
            int n = 0;
            for (int i = 0; i < runs.Length; i++) if (runs[i] != null && runs[i].active) n++;
            return n;
        }
    }

    public static ShipAttackRunner Attach(GameObject go, int shipId)
    {
        var runner = go.GetComponent<ShipAttackRunner>();
        if (runner == null) runner = go.AddComponent<ShipAttackRunner>();
        runner.Setup(shipId);
        return runner;
    }

    public void Setup(int shipId)
    {
        ship = shipId;
        loadout = ShipWeaponUpgrades.LoadoutFor(shipId);
        hull = GetComponent<SpriteRenderer>();
        noseOffset = -1f;
        for (int i = 0; i < runs.Length; i++) if (runs[i] == null) runs[i] = new AttackRun();
    }

    // Front of the hull (top of the sprite's bounds), where shots leave.
    public Vector3 Nose
    {
        get
        {
            if (noseOffset < 0f)
            {
                noseOffset = .3f;
                if (hull != null && hull.sprite != null)
                    noseOffset = Mathf.Max(.15f, hull.sprite.bounds.max.y * Mathf.Abs(transform.lossyScale.y));
            }
            return transform.position + Vector3.up * noseOffset;
        }
    }

    // ---------------------------------------------------------------- fire

    public bool Fire()
    {
        if (runs[0] == null) Setup(ship);
        // The weapon level can change between firings (a colour bought, dev
        // mode switched): read it as the attack goes out.
        loadout = ShipWeaponUpgrades.LoadoutFor(ship);
        AttackRun run = null;
        for (int i = 0; i < runs.Length; i++) if (!runs[i].active) { run = runs[i]; break; }
        if (run == null)
        {
            // every slot busy (an instant recharge mid-attack): the oldest
            // run gives way
            run = runs[0];
            for (int i = 1; i < runs.Length; i++) if (runs[i].startedAt < run.startedAt) run = runs[i];
            run.Stop();
        }
        FireCount++;
        run.Begin(loadout, ship, Time.unscaledTime);
        StartRun(run);
        return true;
    }

    // ---------------------------------------------------------- free fire
    //
    // The red atom's free shot (ShipPowerController.FreeShot): the same
    // weapon at the same level, through the same hits and the same boss
    // rules, but it never touches the charge timer and never takes a run
    // slot from an attack in progress -- with every slot busy it refuses,
    // and the controller keeps it queued. The top tier fires its own verb,
    // shortened (AttackRun.quick), at normal speed: no slow motion.

    public int FreeFireCount { get; private set; }

    public bool HasFreeRunSlot
    {
        get
        {
            if (runs[0] == null) Setup(ship);
            for (int i = 0; i < runs.Length; i++) if (!runs[i].active) return true;
            return false;
        }
    }

    public bool FireFree()
    {
        if (runs[0] == null) Setup(ship);
        AttackRun run = null;
        for (int i = 0; i < runs.Length; i++) if (!runs[i].active) { run = runs[i]; break; }
        if (run == null) return false;
        loadout = ShipWeaponUpgrades.LoadoutFor(ship);
        FreeFireCount++;
        run.Begin(loadout, ship, Time.unscaledTime);
        run.quick = true;
        // the boss fight hands out only BossFreeShotAtoms.Count of these: each is charged (the
        // allowance only matters to capped targets now; the boss has no per-firing cap)
        if (BossEncounter.Running) run.budget = BossFreeShotAtoms.ChargedBudget;
        StartRun(run);
        return true;
    }

    void StartRun(AttackRun run)
    {
        var l = run.loadout;
        Vector3 nose = Nose;
        switch (l.attack)
        {
            case ShipAttack.IonLaser:
            case ShipAttack.FrostNova:
            case ShipAttack.WardenSentries:
            case ShipAttack.Nuke:
                StartTop(run, nose);
                break;
            case ShipAttack.RailSlug:
                // upgraded: side rails either side of the main line
                for (int i = 0; i < Mathf.Max(1, l.shots); i++)
                    Rail(run, nose + Vector3.right * RailOffset(i));
                run.active = false;
                break;
            case ShipAttack.ChainLightning:
                StartSpark(run, nose);
                break;
            case ShipAttack.ArcaneEye:
            case ShipAttack.HeatWave:
            case ShipAttack.BoomerangStar:
            case ShipAttack.ProximityMines:
            case ShipAttack.AbductHurl:
            case ShipAttack.GaleWings:
                StartV3(run, nose);
                break;
            case ShipAttack.Blowtorch:
                run.visual = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, nose,
                    ConeScale(l), 0f, ShipFxArt.LoopTicks, true, l.duration, transform,
                    Vector3.up * (noseOffset + l.range * .5f), 69).Owned(run);
                break;
            case ShipAttack.ThrownDisc:
                // thrown from the nose a little off the vertical (alternating sides), spinning as it flies
                run.point = nose;
                run.dir = ThrowDirection(FireCount + FreeFireCount);
                run.disc = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, nose,
                    Vector3.one * DiscDrawScale(l), 0f, ShipFxArt.LoopTicks, true, l.duration + ThrowLeaveSeconds, null,
                    Vector3.zero, 72).Owned(run);
                break;
        }
        // first volley of the timed attacks goes out on this frame
        Step(run, 0f);
    }

    // A homing shot of a firing marks its target so the next one picks another. The boss is never
    // marked: it is the only thing the shots can chase while it is up, and a claimed boss would leave
    // the rest of the barrage flying straight past it.
    static void Claim(AttackRun run, ClearTarget t)
    {
        if (!ShipTargets.IsBossTarget(t)) run.hit.Add(t.gameObject);
    }

    // Rail i of an upgraded rail gun: 0 the main line, then alternating sides.
    public const float SideRailSpacing = 1.1f;
    public static float RailOffset(int i) { return i == 0 ? 0f : (i % 2 == 1 ? -1f : 1f) * ((i + 1) / 2) * SideRailSpacing; }

    // Streak i of the comet: the first two straight ahead, upgraded extras
    // splay a few degrees to alternate sides.
    public static float StreakAngle(int i) { return i < 2 ? 0f : (i % 2 == 0 ? -1f : 1f) * 6f * ((i / 2)); }

    static Vector3 ConeScale(ShipLoadout l)
    {
        float halfWidth = Mathf.Tan(l.width * Mathf.Deg2Rad) * l.range;
        return new Vector3(Mathf.Max(.3f, halfWidth * 2f * .75f), l.range, 1f);   // drawn slimmer than the hit cone
    }

    static float BeamLength(Vector3 from, float range)
    {
        Rect view = ShipTargets.View();
        float top = Mathf.Min(view.yMax + .3f, from.y + range);
        return Mathf.Max(1f, top - from.y);
    }

    AttackProjectile Launch(AttackRun run, AttackProjectile.Kind kind, Vector3 from, Vector3 dir, float speed,
                            float radius, float size, Transform target, float weight)
    {
        var p = AttackPool.Projectile();
        if (p == null) return null;
        p.Launch(this, run, kind, ship, from, dir, speed, radius, size, target, weight);
        return p;
    }

    // A javelin striking down from `from` (where its rocket reached the top).
    public AttackProjectile LaunchJavelin(AttackRun run, Vector3 from, float speed, float hitHalfWidth,
                                          float weight, float stopAtY)
    {
        var p = Launch(run, AttackProjectile.Kind.Javelin, from, Vector3.down, speed, hitHalfWidth, 1.6f, null, weight);
        if (p != null) p.StopAtY = stopAtY;
        return p;
    }

    public const float RocketSpeed = 17f;

    // ---------------------------------------------------------------- rail

    // Instant: everything whose body is within `width` of the ship's line,
    // from just behind the nose up to the top of the screen, is hit.
    void Rail(AttackRun run, Vector3 nose)
    {
        var l = run.loadout;
        float len = BeamLength(nose, l.range);
        AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, nose + Vector3.up * len * .5f,
            new Vector3(Mathf.Max(.14f, l.width * 2f * .6f), len, 1f), 0f, ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 70);
        // the muzzle flash is drawn low in its cell: lifted so it sits on the nose, not over the hull
        AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, nose + Vector3.up * .18f, Vector3.one * .6f, 0f,
            ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 71);
        ShipTargets.Collect(targets);
        int kills = 0;
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || !t.isActiveAndEnabled) continue;
            if (InRailLine(t.transform.position, t.Radius, nose, l.width, nose.y + len))
            {
                Vector3 at = t.transform.position;
                if (ShipAttackHits.Hit(t.gameObject, ship, 1f, ref run.budget)) kills++;
                if (ShipSignatures.HaloPierces(l))   // signature: a spark at every target the slug passes through
                    AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, at, Vector3.one * .45f, 0f,
                        ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 71);
            }
        }
        if (ShipSignatures.HaloPierces(l)) HaloPierce(nose, len, l.width);
    }

    public static bool InRailLine(Vector3 p, float radius, Vector3 nose, float halfWidth, float top)
    {
        return Mathf.Abs(p.x - nose.x) <= halfWidth + radius * .5f &&
               p.y >= nose.y - .35f && p.y - radius <= top;
    }

    // ---------------------------------------------------------------- step

    public static float ScaledDelta() => Time.timeScale > 0f ? Time.deltaTime : 0f;

    void Update()
    {
        Step(ScaledDelta());
    }

    public void Step(float dt)
    {
        if (dt <= 0f) return;
        for (int i = 0; i < runs.Length; i++)
            if (runs[i] != null && runs[i].active) Step(runs[i], dt);
        StepTopObjects(dt);
        var list = AttackPool.Projectiles;
        for (int i = 0; i < list.Count; i++)
        {
            var p = list[i];
            if (p != null && p.Active && p.Owner == this) p.Tick(dt);
        }
    }

    void Step(AttackRun run, float dt)
    {
        run.t += dt;
        var l = run.loadout;
        switch (l.attack)
        {
            case ShipAttack.CometStreak:
                while (run.fired < l.shots && run.t >= run.fired * .16f)
                {
                    var streak = Launch(run, AttackProjectile.Kind.Straight, Nose,
                           Quaternion.Euler(0f, 0f, -StreakAngle(run.fired)) * Vector3.up, l.speed, l.width, .8f, null,
                           1f / Mathf.Max(1, l.shots));
                    if (streak != null) streak.SetPierce(ShipSignatures.CometPierce(l, run.fired));   // signature: later streaks pierce
                    run.fired++;
                }
                if (run.fired >= l.shots) run.active = false;
                break;

            case ShipAttack.IonLaser:
            case ShipAttack.FrostNova:
            case ShipAttack.WardenSentries:
            case ShipAttack.Nuke:
                StepTop(run, dt);
                break;

            case ShipAttack.SkyJavelins:
                {
                    // Three small rockets leave the nose and fly up to the top of
                    // the screen, one per lane (centre lane first, then the outer
                    // two); each opens into its javelin there (AttackProjectile
                    // Rocket -> ManifestJavelin) and strikes down its lane.
                    Rect view = ShipTargets.View();
                    while (run.fired < l.shots && run.t >= run.fired * .08f)
                    {
                        int lane = run.fired == 0 ? 0 : (run.fired % 2 == 1 ? -1 : 1) * ((run.fired + 1) / 2);
                        float x = Mathf.Clamp(transform.position.x + lane * l.range, view.xMin + .3f, view.xMax - .3f);
                        float top = Mathf.Min(view.yMax + .6f, transform.position.y + 30f);
                        Vector3 nose = Nose;
                        var p = Launch(run, AttackProjectile.Kind.Rocket, nose, new Vector3(x - nose.x, top - nose.y, 0f),
                                       RocketSpeed, .05f, .6f, null, 1f / Mathf.Max(1, l.shots));
                        if (p != null) p.SetRocket(new Vector3(x, top, 0f), transform.position.y + .3f);
                        run.fired++;
                    }
                    if (run.fired >= l.shots) run.active = false;
                }
                break;

            case ShipAttack.ChainLightning:
                if (run.fired == 0) StepSpark(run, dt);
                if (run.active && run.fired == 1) StepChain(run);
                break;

            case ShipAttack.Blowtorch:
                StepCone(run);
                if (run.t >= l.duration) run.Stop();
                break;

            case ShipAttack.ArcaneEye:
            case ShipAttack.HeatWave:
            case ShipAttack.BoomerangStar:
            case ShipAttack.ProximityMines:
            case ShipAttack.AbductHurl:
            case ShipAttack.GaleWings:
                StepV3(run, dt);
                break;

            case ShipAttack.ThrownDisc:
                StepThrow(run, dt);
                break;

            default:
                // projectile attacks (comet, javelins) are done
                // launching; their projectiles finish on their own
                run.active = false;
                break;
        }
    }

    // The chain, from where the spark landed: its target takes the first strike, then one hop every 60 ms, each to
    // the nearest ENEMY not yet hit within reach (ShipAttackTargets: never a bullet, mine, rock or pickup).
    void StepChain(AttackRun run)
    {
        var l = run.loadout;
        while (run.active && run.t >= run.next)
        {
            ClearTarget t;
            if (run.hops == 0 && run.spark != null && run.spark.target != null && ShipAttackTargets.IsEnemy(run.spark.target))
                t = run.spark.target;
            else
                t = ShipAttackTargets.Nearest(run.point, run.hops == 0 ? SparkArriveRadius + 1f : l.width, run.hit, false);
            if (t == null || run.hops >= l.maxHits) { run.active = false; break; }
            Vector3 to = t.transform.position;
            Arc(run.point, to);
            AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, to, Vector3.one * .5f, 0f,
                ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 71);
            run.hit.Add(t.gameObject);
            ShipAttackHits.Hit(t.gameObject, ship, 1f, ref run.budget);
            ViperJolt(run, to, t.gameObject);   // signature: the jump stuns the enemies around it
            run.point = to;
            run.hops++;
            run.next += .06f;
        }
    }

    void Arc(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        float len = Mathf.Max(.2f, d.magnitude);
        float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, (a + b) * .5f, new Vector3(len, ShipFxArt.BeamThin, 1f),
            ang, ShipFxArt.LoopTicks, true, .22f, null, Vector3.zero, 70);
    }

    void StepCone(AttackRun run)
    {
        var l = run.loadout;
        Vector3 nose = Nose;
        ShipTargets.Collect(targets);
        bool bossTick = run.t >= run.next;
        if (bossTick) run.next = run.t + .2f;
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || !t.isActiveAndEnabled) continue;
            if (!InCone(t.transform.position, t.Radius, nose, l.range, l.width)) continue;
            Touch(run, t.gameObject, bossTick, .2f);
        }
    }

    public static bool InCone(Vector3 p, float radius, Vector3 origin, float range, float halfAngle)
    {
        Vector2 d = p - origin;
        float dist = d.magnitude;
        if (dist > range + radius) return false;
        if (dist <= radius) return true;
        float slack = Mathf.Asin(Mathf.Clamp01(radius / dist)) * Mathf.Rad2Deg;
        return Vector2.Angle(Vector2.up, d) <= halfAngle + slack;
    }

    // ---------------------------------------------------------------- thrown disc

    // The Turtle's Shell Disc is thrown from the nose and ricochets: off the enemy it strikes (on to the nearest
    // other one in reach, else bouncing straight back off it) and off the screen walls. The loadout reads:
    // duration the flight time, speed its speed in world units per second, range the disc's hit radius and
    // maxHits how many bounces it has. After the last bounce (or when its time is up) it flies on out of the
    // screen. Ordinary hazards die from the touch; a boss or elite takes a weighted hit on its first touch, then
    // at most one per DiscBossTick while the disc stays on it.
    public const float ThrowAngle = 14f;           // degrees off the vertical, the side alternating each throw
    public const float ThrowLeaveSeconds = 3f;     // after its flight time it still needs this long to fly out
    public const float DiscReach = 7f;             // how far it looks for the next enemy to ricochet to
    public const float DiscBossTick = .4f;         // a boss / elite under the disc takes at most one hit this often
    public static Vector3 ThrowDirection(int throwNumber) { return Quaternion.Euler(0f, 0f, (throwNumber & 1) == 1 ? -ThrowAngle : ThrowAngle) * Vector3.up; }
    const float DiscArtFill = .55f;                // the shell fills this much of its 256 px cell

    static float DiscDrawScale(ShipLoadout l) { return l.range * 2f / DiscArtFill; }

    void StepThrow(AttackRun run, float dt)
    {
        var l = run.loadout;
        bool leaving = run.hops >= l.maxHits || run.t >= l.duration;
        Rect view = ShipTargets.View(-.2f);
        Vector3 p = run.point + run.dir * l.speed * dt;
        bool wall = false;
        if (!leaving)
        {
            if (p.x < view.xMin) { p.x = view.xMin; run.dir.x = Mathf.Abs(run.dir.x); wall = true; }
            else if (p.x > view.xMax) { p.x = view.xMax; run.dir.x = -Mathf.Abs(run.dir.x); wall = true; }
            if (p.y < view.yMin) { p.y = view.yMin; run.dir.y = Mathf.Abs(run.dir.y); wall = true; }
            else if (p.y > view.yMax) { p.y = view.yMax; run.dir.y = -Mathf.Abs(run.dir.y); wall = true; }
        }
        run.point = p;
        if (wall) DiscBounce(run, p);
        if (run.disc != null && run.disc.Active) run.disc.transform.position = p;

        ShipTargets.Collect(targets);
        // The boss / elite tick is spent only by a touch: the first contact always lands, then at most one hit
        // per DiscBossTick while the disc stays on it. (A free-running clock let a whole pass over the boss,
        // about .33 s, fall between two ticks: the disc flew straight through it and dealt nothing.)
        bool bossTick = run.t >= run.next;
        bool ticked = false;
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || !t.isActiveAndEnabled) continue;
            Vector2 d = t.transform.position - p;
            float r = l.range + t.Radius;
            if (d.sqrMagnitude > r * r) continue;
            bool special = t.GetComponent<IShipAttackTarget>() != null;
            if (special && !bossTick) continue;
            if (special) ticked = true;
            // read what is needed before the touch: it may destroy the target
            GameObject struck = t.gameObject;
            Vector3 struckAt = t.transform.position;
            Touch(run, struck, bossTick, .25f);
            if (leaving) continue;
            run.hit.Add(struck);
            if (run.hit.Count >= 30) run.hit.RemoveRange(0, 15);
            var next = ShipTargets.Nearest(p, DiscReach, run.hit);
            Vector3 away = (p - struckAt).normalized;
            if (away.sqrMagnitude < .01f) away = -run.dir;
            // on to the next enemy, else bouncing straight off this one
            run.dir = next != null ? (next.transform.position - p).normalized : Vector3.Reflect(run.dir, away).normalized;
            DiscBounce(run, p);
            break;
        }
        if (ticked) run.next = run.t + DiscBossTick;

        if (leaving && !ShipTargets.View(1f).Contains(p)) run.Stop();
        else if (run.t >= l.duration + ThrowLeaveSeconds) run.Stop();
    }

    void DiscBounce(AttackRun run, Vector3 at)
    {
        run.hops++;
        AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, at, Vector3.one * .7f, 0f,
            ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 73);
    }

    // A held weapon touching something: ordinary hazards die at once, a
    // boss takes `weight` once per tick (the tick rate keeps a per-frame overlap from
    // flooding it; the boss's hit window does the limiting).
    void Touch(AttackRun run, GameObject go, bool bossTick, float weight)
    {
        if (go.GetComponent<IShipAttackTarget>() != null)
        {
            if (bossTick) ShipAttackHits.Hit(go, ship, weight, ref run.budget);
            return;
        }
        ShipAttackHits.Hit(go, ship, weight, ref run.budget);
    }

    // Shared by projectiles: hazards whose body crosses the segment a..b.
    public ClearTarget FirstAlong(Vector3 a, Vector3 b, float radius, GameObject skip)
    {
        ShipTargets.Collect(targets);
        ClearTarget best = null;
        float bestT = float.MaxValue;
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || !t.isActiveAndEnabled || t.gameObject == skip) continue;
            float along;
            if (SegmentHits(a, b, t.transform.position, radius + t.Radius, out along) && along < bestT)
            {
                bestT = along;
                best = t;
            }
        }
        return best;
    }

    public List<ClearTarget> TargetsInView()
    {
        ShipTargets.Collect(targets);
        return targets;
    }

    public static bool SegmentHits(Vector2 a, Vector2 b, Vector2 c, float r, out float along)
    {
        Vector2 ab = b - a;
        float len2 = ab.sqrMagnitude;
        along = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(c - a, ab) / len2) : 0f;
        Vector2 closest = a + ab * along;
        return (c - closest).sqrMagnitude <= r * r;
    }

    // ---------------------------------------------------------------- end

    public void StopAll()
    {
        for (int i = 0; i < runs.Length; i++) if (runs[i] != null) runs[i].Stop();
        StopTop();
        var list = AttackPool.Projectiles;
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null && list[i].Active && list[i].Owner == this) list[i].Finish(false);
    }

    void OnDestroy()
    {
        StopAll();
        for (int i = 0; i < runs.Length; i++) if (runs[i] != null && runs[i].spark != null) runs[i].spark.Dispose();
    }
}

// One firing of an attack in progress. Preallocated by the runner and reused.
public class AttackRun
{
    public bool active;
    public bool quick;      // a red atom's free shot: the shortened version of the attack, no slow motion
    public bool slowDone;   // a cinematic attack has played the part that needs the slow motion
    public ShipLoadout loadout;
    public int ship;
    public float t, next, startedAt;
    public int fired, hops;
    public float angle;
    public float budget;
    public Vector3 point, dir;   // dir: the thrown disc's heading
    public AttackSprite visual, disc;
    public SparkBolt spark;   // Volt Viper's spark (ShipAttackSpark.cs), allocated once per slot
    public AttackV3 v3;   // scratch of the v3 attacks (ShipAttacksV3.cs), allocated once per slot
    public readonly List<GameObject> hit = new List<GameObject>(32);

    public void Begin(ShipLoadout l, int shipId, float now)
    {
        active = true;
        loadout = l;
        ship = shipId;
        t = next = 0f;
        quick = slowDone = false;
        startedAt = now;
        fired = hops = 0;
        angle = 0f;
        budget = 1f;
        point = dir = Vector3.zero;
        visual = disc = null;
        if (spark != null) spark.Hide();
        hit.Clear();
        if (v3 != null) v3.Reset();
    }

    public void Stop()
    {
        active = false;
        if (v3 != null) v3.Release();
        if (spark != null) spark.Hide();
        if (visual != null && visual.Active && visual.Run == this) visual.Stop();
        if (disc != null && disc.Active && disc.Run == this) disc.Stop();
        visual = disc = null;
    }
}

// Pools for the attacks' projectiles and drawn effects. Grow to the peak in
// use, then reuse; hard caps. An over-cap projectile is simply not launched
// (the rest of the volley still flies); an over-cap effect steals the
// oldest one. Lives under "~ShipAttacks", so a scene load starts it over.
public static class AttackPool
{
    public const int MaxProjectiles = 40;
    public const int MaxSprites = 40;

    static readonly List<AttackProjectile> projectiles = new List<AttackProjectile>();
    static readonly List<AttackSprite> sprites = new List<AttackSprite>();
    static Transform root;

    static Transform Root
    {
        get
        {
            if (root == null)
            {
                root = new GameObject("~ShipAttacks").transform;
                projectiles.Clear();
                sprites.Clear();
            }
            return root;
        }
    }

    public static List<AttackProjectile> Projectiles { get { Purge(); return projectiles; } }

    static void Purge()
    {
        if (root == null) { projectiles.Clear(); sprites.Clear(); return; }
        for (int i = projectiles.Count - 1; i >= 0; i--) if (projectiles[i] == null) projectiles.RemoveAt(i);
        for (int i = sprites.Count - 1; i >= 0; i--) if (sprites[i] == null) sprites.RemoveAt(i);
    }

    public static int ProjectilePoolSize { get { Purge(); return projectiles.Count; } }
    public static int SpritePoolSize { get { Purge(); return sprites.Count; } }

    public static int ActiveProjectiles
    {
        get
        {
            Purge();
            int n = 0;
            for (int i = 0; i < projectiles.Count; i++) if (projectiles[i].Active) n++;
            return n;
        }
    }

    public static int ActiveSprites
    {
        get
        {
            Purge();
            int n = 0;
            for (int i = 0; i < sprites.Count; i++) if (sprites[i].Active) n++;
            return n;
        }
    }

    public static AttackProjectile Projectile()
    {
        var parent = Root;
        Purge();
        for (int i = 0; i < projectiles.Count; i++) if (!projectiles[i].Active) return projectiles[i];
        if (projectiles.Count >= MaxProjectiles) return null;
        var go = new GameObject("~atk");
        go.transform.SetParent(parent, false);
        var p = go.AddComponent<AttackProjectile>();
        p.Build();
        projectiles.Add(p);
        return p;
    }

    public static AttackSprite Sprite()
    {
        var parent = Root;
        Purge();
        AttackSprite oldest = null;
        for (int i = 0; i < sprites.Count; i++)
        {
            var s = sprites[i];
            if (!s.Active) return s;
            if (oldest == null || s.StartedAt < oldest.StartedAt) oldest = s;
        }
        if (sprites.Count >= MaxSprites && oldest != null) { oldest.Stop(); return oldest; }
        var go = new GameObject("~atkfx", typeof(SpriteRenderer));
        go.transform.SetParent(parent, false);
        var fx = go.AddComponent<AttackSprite>();
        fx.Build();
        sprites.Add(fx);
        return fx;
    }

    public static void StopAll()
    {
        Purge();
        for (int i = 0; i < projectiles.Count; i++) if (projectiles[i].Active) projectiles[i].Finish(false);
        for (int i = 0; i < sprites.Count; i++) if (sprites[i].Active) sprites[i].Stop();
    }
}

// A pooled drawn effect: a flipbook from ShipFxArt (or the ship's own shot
// frames) placed, stretched and turned by whoever plays it, optionally
// riding along with a transform. Gameplay time: frozen while paused.
public class AttackSprite : MonoBehaviour
{
    public enum Source { AttackLoop, AttackBurst, Extra, Dome }

    SpriteRenderer sr;
    Source source;
    int ship;
    int[] ticks;
    bool loop;
    float clock, life;
    Transform follow;
    Vector3 offset;
    Color tint = Color.white;
    bool renamed;
    float alphaMul = 1f;
    string suffix;   // an extra strip of the ship (ShipFxArt.Extra) instead of its main attack strip
    // a flight from the ship to where the effect acts (Travel)
    bool travelling;
    Vector3 travelFrom, travelTo;
    float travelSeconds, travelClock;
    Vector3 baseScale;
    float growFrom = 1f;
    // Source.Extra: which strip (ShipFxArt.Extra suffix) and which cell the frame numbers start at
    string extraSuffix;
    int extraCell;
    bool reversed;

    public bool Active { get; private set; }
    public float StartedAt { get; private set; }
    public int Frame { get; private set; }
    public AttackRun Run { get; set; }
    public Source CurrentSource => source;
    public Sprite CurrentSprite => sr != null ? sr.sprite : null;
    public SpriteRenderer Renderer => sr;

    public void Build()
    {
        sr = GetComponent<SpriteRenderer>();
        gameObject.SetActive(false);
    }

    // life <= 0: a one-shot plays its ticks once; a loop runs until Stop().
    public AttackSprite Play(Source src, int shipId, Vector3 at, Vector3 scale, float angle, int[] tickTable,
                             bool looping, float lifeSeconds, Transform followTransform, Vector3 followOffset,
                             int order)
    {
        if (renamed) { gameObject.name = "~atkfx"; renamed = false; }
        source = src;
        ship = shipId;
        ticks = tickTable;
        loop = looping;
        life = lifeSeconds;
        clock = 0f;
        Frame = 0;
        follow = followTransform;
        offset = followOffset;
        tint = Color.white;
        Run = null;
        suffix = null;
        travelling = false;
        growFrom = 1f;
        extraSuffix = null;
        extraCell = 0;
        reversed = false;
        baseScale = scale;
        alphaMul = AlphaFor(src);
        StartedAt = Time.unscaledTime;
        sr.sortingOrder = order;
        sr.color = Shown(tint);
        transform.position = follow != null ? follow.position + offset : at;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
        transform.localScale = scale;
        Active = true;
        gameObject.SetActive(true);
        Apply();
        return this;
    }

    public AttackSprite Owned(AttackRun run) { Run = run; return this; }

    // Draws an extra strip of the ship ("mark" -> <Ship>_attack_mark.png); AttackLoop / AttackBurst sources only.
    public AttackSprite Strip(string stripSuffix) { suffix = stripSuffix; Apply(); return this; }

    // Draws from a ship's extra strip instead (Source.Extra): frame i shows cell firstCell + i.
    // Loops and bursts alike get the burst alpha unless `loopAlpha`.
    public AttackSprite Strip(string suffix, int firstCell, bool loopAlpha = false)
    {
        source = Source.Extra;
        extraSuffix = suffix;
        extraCell = firstCell;
        alphaMul = loopAlpha ? ShipFxArt.AttackAlpha : ShipFxArt.AttackBurstAlpha;
        sr.color = Shown(tint);
        Apply();
        return this;
    }

    // Overrides the source's usual alpha (the mushroom cloud's 0.8).
    public AttackSprite Alpha(float a)
    {
        alphaMul = a;
        sr.color = Shown(tint);
        return this;
    }

    // Plays the 8 drawings backwards (a pod folding away). Attack bursts and extra strips only.
    public AttackSprite Reverse() { reversed = true; Apply(); return this; }

    // Scale and position are the caller's to drive each step (a sweeping beam, a growing ring).
    public Transform Body => transform;

    // A findable name while this effect plays (reset on the next Play).
    public void Rename(string n) { gameObject.name = n; renamed = true; }

    public void SetColor(Color c) { tint = c; if (sr != null) sr.color = Shown(c); }

    static float AlphaFor(Source src)
    {
        switch (src)
        {
            case Source.AttackLoop:  return ShipFxArt.AttackAlpha;
            case Source.AttackBurst: return ShipFxArt.AttackBurstAlpha;
            case Source.Dome:        return ShipFxArt.RimAlpha;
            default:                 return 1f;
        }
    }

    Color Shown(Color c) { c.a *= alphaMul; return c; }

    // Sends the effect from where it was played (the ship) to `to`, growing
    // from `startScale` x its size to full on the way. Gameplay is unaffected:
    // callers place the thing where it acts at once and only draw the flight.
    public AttackSprite Travel(Vector3 to, float seconds, float startScale = .4f)
    {
        travelling = seconds > 0f;
        travelFrom = transform.position;
        travelTo = to;
        travelSeconds = seconds;
        travelClock = 0f;
        growFrom = startScale;
        if (travelling) transform.localScale = baseScale * growFrom;
        return this;
    }

    public bool Travelling => travelling;

    void Update()
    {
        if (Active) Tick(ShipAttackRunner.ScaledDelta());
    }

    public void Tick(float dt)
    {
        if (!Active) return;
        if (follow != null) transform.position = follow.position + offset;
        if (dt <= 0f) return;
        clock += dt;
        if (travelling)
        {
            travelClock += dt;
            float k = Mathf.Clamp01(travelClock / travelSeconds);
            float e = 1f - (1f - k) * (1f - k);
            transform.position = Vector3.Lerp(travelFrom, travelTo, e);
            transform.localScale = baseScale * Mathf.Lerp(growFrom, 1f, e);
            if (k >= 1f) travelling = false;
        }
        else if (source == Source.Dome && loop)
        {
            // the held dome breathes, so it reads as alive without filling in
            var pc = tint; pc.a *= .78f + .22f * Mathf.Sin(clock * 6f);
            sr.color = Shown(pc);
        }
        if (life > 0f && clock >= life) { Stop(); return; }
        int f = WeaponArt.FrameAt(ticks, clock, loop);
        if (!loop && f >= ticks.Length) { Stop(); return; }
        if (f != Frame) { Frame = f; Apply(); }
        // the last fifth of a timed effect fades out in two steps (cel timing)
        if (life > 0f)
        {
            float left = 1f - clock / life;
            var c = tint;
            c.a *= left > .2f ? 1f : left > .1f ? .6f : .3f;
            if (source == Source.Dome && loop) c.a *= .78f + .22f * Mathf.Sin(clock * 6f);
            sr.color = Shown(c);
        }
    }

    void Apply()
    {
        switch (source)
        {
            case Source.AttackLoop:  sr.sprite = suffix != null ? ShipFxArt.Extra(ship, suffix, false, Frame) : ShipFxArt.AttackLoop(ship, Frame); break;
            case Source.AttackBurst:
                {
                    int bf = reversed ? ShipFxArt.BurstFrames - 1 - Frame : Frame;
                    sr.sprite = suffix != null ? ShipFxArt.Extra(ship, suffix, true, bf) : ShipFxArt.AttackBurst(ship, bf);
                }
                break;
            case Source.Extra:       sr.sprite = ShipFxArt.Extra(ship, extraSuffix, extraCell + (reversed ? ShipFxArt.BurstFrames - 1 - Frame : Frame)); break;
            case Source.Dome:        sr.sprite = ShipFxArt.Dome; break;
        }
    }

    public void Stop()
    {
        Active = false;
        follow = null;
        Run = null;
        gameObject.SetActive(false);
    }
}

// One pooled projectile: the ship's own shot drawing (WeaponArt) with a short
// light streak behind it. Its runner steps it on gameplay time.
public class AttackProjectile : MonoBehaviour
{
    public enum Kind { Straight, Homing, Javelin, Rocket }

    SpriteRenderer body;

    ShipAttackRunner owner;
    AttackRun run;
    Kind kind;
    int ship;
    Vector3 dir;
    float speed, radius, size, weight, age, travelled;
    Transform target;
    GameObject lastSpecial;
    int hops;
    int pierceLeft;        // Straight: enemies it can still pass through (ShipSignatures.CometPierce)
    Vector3 rocketTo;      // Rocket: where the javelin manifests
    float rocketStopY;     // ...and where it will strike down to

    // Projectiles launched since load (tests, previews).
    public static int LaunchCount;

    // The ones in flight right now, for the elites' threat sensor
    // (EliteEvasion.PlayerShotAwareness; nothing reads it at the default 0).
    public static readonly System.Collections.Generic.List<AttackProjectile> Flying = new System.Collections.Generic.List<AttackProjectile>(32);
    public Vector2 Velocity => (Vector2)dir * speed;
    public float HitRadius => radius;
    void OnDestroy() { Flying.Remove(this); }

    // The shield's shockwave (ShieldShockwave): displaced by `by`, trail and
    // all. It steers from where it is (dir), so it flies on from the new place.
    public void Shove(Vector2 by)
    {
        if (!Active) return;
        transform.position += (Vector3)by;
    }

    public bool Active { get; private set; }
    public Transform TargetTransform => target;
    public ShipAttackRunner Owner => owner;
    public Transform Target => target;
    public Kind CurrentKind => kind;
    public float StopAtY { get; set; }

    // A Sky Javelins rocket: flies from the ship to `top`, where its javelin
    // manifests and strikes down the lane to `stopY`. Damages nothing itself.
    public void SetRocket(Vector3 top, float stopY) { rocketTo = top; rocketStopY = stopY; }
    public int Hops => hops;

    // A Straight shot that passes through `n` enemies before it stops (never a boss or elite). Drawn a little
    // larger while it still can, so a piercing streak reads at a glance.
    public void SetPierce(int n) { pierceLeft = Mathf.Max(0, n); Draw(0f); }
    public int PierceLeft => pierceLeft;
    const float PierceDrawScale = 1.25f;

    public void Build()
    {
        var bodyGo = new GameObject("Body", typeof(SpriteRenderer));
        bodyGo.transform.SetParent(transform, false);
        body = bodyGo.GetComponent<SpriteRenderer>();
        body.sortingOrder = 72;
        body.color = new Color(1f, 1f, 1f, BodyAlpha);
        gameObject.SetActive(false);
    }

    // The shot's own drawing (its Codex strip carries the light tail), a touch fainter than solid.
    const float BodyAlpha = .9f;

    public void Launch(ShipAttackRunner by, AttackRun from, Kind k, int shipId, Vector3 at, Vector3 heading,
                       float spd, float hitRadius, float worldSize, Transform homeOn, float hitWeight)
    {
        if (k != Kind.Rocket) LaunchCount++;   // a Sky Javelins rocket is not a shot: its javelin is
        owner = by;
        run = from;
        kind = k;
        ship = shipId;
        dir = heading.sqrMagnitude > 1e-6f ? heading.normalized : Vector3.up;
        speed = spd;
        radius = hitRadius;
        size = worldSize * (k == Kind.Rocket ? 1f : ShipFxArt.ShotScale);
        weight = hitWeight;
        target = homeOn;
        age = travelled = 0f;
        lastSpecial = null;
        hops = 0;
        pierceLeft = 0;
        StopAtY = float.MinValue;
        transform.position = at;
        if (!Active) Flying.Add(this);
        Active = true;
        gameObject.SetActive(true);
        Draw(0f);
    }

    public void Tick(float dt)
    {
        if (!Active || dt <= 0f) return;
        age += dt;
        Vector3 from = transform.position;

        if (kind == Kind.Homing && target != null)
        {
            // dropped when it dies, scrolls off the screen, or a boss is up and this isn't it
            var ct = target.GetComponent<ClearTarget>();
            if (ct == null || !ct.isActiveAndEnabled || !ShipTargets.CanHome(ct)) target = null;
        }
        if (kind == Kind.Homing && target == null && age < 1.2f)
        {
            // lost its mark: the nearest thing still around, if any (the boss, while one is up)
            var t = ShipTargets.NearestHoming(from, ShipTargets.BossAlive() ? 0f : 3.5f, null);
            if (t != null) target = t.transform;
        }
        if (kind == Kind.Homing && target != null)
            dir = PowerFx.SteerHeading(dir, target.position - from, 420f, dt);

        Vector3 to = from + dir * speed * dt;
        travelled += speed * dt;
        if (kind == Kind.Rocket)
        {
            // straight at the lane's top; on arrival the javelin manifests from it
            Vector3 left = rocketTo - from;
            if (left.sqrMagnitude <= (speed * dt) * (speed * dt))
            {
                transform.position = rocketTo;
                ManifestJavelin();
                return;
            }
            dir = left.normalized;
            transform.position = from + dir * speed * dt;
            Draw(dt);
            return;
        }
        transform.position = to;
        // Defensive play: hostile shots it passes through are shot down
        // (no score); it flies on, its own hits unchanged (HostileShots).
        HostileShots.ShootDownAlong(from, to, radius);

        switch (kind)
        {
            case Kind.Javelin:
                StrikeLane(from, to);
                if (to.y <= StopAtY) { Land(); return; }
                break;
            default:
                {
                    var hit = owner != null ? owner.FirstAlong(from, to, radius, lastSpecial) : null;
                    if (hit != null)
                    {
                        var struckGo = hit.gameObject;
                        bool special = struckGo.GetComponent<IShipAttackTarget>() != null;
                        Strike(struckGo);
                        if (kind == Kind.Straight && pierceLeft > 0 && !special)
                        {
                            // signature pierce: on through this enemy (it is gone or about to be), a spark where it passed
                            pierceLeft--;
                            lastSpecial = struckGo;
                            AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, struckGo.transform.position,
                                Vector3.one * .35f, 0f, ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 71);
                        }
                        else if (kind == Kind.Homing || kind == Kind.Straight) { Finish(true); return; }
                    }
                }
                break;
        }

        Rect view = ShipTargets.View(1f);
        if (!view.Contains(to) || age > 4f) { Finish(false); return; }
        Draw(dt);
    }

    void Strike(GameObject go)
    {
        if (go.GetComponent<IShipAttackTarget>() != null) lastSpecial = go;
        ShipAttackHits.Hit(go, ship, weight, ref run.budget);
    }

    // Javelins pierce: everything in their lane between this step's two
    // heights is struck.
    void StrikeLane(Vector3 from, Vector3 to)
    {
        if (owner == null) return;
        var list = owner.TargetsInView();
        for (int i = 0; i < list.Count; i++)
        {
            var t = list[i];
            if (t == null || !t.isActiveAndEnabled || t.gameObject == lastSpecial) continue;
            Vector3 p = t.transform.position;
            float r = t.Radius;
            if (Mathf.Abs(p.x - to.x) > radius + r * .5f) continue;
            if (p.y - r > from.y || p.y + r < to.y) continue;
            Strike(t.gameObject);
        }
    }

    void ManifestJavelin()
    {
        Vector3 at = transform.position;
        float stop = rocketStopY;
        var by = owner;
        var r = run;
        int sh = ship;
        float rad = radius, w = weight, spd = speed;
        Finish(false);
        if (by == null || r == null) return;
        // a small spark where the rocket opens into its javelin
        AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, sh, at, Vector3.one * .4f, 0f,
            ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 71).Strip("attack_shot", ShipFxArt.LoopFrames);
        by.LaunchJavelin(r, at, JavelinSpeedFor(r), rad, w, stop);
    }

    static float JavelinSpeedFor(AttackRun r) { return r.loadout.speed; }

    void Land()
    {
        AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, new Vector3(transform.position.x, StopAtY, 0f),
            Vector3.one * .55f, 0f, ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 71);
        Finish(false);
    }

    void Draw(float dt)
    {
        int frame = Mathf.FloorToInt(age * 24f);
        // a Sky Javelins rocket draws the ship's "attack_shot" strip, every other shot (comet, javelin) its attack loop
        Sprite sprite = kind == Kind.Rocket ? ShipFxArt.Extra(ship, "attack_shot", frame % ShipFxArt.LoopFrames) : null;
        body.sprite = sprite != null ? sprite : ShipFxArt.AttackLoop(ship, frame);
        body.transform.localScale = Vector3.one * size * (pierceLeft > 0 ? PierceDrawScale : 1f);
        body.transform.up = dir;
    }

    public void Finish(bool hit)
    {
        if (!Active) return;
        Active = false;
        Flying.Remove(this);
        target = null;
        lastSpecial = null;
        if (hit)
            AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, transform.position, Vector3.one * .5f, 0f,
                ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 71);
        gameObject.SetActive(false);
    }
}

// Burst-size scales for the fireball's (and the old blast's) burst art, kept
// in one place (the top tier's volley that used to live here is gone: the
// three cinematic attacks are in ShipAttacksV3.cs).
public static class ShipVolley
{
    // Blast burst art drew far too large next to every other weapon's
    // 0.5-1.1 unit effects (median ~0.8): the Gold Warden plays at 30% of its old size.
    public const int GoldWardenShip = 7;
    public const float GoldWardenImpactScale = .3f;
    public const float MaxBurstWorld = 2f;   // guarded by HomingTargetTest
    public static float BurstScaleFor(int ship)
    {
        return ship == GoldWardenShip ? GoldWardenImpactScale : 1f;
    }
    public static float BlastBurstSize(int ship, float radius)
    {
        return radius * 2.3f * BurstScaleFor(ship) * ShipFxArt.BurstScale;
    }
}

