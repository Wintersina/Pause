using UnityEngine;

// Every ship starts with three dormant companions. Directional cores activate them and set
// straight, alternating or aimed fire; bosses recruit additional drones up to twelve.
// Aimed shots choose a heading at launch and never track afterward. Weapon skins retain
// their colour and final-tier cooldown cut. Core levels and shared RAPID increase fire rate.
// Firing holds during pauses, entry, transitions, death and tutorial, but not ultimates.
// Pooled bullets deal direct quarter-heart damage to ordinary enemies, elites and bosses.
public static class FloatieTuning
{
    public const int MaxFloaties = DroneSquad.MaxSlots;
    public const float FanDegrees = 10f;           // floaties 4 and 5 fire this far off straight up (left, right)
    // The normal gun fires half as often (combat rework, 2026-11-22: x2 on every base cooldown; was 1 s / 2 s). The boss rate is unchanged.
    public const float NormalCooldown = 2f;        // floatie #1 and #2, seconds
    public const float BossCooldown = 5f;          // every floatie while a boss is active
    public const float FinalLevelCut = .25f;       // the final level's cooldown cut
    public const float SpreadMinDegrees = 15f, SpreadMaxDegrees = 30f;   // floatie #2's random angle off straight up, left or right
    public const float SeekerRange = 6f;           // world units from the seeker's muzzle
    public const float BulletSpeed = 9f;           // world units a second
    public const float BulletRadius = .09f;        // hit radius
    public const float EliteWeight = 1f;         // one plain bullet, in hearts
    public const float BossWeight = 1f;          // one plain bullet, in hearts
    public const int PoolSize = ShotAtomTuning.MaxBullets;   // bullets alive at once, at most (the shot atoms' cap; twin and split need the room)

    public static int CountAt(int level) => DroneSquad.Slots;
    public static int CountAt(int ship, int level) => DroneSquad.Slots;

    public enum Role { Main, Spread, Seeker, FanLeft, FanRight, SeekerTwo }

    // The slot's collected core determines its firing mode.
    public static Role RoleOf(int index) { return DroneSquad.Mode(index) == DroneMode.Aimed ? Role.Seeker : DroneSquad.Mode(index) == DroneMode.Alternating ? Role.Spread : Role.Main; }
    public static bool IsSeeker(int index) { return DroneSquad.Mode(index) == DroneMode.Aimed; }

    // Floatie 4 fires FanDegrees to the left, floatie 5 the same to the right (+ is left, as a bullet's angle).
    public static float FanAngle(int index) { return index == 3 ? FanDegrees : index == 4 ? -FanDegrees : 0f; }
    public static bool IsFinal(int level) { return level >= ShipWeaponUpgrades.MaxLevel; }

    // Seconds between floatie `index` (0-based) shots at `level`.
    // (the shot atom RAPID then shortens it: +20% .. +160% fire rate, never under ShotAtomTuning.MinCooldown, boss fights included)
    public static float Cooldown(int index, int level, bool bossActive)
    {
        float seconds = bossActive ? BossCooldown : NormalCooldown * (IsFinal(level) ? 1f - FinalLevelCut : 1f);
        return Mathf.Max(ShotAtomTuning.MinCooldown, ShotAtoms.Rapid(seconds) / (1f + DroneSquad.RateBonus(index)));
    }

    // Floatie #2's angle off straight up (degrees, + is to the left): `side01` < .5 left, otherwise right; `spread01` 0..1 picks the size.
    public static float SpreadAngle(float side01, float spread01)
    {
        float size = Mathf.Lerp(SpreadMinDegrees, SpreadMaxDegrees, Mathf.Clamp01(spread01));
        return side01 < .5f ? size : -size;
    }

    // The same, as short as it can be (the dock's weapon row has two short lines).
    public static string DockNote(int level) { return DockNote(0, level); }

    public static string DockNote(int ship, int level)
    {
        return level >= ShipWeaponUpgrades.MaxLevel ? "-25% DRONE CD" : null;
    }

