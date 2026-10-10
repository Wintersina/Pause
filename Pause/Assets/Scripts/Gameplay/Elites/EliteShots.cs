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
// PROJECTILE BEHAVIOURS (plan phase 1f; ShotMotion flags read from the shot's skin, ShotSkin.motion -- None for every world until
// its phase calls ShotSkins.Enable(world), so nothing below changes today's shots). Numbers: ShotMotions.
//   Streak   a fast thin slug: speed fixed at ShotMotions.StreakSpeed, a hairline sight line from the shot to the edge of the view
//            and an afterimage of ghosts behind it
//   Shatter  an ice shard that splits into ShatterChips smaller chips (a fan ahead of it) on a rail, on a hazard it hits, or
//            at ShatterSeconds of flight; shot down, it just pops
//   Flutter  a leaf: spins in 45 degree steps and weaves on a sine path (ShotMotions.FlutterAmp at FlutterHz) round its straight
//            course; the two leaves of a volley weave in opposite phase
//   Slash    a crescent that crosses the lane lengthwise: drawn SlashLength long, hit as a capsule along its length
//            (thickness = the kind's own hit radius), speed capped at SlashSpeed
//   Roll     a lobbed heavy log (Lob): it lands, then rolls down the board on a diagonal at RollSpeed, spinning in steps, bouncing off a
//            rail once, RollSeconds of life; heavy mass
//   Burst    a pod that opens into BurstSpores spores (a ring of small shots) after BurstSeconds of flight, on a rail or a hazard;
//            a lobbed pod does it on landing and still leaves its cloud pool
// The hit radius is the kind's own whatever is drawn; every child is a pooled shot (nothing allocates).
//
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
    // its skin (ShotSkins): the drawing, its second frame, how big it is drawn; a still procedural one today
    ShotSkin skin;
    Sprite frameA, frameB, rimA, rimB;
    bool framed;
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
    // the projectile behaviours of the skin (ShotMotion): see the header
    ShotMotion mot;
    int generation;                 // 0 a shot, 1 a chip / spore (never splits again)
    Vector2 baseP;                  // Flutter: the straight course the leaf weaves round
    float flutterPhase;
    bool rolling;                   // Roll: landed, rolling
    bool slashShape;                // Slash: the capsule is the hitbox
    CapsuleCollider2D slashCol;
    SpriteRenderer[] ghosts;        // Streak: the afterimage
    float chipLife;                 // a chip's own life (0: the kind's)
    int lastSpawned;
    float ageBias;                  // a chip / spore: its parent's age at the split (HostileShots tells a volley's shots apart by age)

    public bool Active { get; private set; }
    public EliteShots.Kind Kind { get; private set; }
    public ShotSkin Skin => skin;
    public bool Framed => framed;
    public Sprite BodySprite => body != null ? body.sprite : null;
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
    public bool Pooled => Active && Kind == EliteShots.Kind.Glob && !airborne && !rolling;
    public ShotMotion Motion => mot;
    // The straight course a Flutter leaf weaves round (its position otherwise): what a pilot reads the leaf's heading from.
    public Vector2 CoursePosition => (mot & ShotMotion.Flutter) != 0 && !airborne ? baseP : (Vector2)transform.position;
    // Seconds until a Shatter spear splits by itself (-1: it will not).
    public float SplitIn => Active && generation == 0 && (mot & ShotMotion.Shatter) != 0 && !airborne ? Mathf.Max(0f, ShotMotions.ShatterSeconds - age) : -1f;
    // The chip `i` of `n` a split at heading `v` makes: its velocity (the board's pull on a roster shot's chips included).
    public static Vector2 ChipVelocity(Vector2 v, int i, int n, float rideBoard)
    {
        Vector2 dir = v.sqrMagnitude > 1e-6f ? v.normalized : Vector2.down;
        float a = n > 1 ? Mathf.Lerp(-ShotMotions.ChipSpreadDeg, ShotMotions.ChipSpreadDeg, i / (float)(n - 1)) : 0f;
        return Rotated(dir, a) * Mathf.Max(ShotMotions.ChipMinSpeed, v.magnitude * ShotMotions.ChipSpeedShare) + new Vector2(0f, -EliteSystem.Scroll * rideBoard);
    }
    public bool Rolling => Active && rolling;
    public bool IsChip => generation > 0;
    public int Splits { get; private set; }           // times this pooled object split into chips / spores (all lives)
    public int LastSpawned => lastSpawned;            // children the last split made
    public bool SightShown => mark != null && mark.enabled && (mot & ShotMotion.Streak) != 0;
    public float SightLength { get; private set; }
    public int GhostsShown { get { int n = 0; if (ghosts != null) for (int i = 0; i < ghosts.Length; i++) if (ghosts[i] != null && ghosts[i].enabled) n++; return n; } }
    public bool SlashCapsule => slashShape && slashCol != null && slashCol.enabled;
    public Vector2 SlashAxis => transform.right;
    public float HitRadiusOfCollider => slashShape && slashCol != null ? slashCol.size.y * .5f * transform.lossyScale.x : (hitCol != null ? hitCol.radius * transform.lossyScale.x : 0f);
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
    public bool ShotCollidable => Active && !airborne && hitbox != null && hitCol != null && (hitCol.enabled || SlashCapsule);
    public Vector2 ShotPosition => transform.position;
    public float ShotRadius => radius;
    public int ShotOwner => ownerId;
    public float ShotAge => age + ageBias;   // (a chip keeps its parent's age, so it never clashes with the volley it came from)
    public int ShotMass => rolling ? HostileShots.Heavy
                         : Kind == EliteShots.Kind.Glob || Kind == EliteShots.Kind.Slab ? HostileShots.Fixed
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
        // the Streak afterimage: four ghosts of the body, built up front (firing never allocates)
        s.ghosts = new SpriteRenderer[ShotMotions.GhostCount];
        for (int i = 0; i < s.ghosts.Length; i++)
        {
            var g = new GameObject("Ghost" + i).AddComponent<SpriteRenderer>();
            g.transform.SetParent(go.transform, false);
            g.sortingOrder = 29;
            g.enabled = false;
            s.ghosts[i] = g;
        }
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
        mot = ShotMotion.None;
        generation = 0;
        rolling = false;
        slashShape = false;
        chipLife = 0f;
        lastSpawned = 0;
        ageBias = 0f;
        SightLength = 0f;
        bounces = Mathf.Max(0, d.shotBounces);
        Bounced = 0;
        if (mark != null) mark.enabled = false;
        float size = Mathf.Max(.06f, kind == EliteShots.Kind.Slab || kind == EliteShots.Kind.Orb ? d.hazardSize : d.shotSize);
        // the drawing comes from the shot's skin (its world's, once that world is themed: ShotSkins);
        // the hit radius below never depends on it
        skin = ShotSkins.For(d, kind);
        Sprite sprite = skin.a;
        switch (kind)
        {
            case EliteShots.Kind.Shard: radius = size * .32f; life = 4f; pierce = 0; break;
            case EliteShots.Kind.Slag: radius = size * .42f; life = 7f; pierce = 0; break;
            case EliteShots.Kind.Shell: radius = size * .36f; life = 4f; pierce = 2; break;
            case EliteShots.Kind.Glob: radius = size * .4f; life = d.lobSeconds + d.poolSeconds; pierce = 0; break;
            case EliteShots.Kind.Slab: radius = size * .42f; life = 12f; pierce = 3; bounces = 99; armour = Mathf.Max(1, d.hazardArmour); break;
            case EliteShots.Kind.Orb: radius = size * .4f; life = 6f; pierce = 0; break;
            default: radius = size * .3f; life = 4f; pierce = 0; break;
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
        mot = skin.motion;
        float k = size * skin.drawScale / Mathf.Max(.01f, sprite.bounds.size.y);
        // a crescent is drawn lengthwise (its art is horizontal): SlashLength long whatever the shot's size
        if ((mot & ShotMotion.Slash) != 0) k = ShotMotions.SlashLength / Mathf.Max(.01f, sprite.bounds.size.x);
        transform.localScale = Vector3.one * k;
        transform.position = new Vector3(at.x, at.y, 0f);
        Face();
        EnsureHitbox();
        hitCol.radius = radius / k;   // (the hit radius is the nominal one whatever the drawn size)
        hitCol.enabled = true;
        ownerId = from != null ? from.GetInstanceID() : 0;
        // the outline's light trace: the hostile pink only a little paled
        // (HostileShotPalette.TraceWhite), so even a thin bolt over a blue
        // planet reads hot pink, never lilac
        // (bold, on a bright world: paled further, ShotOutline.BoldTrace, so the
        // light ring reads over the mid-dark patches the dark keyline can't)
        glowTint = ShotOutline.UseBold ? ShotOutline.BoldTrace(HostileShotPalette.Trace(tint)) : HostileShotPalette.Trace(tint);
        rimA = Outline(sprite, k);
        framed = skin.TwoFrames;
        frameA = skin.a;
        frameB = skin.b;
        rimB = framed ? ShotOutline.For(frameB, frameB.bounds.size.y * k) : null;
        Pulse();
        SetupMotion(k);
        Active = true;
        gameObject.SetActive(true);
    }

    // The skin's behaviour flags take hold: what a Streak, Slash or Flutter shot needs from its first frame.
    void SetupMotion(float k)
    {
        for (int i = 0; i < ghosts.Length; i++) ghosts[i].enabled = false;
        if (mark != null) mark.enabled = false;
        if (slashCol != null) slashCol.enabled = false;
        if ((mot & ShotMotion.Streak) != 0)
        {
            // a fast thin slug: the speed is the design's, along the aimed line; a hairline sight line runs to the edge of the view
            if (velocity.sqrMagnitude > 1e-6f) velocity = velocity.normalized * ShotMotions.StreakSpeed;
            Face();
            for (int i = 0; i < ghosts.Length; i++) { ghosts[i].sprite = body.sprite; ghosts[i].enabled = true; }
            PlaceStreak();
        }
        if ((mot & ShotMotion.Slash) != 0)
        {
            float sp = velocity.magnitude;
            if (sp > ShotMotions.SlashSpeed) velocity *= ShotMotions.SlashSpeed / sp;
            slashShape = true;
            // a capsule along the crescent: ShotMotions.SlashLength long, twice the kind's hit radius thick (local units: the shot is scaled by k)
            slashCol.size = new Vector2(ShotMotions.SlashLength / k, Mathf.Max(radius * 2f, .02f) / k);
            slashCol.enabled = true;
            hitCol.enabled = false;
            Face();
        }
        if ((mot & ShotMotion.Flutter) != 0)
        {
            baseP = transform.position;
            flutterPhase = (pool != null && (pool.Launched & 1) == 1) ? Mathf.PI : 0f;
        }
    }

    // The hit shape is on or off (a lob has none in the air): the circle, or the crescent's capsule.
    void HitOn(bool on)
    {
        if (slashShape && slashCol != null) { slashCol.enabled = on; hitCol.enabled = false; }
        else hitCol.enabled = on;
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
        HitOn(false);
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
        if ((mot & ShotMotion.Roll) != 0) { StartRolling(p); return; }
        Sprite poolB;
        Sprite pool = ShotSkins.PoolSprite(ShotSkins.WorldOf(def), out poolB);
        body.sprite = pool;
        core.sprite = pool;
        float size = def.shotSize * 2.1f;
        float k = size / Mathf.Max(.01f, pool.bounds.size.x);
        transform.localScale = Vector3.one * k;
        radius = size * .42f;
        hitCol.radius = radius / k;
        HitOn(true);
        rimA = Outline(pool, k);
        frameA = pool;
        frameB = poolB;
        framed = poolB != null;
        rimB = framed ? ShotOutline.For(poolB, poolB.bounds.size.y * k) : null;
        Pulse();
        mark.enabled = false;
        velocity = new Vector2(0f, -EliteSystem.Scroll);
        age = def.lobSeconds;
        EliteSystem.Fx.Sparks(lobTo, tint, 5);
        Physics2D.SyncTransforms();
        // a pod opens as it lands: the cloud pool stays, spores scatter out of it
        if ((mot & ShotMotion.Burst) != 0 && generation == 0)
        {
            lastSpawned = SpawnRing(p, ShotMotions.BurstSpores, ShotMotions.SporeSpeed, ShotMotions.SporeScale, ShotMotions.SporeSeconds);
            Splits++;
            mot &= ~ShotMotion.Burst;   // (the cloud that stays is the pool: it does not burst again at a rail)
        }
    }

    // Roll: the log has come down; it rolls on down the board on a diagonal, bouncing off a rail once.
    void StartRolling(Vector3 p)
    {
        rolling = true;
        mark.enabled = false;
        HitOn(true);
        float dirX = p.x > .25f ? 1f : (p.x < -.25f ? -1f : ((pool != null && (pool.Launched & 1) == 1) ? -1f : 1f));   // toward the nearer rail
        velocity = new Vector2(dirX * ShotMotions.RollSpeed * .65f, -EliteSystem.Scroll - ShotMotions.RollSpeed * .76f);
        bounces = 1;
        age = 0f;
        life = ShotMotions.RollSeconds;
        EliteSystem.Fx.Sparks(p, tint, 6);
        Physics2D.SyncTransforms();
    }

    // The outline traced from `art`, drawn at the shot's scale `k` (cached
    // per drawing and size: nothing is built per shot after the first).
    Sprite Outline(Sprite art, float k)
    {
        var rim = ShotOutline.For(art, art.bounds.size.y * k);
        glow.sprite = rim;
        glow.enabled = rim != null;
        glow.transform.localScale = Vector3.one;
        return rim;
    }

    // A two-frame skin flips between its drawings at the skin's fps (stepped, never a smooth breath: PC5);
    // the outline follows. Nothing is made here: both rims were built when the shot launched.
    void SwapFrame()
    {
        bool second = (Mathf.FloorToInt(age * skin.frameFps) & 1) == 1;
        var f = second ? frameB : frameA;
        if (body.sprite == f) return;
        body.sprite = f;
        core.sprite = f;
        var rim = second ? rimB : rimA;
        glow.sprite = rim;
        glow.enabled = rim != null;
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
        // the crescent's capsule (Slash), built with the hitbox and off: firing never adds a collider
        slashCol = hitbox.AddComponent<CapsuleCollider2D>();
        slashCol.isTrigger = true;
        slashCol.direction = CapsuleDirection2D.Horizontal;
        slashCol.enabled = false;
    }

    void Face()
    {
        // (slabs and orbs float upright)
        if (Kind == EliteShots.Kind.Slab || Kind == EliteShots.Kind.Orb) { transform.rotation = Quaternion.identity; return; }
        if (velocity.sqrMagnitude < 1e-6f) return;
        // (a crescent's art is horizontal: its length runs along the course)
        float deg = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg - ((mot & ShotMotion.Slash) != 0 ? 0f : 90f);
        transform.rotation = Quaternion.Euler(0f, 0f, deg);
    }

    public void Step(float dt)
    {
        if (!Active) return;
        if (hitbox == null) { EndReason = 3; Recycle(); return; }
        if (dt <= 0f) return;
        age += dt;
        Pulse();
        if (framed) SwapFrame();
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
            transform.localScale = Vector3.one * (def.shotSize * skin.drawScale / Mathf.Max(.01f, skin.a.bounds.size.y)) * (1f + .9f * height);
            transform.rotation = Quaternion.Euler(0f, 0f, age * 240f);
            mark.transform.position = new Vector3(lobTo.x, lobTo.y, 0f);
            Color mc = tint;
            mc.a = Mathf.FloorToInt(lobTime / ((k > .6f ? 2f : 4f) * EliteArt.Tick)) % 2 == 0 ? .9f : .35f;
            mark.color = mc;
            if (k >= 1f) Land();
            return;
        }
        if (Kind == EliteShots.Kind.Glob && !rolling)
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
        else if ((mot & ShotMotion.Flutter) != 0 && !airborne)
        {
            // the leaf: its straight course, and a sine weave across it; spinning in 45 degree steps (stepped, never a smooth turn: PC5)
            baseP.x += velocity.x * dt;
            baseP.y += velocity.y * dt;
            if (ride != 0f) baseP.y -= EliteSystem.Scroll * ride * dt;
            Vector2 dv = velocity.sqrMagnitude > 1e-6f ? velocity.normalized : Vector2.down;
            float w = Mathf.Sin(age * Mathf.PI * 2f * ShotMotions.FlutterHz + flutterPhase) * ShotMotions.FlutterAmp;
            p.x = baseP.x - dv.y * w;
            p.y = baseP.y + dv.x * w;
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Floor(age * 10f) * 45f);
        }
        else
        {
            if (rolling)
            {
                // the log keeps its pace down the board and turns in 30 degree steps
                velocity.y = -EliteSystem.Scroll - ShotMotions.RollSpeed * .76f;
                transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Floor(age * 10f) * 30f * (velocity.x >= 0f ? -1f : 1f));
            }
            p.x += velocity.x * dt;
            p.y += velocity.y * dt;
            if (ride != 0f) p.y -= EliteSystem.Scroll * ride * dt;
        }
        transform.position = p;
        if ((mot & ShotMotion.Streak) != 0) PlaceStreak();

        // the rails
        float edge = EliteSystem.RailEdge;
        // a crescent crosses the lane: it ends when its centre reaches the rail (a blade's end may lap over it)
        float reach = slashShape ? radius + ShotMotions.SlashRailLap : radius;
        if (Mathf.Abs(p.x) + reach > edge)
        {
            EliteSystem.Fx.Sparks(new Vector2(Mathf.Sign(p.x) * edge, p.y), tint, 4);
            // an ice shard shatters on the rail, a pod bursts on it
            if (CanScatter)
            {
                p.x = Mathf.Sign(p.x) * (edge - reach - .01f);
                transform.position = p;
                EndReason = 2;
                if (Scatter(p)) return;
            }
            if (bounces > 0 && Mathf.Sign(velocity.x) == Mathf.Sign(p.x))
            {
                // glances off the rail, back across the board
                bounces--;
                Bounced++;
                velocity.x = -velocity.x * (Kind == EliteShots.Kind.Slab ? 1f : (rolling ? 1f : .85f));
                if (Kind == EliteShots.Kind.Slab) drift = velocity.x;
                p.x = Mathf.Sign(p.x) * (edge - reach - .01f);
                transform.position = p;
                baseP = p;
                if (!rolling) Face();
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
            if (slashShape)
            {
                // along the crescent: the distance to its centre line
                Vector2 ax = transform.right * (ShotMotions.SlashLength * .5f);
                if (HostileShots.SegmentDistanceSq(at - ax, at + ax, t.transform.position) > R * R) continue;
            }
            else if (((Vector2)t.transform.position - at).sqrMagnitude > R * R) continue;
            if (!FriendlyFire.HostileFireCanHit(t, shooter)) continue;   // its shooter, the boss, a target just come in
            string by = Kind == EliteShots.Kind.Glob ? (rolling ? "rolling log" : "resin pool") : Kind == EliteShots.Kind.Slab ? "ice slab" : rosterShot ? "enemy shot" : "elite shot";
            if (!FriendlyFire.HostileHit(t, p, by)) continue;   // the frame's kill cap: not spent, next frame
            pool.CountFriendly();
            if (pierce-- <= 0) { EndReason = 4; if (!Scatter(p)) Recycle(); return; }
            break;   // the registry may have changed
        }

        // an ice shard splits, a pod bursts, once it has flown far enough
        if (CanScatter && !airborne && ((mot & ShotMotion.Shatter) != 0 ? age >= ShotMotions.ShatterSeconds : age >= ShotMotions.BurstSeconds) && Kind != EliteShots.Kind.Glob)
        {
            EndReason = 6;
            if (Scatter(p)) return;
        }
        if (age > life || p.y < EliteSystem.ViewBottom - 1f || p.y > EliteSystem.ViewTop + 1.5f)
        {
            EndReason = 1;
            Recycle();
        }
    }

    // ---- the projectile behaviours: children, the sight line ------------------------------------------------

    // Shatter / Burst apply to a first-generation shot of that skin (a chip or a spore never splits again).
    bool CanScatter => generation == 0 && (mot & (ShotMotion.Shatter | ShotMotion.Burst)) != 0;

    // The shot splits where it is -- chips in a fan ahead (Shatter) or spores in a ring (Burst) -- and is gone.
    // False when it is not that kind of shot (the caller carries on).
    bool Scatter(Vector2 at)
    {
        if (!CanScatter) return false;
        if ((mot & ShotMotion.Shatter) != 0)
        {
            Vector2 dir = velocity.sqrMagnitude > 1e-6f ? velocity.normalized : Vector2.down;
            float sp = Mathf.Max(ShotMotions.ChipMinSpeed, velocity.magnitude * ShotMotions.ChipSpeedShare);
            EliteSystem.Fx.Sparks(at, tint, 5);
            lastSpawned = SpawnFan(at, dir, sp);
        }
        else
        {
            EliteSystem.Fx.Sparks(at, tint, 6);
            lastSpawned = SpawnRing(at, ShotMotions.BurstSpores, ShotMotions.SporeSpeed, ShotMotions.SporeScale, ShotMotions.SporeSeconds);
        }
        Splits++;
        if (EndReason == 0) EndReason = 6;
        Recycle();
        return true;
    }

    int SpawnFan(Vector2 at, Vector2 dir, float speed)
    {
        int n = ShotMotions.ShatterChips, made = 0;
        for (int i = 0; i < n; i++)
        {
            float a = n > 1 ? Mathf.Lerp(-ShotMotions.ChipSpreadDeg, ShotMotions.ChipSpreadDeg, i / (float)(n - 1)) : 0f;
            Vector2 v = Rotated(dir, a) * speed;
            if (Child(at, v, ShotMotions.ChipScale, ShotMotions.ChipSeconds)) made++;
        }
        return made;
    }

    int SpawnRing(Vector2 at, int n, float speed, float scale, float seconds)
    {
        int made = 0;
        float spin = Mathf.Repeat(age * 37f, 360f / Mathf.Max(1, n));
        for (int i = 0; i < n; i++)
        {
            float r = (spin + i * 360f / n) * Mathf.Deg2Rad;
            if (Child(at, new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * speed, scale, seconds)) made++;
        }
        return made;
    }

    static Vector2 Rotated(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    bool Child(Vector2 at, Vector2 v, float scale, float seconds)
    {
        if (pool == null) return false;
        var c = pool.Fire(owner, def, EliteShots.Kind.Shard, at, v);
        if (c == null) return false;
        c.BecomeChip(scale, seconds, rosterShot, shooter, ride, ownerId, age + ageBias);
        return true;
    }

    // A chip / spore: a small plain shard of its parent's shooter and side; never splits, ghosts or flutters.
    void BecomeChip(float scale, float seconds, bool fromRoster, GameObject by, float rideBoard, int owningId, float parentAge)
    {
        generation = 1;
        ageBias = parentAge;
        mot = ShotMotion.None;
        slashShape = false;
        rolling = false;
        rosterShot = fromRoster;
        shooter = by;
        ride = rideBoard;
        ownerId = owningId;
        for (int i = 0; i < ghosts.Length; i++) ghosts[i].enabled = false;
        mark.enabled = false;
        if (slashCol != null) slashCol.enabled = false;
        hitCol.enabled = true;
        transform.localScale *= scale;
        radius *= scale;
        life = seconds;
        Face();
    }

    // Streak: the afterimage behind the slug, and a hairline from it to the edge of the view along its course.
    void PlaceStreak()
    {
        Vector2 p = transform.position;
        Vector2 d = velocity.sqrMagnitude > 1e-6f ? velocity.normalized : Vector2.down;
        for (int i = 0; i < ghosts.Length; i++)
        {
            var g = ghosts[i];
            g.enabled = true;
            g.sprite = body.sprite;
            g.transform.position = new Vector3(p.x - d.x * ShotMotions.GhostGap * (i + 1), p.y - d.y * ShotMotions.GhostGap * (i + 1), 0f);
            g.transform.rotation = transform.rotation;
            var c = tint;
            c.a = ShotMotions.GhostAlpha / (i + 1.4f);
            g.color = c;
        }
        // the distance along the course to the rails or the top / bottom of the view
        float t = 40f;
        float edge = EliteSystem.RailEdge;
        if (d.x > 1e-4f) t = Mathf.Min(t, (edge - p.x) / d.x); else if (d.x < -1e-4f) t = Mathf.Min(t, (-edge - p.x) / d.x);
        if (d.y > 1e-4f) t = Mathf.Min(t, (EliteSystem.ViewTop + 1f - p.y) / d.y); else if (d.y < -1e-4f) t = Mathf.Min(t, (EliteSystem.ViewBottom - 1f - p.y) / d.y);
        t = Mathf.Max(0f, t);
        SightLength = t;
        mark.enabled = t > .05f;
        mark.sprite = AttackHazardArt.Band();
        var mc = tint;
        mc.a = (Mathf.FloorToInt(age * 12f) & 1) == 0 ? .6f : .32f;   // (stepped)
        mark.color = mc;
        mark.transform.position = new Vector3(p.x + d.x * t * .5f, p.y + d.y * t * .5f, 0f);
        mark.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        mark.transform.localScale = new Vector3(t, ShotMotions.SightThickness, 1f);
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
        framed = false;
        rolling = false;
        if (mark != null) mark.enabled = false;
        if (ghosts != null) for (int i = 0; i < ghosts.Length; i++) if (ghosts[i] != null) ghosts[i].enabled = false;
        gameObject.SetActive(false);
    }
}

