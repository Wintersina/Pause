using UnityEngine;

// The "wrapper" every hostile projectile wears so it reads on any backdrop:
// a soft bright halo with a thin dark hairline just outside the shot's body
// and a light rim outside that. The light rim and halo carry it over the
// dark nebulae; the dark hairline carries it over a bright flare (an
// aurora, a volcano burst, an explosion). It pulses a little (scale and
// alpha) so a shot reads as alive even when it hangs frozen.
//
// One tiny shared texture each for the round halo and the laser sheath,
// made in code (no art file), Point-filtered so the rims stay crisp pixels,
// drawn with the default sprite material by one extra SpriteRenderer per
// shot: everything batches, nothing allocates after the first use. The
// glow has no collider -- a shot's hitbox is exactly what it was.
//
// Profile (u = distance from the centre / the sprite's half-size):
//   u < BodyEdge     a faint pale fill (shows through the shot's art)
//   .. DarkEdge      a dark hairline (bright backdrops)
//   .. LightEdge     a light rim in the source's pale tint (dark backdrops)
//   .. 1             a soft halo, fading out
// The shot's body edge sits at BodyEdge, so the glow's world diameter is
// 2 * bodyRadius / BodyEdge -- the wrapper extends only a little past the
// art, the shot is drawn at the same size as before.
public static class HostileGlow
{
    public const int Texels = 32;
    public const float BodyEdge = .55f, DarkEdge = .66f, LightEdge = .79f;
    public const float FillAlpha = .22f, DarkAlpha = .85f, HaloAlpha = .55f;
    public const float PulseHz = 2.2f, PulseScale = .07f, PulseAlpha = .15f;
    public const int SortBehindShots = 29, SortBehindBeam = 25;
    // A boss shot's drawn body, as a multiple of its hit radius; a laser's
    // as a share of its drawn width (half of it: the edge).
    public const float BossShotBody = 1.35f, BeamBody = .5f;
    // An elite shot's drawn body: half its drawn diameter (shotSize).
    public const float EliteShotBody = .5f;
    // A landed resin pool: its flat drawing is mostly rim, so a little less.
    public const float PoolBody = .36f;
    // The sheath sprite's height at scale 1 (4 texels of 32 per unit).
    public const float SheathHeight = 4f / Texels;

    static Sprite halo, sheath;

    // The round wrapper: one world unit across at scale 1.
    public static Sprite Halo
    {
        get
        {
            if (halo == null) halo = Make(Texels, Texels, "HostileGlowHalo", round: true);
            return halo;
        }
    }

    // A laser's sheath: the same profile across x, flat along y; one world
    // unit wide and long at scale 1.
    public static Sprite Sheath
    {
        get
        {
            if (sheath == null) sheath = Make(Texels, 4, "HostileGlowSheath", round: false);
            return sheath;
        }
    }

    // World diameter of the wrapper around a body of this radius.
    public static float DiameterFor(float bodyRadius) => 2f * bodyRadius / BodyEdge;

    // Colour and alpha of the profile at u (0 centre .. 1 edge); rgb is a
    // grey the renderer's tint multiplies.
    public static Color Profile(float u)
    {
        if (u < BodyEdge) return new Color(1f, 1f, 1f, FillAlpha);
        if (u < DarkEdge) return new Color(0f, 0f, 0f, DarkAlpha);
        if (u < LightEdge) return new Color(1f, 1f, 1f, 1f);
        if (u >= 1f) return new Color(1f, 1f, 1f, 0f);
        float k = 1f - (u - LightEdge) / (1f - LightEdge);
        return new Color(1f, 1f, 1f, HaloAlpha * k * Mathf.Sqrt(k));
    }

    static Sprite Make(int w, int h, string name, bool round)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.name = name;
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.HideAndDontSave;
        var px = new Color[w * h];
        float half = w * .5f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (x + .5f - half) / half;
                float dy = round ? (y + .5f - h * .5f) / (h * .5f) : 0f;
                px[y * w + x] = Profile(Mathf.Sqrt(dx * dx + dy * dy));
            }
        tex.SetPixels(px);
        tex.Apply(false, true);
        var s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), round ? w : w, 0, SpriteMeshType.FullRect);
        s.name = name;
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    // The player's own red (its hearts, its hit flash, the red atom) is
    // never a hostile colour: a reddish source is pushed out to amber or
    // magenta-pink, then everything is paled towards white so the rim reads
    // as light.
    public const float RedBandDeg = 28f;

    public static bool IsPlayerRed(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        float deg = h * 360f;
        return s > .35f && v > .25f && (deg < RedBandDeg || deg > 360f - RedBandDeg);
    }

    public static Color Tint(Color source)
    {
        Color.RGBToHSV(source, out float h, out float s, out float v);
        float deg = h * 360f;
        if (s > .35f && (deg < RedBandDeg || deg > 360f - RedBandDeg))
            h = deg < 180f ? 34f / 360f : 318f / 360f;
        var c = Color.HSVToRGB(h, s, Mathf.Max(v, .85f));
        c = Color.Lerp(c, Color.white, .35f);
        c.a = 1f;
        return c;
    }

    // The wrapper's pulse at `age` seconds: a scale factor, an alpha.
    public static float PulseScaleAt(float age) => 1f + PulseScale * Mathf.Sin(age * PulseHz * 2f * Mathf.PI);
    public static float PulseAlphaAt(float age) => 1f - PulseAlpha * (.5f + .5f * Mathf.Sin(age * PulseHz * 2f * Mathf.PI + 1.3f));

    // A glow renderer child under `parent`, drawn just behind the shot.
    public static SpriteRenderer Attach(Transform parent, int order, bool beam = false)
    {
        var go = new GameObject("Glow");
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = beam ? Sheath : Halo;
        sr.sortingOrder = order;
        return sr;
    }
}
