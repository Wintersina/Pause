using UnityEngine;

// The hazards' drawn bodies and telegraphs, as pooled placeholder sprites (WeatherSprites) in the world's cue
// colour (pink only in Space), until Codex's sheets are vetted. At most Pool renderers, built once; Draw
// allocates nothing. Sorting order -7: behind enemies and shots (a boulder is -7 too, and never shares the screen).
public sealed class HazardVisuals
{
    public const int Pool = 150, PerField = 14;
    public const int SortingOrder = -7;
    readonly SpriteRenderer[] sr = new SpriteRenderer[Pool];
    readonly Transform root;
    readonly SpriteRenderer[] artSr = new SpriteRenderer[6];   // the world's approved hazard sheet: a swirl's body or a front's tiles
    public int Drawn { get; private set; }
    int artDrawn;
    int artLast;
    public int ArtDrawn => artLast;
    public SpriteRenderer[] ArtRenderers => artSr;
    public SpriteRenderer[] Renderers => sr;

    public HazardVisuals()
    {
        root = new GameObject("~HazardFx").transform;
        for (int i = 0; i < Pool; i++)
        {
            var go = new GameObject("p" + i, typeof(SpriteRenderer));
            go.transform.SetParent(root, false);
            var r = go.GetComponent<SpriteRenderer>();
            r.sortingOrder = SortingOrder;
            r.enabled = false;
            sr[i] = r;
        }
        for (int i = 0; i < artSr.Length; i++) artSr[i] = Hero("art" + i, SortingOrder);
    }

    SpriteRenderer Hero(string name, int order)
    {
        var go = new GameObject(name, typeof(SpriteRenderer));
        go.transform.SetParent(root, false);
        var r = go.GetComponent<SpriteRenderer>();
        r.sortingOrder = order;
        r.enabled = false;
        return r;
    }

    public void Destroy() { if (root != null) Object.DestroyImmediate(root.gameObject); }

    public static float Hash(int i, int k)
    {
        unchecked { uint h = (uint)(i * 73856093 ^ k * 19349663); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (h & 0xffff) / 65535f; }
    }

    public void Draw(HazardField[] fields, bool on)
    {
        int n = 0;
        if (on)
            for (int f = 0; f < fields.Length; f++)
                if (fields[f].Active) n = DrawField(fields[f], n);
        for (int i = n; i < Pool; i++) if (sr[i].enabled) sr[i].enabled = false;
        for (int i = artDrawn; i < artSr.Length; i++) artSr[i].enabled = false;
        artLast = artDrawn; artDrawn = 0;
        Drawn = n;
    }

    void Put(ref int n, AmbientShape shape, Vector2 pos, float rot, float size, Color c) { Put(ref n, shape, pos, rot, size, size, c); }

    void Put(ref int n, AmbientShape shape, Vector2 pos, float rot, float sx, float sy, Color c)
    {
        if (n >= Pool) return;
        var r = sr[n++];
        r.sprite = WeatherSprites.Get(shape);
        var t = r.transform;
        t.position = new Vector3(pos.x, pos.y, 0f);
        t.rotation = Quaternion.Euler(0f, 0f, rot);
        t.localScale = new Vector3(sx, sy, 1f);
        r.color = c;
        r.enabled = true;
    }

    static float RotAlong(Vector2 d) { return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f; }   // a vertical streak laid along d

