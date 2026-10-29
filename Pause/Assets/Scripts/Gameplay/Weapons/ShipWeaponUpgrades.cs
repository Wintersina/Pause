using UnityEngine;

// Hull colours make the ship's main attack stronger.
//
// A ship's weapon level is how many of its non-stock skins (ShipSkins) the
// player owns: 0 with only the stock colours, MaxLevel with every colourway
// bought. Which skin is equipped doesn't matter. Developer mode shows every
// skin as owned, so it flies every ship at MaxLevel.
//
// Each level adds the next step of the ship's own upgrade track below,
// cumulatively: more projectiles / marks / mines / hops, a bigger or longer
// effect, or a shorter cooldown (a cut of the charge time, additive). Every
// track alternates "more" and "faster" so each colour bought is felt.
//
// Fairness: every hit goes through ShipAttackHits, and multi-shot weapons
// split one full hit between their shots, so a firing is worth about one
// hit on a boss at any level (held weapons -- flame, beam, disc -- add a
// tick's worth for every extra tick, and the boss's own hit window,
// BossConfig.HitInvulnerableSeconds, paces the rest). Shot counts stay well
// under AttackPool.MaxProjectiles.
//
// The one place for the numbers: change a track here and the runner
// (ShipAttackRunner), the charge timer (ShipPowerController) and the dock's
// weapon row (DockPopup) all follow.
public struct WeaponStep
{
    public int shots, maxHits;
    public float duration, range, width, speed;
    public float cooldownCut;   // fraction of the charge time removed (additive)
    public string label;        // dock text, e.g. "+1 STREAK", "-15% COOLDOWN"
}

public static class ShipWeaponUpgrades
{
    public const int MaxLevel = ShipSkins.PerShip - 1;

    // `note`: the signature effect (ShipSignatures) this step also unlocks, appended to the label.
    static WeaponStep Cd(float cut, string note = null)
    {
        return new WeaponStep { cooldownCut = cut, label = ShipSignatures.Note("-" + Mathf.RoundToInt(cut * 100f) + "% COOLDOWN", note) };
    }

    static WeaponStep Up(string label, int shots = 0, int maxHits = 0, float duration = 0f,
                         float range = 0f, float width = 0f, float speed = 0f)
    {
        return new WeaponStep
        {
            label = label, shots = shots, maxHits = maxHits, duration = duration,
            range = range, width = width, speed = speed,
        };
    }

    // Ligher's blowtorch burns 5 s at level 0 (ShipLoadoutTable); each of its two
    // flame steps adds this many seconds: 6.25 s, then 7.5 s at MaxLevel.
    public const float BlowtorchStepSeconds = 1.25f;

    // By ShipId; MaxLevel steps each.
    static readonly WeaponStep[][] tracks =
    {
        null,
        /*  1 Neon Comet   */ new[] { Up("+1 STREAK", shots: 1), Cd(.15f, ShipSignatures.CometExtrasPierceNote), Up("+1 STREAK", shots: 1), Cd(.20f, ShipSignatures.CometAllPierceNote) },
        /*  2 Volt Viper   */ new[] { Up("+2 JUMPS", maxHits: 2), Cd(.15f), Up(ShipSignatures.Note("+2 JUMPS", ShipSignatures.ViperStunNote), maxHits: 2, width: .5f), Cd(.20f, ShipSignatures.ViperLongStunNote) },
        /*  3 Solar Fang   */ new[] { Up("THICKER BAND", width: .3f), Cd(.15f), Up("+1 WAVE", shots: 1), Cd(.20f) },
        /*  4 Crimson Halo */ new[] { Cd(.15f), Up("WIDER RAIL", width: .14f), Cd(.20f, ShipSignatures.HaloPierceNote), Up("+2 SIDE RAILS", shots: 2) },
        /*  5 Ion Lancer   */ new[] { Up("LONGER SWEEP", duration: .3f), Cd(.15f), Up("+1 PASS", shots: 1), Cd(.20f, ShipSignatures.IonCrossNote) },
        /*  6 Jade Phantom */ new[] { Up("SHATTER SPLASH", width: .8f), Cd(.15f), Up("BIGGER SPLASH", width: .4f), Cd(.20f) },
        /*  7 Gold Warden  */ new[] { Up("+1 SENTRY", shots: 1), Cd(.15f), Up("LONGER SENTRIES", duration: .9f), Cd(.20f) },
        /*  8 Lightning    */ new[] { Up("+2 JAVELINS", shots: 2), Cd(.15f), Up("WIDER JAVELINS", width: .12f), Cd(.20f) },
        /*  9 Ligher       */ new[] { Up("LONGER BURN", duration: BlowtorchStepSeconds), Cd(.15f), Up("BIGGER, LONGER FLAME", duration: BlowtorchStepSeconds, range: .6f, width: 6f), Cd(.20f) },
        /* 10 Paranoid     */ new[] { Up("+1 MARK", shots: 1), Cd(.15f), Up("+1 MARK", shots: 1), Cd(.20f) },
        /* 11 Ninja        */ new[] { Up("+1 STAR", shots: 1), Cd(.15f), Up("LONGER THROW", range: 1f), Cd(.20f) },
        /* 12 Saboteur     */ new[] { Up("+1 MINE", shots: 1), Cd(.15f), Up("+1 MINE", shots: 1), Cd(.20f) },
        /* 13 UFO          */ new[] { Up("+1 PIERCE", maxHits: 1), Cd(.15f), Up("+1 GRAB", shots: 1), Cd(.20f) },
        /* 14 Dove         */ new[] { Up("WIDER GUST", width: 12f), Cd(.15f), Up("LONGER GUST", range: 1.5f), Cd(.20f) },
        /* 15 Turtle       */ new[] { Up("LONGER THROW", duration: 1.5f), Cd(.15f), Up("BIGGER DISC", range: .14f, speed: 1.5f), Cd(.20f) },
        // 16 Paragon: four skins like every ship. Its tiers' mini-warheads, shield, hearts and speed are PremiumShips.
        /* 16 Paragon      */ new[] { Up("FASTER WARHEAD", speed: 6f), Cd(.15f), Up("FASTER WARHEAD", speed: 8f), Cd(.20f) },
    };

