using UnityEngine;

// How loud the boss's on-beat telegraphs are, in one place to tune. Light dotted lines, fainter / thinner / later the
// busier the song gets (Intensity 0 = a calm section, 1 = a drop), so a fast wave never floods the screen. The safe gap
// is always drawn (a brighter dotted pair on its two edges); only how loudly changes. See docs/boss-telegraph-dotted.md.
public static class BossTelegraphStyle
{
    // The dots: one pooled tiled sprite per line. Dash + gap are pixels of a 1-pixel-wide texture at PixelsPerUnit.
    public const int DashPixels = 2, GapPixels = 3;
    public const float PixelsPerUnit = 12.5f;                       // period = 5 px = .4 world units, dot = .16
    public const float DashLength = DashPixels / PixelsPerUnit;
    public const float DashPeriod = (DashPixels + GapPixels) / PixelsPerUnit;
    public const int RingDots = 12;                                 // dots round a mine / bomb spot's ring

    // Intensity scaling: value at a calm section -> value at a drop.
    public const float EdgeAlphaCalm = .60f, EdgeAlphaBusy = .22f;  // the gap lane's two edges (the important line)
    public const float AimAlphaCalm = .32f, AimAlphaBusy = .12f;    // single aim / sight / spot marks
    public const float RingAlphaCalm = .45f, RingAlphaBusy = .18f;  // the dotted ring round a mine / bomb spot
    public const float WidthCalm = .07f, WidthBusy = .04f;          // world units across the dotted line
    public const float VisibleSecondsCalm = 1.6f, VisibleSecondsBusy = .55f;   // shown only for the last stretch before the beat
    public const float NonMusicIntensity = .3f;                     // scripted (non-musical) patterns
    public const float LaneMinHalfWidth = .1f;                      // wider than this = a gap lane: draw its two edges
    public const float PaleMix = .3f;                               // how far the world's cue colour is lightened toward white

    // Caps for everything on screen at once: summed alpha x dotted length (world units), and the pool size.
    public const float MaxTotalWeight = 16f;
    public const int MaxMarks = 16;

    // The muzzle charge / flash glow on the boss, softened toward a drop (1 = old strength).
    public const float MuzzleGlowCalm = .85f, MuzzleGlowBusy = .45f;
    public const float MuzzleSizeBusy = .8f;                        // size factor at a drop

    // The laser's own aim line (BossProjectiles) eases down toward a drop too: alpha factor calm -> drop.
    public const float SightAlphaCalm = 1f, SightAlphaBusy = .6f;
    public static float SightAlpha(float intensity) => Lerp(SightAlphaCalm, SightAlphaBusy, intensity);
    // The song's intensity, as last set by the live boss (BossActor); calm when none.
    public static float LiveIntensity = NonMusicIntensity;

    // WHICH attacks get a preview (the user's call: only the major ones). By default by kind:
    //   yes: Beam (boss lasers), Lash (vine lash), Jet, Wave, Blast, Strike, Roll: heavy area denial, nothing leaves the
    //        muzzle to read, so the telegraph is the only warning (each draws its own dotted preview / sight line)
    //   no:  Aimed, Fan, Lob, Pattern: bullets leaving the boss's muzzles are readable by themselves; the boss's tell
    //        pose and muzzle charge stay, but no lane / line / ring overlay (BossPatternMarkers) is drawn.
    // One attack opts in or out with BossAttack.telegraph (1 / 0).
    public static bool MajorByKind(BossAttackKind k) =>
        k == BossAttackKind.Beam || k == BossAttackKind.Lash || k == BossAttackKind.Jet || k == BossAttackKind.Wave ||
        k == BossAttackKind.Blast || k == BossAttackKind.Strike || k == BossAttackKind.Roll;

    // Is the dotted lane / aim / ring overlay drawn while `a` plays? Only a major bullet-hell pattern (none today).
    public static bool OverlayAllowed(BossAttack a) => a != null && a.kind == BossAttackKind.Pattern && a.IsMajor;

    static float Lerp(float calm, float busy, float intensity) => Mathf.Lerp(calm, busy, Mathf.Clamp01(intensity));
    public static float EdgeAlpha(float intensity) => Lerp(EdgeAlphaCalm, EdgeAlphaBusy, intensity);
    public static float AimAlpha(float intensity) => Lerp(AimAlphaCalm, AimAlphaBusy, intensity);
    public static float RingAlpha(float intensity) => Lerp(RingAlphaCalm, RingAlphaBusy, intensity);
    public static float Width(float intensity) => Lerp(WidthCalm, WidthBusy, intensity);
    public static float VisibleSeconds(float intensity) => Lerp(VisibleSecondsCalm, VisibleSecondsBusy, intensity);
    public static float MuzzleAlpha(float intensity) => Lerp(MuzzleGlowCalm, MuzzleGlowBusy, intensity);
    public static float MuzzleSize(float intensity) => Lerp(1f, MuzzleSizeBusy, intensity);

    static Sprite dash, ring;

    // The tiled dotted-line sprite (1 px wide, repeats along its height). Built once.
    public static Sprite Dash
    {
        get
        {
            if (dash != null) return dash;
            int h = DashPixels + GapPixels;
            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false) { name = "TelegraphDash", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            for (int y = 0; y < h; y++) tex.SetPixel(0, y, y < DashPixels ? Color.white : new Color(1f, 1f, 1f, 0f));
            tex.Apply(false, false);
            dash = Sprite.Create(tex, new Rect(0, 0, 1, h), new Vector2(.5f, 0f), PixelsPerUnit, 0, SpriteMeshType.FullRect);
            dash.name = "TelegraphDash";
            return dash;
        }
    }

    // A ring of dots, 1 world unit across.
    public static Sprite Ring
    {
        get
        {
            if (ring != null) return ring;
            const int S = 48;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { name = "TelegraphRing", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var clear = new Color(1f, 1f, 1f, 0f);
            for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) tex.SetPixel(x, y, clear);
            for (int i = 0; i < RingDots; i++)
            {
                float a = i * Mathf.PI * 2f / RingDots;
                int cx = Mathf.RoundToInt(S * .5f + Mathf.Cos(a) * (S * .5f - 3f)), cy = Mathf.RoundToInt(S * .5f + Mathf.Sin(a) * (S * .5f - 3f));
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) tex.SetPixel(cx + dx, cy + dy, Color.white);
            }
            tex.Apply(false, false);
            ring = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(.5f, .5f), S);
            ring.name = "TelegraphRing";
            return ring;
        }
    }
}
