using System.Collections.Generic;
using UnityEngine;

// Pools for the ultimate's homing shots and for every flipbook effect a
// player weapon spawns (weapon impact bursts, target explosions and their
// tinted flash / shockwave overlays).
//
// Both pools grow only up to the peak number in flight at once and are then
// reused, with hard caps: an over-cap shot lands instantly (its target still
// dies, it just isn't drawn) and an over-cap flipbook steals the oldest one.
// Nothing here allocates per frame. The pool lives under a "~WeaponFx" scene
// object, so a scene load simply starts it over.
public static class WeaponFx
{
    public const int MaxShots = 48;
    public const int MaxFlipbooks = 72;

    static readonly List<WeaponShot> shots = new List<WeaponShot>();
    static readonly List<FlipbookFx> flipbooks = new List<FlipbookFx>();
    static Transform root;

    static Transform Root
    {
        get
        {
            if (root == null)
            {
                root = new GameObject("~WeaponFx").transform;
                shots.Clear();
                flipbooks.Clear();
            }
            return root;
        }
    }

    public static int ShotPoolSize { get { Purge(); return shots.Count; } }
    public static int FlipbookPoolSize { get { Purge(); return flipbooks.Count; } }

    public static int ActiveShots
    {
        get
        {
            int n = 0;
            for (int i = 0; i < shots.Count; i++) if (shots[i] != null && shots[i].Active) n++;
            return n;
        }
    }

    public static int ActiveFlipbooks
    {
        get
        {
            int n = 0;
            for (int i = 0; i < flipbooks.Count; i++) if (flipbooks[i] != null && flipbooks[i].Active) n++;
            return n;
        }
    }

    // Active flipbooks of one mode (no allocation).
    public static int ActiveOf(FlipbookFx.Mode mode)
    {
        int n = 0;
        for (int i = 0; i < flipbooks.Count; i++)
        {
            var f = flipbooks[i];
            if (f != null && f.Active && f.CurrentMode == mode) n++;
        }
        return n;
    }

    // Edit-mode tests open fresh scenes under a static pool; drop whatever
    // the editor already destroyed.
    static void Purge()
    {
        if (root == null) { shots.Clear(); flipbooks.Clear(); return; }
        for (int i = shots.Count - 1; i >= 0; i--) if (shots[i] == null) shots.RemoveAt(i);
        for (int i = flipbooks.Count - 1; i >= 0; i--) if (flipbooks[i] == null) flipbooks.RemoveAt(i);
    }

    // Returns null when every pooled shot is in flight and the cap is hit.
    public static WeaponShot Launch(int ship, Vector3 from, Transform target, float maxSeconds, System.Action onHit)
    {
        var parent = Root;
        Purge();
        WeaponShot shot = null;
        for (int i = 0; i < shots.Count; i++)
            if (!shots[i].Active) { shot = shots[i]; break; }
        if (shot == null)
        {
            if (shots.Count >= MaxShots)
            {
                if (target != null) onHit?.Invoke();
                return null;
            }
            var go = new GameObject("~shot");
            go.transform.SetParent(parent, false);
            shot = go.AddComponent<WeaponShot>();
            shot.Build();
            shots.Add(shot);
        }
        shot.Launch(ship, from, target, maxSeconds, onHit);
        return shot;
    }

    public static FlipbookFx Flipbook()
    {
        var parent = Root;
        Purge();
        FlipbookFx oldest = null;
        for (int i = 0; i < flipbooks.Count; i++)
        {
            var f = flipbooks[i];
            if (!f.Active) return f;
            if (oldest == null || f.StartedAt < oldest.StartedAt) oldest = f;
        }
        if (flipbooks.Count >= MaxFlipbooks && oldest != null) return oldest;
        var go = new GameObject("~fxbook", typeof(SpriteRenderer));
        go.transform.SetParent(parent, false);
        var fx = go.AddComponent<FlipbookFx>();
        fx.Build();
        flipbooks.Add(fx);
        return fx;
    }

    // The ship's weapon burst where a shot connects.
    public static void Impact(int ship, Vector3 at, float worldSize)
    {
        Flipbook().Play(FlipbookFx.Mode.WeaponImpact, at, worldSize, ship, TargetExplosion.Kind.Metal,
                        Color.white, 1f, 74);
    }

