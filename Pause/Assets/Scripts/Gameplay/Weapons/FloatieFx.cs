using UnityEngine;

// THE FEEL OF THE NORMAL GUN (the floaties' bullets, FloatieGuns.cs): the little things that make a shot land.
//
//   * HIT SPARK     a few hard pixels fly back off the point of impact (a kill, a hit, a stopped enemy shot alike)
//   * PIERCE STREAK a short forward streak where a piercing bullet passes through
//   * SPLIT BURST   a ring of pixels where a shot breaks into shards
//   * BOUNCE SPARK  a flick of pixels where a ricochet turns at the screen's edge
//   * MUZZLE FLASH  two tiny pixels at the drone's tip when it fires (subtle on purpose)
//   * NUDGE         a normal enemy that took a non-lethal hit is pushed a hair along the shot (a few hundredths of a unit, decaying)
//
// Pixel particles, hull nudges and muzzle flashes use fixed pools with no per-frame allocation.
// Heart breaks and kills have stronger bursts; upgrade volleys emphasize their muzzle flash.
// GunImpactFeedback supplies distinct rate-limited audio and a small bounded kill kick.
public static class FloatieFx
{
    public const int MaxParticles = 96, MaxNudges = 16;
    public const int SortOrder = 75;               // above the bullets (74) and every ultimate effect (<= 72), under the HUD
    public const float HitLife = .22f, MuzzleLife = .1f;
    public const float NudgeSpeed = 1.4f, NudgeDrag = 14f, NudgeMaxSeconds = .25f;   // ~0.1 world units in all

    public enum Kind { Hit, Pierce, Split, Bounce, Muzzle, HeartBreak, Kill, UpgradeMuzzle }

    struct Particle
    {
        public bool on;
        public Vector2 pos, vel;
        public float age, life, drag;
        public Color color;
    }
    struct Push { public Transform t; public Vector2 vel; public float age; }

    static readonly Particle[] parts = new Particle[MaxParticles];
    static readonly SpriteRenderer[] renderers = new SpriteRenderer[MaxParticles];
    static readonly Push[] pushes = new Push[MaxNudges];
    static Transform root;
    static Sprite pixel;
    static int lastTickFrame = -1;

