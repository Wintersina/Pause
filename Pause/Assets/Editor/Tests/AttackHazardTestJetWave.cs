using System.IO;
using System.Reflection;
using UnityEngine;
using static AttackTestKit;

// AttackHazardTest, the jet (plan phase 1a AttackJet) and the wave (phase 1b AttackWave):
//
//   * geometry: the jet is a trapezoid from the nozzle to its length (a cone widens, a column does not), reaches its locked
//     target, is locked at the tell, sweeps toward the lane's middle at a capped tip speed; the wave is two rectangles
//     either side of a gap >= 1.4 u that sits inside the rails, aimed at the pilot at the tell, falling at a capped speed
//     from a height at least .8 s above the pilot's row
//   * life: Tell (harmless, previewed) -> Live (<= .6 s for a jet) -> After -> back in the pool; ignites itself when its tell is up
//   * the hit: the trigger collider is the shape; heart / shield / blink rules; friendly fire once a pulse, never the shooter;
//     crossing shots burn; pause freezes it; pools hold their size and allocate nothing; world change / death / restart clear it
public static partial class AttackHazardTest
{
    static void JetWaveSuite()
    {
        JetGeometry();
        JetSweep();
        JetLife();
        WaveGeometry();
        WaveLife();
        JetWaveHitbox();
        JetWaveHitRules();
        JetWaveFriendlyFire();
        JetWaveShotsBurn();
        JetWavePauseAndPools();
        JetWaveCleanup();
        JetWaveBehaviourKinds();
    }

    static AttackJet ArmJet(JetSpec spec, Vector2 muzzle, Vector2 target, float tell = 1f, GameObject by = null) => AttackJet.Arm(spec, muzzle, target, tell, by);
    static AttackWave ArmWave(WaveSpec spec, Vector2 muzzle, Vector2 target, float tell = 1f, GameObject by = null) => AttackWave.Arm(spec, muzzle, target, tell, by);

    // ---- the jet ------------------------------------------------------------------------------

    static void JetGeometry()
    {
        Fresh();
        var spec = JetSpec.Flame(3);
        Vector2 muzzle = new Vector2(0f, 2f), target = new Vector2(0f, -1.2f);
        var j = ArmJet(spec, muzzle, target);
        float expectLen = Vector2.Distance(muzzle, target) + AttackJet.ReachPast;
        float expectTip = spec.baseHalf + (spec.tipHalf - spec.baseHalf) * expectLen / spec.length;
        Check("a flame in its tell: one hit trapezoid (one polygon, its own outline + the sweep's end footprint + the arc = " + j.Footprint.LoopCount + " loops), previewed from the first frame, " +
              "harmless (collider off, zone not live), the nozzle's flare shows and the body does not",
              j.State == AttackHazard.Phase.Tell && j.Footprint.PolyCount == 1 && j.Footprint.LoopCount == 3 && j.Preview != null && j.Preview.Active && j.Preview.DotCount > 30 &&
              !j.ZoneLive && !j.HitCollider.enabled && j.NozzleRenderer.enabled && !j.BodyRenderer.enabled && j.TellSeconds >= AttackHazard.MinTellSeconds);
        Check("it reaches its locked target (" + Vector2.Distance(muzzle, target).ToString("F1") + " u away): length " + j.Length.ToString("F2") + " (expected " + expectLen.ToString("F2") + "), " +
              "the cone widens in proportion to " + j.TipHalf.ToString("F2") + " (expected " + expectTip.ToString("F2") + ")", Mathf.Abs(j.Length - expectLen) < .01f && Mathf.Abs(j.TipHalf - expectTip) < .01f);
        float aimDeg = -90f;
        Check("the direction is aimed at the pilot when the tell starts (" + (j.AimDirection * Mathf.Rad2Deg).ToString("F1") + " deg)", Mathf.Abs(Mathf.DeltaAngle(j.AimDirection * Mathf.Rad2Deg, aimDeg)) < 2f);
        Pilot.position = new Vector3(2f, -3f, 0f);
        Advance(.3f);
        Check("... and it stays there when the pilot moves afterwards (aim locked at the tell)", Mathf.Abs(Mathf.DeltaAngle(j.AimDirection * Mathf.Rad2Deg, aimDeg)) < 2f);
        j.Ignite();
        Check("ignited: live, the collider is on, the zone is live, the body is drawn", j.State == AttackHazard.Phase.Live && j.ZoneLive && j.HitCollider.enabled && j.BodyRenderer.enabled);
        var f = j.Footprint;
        Vector2 o = j.Origin, u = AttackJet.Dir(j.Direction), n = new Vector2(-u.y, u.x);
        float L = j.Length, mid = Mathf.Lerp(j.BaseHalf, j.TipHalf, .5f);
        Check("the jet is hit along its axis to the far end (" + L.ToString("F2") + " u) and not beyond, nor behind the nozzle",
              f.Touches(o + u * (L - .05f), 0f) && f.Touches(o + u * .1f, 0f) && !f.Touches(o + u * (L + .12f), 0f) && !f.Touches(o - u * .25f, 0f));
        Vector2 m = o + u * (L * .5f);
        Check("halfway down it is " + (mid * 2f).ToString("F2") + " wide: hit at the edge, clear beside it (the ship's circle r .28 included)",
              f.Touches(m + n * (mid - .02f), 0f) && !f.Touches(m + n * (mid + .03f), 0f) && f.Touches(m + n * (mid + .26f), .28f) && !f.Touches(m + n * (mid + .31f), .28f) &&
              f.Touches(m - n * (mid - .02f), 0f) && !f.Touches(m - n * (mid + .03f), 0f));
        Vector2 tipEdge = o + u * (L - .02f);
        Check("at the far end it is the full tip width (" + (j.TipHalf * 2f).ToString("F2") + "): hit at its edge, clear just past it",
              f.Touches(tipEdge + n * (j.TipHalf - .03f), 0f) && !f.Touches(tipEdge + n * (j.TipHalf + .06f), 0f));
        j.Cancel();
        Check("cancelled: gone at once, preview and collider off, back in the pool", j.State == AttackHazard.Phase.Off && AttackJet.Pool.ActiveCount == 0 && AttackPreview.ActiveCount == 0 && !j.HitCollider.enabled);
        // a column does not widen; a fixed-length spec does not reach
        Pilot.position = new Vector3(0f, -3f, 0f);
        var col = ArmJet(JetSpec.Pressure(4), muzzle, target);
        Check("a column keeps its width (" + col.BaseHalf.ToString("F2") + " = " + col.TipHalf.ToString("F2") + ") and reaches too (" + col.Length.ToString("F2") + " u)",
              Mathf.Approximately(col.BaseHalf, col.TipHalf) && col.Length > 3.5f && !col.Spec.IsCone);
        col.Cancel();
        var fixedSpec = JetSpec.Lance(0);
        fixedSpec.maxLength = 0f;
        var lance = ArmJet(fixedSpec, muzzle, target);
        Check("a spec without maxLength keeps its " + fixedSpec.length + " u (" + lance.Length.ToString("F2") + ")", Mathf.Abs(lance.Length - 1.8f) < .001f);
        lance.Cancel();
        var far = ArmJet(JetSpec.Flame(3), muzzle, new Vector2(0f, -30f));
        Check("a far target is reached only up to maxLength (" + far.Length.ToString("F2") + " <= " + JetSpec.Flame(3).maxLength + ")", far.Length <= JetSpec.Flame(3).maxLength + .001f);
        far.Cancel();
        // aimed sideways it is held to 60 deg of straight down
        var side = ArmJet(JetSpec.Flame(3), muzzle, new Vector2(8f, 1.9f));
        Check("a target level with the nozzle is aimed at most " + AttackJet.MaxAimDeg + " deg from straight down (" + Mathf.Abs(Mathf.DeltaAngle(side.AimDirection * Mathf.Rad2Deg, -90f)).ToString("F0") + ")",
              Mathf.Abs(Mathf.DeltaAngle(side.AimDirection * Mathf.Rad2Deg, -90f)) <= AttackJet.MaxAimDeg + .1f);
        side.Cancel();
        EliteSystem.Clear();
    }

