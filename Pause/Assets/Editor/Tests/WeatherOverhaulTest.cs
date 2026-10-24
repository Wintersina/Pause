using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// THE WEATHER OVERHAUL (docs/weather-overhaul.md): Space's SOLAR FLARES (the Sweep shape), the always-on WIND (breeze, shear,
// telegraphed gusts), the stronger pushes, and the FAIRNESS rule for boss fights (every boss-fight bullet drifts together, capped
// and slew-limited, so a wave's gap moves with its bullets) with the proof that WaveGaps' gaps stay reachable at the maximum drift.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod WeatherOverhaulTest.Run
public static class WeatherOverhaulTest
{
    static int fails;
    static void Check(string what, bool ok, string extra = "")
    {
        Debug.Log((ok ? "[WX] PASS  " : "[WX] FAIL  ") + what + (extra.Length > 0 ? "  [" + extra + "]" : ""));
        if (!ok) fails++;
    }
    static void Info(string what) { Debug.Log("[WX] INFO  " + what); }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    static readonly List<Object> made = new List<Object>();

    static HazardDef Def(string id)
    {
        foreach (var e in WorldWeatherTable.Entries) foreach (var h in e.hazards) if (h.id == id) return h;
        return null;
    }

    static WorldHazards NewManager()
    {
        if (WorldHazards.Instance != null) Object.DestroyImmediate(WorldHazards.Instance.gameObject);
        var go = new GameObject("~WeatherOverhaulTest");
        made.Add(go);
        return go.AddComponent<WorldHazards>();
    }

    static HazardField Live(HazardDef d, float side, float sweepX, float t = -1f)
    {
        var f = new HazardField { def = d, phase = HazardField.Phase.Live, side = side, sweepX = sweepX, viewBottom = -6f, viewTop = 6f, laneHalf = 4f, liveFor = 30f };
        f.t = t >= 0f ? t : d.warning + 3f;
        return f;
    }

