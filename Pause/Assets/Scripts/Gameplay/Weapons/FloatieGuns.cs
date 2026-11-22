using UnityEngine;

// THE FLOATIES' SMALL GUN. Every ship's companion (UltimateGun, the "floatie" hovering beside the hull) also fires a small bullet
// by itself, and the ship's weapon level (ShipWeaponUpgrades.Level, 0..MaxLevel = colours bought) adds floaties:
//
//   level 0-1  1 floatie  (the UltimateGun itself)                      straight up the screen, every 1 s
//   level 2-3  2 floaties (+ a FloatieCompanion on the other side)      #2 fires up and angled left or right, every 1 s
//   level 4    3 floaties (+ a seeker below the first), all 25% faster  #1/#2 every 0.75 s, the SEEKER every 1.5 s
//
// While a boss fight is on (BossEncounter.Running, the fly-in included) every floatie waits 5 s between shots. Fire holds (the
// timers freeze, nothing is lost) while the ship's ultimate slides out / fires, the world is frozen (a pause), the pilot is dead,
// a world transition (planetfall / lift-off) runs, the boss's scripted entrance plays, and in the tutorial.
//
// A bullet is small, thin and low-glow, in the ship's own weapon colour, from a fixed pool (no allocation per shot). It stops on the
// FIRST thing it touches: a hostile projectile (popped, no score), a hazard (ShipAttackHits: score, dust, chain, codex like any
// weapon kill), an elite (a quarter of a heart a bullet: four bullets cost one heart through the elite's own hit rules) or the boss
// (a quarter of a hit through the boss's hit window, so its 0.25 s invulnerability still applies).
//
// Everything tunable is a constant in FloatieTuning.
public static class FloatieTuning
{
    public const int MaxFloaties = 3;
    public const float NormalCooldown = 1f;        // floatie #1 and #2, seconds
    public const float SeekerCooldown = 2f;        // floatie #3 (it only exists at the final level, where the cut applies: 1.5 s)
    public const float BossCooldown = 5f;          // every floatie while a boss is active
    public const float FinalLevelCut = .25f;       // the final level's cooldown cut
    public const float SpreadMinDegrees = 15f, SpreadMaxDegrees = 30f;   // floatie #2's random angle off straight up, left or right
    public const float SeekerRange = 6f;           // world units from the seeker's muzzle
    public const float BulletSpeed = 9f;           // world units a second
    public const float BulletRadius = .09f;        // hit radius
    public const float EliteWeight = .25f;         // of a heart (the elite's hit rules bank it)
    public const float BossWeight = .25f;          // of a hit on the boss (its hit window and banking apply)
    public const int PoolSize = 16;                // bullets alive at once, at most

    public static int CountAt(int level)
    {
        return level >= ShipWeaponUpgrades.MaxLevel ? 3 : level >= 2 ? 2 : 1;
    }

    public static bool IsFinal(int level) { return level >= ShipWeaponUpgrades.MaxLevel; }

    // Seconds between floatie `index` (0-based) shots at `level`.
    public static float Cooldown(int index, int level, bool bossActive)
    {
        if (bossActive) return BossCooldown;
        float baseSeconds = index >= 2 ? SeekerCooldown : NormalCooldown;
        return IsFinal(level) ? baseSeconds * (1f - FinalLevelCut) : baseSeconds;
    }

    // Floatie #2's angle off straight up (degrees, + is to the left): `side01` < .5 left, otherwise right; `spread01` 0..1 picks the size.
    public static float SpreadAngle(float side01, float spread01)
    {
        float size = Mathf.Lerp(SpreadMinDegrees, SpreadMaxDegrees, Mathf.Clamp01(spread01));
        return side01 < .5f ? size : -size;
    }

    // The same, as short as it can be (the dock's weapon row has two short lines).
    public static string DockNote(int level)
    {
        if (level == 2) return "+1 FLOATIE";
        if (level >= ShipWeaponUpgrades.MaxLevel) return "+SEEKER, -25% CD";
        return null;
    }