    static void JetSweep()
    {
        Fresh();
        var spec = JetSpec.Flame(3);
        Vector2 muzzle = new Vector2(1.5f, 2f), target = new Vector2(1.5f, -1.2f);
        var j = ArmJet(spec, muzzle, target, .8f);
        float total = Mathf.Abs(j.SweepRadians) * Mathf.Rad2Deg;
        Vector2 startTip = j.Origin + AttackJet.Dir(j.StartRad) * j.Length, endTip = j.Origin + AttackJet.Dir(j.StartRad + j.SweepRadians) * j.Length;
        Check("a flame swept " + spec.sweepDeg + " deg turns " + total.ToString("F1") + " deg, toward the lane's middle (the tip goes from x " + startTip.x.ToString("F2") + " to " + endTip.x.ToString("F2") + ")",
              Mathf.Abs(total - spec.sweepDeg) < .1f && Mathf.Abs(endTip.x) < Mathf.Abs(startTip.x));
        Check("the tell shows the end footprint and the arc too (3 outlines)", j.Footprint.LoopCount == 3);
        j.Ignite();
        Vector2 tip0 = j.Origin + AttackJet.Dir(j.Direction) * j.Length;
        Advance(.1f);
        Vector2 tip1 = j.Origin + AttackJet.Dir(j.Direction) * j.Length;
        float speed = Vector2.Distance(tip0, tip1) / .1f;
        Check("the far end moves at " + speed.ToString("F2") + " u/s while it sweeps (<= " + AttackJet.MaxTipSpeed + ")", speed <= AttackJet.MaxTipSpeed + .05f && speed > .1f);
        Advance(.6f);
        Check("the jet ended on the sweep's end footprint (" + Mathf.Abs(Mathf.DeltaAngle(j.DirectionAt(j.LiveSeconds) * Mathf.Rad2Deg, (j.StartRad + j.SweepRadians) * Mathf.Rad2Deg)).ToString("F2") + " deg off)",
              Mathf.Abs(Mathf.DeltaAngle(j.DirectionAt(j.LiveSeconds) * Mathf.Rad2Deg, (j.StartRad + j.SweepRadians) * Mathf.Rad2Deg)) < .01f);
        EliteSystem.Clear();

        // a boss's flame sweep asks for a lot: the tip speed cap wins
        Fresh();
        var wild = JetSpec.Flame(3);
        wild.sweepDeg = 90f; wild.centered = true;
        var w = ArmJet(wild, new Vector2(0f, 2f), new Vector2(0f, -1.2f), 1f);
        float cap = AttackJet.MaxTipSpeed * w.LiveSeconds / w.Length * Mathf.Rad2Deg;
        Check("a 90 deg sweep is held to " + (Mathf.Abs(w.SweepRadians) * Mathf.Rad2Deg).ToString("F1") + " deg so the far end stays under " + AttackJet.MaxTipSpeed + " u/s (cap " + cap.ToString("F1") + ")",
              Mathf.Abs(w.SweepRadians) * Mathf.Rad2Deg <= cap + .1f);
        Check("a centred sweep is symmetric about the aim (starts " + ((w.StartRad - w.AimDirection) * Mathf.Rad2Deg).ToString("F1") + " deg from it, ends " + ((w.DirectionAt(w.LiveSeconds) - w.AimDirection) * Mathf.Rad2Deg).ToString("F1") + ")",
              Mathf.Abs((w.StartRad - w.AimDirection) + (w.DirectionAt(w.LiveSeconds) - w.AimDirection)) < .001f);
        var still = ArmJet(JetSpec.Pressure(4), new Vector2(-1f, 2f), new Vector2(-1f, -1.2f), 1f);
        Check("a column (no sweep) does not turn", still.SweepRadians == 0f && still.Footprint.LoopCount == 1);
        EliteSystem.Clear();
    }

