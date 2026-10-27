using UnityEngine;

// Space's OPENING SKY: which planet (if any) the world starts with. The director's own stream is seeded, so it used to
// open every run on the same giant at the same spot; now each run rolls its start once:
//
//   kind      a weighted pick (OpeningWeights): sometimes no planet at all (nebula and stars only), otherwise the
//             giant (one of the six world planets) -- Space no longer loads the reference, neon or halo planets, so
//             those kinds are never available and drop out of the draw (None always stays).
//   look      the planet's size (OpeningScaleMin..Max), the side it sits on, how far down the view it starts and its
//             light (one of the planet tints) are rolled too, so a repeat of the same kind still looks different.
//
// Everything is drawn from its own stream (never the director's, never gameplay's), so the sky that follows is the
// same sky it always was. The planet queue (NextPlanet) is untouched: with no opening planet the first one simply
// arrives from the top after its usual wait. Edit-mode (tests, previews) keeps the old opening unless a test sets
// OpeningOverride.
public partial class SpaceDirector
{
    public enum OpeningKind { None = 0, Giant = 1, Reference = 2, Neon = 3, Halo = 4 }

    // Weight of each kind, indexed by OpeningKind: no planet at all, then each kind in proportion to its number of
    // cells (6 giants; the reference, neon and halo planets are retired, so 0), so with the uniform pick inside a
    // kind every cell is equally likely, like every spawn (SpaceDirector.EvenPlanetPool). SpacePlanetSheetTest
    // checks the counts against the art.
    public const float OpeningNoneWeight = 0.14f;
    public static readonly int[] OpeningCellCounts = { 0, 6, 0, 0, 0 };
    public static readonly float[] OpeningWeights = EvenOpeningWeights();
    static float[] EvenOpeningWeights()
    {
        int total = 0;
        foreach (int c in OpeningCellCounts) total += c;
        var w = new float[OpeningCellCounts.Length];
        w[0] = OpeningNoneWeight;
        for (int i = 1; i < w.Length; i++) w[i] = (1f - OpeningNoneWeight) * OpeningCellCounts[i] / total;
        return w;
    }
    public const float OpeningScaleMin = 0.8f, OpeningScaleMax = 1.2f;
    public const float OpeningYMin = 0.12f, OpeningYMax = 0.42f;   // share of the half-height the planet starts above the middle

    // Tests: force the kind (>= 0) / pin the stream. Edit-mode with no override keeps the legacy giant.
    public static int OpeningOverride = -1, OpeningSeedOverride = -1;

    public OpeningKind Opening { get; private set; } = OpeningKind.Giant;
    public bool OpeningWasLegacy { get; private set; }

    Sprite[] openingArt;
    Color openingTint;
    bool openingTinted;

    // The kind a roll u (0..1) picks, given which kinds have art. None is always available.
    public static OpeningKind PickOpening(double u, bool giant, bool reference, bool neon, bool halo)
    {
        float total = 0f;
        for (int i = 0; i < OpeningWeights.Length; i++)
            if (OpeningAvailable(i, giant, reference, neon, halo)) total += OpeningWeights[i];
        double t = u * total;
        for (int i = 0; i < OpeningWeights.Length; i++)
        {
            if (!OpeningAvailable(i, giant, reference, neon, halo)) continue;
            t -= OpeningWeights[i];
            if (t < 0.0) return (OpeningKind)i;
        }
        return OpeningKind.None;
    }

    static bool OpeningAvailable(int kind, bool giant, bool reference, bool neon, bool halo)
    {
        switch ((OpeningKind)kind)
        {
            case OpeningKind.Giant: return giant;
            case OpeningKind.Reference: return reference;
            case OpeningKind.Neon: return neon;
            case OpeningKind.Halo: return halo;
            default: return true;
        }
    }

    Sprite[] OpeningArt(OpeningKind k)
    {
        switch (k)
        {
            case OpeningKind.Giant: return giant;
            case OpeningKind.Reference: return referencePlanet;
            case OpeningKind.Neon: return neonPlanets;
            case OpeningKind.Halo: return haloPlanets;
            default: return null;
        }
    }

    static bool Has(Sprite[] a) { return a != null && a.Length > 0; }

    // Opens the sky with the rolled planet (or none). `hero` is the planned near planet.
    void OpenSky(Plan hero)
    {
        bool legacy = !Application.isPlaying && OpeningOverride < 0;
        OpeningWasLegacy = legacy;
        var r = new System.Random(OpeningSeedOverride >= 0 ? OpeningSeedOverride : System.Environment.TickCount ^ GetHashCode());
        double roll = r.NextDouble();
        var kind = OpeningOverride >= 0 ? (OpeningKind)OpeningOverride
            : legacy ? OpeningKind.Giant
            : PickOpening(roll, Has(giant), Has(referencePlanet), Has(neonPlanets), Has(haloPlanets));
        // a forced kind the atlases lack falls back to the giant, then to an empty sky
        if (kind != OpeningKind.None && !Has(OpeningArt(kind))) kind = Has(giant) ? OpeningKind.Giant : OpeningKind.None;
        Opening = kind;
        openingArt = null;
        openingTinted = false;
        if (kind == OpeningKind.None) return;

        float y = HalfH * 0.3f;
        if (!legacy)
        {
            openingArt = OpeningArt(kind);
            float scale = Mathf.Lerp(OpeningScaleMin, OpeningScaleMax, (float)r.NextDouble());
            hero.size *= scale;
            hero.reach *= scale;
            planetSide = r.NextDouble() < 0.5 ? -1 : 1;
            y = HalfH * Mathf.Lerp(OpeningYMin, OpeningYMax, (float)r.NextDouble());
            openingTint = PlanetTints[r.Next(PlanetTints.Length)];
            openingTinted = true;
        }
        Enter(hero, y);
        openingArt = null;
        openingTinted = false;
    }
}