    // THE CLARITY PASS: what a hazard does is drawn as what it does. A swirl shows its RADIUS (a faint ring: the true edge of the
    // pull), PULL LINES (streaks and debris that spiral INWARD along the force), and, for the Storm tornado, a hero FUNNEL seen
    // from ~30 degrees (cloud cap, funnel, dust ring at the foot) that FORMS during the warning (clouds gather, the funnel
    // descends). A front shows its leading edge (a row of dashes), its push lines and debris. All pooled, placeholders for Codex's sheets.
    int DrawField(HazardField f, int n)
    {
        var d = f.def;
        Color pale = Color.Lerp(WorldCue.Body(d.world), Color.white, .6f);
        float env = f.Envelope;
        bool warn = f.phase == HazardField.Phase.Warning;
        float p = warn ? Mathf.Clamp01(f.t / d.warning) : 1f;          // how far the telegraph has built
        // warning pulses gently; live is steady
        float a = warn ? .2f + .16f * p + .06f * Mathf.Sin(f.t * Mathf.PI * 2f) : .55f * Mathf.Max(.25f, env);
        a = Mathf.Min(a, WorldWeatherTable.AmbientMaxAlpha);
        float tm = f.t;
        bool bodyArt = d.artRow >= 0 && HazardArt.Loaded && HazardArt.World == d.world;
        if (bodyArt) { DrawBodyArt(f, p, warn, env); a *= .6f; }   // the painted body carries it; the pull lines stay, fainter
        switch (d.shape)
        {
            case HazardShape.Swirl:
            {
                float R = d.size;
                // the radius ring (the pull's true edge), dashes turning slowly, growing in during the warning
                float ringR = R * (warn ? Mathf.Lerp(.35f, 1f, p) : 1f);
                const int Dashes = 20;
                for (int i = 0; i < Dashes; i++)
                {
                    float ang = (i / (float)Dashes) * 6.2832f + tm * .35f * f.side;
                    Vector2 pos = f.centre + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * ringR;
                    Put(ref n, AmbientShape.Streak, pos, ang * Mathf.Rad2Deg, .3f, WithA(pale, a * .42f * (.6f + .4f * Mathf.Sin(ang * 3f + tm))));
                }
                // pull lines: streaks and debris spiral INWARD along the force; they gather as the warning builds
                int lines = warn ? Mathf.CeilToInt(12 * p) : 14;
                float inward = Mathf.Clamp01(d.pull * 1.4f + .25f);
                for (int i = 0; i < lines; i++)
                {
                    float u = Mathf.Repeat(i / 14f + tm * .55f, 1f);            // 1.8 s from the rim to the core
                    float r = R * (1f - .86f * u) * (warn ? Mathf.Lerp(.5f, 1f, p) : 1f);
                    float ang = i * 2.399f + f.side * (tm * .9f * Mathf.Max(.4f, d.swirl) + (1f - u) * 1.5f);
                    Vector2 radial = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    Vector2 flow = -radial * inward + new Vector2(-radial.y, radial.x) * (f.side * Mathf.Max(.25f, d.swirl));
                    Put(ref n, AmbientShape.Streak, f.centre + radial * r, RotAlong(flow), .2f + .34f * (1f - u), WithA(pale, a * Mathf.Sin(Mathf.PI * u) * (warn ? .8f : 1f)));
                }
                for (int i = 0; i < (warn ? 2 : 6); i++)                          // debris on the same spiral, bigger, slower
                {
                    float u = Mathf.Repeat(i / 6f + tm * .32f + .13f, 1f);
                    float r = R * (1f - .8f * u);
                    float ang = i * 1.7f + f.side * (tm * .7f + (1f - u) * 1.2f);
                    Put(ref n, i % 2 == 0 ? AmbientShape.Leaf : AmbientShape.Dot, f.centre + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r, tm * 120f * (i % 3 - 1), .13f + .08f * (1f - u), WithA(pale, a * 1.2f * Mathf.Sin(Mathf.PI * u)));
                }
                break;
            }
            case HazardShape.Front:
            {
                float lane = f.laneHalf;
                int streaks = warn ? 10 : 18;
                for (int i = 0; i < streaks; i++)
                {
                    float jx = Hash(i, 1), jy = Hash(i, 2);
                    float y, x;
                    if (warn)
                    {
                        // streaks stream in from the upwind edge, at the top of the view: the warning shows the side and the direction
                        y = f.viewTop - .3f - jy * 2.6f;
                        float k = Mathf.Repeat(jx + tm * .9f, 1f);
                        x = -f.side * lane + f.side * k * lane * .9f;
                    }
                    else
                    {
                        y = f.frontY + (jy - .5f) * d.size * .85f;
                        float k = Mathf.Repeat(jx + tm * .55f * (.7f + jy * .6f), 1f);
                        x = f.side * (-lane + k * lane * 2f);
                    }
                    if (y > f.viewTop + .5f || y < f.viewBottom - .5f) continue;
                    Vector2 push = new Vector2(d.push.x * f.side, d.push.y);
                    Put(ref n, AmbientShape.Streak, new Vector2(x, y), RotAlong(push), .5f + .6f * jx, WithA(pale, a));
                }
                if (!warn)
                {
                    // the leading edge: a row of dashes along the band's lower rim, and debris riding the push
                    float edgeY = f.frontY - d.size * .5f * .92f;
                    for (int i = 0; i < 12; i++)
                    {
                        float x = Mathf.Lerp(-lane, lane, (i + .5f) / 12f);
                        float wob = Mathf.Sin(tm * 2f + i * 1.3f) * .12f;
                        if (edgeY + wob < f.viewBottom - .3f || edgeY > f.viewTop + .3f) continue;
                        Put(ref n, AmbientShape.Streak, new Vector2(x, edgeY + wob), f.side > 0 ? -90f : 90f, .42f, WithA(pale, a * .55f));
                    }
                    for (int i = 0; i < 6; i++)
                    {
                        float jx = Hash(i, 7), jy = Hash(i, 8);
                        float k = Mathf.Repeat(jx + tm * .4f, 1f);
                        float y = f.frontY + (jy - .5f) * d.size * .8f;
                        if (y > f.viewTop + .3f || y < f.viewBottom - .3f) continue;
                        Put(ref n, i % 2 == 0 ? AmbientShape.Leaf : AmbientShape.Dot, new Vector2(f.side * (-lane + k * lane * 2f), y), tm * 90f * (i % 3 - 1), .12f + .08f * jx, WithA(pale, a * 1.1f));
                    }
                }
                break;
            }
            default:
            {
                float h = f.viewTop - f.viewBottom;
                for (int i = 0; i < PerField; i++)
                {
                    float jx = Hash(i, 3);
                    float k = warn ? Mathf.Repeat(Hash(i, 4) + tm * .25f, 1f) * .12f : Mathf.Repeat(Hash(i, 4) + tm * (.3f + .3f * jx), 1f);
                    float y = f.viewBottom + k * h * .9f;
                    float sway = Mathf.Sin(tm * 2f + i) * d.size * .3f * (.4f + k);
                    Vector2 pos = new Vector2(f.centre.x + sway + (jx - .5f) * d.size * .5f, y);
                    Put(ref n, i % 2 == 0 ? AmbientShape.Ember : AmbientShape.Dot, pos, 0f, .12f + .16f * jx, WithA(pale, a * (1f - k * .6f)));
                }
                break;
            }
        }
        return n;
    }

