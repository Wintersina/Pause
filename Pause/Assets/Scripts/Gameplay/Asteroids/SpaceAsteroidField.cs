using System.Collections.Generic;
using UnityEngine;

// SPACE ASTEROID FIELD: the gameplay rocks of the Space world. They are a foreground-lane layer of their own; the
// far-parallax rocks of the backdrop (SpaceAsteroidDrift) are pure decoration: no collider, never hit, never targeted.
//
// A rock is a real hazard like any other "Astr" body, so every existing system already knows it:
//   * touching it costs a heart (collisionDetection, unshielded) or smashes it (shielded / ramming), like a hazard rock
//   * the ship's attacks and the ultimate lock onto it through ClearTarget; it takes SizeHits worth of hits
//     (IShipAttackTarget) and then bursts in the rock explosion, paying the ordinary rock score
//   * a large rock breaks into two medium ones, a medium into two small ones (Split), a small one just bursts
//
// It comes at the player from the TOP at a share of the board's scroll, tumbling, in full colour (the greyed,
// bigger backdrop rocks of SpaceAsteroidDrift cross the sky sideways and are harmless, so the two never get
// confused). Each rock picks one of two patterns when it is made: Straight (straight down the lane) or ZigZag
// (a sine weave across the lane while it descends, ZigAmp wide, ZigPeriod seconds per sway, kept inside the rails).
// A chunk of a split just flies straight with its parting kick. It leaves once it is past the view. FieldDirector decides when one turns up; the rules (Blocked) keep it away from bosses, elites,
// transitions and the calm start, and at a modest rate (Gap) so the lane stays fair and dodgeable.
public static class SpaceAsteroidField
{
    // ---- tunables ----
    public const int SpaceWorld = 0;                        // WorldManager index of Space
    public const int MaxAlive = 3;                          // rocks (whole ones from the director) on screen at once
    public const int MaxTotal = 6;                          // ... counting the chunks of a split
    public const float FirstSeconds = 25f;                  // none in the first 25 s of the world (the 8 s calm start is inside it)
    public const float BossLeadSeconds = 14f;              // none in the run-up to a boss
    public static readonly Vector2 Gap = new Vector2(9f, 16f);   // seconds between arrivals
    public const float SpawnMargin = .5f;                   // enters this far above the view
    public const float LaneShare = .8f;                     // x of a new rock: within this share of the rail-to-rail half width
    public const float ClearRadius = 1.3f;                  // a new rock keeps this far from any other hazard

    // size tiers: 0 small, 1 medium, 2 large
    public static readonly float[] Width = { HazardSize.MinDrawnWidth, .85f, 1.25f };   // drawn width (u); the small tier is the shared minimum
    public static readonly float[] SizeHits = { 1f, 2f, 3f };               // full ship-attack hits to break it
    public static readonly float[] TierWeight = { .35f, .45f, .20f };       // what the director draws
    public const float ScrollShare = .55f;                  // share of the board's scroll it falls at
    public const float ScrollK = 30f;                       // the board's scroll is speed * 30 u/s (moveItemEnmInStrightLine)
    public const int Straight = 0, ZigZag = 1;              // motion patterns
    public const float ZigChance = .5f;                     // share of the director's rocks that weave
    public static readonly Vector2 ZigAmp = new Vector2(.45f, 1.0f);        // u, half-width of the weave
    public static readonly Vector2 ZigPeriod = new Vector2(3f, 5f);         // s per full sway
    public const float ZigEdgeMargin = .35f;                // a weaving rock stays this far (x its width) inside the rail face
    public static readonly Vector2 Spin = new Vector2(15f, 55f);            // deg/s, either way
    public const float WholeFill = .86f;                    // the footprint share of a drawn width (the old backdrop rocks' fill)
    public const float ColliderShare = .38f;                // collider radius as a share of the drawn width (the footprint is WholeFill of it; measured per type)
    public const float SplitKick = .55f;                    // u/s the two chunks part by
    public const float HitFlashSeconds = .09f;
    public const int SortingOrder = 2;                      // with the hazard rocks (EnemyFactory)
    public const string ObjectName = "space_asteroid";           // (a rock is named by its type: SpaceRockType.objectName)

    // test / tool switches
    public static bool Disabled;
    public static int SeedOverride = -1;
    public static int Spawned, Broken, Splits;              // (tests, tuning)

    static readonly List<FieldAsteroid> live = new List<FieldAsteroid>();
    public static IList<FieldAsteroid> Live { get { return live; } }
    public static void Register(FieldAsteroid a) { if (!live.Contains(a)) live.Add(a); }
    public static void Unregister(FieldAsteroid a) { live.Remove(a); }

    public static int Alive
    {
        get { int n = 0; for (int i = 0; i < live.Count; i++) if (live[i] != null && !live[i].Broken) n++; return n; }
    }

