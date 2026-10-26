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
// SYNC / CHAINING. Catching either atom while the other power runs restarts
// BOTH to the same full SyncSeconds, so the two always end together. It is a
// reset, never an addition (no time is banked beyond one full SyncSeconds),
// and it can be re-triggered any number of times: by design a lucky pilot can
// chain shields and magnets and keep the vacuum going indefinitely. Neither
// atom ever creates the other power: a magnet with no shield up is just a
// magnet, a shield with no magnet running just a shield (AfterPickup).
//
// RANGE. Both pulls reach every body inside the pull field (PullRect): the
// visible rect shrunk about its centre to AreaShare (90%) of its area, i.e.
// each side x sqrt(0.9) = 0.949, from the live camera (orthographic size and
// aspect), with each half clamped to sane limits.
//
// CALLOUT. The combo is announced big (CalloutFor): the full COMBO words the
// moment both are up (whichever atom completes it), "CHAIN xN" on every
// re-chain after that; nothing without a HUD, in the tutorial or in the kill
// cam's replay. A magnet or shield on its own shows its normal pickup word.
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

    // ---- range ----
    public const float AreaShare = .9f;                        // the pull field covers this share of the visible rect's area
    public static readonly float SideShare = Mathf.Sqrt(AreaShare);   // ... so each side is this share of the view's (0.949)
    public const float MinHalfWidth = 1.5f, MaxHalfWidth = 8f;     // u, clamps on the field's half extents
    public const float MinHalfHeight = 3f, MaxHalfHeight = 12f;

    // The pull field for a visible rect `view`: centred on it, AreaShare of
    // its area (each side x SideShare), each half clamped. With no camera
    // (an unbounded view) it is the max clamp about the view's centre.
    public static Rect PullRect(Rect view)
    {
        Vector2 c = view.center;
        float hw = Mathf.Clamp(view.width * .5f * SideShare, MinHalfWidth, MaxHalfWidth);
        float hh = Mathf.Clamp(view.height * .5f * SideShare, MinHalfHeight, MaxHalfHeight);
        return new Rect(c.x - hw, c.y - hh, hw * 2f, hh * 2f);
    }

    // The visible rect of an orthographic camera of `orthoSize` and `aspect`
    // (w/h) centred on `centre` (tests, the range report).
    public static Rect ViewRect(Vector2 centre, float orthoSize, float aspect)
    {
        float h = orthoSize, w = orthoSize * aspect;
        return new Rect(centre.x - w, centre.y - h, w * 2f, h * 2f);
    }

    // ---- sync ----
    public const float MagnetSeconds = MagnetPull.PullSeconds;          // 6 s: a magnet on its own
    public const float ShieldSeconds = collisionDetection.ShieldAtomSeconds;   // 5.8 s: a blue-atom shield on its own
    // Both running: the two restart to this together. The longer of the two,
    // so syncing never shortens either power.
    public const float SyncSeconds = MagnetSeconds > ShieldSeconds ? MagnetSeconds : ShieldSeconds;

    public enum Pickup { Magnet, Shield }

    // What is left on each power, in seconds (0 = off).
    public struct Timers
    {
        public float magnet, shield;
        public bool MagnetOn => magnet > 0f;
        public bool ShieldOn => shield > 0f;
        public bool Combo => MagnetOn && ShieldOn;
    }

    // The timers after catching `pickup`: the caught power starts at its own
    // full length, and if the other one is running both restart to
    // SyncSeconds. Never creates the other power.
    public static Timers AfterPickup(Timers t, Pickup pickup)
    {
        if (pickup == Pickup.Magnet)
        {
            t.magnet = MagnetSeconds;
            if (t.ShieldOn) t.magnet = t.shield = SyncSeconds;
        }
        else
        {
            t.shield = ShieldSeconds;
            if (t.MagnetOn) t.magnet = t.shield = SyncSeconds;
        }
        return t;
    }

    // Both clocks run on the same (world) clock.
    public static Timers Tick(Timers t, float dt)
    {
        t.magnet = Mathf.Max(0f, t.magnet - dt);
        t.shield = Mathf.Max(0f, t.shield - dt);
        return t;
    }

    // ---- callout ----
    public enum Callout { None, Combo, Chain }

    // What to announce after a magnet / shield catch. `comboBefore`: both
    // were already up; `comboAfter`: both are up now.
    public static Callout CalloutFor(bool comboBefore, bool comboAfter, bool hud, bool tutorial, bool replay)
    {
        if (!comboAfter || !hud || tutorial || replay) return Callout.None;
        return comboBefore ? Callout.Chain : Callout.Combo;
    }

    // The chain count after a catch that leaves the combo on: 1 for a fresh
    // combo, +1 for every re-chain.
    public static int NextChain(int chain, bool comboBefore) { return comboBefore ? Mathf.Max(1, chain) + 1 : 1; }

    public const string ComboWord = "MAGNET + SHIELD\nVACUUM!";
    public const string ComboWordShort = "MAGNET + SHIELD";   // the one-line hype read-out
    public const int CalloutSize = 40;            // the normal pickup words are 28
    public const float CalloutSeconds = 1.4f;     // they hold .8 s
    public const float CalloutRise = 70f;         // they rise 50
    public const float CalloutLift = 1f;          // u above the ship (the pickup words: collisionDetection.PickupWordLift)
    // shield cyan blended with the magnet's blue pole
    public static readonly Color CalloutColour = Color.Lerp(AkiraPalette.Cyan, AkiraPalette.MagnetBlueHi, .5f);

    // "CHAIN x2" .. built once; past the table, built on the catch.
    static readonly string[] chainWords = BuildChainWords(20);
    static string[] BuildChainWords(int n)
    {
        var w = new string[n + 1];
        for (int i = 2; i <= n; i++) w[i] = "CHAIN x" + i;
        return w;
    }

    public static string WordFor(Callout c, int chain)
    {
        if (c == Callout.Combo) return ComboWord;
        if (c != Callout.Chain) return null;
        int n = Mathf.Max(2, chain);
        return n < chainWords.Length ? chainWords[n] : "CHAIN x" + n;
    }

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
