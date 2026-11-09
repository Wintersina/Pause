using UnityEngine;
using UnityEngine.UI;

// The Shop's reward flourish: a small neon burst at the watch-ad row, then one star dust per unit flies out of it in an
// arc to the total at the top, and the total counts up one dust per landing, with a small pulse. Unscaled time, driven
// by ShopPanel.Update. The balance is already saved when this starts, so the displayed number is only a cosmetic walk
// from `from` to `to`: Finish() (panel closed, another reward) snaps it to the end and hides everything.
// The timeline is arithmetic on `t` (Displayed / Pulse), so it tests without a frame loop.
public sealed class ShopDustFx
{
    public const int MaxParticles = 50;
    public const float PopSeconds = .16f;       // the pieces pop out of the burst
    public const float FirstLaunch = .2f;
    public const float LaunchGap = .012f;       // 49 gaps = .59 s of staggered launches
    public const float Flight = .55f;
    public const float PulseSeconds = .14f;
    public const float PulseScale = .14f;
    // last launch + flight = ~1.34 s, plus the pulse settling: inside 1.2 .. 1.8 s
    public static float TotalSeconds { get { return FirstLaunch + LaunchGap * (MaxParticles - 1) + Flight + PulseSeconds; } }

    const int Sparks = 12;
    static readonly Color Cyan = new Color(.45f, .95f, 1f, 1f), Gold = new Color(1f, .82f, .35f, 1f);

    readonly RectTransform layer;
    Image glow, ring;
    Image[] dustImg, sparkImg;
    Vector2[] scatter, ctrl;
    Vector2[] sparkDir;
    Vector2 origin, target;
    int from, to, count;
    float t;
    bool active;

    public ShopDustFx(RectTransform layer) { this.layer = layer; if (layer != null) layer.gameObject.SetActive(false); }

    public RectTransform LayerRect { get { return layer; } }
    public bool Active { get { return active; } }
    public int Count { get { return count; } }

    // Starts (or restarts, from wherever the number has got to) a walk from `fromDust` to `toDust`. origin / target are in
    // the layer's local space. Particle sprites are optional (tests pass none).
    public void Begin(Vector2 originLocal, Vector2 targetLocal, int fromDust, int toDust, Sprite dust)
    {
        if (active) fromDust = Displayed;
        origin = originLocal; target = targetLocal;
        from = fromDust; to = Mathf.Max(fromDust, toDust);
        count = Mathf.Min(MaxParticles, to - from);
        t = 0f;
        active = count > 0;
        if (!active) { Hide(); return; }
        Build(dust);
        for (int i = 0; i < count; i++)
        {
            float a = Random.value * Mathf.PI * 2f;
            scatter[i] = origin + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(18f, 70f);
            Vector2 d = target - scatter[i];
            Vector2 perp = new Vector2(-d.y, d.x).normalized;
            ctrl[i] = (scatter[i] + target) * .5f + perp * (Random.value < .5f ? -1f : 1f) * d.magnitude * Random.Range(.2f, .4f);
        }
        for (int k = 0; k < Sparks; k++)
        {
            float a = (k + Random.value * .6f) / Sparks * Mathf.PI * 2f;
            sparkDir[k] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(60f, 110f);
        }
        if (layer != null) layer.gameObject.SetActive(true);
        Apply();
    }

    // The number to show right now (the saved balance once it is over).
    public int Displayed
    {
        get
        {
            if (!active) return to;
            return from + (int)((long)(to - from) * Landed / count);
        }
    }

    // 1 normally, a little more just after a dust lands.
    public float Pulse
    {
        get
        {
            int landed = Landed;
            if (!active || landed == 0 || landed >= count && t >= LandTime(count - 1) + PulseSeconds) return 1f;
            float since = t - LandTime(landed - 1);
            return 1f + PulseScale * Mathf.Clamp01(1f - since / PulseSeconds);
        }
    }

    static float LaunchTime(int i) { return FirstLaunch + LaunchGap * i; }
    static float LandTime(int i) { return LaunchTime(i) + Flight; }

    int Landed
    {
        get
        {
            int n = 0;
            while (n < count && t >= LandTime(n)) n++;
            return n;
        }
    }

