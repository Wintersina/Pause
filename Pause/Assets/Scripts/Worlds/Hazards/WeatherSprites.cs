using UnityEngine;

// The procedural PLACEHOLDER pixel sprites of the weather particles and hazard bodies (white, tinted at draw
// time, point-filtered, longest side = 1 world unit at scale 1). They stand in until Codex's sheets
// (Art/Weather~/<World>/, docs/weather-codex-prompts) are vetted and wired. Built once.
public static class WeatherSprites
{
    static Sprite[] cache;

    public static Sprite Get(AmbientShape s)
    {
        if (cache == null) cache = new Sprite[System.Enum.GetValues(typeof(AmbientShape)).Length];
        int i = (int)s;
        if (cache[i] == null) cache[i] = Build(s);
        return cache[i];
    }

    // A soft horizontal gradient bar: opaque at the left (x = 0), fading to nothing at the right (x = 1 unit); 1 unit long, tinted at draw time.
    static Sprite BuildEdge()
    {
        const int W = 64, H = 64;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "weather_edge" };
        var px = new Color32[W * H];
        for (int x = 0; x < W; x++)
        {
            float u = x / (float)(W - 1);
            float a = (1f - u) * (1f - u) * (1f - .35f * u);          // quick falloff, a long soft tail
            for (int y = 0; y < H; y++) px[y * W + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
        }
        tex.SetPixels32(px); tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0f, .5f), W);   // pivot at the opaque end
    }

    static Sprite Build(AmbientShape s)
    {
        if (s == AmbientShape.Edge) return BuildEdge();
        int w, h;
        string[] rows;
        switch (s)
        {
            case AmbientShape.Streak:
                w = 3; h = 16; rows = new string[16];
                for (int y = 0; y < 16; y++) rows[y] = y < 2 || y > 13 ? "..." : ".#.";
                break;
            case AmbientShape.Flake:
                w = h = 7; rows = new[] { "...#...", ".#.#.#.", "..###..", "#######", "..###..", ".#.#.#.", "...#..." }; break;
            case AmbientShape.Leaf:
                w = 7; h = 5; rows = new[] { "..###..", ".#####.", "#######", ".#####.", "..###.." }; break;
            case AmbientShape.Bubble:
                w = h = 8; rows = new[] { "..####..", ".#....#.", "#.##...#", "#.#....#", "#......#", "#......#", ".#....#.", "..####.." }; break;
            case AmbientShape.Ember:
                w = 4; h = 6; rows = new[] { ".##.", "####", "####", ".##.", ".#..", ".#.." }; break;
            case AmbientShape.Grain:
                w = h = 2; rows = new[] { "##", "##" }; break;
            default:
                w = h = 4; rows = new[] { ".##.", "####", "####", ".##." }; break;
        }
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "weather_" + s };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool on = rows[h - 1 - y][x] == '#';
                // a highlight pixel on the upper-left of a dot-like body
                px[y * w + x] = on ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
        tex.SetPixels32(px); tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), Mathf.Max(w, h));
    }
}
