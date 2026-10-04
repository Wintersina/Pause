using UnityEngine;

// Hull colours make the ship's main attack stronger.
//
// A ship's weapon level is how many of its non-stock skins (ShipSkins) the
// player owns: 0 with only the stock colours, MaxLevel with every colourway
// bought. Which skin is equipped doesn't matter. Developer mode shows every
// skin as owned, so it flies every ship at MaxLevel.
//
// Each level adds the next step of the ship's own upgrade track below,
// cumulatively: more projectiles / hops / bounces, a bigger or longer
// effect, or a shorter cooldown (a cut of the charge time, additive). Every
// track alternates "more" and "faster" so each colour bought is felt.
//
// Fairness: every hit still goes through ShipAttackHits with the firing's
// boss budget, and multi-shot weapons split one full hit between their
// shots, so a boss never takes more than one hit per firing at any level --
// a faster cooldown is the only thing that speeds up a boss fight. Shot
// counts stay well under AttackPool.MaxProjectiles.
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

    static WeaponStep Cd(float cut)
    {
        return new WeaponStep { cooldownCut = cut, label = "-" + Mathf.RoundToInt(cut * 100f) + "% COOLDOWN" };
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

    static readonly WeaponStep[] TopTier = { Cd(.10f), Cd(.10f), Cd(.10f), Cd(.10f) };

    // By ShipId; MaxLevel steps each.
    static readonly WeaponStep[][] tracks =
    {
        null,
        /*  1 Neon Comet   */ new[] { Up("+1 STREAK", shots: 1), Cd(.15f), Up("+1 STREAK", shots: 1), Cd(.20f) },
        /*  2 Volt Viper   */ new[] { Up("+2 JUMPS", maxHits: 2), Cd(.15f), Up("+2 JUMPS", maxHits: 2, width: .5f), Cd(.20f) },
        /*  3 Solar Fang   */ new[] { Up("BIGGER BLAST", width: .35f), Cd(.15f), Up("+1 FIREBALL", shots: 1), Cd(.20f) },
        /*  4 Crimson Halo */ new[] { Cd(.15f), Up("WIDER RAIL", width: .14f), Cd(.20f), Up("+2 SIDE RAILS", shots: 2) },
        /*  5 Ion Lancer   */ TopTier,
        /*  6 Jade Phantom */ TopTier,
        /*  7 Gold Warden  */ TopTier,
        /*  8 Lightning    */ new[] { Up("+2 JAVELINS", shots: 2), Cd(.15f), Up("WIDER JAVELINS", width: .12f), Cd(.20f) },
        /*  9 Ligher       */ new[] { Up("LONGER BURN", duration: .5f), Cd(.15f), Up("BIGGER FLAME", range: .6f, width: 6f), Cd(.20f) },
        /* 10 Paranoid     */ new[] { Up("+1 EYE", shots: 1), Cd(.15f), Up("+1 EYE", shots: 1), Cd(.20f) },
        /* 11 Ninja        */ new[] { Up("+2 BOUNCES", maxHits: 2), Cd(.15f), Up("+2 BOUNCES", maxHits: 2, range: .5f), Cd(.20f) },
        /* 12 Saboteur     */ new[] { Up("+6 SHELLS", shots: 6), Cd(.15f), Up("+6 SHELLS", shots: 6, duration: .2f), Cd(.20f) },
        /* 13 UFO          */ new[] { Up("LONGER BEAM", duration: .6f), Cd(.15f), Up("WIDER BEAM", width: .14f), Cd(.20f) },
        /* 14 Dove         */ new[] { Up("+2 DARTS", shots: 2, width: 8f), Cd(.15f), Up("+2 DARTS", shots: 2, width: 8f), Cd(.20f) },
        /* 15 Turtle       */ new[] { Up("LONGER ORBIT", duration: 1.5f), Cd(.15f), Up("BIGGER DISC", range: .14f, speed: 60f), Cd(.20f) },
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
