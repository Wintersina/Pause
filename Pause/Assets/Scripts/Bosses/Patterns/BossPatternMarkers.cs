using UnityEngine;

// The telegraphs of a bullet-hell pattern (BossPatternRunner.Mark): a blinking line down the safe lane of the first
// curtain, the gap of a ring, a ring round each mine's spot or bomb's landing. Pooled; built when the fight starts, so
// nothing is made mid-attack. They reuse the boss's own telegraph sprite (the laser's sight line: BossArt.Telegraph) in
// the world's cue colour, so no new art is needed.
public sealed class BossPatternMarkers
{
    public const int Max = 16;
    public const float Alpha = .55f;

    sealed class Mark
    {
        public SpriteRenderer sr;
        public float left, total;
        public bool live;
    }

    readonly Mark[] marks = new Mark[Max];
    GameObject root;
    BossDef boss;
    Color tint = Color.white;

    public int Live
    {
        get { int n = 0; for (int i = 0; i < Max; i++) if (marks[i] != null && marks[i].live) n++; return n; }
    }
    public int Shown { get; private set; }

    public void Build(Transform parent, BossDef def)
    {
        boss = def;
        root = new GameObject("PatternMarkers");
        root.transform.SetParent(parent, false);
        for (int i = 0; i < Max; i++)
        {
            var go = new GameObject("Mark" + i);
            go.transform.SetParent(root.transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 24;
            sr.enabled = false;
            marks[i] = new Mark { sr = sr };
        }
        tint = HostileGlow.Tint(def != null ? def.flash : Color.white);
    }

    // A line a -> b (a == b: a spot) `halfWidth` either side, for `seconds`.
    public void Add(Vector2 a, Vector2 b, float halfWidth, float seconds)
    {
        if (root == null || seconds <= 0f) return;
        Mark m = null;
        for (int i = 0; i < Max; i++) if (!marks[i].live) { m = marks[i]; break; }
        if (m == null) return;
        m.live = true;
        m.left = m.total = seconds;
        var t = m.sr.transform;
        var d = b - a;
        float len = d.magnitude;
        m.sr.sprite = BossArt.Shot(boss, BossArt.Telegraph);
        if (len < .05f)
        {
            // a spot: a square of the sight line's art, turned a quarter each blink
            t.position = new Vector3(a.x, a.y, 0f);
            t.rotation = Quaternion.identity;
            t.localScale = new Vector3(halfWidth * 2f, halfWidth * 2f, 1f);
        }
        else
        {
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f;
            t.position = new Vector3((a.x + b.x) * .5f, (a.y + b.y) * .5f, 0f);
            t.rotation = Quaternion.Euler(0f, 0f, ang);
            t.localScale = new Vector3(Mathf.Max(.06f, halfWidth * 2f), len, 1f);
        }
        m.sr.color = new Color(tint.r, tint.g, tint.b, Alpha);
        m.sr.enabled = true;
    }

    public void Step(float dt)
    {
        Shown = 0;
        for (int i = 0; i < Max; i++)
        {
            var m = marks[i];
            if (m == null || !m.live) continue;
            m.left -= dt;
            if (m.left <= 0f) { m.live = false; m.sr.enabled = false; continue; }
            // hard blink on twos, like the laser's sight line
            int blink = Mathf.FloorToInt((m.total - m.left) / (BossArt.TelegraphBlinkTicks * BossArt.Tick));
            m.sr.enabled = blink % 2 == 0;
            if (m.sr.enabled) Shown++;
        }
    }

    public void Clear()
    {
        for (int i = 0; i < Max; i++) if (marks[i] != null) { marks[i].live = false; marks[i].sr.enabled = false; }
    }

    public void Destroy()
    {
        if (root != null) BossUtil.Kill(root);
        root = null;
    }
}
