using System.Collections.Generic;
using UnityEngine;

// A rail mine's laser ("have the rail mines shoot a laser"): a straight,
// horizontal beam from the mine's rail across the whole lane to the
// opposite rail. Driven by the mine's EnemyBrain (EnemyAttack.Laser):
//
//   Aim    the brain's windup (the mine's waking -> charging loop and charge
//          light, as before); for its last AimSeconds a thin blinking aim
//          line shows exactly where the beam will burn. Harmless.
//   Beam   BeamSeconds of sustained beam: the only phase with a hitbox.
//   Cool   CoolSeconds of flicker as it dies out. Harmless.
//
// LOOK. The world boss's own laser art (BossArt: the lane telegraph cell
// for the aim line, the two beam cells, BossAttackFx's muzzle flash and rail
// spark), so it is the same hard-edged pixel beam as the boss lasers, with a
// white core (lime in Verdant, that world's laser). The boss beams' hot pink
// sits inside the player's red band (HostileGlow.IsPlayerRed, 334 deg), so
// the mine's copy of those cells has every such pixel turned to neon
// magenta (MagentaHue), keeping its saturation and brightness: built once
// per world (MineLaserArt), never per fire or per frame.
//
// GEOMETRY. Every frame (LateUpdate, after RailMineMount places the mine)
// the beam is laid at the mine's current y, from its own rail's drawn inner
// face to the opposite rail's (BossRails.DrawnInnerEdge: the measured rails,
// or where WorldPainter puts them on this screen, RailInset included). It
// rides the board with the mine and is blocked by nothing but the rails.
//
// DAMAGE. The pilot: a trigger BoxCollider2D tagged "Enimey" on a child
// (HitboxName), HitThickness across the beam, enabled in the Beam phase
// only -- collisionDetection's normal hostile hit: a heart, or, shielded,
// absorbed (EliteShip.ShieldRam -> EraseHitbox ends the beam), erased by a
// blink (EliteShip.TeleportStrike). A hit that destroys the hitbox ends the
// beam. Other hazards: TARGETS ARE DECIDED IN ONE PLACE, Burn(): when
// HurtsOtherEnemies is on, every ClearTarget hazard the live beam's rect
// touches (FriendlyFire.HostileFireCanHit: not the boss, not shot hitboxes,
// not a target still in its spawn-in protection, never its own mine; off in
// the tutorial and during a player death) takes FriendlyFire.HostileHit --
// the hostile-fire hit, a kill or an elite's heart, unpaid, within the
// frame's kill cap -- at most once per pulse.
//
// PAUSE / 60 FPS. Timers only advance through Step(dt), which the brain
// calls on running frames, so a frozen world freezes the laser. Pooled
// (RailMineLasers, at most Max), every sprite looked up once per fire;
// nothing allocates per frame.
public class RailMineLaser : MonoBehaviour
{
    public enum Phase { Off, Aim, Beam, Cool }

    // ---- tunables ----
    public const float AimSeconds = .7f;       // the aim line: the last this-many seconds of the windup
    public const float BeamSeconds = .4f;      // the sustained beam: the only harmful phase
    public const float CoolSeconds = .2f;      // the flicker out
    public const float DrawWidth = .4f;        // drawn beam (the art cell's width)
    public const float HitThickness = .28f;    // hitbox across the beam (DrawWidth x BossConfig.BeamHitFraction)
    public const float SightWidth = .12f;      // the aim line (DrawWidth x BossConfig.BeamSightWidth)
    public const float FlashAlong = .45f;      // muzzle flash: this far along from the rail face (the mine's lane-side face)
    public const float FlashSize = .34f;
    public const int SortBeam = 11;            // under the mine (12): the beam leaves its body
    public const int SortFx = 13;
    public const string HitboxName = "RailMineLaserHit";
    // Friendly fire: does the live beam hit other hazards (rocks, enemies,
    // mines, elites)? The one switch for the beam's target filter.
    public static bool HurtsOtherEnemies = true;
    public const int MaxHitsPerPulse = 16;

