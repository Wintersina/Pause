using System.Collections.Generic;
using UnityEngine;
using static AttackTestKit;

// AttackFairnessTest, the jet (plan phase 1a) and the wave (phase 1b):
//
//   * FR1 tells / FR3 live time and speed / the firing rules / aim locked at the tell: TellsFromTheRealBrain (AttackFairnessTest.cs)
//     over the themed fixtures themed:ember_flame_jet, frost_ray, tide_pressure_jet, tide_surf_wave, space_scan_line
//   * FR4 safe corridor: a jet always leaves a free chord >= 1.4 u across the lane in every row the ship can fly in, and the ship
//     can fly out of the footprint in the .45 s a .7 s tell leaves after a .25 s reaction; a wave's gap is >= 1.4 u, inside the
//     rails, with the ship's body clear in it, and reachable from anywhere in the lane in the tell plus the wave's .8 s front
//   * FR7 threat weights: a jet 1.5, a wave 2, counted against the roster budget
//   * the pink cue (PC1-PC3) on the drawn pixels of every jet and wave sprite lives in DrawnPixels (AllSprites); here the flicker and the
//     shape, the bold keyline on a bright world, the hit box against the drawing, and the art slots
public static partial class AttackFairnessTest
{
    static void JetWaveSuite()
    {
        JetWaveThreat();
        JetCorridor();
        WaveCorridor();
        JetWaveSprites();
        JetWaveBold();
        JetWaveHitMatchesDrawing();
        JetWaveArtSlots();
    }

    // ---- FR7 -------------------------------------------------------------------------------------

    static void JetWaveThreat()
    {
        Fresh();
        var j = AttackJet.Arm(JetSpec.Flame(3), new Vector2(-1.5f, 2.2f), new Vector2(-1.5f, -1.5f), 1f, null);
        var w = AttackWave.Arm(WaveSpec.Surf(4), new Vector2(0f, 3f), new Vector2(0f, -3f), 1f, null);
        Check("a jet and a wave in their tell are not counted yet", AttackHazard.LiveThreat == 0);
        j.Ignite();
        Check("a live jet counts 1.5 shots against the roster budget (" + AttackHazard.LiveThreat + " rounded up to 2)", AttackHazard.LiveThreat == 2);
        w.Ignite();
        Check("... and a live wave 2 more (" + AttackHazard.LiveThreat + " for jet + wave = 3.5 -> 4); EnemyThreat.LiveShots includes them (" + EnemyThreat.LiveShots + ", budget " + EnemyThreat.ShotBudget + ")",
              AttackHazard.LiveThreat == 4 && EnemyThreat.LiveShots >= 4);
        EliteSystem.Clear();
    }

    // ---- FR4: the jet ------------------------------------------------------------------------------

