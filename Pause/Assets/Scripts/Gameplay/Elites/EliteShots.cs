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
// FRIENDLY FIRE (hostile fire, FriendlyFire.HostileHit): a shot -- an
// elite's or a roster enemy's -- also hits every other hazard on the board:
// rocks, enemies, rail mines, other elites; never its own shooter, never a
// target still inside its spawn-in protection. A hazard it hits is destroyed
// with its blast but pays the pilot nothing; an elite it hits loses a heart.
//
// Kinds (EliteDef.shotKind):
//   bolt   a fast neon capsule
//   shard  a small diamond (fans)
//   slag   a big molten blob that sinks down the board, lingering
//   shell  the siege shell: big, fast, pierces two hazards
//   glob   resin lobbed in a high arc (Lob) onto a marked spot: harmless
//          and untouchable in the air, it lands as a sticky pool that
//          rides the board (poolSeconds) and catches whatever touches it
//   slab   (floe_cast) a block of floating ice: glides out of its chute onto
//          a spot on a row, then rides the board drifting sideways,
//          glancing off the rails; soaks hazardArmour player hits (it
//          blocks shots) and hurts on touch
//   orb    (frost_bloom) a slow cryo orb on a fuse (a blinking ring round
//          it): at zero it bursts into a ring of hazardCount shards. Shot
//          down first, it just pops
// Any kind can be slung (Sling, the Singularity Hauler's gravity_sling):
// it curves past a bend point through a ringed spot on the board, deadly
// all the way, then flies straight on.
// A def's shotBounces lets its shots glance off the side rails that many
// times (the Rimebreaker's frost shards) instead of breaking there.
// All drawn in the hostile family (HostileShotPalette: the def's shotColor
// pulled to magenta-pink, a pink-white flickering core), as arrows, arrowheads
// and spiked mines (EliteFxArt) -- never an atom's colour or shape. Was:
// the elite's shotColor with a shotCore centre -- magenta /
// violet / cyan, never the player's red.
public sealed class EliteShots
{
    public enum Kind { Bolt, Shard, Slag, Shell, Glob, Slab, Orb }
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
            case "slab": return Kind.Slab;
            case "orb": return Kind.Orb;
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
        if (dt > 0f) FriendlyFire.HostileStep();   // the kill cap's step in edit mode
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
        if (shot != null) shot.Struck();
    }
}

public class EliteShot : MonoBehaviour, IHostileShot
{
    EliteShots pool;
    SpriteRenderer body, core, mark;
    SpriteRenderer glow;      // the visibility outline (ShotOutline): hugs the drawing, never a round halo
    Color glowTint;
    // the shot's colours: the def's, pulled into the hostile family (HostileShotPalette)
    Color tint = Color.white, coreTint = Color.white;
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
    // fired by a roster enemy (EnemyVolley): may ride the board (`ride` x
    // the scroll is added to its fall); `shooter` is never hurt by it
    bool rosterShot;
    GameObject shooter;
    float ride;
    float lobTime, lobTotal;
    Vector2 lobFrom, lobTo;
    // a slung shot (gravity_sling): on a curve from `slingFrom` past `slingBend`
    // through `slingTo` (all riding the board), dangerous all the way
    bool slung;
    float slingTime, slingTotal;
    Vector2 slingFrom, slingBend, slingTo;
    // a slab gliding out of its chute (floe_cast): from `glideFrom` to `glideTo`
    // (both riding the board) in `glideTotal`, then drifting `drift` sideways
    bool gliding;
    float glideTime, glideTotal, drift;
    Vector2 glideFrom, glideTo;
    int armour;
    bool afloat;   // a slab out of its glide: it rides the board
    // an orb's fuse (frost_bloom): seconds left, and the whole of it
    float fuse, fuseTotal;