    // The word the dock, the shop's offer and the Codex add to a step: what the floaties gain at that level (null: nothing).
    public static string StepNote(int level) { return StepNote(0, level); }

    public static string StepNote(int ship, int level)
    {
        return level >= ShipWeaponUpgrades.MaxLevel ? "-25% DRONE COOLDOWN" : null;
    }
}

// What a floatie's bullets can lock on to and how a seeker picks: the nearest valid thing in range (an enemy, an elite, the boss, a
// hazard, or a hostile projectile) inside the view. Nothing here allocates.
public static class FloatieAim
{
    public struct Pick
    {
        public Vector3 at;
        public Transform lockOn;
        public ClearTarget target;   // null for a hostile shot
    }

    public static bool Valid(ClearTarget t)
    {
        return ShipTargets.IsHazard(t) && !t.IsScenery;
    }

    public static bool InView(Vector3 p, float margin)
    {
        return ShipTargets.View(margin).Contains(p);
    }

    public static bool Nearest(Vector3 from, float range, out Pick pick)
    {
        pick = default(Pick);
        float bestSq = range * range;
        bool found = false;
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (!Valid(t)) continue;
            Vector3 p = t.transform.position;
            if (!InView(p, 0f)) continue;
            float sq = ((Vector2)(p - from)).sqrMagnitude;
            if (sq >= bestSq) continue;
            bestSq = sq;
            found = true;
            pick.at = p; pick.lockOn = t.transform; pick.target = t;
        }
        var shots = HostileShots.All;
        for (int i = 0; i < shots.Count; i++)
        {
            var s = shots[i];
            if (s == null || (Object)s == null || !s.ShotCollidable || s.ShotMass == HostileShots.Fixed) continue;
            Vector2 sp = s.ShotPosition;
            if (!InView(sp, 0f)) continue;
            float sq = (sp - (Vector2)from).sqrMagnitude;
            if (sq >= bestSq) continue;
            var c = s as Component;
            bestSq = sq;
            found = true;
            pick.at = sp; pick.lockOn = c != null ? c.transform : null; pick.target = null;
        }
        return found;
    }
}

// One small pooled bullet.
public class FloatieBullet : MonoBehaviour
{
    public const float MaxAge = 2.4f, ViewMargin = .5f;   // NOTHING HOMES: a bullet's heading never changes after it leaves (only a RICOCHET bounce turns it)
    // The drawing (BulletSprite): a 5 x 28 pixel sprite at 80 px a unit, ~.06 wide, a head ~.18 long and a fading tail ~.17 behind it. Its
    // pivot is the head's middle, so a bullet is placed by its head. Alpha stays high so it reads on every backdrop.
    const float Alpha = 1f;
    public const int HeadTop = 7;    // sprite rows above the pivot

    // Counters (tests, previews).
    public static int Launched, Landed, ShotDown, Wasted;

    SpriteRenderer body;
    int ship;
    Vector3 dir;
    float age;
    FloatieBattery owner;
    float pierceStreak;

    // The shot atoms on this bullet (ShotAtoms.cs). Plain: whole damage, nothing else.
    public struct Mods
    {
        public float damage;     // of a whole shot (elites / the boss take it as a share of the weight)
        public int pierce;       // more enemies it passes through
        public int bounces;      // side-edge ricochets left
        public int shards;       // level of SPLIT (0: none)
        public int arc;          // level of ARC (0: none)
        public bool shard;       // a split shard: small, short, never splits or zaps again
        public float speed, life;
        public ClearTarget ignore;   // a shard's parent: it starts inside it
        public int source;           // the floatie (0-based) that fired it: the battery's fair share counts bullets by it
        public int tier;
        public bool celebrated;
        public bool rapid;           // RAPID is active: a slightly longer tail
        public static Mods Plain { get { return new Mods { damage = 1f }; } }
    }
    Mods mods;
    float speed, maxAge;
    readonly ClearTarget[] passed = new ClearTarget[ShotAtomTuning.MaxPierce + 2];   // the parent a shard ignores + every enemy a PIERCE shot passes
    int passedCount;

