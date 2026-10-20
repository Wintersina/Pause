using UnityEngine;

// The way to the next planet after a boss is down: the portal is open and the ship only has to reach it. The world's
// rocks (enmiesOnBoard's four rock slots) come back through this leg VERY light so that reach is easy: a rock at most
// every MinGap..MaxGap seconds (about a fifth of the ordinary rate), never more than MaxOnBoard in play, and PortalPressure's
// rising spawn rate does not touch them. Enemies, rail mines, elites and the pressure on everything else are unchanged; the
// Space asteroid field and the cover boulders already hold off while a portal waits.
//
// Begins when the boss is over (WorldManager.OnBossOver) and ends the moment the portal is flown, closed or the scene resets
// (PortalPressure.Clear), so the next world starts at its normal density. Stepped by enmiesOnBoard on running frames only.
public static class PostBossLeg
{
    // ---- tunables ----
    // Seconds between two rocks admitted on the leg (ordinary play: a rock every 1 to 3 s from the four slots).
    public const float MinGap = 5f, MaxGap = 8f;
    // Rocks in play at once, counted from the view's top edge down.
    public const int MaxOnBoard = 1;

    public static bool Active { get; private set; }
    static float cooldown;

    public static void Begin()
    {
        Active = true;
        cooldown = MinGap;   // (the first rock waits a breath after the boss)
    }

    public static void End()
    {
        Active = false;
        cooldown = 0f;
    }

    public static void Step(float dt)
    {
        if (Active && cooldown > 0f) cooldown -= dt;
    }

    // Whether a rock slot firing now may spawn, given the rocks already in play; true outside the leg.
    // Admitting one starts the next gap.
    public static bool AdmitRock(int rocksInPlay)
    {
        if (!Active) return true;
        if (cooldown > 0f || rocksInPlay >= MaxOnBoard) return false;
        cooldown = Random.Range(MinGap, MaxGap);
        return true;
    }

    // Rocks in play: the enemy registry's rocks from the view's bottom up to just above its top.
    public static int RocksInPlay()
    {
        var live = SpawnSpace.Live(SpawnLayer.Enemy);
        float top = CameraFit.ViewTop + EnemyDensity.CountAboveView, bottom = CameraFit.ViewBottom;
        int n = 0;
        for (int i = 0; i < live.Count; i++)
        {
            var f = live[i];
            if (f == null) continue;
            float y = f.transform.position.y;
            if (y < bottom || y > top) continue;
            var id = f.GetComponent<EnemyIdentity>();
            if (id != null && id.Def != null && id.Def.role == EnemyRole.Rock) n++;
        }
        return n;
    }
}
