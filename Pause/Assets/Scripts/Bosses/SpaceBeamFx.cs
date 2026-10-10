using UnityEngine;

// Procedural art for the Void Archon's pod lasers (BossBeam): a white-hot core inside a hot-pink body with soft
// edges and travelling noise (four frames), a radial glow for the windup flare at the pod and the muzzle, and a
// radial glow plus a spark star for the impact on a rail. Built once, cached. Visual only: no hit shape depends on
// it (the hitbox is BossConfig.BeamHitFraction of the beam width, as for every laser). Hostile pink-leaning hue.
public static class SpaceBeamFx
{
    public const int BeamFrames = 4;
    const int BW = 48, BH = 192;

    static Sprite[] beam, glow = new Sprite[1], star = new Sprite[1];

    public static bool Applies(BossDef boss) => boss != null && boss.artKey == "Space";

    static float Hash(int x, int y, int s)
    {
        float v = Mathf.Sin(x * 12.9898f + y * 78.233f + s * 37.719f) * 43758.547f;
        return v - Mathf.Floor(v);
    }

    static float Noise(float x, float y, int s)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        float a = Mathf.Lerp(Hash(xi, yi, s), Hash(xi + 1, yi, s), fx);
        float b = Mathf.Lerp(Hash(xi, yi + 1, s), Hash(xi + 1, yi + 1, s), fx);
        return Mathf.Lerp(a, b, fy);
    }

    static Sprite Make(Texture2D t) => Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(.5f, .5f), t.width);

    // 1 unit wide x 1 unit tall at scale 1 (BossBeam stretches it to width x length); local +y runs down the beam,
    // texture row 0 is the root at the pod.
    public static Sprite Beam(float age)
    {
        if (beam == null)
        {
            beam = new Sprite[BeamFrames];
            for (int f = 0; f < BeamFrames; f++)
            {
                var t = new Texture2D(BW, BH, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
                var px = new Color32[BW * BH];
                for (int y = 0; y < BH; y++)
                    for (int x = 0; x < BW; x++)
                    {
                        float u = Mathf.Abs((x + .5f) / BW * 2f - 1f);                    // 0 centre .. 1 edge
                        // noise drifts down the beam, a different phase per frame; stretched lengthwise so it reads as streaks
                        float n = Noise(x * .35f, y * .09f - f * 1.7f, 3) * .6f + Noise(x * .9f, y * .22f - f * 3.1f, 7) * .4f;
                        float body = Mathf.Clamp01(1f - Mathf.Pow(u, 2.2f));              // soft edge, no hard rectangle
                        float core = Mathf.Clamp01(1f - u / (.28f + .06f * n));
                        core = core * core;
                        float a = Mathf.Clamp01(body * (.72f + .5f * n)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(y / 6f));
                        Color body0 = Color.Lerp(new Color(.78f, .06f, .5f), new Color(1f, .3f, .78f), Mathf.Clamp01(body * (.5f + n)));
                        Color c = Color.Lerp(body0, new Color(1f, .93f, .98f), core);
                        // a bright rolling pulse inside the core
                        float pulse = Mathf.Clamp01(Noise(y * .05f - f * .9f, 0f, 11) * 1.4f - .25f) * core * .6f;
                        c = Color.Lerp(c, Color.white, pulse);
                        c.a = Mathf.Clamp01(a + core);
                        px[y * BW + x] = c;
                    }
                t.SetPixels32(px); t.Apply(false, true);
                beam[f] = Make(t);
            }
        }
        return beam[Mathf.FloorToInt(age / (BossArt.Tick * 2f)) % BeamFrames];
    }

    // A soft round flare, white at the middle to hot pink to nothing; 1 unit across at scale 1.
    public static Sprite Glow()
    {
        if (glow[0] == null)
        {
            const int S = 64;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float r = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(S * .5f, S * .5f)) / (S * .5f);
                    float a = Mathf.Clamp01(1f - r); a *= a;
                    float core = Mathf.Clamp01(1f - r / .4f);
                    Color c = Color.Lerp(new Color(1f, .22f, .66f), Color.white, core * core);
                    c.a = Mathf.Clamp01(a * 1.1f);
                    px[y * S + x] = c;
                }
            t.SetPixels32(px); t.Apply(false, true);
            glow[0] = Make(t);
        }
        return glow[0];
    }

    // A four-point spark star for the impact (thin cross + diagonals), 1 unit across.
    public static Sprite Star()
    {
        if (star[0] == null)
        {
            const int S = 64;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + .5f) / S * 2f - 1f, dy = (y + .5f) / S * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float cross = Mathf.Max(Mathf.Exp(-Mathf.Abs(dx) * 22f) * Mathf.Clamp01(1f - Mathf.Abs(dy)),
                                            Mathf.Exp(-Mathf.Abs(dy) * 22f) * Mathf.Clamp01(1f - Mathf.Abs(dx)));
                    float dd = Mathf.Abs(dx - dy) * .7071f, de = Mathf.Abs(dx + dy) * .7071f;
                    float diag = Mathf.Max(Mathf.Exp(-dd * 30f), Mathf.Exp(-de * 30f)) * Mathf.Clamp01(1f - r) * .6f;
                    float a = Mathf.Clamp01(Mathf.Max(cross, diag));
                    Color c = Color.Lerp(new Color(1f, .35f, .75f), Color.white, a * a);
                    c.a = a;
                    px[y * S + x] = c;
                }
            t.SetPixels32(px); t.Apply(false, true);
            star[0] = Make(t);
        }
        return star[0];
    }
}
