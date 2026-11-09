using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

// The launch glide: the first touch glides the ship to the finger (never a
// teleport), and the ship follows the finger through READY and GO alike -- a
// finger already moving before GO used to leave the ship stuck until GO.
// Pause-teleport after GO, a lift mid-glide, and a finger landing after GO
// are unchanged. Drives the real movePlayer.Update with injected touch and
// frame time.
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
        goLabel = goText.AddComponent<Text>();
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
            if (IsGo()) return i;
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
            CooldownFollow();
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

    // goText is deactivated once GO has faded, so GameObject.Find can't reach
    // it then: read the Text built for this ship instead.
    static Text goLabel;
    static bool IsGo() { return goLabel != null && goLabel.text == movePlayer.GoLabel; }

    static void Glide()
    {
        Build();
        Vector2 sp = new Vector2(480, 800);
        Vector3 target = FingerTarget(sp);
        Vector3 start0 = Pos;
        Check("the finger starts far from the ship", Vector3.Distance(start0, target) > 3f);
        pressed = true; screen = sp;
        float maxJump = movePlayer.GlideMaxSpeed * Dt + 1e-3f;
        float prevDist = Vector3.Distance(Pos, target);
        bool monotonic = true, capped = true, arrived = false, readyAll = true;
        Vector3 prev = Pos;
        int frames = 0;
        for (int i = 0; i < 120 && !arrived; i++)
        {
            Frame(); frames++;
            if (IsGo()) readyAll = false;
            float d = Vector3.Distance(Pos, target);
            if (d > prevDist + 1e-4f) monotonic = false;
            if (Vector3.Distance(Pos, prev) > maxJump) capped = false;
            prevDist = d; prev = Pos;
            arrived = d < 1e-4f;
        }
        Check("the first touch moves the ship during READY (no freeze until GO)", Vector3.Distance(Pos, start0) > 3f);
        Check("the glide reaches the finger while READY is still up", arrived && readyAll);
        Check("distance to the finger only shrinks during the glide", monotonic);
        Check("no frame moves more than the speed cap (no snap)", capped);
        Check("the glide is 0.2 .. 0.65 s (" + frames + " frames)", frames >= 12 && frames <= 39);
        int go = RunToGo(sp);
        Check("GO still fires with the finger down", go >= 0);
        for (int i = 0; i < 20; i++) Frame();
        Check("after GO the ship follows the held finger 1:1", Vector3.Distance(Pos, target) < 1e-4f);
        Teardown();

        // The bug: a finger already moving before GO. The ship must follow it
        // every frame of READY, through the GO frame and after, never stuck.
        Build();
        pressed = true;
        bool followedAll = true, sawGo = false, stuckFrame = false, smooth = true;
        float maxFingerStep = 0f;
        Vector3 last = Pos;
        for (int i = 0; i < 150; i++)
        {
            float x = 100f + 340f * Mathf.PingPong(i * 6f, 340f) / 340f;
            Vector2 prevSp = screen;
            screen = new Vector2(x, 400f + i);
            if (i > 0) maxFingerStep = Mathf.Max(maxFingerStep, Vector3.Distance(FingerTarget(prevSp), FingerTarget(screen)));
            Frame();
            if (IsGo()) sawGo = true;
            // once the launch glide is over (<= GlideMaxSeconds) the ship sits on the finger
            if (i * Dt > movePlayer.GlideMaxSeconds + Dt && Vector3.Distance(Pos, FingerTarget(screen)) > 1e-4f) followedAll = false;
            if (i * Dt > movePlayer.GlideMaxSeconds + Dt && i > 0 && Vector3.Distance(Pos, last) < 1e-6f) stuckFrame = true;
            if (Vector3.Distance(Pos, last) > Mathf.Max(maxJump, maxFingerStep) + 1e-3f) smooth = false;
            last = Pos;
        }
        Check("a moving finger before GO: GO still fires", sawGo);
        Check("a moving finger before GO is followed every frame, before and after GO", followedAll);
        Check("... the ship is never stuck in place while the finger moves", !stuckFrame);
        Check("... and never snaps (no step bigger than the glide cap or the finger's own step)", smooth);
        Teardown();

        // Held through the run's entry: the hand-back left the ship under the
        // finger. READY must not hold it there while the finger moves on.
        Build();
        sp = new Vector2(200, 600);
        mp.transform.position = FingerTarget(sp);
        pressed = true; screen = sp;
        Frame();
        Check("a hand-back under the finger is not pulled away (no jump)", Vector3.Distance(Pos, FingerTarget(sp)) < 0.06f);
        for (int i = 0; i < 10; i++) Frame();
        Vector2 sp2 = new Vector2(380, 640); screen = sp2;
        for (int i = 0; i < 3; i++) Frame();
        Check("still READY", !IsGo());
        Check("during READY the ship follows the moving finger (" + Vector3.Distance(Pos, FingerTarget(sp2)) + ")", Vector3.Distance(Pos, FingerTarget(sp2)) < 1e-4f);
        Teardown();

        // finger moves during the glide: glide tracks the live finger and ends on it
        Build();
        pressed = true; screen = new Vector2(100, 700); Frame(); Frame();
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
        pressed = true; screen = sp;
        for (int i = 0; i < 3; i++) Frame();
        Vector3 target = FingerTarget(sp);
        Check("mid-glide (READY) the ship is between start and finger", Vector3.Distance(Pos, target) > 0.3f);
        pressed = false;
        Frame(); Vector3 held = Pos; Frame(); Frame();
        Check("lifting the finger (pause) freezes the glide in place", Vector3.Distance(Pos, held) < 1e-4f);
        Check("READY is still up after the lift", !IsGo());
        Vector2 sp2 = new Vector2(80, 200);
        pressed = true; screen = sp2;
        Vector3 before = Pos;
        Frame();
        Check("a press again before GO glides, it does not blink onto the finger",
              Vector3.Distance(Pos, before) <= movePlayer.GlideMaxSpeed * Dt + 1e-3f &&
              Vector3.Distance(Pos, movePlayer.LandingPoint(FingerWorld(sp2))) > 0.3f);
        for (int i = 0; i < 60; i++) Frame();
        Check("... and ends on the finger", Vector3.Distance(Pos, FingerTarget(sp2)) < 1e-4f);

        // after GO, a lift mid-glide then a press is the ordinary pause-teleport
        Teardown(); Build();
        RunToGo(sp);
        pressed = false; Frame(); Frame();
        pressed = true; screen = sp2;
        Frame();
        LandsOnFingerThenReturns("touching while paused after GO", sp2);
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

    // Out of pauses: a press inside the tap cooldown never blinks, but a held
    // finger glides the ship in (capped speed, no jump) and it can still hit
    // things on the way. With pauses left nothing about this applies.
    static void CooldownFollow()
    {
        int saved = score.pauseCounter;
        score.pauseCounter = 0;
        Build();
        RunToGo(new Vector2(270, 480));
        for (int i = 0; i < 60; i++) Frame();
        // first blink is allowed (cooldown not yet started), and starts the cooldown
        pressed = false; Frame();
        Vector2 a = new Vector2(100, 300); pressed = true; screen = a; Frame();
        Check("out of pauses: the first blink still lands on the finger", Vector3.Distance(Pos, movePlayer.LandingPoint(FingerWorld(a))) < 1e-4f);
        for (int i = 0; i < 30; i++) Frame();

        // spam taps inside the cooldown: no blink, ever
        bool blinked = false;
        for (int n = 0; n < 6; n++)
        {
            pressed = false; Frame();
            Vector3 before = Pos;
            Vector2 sp = new Vector2(100 + 60 * n, 800);
            pressed = true; screen = sp; Frame();
            if (Vector3.Distance(Pos, before) > movePlayer.GlideMaxSpeed * Dt + 1e-3f) blinked = true;
        }
        Check("spam taps inside the cooldown never teleport", !blinked);

        // a held finger inside the cooldown: flies down to it, smoothly
        pressed = false; Frame();
        Vector2 held = new Vector2(430, 120);
        Vector3 target = FingerTarget(held);
        Vector3 start = Pos;
        pressed = true; screen = held;
        float cap = movePlayer.GlideMaxSpeed * Dt + 1e-3f;
        bool shrinks = true, capped = true;
        float prevD = Vector3.Distance(Pos, target);
        Vector3 prev = Pos;
        int frames = 0;
        for (int i = 0; i < 90; i++)
        {
            Frame(); frames++;
            float d = Vector3.Distance(Pos, target);
            if (d > prevD + 1e-4f) shrinks = false;
            if (Vector3.Distance(Pos, prev) > cap) capped = false;
            prevD = d; prev = Pos;
        }
        Check("held finger in the cooldown: the ship moved toward it", Vector3.Distance(start, target) > 3f && Vector3.Distance(Pos, start) > 3f);
        Check("... distance only shrinks", shrinks);
        Check("... no frame jumps past the speed cap (" + frames + ")", capped);
        Check("... and it ends on the finger", Vector3.Distance(Pos, target) < 1e-4f);

        // collisions still apply along the way
        pressed = false; Frame();
        var hz = Make("CooldownHazard");
        var box = hz.AddComponent<BoxCollider2D>();
        box.size = new Vector2(2f, 0.6f);
        Vector2 far = new Vector2(430, 800);
        Vector3 farT = FingerTarget(far);
        Vector3 mid = Vector3.Lerp(Pos, farT, 0.5f);
        hz.transform.position = mid;
        Physics2D.SyncTransforms();
        pressed = true; screen = far;
        bool hit = false;
        Vector3 last = Pos;
        for (int i = 0; i < 60; i++)
        {
            Frame();
            // The glide moves up to GlideMaxSpeed*Dt (1 unit) per frame, more
            // than the hazard is tall, so a point test at frame ends can step
            // over it. Test the whole path travelled this frame (the physics
            // sweep does the same for the real ship collider).
            for (int k = 0; k <= 40; k++)
                if (box.OverlapPoint(Vector3.Lerp(last, Pos, k / 40f))) hit = true;
            last = Pos;
        }
        Check("the ship still touches a hazard in its path during the cooldown fly", hit);
        Teardown();

        // pauses left: the blink is unchanged (never denied, lands on the finger)
        score.pauseCounter = 3;
        Build();
        RunToGo(new Vector2(270, 480));
        for (int i = 0; i < 60; i++) Frame();
        for (int n = 0; n < 3; n++)
        {
            pressed = false; Frame();
            Vector2 sp = new Vector2(80 + 150 * n, 200 + 200 * n);
            pressed = true; screen = sp; Frame();
            Check("with pauses left tap " + n + " still blinks onto the finger", Vector3.Distance(Pos, movePlayer.LandingPoint(FingerWorld(sp))) < 1e-4f);
        }
        Teardown();
        score.pauseCounter = saved;
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
