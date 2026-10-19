using UnityEngine;

// The telegraphs of a bullet-hell pattern (BossPatternRunner.Mark): light DOTTED lines in the world's cue colour. A gap
// lane is its two edges (a brighter dotted pair; the safe corridor between stays empty), an aim line is one faint dotted
// line, a mine's spot or bomb's landing a ring of dots. Each line is ONE pooled tiled sprite (BossTelegraphStyle.Dash),
// built when the fight starts, so nothing is made mid-attack. Alpha, width and how long they show before the beat
// scale down with the song's intensity, and the total on screen is capped (BossTelegraphStyle).
public sealed class BossPatternMarkers
{
    public const int Max = BossTelegraphStyle.MaxMarks;

    sealed class Mark
    {
        public SpriteRenderer a, b;          // the two edges of a lane (b unused for a single line / ring)
        public float left, total, delay;     // delay: hidden until this much has elapsed (ends on the beat as before)
        public float alpha, length;          // per renderer
        public int renderers;
        public bool live, ring;
    }

    readonly Mark[] marks = new Mark[Max];
    GameObject root;
    Color tint = Color.white;

    // The song's intensity 0..1 (BossActor sets it before a telegraph is added).
    public float Intensity = BossTelegraphStyle.NonMusicIntensity;

    public int Live
    {
        get { int n = 0; for (int i = 0; i < Max; i++) if (marks[i] != null && marks[i].live) n++; return n; }
    }
    public int Shown { get; private set; }
    public int Suppressed;                   // telegraphs asked for by non-major attacks and dropped (test read)
    // Summed (alpha x dotted length) of what is drawn now: the "how much screen is covered" measure, capped.
    public float Weight { get; private set; }
    // Renderers showing a dotted line now, and the largest alpha among them (tests / tuning).
    public int ShownRenderers { get; private set; }
    public float MaxShownAlpha { get; private set; }
    public SpriteRenderer FirstRenderer(int i) => marks[i] != null ? marks[i].a : null;
    public SpriteRenderer SecondRenderer(int i) => marks[i] != null ? marks[i].b : null;
    public bool IsLive(int i) => marks[i] != null && marks[i].live;
    public bool IsRing(int i) => marks[i] != null && marks[i].ring;

    SpriteRenderer Make(string name, int i)
    {
        var go = new GameObject(name + i);
        go.transform.SetParent(root.transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 24;
        sr.enabled = false;
        return sr;
    }

    public void Build(Transform parent, BossDef def)
    {
        root = new GameObject("PatternMarkers");
        root.transform.SetParent(parent, false);
        for (int i = 0; i < Max; i++) marks[i] = new Mark { a = Make("MarkA", i), b = Make("MarkB", i) };
        tint = Color.Lerp(HostileGlow.Tint(def != null ? def.flash : Color.white), Color.white, BossTelegraphStyle.PaleMix);
        if (def != null && def.WorldIndex == 2) { tint = VerdantMarkTint; blinkLow = VerdantBlinkLow; }
    }

    static void Line(SpriteRenderer sr, Vector2 from, Vector2 to, float width)
    {
        var d = to - from;
        sr.sprite = BossTelegraphStyle.Dash;
        sr.drawMode = SpriteDrawMode.Tiled;
        sr.tileMode = SpriteTileMode.Continuous;
        sr.size = new Vector2(1f / BossTelegraphStyle.PixelsPerUnit, d.magnitude);
        var t = sr.transform;
        t.position = new Vector3(from.x, from.y, 0f);
        t.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f);   // local +y runs along the line
        t.localScale = new Vector3(width * BossTelegraphStyle.PixelsPerUnit, 1f, 1f);
    }

