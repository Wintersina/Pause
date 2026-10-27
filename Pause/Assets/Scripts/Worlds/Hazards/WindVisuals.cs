using UnityEngine;

// WHAT THE WIND IS DOING, DRAWN (docs/weather-overhaul.md): a pooled field of thin streaks advected by the very function that
// pushes the bullets (WeatherWind.Sample), so the player READS the force: layers of air visibly flow different ways where the
// shear changes sign, streaks lengthen and brighten in a gust, and bend round with the turbulence. Before a gust the edge it will
// come from GLOWS and streaks start to blow in from there (the telegraph, `warn` seconds), so a gust is never a surprise.
// All pooled (Streaks + 2 edge bars, built once), no per-frame allocation, in the world's own cue colour (pink only in Space,
// and Space has no wind), sorted with the ambient weather layer under the board.
public sealed class WindVisuals
{
    public const int Streaks = 56;
    public const int SortingOrder = -8;
    // The drawn streaks outrun the push (they are weather, not bodies): how much faster they move than the field's speed.
    public const float VisualGain = 2.2f;

    public const int Ribbons = 8, Debris = 10;
    struct S { public Vector2 pos; public float seed; }
    readonly SpriteRenderer[] ribbon = new SpriteRenderer[Ribbons], debris = new SpriteRenderer[Debris], head = new SpriteRenderer[2];
    readonly S[] rs = new S[Ribbons], ds = new S[Debris];
    readonly SpriteRenderer[] sr = new SpriteRenderer[Streaks];
    readonly SpriteRenderer[] edge = new SpriteRenderer[2];
    readonly S[] ss = new S[Streaks];
    readonly Transform root;
    float halfW = 3.7f, top = 5f, bottom = -5f;
    int world = -1;
    float fade;
    public int Visible { get { int n = 0; for (int i = 0; i < sr.Length; i++) if (sr[i].enabled) n++; return n; } }
    public float Fade => fade;

    public WindVisuals()
    {
        root = new GameObject("~WindFx").transform;
        for (int i = 0; i < Streaks; i++)
        {
            var go = new GameObject("s" + i, typeof(SpriteRenderer));
            go.transform.SetParent(root, false);
            var r = go.GetComponent<SpriteRenderer>();
            r.sortingOrder = SortingOrder;
            r.sprite = WeatherSprites.Get(AmbientShape.Streak);
            r.enabled = false;
            sr[i] = r;
        }
        for (int i = 0; i < Ribbons; i++) ribbon[i] = Art("r" + i);
        for (int i = 0; i < Debris; i++) debris[i] = Art("d" + i);
        for (int i = 0; i < head.Length; i++) head[i] = Art("h" + i);
        for (int i = 0; i < edge.Length; i++)
        {
            var go = new GameObject("e" + i, typeof(SpriteRenderer));
            go.transform.SetParent(root, false);
            var r = go.GetComponent<SpriteRenderer>();
            r.sortingOrder = SortingOrder;
            r.sprite = WeatherSprites.Get(AmbientShape.Edge);
            r.enabled = false;
            edge[i] = r;
        }
    }

    SpriteRenderer Art(string name)
    {
        var go = new GameObject(name, typeof(SpriteRenderer));
        go.transform.SetParent(root, false);
        var r = go.GetComponent<SpriteRenderer>();
        r.sortingOrder = SortingOrder;
        r.enabled = false;
        return r;
    }

    public void Destroy() { WindArt.Unload(); if (root != null) Object.DestroyImmediate(root.gameObject); }

    static float Hash(int i, int k) { return HazardVisuals.Hash(i, k); }