    static void JetLife()
    {
        Fresh();
        var spec = JetSpec.Flame(3);
        var j = ArmJet(spec, new Vector2(0f, 2f), new Vector2(0f, -1.2f), .2f);
        Check("a tell asked for in .2 s is raised to " + AttackHazard.MinTellSeconds + " s (FR1) and the jet ignites by itself when it is up", Mathf.Approximately(j.TellSeconds, AttackHazard.MinTellSeconds));
        Advance(.5f);
        Check("still telling at .5 s of .7: no hitbox, no body, the preview shows (" + j.Preview.ShownSeconds.ToString("F2") + " s)", j.State == AttackHazard.Phase.Tell && !j.HitCollider.enabled && !j.BodyRenderer.enabled && j.Preview.ShownSeconds > .45f);
        float told = AdvanceUntil(() => j.State != AttackHazard.Phase.Tell, 2f);
        Check("it went live " + (told + .5f).ToString("F2") + " s after it was armed", j.State == AttackHazard.Phase.Live && Mathf.Abs(told + .5f - AttackHazard.MinTellSeconds) < .05f);
        float live = AdvanceUntil(() => j.State != AttackHazard.Phase.Live, 2f);
        Check("it burned for " + live.ToString("F2") + " s (spec " + spec.liveSeconds + ", <= " + AttackJet.MaxLiveSeconds + ", FR3), then it flickers out, harmless",
              Mathf.Abs(live - spec.liveSeconds) < .06f && live <= AttackJet.MaxLiveSeconds + .03f && j.State == AttackHazard.Phase.After && !j.HitCollider.enabled && !j.ZoneLive);
        AdvanceUntil(() => j.State == AttackHazard.Phase.Off, 1f);
        Check("then it is back in the pool, every renderer off, no preview or dot left",
              j.State == AttackHazard.Phase.Off && AttackJet.Pool.ActiveCount == 0 && !j.gameObject.activeSelf && !j.BodyRenderer.enabled && !j.NozzleRenderer.enabled && AttackPreview.ActiveCount == 0 && AttackPreview.DotsInUse == 0);
        var longer = JetSpec.Flame(3);
        longer.liveSeconds = 5f;
        var k = ArmJet(longer, new Vector2(0f, 2f), new Vector2(0f, -1.2f), 1f);
        k.Ignite();
        float lv = AdvanceUntil(() => k.State != AttackHazard.Phase.Live, 3f);
        Check("a spec asking for 5 s of fire is held to " + AttackJet.MaxLiveSeconds + " s (" + lv.ToString("F2") + ")", lv <= AttackJet.MaxLiveSeconds + .03f);
        EliteSystem.Clear();
        // the shooter's nozzle: a jet rides it until it ignites
        Fresh();
        var holder = new GameObject("~Shooter").transform;
        holder.position = new Vector3(0f, 2f, 0f);
        var r = ArmJet(spec, holder.position, new Vector2(0f, -1.2f), 1f);
        r.Follow(holder, new Vector2(0f, -.3f));
        holder.position = new Vector3(1f, 2.4f, 0f);
        Advance(.1f);
        Check("a jet follows its shooter through the first of the tell (nozzle at " + r.Origin.ToString("F2") + ", shooter at (1.00, 2.40) + (0, -.3)), the direction stays locked, and the preview moved with it",
              Vector2.Distance(r.Origin, new Vector2(1f, 2.1f)) < .01f && Mathf.Abs(Mathf.DeltaAngle(r.AimDirection * Mathf.Rad2Deg, -90f)) < 2f);
        Advance(.4f);   // 1 s tell: .5 s in, .5 s left -- still following
        holder.position = new Vector3(1.5f, 2.4f, 0f);
        Advance(.08f);
        Vector2 atLock = r.Origin;
        Check("... it follows until " + AttackJet.FootprintLockSeconds + " s before ignition (nozzle " + r.Origin.ToString("F2") + " with " + r.TellLeft.ToString("F2") + " s left)", Vector2.Distance(atLock, new Vector2(1.5f, 2.1f)) < .01f || r.TellLeft <= AttackJet.FootprintLockSeconds + .02f);
        Advance(.2f);
        holder.position = new Vector3(-1.5f, 2.4f, 0f);
        Vector2 stillAt = r.Origin;
        float shown = r.Preview.ShownSeconds;
        Advance(.1f);
        Check("... then the footprint is still for the last " + AttackJet.FootprintLockSeconds + " s whatever the shooter does (nozzle " + r.Origin.ToString("F2") + " vs " + stillAt.ToString("F2") + "; " + r.TellLeft.ToString("F2") + " s left)",
              Vector2.Distance(r.Origin, stillAt) < .0001f && r.TellLeft < AttackJet.FootprintLockSeconds && shown >= AttackPreview.MinLead);
        r.Ignite();
        Vector2 igniteAt = r.Origin;
        holder.position = new Vector3(-1f, 2.4f, 0f);
        Advance(.1f);
        Check("... and stays where it ignited once it is live (" + r.Origin.ToString("F2") + ")", Vector2.Distance(r.Origin, igniteAt) < .01f);
        // ride: a hazard's jet falls with the board, a pilot's stays
        moveBackGround.speed = .4f;
        var rideSpec = JetSpec.Flame(3);
        rideSpec.ride = 1f;
        var riding = ArmJet(rideSpec, new Vector2(-2f, 3f), new Vector2(-2f, -1f), 1f);
        riding.Ignite();
        var held = ArmJet(JetSpec.Flame(3), new Vector2(2f, 3f), new Vector2(2f, -1f), 1f);
        held.Ignite();
        float y0 = riding.Origin.y, hy0 = held.Origin.y;
        Advance(.4f);
        Check("a jet with ride 1 falls with the board (" + (y0 - riding.Origin.y).ToString("F2") + " u in .4 s), one with ride 0 stays (" + (hy0 - held.Origin.y).ToString("F2") + ")",
              y0 - riding.Origin.y > .05f && Mathf.Abs(hy0 - held.Origin.y) < .001f);
        moveBackGround.speed = 0f;
        Object.DestroyImmediate(holder.gameObject);
        EliteSystem.Clear();
    }

    // ---- the wave -----------------------------------------------------------------------------