    SpriteRenderer sight, beam, flash, impact;
    GameObject hitbox;
    BoxCollider2D box;
    Transform owner;
    Sprite sightSprite;
    readonly Sprite[] beamSprites = new Sprite[2];
    readonly Sprite[] flashSprites = new Sprite[BossAttackFx.FlashFrames];
    readonly Sprite[] sparkSprites = new Sprite[2];
    readonly int[] hit = new int[MaxHitsPerPulse];
    int hitCount;
    float t, age, aimDelay, length, side, y;
    Vector2 from, to;

    public Phase State { get; private set; }
    public bool Active => State != Phase.Off;
    public bool Live => State == Phase.Beam && hitbox != null && box != null && box.enabled;
    public bool SightShown => sight != null && sight.enabled;
    public bool BeamShown => beam != null && beam.enabled;
    public GameObject Hitbox => hitbox;
    public Transform Owner => owner;
    public Vector2 From => from;            // its own rail's inner face, at the mine's y
    public Vector2 To => to;                // the opposite rail's inner face
    public float Length => length;
    public float Y => y;
    public float PhaseTime => t;
    public int HitsThisPulse => hitCount;
    public SpriteRenderer BeamRenderer => beam;
    public SpriteRenderer SightRenderer => sight;

    // Where a laser from a mine at x runs: rail face to rail face.
    public static void SpanFor(float mineX, out float fromX, out float toX)
    {
        float edge = BossRails.DrawnInnerEdge;
        float s = mineX >= 0f ? 1f : -1f;
        fromX = s * edge;
        toX = -s * edge;
    }

    // Does a body at p (half-height halfH, half-width halfW) overlap the
    // live beam's hit rect? (The same rect as the hitbox.)
    public bool Touches(Vector2 p, float halfW, float halfH)
    {
        if (!Live) return false;
        float lo = Mathf.Min(from.x, to.x), hi = Mathf.Max(from.x, to.x);
        return Mathf.Abs(p.y - y) < HitThickness * .5f + halfH && p.x + halfW > lo && p.x - halfW < hi;
    }

    public static RailMineLaser Create(Transform root)
    {
        var go = new GameObject("RailMineLaser");
        go.transform.SetParent(root, false);
        var l = go.AddComponent<RailMineLaser>();
        l.sight = Piece(go.transform, "Sight", SortBeam);
        l.beam = Piece(go.transform, "Beam", SortBeam);
        l.flash = Piece(go.transform, "Flash", SortFx);
        l.impact = Piece(go.transform, "Impact", SortFx);
        l.EnsureHitbox();
        l.box.enabled = false;
        go.SetActive(false);
        return l;
    }

    static SpriteRenderer Piece(Transform parent, string name, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = order;
        sr.enabled = false;
        return sr;
    }

    void EnsureHitbox()
    {
        if (hitbox != null) return;
        hitbox = new GameObject(HitboxName);
        hitbox.tag = "Enimey";
        hitbox.transform.SetParent(transform, false);
        box = hitbox.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = new Vector2(HitThickness, .01f);
        hitbox.AddComponent<RailMineLaserHitbox>().laser = this;
    }

    // The windup begins (`tellSeconds` long): the aim line will show for
    // its last AimSeconds.
    public void Arm(Transform mine, int world, float tellSeconds)
    {
        owner = mine;
        var art = MineLaserArt.For(world);
        sightSprite = art.sight;
        beamSprites[0] = art.beam0;
        beamSprites[1] = art.beam1;
        for (int i = 0; i < flashSprites.Length; i++) flashSprites[i] = art.flash[i];
        sparkSprites[0] = art.spark[0];
        sparkSprites[1] = art.spark[1];
        sight.sprite = sightSprite;
        beam.sprite = beamSprites[0];
        aimDelay = Mathf.Max(0f, tellSeconds - AimSeconds);
        t = age = 0f;
        hitCount = 0;
        State = Phase.Aim;
        if (box != null) box.enabled = false;
        sight.enabled = beam.enabled = flash.enabled = impact.enabled = false;
        gameObject.SetActive(true);
        Place();
    }