    // Counters (tests, previews).
    public static int Pierced, Bounced, ShardsLaunched, ArcZaps;
    public Mods Applied => mods;
    public int PierceLeft => mods.pierce;
    public int BouncesLeft => mods.bounces;

    public bool Active { get; private set; }
    public int LaunchId { get; private set; }   // the global Launched count at this bullet's latest launch (a pooled bullet re-fired is a new shot)
    public Vector3 Heading => dir;
    public FloatieBattery Owner => owner;
    public int Source => mods.source;
    public Vector3 Velocity => dir * speed;
    public Color Tint => body != null ? body.color : Color.white;
    public SpriteRenderer Renderer => body;

    static Sprite pill;
    // Pixel art, white / grey so the weapon colour multiplies it: row 0 is the bottom. Head (rows 14..27): a bright core column, a body
    // column either side of it (70%), and a dark edge pixel outside that (the contrast on a light backdrop); behind it a 1-px tail that
    // fades out (and a faint second one for the first rows). No glow, no soft edge.
    public static Sprite BulletSprite()
    {
        if (pill != null) return pill;
        const int W = 5, H = 28;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        tex.hideFlags = HideFlags.HideAndDontSave;
        var clear = new Color(1f, 1f, 1f, 0f);
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) tex.SetPixel(x, y, clear);
        int top = H - 1;                       // the tip row
        for (int y = 14; y <= top; y++)
        {
            int fromTip = top - y;
            if (fromTip == 0) { tex.SetPixel(2, y, Color.white); continue; }                 // the tip: core only
            tex.SetPixel(2, y, Color.white);                                                   // core
            if (fromTip >= 1) { tex.SetPixel(1, y, new Color(.72f, .72f, .72f, 1f)); tex.SetPixel(3, y, new Color(.72f, .72f, .72f, 1f)); }
            if (fromTip >= 2 && y >= 15) { tex.SetPixel(0, y, new Color(.08f, .08f, .1f, .8f)); tex.SetPixel(4, y, new Color(.08f, .08f, .1f, .8f)); }
        }
        for (int y = 0; y < 14; y++)
        {
            float k = y / 13f;                               // 0 at the very end of the tail, 1 at the head
            tex.SetPixel(2, y, new Color(.85f, .85f, .85f, k * k * .75f));
            if (y >= 9) { float a = (y - 8) / 5f * .3f; tex.SetPixel(1, y, new Color(.7f, .7f, .7f, a)); tex.SetPixel(3, y, new Color(.7f, .7f, .7f, a)); }
        }
        tex.Apply(false, false);
        pill = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(.5f, 21f / H), 80f, 0, SpriteMeshType.FullRect);
        pill.hideFlags = HideFlags.HideAndDontSave;
        return pill;
    }

    const float ShardScale = .7f;
    public const int SortOrder = 74;   // above every ultimate effect (<= 72) and the hit bursts, under FloatieFx (75)

    public void Build()
    {
        body = gameObject.AddComponent<SpriteRenderer>();
        body.sprite = BulletSprite();
        body.sortingOrder = SortOrder;
        transform.localScale = Vector3.one;
        gameObject.SetActive(false);
    }

    public void Launch(FloatieBattery by, int shipId, Vector3 at, Vector3 heading, Color tint, FloatieAim.Pick lockOnPick)
    {
        Launch(by, shipId, at, heading, tint, lockOnPick, Mods.Plain);
    }

    public void Launch(FloatieBattery by, int shipId, Vector3 at, Vector3 heading, Color tint, FloatieAim.Pick lockOnPick, Mods withMods)
    {
        mods = withMods;
        if (mods.damage <= 0f) mods.damage = 1f;
        speed = mods.speed > 0f ? mods.speed : FloatieTuning.BulletSpeed;
        maxAge = mods.life > 0f ? mods.life : MaxAge;
        passedCount = 0;
        if (mods.ignore != null) passed[passedCount++] = mods.ignore;
        float k = (mods.shard ? ShardScale : 1f) * (1f + .1f * mods.tier) * (mods.celebrated ? 1.15f : 1f);
        // PIERCE draws a longer streak behind the head, RAPID a slightly longer tail; shards are small
        pierceStreak = mods.pierce > 0 ? 1.7f : mods.rapid ? 1.25f : 1f;
        transform.localScale = new Vector3(k, k * pierceStreak, 1f);
        owner = by;
        ship = shipId;
        dir = heading.sqrMagnitude > 1e-6f ? heading.normalized : Vector3.up;
        age = 0f;
        transform.position = new Vector3(at.x, at.y, 0f);
        Face();
        body.sprite = GunBoltArt.For(mods.tier);
        // a little lifted toward white so the core reads on dark and light backdrops alike (the dark edge pixel carries the light ones)
        float lift = mods.celebrated ? .5f : mods.pierce > 0 ? .3f : .12f;
        body.color = new Color(Mathf.Lerp(tint.r, 1f, lift), Mathf.Lerp(tint.g, 1f, lift), Mathf.Lerp(tint.b, 1f, lift), Alpha);
        Active = true;
        Launched++;
        LaunchId = Launched;
        gameObject.SetActive(true);
    }

    void Face()
    {
        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
    }

    public void Release()
    {
        Active = false;
        gameObject.SetActive(false);
    }

    // One step on the world's clock: fly straight (no homing, ever), then stop on the first thing along the step.
    public void Tick(float dt)
    {
        if (!Active || dt <= 0f) return;
        age += dt;
        Vector3 from = transform.position;

        Vector3 to = from + dir * (speed * dt);
        if (mods.bounces > 0) Ricochet(ref to);
        transform.position = to;
        Face();

        if (Resolve(from, to)) return;
        if (age > maxAge || !FloatieAim.InView(to, ViewMargin)) Release();
    }

    static bool Valid(ClearTarget t) { return FloatieAim.Valid(t); }

    // RICOCHET: past a side edge of the screen the shot is mirrored back in and loses one bounce.
    void Ricochet(ref Vector3 to)
    {
        Rect v = ShipTargets.View(0f);
        if (to.x < v.xMin && dir.x < 0f) { to.x = v.xMin + (v.xMin - to.x); dir.x = -dir.x; mods.bounces--; Bounced++; FloatieFx.Spark(FloatieFx.Kind.Bounce, new Vector3(v.xMin, to.y, 0f), dir, body.color); }
        else if (to.x > v.xMax && dir.x > 0f) { to.x = v.xMax - (to.x - v.xMax); dir.x = -dir.x; mods.bounces--; Bounced++; FloatieFx.Spark(FloatieFx.Kind.Bounce, new Vector3(v.xMax, to.y, 0f), dir, body.color); }
    }

    bool HasPassed(ClearTarget t)
    {
        for (int i = 0; i < passedCount; i++) if (passed[i] == t) return true;
        return false;
    }

    // The first hostile shot or hazard along the step: one bullet, one hit.
    bool Resolve(Vector3 from, Vector3 to)
    {
        float r = FloatieTuning.BulletRadius;
        Vector2 a = from, b = to, ab = b - a;
        float len = ab.sqrMagnitude;

        float shotAlong;
        var shot = HostileShots.FirstAlong(a, b, r, out shotAlong);

        ClearTarget best = null;
        float bestAlong = 2f;
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (!Valid(t) || HasPassed(t)) continue;
            Vector3 p = t.transform.position;
            float reach = r + t.Radius;
            if (HostileShots.SegmentDistanceSq(a, b, p) > reach * reach) continue;
            float along = len > 1e-8f ? Mathf.Clamp01(Vector2.Dot((Vector2)p - a, ab) / len) : 0f;
            if (along < bestAlong) { bestAlong = along; best = t; }
        }

        if (shot != null && shotAlong <= bestAlong)
        {
            if (HostileShots.ShootDown(shot)) { ShotDown++; FloatieFx.Spark(FloatieFx.Kind.Hit, transform.position, dir, body.color); Release(); return true; }
        }
        if (best == null) return false;
        Vector3 struckAt = best.transform.position;   // read before the hit: a kill destroys the target
        Strike(best);
        // PIERCE: it goes on through the next enemy (never twice through the same one)
        if (mods.pierce > 0 && passedCount < passed.Length)
        {
            mods.pierce--;
            passed[passedCount++] = best;
            Pierced++;
            FloatieFx.Spark(FloatieFx.Kind.Pierce, struckAt, dir, body.color);
            return false;
        }
        Release();
        return true;
    }

    // Lands the authored quarter-heart damage, then adds SPLIT shards and ARC sparks.
    void Strike(ClearTarget hit)
    {
        Vector3 at = hit.transform.position;
        Landed++;
        Vector3 impact = transform.position;
        bool nudge = hit.TryGetComponent(out EnemyHearts _);
        var struck = hit.gameObject;
        int before = GunImpactFeedback.Quarters(struck);
        bool dead = Damage(struck, ship, mods.damage, impact);
        int after = GunImpactFeedback.Quarters(struck);
        bool fullHeart = HeartDamage.WholeHearts(before) > HeartDamage.WholeHearts(after);
        bool killed = before > 0 && after == 0;
        FloatieFx.Spark(killed ? FloatieFx.Kind.Kill : fullHeart ? FloatieFx.Kind.HeartBreak : FloatieFx.Kind.Hit, impact, dir, body.color);
        GunImpactFeedback.Hit(fullHeart, killed);
        // a normal enemy that took the hit and lives on is pushed a hair along the shot
        if (nudge && !dead && hit != null && hit.enabled) FloatieFx.Nudge(hit.transform, dir);
        if (mods.shard) return;
        if (mods.shards > 0) { FloatieFx.Spark(FloatieFx.Kind.Split, at, dir, body.color); Split(at, hit); }
        if (mods.arc > 0) Zap(at, hit);
    }

    // One hit of `scale` hearts on `target`.
    static bool Damage(GameObject target, int ship, float scale, Vector3 blowFrom)
    {
        float budget = 1f, weight = HeartDamage.InQuarters(scale);
        ShipAttackHits.HitFrom = blowFrom;
        bool dead = ShipAttackHits.Hit(target, ship, weight, ref budget, HitKind.Drone);
        ShipAttackHits.HitFrom = null;
        return dead;
    }

    // SPLIT: small diagonal shards leave the struck enemy (a shard never splits, zaps or pierces).
    void Split(Vector3 at, ClearTarget parent)
    {
        int level = mods.shards;
        float[] angles = ShotAtomTuning.SplitAngles[level];
        for (int i = 0; i < angles.Length; i++)
        {
            var b = FloatieBullets.Take(false);
            if (b == null) return;   // at the cap (shards leave the volleys' reserve alone): the rest are not fired
            Vector3 heading = Quaternion.Euler(0f, 0f, angles[i]) * dir;
            var m = new Mods { damage = ShotAtomTuning.SplitDamage[level], shard = true, speed = ShotAtomTuning.ShardSpeed, life = ShotAtomTuning.ShardLife, tier = mods.tier, ignore = parent, source = mods.source };
            b.Launch(owner, ship, at, heading, body.color, default(FloatieAim.Pick), m);
            ShardsLaunched++;
        }
    }

    static readonly ClearTarget[] zapped = new ClearTarget[ShotAtomTuning.MaxArcJumps + 2];

    // ARC: a spark to the nearest other enemy in range, then on (the level's jumps), each one hit for a share of a shot.
    void Zap(Vector3 at, ClearTarget first)
    {
        int level = mods.arc;
        float range = ShotAtomTuning.ArcRange[level], scale = ShotAtomTuning.ArcDamage[level];
        int jumps = ShotAtomTuning.ArcJumps[level], count = 0;
        zapped[count++] = first;
        Vector3 from = at;
        for (int j = 0; j < jumps && count < zapped.Length; j++)
        {
            ClearTarget next = null;
            float bestSq = range * range;
            var live = ClearTarget.Live;
            for (int i = 0; i < live.Count; i++)
            {
                var t = live[i];
                if (!Valid(t) || !FloatieAim.InView(t.transform.position, 0f)) continue;
                bool used = false;
                for (int z = 0; z < count; z++) if (zapped[z] == t) { used = true; break; }
                if (used) continue;
                float sq = ((Vector2)(t.transform.position - from)).sqrMagnitude;
                if (sq >= bestSq) continue;
                bestSq = sq;
                next = t;
            }
            if (next == null) break;
            Vector3 to = next.transform.position;
            VoltArcs.Begin(from, to, VoltArcs.LinkLife * 1.4f, ShotAtomTuning.ArcCore, ShotAtomTuning.ArcOuter, ShotAtomTuning.ArcStar);
            zapped[count++] = next;
            ArcZaps++;
            Damage(next.gameObject, ship, scale, from);
            from = to;
        }
        for (int i = 0; i < count; i++) zapped[i] = null;
    }
}