    static Sprite solid;
    // 1x1 world unit white square (pixelsPerUnit == texture size).
    public static Sprite Solid
    {
        get
        {
            if (solid != null) return solid;
            const int S = 4;
            var tex = new Texture2D(S, S);
            var px = new Color[S * S];
            for (int i = 0; i < px.Length; i++) px[i] = Color.white;
            tex.SetPixels(px);
            tex.Apply();
            solid = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(.5f, .5f), S);
            return solid;
        }
    }
}

// One homing shot: a flipbook body that steers into its target, a long
// hard-edged light streak behind it (weapon colour / energy / BONE core
// bands, the Akira bike tail-light smeared across the frame -- a light, so no
// ink), and little trail emblems dropped along the way. Runs on unscaled time
// so it stays readable through the cinematic slow motion.
public class WeaponShot : MonoBehaviour
{
    public const float BodySize = .5f;
    const int Segments = 8;
    const float SampleSpacing = .1f;
    const float TrailWidth = .13f;
    const int PuffCount = 4;
    const float PuffEvery = .055f, PuffLife = .32f, PuffSize = .2f;
    static readonly Color Bone = new Color(.957f, .918f, .831f, 1f); // #F4EAD4

    SpriteRenderer body;
    SpriteRenderer[] outer, energy, core;
    SpriteRenderer[] puffs;
    float[] puffAge;
    readonly Vector3[] hist = new Vector3[Segments + 1];
    int histCount;

    Transform target;
    System.Action onHit;
    int ship;
    WeaponStyle style;
    Vector3 heading;
    float age, maxSeconds, spinAngle, puffTimer;
    int nextPuff;

    public bool Active { get; private set; }
    public int Ship => ship;
    public Sprite BodySprite => body != null ? body.sprite : null;

    public void Build()
    {
        var bodyGo = new GameObject("Body", typeof(SpriteRenderer));
        bodyGo.transform.SetParent(transform, false);
        body = bodyGo.GetComponent<SpriteRenderer>();
        body.sortingOrder = 72;
        outer = Layer("Outer", 70);
        energy = Layer("Energy", 70);
        core = Layer("Core", 71);
        puffs = new SpriteRenderer[PuffCount];
        puffAge = new float[PuffCount];
        for (int i = 0; i < PuffCount; i++)
        {
            var go = new GameObject("Puff", typeof(SpriteRenderer));
            go.transform.SetParent(transform, false);
            puffs[i] = go.GetComponent<SpriteRenderer>();
            puffs[i].sortingOrder = 68;
            puffs[i].enabled = false;
        }
        gameObject.SetActive(false);
    }

    SpriteRenderer[] Layer(string name, int order)
    {
        var layer = new SpriteRenderer[Segments];
        for (int i = 0; i < Segments; i++)
        {
            var go = new GameObject(name, typeof(SpriteRenderer));
            go.transform.SetParent(transform, false);
            layer[i] = go.GetComponent<SpriteRenderer>();
            layer[i].sprite = WeaponFx.Solid;
            // later segments draw under earlier ones so the head stays on top
            layer[i].sortingOrder = order;
            layer[i].enabled = false;
        }
        return layer;
    }

    public void Launch(int shipIndex, Vector3 from, Transform hitTarget, float seconds, System.Action hit)
    {
        ship = shipIndex;
        style = WeaponStyleTable.For(shipIndex);
        target = hitTarget;
        onHit = hit;
        maxSeconds = seconds;
        age = 0f;
        spinAngle = 0f;
        puffTimer = 0f;
        nextPuff = 0;
        heading = Vector3.up;
        transform.position = from;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;
        for (int i = 0; i < hist.Length; i++) hist[i] = from;
        histCount = 1;
        for (int i = 0; i < PuffCount; i++) { puffs[i].enabled = false; puffAge[i] = PuffLife; }
        Tint(outer, style.main);
        Tint(energy, style.energy);
        Tint(core, Bone);
        Active = true;
        gameObject.SetActive(true);
        body.sprite = WeaponArt.Shot(ship, WeaponArt.ShotLoopFrames); // launch smear
        Draw(0f);
    }

    static void Tint(SpriteRenderer[] layer, Color c)
    {
        for (int i = 0; i < layer.Length; i++) layer[i].color = c;
    }

    void Update()
    {
        if (Active) Tick(Time.unscaledDeltaTime);
    }

