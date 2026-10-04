using System.Collections.Generic;
using UnityEngine;

// The directional and limited main attacks (every ship below the top price
// tier; see ShipLoadoutTable). ShipPowerController.Fire() calls Fire() here
// when the charge meter fills; the top tier runs the cinematic volley
// instead.
//
// Rules:
//  * Gameplay time. Everything steps on scaled time, so a paused world
//    (timeScale 0) freezes every shot, beam, cone and orbit mid-flight, and
//    there is no slow motion: these play at normal speed (a big hit may add
//    a hit-stop of a few hundredths of a second, WorldTimeFx).
//  * Pooled. Projectiles and drawn effects come from AttackPool, capped,
//    reused; runs are preallocated; target queries fill a list owned here.
//    Nothing allocates per frame.
//  * Every hit goes through ShipAttackHits.Hit, so an IShipAttackTarget (the
//    boss) takes a weighted hit instead of being destroyed, and one firing
//    never deals it more than one full hit in total.
public class ShipAttackRunner : MonoBehaviour
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
        loadout = ShipLoadoutTable.For(shipId);
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
        if (loadout.attack == ShipAttack.ScreenClear) return false;
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

    void StartRun(AttackRun run)
    {
        var l = run.loadout;
        Vector3 nose = Nose;
        switch (l.attack)
        {
            case ShipAttack.RailSlug:
                Rail(run, nose);
                run.active = false;
                break;
            case ShipAttack.ChainLightning:
                run.point = nose;
                run.next = 0f;
                break;
            case ShipAttack.SolarFireball:
                Launch(run, AttackProjectile.Kind.Fireball, nose, Vector3.up, l.speed, .34f, .72f, null, 1f);
                break;
            case ShipAttack.SeekerEyes:
                LaunchSeekers(run, nose);
                break;
            case ShipAttack.Ricochet:
                {
                    var first = ShipTargets.Nearest(nose, 0f, run.hit);
                    Launch(run, AttackProjectile.Kind.Ricochet, nose, Vector3.up, l.speed, .2f, .5f,
                           first != null ? first.transform : null, 1f);
                }
                break;
            case ShipAttack.Blowtorch:
                run.visual = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, nose,
                    ConeScale(l), 0f, ShipFxArt.LoopTicks, true, l.duration, transform,
                    Vector3.up * (noseOffset + l.range * .5f), 69).Owned(run);
                break;
            case ShipAttack.TractorBeam:
                {
                    float len = BeamLength(nose, l.range);
                    run.visual = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, nose,
                        new Vector3(l.width * 2f * 1.5f, len, 1f), 0f, ShipFxArt.LoopTicks, true, l.duration,
                        transform, Vector3.up * (noseOffset + len * .5f), 69).Owned(run);
                    AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, nose, Vector3.one * .9f, 0f,
                        ShipFxArt.BurstTicks, false, 0f, transform, Vector3.up * noseOffset, 71);
                }
                break;
            case ShipAttack.OrbitDisc:
                run.angle = 90f;
                run.visual = AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, transform.position,
                    Vector3.one * (l.width * 2f + .5f), 0f, ShipFxArt.LoopTicks, true, l.duration, transform,
                    Vector3.zero, 68).Owned(run);
                run.disc = AttackPool.Sprite().Play(AttackSprite.Source.Shot, ship, transform.position,
                    Vector3.one * .55f, 0f, ShotLoop, true, l.duration, null, Vector3.zero, 72).Owned(run);
                break;
        }
        // first volley of the timed attacks goes out on this frame
        Step(run, 0f);
    }

    static readonly int[] ShotLoop = { 2, 2, 2, 2 };

    static Vector3 ConeScale(ShipLoadout l)
    {
        float halfWidth = Mathf.Tan(l.width * Mathf.Deg2Rad) * l.range;
        return new Vector3(halfWidth * 2f * 1.15f, l.range, 1f);
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

    void LaunchSeekers(AttackRun run, Vector3 nose)
    {
        var l = run.loadout;
        for (int i = 0; i < l.shots; i++)
        {
            var t = ShipTargets.Nearest(nose, 0f, run.hit);
            if (t != null) run.hit.Add(t.gameObject); // claimed: the next eye picks another
            float a = (i - (l.shots - 1) * .5f) * 24f;
            Vector3 dir = Quaternion.Euler(0f, 0f, -a) * Vector3.up;
            Launch(run, AttackProjectile.Kind.Homing, nose, dir, l.speed, .22f, .5f,
                   t != null ? t.transform : null, 1f / Mathf.Max(1, l.shots));
        }
        run.hit.Clear();
    }

    // ---------------------------------------------------------------- rail

    // Instant: everything whose body is within `width` of the ship's line,
    // from just behind the nose up to the top of the screen, is hit.
    void Rail(AttackRun run, Vector3 nose)
    {
        var l = run.loadout;
        float len = BeamLength(nose, l.range);
        AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, nose + Vector3.up * len * .5f,
            new Vector3(l.width * 2f * 1.6f, len, 1f), 0f, ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 70);
        AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, nose, Vector3.one * 1.1f, 0f,
            ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 71);
        ShipTargets.Collect(targets);
        int kills = 0;
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || !t.isActiveAndEnabled) continue;
            if (InRailLine(t.transform.position, t.Radius, nose, l.width, nose.y + len))
                if (ShipAttackHits.Hit(t.gameObject, ship, 1f, ref run.budget)) kills++;
        }
        if (kills > 0) WorldTimeFx.HitStop(.05f);
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
                    Launch(run, AttackProjectile.Kind.Straight, Nose, Vector3.up, l.speed, l.width, .55f, null,
                           1f / Mathf.Max(1, l.shots));
                    run.fired++;
                }
                if (run.fired >= l.shots) run.active = false;
                break;

            case ShipAttack.FeatherFan:
                for (int i = 0; i < l.shots; i++)
                {
                    float a = l.shots <= 1 ? 0f : -l.width * .5f + l.width * i / (l.shots - 1);
                    Vector3 dir = Quaternion.Euler(0f, 0f, -a) * Vector3.up;
                    Launch(run, AttackProjectile.Kind.Straight, Nose, dir, l.speed, .2f, .44f, null,
                           1f / Mathf.Max(1, l.shots));
                }
                run.fired = l.shots;
                run.active = false;
                break;

            case ShipAttack.Gatling:
                {
                    float every = l.duration / Mathf.Max(1, l.shots);
                    while (run.fired < l.shots && run.t >= run.fired * every)
                    {
                        // a tight weave across the cone, not random
                        float a = Mathf.Sin(run.fired * 2.4f) * l.width;
                        Vector3 dir = Quaternion.Euler(0f, 0f, -a) * Vector3.up;
                        Vector3 nose = Nose;
                        Launch(run, AttackProjectile.Kind.Straight, nose, dir, l.speed, .16f, .32f, null,
                               1f / Mathf.Max(1, l.shots));
                        AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, nose, Vector3.one * .5f,
                            -a, ShipFxArt.BurstTicks, false, 0f, transform, Vector3.up * noseOffset, 71);
                        run.fired++;
                    }
                    if (run.fired >= l.shots) run.active = false;
                }
                break;

            case ShipAttack.SkyJavelins:
                {
                    // centre lane first, then the outer two
                    Rect view = ShipTargets.View();
                    while (run.fired < l.shots && run.t >= run.fired * .08f)
                    {
                        int lane = run.fired == 0 ? 0 : (run.fired % 2 == 1 ? -1 : 1) * ((run.fired + 1) / 2);
                        float x = Mathf.Clamp(transform.position.x + lane * l.range, view.xMin + .3f, view.xMax - .3f);
                        float top = Mathf.Min(view.yMax + .6f, transform.position.y + 30f);
                        var p = Launch(run, AttackProjectile.Kind.Javelin, new Vector3(x, top, 0f), Vector3.down,
                                       l.speed, l.width, .9f, null, 1f / Mathf.Max(1, l.shots));
                        if (p != null) p.StopAtY = transform.position.y + .3f;
                        run.fired++;
                    }
                    if (run.fired >= l.shots) run.active = false;
                }
                break;

            case ShipAttack.ChainLightning:
                StepChain(run);
                break;

            case ShipAttack.Blowtorch:
                StepCone(run);
                if (run.t >= l.duration) run.Stop();
                break;

            case ShipAttack.TractorBeam:
                StepBeam(run);
                if (run.t >= l.duration) run.Stop();
                break;

            case ShipAttack.OrbitDisc:
                StepOrbit(run, dt);
                if (run.t >= l.duration) run.Stop();
                break;

            default:
                // projectile attacks (fireball, seekers, ricochet) are done
                // launching; their projectiles finish on their own
                run.active = false;
                break;
        }
    }

    // A bolt from the nose to the nearest enemy, then hop to hop, one hop
    // every 60 ms, each to the nearest one not yet hit within reach.
    void StepChain(AttackRun run)
    {
        var l = run.loadout;
        while (run.active && run.t >= run.next)
        {
            float reach = run.hops == 0 ? l.range : l.width;
            var t = ShipTargets.Nearest(run.point, reach, run.hit);
            if (t == null || run.hops >= l.maxHits) { run.active = false; break; }
            Vector3 to = t.transform.position;
            Arc(run.point, to);
            AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, to, Vector3.one * .8f, 0f,
                ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 71);
            run.hit.Add(t.gameObject);
            ShipAttackHits.Hit(t.gameObject, ship, 1f, ref run.budget);
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
        AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship, (a + b) * .5f, new Vector3(len, .55f, 1f),
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

    void StepBeam(AttackRun run)
    {
        var l = run.loadout;
        Vector3 nose = Nose;
        float len = BeamLength(nose, l.range);
        ShipTargets.Collect(targets);
        bool bossTick = run.t >= run.next;
        if (bossTick) run.next = run.t + .25f;
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || !t.isActiveAndEnabled) continue;
            if (!InRailLine(t.transform.position, t.Radius, nose, l.width, nose.y + len)) continue;
            Touch(run, t.gameObject, bossTick, .2f);
        }
    }

    void StepOrbit(AttackRun run, float dt)
    {
        var l = run.loadout;
        run.angle += l.speed * dt;
        float a = run.angle * Mathf.Deg2Rad;
        Vector3 disc = transform.position + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * l.width;
        if (run.disc != null && run.disc.Active) run.disc.transform.position = disc;
        run.point = disc;
        ShipTargets.Collect(targets);
        bool bossTick = run.t >= run.next;
        if (bossTick) run.next = run.t + .4f;
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t == null || !t.isActiveAndEnabled) continue;
            Vector2 d = t.transform.position - disc;
            float r = l.range + t.Radius;
            if (d.sqrMagnitude > r * r) continue;
            Touch(run, t.gameObject, bossTick, .25f);
        }
    }

    // A held weapon touching something: ordinary hazards die at once, a
    // boss takes `weight` at most once per tick.
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
        var list = AttackPool.Projectiles;
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null && list[i].Active && list[i].Owner == this) list[i].Finish(false);
    }

    void OnDestroy()
    {
        StopAll();
    }
}