    void Reseed(int w)
    {
        world = w;
        WindArt.Load(w);
        for (int i = 0; i < Ribbons; i++) rs[i] = new S { seed = Hash(i, 61), pos = new Vector2((Hash(i, 62) - .5f) * 2f * halfW, Mathf.Lerp(bottom + .8f, top - .8f, (i + .5f) / Ribbons)) };
        for (int i = 0; i < Debris; i++) ds[i] = new S { seed = Hash(i, 63), pos = new Vector2((Hash(i, 64) - .5f) * 2f * halfW, Mathf.Lerp(bottom, top, Hash(i, 65))) };
        for (int i = 0; i < Streaks; i++)
            ss[i] = new S { seed = Hash(i, 51), pos = new Vector2((Hash(i, 52) - .5f) * 2f * halfW, Mathf.Lerp(bottom, top, (i + Hash(i, 53)) / Streaks)) };
    }

    public static Color CueColour(int world)
    {
        return Color.Lerp(WorldCue.Body(world), Color.white, .72f);
    }

    // The wind the streaks feel: the field, plus a preview of the coming gust while it is only warned (they start to lean into it).
    static Vector2 Feel(Vector2 at)
    {
        var p = WeatherWind.Profile;
        float env = WeatherWind.GustEnvelope;
        if (WeatherWind.Warning) env = Mathf.Max(env, .3f * WeatherWind.WarnProgress);
        return WeatherWind.Sample(p, at, HazardTarget.Hostile, WeatherWind.Clock, env, WeatherWind.Side, Mathf.Max(WeatherWind.Fade, .0001f));
    }

