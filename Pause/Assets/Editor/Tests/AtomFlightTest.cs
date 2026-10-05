using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Bug: "the atoms that come down sometimes fly so high that the ship cannot
// get them". Atoms rode moveItemEnmInStrightLine's local-space Translate while
// AtomSpin turned them, so they flew circles of radius v/0.5585 and, below
// ~3.4 u/s (HUD ~11), climbed straight back off the top of the screen.
//
// Simulates many seeded atoms through AtomWander at every supported screen
// height, at world speeds HUD 0/5/15/30/max (and with the speed jumping
// around mid-flight: boss drain to 0, loop arrival at max), through slow-mo,
// pause and hitch frames:
//   - once on screen an atom never rises above the ceiling (under the view's
//     top and inside the ship's reach), and never climbs while above it;
//   - every atom crosses the ship's lowest line and leaves the bottom within
//     a bounded time;
//   - it still visibly wanders (sideways sway, vertical bob);
// then runs the real blue/red/green atoms through their scroller, and checks
// star dust still falls straight.
public static class AtomFlightTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[AF] PASS  " : "[AF] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const float Dt = 1f / 60f;
    // Worst case: 9:24 view (half-height 7.6), spawn 0.5 over the top, gone
    // 1 under the bottom = 16.7 units at MinDescent 1.2 = 13.9 s, + the wander.
    const float MaxSeconds = 20f;
    // movePlayer's floor in a view (ShipReach; it was the constant -4.15)
    static float ShipLowestY(float bottom, float top) =>
        ShipReach.BottomFor(new PlayField.Frame { bottom = bottom, top = top, safeBottom = bottom, safeTop = top, bandBottom = top });
    const int AtomsPerCase = 120;

    static readonly float[] Speeds = { 0f, .05f, .15f, .30f, SpeedRamp.Cap + SpeedRamp.MaxBoost };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        Tuning();
        foreach (var s in TallScreenTest.Screens)
        {
            float half = CameraFit.ComputeSize(5f, 2.85f, s.w, s.h);
            foreach (float speed in Speeds)
                Simulate(s.name, half, speed, false, 1000 + (int)(speed * 100f) + s.h);
            Simulate(s.name, half, -1f, true, 7 + s.h);   // speed changing mid-flight
        }
        OldPathClimbedOffTheTop();
        RealAtoms();
        StarDust();

        Debug.Log("[AF] failures: " + fails);
        return fails;
    }

    static void Tuning()
    {
        // (the reach's top was the constant 4.5; it is a share of the view now)
        Check("the ceiling is inside the ship's reach at every screen height",
              AtomWander.Ceiling(5f) <= AtomWander.ShipTopFor(5f) + .5f && AtomWander.Ceiling(7.6f) <= AtomWander.ShipTopFor(7.6f) + .5f &&
              AtomWander.Ceiling(9.1f) <= AtomWander.ShipTopFor(9.1f) + .5f);
        Check("the ceiling is under the top of the default view (" + AtomWander.Ceiling(5f) + " < 5)",
              AtomWander.Ceiling(5f) < 5f);
        Check("atoms always drift down, even when the world stands still",
              AtomWander.Descent(0f) >= 1f && AtomWander.Descent(.3f) >= 9f - .001f);
    }

    // One batch of atoms. speed < 0: the speed jumps around mid-flight.
    static void Simulate(string screen, float half, float speed, bool varying, int seed)
    {
        var rng = new System.Random(seed);
        float top = half, bottom = -half;
        float ceiling = AtomWander.Ceiling(top);
        string who = screen + (varying ? " varying speed" : " HUD " + Mathf.RoundToInt(speed * 100f));

        int aboveCeiling = 0, climbedEntering = 0, outOfLane = 0, stuck = 0, missedShipLine = 0;
        float worstSeconds = 0f, lateralSum = 0f;
        int bobbed = 0;

        for (int a = 0; a < AtomsPerCase; a++)
        {
            var w = AtomWander.Roll(rng);
            // spawnGoodStuff spawns 0.5 over the top; HealAtomSpawner at y 7
            float spawnY = a % 3 == 2 ? Mathf.Max(7f, top + .1f) : top + .5f;
            var p = new Vector3(Mix(rng, -2.2f, 2.2f), spawnY, 0f);
            bool entered = false, crossedShip = false, gone = false;
            float t = 0f, minX = p.x, maxX = p.x;
            float v = varying ? Speeds[rng.Next(Speeds.Length)] : speed;
            float pausedFor = 0f;

            while (t < MaxSeconds + 5f)
            {
                // pause: the world (and the scroller) holds still
                if (pausedFor > 0f) { pausedFor -= Dt; continue; }
                if (rng.NextDouble() < .004) { pausedFor = Mix(rng, .2f, 2f); continue; }

                if (varying && rng.NextDouble() < .01) v = Speeds[rng.Next(Speeds.Length)];

                // slow-mo (ResumeSlowMo / hit-stop) and the odd hitch frame
                float scale = rng.NextDouble() < .2 ? Mix(rng, .05f, 1f) : 1f;
                float dt = rng.NextDouble() < .01 ? .25f : Dt * scale;

                float before = p.y;
                p = w.Advance(p, dt, v, top);
                t += dt;

                if (!entered && p.y <= ceiling) entered = true;
                if (entered && p.y > ceiling + 1e-4f) aboveCeiling++;
                if (!entered && p.y > before + 1e-5f) climbedEntering++;
                if (Mathf.Abs(p.x) > AtomWander.LaneHalfWidth + 1e-4f) outOfLane++;
                if (entered && p.y > before + 1e-5f) bobbed++;
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                if (p.y < ShipLowestY(bottom, top)) crossedShip = true;
                if (AtomWander.Gone(p.y, bottom)) { gone = true; break; }
            }
            if (!gone) stuck++;
            if (!crossedShip) missedShipLine++;
            worstSeconds = Mathf.Max(worstSeconds, t);
            lateralSum += maxX - minX;
        }

        float lateral = lateralSum / AtomsPerCase;
        Check(who + ": never above the ceiling " + ceiling.ToString("F2") + " once on screen (" + aboveCeiling + " frames)",
              aboveCeiling == 0);
        Check(who + ": never climbs while still above the ceiling (" + climbedEntering + ")", climbedEntering == 0);
        Check(who + ": stays in the lane (" + outOfLane + ")", outOfLane == 0);
        Check(who + ": every atom passes the ship's lowest line (" + missedShipLine + " missed)", missedShipLine == 0);
        Check(who + ": every atom leaves the bottom, worst " + worstSeconds.ToString("F1") + " s <= " + MaxSeconds,
              stuck == 0 && worstSeconds <= MaxSeconds);
        float minLateral = speed >= .3f || varying ? .05f : .4f;
        Check(who + ": still wanders sideways (mean sway " + lateral.ToString("F2") + " >= " + minLateral + ")",
              lateral >= minLateral);
        if (!varying && speed <= .05f)
            Check(who + ": still bobs up now and then (" + bobbed + " frames)", bobbed > 0);
    }

    static float Mix(System.Random rng, float a, float b)
    {
        return Mathf.Lerp(a, b, (float)rng.NextDouble());
    }

    // The root cause, kept as a record: the old local-space translate on a
    // 32 deg/s spin flies a circle and comes back to the spawn height.
    static void OldPathClimbedOffTheTop()
    {
        var go = new GameObject("oldPath");
        try
        {
            float v = .05f * 30f;                // HUD 5
            float maxY = float.MinValue, minY = float.MaxValue;
            go.transform.position = new Vector3(0f, 5.5f, 0f);
            for (float t = 0f; t < 11.25f; t += Dt)
            {
                go.transform.Rotate(0f, 0f, 32f * Dt);
                go.transform.Translate(new Vector2(0, -1) * v * Dt);
                if (t > 6f) maxY = Mathf.Max(maxY, go.transform.position.y);
                minY = Mathf.Min(minY, go.transform.position.y);
            }
            Check("root cause reproduced: a spinning atom on the old local-space scroller climbs back to " +
                  maxY.ToString("F2") + " (dipped only to " + minY.ToString("F2") + ")", maxY > 5f && minY > -5f);
        }
        finally { Object.DestroyImmediate(go); }
    }

    // The real atoms, as the game spawns them, through their own scroller.
    static void RealAtoms()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var blue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/prefabs/atom3a.prefab");
        var red = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/prefabs/pauseAtom.prefab");
        Check("atom prefabs load", blue != null && red != null);
        if (blue == null || red == null) return;

        var rng = new System.Random(42);
        foreach (float speed in Speeds)
        {
            var atoms = new List<GameObject>
            {
                AtomSpin.AddTo(Object.Instantiate(blue, new Vector3(1f, 5.5f, 0f), Quaternion.identity)),
                AtomSpin.AddTo(Object.Instantiate(red, new Vector3(-1f, 5.5f, 0f), Quaternion.identity)),
                HealAtom.Spawn(new Vector3(0f, 7f, 0f)),
            };
            foreach (var go in atoms)
            {
                string name = go.name + " HUD " + Mathf.RoundToInt(speed * 100f);
                var spin = go.GetComponent<AtomSpin>();
                var scroller = go.GetComponent<moveItemEnmInStrightLine>();
                Check(name + ": spins and rides the world scroller", spin != null && scroller != null && scroller.enabled);
                if (scroller == null) { Object.DestroyImmediate(go); continue; }
                scroller.SetWander(AtomWander.Roll(rng));

                float t = 0f, maxY = float.MinValue;
                bool entered = false, destroyed = false;
                float ceiling = AtomWander.Ceiling(5f);
                while (t < MaxSeconds)
                {
                    // the spin turns the transform exactly as it does in game
                    go.transform.Rotate(0f, 0f, spin.degreesPerSecond * Dt);
                    if (!scroller.Step(Dt, speed, 5f, -5f)) { destroyed = true; break; }
                    t += Dt;
                    float y = go.transform.position.y;
                    if (y <= ceiling) entered = true;
                    if (entered) maxY = Mathf.Max(maxY, y);
                }
                Check(name + ": never back above the ceiling once on screen (max " + maxY.ToString("F2") + ")",
                      maxY <= ceiling + 1e-4f);
                Check(name + ": falls out the bottom and is removed in " + t.ToString("F1") + " s", destroyed && go == null);
                if (go != null) Object.DestroyImmediate(go);
            }
        }
    }

    // Star dust rides the same scroller without AtomSpin: it must keep
    // falling straight, at the world's own speed, and never turn.
    static void StarDust()
    {
        foreach (var path in new[] { "Assets/Resources/prefabs/smStar_1.prefab", "Assets/Resources/prefabs/LargeStar_1.prefab" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Check(System.IO.Path.GetFileName(path) + " loads", prefab != null);
            if (prefab == null) continue;
            Check(prefab.name + " has nothing that turns it",
                  prefab.GetComponentInChildren<AtomSpin>(true) == null &&
                  prefab.GetComponentInChildren<AsteroidSpin>(true) == null &&
                  prefab.GetComponentInChildren<roate>(true) == null &&
                  prefab.GetComponentInChildren<rotateRight>(true) == null);
            var go = Object.Instantiate(prefab, new Vector3(0f, 5.5f, 0f), Quaternion.identity);
            try
            {
                var scroller = go.GetComponent<moveItemEnmInStrightLine>();
                Check(prefab.name + " rides the world scroller", scroller != null);
                if (scroller == null) continue;
                bool down = true;
                for (int i = 0; i < 120; i++)
                {
                    float before = go.transform.position.y;
                    scroller.Step(Dt, .15f, 5f, -5f);
                    down &= go.transform.position.y < before;
                }
                Check(prefab.name + " falls straight at world speed (no wander)",
                      down && scroller.Wander == null &&
                      Mathf.Abs(go.transform.position.y - (5.5f - .15f * 30f * 2f)) < .01f);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
