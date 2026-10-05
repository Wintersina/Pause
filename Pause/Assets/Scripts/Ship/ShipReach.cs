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
//   TOP     TopShare of the way up the view (its centre), measured from the
//           view's bottom, the same share on every phone. Why that share:
//           docs/enemy-behaviours.md "Ship reach and boss height". In short:
//           pilots own the top third of the view (their stations are 14% -
//           30% below its top, a Swoop dips 9% further), hazards appear at
//           its top edge, and the HUD band and the boss sit above that; the
//           ship stops under all of it with about half a second to react at
//           HUD 35. While a boss is up it also stays under the boss
//           (BossCeilingFor).
//   SIDES   +/-HalfWidth (unchanged: the lane and rails are fixed in x).
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
    // Highest the ship's centre goes: this share of the view's height, from its bottom.
    public static float TopShare = .6f;
    // Clear space between the ship's lowest drawn pixel and the safe area's bottom (world u).
    public static float BottomMargin = .15f;
    // The ship's drawing below / above its centre: the hulls are 0.58 u tall
    // at most; the longest nozzle flame (drawn at its full boost size) ends
    // 0.81 u under the centre. ShipReachTest measures every hull.
    public const float HullBelow = .82f;
    public const float HullAbove = .3f;
    // The ship's centre sits this far above the finger (HeartOrbit.ThumbBelow).
    public const float FingerOffset = 1f;
    // Sideways: the ship's centre stays within +/- this (unchanged).
    public const float HalfWidth = 2.4f;
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
        float top = f.At(TopShare);
        // never into the HUD band (only ever binds on a squat screen)
        top = Mathf.Min(top, f.bandBottom - HullAbove - BottomMargin);
        return Mathf.Max(top, BottomFor(f) + MinSpan);
    }

    public static float ClampY(PlayField.Frame f, float y)
    {
        return Mathf.Clamp(y, BottomFor(f), TopFor(f));
    }

    public static float StartYFor(PlayField.Frame f)
    {
        return Mathf.Clamp(f.At(StartShare), BottomFor(f), TopFor(f));
    }

    // While a boss is up the ship also stays BossConfig.ShipCeilingBelowFor
    // under it: under its lowest muzzle with a clear gap, never inside its
    // art. The ceiling follows the boss as it warps in (and as it retreats),
    // so a ship parked high is eased down by the arriving boss, not snapped.
    // +infinity with no boss.
    public static float BossCeilingFor(PlayField.Frame f)
    {
        if (!FitToView) return float.PositiveInfinity;
        var enc = BossEncounter.Instance;
        var actor = enc != null ? enc.Actor : null;
        if (actor == null) return float.PositiveInfinity;
        float y;
        switch (actor.State)
        {
            case BossActor.Mode.Fighting:
            case BossActor.Mode.Dying:
                y = BossConfig.RestYFor(f);
                break;
            case BossActor.Mode.Arriving:
            case BossActor.Mode.Retreating:
                y = actor.transform.position.y;
                break;
            default:
                return float.PositiveInfinity;
        }
        return y - BossConfig.ShipCeilingBelowFor(f);
    }

    // ---- live (the main camera, the device's safe area, the boss) ----
    public static float Bottom => BottomFor(PlayField.Live);
    public static float Top
    {
        get
        {
            var f = PlayField.Live;
            if (!FitToView) return LegacyTop;
            return Mathf.Max(BottomFor(f) + MinSpan, Mathf.Min(TopFor(f), BossCeilingFor(f)));
        }
    }
    public static float ClampY(float y) => Mathf.Clamp(y, Bottom, Top);
    public static float ClampX(float x) => Mathf.Clamp(x, -HalfWidth, HalfWidth);
    public static float StartY => StartYFor(PlayField.Live);

    // A pilot coming into the view (a Swoop's dip) keeps its body this far
    // above the top of the ship's reach, so it never drops onto a ship
    // parked there (EnemyBrain): the hull's top plus a clear gap.
    public const float EntryClearance = .25f;
    public static float EntryFloor => TopFor(PlayField.Live) + HullAbove + EntryClearance;
}