    static void WaveGeometry()
    {
        Fresh();
        float rail = BossRails.DrawnInnerEdge;
        var spec = WaveSpec.Surf(4);
        Vector2 muzzle = new Vector2(0f, 3f), pilot = new Vector2(1f, -3f);
        Pilot.position = pilot;
        var w = ArmWave(spec, muzzle, pilot);
        Check("a surf wave in its tell: two bars either side of a gap (two polygons, their outlines + the gap lane's = " + w.Footprint.LoopCount + " loops), previewed from the first frame, " +
              "harmless, the chevrons blink at the gap's edges, the band itself is not drawn yet",
              w.State == AttackHazard.Phase.Tell && w.Footprint.PolyCount == 2 && w.Footprint.LoopCount == 3 && w.Preview != null && w.Preview.Active && !w.ZoneLive && !w.HitCollider.enabled &&
              w.MarkerA.enabled && w.MarkerB.enabled && !w.LeftRenderer.enabled && !w.RightRenderer.enabled && w.TellSeconds >= AttackHazard.MinTellSeconds);
        Check("the gap is aimed at the pilot's x (" + w.GapX.ToString("F2") + " vs 1.00) and is " + w.GapWidth.ToString("F2") + " u wide (>= " + AttackWave.MinGap + ")", Mathf.Abs(w.GapX - 1f) < .001f && w.GapWidth >= AttackWave.MinGap);
        var f = w.Footprint;
        float y = w.Y;
        float gx = w.GapX, gh = w.GapWidth * .5f;
        Check("the band is hit across the lane either side of the gap, the ship (r .28) clear anywhere in the gap's middle and touching at its edges",
              f.Touches(new Vector2(-2f, y), 0f) && f.Touches(new Vector2(rail, y), 0f) && !f.Touches(new Vector2(gx, y), .28f) && !f.Touches(new Vector2(gx + gh - .3f, y), .28f) && !f.Touches(new Vector2(gx - gh + .3f, y), .28f) &&
              f.Touches(new Vector2(gx + gh - .2f, y), .28f) && f.Touches(new Vector2(gx - gh + .2f, y), .28f));
        Check("the band is " + (spec.hitHalf * 2f).ToString("F2") + " thick: hit at its edge, clear above and below it",
              f.Touches(new Vector2(-2f, y + spec.hitHalf - .01f), 0f) && !f.Touches(new Vector2(-2f, y + spec.hitHalf + .03f), 0f) && !f.Touches(new Vector2(-2f, y - spec.hitHalf - .03f), 0f));
        Check("it starts at the shooter's height (" + w.StartHeight.ToString("F2") + " vs " + muzzle.y + ") and at least " + AttackWave.MinFrontSeconds + " s of fall above the pilot's row",
              Mathf.Abs(w.StartHeight - muzzle.y) < .001f && (w.StartHeight - spec.hitHalf - pilot.y) / spec.speed >= AttackWave.MinFrontSeconds);
        // a shooter close above the pilot: the band starts higher, never closer than the front time
        var low = ArmWave(spec, new Vector2(0f, pilot.y + 1.7f), pilot, 1f);
        float front = (low.StartHeight - low.HitHalf - pilot.y) / spec.speed;
        Check("a shooter only 1.7 u above the pilot: the band starts at " + low.StartHeight.ToString("F2") + ", " + front.ToString("F2") + " s of fall from his row (>= " + AttackWave.MinFrontSeconds + ", FR3)",
              front >= AttackWave.MinFrontSeconds - .001f && low.StartHeight > pilot.y + 1.7f);
        low.Cancel();
        // the gap in the lane at the rail: it stays inside, never narrower
        var railed = ArmWave(spec, muzzle, new Vector2(rail - .1f, -3f), 1f);
        float inner = railed.GapX + railed.GapWidth * .5f;
        Check("a pilot at the rail: the gap (" + (railed.GapX - railed.GapWidth * .5f).ToString("F2") + " .. " + inner.ToString("F2") + ") stays inside the rails at " + rail.ToString("F2") + " and is still " + railed.GapWidth.ToString("F2") + " wide",
              inner <= rail && railed.GapWidth >= AttackWave.MinGap);
        railed.Cancel();
        // the gap's width and speed are held to the doc's numbers
        var wild = WaveSpec.Surf(4);
        wild.gapWidth = .5f; wild.speed = 9f;
        var wi = ArmWave(wild, muzzle, pilot, 1f);
        Check("a spec asking for a .5 u gap and 9 u/s is run at the " + AttackWave.MinGap + " u minimum and the " + AttackWave.MaxSpeed + " u/s cap (" + wi.GapWidth.ToString("F2") + ", " + wi.FallSpeed.ToString("F1") + ")",
              wi.GapWidth >= AttackWave.MinGap - .001f && wi.FallSpeed <= AttackWave.MaxSpeed + .001f);
        wi.Cancel();
        // the gap turned off the pilot toward the lane's middle
        var off = WaveSpec.Surf(4);
        off.gapOffset = 1f;
        var ow = ArmWave(off, muzzle, new Vector2(1.2f, -3f), 1f);
        var ow2 = ArmWave(off, muzzle, new Vector2(-1.5f, -3f), 1f);
        Check("a gap offset 1 u turns the gap toward the lane's middle (pilot 1.2 -> gap " + ow.GapX.ToString("F2") + ", pilot -1.5 -> " + ow2.GapX.ToString("F2") + "): standing still is not safe",
              Mathf.Abs(ow.GapX - .2f) < .001f && Mathf.Abs(ow2.GapX - -.5f) < .001f && ow.Footprint.Touches(new Vector2(1.2f, ow.Y), .28f));
        ow.Cancel(); ow2.Cancel();
        w.Cancel();
        Check("cancelled: gone at once, preview and collider off, back in the pool", w.State == AttackHazard.Phase.Off && AttackWave.Pool.ActiveCount == 0 && AttackPreview.ActiveCount == 0 && !w.HitCollider.enabled);
        EliteSystem.Clear();
    }

    static void WaveLife()
    {
        Fresh();
        var spec = WaveSpec.Surf(4);
        Vector2 muzzle = new Vector2(0f, 3f), pilot = new Vector2(0f, -3f);
        var w = ArmWave(spec, muzzle, pilot, .2f);
        Check("a tell asked for in .2 s is raised to " + AttackHazard.MinTellSeconds + " s (FR1) and the wave ignites by itself when it is up", Mathf.Approximately(w.TellSeconds, AttackHazard.MinTellSeconds));
        Advance(.6f);
        Check("still telling at .6 s of .7: the band is not drawn, the preview has shown " + w.Preview.ShownSeconds.ToString("F2") + " s (>= " + AttackPreview.MinLead + ")",
              w.State == AttackHazard.Phase.Tell && !w.LeftRenderer.enabled && w.Preview.ShownSeconds >= AttackPreview.MinLead);
        AdvanceUntil(() => w.State == AttackHazard.Phase.Live, 1f);
        Check("then it is live: collider on, both bars drawn, chevrons still at the gap edges", w.State == AttackHazard.Phase.Live && w.HitCollider.enabled && w.LeftRenderer.enabled && w.RightRenderer.enabled && w.MarkerA.enabled);
        float y0 = w.Y;
        Advance(.5f);
        float speed = (y0 - w.Y) / .5f;
        Check("the band falls at " + speed.ToString("F2") + " u/s (spec " + spec.speed + ", <= " + AttackWave.MaxSpeed + ", FR3)", Mathf.Abs(speed - spec.speed) < .06f && speed <= AttackWave.MaxSpeed + .01f);
        float live = AdvanceUntil(() => w.State != AttackHazard.Phase.Live, 6f);
        Check("it falls out of the view after " + (live + .5f).ToString("F1") + " s of fall, then it is harmless",
              w.State == AttackHazard.Phase.After && !w.HitCollider.enabled && !w.ZoneLive && w.Y <= w.EndHeight + .01f && !w.LeftRenderer.enabled);
        AdvanceUntil(() => w.State == AttackHazard.Phase.Off, 1f);
        Check("then it is back in the pool, every renderer off, no preview or dot left",
              w.State == AttackHazard.Phase.Off && AttackWave.Pool.ActiveCount == 0 && !w.gameObject.activeSelf && AttackPreview.ActiveCount == 0 && AttackPreview.DotsInUse == 0 && !w.MarkerA.enabled);
        // a pilot's wave stays in the world, a hazard's rides the board on top of its fall
        moveBackGround.speed = .4f;
        var rideSpec = WaveSpec.Surf(4);
        rideSpec.ride = 1f;
        var a = ArmWave(rideSpec, muzzle, pilot, 1f);
        var b = ArmWave(WaveSpec.Surf(4), muzzle, pilot, 1f);
        a.Ignite(); b.Ignite();
        float ya = a.Y, yb = b.Y;
        Advance(.5f);
        Check("a wave with ride 1 falls faster than the pilot's one by the board's scroll (" + (ya - a.Y).ToString("F2") + " vs " + (yb - b.Y).ToString("F2") + " u in .5 s)", (ya - a.Y) > (yb - b.Y) + .05f);
        moveBackGround.speed = 0f;
        EliteSystem.Clear();
    }