    public void Tick(float dt)
    {
        if (!Active) return;
        age += dt;
        if (target == null) { Finish(false); return; }
        Vector3 pos = transform.position;
        Vector3 to = target.position - pos;
        to.z = 0f;
        if (to.sqrMagnitude <= .018f || age >= maxSeconds) { Finish(true); return; }

        Vector3 before = heading;
        heading = PowerFx.SteerHeading(heading, to, 235f, dt);
        Vector3 side = new Vector3(-heading.y, heading.x, 0f);
        float weave = style.weaveAmplitude * Mathf.Cos(age * style.weaveFrequency) * style.weaveFrequency * .12f;
        Vector3 velocity = heading * style.speed + side * weave;
        transform.position = pos + velocity * dt;

        float turnRate = dt > 0f ? Vector3.Angle(before, heading) / dt : 0f;
        // Smear frames on the launch and whenever it whips round a corner.
        bool smear = age < .07f || turnRate > 420f;
        int frame = smear
            ? WeaponArt.ShotLoopFrames + (Mathf.FloorToInt(age * 30f) & 1)
            : WeaponArt.FrameAt(LoopTicks, age, true);
        body.sprite = WeaponArt.Shot(ship, frame);
        Draw(dt);
        if (velocity.sqrMagnitude > .0001f && style.shotSpin == 0f)
            body.transform.up = velocity.normalized;
    }

    // the flight loop part of WeaponArt.ShotTicks (the smear frames follow it)
    static readonly int[] LoopTicks = { 2, 2, 2, 2 };

    void Draw(float dt)
    {
        // body: the stretch is in the smear drawings; spin is motion
        body.transform.localScale = Vector3.one * BodySize;
        if (style.shotSpin != 0f)
        {
            spinAngle += style.shotSpin * dt;
            body.transform.rotation = Quaternion.Euler(0f, 0f, spinAngle);
        }

        // light streak history
        Vector3 p = transform.position;
        hist[0] = p;
        if ((hist[0] - hist[1]).sqrMagnitude >= SampleSpacing * SampleSpacing || histCount == 1)
        {
            for (int i = hist.Length - 1; i > 0; i--) hist[i] = hist[i - 1];
            if (histCount < hist.Length) histCount++;
        }
        for (int i = 0; i < Segments; i++)
        {
            bool on = i + 1 < histCount;
            float taper = 1f - i / (float)Segments;
            Segment(outer[i], on, hist[i], hist[i + 1], TrailWidth * taper);
            Segment(energy[i], on, hist[i], hist[i + 1], TrailWidth * taper * .55f);
            Segment(core[i], on && i < Segments / 2, hist[i], hist[i + 1], TrailWidth * taper * .22f);
        }

        // trail emblems
        puffTimer += dt;
        if (puffTimer >= PuffEvery && dt > 0f)
        {
            puffTimer = 0f;
            var r = puffs[nextPuff];
            puffAge[nextPuff] = 0f;
            r.transform.position = p;
            r.sprite = WeaponArt.Trail(ship, nextPuff & 1);
            r.enabled = true;
            nextPuff = (nextPuff + 1) % PuffCount;
        }
        for (int i = 0; i < PuffCount; i++)
        {
            if (!puffs[i].enabled) continue;
            puffAge[i] += dt;
            float k = puffAge[i] / PuffLife;
            if (k >= 1f) { puffs[i].enabled = false; continue; }
            // held for a beat, then snaps smaller in two steps (cel timing)
            float s = k < .4f ? 1f : k < .75f ? .7f : .4f;
            puffs[i].transform.localScale = Vector3.one * PuffSize * s;
        }
    }

    static void Segment(SpriteRenderer r, bool on, Vector3 a, Vector3 b, float width)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        if (!on || len < .0005f) { r.enabled = false; return; }
        r.enabled = true;
        var t = r.transform;
        t.position = (a + b) * .5f;
        t.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        t.localScale = new Vector3(len + width * .5f, width, 1f);
    }

    // Lands (hit) or fizzles (its target is already gone). Either way the
    // ship's own impact burst plays and the shot goes back to the pool.
    public void Finish(bool hit)
    {
        if (!Active) return;
        Active = false;
        Vector3 at = transform.position;
        var callback = onHit;
        onHit = null;
        bool landed = hit && target != null;
        target = null;
        WeaponFx.Impact(ship, at, landed ? .8f : .45f);
        gameObject.SetActive(false);
        if (landed) callback?.Invoke();
    }
}

