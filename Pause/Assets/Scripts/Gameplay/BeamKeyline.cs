using UnityEngine;

// THE ONE PLACE the warning beams' look lives (user, 2026-10-25: "the warning beams all around need subtle black lines all around, all the
// worlds", then "hollow ... dotted squares"). Every warning beam / aim line / marker lane / elite sight line is drawn as a chain of small HOLLOW
// SQUARES along its length: a coloured edge (the world's cue colour), a thin near-black line outside it (#05070a, ~1-2 px) and a see-through
// centre, spaced with gaps and marching gently toward the far end. The same in every world, present for the whole warning phase, fading and
// blinking with the warning's own alpha. Only warnings: the fired beam body, projectiles and ships never get one.
//
//   * Layer    ONE extra pooled Tiled SpriteRenderer under a beam (BeamKeyline.Attach, built once with the beam). Fit* lays its geometry from
//              the beam's own (the sheared rail-mine parallelogram: a child of the beam's art transform; the straight boss / marker lines:
//              a child of the line); Sync, every frame after the beam is placed and coloured, shows it with the beam, hides the beam's own sprite
//              (alpha 0, still enabled for the code that reads it) and lays the march. Plain SpriteRenderers, so the kill cam records them.
//   * Sight    SquareSight: the same chain on a free-standing renderer (the elites' sight lines, Frost's cone edges and tethers).
//   * Rims     TelegraphStyle's baked dots and marker rings use the same near-black (Shade) and alpha (Alpha).
public static class BeamKeyline
{
    public const float Shade = 5f / 255f;                      // #05070a
    public static readonly Color Colour = new Color(5f / 255f, 7f / 255f, 10f / 255f, 1f);
    public const float Alpha = .8f;                            // the squares' strength (dark line and coloured edge), x the warning's own alpha
    public static bool Suppress;                               // before / after contact sheets only: the layers stay off and the beam shows

    // The chain: each cell's side is the beam's visible width (never under SquareMin), the gap SquareGapShare of the side, and it marches toward
    // the far end at SquareMarchSpeed world u/s (0 = still) on real time that stops while the game is paused (timeScale 0), so it is the same
    // speed at any game speed (dev x30 included).
    public const float MinVisible = .03f;
    public const float SquareMin = .09f, SquareGapShare = .5f, SquareMarchSpeed = .5f;
    public const float SquareEliteMin = .05f;                  // the elites' hairline sight lines: smaller cells
    public static float ClockOverride = -1f;                   // previews / tests: a fixed march phase (>= 0 seconds)

    static float clock, lastReal;
    static int clockFrame = -1;

    public static float MarchClock
    {
        get
        {
            if (ClockOverride >= 0f) return ClockOverride;
            if (clockFrame != Time.frameCount)
            {
                float r = Time.realtimeSinceStartup;
                if (Time.timeScale > 0f) clock += r - lastReal;   // frozen while paused
                lastReal = r;
                clockFrame = Time.frameCount;
            }
            return clock;
        }
    }

    // The visible width of a tiled aim line whose painted line fills `px` of its 128 px wide tile, the tile `tile` world units wide.
    public static float VisibleOf(int px, float tile) => Mathf.Max(MinVisible, px / 128f * tile);

    // The world's coloured edge (EnemyRoster.WorldKeys order), read off each world's aim-line art.
    static readonly Color[] cue =
    {
        new Color(1f, .31f, .88f), new Color(.31f, .85f, 1f), new Color(.75f, 1f, .82f), new Color(1f, .69f, .13f),
        new Color(.37f, .91f, 1f), new Color(.72f, .61f, 1f), new Color(1f, .82f, .29f),
    };
    public static Color CueColour(int world) => cue[Mathf.Clamp(world, 0, cue.Length - 1)];

    const int SqSide = 32, SqDark = 2, SqEdge = 3;
    const int SqGap = (int)(SqSide * SquareGapShare);
    static Sprite squares;

