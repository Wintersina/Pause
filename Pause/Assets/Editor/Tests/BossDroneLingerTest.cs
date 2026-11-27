using UnityEngine;

// The boss's drone pickup was easily missed: it fell at the normal pickup descent. It now falls at
// DroneRewardPickup.LingerDescentScale of it, so it stays on screen (visible and collectable) about twice as long.
public static class BossDroneLingerTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BDL] PASS  " : "[BDL] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;

    // Seconds the pickup is inside the view [bottom, top] before AtomWander drops it, plus its extremes.
    static float VisibleSeconds(float scale, float half, float worldSpeed, int seed, out float maxAbsX)
    {
        var rng = new System.Random(seed);
        var w = AtomWander.Roll(rng); w.descentScale = scale;
        var p = new Vector3(0f, half + .5f, 0f);
        float visible = 0f; maxAbsX = 0f;
        for (float t = 0f; t < 120f; t += Dt)
        {
            p = w.Advance(p, Dt, worldSpeed, half);
            maxAbsX = Mathf.Max(maxAbsX, Mathf.Abs(p.x));
            if (p.y <= half && p.y >= -half) visible += Dt;
            if (AtomWander.Gone(p.y, -half)) break;
        }
        return visible;
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        Check("the linger scale is a half-speed fall", Mathf.Approximately(DroneRewardPickup.LingerDescentScale, .5f));
        foreach (var s in TallScreenTest.Screens)
        {
            float half = CameraFit.ComputeSize(5f, 2.85f, s.w, s.h);
            foreach (float speed in new[] { 0f, .05f, .15f })
            {
                float need = speed <= 0f ? 1.8f : 1.6f;   // a standing world (boss fight) doubles; at speed the capped bob trims it a little
                float oldT = VisibleSeconds(1f, half, speed, 11 + s.h, out _);
                float newT = VisibleSeconds(DroneRewardPickup.LingerDescentScale, half, speed, 11 + s.h, out float maxX);
                Check(s.name + " HUD " + Mathf.RoundToInt(speed * 100f) + ": on screen " + oldT.ToString("F1") + " s -> " + newT.ToString("F1") + " s (>= " + need + "x)",
                      oldT > 0f && newT >= oldT * need);
                Check(s.name + " HUD " + Mathf.RoundToInt(speed * 100f) + ": stays inside the lane", maxX <= AtomWander.LaneHalfWidth + 1e-3f);
            }
        }

        var go = DroneRewardPickup.Spawn(new Vector3(0f, 3f, 0f));
        try
        {
            var mover = go.GetComponent<moveItemEnmInStrightLine>();
            Check("a spawned boss drone carries the lingering wander",
                  mover != null && mover.Wander != null && Mathf.Approximately(mover.Wander.descentScale, DroneRewardPickup.LingerDescentScale));
            float y0 = go.transform.position.y;
            for (int i = 0; i < 600; i++) mover.Step(Dt, 0f, 5f, -5f);
            Check("after 10 s at a standstill world it is still on screen and collectable",
                  go != null && go.activeInHierarchy && go.transform.position.y > -5f && go.transform.position.y < y0 + .5f);
        }
        finally { if (go != null) Object.DestroyImmediate(go); }

        Debug.Log("[BDL] failures: " + fails);
        return fails;
    }
}