// One firing of an attack in progress. Preallocated by the runner and reused.
public class AttackRun
{
    public bool active;
    public ShipLoadout loadout;
    public int ship;
    public float t, next, startedAt;
    public int fired, hops;
    public float angle;
    public float budget;
    public Vector3 point;
    public AttackSprite visual, disc;
    public readonly List<GameObject> hit = new List<GameObject>(32);

    public void Begin(ShipLoadout l, int shipId, float now)
    {
        active = true;
        loadout = l;
        ship = shipId;
        t = next = 0f;
        startedAt = now;
        fired = hops = 0;
        angle = 0f;
        budget = 1f;
        point = Vector3.zero;
        visual = disc = null;
        hit.Clear();
    }

    public void Stop()
    {
        active = false;
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
    public enum Source { AttackLoop, AttackBurst, PowerLoop, PowerBurst, Shot }

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
        StartedAt = Time.unscaledTime;
        sr.sortingOrder = order;
        sr.color = tint;
        transform.position = follow != null ? follow.position + offset : at;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
        transform.localScale = scale;
        Active = true;
        gameObject.SetActive(true);
        Apply();
        return this;
    }

    public AttackSprite Owned(AttackRun run) { Run = run; return this; }

    // A findable name while this effect plays (reset on the next Play).
    public void Rename(string n) { gameObject.name = n; renamed = true; }