    public bool Active { get; private set; }
    public EliteShots.Kind Kind { get; private set; }
    public Vector2 Velocity => velocity;
    public Vector2 LaunchedAt { get; private set; }
    public EliteShip Owner => owner;
    public float Radius => radius;
    public GameObject Hitbox => hitbox;
    public bool Airborne => airborne;
    public bool RosterShot => rosterShot;
    public GameObject Shooter => shooter;
    public float Ride => ride;
    public float Age => age;
    public bool Pooled => Active && Kind == EliteShots.Kind.Glob && !airborne;
    public Vector2 LobTarget => lobTo;
    public bool Slung => Active && slung;
    public Vector2 SlingTarget => slingTo;
    public bool Gliding => Active && gliding;
    public Vector2 GlideTarget => glideTo;
    public float Drift => drift;
    public int Armour => armour;
    public float FuseLeft => Active && Kind == EliteShots.Kind.Orb ? fuse : 0f;
    public int Bursts { get; private set; }
    // A glob in the air: seconds until it lands, and the pool it will be.
    public float LobRemaining => airborne ? Mathf.Max(0f, lobTotal - lobTime) : 0f;
    public float PoolRadius => def != null ? def.shotSize * 2.1f * .42f : radius;
    public float PoolSeconds => def != null ? def.poolSeconds : 0f;
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
    public int ShotMass => Kind == EliteShots.Kind.Glob || Kind == EliteShots.Kind.Slab ? HostileShots.Fixed
                         : Kind == EliteShots.Kind.Orb ? HostileShots.Heavy
                         : Kind == EliteShots.Kind.Slag || Kind == EliteShots.Kind.Shell ? HostileShots.Heavy
                         : HostileShots.Light;
    public Color ShotTint => def != null ? tint : Color.white;
    public Color BodyTint => tint;
    public Color CoreTint => coreTint;

    public void ShotPop(Vector2 at)
    {
        if (!Active) return;
        if (def != null) EliteSystem.Fx.Sparks(at, tint, 4);
        EndReason = 5;
        Recycle();
    }

    void OnDestroy() { HostileShots.Unregister(this); }

    // Only the outline's alpha breathes; it never swells (a swelling outline
    // reads as a round glow).
    void Pulse()
    {
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
        s.glow = new GameObject("Outline").AddComponent<SpriteRenderer>();
        s.glow.transform.SetParent(go.transform, false);
        s.glow.sortingOrder = HostileGlow.SortBehindShots;
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
        slung = false;
        gliding = false;
        fuse = 0f;
        drift = 0f;
        armour = 1;
        afloat = false;
        rosterShot = false;
        shooter = from != null ? from.gameObject : null;
        ride = 0f;
        bounces = Mathf.Max(0, d.shotBounces);
        Bounced = 0;
        if (mark != null) mark.enabled = false;
        float size = Mathf.Max(.06f, kind == EliteShots.Kind.Slab || kind == EliteShots.Kind.Orb ? d.hazardSize : d.shotSize);
        Sprite sprite;
        switch (kind)
        {
            case EliteShots.Kind.Shard: sprite = EliteFxArt.Shard; radius = size * .32f; life = 4f; pierce = 0; break;
            case EliteShots.Kind.Slag: sprite = EliteFxArt.Slag; radius = size * .42f; life = 7f; pierce = 0; break;
            case EliteShots.Kind.Shell: sprite = EliteFxArt.Shell; radius = size * .36f; life = 4f; pierce = 2; break;
            case EliteShots.Kind.Glob: sprite = EliteFxArt.Slag; radius = size * .4f; life = d.lobSeconds + d.poolSeconds; pierce = 0; break;
            case EliteShots.Kind.Slab: sprite = EliteFxArt.Slab; radius = size * .42f; life = 12f; pierce = 3; bounces = 99; armour = Mathf.Max(1, d.hazardArmour); break;
            case EliteShots.Kind.Orb: sprite = EliteFxArt.Orb; radius = size * .4f; life = 6f; pierce = 0; break;
            default: sprite = EliteFxArt.Bolt; radius = size * .3f; life = 4f; pierce = 0; break;
        }
        body.sprite = sprite;
        core.sprite = sprite;
        // (a slab is the world's own ice: drawn as it is, no core; its outline says "hazard")
        bool slab = kind == EliteShots.Kind.Slab;
        tint = HostileShotPalette.Body(d.ShotColor);
        coreTint = HostileShotPalette.Core(tint);
        body.color = slab ? Color.white : tint;
        core.color = coreTint;
        core.enabled = !slab;
        // a big white-hot core: the thin arrows still read on the bright skies (Frost)
        core.transform.localScale = Vector3.one * .62f;
        float k = size / Mathf.Max(.01f, sprite.bounds.size.y);
        transform.localScale = Vector3.one * k;
        transform.position = new Vector3(at.x, at.y, 0f);
        Face();
        EnsureHitbox();
        hitCol.radius = radius / k;
        hitCol.enabled = true;
        ownerId = from != null ? from.GetInstanceID() : 0;
        // the outline's light trace: the hostile pink only a little paled
        // (HostileShotPalette.TraceWhite), so even a thin bolt over a blue
        // planet reads hot pink, never lilac
        glowTint = HostileShotPalette.Trace(tint);
        Outline(sprite, k);
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
        shooter = source;
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
        mark.color = tint;
    }

