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
//   glob   resin lobbed in a high arc (Lob) onto a marked spot: harmless
//          and untouchable in the air, it lands as a sticky pool that
//          rides the board (poolSeconds) and catches whatever touches it
// A def's shotBounces lets its shots glance off the side rails that many
// times (the Rimebreaker's frost shards) instead of breaking there.
// All drawn in the elite's shotColor with a shotCore centre -- magenta /
// violet / cyan, never the player's red.
public sealed class EliteShots
{
    public enum Kind { Bolt, Shard, Slag, Shell, Glob }
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
            case "glob": return Kind.Glob;
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
        if (dt > 0f) HostileShots.Resolve();   // shot vs shot (HostileShots)
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

public class EliteShot : MonoBehaviour, IHostileShot
{
    EliteShots pool;
    SpriteRenderer body, core, mark;
    SpriteRenderer glow;      // the visibility wrapper (HostileGlow)
    float glowBase;           // its local scale before the pulse
    Color glowTint;
    int ownerId;
    GameObject hitbox;
    CircleCollider2D hitCol;
    EliteShip owner;
    EliteDef def;
    Vector2 velocity;
    float age, radius, life;
    int pierce, bounces;
    // a lobbed glob: in the air until `landAt`, flying from `lobFrom` to `lobTo`
    bool airborne;
    // fired by a roster enemy (EnemyVolley): never hurts other hazards, and
    // may ride the board (`ride` x the scroll is added to its fall)
    bool rosterShot;
    float ride;
    float lobTime, lobTotal;
    Vector2 lobFrom, lobTo;

    public bool Active { get; private set; }
    public EliteShots.Kind Kind { get; private set; }
    public Vector2 Velocity => velocity;
    public Vector2 LaunchedAt { get; private set; }
    public EliteShip Owner => owner;
    public float Radius => radius;
    public GameObject Hitbox => hitbox;
    public bool Airborne => airborne;
    public bool RosterShot => rosterShot;
    public float Ride => ride;
    public float Age => age;
    public bool Pooled => Active && Kind == EliteShots.Kind.Glob && !airborne;
    public Vector2 LobTarget => lobTo;
    public int Bounced { get; private set; }
    public bool MarkShown => mark != null && mark.enabled;
    // Why it last left play: 0 none, 1 off screen / spent, 2 rail, 3 hitbox gone, 4 hit a hazard,
    // 5 broken by another shot or a player shot.
    public int EndReason { get; private set; }
    public SpriteRenderer Glow => glow;

    // IHostileShot (HostileShots: shot vs shot)
    public bool ShotCollidable => Active && !airborne && hitbox != null && hitCol != null && hitCol.enabled;
    public Vector2 ShotPosition => transform.position;
    public float ShotRadius => radius;
    public int ShotOwner => ownerId;
    public float ShotAge => age;
    public int ShotMass => Kind == EliteShots.Kind.Glob ? HostileShots.Fixed
                         : Kind == EliteShots.Kind.Slag || Kind == EliteShots.Kind.Shell ? HostileShots.Heavy
                         : HostileShots.Light;
    public Color ShotTint => def != null ? def.ShotColor : Color.white;

    public void ShotPop(Vector2 at)
    {
        if (!Active) return;
        if (def != null) EliteSystem.Fx.Sparks(at, def.ShotColor, 4);
        EndReason = 5;
        Recycle();
    }

    void OnDestroy() { HostileShots.Unregister(this); }

    void Pulse()
    {
        glow.transform.localScale = Vector3.one * (glowBase * HostileGlow.PulseScaleAt(age));
        var c = glowTint;
        c.a = HostileGlow.PulseAlphaAt(age);
        glow.color = c;
    }

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
        s.glow = HostileGlow.Attach(go.transform, HostileGlow.SortBehindShots);
        HostileShots.Register(s);
        var m = new GameObject("Mark");
        m.transform.SetParent(root, false);
        s.mark = m.AddComponent<SpriteRenderer>();
        s.mark.sortingOrder = 4;
        s.mark.enabled = false;
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
        airborne = false;
        rosterShot = false;
        ride = 0f;
        bounces = Mathf.Max(0, d.shotBounces);
        Bounced = 0;
        if (mark != null) mark.enabled = false;
        float size = Mathf.Max(.06f, d.shotSize);
        Sprite sprite;
        switch (kind)
        {
            case EliteShots.Kind.Shard: sprite = EliteFxArt.Shard; radius = size * .32f; life = 4f; pierce = 0; break;
            case EliteShots.Kind.Slag: sprite = EliteFxArt.Slag; radius = size * .42f; life = 7f; pierce = 0; break;
            case EliteShots.Kind.Shell: sprite = EliteFxArt.Shell; radius = size * .36f; life = 4f; pierce = 2; break;
            case EliteShots.Kind.Glob: sprite = EliteFxArt.Slag; radius = size * .4f; life = d.lobSeconds + d.poolSeconds; pierce = 0; break;
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
        hitCol.enabled = true;
        ownerId = from != null ? from.GetInstanceID() : 0;
        glowTint = HostileGlow.Tint(d.ShotColor);
        glowBase = HostileGlow.DiameterFor(size * HostileGlow.EliteShotBody) / k;
        Pulse();
        Active = true;
        gameObject.SetActive(true);
    }

    // Marks a just-fired shot as a roster enemy's (EnemyVolley): `source` is
    // its shooter (shots of one enemy's volley never clash with each other),
    // `rideBoard` the share of the board's scroll added to its fall.
    public void AsRosterShot(GameObject source, float rideBoard)
    {
        rosterShot = true;
        ride = rideBoard;
        ownerId = source != null ? source.GetInstanceID() : 0;
    }