// The pool of bullets: built once up to PoolSize, then reused. An over-cap shot is simply not fired.
public static class FloatieBullets
{
    static readonly System.Collections.Generic.List<FloatieBullet> pool = new System.Collections.Generic.List<FloatieBullet>(FloatieTuning.PoolSize);
    static Transform root;

    static Transform Root
    {
        get
        {
            if (root == null) { root = new GameObject("~Floaties").transform; pool.Clear(); }
            return root;
        }
    }

    public static int Size { get { Purge(); return pool.Count; } }
    public static System.Collections.Generic.List<FloatieBullet> All { get { Purge(); return pool; } }

    static void Purge()
    {
        if (root == null) { pool.Clear(); return; }
        for (int i = pool.Count - 1; i >= 0; i--) if (pool[i] == null) pool.RemoveAt(i);
    }

    public static int Active
    {
        get
        {
            Purge();
            int n = 0;
            for (int i = 0; i < pool.Count; i++) if (pool[i].Active) n++;
            return n;
        }
    }

    // Bullets a split's shards may never use: a drone's volley always finds room for at least this many (the fair-share rule).
    public const int VolleyReserve = DroneSquad.MaxSlots * 2;

    // A free bullet, or null at the cap (ours, or the attack pool's shared projectile cap). `primary` is a drone's volley bullet; the
    // shards a split makes (primary false) stop VolleyReserve short of the cap so a busy field of shards never starves the guns.
    public static FloatieBullet Take(bool primary = true)
    {
        var parent = Root;
        Purge();
        if (Active + AttackPool.ActiveProjectiles >= AttackPool.MaxCombinedProjectiles) return null;
        if (!primary && Active >= FloatieTuning.PoolSize - VolleyReserve) return null;
        for (int i = 0; i < pool.Count; i++) if (!pool[i].Active) return pool[i];
        if (pool.Count >= FloatieTuning.PoolSize) return null;
        var go = new GameObject("~floatie");
        go.transform.SetParent(parent, false);
        var b = go.AddComponent<FloatieBullet>();
        b.Build();
        pool.Add(b);
        return b;
    }

    public static void ReleaseAll(FloatieBattery owner = null)
    {
        Purge();
        for (int i = 0; i < pool.Count; i++)
            if (pool[i].Active && (owner == null || pool[i].Owner == owner)) pool[i].Release();
    }

    // Builds bullets up front so firing in play never instantiates.
    public static void Prebuild(int n)
    {
        var parent = Root;
        Purge();
        n = Mathf.Min(n, FloatieTuning.PoolSize);
        while (pool.Count < n)
        {
            var go = new GameObject("~floatie");
            go.transform.SetParent(parent, false);
            var b = go.AddComponent<FloatieBullet>();
            b.Build();
            pool.Add(b);
        }
    }
}
