using UnityEngine;

// SIGNATURE UPGRADE EFFECTS of the UFO, Dove, Turtle and Paragon: a visible extra that unlocks with the
// weapon level, on top of the numeric tracks of ShipWeaponUpgrades (which are unchanged). The level is
// ShipLoadout.weaponLevel (non-stock skins owned, 0..4).
//
// Every effect has a MILD version (MildLevel, 2: the cooldown step's colour) and a FULL version (FullLevel,
// 4). Below MildLevel nothing changes. The tables below are indexed by weapon level 0..4, so a row reads
// "what this ship does at each level"; the ShipWeaponUpgrades dock labels (the *Label constants) name the
// effect on the step that unlocks it.
//
// Fairness: every hit still goes through ShipAttackHits and bosses / elites never take an extra hit from a
// signature (UFO's blast skips them; the Dove's returns and the Turtle's bounces stay inside the boss's own
// hit window; Paragon's two warheads split one boss hit between them).
public static class ShipSignaturesG3
{
    public const int MildLevel = 2, FullLevel = 4;

    // Dock labels (appended to the cooldown steps' own text).
    public const string UfoBlastMildLabel = "IMPACT BLAST";
    public const string UfoBlastFullLabel = "BIGGER BLAST";
    public const string DoveReturnMildLabel = "HARDER RETURNS";
    public const string DoveReturnFullLabel = "TWIN RETURNS";
    public const string DiscMildLabel = "+2 BOUNCES, SPARKS";
    public const string DiscFullLabel = "+4 BOUNCES, TRAIL";
    public const string NukeMildLabel = "BIGGER FIREBALL";
    public const string NukeFullLabel = "2ND WARHEAD";

    static int At(int level) { return Mathf.Clamp(level, 0, ShipWeaponUpgrades.MaxLevel); }

    // ---- UFO (Abduct & Hurl): the hurled body explodes where it ends, taking ordinary enemies nearby with it.
    // Radius in world units; 0 = no blast. Bosses and elites are never touched by it.
    static readonly float[] ufoBlast = { 0f, 0f, 1.0f, 1.0f, 1.7f };
    public static float UfoBlastRadius(int level) { return ufoBlast[At(level)]; }

    // ---- Dove (Gale Wings): the shots the gust turns hit harder and are drawn bigger, and at the top level
    // every turned shot comes back as a pair. Weight = share of a hit an ordinary body takes from one return.
    static readonly float[] doveReturnWeight = { .5f, .5f, .75f, .75f, .75f };
    static readonly float[] doveReturnScale = { .6f, .6f, .85f, .85f, .85f };
    public const float DoveTwinSpread = 16f;   // degrees either side of the first return
    public static float DoveReturnWeight(int level) { return doveReturnWeight[At(level)]; }
    public static float DoveReturnScale(int level) { return doveReturnScale[At(level)]; }
    public static bool DoveTwins(int level) { return level >= FullLevel; }

    // ---- Turtle (Shell Disc): extra ricochets on top of the loadout's maxHits (8): 10 at level 2, 14 at
    // level 4. Sparks bigger from level 2, and a trail of sparks behind the disc at level 4.
    static readonly int[] discBounces = { 0, 0, 2, 2, 6 };
    static readonly float[] discSparkScale = { .7f, .7f, 1.1f, 1.1f, 1.1f };
    public const float DiscTrailEvery = .09f, DiscTrailScale = .3f;
    public static int DiscExtraBounces(int level) { return discBounces[At(level)]; }
    public static float DiscSparkScale(int level) { return discSparkScale[At(level)]; }
    public static bool DiscTrail(int level) { return level >= FullLevel; }

    // ---- Paragon (Nuke): the shockwave already covers the screen, so the signature is the blast itself: a
    // bigger core flash and mushroom from level 2, and a second warhead on another spot at level 4. The
    // warheads share one hit on a boss (weight each), so the total stays as it was.
    static readonly float[] nukeBlastScale = { 1f, 1f, 1.5f, 1.5f, 1.5f };
    static readonly int[] nukeWarheads = { 1, 1, 1, 1, 2 };
    public const float NukeSecondDelay = .5f, NukeSecondOffset = 2.4f;
    public static float NukeBlastScale(int level) { return nukeBlastScale[At(level)]; }
    public static int NukeWarheads(int level) { return nukeWarheads[At(level)]; }
    public static float NukeWarheadWeight(int level) { return 1f / NukeWarheads(level); }
}