    // ---- art: the four hazard rock types (SpaceRockTypes: ice, molten, iron, geode), a 4-frame blink strip each ----
    public static int TypeCount { get { return SpaceRockTypes.Count; } }
    public static Sprite[] Art(int type, bool chunk)
    {
        var t = SpaceRockTypes.Of(type);
        return chunk ? t.Chunk : t.Whole;
    }

    public static void Forget() { SpaceRockTypes.Forget(); live.Clear(); }

    // The rails' drawn inner face: a rock is kept within it.
    public static float Edge => BossRails.DrawnInnerEdge;

    // A tier from a roll in [0, 1).
    public static int TierFor(float u)
    {
        for (int i = 0; i < TierWeight.Length; i++)
        {
            u -= TierWeight[i];
            if (u < 0f) return i;
        }
        return TierWeight.Length - 1;
    }

    // Builds one rock. `vx` / `vy` are its own velocity on top of the board's scroll.
    // `artIndex` picks the rock type (mod the four types); `chunk` draws that type's chunk art (what a split leaves).
    public static FieldAsteroid Spawn(int tier, int artIndex, Vector2 at, float vx, float vy, float spinDegrees, bool chunk = false)
    {
        return Spawn(tier, artIndex, at, vx, vy, spinDegrees, 0f, 0f, 0f, chunk);
    }

    // The same, weaving: a sine sway of `zigAmp` u half-width, `zigPeriod` s per sway, starting at `zigPhase` radians.
    public static FieldAsteroid Spawn(int tier, int artIndex, Vector2 at, float vx, float vy, float spinDegrees,
                                      float zigAmp, float zigPeriod, float zigPhase, bool chunk = false)
    {
        int type = ((artIndex % TypeCount) + TypeCount) % TypeCount;
        var rock = SpaceRockTypes.Of(type);
        var sprites = Art(type, chunk);
        if (sprites == null || sprites.Length == 0) return null;
        tier = Mathf.Clamp(tier, 0, Width.Length - 1);
        var go = new GameObject(rock.objectName);
        go.tag = "Astr";
        go.transform.position = new Vector3(at.x, at.y, 0f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprites[0];
        sr.sortingOrder = SortingOrder;
        float w = Width[tier];
        // the drawn width is the rock's visible footprint (alpha), so a small chunk frame is scaled up to match
        float scale = w * WholeFill / (Mathf.Max(.01f, sr.sprite.bounds.size.x) * rock.Fill(chunk));
        go.transform.localScale = new Vector3(scale, scale, 1f);
        go.transform.rotation = Quaternion.Euler(0f, 0f, (artIndex * 97) % 360);
        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = ColliderShare * w / scale;     // (collider units are local)
        var a = go.AddComponent<FieldAsteroid>();
        a.Init(tier, vx, vy, spinDegrees, type, chunk, sprites);
        if (zigAmp > 0f && zigPeriod > .1f) a.SetZigZag(zigAmp, zigPeriod, zigPhase);
        ClearTarget.Ensure(go);
        Spawned++;
        return a;
    }

    // Dismisses every rock quietly (a world change, a death, a boss arriving, the scene).
    public static void ClearAll()
    {
        for (int i = live.Count - 1; i >= 0; i--)
            if (live[i] != null) BossUtil.Kill(live[i].gameObject);
        live.Clear();
    }
}

// One rock of the field. It takes every weapon hit in full (IShipAttackTargetUncapped), like the backdrop
// rocks: it is an ordinary hazard that happens to need more than one hit, not an elite, so it never spends the
// firing's elite allowance (a rail through two rocks hits both; a rock in front of an elite leaves the elite
// its hit). To the secret powers it is ordinary debris too (SecretPowerController.PowerSpecial / PowerKill).
public class FieldAsteroid : MonoBehaviour, IShipAttackTargetUncapped
{
    public int Tier { get; private set; }
    public int Type { get; private set; }           // SpaceRockTypes index: 0 ice, 1 molten, 2 iron, 3 geode
    public bool IsChunk { get; private set; }       // the chunk art of a split
    public int Frame { get; private set; }          // the blink strip's frame now
    Sprite[] frames;
    float blinkClock;
    public bool Broken { get; private set; }
    public float Health { get; private set; }
    public float Vx { get; private set; }
    public float Vy { get; private set; }
    public int Pattern { get; private set; }        // SpaceAsteroidField.Straight / ZigZag
    public float ZigAmp { get; private set; }
    float zigRate, zigPhase, zigAge;

    static readonly Color HitBlink = new Color(.55f, .55f, .65f, 1f);   // a hit dims it for a blink
    float spin, flash;
    SpriteRenderer sr;

    public void Init(int tier, float vx, float vy, float spinDegrees, int type = 0, bool chunk = false, Sprite[] blink = null)
    {
        Tier = tier;
        Type = type; IsChunk = chunk; frames = blink;
        // a random phase per rock, so they don't pulse in sync
        blinkClock = Random.value * SpaceRockTypes.BlinkFrames / SpaceRockTypes.BlinkFps;
        Vx = vx; Vy = vy;
        spin = spinDegrees;
        Health = SpaceAsteroidField.SizeHits[tier];
        sr = GetComponent<SpriteRenderer>();
        SpaceAsteroidField.Register(this);
    }

