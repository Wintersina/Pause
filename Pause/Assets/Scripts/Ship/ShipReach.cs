using UnityEngine;

// Where the player's ship may fly: the one place its reach is decided.
//
// HOW TOUCH MAPS TO THE SHIP (movePlayer): absolute, not relative. Every
// frame a finger is down the ship's centre is put FingerOffset above the
// finger (so the thumb never covers the hull), clamped to this range; the
// first touch after a lift is a teleport to that spot. Lifting the finger
// pauses the game.
//
// The range used to be the constant y -4.15 .. 4.5, written for the
// authored 10 u view (camera half-height 5). The camera is width-fitted
// (CameraFit), so on phones it shows 13.2 u (16:9) to 18.2 u (22:9), and the
// constant left the bottom 19% - 26% and the top 16% - 25% of those screens
// out of reach. It is now a share of the view the device actually shows:
//
//   BOTTOM  the ship fully drawn (hull and exhaust: HullBelow under its
//           centre) BottomMargin above the safe area's bottom -- the home
//           indicator / gesture bar / 3-button bar -- on every screen. (A
//           finger can't usually go lower than the screen's edge anyway:
//           that puts the ship FingerOffset up, which is higher on phones
//           with no bottom inset.)
//   TOP     the ship's top edge (its centre + HullAbove) sits TopGap under
//           the HUD top band's lowest edge (PlayField.Frame.bandBottom: the
//           score read-out, the home / replay icons, already below any
//           cutout / status bar) -- right under the score board, on every
//           screen shape. It used to stop TopShare (70%) of the way up the
//           view, which left the top 17% - 21% of a phone's play area (the
//           band and the strip under it) unreachable. The ship may overlap
//           the HUD's pieces never; it draws under them (HUD canvases sort
//           above the world) and taps on them still win (movePlayer).
//           While a boss is up, an UNSHIELDED ship stays under the boss
//           (BossCeilingFor: 65% of the view or lower, under its muzzles);
//           a shielded one (blue atom / Cloak) may fly all the way up and
//           ram it (BossRam), and is eased back down as the shield ends
//           (BossOpenFallSpeed).
//   SIDES   +/-HalfWidth: the hull's side (ShipScale.HullHalfWidth) just
//           reaches the rails' drawn inner edge (BossRails.InnerEdge), never
//           over it. The rails move out where the screen has room
//           (RailInset), and the reach with them.
//
// The range follows the live camera and safe area every frame (a foldable
// folding, a rotation): it is a pure function of them, so it changes
// exactly when they do and allocates nothing.
public static class ShipReach
{
    // ---- tunables ----
    // false: the old constant reach (LegacyBottom .. LegacyTop) on every screen.
    public static bool FitToView = true;
    public const float LegacyBottom = -4.15f, LegacyTop = 4.5f;
    // The ship's top edge stops this far under the HUD band's bottom (world u).
    public static float TopGap = .03f;
    // The share of the view atoms may float up to (AtomWander): they stay
    // clear of the HUD band though the ship now reaches it.
    public static float PickupTopShare = .7f;
    // Clear space between the ship's lowest drawn pixel and the safe area's bottom (world u).
    public static float BottomMargin = .15f;
    // The ship's drawing below / above its centre at the normalised size: the
    // hulls are 0.58 u tall at most; the longest nozzle flame (drawn at its
    // full boost size) ends 0.81 u under the centre. ShipReachTest measures
    // every hull. Live: x ShipScale (1.35 in the main game).
    public const float AuthoredHullBelow = .82f;
    public const float AuthoredHullAbove = .3f;
    public static float HullBelow => AuthoredHullBelow * ShipScale.Live;
    public static float HullAbove => AuthoredHullAbove * ShipScale.Live;
    // The ship's centre sits this far above the finger (HeartOrbit.ThumbBelow).
    public const float FingerOffset = 1f;
    // Sideways: the ship's centre stays within +/- this. It was the constant
    // 2.4 (LegacyHalfWidth), which let a 0.58 u hull poke 0.08 u into the
    // drawn rails (inner edge 2.61); now the hull's side stops on the rails'
    // inner edge: 2.21 (16:9) .. 2.45 (19.5:9 and taller, tablets) at the
    // main game's 1.35x ship, 2.32 .. 2.56 at the normalised size.
    public const float LegacyHalfWidth = 2.4f;
    public static float HalfWidthFor(float railInnerEdge, float hullHalfWidth)
    {
        return Mathf.Max(MinHalfWidth, railInnerEdge - hullHalfWidth - RailClearance);
    }
    public static float HalfWidth => HalfWidthFor(BossRails.DrawnInnerEdge, ShipScale.HullHalfWidth);
    // Clear space between the hull's side and the rail at the clamp (world u).
    public const float RailClearance = 0f;
    // Never squeezed to less than this (no rails / a nonsense measurement).
    public const float MinHalfWidth = 1.5f;
    // Never squeezed to less than this (a landscape / very short window).
    public const float MinSpan = 2f;
    // Where a run's ship starts: this share up the view (the authored y -2 of a 10 u view).
    public static float StartShare = .3f;

