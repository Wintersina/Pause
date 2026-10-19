using UnityEngine;

// THE ONE PLACE the elites' (and the themed hazards') warning drawings get their weight: how thick a warning line is, how bright,
// how a ring is sized, how the blink steps. The user's call (2026-10-18, watching the Space elite clips): the telegraphs read too big
// and heavy -- a warning is a SUBTLE line, a thin hairline with a gentle blink and a faint dark rim for contrast on any backdrop.
//
//   sight line (EliteShip.ShowSight, the Frost cones, the streak shot's sight)   LineWidth wide, LineAlpha <-> LineAlphaLow
//   dotted lane / ring outlines (AttackPreview, EliteTelegraph)                  dots DotSize across, DotAlpha, DotSpacing apart
//   marker rings (EliteMarks, the lob / sling / fuse marks)                      a thin ring (EliteFxArt.MarkRing), RingScale x the
//                                                                                hazard's radius, RingAlpha <-> RingAlphaLow
// Every drawn line / ring carries a faint dark rim (RimShare of the sprite, RimAlpha): the contrast on a bright or busy backdrop.
public static class TelegraphStyle
{
    public const float LineWidth = .02f;          // world u across the drawn core of a warning line (about 3 screen px at phone width)
    public const float MaxLineWidth = .03f;       // no warning line is ever drawn wider than this (TelegraphStyleTest)
    public const float LineAlpha = .5f, LineAlphaLow = .22f;     // a sight line's two blink levels
    public const float RingAlpha = .5f, RingAlphaLow = .22f;     // a marker ring's
    public const float RingScale = .8f;           // marker rings are drawn this much of the hazard's radius (smaller, never hidden)
    public const float DotSize = .06f;            // a dotted outline's dot, rim included (core = a third of it)
    public const float DotSpacing = .12f;
    public const float DotAlpha = .42f;
    public const float BoldDotScale = 1.3f, BoldDotAlpha = .7f;  // the elites' preview on a bright world: a touch more
    public const float RimAlpha = .5f;            // the dark rim's alpha, x the tint's
    public const float RimShade = .1f;            // the rim's grey (x the tint colour)
    public const float BlinkLowShare = .55f;      // a dotted outline's off-beat alpha, x its on-beat

    public static Color WithAlpha(Color c, float a) { c.a = a; return c; }

    // The drawn core of a warning line asked for at `width`: never above the cap.
    public static float Line(float width) => Mathf.Min(Mathf.Max(.005f, width), MaxLineWidth);

    static Sprite dot, line;

    // A dot: a white core with a faint dark rim all round it (6 x 6 px, the sprite is DotSize across).
    public static Sprite Dot
    {
        get
        {
            if (dot != null) return dot;
            const int N = 6;
            var t = NewTex(N, N, "TelegraphDot");
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    bool core = x >= 2 && x <= 3 && y >= 2 && y <= 3;
                    bool corner = (x == 0 || x == N - 1) && (y == 0 || y == N - 1);
                    t.SetPixel(x, y, core ? Color.white : corner ? new Color(0, 0, 0, 0) : new Color(RimShade, RimShade, RimShade, RimAlpha));
                }
            t.filterMode = FilterMode.Bilinear;
            t.Apply(false, false);
            dot = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(.5f, .5f), N / DotSize, 0, SpriteMeshType.FullRect);
            dot.name = "TelegraphDot";
            dot.hideFlags = HideFlags.DontSave;
            return dot;
        }
    }

    // A sight line: a solid hairline (the middle third, white: tint it) between two faint dark rim columns; 3 px wide, 1 u tall
    // (pivot at the bottom, so the line grows from its muzzle). Scale x by Scale(width) and y by the length.
    public static Sprite Line1
    {
        get
        {
            if (line != null) return line;
            var t = NewTex(3, 4, "TelegraphLine");
            for (int y = 0; y < 4; y++)
            {
                t.SetPixel(0, y, new Color(RimShade, RimShade, RimShade, RimAlpha));
                t.SetPixel(1, y, Color.white);
                t.SetPixel(2, y, new Color(RimShade, RimShade, RimShade, RimAlpha));
            }
            t.filterMode = FilterMode.Bilinear;
            t.Apply(false, false);
            // 3 px = 3 x LineWidth world units at scale 1 (the core is the middle pixel: LineWidth)
            line = Sprite.Create(t, new Rect(0, 0, 3, 4), new Vector2(.5f, 0f), 1f / LineWidth, 0, SpriteMeshType.FullRect);
            line.name = "TelegraphLine";
            line.hideFlags = HideFlags.DontSave;
            return line;
        }
    }

    // The localScale.x that draws Line1's core `width` wide (the sprite is 4 px tall at 1/LineWidth ppu: the y scale for `length` u is length / (4 x LineWidth)).
    public static float ScaleX(float width) => Line(width) / LineWidth;
    public static float ScaleY(float length) => length / (4f * LineWidth);

    static Texture2D NewTex(int w, int h, string name)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
        return t;
    }
}