    // ---- the hitbox is the shape ------------------------------------------------------------------

    static void JetWaveHitbox()
    {
        Fresh();
        var jet = ArmJet(JetSpec.Flame(3), new Vector2(0f, 2.2f), new Vector2(.5f, -1.2f), 1f);
        jet.Ignite();
        Advance(.25f);
        Physics2D.SyncTransforms();
        int agree = 0, any = 0;
        var col = jet.HitCollider;
        for (float x = -3.2f; x <= 3.2f; x += .05f)
            for (float y = -3f; y <= 2.4f; y += .05f)
            {
                var p = new Vector2(x, y);
                bool a = jet.Footprint.Touches(p, 0f), b = col.OverlapPoint(p);
                if (a || b) any++;
                if (a && b) agree++;
            }
        Check("the jet's trigger collider covers its trapezoid (" + agree + " of " + any + " points agree, at " + (jet.Direction * Mathf.Rad2Deg).ToString("F1") + " deg)", any > 100 && agree >= any * .93f);
        Check("the hitbox is tagged Enimey, named for the death crash and carries the marker", jet.Hitbox.CompareTag("Enimey") && jet.Hitbox.name == AttackHazard.HitboxName && AttackHazard.IsHitbox(jet.Hitbox));
        jet.Cancel();

        var wave = ArmWave(WaveSpec.Surf(4), new Vector2(0f, 3f), new Vector2(.6f, -3f), 1f);
        wave.Ignite();
        Advance(.3f);
        Physics2D.SyncTransforms();
        agree = 0; any = 0;
        col = wave.HitCollider;
        float yy = wave.Y;
        for (float x = -3.4f; x <= 3.4f; x += .05f)
            for (float y = yy - .5f; y <= yy + .5f; y += .02f)
            {
                var p = new Vector2(x, y);
                bool a = wave.Footprint.Touches(p, 0f), b = col.OverlapPoint(p);
                if (a || b) any++;
                if (a && b) agree++;
            }
        Check("the wave's collider covers its two bars (" + agree + " of " + any + " points agree), and not the gap: " + (col.OverlapPoint(new Vector2(wave.GapX, wave.Y)) ? "COVERED" : "clear"),
              any > 300 && agree >= any * .95f && !col.OverlapPoint(new Vector2(wave.GapX, wave.Y)));
        wave.Cancel();
    }

    // ---- what a hit does -------------------------------------------------------------------------------

