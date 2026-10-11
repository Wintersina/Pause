using System.IO;
using UnityEngine;
using static AttackTestKit;

// THE THROWN TRUNK (plan section 8.2e; the Roll behaviour as a boss hazard, AttackLog):
//
//   * geometry: the roll path is the Roll behaviour's (a diagonal at 1.4 u/s, one bounce off the rail), locked at the tell
//   * the tell: >= .7 s, the landing ring and the whole rolling track are previewed from the FIRST frame, the trunk is harmless in the air
//     and lands exactly when the tell is up
//   * life: Tell -> Live (rolls, bounces once, ends after its roll seconds or at the bottom of the view) -> fade -> pool
//   * the hit: a disc on the trunk's centre (the collider is the shape); heart / shield / blink rules, pause, pools + zero allocation
//   * the art slot verdant_attack_log.png, the procedural fallback and the pink cue
public static class AttackLogTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ATKLOG] PASS  " : "[ATKLOG] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                Path();
                Tell();
                Life();
                HitRules();
                PauseFreezes();
                PoolsAndAllocation();
                ArtSlot();
            }
            finally { AttackTestKit.Cleanup(); AttackBudgetScenarios.Cleanup(); }
        }
        Debug.Log("[ATKLOG] failures: " + fails);
        return fails;
    }

    static int FirstShip { get { foreach (int id in ShipId.All) return id; return 0; } }

    static AttackLog Arm(Vector2 landing, float dir, float tell = 1.2f, int world = 2)
    {
        return AttackLog.Arm(LogSpec.Standard(world), new Vector2(-2f, 3f), landing, dir, tell, null);
    }

    // ---- the path ----------------------------------------------------------------------------------------

    static void Path()
    {
        Fresh();
        var spec = LogSpec.Standard(2);
        Check("the roll is the Roll behaviour's: speed " + spec.speed + " u/s = ShotMotions.RollSpeed, life " + spec.rollSeconds + " s = ShotMotions.RollSeconds",
              Mathf.Approximately(spec.speed, ShotMotions.RollSpeed) && Mathf.Approximately(spec.rollSeconds, ShotMotions.RollSeconds));
        Vector2 v = AttackLog.Velocity(spec.speed, 1f);
        Check("down the board on a diagonal toward the rail: (" + v.x.ToString("F2") + ", " + v.y.ToString("F2") + ") has the speed " + v.magnitude.ToString("F2") + " u/s",
              v.x > 0f && v.y < 0f && Mathf.Abs(v.magnitude - spec.speed) < .1f);
        Check("it rolls toward the nearer rail, a coin (the parity) in the lane's middle", AttackLog.DirFor(1.2f, 0) == 1f && AttackLog.DirFor(-1.2f, 0) == -1f && AttackLog.DirFor(0f, 0) == 1f && AttackLog.DirFor(0f, 1) == -1f);
        float lim = 2.2f;
        Vector2 land = new Vector2(.4f, 1f);
        float tb = AttackLog.BounceTime(land, v, lim);
        Vector2 pb = AttackLog.PositionAt(land, v, lim, tb), pAfter = AttackLog.PositionAt(land, v, lim, tb + 1f);
        Check("it reaches the rail at " + tb.ToString("F2") + " s (x " + pb.x.ToString("F2") + " = the limit) and goes back out: a second later x " + pAfter.x.ToString("F2") + ", still falling",
              Mathf.Abs(pb.x - lim) < 1e-3f && pAfter.x < pb.x && pAfter.y < pb.y);
        bool onceOnly = true;
        float prevX = land.x;
        int flips = 0;
        float prevDx = 0f;
        float endT = AttackLog.EndTime(land, v, lim, 7f);
        for (float tau = 0f; tau <= endT; tau += .05f)
        {
            float x = AttackLog.PositionAt(land, v, lim, tau).x;
            float dx = x - prevX;
            if (tau > 0f && prevDx * dx < 0f) flips++;
            if (Mathf.Abs(x) > lim + 1e-3f) onceOnly = false;
            if (Mathf.Abs(dx) > 1e-6f) prevDx = dx;
            prevX = x;
        }
        Check("exactly one bounce along the whole path (" + flips + "), the centre never leaves the lane, and the roll ends at " + endT.ToString("F2") + " s, when it would meet the far rail (not the 7 s asked)", flips == 1 && onceOnly && endT < 7f);
    }

    // ---- the tell ----------------------------------------------------------------------------------------

    static void Tell()
    {
        Fresh();
        var l = Arm(new Vector2(.5f, 1f), 1f);
        Check("armed: the tell is " + l.TellSeconds + " s (>= " + AttackHazard.MinTellSeconds + "), harmless, the collider is off, the preview shows from the first frame (" + (l.Preview != null ? l.Preview.DotCount : 0) + " dots)",
              l.State == AttackHazard.Phase.Tell && l.TellSeconds >= AttackHazard.MinTellSeconds && !l.ZoneLive && !l.HitCollider.enabled && l.Preview != null && l.Preview.Active && l.Preview.DotCount > 40);
        Check("... the ghost trunk rests on the landing spot, dashes mark the rolling line (" + l.DashesShown + "), the trunk is shown in the thrower's petal",
              l.GhostRenderer.enabled && l.DashesShown >= 4 && l.BodyRenderer.enabled && Vector2.Distance(l.BodyRenderer.transform.position, new Vector2(-2f, 3f)) < .05f);
        Check("... the footprint is outlines only (no hit polygon yet), nothing can touch it", l.Footprint.PolyCount == 0 && l.Footprint.LoopCount >= 3 && !l.ZoneTouches(l.Landing, 1f));
        Vector2 land0 = l.Landing, vel0 = l.RollVelocity, org0 = l.Origin;
        Pilot.position = new Vector3(-2f, -3f, 0f);
        float flightMax = 0f;
        for (int i = 0; i < 40; i++) { Advance(1f / 60f); }
        Vector2 mid = l.BodyRenderer.transform.position;
        Check("it flies in the air: after .67 s the trunk is off the petal (" + mid.x.ToString("F2") + ", " + mid.y.ToString("F2") + ") and the aim has not moved", Vector2.Distance(mid, org0) > .5f && l.Landing == land0 && l.RollVelocity == vel0);
        Advance(l.TellSeconds * 0.5f - .67f + .2f);
        Check("the preview is still drawn in the last .4 s of the tell", l.State == AttackHazard.Phase.Tell && l.Preview != null && l.Preview.Active);
        float untilLand = l.TellLeft;
        Advance(untilLand - .05f);
        Vector2 nearEnd = l.BodyRenderer.transform.position;
        Check("... and just before it comes down the trunk is " + Vector2.Distance(nearEnd, land0).ToString("F2") + " u from the spot, still harmless (state " + l.State + ")", l.State == AttackHazard.Phase.Tell && Vector2.Distance(nearEnd, land0) < .6f && !l.HitCollider.enabled);
        Advance(.1f);
        Check("it lands when the tell is up: live, the hit disc on the spot, the dashes and the ghost gone", l.State == AttackHazard.Phase.Live && l.HitCollider.enabled && l.ZoneTouches(land0, .05f) && l.DashesShown == 0 && !l.GhostRenderer.enabled);
        EliteSystem.Clear();
        Fresh();
        var follow = new GameObject("~Thrower").transform;
        follow.position = new Vector3(-2f, 3f, 0f);
        var f = Arm(new Vector2(.5f, 1f), 1f);
        f.Follow(follow, Vector2.zero);
        follow.position = new Vector3(-1.6f, 3.2f, 0f);
        Advance(.1f);
        Check("until it is thrown the trunk rides the petal it waits in (" + f.BodyRenderer.transform.position.x.ToString("F2") + ", " + f.BodyRenderer.transform.position.y.ToString("F2") + ")", Vector2.Distance(f.BodyRenderer.transform.position, follow.position) < .05f);
        EliteSystem.Clear();
    }

    // ---- life ---------------------------------------------------------------------------------------------

    static void Life()
    {
        Fresh();
        var l = Arm(new Vector2(.5f, 1.2f), 1f, .8f);
        l.Ignite();
        Check("igniting early lands it at once on the spot (not before the caller says)", l.State == AttackHazard.Phase.Live && Vector2.Distance(l.Position, l.Landing) < .01f);
        float bottomSeen = 99f;
        bool bounced = false, discFollows = true;
        int steps = 0;
        while (l.State == AttackHazard.Phase.Live && steps < 60 * 12)
        {
            Advance(1f / 60f);
            steps++;
            if (l.State != AttackHazard.Phase.Live) break;
            if (l.Bounced) bounced = true;
            discFollows &= l.ZoneTouches(l.Position, .02f) && !l.ZoneTouches(l.Position + new Vector2(0f, l.HitHalf + .3f), .02f);
            bottomSeen = Mathf.Min(bottomSeen, l.Position.y);
        }
        float lived = steps / 60f;
        Check("it rolled " + lived.ToString("F1") + " s (life " + l.RollSeconds + " s or the bottom of the view), bounced off the rail once: " + bounced, bounced && lived <= l.RollSeconds + .1f);
        Check("the hit disc stayed on the centre of the trunk the whole roll", discFollows);
        Advance(.1f);
        Check("it fades (harmless) and goes back to the pool", l.State == AttackHazard.Phase.After || l.State == AttackHazard.Phase.Off);
        Advance(.5f);
        Check("... gone: nothing active, the preview over", AttackHazard.ActiveCount == 0 && AttackPreview.ActiveCount == 0 && AttackPools.ActiveCount == 0);
        EliteSystem.Clear();

        // it leaves by the bottom of the view when it rolled a long way
        Fresh();
        var spec = LogSpec.Standard(2);
        spec.rollSeconds = AttackLog.MaxRoll;
        var low = AttackLog.Arm(spec, new Vector2(-2f, 3f), new Vector2(0f, -3f), 1f, .8f, null);
        low.Ignite();
        float t0 = Time.realtimeSinceStartup;
        int n = 0;
        while (low.State == AttackHazard.Phase.Live && n < 60 * 10) { Advance(1f / 60f); n++; }
        Check("a trunk that landed low ends when it has left the bottom of the view (" + (n / 60f).ToString("F1") + " s, y " + low.Position.y.ToString("F1") + ")", n / 60f < AttackLog.MaxRoll && low.Position.y < CameraFit.ViewBottom);
        EliteSystem.Clear();
    }

    // ---- the hit ------------------------------------------------------------------------------------------

    static void HitRules()
    {
        Fresh();
        var rig = new DeathCrashTest.Rig(FirstShip);
        try
        {
            collisionDetection.MAXLIFE = 5;
            EliteSystem.PlayerOverride = rig.ship.transform;
            collisionDetection.lifeCounter = 0;
            PlayerInvuln.Reset();
            var l = Arm(new Vector2(0f, 1f), 1f, .8f);
            l.Ignite();
            Advance(.05f);
            Check("a live trunk is a hostile hitbox that is not a hazard body (RamKill) and a projectile to the death crash",
                  RamKill.NotAHazardBody(l.Hitbox) && DeathCrash.Classify(l.Hitbox) == DeathCrash.KillerKind.Projectile);
            rig.Hit(l.Hitbox);
            Check("an unshielded hit costs one heart (" + collisionDetection.lifeCounter + "); the trunk rolls on (" + l.State + ")", collisionDetection.lifeCounter == 1 && !buttonClicks.playerDied && l.State == AttackHazard.Phase.Live);
            l.Cancel();
            collisionDetection.lifeCounter = 0;
            PlayerInvuln.Reset();
            collisionDetection.atomCheck = true;
            RunScore.OnShieldRaised();
            l = Arm(new Vector2(0f, 1f), 1f, .8f);
            l.Ignite();
            Advance(.05f);
            rig.Hit(l.Hitbox);
            Check("under the shield the trunk is absorbed: the pulse ends (" + l.State + "), no heart lost (" + collisionDetection.lifeCounter + ")", l.State == AttackHazard.Phase.After && collisionDetection.lifeCounter == 0 && !l.HitCollider.enabled);
            collisionDetection.atomCheck = false;
            l.Cancel();
            l = Arm(new Vector2(0f, 1f), 1f, .8f);
            l.Ignite();
            Advance(.05f);
            Object.DestroyImmediate(l.Hitbox);
            Advance(.05f);
            Check("a destroyed hitbox ends it cleanly (" + l.State + ")", l.State == AttackHazard.Phase.After || l.State == AttackHazard.Phase.Off);
        }
        finally { rig.Dispose(); PlayerInvuln.Reset(); collisionDetection.lifeCounter = 0; collisionDetection.atomCheck = false; }
    }

    // ---- pause, pools -------------------------------------------------------------------------------------

    static void PauseFreezes()
    {
        Fresh();
        var l = Arm(new Vector2(0f, 1f), 1f);
        Advance(.3f);
        float tb = l.PhaseTime, shown = l.Preview.ShownSeconds;
        for (int i = 0; i < 120; i++) { EliteSystem.Step(0f); l.Step(0f); AttackPools.StepAll(0f); }
        Check("a frozen world (dt 0) freezes the trunk and its preview (" + tb.ToString("F2") + " -> " + l.PhaseTime.ToString("F2") + ")", l.PhaseTime == tb && l.Preview.ShownSeconds == shown && l.State == AttackHazard.Phase.Tell);
        string src = File.ReadAllText("Assets/Scripts/Gameplay/Enemies/Attacks/AttackLog.cs");
        bool clean = true;
        string why = "";
        foreach (string bad in new[] { "Time.deltaTime", "Time.unscaledDeltaTime", "void Update(", "void LateUpdate(", "void FixedUpdate(", "Time.time" })
            if (src.Contains(bad)) { clean = false; why += bad + "; "; }
        Check("the trunk advances only through Step(dt) (" + why + ")", clean);
        EliteSystem.Clear();
    }

    static void PoolsAndAllocation()
    {
        Fresh();
        var a = Arm(new Vector2(-1f, 1f), 1f);
        var b = Arm(new Vector2(0f, 1f), 1f);
        var c = Arm(new Vector2(1f, 1f), 1f);
        var d = Arm(new Vector2(0f, 2f), 1f);
        Check("three trunks fit, a fourth is skipped (a busy screen skips one)", a != null && b != null && c != null && d == null && AttackLog.Pool.Capacity == AttackLog.PoolSize);
        EliteSystem.Clear();
        Fresh();
        System.Action life = () =>
        {
            var l = Arm(new Vector2(.3f, 1f), 1f, .8f);
            for (int i = 0; i < 480; i++) { AttackPools.StepAll(Dt); HostileShots.Resolve(); }
        };
        life(); life();
        bool meter = TestHarness.AllocMeterWorks(out long ctl);
        long used = TestHarness.AllocatedBytes(life);
        Check("arming, throwing, landing, rolling, bouncing, fading and releasing a trunk allocates nothing after warm-up (" + used + " bytes; meter " + (meter ? "ok" : "blind") + ")", meter && used == 0);
        Check("the pool is back to idle", AttackPools.ActiveCount == 0 && AttackPreview.ActiveCount == 0 && AttackHazard.ActiveCount == 0);
        EliteSystem.Clear();
    }

    // ---- art ----------------------------------------------------------------------------------------------

    static Texture2D Atlas(int w, int h, Color32 c)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color32[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = c;
        t.SetPixels32(px);
        t.Apply();
        t.filterMode = FilterMode.Point;
        return t;
    }

    static void ArtSlot()
    {
        Fresh();
        AttackArt.Inject(2, "log", null);   // (the painted trunk is installed now: serve none to check the fallback)
        AttackHazardArt.Forget();
        var l = Arm(new Vector2(0f, 1f), 1f);
        var proc = l.BodyRenderer.sprite;
        Check("without verdant_attack_log.png the procedural trunk (ShotMotionArt's log) is drawn", !AttackArt.Has(2, "log") && proc != null && proc.texture != null && proc.bounds.size.y > proc.bounds.size.x);
        // the procedural body keeps the pink cue: a pink-white rim and a flickering core, at most half material pixels
        var tex = proc.texture;
        var px = tex.GetPixels32();
        int opaque = 0, pinkish = 0;
        foreach (var p in px)
        {
            if (p.a < 200) continue;
            opaque++;
            Color.RGBToHSV(p, out float h, out float s, out float v);
            if (h * 360f > 300f && h * 360f < 345f && s > .15f) pinkish++;
            else if (s < .25f && v > .8f) pinkish++;   // the white-hot core
        }
        Check("... with the pink cue in its pixels (" + pinkish + " of " + opaque + " are pink-white)", opaque > 20 && pinkish >= opaque / 5);
        EliteSystem.Clear();
        Fresh();
        AttackArt.Clear();
        AttackHazardArt.Forget();
        var atlas = Atlas(1024, 256, new Color32(255, 90, 220, 255));
        AttackArt.Inject(2, "log", atlas);
        var a = Arm(new Vector2(0f, 1f), 1f);
        Check("a delivered verdant_attack_log.png is used automatically (4 x 2 cells of 256 x 128, 1.1 u long)",
              a.BodyRenderer.sprite != null && a.BodyRenderer.sprite.texture == atlas && a.BodyRenderer.sprite.bounds.size.x > a.BodyRenderer.sprite.bounds.size.y);
        a.Ignite();
        Advance(.2f);
        Check("... and the rolling frames cycle through the eight cells", AttackArt.LogFrame(2, 0) != null && AttackArt.LogFrame(2, 7) != null && AttackArt.LogFrame(2, 0) != AttackArt.LogFrame(2, 5) && a.BodyRenderer.sprite.texture == atlas);
        AttackArt.Clear();
        AttackHazardArt.Forget();
        EliteSystem.Clear();
    }
}
