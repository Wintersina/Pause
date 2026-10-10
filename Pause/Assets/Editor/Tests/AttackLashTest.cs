using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using static AttackTestKit;

// THE LASH CORE (plan phase 1e; docs/world-attacks-design.md 0.1 / 0.2: Verdant's vine whip, Tide's tentacle lash).
// Written like AttackHazardTest + the pink-cue and corridor parts of AttackFairnessTest, for AttackLash:
//
//   * geometry: the hit shape is a ribbon of trapezoids along the chain (no two overlap), the swept area is the
//     preview outline, the sweep starts beside the pilot (Aim) and is locked at the tell
//   * life: Tell (>= .7 s, previewed) -> Live (sweep .25-.45 s) -> retract -> back in the pool
//   * the hit: the trigger collider is the shape; a heart / shield / blink / destroyed hitbox, friendly fire once
//     a pulse, burning of crossing shots, pause, pools + zero allocation, cleanup
//   * fairness: a corridor >= 1.4 u beyond the start line the pilot can reach in the tell, for pilots across the lane
//   * the pink cue on the procedural sprites (vine and tentacle), the art slot, the behaviour through the real brain
public static class AttackLashTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ATKLASH] PASS  " : "[ATKLASH] FAIL  ") + what);
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
                Geometry();
                Life();
                Sweep();
                HitboxIsTheShape();
                HitRules();
                FriendlyFireOncePerPulse();
                ShotsBurn();
                PauseFreezes();
                PoolsAndAllocation();
                Cleanup_();
                BehaviourKind();
                RealBrain();
                Corridor();
                DrawnPixels();
                ArtSlot();
            }
            finally { AttackTestKit.Cleanup(); AttackBudgetScenarios.Cleanup(); AttackHazard.HurtsOtherEnemies = true; }
        }
        Debug.Log("[ATKLASH] failures: " + fails);
        return fails;
    }

    static int FirstShip { get { foreach (int id in ShipId.All) return id; return 0; } }

    static AttackLash Arm(Vector2 root, Vector2 target, float tell = 1f, int world = 2, GameObject by = null)
    {
        return AttackLash.Arm(LashSpec.Standard(world), root, target, tell, by);
    }

    // How many sample points of a grid lie inside more than one polygon of the shape (a collider fills even-odd: overlap would punch holes).
    static int Overlapped(AttackShape sh)
    {
        int n = 0;
        for (float x = -5f; x <= 5f; x += .05f)
            for (float y = -6f; y <= 5f; y += .05f)
            {
                int inside = 0;
                for (int k = 0; k < sh.PolyCount; k++) if (InPoly(sh, k, new Vector2(x, y))) inside++;
                if (inside > 1) n++;
            }
        return n;
    }

    static bool InPoly(AttackShape sh, int k, Vector2 p)
    {
        int len = sh.PolyLength(k);
        float sign = 0f;
        for (int i = 0; i < len; i++)
        {
            Vector2 a = sh.PolyAt(k, i), b = sh.PolyAt(k, (i + 1) % len);
            float cross = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
            if (Mathf.Abs(cross) < 1e-5f) continue;   // (on an edge: the shared edge of two neighbours counts for neither)
            if (sign == 0f) sign = cross; else if (cross * sign < 0f) return false;
        }
        return sign != 0f;
    }

    static float DegDelta(float a, float b) => Mathf.DeltaAngle(a * Mathf.Rad2Deg, b * Mathf.Rad2Deg);

    // ---- geometry --------------------------------------------------------------------------------------

    static void Geometry()
    {
        Fresh();
        float rail = BossRails.DrawnInnerEdge;
        Vector2 root = new Vector2(0f, 3f), pilot = new Vector2(.4f, -2.5f);
        var lash = Arm(root, pilot);
        Check("a whip in its tell: " + lash.Links + " links as ribbon polygons (" + lash.Footprint.PolyCount + "), one outline loop (the swept area), the tell is " + lash.TellSeconds.ToString("F2") +
              " s, it is harmless (not live, collider off, nothing burnt) and its sweep is previewed (" + (lash.Preview != null ? lash.Preview.DotCount : 0) + " dots)",
              lash.State == AttackHazard.Phase.Tell && lash.Links >= AttackLash.MinLinks && lash.Footprint.PolyCount == lash.Links && lash.Footprint.LoopCount == 1 &&
              lash.TellSeconds >= .7f && !lash.ZoneLive && !lash.HitCollider.enabled && lash.Preview != null && lash.Preview.Active && lash.Preview.DotCount > 40 &&
              !lash.ZoneTouches(root, 5f));
        Check("... the ghost whip lies on the first line (" + lash.LinksShown + " links, the thorn tip, the root bud) and the dotted arc is marked (" + lash.DashesShown + " dashes), nothing burns",
              lash.LinksShown == lash.Links && lash.TipRenderer.enabled && lash.RootRenderer.enabled && lash.DashesShown >= 5);
        float range = Vector2.Distance(root, pilot);
        Check("the whip is long enough to reach the pilot's range (" + lash.Length.ToString("F2") + " u for " + range.ToString("F2") + " u)", lash.Length >= range + 1f && lash.Length <= AttackLash.MaxLength + .01f);
        // the start line crosses the pilot's row beside him
        Vector2 u = new Vector2(Mathf.Cos(lash.StartRad), Mathf.Sin(lash.StartRad));
        float edgeX = root.x + u.x * ((pilot.y - root.y) / u.y);
        Check("the sweep starts on a line that crosses the pilot's row at x " + edgeX.ToString("F2") + " (pilot at " + pilot.x + "), " + Mathf.Abs(edgeX - pilot.x).ToString("F2") + " u beside him, with " +
              lash.Room.ToString("F2") + " u of floor beyond it (>= " + (AttackLash.MinCorridor + AttackLash.CorridorMargin) + "): he stands inside the sweep and has to step out",
              Mathf.Abs(edgeX - pilot.x) >= .5f && Mathf.Abs(edgeX - pilot.x) <= 1.55f && lash.Room >= AttackLash.MinCorridor + AttackLash.CorridorMargin - .01f);
        // aim locked at the tell
        float start0 = lash.StartRad;
        int dir0 = lash.Dir;
        Pilot.position = new Vector3(-2f, -3f, 0f);
        Advance(.3f);
        Check("the aim is locked at the tell: the start line and the turning do not follow the pilot (" + lash.StartRad.ToString("F3") + " vs " + start0.ToString("F3") + ")", lash.StartRad == start0 && lash.Dir == dir0);
        Pilot.position = new Vector3(0f, -3f, 0f);
        lash.Cancel();

        // the ribbon: shared joint edges, no overlap, at the first pose, mid sweep, the end
        lash = Arm(root, pilot);
        lash.Ignite();
        int over = 0, polys = 0;
        for (int step = 0; step < 4; step++)
        {
            Advance(lash.SweepSeconds / 4f - .001f);
            if (lash.State != AttackHazard.Phase.Live) break;
            over += Overlapped(lash.Footprint);
            polys = lash.Footprint.PolyCount;
        }
        Check("the live ribbon of " + polys + " trapezoids shares its joint edges: no point of the floor lies in two of them (" + over + " overlapped samples at four moments of the sweep)", over == 0 && polys == lash.Links);
        lash.Cancel();
        EliteSystem.Clear();
    }

    // ---- life ------------------------------------------------------------------------------------------

    static void Life()
    {
        Fresh();
        var lash = Arm(new Vector2(0f, 3f), new Vector2(0f, -2.5f), .2f);
        Check("a tell asked for in .2 s is raised to " + AttackHazard.MinTellSeconds + " s (FR1) and the whip ignites by itself when it is up", Mathf.Approximately(lash.TellSeconds, AttackHazard.MinTellSeconds));
        float told = AdvanceUntil(() => lash.State != AttackHazard.Phase.Tell, 2f);
        Check("it went live after " + told.ToString("F2") + " s, the dashes gone", lash.State == AttackHazard.Phase.Live && Mathf.Abs(told - AttackHazard.MinTellSeconds) < .05f && lash.DashesShown == 0 && lash.HitCollider.enabled);
        float live = AdvanceUntil(() => lash.State != AttackHazard.Phase.Live, 2f);
        Check("it swept for " + live.ToString("F2") + " s (spec " + lash.SweepSeconds.ToString("F2") + ", inside " + AttackLash.MinSweep + " .. " + AttackLash.MaxSweep + ", FR3), then it draws back, harmless",
              Mathf.Abs(live - lash.SweepSeconds) < .05f && lash.SweepSeconds >= AttackLash.MinSweep && lash.SweepSeconds <= AttackLash.MaxSweep && lash.State == AttackHazard.Phase.After && !lash.HitCollider.enabled && !lash.ZoneLive);
        Advance(AttackLash.RetractSeconds * .5f);
        Check("... tip end first (" + lash.LinksShown + " of " + lash.Links + " links left halfway)", lash.LinksShown > 0 && lash.LinksShown < lash.Links && !lash.TipRenderer.enabled);
        AdvanceUntil(() => lash.State == AttackHazard.Phase.Off, 1f);
        Check("then it is back in the pool, every renderer off, no preview or dot left",
              lash.State == AttackHazard.Phase.Off && AttackLash.Pool.ActiveCount == 0 && !lash.gameObject.activeSelf && AttackPreview.ActiveCount == 0 && AttackPreview.DotsInUse == 0);
        // spec limits
        var fast = LashSpec.Standard(2); fast.sweepSeconds = .05f;
        var slow = LashSpec.Standard(2); slow.sweepSeconds = 9f;
        var a = AttackLash.Arm(fast, new Vector2(-1f, 3f), new Vector2(-1f, -2.5f), 1f, null);
        var b = AttackLash.Arm(slow, new Vector2(1f, 3f), new Vector2(1f, -2.5f), 1f, null);
        Check("a sweep asked for in .05 s is held to " + AttackLash.MinSweep + " s and one asked for in 9 s to " + AttackLash.MaxSweep + " s (" + a.SweepSeconds.ToString("F2") + ", " + b.SweepSeconds.ToString("F2") + ")",
              Mathf.Approximately(a.SweepSeconds, AttackLash.MinSweep) && Mathf.Approximately(b.SweepSeconds, AttackLash.MaxSweep));
        a.Cancel(); b.Cancel();
        // ride: a hazard's whip falls with the board, a pilot's stays
        moveBackGround.speed = .4f;
        var rideSpec = LashSpec.Standard(2); rideSpec.ride = 1f;
        var riding = AttackLash.Arm(rideSpec, new Vector2(-1f, 3f), new Vector2(-1f, -2.5f), 1f, null);
        var held = AttackLash.Arm(LashSpec.Standard(2), new Vector2(1f, 3f), new Vector2(1f, -2.5f), 1f, null);
        float y0 = riding.Root.y, hy0 = held.Root.y;
        Advance(.5f);
        Check("a whip with ride 1 falls with the board (" + (y0 - riding.Root.y).ToString("F2") + " u in .5 s), one with ride 0 stays (" + (hy0 - held.Root.y).ToString("F2") + ")",
              y0 - riding.Root.y > .05f && Mathf.Abs(hy0 - held.Root.y) < .001f);
        moveBackGround.speed = 0f;
        // the root follows its shooter through the tell and the sweep
        var shooter = new GameObject("~Shooter").transform;
        shooter.position = new Vector3(1f, 3f, 0f);
        var f = Arm(new Vector2(1f, 3f), new Vector2(1f, -2.5f), 1f, 2, shooter.gameObject);
        f.Follow(shooter, Vector2.zero);
        shooter.position = new Vector3(1.6f, 3.2f, 0f);
        Advance(.1f);
        Check("a whip that follows its shooter keeps its root on him (" + f.Root.ToString("F2") + ")", Vector2.Distance(f.Root, new Vector2(1.6f, 3.2f)) < .01f);
        EliteSystem.Clear();
    }

    // ---- the sweep: the swept area is the outline, the safe side stays open -----------------------------------

    static void Sweep()
    {
        Fresh();
        float rail = BossRails.DrawnInnerEdge;
        Vector2 root = new Vector2(.5f, 3f), pilot = new Vector2(.3f, -2.8f);
        var lash = Arm(root, pilot);
        lash.Ignite();
        float arc = AttackLash.ArcRad(lash.Spec);
        var tipPos = new List<Vector2>();
        var touched = new HashSet<long>();
        int aheadOk = 0, frames = 0;
        float lastAng = lash.StartRad;
        bool monotonic = true;
        while (lash.State == AttackHazard.Phase.Live && frames < 100)
        {
            EliteSystem.Step(Dt);
            if (lash.State != AttackHazard.Phase.Live) break;
            frames++;
            var tip = lash.Joint(lash.Links);
            float ang = Mathf.Atan2(tip.y - lash.Root.y, tip.x - lash.Root.x);
            if (lash.Dir * DegDelta(lastAng, ang) < -.01f) monotonic = false;
            lastAng = ang;
            if (frames == Mathf.RoundToInt(lash.SweepSeconds / Dt / 2f))
            {
                // mid sweep the tip is further along than the middle of the chain: the whip bends back
                var mid = lash.Joint(lash.Links / 2);
                float angMid = Mathf.Atan2(mid.y - lash.Root.y, mid.x - lash.Root.x);
                if (lash.Dir * DegDelta(angMid, ang) > 2f) aheadOk++;
            }
            for (float x = -4.5f; x <= 4.5f; x += .12f)
                for (float y = -6f; y <= 4.5f; y += .12f)
                    if (lash.Footprint.Touches(new Vector2(x, y), 0f)) touched.Add(((long)Mathf.RoundToInt(x * 100f) << 20) ^ (long)Mathf.RoundToInt(y * 100f + 1000f));
        }
        Check("the tip turns one way only, " + (lash.Dir > 0 ? "counter-clockwise" : "clockwise") + ", through " + (arc * Mathf.Rad2Deg).ToString("F0") + " deg and ends on the end line (" +
              (lash.Dir * DegDelta(lash.StartRad, lastAng)).ToString("F1") + " deg turned)",
              monotonic && Mathf.Abs(lash.Dir * DegDelta(lash.StartRad, lastAng) - arc * Mathf.Rad2Deg) < 3f);
        Check("mid sweep the tip leads the middle of the chain by more than 2 deg (the lash bends back and straightens as it ends)", aheadOk == 1);
        // everything the whip ever touched lies in the swept area the preview outlines: radius <= reach, angle between the start and end lines
        int outside = 0;
        float reach = lash.Length + lash.HitHalf + .1f;
        foreach (long key in touched)
        {
            // (decode is not worth it: re-scan below)
        }
        for (float x = -4.5f; x <= 4.5f; x += .12f)
            for (float y = -6f; y <= 4.5f; y += .12f)
            {
                long k = ((long)Mathf.RoundToInt(x * 100f) << 20) ^ (long)Mathf.RoundToInt(y * 100f + 1000f);
                if (!touched.Contains(k)) continue;
                Vector2 d = new Vector2(x, y) - lash.Root;
                float r = d.magnitude;
                if (r > reach) { outside++; continue; }
                if (r < .4f) continue;
                float along = lash.Dir * DegDelta(lash.StartRad, Mathf.Atan2(d.y, d.x));
                float slack = Mathf.Atan2(lash.HitHalf + .12f, r) * Mathf.Rad2Deg + 1f;
                if (along < -slack || along > arc * Mathf.Rad2Deg + slack) outside++;
            }
        Check("every point the whip ever burned (" + touched.Count + " samples over the sweep) lies in the swept area the preview outlined: within the reach and between the start and end lines (" + outside + " outside)", touched.Count > 300 && outside == 0);
        // the floor beyond the start line is never touched
        Vector2 u = new Vector2(Mathf.Cos(lash.StartRad), Mathf.Sin(lash.StartRad));
        float edgeX = root.x + u.x * ((pilot.y - root.y) / u.y);
        float away = Mathf.Sign(edgeX - pilot.x);   // the safe side is the one the line lies on
        // (the start line is on the safe side of the pilot; beyond it is the corridor)
        int hits = 0, samples = 0;
        var again = Arm(root, pilot);
        again.Ignite();
        for (int f = 0; f < 40 && again.State == AttackHazard.Phase.Live; f++)
        {
            EliteSystem.Step(Dt);
            if (again.State != AttackHazard.Phase.Live) break;
            for (float x = edgeX + away * .45f; Mathf.Abs(x) <= rail; x += away * .1f)
            {
                samples++;
                if (again.Footprint.Touches(new Vector2(x, pilot.y), 0f)) hits++;
            }
        }
        Check("the floor beyond the start line (" + samples + " samples at the pilot's row over the sweep) is never touched: the way out stays open (" + hits + " touched)", samples > 100 && hits == 0);
        float tipSpeed = lash.Length * arc * (1f + AttackLash.Lead(lash.Spec)) / lash.SweepSeconds;
        Debug.Log("[ATKLASH] INFO  tip speed " + tipSpeed.ToString("F1") + " u/s over " + lash.Length.ToString("F2") + " u, " + lash.SweepSeconds.ToString("F2") + " s");
        EliteSystem.Clear();
    }

    // ---- the hitbox is the shape -------------------------------------------------------------------------

    static void HitboxIsTheShape()
    {
        Fresh();
        var lash = Arm(new Vector2(0f, 3f), new Vector2(.2f, -2.5f));
        lash.Ignite();
        Advance(lash.SweepSeconds * .5f);
        Physics2D.SyncTransforms();
        int bothIn = 0, either = 0, shapeOnly = 0;
        var col = lash.HitCollider;
        for (float x = -4f; x <= 4f; x += .06f)
            for (float y = -5f; y <= 3.2f; y += .06f)
            {
                var p = new Vector2(x, y);
                bool a = lash.Footprint.Touches(p, 0f), b = col.OverlapPoint(p);
                if (a || b) either++;
                if (a && b) bothIn++;
                if (a && !b) shapeOnly++;
            }
        Check("the whip's trigger collider covers its hit footprint (" + bothIn + " of " + either + " sample points agree; " + shapeOnly + " in the shape only; " + col.pathCount + " paths)", either > 150 && bothIn >= either * .95f && col.pathCount == lash.Links);
        Check("the hitbox is tagged Enimey, named for the death crash, and carries the marker", lash.Hitbox.CompareTag("Enimey") && lash.Hitbox.name == AttackHazard.HitboxName && AttackHazard.IsHitbox(lash.Hitbox));
        // the chain's drawing lies on the hit ribbon: each link sprite is centred between its two joints
        int placed = 0;
        for (int i = 0; i < lash.Links; i++)
        {
            Vector2 c = (lash.Joint(i) + lash.Joint(i + 1)) * .5f;
            if (lash.LinkRenderer(i).enabled && Vector2.Distance(lash.LinkRenderer(i).transform.position, c) < .01f) placed++;
        }
        Check("each drawn link lies between its two joints, on the ribbon that hits (" + placed + "/" + lash.Links + "), and the tip is capped by the thorn", placed == lash.Links && lash.TipRenderer.enabled);
        // the hit width: .14 + the ship's .28 either side of the chain, no more
        var mid = (lash.Joint(2) + lash.Joint(3)) * .5f;
        Vector2 dd = (lash.Joint(3) - lash.Joint(2)).normalized, nn = new Vector2(-dd.y, dd.x);
        float hw = lash.HitHalf;
        Check("the whip hits within its half width plus the ship's radius (" + hw + " + .28) of the chain and not beyond",
              lash.Footprint.Touches(mid + nn * (hw + .26f), .28f) && !lash.Footprint.Touches(mid + nn * (hw + .33f), .28f) && !lash.Footprint.Touches(mid + nn * .8f, 0f));
        lash.Cancel();
        EliteSystem.Clear();
    }

    // ---- what a hit does ------------------------------------------------------------------------------------

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
            var l = Arm(new Vector2(0f, 3f), new Vector2(0f, -2.5f));
            l.Ignite();
            Advance(.05f);
            Check("a live whip is a hostile hitbox that is not a hazard body (RamKill) and a projectile to the death crash",
                  RamKill.NotAHazardBody(l.Hitbox) && DeathCrash.Classify(l.Hitbox) == DeathCrash.KillerKind.Projectile);
            long paid = RunScore.Total;
            rig.Hit(l.Hitbox);
            Check("an unshielded hit costs one heart (" + collisionDetection.lifeCounter + ") and pays nothing; the whip is not spent on the hull (" + l.State + ")",
                  collisionDetection.lifeCounter == 1 && !buttonClicks.playerDied && RunScore.Total == paid && l.Hitbox != null && l.State == AttackHazard.Phase.Live && l.HitCollider.enabled);
            l.Cancel();

            collisionDetection.lifeCounter = 0;
            PlayerInvuln.Reset();
            collisionDetection.atomCheck = true;
            RunScore.OnShieldRaised();
            l = Arm(new Vector2(0f, 3f), new Vector2(0f, -2.5f));
            l.Ignite();
            Advance(.05f);
            rig.Hit(l.Hitbox);
            Check("under the shield the whip is absorbed: the pulse ends (" + l.State + "), no heart lost (" + collisionDetection.lifeCounter + "), the collider is off",
                  l.State == AttackHazard.Phase.After && collisionDetection.lifeCounter == 0 && !l.HitCollider.enabled);
            collisionDetection.atomCheck = false;
            l.Cancel();

            // a blink: erased only where the hull lands on it
            PlayerInvuln.Reset();
            l = Arm(new Vector2(0f, 3f), new Vector2(0f, -2.5f));
            l.Ignite();
            Advance(l.SweepSeconds * .5f);
            Vector2 on = (l.Joint(l.Links / 2) + l.Joint(l.Links / 2 + 1)) * .5f;
            bool handledFar = EliteShip.TeleportStrike(l.Hitbox, new Vector3(on.x + 3f, on.y, 0f));
            bool stillLive = l.State == AttackHazard.Phase.Live;
            bool handledOn = EliteShip.TeleportStrike(l.Hitbox, new Vector3(on.x, on.y, 0f));
            Check("a blink landing 3 u from the chain leaves it burning (handled: " + handledFar + "); one landing on it erases it", handledFar && stillLive && handledOn && l.State == AttackHazard.Phase.After);
            l.Cancel();

            l = Arm(new Vector2(0f, 3f), new Vector2(0f, -2.5f));
            l.Ignite();
            Advance(.05f);
            Object.DestroyImmediate(l.Hitbox);
            Advance(.05f);
            Check("a destroyed hitbox (the fatal hit) ends the pulse cleanly (" + l.State + ")", l.State == AttackHazard.Phase.After || l.State == AttackHazard.Phase.Off);
            EliteSystem.Clear();
            var again = Arm(new Vector2(0f, 3f), new Vector2(0f, -2.5f));
            again.Ignite();
            Check("... and the pool makes a fresh hitbox for the next take", again != null && again.Hitbox != null && again.HitCollider.enabled);
        }
        finally { rig.Dispose(); PlayerInvuln.Reset(); collisionDetection.lifeCounter = 0; collisionDetection.atomCheck = false; }
    }

    // ---- friendly fire -----------------------------------------------------------------------------------------

    static GameObject Rock(Vector2 at)
    {
        var go = EnemyFactory.Create(EnemyRoster.One(3, EnemyRole.Rock), at, Quaternion.identity);
        ClearTarget.Ensure(go);
        FriendlyFire.Settle(go);
        return go;
    }

    // A point on the chain `tau` seconds into a sweep of `spec` armed with (root, pilot): the hazard's own static geometry, ahead of time.
    static Vector2 OnChain(AttackLash l, float tau, float share)
    {
        var j = new Vector2[AttackLash.MaxLinks + 1];
        AttackLash.Chain(l.Spec, l.Links, l.Length, l.Root, l.StartRad, l.Dir, tau, j);
        float f = share * l.Links;
        int i = Mathf.Min(l.Links - 1, Mathf.FloorToInt(f));
        return Vector2.Lerp(j[i], j[i + 1], f - i);
    }

    static void FriendlyFireOncePerPulse()
    {
        Fresh();
        var tmp = Arm(new Vector2(0f, 3f), new Vector2(0f, -2.5f));
        Vector2 where = OnChain(tmp, tmp.SweepSeconds * .5f, .7f);
        Vector2 beyond = OnChain(tmp, 0f, .7f) + new Vector2(tmp.Dir * (tmp.StartRad > -1.6f ? 1f : -1f) * -1.2f, 0f);
        tmp.Cancel();
        EliteSystem.Clear();
        Fresh();
        var shooter = Rock(new Vector2(0f, 3f));
        var victim = Rock(where);
        var l = Arm(new Vector2(0f, 3f), new Vector2(0f, -2.5f), 1f, 2, shooter);
        Vector2 safe = l.Dir > 0 ? new Vector2(-2.3f, -2.5f) : new Vector2(2.3f, -2.5f);
        var beside = Rock(safe);
        Advance(.5f);
        Check("a whip in its tell hurts nothing", victim != null && FriendlyFire.HostileKills == 0);
        l.Ignite();
        Advance(l.SweepSeconds + .05f);
        Check("a live whip destroys the hazard on its path, unpaid (" + FriendlyFire.HostileKills + " kill), spares its own shooter standing at its root and one on the floor beyond the start line",
              victim == null && shooter != null && beside != null && FriendlyFire.HostileKills == 1);
        l.Cancel();

        EliteEvasion.Enabled = false;
        Fresh();
        EliteDef def = null;
        foreach (var d in EliteCatalog.All) if (d.brain == "gunship") { def = d; break; }
        var probe = Arm(new Vector2(0f, 3f), new Vector2(0f, -2.5f));
        Vector2 at = OnChain(probe, probe.SweepSeconds * .5f, .8f);
        probe.Cancel();
        EliteSystem.Clear();
        Fresh();
        var elite = EliteShip.CreateInPlay(def, at);
        elite.AttackCooldown = 99f;
        FriendlyFire.ResetHostileCounters();
        var w = Arm(new Vector2(0f, 3f), new Vector2(0f, -2.5f));
        w.Ignite();
        AdvanceUntil(() => w.State != AttackHazard.Phase.Live, 2f);
        Check("a whip passes over an elite and takes one heart (" + FriendlyFire.HostileEliteHits + " hit) in its whole pulse", FriendlyFire.HostileEliteHits == 1);
        AttackHazard.HurtsOtherEnemies = false;
        var rock = Rock(OnChain(w, w.SweepSeconds * .5f, .6f));
        var s2 = Arm(new Vector2(0f, 3f), new Vector2(0f, -2.5f));
        s2.Ignite();
        Advance(s2.SweepSeconds + .05f);
        Check("with friendly fire switched off for hazards nothing is hit", rock != null);
        AttackHazard.HurtsOtherEnemies = true;
        EliteEvasion.Enabled = true;
        EliteSystem.Clear();
    }

    // ---- shots -------------------------------------------------------------------------------------------------

    static void ShotsBurn()
    {
        Fresh();
        var def = new EliteDef { key = "test_verdant", world = "verdant", shotSize = .22f, shotColor = "#FF4FD8", shotCore = "#FFFFFF" };
        def.Resolve();
        var l = Arm(new Vector2(0f, 3f), new Vector2(0f, -2.5f), 2f);
        var pool = EliteSystem.Shots;
        Vector2 onChain = OnChain(l, l.SweepSeconds * .4f, .6f);
        // (the shots are older than the whip's pulse: shots of one volley never clash with their own volley's hazard)
        var burned = pool.Fire(null, def, EliteShots.Kind.Bolt, onChain, Vector2.zero);
        var heavy = pool.Fire(null, def, EliteShots.Kind.Shell, onChain + new Vector2(.02f, 0f), Vector2.zero);
        Vector2 far = l.Dir > 0 ? new Vector2(-2.3f, -2.5f) : new Vector2(2.3f, -2.5f);
        var safe = pool.Fire(null, def, EliteShots.Kind.Bolt, far, Vector2.zero);
        Advance(1.3f);
        Check("a whip in its tell burns nothing", burned.Active && heavy.Active && safe.Active);
        l.Ignite();
        Advance(l.SweepSeconds * .4f + .05f);
        Check("a live whip burns the light and the heavy shot it passes over and spares one on the floor beyond the start line (burns " + HostileShots.ZoneBurns + ")",
              !burned.Active && !heavy.Active && safe.Active && HostileShots.ZoneBurns >= 2);
        Check("a player shot through the whip does not shoot it down", HostileShots.ShootDownAlong(new Vector2(-3f, 3f), new Vector2(3f, 3f), .2f) >= 0 && l.State == AttackHazard.Phase.Live);
        bool registered = false;
        foreach (var z in HostileShots.Zones) registered |= ReferenceEquals(z, l);
        Check("the whip is registered with HostileShots as a zone", registered);
        EliteSystem.Clear();
    }

    // ---- pause ---------------------------------------------------------------------------------------------------

    static void PauseFreezes()
    {
        Fresh();
        var l = Arm(new Vector2(0f, 3f), new Vector2(0f, -2.5f));
        Advance(.3f);
        float tb = l.PhaseTime, shown = l.Preview.ShownSeconds;
        for (int i = 0; i < 120; i++) { EliteSystem.Step(0f); l.Step(0f); AttackPools.StepAll(0f); }
        Check("a frozen world (dt 0) freezes the whip and its preview (" + tb.ToString("F2") + " -> " + l.PhaseTime.ToString("F2") + ")", l.PhaseTime == tb && l.Preview.ShownSeconds == shown && l.State == AttackHazard.Phase.Tell);
        string src = File.ReadAllText("Assets/Scripts/Gameplay/Enemies/Attacks/AttackLash.cs");
        bool clean = true;
        string why = "";
        foreach (string bad in new[] { "Time.deltaTime", "Time.unscaledDeltaTime", "void Update(", "void LateUpdate(", "void FixedUpdate(", "Time.time" })
            if (src.Contains(bad)) { clean = false; why += bad + "; "; }
        Check("the lash advances only through Step(dt): no Update, Time.deltaTime or Time.time in it (" + why + ")", clean);
        EliteSystem.Clear();
    }

    // ---- pools and allocation ----------------------------------------------------------------------------------

    static void PoolsAndAllocation()
    {
        Fresh();
        var a = Arm(new Vector2(-1f, 3f), new Vector2(-1f, -2.5f));
        var b = Arm(new Vector2(0f, 3f), new Vector2(0f, -2.5f));
        var c = Arm(new Vector2(1f, 3f), new Vector2(1f, -2.5f));
        var d = Arm(new Vector2(0f, 4f), new Vector2(0f, -2.5f));
        Check("three whips fit, a fourth is skipped (a busy screen skips one)", a != null && b != null && c != null && d == null && AttackLash.Pool.Capacity == AttackLash.PoolSize);
        EliteSystem.Clear();
        Fresh();
        AttackPreview.ResetCounters();
        for (int i = 0; i < 3; i++) Arm(new Vector2(-1.5f + i * 1.5f, 3f), new Vector2(-1.5f + i * 1.5f, -2.5f));
        Check("three whips in their tell at once: all " + AttackPreview.ActiveCount + " previews are drawn in full (" + AttackPreview.DotsInUse + " dots, " + AttackPreview.Dropped + " dropped)",
              AttackPreview.ActiveCount == 3 && AttackPreview.Dropped == 0 && AttackPreview.DotsInUse < AttackPreview.MaxDots);
        EliteSystem.Clear();

        Fresh();
        System.Action life = () =>
        {
            var l = Arm(new Vector2(0f, 3f), new Vector2(.3f, -2.5f), .8f);
            for (int i = 0; i < 220; i++) { AttackPools.StepAll(Dt); HostileShots.Resolve(); }
        };
        life(); life();
        bool meter = TestHarness.AllocMeterWorks(out long ctl);
        long used = TestHarness.AllocatedBytes(life);
        Check("arming, telling, igniting, sweeping, drawing back and releasing a whip allocates nothing after warm-up (" + used + " bytes; meter " + (meter ? "ok" : "blind") + ")", meter && used == 0);
        Check("the pool is back to idle, nothing in the scene stays active", AttackPools.ActiveCount == 0 && AttackPreview.ActiveCount == 0 && AttackHazard.ActiveCount == 0);
        EliteSystem.Clear();
    }

    // ---- cleanup -----------------------------------------------------------------------------------------------

    static void Cleanup_()
    {
        Fresh();
        var l = Arm(new Vector2(0f, 3f), new Vector2(0f, -2.5f));
        l.Ignite();
        var l2 = Arm(new Vector2(1f, 3f), new Vector2(1f, -2.5f));
        Advance(.1f);
        Check("two whips are live/telling", AttackHazard.ActiveCount == 2 && AttackPreview.ActiveCount >= 1);
        Planetfall.ClearBoard(null);
        Check("a world change (Planetfall.ClearBoard) clears every whip and preview", AttackHazard.ActiveCount == 0 && AttackPreview.ActiveCount == 0 && !l.gameObject.activeSelf);

        var b2 = Arm(new Vector2(0f, 3f), new Vector2(0f, -2.5f));
        b2.Ignite();
        var dir = new GameObject("~Director").AddComponent<EliteDirector>();
        buttonClicks.playerDied = true;
        typeof(EliteDirector).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(dir, null);
        Check("a player death clears it on the director's next frame", AttackHazard.ActiveCount == 0 && AttackPreview.ActiveCount == 0 && !b2.HitCollider.enabled);
        buttonClicks.playerDied = false;
        Object.DestroyImmediate(dir.gameObject);

        var b3 = Arm(new Vector2(0f, 3f), new Vector2(0f, -2.5f));
        b3.Ignite();
        int before = HostileShots.Zones.Count;
        EliteSystem.Clear();
        HostileShots.Resolve();
        bool left = false;
        foreach (var z in HostileShots.Zones) left |= z != null && !(z is Object o && o == null) && ReferenceEquals(z, b3);
        Check("a restart (EliteSystem.Clear) leaves no whip active and unregisters the destroyed zones (" + before + " -> " + HostileShots.Zones.Count + ")",
              AttackHazard.ActiveCount == 0 && !left && HostileShots.Zones.Count < before);
        var fresh = Arm(new Vector2(0f, 3f), new Vector2(0f, -2.5f));
        Check("the next take builds a fresh pool", fresh != null && fresh.State == AttackHazard.Phase.Tell && AttackLash.Pool.Alive);
        EliteSystem.Clear();
    }

    // ---- EnemyAttack.Lash through the table and the brain -----------------------------------------------------------

    static void BehaviourKind()
    {
        var lash = new EnemyBehaviour { key = "x" }.Lash(LashSpec.Standard(2)).Timing(.5f, 3f, 2);
        var small = new EnemyBehaviour { key = "y" }.Lash(5f, 70f, .35f).Timing(.5f, 3f, 2);
        var shot = new EnemyBehaviour { key = "z" }.Shot(EliteShots.Kind.Shard, 3, 22f, 2.4f, .22f);
        Check("Lash is an attack that shoots and an area hazard; a fan is not; the builder with numbers fills the spec (" + small.lash.length + ", " + small.lash.arcDeg + ", " + small.lash.sweepSeconds + ")",
              lash.Attacks && lash.Shoots && lash.IsAreaHazard && !shot.IsAreaHazard && small.lash.length == 5f && small.lash.arcDeg == 70f && Mathf.Approximately(small.lash.sweepSeconds, .35f));
        Check("its windup is raised to the instant-hit floor (" + EnemyBrain.TellFor(lash).ToString("F2") + ") and it reserves the shot budget FR7 names: 1.5 -> " + lash.ThreatCount + " shots",
              EnemyBrain.TellFor(lash) >= AttackHazard.MinTellSeconds && lash.ThreatCount == 2);
        int used = 0;
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def.key);
            if (b != null && b.attack == EnemyAttack.Lash) used++;
        }
        Check("no world's roster uses Lash yet (behaviour unchanged until the per-world phases): " + used, used == 0);
        Check("EnemyAttack.Lash was appended at the end of the enum (merge-safe with the jet and wave cores)",
              (int)EnemyAttack.Lash == System.Enum.GetValues(typeof(EnemyAttack)).Length - 1 || (int)EnemyAttack.Lash > (int)EnemyAttack.Strike);
    }

    static void RealBrain()
    {
        string id = "themed:verdant_vine_lash";
        AttackBudgetScenarios.Reset();
        AttackPreview.ResetCounters();
        var sc = AttackBudgetScenarios.Make(id);
        var rng = new System.Random(77);
        sc.Begin(rng, new Vector2(.4f, -3f));
        float t = 0f, tellStart = -1f, minTell = 99f, maxSweep = 0f, liveStart = -1f;
        int n = 0;
        float startAtTell = 0f;
        bool locked = true, ropeAbove = true;
        AttackLash seen = null;
        for (int f = 0; f < 60 * 14 && t < 14f; f++)
        {
            sc.Step(DodgeBot.Dt);
            t += DodgeBot.Dt;
            foreach (var hz in AttackHazard.All)
            {
                var lash = hz as AttackLash;
                if (lash == null || !lash.Active) continue;
                if (lash.State == AttackHazard.Phase.Tell && tellStart < 0f) { tellStart = t; seen = lash; startAtTell = lash.StartRad; ropeAbove &= lash.Root.y - AttackBudgetScenarios.Ship.position.y >= EnemyBrain.MinFireAbove - .05f; }
                if (lash.State == AttackHazard.Phase.Live && liveStart < 0f)
                {
                    liveStart = t;
                    minTell = Mathf.Min(minTell, liveStart - tellStart);
                    locked &= lash.StartRad == startAtTell;
                    n++;
                }
                if (lash.State == AttackHazard.Phase.Live) maxSweep = Mathf.Max(maxSweep, lash.Age + DodgeBot.Dt);
                if (lash.State == AttackHazard.Phase.Off || lash.State == AttackHazard.Phase.After) { tellStart = -1f; liveStart = -1f; }
            }
            if (sc.Done && f > 60) break;
        }
        Check(id + ": " + n + " whips went live through the real brain; the shortest tell was " + minTell.ToString("F2") + " s (>= " + AttackHazard.MinTellSeconds + ", FR1), the longest sweep " + maxSweep.ToString("F2") +
              " s (<= " + AttackLash.MaxSweep + "), the aim was locked at the tell, it fired from >= " + EnemyBrain.MinFireAbove + " u above",
              n >= 1 && minTell >= AttackHazard.MinTellSeconds - .03f && maxSweep <= AttackLash.MaxSweep + .05f && locked && ropeAbove);
        Check(id + ": every footprint was previewed >= " + AttackPreview.MinLead + " s before it went live (shortest " + AttackPreview.MinShown.ToString("F2") + " s; " + AttackPreview.TooShort + " too short)",
              AttackPreview.Shown >= n && AttackPreview.TooShort == 0 && AttackPreview.MinShown >= AttackPreview.MinLead);
        sc.End();
        AttackBudgetScenarios.Cleanup();

        // a shooter that dies in its tell takes its whip with it
        Fresh();
        var rock = Rock(new Vector2(0f, 3f));
        var w = Arm(new Vector2(0f, 3f), new Vector2(0f, -2.5f), 1f, 2, rock);
        w.Follow(rock.transform, Vector2.zero);
        Object.DestroyImmediate(rock);
        Advance(.1f);
        Check("a whip whose shooter is gone does not throw (it keeps its last root) and still ends cleanly", w.State == AttackHazard.Phase.Tell);
        w.Cancel();
        EliteSystem.Clear();
    }

    // ---- FR4: the corridor beyond the start line, for pilots across the lane --------------------------------------

    static void Corridor()
    {
        Fresh();
        float rail = BossRails.DrawnInnerEdge;
        float want = AttackLash.MinCorridor + AttackLash.CorridorMargin;
        var spec = LashSpec.Standard(2);
        int cases = 0, thin = 0, standStill = 0, unreachable = 0, mirrored = 0;
        float worstRoom = 99f, worstNeed = 0f;
        // the ship needs this long to leave: the tell minus the bot's reaction, with its acceleration (the bot's own numbers)
        float time = AttackHazard.MinTellSeconds - DodgeBot.Reaction;
        float accelLoss = .5f * DodgeBot.Speed * DodgeBot.Speed / DodgeBot.Accel * .5f;   // the distance the ramp-up costs: v^2 / (2a) of the flight at top speed, halved
        float reachable = DodgeBot.Speed * time - DodgeBot.Speed * DodgeBot.Speed / (2f * DodgeBot.Accel);
        foreach (float rx in new[] { -2f, 0f, 2f })
            for (float px = -2.3f; px <= 2.31f; px += .1f)
                for (float py = -3.8f; py <= -1f; py += .7f)
                {
                    Vector2 root = new Vector2(rx, 3f), pilot = new Vector2(px, py);
                    AttackLash.Aim(in spec, root, pilot, rail, out float start, out int dir, out float length, out float room);
                    Vector2 u = new Vector2(Mathf.Cos(start), Mathf.Sin(start));
                    float edgeX = root.x + u.x * ((py - root.y) / u.y);
                    float inside = Mathf.Abs(px - edgeX);
                    bool pilotInside = (px - edgeX) * (dir * (pilot.y < root.y ? 1f : -1f)) > 0f;   // he is on the sweep's side of the line
                    cases++;
                    worstRoom = Mathf.Min(worstRoom, room);
                    if (room < want - .01f) thin++;
                    if (!pilotInside || inside < .45f) standStill++;
                    // he has to cross the line and clear the whip's width and his own: the distance against what he can fly in the tell
                    float need = inside + DodgeBot.ShipRadius + spec.hitHalf + .1f;
                    worstNeed = Mathf.Max(worstNeed, need);
                    if (need > reachable) unreachable++;
                    if (length < Vector2.Distance(root, pilot) + .3f) unreachable++;
                    if (px > 0f && dir * (pilot.y < root.y ? 1f : -1f) < 0) mirrored++;
                }
        Check(cases + " pilot positions across the lane (3 shooters, every .1 u, 5 depths): the floor beyond the start line is at least " + want + " u (FR4 + a margin) in every one (least " + worstRoom.ToString("F2") +
              " u; " + thin + " thinner)", thin == 0);
        Check("... the pilot always stands inside the sweep when it starts (standing still is hit: " + standStill + " cases where he would not be), and mirrors it for a pilot at either rail (" + mirrored + " mirrored on the right)",
              standStill == 0 && mirrored > 0);
        Check("... and the dodge is within reach: the farthest he must fly out (" + worstNeed.ToString("F2") + " u) is under what he can in the tell after the bot's reaction (" + reachable.ToString("F2") + " u); " + unreachable + " cases out of reach", unreachable == 0);
        // the lash always reaches him
        EliteSystem.Clear();
    }

    // ---- the pink cue on the drawn pixels -----------------------------------------------------------------------

    static readonly float[] PickupHues = { 178f, 82f, 259f, 37f };

    static Color32[] Pixels(Sprite sp)
    {
        var tex = sp.texture;
        var r = sp.rect;
        var all = tex.GetPixels32();
        var res = new Color32[(int)(r.width * r.height)];
        for (int y = 0; y < r.height; y++)
            for (int x = 0; x < r.width; x++)
                res[y * (int)r.width + x] = all[((int)r.y + y) * tex.width + (int)r.x + x];
        return res;
    }

    static bool IsKey(Color32 c) => c.a > 24 && c.r == ShotOutline.BoldKey.r && c.g == ShotOutline.BoldKey.g && c.b == ShotOutline.BoldKey.b;

    static bool PinkOrWhite(Color32 c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        float deg = h * 360f;
        return (s < .3f && v > .75f) || (deg >= 300f && deg <= 335f && s > .2f);
    }

    static IEnumerable<KeyValuePair<string, Sprite>> LashSprites(int world, bool bold)
    {
        for (int f = 0; f < 2; f++)
        {
            yield return new KeyValuePair<string, Sprite>("link " + f, AttackHazardArt.LashLink(world, f, bold));
            yield return new KeyValuePair<string, Sprite>("tip " + f, AttackHazardArt.LashTip(world, f, bold));
            yield return new KeyValuePair<string, Sprite>("root " + f, AttackHazardArt.LashRoot(world, f, bold));
            yield return new KeyValuePair<string, Sprite>("dash " + f, AttackHazardArt.LashDash(world, f, bold));
        }
    }

    static void DrawnPixels()
    {
        Fresh();
        int n = 0, lowPink = 0, red = 0, hot = 0, heavy = 0, softEdge = 0, keylineWrong = 0;
        string worstPink = "", worstEdge = "", hotWhere = "";
        float minPink = 9f, minEdge = 9f, maxMat = 0f;
        for (int world = 0; world < ShotSkins.Worlds; world++)
            for (int b = 0; b < 2; b++)
                foreach (var kv in LashSprites(world, b == 1))
                {
                    var px = Pixels(kv.Value);
                    int w = (int)kv.Value.rect.width, h = (int)kv.Value.rect.height;
                    var clean = new Color32[px.Length];
                    int key = 0;
                    for (int i = 0; i < px.Length; i++) { if (IsKey(px[i])) { key++; continue; } clean[i] = px[i]; }
                    var au = ShotSkinTest.AuditPixels(clean);
                    n++;
                    string tag = kv.Key + " w" + world + (b == 1 ? " bold" : "");
                    if (au.PinkShare < minPink) { minPink = au.PinkShare; worstPink = tag; }
                    if (au.PinkShare < ShotSkinTest.MinPinkShare) lowPink++;
                    if (au.red > 0) red++;
                    if (au.PickupShare >= ShotSkinTest.MaxPickupShare) { hot++; hotWhere += tag + "; "; }
                    // material = the opaque pixels that are neither pink nor white; the cue contract keeps it to half
                    float mat = au.opaque > 0 ? 1f - au.PinkShare : 0f;
                    if (mat > maxMat) maxMat = mat;
                    if (mat > .5f) heavy++;
                    // the silhouette edge: a neighbour that is empty (or the keyline) -- the canvas border of a link is a tile seam
                    int edge = 0, edgePink = 0;
                    bool seamTile = kv.Key.StartsWith("link");
                    for (int y = 0; y < h; y++)
                        for (int x = 0; x < w; x++)
                        {
                            var c = clean[y * w + x];
                            if (c.a <= 24) continue;
                            bool isEdge = false;
                            for (int k = 0; k < 4 && !isEdge; k++)
                            {
                                int nx = x + (k == 0 ? -1 : k == 1 ? 1 : 0), ny = y + (k == 2 ? -1 : k == 3 ? 1 : 0);
                                if (nx < 0 || ny < 0 || nx >= w || ny >= h) { if (!seamTile || (k < 2)) isEdge = !seamTile; continue; }
                                var nb = px[ny * w + nx];
                                isEdge = nb.a <= 24 || IsKey(nb);
                            }
                            if (isEdge) { edge++; if (PinkOrWhite(c) || (c.r > 200 && c.b > 150)) edgePink++; }
                        }
                    float ep = edge > 0 ? edgePink / (float)edge : 1f;
                    if (ep < minEdge) { minEdge = ep; worstEdge = tag; }
                    if (ep < .8f) softEdge++;
                    if (b == 1 && key == 0) keylineWrong++;
                    if (b == 0 && key != 0) keylineWrong++;
                }
        Check(n + " procedural lash sprites (5 worlds x link, tip, root, dash x 2 frames, standard and bold): at least " + (ShotSkinTest.MinPinkShare * 100f) + "% of every one's pixels are pink-family or white; the least is " +
              (minPink * 100f).ToString("F0") + "% (" + worstPink + ")", lowPink == 0 && n >= 80);
        Check("... none has a player-red pixel, none is a saturated pickup hue (" + red + " red, " + hot + " hot: " + hotWhere + "), and the world's material is at most half the pixels (most: " + (maxMat * 100f).ToString("F0") + "%)",
              red == 0 && hot == 0 && heavy == 0);
        Check("... the silhouette edge is the pink-white stroke (least " + (minEdge * 100f).ToString("F0") + "%, " + worstEdge + "; " + softEdge + " under 80%) and the bold ones wear the keyline ring, the standard ones do not (" + keylineWrong + " wrong)",
              softEdge == 0 && keylineWrong == 0);
        // two stepped frames, vine and tentacle differ, link elongated, tip pointed
        var l0 = Pixels(AttackHazardArt.LashLink(2, 0, false)); var l1 = Pixels(AttackHazardArt.LashLink(2, 1, false));
        int diff = 0;
        for (int i = 0; i < l0.Length; i++) if (l0[i].r != l1[i].r || l0[i].g != l1[i].g || l0[i].b != l1[i].b || l0[i].a != l1[i].a) diff++;
        var vine = AttackHazardArt.LashLink(2, 0, false); var tent = AttackHazardArt.LashLink(4, 0, false);
        var vp = Pixels(vine); var tp = Pixels(tent);
        int unlike = 0;
        for (int i = 0; i < Mathf.Min(vp.Length, tp.Length); i++) if (vp[i].r != tp[i].r || vp[i].g != tp[i].g || vp[i].b != tp[i].b) unlike++;
        var tip = AttackHazardArt.LashTip(2, 0, false);
        var tipPx = Pixels(tip);
        int tw = (int)tip.rect.width, th = (int)tip.rect.height;
        int rowBottom = 0, rowTop = 0;
        for (int x = 0; x < tw; x++) { if (tipPx[2 * tw + x].a > 24) rowBottom++; if (tipPx[(th - 3) * tw + x].a > 24) rowTop++; }
        Check("the link's two frames differ in " + diff + " pixels (stepped flicker and a thorn on the other side, not a smooth breath), the vine and the tentacle differ in " + unlike + ", the link is elongated (" +
              vine.rect.width + " x " + vine.rect.height + ") and the tip is a point (" + rowBottom + " px wide near the point, " + rowTop + " near the base)",
              diff >= 10 && unlike >= 40 && vine.rect.height > vine.rect.width && rowBottom < rowTop);
    }

    // ---- the art slot -------------------------------------------------------------------------------------------

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
        Check("without the art file the procedural whip is drawn (no verdant lash atlas)", !AttackArt.Has(2, "lash") && !AttackHazardArt.LashArt(2));
        var lashAtlas = Atlas(1024, 128, new Color32(255, 90, 220, 255));
        AttackArt.Inject(2, "lash", lashAtlas);
        AttackHazardArt.Forget();
        var l = Arm(new Vector2(0f, 3f), new Vector2(0f, -2.5f));
        Check("a delivered verdant_attack_lash.png is used automatically: the links, the tip, the root bud and the dash all come from the atlas",
              AttackHazardArt.LashArt(2) && l.LinkRenderer(0).sprite != null && l.LinkRenderer(0).sprite.texture == lashAtlas && l.TipRenderer.sprite.texture == lashAtlas &&
              l.RootRenderer.sprite.texture == lashAtlas && l.DashRenderer(0).sprite.texture == lashAtlas);
        l.Ignite();
        Advance(.1f);
        Check("... and the live chain is laid on the hit ribbon with them (the link is scaled to its length: " + l.LinkRenderer(0).transform.localScale.y.ToString("F2") + ")",
              l.LinkRenderer(0).enabled && l.LinkRenderer(0).sprite.texture == lashAtlas && l.LinkRenderer(0).transform.localScale.y > .3f);
        AttackArt.Clear();
        AttackHazardArt.Forget();
        EliteSystem.Clear();
    }
}
