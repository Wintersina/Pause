using System.Collections.Generic;
using UnityEngine;

// Boss projectiles and lane beams, pooled and bounded.
//
// Damage goes through the game's existing rules, untouched: each projectile
// carries a child "hitbox" tagged "Enimey" with a trigger collider, so
// collisionDetection treats a hit exactly like any enemy -- a life lost (or
// death on the last), or absorbed and destroyed under the blue atom's
// shield. The same tag makes the hitboxes CinematicClear targets, so the
// ultimate shoots them down too. Whatever destroys a hitbox only destroys
// that child: the pooled visual notices on its next step and recycles, and
// the next launch builds a fresh hitbox. Nothing else is allocated per
// frame; the encounter steps every live object from one loop.
//
// Movement uses the world's time (dt = real dt * timeScale), so a frozen
// world (finger up, the intro, death) freezes every shot and beam in place.
public static class BossHitbox
{
    public const string Tag = "Enimey";

    public static GameObject Circle(Transform parent, string name, float radius)
    {
        var go = Child(parent, name);
        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = radius;
        return go;
    }

    public static GameObject Box(Transform parent, string name, Vector2 size)
    {
        var go = Child(parent, name);
        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = size;
        return go;
    }

    static GameObject Child(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.tag = Tag;
        go.transform.SetParent(parent, false);
        return go;
    }
}

public class BossProjectile : MonoBehaviour
{
    SpriteRenderer sr;
    GameObject hitbox;
    BossDef boss;
    BossShotStyle style;
    Vector2 velocity;
    float age;

    public bool Active { get; private set; }
    public Vector2 Velocity => velocity;
    public GameObject Hitbox => hitbox;

    public static BossProjectile Create(Transform root)
    {
        var go = new GameObject("BossShot");
        go.transform.SetParent(root, false);
        var p = go.AddComponent<BossProjectile>();
        p.sr = go.AddComponent<SpriteRenderer>();
        p.sr.sortingOrder = 30;
        go.SetActive(false);
        return p;
    }

    public void Launch(BossDef def, BossShotStyle shotStyle, Vector3 at, Vector2 v)
    {
        boss = def;
        style = shotStyle;
        velocity = v;
        age = 0f;
        transform.position = new Vector3(at.x, at.y, 0f);
        float size = style == BossShotStyle.Bolt ? BossConfig.BoltWorldSize : BossConfig.ShardWorldSize;
        transform.localScale = Vector3.one * size;
        // Art points down the screen; turn it to face along its velocity.
        float deg = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg + 90f;
        transform.rotation = Quaternion.Euler(0f, 0f, deg);
        if (hitbox == null)
        {
            float r = style == BossShotStyle.Bolt ? BossConfig.BoltHitRadius : BossConfig.ShardHitRadius;
            // Collider radius is in local units; the root is scaled to the art size.
            hitbox = BossHitbox.Circle(transform, "BossShotHit", r / size);
        }
        else
        {
            var col = hitbox.GetComponent<CircleCollider2D>();
            float r = style == BossShotStyle.Bolt ? BossConfig.BoltHitRadius : BossConfig.ShardHitRadius;
            if (col != null) col.radius = r / size;
        }
        Active = true;
        gameObject.SetActive(true);
        sr.sprite = BossArt.Shot(boss, FirstCell);
    }

    int FirstCell => style == BossShotStyle.Bolt ? BossArt.Bolt0 : BossArt.Shard0;

    public void Step(float dt)
    {
        if (!Active) return;
        // The player or the ultimate destroyed the hitbox: this shot is spent.
        if (hitbox == null) { Recycle(); return; }
        if (dt <= 0f) return;

        age += dt;
        Vector3 p = transform.position;
        p.x += velocity.x * dt;
        p.y += velocity.y * dt;
        transform.position = p;
        int frame = BossArt.FrameAt(BossArt.ShotTicks, age, true);
        sr.sprite = BossArt.Shot(boss, FirstCell + frame);

        if (p.y < -6.5f || p.y > 7.5f || Mathf.Abs(p.x) > 4.2f) Recycle();
    }

    public void Recycle()
    {
        Active = false;
        gameObject.SetActive(false);
    }
}

// A telegraphed lane: a flashing stripe for tellSeconds, then a beam down
// the whole column for holdSeconds. Its hitbox only exists while the beam
// is live, so the telegraph itself never hurts.
public class BossBeam : MonoBehaviour
{
    SpriteRenderer telegraph, beam;
    GameObject hitbox;
    BossDef boss;
    float tellLeft, holdLeft, age, width, top;
    bool live;

    public bool Active { get; private set; }
    public bool Live => Active && live;
    public bool Telegraphing => Active && !live;
    public GameObject Hitbox => hitbox;
    public float X => transform.position.x;

    public static BossBeam Create(Transform root)
    {
        var go = new GameObject("BossLane");
        go.transform.SetParent(root, false);
        var b = go.AddComponent<BossBeam>();
        b.telegraph = Part(go.transform, "Telegraph", 24);
        b.beam = Part(go.transform, "Beam", 26);
        go.SetActive(false);
        return b;
    }

    static SpriteRenderer Part(Transform parent, string name, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = order;
        return sr;
    }

