using UnityEngine;

// The Storm landform's effects layer (decorative; pooled children of the boulder's transform, built once, nothing allocated per frame):
//   * 8 cloud wisps that drift and roll round the base (a soft vapour skirt under and around it, some in front of the rock, some behind),
//   * 14 rain streaks and 5 hail pellets falling off the underside,
//   * 3 faint lightning arcs that flicker across the rock now and then (storm-white / pale yellow, never pink),
//   * a faint darker cloud shadow below.
// Everything is a pure function of the boulder's age, so it is deterministic, follows the boulder's transform, grows with its emerge,
// sinks with its recede and is gone the moment the boulder fades (a boss arrives) or is released.  Local units are the art's cell px / 100.
public sealed class CoverStormFx
{
    const int Wisps = 8, Rain = 14, Hail = 5, Arcs = 3;
    readonly SpriteRenderer[] wisp = new SpriteRenderer[Wisps], rain = new SpriteRenderer[Rain], hail = new SpriteRenderer[Hail], arc = new SpriteRenderer[Arcs];
    readonly SpriteRenderer shadow;
    readonly Transform root;
    Sprite[] sp;
    float cx, baseY, rx, bodyY, bodyRx, seed;
    bool shown;
    public bool Active { get; private set; }
    public Transform Root => root;
    public int Renderers => Wisps + Rain + Hail + Arcs + 1;

    public CoverStormFx(Transform parent)
    {
        root = new GameObject("StormFx").transform;
        root.SetParent(parent, false);
        shadow = New("Shadow", CoverBoulder.SortingOrder - 1);
        for (int i = 0; i < Wisps; i++) wisp[i] = New("Wisp", CoverBoulder.SortingOrder - 1);
        for (int i = 0; i < Rain; i++) rain[i] = New("Rain", CoverBoulder.SortingOrder - 1);
        for (int i = 0; i < Hail; i++) hail[i] = New("Hail", CoverBoulder.SortingOrder - 1);
        for (int i = 0; i < Arcs; i++) arc[i] = New("Arc", CoverBoulder.SortingOrder + 2);
        root.gameObject.SetActive(false);
    }

    SpriteRenderer New(string n, int order)
    {
        var g = new GameObject(n);
        g.transform.SetParent(root, false);
        var sr = g.AddComponent<SpriteRenderer>();
        sr.sortingOrder = order;
        return sr;
    }

    static float Hash(int a, int b)
    {
        unchecked
        {
            uint h = (uint)(a * 374761393 + b * 668265263 + 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFu) / 65535f;
        }
    }
    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

    // A boulder with this art starts (the sprites load once; false: no atlas, no effects).
    public bool Begin(CoverVariant v, string folder, float rng01)
    {
        sp = CoverBoulderArt.FxSprites(folder);
        Active = sp != null;
        if (!Active) { root.gameObject.SetActive(false); return false; }
        float half = v.cellPx * .5f;
        cx = (v.x0 + v.x1) * .5f - half;
        baseY = -(v.y1 - half);                    // local px, y up: the lowest point of the art
        bodyY = -((v.y0 + v.y1) * .5f - half);
        rx = (v.x1 - v.x0) * .5f;
        bodyRx = rx;
        seed = rng01 * 97.3f;
        wisp[0].sprite = sp[0];
        shown = false;
        for (int i = 0; i < Wisps; i++) wisp[i].sprite = sp[i & 3];
        for (int i = 0; i < Rain; i++) rain[i].sprite = sp[8 + (i & 3)];
        for (int i = 0; i < Hail; i++) hail[i].sprite = sp[12 + (i % 3)];
        for (int i = 0; i < Arcs; i++) arc[i].sprite = sp[4 + i];
        shadow.sprite = sp[7];
        return true;
    }

    public void Hide()
    {
        shown = false;
        root.gameObject.SetActive(false);
    }

    static void Put(SpriteRenderer r, float x, float y, float scale, float rot, float alpha, Color c)
    {
        c.a = alpha;
        r.color = c;
        var t = r.transform;
        t.localPosition = new Vector3(x * .01f, y * .01f, 0f);
        t.localRotation = Quaternion.Euler(0f, 0f, rot);
        t.localScale = new Vector3(scale, scale, 1f);
        r.enabled = alpha > .003f;
    }

