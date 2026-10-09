using UnityEngine;

// The FRIENDLY look every pickup wears, so an atom can never be read as a
// hostile shot ("I keep mistaking enemy bullets for atoms"):
//
//   pickups (friendly)                 hostile shots (EliteShot, ShotOutline)
//   -------------------------------    ----------------------------------------
//   soft round halo in the atom's      a hard traced outline, no halo
//   own hue, bleeding well past it
//   a dashed orbit ring turning        pointed darts / chevrons / spiked burrs
//   round it (atoms only)              facing their flight
//   cyan / green / red / violet /      one hostile family: magenta-pink
//   amber                              (HostileShotPalette, 312-326 deg)
//   a slow, smooth breath (scale and   a hard stepped flicker of the core
//   alpha) and a slow spin
//   drawn PickupArt.AtomVisualScale    unchanged sizes and hitboxes
//   bigger (hitbox unchanged)
//
// Two child SpriteRenderers (halo, ring) with two tiny shared textures made
// in code once; Update only writes a scale, a rotation and a colour: no
// allocation per frame. Runs on the world's scaled clock, so the breath
// freezes with the world when the finger lifts (score.pauseCounter freeze).
public class PickupGlow : MonoBehaviour
{
    // ---- tuning ----
    public const float HaloDiameter = 2.3f;     // x the atom's body diameter
    public const float DustHaloDiameter = 2.0f; // x star dust's
    public const float RingDiameter = 1.62f;    // x the atom's body diameter
    public const float HaloAlpha = .62f;        // peak of the halo's centre
    public const float RingAlpha = .9f;
    public const float BreathHz = .75f;         // one slow breath every 1.33 s
    public const float BreathScale = .12f;      // halo grows and shrinks this share
    public const float BreathAlpha = .25f;      // and dims this share
    public const float RingDegreesPerSecond = -70f;
    public const int HaloOrder = -2, RingOrder = -1;   // relative to the atom's own sorting order

    public PickupKind Kind { get; private set; }
    public SpriteRenderer Halo { get; private set; }
    public SpriteRenderer Ring { get; private set; }
    public Color Tint { get; private set; }
    public float Age => age;

    float age, haloBase, ringBase;
    static Sprite haloSprite, ringSprite;

    // The friendly hue of each pickup (its art's light tone).
    public static Color TintFor(PickupKind kind)
    {
        switch (kind)
        {
            case PickupKind.Shield: return AkiraPalette.Cyan;
            case PickupKind.Pause: return AkiraPalette.RedHi;
            case PickupKind.Cooldown: return AkiraPalette.VioletHi;
            case PickupKind.Heal: return HealGreen;
            default: return AkiraPalette.Amber;
        }
    }

    public static readonly Color HealGreen = new Color(.706f, .949f, .275f);   // #B4F246, the heal ramp's light

    public static bool IsAtom(PickupKind kind) => kind != PickupKind.Dust && kind != PickupKind.DustSmall;

    // Dresses a freshly spawned pickup in the friendly look: an atom is
    // drawn PickupArt.AtomVisualScale bigger (its colliders shrunk back, so
    // what it takes to collect it is unchanged), then the halo and ring.
    // Idempotent.
    public static PickupGlow Dress(GameObject go)
    {
        if (go == null) return null;
        var g = go.GetComponent<PickupGlow>();
        if (g != null) return g;
        PickupKind kind;
        if (PickupArt.TryKindOf(go, out kind) && IsAtom(kind)) PickupArt.EnlargeVisual(go, PickupArt.AtomVisualScale);
        return Ensure(go);
    }

    // Adds (once) the halo, and for an atom the orbit ring, to a pickup.
    public static PickupGlow Ensure(GameObject go)
    {
        if (go == null) return null;
        var g = go.GetComponent<PickupGlow>();
        if (g != null) return g;
        PickupKind kind;
        if (!PickupArt.TryKindOf(go, out kind)) return null;
        var sr = go.GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return null;
        g = go.AddComponent<PickupGlow>();
        g.Build(kind, sr);
        return g;
    }