    static void JetWaveHitRules()
    {
        Fresh();
        var rig = new DeathCrashTest.Rig(FirstShip);
        try
        {
            var ship = rig.ship.transform;
            collisionDetection.MAXLIFE = 5;
            EliteSystem.PlayerOverride = ship;
            ship.position = Vector3.zero;
            collisionDetection.lifeCounter = 0;
            PlayerInvuln.Reset();
            var j = ArmJet(JetSpec.Pressure(4), new Vector2(0f, 3f), new Vector2(0f, 0f), 1f);
            j.Ignite();
            Advance(.02f);
            Check("a live jet is a hostile hitbox that is not a hazard body (RamKill) and a projectile to the death crash", RamKill.NotAHazardBody(j.Hitbox) && DeathCrash.Classify(j.Hitbox) == DeathCrash.KillerKind.Projectile);
            long paid = RunScore.Total;
            rig.Hit(j.Hitbox);
            Check("an unshielded hit costs one heart (" + collisionDetection.lifeCounter + "), pays nothing, the jet is not spent on the hull (" + j.State + ")",
                  collisionDetection.lifeCounter == 1 && !buttonClicks.playerDied && RunScore.Total == paid && j.Hitbox != null && j.State == AttackHazard.Phase.Live && j.HitCollider.enabled);
            j.Cancel();

            collisionDetection.lifeCounter = 0;
            PlayerInvuln.Reset();
            collisionDetection.atomCheck = true;
            RunScore.OnShieldRaised();
            j = ArmJet(JetSpec.Pressure(4), new Vector2(0f, 3f), new Vector2(0f, 0f), 1f);
            j.Ignite();
            Advance(.02f);
            rig.Hit(j.Hitbox);
            Check("under the shield the jet is absorbed: the pulse ends (" + j.State + "), no heart lost (" + collisionDetection.lifeCounter + ")", j.State == AttackHazard.Phase.After && collisionDetection.lifeCounter == 0 && !j.HitCollider.enabled);
            collisionDetection.atomCheck = false;
            j.Cancel();

            PlayerInvuln.Reset();
            var w = ArmWave(WaveSpec.Surf(4), new Vector2(0f, .6f), new Vector2(0f, -3f), 1f);
            w.Ignite();
            Advance(.02f);
            rig.Hit(w.Hitbox);
            Check("a hit by the wave costs one heart too (" + collisionDetection.lifeCounter + "), the band keeps falling", collisionDetection.lifeCounter == 1 && w.State == AttackHazard.Phase.Live);
            collisionDetection.lifeCounter = 0;
            PlayerInvuln.Reset();
            collisionDetection.atomCheck = true;
            RunScore.OnShieldRaised();
            rig.Hit(w.Hitbox);
            Check("under the shield the wave is absorbed: the pulse ends (" + w.State + ")", w.State == AttackHazard.Phase.After && collisionDetection.lifeCounter == 0);
            collisionDetection.atomCheck = false;
            w.Cancel();

            // a blink: erased only where the hull lands on it
            PlayerInvuln.Reset();
            w = ArmWave(WaveSpec.Surf(4), new Vector2(0f, .6f), new Vector2(0f, -3f), 1f);
            w.Ignite();
            Advance(.02f);
            bool handledGap = EliteShip.TeleportStrike(w.Hitbox, new Vector3(w.GapX, w.Y, 0f));
            bool stillLive = w.State == AttackHazard.Phase.Live;
            bool handledOn = EliteShip.TeleportStrike(w.Hitbox, new Vector3(-2f, w.Y, 0f));
            Check("a blink landing in the wave's gap leaves it falling (handled: " + handledGap + "); one landing on the band erases it", handledGap && stillLive && handledOn && w.State == AttackHazard.Phase.After);
            w.Cancel();
            j = ArmJet(JetSpec.Pressure(4), new Vector2(0f, 3f), new Vector2(0f, 0f), 1f);
            j.Ignite();
            Advance(.02f);
            bool farHandled = EliteShip.TeleportStrike(j.Hitbox, new Vector3(1.2f, 1.5f, 0f));
            bool farLive = j.State == AttackHazard.Phase.Live;
            bool onHandled = EliteShip.TeleportStrike(j.Hitbox, new Vector3(0f, 1.5f, 0f));
            Check("a blink landing 1.2 u beside the jet leaves it burning; one landing on it erases it", farHandled && farLive && onHandled && j.State == AttackHazard.Phase.After);
            j.Cancel();

            // the fatal hit destroys the hitbox: the pulse is over, nothing throws
            j = ArmJet(JetSpec.Pressure(4), new Vector2(0f, 3f), new Vector2(0f, 0f), 1f);
            j.Ignite();
            Advance(.02f);
            Object.DestroyImmediate(j.Hitbox);
            Advance(.05f);
            Check("a destroyed hitbox (the fatal hit) ends the jet cleanly (" + j.State + ")", j.State == AttackHazard.Phase.After || j.State == AttackHazard.Phase.Off);
            w = ArmWave(WaveSpec.Surf(4), new Vector2(0f, .6f), new Vector2(0f, -3f), 1f);
            w.Ignite();
            Object.DestroyImmediate(w.Hitbox);
            Advance(.05f);
            Check("... and the wave's (" + w.State + ")", w.State == AttackHazard.Phase.After || w.State == AttackHazard.Phase.Off);
            EliteSystem.Clear();
            var again = ArmJet(JetSpec.Pressure(4), new Vector2(0f, 3f), new Vector2(0f, 0f), 1f);
            again.Ignite();
            Check("... and the pool makes a fresh hitbox for the next take", again != null && again.Hitbox != null && again.HitCollider.enabled);
        }
        finally { rig.Dispose(); PlayerInvuln.Reset(); collisionDetection.lifeCounter = 0; collisionDetection.atomCheck = false; }
    }

    // ---- friendly fire ----------------------------------------------------------------------------------

    static void JetWaveFriendlyFire()
    {
        Fresh();
        var shooter = Rock(new Vector2(0f, 2.4f));
        var victim = Rock(new Vector2(0f, 0f));
        var beside = Rock(new Vector2(2.1f, 0f));
        var j = ArmJet(JetSpec.Pressure(4), new Vector2(0f, 2.4f), new Vector2(0f, -1f), 1f, shooter);
        Advance(.5f);
        Check("a jet in its tell hurts nothing", victim != null && FriendlyFire.HostileKills == 0);
        j.Ignite();
        Advance(.2f);
        Check("a live jet destroys the hazard on its line, unpaid (" + FriendlyFire.HostileKills + " kill), spares its own shooter at the nozzle and one beside it",
              victim == null && shooter != null && beside != null && FriendlyFire.HostileKills == 1);
        j.Cancel();

        Fresh();
        var rockInBand = Rock(new Vector2(-1.8f, 2.9f));
        var rockInGap = Rock(new Vector2(0f, 2.9f));
        var by = Rock(new Vector2(1.9f, 4f));
        FriendlyFire.ResetHostileCounters();
        var w = ArmWave(WaveSpec.Surf(4), new Vector2(0f, 3f), new Vector2(0f, -3f), 1f, by);
        w.Ignite();
        Advance(.1f);
        Check("a live wave destroys the hazard lying in its band, spares the one in the gap (" + FriendlyFire.HostileKills + " kill)", rockInBand == null && rockInGap != null && FriendlyFire.HostileKills == 1);
        w.Cancel();
        // an elite loses one heart per pulse
        EliteEvasion.Enabled = false;
        Fresh();
        EliteDef def = null;
        foreach (var d in EliteCatalog.All) if (d.brain == "gunship") { def = d; break; }
        var elite = EliteShip.CreateInPlay(def, new Vector2(1.5f, 1.5f));
        elite.AttackCooldown = 99f;
        FriendlyFire.ResetHostileCounters();
        var jet = ArmJet(JetSpec.Pressure(4), new Vector2(1.5f, 3f), new Vector2(1.5f, -1f), 1f);
        jet.Ignite();
        AdvanceUntil(() => jet.State != AttackHazard.Phase.Live, 2f);
        Check("a jet over an elite takes one heart (" + FriendlyFire.HostileEliteHits + " hit) in its whole pulse", FriendlyFire.HostileEliteHits == 1);
        AttackHazard.HurtsOtherEnemies = false;
        var rock = Rock(new Vector2(0f, 0f));
        var j2 = ArmJet(JetSpec.Pressure(4), new Vector2(0f, 2.4f), new Vector2(0f, -1f), 1f);
        j2.Ignite();
        Advance(.2f);
        Check("with friendly fire switched off for hazards nothing is hit", rock != null);
        AttackHazard.HurtsOtherEnemies = true;
        EliteEvasion.Enabled = true;
        EliteSystem.Clear();
    }

    // ---- shots ------------------------------------------------------------------------------------------

