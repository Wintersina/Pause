using UnityEngine;

// The magnet + shield combo, as pure rules (MagnetPull applies them; the
// tests pin them).
//
//   MAGNET ALONE   every goodie on screen (star dust AND the other atoms) is
//                  hauled into the hull. No enemy, rock, mine or shot moves.
//   SHIELD ALONE   nothing is pulled (the shield is unchanged).
//   BOTH           the goodies as above, AND every plain enemy on screen is
//                  sucked in toward the hull; the shield destroys what
//                  touches it (collisionDetection's ordinary shield kill, so
//                  the score, dust, combo, achievements and death effects are
//                  the usual ones).
//
// "Both" is decided LIVE, every frame, from the two states as they are right
// now -- magnet pull running, blue-atom shield up -- never latched at a
// pickup. So the order the two were caught in (magnet then shield, shield
// then magnet, either one re-caught while the other runs) cannot change the
// result, and the moment either one ends the enemy pull ends with it.
//
// Never pulled, combo or not: bosses (and their body hitboxes), elites (the
// mini-bosses keep their own rules: a shielded ram still costs them hearts,
// EliteShip.ShieldRam), anything an elite owns and flies (a Tender's drones,
// a hauler's boulder: ClearTarget.Mother), hostile shots and beams, rail
// mines, rocks / asteroids (tag Astr), backdrop scenery. Nothing in the
// tutorial is pulled in.
public static class MagnetCombo
{
    public enum Kind
    {
        Dust,          // star dust (smStar1 / LargeStar1)
        Atom,          // any other pickup: the atoms
        Enemy,         // a plain enemy (tag Enimey, none of the below)
        Rock,          // tag Astr: rocks, asteroids
        Boss,          // a boss body or any other special hit target (IShipAttackTarget)
        Elite,         // an elite (EliteShip)
        EliteOwned,    // flown by an elite: drones, thrown boulders (ClearTarget.Mother)
        HostileShot,   // enemy / elite / boss projectiles and their hitboxes
        RailMine,      // a mine on its rail (RailMineMount)
        Scenery,       // backdrop bodies that only take hits (ClearTarget.Scenery)
        Other,         // anything else in the registry
    }

    // ---- the enemy pull's tunables ----
    // Speeds are in view heights per second, so a pulled enemy crosses the
    // same share of the screen in the same time on every phone (the camera
    // shows 13.2 u at 1080x1920, 17.4 u at 1080x2520).
    public const float EnemySpeedStartPerView = .3f;   // the first moment: a visible slide, not a snatch
    public const float EnemySpeedEndPerView = 1.1f;    // fully ramped: top of the screen to the hull in about a second
    public const float EnemyRampSeconds = 1f;          // from start to end speed, counted from when the combo starts
    public const float MinViewHeight = 8f, MaxViewHeight = 20f;   // the view height used is clamped to this (no camera: Max)

    // Is the enemy pull on right now? Order-free: only the two live states count.
    public static bool EnemyPullActive(bool magnetRunning, bool shieldUp, bool tutorial)
    {
        return magnetRunning && shieldUp && !tutorial;
    }

    // Does the magnet move a body of this kind right now?
    public static bool Pulls(Kind kind, bool magnetRunning, bool shieldUp, bool tutorial)
    {
        if (!magnetRunning) return false;
        switch (kind)
        {
            case Kind.Dust:
            case Kind.Atom:
                return true;
            case Kind.Enemy:
                return EnemyPullActive(true, shieldUp, tutorial);
            default:
                return false;
        }
    }

    // Classifies a body from what it is (pure; MagnetPull reads the flags).
    public static Kind Classify(bool scenery, bool pickup, bool starDust, bool rockTag, bool enemyTag,
                                bool boss, bool elite, bool eliteOwned, bool hostileShot, bool railMine)
    {
        if (scenery) return Kind.Scenery;
        if (pickup) return starDust ? Kind.Dust : Kind.Atom;
        if (rockTag) return Kind.Rock;
        if (!enemyTag) return Kind.Other;
        if (elite) return Kind.Elite;
        if (boss) return Kind.Boss;
        if (eliteOwned) return Kind.EliteOwned;
        if (hostileShot) return Kind.HostileShot;
        if (railMine) return Kind.RailMine;
        return Kind.Enemy;
    }

    // How fast a pulled enemy moves (u/s), `comboSeconds` after the combo
    // came on, with a view `viewHeight` units tall.
    public static float EnemySpeed(float comboSeconds, float viewHeight)
    {
        float h = Mathf.Clamp(viewHeight, MinViewHeight, MaxViewHeight);
        float k = Mathf.Clamp01(comboSeconds / EnemyRampSeconds);
        return Mathf.Lerp(EnemySpeedStartPerView, EnemySpeedEndPerView, k * k) * h;
    }
}