    // one cell: SqSide px square (dark SqDark px outside, coloured SqEdge px, faint fill) + SqGap px of nothing, pivot bottom centre, 1 u wide
    public static Sprite SquaresSprite
    {
        get
        {
            if (squares != null) return squares;
            int h = SqSide + SqGap;
            var t = new Texture2D(SqSide, h, TextureFormat.RGBA32, false) { name = "BeamSquares", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            var clear = new Color(1f, 1f, 1f, 0f);
            var dark = new Color(Colour.r, Colour.g, Colour.b, 1f);
            var fill = new Color(1f, 1f, 1f, .1f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < SqSide; x++)
                {
                    Color c = clear;
                    if (y < SqSide)
                    {
                        int d = Mathf.Min(Mathf.Min(x, SqSide - 1 - x), Mathf.Min(y, SqSide - 1 - y));
                        c = d < SqDark ? dark : d < SqDark + SqEdge ? Color.white : fill;
                    }
                    t.SetPixel(x, y, c);
                }
            t.Apply(false, false);
            squares = Sprite.Create(t, new Rect(0, 0, SqSide, h), new Vector2(.5f, 0f), SqSide, 0, SpriteMeshType.FullRect);
            squares.name = "BeamSquares";
            squares.hideFlags = HideFlags.DontSave;
            return squares;
        }
    }

    // Lays `sr` (Tiled) as a marching chain of squares `side` wide along `len` from `beamStartAlong` (world u from the parent's origin), the
    // parent's world size per local unit being latPerX / alongPerY. The strip starts one period early (shifted by the march) and runs to the end.
    static void LaySquares(SpriteRenderer sr, float latPerX, float alongPerY, float side, float len, float beamStartAlong)
    {
        float period = side * (SqSide + SqGap) / SqSide;
        float s = SquareMarchSpeed > 0f ? Mathf.Repeat(MarchClock * SquareMarchSpeed, period) : 0f;
        sr.drawMode = SpriteDrawMode.Tiled;
        sr.tileMode = SpriteTileMode.Continuous;
        sr.size = new Vector2(1f, Mathf.Max(.001f, len + period - s) / side);
        var tr = sr.transform;
        tr.localRotation = Quaternion.identity;
        tr.localPosition = new Vector3(0f, (beamStartAlong + s - period) / alongPerY, 0f);
        tr.localScale = new Vector3(side / latPerX, side / alongPerY, 1f);
    }

    // One warning's pooled layer (built once with the beam).
    public sealed class Layer
    {
        public SpriteRenderer squares;
        public Color tint = Color.white;             // the coloured edge (CueColour, or a marker's own tint)
        public float latPerX = 1f, alongPerY = 1f, visible, len, centre;   // the last Fit
        public bool Shown => squares != null && squares.enabled;
    }

    // The layer for `beam`: a child of `parent`, sorted with the beam.
    public static Layer Attach(Transform parent, SpriteRenderer beam)
    {
        var go = new GameObject("KeylineSquares");
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = SquaresSprite;
        if (beam != null) { sr.sortingLayerID = beam.sortingLayerID; sr.sortingOrder = beam.sortingOrder; }
        sr.enabled = false;
        return new Layer { squares = sr };
    }

    // Straight beam (BossProjectiles, marker lines): `parent` is the beam's transform, its lateral world width per local x is `latPerX` and
    // its along-the-beam world length per local y is `alongPerY` (the tiled sprites: the transform scale). The chain covers `len` along the beam,
    // the beam's middle `centre` along it (world u from the parent's origin), `visible` wide.
    public static void FitStraight(Layer l, float latPerX, float alongPerY, float visible, float len, float centre)
    {
        if (l == null) return;
        l.latPerX = Mathf.Max(1e-5f, latPerX); l.alongPerY = Mathf.Max(1e-5f, alongPerY); l.visible = visible; l.len = len; l.centre = centre;
    }

    // Sheared beam (RailMineLaser.Span / SpanTiled): `art` is the beam's art transform under its frame (after Span / SpanTiled laid it);
    // `frameWidth` is the w that was given to Span, `len` its length, `visible` the visible line's width in the same units. The chain is a
    // child of `art`, so it rides the frame's rotation, scale and shear and both ends stay flush with the rails.
    public static void FitFrame(Layer l, Transform art, float frameWidth, float visible, float len)
    {
        if (l == null || art == null) return;
        var s = art.localScale;
        float ax = Mathf.Abs(s.x) > 1e-6f ? s.x : 1e-6f, ay = Mathf.Abs(s.y) > 1e-6f ? s.y : 1e-6f;
        FitStraight(l, Mathf.Max(1e-5f, frameWidth) * ax, Mathf.Max(.001f, len) * ay, visible, len, 0f);
    }

    // Every frame, after the beam was placed and coloured: the chain shows exactly when `beam` does, at Alpha x `baseAlpha` (the beam's alpha),
    // so it blinks and fades with it. `on`: false hides it (a spot's ring).
    public static void Sync(Layer l, SpriteRenderer beam, float baseAlpha, bool on = true)
    {
        if (l == null) return;
        on = on && !Suppress && beam != null && beam.enabled && beam.sprite != null;
        l.squares.enabled = on;
        if (!on) return;
        var bc = beam.color;
        bc.a = 0f;                                   // the beam's own sprite is hidden (still enabled: the logic reads it)
        beam.color = bc;
        LaySquares(l.squares, Mathf.Max(1e-5f, l.latPerX), Mathf.Max(1e-5f, l.alongPerY), Mathf.Max(l.visible, SquareMin), l.len, l.centre - l.len * .5f);
        l.squares.color = new Color(l.tint.r, l.tint.g, l.tint.b, Alpha * baseAlpha);
    }

    // The warning is over (the fired beam has no squares).
    public static void Off(Layer l)
    {
        if (l != null) l.squares.enabled = false;
    }

    // A free-standing sight line (an elite's, a Frost cone edge / tether): the chain from `from` along `deg` for `length`, `width` wide;
    // `lineAlpha` is the line's own blink level (TelegraphStyle.LineAlpha / LineAlphaLow).
    public static void SquareSight(SpriteRenderer sr, Vector2 from, float deg, float length, float width, Color tint, float lineAlpha)
    {
        sr.sprite = SquaresSprite;
        sr.transform.position = new Vector3(from.x, from.y, 0f);
        sr.transform.rotation = Quaternion.Euler(0f, 0f, deg - 90f);
        LaySquares(sr, 1f, 1f, Mathf.Max(Mathf.Min(width, TelegraphStyle.MaxLineWidth), SquareEliteMin), length, 0f);   // (a hairline ask stays a small cell)
        sr.color = new Color(tint.r, tint.g, tint.b, Mathf.Min(1f, Alpha * lineAlpha / TelegraphStyle.LineAlpha));
    }
}