    static void JetWaveShotsBurn()
    {
        Fresh();
        var def = new EliteDef { key = "test_ember", world = "ember", shotSize = .22f, shotColor = "#FF4FD8", shotCore = "#FFFFFF" };
        def.Resolve();
        var pool = EliteSystem.Shots;
        var other = new GameObject("~OtherShooter");   // (a zone spares shots of its own shooter's volley: these belong to somebody else)
        var jet = ArmJet(JetSpec.Pressure(4), new Vector2(0f, 3f), new Vector2(0f, -1f), 1f, other);
        var early = pool.Fire(null, def, EliteShots.Kind.Bolt, new Vector2(0f, 1f), Vector2.zero);
        Advance(.2f);
        Check("a jet in its tell burns nothing", early.Active);
        early.Recycle();
        jet.Ignite();
        var burned = pool.Fire(null, def, EliteShots.Kind.Bolt, new Vector2(0f, 1f), Vector2.zero);
        var heavy = pool.Fire(null, def, EliteShots.Kind.Shell, new Vector2(.02f, 0f), Vector2.zero);
        var safe = pool.Fire(null, def, EliteShots.Kind.Bolt, new Vector2(1.5f, 1f), Vector2.zero);
        Advance(.05f);
        Check("a live jet burns the light and the heavy shot lying in it and spares one beside it (burns " + HostileShots.ZoneBurns + ")", !burned.Active && !heavy.Active && safe.Active && HostileShots.ZoneBurns >= 2);
        bool registered = false;
        foreach (var z in HostileShots.Zones) registered |= ReferenceEquals(z, jet);
        Check("the jet is registered with HostileShots as a zone", registered);
        Check("a player shot sweeping through the jet does not shoot it down", HostileShots.ShootDownAlong(new Vector2(-3f, 1f), new Vector2(3f, 1f), .2f) >= 0 && jet.State == AttackHazard.Phase.Live);
        EliteSystem.Clear();

        Fresh();
        pool = EliteSystem.Shots;
        other = new GameObject("~OtherShooter");
        var wave = ArmWave(WaveSpec.Surf(4), new Vector2(0f, 3f), new Vector2(0f, -3f), 1f, other);
        wave.Ignite();
        Advance(.25f);
        float y = wave.Y;
        var onBand = pool.Fire(null, def, EliteShots.Kind.Bolt, new Vector2(-1.8f, y), Vector2.zero);
        var inGap = pool.Fire(null, def, EliteShots.Kind.Bolt, new Vector2(wave.GapX, y), Vector2.zero);
        Advance(.02f);
        Check("a live wave burns a shot lying on the band and spares one in the gap (burns " + HostileShots.ZoneBurns + ")", !onBand.Active && inGap.Active);
        EliteSystem.Clear();
    }

    // ---- pause, pools, allocation -----------------------------------------------------------------------------

    static void JetWavePauseAndPools()
    {
        Fresh();
        var jet = ArmJet(JetSpec.Flame(3), new Vector2(0f, 2.2f), new Vector2(0f, -1.2f), 1f);
        var wave = ArmWave(WaveSpec.Surf(4), new Vector2(0f, 3f), new Vector2(0f, -3f), 1f);
        Advance(.3f);
        float tj = jet.PhaseTime, tw = wave.PhaseTime, shown = jet.Preview.ShownSeconds;
        for (int i = 0; i < 120; i++) { EliteSystem.Step(0f); jet.Step(0f); wave.Step(0f); AttackPools.StepAll(0f); }
        Check("a frozen world (dt 0) freezes the jet and the wave and their previews (jet " + tj.ToString("F2") + " -> " + jet.PhaseTime.ToString("F2") + ", wave " + tw.ToString("F2") + " -> " + wave.PhaseTime.ToString("F2") + ")",
              jet.PhaseTime == tj && wave.PhaseTime == tw && jet.Preview.ShownSeconds == shown && jet.State == AttackHazard.Phase.Tell);
        jet.Ignite(); wave.Ignite();
        Advance(.1f);
        float ya = wave.Y, da = jet.Direction;
        for (int i = 0; i < 120; i++) { EliteSystem.Step(0f); AttackPools.StepAll(0f); }
        Check("... and a live one: the band does not fall and the jet does not turn while the world is frozen", wave.Y == ya && jet.Direction == da && wave.State == AttackHazard.Phase.Live);
        string dir = "Assets/Scripts/Gameplay/Enemies/Attacks/";
        bool clean = true;
        string why = "";
        foreach (string file in new[] { "AttackJet.cs", "AttackWave.cs", "AttackHazardArtJetWave.cs" })
        {
            string src = File.ReadAllText(dir + file);
            foreach (string bad in new[] { "Time.deltaTime", "Time.unscaledDeltaTime", "void Update(", "void LateUpdate(", "void FixedUpdate(", "Time.time" })
                if (src.Contains(bad)) { clean = false; why += file + " uses " + bad + "; "; }
        }
        Check("the jet and the wave advance only through Step(dt): no Update, Time.deltaTime or Time.time (" + why + ")", clean);
        EliteSystem.Clear();

        // pools: fixed mass
        Fresh();
        int jets = 0, waves = 0;
        for (int i = 0; i < AttackJet.PoolSize + 2; i++) if (ArmJet(JetSpec.Flame(3), new Vector2(-2f + i * .8f, 2f), new Vector2(-2f + i * .8f, -1f)) != null) jets++;
        for (int i = 0; i < AttackWave.PoolSize + 2; i++) if (ArmWave(WaveSpec.Surf(4), new Vector2(0f, 3f + i), new Vector2(0f, -3f)) != null) waves++;
        Check("the jet pool holds " + AttackJet.PoolSize + " (" + jets + " armed) and the wave pool " + AttackWave.PoolSize + " (" + waves + " armed)", jets == AttackJet.PoolSize && waves == AttackWave.PoolSize && AttackJet.Pool.Capacity == AttackJet.PoolSize);
        EliteSystem.Clear();

        // every outline of a jet, a wave, a ring and a strike at once is drawn in full
        Fresh();
        AttackPreview.ResetCounters();
        ArmJet(JetSpec.Flame(3), new Vector2(-1.5f, 2.2f), new Vector2(-1.5f, -2.2f));
        ArmJet(JetSpec.Pressure(4), new Vector2(1.5f, 2.2f), new Vector2(1.5f, -2.2f));
        ArmWave(WaveSpec.Surf(4), new Vector2(0f, 3f), new Vector2(0f, -3f));
        AttackBlast.Arm(BlastSpec.Standard(1), new Vector2(0f, 2.2f), new Vector2(0f, -1.2f), 1f, null);
        Check("two jets, a wave and a ring in their tell at once: all " + AttackPreview.ActiveCount + " previews are drawn in full (" + AttackPreview.DotsInUse + " dots, " + AttackPreview.Dropped + " dropped)",
              AttackPreview.ActiveCount == 4 && AttackPreview.Dropped == 0 && AttackPreview.DotsInUse < AttackPreview.MaxDots);
        EliteSystem.Clear();

        // a whole life of each, twice to warm, then measured
        Fresh();
        System.Action life = () =>
        {
            ArmJet(JetSpec.Flame(3), new Vector2(0f, 2.2f), new Vector2(0f, -1.2f), .8f);
            ArmWave(WaveSpec.Surf(4), new Vector2(0f, 3f), new Vector2(0f, -3f), .8f);
            for (int i = 0; i < 480; i++) { AttackPools.StepAll(Dt); HostileShots.Resolve(); }
        };
        life(); life();
        bool meter = TestHarness.AllocMeterWorks(out long ctl);
        long used = TestHarness.AllocatedBytes(life);
        Check("arming, telling, igniting, burning (the jet sweeping, the band falling), fading and releasing a jet and a wave allocates nothing after warm-up (" + used + " bytes; meter " + (meter ? "ok" : "blind") + ")", meter && used == 0);
        Check("the pools are back to idle, nothing in the scene stays active", AttackPools.ActiveCount == 0 && AttackPreview.ActiveCount == 0 && AttackHazard.ActiveCount == 0);
        EliteSystem.Clear();
    }