// The numbers of the projectile behaviours (EliteShot, ShotMotion flags read from the skin). Budgets are the design doc's
// (docs/world-attacks-design.md section 3): the Warden's slug is 6.0 u/s, the Mantis's crescent 4.5 u/s and 1.1 u long, the Snap
// Sprout's leaf weaves .12 u at 2 Hz, the Timber Hauler's log rolls 1.4 u/s with five seconds of life.
public static class ShotMotions
{
    public const float StreakSpeed = 6f;       // u/s
    public const int GhostCount = 4;
    public const float GhostGap = .35f;        // u between afterimages: 1.4 u in all
    public const float GhostAlpha = .6f;
    public const float SightThickness = .03f;  // the hairline

    public const float ShatterSeconds = 1.2f;  // flight before an ice shard splits by itself
    public const int ShatterChips = 3;
    public const float ChipSpreadDeg = 14f;    // the fan: -14, 0, +14 round the heading
    public const float ChipSpeedShare = .9f;
    public const float ChipMinSpeed = 1.2f;
    public const float ChipScale = .6f;        // drawn and hit at 60% of the spear (r .054 -> .032)
    public const float ChipSeconds = 1.6f;

    public const float FlutterAmp = .12f;      // u across the course
    public const float FlutterHz = 2f;

    public const float SlashLength = 1.1f;     // u, drawn and hit lengthwise
    public const float SlashSpeed = 4.5f;      // u/s cap
    public const float SlashRailLap = .15f;    // u past the rail's face the centre may go before the blade ends

    public const float RollSpeed = 1.4f;       // u/s along the ground relative to the board
    public const float RollSeconds = 5f;

    public const float BurstSeconds = 1.3f;    // flight before a (non-lobbed) pod bursts by itself
    public const int BurstSpores = 6;
    public const float SporeSpeed = 1.6f;
    public const float SporeScale = .55f;
    public const float SporeSeconds = .9f;
}