    public void Draw(float dt, int worldIndex)
    {
        var cam = CameraFit.Main;
        if (cam != null && cam.orthographic)
        {
            top = cam.transform.position.y + cam.orthographicSize;
            bottom = cam.transform.position.y - cam.orthographicSize;
            halfW = cam.orthographicSize * cam.aspect + .3f;
        }
        var prof = WeatherWind.Profile;
        if (worldIndex != world) Reseed(worldIndex);
        float target = prof != null && !WeatherLayer.Quiet ? 1f : 0f;
        fade = Mathf.MoveTowards(fade, target, (dt > 0f ? dt : 0f) * .8f);
        if (prof == null || fade <= 0f)
        {
            for (int i = 0; i < Streaks; i++) if (sr[i].enabled) sr[i].enabled = false;
            for (int i = 0; i < edge.Length; i++) if (edge[i].enabled) edge[i].enabled = false;
            HideArt();
            return;
        }
        Color cue = CueColour(worldIndex);
        float clock = WeatherWind.Clock;
        for (int i = 0; i < Streaks; i++)
        {
            var s = ss[i];
            Vector2 v = Feel(s.pos);
            float speed = v.magnitude;
            if (dt > 0f)
            {
                s.pos += v * (dt * VisualGain) + new Vector2(0f, Mathf.Sin(clock * .6f + s.seed * 9f) * .12f * dt);
                bool out_x = s.pos.x > halfW + 1f || s.pos.x < -halfW - 1f, out_y = s.pos.y > top + .6f || s.pos.y < bottom - .6f;
                if (out_x || out_y)
                {
                    // re-enter on the upwind side of the wind it feels, at a fresh height
                    int tick = (int)(clock * 3f) + i * 7;
                    float h = Mathf.Lerp(bottom, top, Hash(tick, 54));
                    if (speed > .05f && Mathf.Abs(v.x) >= Mathf.Abs(v.y) * .5f) s.pos = new Vector2(v.x > 0f ? -halfW - .8f : halfW + .8f, h);
                    else if (speed > .05f) s.pos = new Vector2((Hash(tick, 55) - .5f) * 2f * halfW, v.y > 0f ? bottom - .5f : top + .5f);
                    else s.pos = new Vector2((Hash(tick, 56) - .5f) * 2f * halfW, h);
                }
                ss[i] = s;
            }
            // brighter and longer the harder it blows; invisible in a dead calm
            float k = Mathf.Clamp01((speed - .15f) / 3.5f);
            var r = sr[i];
            if (k <= .02f) { r.enabled = false; continue; }
            float len = (.22f + 1.5f * k) * (.7f + .6f * s.seed);
            r.transform.position = new Vector3(s.pos.x, s.pos.y, 0f);
            r.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg - 90f);
            r.transform.localScale = new Vector3(.18f + .16f * k, len, 1f);
            Color c = cue; c.a = Mathf.Min(.5f, (.1f + .5f * k) * fade);
            r.color = c;
            r.enabled = true;
        }
        DrawTelegraph(cue);
        if (WindArt.LoadedFor(worldIndex)) DrawArt(dt); else HideArt();
    }

    void HideArt()
    {
        for (int i = 0; i < Ribbons; i++) if (ribbon[i].enabled) ribbon[i].enabled = false;
        for (int i = 0; i < Debris; i++) if (debris[i].enabled) debris[i].enabled = false;
        for (int i = 0; i < head.Length; i++) if (head[i].enabled) head[i].enabled = false;
    }

    // The painted wind (wx_gust.png): ribbons of streamlines riding the field (long and bright in a gust, a few faint in the breeze), debris
    // the world's own (snow, leaves, embers, foam, grit) carried along, and the gust's HEAD curling in from the upwind edge during the warning.
    // The art flows to +x; a ribbon is turned along the wind it feels and mirrored when that is leftward.
    void DrawArt(float dt)
    {
        float clock = WeatherWind.Clock;
        for (int i = 0; i < Ribbons; i++)
        {
            var s = rs[i];
            Vector2 v = Feel(s.pos);
            float speed = v.magnitude;
            if (dt > 0f)
            {
                s.pos += v * (dt * VisualGain * 1.15f);
                s.pos.y += Mathf.Sin(clock * .5f + s.seed * 7f) * .25f * dt;
                if (s.pos.x > halfW + 3f || s.pos.x < -halfW - 3f || s.pos.y > top + 1.5f || s.pos.y < bottom - 1.5f)
                {
                    int tick = (int)(clock * 2f) + i * 5;
                    s.pos = new Vector2(v.x >= 0f ? -halfW - 2.6f : halfW + 2.6f, Mathf.Lerp(bottom + .8f, top - .8f, Hash(tick, 66)));
                }
                rs[i] = s;
            }
            float k = Mathf.Clamp01((speed - .6f) / 3.2f);
            var r = ribbon[i];
            if (k <= .03f) { r.enabled = false; continue; }
            bool left = v.x < 0f;
            r.sprite = WindArt.Cell(0, (int)(s.seed * 4f) & 3);
            float w = 3.0f + 2.2f * k;
            r.transform.position = new Vector3(s.pos.x, s.pos.y, 0f);
            r.transform.rotation = Quaternion.Euler(0f, 0f, left ? Mathf.Atan2(-v.y, -v.x) * Mathf.Rad2Deg : Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg);
            r.transform.localScale = new Vector3(left ? -w : w, w, 1f);
            r.color = new Color(1f, 1f, 1f, Mathf.Min(.85f, (.1f + .9f * k) * fade * (.7f + .3f * Mathf.Sin(clock * 1.3f + s.seed * 9f))));
            r.enabled = true;
        }
        for (int i = 0; i < Debris; i++)
        {
            var s = ds[i];
            Vector2 v = Feel(s.pos);
            if (dt > 0f)
            {
                s.pos += v * (dt * VisualGain * .9f);
                if (s.pos.x > halfW + 1f || s.pos.x < -halfW - 1f || s.pos.y > top + 1f || s.pos.y < bottom - 1f)
                {
                    int tick = (int)(clock * 2f) + i * 3;
                    s.pos = new Vector2(v.x >= 0f ? -halfW - .6f : halfW + .6f, Mathf.Lerp(bottom, top, Hash(tick, 67)));
                }
                ds[i] = s;
            }
            float k = Mathf.Clamp01((v.magnitude - .4f) / 3f);
            var r = debris[i];
            if (k <= .03f) { r.enabled = false; continue; }
            r.sprite = WindArt.Cell(1, 2 + ((int)(s.seed * 2f) & 1));
            float sz = .35f + .35f * s.seed;
            r.transform.position = new Vector3(s.pos.x, s.pos.y, 0f);
            r.transform.rotation = Quaternion.Euler(0f, 0f, clock * 90f * (s.seed - .5f) + s.seed * 360f);
            r.transform.localScale = new Vector3(sz, sz, 1f);
            r.color = new Color(1f, 1f, 1f, Mathf.Min(.9f, (.2f + .8f * k) * fade));
            r.enabled = true;
        }
        // the head of a coming gust: it curls in from the upwind edge as the warning builds
        bool warn = WeatherWind.Warning;
        for (int i = 0; i < head.Length; i++)
        {
            var r = head[i];
            if (!warn) { r.enabled = false; continue; }
            var prof = WeatherWind.Profile;
            Vector2 d = new Vector2(prof.gustDir.x * WeatherWind.Side, prof.gustDir.y).normalized;
            float p = WeatherWind.WarnProgress;
            bool horizontal = Mathf.Abs(d.x) >= Mathf.Abs(d.y);
            float reach = horizontal ? halfW : (top - bottom) * .5f;
            Vector2 c = new Vector2(0f, (top + bottom) * .5f) - d * (reach + 1.6f - p * 3.2f) + new Vector2(0f, (i == 0 ? .25f : -.25f) * (top - bottom) * .5f);
            float sz = 4.4f * (.55f + .45f * p);
            r.sprite = WindArt.Cell(1, (i + (WeatherWind.Side > 0f ? 0 : 1)) & 1);
            r.transform.position = new Vector3(c.x, c.y, 0f);
            r.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            r.transform.localScale = new Vector3(sz, sz, 1f);
            r.color = new Color(1f, 1f, 1f, (.15f + .6f * p) * fade * (.75f + .25f * Mathf.Sin(WeatherWind.Clock * 12f + i)));
            r.enabled = true;
        }
    }

    // The upwind edge glows while a gust is warned (and a fainter one while it blows), bars of soft light the world's colour.
    void DrawTelegraph(Color cue)
    {
        var prof = WeatherWind.Profile;
        bool warn = WeatherWind.Warning;
        float g = WeatherWind.GustEnvelope;
        if (!warn && g <= .02f) { for (int i = 0; i < edge.Length; i++) edge[i].enabled = false; return; }
        Vector2 d = new Vector2(prof.gustDir.x * WeatherWind.Side, prof.gustDir.y);
        Vector2 u = -d.normalized;                            // toward the upwind edge
        float p = WeatherWind.WarnProgress;
        float a = warn ? (.12f + .3f * p) * (.75f + .25f * Mathf.Sin(WeatherWind.Clock * 14f)) : .16f * g;
        bool horizontal = Mathf.Abs(u.x) >= Mathf.Abs(u.y);
        for (int i = 0; i < edge.Length; i++)
        {
            var r = edge[i];
            float w = (i == 0 ? 2.4f : 1.2f) * (warn ? .4f + .6f * p : 1f);
            Color c = i == 0 ? cue : Color.Lerp(cue, Color.white, .5f);
            c.a = a * (i == 0 ? 1f : .8f) * fade;
            Vector3 pos;
            float rot;
            Vector3 scale;
            if (horizontal)
            {
                float sx = u.x > 0f ? halfW : -halfW;
                pos = new Vector3(sx, (top + bottom) * .5f, 0f);
                rot = u.x > 0f ? 180f : 0f;                    // opaque end on the screen edge, fading inward
                scale = new Vector3(w, top - bottom + 1f, 1f);
            }
            else
            {
                float sy = u.y > 0f ? top : bottom;
                pos = new Vector3(0f, sy, 0f);
                rot = u.y > 0f ? -90f : 90f;
                scale = new Vector3(w, halfW * 2f + 1f, 1f);
            }
            r.transform.position = pos;
            r.transform.rotation = Quaternion.Euler(0f, 0f, rot);
            r.transform.localScale = scale;
            r.color = c;
            r.enabled = true;
        }
    }
}
