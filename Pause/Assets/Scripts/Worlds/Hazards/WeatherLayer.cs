using UnityEngine;

// AMBIENT WEATHER (docs/world-weather-hazards.md 3.1): a pooled layer of small sprites that gives each world its
// own always-on weather over the backdrop (snow, sand grains, rain streaks, embers, bubbles, pollen, ion motes).
// No gameplay effect and no visibility limit (alpha <= AmbientMaxAlpha, tiny sprites). Particles are advected by
// the live hazard field, so a tornado visibly pulls the rain into itself. Placeholder sprites (WeatherSprites)
// until Codex's ambient sheets are vetted. All pooled (WorldWeatherTable.AmbientMax renderers, built once);
// Step allocates nothing.
public sealed class WeatherLayer
{
    public const int SortingOrder = -8;
    // The hook for a future player option ("reduce motion"): fewer particles.
    public static bool Reduced;
    public static float ReducedShare = .4f;

    struct P { public Vector2 pos, vel; public float seed, phase; public int kind; }

    readonly SpriteRenderer[] sr = new SpriteRenderer[WorldWeatherTable.AmbientMax];
    readonly P[] ps = new P[WorldWeatherTable.AmbientMax];
    readonly Transform root;
    int world = -1, count;
    float fade;                      // 0..1 global alpha ramp (world change, planetfall)
    float clock;
    WorldWeatherEntry entry;
    float halfW = 3.7f, top = 5f, bottom = -5f;

    public int Count => count;
    public int WorldIndex => world;
    public float Fade => fade;
    public int Visible { get { int n = 0; for (int i = 0; i < sr.Length; i++) if (sr[i].enabled) n++; return n; } }

    public WeatherLayer()
    {
        root = new GameObject("~WeatherLayer").transform;
        for (int i = 0; i < sr.Length; i++)
        {
            var go = new GameObject("w" + i, typeof(SpriteRenderer));
            go.transform.SetParent(root, false);
            var r = go.GetComponent<SpriteRenderer>();
            r.sortingOrder = SortingOrder;
            r.enabled = false;
            sr[i] = r;
        }
    }

    public void Destroy()
    {
        WeatherArt.Unload();
        if (root != null) Object.DestroyImmediate(root.gameObject);
    }

    void SetWorld(int w)
    {
        world = w;
        entry = WorldWeatherTable.ForWorld(w);
        count = 0;
        WeatherArt.Load(w);
        if (entry == null) return;
        int k = 0;
        for (int a = 0; a < entry.ambient.Length; a++)
        {
            int n = Mathf.CeilToInt(entry.ambient[a].count * WorldWeatherTable.AmbientBoost);
            if (Reduced) n = Mathf.CeilToInt(n * ReducedShare);
            for (int i = 0; i < n && k < ps.Length; i++, k++)
                ps[k] = new P
                {
                    kind = a, seed = Hash(k, 11), phase = Hash(k, 12) * 6.283f,
                    pos = new Vector2((Hash(k, 13) - .5f) * 2f * halfW, Mathf.Lerp(bottom, top, Hash(k, 14))),
                };
        }
        count = k;
        fade = 0f;
    }

    static float Hash(int i, int k) { return HazardVisuals.Hash(i, k); }

    public static bool Quiet
    {
        get { return Planetfall.HoldsShip || Liftoff.HoldsShip || PortalArrival.Active; }
    }

    // One frame. dt 0 while the world is frozen (nothing moves, nothing flickers).
    public void Step(float dt, bool on, int worldIndex, HazardField[] fields)
    {
        var cam = Camera.main;
        if (cam != null && cam.orthographic)
        {
            top = cam.transform.position.y + cam.orthographicSize;
            bottom = cam.transform.position.y - cam.orthographicSize;
            halfW = cam.orthographicSize * cam.aspect + .3f;
        }
        if (worldIndex != world) SetWorld(worldIndex);
        float target = on && !Quiet ? 1f : 0f;
        fade = Mathf.MoveTowards(fade, target, (dt > 0f ? dt : 0f) * .7f);
        if (entry == null || fade <= 0f)
        {
            for (int i = 0; i < sr.Length; i++) if (sr[i].enabled) sr[i].enabled = false;
            return;
        }
        clock += dt;
        int tick = (int)(clock * 7f);
        for (int i = 0; i < count; i++)
        {
            var p = ps[i];
            var k = entry.ambient[p.kind];
            if (dt > 0f)
            {
                Vector2 v = k.drift * (.8f + .4f * p.seed);
                v.x += Mathf.Sin(clock * k.swayHz * 6.283f + p.phase) * k.sway;
                // the live wind of any hazard drags it (the same field the forces use)
                for (int f = 0; f < fields.Length; f++)
                {
                    var hf = fields[f];
                    if (hf.Acting && HazardForce.Sample(hf, p.pos, HazardTarget.Body, out Vector2 w)) v += w * (WorldWeatherTable.AmbientWindGain * (1f + k.windLean));
                }
                // the always-on wind carries it too (the same function that bends the bullets), strongest on the light particles
                if (WeatherWind.Profile != null) v += WeatherWind.Sample(WeatherWind.Profile, p.pos, HazardTarget.Hostile, WeatherWind.Clock, WeatherWind.GustEnvelope, WeatherWind.Side, WeatherWind.Fade) * (WindVisuals.VisualGain * .8f * (1f + k.windLean));
                p.vel = v;
                p.pos += v * dt;
                if (p.pos.y > top + .4f) { p.pos.y = bottom - .3f; p.pos.x = (Hash(i, tick) - .5f) * 2f * halfW; }
                else if (p.pos.y < bottom - .4f) { p.pos.y = top + .3f; p.pos.x = (Hash(i, tick + 5) - .5f) * 2f * halfW; }
                if (p.pos.x > halfW + .4f) p.pos.x = -halfW - .3f;
                else if (p.pos.x < -halfW - .4f) p.pos.x = halfW + .3f;
                ps[i] = p;
            }
            var r = sr[i];
            bool art = k.artKind >= 0 && WeatherArt.LoadedFor(world);
            r.sprite = art ? WeatherArt.Get(k.artKind, (int)(p.seed * 4f)) : WeatherSprites.Get(k.shape);
            Vector2 d = p.vel.sqrMagnitude > 1e-4f ? p.vel : k.drift;
            float rot = 0f;
            if (art) rot = 0f;                                  // the painted particles carry their own slant
            else if (k.shape == AmbientShape.Streak) rot = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f;
            else if (k.shape == AmbientShape.Leaf || k.shape == AmbientShape.Flake) rot = clock * 40f * (p.seed - .5f) + p.phase * 57f;
            var t = r.transform;
            t.position = new Vector3(p.pos.x, p.pos.y, 0f);
            t.rotation = Quaternion.Euler(0f, 0f, rot);
            float s = k.size * (.8f + .4f * p.seed);
            t.localScale = new Vector3(s, s, 1f);
            Color c = art ? Color.white : k.color;
            float flick = k.shape == AmbientShape.Ember ? .75f + .25f * Mathf.Sin(clock * 9f + p.phase) : 1f;
            c.a = Mathf.Min(k.alpha, WorldWeatherTable.AmbientMaxAlpha) * fade * flick;
            r.color = c;
            r.enabled = true;
        }
        for (int i = count; i < sr.Length; i++) if (sr[i].enabled) sr[i].enabled = false;
    }
}