    // The windup is over: the beam burns.
    public void Fire()
    {
        if (State != Phase.Aim) return;
        State = Phase.Beam;
        t = age = 0f;
        hitCount = 0;
        EnsureHitbox();
        box.enabled = true;
        sight.enabled = false;
        beam.enabled = flash.enabled = true;
        Place();
    }

    // Absorbed by a shield or erased by a blink: the beam is spent.
    public void Erase()
    {
        if (State == Phase.Beam) EndBeam();
    }

    void EndBeam()
    {
        State = Phase.Cool;
        t = 0f;
        if (box != null) box.enabled = false;
        flash.enabled = impact.enabled = false;
    }

    // Gone at once (its mine is gone).
    public void Cancel() { Recycle(); }

    public void Step(float dt)
    {
        if (!Active || dt <= 0f) return;
        t += dt;
        age += dt;
        switch (State)
        {
            case Phase.Beam:
                // hit the pilot (collisionDetection destroyed the hitbox) or done
                if (hitbox == null || t >= BeamSeconds) EndBeam();
                break;
            case Phase.Cool:
                if (t >= CoolSeconds) { Recycle(); return; }
                break;
        }
        Place();
        if (State == Phase.Beam) Burn();
    }

    // After RailMineMount has put the mine where it is this frame.
    void LateUpdate()
    {
        if (Active) Place();
    }

    // Lays everything along the beam at the mine's current y.
    public void Place()
    {
        if (owner == null) { Recycle(); return; }
        Vector3 m = owner.position;
        y = m.y;
        float fx, tx;
        SpanFor(m.x, out fx, out tx);
        side = m.x >= 0f ? 1f : -1f;
        from = new Vector2(fx, y);
        to = new Vector2(tx, y);
        length = Mathf.Abs(tx - fx);
        transform.position = new Vector3(fx, y, 0f);
        // local +y runs along the beam, toward the far rail
        transform.rotation = Quaternion.Euler(0f, 0f, side > 0f ? 90f : -90f);
        transform.localScale = Vector3.one;

        float tick = BossArt.Tick;
        switch (State)
        {
            case Phase.Aim:
            {
                bool on = t >= aimDelay && Mathf.FloorToInt((t - aimDelay) / (BossArt.TelegraphBlinkTicks * tick)) % 2 == 0;
                sight.enabled = on && sightSprite != null;
                Span(sight.transform, SightWidth, length);
                break;
            }
            case Phase.Beam:
            {
                // a hot flicker on twos, never a smooth pulse (as the boss beams)
                float w = DrawWidth * (Mathf.FloorToInt(age / (2f * tick)) % 2 == 0 ? 1f : .85f);
                beam.enabled = beamSprites[0] != null;
                beam.sprite = beamSprites[BossArt.FrameAt(BossArt.BeamTicks, age, true) % 2];
                Span(beam.transform, w, length);
                int f = Mathf.Min(BossArt.FrameAt(BossAttackFx.FlashTickTable, age, true), flashSprites.Length - 1);
                flash.sprite = flashSprites[f];
                flash.enabled = flash.sprite != null;
                flash.transform.localPosition = new Vector3(0f, FlashAlong, 0f);
                flash.transform.localScale = Vector3.one * FlashSize;
                impact.sprite = sparkSprites[Mathf.FloorToInt(age / tick) % 2];
                impact.enabled = impact.sprite != null;
                impact.transform.localPosition = new Vector3(0f, length, 0f);
                impact.transform.localScale = Vector3.one * BossConfig.RailSparkSize;
                if (box != null)
                {
                    // the hitbox's centre sits level with the pilot along the
                    // beam (where a shield shows the absorb, a heart darts to)
                    var p = EliteSystem.Player;
                    float along = length * .5f;
                    if (p != null) along = Mathf.Clamp((p.position.x - fx) * -side, 0f, length);
                    hitbox.transform.localPosition = new Vector3(0f, along, 0f);
                    box.size = new Vector2(HitThickness, Mathf.Max(.01f, length));
                    box.offset = new Vector2(0f, length * .5f - along);
                }
                break;
            }
            case Phase.Cool:
            {
                // flickers out on ticks, thinning
                float k = 1f - Mathf.Clamp01(t / CoolSeconds);
                beam.enabled = beamSprites[0] != null && Mathf.FloorToInt(t / tick) % 2 == 0;
                Span(beam.transform, DrawWidth * Mathf.Max(.2f, k), length);
                break;
            }
        }
    }

