using System;
using UnityEngine;
using static AttackTestKit;

// Dense-combat hot paths (perf/hectic-combat): the pair tests that run every frame over everything on the board must keep their
// results while doing their distance work on plain data.
//
//   * HostileShots.Resolve: overlapping shots of different shooters still pop each other (light vs light: both), shots of one
//     volley and far-apart shots are untouched, and a field of 300 shots is resolved without allocating.
//   * FriendlyFire.StepCrashes: a crowd spread apart never crashes (and nothing is destroyed); a free mover dropped on a rock still does.
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites PerfDenseCombatTest
public static class PerfDenseCombatTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DENSE] PASS  " : "[DENSE] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    sealed class FakeShot : MonoBehaviour, IHostileShot
    {
        public Vector2 pos;
        public int owner = 1;
        public bool live = true;
        public bool ShotCollidable => live;
        public Vector2 ShotPosition => pos;
        public float ShotRadius => .1f;
        public int ShotOwner => owner;
        public float ShotAge => owner * 10f;   // never the same volley as another owner
        public int ShotMass => HostileShots.Light;
        public Color ShotTint => Color.white;
        public void ShotPop(Vector2 at) { live = false; }
        public bool ShotShovable => live;
        public void ShotShove(Vector2 by) { pos += by; }
    }

    static FakeShot Shot(float x, float y, int owner)
    {
        var s = new GameObject("fakeshot").AddComponent<FakeShot>();
        s.pos = new Vector2(x, y);
        s.owner = owner;
        HostileShots.Register(s);
        return s;
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            ShotPairs();
            Crashes();
        }
        finally
        {
            Cleanup();
            HostileShots.ResetCounters();
        }
        Debug.Log("[DENSE] " + (fails == 0 ? "ALL PASS" : fails + " FAILED"));
        return fails;
    }

    static void ShotPairs()
    {
        Fresh();
        var a = Shot(0f, 0f, 1);
        var b = Shot(.05f, 0f, 2);
        var far = Shot(3f, 3f, 3);
        var sameVolley = Shot(0f, .05f, 1);   // a's own volley: never collides with it
        HostileShots.Resolve();
        Check("two overlapping shots of different shooters pop each other", !a.live && !b.live);
        Check("a shot far from everything is untouched", far.live);
        Check("a shot of the same volley as a popped one is not popped by it", sameVolley.live);

        Fresh();
        var field = new FakeShot[300];
        for (int i = 0; i < field.Length; i++) field[i] = Shot((i % 20) * 1.5f - 14f, (i / 20) * 1.5f - 11f, 1 + i % 5);
        HostileShots.Resolve();   // warm
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 10; k++) HostileShots.Resolve();
        long grown = GC.GetAllocatedBytesForCurrentThread() - before;
        int alive = 0;
        for (int i = 0; i < field.Length; i++) if (field[i].live) alive++;
        Check("a field of 300 well-spaced shots loses none to Resolve", alive == field.Length);
        Check("resolving 300 shots allocates nothing (" + grown + " bytes)", grown == 0);
    }

    static GameObject Body(EnemyRole role, Vector2 at)
    {
        var def = EnemyRoster.One(3, role);
        for (int w = 0; def == null && w < 6; w++) def = EnemyRoster.One(w, role);
        var go = EnemyFactory.Create(def, at, Quaternion.identity);
        ClearTarget.Ensure(go);
        var brain = go.GetComponent<EnemyBrain>();
        if (brain != null) brain.enabled = false;
        FriendlyFire.Settle(go);
        return go;
    }

    static bool Gone(GameObject go) => go == null || !go.TryGetComponent(out ClearTarget t) || !t.enabled;

    static void Crashes()
    {
        Fresh();
        HazardRuntime.Ensure().ClearAll();
        var crowd = new GameObject[40];
        for (int i = 0; i < crowd.Length; i++)
            crowd[i] = Body(i % 3 == 0 ? EnemyRole.Chaser : EnemyRole.Rock, new Vector2((i % 8) * 4f - 14f, (i / 8) * 4f - 8f));
        bool crashed = FriendlyFire.StepCrashes(ShipTargets.View());
        bool any = false;
        for (int i = 0; i < crowd.Length; i++) any |= Gone(crowd[i]);
        Check("a crowd spread apart never crashes and loses nothing", !crashed && !any);

        var chaser = Body(EnemyRole.Chaser, new Vector2(30f, 30f));
        var rock = Body(EnemyRole.Rock, new Vector2(30.1f, 30f));
        crashed = FriendlyFire.StepCrashes(new Rect(-100f, -100f, 200f, 200f));
        Check("a free mover on top of a rock still crashes in the crowd, both destroyed", crashed && Gone(chaser) && Gone(rock));
    }
}
