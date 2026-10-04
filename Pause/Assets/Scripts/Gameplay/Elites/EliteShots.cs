using System.Collections.Generic;
using UnityEngine;

// The elites' shots, pooled and bounded (MaxShots), stepped by EliteSystem.
//
// Hitting the pilot goes through the game's normal rules: each live shot
// carries a child hitbox tagged "Enimey" with a trigger collider (named
// HitboxName), so collisionDetection treats it like any enemy -- a heart
// lost, or absorbed under a shield (EliteShip.ShieldRam erases it). The
// hitbox is an IShipAttackTarget, so a player weapon that touches it just
// shoots it down (no score), and a blink landing on it erases it
// (EliteShip.TeleportStrike).
//
// FRIENDLY FIRE: a shot also hits every other hazard on the board -- rocks,
// enemies, rail mines, other elites (and its own ship once it has cleared
// the muzzle). A hazard it hits is destroyed with its blast but pays the
// pilot nothing (EliteShip.FriendlyKill); an elite it hits loses a heart.
//
// Kinds (EliteDef.shotKind):
//   bolt   a fast neon capsule
//   shard  a small diamond (fans)
//   slag   a big molten blob that sinks down the board, lingering
//   shell  the siege shell: big, fast, pierces two hazards
// All drawn in the elite's shotColor with a shotCore centre -- magenta /
// violet / cyan, never the player's red.
public sealed class EliteShots
{
    public enum Kind { Bolt, Shard, Slag, Shell }
    public const int MaxShots = 48;
    public const string HitboxName = "EliteShotHit";

    readonly List<EliteShot> shots = new List<EliteShot>(MaxShots);
    readonly Transform root;

    public EliteShots(Transform parent)
    {
        root = new GameObject("~EliteShots").transform;
        root.SetParent(parent, false);
        // built up front: firing never allocates
        while (shots.Count < MaxShots) shots.Add(EliteShot.Create(root, this));
    }

    public bool Alive => root != null;
    public IReadOnlyList<EliteShot> All => shots;
    public int Launched { get; private set; }
    public int FriendlyHits { get; private set; }

    public int ActiveCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < shots.Count; i++) if (shots[i] != null && shots[i].Active) n++;
            return n;
        }
    }

    public static Kind KindOf(string s)
    {
        switch (s)
        {
            case "shard": return Kind.Shard;
            case "slag": return Kind.Slag;
            case "shell": return Kind.Shell;
            default: return Kind.Bolt;
        }
    }

    // Null when the pool is spent (a busy screen skips a shot).
    public EliteShot Fire(EliteShip owner, EliteDef def, Kind kind, Vector2 at, Vector2 velocity)
    {
        EliteShot s = null;
        for (int i = 0; i < shots.Count; i++)
            if (shots[i] != null && !shots[i].Active) { s = shots[i]; break; }
        if (s == null)
        {
            if (shots.Count >= MaxShots || root == null) return null;
            s = EliteShot.Create(root, this);
            shots.Add(s);
        }
        s.Launch(owner, def, kind, at, velocity);
        Launched++;
        return s;
    }

    public void Step(float dt)
    {
        for (int i = 0; i < shots.Count; i++) if (shots[i] != null && shots[i].Active) shots[i].Step(dt);
    }

    internal void CountFriendly() { FriendlyHits++; }

    // A blink or a shield ate this hitbox: the shot is gone. False if `go`
    // isn't an elite shot's hitbox.
    public static bool EraseHitbox(GameObject go)
    {
        if (go == null) return false;
        var hb = go.GetComponent<EliteShotHitbox>();
        if (hb == null) return false;
        if (hb.shot != null) hb.shot.Recycle();
        return true;
    }
}

// The hitbox child: a player weapon touching it shoots it down.
public class EliteShotHitbox : MonoBehaviour, IShipAttackTarget
{
    public EliteShot shot;

    public void TakeShipAttack(int ship, float weight, Vector3 at)
    {
        if (shot != null) shot.Recycle();
    }
}

public class EliteShot : MonoBehaviour
{
    EliteShots pool;
    SpriteRenderer body, core;
    GameObject hitbox;
    CircleCollider2D hitCol;
    EliteShip owner;
    EliteDef def;
    Vector2 velocity;
    float age, radius, life;
    int pierce;

    public bool Active { get; private set; }
    public EliteShots.Kind Kind { get; private set; }
    public Vector2 Velocity => velocity;
    public Vector2 LaunchedAt { get; private set; }
    public EliteShip Owner => owner;
    public float Radius => radius;
    public GameObject Hitbox => hitbox;
    // Why it last left play: 0 none, 1 off screen / spent, 2 rail, 3 hitbox gone, 4 hit a hazard.
    public int EndReason { get; private set; }