    // Advances `dt` unscaled seconds. True while it is still running.
    public bool Step(float dt)
    {
        if (!active) return false;
        t += Mathf.Max(0f, dt);
        if (t >= LandTime(count - 1) + PulseSeconds) { Finish(); return false; }
        Apply();
        return true;
    }

    // Snaps to the end: the number is the true balance, the pieces are gone. Safe at any time, any number of times.
    public void Finish()
    {
        active = false;
        Hide();
    }

    void Hide()
    {
        if (layer != null) layer.gameObject.SetActive(false);
    }

    void Build(Sprite dust)
    {
        if (layer == null) { Alloc(); return; }
        if (dustImg != null && dustImg[0] != null && dustImg[0].gameObject != null) return;
        Alloc();
        glow = CodexUi.NewImage("Glow", layer, EliteFxArt.Glow, Color.white);
        ring = CodexUi.NewImage("Ring", layer, EliteFxArt.Ring, Color.white);
        for (int k = 0; k < Sparks; k++) sparkImg[k] = CodexUi.NewImage("Spark", layer, EliteFxArt.Spark, k % 2 == 0 ? Cyan : Gold);
        for (int i = 0; i < MaxParticles; i++)
        {
            dustImg[i] = CodexUi.NewImage("Dust", layer, dust, Color.white);
            dustImg[i].preserveAspect = true;
            dustImg[i].rectTransform.sizeDelta = new Vector2(30f, 30f);
        }
    }

    void Alloc()
    {
        if (scatter != null && scatter.Length == MaxParticles && (layer == null || dustImg[0] != null)) return;
        scatter = new Vector2[MaxParticles]; ctrl = new Vector2[MaxParticles];
        sparkDir = new Vector2[Sparks];
        dustImg = new Image[MaxParticles]; sparkImg = new Image[Sparks];
    }

    void Apply()
    {
        if (layer == null || dustImg == null || dustImg[0] == null) return;
        // the burst: a ring and a glow flash, a few diamonds flung out and fading
        float bk = Mathf.Clamp01(t / .32f);
        SetImg(ring, bk < 1f, origin, Mathf.Lerp(30f, 150f, bk), new Color(Cyan.r, Cyan.g, Cyan.b, (1f - bk) * .75f));
        SetImg(glow, bk < 1f, origin, Mathf.Lerp(110f, 40f, bk), new Color(Gold.r, Gold.g, Gold.b, 1f - bk));
        for (int k = 0; k < Sparks; k++)
        {
            var c = k % 2 == 0 ? Cyan : Gold;
            float e = 1f - (1f - bk) * (1f - bk);
            SetImg(sparkImg[k], bk < 1f, origin + sparkDir[k] * e, 14f, new Color(c.r, c.g, c.b, 1f - bk));
        }
        for (int i = 0; i < MaxParticles; i++)
        {
            var img = dustImg[i];
            if (img == null) continue;
            bool live = i < count && t < LandTime(i);
            img.enabled = live;
            if (!live) continue;
            Vector2 p;
            if (t < LaunchTime(i))
            {
                float pk = Mathf.Clamp01(t / PopSeconds);
                p = Vector2.Lerp(origin, scatter[i], 1f - (1f - pk) * (1f - pk));
                img.rectTransform.localScale = Vector3.one * Mathf.Lerp(.3f, 1f, pk);
            }
            else
            {
                float u = Mathf.Clamp01((t - LaunchTime(i)) / Flight);
                u *= u * (3f - 2f * u) * .5f + u * .5f;   // gentle ease in, a clean landing
                float m = 1f - u;
                p = m * m * scatter[i] + 2f * m * u * ctrl[i] + u * u * target;
                img.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, .7f, u);
            }
            img.rectTransform.anchoredPosition = p;
        }
    }

    static void SetImg(Image img, bool on, Vector2 pos, float size, Color c)
    {
        if (img == null) return;
        img.enabled = on;
        if (!on) return;
        img.rectTransform.anchoredPosition = pos;
        img.rectTransform.sizeDelta = new Vector2(size, size);
        img.color = c;
    }
}