    // Turns a just-fired glob into a lob onto `to` (world), landing in
    // `seconds`: no hitbox until it lands, a blinking ring marks the spot.
    public void Lob(Vector2 to, float seconds)
    {
        airborne = true;
        lobFrom = transform.position;
        lobTo = to;
        lobTime = 0f;
        lobTotal = Mathf.Max(.1f, seconds);
        velocity = (to - lobFrom) / lobTotal;
        hitCol.enabled = false;
        mark.sprite = EliteFxArt.Ring;
        mark.enabled = true;
        mark.transform.position = new Vector3(to.x, to.y, 0f);
        mark.transform.localScale = Vector3.one * def.shotSize * 2.4f / Mathf.Max(.01f, EliteFxArt.Ring.bounds.size.x);
        mark.color = def.ShotColor;
    }

    // The glob comes down: a flat sticky pool on the board.
    void Land()
    {
        airborne = false;
        Vector3 p = new Vector3(lobTo.x, lobTo.y, 0f);
        transform.position = p;
        transform.rotation = Quaternion.identity;
        body.sprite = EliteFxArt.Pool;
        core.sprite = EliteFxArt.Pool;
        float size = def.shotSize * 2.1f;
        float k = size / Mathf.Max(.01f, EliteFxArt.Pool.bounds.size.x);
        transform.localScale = Vector3.one * k;
        radius = size * .42f;
        hitCol.radius = radius / k;
        hitCol.enabled = true;
        glowBase = HostileGlow.DiameterFor(size * HostileGlow.PoolBody) / k;
        Pulse();
        mark.enabled = false;
        velocity = new Vector2(0f, -EliteSystem.Scroll);
        age = def.lobSeconds;
        EliteSystem.Fx.Sparks(lobTo, def.ShotColor, 5);
        Physics2D.SyncTransforms();
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
        Pulse();
        Vector3 p = transform.position;
        if (airborne)
        {
            // the spot rides the board; the glob arcs up and drops onto it
            lobTo.y -= EliteSystem.Scroll * dt;
            lobTime += dt;
            float k = Mathf.Clamp01(lobTime / lobTotal);
            Vector2 g = Vector2.Lerp(lobFrom, lobTo, k);
            float height = Mathf.Sin(k * Mathf.PI);
            transform.position = new Vector3(g.x, g.y + height * .35f, 0f);
            transform.localScale = Vector3.one * (def.shotSize / Mathf.Max(.01f, EliteFxArt.Slag.bounds.size.y)) * (1f + .9f * height);
            transform.rotation = Quaternion.Euler(0f, 0f, age * 240f);
            mark.transform.position = new Vector3(lobTo.x, lobTo.y, 0f);
            Color mc = def.ShotColor;
            mc.a = Mathf.FloorToInt(lobTime / ((k > .6f ? 2f : 4f) * EliteArt.Tick)) % 2 == 0 ? .9f : .35f;
            mark.color = mc;
            if (k >= 1f) Land();
            return;
        }
        if (Kind == EliteShots.Kind.Glob)
        {
            // a pool: rides the board, wobbling a little, then dries up
            velocity = new Vector2(0f, -EliteSystem.Scroll);
            float pulse = 1f + .08f * (Mathf.FloorToInt(age * 5f) % 2);
            core.transform.localScale = Vector3.one * .55f * pulse;
        }
        else if (Kind == EliteShots.Kind.Slag)
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
        if (ride != 0f) p.y -= EliteSystem.Scroll * ride * dt;
        transform.position = p;

        // the rails
        float edge = EliteSystem.RailEdge;
        if (Mathf.Abs(p.x) + radius > edge)
        {
            EliteSystem.Fx.Sparks(new Vector2(Mathf.Sign(p.x) * edge, p.y), def.ShotColor, 4);
            if (bounces > 0 && Mathf.Sign(velocity.x) == Mathf.Sign(p.x))
            {
                // glances off the rail, back across the board
                bounces--;
                Bounced++;
                velocity.x = -velocity.x * .85f;
                p.x = Mathf.Sign(p.x) * (edge - radius - .01f);
                transform.position = p;
                Face();
            }
            else if (bounces <= 0)
            {
                EndReason = 2;
                Recycle();
                return;
            }
        }

        // friendly fire
        Vector2 at = p;
        var live = ClearTarget.Live;
        // (a roster enemy's shot passes through other hazards: no friendly fire)
        for (int i = 0; !rosterShot && i < live.Count; i++)
        {
            var t = live[i];
            if (t == null || !t.isActiveAndEnabled || !ClearTarget.IsHazard(t.gameObject)) continue;
            if (FriendlyFire.Immune(t.gameObject)) continue;   // the boss is never hurt by friendly fire
            bool own = owner != null && t.gameObject == owner.gameObject;
            if (own && age < .35f) continue;
            float R = radius + t.Radius * .8f;
            if (((Vector2)t.transform.position - at).sqrMagnitude > R * R) continue;
            pool.CountFriendly();
            var elite = t.GetComponent<EliteShip>();
            if (elite != null)
            {
                EliteShip.HitBy = Kind == EliteShots.Kind.Glob ? (own ? "own resin pool" : "resin pool") : own ? "own shot" : "elite shot";
                elite.TakeHit(EliteDamage.FriendlyFire, p);
            }
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
        airborne = false;
        if (mark != null) mark.enabled = false;
        gameObject.SetActive(false);
    }
}
