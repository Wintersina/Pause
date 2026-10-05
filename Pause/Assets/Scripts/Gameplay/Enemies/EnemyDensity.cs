using UnityEngine;

// How many enemies the spawner fields, as a function of speed: every tunable
// of the 2026-10 density cut in one place (docs/enemy-behaviours.md has the
// measured before / after table; EnemyDensityTest holds it).
//
// Pilots (fighters, heavies, aliens, chasers) stay and fight for seconds
// rather than crossing in one, so they are budgeted by how many are in play
// at once (MaxPilotLoad, MaxChasers; PilotAirspace), and the ceiling below
// counts them for as long as they are there.
//
// Each enemy now moves and attacks on its own, so there are fewer of them:
// about 45% fewer threats over a run, cut less at low speed (the early game
// must not feel empty) and more at high speed (where the board scrolls past
// in about a second).
//
//   RateScale(hud)   multiplies every spawn timer's rate (enmiesOnBoard.Roll)
//   MaxThreats(hud)  a ceiling on what is on the board at once; a spawn that
//                    would pass it is skipped, not queued. A live enemy
//                    projectile counts as EnemyThreat.ShotWeight of a body,
//                    so shooters pay for what they put on screen.
public static class EnemyDensity
{
    // ---- tunables ----
    // Spawn rate at and below LowHud, at and above HighHud (linear between).
    public static float LowHud = 5f, HighHud = 35f;
    public static float RateAtLowSpeed = 1f, RateAtHighSpeed = .55f;
    // Threats (bodies in or just above the view + weighted shots) allowed.
    public static float ThreatsAtLowSpeed = 11f, ThreatsAtHighSpeed = 10f;
    // How far above the top of the view a body already counts.
    public static float CountAboveView = 2.5f;

    // ---- pilots (PilotAirspace): they stay and fight, so they are capped ----
    // The pilot load allowed at once, at low / high speed, per world (Space,
    // Frost, Verdant, Ember). A scout or an alien weighs 0.5, a heavy or a
    // tier-4 fighter 1.5, the rest 1 (PilotAirspace.Weight).
    public static readonly float[] PilotLoadAtLowSpeed = { 3f, 3f, 3.5f, 3.5f };
    public static readonly float[] PilotLoadAtHighSpeed = { 2f, 2f, 2.5f, 2.5f };
    // Chasers hold no column; they have their own cap.
    public static int ChasersAtLowSpeed = 2, ChasersAtHighSpeed = 1;

    // (Speed is capped at HUD 35 -- SpeedRamp.Cap -- and K clamps there, so a
    // limit break's few seconds above it field what 35 fields. Later loops
    // and a portal kept waiting add to the budgets instead: LoopRules,
    // PortalPressure.)
    public static float MaxPilotLoad(float hud, int world)
    {
        world = Mathf.Clamp(world, 0, PilotLoadAtLowSpeed.Length - 1);
        return Mathf.Lerp(PilotLoadAtLowSpeed[world], PilotLoadAtHighSpeed[world], K(hud))
               + LoopRules.PilotLoadBonus(RunLoop.Index) + PortalPressure.PilotLoadBonus;
    }

    public static int MaxChasers(float hud)
    {
        return (K(hud) < .5f ? ChasersAtLowSpeed : ChasersAtHighSpeed) + PortalPressure.ChaserBonus;
    }

    // Tests / the probe: 1 = the old spawner's rate, no ceiling.
    public static bool Disabled;

    public static float Hud => moveBackGround.speed * 100f;

    static float K(float hud) { return Mathf.InverseLerp(LowHud, HighHud, hud); }

    public static float RateScale(float hud)
    {
        return Disabled ? 1f : Mathf.Lerp(RateAtLowSpeed, RateAtHighSpeed, K(hud));
    }

    // The ceilings above are for the authored 10 u view. A taller view shows
    // more board (CameraFit: 13.2 u at 1080x1920, 17.4 u at 1080x2520), so
    // the same density of hazards is more bodies in view: the ceiling grows
    // with the stretch of board it counts, or tall phones would get a
    // sparser board than short ones.
    public const float AuthoredViewHeight = 10f;

    public static float ViewScale
    {
        get
        {
            float h = CameraFit.ViewTop - CameraFit.ViewBottom;
            return Mathf.Max(1f, (h + CountAboveView) / (AuthoredViewHeight + CountAboveView));
        }
    }

    public static float MaxThreats(float hud)
    {
        if (Disabled) return float.MaxValue;
        float authored = Mathf.Lerp(ThreatsAtLowSpeed, ThreatsAtHighSpeed, K(hud)) + LoopRules.ThreatBonus(RunLoop.Index);
        // a portal kept waiting raises it, up to the body cap (PortalPressure)
        return PortalPressure.Ceiling(authored, ViewScale);
    }

    // Bodies in play (in the view or about to enter it) plus weighted shots.
    // A plain loop over SpawnSpace's registry; called per spawn, not per frame.
    public static float Threats()
    {
        float top = CameraFit.ViewTop + CountAboveView, bottom = CameraFit.ViewBottom;
        var live = SpawnSpace.Live(SpawnLayer.Enemy);
        int bodies = 0;
        for (int i = 0; i < live.Count; i++)
        {
            var f = live[i];
            if (f == null) continue;
            float y = f.transform.position.y;
            // (a pilot or chaser counts wherever it is: one waiting above the
            // view or climbing in from below is already on its way)
            var plan = f.Plan;
            if ((y >= bottom && y <= top) || (plan != null && plan.SelfSteering)) bodies++;
        }
        return bodies + HostileShots.ActiveCount * EnemyThreat.ShotWeight;
    }

    public static bool RoomFor(float hud)
    {
        return Disabled || Threats() < MaxThreats(hud);
    }
}