    // The word the dock, the shop's offer and the Codex add to a step: what the floaties gain at that level (null: nothing).
    public static string StepNote(int level)
    {
        if (level == 2) return "+1 FLOATIE";
        if (level >= ShipWeaponUpgrades.MaxLevel) return "+1 SEEKER FLOATIE, -25% FLOATIE CD";
        return null;
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

// The quarter-hearts an elite has taken from floatie bullets: every FloatieTuning.EliteWeight a bullet adds, and a whole heart lands
// (through the elite's own hit rules) when a quarter-heart sum reaches one. Elites are mini-bosses, one at a time, so a few slots do.
public static class FloatieBank
{
    const int Slots = 4;
    static readonly EliteShip[] owners = new EliteShip[Slots];
    static readonly float[] banked = new float[Slots];

    public static float Of(EliteShip e)
    {
        for (int i = 0; i < Slots; i++) if (owners[i] == e && e != null) return banked[i];
        return 0f;
    }

    // Adds `weight`; true when a whole heart is now due (the bank then keeps the remainder).
    public static bool Add(EliteShip e, float weight)
    {
        int slot = -1;
        for (int i = 0; i < Slots; i++)
        {
            if (owners[i] == null) { if (slot < 0) slot = i; continue; }
            if (owners[i] == e) { slot = i; break; }
        }
        if (slot < 0) slot = 0;
        if (owners[slot] != e) { owners[slot] = e; banked[slot] = 0f; }
        banked[slot] += weight;
        if (banked[slot] < 1f - 1e-4f) return false;
        banked[slot] -= 1f;
        return true;
    }

    public static void Clear()
    {
        for (int i = 0; i < Slots; i++) { owners[i] = null; banked[i] = 0f; }
    }
}

// One small pooled bullet.
public class FloatieBullet : MonoBehaviour
{
    public const float MaxAge = 2.4f, TurnDegreesPerSecond = 200f, ViewMargin = .5f;
    // The size of the drawing in world units (a thin pill), and its alpha.
    const float Width = .06f, Length = .2f, Alpha = .85f;

    // Counters (tests, previews).
    public static int Launched, Landed, ShotDown, Wasted;

    SpriteRenderer body;
    int ship;
    Vector3 dir;
    float age;
    Transform lockOn;
    ClearTarget lockTarget;
    FloatieBattery owner;

    public bool Active { get; private set; }
    public Vector3 Heading => dir;
    public FloatieBattery Owner => owner;
    public Color Tint => body != null ? body.color : Color.white;
    public SpriteRenderer Renderer => body;

    static Sprite pill;
    static Sprite Pill()
    {
        if (pill != null) return pill;
        // 3 x 8 pixels: a rounded pill, the middle column solid, the edges fainter (crisp, no filtering)
        var tex = new Texture2D(3, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        tex.hideFlags = HideFlags.HideAndDontSave;
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 3; x++)
            {
                bool tip = y == 0 || y == 7;
                float a = x == 1 ? 1f : tip ? 0f : .55f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply(false, true);
        pill = Sprite.Create(tex, new Rect(0, 0, 3, 8), new Vector2(.5f, .5f), 40f);
        pill.hideFlags = HideFlags.HideAndDontSave;
        return pill;
    }

    public void Build()
    {
        body = gameObject.AddComponent<SpriteRenderer>();
        body.sprite = Pill();
        body.sortingOrder = 71;
        transform.localScale = new Vector3(Width / (3f / 40f), Length / (8f / 40f), 1f);
        gameObject.SetActive(false);
    }

    public void Launch(FloatieBattery by, int shipId, Vector3 at, Vector3 heading, Color tint, FloatieAim.Pick lockOnPick)
    {
        owner = by;
        ship = shipId;
        dir = heading.sqrMagnitude > 1e-6f ? heading.normalized : Vector3.up;
        age = 0f;
        lockOn = lockOnPick.lockOn;
        lockTarget = lockOnPick.target;
        transform.position = new Vector3(at.x, at.y, 0f);
        Face();
        if (body.sprite == null) body.sprite = Pill();
        body.color = new Color(tint.r, tint.g, tint.b, Alpha);
        Active = true;
        Launched++;
        gameObject.SetActive(true);
    }

    void Face()
    {
        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
    }

    public void Release()
    {
        Active = false;
        lockOn = null;
        lockTarget = null;
        gameObject.SetActive(false);
    }

    // One step on the world's clock: steer on a seeker's lock, fly, then stop on the first thing along the step.
    public void Tick(float dt)
    {
        if (!Active || dt <= 0f) return;
        age += dt;
        Vector3 from = transform.position;

        if (lockOn != null)
        {
            bool alive = lockTarget != null ? Valid(lockTarget) : lockOn.gameObject.activeInHierarchy;
            if (!alive) { lockOn = null; lockTarget = null; }
            else dir = PowerFx.SteerHeading(dir, lockOn.position - from, TurnDegreesPerSecond, dt);
        }
        Vector3 to = from + dir * (FloatieTuning.BulletSpeed * dt);
        transform.position = to;
        Face();

        if (Resolve(from, to)) return;
        if (age > MaxAge || !FloatieAim.InView(to, ViewMargin)) Release();
    }

    static bool Valid(ClearTarget t) { return FloatieAim.Valid(t); }

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
            if (!Valid(t)) continue;
            Vector3 p = t.transform.position;
            float reach = r + t.Radius;
            if (HostileShots.SegmentDistanceSq(a, b, p) > reach * reach) continue;
            float along = len > 1e-8f ? Mathf.Clamp01(Vector2.Dot((Vector2)p - a, ab) / len) : 0f;
            if (along < bestAlong) { bestAlong = along; best = t; }
        }

        if (shot != null && shotAlong <= bestAlong)
        {
            if (HostileShots.ShootDown(shot)) { ShotDown++; Release(); return true; }
        }
        if (best == null) return false;
        Strike(best.gameObject);
        Release();
        return true;
    }

    // Lands on `target`: whole kills for the small things, a quarter of a heart on an elite and a quarter of a hit on the boss.
    void Strike(GameObject target)
    {
        float budget = 1f, weight = 1f;
        if (target.TryGetComponent(out EliteShip elite))
        {
            weight = 1f;
            if (!FloatieBank.Add(elite, FloatieTuning.EliteWeight)) { Landed++; return; }   // banked: the heart follows with the fourth
        }
        else if (target.TryGetComponent(out BossTarget _)) weight = FloatieTuning.BossWeight;
        Landed++;
        ShipAttackHits.Hit(target, ship, weight, ref budget);
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

    // A free bullet, or null at the cap (ours, or the attack pool's shared projectile cap).
    public static FloatieBullet Take()
    {
        var parent = Root;
        Purge();
        if (Active + AttackPool.ActiveProjectiles >= AttackPool.MaxProjectiles) return null;
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