    // FRIENDLY FIRE: the one place the beam picks its targets (besides the
    // pilot's hitbox). Every hazard whose body the live beam's rect touches,
    // once per pulse, never its own mine, through the hostile-fire hit
    // (FriendlyFire.HostileHit: unpaid, the frame's kill cap, off in the
    // tutorial / during a death).
    void Burn()
    {
        if (!HurtsOtherEnemies || length <= 0f || !FriendlyFire.HostileFireAllowed) return;
        var live = ClearTarget.Live;
        var shooter = owner != null ? owner.gameObject : null;
        float lo = Mathf.Min(from.x, to.x), hi = Mathf.Max(from.x, to.x), half = HitThickness * .5f;
        for (int i = 0; i < live.Count; i++)
        {
            var c = live[i];
            if (c == null || !FriendlyFire.HostileFireCanHit(c, shooter)) continue;
            var go = c.gameObject;
            int id = go.GetInstanceID();
            if (AlreadyHit(id)) continue;
            Vector2 p = c.transform.position;
            float r = c.Radius * .8f;
            if (Mathf.Abs(p.y - y) > half + r || p.x + r < lo || p.x - r > hi) continue;
            // capped this frame: not spent, the beam tries again next frame
            if (!FriendlyFire.HostileHit(c, new Vector3(p.x, y, 0f), "mine laser")) return;
            if (hitCount < hit.Length) hit[hitCount++] = id;
            return;   // one a frame: the registry changes under a kill
        }
    }

    bool AlreadyHit(int id)
    {
        for (int i = 0; i < hitCount; i++) if (hit[i] == id) return true;
        return false;
    }

    static void Span(Transform tr, float w, float len)
    {
        tr.localPosition = new Vector3(0f, len * .5f, 0f);
        tr.localScale = new Vector3(w, Mathf.Max(.001f, len), 1f);
    }

    public void Recycle()
    {
        State = Phase.Off;
        owner = null;
        if (box != null) box.enabled = false;
        if (sight != null) sight.enabled = beam.enabled = flash.enabled = impact.enabled = false;
        gameObject.SetActive(false);
    }

    // collisionDetection's shielded path (EliteShip.ShieldRam) and a blink
    // (EliteShip.TeleportStrike): a mine laser's hitbox is absorbed /
    // erased, the beam ends. False if `go` is not one.
    public static bool EraseHitbox(GameObject go)
    {
        if (go == null || !go.TryGetComponent(out RailMineLaserHitbox hb)) return false;
        if (hb.laser != null) hb.laser.Erase();
        return true;
    }
}

// Marks a mine laser's hitbox (RailMineLaser.EraseHitbox, RamKill, DeathCrash).
public class RailMineLaserHitbox : MonoBehaviour
{
    public RailMineLaser laser;
}