    static readonly Color WispCol = new Color(.80f, .84f, .92f), RainCol = new Color(.85f, .9f, 1f), HailCol = new Color(.9f, .94f, 1f),
                          ArcCol = new Color(.95f, .97f, 1f), ShadowCol = new Color(.55f, .6f, .7f);

    // `age` the boulder's clock, `alpha` its emerge x recede x fade presence (0 hides everything).
    public void Step(float age, float alpha)
    {
        if (!Active) return;
        if (alpha <= .004f) { if (shown) Hide(); return; }
        if (!shown) { root.gameObject.SetActive(true); shown = true; }
        float t = age + seed;

        // cloud shadow, low and a little to the lee side
        Put(shadow, cx + rx * .22f, baseY - 70f, rx * 2.3f / 256f, 0f, .32f * alpha, ShadowCol);

        // wisps: ride an ellipse round the base, near half in front of the rock, far half behind
        for (int i = 0; i < Wisps; i++)
        {
            float dir = (i & 1) == 0 ? 1f : -1f;
            float a = i * .785f + t * (.11f + .035f * (i % 3)) * dir + Hash(i, 3) * 6.28f;
            float s = Mathf.Sin(a), c = Mathf.Cos(a);
            float edge = 1f - Smooth((Mathf.Abs(s) - .72f) / .28f);
            float size = (.55f + .35f * Hash(i, 7)) * (.93f + .07f * Mathf.Sin(t * .9f + i)) * rx * 1.15f / 512f * 2f;
            float x = cx + s * rx * .9f;
            float y = baseY + 6f + c * 38f + Mathf.Sin(t * .6f + i * 1.7f) * 12f;
            wisp[i].sortingOrder = c > 0f ? CoverBoulder.SortingOrder + 3 : CoverBoulder.SortingOrder - 1;
            Put(wisp[i], x, y, size, 7f * Mathf.Sin(a * 2f + i), (c > 0f ? .62f : .5f) * edge * alpha, WispCol);
        }

        // rain and hail fall from the underside, slanted by the wind
        for (int i = 0; i < Rain; i++)
        {
            float speed = 300f + 170f * Hash(i, 11), span = 300f + 120f * Hash(i, 13);
            float u = Mathf.Repeat(t * speed + Hash(i, 17) * span, span) / span;     // 0 at the underside .. 1 gone
            float x = cx + (Hash(i, 19) - .5f) * rx * 1.7f + u * 40f;
            float y = baseY + 10f - u * span;
            float fade = Smooth(u * 8f) * (1f - Smooth((u - .55f) / .45f));
            Put(rain[i], x, y, .7f + .5f * Hash(i, 23), 6f, .55f * fade * alpha, RainCol);
        }
        for (int i = 0; i < Hail; i++)
        {
            float speed = 420f + 160f * Hash(i, 29), span = 340f;
            float u = Mathf.Repeat(t * speed + Hash(i, 31) * span, span) / span;
            float x = cx + (Hash(i, 37) - .5f) * rx * 1.5f + u * 55f;
            float y = baseY + 14f - u * span;
            Put(hail[i], x, y, .7f + .3f * Hash(i, 41), u * 360f, .8f * Smooth(u * 10f) * (1f - Smooth((u - .6f) / .4f)) * alpha, HailCol);
        }

        // static: each slot flickers for a moment every few seconds at a hashed place on the rock
        for (int i = 0; i < Arcs; i++)
        {
            float period = 3.1f + .9f * i, ph = t + Hash(i, 43) * period;
            int ev = Mathf.FloorToInt(ph / period);
            float u = ph - ev * period;
            float on = u < .16f ? (u < .05f ? 1f : .55f + .3f * Mathf.Sin(u * 90f)) : 0f;
            if (Hash(ev, i + 47) < .25f) on = 0f;                                    // some events skip: not metronomic
            float x = cx + (Hash(ev, i + 53) - .5f) * bodyRx * 1.1f;
            float y = bodyY + (Hash(ev, i + 59) - .35f) * 180f;
            Put(arc[i], x, y, .85f + .4f * Hash(ev, i + 61), (Hash(ev, i + 67) - .5f) * 50f, .5f * on * alpha, ArcCol);
        }
    }
}