    // The reach in a given view (world y; pure, for tests and tools).
    public static float BottomFor(PlayField.Frame f)
    {
        if (!FitToView) return LegacyBottom;
        return Mathf.Max(f.bottom, f.safeBottom) + HullBelow + BottomMargin;
    }

    public static float TopFor(PlayField.Frame f)
    {
        if (!FitToView) return LegacyTop;
        // the hull's top edge right under the HUD band (bandBottom = the safe
        // area's top where no band is laid out)
        float top = f.bandBottom - HullAbove - TopGap;
        return Mathf.Max(top, BottomFor(f) + MinSpan);
    }

    // The highest an atom floats (the old reach: PickupTopShare of the view).
    public static float PickupTopFor(PlayField.Frame f)
    {
        return Mathf.Max(f.At(PickupTopShare), BottomFor(f) + MinSpan);
    }

    public static float ClampY(PlayField.Frame f, float y)
    {
        return Mathf.Clamp(y, BottomFor(f), TopFor(f));
    }

    public static float StartYFor(PlayField.Frame f)
    {
        return Mathf.Clamp(f.At(StartShare), BottomFor(f), TopFor(f));
    }

    // While a boss is up the ship's ceiling is BossConfig.ShipCeilingFor:
    // FightCeilingShare of the view or lower, under the boss's lowest muzzle
    // with a clear gap. The ceiling follows the boss as it warps in (and as
    // it retreats) -- the resting ceiling moved by the boss's distance from
    // its rest -- so a ship parked high is eased down by the arriving boss,
    // not snapped. +infinity with no boss.
    public static float BossCeilingFor(PlayField.Frame f)
    {
        if (!FitToView) return float.PositiveInfinity;
        var enc = BossEncounter.Instance;
        var actor = enc != null ? enc.Actor : null;
        if (actor == null) return float.PositiveInfinity;
        float rest = BossConfig.ShipCeilingFor(f, actor.Boss);
        switch (actor.State)
        {
            case BossActor.Mode.Fighting:
            case BossActor.Mode.Dying:
                return rest;
            case BossActor.Mode.Arriving:
            case BossActor.Mode.Retreating:
                return rest + (actor.transform.position.y - BossConfig.RestYFor(f));
            default:
                return float.PositiveInfinity;
        }
    }

    // ---- live (the main camera, the device's safe area, the boss) ----
    public static float Bottom => BottomFor(PlayField.Live);
    public static float Top
    {
        get
        {
            var f = PlayField.Live;
            if (!FitToView) return LegacyTop;
            float zone = TopFor(f);
            return Mathf.Max(BottomFor(f) + MinSpan, Mathf.Min(zone, BossCeilingFor(f) + OpenExtra(f, zone)));
        }
    }

    // ---- a shielded ship rams the boss (BossRam) ----
    // The boss ceiling is lifted while a blue-atom shield or Cloak has more
    // than BossOpenLead seconds left, and comes back at BossOpenFallSpeed
    // (u/s) once it has less, so a ship rammed up into the boss is out of
    // its body before the shield is gone.
    public const float BossOpenLead = 1f;
    public const float BossOpenFallSpeed = 8f;

    // Does the ship's shield hold the boss ceiling open right now?
    public static bool ShieldOpensCeiling =>
        (collisionDetection.atomCheck && collisionDetection.invTimer > BossOpenLead) ||
        collisionDetection.cloakTimer > BossOpenLead;

    static float openExtra;
    static int openFrame = -1;

    // How far above the boss ceiling the ship may fly (0 .. zone - ceiling).
    static float OpenExtra(PlayField.Frame f, float zone)
    {
        float ceiling = BossCeilingFor(f);
        if (float.IsInfinity(ceiling)) { openExtra = 0f; return 0f; }
        float full = Mathf.Max(0f, zone - ceiling);
        if (!Application.isPlaying) return ShieldOpensCeiling ? full : 0f;
        if (openFrame != Time.frameCount)
        {
            openFrame = Time.frameCount;
            openExtra = ShieldOpensCeiling ? full : Mathf.Max(0f, openExtra - BossOpenFallSpeed * Time.deltaTime);
        }
        return Mathf.Min(openExtra, full);
    }

    public static void ResetOpen() { openExtra = 0f; openFrame = -1; }

    public static float ClampY(float y) => Mathf.Clamp(y, Bottom, Top);
    public static float ClampX(float x) => Mathf.Clamp(x, -HalfWidth, HalfWidth);
    public static float StartY => StartYFor(PlayField.Live);

    // A pilot coming into the view (a Swoop's dip) keeps its body this far
    // above the top of the ship's reach, so it never drops onto a ship
    // parked there (EnemyBrain): the hull's top plus a clear gap.
    public const float EntryClearance = .25f;
    public static float EntryFloor => TopFor(PlayField.Live) + HullAbove + EntryClearance;
}
