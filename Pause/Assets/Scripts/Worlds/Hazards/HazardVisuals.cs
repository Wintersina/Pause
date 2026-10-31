using UnityEngine;

// The hazards' drawn bodies and telegraphs, as pooled placeholder sprites (WeatherSprites) in the world's cue
// colour (pink only in Space), until Codex's sheets are vetted. At most Pool renderers, built once; Draw
// allocates nothing. Sorting order -7: behind enemies and shots (above every backdrop layer and any boulder, which sits on the ground plane).
public sealed class HazardVisuals
{
    public const int Pool = 150, PerField = 14;
    public const int SortingOrder = -7;
    readonly SpriteRenderer[] sr = new SpriteRenderer[Pool];
    readonly Transform root;
    readonly SpriteRenderer[] artSr = new SpriteRenderer[14];   // 14: a three-band flare train of three stacked tiles each needs nine   // the world's approved hazard sheet: a swirl's body or a front's tiles
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

    public void Draw(HazardField[] fields)
    {
        int n = 0;
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
            case HazardShape.Sweep:
                n = DrawFlare(f, n, warn, p, env);
                break;
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

    public const float FlareTile = 2.1f, FlareLead = .78f;   // the Space flare sheet: cell width in band widths, the leading edge's place in the cell (0..1)

    static readonly Color FlareHot = new Color(1f, .97f, .86f), FlareGold = new Color(1f, .78f, .32f), FlareOrange = new Color(1f, .5f, .16f),
                          FlareMagenta = new Color(1f, .3f, .78f), FlareViolet = new Color(.6f, .34f, 1f);

    // One Edge sprite: opaque at `from` on the x axis, fading to nothing `len` away in direction `dir` (+1 / -1), `h` tall, centred at y.
    void EdgeX(ref int n, float from, float dir, float len, float y, float h, Color c)
    {
        Put(ref n, AmbientShape.Edge, new Vector2(from, y), 0f, dir > 0f ? len : -len, h, c);
    }

    // SOLAR FLARES (Space): bands of plasma crossing the screen sideways. Procedural layers (soft glow edges, a hot core, plasma arcs
    // streaming behind the leading edge, sparks thrown ahead of it); the painted sheet (DrawBodyArt) goes on top when installed.
    // The WARNING is the origin edge flickering and glowing, a few hot slivers and sparks drifting in. Pink is allowed here.
    int DrawFlare(HazardField f, int n, bool warn, float p, float env)
    {
        var d = f.def;
        float H = f.viewTop - f.viewBottom + 1.2f, midY = (f.viewTop + f.viewBottom) * .5f;
        float tm = f.t, lane = f.laneHalf;
        float flick = .7f + .3f * Hash((int)(tm * 16f), 5);
        if (warn)
        {
            float x0 = -f.side * lane;
            float w = .35f + 1.7f * p;
            EdgeX(ref n, x0, f.side, w * 1.4f, midY, H, WithA(FlareMagenta, (.07f + .18f * p) * flick));
            EdgeX(ref n, x0, f.side, w, midY, H, WithA(FlareOrange, (.10f + .21f * p) * flick));
            // hot slivers: short bright bars on the edge, jumping about (the front's tip trying to break through)
            for (int i = 0; i < 6; i++)
            {
                int cyc = (int)(tm * 7f) + i * 13;
                if (Hash(cyc, 9 + i) > .35f + .5f * p) continue;
                float y = f.viewBottom + Hash(cyc, 3) * (f.viewTop - f.viewBottom);
                Put(ref n, AmbientShape.Streak, new Vector2(x0 + f.side * .08f, y), 0f, .55f, .6f + 1.8f * Hash(cyc, 4), WithA(FlareHot, (.25f + .35f * p) * flick));
            }
            for (int i = 0; i < 4 + (int)(14 * p); i++)       // sparks drifting in from the edge
            {
                float jx = Hash(i, 21), jy = Hash(i, 22);
                float k = Mathf.Repeat(jx + tm * (.35f + .4f * jy), 1f);
                Put(ref n, AmbientShape.Dot, new Vector2(x0 + f.side * k * (1.5f + 3f * p), f.viewBottom + Hash(i, 23) * (f.viewTop - f.viewBottom)), 0f, .05f + .07f * jy, WithA(i % 3 == 0 ? FlareMagenta : FlareGold, (1f - k) * (.4f + .4f * p)));
            }
            return n;
        }
        float fade = Mathf.Clamp01(env * 1.6f);
        for (int b = 0; b < d.bands; b++)
        {
            float cx = f.sweepX - f.side * b * d.bandGap;
            float lead = cx + f.side * d.size * .5f;
            if (Mathf.Abs(cx) > lane + d.size * 1.6f) continue;            // wholly gone / not yet in view
            float fl = .85f + .15f * Hash((int)(tm * 20f) + b * 7, 6);
            // glow layers, widest and softest first
            EdgeX(ref n, lead, -f.side, d.size * 2.1f, midY, H, WithA(FlareViolet, .18f * fade * fl));
            EdgeX(ref n, lead, -f.side, d.size * 1.45f, midY, H, WithA(FlareMagenta, .24f * fade * fl));
            EdgeX(ref n, lead, -f.side, d.size * .95f, midY, H, WithA(FlareOrange, .35f * fade * fl));
            EdgeX(ref n, lead, f.side, d.size * .5f, midY, H, WithA(FlareGold, .29f * fade * fl));   // the shocked air ahead of it
            EdgeX(ref n, lead, -f.side, d.size * .32f, midY, H, WithA(FlareGold, .56f * fade * fl));
            EdgeX(ref n, lead, -f.side, .2f, midY, H, WithA(FlareHot, .71f * fade));                 // the white-hot core line
            EdgeX(ref n, lead, f.side, .1f, midY, H, WithA(FlareHot, .6f * fade));
            // plasma arcs: long streaks trailing the leading edge, undulating, hot near the front and violet at the tail
            for (int i = 0; i < 12; i++)
            {
                float jy = Hash(i + b * 31, 31), ju = Hash(i + b * 31, 32);
                float y = f.viewBottom + Mathf.Repeat(jy + tm * .03f * (i % 2 == 0 ? 1f : -1f), 1f) * (f.viewTop - f.viewBottom);
                float u = .15f + .85f * ju;                                                           // how far behind the lead (0..1 of 1.7 widths)
                float wob = Mathf.Sin(tm * (5f + 3f * jy) + i * 1.9f) * .18f;
                float len = 1.0f + 2.2f * Hash(i, 33) * (1f - .4f * u);
                Color c = u < .35f ? Color.Lerp(FlareHot, FlareGold, u / .35f) : u < .7f ? Color.Lerp(FlareGold, FlareMagenta, (u - .35f) / .35f) : Color.Lerp(FlareMagenta, FlareViolet, (u - .7f) / .3f);
                Put(ref n, AmbientShape.Streak, new Vector2(lead - f.side * u * d.size * 1.7f, y + wob), -f.side * 90f, .6f, len, WithA(c, (.8f - .45f * u) * .7f * fade));
            }
            // sparks thrown ahead of the shock
            for (int i = 0; i < 10; i++)
            {
                float jx = Hash(i + b * 17, 41), jy = Hash(i + b * 17, 42);
                float k = Mathf.Repeat(jx + tm * (.9f + .7f * jy), 1f);
                Put(ref n, AmbientShape.Dot, new Vector2(lead + f.side * (.1f + k * 2.2f), f.viewBottom + jy * (f.viewTop - f.viewBottom)), 0f, .05f + .09f * jx, WithA(i % 3 == 0 ? FlareHot : FlareGold, (1f - k) * .9f * fade));
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
        else if (d.shape == HazardShape.Sweep)
        {
            // the painted flare: per band a stack of square tiles (cell width = FlareTile x the band's width) laid up the view, the cell's
            // hot leading edge (FlareLead of the cell's width) on the band's leading rim, the tail behind it; mirrored when it travels left
            float W = d.size * FlareTile;
            int perBand = Mathf.Max(1, Mathf.CeilToInt((f.viewTop - f.viewBottom) / (W * .92f)));
            int used = 0;
            int frame = (int)(f.t * 9f);
            for (int b = 0; b < d.bands; b++)
            {
                float lead = f.sweepX - f.side * b * d.bandGap + f.side * d.size * .5f;
                float cx = lead - f.side * (FlareLead - .5f) * W;
                if (warn) { lead = -f.side * f.laneHalf; cx = lead - f.side * (FlareLead - .5f) * W; if (b > 0) break; }
                for (int i = 0; i < perBand && used < artSr.Length; i++)
                {
                    var r = artSr[used++]; r.enabled = true;
                    r.sprite = warn ? HazardArt.Cell(2, d.artWarnCol + ((int)(f.t * 4f) & 1)) : HazardArt.Cell(d.artRow, (frame + i * 2 + b * 3) % 6);
                    r.transform.position = new Vector3(cx, f.viewBottom + W * .5f + i * W * .92f, 0f);
                    r.transform.rotation = Quaternion.identity;
                    r.transform.localScale = new Vector3(W * (f.side < 0f ? -1f : 1f), W, 1f);
                    float edgeFade = Mathf.Clamp01(1.4f - Mathf.Abs(cx) / (f.laneHalf + W * .8f));   // dims as it leaves the view
                    r.color = new Color(1f, 1f, 1f, warn ? (.21f + .35f * p) : .7f * k * Mathf.Clamp01(edgeFade + .3f));
                }
            }
            artDrawn = used;
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