// A pooled, flipbook-driven sprite: a weapon's impact burst, a target
// explosion, or one of the explosion's tinted overlays. Frames are held for
// the art's tick table (24 fps); squash and stretch are drawn into the frames,
// only the shockwave ring is scaled here (it is motion, not a pose). Explosions and impacts
// run on gameplay time (TargetExplosion.Delta), so they freeze with the world
// when it pauses, and drift down with the scrolling world like the debris
// they are.
public class FlipbookFx : MonoBehaviour
{
    public enum Mode { WeaponImpact, Explosion, Flash, Ring }

    static readonly int[] FlashTicks = { 1, 1 };
    const float RingSeconds = 4f / 24f;

    SpriteRenderer sr;
    Mode mode;
    int ship;
    TargetExplosion.Kind kind;
    float clock, size, hold;
    Color tint;
    float baseAngle;

    public bool Active { get; private set; }
    public int Frame { get; private set; }
    public float StartedAt { get; private set; }
    public Mode CurrentMode => mode;
    public Sprite CurrentSprite => sr != null ? sr.sprite : null;

    public void Build()
    {
        sr = GetComponent<SpriteRenderer>();
        gameObject.SetActive(false);
    }

    public void Play(Mode m, Vector3 at, float worldSize, int shipIndex, TargetExplosion.Kind k, Color color,
                     float holdScale, int order)
    {
        mode = m;
        ship = shipIndex;
        kind = k;
        size = worldSize;
        hold = Mathf.Max(.1f, holdScale);
        tint = color;
        clock = 0f;
        Frame = 0;
        StartedAt = Time.unscaledTime;
        sr.sortingOrder = order;
        sr.color = color;
        transform.position = at;
        baseAngle = m == Mode.Ring ? 0f : Random.Range(0, 4) * 90f;
        transform.rotation = Quaternion.Euler(0f, 0f, baseAngle);
        Active = true;
        gameObject.SetActive(true);
        Apply();
    }

    int[] Ticks()
    {
        switch (mode)
        {
            case Mode.Explosion: return WeaponArt.ExplosionTicks;
            case Mode.WeaponImpact: return WeaponArt.ImpactTicks;
            default: return FlashTicks;
        }
    }

    void Update()
    {
        if (Active) Tick(TargetExplosion.Delta());
    }

    public void Tick(float dt)
    {
        if (!Active || dt <= 0f) return;
        clock += dt;
        // ride along with the scrolling world, same rule the hazards use
        if (TargetExplosion.WorldScrolling)
            transform.position += Vector3.down * (moveBackGround.speed * dt * 30f);

        if (mode == Mode.Ring)
        {
            if (clock >= RingSeconds * hold) { Stop(); return; }
            Apply();
            return;
        }
        var ticks = Ticks();
        int f = WeaponArt.FrameAt(ticks, clock / hold, false);
        if (f >= ticks.Length) { Stop(); return; }
        if (f != Frame) { Frame = f; Apply(); }
        if (mode == Mode.Explosion) Fade(ticks);
    }

    // The explosion body eases out over the last FadeTail of its run, so it
    // always ends on transparent, never on a cut.
    public const float FadeTail = .3f;
    void Fade(int[] ticks)
    {
        float total = WeaponArt.Seconds(ticks) * hold;
        float left = total > 0f ? 1f - clock / total : 0f;
        var c = tint; c.a = tint.a * Mathf.Clamp01(left / FadeTail); sr.color = c;
    }
    public float Alpha => sr != null ? sr.color.a : 0f;

    void Apply()
    {
        switch (mode)
        {
            case Mode.Explosion:
                sr.sprite = WeaponArt.Explosion(kind, Frame);
                transform.localScale = Vector3.one * size;
                break;
            case Mode.WeaponImpact:
                sr.sprite = WeaponArt.Impact(ship, Frame);
                transform.localScale = Vector3.one * size;
                break;
            case Mode.Flash:
                sr.sprite = WeaponArt.ExplosionFlash(Frame);
                transform.localScale = Vector3.one * size;
                break;
            case Mode.Ring:
                float k = Mathf.Clamp01(clock / (RingSeconds * hold));
                float e = 1f - (1f - k) * (1f - k);
                sr.sprite = WeaponArt.ExplosionRing();
                transform.localScale = Vector3.one * size * Mathf.Lerp(.3f, 1f, e);
                var c = tint; c.a = tint.a * (1f - k); sr.color = c;
                break;
        }
    }

    public void Stop()
    {
        Active = false;
        gameObject.SetActive(false);
    }
}