    // Counters (tests, previews).
    public static int Sparks, Nudged;
    public static int ActiveParticles { get { int n = 0; for (int i = 0; i < parts.Length; i++) if (parts[i].on) n++; return n; } }
    public static int BuiltParticles { get { int n = 0; for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) n++; return n; } }

    static Sprite Pixel()
    {
        if (pixel != null) return pixel;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        tex.hideFlags = HideFlags.HideAndDontSave;
        for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++) tex.SetPixel(x, y, Color.white);
        tex.Apply(false, true);
        pixel = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f), 32f, 0, SpriteMeshType.FullRect);
        pixel.hideFlags = HideFlags.HideAndDontSave;
        return pixel;
    }

    // Builds the pool up front (idempotent; rebuilt when the scene that held it is gone).
    public static void Prebuild()
    {
        if (root == null)
        {
            root = new GameObject("~FloatieFx").transform;
            for (int i = 0; i < MaxParticles; i++) { renderers[i] = null; parts[i].on = false; }
            for (int i = 0; i < MaxNudges; i++) pushes[i].t = null;
        }
        for (int i = 0; i < MaxParticles; i++)
        {
            if (renderers[i] != null) continue;
            var go = new GameObject("~px");
            go.transform.SetParent(root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Pixel();
            sr.sortingOrder = SortOrder;
            go.SetActive(false);
            renderers[i] = sr;
        }
    }

    static int Free()
    {
        for (int i = 0; i < parts.Length; i++) if (!parts[i].on) return i;
        return -1;
    }

    static void Emit(Vector2 at, Vector2 vel, float life, float drag, Color c)
    {
        int i = Free();
        if (i < 0 || renderers[i] == null) return;
        parts[i] = new Particle { on = true, pos = at, vel = vel, age = 0f, life = life, drag = drag, color = c };
        var sr = renderers[i];
        sr.transform.position = new Vector3(at.x, at.y, 0f);
        sr.color = c;
        sr.gameObject.SetActive(true);
    }

    static Color Hot(Color tint, float towardWhite)
    {
        return new Color(Mathf.Lerp(tint.r, 1f, towardWhite), Mathf.Lerp(tint.g, 1f, towardWhite), Mathf.Lerp(tint.b, 1f, towardWhite), 1f);
    }

    // `heading` is the way the shot was flying. Particles spread with a fixed fan (no Random per call beyond a cheap hash).
    public static void Spark(Kind kind, Vector3 at, Vector3 heading, Color tint)
    {
        if (root == null) Prebuild();
        Sparks++;
        Vector2 h = heading.sqrMagnitude > 1e-6f ? (Vector2)heading.normalized : Vector2.up;
        Vector2 back = -h, side = new Vector2(-h.y, h.x);
        Color white = Hot(tint, .85f), mid = Hot(tint, .35f);
        switch (kind)
        {
            case Kind.Hit:
                for (int k = 0; k < 5; k++)
                {
                    float f = (k - 2) * .55f;
                    Vector2 d = (back + side * f).normalized;
                    Emit(at, d * (2.4f + (k % 2) * 1.1f), HitLife, 7f, k % 2 == 0 ? white : mid);
                }
                break;
            case Kind.HeartBreak:
            case Kind.Kill:
                int count = kind == Kind.Kill ? 10 : 7;
                for (int k = 0; k < count; k++)
                {
                    Vector2 d = (back + side * (k - (count - 1) * .5f) * .45f).normalized;
                    Emit(at, d * (3f + k % 3), kind == Kind.Kill ? .3f : .24f, 6f, k % 2 == 0 ? white : mid);
                }
                break;
            case Kind.UpgradeMuzzle:
                for (int k = 0; k < 6; k++) Emit(at, (h + side * (k - 2.5f) * .14f).normalized * 2.4f, .14f, 8f, white);
                break;
            case Kind.Pierce:
                for (int k = 0; k < 3; k++)
                    Emit(at, (h + side * (k - 1) * .18f).normalized * 4.2f, HitLife * .8f, 9f, k == 1 ? white : mid);
                break;
            case Kind.Split:
                for (int k = 0; k < 6; k++)
                {
                    float a = k * 60f * Mathf.Deg2Rad;
                    Emit(at, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 2.6f, HitLife, 8f, k % 2 == 0 ? white : mid);
                }
                break;
            case Kind.Bounce:
                for (int k = 0; k < 4; k++)
                    Emit(at, (h + side * (k - 1.5f) * .45f).normalized * (2f + k * .5f), HitLife, 8f, k % 2 == 0 ? white : mid);
                break;
            case Kind.Muzzle:
                Emit(at, h * 1.6f + side * .25f, MuzzleLife, 6f, white);
                Emit(at, h * 1.6f - side * .25f, MuzzleLife, 6f, mid);
                break;
        }
    }

    // A normal enemy takes a hair of push along the shot.
    public static void Nudge(Transform t, Vector3 along)
    {
        if (t == null) return;
        Vector2 d = along.sqrMagnitude > 1e-6f ? (Vector2)along.normalized : Vector2.up;
        int free = -1;
        for (int i = 0; i < pushes.Length; i++)
        {
            if (pushes[i].t == t) { free = i; break; }
            if (free < 0 && pushes[i].t == null) free = i;
        }
        if (free < 0) return;
        pushes[free] = new Push { t = t, vel = d * NudgeSpeed, age = 0f };
        Nudged++;
    }

    public static int ActiveNudges { get { int n = 0; for (int i = 0; i < pushes.Length; i++) if (pushes[i].t != null) n++; return n; } }

    // One step on the world's clock. Safe to call from several batteries in one frame (a frame plays once).
    public static void Tick(float dt)
    {
        if (dt <= 0f) return;
        if (Application.isPlaying)
        {
            if (lastTickFrame == Time.frameCount) return;
            lastTickFrame = Time.frameCount;
        }
        for (int i = 0; i < parts.Length; i++)
        {
            if (!parts[i].on) continue;
            parts[i].age += dt;
            var sr = renderers[i];
            if (sr == null) { parts[i].on = false; continue; }
            if (parts[i].age >= parts[i].life) { parts[i].on = false; sr.gameObject.SetActive(false); continue; }
            parts[i].vel *= Mathf.Max(0f, 1f - parts[i].drag * dt);
            parts[i].pos += parts[i].vel * dt;
            float k = parts[i].age / parts[i].life;
            var c = parts[i].color;
            c.a = 1f - k * k;
            sr.color = c;
            sr.transform.position = new Vector3(parts[i].pos.x, parts[i].pos.y, 0f);
            sr.transform.localScale = Vector3.one * (k < .55f ? 1f : .6f);
        }
        for (int i = 0; i < pushes.Length; i++)
        {
            if (pushes[i].t == null) continue;
            var t = pushes[i].t;
            // (a destroyed enemy's transform compares null: the slot frees itself)
            if ((object)t == null || !t) { pushes[i].t = null; continue; }
            pushes[i].age += dt;
            if (pushes[i].age >= NudgeMaxSeconds) { pushes[i].t = null; continue; }
            pushes[i].vel *= Mathf.Max(0f, 1f - NudgeDrag * dt);
            t.position += (Vector3)(pushes[i].vel * dt);
        }
    }

    public static void Clear()
    {
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i].on = false;
            if (renderers[i] != null) renderers[i].gameObject.SetActive(false);
        }
        for (int i = 0; i < pushes.Length; i++) pushes[i].t = null;
    }
}