    // A line a -> b (a == b: a spot) `halfWidth` either side, for `seconds` (only the last stretch of which is drawn).
    public void Add(Vector2 a, Vector2 b, float halfWidth, float seconds)
    {
        if (root == null || seconds <= 0f) return;
        Mark m = null;
        for (int i = 0; i < Max; i++) if (!marks[i].live) { m = marks[i]; break; }
        if (m == null) return;
        float inten = Mathf.Clamp01(Intensity);
        m.live = true;
        m.left = m.total = seconds;
        m.delay = Mathf.Max(0f, seconds - BossTelegraphStyle.VisibleSeconds(inten));
        var d = b - a;
        float len = d.magnitude;
        float width = BossTelegraphStyle.Width(inten);
        float dotted = BossTelegraphStyle.DashLength / BossTelegraphStyle.DashPeriod;
        m.ring = false;
        if (len < .05f)
        {
            // a spot: a ring of dots round it
            m.ring = true;
            m.renderers = 1;
            m.alpha = BossTelegraphStyle.RingAlpha(inten);
            m.length = Mathf.PI * halfWidth * 2f * dotted;
            m.a.drawMode = SpriteDrawMode.Simple;
            m.a.sprite = BossTelegraphStyle.Ring;
            m.a.transform.position = new Vector3(a.x, a.y, 0f);
            m.a.transform.rotation = Quaternion.identity;
            m.a.transform.localScale = new Vector3(halfWidth * 2f, halfWidth * 2f, 1f);
        }
        else if (halfWidth >= BossTelegraphStyle.LaneMinHalfWidth)
        {
            // a gap lane: its two edges, brighter than any other line
            m.renderers = 2;
            m.alpha = BossTelegraphStyle.EdgeAlpha(inten);
            m.length = len * dotted;
            var n = new Vector2(-d.y, d.x) / len * halfWidth;
            Line(m.a, a + n, b + n, width);
            Line(m.b, a - n, b - n, width);
        }
        else
        {
            m.renderers = 1;
            m.alpha = BossTelegraphStyle.AimAlpha(inten);
            m.length = len * dotted;
            Line(m.a, a, b, width);
        }
        m.a.enabled = m.b.enabled = false;   // Step shows it when its last stretch begins
    }

    // Verdant (jungle, world 2): the marks read over the green canopy by colour and a deeper blink, never a border: a saturated
    // chartreuse (clear of the canopy's green and of Ember's amber) that blinks between full and 40 %.
    static readonly Color VerdantMarkTint = new Color(.82f, 1f, .2f);
    const float VerdantBlinkLow = .4f;
    float blinkLow = .6f;

    float Pulse(Mark m) => Mathf.FloorToInt((m.total - m.left) / (BossArt.TelegraphBlinkTicks * BossArt.Tick)) % 2 == 0 ? 1f : blinkLow;

    public void Step(float dt)
    {
        Shown = 0; ShownRenderers = 0; MaxShownAlpha = 0f; Weight = 0f;
        for (int i = 0; i < Max; i++)
        {
            var m = marks[i];
            if (m == null || !m.live) continue;
            m.left -= dt;
            if (m.left <= 0f) { m.live = false; m.a.enabled = m.b.enabled = false; continue; }
            bool show = m.total - m.left >= m.delay;
            m.a.enabled = show;
            m.b.enabled = show && m.renderers > 1;
            if (!show) continue;
            Shown++;
            ShownRenderers += m.renderers;
            Weight += m.alpha * m.length * m.renderers * Pulse(m);
        }
        // cap the total: everything scales down together when over
        float k = Weight > BossTelegraphStyle.MaxTotalWeight ? BossTelegraphStyle.MaxTotalWeight / Weight : 1f;
        Weight *= k;
        for (int i = 0; i < Max; i++)
        {
            var m = marks[i];
            if (m == null || !m.live || !m.a.enabled) continue;
            float al = m.alpha * k * Pulse(m);
            var c = new Color(tint.r, tint.g, tint.b, al);
            m.a.color = c;
            if (m.b.enabled) m.b.color = c;
            if (al > MaxShownAlpha) MaxShownAlpha = al;
        }
    }

    public void Clear()
    {
        for (int i = 0; i < Max; i++) if (marks[i] != null) { marks[i].live = false; marks[i].a.enabled = marks[i].b.enabled = false; }
        Shown = 0; ShownRenderers = 0; Weight = 0f; MaxShownAlpha = 0f;
    }

    public void Destroy()
    {
        if (root != null) BossUtil.Kill(root);
        root = null;
    }
}