    public static bool Has(int shipId) { return shipId > 0 && shipId < tracks.Length && tracks[shipId] != null; }

    // The step that level `level` (1..MaxLevel) adds.
    public static WeaponStep Step(int shipId, int level)
    {
        if (!Has(shipId)) shipId = ShipId.Starter;
        return tracks[shipId][Mathf.Clamp(level, 1, MaxLevel) - 1];
    }

    // ---------------------------------------------------------- level

    // The weapon level the ship flies at: non-stock skins owned (developer
    // mode owns them all).
    public static int Level(int shipId)
    {
        if (ShipId.TutorialFlight) return 0;   // the tutorial's ship has no upgrades
        int n = 0;
        for (int skin = 1; skin < ShipSkins.CountFor(shipId); skin++)
            if (ShipSkins.IsOwned(shipId, skin)) n++;
        return Mathf.Min(n, MaxLevel);
    }

    // ---------------------------------------------------------- effect

    // The ship's attack at `level`: the base row of ShipLoadoutTable plus
    // every step up to that level.
    public static ShipLoadout Apply(ShipLoadout l, int level)
    {
        int id = Has(l.shipId) ? l.shipId : ShipId.Starter;
        level = Mathf.Clamp(level, 0, MaxLevel);
        l.weaponLevel = level;
        for (int i = 1; i <= level; i++)
        {
            var s = tracks[id][i - 1];
            l.shots += s.shots;
            l.maxHits += s.maxHits;
            l.duration += s.duration;
            l.range += s.range;
            l.width += s.width;
            l.speed += s.speed;
        }
        return l;
    }

    public static ShipLoadout LoadoutAt(int shipId, int level) { return Apply(ShipLoadoutTable.For(shipId), level); }

    // What the ship fires right now.
    public static ShipLoadout LoadoutFor(int shipId) { return LoadoutAt(shipId, Level(shipId)); }

    // Multiplier on the charge time (1 at level 0).
    public static float CooldownScaleAt(int shipId, int level)
    {
        if (!Has(shipId)) shipId = ShipId.Starter;
        level = Mathf.Clamp(level, 0, MaxLevel);
        float cut = 0f;
        for (int i = 1; i <= level; i++) cut += tracks[shipId][i - 1].cooldownCut;
        return Mathf.Clamp(1f - cut, .4f, 1f);
    }

    public static float CooldownScale(int shipId) { return CooldownScaleAt(shipId, Level(shipId)); }

    // ---------------------------------------------------------- dock text

    // What buying one more colour adds; null at MaxLevel.
    public static string NextLabel(int shipId)
    {
        int level = Level(shipId);
        return level >= MaxLevel ? null : Step(shipId, level + 1).label;
    }
}
