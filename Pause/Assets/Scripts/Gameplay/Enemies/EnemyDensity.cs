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
    // Regular enemy spawns (and, through the shared Roll, asteroid hazards, rails
    // and mines) come 25% less often at high speed: the rate at HighHud is
    // RateAtHighSpeed * HighSpeedSpawnCut (x0.4125). Low speed is untouched.
    public static float HighSpeedSpawnCut = .75f;
    // ---- run length (a fast start's world is flown in fewer seconds) ----
    // A world is a fixed distance, so a fast start reaches its boss sooner and
    // the time-cadenced spawner fields fewer enemies for the same stretch.
    // The spawn rate is raised by RunLengthFactor to make up part of that:
    // up to x MaxRunBoost when the level is expected to take 120/MaxRunBoost
    // seconds or less, tapering as the speed climbs toward the cap (the board
    // thins out as you get faster, TaperAway of the boost gone at the cap).
    // A stock start (120 s) is x1 throughout; a start at the cap never climbs,
    // so it gets one steady factor, the fully tapered one (the fastest start is the sparsest). A fast start stays sparser per distance
    // than a stock one on purpose (the high-speed cut above is kept).
    public static float MaxRunBoost = 1.6f, TaperAway = .6f;

    // startSeconds: the level's expected length from its start speed;
    // startSpeed / speed: natural speed (0..1 scale) at the start and now.
    public static float RunLengthFactor(float startSeconds, float startSpeed, float speed)
    {
        if (Disabled || startSeconds <= 0f || float.IsNaN(startSeconds) || float.IsInfinity(startSeconds)) return 1f;
        float boost = Mathf.Clamp(WorldManager.BaselineWorldSeconds / startSeconds, 1f, MaxRunBoost);
        float cap = SpeedRamp.Cap;
        float climb = startSpeed >= cap ? 1f : Mathf.Clamp01((speed - startSpeed) / (cap - startSpeed));
        return 1f + (boost - 1f) * (1f - TaperAway * climb);
    }

    // Threats (bodies in or just above the view + weighted shots) allowed.
    public static float ThreatsAtLowSpeed = 11f, ThreatsAtHighSpeed = 10f;
    // How far above the top of the view a body already counts.
    public static float CountAboveView = 2.5f;

    // ---- pilots (PilotAirspace): they stay and fight, so they are capped ----
    // The pilot load allowed at once, at low / high speed, per world (Space,
    // Frost, Verdant, Ember, Tide, Storm, Desert). A scout or an alien weighs 0.5, a heavy or a
    // tier-4 fighter 1.5, the rest 1 (PilotAirspace.Weight).
    // (The fifth entry is Tide's own: Ember's numbers on purpose, so the new cast
    // fights at the difficulty the stand-in did; EnemyDensityTest pins one entry per world.
    // The sixth is Storm's: the gas giant holds the busiest air, 4.5 / 3.5; the seventh is Desert's: 5.0 / 4.0.)
    public static readonly float[] PilotLoadAtLowSpeed = { 3f, 3f, 3.5f, 3.5f, 3.5f, 4.5f, 5f };
    public static readonly float[] PilotLoadAtHighSpeed = { 2f, 2f, 2.5f, 2.5f, 2.5f, 3.5f, 4f };
    // Chasers hold no column; they have their own cap.
    public static int ChasersAtLowSpeed = 2, ChasersAtHighSpeed = 1;

    // (Speed is capped at HUD 35 -- SpeedRamp.Cap -- and K clamps there, so a
    // limit break's few seconds above it field what 35 fields. Later loops
    // and a portal kept waiting add to the budgets instead: LoopRules,
    // PortalPressure.)
    public static float MaxPilotLoad(float hud, int world)
    {
        world = Mathf.Clamp(world, 0, PilotLoadAtLowSpeed.Length - 1);
        // (x LaneMetrics.Density: a wider lane holds proportionally more pilots; 1 on phones)
        return Mathf.Lerp(PilotLoadAtLowSpeed[world], PilotLoadAtHighSpeed[world], K(hud)) * LaneMetrics.Density
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
        return Disabled ? 1f : Mathf.Lerp(RateAtLowSpeed, RateAtHighSpeed * HighSpeedSpawnCut, K(hud));
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
        // (the stretch of board counted: the view's height (ViewScale) x the lane's width (LaneMetrics.Density, 1 on phones))
        return PortalPressure.Ceiling(authored, ViewScale * LaneMetrics.Density);
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
