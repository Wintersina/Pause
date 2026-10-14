using UnityEngine;

// The little world-coloured pixels a cover boulder throws: chips where something is stopped on its face, a burst of
// debris when it is destroyed. A fixed pool of Capacity sprite renderers (the 1 x 1 u white square, scaled to a few
// pixels), integrated on the world's clock by CoverBoulders.Step, so a frozen world freezes them. Nothing allocates.
public sealed class CoverFx
{
    public const int Capacity = 72;
    public const int Order = 28;   // under the shots (30), over the boulder (-7), the ship and the enemies

    struct P
    {
        public SpriteRenderer sr;
        public Vector2 pos, vel;
        public float life, max, size;
        public Color color;
    }

    readonly P[] ps = new P[Capacity];
    int next;
    static uint seed = 2463534242u;

    public int Live
    {
        get { int n = 0; for (int i = 0; i < ps.Length; i++) if (ps[i].life > 0f) n++; return n; }
    }

    public CoverFx(Transform root)
    {
        var sprite = WeaponFx.Solid;
        for (int i = 0; i < ps.Length; i++)
        {
            var go = new GameObject("Chip");
            go.transform.SetParent(root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = Order;
            sr.enabled = false;
            ps[i].sr = sr;
        }
    }

    // xorshift: a cheap, deterministic 0..1
    static float Rnd()
    {
        seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
        return (seed & 0xFFFFFF) / (float)0x1000000;
    }

    // `n` chips from `at`, thrown about `dir` within `spread` (0 .. 1 of a half circle), at speed (min, max).
    public void Burst(Vector2 at, Vector2 dir, float spread, int n, float speedMin, float speedMax, float life, Color a, Color b)
    {
        float baseAng = Mathf.Atan2(dir.y, dir.x);
        for (int i = 0; i < n; i++)
        {
            ref P p = ref ps[next];
            next = (next + 1) % ps.Length;
            float ang = baseAng + (Rnd() * 2f - 1f) * Mathf.PI * Mathf.Clamp01(spread);
            float sp = Mathf.Lerp(speedMin, speedMax, Rnd());
            p.pos = at;
            p.vel = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * sp;
            p.max = p.life = life * (.6f + Rnd() * .6f);
            p.size = Mathf.Lerp(.05f, .11f, Rnd());
            p.color = Rnd() < .5f ? a : b;
            p.sr.enabled = true;
            p.sr.transform.position = new Vector3(at.x, at.y, 0f);
            p.sr.transform.localScale = new Vector3(p.size, p.size, 1f);
            p.sr.color = p.color;
        }
    }

    public void Step(float dt)
    {
        if (dt <= 0f) return;
        float drag = Mathf.Exp(-2.2f * dt);
        for (int i = 0; i < ps.Length; i++)
        {
            ref P p = ref ps[i];
            if (p.life <= 0f) continue;
            p.life -= dt;
            if (p.life <= 0f) { p.sr.enabled = false; continue; }
            p.vel *= drag;
            p.pos += p.vel * dt;
            float k = p.life / p.max;
            var c = p.color; c.a = Mathf.Clamp01(k * 1.6f);
            p.sr.color = c;
            p.sr.transform.position = new Vector3(p.pos.x, p.pos.y, 0f);
            float s = p.size * (.5f + k * .5f);
            p.sr.transform.localScale = new Vector3(s, s, 1f);
        }
    }

    public void Clear()
    {
        for (int i = 0; i < ps.Length; i++) { ps[i].life = 0f; if (ps[i].sr != null) ps[i].sr.enabled = false; }
    }
}
