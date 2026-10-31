using UnityEngine;

// Signature upgrade effects: a visible new trick a ship's attack gains at a set weapon level, ON TOP of
// its numeric track (ShipWeaponUpgrades; the steps, numbers and the four-step structure are unchanged).
// Group 1 of the signatures: Neon Comet, Volt Viper, Crimson Halo, Ion Lancer.
//
// The level is ShipLoadout.weaponLevel (set by ShipWeaponUpgrades.Apply), so the effect follows the
// loadout the runner fires with. Every effect is a state check on that, OFF below its unlock level.
// The step that unlocks an effect says so in its dock / codex label (the *Note strings below, appended
// by ShipWeaponUpgrades.Cd / Note).
//
// Fairness: every hit still goes through ShipAttackHits with the same weights; none of these adds a boss
// or elite hit (a pierce stops at a boss / elite, a stun never touches boss damage, the second beam
// shares the first beam's per-pass hit list). Bosses are never stunned.
public static class ShipSignatures
{
    public const int NeonComet = 1, VoltViper = 2, CrimsonHalo = 4, IonLancer = 5;

    // Appends a signature note to a dock label: "+2 JUMPS" + "STUNS" -> "+2 JUMPS, STUNS".
    public static string Note(string label, string note)
    {
        return string.IsNullOrEmpty(note) ? label : label + ", " + note;
    }

    // ---- Neon Comet: later streaks pierce ----------------------------------------------------
    // Level 2: the extra (third and later) streaks, which splay, pass through one enemy and carry on.
    // Level 4: every streak pierces one enemy, so a firing can take two enemies per streak.
    // A pierce never passes through a boss or an elite (those are IShipAttackTarget: the streak stops).
    public const int CometExtrasPierceLevel = 2;
    public const int CometAllPierceLevel = 4;
    public const int CometPierceCount = 1;          // enemies a piercing streak passes through
    public const string CometExtrasPierceNote = "3+ PIERCE";
    public const string CometAllPierceNote = "ALL PIERCE";

    public static int CometPierce(ShipLoadout l, int streak)
    {
        if (l.shipId != NeonComet) return 0;
        if (l.weaponLevel >= CometAllPierceLevel) return CometPierceCount;
        if (l.weaponLevel >= CometExtrasPierceLevel && streak >= 2) return CometPierceCount;
        return 0;
    }

    // ---- Volt Viper: chain jumps stun -----------------------------------------------------------
    // Each jump of the chain jolts every enemy within JoltRadius of the jump point (not only the ones the
    // chain hits): stunned, dazed sparks over its head, for the seconds below. A surviving elite is stunned
    // too. Level 3: a short stun in a small radius; level 4: longer and wider. Bosses are never stunned.
    public const int ViperStunLevel = 3;
    public const int ViperLongStunLevel = 4;
    public const float ViperStunSeconds = 1f, ViperLongStunSeconds = 1.8f;
    public const float ViperJoltRadius = 1.3f, ViperLongJoltRadius = 1.8f;
    public const string ViperStunNote = "STUNS";
    public const string ViperLongStunNote = "LONGER STUN";

    public static float ViperStun(ShipLoadout l)
    {
        if (l.shipId != VoltViper) return 0f;
        return l.weaponLevel >= ViperLongStunLevel ? ViperLongStunSeconds : l.weaponLevel >= ViperStunLevel ? ViperStunSeconds : 0f;
    }

    public static float ViperRadius(ShipLoadout l)
    {
        return l.weaponLevel >= ViperLongStunLevel ? ViperLongJoltRadius : ViperJoltRadius;
    }

    // Stuns one enemy for `seconds` (the same rules as the Space flares: ordinary enemies freeze, an elite is
    // stunned, bosses / mines / hostile shots never are). True when something was stunned.
    public static bool Stun(GameObject go, float seconds)
    {
        if (go == null || seconds <= 0f || !go.CompareTag("Enimey")) return false;
        if (go.TryGetComponent(out BossTarget _) || go.TryGetComponent(out BossActor _)) return false;
        if (go.TryGetComponent(out EliteShip elite))
        {
            if (!elite.InPlay || !elite.Stun(seconds)) return false;
            DazedSparks.Mark(go.transform, null, elite);
            return true;
        }
        if (RunScore.IsHostileShot(go) || go.TryGetComponent(out RailMineMount _)) return false;
        var frozen = FrozenState.Freeze(go, seconds);
        if (frozen == null) return false;
        DazedSparks.Mark(go.transform, frozen, null);
        return true;
    }

    // ---- Crimson Halo: the rail pierces shields --------------------------------------------------
    // Level 3: the slug is armour piercing: it punches through anything in its line (as it always does) and
    // now also breaks the enemy shot screen -- every hostile shot in the line is deleted -- leaves a
    // lingering white-hot core, and a spark at every target it passes. (No ordinary enemy carries a shield;
    // elite / boss shields and hit blocks are a separate system and are not touched.)
    public const int HaloPierceLevel = 3;
    public const float HaloCoreSeconds = .3f;       // the lingering core line
    public const string HaloPierceNote = "SHIELD PIERCE";

    public static bool HaloPierces(ShipLoadout l) { return l.shipId == CrimsonHalo && l.weaponLevel >= HaloPierceLevel; }

    // ---- Ion Lancer: a second beam ------------------------------------------------------------
    // Level 4: a second, thinner beam sweeps the mirror way (the two scissor through the middle of every
    // pass). The pair share one per-pass hit list, so a target is still hit once per pass, whichever beam
    // reaches it first: more of the screen is swept, a boss takes no extra hit.
    public const int IonCrossLevel = 4;
    public const float IonCrossHalfWidth = .14f;
    public const float IonCrossThickness = .6f;     // drawn thickness relative to the main beam
    public const string IonCrossNote = "CROSS BEAM";

    public static bool IonCross(ShipLoadout l) { return l.shipId == IonLancer && l.weaponLevel >= IonCrossLevel; }
}