    public const float ArtFps = 12f, SpinDegrees = 150f;

    // The vortex's turn so far (degrees): it spins UP over the warning (speed grows with the square of the warning's progress, so the
    // angle grows with its cube), then turns at SpinDegrees per second; continuous across the hand-over (no jump).
    public static float SpinAngle(HazardField f)
    {
        float w = Mathf.Max(.01f, f.def.warning);
        float rate = f.def.spinDegrees > 0f ? f.def.spinDegrees : SpinDegrees;
        if (f.t < w) return rate * f.t * f.t * f.t / (3f * w * w);
        return rate * (w / 3f + (f.t - w));
    }

    // A hazard's approved body (wx_hazard.png): a swirl is one cell scaled to its diameter, a front is a row of seamless tiles across the
    // lane at the band's place (the warning cells show at the top edge before it comes).
    void DrawBodyArt(HazardField f, float p, bool warn, float env)
    {
        var d = f.def;
        float k = Mathf.Clamp01(env * 1.4f);
        if (d.shape == HazardShape.Swirl)
        {
            // TOP-DOWN vortex: the painted body turns as a whole (continuous rotation: the arms visibly advance every frame), a second copy
            // at 0.72 scale turns faster the same way and runs the loop out of phase (layered arms at different speeds); the warning SPINS IT UP
            // (clouds gather: it grows, brightens and accelerates), the live phase turns at SpinDegrees per second.
            bool spins = d.spinDegrees > 0f;
            float spin = spins ? SpinAngle(f) * f.side : 0f;
            float grow = warn ? Mathf.Lerp(.55f, 1f, p) : 1f;
            float s = d.size * 2f * 1.1f * grow;
            int frame = (int)(f.t * (spins ? ArtFps : 8f));
            var sprite = warn ? HazardArt.Cell(2, d.artWarnCol + ((int)(f.t * 4f) & 1)) : HazardArt.Cell(d.artRow, frame % 6);
            var r = artSr[0]; r.enabled = true; artDrawn = 1;
            r.sprite = sprite;
            r.transform.position = new Vector3(f.centre.x, f.centre.y, 0f);
            r.transform.rotation = Quaternion.Euler(0f, 0f, spin);
            r.transform.localScale = new Vector3(s, s, 1f);
            r.color = new Color(1f, 1f, 1f, warn ? .45f + .45f * p : .95f * k);
            if (!warn && spins)
            {
                // the inner layer, and an occasional soft lightning brightening of the whole (under 15 %, once per 4.5 s: no flash)
                float cyc = Mathf.Repeat(f.t / 4.5f + Hash(f.variant, 11), 1f);
                float flick = cyc < .02f ? Mathf.Sin(cyc / .02f * Mathf.PI) * .15f : 0f;
                var r2 = artSr[1]; r2.enabled = true; artDrawn = 2;
                r2.sprite = HazardArt.Cell(d.artRow, (frame + 3) % 6);
                r2.transform.position = r.transform.position;
                r2.transform.rotation = Quaternion.Euler(0f, 0f, spin * 1.7f + 40f);
                r2.transform.localScale = new Vector3(s * .72f, s * .72f, 1f);
                r2.color = new Color(1f, 1f, 1f, .55f * k * (1f + flick));
                r.color = new Color(1f - flick * .1f, 1f - flick * .1f, 1f, .95f * k * (1f + flick));
            }
        }
        else if (d.shape == HazardShape.Column)
        {
            // a column rises from the bottom of the view: tiles of the painted stream stacked upward (overlapping a little), its gate (the geyser's
            // bursts, the updraft's pulse) breathing their opacity; the warning shows one faint tile at the foot
            float tile = Mathf.Max(2f, d.size * 2.6f);
            float gate = HazardForce.Gate(f);
            int n = warn ? 1 : Mathf.Min(artSr.Length, Mathf.CeilToInt((f.viewTop - f.viewBottom) / (tile * .8f)));
            artDrawn = n;
            int frame = (int)(f.t * 8f);
            for (int i = 0; i < n; i++)
            {
                var r = artSr[i]; r.enabled = true;
                r.sprite = warn ? HazardArt.Cell(2, d.artWarnCol + ((int)(f.t * 4f) & 1)) : HazardArt.Cell(d.artRow, (frame + i * 2) % 6);
                r.transform.position = new Vector3(f.centre.x, f.viewBottom + tile * .5f + i * tile * .8f, 0f);
                r.transform.rotation = Quaternion.identity;
                r.transform.localScale = new Vector3(tile, tile, 1f);
                r.color = new Color(1f, 1f, 1f, warn ? .35f + .55f * p : .95f * k * Mathf.Max(.3f, gate) * (1f - .08f * i));
            }
        }
        else if (d.shape == HazardShape.Front)
        {
            float tile = d.size * 1.4f;
            int n = Mathf.Min(artSr.Length, Mathf.CeilToInt(f.laneHalf * 2f / tile) + 1);
            float y = warn ? f.viewTop - tile * .3f : f.frontY;
            artDrawn = n;
            for (int i = 0; i < n; i++)
            {
                var r = artSr[i]; r.enabled = true;
                r.sprite = warn ? HazardArt.Cell(2, d.artWarnCol + ((int)(f.t * 4f) & 1)) : HazardArt.Cell(d.artRow, (int)(f.t * 8f) % 6);
                r.transform.position = new Vector3((i - (n - 1) * .5f) * tile, y, 0f);
                r.transform.rotation = Quaternion.identity;
                r.transform.localScale = new Vector3(tile * (f.side < 0f ? -1f : 1f), tile, 1f);
                r.color = new Color(1f, 1f, 1f, warn ? .35f + .55f * p : .95f * k);
            }
        }
    }

    static Color WithA(Color c, float a) { c.a = a; return c; }
}