    public void SetColor(Color c) { tint = c; if (sr != null) sr.color = c; }

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
            sr.color = c;
        }
    }

    void Apply()
    {
        switch (source)
        {
            case Source.AttackLoop:  sr.sprite = ShipFxArt.AttackLoop(ship, Frame); break;
            case Source.AttackBurst: sr.sprite = ShipFxArt.AttackBurst(ship, Frame); break;
            case Source.PowerLoop:   sr.sprite = ShipFxArt.PowerLoop(ship, Frame); break;
            case Source.PowerBurst:  sr.sprite = ShipFxArt.PowerBurst(ship, Frame); break;
            case Source.Shot:        sr.sprite = WeaponArt.Shot(ship, Frame); break;
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
    public enum Kind { Straight, Homing, Ricochet, Javelin, Fireball }

    const int Segments = 5;
    const float SampleSpacing = .12f;

    SpriteRenderer body;
    SpriteRenderer[] outer, core;
    readonly Vector3[] hist = new Vector3[Segments + 1];
    int histCount;

    ShipAttackRunner owner;
    AttackRun run;
    Kind kind;
    int ship;
    WeaponStyle style;
    Vector3 dir;
    float speed, radius, size, weight, age, travelled, spinAngle;
    Transform target;
    GameObject lastSpecial;
    int hops;

    // Projectiles launched since load (tests, previews).
    public static int LaunchCount;

    public bool Active { get; private set; }
    public ShipAttackRunner Owner => owner;
    public Transform Target => target;
    public Kind CurrentKind => kind;
    public float StopAtY { get; set; }
    public int Hops => hops;

    public void Build()
    {
        var bodyGo = new GameObject("Body", typeof(SpriteRenderer));
        bodyGo.transform.SetParent(transform, false);
        body = bodyGo.GetComponent<SpriteRenderer>();
        body.sortingOrder = 72;
        outer = Layer("Outer", 70);
        core = Layer("Core", 71);
        gameObject.SetActive(false);
    }

    SpriteRenderer[] Layer(string name, int order)
    {
        var layer = new SpriteRenderer[Segments];
        for (int i = 0; i < Segments; i++)
        {
            var go = new GameObject(name, typeof(SpriteRenderer));
            go.transform.SetParent(transform, false);
            layer[i] = go.GetComponent<SpriteRenderer>();
            layer[i].sprite = WeaponFx.Solid;
            layer[i].sortingOrder = order;
            layer[i].enabled = false;
        }
        return layer;
    }

    public void Launch(ShipAttackRunner by, AttackRun from, Kind k, int shipId, Vector3 at, Vector3 heading,
                       float spd, float hitRadius, float worldSize, Transform homeOn, float hitWeight)
    {
        LaunchCount++;
        owner = by;
        run = from;
        kind = k;
        ship = shipId;
        style = WeaponStyleTable.For(shipId);
        dir = heading.sqrMagnitude > 1e-6f ? heading.normalized : Vector3.up;
        speed = spd;
        radius = hitRadius;
        size = worldSize;
        weight = hitWeight;
        target = homeOn;
        age = travelled = spinAngle = 0f;
        lastSpecial = null;
        hops = 0;
        StopAtY = float.MinValue;
        transform.position = at;
        for (int i = 0; i < hist.Length; i++) hist[i] = at;
        histCount = 1;
        for (int i = 0; i < Segments; i++)
        {
            outer[i].color = style.main;
            core[i].color = style.energy;
        }
        Active = true;
        gameObject.SetActive(true);
        body.sprite = WeaponArt.Shot(ship, WeaponArt.ShotLoopFrames); // launch smear
        Draw(0f);
    }

    public void Tick(float dt)
    {
        if (!Active || dt <= 0f) return;
        age += dt;
        Vector3 from = transform.position;

        if ((kind == Kind.Homing || kind == Kind.Ricochet) && target != null)
        {
            var ct = target.GetComponent<ClearTarget>();
            if (ct == null || !ct.isActiveAndEnabled) target = null;
        }
        if (kind == Kind.Homing && target == null && age < 1.2f)
        {
            // lost its mark: the nearest thing still around, if any
            var t = ShipTargets.Nearest(from, 3.5f, null);
            if (t != null) target = t.transform;
        }
        if ((kind == Kind.Homing || kind == Kind.Ricochet) && target != null)
            dir = PowerFx.SteerHeading(dir, target.position - from, kind == Kind.Ricochet ? 900f : 420f, dt);

        Vector3 to = from + dir * speed * dt;
        travelled += speed * dt;
        transform.position = to;

        switch (kind)
        {
            case Kind.Javelin:
                StrikeLane(from, to);
                if (to.y <= StopAtY) { Land(); return; }
                break;
            case Kind.Ricochet:
                if (target != null)
                {
                    var ct = target.GetComponent<ClearTarget>();
                    float r = radius + (ct != null ? ct.Radius : .3f);
                    float along;
                    if (ShipAttackRunner.SegmentHits(from, to, target.position, r, out along)) Bounce(target.gameObject);
                }
                else
                {
                    // nothing to home on: it still bounces off whatever it meets
                    var met = owner != null ? owner.FirstAlong(from, to, radius, lastSpecial) : null;
                    if (met != null && !run.hit.Contains(met.gameObject)) Bounce(met.gameObject);
                    else if (age > .5f && hops > 0) { Finish(false); return; }
                }
                if (!Active) return;
                break;
            default:
                {
                    var hit = owner != null ? owner.FirstAlong(from, to, radius, lastSpecial) : null;
                    if (hit != null)
                    {
                        if (kind == Kind.Fireball) { Burst(hit.transform.position); return; }
                        Strike(hit.gameObject);
                        if (kind == Kind.Homing || kind == Kind.Straight) { Finish(true); return; }
                    }
                    if (kind == Kind.Fireball && travelled >= run.loadout.range) { Burst(to); return; }
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

    void Land()
    {
        AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, new Vector3(transform.position.x, StopAtY, 0f),
            Vector3.one * 1.1f, 0f, ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 71);
        AttackPool.Sprite().Play(AttackSprite.Source.AttackLoop, ship,
            new Vector3(transform.position.x, (StopAtY + ShipTargets.View().yMax) * .5f, 0f),
            new Vector3(radius * 2f * 1.4f, Mathf.Max(1f, ShipTargets.View().yMax - StopAtY), 1f), 0f,
            ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 69);
        Finish(false);
    }

    void Bounce(GameObject go)
    {
        run.hit.Add(go);
        Strike(go);
        hops++;
        var next = hops < run.loadout.maxHits
            ? ShipTargets.Nearest(transform.position, run.loadout.range, run.hit)
            : null;
        target = next != null ? next.transform : null;
        age = 0f;
        if (target == null && hops >= run.loadout.maxHits) Finish(true);
    }

    void Burst(Vector3 at)
    {
        float r = run.loadout.width;
        AttackPool.Sprite().Play(AttackSprite.Source.AttackBurst, ship, at, Vector3.one * r * 2.3f, 0f,
            ShipFxArt.BurstTicks, false, 0f, null, Vector3.zero, 71);
        int kills = 0;
        if (owner != null)
        {
            var list = owner.TargetsInView();
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (t == null || !t.isActiveAndEnabled) continue;
                Vector2 d = t.transform.position - at;
                float reach = r + t.Radius * .5f;
                if (d.sqrMagnitude > reach * reach) continue;
                if (ShipAttackHits.Hit(t.gameObject, ship, 1f, ref run.budget)) kills++;
            }
        }
        if (kills > 0) WorldTimeFx.HitStop(.06f);
        CameraKick.Kick(.03f);
        Finish(false);
    }

    void Draw(float dt)
    {
        int frame = age < .07f ? WeaponArt.ShotLoopFrames : WeaponArt.FrameAt(LoopTicks, age, true);
        body.sprite = WeaponArt.Shot(ship, frame);
        body.transform.localScale = Vector3.one * size;
        if (style.shotSpin != 0f)
        {
            spinAngle += style.shotSpin * dt;
            body.transform.rotation = Quaternion.Euler(0f, 0f, spinAngle);
        }
        else body.transform.up = dir;

        Vector3 p = transform.position;
        hist[0] = p;
        if ((hist[0] - hist[1]).sqrMagnitude >= SampleSpacing * SampleSpacing || histCount == 1)
        {
            for (int i = hist.Length - 1; i > 0; i--) hist[i] = hist[i - 1];
            if (histCount < hist.Length) histCount++;
        }
        float w = Mathf.Clamp(size * .22f, .06f, .14f);
        for (int i = 0; i < Segments; i++)
        {
            bool on = i + 1 < histCount;
            float taper = 1f - i / (float)Segments;
            Segment(outer[i], on, hist[i], hist[i + 1], w * taper);
            Segment(core[i], on && i < 3, hist[i], hist[i + 1], w * taper * .45f);
        }
    }

    static readonly int[] LoopTicks = { 2, 2, 2, 2 };

    static void Segment(SpriteRenderer r, bool on, Vector3 a, Vector3 b, float width)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        if (!on || len < .0005f) { r.enabled = false; return; }
        r.enabled = true;
        var t = r.transform;
        t.position = (a + b) * .5f;
        t.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        t.localScale = new Vector3(len + width * .5f, width, 1f);
    }

    public void Finish(bool hit)
    {
        if (!Active) return;
        Active = false;
        target = null;
        lastSpecial = null;
        if (hit) WeaponFx.Impact(ship, transform.position, .5f);
        gameObject.SetActive(false);
    }
}

// Small multipliers on the world's time scale that moveBackGround applies on
// top of its own (running branch only; a frozen world stays at 0):
//   * hit-stop: a few hundredths of a second near-frozen on a big hit
//     (real time, so it can't stretch);
//   * the Time Bubble power's slow-down (SecretPowerController).
public static class WorldTimeFx
{
    public const float HitStopScale = .08f;
    static float hitStopUntil = -1f;
    public static float BubbleScale = 1f;

    // Tests drive real time through this; null means Time.unscaledTime.
    public static System.Func<float> ClockOverride;
    static float Now => ClockOverride != null ? ClockOverride() : Time.unscaledTime;

    public static void HitStop(float seconds)
    {
        hitStopUntil = Mathf.Max(hitStopUntil, Now + Mathf.Clamp(seconds, 0f, .12f));
    }

    public static bool HitStopping => Now < hitStopUntil;

    public static float Scale
    {
        get
        {
            float s = Mathf.Clamp(BubbleScale, .1f, 1f);
            if (HitStopping) s *= HitStopScale;
            return s;
        }
    }

    public static void Reset()
    {
        hitStopUntil = -1f;
        BubbleScale = 1f;
    }
}
