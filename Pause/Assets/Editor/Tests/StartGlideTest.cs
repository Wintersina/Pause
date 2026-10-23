using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

// The launch glide: a finger already down when GO fires no longer teleports
// the ship onto it; it glides there. Pause-teleport after the glide, a lift
// mid-glide, and a finger landing after GO are unchanged. Drives the real
// movePlayer.Update with injected touch and frame time.
public static class StartGlideTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SG] PASS  " : "[SG] FAIL  ") + what);
        if (!ok) fails++;
    }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    const float Dt = 1f / 60f;

    public static void Run() { TestHarness.Exit(Execute()); }

    static bool pressed;
    static Vector2 screen;
    static movePlayer mp;
    static MethodInfo update, start;
    static Camera cam;
    static readonly System.Collections.Generic.List<GameObject> made = new System.Collections.Generic.List<GameObject>();

    static GameObject Make(string name)
    {
        var g = new GameObject(name);
        made.Add(g);
        return g;
    }

    static void Frame() { update.Invoke(mp, null); }
    static Vector3 Pos { get { return mp.transform.position; } }

    static Vector3 FingerTarget(Vector2 sp)
    {
        return movePlayer.FollowTarget(cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, 0)));
    }

    static Vector3 FingerWorld(Vector2 sp)
    {
        return cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, 0));
    }

    // The jump lands on the finger, then slides back to the perch within
    // ReturnSeconds: distance to the perch only shrinks, no overshoot.
    static void LandsOnFingerThenReturns(string what, Vector2 sp)
    {
        Vector3 land = movePlayer.LandingPoint(FingerWorld(sp));
        Vector3 rest = FingerTarget(sp);
        Check(what + ": lands on the finger", Vector3.Distance(Pos, land) < 1e-4f);
        float prev = Vector3.Distance(Pos, rest);
        bool mono = true;
        int frames = Mathf.CeilToInt(movePlayer.ReturnSeconds / Dt) + 1;
        for (int i = 0; i < frames; i++)
        {
            Frame();
            float d = Vector3.Distance(Pos, rest);
            if (d > prev + 1e-4f) mono = false;
            prev = d;
        }
        Check(what + ": slides back monotonically", mono);
        Check(what + ": ends on the perch", Vector3.Distance(Pos, rest) < 1e-4f);
    }

    static void Build()
    {
        made.Clear();
        var c = Make("StartGlideCam");
        c.tag = "MainCamera";
        cam = c.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.transform.position = new Vector3(0, 0, -10);
        cam.targetTexture = new RenderTexture(540, 960, 16);
        Make("boostText").AddComponent<RectTransform>();
        Make("hypeText").AddComponent<RectTransform>();
        var goText = Make("goText");
        goText.AddComponent<Text>();
        Make("GOSound").AddComponent<AudioSource>();
        var ship = Make("StartGlideShip");
        ship.transform.position = new Vector3(0, -3.5f, 0);
        mp = ship.AddComponent<movePlayer>();
        update = typeof(movePlayer).GetMethod("Update", Inst);
        start = typeof(movePlayer).GetMethod("Start", Inst);
        start.Invoke(mp, null);
    }

    static void Teardown()
    {
        foreach (var g in made) if (g != null) Object.DestroyImmediate(g);
        if (cam != null && cam.targetTexture != null) Object.DestroyImmediate(cam.targetTexture);
        made.Clear();
    }

    // Holds the finger from READY to GO; returns the frame index of GO.
    static int RunToGo(Vector2 sp)
    {
        pressed = true; screen = sp;
        for (int i = 0; i < 200; i++)
        {
            Frame();
            var t = GameObject.Find("goText").GetComponent<Text>();
            if (t.text == movePlayer.GoLabel) return i;
        }
        return -1;
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        TouchInput.PressedOverride = () => pressed;
        TouchInput.PositionOverride = () => screen;
        movePlayer.DeltaOverride = () => Dt;
        score.pauseCounter = 3;
        try
        {
            Glide();
            LateFinger();
            LiftMidGlide();
            PauseTeleportAfterGlide();
            Constants();
        }
        finally
        {
            Teardown();
            TouchInput.PressedOverride = null;
            TouchInput.PositionOverride = null;
            movePlayer.DeltaOverride = null;
        }
        Debug.Log("[SG] failures: " + fails);
        return fails;
    }

    static void Glide()
    {
        Build();
        Vector2 sp = new Vector2(480, 800);
        pressed = true; screen = sp;
        Vector3 start0 = Pos;
        int go = RunToGo(sp);
        Check("GO fires with the finger down", go > 0);
        Vector3 target = FingerTarget(sp);
        Check("the ship was not moved before GO", Vector3.Distance(Pos, start0) < Mathf.Max(0.0001f, Vector3.Distance(Pos, target)) && Vector3.Distance(start0, target) > 3f);
        float maxJump = movePlayer.GlideMaxSpeed * Dt + 1e-3f;
        float prevDist = Vector3.Distance(Pos, target);
        Check("the GO frame itself does not land on the finger", prevDist > 0.5f);
        bool monotonic = true, capped = true, arrived = false;
        Vector3 prev = Pos;
        int frames = 0;
        for (int i = 0; i < 120 && !arrived; i++)
        {
            Frame(); frames++;
            float d = Vector3.Distance(Pos, target);
            if (d > prevDist + 1e-4f) monotonic = false;
            if (Vector3.Distance(Pos, prev) > maxJump) capped = false;
            prevDist = d; prev = Pos;
            arrived = d < 1e-4f;
        }
        Check("distance to the finger only shrinks during the glide", monotonic);
        Check("no frame moves more than the speed cap", capped);
        Check("the glide ends exactly at the finger", arrived);
        Check("the glide is 0.2 .. 0.65 s (" + frames + " frames)", frames >= 12 && frames <= 39);
        for (int i = 0; i < 20; i++) Frame();
        Check("after the glide the ship follows the held finger 1:1", Vector3.Distance(Pos, target) < 1e-4f);

        // finger moves during the glide: glide tracks the live finger and ends on it
        Teardown(); Build();
        sp = new Vector2(100, 700); RunToGo(sp);
        sp = new Vector2(440, 700); screen = sp;
        for (int i = 0; i < 80; i++) Frame();
        Check("a finger that moves during the glide is still reached", Vector3.Distance(Pos, FingerTarget(sp)) < 1e-4f);
        Teardown();
    }

    static void LateFinger()
    {
        // Finger down after GO: lands (first touch counts down READY with the
        // finger, so "after GO" means touch, lift, then land again).
        Build();
        Vector2 sp = new Vector2(480, 800);
        RunToGo(sp);
        for (int i = 0; i < 60; i++) Frame();
        pressed = false;
        Frame(); Frame();
        Vector2 sp2 = new Vector2(60, 300);
        pressed = true; screen = sp2;
        Frame();
        LandsOnFingerThenReturns("a finger landing after the glide", sp2);
        Teardown();
    }

    static void LiftMidGlide()
    {
        Build();
        Vector2 sp = new Vector2(480, 800);
        RunToGo(sp);
        for (int i = 0; i < 3; i++) Frame();
        Vector3 target = FingerTarget(sp);
        Check("mid-glide the ship is between start and finger", Vector3.Distance(Pos, target) > 0.3f);
        pressed = false;
        Frame(); Vector3 held = Pos; Frame(); Frame();
        Check("lifting the finger (pause) freezes the glide in place", Vector3.Distance(Pos, held) < 1e-4f);
        Vector2 sp2 = new Vector2(80, 200);
        pressed = true; screen = sp2;
        Frame();
        LandsOnFingerThenReturns("touching while paused mid-glide", sp2);
        Teardown();
    }

    static void PauseTeleportAfterGlide()
    {
        Build();
        Vector2 sp = new Vector2(480, 800);
        RunToGo(sp);
        for (int i = 0; i < 60; i++) Frame();
        pressed = false; Frame();
        Vector2 sp2 = new Vector2(70, 150);
        pressed = true; screen = sp2; Frame();
        LandsOnFingerThenReturns("pause-teleport after the glide", sp2);
        // a second launch (fresh ship = mid-level resume has playoneshot spent) never glides
        pressed = false; Frame();
        Vector2 sp3 = new Vector2(300, 600); pressed = true; screen = sp3; Frame();
        LandsOnFingerThenReturns("a later pause/resume never glides", sp3);
        Teardown();
    }

    static void Constants()
    {
        bool done;
        Vector3 p = movePlayer.GlideStep(Vector3.zero, new Vector3(0, 9, 0), 0f, Dt, out done);
        Check("a far finger moves at the speed cap on frame one", Mathf.Abs(p.y - movePlayer.GlideMaxSpeed * Dt) < 1e-3f && !done);
        p = movePlayer.GlideStep(Vector3.zero, new Vector3(0, 9, 0), movePlayer.GlideMaxSeconds, Dt, out done);
        Check("the glide has a hard end", done && p == new Vector3(0, 9, 0));
    }
}