    // Turns a just-fired shot into a slung one (gravity_sling): it curves
    // from where it is past `bend` and through `to` (world), arriving in
    // `seconds`, then flies on along the curve. The three points ride the
    // board; a blinking ring marks `to` while it is on its way. Its hitbox
    // stays on: dangerous all along the curve.
    public void Sling(Vector2 bend, Vector2 to, float seconds)
    {
        slung = true;
        slingFrom = transform.position;
        slingBend = bend;
        slingTo = to;
        slingTime = 0f;
        slingTotal = Mathf.Max(.1f, seconds);
        velocity = 2f * (bend - slingFrom) / slingTotal;
        Face();
        mark.sprite = EliteFxArt.Ring;
        mark.enabled = true;
        mark.transform.position = new Vector3(to.x, to.y, 0f);
        mark.transform.localScale = Vector3.one * def.shotSize * 2.4f / Mathf.Max(.01f, EliteFxArt.Ring.bounds.size.x);
        mark.color = tint;
    }

    // Turns a just-fired slab into a glide out to `to` (world, riding the
    // board) in `seconds`; then it rides the board drifting `sideways` u/s.
    // Dangerous all the way.
    public void Glide(Vector2 to, float seconds, float sideways)
    {
        gliding = true;
        glideFrom = transform.position;
        glideTo = to;
        glideTime = 0f;
        glideTotal = Mathf.Max(.05f, seconds);
        drift = sideways;
        velocity = (to - glideFrom) / glideTotal;
    }

    // Lights a just-fired orb's fuse: it bursts in `seconds`. A blinking
    // ring round it shows the fuse, blinking faster near the end.
    public void Fuse(float seconds)
    {
        fuse = fuseTotal = Mathf.Max(.05f, seconds);
        mark.sprite = EliteFxArt.Ring;
        mark.enabled = true;
        mark.color = tint;
    }

    // A player weapon touched it: a slab soaks it (armour) until it breaks,
    // anything else is shot down (an orb pops without bursting).
    public void Struck()
    {
        if (!Active) return;
        if (Kind == EliteShots.Kind.Slab && armour > 1)
        {
            armour--;
            EliteSystem.Fx.Sparks(transform.position, def.ShotCore, 5);
            return;
        }
        if (Kind == EliteShots.Kind.Slab || Kind == EliteShots.Kind.Orb) EliteSystem.Fx.Sparks(transform.position, tint, 8);
        EndReason = 5;
        Recycle();
    }

    // The orb's fuse ran out: a ring of hazardCount shards out of where it is.
    void Burst()
    {
        Vector2 at = transform.position;
        int n = Mathf.Max(3, def.hazardCount);
        float speed = Mathf.Max(.5f, def.hazardSpeed);
        float spin = Mathf.Repeat(age * 37f, 360f / n);   // (never quite the same ring twice)
        var o = owner;
        var d = def;
        EliteSystem.Fx.Sparks(at, d.ShotCore, 10);
        EndReason = 6;
        Recycle();
        Bursts++;
        for (int i = 0; i < n; i++)
        {
            float r = (spin + i * 360f / n) * Mathf.Deg2Rad;
            pool.Fire(o, d, EliteShots.Kind.Shard, at, new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * speed);
        }
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
        Outline(EliteFxArt.Pool, k);
        Pulse();
        mark.enabled = false;
        velocity = new Vector2(0f, -EliteSystem.Scroll);
        age = def.lobSeconds;
        EliteSystem.Fx.Sparks(lobTo, tint, 5);
        Physics2D.SyncTransforms();
    }