    void Build(PickupKind kind, SpriteRenderer body)
    {
        Kind = kind;
        Tint = TintFor(kind);
        // the body's diameter in the root's local units (sprite bounds are local)
        float local = Mathf.Max(body.sprite.bounds.size.x, body.sprite.bounds.size.y);
        bool atom = IsAtom(kind);
        haloBase = local * (atom ? HaloDiameter : DustHaloDiameter);
        Halo = Child("PickupHalo", HaloSprite, body.sortingOrder + HaloOrder, haloBase);
        if (atom)
        {
            ringBase = local * RingDiameter;
            Ring = Child("PickupRing", RingSprite, body.sortingOrder + RingOrder, ringBase);
        }
        Apply();
    }

    SpriteRenderer Child(string name, Sprite sprite, int order, float diameter)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * diameter;   // the sprites are one unit across
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        return sr;
    }

    void Update()
    {
        Advance(Time.deltaTime);
    }

    // Exposed for tests and previews: advance by dt seconds of world time.
    public void Advance(float dt)
    {
        if (dt <= 0f) return;
        age += dt;
        Apply();
    }

    // The breath at `age`: 0 (exhaled) .. 1 (inhaled), a smooth sine.
    public static float BreathAt(float age) => .5f + .5f * Mathf.Sin(age * BreathHz * 2f * Mathf.PI);

    void Apply()
    {
        float b = BreathAt(age);
        if (Halo != null)
        {
            Halo.transform.localScale = Vector3.one * (haloBase * (1f - BreathScale * .5f + BreathScale * b));
            var c = Tint;
            c.a = HaloAlpha * (1f - BreathAlpha + BreathAlpha * b);
            Halo.color = c;
        }
        if (Ring != null)
        {
            // counter to AtomSpin's turn, so the ring visibly orbits
            Ring.transform.localRotation = Quaternion.Euler(0f, 0f, age * RingDegreesPerSecond);
            var c = Color.Lerp(Tint, Color.white, .35f);
            c.a = RingAlpha;
            Ring.color = c;
        }
    }

    // ---- the shared drawings ----

    // A soft round glow, one unit across: bright centre, smooth falloff to
    // nothing at the rim (bilinear: the one soft thing on the board).
    public static Sprite HaloSprite
    {
        get
        {
            if (haloSprite != null) return haloSprite;
            const int n = 64;
            var tex = NewTex(n, "PickupHalo", FilterMode.Bilinear);
            var px = new Color32[n * n];
            float c = (n - 1) * .5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / (n * .5f);
                    float a = d >= 1f ? 0f : Mathf.Pow(1f - d, 1.8f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * a));
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            haloSprite = MakeSprite(tex, n);
            return haloSprite;
        }
    }

    // A dashed orbit, one unit across: three arcs with gaps, crisp pixels.
    public static Sprite RingSprite
    {
        get
        {
            if (ringSprite != null) return ringSprite;
            const int n = 48;
            var tex = NewTex(n, "PickupRing", FilterMode.Point);
            var px = new Color32[n * n];
            float c = (n - 1) * .5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x - c, dy = y - c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 360f;
                    bool arc = (ang % 120f) < 84f;
                    bool band = r >= n * .5f - 2.6f && r <= n * .5f - .6f;
                    // a brighter "electron" head at the end of each arc
                    bool head = arc && (ang % 120f) > 72f && r >= n * .5f - 3.6f && r <= n * .5f + .2f;
                    byte a = (byte)(band && arc ? 255 : head ? 255 : 0);
                    px[y * n + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            ringSprite = MakeSprite(tex, n);
            return ringSprite;
        }
    }

    static Texture2D NewTex(int n, string name, FilterMode filter)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        tex.name = name;
        tex.filterMode = filter;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.HideAndDontSave;
        return tex;
    }

    static Sprite MakeSprite(Texture2D tex, int n)
    {
        var s = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), n, 0, SpriteMeshType.FullRect);
        s.name = tex.name;
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }
}