    public void Begin(BossDef def, float x, float laneWidth, float topY, float tellSeconds, float holdSeconds)
    {
        boss = def;
        width = laneWidth;
        top = topY;
        tellLeft = Mathf.Max(0f, tellSeconds);
        holdLeft = Mathf.Max(.05f, holdSeconds);
        age = 0f;
        live = false;
        transform.position = new Vector3(x, 0f, 0f);
        transform.localScale = Vector3.one;
        if (hitbox != null) { BossUtil.Kill(hitbox); hitbox = null; }

        const float bottom = -5.6f;
        float height = top - bottom;
        float mid = (top + bottom) * .5f;
        telegraph.sprite = BossArt.Shot(boss, BossArt.Telegraph);
        telegraph.transform.localPosition = new Vector3(0f, mid, 0f);
        telegraph.transform.localScale = new Vector3(width, height, 1f);
        telegraph.enabled = true;
        beam.sprite = BossArt.Shot(boss, BossArt.Beam0);
        beam.transform.localPosition = new Vector3(0f, mid, 0f);
        beam.transform.localScale = new Vector3(width, height, 1f);
        beam.enabled = false;

        Active = true;
        gameObject.SetActive(true);
    }

    public void Step(float dt)
    {
        if (!Active || dt <= 0f) return;
        age += dt;
        if (!live)
        {
            tellLeft -= dt;
            // On threes: a hard blink, never a fade.
            int blink = Mathf.FloorToInt(age / (BossArt.TelegraphBlinkTicks * BossArt.Tick));
            telegraph.enabled = blink % 2 == 0;
            if (tellLeft <= 0f) Ignite();
            return;
        }

        holdLeft -= dt;
        int frame = BossArt.FrameAt(BossArt.BeamTicks, age, true);
        beam.sprite = BossArt.Shot(boss, BossArt.Beam0 + frame);
        if (holdLeft <= 0f) Recycle();
    }

    void Ignite()
    {
        live = true;
        age = 0f;
        telegraph.enabled = false;
        beam.enabled = true;
        const float bottom = -5.6f;
        hitbox = BossHitbox.Box(transform, "BossLaneHit",
            new Vector2(width * BossConfig.LaneHitFraction, top - bottom));
        hitbox.transform.localPosition = new Vector3(0f, (top + bottom) * .5f, 0f);
    }

    public void Recycle()
    {
        Active = false;
        live = false;
        if (hitbox != null) { BossUtil.Kill(hitbox); hitbox = null; }
        gameObject.SetActive(false);
    }
}

public sealed class BossProjectilePool
{
    readonly List<BossProjectile> shots = new List<BossProjectile>();
    readonly List<BossBeam> beams = new List<BossBeam>();
    readonly int maxShots, maxBeams;
    GameObject root;

    public BossProjectilePool(int maxShots, int maxBeams)
    {
        this.maxShots = Mathf.Max(1, maxShots);
        this.maxBeams = Mathf.Max(1, maxBeams);
        root = new GameObject("~BossProjectiles");
    }

    public int Capacity => maxShots;
    public int BeamCapacity => maxBeams;
    public int Created => shots.Count;
    public int BeamsCreated => beams.Count;
    public IReadOnlyList<BossProjectile> Shots => shots;
    public IReadOnlyList<BossBeam> Beams => beams;

    public int ActiveShots
    {
        get
        {
            int n = 0;
            for (int i = 0; i < shots.Count; i++) if (shots[i] != null && shots[i].Active) n++;
            return n;
        }
    }

    public int ActiveBeams
    {
        get
        {
            int n = 0;
            for (int i = 0; i < beams.Count; i++) if (beams[i] != null && beams[i].Active) n++;
            return n;
        }
    }

    // Null when the pool is spent: a full screen simply skips a shot rather
    // than growing without bound.
    public BossProjectile Fire(BossDef boss, BossShotStyle style, Vector3 at, Vector2 velocity)
    {
        BossProjectile p = null;
        for (int i = 0; i < shots.Count; i++)
            if (shots[i] != null && !shots[i].Active) { p = shots[i]; break; }
        if (p == null)
        {
            if (shots.Count >= maxShots || root == null) return null;
            p = BossProjectile.Create(root.transform);
            shots.Add(p);
        }
        p.Launch(boss, style, at, velocity);
        return p;
    }

    public BossBeam Lane(BossDef boss, float x, float width, float top, float tell, float hold)
    {
        BossBeam b = null;
        for (int i = 0; i < beams.Count; i++)
            if (beams[i] != null && !beams[i].Active) { b = beams[i]; break; }
        if (b == null)
        {
            if (beams.Count >= maxBeams || root == null) return null;
            b = BossBeam.Create(root.transform);
            beams.Add(b);
        }
        b.Begin(boss, x, width, top, tell, hold);
        return b;
    }

    public void Step(float dt)
    {
        for (int i = 0; i < shots.Count; i++) if (shots[i] != null) shots[i].Step(dt);
        for (int i = 0; i < beams.Count; i++) if (beams[i] != null) beams[i].Step(dt);
    }

    public void RecycleAll()
    {
        for (int i = 0; i < shots.Count; i++) if (shots[i] != null && shots[i].Active) shots[i].Recycle();
        for (int i = 0; i < beams.Count; i++) if (beams[i] != null && beams[i].Active) beams[i].Recycle();
    }

    public void Dispose()
    {
        if (root != null) BossUtil.Kill(root);
        root = null;
        shots.Clear();
        beams.Clear();
    }

    public GameObject Root => root;
}
