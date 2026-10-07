using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tutorial atoms flow down into the ship's lane and wait there to be caught.
//
// They used to ride the world scroller at the tutorial's near-zero speed and
// spawn above the top edge, so they could sit out of sight for the whole
// step. Each atom step is simulated from spawn with world speed 0 and time
// running: the atom has to reach the bottom 40% of the view within 3 s and
// stay on screen until caught, must not move while the world is frozen, and
// keeps respawning until caught. Real-game atoms keep the world scroller.
public static class TutorialAtomFlowTest
{
    const float Dt = 1f / 60f;
    const float ReachSeconds = 3f;
    const float WaitSeconds = 30f;

    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TAF] PASS  " : "[TAF] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        EditorSceneManager.OpenScene("Assets/Scenes/tutorialS5.unity", OpenSceneMode.Single);
        moveBackGround.speed = 0f;
        buttonClicks.playerDied = false;
        score.pauseCounter = 50;

        var spawner = Object.FindFirstObjectByType<spawnGoodStuffTut>();
        Check("tutorialS5 has the tutorial spawner", spawner != null);
        Check("tutorialS5 has a main camera", Camera.main != null);
        if (spawner == null) return Done();

        var ship = Object.FindFirstObjectByType<movePlayerInTut>();
        Check("tutorialS5 has the tutorial ship", ship != null);
        Vector3 shipStart = ship != null ? ship.transform.position : Vector3.zero;
        Debug.Log("[TAF] ship starts at " + shipStart + ", spawner at " + spawner.transform.position);

        float bottom, top;
        TutorialAtomDrift.View(out bottom, out top);
        Debug.Log("[TAF] view y " + bottom + " .. " + top);
        Check("the old spawn point is above the view (why atoms got stuck)", spawner.transform.position.y > top);

        foreach (var kind in new[] { TutorialAtom.Green, TutorialAtom.Blue, TutorialAtom.Red })
        {
            // The ship in its usual low lane, and up high / at the very bottom.
            foreach (float shipY in new[] { shipStart.y, -3f, -4.15f, 2f })
            {
                if (ship != null) ship.transform.position = new Vector3(0f, shipY, shipStart.z);
                FlowsDownAndWaits(spawner, kind, "ship y " + shipY.ToString("0.##"));
            }
            FreezesWithTheWorld(spawner, kind);
        }
        if (ship != null) ship.transform.position = shipStart;

        DriftsTowardTheShip(spawner, ship);
        WorldSpeedNeverSlowsItBelowTheFloor(spawner);
        KeepsComingUntilCaught(spawner);
        RealGameAtomsUnchanged();

        return Done();
    }

    static int Done()
    {
        Debug.Log("[TAF] failures: " + fails);
        return fails;
    }

    static readonly MethodInfo SpawnAtom =
        typeof(spawnGoodStuffTut).GetMethod("spawnAtom", BindingFlags.Instance | BindingFlags.NonPublic);

    static TutorialAtomDrift Spawn(spawnGoodStuffTut spawner, TutorialAtom kind)
    {
        SpawnAtom.Invoke(spawner, new object[] { kind });
        var live = spawnGoodStuffTut.LiveAtom;
        return live != null ? live.GetComponent<TutorialAtomDrift>() : null;
    }

    static void Destroy(TutorialAtomDrift d)
    {
        if (d != null) Object.DestroyImmediate(d.gameObject);
    }

    static void FlowsDownAndWaits(spawnGoodStuffTut spawner, TutorialAtom kind, string where)
    {
        string name = kind + " atom (" + where + ")";
        var drift = Spawn(spawner, kind);
        Check(name + " is driven by TutorialAtomDrift", drift != null);
        if (drift == null) return;
        var t = drift.transform;
        Check(name + " is the live atom the arrow points at", spawnGoodStuffTut.LiveAtom == t);
        var scroller = t.GetComponent<moveItemEnmInStrightLine>();
        Check(name + " no longer rides the world scroller", scroller == null || !scroller.enabled);

        float bottom, top;
        TutorialAtomDrift.View(out bottom, out top);
        float h = top - bottom;
        float reachLine = bottom + .4f * h;
        float radius = HealAtom.TargetDiameter * .5f;
        Check(name + " spawns just above the top edge (y " + t.position.y.ToString("0.00") + ")",
              t.position.y > top && t.position.y < top + 1f);

        float time = 0f, reachedAt = -1f, enteredAt = -1f;
        bool leftTop = false, leftBottom = false, leftSide = false;
        float minY = float.MaxValue;
        while (time < ReachSeconds + WaitSeconds)
        {
            drift.Step(Dt, true);
            time += Dt;
            var p = t.position;
            if (enteredAt < 0f && p.y + radius < top) enteredAt = time;
            if (reachedAt < 0f && p.y <= reachLine) reachedAt = time;
            if (enteredAt >= 0f && p.y - radius > top) leftTop = true;
            if (p.y - radius < bottom) leftBottom = true;
            if (Mathf.Abs(p.x) > 2.35f) leftSide = true;
            if (reachedAt >= 0f) minY = Mathf.Min(minY, p.y);
        }

        Check(name + " comes into view quickly (" + enteredAt.ToString("0.00") + " s)", enteredAt > 0f && enteredAt <= .5f);
        Check(name + " reaches the bottom 40% within " + ReachSeconds + " s (" + reachedAt.ToString("0.00") + " s)",
              reachedAt > 0f && reachedAt <= ReachSeconds);
        Check(name + " stays in the bottom 40% while it waits", t.position.y <= reachLine);
        Check(name + " never goes back off the top", !leftTop);
        Check(name + " never goes off the bottom (lowest " + minY.ToString("0.00") + ", edge " + bottom + ")", !leftBottom);
        Check(name + " stays inside the lane", !leftSide);
        Check(name + " is still there after " + (ReachSeconds + WaitSeconds) + " s uncaught", t != null && spawnGoodStuffTut.LiveAtom == t);
        Check(name + " has settled into its hover", drift.Arrived);
        Destroy(drift);
    }

    static void FreezesWithTheWorld(spawnGoodStuffTut spawner, TutorialAtom kind)
    {
        var drift = Spawn(spawner, kind);
        if (drift == null) { Check(kind + " atom spawns for the freeze check", false); return; }
        var t = drift.transform;

        for (int i = 0; i < 30; i++) drift.Step(Dt, true);   // mid-descent
        Vector3 before = t.position;
        // timeScale 0: scaled delta time is 0
        for (int i = 0; i < 300; i++) drift.Step(Dt * 0f, true);
        Check(kind + " atom does not move at timeScale 0", t.position == before);
        // finger lifted with pauses left: the world scroller's own gate
        for (int i = 0; i < 300; i++) drift.Step(Dt, false);
        Check(kind + " atom does not move while the world is frozen", t.position == before);
        drift.Step(Dt, true);
        Check(kind + " atom resumes when time resumes", t.position.y < before.y);

        // And once it is hovering, the bob freezes too.
        for (int i = 0; i < 400; i++) drift.Step(Dt, true);
        before = t.position;
        for (int i = 0; i < 120; i++) drift.Step(0f, true);
        for (int i = 0; i < 120; i++) drift.Step(Dt, false);
        Check(kind + " atom's hover bob freezes with the world", t.position == before);
        Destroy(drift);
    }

    static void DriftsTowardTheShip(spawnGoodStuffTut spawner, movePlayerInTut ship)
    {
        if (ship == null) return;
        Vector3 shipWas = ship.transform.position;
        ship.transform.position = new Vector3(2f, -3f, shipWas.z);
        var drift = Spawn(spawner, TutorialAtom.Blue);
        if (drift == null) return;
        drift.transform.position = new Vector3(-1.5f, drift.transform.position.y, drift.transform.position.z);
        for (float time = 0f; time < 3f; time += Dt) drift.Step(Dt, true);
        float xAfterArrival = drift.transform.position.x;
        for (float time = 0f; time < 12f; time += Dt) drift.Step(Dt, true);
        Check("an uncaught atom drifts toward the ship's x (" + xAfterArrival.ToString("0.00") + " -> "
              + drift.transform.position.x.ToString("0.00") + ")",
              drift.transform.position.x > xAfterArrival + 1f && drift.transform.position.x <= 2.2f);
        Destroy(drift);
        ship.transform.position = shipWas;
    }

    static void WorldSpeedNeverSlowsItBelowTheFloor(spawnGoodStuffTut spawner)
    {
        // A faster world (later in a long tutorial) still brings it down; and
        // a negative/odd speed can't push it back up.
        foreach (float speed in new[] { 0f, .05f, .3f, -1f })
        {
            moveBackGround.speed = speed;
            var drift = Spawn(spawner, TutorialAtom.Red);
            if (drift == null) continue;
            float startY = drift.transform.position.y;
            drift.Step(Dt, true);
            float v = (startY - drift.transform.position.y) / Dt;
            Check("descent at world speed " + speed + " is at least the floor (" + v.ToString("0.00") + " u/s)",
                  v >= TutorialAtomDrift.FloorSpeed - .01f);
            Destroy(drift);
        }
        moveBackGround.speed = 0f;
    }

    static void KeepsComingUntilCaught(spawnGoodStuffTut spawner)
    {
        var spawn = typeof(spawnGoodStuffTut).GetMethod("spawn", BindingFlags.Instance | BindingFlags.NonPublic);
        var delay = typeof(spawnGoodStuffTut).GetField("atomDelay", BindingFlags.Static | BindingFlags.NonPublic);
        spawnGoodStuffTut.smStarTimer = 1000f;
        spawnGoodStuffTut.midStarTimer = 1000f;
        spawnGoodStuffTut.keepAtomComing = TutorialAtom.Green;

        delay.SetValue(null, 0f);
        spawn.Invoke(spawner, null);
        var first = spawnGoodStuffTut.LiveAtom;
        Check("an atom step spawns its atom", first != null && first.GetComponent<TutorialAtomDrift>() != null);

        delay.SetValue(null, 0f);
        spawn.Invoke(spawner, null);
        Check("only one at a time while it is still uncaught", spawnGoodStuffTut.LiveAtom == first);

        if (first != null) Object.DestroyImmediate(first.gameObject);   // caught
        delay.SetValue(null, 0f);
        spawn.Invoke(spawner, null);
        var second = spawnGoodStuffTut.LiveAtom;
        Check("a new one drops in after the last is caught", second != null && second != first
              && second.GetComponent<TutorialAtomDrift>() != null);
        if (second != null) Object.DestroyImmediate(second.gameObject);
        spawnGoodStuffTut.keepAtomComing = TutorialAtom.None;
    }

    static void RealGameAtomsUnchanged()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = new GameObject("spawner");
        go.transform.position = new Vector3(0f, 6.5f, 0f);
        var real = go.AddComponent<spawnGoodStuff>();
        real.Atom = Resources.Load<GameObject>("prefabs/atom3a");
        real.redAtom = Resources.Load<GameObject>("prefabs/pauseAtom");
        Check("the real atom prefabs load", real.Atom != null && real.redAtom != null);
        if (real.Atom == null || real.redAtom == null) return;

        foreach (string method in new[] { "spawnAtom", "spawnRedAtom" })
        {
            var m = typeof(spawnGoodStuff).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            m.Invoke(real, null);
        }
        var heal = HealAtom.Spawn(new Vector3(0f, 6.5f, 0f));

        var atoms = Object.FindObjectsByType<AtomSpin>(FindObjectsSortMode.None);
        Check("the real spawner and HealAtom.Spawn made three atoms (" + atoms.Length + ")", atoms.Length == 3);
        foreach (var a in atoms)
        {
            var scroller = a.GetComponent<moveItemEnmInStrightLine>();
            Check("real-game " + a.name + " still rides the world scroller", scroller != null && scroller.enabled);
            Check("real-game " + a.name + " has no tutorial drift", a.GetComponent<TutorialAtomDrift>() == null);
            Check("real-game " + a.name + " spawns where the spawner put it", Mathf.Approximately(a.transform.position.y, 6.5f));
        }
        Object.DestroyImmediate(heal);

        foreach (var file in new[] { "Assets/Scripts/Gameplay/Pickups/spawnGoodStuff.cs", "Assets/Scripts/Gameplay/Pickups/HealAtom.cs",
                                     "Assets/Scripts/Gameplay/moveItemEnmInStrightLine.cs", "Assets/Scripts/Gameplay/Pickups/AtomSpin.cs" })
            Check(Path.GetFileName(file) + " knows nothing of the tutorial drift",
                  !File.ReadAllText(file).Contains("TutorialAtomDrift"));

        string drift = File.ReadAllText("Assets/Scripts/Tutorial/TutorialAtomDrift.cs");
        Check("TutorialAtomDrift moves on scaled time (freezes at timeScale 0)",
              drift.Contains("Step(Time.deltaTime") && !drift.Contains("unscaled"));
    }
}
