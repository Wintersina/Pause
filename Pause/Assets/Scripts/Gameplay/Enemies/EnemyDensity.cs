using UnityEngine;

// How many enemies the spawner fields, as a function of speed: every tunable
// of the 2026-10 density cut in one place (docs/enemy-behaviours.md has the
// measured before / after table; EnemyDensityTest holds it).
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
    public static float RateAtLowSpeed = .90f, RateAtHighSpeed = .55f;
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

    public static float MaxPilotLoad(float hud, int world)
    {
        world = Mathf.Clamp(world, 0, PilotLoadAtLowSpeed.Length - 1);
        return Mathf.Lerp(PilotLoadAtLowSpeed[world], PilotLoadAtHighSpeed[world], K(hud));
    }

    public static int MaxChasers(float hud)
    {
        return K(hud) < .5f ? ChasersAtLowSpeed : ChasersAtHighSpeed;
    }

    // Tests / the probe: 1 = the old spawner's rate, no ceiling.
    public static bool Disabled;

    public static float Hud => moveBackGround.speed * 100f;

    static float K(float hud) { return Mathf.InverseLerp(LowHud, HighHud, hud); }

    public static float RateScale(float hud)
    {
        return Disabled ? 1f : Mathf.Lerp(RateAtLowSpeed, RateAtHighSpeed, K(hud));
    }

    public static float MaxThreats(float hud)
    {
        return Disabled ? float.MaxValue : Mathf.Lerp(ThreatsAtLowSpeed, ThreatsAtHighSpeed, K(hud));
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
            if (y >= bottom && y <= top) bodies++;
        }
        return bodies + HostileShots.ActiveCount * EnemyThreat.ShotWeight;
    }

    public static bool RoomFor(float hud)
    {
        return Disabled || Threats() < MaxThreats(hud);
    }
}
