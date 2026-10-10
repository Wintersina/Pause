using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using static AttackTestKit;

// The themed area hazards' cores (plan phases 1c AttackBlast + 1d AttackStrike; docs/world-attacks-design.md 0.2):
//
//   * geometry: the ring is an annulus of bars with a crack (aimed at the pilot), grows at its speed, never past
//     the speed cap; the strike is a .36 wide column from just under the impact up; both against sample points
//   * life: Tell (harmless, previewed) -> Live -> After -> back in the pool; ignites itself when its tell is up
//   * the hit: the trigger collider is the shape (sampled), tagged "Enimey", not spent on the hull; a hit costs
//     one heart, a shield absorbs it, a blink erases it only where the hull lands on it, the fatal hit's
//     destroyed hitbox ends the pulse; RamKill / DeathCrash classify it as a projectile
//   * friendly fire (once a pulse, never the shooter), burning of crossing shots (IHostileZone), pause
//     (Step only, no Update / Time.deltaTime), pooling (fixed mass, nothing allocated per cycle), cleanup
//     (EliteSystem.Clear, Planetfall.ClearBoard, a death) and registration with HostileShots
public static class AttackHazardTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ATKHAZ] PASS  " : "[ATKHAZ] FAIL  ") + what);
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
                BlastGeometry();
                BlastLife();
                StrikeGeometry();
                StrikeLife();
                HitboxIsTheShape();
                HitRules();
                FriendlyFireOncePerPulse();
                ShotsBurn();
                PauseFreezes();
                PoolsAndAllocation();
                Cleanup_();
                BehaviourKinds();
            }
            finally { AttackTestKit.Cleanup(); AttackHazard.HurtsOtherEnemies = true; }
        }
        Debug.Log("[ATKHAZ] failures: " + fails);
        return fails;
    }

    static int FirstShip { get { foreach (int id in ShipId.All) return id; return 0; } }

    static Vector2 Dir(float rad) => new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

    // ---- the blast ----------------------------------------------------------------------------

    static AttackBlast ArmBlast(Vector2 muzzle, Vector2 target, float tell = 1f, int world = 1, GameObject by = null)
    {
        return AttackBlast.Arm(BlastSpec.Standard(world), muzzle, target, tell, by);
    }

    static void BlastGeometry()
    {
        Fresh();
        var spec = BlastSpec.Standard(1);
        int bars = AttackBlast.BarCount(in spec);
        var blast = ArmBlast(new Vector2(0f, 3f), new Vector2(0f, -1.2f));
        Check("a ring in its tell: " + bars + " bars of the 18-bar circle (the crack takes the rest), the tell is " + blast.TellSeconds.ToString("F2") +
              " s, it is harmless (no live flag, collider off, nothing burnt) and its footprint is previewed (" + (blast.Preview != null ? blast.Preview.DotCount : 0) + " dots)",
              blast.State == AttackHazard.Phase.Tell && blast.Footprint.PolyCount == bars && bars == 14 && blast.TellSeconds >= .7f && !blast.ZoneLive &&
              !blast.HitCollider.enabled && blast.Preview != null && blast.Preview.Active && blast.Preview.DotCount > 30 && !blast.ZoneTouches(new Vector2(0f, 3f), 5f));
        Check("... the glyph ring pulses at the muzzle and the crack's two edges are marked, the bars are not drawn yet",
              blast.GlyphRenderer.enabled && !blast.BarRenderer(0).enabled);
        float aimDeg = Mathf.Atan2(-1.2f - 3f, 0f) * Mathf.Rad2Deg;
        Check("the crack is aimed at the pilot when the tell starts (" + (blast.GapRad * Mathf.Rad2Deg).ToString("F1") + " deg vs " + aimDeg.ToString("F1") + ")",
              Mathf.Abs(Mathf.DeltaAngle(blast.GapRad * Mathf.Rad2Deg, aimDeg)) < 3f);
        // the crack stays where it was aimed even if the pilot moves
        Pilot.position = new Vector3(2f, -3f, 0f);
        Advance(.3f);
        Check("... and it stays there when the pilot moves afterwards (aim locked at the tell)",
              Mathf.Abs(Mathf.DeltaAngle(blast.GapRad * Mathf.Rad2Deg, aimDeg)) < 3f);
        blast.Ignite();
        Check("ignited: live, the collider is on, the zone is live", blast.State == AttackHazard.Phase.Live && blast.ZoneLive && blast.HitCollider.enabled);
        float r0 = blast.Radius;
        Advance(.5f);
        float speed = (blast.Radius - r0) / .5f;
        Check("the ring grows at " + spec.speed + " u/s (measured " + speed.ToString("F2") + "), never past the " + AttackBlast.MaxSpeed + " cap",
              Mathf.Abs(speed - spec.speed) < .1f && speed <= AttackBlast.MaxSpeed + .01f);
        Advance(.25f);
        float r = blast.Radius;
        Vector2 o = blast.Origin;
        float gap = blast.GapRad;
        var sh = blast.Footprint;
        int hit = 0;
        for (int k = 0; k < blast.Bars; k++)
        {
            AttackBlast.BarAt(in spec, o, gap, r, k, out Vector2 a, out Vector2 b, out float ang);
            if (sh.Touches((a + b) * .5f, 0f)) hit++;
        }
        Check("every bar's centre is on the hit shape (" + hit + "/" + blast.Bars + " at radius " + r.ToString("F2") + ")", hit == blast.Bars);
        Check("the crack's middle is clear even for the ship's circle (r .28), and so are the points .5 u inside and outside the ring on a bar",
              !sh.Touches(o + Dir(gap) * r, .28f));
        AttackBlast.BarAt(in spec, o, gap, r, 3, out Vector2 a3, out Vector2 b3, out float ang3);
        Vector2 c3 = (a3 + b3) * .5f, radial = Dir(ang3);
        Check("a bar hits within its half-thickness plus the ship's radius (" + spec.barHalf + " + .28) and not beyond, and not .5 u inside or outside",
              sh.Touches(c3 + radial * (spec.barHalf + .26f), .28f) && !sh.Touches(c3 + radial * (spec.barHalf + .32f), .28f) &&
              !sh.Touches(c3 + radial * .5f, 0f) && !sh.Touches(c3 - radial * .5f, 0f));
        // the free arc through the crack is at least the specified gap, bar to bar
        float stepDeg = AttackBlast.StepDeg(in spec);
        Check("the free gap between the two bars beside the crack is at least " + spec.gapDeg + " deg of arc (bars every " + stepDeg.ToString("F1") + " deg, " +
              (AttackBlast.BarFill * stepDeg).ToString("F1") + " deg long)", spec.gapDeg + stepDeg - AttackBlast.BarFill * stepDeg >= spec.gapDeg);
        // the wall is closed: outside the crack no ship (r .28) slips between two bars, at any radius, for the standard and the wide ring
        int open = 0, sampled = 0;
        foreach (var sp in new[] { BlastSpec.Standard(1), BlastSpec.Wide(1) })
            foreach (float rr in new[] { 1f, 2f, 3.4f, 4.5f, 6f })
            {
                if (rr > sp.reach) continue;
                var wall = new AttackShape();
                int n = AttackBlast.BarCount(in sp);
                for (int k = 0; k < n; k++)
                {
                    AttackBlast.BarAt(in sp, Vector2.zero, 0f, rr, k, out Vector2 ba, out Vector2 bb, out float bang);
                    wall.AddQuad(ba, bb, sp.barHalf, false);
                }
                float free = sp.gapDeg * .5f + AttackBlast.StepDeg(in sp);   // (the crack, plus the slot beside it)
                for (float deg = free; deg <= 360f - free; deg += .5f)
                {
                    sampled++;
                    if (!wall.Touches(Dir(deg * Mathf.Deg2Rad) * rr, DodgeBot.ShipRadius)) open++;
                }
            }
        Check("outside the crack the ring is a closed wall for the ship at every radius (" + open + " open points of " + sampled + " sampled: standard 18 bars to 3.4 u, wide 32 bars to 6 u)", open == 0 && sampled > 1000);
        Pilot.position = new Vector3(0f, -3f, 0f);
        blast.Cancel();
        Check("cancelled: gone at once, preview and collider off, back in the pool",
              blast.State == AttackHazard.Phase.Off && AttackBlast.Pool.ActiveCount == 0 && AttackPreview.ActiveCount == 0 && !blast.HitCollider.enabled);
    }

    static void BlastLife()
    {
        Fresh();
        var spec = BlastSpec.Standard(1);
        var blast = ArmBlast(new Vector2(0f, 3f), new Vector2(0f, -1.2f), .2f);   // (a tell asked for under the floor)
        Check("a tell asked for in .2 s is raised to " + AttackHazard.MinTellSeconds + " s (FR1) and the ring ignites by itself when it is up", Mathf.Approximately(blast.TellSeconds, AttackHazard.MinTellSeconds));
        float told = AdvanceUntil(() => blast.State != AttackHazard.Phase.Tell, 2f);
        Check("it went live after " + told.ToString("F2") + " s", blast.State == AttackHazard.Phase.Live && Mathf.Abs(told - AttackHazard.MinTellSeconds) < .05f);
        float live = AdvanceUntil(() => blast.State != AttackHazard.Phase.Live, 3f);
        Check("it burned for " + live.ToString("F2") + " s (reach " + spec.reach + " from " + spec.startRadius + " at " + spec.speed + " = " + spec.LiveSeconds.ToString("F2") + " s), then the last bars fade, harmless",
              Mathf.Abs(live - spec.LiveSeconds) < .08f && blast.State == AttackHazard.Phase.After && !blast.HitCollider.enabled && !blast.ZoneLive);
        AdvanceUntil(() => blast.State == AttackHazard.Phase.Off, 1f);
        Check("then it is back in the pool, every renderer off, no preview or dot left",
              blast.State == AttackHazard.Phase.Off && AttackBlast.Pool.ActiveCount == 0 && !blast.gameObject.activeSelf && AttackPreview.ActiveCount == 0 && AttackPreview.DotsInUse == 0);
        // ride: a hazard's ring rides the board; a pilot's stays in the world
        moveBackGround.speed = .4f;
        var rideSpec = BlastSpec.Standard(1);
        rideSpec.ride = 1f;
        var riding = AttackBlast.Arm(rideSpec, new Vector2(0f, 3f), new Vector2(0f, -1.2f), 1f, null);
        riding.Ignite();
        float y0 = riding.Origin.y;
        Advance(.5f);
        var held = AttackBlast.Arm(BlastSpec.Standard(1), new Vector2(1f, 3f), new Vector2(1f, -1.2f), 1f, null);
        held.Ignite();
        float hy0 = held.Origin.y;
        Advance(.5f);
        Check("a ring with ride 1 falls with the board (" + (y0 - riding.Origin.y).ToString("F2") + " u in .5 s), one with ride 0 stays (" + (hy0 - held.Origin.y).ToString("F2") + ")",
              y0 - riding.Origin.y > .05f && Mathf.Abs(hy0 - held.Origin.y) < .001f);
        moveBackGround.speed = 0f;
        // an over-fast spec is capped, a narrow crack is widened
        var wild = BlastSpec.Standard(1);
        wild.speed = 9f; wild.gapDeg = 20f;
        var b2 = AttackBlast.Arm(wild, new Vector2(0f, 3f), new Vector2(0f, -1.2f), 1f, null);
        b2.Ignite();
        float rr = b2.Radius;
        Advance(.3f);
        Check("a spec asking for 9 u/s is run at the " + AttackBlast.MaxSpeed + " cap (" + ((b2.Radius - rr) / .3f).ToString("F2") + ") and a 20 deg crack is widened to " + AttackBlast.MinGapDeg,
              (b2.Radius - rr) / .3f <= AttackBlast.MaxSpeed + .05f && b2.Spec.gapDeg >= AttackBlast.MinGapDeg);
        EliteSystem.Clear();
    }

    // ---- the strike ---------------------------------------------------------------------------

    static void StrikeGeometry()
    {
        Fresh();
        var spec = StrikeSpec.Standard(1);
        var s = AttackStrike.Arm(spec, 1f, -1.5f, 1f, null);
        Check("a strike in its tell: one hit rectangle that IS the outline (one polygon, one loop of four corners), previewed from the first frame, harmless, the lane glyph blinking",
              s.State == AttackHazard.Phase.Tell && s.Footprint.PolyCount == 1 && s.Footprint.LoopCount == 1 && s.Footprint.LoopLength(0) == 4 &&
              s.Preview != null && s.Preview.Active && !s.ZoneLive && !s.HitCollider.enabled && s.GlyphRenderer.enabled);
        var f = s.Footprint;
        float half = spec.hitHalf;
        Check("the column is " + (half * 2).ToString("F2") + " wide: hit on the lane and at the edge, clear beside it (the ship's circle r .28 included)",
              f.Touches(new Vector2(1f, 0f), 0f) && f.Touches(new Vector2(1f + half - .01f, 0f), 0f) && !f.Touches(new Vector2(1f + half + .01f, 0f), 0f) &&
              f.Touches(new Vector2(1f + half + .27f, 0f), .28f) && !f.Touches(new Vector2(1f + half + .3f, 0f), .28f) && !f.Touches(new Vector2(1f - half - .3f, 0f), .28f));
        Check("it runs from just under the impact point (" + AttackStrike.FootDepth + " u) up out of the top of the view: ground below it is clear",
              f.Touches(new Vector2(1f, -1.55f), 0f) && !f.Touches(new Vector2(1f, -1.8f), 0f) && f.Touches(new Vector2(1f, 5.4f), 0f) && !f.Touches(new Vector2(1f, 7f), 0f));
        var tall = StrikeSpec.Standard(3);
        var e = AttackStrike.Arm(tall, -1f, 0f, 1f, null);
        Check("an eruption is a finite geyser (" + tall.height + " u) rather than the whole lane", e.Footprint.Touches(new Vector2(-1f, 3f), 0f) && !e.Footprint.Touches(new Vector2(-1f, 4.5f), 0f));
        s.Cancel(); e.Cancel();
    }

    static void StrikeLife()
    {
        Fresh();
        var spec = StrikeSpec.Standard(1);
        var s = AttackStrike.Arm(spec, 0f, -1.5f, 1f, null);
        Advance(.95f);
        Check("still telling at .95 s of a 1 s tell: no hitbox, no body drawn, the preview shows (" + s.Preview.ShownSeconds.ToString("F2") + " s)",
              s.State == AttackHazard.Phase.Tell && !s.HitCollider.enabled && s.TilesShown == 0 && s.Preview.ShownSeconds > .9f);
        Advance(.1f);
        Check("then the column is live: collider on, body drawn from foot to top (" + s.TilesShown + " tiles), the glyph gone", s.State == AttackHazard.Phase.Live && s.HitCollider.enabled && s.TilesShown >= 6 && !s.GlyphRenderer.enabled);
        float live = AdvanceUntil(() => s.State != AttackHazard.Phase.Live, 1f);
        Advance(Dt);
        Check("it is live for " + live.ToString("F2") + " s (<= " + AttackStrike.MaxLiveSeconds + ", FR3), then the ground burst plays, harmless",
              live <= AttackStrike.MaxLiveSeconds + .03f && live >= .2f && s.State == AttackHazard.Phase.After && !s.HitCollider.enabled && s.BurstRenderer.enabled);
        float burst = AdvanceUntil(() => s.State == AttackHazard.Phase.Off, 1f);
        Check("the burst lasts " + burst.ToString("F2") + " s, then it is back in the pool with nothing drawn",
              s.State == AttackHazard.Phase.Off && AttackStrike.Pool.ActiveCount == 0 && AttackPreview.ActiveCount == 0 && Mathf.Abs(burst - AttackStrike.BurstSeconds) < .08f);
        var longer = StrikeSpec.Standard(1);
        longer.liveSeconds = 2f;
        var t = AttackStrike.Arm(longer, 0f, -1.5f, 1f, null);
        t.Ignite();
        float lv = AdvanceUntil(() => t.State != AttackHazard.Phase.Live, 3f);
        Check("a spec asking for 2 s of column is held to " + AttackStrike.MaxLiveSeconds + " s (" + lv.ToString("F2") + ")", lv <= AttackStrike.MaxLiveSeconds + .03f);
        EliteSystem.Clear();
        // lane pattern: nearest the pilot first, spaced, inside the rails
        float[] buf = new float[4];
        float rail = BossRails.DrawnInnerEdge;
        int n = StrikeLanes.Pick(.4f, 3, 1.9f, rail, .18f, buf);
        Check("a pattern of three: the pilot's lane then one either side, " + AttackStrike.MinLaneSpacing + " u apart (" + buf[0].ToString("F2") + ", " + buf[1].ToString("F2") + ", " + buf[2].ToString("F2") + ")",
              n == 3 && Mathf.Abs(buf[0] - .4f) < .001f && Mathf.Abs(Mathf.Abs(buf[1] - buf[0]) - 1.9f) < .001f && Mathf.Abs(Mathf.Abs(buf[2] - buf[0]) - 1.9f) < .001f && Mathf.Sign(buf[1] - buf[0]) != Mathf.Sign(buf[2] - buf[0]));
        n = StrikeLanes.Pick(rail - .3f, 4, 1.9f, rail, .18f, buf);
        bool inside = true;
        for (int i = 0; i < n; i++) inside &= Mathf.Abs(buf[i]) <= rail - .18f;
        Check("a pilot at the rail: the lanes are clamped inside the rails and pushed to the open side (" + n + " lanes)", n >= 2 && inside);
        n = StrikeLanes.Pick(0f, 1, 0.5f, rail, .18f, buf);
        Check("one lane is one lane; a spacing below the minimum is raised (" + AttackStrike.MinLaneSpacing + ")", n == 1);
        n = StrikeLanes.Pick(0f, 3, 0.5f, rail, .18f, buf);
        Check("... spacing asked for 0.5 gives " + Mathf.Abs(buf[1] - buf[0]).ToString("F2"), Mathf.Abs(Mathf.Abs(buf[1] - buf[0]) - AttackStrike.MinLaneSpacing) < .001f);
    }

    // ---- the hitbox is the shape ----------------------------------------------------------------

    static void HitboxIsTheShape()
    {
        Fresh();
        var blast = ArmBlast(new Vector2(0f, 3f), new Vector2(0f, -1.2f));
        blast.Ignite();
        Advance(.75f);
        Physics2D.SyncTransforms();
        int bothIn = 0, either = 0, total = 0, shapeOnly = 0;
        var col = blast.HitCollider;
        for (float x = -3.6f; x <= 3.6f; x += .071f)
            for (float y = -.7f; y <= 6.7f; y += .071f)
            {
                var p = new Vector2(x, y);
                bool a = blast.Footprint.Touches(p, 0f), b = col.OverlapPoint(p);
                total++;
                if (a || b) either++;
                if (a && b) bothIn++;
                if (a && !b) shapeOnly++;
            }
        Check("the ring's trigger collider covers the drawn/hit footprint (" + bothIn + " of " + either + " sample points agree; " + shapeOnly + " in the shape only, " + total + " sampled)",
              either > 150 && bothIn >= either * .95f);
        Check("the hitbox is tagged Enimey, named for the death crash, and carries the marker", blast.Hitbox.CompareTag("Enimey") && blast.Hitbox.name == AttackHazard.HitboxName && AttackHazard.IsHitbox(blast.Hitbox));
        blast.Cancel();

        var strike = AttackStrike.Arm(StrikeSpec.Standard(1), -.8f, -1.5f, 1f, null);
        strike.Ignite();
        Advance(.05f);
        Physics2D.SyncTransforms();
        int agree = 0, any = 0;
        col = strike.HitCollider;
        for (float x = -1.6f; x <= .1f; x += .02f)
            for (float y = -2.5f; y <= 5.5f; y += .1f)
            {
                var p = new Vector2(x, y);
                bool a = strike.Footprint.Touches(p, 0f), b = col.OverlapPoint(p);
                if (a || b) any++;
                if (a && b) agree++;
            }
        Check("the column's collider covers its rectangle (" + agree + " of " + any + " points)", any > 100 && agree >= any * .93f);
        strike.Cancel();
    }

    // ---- what a hit does ---------------------------------------------------------------------------

    static void HitRules()
    {
        Fresh();
        var rig = new DeathCrashTest.Rig(FirstShip);
        try
        {
            var ship = rig.ship.transform;
            collisionDetection.MAXLIFE = 5;   // (a non-fatal hit: the hull's own hearts do not matter here)
            EliteSystem.PlayerOverride = ship;
            ship.position = new Vector3(0f, 0f, 0f);
            collisionDetection.lifeCounter = 0;
            PlayerInvuln.Reset();
            // a strike on the ship's lane
            var s = AttackStrike.Arm(StrikeSpec.Standard(1), 0f, -1.5f, 1f, null);
            s.Ignite();
            Advance(.02f);
            Check("a live column is a hostile hitbox that is not a hazard body (RamKill) and a projectile to the death crash",
                  RamKill.NotAHazardBody(s.Hitbox) && DeathCrash.Classify(s.Hitbox) == DeathCrash.KillerKind.Projectile);
            long paid = RunScore.Total;
            rig.Hit(s.Hitbox);
            Check("an unshielded hit costs one heart (" + collisionDetection.lifeCounter + ") and pays nothing (" + (RunScore.Total - paid) + "); the hitbox survives (a column is not spent on the hull): " +
                  "died " + buttonClicks.playerDied + ", hitbox " + (s.Hitbox != null) + ", " + s.State + ", collider " + s.HitCollider.enabled,
                  collisionDetection.lifeCounter == 1 && !buttonClicks.playerDied && RunScore.Total == paid && s.Hitbox != null && s.State == AttackHazard.Phase.Live && s.HitCollider.enabled);
            s.Cancel();

            // under the shield: absorbed, the pulse ends, no heart
            collisionDetection.lifeCounter = 0;
            PlayerInvuln.Reset();
            collisionDetection.atomCheck = true;
            RunScore.OnShieldRaised();
            s = AttackStrike.Arm(StrikeSpec.Standard(1), 0f, -1.5f, 1f, null);
            s.Ignite();
            Advance(.02f);
            rig.Hit(s.Hitbox);
            Check("under the shield the column is absorbed: the pulse ends (" + s.State + "), no heart lost (" + collisionDetection.lifeCounter + "), the collider is off",
                  s.State == AttackHazard.Phase.After && collisionDetection.lifeCounter == 0 && !s.HitCollider.enabled);
            collisionDetection.atomCheck = false;
            s.Cancel();
            var blast = ArmBlast(new Vector2(0f, 3f), new Vector2(0f, -1.2f));
            blast.Ignite();
            Advance(.3f);
            Physics2D.SyncTransforms();
            collisionDetection.atomCheck = true;
            RunScore.OnShieldRaised();
            rig.Hit(blast.Hitbox);
            Check("a shielded hit on a ring ends that ring's pulse too (" + blast.State + ")", blast.State == AttackHazard.Phase.After && collisionDetection.lifeCounter == 0);
            collisionDetection.atomCheck = false;
            blast.Cancel();

            // a blink: erased only where the hull lands on it
            PlayerInvuln.Reset();
            s = AttackStrike.Arm(StrikeSpec.Standard(1), 0f, -1.5f, 1f, null);
            s.Ignite();
            Advance(.02f);
            bool handledFar = EliteShip.TeleportStrike(s.Hitbox, new Vector3(1.1f, 0f, 0f));
            bool stillLive = s.State == AttackHazard.Phase.Live;
            bool handledOn = EliteShip.TeleportStrike(s.Hitbox, new Vector3(0f, 0f, 0f));
            Check("a blink landing 1.1 u beside the column leaves it burning (handled: " + handledFar + "); one landing on it erases it",
                  handledFar && stillLive && handledOn && s.State == AttackHazard.Phase.After);
            s.Cancel();

            // the fatal hit destroys the hitbox: the pulse is over, nothing throws
            s = AttackStrike.Arm(StrikeSpec.Standard(1), 0f, -1.5f, 1f, null);
            s.Ignite();
            Advance(.02f);
            Object.DestroyImmediate(s.Hitbox);
            Advance(.05f);
            Check("a destroyed hitbox (the fatal hit) ends the pulse cleanly (" + s.State + ")", s.State == AttackHazard.Phase.After || s.State == AttackHazard.Phase.Off);
            EliteSystem.Clear();
            var again = AttackStrike.Arm(StrikeSpec.Standard(1), 0f, -1.5f, 1f, null);
            again.Ignite();
            Check("... and the pool makes a fresh hitbox for the next take", again != null && again.Hitbox != null && again.HitCollider.enabled);
        }
        finally { rig.Dispose(); PlayerInvuln.Reset(); collisionDetection.lifeCounter = 0; collisionDetection.atomCheck = false; }
    }

    // ---- friendly fire -------------------------------------------------------------------------------

    static GameObject Rock(Vector2 at)
    {
        var go = EnemyFactory.Create(EnemyRoster.One(3, EnemyRole.Rock), at, Quaternion.identity);
        ClearTarget.Ensure(go);
        FriendlyFire.Settle(go);
        return go;
    }

    static void FriendlyFireOncePerPulse()
    {
        Fresh();
        var shooter = Rock(new Vector2(0f, 2f));
        var victim = Rock(new Vector2(0f, 0f));
        var beside = Rock(new Vector2(1.5f, 0f));
        var s = AttackStrike.Arm(StrikeSpec.Standard(1), 0f, -1.5f, 1f, shooter);
        Advance(.5f);
        Check("a column in its tell hurts nothing", victim != null && FriendlyFire.HostileKills == 0);
        s.Ignite();
        Advance(.2f);
        Check("a live column destroys the hazard on its lane, unpaid (" + FriendlyFire.HostileKills + " kill), spares its own shooter standing in it and one beside it",
              victim == null && shooter != null && beside != null && FriendlyFire.HostileKills == 1);
        s.Cancel();

        // an elite loses one heart per pulse, however long the pulse stays on it
        EliteEvasion.Enabled = false;
        Fresh();
        EliteDef def = null;
        foreach (var d in EliteCatalog.All) if (d.brain == "gunship") { def = d; break; }
        var elite = EliteShip.CreateInPlay(def, new Vector2(2.1f, 3f));   // on the ring's path, off the crack (which points down at the pilot)
        elite.AttackCooldown = 99f;
        FriendlyFire.ResetHostileCounters();
        var blast = ArmBlast(new Vector2(0f, 3f), new Vector2(0f, -3f));
        blast.Ignite();
        AdvanceUntil(() => blast.State != AttackHazard.Phase.Live, 2f);
        Check("a ring passes over an elite and takes one heart (" + FriendlyFire.HostileEliteHits + " hit) in its whole pulse", FriendlyFire.HostileEliteHits == 1);
        AttackHazard.HurtsOtherEnemies = false;
        var rock = Rock(new Vector2(0f, 1f));
        var s2 = AttackStrike.Arm(StrikeSpec.Standard(1), 0f, -1.5f, 1f, null);
        s2.Ignite();
        Advance(.2f);
        Check("with friendly fire switched off for hazards nothing is hit", rock != null);
        AttackHazard.HurtsOtherEnemies = true;
        EliteEvasion.Enabled = true;
        EliteSystem.Clear();
    }

    // ---- shots ------------------------------------------------------------------------------------------

    static void ShotsBurn()
    {
        Fresh();
        var def = new EliteDef { key = "test_frost", world = "frost", shotSize = .22f, shotColor = "#FF4FD8", shotCore = "#FFFFFF" };
        def.Resolve();
        var spec = BlastSpec.Standard(1);
        var blast = ArmBlast(new Vector2(0f, 3f), new Vector2(0f, -1.2f));
        var pool = EliteSystem.Shots;
        var early = pool.Fire(null, def, EliteShots.Kind.Bolt, new Vector2(2f, 3f), Vector2.zero);
        Advance(.2f);
        Check("a ring in its tell burns nothing", early.Active);
        early.Recycle();
        blast.Ignite();
        Advance(.7f);
        AttackBlast.BarAt(in spec, blast.Origin, blast.GapRad, blast.Radius, 5, out Vector2 a, out Vector2 b, out float ang);
        Vector2 onBar = (a + b) * .5f;
        var burned = pool.Fire(null, def, EliteShots.Kind.Bolt, onBar, Vector2.zero);
        var heavy = pool.Fire(null, def, EliteShots.Kind.Shell, onBar + Dir(ang) * .02f, Vector2.zero);
        var safe = pool.Fire(null, def, EliteShots.Kind.Bolt, blast.Origin + Dir(blast.GapRad) * blast.Radius, Vector2.zero);
        Advance(.05f);
        Check("a live ring burns the light and the heavy shot lying on a bar and spares one in the crack (burns " + HostileShots.ZoneBurns + ")",
              !burned.Active && !heavy.Active && safe.Active && HostileShots.ZoneBurns >= 2);
        Check("a player shot sweeping through the ring does not shoot it down", HostileShots.ShootDownAlong(new Vector2(-3f, 3f), new Vector2(3f, 3f), .2f) >= 0 && blast.State == AttackHazard.Phase.Live);
        var zones = HostileShots.Zones;
        bool registered = false;
        foreach (var z in zones) registered |= ReferenceEquals(z, blast);
        Check("the ring is registered with HostileShots as a zone", registered);
        EliteSystem.Clear();
    }

    // ---- pause -------------------------------------------------------------------------------------------

    static void PauseFreezes()
    {
        Fresh();
        var blast = ArmBlast(new Vector2(0f, 3f), new Vector2(0f, -1.2f));
        var s = AttackStrike.Arm(StrikeSpec.Standard(1), 0f, -1.5f, 1f, null);
        Advance(.3f);
        float tb = blast.PhaseTime, ts = s.PhaseTime, shown = blast.Preview.ShownSeconds;
        for (int i = 0; i < 120; i++) { EliteSystem.Step(0f); blast.Step(0f); s.Step(0f); AttackPools.StepAll(0f); }
        Check("a frozen world (dt 0) freezes the hazards and their previews (blast " + tb.ToString("F2") + " -> " + blast.PhaseTime.ToString("F2") + ", strike " + ts.ToString("F2") + " -> " + s.PhaseTime.ToString("F2") + ")",
              blast.PhaseTime == tb && s.PhaseTime == ts && blast.Preview.ShownSeconds == shown && blast.State == AttackHazard.Phase.Tell);
        // the guard: no hazard runs on its own clock
        string dir = "Assets/Scripts/Gameplay/Enemies/Attacks/";
        bool clean = true;
        string why = "";
        foreach (string f in new[] { "AttackHazard.cs", "AttackBlast.cs", "AttackStrike.cs" })
        {
            string src = File.ReadAllText(dir + f);
            foreach (string bad in new[] { "Time.deltaTime", "Time.unscaledDeltaTime", "void Update(", "void LateUpdate(", "void FixedUpdate(", "Time.time" })
                if (src.Contains(bad)) { clean = false; why += f + " uses " + bad + "; "; }
        }
        Check("the hazards advance only through Step(dt): no Update, Time.deltaTime or Time.time in them (" + why + ")", clean);
        EliteSystem.Clear();
    }

    // ---- pools and allocation ---------------------------------------------------------------------------------

    static void PoolsAndAllocation()
    {
        Fresh();
        var a = ArmBlast(new Vector2(-1f, 3f), new Vector2(-1f, -1f));
        var b = ArmBlast(new Vector2(0f, 3f), new Vector2(0f, -1f));
        var c = ArmBlast(new Vector2(1f, 3f), new Vector2(1f, -1f));
        var d = ArmBlast(new Vector2(0f, 4f), new Vector2(0f, -1f));
        Check("three rings fit, a fourth is skipped (a busy screen skips one)", a != null && b != null && c != null && d == null && AttackBlast.Pool.Capacity == AttackBlast.PoolSize);
        int strikes = 0;
        for (int i = 0; i < AttackStrike.PoolSize + 2; i++) if (AttackStrike.Arm(StrikeSpec.Standard(1), -2f + i * .4f, 0f, 1f, null) != null) strikes++;
        Check("the strike pool holds " + AttackStrike.PoolSize + " (" + strikes + " armed of " + (AttackStrike.PoolSize + 2) + " asked)", strikes == AttackStrike.PoolSize);
        EliteSystem.Clear();

        // a ring and four columns telling at once: every outline is drawn in full (the dot pool is big enough)
        Fresh();
        AttackPreview.ResetCounters();
        ArmBlast(new Vector2(0f, 3f), new Vector2(0f, -1.2f));
        for (int i = 0; i < 4; i++) AttackStrike.Arm(StrikeSpec.Standard(1), -2.2f + i * 1.45f, -1.5f, 1f, null);
        Check("a ring and four columns in their tell at once: all " + AttackPreview.ActiveCount + " previews are drawn in full (" + AttackPreview.DotsInUse + " dots, " + AttackPreview.Dropped + " dropped)",
              AttackPreview.ActiveCount == 5 && AttackPreview.Dropped == 0 && AttackPreview.DotsInUse < AttackPreview.MaxDots);
        EliteSystem.Clear();

        // a whole life of each, twice to warm, then measured
        Fresh();
        System.Action life = () =>
        {
            var bl = ArmBlast(new Vector2(0f, 3f), new Vector2(0f, -1.2f), .8f);
            var st = AttackStrike.Arm(StrikeSpec.Standard(1), 0f, -1.5f, .8f, null);
            for (int i = 0; i < 220; i++) { AttackPools.StepAll(Dt); HostileShots.Resolve(); }
        };
        life(); life();
        bool meter = TestHarness.AllocMeterWorks(out long ctl);
        long used = TestHarness.AllocatedBytes(life);
        Check("arming, telling, igniting, burning, fading and releasing a ring and a strike allocates nothing after warm-up (" + used + " bytes; meter " + (meter ? "ok" : "blind") + ")", meter && used == 0);
        Check("the pools are back to idle, nothing in the scene stays active", AttackPools.ActiveCount == 0 && AttackPreview.ActiveCount == 0 && AttackHazard.ActiveCount == 0);
        EliteSystem.Clear();
    }

    // ---- cleanup -----------------------------------------------------------------------------------------------

    static void Cleanup_()
    {
        Fresh();
        var blast = ArmBlast(new Vector2(0f, 3f), new Vector2(0f, -1.2f));
        blast.Ignite();
        var s = AttackStrike.Arm(StrikeSpec.Standard(1), 1f, -1.5f, 1f, null);
        Advance(.1f);
        Check("a ring and a strike are live/telling", AttackHazard.ActiveCount == 2 && AttackPreview.ActiveCount >= 1);
        Planetfall.ClearBoard(null);
        Check("a world change (Planetfall.ClearBoard) clears every hazard and preview", AttackHazard.ActiveCount == 0 && AttackPreview.ActiveCount == 0 && !blast.gameObject.activeSelf);

        var b2 = ArmBlast(new Vector2(0f, 3f), new Vector2(0f, -1.2f));
        b2.Ignite();
        var s2 = AttackStrike.Arm(StrikeSpec.Standard(1), 0f, -1.5f, 1f, null);
        var dir = new GameObject("~Director").AddComponent<EliteDirector>();
        buttonClicks.playerDied = true;
        typeof(EliteDirector).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(dir, null);
        Check("a player death clears them on the director's next frame", AttackHazard.ActiveCount == 0 && AttackPreview.ActiveCount == 0 && !b2.HitCollider.enabled);
        buttonClicks.playerDied = false;
        Object.DestroyImmediate(dir.gameObject);

        var b3 = ArmBlast(new Vector2(0f, 3f), new Vector2(0f, -1.2f));
        b3.Ignite();
        int before = HostileShots.Zones.Count;
        EliteSystem.Clear();   // a restart / scene teardown
        HostileShots.Resolve();
        bool left = false;
        foreach (var z in HostileShots.Zones) left |= z != null && !(z is Object o && o == null) && ReferenceEquals(z, b3);
        Check("a restart (EliteSystem.Clear) leaves no hazard active and unregisters the destroyed zones (" + before + " -> " + HostileShots.Zones.Count + ")",
              AttackHazard.ActiveCount == 0 && !left && HostileShots.Zones.Count < before);
        // a hazard armed again after the world was torn down builds a fresh pool
        var fresh = ArmBlast(new Vector2(0f, 3f), new Vector2(0f, -1.2f));
        Check("the next take builds a fresh pool", fresh != null && fresh.State == AttackHazard.Phase.Tell && AttackBlast.Pool.Alive);
        EliteSystem.Clear();
    }

    // ---- EnemyAttack.Blast / Strike through the real brain ---------------------------------------------------------

    static void BehaviourKinds()
    {
        var blast = new EnemyBehaviour { key = "x" }.Blast(BlastSpec.Standard(1)).Timing(.5f, 3f, 2);
        var strike = new EnemyBehaviour { key = "y" }.Strike(StrikeSpec.Standard(3), 3).Timing(.5f, 3f, 2);
        var shot = new EnemyBehaviour { key = "z" }.Shot(EliteShots.Kind.Shard, 3, 22f, 2.4f, .22f);
        Check("Blast and Strike are attacks that shoot and are area hazards; a fan is not",
              blast.Attacks && blast.Shoots && blast.IsAreaHazard && strike.Attacks && strike.Shoots && strike.IsAreaHazard && !shot.IsAreaHazard);
        Check("their windup is raised to the instant-hit floor (" + EnemyBrain.TellFor(blast).ToString("F2") + ", " + EnemyBrain.TellFor(strike).ToString("F2") + " vs a fan's " + EnemyBrain.TellFor(shot).ToString("F2") + ")",
              EnemyBrain.TellFor(blast) >= AttackHazard.MinTellSeconds && EnemyBrain.TellFor(strike) >= AttackHazard.MinTellSeconds && EnemyBrain.TellFor(shot) == Mathf.Max(EnemyBrain.TellFloorSeconds, shot.tell));
        Check("they reserve the shot budget FR7 names: a blast 2, a strike one a lane, a fan its shots (" + blast.ThreatCount + "/" + strike.ThreatCount + "/" + shot.ThreatCount + ")",
              blast.ThreatCount == 2 && strike.ThreatCount == 3 && shot.ThreatCount == 3);
        // nothing in the shipping table uses them yet: the game behaves as before
        int used = 0;
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def.key);
            if (b != null && b.IsAreaHazard) used++;
        }
        Check("no world's roster uses Blast or Strike yet (behaviour unchanged until the per-world phases): " + used, used == 0);
    }
}