    public static EliteShot Create(Transform root, EliteShots owner)
    {
        var go = new GameObject("EliteShot");
        go.transform.SetParent(root, false);
        var s = go.AddComponent<EliteShot>();
        s.pool = owner;
        s.body = go.AddComponent<SpriteRenderer>();
        s.body.sortingOrder = 30;
        var c = new GameObject("Core");
        c.transform.SetParent(go.transform, false);
        s.core = c.AddComponent<SpriteRenderer>();
        s.core.sortingOrder = 31;
        s.EnsureHitbox();
        go.SetActive(false);
        return s;
    }

    public void Launch(EliteShip from, EliteDef d, EliteShots.Kind kind, Vector2 at, Vector2 v)
    {
        owner = from;
        def = d;
        Kind = kind;
        velocity = v;
        age = 0f;
        EndReason = 0;
        LaunchedAt = at;
        float size = Mathf.Max(.06f, d.shotSize);
        Sprite sprite;
        switch (kind)
        {
            case EliteShots.Kind.Shard: sprite = EliteFxArt.Shard; radius = size * .32f; life = 4f; pierce = 0; break;
            case EliteShots.Kind.Slag: sprite = EliteFxArt.Slag; radius = size * .42f; life = 7f; pierce = 0; break;
            case EliteShots.Kind.Shell: sprite = EliteFxArt.Shell; radius = size * .36f; life = 4f; pierce = 2; break;
            default: sprite = EliteFxArt.Bolt; radius = size * .3f; life = 4f; pierce = 0; break;
        }
        body.sprite = sprite;
        core.sprite = sprite;
        body.color = d.ShotColor;
        core.color = d.ShotCore;
        core.transform.localScale = Vector3.one * .5f;
        float k = size / Mathf.Max(.01f, sprite.bounds.size.y);
        transform.localScale = Vector3.one * k;
        transform.position = new Vector3(at.x, at.y, 0f);
        Face();
        EnsureHitbox();
        hitCol.radius = radius / k;
        Active = true;
        gameObject.SetActive(true);
    }

    void EnsureHitbox()
    {
        if (hitbox != null) return;
        hitbox = new GameObject(EliteShots.HitboxName);
        hitbox.tag = "Enimey";
        hitbox.transform.SetParent(transform, false);
        hitCol = hitbox.AddComponent<CircleCollider2D>();
        hitCol.isTrigger = true;
        hitbox.AddComponent<EliteShotHitbox>().shot = this;
    }

    void Face()
    {
        if (velocity.sqrMagnitude < 1e-6f) return;
        float deg = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg - 90f;
        transform.rotation = Quaternion.Euler(0f, 0f, deg);
    }

    public void Step(float dt)
    {
        if (!Active) return;
        if (hitbox == null) { EndReason = 3; Recycle(); return; }
        if (dt <= 0f) return;
        age += dt;
        Vector3 p = transform.position;
        if (Kind == EliteShots.Kind.Slag)
        {
            // sinks with the board, slowly, wobbling
            velocity.y = Mathf.MoveTowards(velocity.y, -EliteSystem.Scroll * .5f - .6f, 2f * dt);
            velocity.x *= 1f - Mathf.Min(1f, 1.5f * dt);
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(age * 6f) * 12f);
            float pulse = 1f + .08f * (Mathf.FloorToInt(age * 8f) % 2);
            core.transform.localScale = Vector3.one * .5f * pulse;
        }
        p.x += velocity.x * dt;
        p.y += velocity.y * dt;
        transform.position = p;

        // the rails
        float edge = EliteSystem.RailEdge;
        if (Mathf.Abs(p.x) + radius > edge)
        {
            EliteSystem.Fx.Sparks(new Vector2(Mathf.Sign(p.x) * edge, p.y), def.ShotColor, 4);
            EndReason = 2;
            Recycle();
            return;
        }

        // friendly fire
        Vector2 at = p;
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (t == null || !t.isActiveAndEnabled || !ClearTarget.IsHazard(t.gameObject)) continue;
            bool own = owner != null && t.gameObject == owner.gameObject;
            if (own && age < .35f) continue;
            float R = radius + t.Radius * .8f;
            if (((Vector2)t.transform.position - at).sqrMagnitude > R * R) continue;
            pool.CountFriendly();
            var elite = t.GetComponent<EliteShip>();
            if (elite != null) elite.TakeHit(EliteDamage.FriendlyFire, p);
            else EliteShip.FriendlyKill(t.gameObject);
            if (pierce-- <= 0) { EndReason = 4; Recycle(); return; }
            break;   // the registry may have changed
        }

        if (age > life || p.y < EliteSystem.ViewBottom - 1f || p.y > EliteSystem.ViewTop + 1.5f)
        {
            EndReason = 1;
            Recycle();
        }
    }

    public void Recycle()
    {
        if (!Active) return;
        Active = false;
        gameObject.SetActive(false);
    }
}
