using System.Collections.Generic;
using UnityEngine;
using static AttackTestKit;

// THE FAIRNESS CONTRACT FR1-FR9 AND THE PINK CUE, FOR THE BLAST AND THE STRIKE CORES
// (docs/world-attacks-design.md 0.1 / 0.2; plan phases 1c + 1d). Each later themed attack (jet, band, lash)
// extends this suite.
//
//   * FR1 tells: >= .7 s from the real brain's windup, the footprint previewed >= .4 s before it goes live,
//     none too short (AttackPreview counters); aim locked at the tell (the crack, the lane do not follow the pilot)
//   * FR3 live time / speed: strike <= .25 s, ring <= 2.8 u/s
//   * FR4 safe corridor: at every moment from 1.8 u out to the pilot's range the ring's crack leaves a free chord
//     >= 1.4 u between its bars (clipped by the rails), for pilots across the lane; the strike lanes of a pattern
//     leave corridors >= 1.4 u, one within reach of the pilot's lane
//   * firing rules: starts only >= MinFireAbove above and >= MinFireDistance from the pilot
//   * FR6 budget: the dodge bot's hit rate on the themed fixtures (a short run here; AttackBudgetTest rolls 2000)
//   * FR7 threat weights count against the shot budget
//   * the pink cue (PC1-PC3) on the drawn pixels of every sprite: edge, core, >= 30% pink-family or white, no red, material
//     <= half and clear of the pickup hues; bold keyline on bright worlds; the hit box matches the drawn footprint
//   * the art slot (frost_attack_ring.png, <w>_attack_strike.png) is used automatically when present
public static partial class AttackFairnessTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ATKFAIR] PASS  " : "[ATKFAIR] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public const float MinCorridor = 1.4f;

    public static int Execute()
    {
        fails = 0;
        using (new TestHarness.Sandbox())
        {
            try
            {
                TellsFromTheRealBrain();
                BlastCorridor();
                StrikeCorridor();
                ThreatWeights();
                DrawnPixels();
                BoldOnBrightWorlds();
                HitMatchesDrawing();
                ArtSlots();
                BudgetRows();
                VerdantEliteSuite();   // AttackFairnessTestVerdantElites.cs: the four new Verdant elites
                JetWaveSuite();   // AttackFairnessTestJetWave.cs: phases 1a (AttackJet) and 1b (AttackWave)
            }
            finally
            {
                AttackTestKit.Cleanup();
                AttackBudgetScenarios.Cleanup();
            }
        }
        Debug.Log("[ATKFAIR] failures: " + fails);
        return fails;
    }

    static Vector2 Dir(float rad) => new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));

    // ---- FR1 / FR3 / firing rules, through EnemyBrain -------------------------------------------------------

    sealed class Seen
    {
        public AttackHazard hz;
        public float tellStart = -1f, liveStart = -1f, liveEnd = -1f, tellSeconds, laneAtTell, laneAtLive, gapAtTell, gapAtLive;
        public float shooterAbove, shooterDistance;
        public float frontSeconds = -1f, gapWidth, gapLeft, gapRight;   // waves: the fall from its start to the pilot's row, the gap as locked
        public bool wasLive;
    }

    static void TellsFromTheRealBrain()
    {
        foreach (string id in AttackBudgetScenarios.ThemedFixtures.HazardIds)
        {
            AttackBudgetScenarios.Reset();
            AttackPreview.ResetCounters();
            var sc = AttackBudgetScenarios.Make(id);
            var rng = new System.Random(77);
            sc.Begin(rng, new Vector2(.4f, -3f));
            var seen = new Dictionary<AttackHazard, Seen>();
            var finished = new List<Seen>();   // a pooled hazard is used again and again: one record per use
            float t = 0f;
            int maxLive = 0;
            for (int f = 0; f < 60 * 14 && t < 14f; f++)
            {
                sc.Step(DodgeBot.Dt);
                t += DodgeBot.Dt;
                int live = 0;
                foreach (var hz in AttackHazard.All)
                {
                    if (hz == null || !hz.Active) continue;
                    Seen s;
                    if (!seen.TryGetValue(hz, out s)) seen[hz] = s = new Seen { hz = hz };
                    var blast = hz as AttackBlast;
                    var strike = hz as AttackStrike;
                    var jet = hz as AttackJet;
                    var wave = hz as AttackWave;
                    // a new use of a pooled item: archive the last one
                    if (hz.State == AttackHazard.Phase.Tell && s.tellStart >= 0f && s.wasLive)
                    {
                        finished.Add(s);
                        seen[hz] = s = new Seen { hz = hz };
                    }
                    if (hz.State == AttackHazard.Phase.Tell && s.tellStart < 0f)
                    {
                        s.tellStart = t;
                        s.tellSeconds = hz.TellSeconds;
                        s.laneAtTell = strike != null ? strike.LaneX : (wave != null ? wave.GapX : 0f);
                        s.gapAtTell = blast != null ? blast.GapRad : (jet != null ? jet.AimDirection : 0f);
                        if (wave != null) { s.gapWidth = wave.GapWidth; s.gapLeft = wave.GapX - wave.GapWidth * .5f; s.gapRight = wave.GapX + wave.GapWidth * .5f; }
                        var sh = hz.Shooter != null ? (Vector2)hz.Shooter.transform.position : Vector2.zero;
                        s.shooterAbove = sh.y - AttackBudgetScenarios.Ship.position.y;
                        s.shooterDistance = Vector2.Distance(sh, AttackBudgetScenarios.Ship.position);
                    }
                    if (hz.State == AttackHazard.Phase.Live)
                    {
                        live++;
                        if (s.liveStart < 0f)
                        {
                            s.liveStart = t;
                            s.laneAtLive = strike != null ? strike.LaneX : (wave != null ? wave.GapX : 0f);
                            s.gapAtLive = blast != null ? blast.GapRad : (jet != null ? jet.AimDirection : 0f);
                            if (wave != null) s.frontSeconds = (wave.Y - wave.HitHalf - AttackBudgetScenarios.Ship.position.y) / (wave.FallSpeed + EliteSystem.Scroll * wave.Spec.ride);
                        }
                        s.liveEnd = t;
                        s.wasLive = true;
                    }
                }
                maxLive = Mathf.Max(maxLive, live);
                if (sc.Done && f > 60) break;
            }
            foreach (var kv in seen) finished.Add(kv.Value);
            int n = 0, short_ = 0, lockedWrong = 0, tooEarly = 0, tooClose = 0;
            float minTell = 99f, maxLiveFor = 0f, minAbove = 99f, minDist = 99f, minFront = 99f, minGap = 99f;
            int gapOut = 0;
            foreach (var s in finished)
            {
                if (!s.wasLive) continue;
                n++;
                float tell = s.liveStart - s.tellStart;
                minTell = Mathf.Min(minTell, tell);
                if (tell < AttackHazard.MinTellSeconds - .03f) short_++;
                if (s.hz is AttackStrike || s.hz is AttackJet) maxLiveFor = Mathf.Max(maxLiveFor, s.liveEnd - s.liveStart + DodgeBot.Dt);
                if (s.frontSeconds >= 0f) { minFront = Mathf.Min(minFront, s.frontSeconds); minGap = Mathf.Min(minGap, s.gapWidth); if (s.gapLeft < -BossRails.DrawnInnerEdge - .001f || s.gapRight > BossRails.DrawnInnerEdge + .001f) gapOut++; }
                if (Mathf.Abs(s.laneAtTell - s.laneAtLive) > .001f || Mathf.Abs(s.gapAtTell - s.gapAtLive) > .001f) lockedWrong++;
                minAbove = Mathf.Min(minAbove, s.shooterAbove);
                minDist = Mathf.Min(minDist, s.shooterDistance);
                if (s.shooterAbove < EnemyBrain.MinFireAbove - .05f) tooEarly++;
                if (s.shooterDistance < EnemyBrain.MinFireDistance - .05f) tooClose++;
            }
            Check(id + ": " + n + " hazards went live; the shortest tell was " + minTell.ToString("F2") + " s (>= " + AttackHazard.MinTellSeconds + ", FR1) in the real brain's windup", n >= 1 && short_ == 0);
            Check(id + ": every footprint was previewed >= " + AttackPreview.MinLead + " s before it went live (shortest " + AttackPreview.MinShown.ToString("F2") + " s; " +
                  AttackPreview.TooShort + " too short of " + AttackPreview.Shown + ")", AttackPreview.Shown >= n && AttackPreview.TooShort == 0 && AttackPreview.MinShown >= AttackPreview.MinLead);
            if (id.Contains("eruption") || id.Contains("icicle"))
                Check(id + ": a strike column was live for at most " + maxLiveFor.ToString("F2") + " s (<= " + AttackStrike.MaxLiveSeconds + ", FR3); " + maxLive + " columns at once",
                      maxLiveFor <= AttackStrike.MaxLiveSeconds + .05f && maxLive >= 2);
            if (id.Contains("jet") || id.Contains("ray"))
                Check(id + ": a jet was live for at most " + maxLiveFor.ToString("F2") + " s (<= " + AttackJet.MaxLiveSeconds + ", FR3)", maxLiveFor > .2f && maxLiveFor <= AttackJet.MaxLiveSeconds + .05f);
            if (id.Contains("wave") || id.Contains("scan"))
                Check(id + ": the band's fall from its start to the pilot's row took at least " + minFront.ToString("F2") + " s (>= " + AttackWave.MinFrontSeconds + ", FR3), its gap is at least " + minGap.ToString("F2") +
                      " u (>= " + AttackWave.MinGap + ") and always inside the rails (" + gapOut + " outside)", minFront >= AttackWave.MinFrontSeconds - .05f && minGap >= AttackWave.MinGap - .001f && gapOut == 0);
            Check(id + ": the aim is locked at the tell (lane / crack / direction / gap unchanged by the time it is live): " + lockedWrong + " moved", lockedWrong == 0);
            Check(id + ": it starts only >= " + EnemyBrain.MinFireAbove + " u above and >= " + EnemyBrain.MinFireDistance + " u from the pilot (least: " + minAbove.ToString("F2") + " / " + minDist.ToString("F2") + ")",
                  tooEarly == 0 && tooClose == 0);
            sc.End();
            AttackBudgetScenarios.Cleanup();
        }
    }

    // ---- FR4: the ring's crack -----------------------------------------------------------------------------------

    // The free chord (u) through the crack at the ring's current radius, edge to edge between hazards, clipped by the rails;
    // its two ends in `lo` / `hi` (world).
    static float FreeChord(AttackBlast hz, float rail, out Vector2 lo, out Vector2 hi)
    {
        float r = hz.Radius;
        Vector2 o = hz.Origin;
        float gap = hz.GapRad;
        var sh = hz.Footprint;
        System.Func<float, bool> blocked = ang =>
        {
            Vector2 p = o + Dir(ang) * r;
            return sh.Touches(p, 0f) || Mathf.Abs(p.x) > rail;
        };
        // the longest free run of the circle within 60 deg of the crack's middle (the middle itself may lie outside the rails when the crack is turned)
        lo = hi = o + Dir(gap) * r;
        float best = 0f;
        bool inRun = false;
        Vector2 runStart = Vector2.zero, last = Vector2.zero;
        for (float a = -60f; a <= 60.001f; a += .25f)
        {
            float ang = gap + a * Mathf.Deg2Rad;
            Vector2 p = o + Dir(ang) * r;
            if (blocked(ang))
            {
                if (inRun) { float c = Vector2.Distance(runStart, last); if (c > best) { best = c; lo = runStart; hi = last; } }
                inRun = false;
                continue;
            }
            if (!inRun) { inRun = true; runStart = p; }
            last = p;
        }
        if (inRun) { float c = Vector2.Distance(runStart, last); if (c > best) { best = c; lo = runStart; hi = last; } }
        return best;
    }

    static void BlastCorridor()
    {
        Fresh();
        float rail = BossRails.DrawnInnerEdge;
        float least = 99f, slowest = 0f;
        int rings = 0, frames = 0, bad = 0, slow = 0, offsetRings = 0;
        string worst = "", debug = "";
        float[] pilotX = { -rail + .3f, -1.2f, 0f, 1.2f, rail - .3f };
        float[] muzzleDx = { -1.1f, 0f, 1.1f };
        float[] muzzleDy = { 1.8f, 2.6f, 3.3f, 4.6f, 5.6f };
        float[] offsets = { 0f, 40f };
        foreach (float offset in offsets)
        foreach (float px in pilotX)
            foreach (float dx in muzzleDx)
                foreach (float dy in muzzleDy)
                {
                    Vector2 pilot = new Vector2(px, -3f), muzzle = new Vector2(Mathf.Clamp(px + dx, -rail + .2f, rail - .2f), -3f + dy);
                    float range = Vector2.Distance(muzzle, pilot);
                    if (range < EnemyBrain.MinFireDistance) continue;
                    Pilot.position = pilot;
                    var spec = dy > 3.4f ? BlastSpec.Wide(1) : BlastSpec.Standard(1);   // (a pilot farther than 3.4 u is met by the wide ring)
                    spec.gapOffsetDeg = offset;
                    var b = AttackBlast.Arm(spec, muzzle, pilot, 1f, null);
                    b.Ignite();
                    rings++;
                    if (offset != 0f) offsetRings++;
                    float upTo = Mathf.Min(b.Spec.reach, range + .3f);
                    bool timed = false;
                    for (int i = 0; i < 400 && b.State == AttackHazard.Phase.Live; i++)
                    {
                        EliteSystem.Step(Dt);
                        if (b.State != AttackHazard.Phase.Live || b.Radius < Mathf.Max(1.8f, range - 1f) || b.Radius > upTo) continue;   // (while the ring is within a unit of him)
                        float chord = FreeChord(b, rail, out Vector2 lo, out Vector2 hi);
                        frames++;
                        if (chord < least) { least = chord; worst = "offset " + offset + " pilot " + px.ToString("F1") + " muzzle " + muzzle.ToString("F1") + " r " + b.Radius.ToString("F2"); }
                        if (chord < MinCorridor - .02f)
                        {
                            bad++;
                            if (debug.Length < 900)
                                debug += "[offset " + offset + " pilot " + px.ToString("F2") + " muzzle " + muzzle.ToString("F2") + " range " + range.ToString("F2") + " r " + b.Radius.ToString("F2") + " gap " + (b.GapRad * Mathf.Rad2Deg).ToString("F1") +
                                         " chord " + chord.ToString("F2") + " lo " + lo.ToString("F2") + " hi " + hi.ToString("F2") + " bars " + b.Bars + "] ";
                        }
                        // the moment the ring reaches the pilot's range: how far he has to fly to be inside the crack, bodily
                        if (!timed && b.Radius >= range - .05f && range <= b.Spec.reach)
                        {
                            timed = true;
                            float d = DistanceToSegment(pilot, lo, hi) + 0f;
                            float need = Mathf.Max(0f, d + DodgeBot.ShipRadius + .05f);
                            // the whole of the tell after his reaction, plus the ring's trip out to him
                            float avail = 1f - DodgeBot.Reaction + (range - b.Spec.startRadius) / b.Spec.speed;
                            float tFly = FlightSeconds(need);
                            slowest = Mathf.Max(slowest, tFly);
                            if (tFly > avail) slow++;
                        }
                    }
                    b.Cancel();
                }
        Check("the ring's crack leaves a free chord of at least " + MinCorridor + " u (edge to edge between bars, inside the rails at " + rail.ToString("F2") + ") at every moment from a unit before the ring reaches the pilot (1.8 u out at least) to just past him, " +
              "for pilots across the lane, rings of every range up to the wide ring's 6 u, the crack aimed at him or turned 40 deg off him: " + rings + " rings, " + frames + " frames, least " + least.ToString("F2") + " u (" + worst + ") " + debug, rings >= 60 && offsetRings >= 30 && frames > 600 && bad == 0);
        Check("... and with the crack turned 40 deg off him he can fly into it, body clear of the bars, before the ring gets to him (slowest " + slowest.ToString("F2") + " s; " + slow + " too slow)", slow == 0);
        // the spec's own numbers satisfy the arithmetic the doc states
        var std = BlastSpec.Standard(1);
        Check("the standard cold blast: " + std.speed + " u/s (<= " + AttackBlast.MaxSpeed + "), reach " + std.reach + ", crack " + std.gapDeg + " deg: a chord of " +
              (2f * 1.8f * Mathf.Sin(std.gapDeg * .5f * Mathf.Deg2Rad)).ToString("F2") + " u even at the 1.8 u minimum range", std.speed <= AttackBlast.MaxSpeed && 2f * 1.8f * Mathf.Sin(std.gapDeg * .5f * Mathf.Deg2Rad) >= MinCorridor);
        Pilot.position = new Vector3(0f, -3f, 0f);
        EliteSystem.Clear();
    }

    static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b) => Mathf.Sqrt(HostileShots.SegmentDistanceSq(a, b, p));

    // ---- FR4: the strike lanes -------------------------------------------------------------------------------------

    // Seconds the ship needs to fly `d` u from rest (the dodge bot's model: .175 s up to 7 u/s).
    static float FlightSeconds(float d)
    {
        float ramp = DodgeBot.Speed / DodgeBot.Accel, rampDist = .5f * DodgeBot.Speed * ramp;
        return d <= rampDist ? Mathf.Sqrt(2f * d / DodgeBot.Accel) : ramp + (d - rampDist) / DodgeBot.Speed;
    }

    static void StrikeCorridor()
    {
        Fresh();
        float rail = BossRails.DrawnInnerEdge;
        float half = .18f, clearance = DodgeBot.ShipRadius + .05f;
        float[] lanes = new float[4];
        int patterns = 0, bad = 0, slow = 0;
        float least = 99f, farthest = 0f, slowest = 0f;
        for (int count = 1; count <= 4; count++)
            for (float px = -rail + .3f; px <= rail - .3f + .001f; px += .3f)
            {
                int n = StrikeLanes.Pick(px, count, AttackStrike.MinLaneSpacing, rail, half, lanes);
                patterns++;
                var xs = new List<float>();
                for (int i = 0; i < n; i++) xs.Add(lanes[i]);
                xs.Sort();
                // free intervals between the rails and the columns
                var gaps = new List<Vector2>();
                float edge = -rail;
                foreach (float x in xs) { gaps.Add(new Vector2(edge, x - half)); edge = x + half; }
                gaps.Add(new Vector2(edge, rail));
                float widest = 0f, nearest = 99f;
                foreach (var g in gaps)
                {
                    float w = g.y - g.x;
                    widest = Mathf.Max(widest, w);
                    // how far the pilot has to fly to be fully inside a corridor that wide, the ship's body clear of both columns
                    if (w >= MinCorridor) nearest = Mathf.Min(nearest, Mathf.Abs(Mathf.Clamp(px, g.x + clearance, g.y - clearance) - px));
                }
                // every pair of neighbouring columns is at least the minimum spacing apart
                bool spaced = true;
                for (int i = 1; i < xs.Count; i++) spaced &= xs[i] - xs[i - 1] >= AttackStrike.MinLaneSpacing - .001f;
                if (n >= 2) least = Mathf.Min(least, widest);
                if (widest < MinCorridor || !spaced) bad++;
                if (nearest < 99f)
                {
                    farthest = Mathf.Max(farthest, nearest);
                    slowest = Mathf.Max(slowest, FlightSeconds(nearest));
                    if (FlightSeconds(nearest) > AttackHazard.MinTellSeconds - DodgeBot.Reaction) slow++;
                }
                else slow++;
            }
        Check("strike patterns of 1-4 lanes for a pilot anywhere in the lane (" + patterns + "): the widest corridor between columns and rails is always >= " + MinCorridor +
              " u (least with two or more columns " + least.ToString("F2") + " u), neighbouring columns >= " + AttackStrike.MinLaneSpacing + " u apart: " + bad + " violations", bad == 0);
        Check("... and the pilot can fly into such a corridor, the ship's body clear of the columns, in the " + (AttackHazard.MinTellSeconds - DodgeBot.Reaction).ToString("F2") +
              " s a .7 s tell leaves after a .25 s reaction (farthest " + farthest.ToString("F2") + " u = " + slowest.ToString("F2") + " s; " + slow + " patterns too slow)", slow == 0);
    }

    // ---- FR7 ------------------------------------------------------------------------------------------------------------

    static void ThreatWeights()
    {
        Fresh();
        var b = AttackBlast.Arm(BlastSpec.Standard(1), new Vector2(0f, 3f), new Vector2(0f, -1f), 1f, null);
        var s1 = AttackStrike.Arm(StrikeSpec.Standard(1), -1.5f, 0f, 1f, null);
        var s2 = AttackStrike.Arm(StrikeSpec.Standard(1), 1.5f, 0f, 1f, null);
        Check("hazards in their tell are not counted yet (the brain's reservation holds their place)", AttackHazard.LiveThreat == 0);
        b.Ignite(); s1.Ignite(); s2.Ignite();
        Check("a live ring counts as 2 shots against the roster budget and a live column as 1 (" + AttackHazard.LiveThreat + " for 1 ring + 2 columns)", AttackHazard.LiveThreat == 4);
        Check("EnemyThreat.LiveShots includes them (" + EnemyThreat.LiveShots + ", budget " + EnemyThreat.ShotBudget + ")", EnemyThreat.LiveShots >= 4);
        EliteSystem.Clear();
    }

    // ---- the pink cue on pixels ---------------------------------------------------------------------------------------

    static Color32[] Pixels(Sprite s)
    {
        var tex = s.texture;
        var r = s.rect;
        var all = tex.GetPixels32();
        var px = new Color32[(int)r.width * (int)r.height];
        for (int y = 0; y < (int)r.height; y++)
            for (int x = 0; x < (int)r.width; x++)
                px[y * (int)r.width + x] = all[((int)r.y + y) * tex.width + (int)r.x + x];
        return px;
    }

    // Width (u) of the opaque pixels of a sprite (its drawing, not its transparent margin).
    static float OpaqueWidth(Sprite s)
    {
        var px = Pixels(s);
        int w = (int)s.rect.width, h = (int)s.rect.height, x0 = w, x1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * w + x].a > 24 && !IsKey(px[y * w + x])) { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); }
        return x1 < x0 ? 0f : (x1 - x0 + 1) / s.pixelsPerUnit;
    }

    static readonly float[] PickupHues = { 178f, 82f, 259f, 37f };

    // Over the opaque pixels of a sprite: the share in the pink family or white, red-band pixels, pickup-hue pixels
    // that are too saturated (PC3: > .65 within 25 deg), the share that is the world's material ramp, and the share of
    // silhouette-edge pixels that are pink-white (PC1; the bold keyline ring itself is not counted).
    struct Pixels_
    {
        public ShotSkinTest.Audit audit;
        public int pickupHot, material, edgePink, edge, keyline;
        public float MaterialShare => audit.opaque > 0 ? material / (float)audit.opaque : 0f;
        public float EdgePink => edge > 0 ? edgePink / (float)edge : 1f;
    }

    static bool IsKey(Color32 c) => c.a > 0 && c.r == ShotOutline.BoldKey.r && c.g == ShotOutline.BoldKey.g && c.b == ShotOutline.BoldKey.b;

    static bool PinkOrWhite(Color32 c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        float deg = h * 360f;
        return (s < .2f && v > .85f) || (s > .3f && deg >= HostileShotPalette.HueMin - 8f && deg <= HostileShotPalette.HueMax + 8f);
    }

    static Pixels_ Audit(Sprite sp, int world)
    {
        var px = Pixels(sp);
        int w = (int)sp.rect.width, h = (int)sp.rect.height;
        var ramp = AttackHazardArt.RampOf(world);
        var res = new Pixels_();
        // the keyline ring is not the drawing: audit the rest
        var clean = new Color32[px.Length];
        for (int i = 0; i < px.Length; i++) { if (IsKey(px[i])) { res.keyline++; continue; } clean[i] = px[i]; }
        res.audit = ShotSkinTest.AuditPixels(clean);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = clean[y * w + x];
                if (c.a <= 24) continue;
                Color.RGBToHSV(c, out float hh, out float s, out float v);
                float deg = hh * 360f;
                foreach (float p in PickupHues)
                {
                    float gap = Mathf.Abs(deg - p); gap = Mathf.Min(gap, 360f - gap);
                    if (gap < 25f && s > .65f && v > .25f) { res.pickupHot++; break; }
                }
                if ((c.r == ramp.light.r && c.g == ramp.light.g && c.b == ramp.light.b) || (c.r == ramp.dark.r && c.g == ramp.dark.g && c.b == ramp.dark.b)) res.material++;
                // the silhouette edge: a neighbour inside the sprite that is empty (or the keyline ring); the canvas border is a tile seam, not an edge
                bool edge = false;
                for (int k = 0; k < 4 && !edge; k++)
                {
                    int nx = x + (k == 0 ? -1 : k == 1 ? 1 : 0), ny = y + (k == 2 ? -1 : k == 3 ? 1 : 0);
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    var n = px[ny * w + nx];
                    edge = n.a <= 24 || IsKey(n);
                }
                if (edge) { res.edge++; if (PinkOrWhite(c)) res.edgePink++; }
            }
        return res;
    }

    static IEnumerable<KeyValuePair<string, Sprite>> AllSprites(int world, bool bold)
    {
        for (int f = 0; f < 2; f++)
        {
            yield return new KeyValuePair<string, Sprite>("ring bar " + f, AttackHazardArt.RingBarProcedural(world, f, bold));
            yield return new KeyValuePair<string, Sprite>("ring gap marker " + f, AttackHazardArt.GapMarker(world, f, bold));
        }
        foreach (StrikeStyle st in System.Enum.GetValues(typeof(StrikeStyle)))
        {
            for (int f = 0; f < 2; f++)
            {
                yield return new KeyValuePair<string, Sprite>(st + " column " + f, AttackHazardArt.ColumnProcedural(world, st, f, bold, false));
                yield return new KeyValuePair<string, Sprite>(st + " glyph " + f, AttackHazardArt.Glyph(st, world, f, bold, false));
            }
            for (int f = 0; f < 3; f++)
                yield return new KeyValuePair<string, Sprite>(st + " burst " + f, AttackHazardArt.Burst(world, st, f, bold));
            if (AttackHazardArt.HasTip(st))
                yield return new KeyValuePair<string, Sprite>(st + " tip", AttackHazardArt.ColumnProcedural(world, st, 0, bold, true));
        }
        // the jet (every preset spec at its nominal and its reaching length, every frame), its flare and sparks, and the wave's strips
        foreach (var spec in new[] { JetSpec.Flame(world), JetSpec.Pressure(world), JetSpec.Ray(world), JetSpec.Lance(world) })
            foreach (float len in new[] { spec.length, spec.maxLength })
            {
                float tip = spec.IsCone ? spec.baseHalf + (spec.tipHalf - spec.baseHalf) * len / spec.length : spec.baseHalf;
                for (int f = 0; f < AttackHazardArt.JetProceduralFrames; f++)
                    yield return new KeyValuePair<string, Sprite>(spec.style + " jet " + len.ToString("F1") + " u frame " + f,
                        AttackHazardArt.JetBody(world, spec.style, f, len, spec.baseHalf, tip, bold, out _, out _, out _));
            }
        foreach (JetStyle js in System.Enum.GetValues(typeof(JetStyle)))
        {
            for (int st = 0; st < 3; st++) yield return new KeyValuePair<string, Sprite>(js + " nozzle flare " + st, AttackHazardArt.Flare(world, js, st, bold));
            for (int st = 0; st < 3; st++) yield return new KeyValuePair<string, Sprite>(js + " tip sparks " + st, AttackHazardArt.Sparks(world, js, st, bold));
        }
        foreach (WaveStyle ws in System.Enum.GetValues(typeof(WaveStyle)))
            for (int f = 0; f < AttackHazardArt.WaveProceduralFrames; f++)
                yield return new KeyValuePair<string, Sprite>(ws + " wave strip " + f, AttackHazardArt.WaveProcedural(world, ws, f, bold));
    }

    static void DrawnPixels()
    {
        Fresh();
        int checkedSprites = 0, lowPink = 0, red = 0, hot = 0, heavy = 0, softEdge = 0, keylineWrong = 0;
        string worstPink = "", worstEdge = "", worstMat = "", hotWhere = "";
        float minPink = 9f, minEdge = 9f, maxMat = 0f;
        for (int world = 0; world < ShotSkins.Worlds; world++)
            for (int b = 0; b < 2; b++)
                foreach (var kv in AllSprites(world, b == 1))
                {
                    var a = Audit(kv.Value, world);
                    checkedSprites++;
                    string tag = kv.Key + " w" + world + (b == 1 ? " bold" : "");
                    float pink = a.audit.PinkShare;
                    if (pink < minPink) { minPink = pink; worstPink = tag; }
                    if (pink < ShotSkinTest.MinPinkShare) lowPink++;
                    if (a.audit.red > 0) red++;
                    if (a.pickupHot > 0) { hot++; if (hotWhere.Length < 120) hotWhere += tag + "; "; }
                    float mat = a.MaterialShare;
                    if (mat > maxMat) { maxMat = mat; worstMat = tag; }
                    if (mat > .5f) heavy++;
                    if (a.EdgePink < minEdge) { minEdge = a.EdgePink; worstEdge = tag; }
                    if (a.EdgePink < .8f) softEdge++;
                    if (b == 1 && a.keyline == 0) keylineWrong++;
                    if (b == 0 && a.keyline != 0) keylineWrong++;
                }
        Check(checkedSprites + " procedural sprites (5 worlds x ring bar / marker / three strike styles x column, glyph, burst, tip; standard and bold): at least " + (ShotSkinTest.MinPinkShare * 100f) +
              "% of every one's pixels are pink-family (312-326) or white; the least is " + (minPink * 100f).ToString("F0") + "% (" + worstPink + ")", lowPink == 0 && checkedSprites >= 150);
        Check("... none has a player-red pixel, and none is a saturated pickup hue (178 / 82 / 259 / 37 within 25 deg at saturation > .65) (" + red + " red, " + hot + " hot: " + hotWhere + ")", red == 0 && hot == 0);
        Check("... the world's material is at most half the pixels (most: " + (maxMat * 100f).ToString("F0") + "%, " + worstMat + ")", heavy == 0);
        Check("... the silhouette edge is the pink-white stroke (least " + (minEdge * 100f).ToString("F0") + "%, " + worstEdge + "; " + softEdge + " under 80%)", softEdge == 0);
        Check("... the bold variant carries the near-black keyline ring and the standard one does not (" + keylineWrong + " wrong)", keylineWrong == 0);
        // the flicker: the two core frames differ, the pink share stays
        var f0 = Pixels(AttackHazardArt.RingBarProcedural(1, 0, false));
        var f1 = Pixels(AttackHazardArt.RingBarProcedural(1, 1, false));
        int diff = 0;
        for (int i = 0; i < f0.Length; i++) if (!f0[i].Equals(f1[i])) diff++;
        Check("the ring bar's two frames differ in " + diff + " core pixels (stepped flicker, not a smooth breath)", diff >= 10);
        // the bar is long, pointed and thin (PC4), never a round disc
        var bar = AttackHazardArt.RingBarProcedural(1, 0, false);
        Check("the bar is " + bar.rect.width + " x " + bar.rect.height + " px: elongated and pointed (" + (bar.rect.width / bar.rect.height).ToString("F1") + ":1), not a disc", bar.rect.width / bar.rect.height >= 2.5f);
    }

    static void BoldOnBrightWorlds()
    {
        Fresh();
        ShotOutline.Bold = true;
        var spec = BlastSpec.Standard(1);
        var b = AttackBlast.Arm(spec, new Vector2(0f, 3f), new Vector2(0f, -1.2f), 1f, null);
        b.Ignite();
        Advance(.1f);
        var sr = b.BarRenderer(0);
        bool boldBars = HasKeyline(sr.sprite);
        var s = AttackStrike.Arm(StrikeSpec.Standard(1), 0f, -1.5f, 1f, null);
        s.Ignite();
        Advance(.05f);
        bool boldCol = HasKeyline(s.BodyRenderer(0).sprite);
        Check("forced bold (a bright backdrop): the ring's bars and the column wear the near-black keyline ring around the pink-white stroke", boldBars && boldCol);
        EliteSystem.Clear();
        Fresh();
        ShotOutline.Bold = false;
        b = AttackBlast.Arm(spec, new Vector2(0f, 3f), new Vector2(0f, -1.2f), 1f, null);
        b.Ignite();
        Advance(.1f);
        Check("standard (a dark backdrop): no keyline ring, the pink-white stroke alone", !HasKeyline(b.BarRenderer(0).sprite));
        EliteSystem.Clear();
        // the real switch: a world whose backdrop Spec.Bright is true turns the bold style on by itself
        int bright = -1, dark = -1;
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            var spec2 = BackdropCatalog.For(WorldManager.Worlds[w].displayName);
            if (spec2.Bright && bright < 0) bright = w;
            if (!spec2.Bright && dark < 0) dark = w;
        }
        bool brightOk = true, darkOk = true;
        string note = "";
        if (bright >= 0)
        {
            Fresh();
            ShotOutline.Bold = null;
            PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, bright);
            var bb = AttackBlast.Arm(BlastSpec.Standard(bright), new Vector2(0f, 3f), new Vector2(0f, -1.2f), 1f, null);
            bb.Ignite(); Advance(.05f);
            brightOk = ShotOutline.UseBold && HasKeyline(bb.BarRenderer(0).sprite);
            note += "bright " + WorldManager.Worlds[bright].displayName + " ";
            EliteSystem.Clear();
        }
        if (dark >= 0)
        {
            Fresh();
            ShotOutline.Bold = null;
            PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, dark);
            var bb = AttackBlast.Arm(BlastSpec.Standard(dark), new Vector2(0f, 3f), new Vector2(0f, -1.2f), 1f, null);
            bb.Ignite(); Advance(.05f);
            darkOk = !ShotOutline.UseBold && !HasKeyline(bb.BarRenderer(0).sprite);
            note += "dark " + WorldManager.Worlds[dark].displayName;
            EliteSystem.Clear();
        }
        Check("on a world whose backdrop Spec.Bright is true the ring wears the bold keyline by itself, on a dark one it does not (" + note + ")", bright >= 0 && dark >= 0 && brightOk && darkOk);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 0);
        ShotOutline.Bold = null;
    }

    static bool HasKeyline(Sprite s)
    {
        if (s == null) return false;
        foreach (var c in Pixels(s)) if (IsKey(c)) return true;
        return false;
    }

    // ---- the hit box matches the drawing ---------------------------------------------------------------------------------

    static void HitMatchesDrawing()
    {
        Fresh();
        var spec = BlastSpec.Standard(1);
        var b = AttackBlast.Arm(spec, new Vector2(0f, 3f), new Vector2(0f, -1.2f), 1f, null);
        b.Ignite();
        Advance(.8f);
        int drawn = 0, centred = 0, longEnough = 0, thick = 0;
        for (int k = 0; k < AttackBlast.MaxBars; k++)
        {
            var sr = b.BarRenderer(k);
            if (!sr.enabled) continue;
            drawn++;
            AttackBlast.BarAt(in spec, b.Origin, b.GapRad, b.Radius, k, out Vector2 a, out Vector2 e, out float ang);
            Vector2 c = (a + e) * .5f;
            if (Vector2.Distance(sr.transform.position, c) < .01f) centred++;
            float len = sr.sprite.bounds.size.x * sr.transform.lossyScale.x, th = sr.sprite.bounds.size.y * sr.transform.lossyScale.y;
            float hitLen = Vector2.Distance(a, e);
            if (len >= hitLen - .01f && len <= hitLen * 1.25f + .05f) longEnough++;
            if (th >= spec.barHalf * 2f && th <= spec.barHalf * 2f * 2f + .02f) thick++;   // drawn thicker than it hits, never past twice (the doc's draw factor)
        }
        Check("each of the ring's " + b.Footprint.PolyCount + " hit polygons has one drawn bar (" + drawn + "), centred on its hit bar (" + centred + "), as long as it (" + longEnough + ") and between 1x and 2x its thickness (" + thick + ")",
              drawn == b.Footprint.PolyCount && centred == drawn && longEnough == drawn && thick == drawn);
        b.Cancel();

        var s = AttackStrike.Arm(StrikeSpec.Standard(1), 1f, -1.5f, 1f, null);
        Check("in its tell the preview outline's four corners ARE the hit polygon's four corners (FR2: the same footprint)",
              SameCorners(s.Footprint));
        s.Ignite();
        Advance(.05f);
        float x0 = 99f, x1 = -99f, y0 = 99f, y1 = -99f, drawnW = 0f;
        for (int i = 0; i < AttackStrike.MaxTiles; i++)
        {
            var sr = s.BodyRenderer(i);
            if (!sr.enabled) continue;
            var bb = sr.bounds;
            x0 = Mathf.Min(x0, bb.min.x); x1 = Mathf.Max(x1, bb.max.x); y0 = Mathf.Min(y0, bb.min.y); y1 = Mathf.Max(y1, bb.max.y);
            drawnW = OpaqueWidth(sr.sprite);
        }
        var tipR = s.transform.Find("Tip").GetComponent<SpriteRenderer>();
        if (tipR.enabled) { y0 = Mathf.Min(y0, tipR.bounds.min.y); }
        var hit = s.Footprint.Bounds();
        float hw = hit.width;
        Check("the column's drawing is " + drawnW.ToString("F2") + " u wide on a " + hw.ToString("F2") + " wide hit (between 1x and 2x), centred on the lane (" + ((x0 + x1) * .5f).ToString("F2") + " vs 1.00), " +
              "from the spear point at its foot (" + y0.ToString("F2") + " vs " + hit.yMin.ToString("F2") + ") up to or past its top (" + y1.ToString("F2") + " vs " + hit.yMax.ToString("F2") + ")",
              drawnW >= hw && drawnW <= hw * 2f + .02f && Mathf.Abs((x0 + x1) * .5f - 1f) < .02f && Mathf.Abs(y0 - hit.yMin) < .06f && y1 >= hit.yMax - .01f);
        s.Cancel();
    }

    static bool SameCorners(AttackShape sh)
    {
        if (sh.PolyCount != 1 || sh.LoopCount != 1 || sh.LoopLength(0) != 4 || sh.PolyLength(0) != 4) return false;
        for (int i = 0; i < 4; i++) if ((sh.LoopAt(0, i) - sh.PolyAt(0, i)).sqrMagnitude > 1e-8f) return false;
        return true;
    }

    // ---- the art slots ------------------------------------------------------------------------------------------------------

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

    static void ArtSlots()
    {
        Fresh();
        Check("without the art files the procedural drawing is used (no frost ring or strike atlas)", !AttackArt.Has(1, "ring") && !AttackArt.Has(1, "strike") && !AttackHazardArt.StrikeArt(1));
        var ring = Atlas(1024, 128, new Color32(255, 80, 216, 255));
        var strike = Atlas(768, 512, new Color32(255, 120, 220, 255));
        AttackArt.Inject(1, "ring", ring);
        AttackArt.Inject(1, "strike", strike);
        AttackHazardArt.Forget();
        var spec = BlastSpec.Standard(1);
        var b = AttackBlast.Arm(spec, new Vector2(0f, 3f), new Vector2(0f, -1.2f), 1f, null);
        b.Ignite();
        Advance(.1f);
        Check("a delivered frost_attack_ring.png is used automatically for the bars and the glyph (bar texture is the atlas: " + (b.BarRenderer(0).sprite.texture == ring) + ")",
              b.BarRenderer(0).sprite != null && b.BarRenderer(0).sprite.texture == ring);
        var s = AttackStrike.Arm(StrikeSpec.Standard(1), 0f, -1.5f, 1f, null);
        Check("... and a delivered frost_attack_strike.png for the lane glyph", s.GlyphRenderer.sprite != null && s.GlyphRenderer.sprite.texture == strike);
        s.Ignite();
        Advance(.1f);
        var body = s.BodyRenderer(0).sprite;
        Check("... and the column body (128 x 384 cells = 3 u tiles; " + s.TilesShown + " tiles stack up the lane)", body != null && body.texture == strike && Mathf.Abs(body.bounds.size.y - 3f) < .01f && s.TilesShown >= 3 && s.TilesShown <= 5);
        AdvanceUntil(() => s.State != AttackHazard.Phase.Live, 1f);
        Advance(Dt * 2f);
        Check("... and the ground burst from its row", s.BurstRenderer.sprite != null && s.BurstRenderer.sprite.texture == strike);
        AttackArt.Clear();
        AttackHazardArt.Forget();
        EliteSystem.Clear();
    }

    // ---- FR6: the dodge bot (a short run; AttackBudgetTest rolls the 2000) -------------------------------------------------------

    static void BudgetRows()
    {
        foreach (string id in AttackBudgetScenarios.ThemedFixtures.Ids)
        {
            var r = AttackBudgetTest.Measure(id, 300);
            string why;
            bool ok = AttackBudgetTest.WithinBudget(id, r, out why);
            Check("the dodge bot against " + id + " (300 rolls; standing still: " + r.GhostRate.ToString("P0") + "): " + why, ok);
        }
    }
}