    static GameObject Body(Vector2 at)
    {
        var go = new GameObject("body");
        go.tag = "Enimey";
        go.transform.position = at;
        var col = go.AddComponent<CircleCollider2D>(); col.radius = .3f;
        go.AddComponent<ClearTarget>();
        var fp = go.AddComponent<SpawnFootprint>(); fp.half = new Vector2(.3f, .3f);
        made.Add(go);
        return go;
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            SpaceIsSolarFlares();
            SweepModel();
            FlareCrossing();
            WindData();
            WindModel();
            GustSchedule();
            WindPushesTheBoard();
            BossShotsDriftTogether();
            WaveGapsStayReachable();
            Visuals();
        }
        finally
        {
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
            HostileShots.ResetCounters();
            WorldHazards.BossRunningOverride = null;
            WorldHazards.SituationOverride = null;
            WorldHazards.ClearAll();
            WorldHazards.PlayerOffset = Vector2.zero;
            EliteSystem.PlayerOverride = null;
            WeatherWind.Reset(null);
        }
        Debug.Log("[WX] " + (fails == 0 ? "ALL PASS" : fails + " FAILED"));
        return fails;
    }

    // ---- Space: the old weather is gone, the flares are in ----
    static void SpaceIsSolarFlares()
    {
        var space = WorldWeatherTable.ForWorld(0);
        Check("Space has exactly the two flare hazards", space.hazards.Length == 2 && space.hazards[0].id == "solar_flare" && space.hazards[1].id == "flare_storm");
        bool gone = true;
        foreach (var e in WorldWeatherTable.Entries) foreach (var h in e.hazards) gone &= h.id != "gravity_well" && h.id != "solar_wind";
        Check("the gravity well and the ion wind are gone", gone);
        foreach (var h in space.hazards)
        {
            Check(h.id + ": a sweeping band with a telegraph of at least 2.4 s", h.shape == HazardShape.Sweep && h.warning >= 2.4f, h.warning.ToString("F1"));
            Check(h.id + ": art rows are wired", h.artRow >= 0);
            Check(h.id + ": elites take a small share", h.elite.gain <= .5f);
        }
        Check("the flare train is exactly two bands and the lone flare one", Def("flare_storm").bands == 2 && Def("solar_flare").bands == 1);
        foreach (var h in space.hazards) Check(h.id + ": never more than 2 flare waves", h.bands <= 2, h.bands.ToString());
        Check("Space has no wind (its weather is the flares)", space.wind == null);
        for (int w = 1; w < WorldWeatherTable.Entries.Length; w++)
            Check(WorldWeatherTable.Entries[w].world + " has a wind", WorldWeatherTable.Entries[w].wind != null);
        Check("the table is valid", WorldWeatherTable.Validate().Length == 0, WorldWeatherTable.Validate());
    }

    static void SweepModel()
    {
        var d = Def("solar_flare");
        var f = Live(d, 1f, 0f);
        Check("flare: shoves a body along the way it travels", HazardForce.Sample(f, new Vector2(0f, 0f), HazardTarget.Body, out Vector2 v) && v.x > 4f, v.ToString());
        HazardForce.Sample(Live(d, -1f, 0f), Vector2.zero, HazardTarget.Body, out Vector2 vl);
        Check("... and the other way when it travels left", vl.x < -4f);
        Check("flare: nothing outside the band", !HazardForce.Sample(f, new Vector2(d.size, 0f), HazardTarget.Body, out _) && !HazardForce.Sample(f, new Vector2(-d.size, 0f), HazardTarget.Body, out _));
        HazardForce.Sample(f, new Vector2(.6f, 0f), HazardTarget.Body, out Vector2 lead);
        HazardForce.Sample(f, new Vector2(-.6f, 0f), HazardTarget.Body, out Vector2 trail);
        Check("flare: the shocked leading edge shoves harder than the tail", lead.x > trail.x * 1.2f, lead.x.ToString("F1") + " vs " + trail.x.ToString("F1"));
        HazardForce.Sample(f, new Vector2(0f, 3f), HazardTarget.Hostile, out Vector2 hv);
        Check("flare: a bullet is flung at least twice as hard as the old ion wind did (2.4 u/s)", hv.x > 4.8f, hv.x.ToString("F1"));
        var warned = Live(d, 1f, 0f); warned.phase = HazardField.Phase.Warning; warned.t = 1f;
        Check("flare: a warned flare exerts nothing", !HazardForce.Sample(warned, Vector2.zero, HazardTarget.Body, out _));
        var train = Live(Def("flare_storm"), 1f, 0f);
        var td = train.def;
        Check("train: the second band, one gap behind, shoves too", HazardForce.Sample(train, new Vector2(-td.bandGap, 0f), HazardTarget.Body, out Vector2 second) && second.x > 0f);
        Check("train: and there is quiet between bands (you can step through)", !HazardForce.Sample(train, new Vector2(-td.bandGap * .5f, 0f), HazardTarget.Body, out _));
        Check("flare bounds cover the whole train", train.Bounds.width >= td.bandGap * (td.bands - 1) + td.size && train.Bounds.Contains(new Vector2(-td.bandGap * (td.bands - 1), 0f)));
        Check("rigid part: positive with the side, zero for a swirl", HazardForce.RigidX(f, HazardTarget.Hostile) > 0f && HazardForce.RigidX(Live(Def("tornado"), 1f, 0f), HazardTarget.Hostile) == 0f);
    }

    // A whole flare, through the real manager: telegraph (nothing moves), the crossing (bodies and bullets are hurled, the ship moves only a little),
    // and the end (it leaves; the ship's offset springs back).
    static void FlareCrossing()
    {
        foreach (string id in new[] { "solar_flare", "flare_storm" })
        {
            var m = NewManager(); WorldHazards.ClearAll();
            WorldHazards.BossRunningOverride = false; WeatherWind.Reset(null);
            var player = new GameObject("player"); made.Add(player);
            player.transform.position = new Vector3(0f, -4f);
            EliteSystem.PlayerOverride = player.transform;
            WorldHazards.PlayerOffset = Vector2.zero;
            var d = Def(id);
            m.Begin(d);
            var f = WorldHazards.Fields[0];
            f.side = 1f; f.sweepX = -(f.laneHalf + d.size * .5f);
            var shot = new GameObject("shot").AddComponent<WorldHazardsTest.StubShot>(); made.Add(shot.gameObject);
            shot.pos = new Vector2(-2.2f, 1f);
            HostileShots.Register(shot);
            var body = Body(new Vector2(-1.8f, 2f));
            Vector2 body0 = body.transform.position;
            Vector2 shot0 = shot.pos;
            // the telegraph: nothing moves for the whole warning, but something is drawn
            int warnFrames = Mathf.FloorToInt(d.warning * 60f) - 2;
            for (int i = 0; i < warnFrames; i++) { m.StepFields(Dt); WorldHazards.Apply(Dt); }
            Check(id + ": during the warning nothing is pushed", (Vector2)body.transform.position == body0 && shot.pos == shot0 && WorldHazards.PlayerOffset == Vector2.zero && f.phase == HazardField.Phase.Warning);
            m.Visuals.Draw(WorldHazards.Fields, true);
            Check(id + ": and the warning is drawn (" + m.Visuals.Drawn + " sprites)", m.Visuals.Drawn >= 8 && m.Visuals.Drawn <= HazardVisuals.Pool);
            var rock = Body(new Vector2(-1.8f, -1f)); rock.tag = "Astr";
            float rock0 = rock.transform.position.x, rockMax = 0f; bool stunnedSeen = false;
            HostileShots.ResetCounters();
            float peakOffset = 0f; int frames = 0; float maxBody = 0f, maxShot = 0f;
            while (f.Active && frames < 60 * 30)
            {
                m.StepFields(Dt); WorldHazards.Apply(Dt); frames++;
                peakOffset = Mathf.Max(peakOffset, WorldHazards.PlayerOffset.magnitude);
                maxBody = Mathf.Max(maxBody, body.transform.position.x - body0.x);
                if (!shot.popped) maxShot = Mathf.Max(maxShot, shot.pos.x - shot0.x);
                rockMax = Mathf.Max(rockMax, rock.transform.position.x - rock0);
                stunnedSeen |= WorldHazards.LastStunned > 0;
            }
            float crossing = (frames + warnFrames) * Dt;
            Check(id + ": the whole thing is over in " + crossing.ToString("F1") + " s", !f.Active && crossing < 14f && crossing > d.warning + 1.5f);
            Check(id + ": an enemy body is thrown " + maxBody.ToString("F1") + " u along the sweep", maxBody > 1.0f);
            Check(id + ": a hostile bullet the band touches is wiped (" + maxShot.ToString("F1") + " u pushed first)", shot.popped && HostileShots.Wiped >= 1);
            Check(id + ": an enemy body the band touches is stunned", stunnedSeen);
            Check(id + ": a rock is flung at least twice as far as the enemy ship", rockMax > maxBody * 2f, rockMax.ToString("F1") + " vs " + maxBody.ToString("F1"));
            Check(id + ": the body is alive, tagged and inside the lane", body != null && body.activeSelf && body.CompareTag("Enimey") && Mathf.Abs(body.transform.position.x) <= SpawnLane.LaneHalf);
            Check(id + ": the ship is moved only a little (" + peakOffset.ToString("F2") + " u <= cap " + WorldWeatherTable.PlayerCap + ")", peakOffset > .1f && peakOffset <= WorldWeatherTable.PlayerCap + 1e-3f);
            for (int i = 0; i < 180; i++) WorldHazards.Apply(Dt);
            Check(id + ": and it is back under the finger 3 s later", WorldHazards.PlayerOffset.magnitude < .03f, WorldHazards.PlayerOffset.magnitude.ToString("F3"));
            HostileShots.Unregister(shot);
        }
        WorldHazards.ClearAll();
        FlareFollowsTheSun();
        // a flare starts wholly off screen on the near side and ends wholly off the far side, for either direction
        var m2 = NewManager(); WorldHazards.ClearAll();
        bool ok = true;
        for (int i = 0; i < 20; i++)
        {
            WorldHazards.ClearAll();
            m2.Begin(Def("flare_storm"));
            var f = WorldHazards.Fields[0];
            ok &= Mathf.Abs(f.sweepX) >= f.laneHalf + f.def.size * .5f - .01f && Mathf.Sign(f.sweepX) == -f.side;
            float travel = f.liveFor * f.def.speed;
            float endLead = f.sweepX + f.side * travel - f.side * (f.def.bands - 1) * f.def.bandGap;
            ok &= Mathf.Abs(endLead) >= f.laneHalf + f.def.size * .5f - .02f && Mathf.Sign(endLead) == f.side;
        }
        Check("a flare train starts wholly off screen and ends wholly off the far edge", ok);
        WorldHazards.ClearAll();
    }

    // The flare is born at the sun: sun on the left of the backdrop -> it travels left to right, sun on the right -> right to left,
    // and the origin edge (where the telegraph glows and the band starts, wholly off screen) is the sun's side.
    static void FlareFollowsTheSun()
    {
        int left = 0, right = 0;
        for (int s = 1; s < 400 && (left == 0 || right == 0); s++)
        {
            if (SpaceSunPick.SideFor(s) < 0 && left == 0) left = s;
            if (SpaceSunPick.SideFor(s) > 0 && right == 0) right = s;
        }
        foreach (string id in new[] { "solar_flare", "flare_storm" })
            foreach (int seed in new[] { left, right })
            {
                SpaceSunPick.NewRun(seed);
                float sunSide = SpaceSunPick.SideFor(seed);
                var m = NewManager(); WorldHazards.ClearAll();
                Check(id + ": the sun art is installed", SpaceSunPick.FlareTravel.HasValue);
                bool ok = true;
                for (int i = 0; i < 12; i++)
                {
                    WorldHazards.ClearAll();
                    m.Begin(Def(id));
                    var f = WorldHazards.Fields[0];
                    ok &= f.side == -sunSide && Mathf.Sign(f.sweepX) == sunSide;
                }
                Check(id + ": sun on the " + (sunSide < 0 ? "left -> travels left to right" : "right -> travels right to left") + ", always", ok);
            }
        SpaceSunPick.NewRun();
        WorldHazards.ClearAll();
    }

    // ---- the wind ----
    static void WindData()
    {
        string problems = WorldWeatherTable.Validate();
        Check("every wind profile passes the fairness validation", problems.Length == 0, problems);
        bool pink = false;
        for (int w = 1; w < WorldWeatherTable.Entries.Length; w++)
        {
            pink |= WorldCue.IsPink(WindVisuals.CueColour(w));
            var p = WorldWeatherTable.Entries[w].wind;
            Check(WorldWeatherTable.Entries[w].world + " wind: telegraph >= 1.5 s, gust <= 6 u/s, gusts at least 5 s apart", p.warn >= WorldWeatherTable.MinWarning && p.gust <= 6f && p.every.x >= 5f);
            Check(WorldWeatherTable.Entries[w].world + " wind: the ship takes the smallest share", p.player.gain <= p.body.gain && p.player.gain <= .3f);
        }
        Check("no wind streak is pink outside Space", !pink);
    }

    static Vector2 Drift(WindProfile p, Vector2 from, float fall, float seconds, float clock0, float gustEnv, float side, out float verticalFall)
    {
        // a bullet falling at `fall` u/s: where does the wind move it (the wind's own displacement, not the fall)
        Vector2 at = from, shift = Vector2.zero;
        float dt = .02f; verticalFall = 0f;
        for (float t = 0f; t < seconds; t += dt)
        {
            Vector2 v = WeatherWind.Sample(p, at, HazardTarget.Hostile, clock0 + t, gustEnv, side, 1f);
            shift += v * dt;
            at += v * dt + new Vector2(0f, -fall * dt);
        }
        return shift;
    }

    static void WindModel()
    {
        var lines = new System.Text.StringBuilder();
        for (int w = 1; w < WorldWeatherTable.Entries.Length; w++)
        {
            var e = WorldWeatherTable.Entries[w]; var p = e.wind;
            // a bullet falling through the board: the breeze alone bends it; a gust bends it a lot
            float calmSum = 0f, gustSum = 0f; int n = 0;
            for (float x = -2f; x <= 2f; x += 1f)
                for (float clock = 0f; clock < 18f; clock += 3f)
                {
                    Vector2 c = Drift(p, new Vector2(x, 4f), 3.3f, 2.8f, clock, 0f, 1f, out _);
                    Vector2 g = Drift(p, new Vector2(x, 4f), 3.3f, 2.8f, clock, 1f, 1f, out _);
                    calmSum += c.magnitude; gustSum += g.magnitude; n++;
                }
            float calm = calmSum / n, gust = gustSum / n;
            lines.Append(e.world).Append(": breeze bends a falling bullet ").Append(calm.ToString("F2")).Append(" u, a gust ").Append(gust.ToString("F2")).Append(" u;  ");
            Check(e.world + ": the breeze alone bends a falling bullet (" + calm.ToString("F2") + " u over 2.8 s)", calm >= .5f);
            Check(e.world + ": a gust bends it much more (" + gust.ToString("F2") + " u)", gust >= 3.5f && gust >= calm * 2.5f);
            // body speed in a gust vs the old events
            Vector2 vg = WeatherWind.Sample(p, Vector2.zero, HazardTarget.Body, 3f, 1f, 1f, 1f);
            Check(e.world + ": a gust shoves a body (" + vg.magnitude.ToString("F1") + " u/s) but under the body cap", vg.magnitude <= WorldWeatherTable.BodyMaxSpeed);
        }
        Info(lines.ToString());
        // shear: layers of air flow opposite ways (Tide's currents, Storm's zigzag, Ember's thermals by column)
        foreach (int w in new[] { 4, 5 })
        {
            var p = WorldWeatherTable.Entries[w].wind; bool pos = false, neg = false;
            for (float y = -5f; y <= 5f; y += .25f)
            {
                var v = WeatherWind.Sample(p, new Vector2(0f, y), HazardTarget.Hostile, 4f, 0f, 1f, 1f);
                if (v.x > .3f) pos = true; if (v.x < -.3f) neg = true;
            }
            Check(WorldWeatherTable.Entries[w].world + ": air layers flow both ways (shear reverses direction)", pos && neg);
        }
        {
            var p = WorldWeatherTable.Entries[3].wind; bool up = false, down = false;
            for (float x = -3f; x <= 3f; x += .2f)
            {
                var v = WeatherWind.Sample(p, new Vector2(x, 0f), HazardTarget.Hostile, 4f, 0f, 1f, 1f);
                if (v.y > .3f) up = true; if (v.y < -.2f) down = true;
            }
            Check("Ember: thermal columns lift in some places and sink in others", up && down);
        }
        Check("no wind when fade is zero (calm start / boss intro)", WeatherWind.Sample(WorldWeatherTable.Entries[1].wind, Vector2.zero, HazardTarget.Hostile, 3f, 1f, 1f, 0f) == Vector2.zero);
    }

    static void GustSchedule()
    {
        for (int w = 1; w < WorldWeatherTable.Entries.Length; w++)
        {
            var e = WorldWeatherTable.Entries[w]; var p = e.wind;
            WeatherWind.Reset(p, 11 + w);
            float t = 0f, prevEnv = 0f, maxStep = 0f;
            WeatherWind.Phase last = WeatherWind.Phase.Calm; float phaseStart = 0f, calmStart = 0f;
            int gusts = 0; bool warnedFirst = true, warnLong = true, early = false, gapsOk = true, liveOk = true;
            while (t < 600f)
            {
                WeatherWind.Step(.05f, true, true); t += .05f;
                float env = WeatherWind.GustEnvelope; maxStep = Mathf.Max(maxStep, Mathf.Abs(env - prevEnv)); prevEnv = env;
                if (WeatherWind.PhaseNow != last)
                {
                    if (WeatherWind.PhaseNow == WeatherWind.Phase.Gust)
                    {
                        warnedFirst &= last == WeatherWind.Phase.Warning;
                        warnLong &= t - phaseStart >= p.warn - .06f && t - phaseStart >= WorldWeatherTable.MinWarning - .06f;
                        if (t < WeatherWind.FirstGustAfter) early = true;
                        gusts++;
                    }
                    if (last == WeatherWind.Phase.Gust) { liveOk &= Mathf.Abs(t - phaseStart - p.live) < .12f; calmStart = t; }
                    if (WeatherWind.PhaseNow == WeatherWind.Phase.Warning && calmStart > 0f) gapsOk &= t - calmStart >= p.every.x - .1f && t - calmStart <= p.every.y + .1f;
                    last = WeatherWind.PhaseNow; phaseStart = t;
                }
            }
            Check(e.world + ": " + gusts + " gusts in 10 min, each telegraphed >= 1.5 s first, none before " + WeatherWind.FirstGustAfter + " s", gusts >= 20 && warnedFirst && warnLong && !early);
            Check(e.world + ": gust spacing and length follow the profile", gapsOk && liveOk);
            Check(e.world + ": a gust ramps (max envelope step per 50 ms " + maxStep.ToString("F3") + ")", maxStep <= .12f);
        }
        // an event running (or a boss intro) holds new gusts back; a warned gust is called off if one starts
        var pf = WorldWeatherTable.Entries[1].wind;
        WeatherWind.Reset(pf, 3);
        for (float t = 0f; t < 120f; t += .05f) WeatherWind.Step(.05f, true, false);
        Check("no gust is scheduled while an event is running (120 s)", WeatherWind.PhaseNow == WeatherWind.Phase.Calm);
        WeatherWind.Reset(pf, 3);
        float tt = 0f; while (WeatherWind.PhaseNow != WeatherWind.Phase.Warning && tt < 100f) { WeatherWind.Step(.05f, true, true); tt += .05f; }
        WeatherWind.Step(.05f, true, false);
        Check("a warned gust is called off when an event starts", WeatherWind.PhaseNow == WeatherWind.Phase.Calm);
        // the wind fades out on an unclean board
        WeatherWind.Reset(pf, 3); for (int i = 0; i < 100; i++) WeatherWind.Step(.05f, true, true);
        float on = WeatherWind.Fade;
        for (int i = 0; i < 100; i++) WeatherWind.Step(.05f, false, true);
        Check("the wind eases in (" + on.ToString("F2") + ") and out when the board is not clean", on > .9f && WeatherWind.Fade < .01f);
    }

    // ---- the wind through the manager ----
    static void WindPushesTheBoard()
    {
        var m = NewManager(); WorldHazards.ClearAll();
        WorldHazards.BossRunningOverride = false;
        var player = new GameObject("player"); made.Add(player);
        player.transform.position = new Vector3(0f, -4f);
        EliteSystem.PlayerOverride = player.transform;
        WorldHazards.PlayerOffset = Vector2.zero;
        var p = WorldWeatherTable.Entries[6].wind;   // Desert: a steady crosswind
        WeatherWind.Reset(p, 9); WeatherWind.Fade = 1f;
        var shot = new GameObject("shot").AddComponent<WorldHazardsTest.StubShot>(); made.Add(shot.gameObject);
        shot.pos = new Vector2(-2f, 2f); HostileShots.Register(shot);
        var body = Body(new Vector2(-1f, 2f));
        float x0 = shot.pos.x, b0 = body.transform.position.x;
        // calm: the breeze alone, 3 s
        for (int i = 0; i < 180; i++) { WeatherWind.Step(Dt, true, true); WorldHazards.Apply(Dt); }
        float calmShot = shot.pos.x - x0, calmBody = body.transform.position.x - b0;
        Check("breeze: bullets drift (" + calmShot.ToString("F2") + " u in 3 s) and bodies drift (" + calmBody.ToString("F2") + " u)", calmShot > .7f && calmBody > .3f && calmShot > calmBody);
        Check("breeze: the ship is hardly moved", WorldHazards.PlayerOffset.magnitude < .3f, WorldHazards.PlayerOffset.magnitude.ToString("F2"));
        // a gust in progress
        WeatherWind.PhaseNow = WeatherWind.Phase.Gust; WeatherWind.PhaseT = 1f; WeatherWind.Side = 1f;
        shot.pos = new Vector2(-2f, 2f); float s1 = shot.pos.x; float peakOffset = 0f;
        for (int i = 0; i < 90; i++) { WorldHazards.Apply(Dt); WeatherWind.Step(Dt, true, true); peakOffset = Mathf.Max(peakOffset, WorldHazards.PlayerOffset.magnitude); }
        Check("gust: a bullet is swept " + (shot.pos.x - s1).ToString("F1") + " u in 1.5 s", shot.pos.x - s1 > 3.5f);
        Check("gust: the ship is nudged but never over its cap (" + peakOffset.ToString("F2") + ")", peakOffset <= WorldWeatherTable.PlayerCap + 1e-3f);
        Check("wind never kills a body", body != null && body.activeSelf);
        // no wind through the manager when the profile is gone (Space)
        WeatherWind.Reset(null);
        Vector2 at = shot.pos; WorldHazards.Apply(Dt);
        Check("Space has no wind: nothing drifts", shot.pos == at);
        HostileShots.Unregister(shot);
        WorldHazards.ClearAll();
    }

    // ---- fairness in boss fights ----
    static void BossShotsDriftTogether()
    {
        var m = NewManager(); WorldHazards.ClearAll();
        var player = new GameObject("player"); made.Add(player);
        player.transform.position = new Vector3(0f, -4f);
        EliteSystem.PlayerOverride = player.transform;
        WorldHazards.BossRunningOverride = true;
        WeatherWind.Reset(WorldWeatherTable.Entries[5].wind, 4); WeatherWind.Fade = 1f;   // Storm: the harshest shear and gusts
        WeatherWind.PhaseNow = WeatherWind.Phase.Gust; WeatherWind.PhaseT = .5f; WeatherWind.Side = 1f;
        // and a flare AND a tornado on top, the worst of the table
        bool wipes = Def("solar_flare").wipesShots; Def("solar_flare").wipesShots = false;   // this test is about the drift, not the wipe
        var f0 = WorldHazards.Fields[0]; f0.def = Def("solar_flare"); f0.phase = HazardField.Phase.Live; f0.side = 1f; f0.sweepX = 0f; f0.t = 5f; f0.viewBottom = -6f; f0.viewTop = 6f; f0.laneHalf = 4f; f0.liveFor = 60f;
        var f1 = WorldHazards.Fields[1]; f1.def = Def("tornado"); f1.phase = HazardField.Phase.Live; f1.side = 1f; f1.centre = new Vector2(.5f, 1f); f1.t = 5f; f1.viewBottom = -6f; f1.viewTop = 6f; f1.laneHalf = 4f; f1.liveFor = 60f;
        var shots = new List<WorldHazardsTest.StubShot>();
        var rnd = new System.Random(3);
        for (int i = 0; i < 12; i++)
        {
            var s = new GameObject("s" + i).AddComponent<WorldHazardsTest.StubShot>(); made.Add(s.gameObject);
            s.pos = new Vector2((float)(rnd.NextDouble() * 5 - 2.5), (float)(rnd.NextDouble() * 9 - 4)); HostileShots.Register(s); shots.Add(s);
        }
        bool together = true, noY = true, capped = true, slew = true;
        float prev = 0f, maxV = 0f;
        var before = new Vector2[shots.Count];
        for (int i = 0; i < 600; i++)
        {
            for (int k = 0; k < shots.Count; k++) before[k] = shots[k].pos;
            WorldHazards.Apply(Dt);
            float dx = shots[0].pos.x - before[0].x;
            for (int k = 0; k < shots.Count; k++)
            {
                Vector2 d = shots[k].pos - before[k];
                together &= Mathf.Abs(d.x - dx) < 1e-6f;
                noY &= Mathf.Abs(d.y) < 1e-6f;
            }
            float v = dx / Dt; maxV = Mathf.Max(maxV, Mathf.Abs(v));
            capped &= Mathf.Abs(v) <= WeatherWind.BossShotWindCap + 1e-4f;
            slew &= Mathf.Abs(v - prev) <= WeatherWind.BossShotWindSlew * Dt + 1e-4f;
            prev = v;
        }
        Check("boss fight: every bullet on screen drifts the same sideways amount each frame (a wave's gap moves WITH its bullets)", together);
        Check("boss fight: no vertical or swirling push on bullets", noY);
        Check("boss fight: the drift never exceeds " + WeatherWind.BossShotWindCap + " u/s (peak " + maxV.ToString("F3") + ")", capped && maxV > .1f);
        Check("boss fight: and changes no faster than " + WeatherWind.BossShotWindSlew + " u/s per s", slew);
        // the same weather outside a boss fight throws the bullets about
        WorldHazards.BossRunningOverride = false;
        foreach (var s in shots) s.shoved = Vector2.zero;
        for (int i = 0; i < 60; i++) WorldHazards.Apply(Dt);
        float most = 0f; foreach (var s in shots) most = Mathf.Max(most, s.shoved.magnitude);
        Check("outside a boss fight the same weather moves bullets " + most.ToString("F1") + " u in a second (> 4x the boss-fight cap)", most > WeatherWind.BossShotWindCap * 4f * 1f);
        foreach (var s in shots) HostileShots.Unregister(s);
        Def("solar_flare").wipesShots = wipes;
        WorldHazards.BossRunningOverride = null;
        WorldHazards.ClearAll();
        WeatherWind.Reset(null);
    }

    // The wind's boss-fight drift, worst case: a triangle wave between the cap and minus the cap at the slew limit.
    static float TriangleV(float t, float phase)
    {
        float cap = WeatherWind.BossShotWindCap, period = 4f * cap / WeatherWind.BossShotWindSlew;
        float u = Mathf.Repeat(t / period + phase, 1f);
        return cap * (u < .25f ? u * 4f : u < .75f ? 2f - u * 4f : u * 4f - 4f);
    }

    static float DriftOver(float from, float to, float phase, float scale)
    {
        float s = 0f;
        for (float t = from; t < to; t += .02f) s += TriangleV(t, phase) * scale * .02f;
        return s;
    }

    // BOSS WAVES (PR #30, BossWaves.cs): WaveGaps.Choose picks each wave's gap so the ship, at 4 u/s planning speed (it really moves ~5), can
    // reach it. Weather moves every boss-fight bullet together by at most BossShotWindCap, so a wave's actual gap is its planned gap
    // plus the drift it collected in flight. This walks 400 random wave chains through the REAL WaveGaps (found by reflection, so this
    // file also compiles in a tree without PR #30) and tracks the interval of ship positions that can still be in the actual gaps,
    // at 5 u/s with the reaction time, under the worst-case wind. It must never empty; and it must (sanity) fail at 8x the cap.
    static void WaveGapsStayReachable()
    {
        // pure bound, always: how far do two consecutive waves' drifts differ at most?
        float worst = 0f;
        var r0 = new System.Random(5);
        for (int i = 0; i < 2000; i++)
        {
            float spb = 60f / Mathf.Lerp(90f, 160f, (float)r0.NextDouble());
            float s = spb * (r0.Next(3) == 0 ? .5f : 1f), T = spb * (3 + r0.Next(4)), ph = (float)r0.NextDouble(), t0 = (float)r0.NextDouble() * 20f;
            float d1 = DriftOver(t0, t0 + T, ph, 1f), d2 = DriftOver(t0 + s, t0 + s + T, ph, 1f);
            worst = Mathf.Max(worst, Mathf.Abs(d2 - d1));
        }
        Check("pure bound: two consecutive waves' drifts differ by at most " + worst.ToString("F3") + " u (WaveGaps overlap slack is .14 u)", worst <= .14f);

        var tGaps = System.Type.GetType("WaveGaps, Assembly-CSharp");
        var tSim = System.Type.GetType("PatternSim, Assembly-CSharp");
        if (tGaps == null || tSim == null)
        {
            Info("WaveGaps / PatternSim are not in this tree (boss bullet waves, PR #30, not merged): the reflection chain check is skipped here and runs when it lands");
            return;
        }
        var choose = tGaps.GetMethod("Choose");
        var reset = tGaps.GetMethod("Reset");
        var fr = tSim.GetField("ShipRadius", BindingFlags.Public | BindingFlags.Static);
        float shipR = fr != null ? (fr.IsLiteral ? (float)fr.GetRawConstantValue() : (float)fr.GetValue(null)) : .4f;
        float reaction = (float)tGaps.GetField("Reaction").GetRawConstantValue();
        float shipW = 2f * shipR + .1f;
        int bad0 = RunChains(tGaps, choose, reset, shipR, shipW, reaction, 0f), badCap = RunChains(tGaps, choose, reset, shipR, shipW, reaction, 1f), badBig = RunChains(tGaps, choose, reset, shipR, shipW, reaction, 8f);
        Check("WaveGaps with no weather: all 400 chains followable at 5 u/s (model sanity, " + bad0 + " broken)", bad0 == 0);
        Check("WaveGaps under the worst-case boss-fight drift (cap " + WeatherWind.BossShotWindCap + " u/s): all 400 chains followable (" + badCap + " broken)", badCap == 0);
        Check("sanity: at 8x the cap the same test does catch broken chains (" + badBig + " broken)", badBig > 0);
    }

    static int RunChains(System.Type tGaps, MethodInfo choose, MethodInfo reset, float shipR, float shipW, float reaction, float scale)
    {
        int broken = 0;
        var rnd = new System.Random(77);
        for (int chain = 0; chain < 400; chain++)
        {
            object gaps = System.Activator.CreateInstance(tGaps);
            reset.Invoke(gaps, null);
            float spb = 60f / Mathf.Lerp(90f, 160f, (float)rnd.NextDouble());
            float phase = (float)rnd.NextDouble();
            int chainK = 3 + rnd.Next(4);
            float spawn = 0f, shipX = 0f, prevArrival = -99f, pgl = -99f, pgh = 99f;
            bool fail = false;
            float lim = 2.4f;
            for (int i = 0; i < 40 && !fail; i++)
            {
                float step = i == 0 ? 0f : spb * new[] { .5f, 1f, 1f, 2f, 4f }[rnd.Next(5)];
                if (i > 0 && rnd.Next(12) == 0) step += spb * 8f;               // a quiet spell
                spawn += step;
                float arrival = Mathf.Max(spawn + chainK * spb, prevArrival + .3f);   // a wave flies a fixed number of beats (BossWaveVoice quantises it); none overtakes another
                float mult = Mathf.Lerp(1.7f, 3.1f, (float)rnd.NextDouble());
                float width = mult * 2f * shipR;
                float thick = .25f;
                float want = (float)(rnd.NextDouble() * 2 - 1);
                // Choose has trailing optional parameters (WaveFront front = default, added by #36): pad with Type.Missing so the default applies
                var given = new object[] { shipX, spawn, arrival, width, thick, lim, want };
                var args = new object[choose.GetParameters().Length];
                for (int a = 0; a < args.Length; a++) args[a] = a < given.Length ? given[a] : System.Type.Missing;
                float c = (float)choose.Invoke(gaps, BindingFlags.Default | BindingFlags.OptionalParamBinding, null, args, null);
                float drift = DriftOver(spawn, arrival, phase, scale);
                float room = Mathf.Max(0f, (width - shipW) * .5f);
                float gl = c + drift - room, gh = c + drift + room;
                // the ship (5 u/s, a human's reaction) sees this wave at spawn + reaction. If the previous wave has not reached it yet it uses
                // the time until then to lean toward the new gap (but must be inside the previous gap when that passes), holds there while it
                // passes, then walks to the nearest point of the actual (drifted) gap
                float pos = shipX;
                float see = spawn + reaction;
                float target = Mathf.Clamp(pos, gl, gh);
                if (prevArrival > see)
                {
                    float t1 = prevArrival - see;
                    pos += Mathf.Sign(target - pos) * Mathf.Min(Mathf.Abs(target - pos), 5f * t1);
                    pos = Mathf.Clamp(pos, pgl, pgh);
                }
                float start = Mathf.Max(prevArrival + thick, see);
                float budget = Mathf.Max(0f, arrival - start);
                float end = pos + Mathf.Sign(target - pos) * Mathf.Min(Mathf.Abs(target - pos), 5f * budget);
                if (end < gl - 1e-3f || end > gh + 1e-3f) { fail = true; break; }
                pgl = gl; pgh = gh;
                shipX = end; prevArrival = arrival;
            }
            if (fail) broken++;
        }
        return broken;
    }

    // ---- drawing ----
    static void Visuals()
    {
        // (an empty scene: with a game scene open the editor's change tracking allocates on every Transform write and would drown the check)
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
        foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
        made.Clear();
        var m = NewManager(); WorldHazards.ClearAll();
        var camGo = new GameObject("cam", typeof(Camera)); camGo.tag = "MainCamera"; made.Add(camGo);
        var cam = camGo.GetComponent<Camera>(); cam.orthographic = true; cam.orthographicSize = 5f; cam.aspect = 9f / 19f;
        // the flare train, all the way across, stays inside its pools and every glow piece stays near the view
        m.Begin(Def("flare_storm"));
        var f = WorldHazards.Fields[0]; int maxDrawn = 0; bool inside = true;
        for (int i = 0; i < 60 * 14 && f.Active; i++)
        {
            m.StepFields(Dt);
            m.Visuals.Draw(WorldHazards.Fields, true);
            maxDrawn = Mathf.Max(maxDrawn, m.Visuals.Drawn);
            foreach (var r in m.Visuals.Renderers)
                if (r.enabled) inside &= Mathf.Abs(r.transform.position.x) <= f.laneHalf + f.def.size * 4f && r.transform.position.y >= -7f && r.transform.position.y <= 7f;
        }
        Check("the flare train draws at most " + maxDrawn + " of " + HazardVisuals.Pool + " pooled sprites", maxDrawn > 30 && maxDrawn <= HazardVisuals.Pool);
        Check("all of it stays close to the view", inside);
        Check("the painted flare needs at most " + m.Visuals.ArtRenderers.Length + " tiles (3 bands x tiles up the view)", Mathf.CeilToInt(11.5f / (Def("flare_storm").size * HazardVisuals.FlareTile * .92f)) * 3 <= m.Visuals.ArtRenderers.Length);
        WorldHazards.ClearAll();
        // the wind streaks: they appear in a breeze, brighten in a gust, flow with the field, and a warned gust glows its upwind edge
        var wv = m.Wind;
        WeatherWind.Reset(WorldWeatherTable.Entries[4].wind, 2); WeatherWind.Fade = 1f;
        for (int i = 0; i < 120; i++) wv.Draw(Dt, true, 4);
        int calmShown = wv.Visible;
        WeatherWind.PhaseNow = WeatherWind.Phase.Warning; WeatherWind.PhaseT = 1.2f;
        wv.Draw(Dt, true, 4);
        Check("a warned gust is drawn (upwind edge bars + streaks)", wv.Visible > 0);
        WeatherWind.PhaseNow = WeatherWind.Phase.Gust; WeatherWind.PhaseT = 1.5f;
        for (int i = 0; i < 30; i++) wv.Draw(Dt, true, 4);
        int gustShown = wv.Visible;
        Check("wind streaks: " + calmShown + " in the breeze, " + gustShown + " in a gust (>=)", calmShown > 5 && gustShown >= calmShown && gustShown <= WindVisuals.Streaks + 2);
        long bytes = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 100; i++) wv.Draw(Dt, true, 4); });
        Check("drawing the wind allocates no more than editor noise (<= 40 B per sprite-frame: five Transform / colour writes at ~8 B)", bytes <= 100L * Mathf.Max(1, gustShown) * 40, bytes + " B for " + gustShown + " sprites");
        WeatherWind.Reset(null);
        wv.Draw(Dt, true, 0);
        for (int i = 0; i < 90; i++) wv.Draw(Dt, true, 0);
        Check("Space draws no wind", wv.Visible == 0);
    }
}