    // Makes it weave: x follows base + amp * sin(rate * t + phase) (integrated as a velocity, so it starts where it is).
    public void SetZigZag(float amp, float period, float phase)
    {
        Pattern = SpaceAsteroidField.ZigZag;
        ZigAmp = amp;
        zigRate = 6.2831853f / period;
        zigPhase = phase;
    }

    // Its sideways speed right now: its own heading plus the weave.
    public float SideSpeed => Pattern == SpaceAsteroidField.ZigZag ? Vx + ZigAmp * zigRate * Mathf.Cos(zigAge * zigRate + zigPhase) : Vx;

    void OnDestroy() { SpaceAsteroidField.Unregister(this); }

    static bool Flying => !buttonClicks.playerDied && (TouchInput.IsPressed || score.pauseCounter <= 0);

    void Update()
    {
        if (Flying) Step(Time.deltaTime, moveBackGround.speed, CameraFit.ViewBottom);
    }

    // One running frame (dt, board speed and view bottom explicit for tests). False once it has left.
    public bool Step(float dt, float boardSpeed, float viewBottom)
    {
        if (dt <= 0f || Broken) return !Broken;
        var tr = transform;
        Vector3 p = tr.position;
        p.x += SideSpeed * dt;
        if (Pattern == SpaceAsteroidField.ZigZag)
        {
            zigAge += dt;
            float lim = Mathf.Max(.2f, SpaceAsteroidField.Edge - SpaceAsteroidField.Width[Tier] * SpaceAsteroidField.ZigEdgeMargin);
            p.x = Mathf.Clamp(p.x, -lim, lim);
        }
        p.y += (Vy - boardSpeed * SpaceAsteroidField.ScrollK * SpaceAsteroidField.ScrollShare) * dt;
        tr.position = p;
        tr.Rotate(0f, 0f, spin * dt, Space.World);
        Blink(dt);
        if (flash > 0f)
        {
            flash -= dt;
            if (sr != null) sr.color = flash > 0f ? HitBlink : Pulse();
        }
        float reach = SpaceAsteroidField.Width[Tier];
        if (p.y < viewBottom - reach - 1f || Mathf.Abs(p.x) > SpaceAsteroidField.Edge + reach + 1f)
        {
            SpaceAsteroidField.Unregister(this);
            if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
            return false;
        }
        return true;
    }

    // The glow's loop: dim, mid, bright, mid at BlinkFps. No allocation: the Sprite[] is shared per type.
    void Blink(float dt)
    {
        if (frames == null || frames.Length == 0 || sr == null) return;
        blinkClock += dt;
        int f = (int)(blinkClock * SpaceRockTypes.BlinkFps) % frames.Length;
        if (f == Frame && sr.sprite == frames[f]) return;
        Frame = f;
        sr.sprite = frames[f];
        if (flash <= 0f) sr.color = Pulse();
    }

    // The strips carry the glow pulse themselves: no code tint (a hit flash aside).
    static Color Pulse() { return Color.white; }

    // A ship attack lands (IShipAttackTarget): `weight` is its share of one full hit.
    public void TakeShipAttack(int ship, float weight, Vector3 at)
    {
        if (Broken) return;
        Health -= weight;
        flash = SpaceAsteroidField.HitFlashSeconds;
        if (sr != null) sr.color = HitBlink;
        if (Health <= .0001f) Break(ship);
    }

    // Bursts like any destroyed rock (explosion, sound, score, codex, meter), then splits.
    public void Break(int ship)
    {
        if (Broken) return;
        Broken = true;
        SpaceAsteroidField.Broken++;
        var from = transform.position;
        TargetExplosion.Spawn(gameObject, ship);
        EnemyDeathAudio.Play(gameObject);
        collisionDetection.AwardDestroyedTarget(gameObject);
        ClearTarget.Release(gameObject);
        Split(from);
        SpaceAsteroidField.Unregister(this);
        if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
    }

    // Removed with no credit and no split (the Hazard Clear booster, HazardClear): no score, codex, meter or dust.
    public void Vanish()
    {
        if (Broken) return;
        Broken = true;
        ClearTarget.Release(gameObject);
        SpaceAsteroidField.Unregister(this);
        if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
    }

    // Two chunks one size down, parting sideways (when there is room in the cap).
    void Split(Vector3 from)
    {
        if (Tier <= 0) return;
        if (SpaceAsteroidField.Live.Count + 1 > SpaceAsteroidField.MaxTotal) return;   // (this rock leaves, two come)
        SpaceAsteroidField.Splits++;
                float kick = SpaceAsteroidField.SplitKick;
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            var at = new Vector2(from.x + side * SpaceAsteroidField.Width[Tier - 1] * .35f, from.y);
            SpaceAsteroidField.Spawn(Tier - 1, Type, at, Vx * .5f + side * kick, Vy, spin * -side * 1.3f, true);   // (the chunks keep the type)
        }
    }
}