    // The outline traced from `art`, drawn at the shot's scale `k` (cached
    // per drawing and size: nothing is built per shot after the first).
    void Outline(Sprite art, float k)
    {
        var rim = ShotOutline.For(art, art.bounds.size.y * k);
        glow.sprite = rim;
        glow.enabled = rim != null;
        glow.transform.localScale = Vector3.one;
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
        // (slabs and orbs float upright)
        if (Kind == EliteShots.Kind.Slab || Kind == EliteShots.Kind.Orb) { transform.rotation = Quaternion.identity; return; }
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
        // the danger tell: the core flickers hard, white-hot and back
        core.color = HostileShotPalette.FlickerHot(age) ? Color.white : coreTint;
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
            Color mc = tint;
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
        if (gliding)
        {
            // out of the chute onto its spot (both riding the board), easing in
            float fall = EliteSystem.Scroll * dt;
            glideFrom.y -= fall;
            glideTo.y -= fall;
            glideTime += dt;
            float k = Mathf.Clamp01(glideTime / glideTotal), e = 1f - (1f - k) * (1f - k);
            Vector2 g = Vector2.Lerp(glideFrom, glideTo, e);
            velocity = (g - (Vector2)p) / dt;
            if (k >= 1f) { gliding = false; afloat = true; velocity = new Vector2(drift, -EliteSystem.Scroll); }
        }
        else if (Kind == EliteShots.Kind.Slab && afloat)
        {
            // afloat: rides the board, drifting (a rail turns the drift round)
            velocity.y = -EliteSystem.Scroll;
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(age * 1.7f) * 4f);
        }
        if (Kind == EliteShots.Kind.Orb && fuse > 0f)
        {
            fuse -= dt;
            float k = 1f - fuse / fuseTotal;
            mark.transform.position = new Vector3(p.x, p.y, 0f);
            // the ring shows the fuse: wider as it runs down, blinking faster at the end
            mark.transform.localScale = Vector3.one * def.hazardSize * (k < .5f ? 1.7f : k < .8f ? 2.1f : 2.5f) / Mathf.Max(.01f, EliteFxArt.Ring.bounds.size.x);
            Color mc = tint;
            mc.a = Mathf.FloorToInt(age / ((k > .6f ? 2f : 4f) * EliteArt.Tick)) % 2 == 0 ? .95f : .35f;
            mark.color = mc;
            float pulse = 1f + .12f * (Mathf.FloorToInt(age / ((k > .6f ? 2f : 4f) * EliteArt.Tick)) % 2);
            core.transform.localScale = Vector3.one * .5f * pulse;
            if (fuse <= 0f) { Burst(); return; }
        }
        if (slung)
        {
            // round the curve (a quadratic through the bend), the points riding the board
            float fall = EliteSystem.Scroll * dt;
            slingFrom.y -= fall;
            slingBend.y -= fall;
            slingTo.y -= fall;
            slingTime += dt;
            float k = Mathf.Clamp01(slingTime / slingTotal), u = 1f - k;
            Vector2 c = u * u * slingFrom + 2f * u * k * slingBend + k * k * slingTo;
            velocity = (2f * u * (slingBend - slingFrom) + 2f * k * (slingTo - slingBend)) / slingTotal;
            p.x = c.x;
            p.y = c.y;
            Face();
            mark.transform.position = new Vector3(slingTo.x, slingTo.y, 0f);
            Color mc = tint;
            mc.a = Mathf.FloorToInt(slingTime / ((k > .6f ? 2f : 4f) * EliteArt.Tick)) % 2 == 0 ? .9f : .35f;
            mark.color = mc;
            if (k >= 1f)
            {
                // through the well: on along the curve (its tangent there)
                slung = false;
                mark.enabled = false;
                velocity.y -= EliteSystem.Scroll;   // (the curve's own speed, plus the board's it was riding)
            }
            else velocity.y -= EliteSystem.Scroll;
        }
        else
        {
            p.x += velocity.x * dt;
            p.y += velocity.y * dt;
            if (ride != 0f) p.y -= EliteSystem.Scroll * ride * dt;
        }
        transform.position = p;

        // the rails
        float edge = EliteSystem.RailEdge;
        if (Mathf.Abs(p.x) + radius > edge)
        {
            EliteSystem.Fx.Sparks(new Vector2(Mathf.Sign(p.x) * edge, p.y), tint, 4);
            if (bounces > 0 && Mathf.Sign(velocity.x) == Mathf.Sign(p.x))
            {
                // glances off the rail, back across the board
                bounces--;
                Bounced++;
                velocity.x = -velocity.x * (Kind == EliteShots.Kind.Slab ? 1f : .85f);
                if (Kind == EliteShots.Kind.Slab) drift = velocity.x;
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

        // friendly fire (hostile fire: FriendlyFire.HostileHit)
        Vector2 at = p;
        var live = ClearTarget.Live;
        for (int i = 0; i < live.Count; i++)
        {
            var t = live[i];
            if (t == null || !t.isActiveAndEnabled) continue;
            float R = radius + t.Radius * FriendlyFire.HostileFireReach;
            if (((Vector2)t.transform.position - at).sqrMagnitude > R * R) continue;
            if (!FriendlyFire.HostileFireCanHit(t, shooter)) continue;   // its shooter, the boss, a target just come in
            string by = Kind == EliteShots.Kind.Glob ? "resin pool" : Kind == EliteShots.Kind.Slab ? "ice slab" : rosterShot ? "enemy shot" : "elite shot";
            if (!FriendlyFire.HostileHit(t, p, by)) continue;   // the frame's kill cap: not spent, next frame
            pool.CountFriendly();
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
        slung = false;
        gliding = false;
        fuse = 0f;
        shooter = null;
        if (mark != null) mark.enabled = false;
        gameObject.SetActive(false);
    }
}