    // The x extent of the footprint on a row (and whether it touches the row at all), over the lane between the rails.
    static bool RowSpan(AttackShape sh, float y, float rail, out float minX, out float maxX)
    {
        minX = float.PositiveInfinity; maxX = float.NegativeInfinity;
        for (float x = -rail; x <= rail; x += .04f)
            if (sh.Touches(new Vector2(x, y), 0f)) { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); }
        return maxX >= minX;
    }

    static void JetCorridor()
    {
        Fresh();
        float rail = BossRails.DrawnInnerEdge;
        float clearance = DodgeBot.ShipRadius + .05f, shipLimit = rail - DodgeBot.ShipRadius;
        float least = 99f, slowest = 0f;
        int jets = 0, frames = 0, bad = 0, slow = 0, unreachable = 0;
        string worst = "";
        float[] pilotX = { -rail + .3f, -1.2f, 0f, 1.2f, rail - .3f };
        float[] muzzleDx = { -1.5f, 0f, 1.5f };
        float[] muzzleDy = { 2.0f, 3.0f, 4.0f, 5.5f };
        foreach (var spec0 in new[] { JetSpec.Flame(3), JetSpec.Pressure(4), JetSpec.Ray(1), JetSpec.Lance(0) })
            foreach (float px in pilotX)
                foreach (float dx in muzzleDx)
                    foreach (float dy in muzzleDy)
                    {
                        Vector2 pilot = new Vector2(px, -3f), muzzle = new Vector2(Mathf.Clamp(px + dx, -rail + .2f, rail - .2f), -3f + dy);
                        if (Vector2.Distance(muzzle, pilot) < EnemyBrain.MinFireDistance) continue;
                        Pilot.position = pilot;
                        var spec = spec0;
                        if (spec.style == JetStyle.Flame) spec.sweepDeg = 30f;   // (the boss's flame sweep: the hardest case for the corridor)
                        var j = AttackJet.Arm(spec, muzzle, pilot, 1f, null);
                        j.Ignite();
                        jets++;
                        // at the instant it goes live: how far the pilot has to fly to have his body clear of the footprint, on his row
                        bool onRow = RowSpan(j.Footprint, pilot.y, rail, out float x0, out float x1);
                        if (onRow)
                        {
                            float needL = px - (x0 - clearance), needR = (x1 + clearance) - px;
                            bool okL = x0 - clearance >= -shipLimit, okR = x1 + clearance <= shipLimit;
                            float need = okL && okR ? Mathf.Min(Mathf.Max(0f, needL), Mathf.Max(0f, needR)) : (okL ? Mathf.Max(0f, needL) : (okR ? Mathf.Max(0f, needR) : float.PositiveInfinity));
                            if (float.IsInfinity(need)) unreachable++;
                            else
                            {
                                slowest = Mathf.Max(slowest, FlightSeconds(need));
                                if (FlightSeconds(need) > AttackHazard.MinTellSeconds - DodgeBot.Reaction) slow++;
                            }
                        }
                        for (int i = 0; i < 120 && j.State == AttackHazard.Phase.Live; i++)
                        {
                            EliteSystem.Step(Dt);
                            if (j.State != AttackHazard.Phase.Live || i % 3 != 0) continue;
                            for (float y = -3.9f; y <= -.7f; y += .4f)
                            {
                                frames++;
                                float chord = 2f * rail;
                                if (RowSpan(j.Footprint, y, rail, out float a, out float b)) chord = Mathf.Max(a - -rail, rail - b);
                                if (chord < least) { least = chord; worst = spec.style + " pilot " + px.ToString("F1") + " muzzle " + muzzle.ToString("F1") + " row " + y.ToString("F1"); }
                                if (chord < MinCorridor - .02f) bad++;
                            }
                        }
                        j.Cancel();
                    }
        Check("every jet (flame swept 30 deg, pressure jet, ray, lance) of " + jets + " leaves a free chord of at least " + MinCorridor + " u between the footprint and a rail on every row the ship can fly in (" + frames + " row-frames, " +
              "least " + least.ToString("F2") + " u: " + worst + ") -- " + bad + " violations", jets >= 150 && frames > 2000 && bad == 0);
        Check("... and the pilot can always fly out of the footprint, body clear of it and of the rails, in the " + (AttackHazard.MinTellSeconds - DodgeBot.Reaction).ToString("F2") + " s a .7 s tell leaves after a .25 s reaction " +
              "(slowest " + slowest.ToString("F2") + " s; " + slow + " too slow, " + unreachable + " with no side to go to)", slow == 0 && unreachable == 0);
        Pilot.position = new Vector3(0f, -3f, 0f);
        EliteSystem.Clear();
    }

    // ---- FR4: the wave -----------------------------------------------------------------------------

    static void WaveCorridor()
    {
        Fresh();
        float rail = BossRails.DrawnInnerEdge;
        float clearance = DodgeBot.ShipRadius + .05f;
        int waves = 0, frames = 0, narrow = 0, outside = 0, blocked = 0, slow = 0;
        float least = 99f, slowest = 0f;
        foreach (var baseSpec in new[] { WaveSpec.Surf(4), WaveSpec.Scan(0) })
            foreach (float offset in new[] { 0f, 1f, 1.5f })
                foreach (float px in new[] { -rail + .2f, -1.5f, -.6f, 0f, .6f, 1.5f, rail - .2f })
                    foreach (float up in new[] { 1.7f, 3f, 6f })
                    {
                        Vector2 pilot = new Vector2(px, -3f), muzzle = new Vector2(Mathf.Clamp(px * .5f, -rail + .2f, rail - .2f), -3f + up);
                        Pilot.position = pilot;
                        var spec = baseSpec;
                        spec.gapOffset = offset;
                        var w = AttackWave.Arm(spec, muzzle, pilot, 1f, null);
                        float gl = w.GapX - w.GapWidth * .5f, gr = w.GapX + w.GapWidth * .5f;
                        w.Ignite();
                        waves++;
                        // the pilot's flight to a spot where his body is clear of both bars, in the tell after his reaction plus the wave's front
                        float lo = gl + clearance, hi = gr - clearance;
                        float need = px < lo ? lo - px : (px > hi ? px - hi : 0f);
                        float front = (w.StartHeight - w.HitHalf - pilot.y) / (w.FallSpeed + EliteSystem.Scroll * w.Spec.ride);
                        float avail = AttackHazard.MinTellSeconds - DodgeBot.Reaction + front;
                        slowest = Mathf.Max(slowest, FlightSeconds(need));
                        if (hi < lo || FlightSeconds(need) > avail) slow++;
                        for (int i = 0; i < 400 && w.State == AttackHazard.Phase.Live; i++)
                        {
                            EliteSystem.Step(Dt);
                            if (w.State != AttackHazard.Phase.Live || i % 4 != 0) continue;
                            frames++;
                            // the free run of the band's row around the gap's middle
                            float y = w.Y;
                            float a = w.GapX, b = w.GapX;
                            while (a > -rail - .3f && !w.Footprint.Touches(new Vector2(a - .02f, y), 0f)) a -= .02f;
                            while (b < rail + .3f && !w.Footprint.Touches(new Vector2(b + .02f, y), 0f)) b += .02f;
                            float run = Mathf.Min(b, rail) - Mathf.Max(a, -rail);
                            least = Mathf.Min(least, run);
                            if (run < MinCorridor - .03f) narrow++;
                            if (gl < -rail || gr > rail) outside++;
                            if (w.Footprint.Touches(new Vector2(w.GapX, y), DodgeBot.ShipRadius)) blocked++;
                        }
                        w.Cancel();
                    }
        Check("every wave (surf and scan line, the gap on the pilot or turned 1 / 1.5 u off him) of " + waves + ": the free run of the band's row around the gap is at least " + MinCorridor + " u at every moment (" + frames + " frames, least " + least.ToString("F2") + " u), " +
              "inside the rails (" + outside + " outside), with the ship's circle clear in the gap's middle (" + blocked + " blocked): " + narrow + " too narrow", waves >= 100 && frames > 800 && narrow == 0 && outside == 0 && blocked == 0);
        Check("... and the pilot, from anywhere in the lane, can fly to where his body is clear in the .7 s tell minus his .25 s reaction plus the wave's front time of at least " + AttackWave.MinFrontSeconds +
              " s (slowest flight " + slowest.ToString("F2") + " s; " + slow + " waves too quick)", slow == 0);
        Pilot.position = new Vector3(0f, -3f, 0f);
        EliteSystem.Clear();
    }

    // ---- the drawing: flicker and shape --------------------------------------------------------------

    static int DiffPixels(Sprite a, Sprite b)
    {
        var pa = Pixels(a); var pb = Pixels(b);
        if (pa.Length != pb.Length) return pa.Length;
        int d = 0;
        for (int i = 0; i < pa.Length; i++) if (!pa[i].Equals(pb[i])) d++;
        return d;
    }

    static void JetWaveSprites()
    {
        Fresh();
        var flame = JetSpec.Flame(3);
        Sprite f0 = AttackHazardArt.JetBody(3, flame.style, 0, 1.8f, flame.baseHalf, flame.tipHalf, false, out float len, out float tipW, out float baseW);
        Sprite f1 = AttackHazardArt.JetBody(3, flame.style, 1, 1.8f, flame.baseHalf, flame.tipHalf, false, out _, out _, out _);
        Sprite f2 = AttackHazardArt.JetBody(3, flame.style, 2, 1.8f, flame.baseHalf, flame.tipHalf, false, out _, out _, out _);
        Check("the flame's frames differ (heat flicker): frame 0 vs 1 in " + DiffPixels(f0, f1) + " pixels, 0 vs 2 in " + DiffPixels(f0, f2) + " (stepped frames, not a smooth breath)", DiffPixels(f0, f1) >= 20 && DiffPixels(f0, f2) >= 20);
        float ratio = f0.rect.height / f0.rect.width;
        Check("the flame strip is " + f0.rect.width + " x " + f0.rect.height + " px: elongated (" + ratio.ToString("F1") + ":1), narrow at the nozzle (" + (baseW * 32f).ToString("F0") + " px) and wide at the tip (" + (tipW * 32f).ToString("F0") + " px): a cone, not a disc",
              ratio >= 1.8f && tipW > baseW * 2.5f && len > 1.7f);
        Sprite w0 = AttackHazardArt.WaveProcedural(4, WaveStyle.Surf, 0, false), w1 = AttackHazardArt.WaveProcedural(4, WaveStyle.Surf, 1, false);
        Check("the surf strip's two frames differ in " + DiffPixels(w0, w1) + " pixels (the crest rolls, the core steps)", DiffPixels(w0, w1) >= 20);
        // seamless left-right: the tile's first and last columns continue each other (the crest lines meet)
        var px = Pixels(w0);
        int ww = (int)w0.rect.width, hh = (int)w0.rect.height;
        int mism = 0;
        for (int y = 0; y < hh; y++)
        {
            var a = px[y * ww]; var b = px[y * ww + ww - 1];
            if ((a.a > 0) != (b.a > 0)) mism++;
        }
        Check("the surf strip tiles left to right: its first and last columns match in opaque/clear (" + mism + " rows of " + hh + " differ by more than the crest's step)", mism <= 3);
        Sprite scan = AttackHazardArt.WaveProcedural(0, WaveStyle.Scan, 0, false);
        Check("the neon scan line's strip is thinner than the surf's (" + scan.rect.height + " vs " + w0.rect.height + " px) and flat", scan.rect.height < w0.rect.height);
    }

    // ---- bold keyline on a bright backdrop ---------------------------------------------------------------------

    static void JetWaveBold()
    {
        Fresh();
        ShotOutline.Bold = true;
        var j = AttackJet.Arm(JetSpec.Flame(3), new Vector2(0f, 2.2f), new Vector2(0f, -1.4f), 1f, null);
        j.Ignite();
        var w = AttackWave.Arm(WaveSpec.Surf(4), new Vector2(0f, 3f), new Vector2(0f, -3f), 1f, null);
        w.Ignite();
        Advance(.05f);
        Check("forced bold (a bright backdrop): the jet's body and the wave's strip wear the near-black keyline ring around the pink-white stroke",
              HasKeyline(j.BodyRenderer.sprite) && HasKeyline(w.LeftRenderer.sprite) && HasKeyline(w.MarkerA.sprite));
        EliteSystem.Clear();
        Fresh();
        ShotOutline.Bold = false;
        j = AttackJet.Arm(JetSpec.Flame(3), new Vector2(0f, 2.2f), new Vector2(0f, -1.4f), 1f, null);
        j.Ignite();
        w = AttackWave.Arm(WaveSpec.Surf(4), new Vector2(0f, 3f), new Vector2(0f, -3f), 1f, null);
        w.Ignite();
        Advance(.05f);
        Check("standard (a dark backdrop): no keyline ring on either, the pink-white stroke alone", !HasKeyline(j.BodyRenderer.sprite) && !HasKeyline(w.LeftRenderer.sprite));
        EliteSystem.Clear();
        int bright = -1, dark = -1;
        for (int i = 0; i < WorldManager.Worlds.Length; i++)
        {
            var spec2 = BackdropCatalog.For(WorldManager.Worlds[i].displayName);
            if (spec2.Bright && bright < 0) bright = i;
            if (!spec2.Bright && dark < 0) dark = i;
        }
        bool brightOk = true, darkOk = true;
        string note = "";
        if (bright >= 0)
        {
            Fresh();
            ShotOutline.Bold = null;
            PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, bright);
            var jb = AttackJet.Arm(JetSpec.Flame(bright), new Vector2(0f, 2.2f), new Vector2(0f, -1.4f), 1f, null);
            var wb = AttackWave.Arm(WaveSpec.Surf(bright), new Vector2(0f, 3f), new Vector2(0f, -3f), 1f, null);
            jb.Ignite(); wb.Ignite(); Advance(.05f);
            brightOk = ShotOutline.UseBold && HasKeyline(jb.BodyRenderer.sprite) && HasKeyline(wb.LeftRenderer.sprite);
            note += "bright " + WorldManager.Worlds[bright].displayName + " ";
            EliteSystem.Clear();
        }
        if (dark >= 0)
        {
            Fresh();
            ShotOutline.Bold = null;
            PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, dark);
            var jb = AttackJet.Arm(JetSpec.Flame(dark), new Vector2(0f, 2.2f), new Vector2(0f, -1.4f), 1f, null);
            var wb = AttackWave.Arm(WaveSpec.Surf(dark), new Vector2(0f, 3f), new Vector2(0f, -3f), 1f, null);
            jb.Ignite(); wb.Ignite(); Advance(.05f);
            darkOk = !ShotOutline.UseBold && !HasKeyline(jb.BodyRenderer.sprite) && !HasKeyline(wb.LeftRenderer.sprite);
            note += "dark " + WorldManager.Worlds[dark].displayName;
            EliteSystem.Clear();
        }
        Check("on a world whose backdrop Spec.Bright is true the jet and the wave wear the bold keyline by themselves, on a dark one they do not (" + note + ")", bright >= 0 && dark >= 0 && brightOk && darkOk);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 0);
        ShotOutline.Bold = null;
    }

    // ---- the hit box matches the drawing -----------------------------------------------------------------------------

    static void JetWaveHitMatchesDrawing()
    {
        Fresh();
        var spec = JetSpec.Flame(3);
        Vector2 muzzle = new Vector2(0f, 2.2f);
        var j = AttackJet.Arm(spec, muzzle, new Vector2(.6f, -1.4f), 1f, null);
        var f = j.Footprint;
        bool same = f.PolyCount == 1 && f.PolyLength(0) == 4 && f.LoopCount >= 1 && f.LoopLength(0) == 4;
        for (int i = 0; same && i < 4; i++) same &= (f.LoopAt(0, i) - f.PolyAt(0, i)).sqrMagnitude < 1e-8f;
        Check("in its tell the preview outline's four corners ARE the hit trapezoid's four corners (FR2: the same footprint)", same);
        j.Ignite();
        Advance(.05f);
        var body = j.BodyRenderer;
        var spr = body.sprite;
        Vector2 u = AttackJet.Dir(j.Direction);
        float drawnLen = spr.bounds.size.y * body.transform.lossyScale.y;
        float drawnTip = OpaqueWidth(spr) * body.transform.lossyScale.x;
        float hitTip = j.TipHalf * 2f;
        Vector2 down = -(Vector2)body.transform.up;
        Check("the flame's drawing is " + drawnLen.ToString("F2") + " u long on a " + j.Length.ToString("F2") + " hit, " + drawnTip.ToString("F2") + " wide at the tip on a " + hitTip.ToString("F2") + " hit (between 1x and 2x), " +
              "its strip points along the jet (" + Vector2.Dot(down, u).ToString("F3") + ") and starts at the nozzle (" + Vector2.Distance(body.transform.position, j.Origin).ToString("F3") + " u off)",
              Mathf.Abs(drawnLen - j.Length) < .06f && drawnTip >= hitTip && drawnTip <= hitTip * 2f + .02f && Vector2.Dot(down, u) > .999f && Vector2.Distance(body.transform.position, j.Origin) < .02f);
        Vector2 end = j.Origin + u * j.Length;
        Check("the far-end sparks sit at the tip (" + Vector2.Distance(j.TipRenderer.transform.position, end).ToString("F3") + " u off) and the flare at the nozzle", Vector2.Distance(j.TipRenderer.transform.position, end) < .02f &&
              Vector2.Distance(j.NozzleRenderer.transform.position, j.Origin) < .02f);
        j.Cancel();
        // a column
        var c = AttackJet.Arm(JetSpec.Pressure(4), muzzle, new Vector2(.6f, -1.4f), 1f, null);
        c.Ignite();
        Advance(.05f);
        float cw = OpaqueWidth(c.BodyRenderer.sprite) * c.BodyRenderer.transform.lossyScale.x;
        Check("the pressure jet's drawing is " + cw.ToString("F2") + " wide on a " + (c.TipHalf * 2f).ToString("F2") + " hit (between 1x and 2x)", cw >= c.TipHalf * 2f && cw <= c.TipHalf * 4f + .02f);
        c.Cancel();

        // the wave
        var w = AttackWave.Arm(WaveSpec.Surf(4), new Vector2(0f, 3f), new Vector2(.4f, -3f), 1f, null);
        Check("in its tell the preview outlines include both hit rectangles' corners (FR2): " + w.Footprint.PolyCount + " polygons, " + w.Footprint.LoopCount + " outlines", w.Footprint.PolyCount == 2 && w.Footprint.LoopCount == 3 &&
              (w.Footprint.LoopAt(0, 0) - w.Footprint.PolyAt(0, 0)).sqrMagnitude < 1e-8f && (w.Footprint.LoopAt(1, 2) - w.Footprint.PolyAt(1, 2)).sqrMagnitude < 1e-8f);
        w.Ignite();
        Advance(.05f);
        var L = w.LeftRenderer; var R = w.RightRenderer;
        float gl = w.GapX - w.GapWidth * .5f, gr = w.GapX + w.GapWidth * .5f, half = w.HalfWidth;
        var bl = L.bounds; var br = R.bounds;
        Check("the left bar is drawn from the rail (" + bl.min.x.ToString("F2") + " vs " + (-half).ToString("F2") + ") to the gap (" + bl.max.x.ToString("F2") + " vs " + gl.ToString("F2") + "), the right from the gap (" + br.min.x.ToString("F2") + " vs " + gr.ToString("F2") +
              ") to the rail (" + br.max.x.ToString("F2") + "), centred on the band (" + bl.center.y.ToString("F2") + " vs " + w.Y.ToString("F2") + ")",
              Mathf.Abs(bl.min.x + half) < .03f && Mathf.Abs(bl.max.x - gl) < .03f && Mathf.Abs(br.min.x - gr) < .03f && Mathf.Abs(br.max.x - half) < .03f && Mathf.Abs(bl.center.y - w.Y) < .02f);
        float th = w.DrawnHeight, hit = w.HitHalf * 2f;
        Check("the band is drawn " + th.ToString("F2") + " thick on a " + hit.ToString("F2") + " hit (between 1x and 2x), tiled across (draw mode " + L.drawMode + ")", th >= hit && th <= hit * 2f && L.drawMode == SpriteDrawMode.Tiled);
        w.Cancel();
    }

    // ---- the art slots -------------------------------------------------------------------------------------------

    static void JetWaveArtSlots()
    {
        Fresh();
        Check("without the art files the procedural drawing is used (no jet or wave atlas)", !AttackArt.Has(3, "jet") && !AttackArt.Has(4, "jet") && !AttackArt.Has(4, "wave") && !AttackHazardArt.JetArt(3) && !AttackHazardArt.WaveArt(4));
        var emberJet = Atlas(768, 384, new Color32(255, 120, 220, 255));
        var tideJet = Atlas(768, 448, new Color32(255, 120, 220, 255));
        var tideWave = Atlas(512, 480, new Color32(255, 120, 220, 255));
        AttackArt.Inject(3, "jet", emberJet);
        AttackArt.Inject(4, "jet", tideJet);
        AttackArt.Inject(4, "wave", tideWave);
        AttackHazardArt.Forget();
        Check("the delivered atlases are seen: " + AttackArt.JetBodyPx(3) + " / " + AttackArt.JetBodyPx(4) + " px bodies", AttackHazardArt.JetArt(3) && AttackHazardArt.JetArt(4) && AttackHazardArt.WaveArt(4) &&
              AttackArt.JetBodyPx(3) == 256 && AttackArt.JetBodyPx(4) == 320);
        var j = AttackJet.Arm(JetSpec.Flame(3), new Vector2(0f, 2.2f), new Vector2(0f, -1.4f), 1f, null);
        Check("a delivered ember_attack_jet.png is used automatically for the nozzle's flare in the tell", j.NozzleRenderer.sprite != null && j.NozzleRenderer.sprite.texture == emberJet);
        j.Ignite();
        Advance(.1f);
        var spr = j.BodyRenderer.sprite;
        float drawnContent = spr.bounds.size.y * j.BodyRenderer.transform.lossyScale.y * (230f / 256f);
        Check("... the flame body (128 x 256 cell = 2 u, the cone is 230 px of it): the content is scaled to the hit length (" + drawnContent.ToString("F2") + " vs " + j.Length.ToString("F2") + "), and the far end's sparks come from the atlas",
              spr != null && spr.texture == emberJet && Mathf.Abs(spr.bounds.size.y - 2f) < .01f && Mathf.Abs(drawnContent - j.Length) < .03f && j.TipRenderer.sprite != null && j.TipRenderer.sprite.texture == emberJet);
        // the art frames cycle at 12 fps
        var seen = new HashSet<Sprite>();
        for (int i = 0; i < 6 && j.State == AttackHazard.Phase.Live; i++) { Advance(1f / 12f); if (j.BodyRenderer.sprite != null) seen.Add(j.BodyRenderer.sprite); }
        Check("... and the six body frames play (" + seen.Count + " distinct sprites in half a second)", seen.Count >= 4);
        j.Cancel();
        var t = AttackJet.Arm(JetSpec.Pressure(4), new Vector2(0f, 2.2f), new Vector2(0f, -1.4f), 1f, null);
        t.Ignite();
        Advance(.05f);
        Check("a delivered tide_attack_jet.png (128 x 320 cells = 2.5 u): the water jet body comes from it", t.BodyRenderer.sprite != null && t.BodyRenderer.sprite.texture == tideJet && Mathf.Abs(t.BodyRenderer.sprite.bounds.size.y - 2.5f) < .01f);
        t.Cancel();
        var w = AttackWave.Arm(WaveSpec.Surf(4), new Vector2(0f, 3f), new Vector2(0f, -3f), 1f, null);
        Check("a delivered tide_attack_wave.png is used for the gap chevrons in the tell", w.MarkerA.sprite != null && w.MarkerA.sprite.texture == tideWave);
        w.Ignite();
        Advance(.1f);
        var body = w.LeftRenderer.sprite;
        Check("... the band (512 x 96 = 4 x .75 u tiles; drawn " + w.DrawnHeight.ToString("F2") + " thick on a " + (w.HitHalf * 2f).ToString("F2") + " hit) and the two caps come from it",
              body != null && body.texture == tideWave && Mathf.Abs(body.bounds.size.x - 4f) < .01f && Mathf.Abs(body.bounds.size.y - .75f) < .01f && w.DrawnHeight <= w.HitHalf * 4f &&
              w.CapLRenderer.enabled && w.CapLRenderer.sprite.texture == tideWave && w.CapRRenderer.enabled && w.CapRRenderer.sprite.texture == tideWave);
        AttackArt.Clear();
        AttackHazardArt.Forget();
        EliteSystem.Clear();
        Check("with the files gone the procedural drawing is back", !AttackHazardArt.JetArt(3) && !AttackHazardArt.WaveArt(4));
    }
}