// The mine laser's sprites for a world: the world boss's laser cells with
// their player-red pixels turned magenta, and the boss's flash / spark
// cells (which are drawn small, at the muzzle and the far rail). Built once
// per world and cached (rebuilt only if an editor scene swap destroyed them).
public sealed class MineLaserArt
{
    public const float MagentaHue = 312f / 360f;   // neon magenta: 44 deg clear of the player's red
    public Sprite sight, beam0, beam1;
    public readonly Sprite[] flash = new Sprite[BossAttackFx.FlashFrames];
    public readonly Sprite[] spark = new Sprite[2];

    static readonly MineLaserArt[] worlds = new MineLaserArt[4];

    public static MineLaserArt For(int world)
    {
        world = Mathf.Clamp(world, 0, worlds.Length - 1);
        var a = worlds[world];
        if (a != null && a.beam0 != null && a.flash[0] != null) return a;
        a = new MineLaserArt();
        var boss = BossCatalog.ForWorld(world);
        a.sight = Recolour(BossArt.Shot(boss, BossArt.Telegraph));
        a.beam0 = Recolour(BossArt.Shot(boss, BossArt.Beam0));
        a.beam1 = Recolour(BossArt.Shot(boss, BossArt.Beam0 + 1));
        for (int i = 0; i < a.flash.Length; i++) a.flash[i] = BossAttackFx.Get(boss, BossAttackFx.Flash0 + i);
        a.spark[0] = BossAttackFx.Get(boss, BossAttackFx.Spark0 + 1);
        a.spark[1] = BossAttackFx.Get(boss, BossAttackFx.Spark0 + 2);
        worlds[world] = a;
        return a;
    }

    // A copy of `cell` with every player-red pixel turned to MagentaHue.
    // (No red in it: the cell itself.)
    static Sprite Recolour(Sprite cell)
    {
        if (cell == null) return null;
        int w, h;
        var px = ShieldContour.ReadPixels(cell, out w, out h);
        if (px == null) return cell;
        bool changed = false;
        for (int i = 0; i < px.Length; i++)
        {
            Color c = px[i];
            if (c.a <= 0f || !HostileGlow.IsPlayerRed(c)) continue;
            Color.RGBToHSV(c, out float hh, out float ss, out float vv);
            var m = Color.HSVToRGB(MagentaHue, ss, vv);
            m.a = c.a;
            px[i] = m;
            changed = true;
        }
        if (!changed) return cell;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.name = cell.name + "MineLaser";
        tex.filterMode = cell.texture != null ? cell.texture.filterMode : FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.SetPixels32(px);
        tex.Apply(false, false);
        var s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), cell.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        s.name = tex.name;
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }
}

// The mine lasers, pooled and bounded: all Max built together on first use
// (so a later fire never allocates), reused.
public static class RailMineLasers
{
    public const int Max = 6;
    static readonly List<RailMineLaser> all = new List<RailMineLaser>(Max);
    static GameObject root;

    public static IReadOnlyList<RailMineLaser> All => all;
    public static int Created => all.Count;

    // A free laser, or null when all Max are busy (a busy screen skips one).
    public static RailMineLaser Take()
    {
        Prewarm();
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && !all[i].Active) return all[i];
        return null;
    }

    // Builds the pool (once per scene).
    public static void Prewarm()
    {
        if (root == null)
        {
            // a new scene took the old ones with it
            all.Clear();
            root = new GameObject("~RailMineLasers");
            while (all.Count < Max) all.Add(RailMineLaser.Create(root.transform));
        }
    }

    // Beams burning right now (the threat budget counts them as shots).
    public static int LiveBeams
    {
        get
        {
            int n = 0;
            for (int i = 0; i < all.Count; i++) if (all[i] != null && all[i].State == RailMineLaser.Phase.Beam) n++;
            return n;
        }
    }

    public static int ActiveCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < all.Count; i++) if (all[i] != null && all[i].Active) n++;
            return n;
        }
    }

    public static void Clear()
    {
        for (int i = 0; i < all.Count; i++) if (all[i] != null) all[i].Recycle();
    }
}