    // ---- cleanup -------------------------------------------------------------------------------------------

    static void JetWaveCleanup()
    {
        Fresh();
        var jet = ArmJet(JetSpec.Flame(3), new Vector2(0f, 2.2f), new Vector2(0f, -1.2f), 1f);
        jet.Ignite();
        var wave = ArmWave(WaveSpec.Surf(4), new Vector2(0f, 3f), new Vector2(0f, -3f), 1f);
        Advance(.1f);
        Check("a jet (live) and a wave (telling) are active", AttackHazard.ActiveCount == 2 && AttackPreview.ActiveCount >= 1);
        Planetfall.ClearBoard(null);
        Check("a world change (Planetfall.ClearBoard) clears both and their previews", AttackHazard.ActiveCount == 0 && AttackPreview.ActiveCount == 0 && !jet.gameObject.activeSelf && !wave.gameObject.activeSelf);

        var j2 = ArmJet(JetSpec.Flame(3), new Vector2(0f, 2.2f), new Vector2(0f, -1.2f), 1f);
        j2.Ignite();
        var w2 = ArmWave(WaveSpec.Surf(4), new Vector2(0f, 3f), new Vector2(0f, -3f), 1f);
        w2.Ignite();
        var dir = new GameObject("~Director").AddComponent<EliteDirector>();
        buttonClicks.playerDied = true;
        typeof(EliteDirector).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(dir, null);
        Check("a player death clears them on the director's next frame", AttackHazard.ActiveCount == 0 && AttackPreview.ActiveCount == 0 && !j2.HitCollider.enabled && !w2.HitCollider.enabled);
        buttonClicks.playerDied = false;
        Object.DestroyImmediate(dir.gameObject);

        var j3 = ArmJet(JetSpec.Flame(3), new Vector2(0f, 2.2f), new Vector2(0f, -1.2f), 1f);
        j3.Ignite();
        var w3 = ArmWave(WaveSpec.Surf(4), new Vector2(0f, 3f), new Vector2(0f, -3f), 1f);
        int before = HostileShots.Zones.Count;
        EliteSystem.Clear();   // a restart / scene teardown
        HostileShots.Resolve();
        bool left = false;
        foreach (var z in HostileShots.Zones) left |= z != null && !(z is Object o && o == null) && (ReferenceEquals(z, j3) || ReferenceEquals(z, w3));
        Check("a restart (EliteSystem.Clear) leaves no jet or wave active and unregisters the destroyed zones (" + before + " -> " + HostileShots.Zones.Count + ")", AttackHazard.ActiveCount == 0 && !left && HostileShots.Zones.Count < before);
        var fresh = ArmJet(JetSpec.Flame(3), new Vector2(0f, 2.2f), new Vector2(0f, -1.2f), 1f);
        var fresh2 = ArmWave(WaveSpec.Surf(4), new Vector2(0f, 3f), new Vector2(0f, -3f), 1f);
        Check("the next take builds fresh pools", fresh != null && fresh2 != null && AttackJet.Pool.Alive && AttackWave.Pool.Alive);
        EliteSystem.Clear();

        // a shooter that dies in the tell takes its hazard with it (EnemyBrain.CancelHazards)
        Fresh();
        var cancel = ArmJet(JetSpec.Flame(3), new Vector2(0f, 2.2f), new Vector2(0f, -1.2f), 1f);
        cancel.Cancel();
        Check("a cancelled tell leaves no hitbox, no preview and a free pool item", cancel.State == AttackHazard.Phase.Off && AttackPreview.ActiveCount == 0 && AttackJet.Pool.ActiveCount == 0);
    }

    // ---- EnemyAttack.Jet / Wave through the table's builders -----------------------------------------------------

    static void JetWaveBehaviourKinds()
    {
        var jet = new EnemyBehaviour { key = "x" }.Jet(JetSpec.Flame(3)).Timing(.5f, 3f, 2);
        var wave = new EnemyBehaviour { key = "y" }.Wave(WaveSpec.Surf(4)).Timing(.5f, 3f, 2);
        Check("Jet and Wave are attacks that shoot and are area hazards", jet.Attacks && jet.Shoots && jet.IsAreaHazard && jet.attack == EnemyAttack.Jet && wave.Attacks && wave.Shoots && wave.IsAreaHazard && wave.attack == EnemyAttack.Wave);
        Check("their windup is raised to the instant-hit floor (" + EnemyBrain.TellFor(jet).ToString("F2") + ", " + EnemyBrain.TellFor(wave).ToString("F2") + ")",
              EnemyBrain.TellFor(jet) >= AttackHazard.MinTellSeconds && EnemyBrain.TellFor(wave) >= AttackHazard.MinTellSeconds);
        Check("they reserve the shot budget FR7 names, rounded up: a jet (1.5) 2, a wave 2 (" + jet.ThreatCount + "/" + wave.ThreatCount + ")", jet.ThreatCount == 2 && wave.ThreatCount == 2);
        int used = 0;
        foreach (var def in EnemyRoster.All)
        {
            var b = EnemyBehaviours.For(def.key);
            if (b != null && (b.attack == EnemyAttack.Jet || b.attack == EnemyAttack.Wave)) used++;
        }
        Check("no world's roster uses Jet or Wave yet (behaviour unchanged until the per-world phases): " + used, used == 0);
    }
}
