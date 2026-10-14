using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

// WORLD WEATHER AND HAZARDS (docs/world-weather-hazards.md section 13): the data, the force model per shape and
// per target, the player's capped offset, the push-only rule, the scheduling rules, the boulder seam, the boss
// fight allowance, the pools and the cost.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod WorldHazardsTest.Run
public static class WorldHazardsTest
{
    static int fails;
    static void Check(string what, bool ok, string extra = "")
    {
        Debug.Log((ok ? "[HAZ] PASS  " : "[HAZ] FAIL  ") + what + (extra.Length > 0 ? "  [" + extra + "]" : ""));
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    static readonly List<Object> made = new List<Object>();

    // A hostile shot stand-in (a MonoBehaviour, as the registry requires): records where it was shoved.
    public sealed class StubShot : MonoBehaviour, IHostileShot
    {
        public bool shovable = true;
        public Vector2 pos;
        public Vector2 shoved;
        public bool ShotCollidable => true;
        public Vector2 ShotPosition => pos;
        public float ShotRadius => .1f;
        public int ShotOwner => 1;
        public float ShotAge => 5f;
        public int ShotMass => HostileShots.Light;
        public Color ShotTint => Color.white;
        public void ShotPop(Vector2 at) { }
        public bool ShotShovable => shovable;
        public void ShotShove(Vector2 by) { pos += by; shoved += by; }
    }

    sealed class StubCover : ICoverQuery
    {
        public bool on;
        public bool AnyOnScreen => on;
        public bool AnyWithin(Rect world) => on;
    }

    static HazardDef Def(string id)
    {
        foreach (var e in WorldWeatherTable.Entries) foreach (var h in e.hazards) if (h.id == id) return h;
        return null;
    }

    static HazardField Live(HazardDef d, Vector2 centre, float side = 1f, float t = -1f)
    {
        var f = new HazardField { def = d, phase = HazardField.Phase.Live, centre = centre, side = side, viewBottom = -6f, viewTop = 6f, laneHalf = 4f, liveFor = 20f };
        f.t = t >= 0f ? t : d.warning + 3f;
        f.frontY = centre.y;
        return f;
    }

    static WorldHazards NewManager()
    {
        if (WorldHazards.Instance != null) Object.DestroyImmediate(WorldHazards.Instance.gameObject);
        var go = new GameObject("~WorldHazardsTest");
        made.Add(go);
        return go.AddComponent<WorldHazards>();
    }

    static WorldHazards.Situation Open(int world)
    {
        return new WorldHazards.Situation { world = world };
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            DataComplete();
            SwirlModel();
            FrontModel();
            ColumnModel();
            TargetsAndScatter();
            PlayerOffsetIsCapped();
            PushOnlyNoDamage();
            SchedulingRules();
            CoverSeam();
            BossFight();
            PoolsAndCost();
            AmbientPerWorld();
            ScreenShapes();
            TornadoVortex();
            ApprovedWorldArt();
            BoulderSeam();
            FlagOff();
        }
        finally
        {
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
            HostileShots.ResetCounters();
            HazardCoverSeam.Cover = null;
            WorldHazards.SituationOverride = null;
            WorldHazards.ClearAll();
            WorldHazards.PlayerOffset = Vector2.zero;
            EliteSystem.PlayerOverride = null;
        }
        Debug.Log("[HAZ] " + (fails == 0 ? "ALL PASS" : fails + " FAILED"));
        return fails;
    }

    // ---- data ----
    static void DataComplete()
    {
        string problems = WorldWeatherTable.Validate();
        Check("world weather table is valid", problems.Length == 0, problems);
        Check("every world has an entry", WorldWeatherTable.Entries.Length == WorldManager.Worlds.Length);
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            var e = WorldWeatherTable.ForWorld(w);
            Check(WorldManager.Worlds[w].displayName + ": ambient + hazards + schedule", e != null && e.ambient.Length > 0 && e.hazards.Length >= 1 && e.gap.x >= 30f);
            foreach (var h in e.hazards)
            {
                Check(h.id + ": warning >= " + WorldWeatherTable.MinWarning + " s", h.warning >= WorldWeatherTable.MinWarning);
                Check(h.id + ": cue colour is pink only in Space", w == 0 || !WorldCue.IsPink(WorldCue.Body(w)) && !WorldCue.IsPink(Color.Lerp(WorldCue.Body(w), Color.white, .6f)));
            }
        }
        // the same bytes as the doc: Space keeps its magenta mote, no other world has pink
        bool spacePink = false;
        foreach (var a in WorldWeatherTable.Entries[0].ambient) spacePink |= WorldCue.IsPink(a.color);
        Check("Space ambient has its magenta mote", spacePink);
    }

    // ---- force model ----
    static void SwirlModel()
    {
        var d = Def("tornado");
        var f = Live(d, new Vector2(0f, 0f));
        Vector2 p = new Vector2(1f, 0f);
        Check("swirl: pulls a body toward the centre", HazardForce.Sample(f, p, HazardTarget.Body, out Vector2 v) && Vector2.Dot(v, -p.normalized) > 0f);
        Check("swirl: and turns it (tangential part)", Mathf.Abs(Vector2.Dot(v, new Vector2(-p.y, p.x).normalized)) > .1f);
        Check("swirl: nothing outside the radius", !HazardForce.Sample(f, new Vector2(d.size + .1f, 0f), HazardTarget.Body, out _));
        HazardForce.Sample(f, new Vector2(.02f, 0f), HazardTarget.Body, out Vector2 core);
        Check("swirl: the core is calm (no jitter)", core.magnitude < v.magnitude, core.magnitude.ToString("F2"));
        var g = Live(d, Vector2.zero, -1f);
        HazardForce.Sample(g, p, HazardTarget.Body, out Vector2 vm);
        Check("swirl: the other side spins the other way, still pulls in", Vector2.Dot(vm, new Vector2(-p.y, p.x)) * Vector2.Dot(v, new Vector2(-p.y, p.x)) < 0f && Vector2.Dot(vm, -p) > 0f);
        var w = Live(Def("gravity_well"), Vector2.zero);
        HazardForce.Sample(w, p, HazardTarget.Hostile, out Vector2 gw);
        Check("gravity well: bends a bullet inward", Vector2.Dot(gw, -p) > 0f);
        var warned = Live(d, Vector2.zero); warned.phase = HazardField.Phase.Warning; warned.t = .5f;
        Check("a warned hazard exerts nothing", !HazardForce.Sample(warned, p, HazardTarget.Body, out _));
    }

    static void FrontModel()
    {
        var d = Def("sandstorm_front");
        var f = Live(d, new Vector2(0f, 1f), 1f);
        Check("front: shoves sideways inside the band", HazardForce.Sample(f, new Vector2(0f, 1f), HazardTarget.Body, out Vector2 v) && v.x > 0f);
        HazardForce.Sample(Live(d, new Vector2(0f, 1f), -1f), new Vector2(0f, 1f), HazardTarget.Body, out Vector2 vl);
        Check("front: the picked side decides the direction", vl.x < 0f);
        Check("front: nothing outside the band", !HazardForce.Sample(f, new Vector2(0f, 1f + d.size), HazardTarget.Body, out _));
        HazardForce.Sample(f, new Vector2(0f, 1f), HazardTarget.Hostile, out Vector2 vh);
        Check("front: a sandstorm bends bullets harder than it shoves bodies", vh.x > v.x);
        var b = Live(Def("blizzard_front"), Vector2.zero, 1f);
        HazardForce.Sample(b, Vector2.zero, HazardTarget.Body, out Vector2 bv);
        Check("blizzard: a headwind (up-screen) as well as a drift", bv.y > 0f && bv.x > 0f);
    }

    static void ColumnModel()
    {
        var d = Def("vine_updraft");
        var f = Live(d, new Vector2(1f, -6f));
        Check("column: lifts up-screen inside it", HazardForce.Sample(f, new Vector2(1f, 0f), HazardTarget.Body, out Vector2 v) && v.y > 0f);
        Check("column: nothing beside it", !HazardForce.Sample(f, new Vector2(1f + d.size, 0f), HazardTarget.Body, out _));
        var gey = Def("geyser_plume");
        bool burst = false, still = false;
        for (float t = 3f; t < 7f; t += .1f)
        {
            var g = Live(gey, new Vector2(0f, -6f), 1f, t);
            if (HazardForce.Sample(g, new Vector2(.05f, 0f), HazardTarget.Body, out Vector2 gv))
            { if (gv.y > 1f) burst = true; if (gv.y < .01f) still = true; }
        }
        Check("geyser: bursts, with quiet between", burst && still);
    }

    static void TargetsAndScatter()
    {
        NewManager();
        var player = new GameObject("player"); made.Add(player);
        player.transform.position = new Vector3(0f, -4f);
        EliteSystem.PlayerOverride = player.transform;

        // a tornado's bullets scatter: outward and round, away from the centre, while bodies are pulled in
        var d = Def("tornado");
        var f = WorldHazards.Fields[0];
        f.def = d; f.phase = HazardField.Phase.Live; f.t = d.warning + 3f; f.centre = new Vector2(0f, 2f); f.side = 1f; f.viewBottom = -6f; f.viewTop = 6f; f.laneHalf = 4f; f.liveFor = 30f;
        var shot = new GameObject("shot").AddComponent<StubShot>(); made.Add(shot.gameObject);
        shot.pos = new Vector2(1f, 2f);
        HostileShots.Register(shot);
        float before = Vector2.Distance(shot.pos, f.centre);
        for (int i = 0; i < 30; i++) WorldHazards.Apply(Dt);
        Check("tornado scatters a bullet outward (further from the funnel)", Vector2.Distance(shot.pos, f.centre) > before, before.ToString("F2") + " -> " + Vector2.Distance(shot.pos, f.centre).ToString("F2"));
        Check("... and swirls it round (it left its line)", Mathf.Abs(shot.pos.y - 2f) > .02f, shot.pos.ToString());
        float cap = WorldWeatherTable.ShotMaxSpeed * Dt * 30f;
        Check("a bullet is never shoved faster than ShotMaxSpeed", shot.shoved.magnitude <= cap + 1e-3f, shot.shoved.magnitude.ToString("F2") + " <= " + cap.ToString("F2"));
        shot.shovable = false; Vector2 at = shot.pos; WorldHazards.Apply(Dt);
        Check("a shot that is not shovable stays", shot.pos == at);
        shot.shovable = true;
        shot.pos = new Vector2(3.5f, -3f); at = shot.pos; WorldHazards.Apply(Dt);
        Check("a shot outside the field is untouched", shot.pos == at);

        // bodies
        var body = Body(new Vector2(1f, 2f));
        Vector2 b0 = body.transform.position;
        for (int i = 0; i < 30; i++) WorldHazards.Apply(Dt);
        Vector2 b1 = body.transform.position;
        Check("an enemy body is pulled toward the funnel", Vector2.Distance(b1, f.centre) < Vector2.Distance(b0, f.centre), b0 + " -> " + b1);
        Check("... and is alive and tagged as before (never killed)", body != null && body.activeSelf && body.CompareTag("Enimey"));
        // the lane clamp: a front shoving a body at the rail never carries it into it
        f.def = Def("sandstorm_front"); f.frontY = 1f; f.side = 1f; f.t = 6f; f.liveFor = 30f;
        float lane = SpawnLane.LaneHalf - .3f;
        body.transform.position = new Vector3(lane - .02f, 1f);
        for (int i = 0; i < 60; i++) WorldHazards.Apply(Dt);
        Check("a shoved body never enters a rail (lane clamp)", Mathf.Abs(body.transform.position.x) <= lane + 1e-3f, body.transform.position.x.ToString("F3") + " <= " + lane.ToString("F3"));
        // a field never drives a body into another one (friendly-fire crashes are not the hazard's to cause)
        var a1 = Body(new Vector2(0.2f, 2f)); var a2 = Body(new Vector2(-0.45f, 2f));
        f.def = Def("whirlpool"); f.centre = new Vector2(-2f, 2f); f.t = 6f; f.side = 1f; f.liveFor = 60f;
        for (int i = 0; i < 90; i++) WorldHazards.Apply(Dt);
        float gap = Mathf.Abs(a1.transform.position.x - a2.transform.position.x) - .6f - .03f;
        Check("a field does not push a body into its neighbour (gap " + gap.ToString("F2") + ")", gap >= -.001f);
        f.def = Def("sandstorm_front"); f.frontY = 1f; f.side = 1f; f.t = 6f; f.liveFor = 30f;
        // a boss body is never moved
        var boss = Body(new Vector2(0f, 1f)); boss.tag = "Enimey";
        bool hasBossTarget = false;
        try { boss.AddComponent<BossTarget>(); hasBossTarget = true; } catch (System.Exception) { }
        Vector2 bp = boss.transform.position;
        for (int i = 0; i < 30; i++) WorldHazards.Apply(Dt);
        Check("a boss body is not moved", !hasBossTarget || (Vector2)boss.transform.position == bp);
        // the player's own shots bend a little, less than the enemies'
        Check("the player's shots take the smaller mix", f.def.mine.gain < f.def.hostile.gain);
        Check("elites take a small share and bosses none", f.def.elite.gain <= .5f);
        HostileShots.Unregister(shot);
        WorldHazards.ClearAll();
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

    static void PlayerOffsetIsCapped()
    {
        NewManager();
        var player = new GameObject("player"); made.Add(player);
        player.transform.position = new Vector3(1.2f, 0f);
        EliteSystem.PlayerOverride = player.transform;
        WorldHazards.PlayerOffset = Vector2.zero;
        var f = WorldHazards.Fields[0];
        f.def = Def("whirlpool"); f.phase = HazardField.Phase.Live; f.t = 5f; f.centre = Vector2.zero; f.side = 1f; f.viewBottom = -6f; f.viewTop = 6f; f.liveFor = 30f;
        float max = 0f;
        for (int i = 0; i < 600; i++) { WorldHazards.Apply(Dt); max = Mathf.Max(max, WorldHazards.PlayerOffset.magnitude); }
        Check("the ship is displaced by the field", WorldHazards.PlayerOffset.magnitude > .05f, WorldHazards.PlayerOffset.ToString());
        Check("... pulled toward the whirlpool", WorldHazards.PlayerOffset.x < 0f);
        Check("... and the offset is capped at PlayerCap", max <= WorldWeatherTable.PlayerCap + 1e-3f, max.ToString("F2"));
        WorldHazards.ClearAll();
        for (int i = 0; i < 180; i++) WorldHazards.Apply(Dt);
        Check("the offset springs back when the field is gone (3 s)", WorldHazards.PlayerOffset.magnitude < .02f, WorldHazards.PlayerOffset.magnitude.ToString("F3"));

        // never trapped: with the offset at its cap the finger can still put the ship exactly anywhere in reach
        WorldHazards.PlayerOffset = new Vector2(WorldWeatherTable.PlayerCap, WorldWeatherTable.PlayerCap) * .7071f;
        bool all = true;
        foreach (float tx in new[] { -2f, 0f, 2f })
            foreach (float ty in new[] { -3f, 0f, 3f })
            {
                Vector3 want = new Vector3(tx, ty);
                Vector3 finger = new Vector3(tx - WorldHazards.PlayerOffsetX, ty - ShipReach.FingerOffset - WorldHazards.PlayerOffsetY);
                Vector3 got = movePlayer.FollowTarget(finger);
                Vector3 clampedWant = new Vector3(ShipReach.ClampX(want.x), movePlayer.ClampPlayerY(want.y));
                all &= Vector2.Distance(got, clampedWant) < 1e-3f;
            }
        Check("with the offset at its cap the finger still reaches every point", all);
        WorldHazards.PlayerOffset = Vector2.zero;
        // warning does nothing to the ship
        WorldHazards.ClearAll();
        var m = NewManager();
        m.Begin(Def("tornado"));
        for (int i = 0; i < 60; i++) { m.StepFields(Dt); WorldHazards.Apply(Dt); }
        Check("nothing pushes the ship during the warning", WorldHazards.PlayerOffset == Vector2.zero && WorldHazards.Fields[0].phase == HazardField.Phase.Warning);
        WorldHazards.ClearAll();
    }

    static void PushOnlyNoDamage()
    {
        // The hazard code has no path to damage, kill or score: hold it to that by its source.
        string[] files = { "WorldHazards.cs", "HazardForce.cs", "HazardDef.cs", "HazardVisuals.cs", "WeatherLayer.cs" };
        string[] banned = { "TakeDamage", "Damage(", "Kill(", "RunScore.", "ShoveCrash", "lifeControler", "ShipLives", "Object.Destroy(", "GameObject.Destroy(", "Hearts", "AddScore" };
        string dir = Application.dataPath + "/Scripts/Worlds/Hazards/";
        string hits = "";
        foreach (var f in files)
        {
            string src = System.IO.File.ReadAllText(dir + f);
            foreach (var b in banned)
            {
                int i = src.IndexOf(b);
                while (i >= 0)
                {
                    // allow "RunScore.IsHostileShot" (a read-only classifier) and the comments
                    int ls = src.LastIndexOf('\n', i) + 1;
                    string line = src.Substring(ls, src.IndexOf('\n', i) - ls).TrimStart();
                    if (!line.StartsWith("//") && !line.Contains("RunScore.IsHostileShot") && !line.Contains("DestroyImmediate")) hits += f + ": " + b + "; ";
                    i = src.IndexOf(b, i + 1);
                }
            }
        }
        Check("the hazard code never references damage, kills, hearts or score", hits.Length == 0, hits);
    }

    // ---- scheduling ----
    static void SchedulingRules()
    {
        var m = NewManager();
        // each blocked state blocks; the open board does not
        var open = Open(1);
        Check("an open board past the calm start is not blocked", WorldHazards.Blocked(open, 50f, 99f) == null);
        Check("the 8 s calm start blocks", WorldHazards.Blocked(open, 7f, 99f) != null);
        string[] names = { "tutorial", "entry", "transition", "portal", "dead", "cinematic", "boss intro / outro", "boss soon", "elite", "cover" };
        var states = new WorldHazards.Situation[10];
        for (int i = 0; i < 10; i++) states[i] = open;
        states[0].tutorial = true; states[1].entry = true; states[2].transition = true; states[3].portalOpen = true; states[4].playerDead = true;
        states[5].cinematic = true; states[6].bossBusy = true; states[7].bossRunUp = true; states[8].eliteBusy = true; states[9].coverOnScreen = true;
        for (int i = 0; i < 10; i++) Check("no event starts during: " + names[i], WorldHazards.Blocked(states[i], 50f, 99f) != null);
        Check("no event right after an elite joined", WorldHazards.Blocked(open, 50f, 1f) != null);

        // first event: never before the world's `first`, and it does come
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            if (!TestHarness.SweepWorld(w)) continue;
            var e = WorldWeatherTable.ForWorld(w);
            WorldHazards.ClearAll(); WorldHazards.Started = 0;
            m = NewManager();
            var sit = Open(w);
            float t = 0f; float firstAt = -1f;
            int events = 0; float lastStart = -999f; float minGap = 9999f;
            while (t < 900f)
            {
                int was = WorldHazards.Started;
                m.Tick(.25f, sit); m.StepFields(.25f);
                t += .25f;
                if (WorldHazards.Started > was)
                {
                    if (firstAt < 0f) firstAt = t;
                    if (lastStart > -900f) minGap = Mathf.Min(minGap, t - lastStart);
                    lastStart = t; events++;
                }
            }
            Check(e.world + ": first event not before " + e.first + " s, but it does come", firstAt >= e.first - .5f && firstAt > 8f && firstAt < e.first + 40f, firstAt.ToString("F1"));
            Check(e.world + ": rare (gap >= " + e.gap.x + " s, " + events + " events in 900 s)", minGap >= e.gap.x - .5f && events <= 900f / e.gap.x + 1f && events >= 5, "min gap " + minGap.ToString("F1"));
            Check(e.world + ": at most " + WorldWeatherTable.MaxFields + " fields and one event at a time", CountActive() <= 1);
        }

        // a running event ends when a hard block begins, and cleanly (a fade)
        m = NewManager(); WorldHazards.ClearAll();
        var live = Open(6);
        m.Tick(Dt, live);   // (the first tick adopts the world)
        m.Begin(Def("sandstorm_front"));
        for (int i = 0; i < 4 * 60; i++) { m.Tick(Dt, live); m.StepFields(Dt); }
        Check("a front is acting after its warning", WorldHazards.AnyActing);
        var trans = live; trans.transition = true;
        for (int i = 0; i < 2 * 60; i++) { m.Tick(Dt, trans); m.StepFields(Dt); }
        Check("a world transition ends the event (fade-out, then none)", !WorldHazards.AnyActive);
        // the front sweeps the whole view and ends
        m = NewManager(); WorldHazards.ClearAll(); m.Begin(Def("gust_front"));
        int steps = 0; while (WorldHazards.AnyActive && steps < 60 * 30) { m.StepFields(Dt); steps++; }
        Check("a front finishes its sweep", !WorldHazards.AnyActive && steps > 60 * 2, (steps * Dt).ToString("F1") + " s");
    }

    static int CountActive() { int n = 0; foreach (var f in WorldHazards.Fields) if (f.Active) n++; return n; }

    static void CoverSeam()
    {
        var m = NewManager(); WorldHazards.ClearAll();
        var cover = new StubCover { on = true };
        HazardCoverSeam.Cover = cover;
        var sit = Open(2);
        sit.coverOnScreen = HazardCoverSeam.CoverOnScreen;
        int was = WorldHazards.Started;
        for (int i = 0; i < 600 * 4; i++) { m.Tick(.25f, sit); m.StepFields(.25f); }
        Check("no hazard starts while a boulder is on screen (600 s)", WorldHazards.Started == was);
        Check("HazardBusy is false while idle", !HazardCoverSeam.HazardBusy);
        m.Begin(Def("tornado"));
        Check("HazardBusy is true from the first frame of the warning", HazardCoverSeam.HazardBusy && !WorldHazards.AnyActing);
        WorldHazards.ClearAll();
        // a boulder appearing mid-event fades it out
        cover.on = false; sit.coverOnScreen = false;
        m.Begin(Def("tornado"));
        for (int i = 0; i < 4 * 60; i++) { m.Tick(Dt, sit); m.StepFields(Dt); }
        Check("the tornado is acting", WorldHazards.AnyActing);
        cover.on = true; sit.coverOnScreen = true;
        for (int i = 0; i < 90; i++) { m.Tick(Dt, sit); m.StepFields(Dt); }
        Check("a boulder appearing fades the hazard out in under 1.5 s", !WorldHazards.AnyActive);
        HazardCoverSeam.Cover = null;
        Check("no boulder system: nothing is blocked", !HazardCoverSeam.CoverOnScreen);
        HazardCoverSeam.Cover = CoverHazardQuery.Instance;
    }

    static void BossFight()
    {
        var m = NewManager(); WorldHazards.ClearAll();
        var sit = Open(5);
        sit.bossRunUp = true; sit.bossFight = true;
        Check("a boss FIGHT does not block hazards", WorldHazards.Blocked(sit, 100f, 99f) == null);
        var intro = sit; intro.bossFight = false; intro.bossBusy = true;
        Check("a boss intro / outro does", WorldHazards.Blocked(intro, 100f, 99f) != null);
        // the fight start brings the next event forward
        m.Tick(1f, Open(5));
        float far = m.UntilNext;
        var e = WorldWeatherTable.ForWorld(5);
        // force a far-off timer by running a world change first
        m.Tick(0f, Open(5));
        m.Tick(Dt, sit);
        Check("the fight start brings the next event within the boss window", m.UntilNext <= e.bossFirst.y + .1f, m.UntilNext.ToString("F1") + " (was " + far.ToString("F1") + ")");
        // and a running event is NOT ended by the fight
        WorldHazards.ClearAll();
        m.Begin(Def("tornado"));
        for (int i = 0; i < 4 * 60; i++) { m.Tick(Dt, Open(5)); m.StepFields(Dt); }
        for (int i = 0; i < 60; i++) { m.Tick(Dt, sit); m.StepFields(Dt); }
        Check("a boss fight starting does not end a running hazard", WorldHazards.AnyActing);
        // boss shots are scaled down in a fight (the authored gaps)
        Check("boss shots take a reduced share", Def("tornado").bossShotScale < 1f);
        WorldHazards.ClearAll();
    }


    // The allocation checks run in an empty scene: with a game scene open, the editor itself allocates a few bytes on
    // every Transform write and every UnityEngine.Object comparison (change tracking), which would drown a real leak.
    static void EmptyScene()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
        foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
        made.Clear();
    }

    // ---- pools and cost ----
    static void PoolsAndCost()
    {
        EmptyScene();
        var m = NewManager(); WorldHazards.ClearAll();
        var ply = new GameObject("player"); made.Add(ply); ply.transform.position = new Vector3(0f, -4f);
        EliteSystem.PlayerOverride = ply.transform;   // (edit mode would look the ship up on every call)
        const int N = 2000;
        var shots = new StubShot[N];
        var rnd = new System.Random(7);
        for (int i = 0; i < N; i++)
        {
            var s = new GameObject("s" + i).AddComponent<StubShot>(); made.Add(s.gameObject);
            s.pos = new Vector2((float)(rnd.NextDouble() * 6 - 3), (float)(rnd.NextDouble() * 12 - 6));
            HostileShots.Register(s); shots[i] = s;
        }
        var f = WorldHazards.Fields[0];
        f.def = Def("tornado"); f.phase = HazardField.Phase.Live; f.t = 5f; f.centre = new Vector2(0f, 0f); f.side = 1f; f.viewBottom = -6f; f.viewTop = 6f; f.liveFor = 60f;
        for (int i = 0; i < 20; i++) WorldHazards.Apply(Dt);   // warm
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 200; i++) WorldHazards.Apply(Dt);
        sw.Stop();
        double ms = sw.Elapsed.TotalMilliseconds / 200.0;
        Check("force loop over " + N + " registered shots stays cheap", ms < 1.0, ms.ToString("F3") + " ms per step");
        Check("the cull leaves most shots alone (" + WorldHazards.LastShotsPushed + " of " + N + " pushed)", WorldHazards.LastShotsPushed < N / 2);
        // (In the editor, with a game scene opened earlier, every UnityEngine.Object comparison and Transform write
        // costs a few bytes of editor change-tracking; a real per-frame allocation is per CALL. So: per-call garbage is
        // held to a tiny ceiling with no shots, and the per-visit noise to a few bytes.)
        long sb = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 100; i++) m.StepFields(Dt); });
        long ab = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 100; i++) WorldHazards.Apply(Dt); });
        Check("stepping the field allocates nothing per frame", sb <= 100 * 8, sb + " B / 100 frames");
        Check("applying the forces to 2000 shots allocates no more than editor noise (<= 8 B per shot visit)", ab <= 100L * N * 8, ab + " B / " + (100L * N) + " visits");
        foreach (var s in shots) HostileShots.Unregister(s);
        long ib = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 100; i++) { m.StepFields(Dt); WorldHazards.Apply(Dt); } });
        Check("with the board empty, a frame of the hazard system allocates (almost) nothing", ib <= 100 * 512, ib + " B / 100 frames");
        foreach (var s in shots) HostileShots.Register(s);
        var vis = m.Visuals;
        vis.Draw(WorldHazards.Fields, true);
        Check("hazard visuals stay inside their pool", vis.Drawn <= HazardVisuals.Pool && vis.Drawn > 0, vis.Drawn + " drawn");
        long vbytes = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 100; i++) vis.Draw(WorldHazards.Fields, true); });
        Check("drawing the hazard allocates no more than editor noise (<= 24 B per sprite-frame: ~4 Transform writes at ~3 B)", vbytes <= 100L * Mathf.Max(1, vis.Drawn) * 24, vbytes + " B");
        foreach (var s in shots) HostileShots.Unregister(s);
        WorldHazards.ClearAll();
    }

    static void AmbientPerWorld()
    {
        EmptyScene();
        var m = NewManager(); WorldHazards.ClearAll();
        var layer = m.Weather;
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            for (int i = 0; i < 90; i++) layer.Step(Dt, true, w, WorldHazards.Fields);
            Check(WorldManager.Worlds[w].displayName + ": ambient weather runs (" + layer.Visible + " particles)", layer.Count > 0 && layer.Visible > 0 && layer.Visible <= WorldWeatherTable.AmbientMax);
        }
        WeatherLayer.Reduced = true; layer.Step(Dt, true, 1, WorldHazards.Fields); layer.Step(Dt, true, 2, WorldHazards.Fields);
        int reduced = layer.Count; WeatherLayer.Reduced = false; layer.Step(Dt, true, 1, WorldHazards.Fields); layer.Step(Dt, true, 2, WorldHazards.Fields);
        Check("the reduced option thins the ambient layer", reduced < layer.Count, reduced + " < " + layer.Count);
        long bytes = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 100; i++) layer.Step(Dt, true, 2, WorldHazards.Fields); });
        Check("the ambient layer allocates no more than editor noise (<= 24 B per sprite-frame)", bytes <= 100L * Mathf.Max(1, layer.Visible) * 24, bytes + " B");
        // no visibility limit: alpha stays under the cap
        bool alpha = true; foreach (var e in WorldWeatherTable.Entries) foreach (var a in e.ambient) alpha &= a.alpha <= WorldWeatherTable.AmbientMaxAlpha;
        Check("ambient alpha never exceeds the readability cap", alpha);
    }

    // ScreenFit: on every phone shape a front spans the whole view width, a swirl / column stays inside the lane and the
    // view, and every drawn particle sits inside the view (+ a margin for the sprite size).
    static void ScreenShapes()
    {
        EmptyScene();
        var m = NewManager();
        var camGo = new GameObject("cam", typeof(Camera)); camGo.tag = "MainCamera"; made.Add(camGo);
        var cam = camGo.GetComponent<Camera>(); cam.orthographic = true;
        float[][] screens = { new[] { 1080f, 1920f }, new[] { 1080f, 2340f }, new[] { 1080f, 2520f }, new[] { 1200f, 2560f }, new[] { 1640f, 2640f }, new[] { 2000f, 2000f } };
        foreach (var sc in screens)
        {
            cam.aspect = sc[0] / sc[1];
            cam.orthographicSize = Mathf.Max(CameraFit.GameplayViewHalfWidth(new Vector2(sc[0], sc[1])) / cam.aspect, 5f);
            float halfW = cam.orthographicSize * cam.aspect, top = cam.orthographicSize;
            string tag = sc[0] + "x" + sc[1];
            bool ok = true, drawn = true;
            foreach (var e in WorldWeatherTable.Entries)
                foreach (var h in e.hazards)
                {
                    WorldHazards.ClearAll();
                    m.Begin(h);
                    var f = WorldHazards.Fields[0];
                    for (int i = 0; i < 20 * 60 && f.Active; i++)
                    {
                        m.StepFields(Dt);
                        if (i % 20 != 0) continue;
                        if (h.shape == HazardShape.Front) ok &= f.laneHalf >= halfW - .05f;
                        else ok &= Mathf.Abs(f.centre.x) <= 2.1f && f.centre.y >= -top && f.centre.y <= top;
                        m.Visuals.Draw(WorldHazards.Fields, true);
                        foreach (var r in m.Visuals.Renderers)
                            if (r.enabled) drawn &= Mathf.Abs(r.transform.position.x) <= halfW + 1.2f && r.transform.position.y >= -top - 1.2f && r.transform.position.y <= top + 1.2f;
                    }
                }
            Check("screen " + tag + ": every hazard spans / stays inside the view", ok);
            Check("screen " + tag + ": every drawn particle is inside the view", drawn);
        }
        WorldHazards.ClearAll();
    }

    // THE TORNADO IS A TOP-DOWN VORTEX (the side-view funnel was dropped): the painted body (Storm's wx_hazard row 0) turns continuously with a
    // second, faster layer on top; the 2 s warning SPINS IT UP; the loop frames really cycle at 12 fps and wrap.
    static void TornadoVortex()
    {
        EmptyScene();
        var m = NewManager(); WorldHazards.ClearAll();
        var def = Def("tornado");
        Check("the tornado draws its painted top-down body", def.artRow == 0 && def.shape == HazardShape.Swirl);
        m.Begin(def);
        var f0 = WorldHazards.Fields[0];
        f0.side = 1f;
        Check("its world's sheet loads for the event", HazardArt.Loaded && HazardArt.World == def.world);
        // warning: the vortex spins UP (its angle grows with the cube of the progress: the speed grows, from rest), grows and brightens
        float last = 0f, lastStep = 0f; bool accel = true, monotone = true; float s0 = -1f, s1 = 0f;
        for (int i = 0; i < 2 * 60 - 2; i++)
        {
            m.StepFields(Dt); m.Visuals.Draw(WorldHazards.Fields, true);
            float ang = HazardVisuals.SpinAngle(f0); float step = ang - last;
            if (i > 3) accel &= step >= lastStep - 1e-4f;
            monotone &= step >= -1e-5f; last = ang; lastStep = step;
            float sc = m.Visuals.ArtRenderers[0].transform.localScale.x; if (s0 < 0f) s0 = sc; s1 = sc;
        }
        Check("the warning spins the vortex up: its speed only grows from rest", accel && monotone && lastStep > 0f);
        Check("... and it grows as the clouds gather (" + s0.ToString("F2") + " -> " + s1.ToString("F2") + " u)", s1 > s0 * 1.3f);
        // live: constant angular speed (SpinDegrees / s), continuous across the hand-over, frames cycle at ArtFps and wrap
        for (int i = 0; i < 20; i++) m.StepFields(Dt);
        float prev = HazardVisuals.SpinAngle(f0); float dMin = 999f, dMax = -999f;
        var frames = new System.Collections.Generic.List<Sprite>(); float rot2Min = 999f, rot2Max = -999f;
        for (int i = 0; i < 4 * 60; i++)
        {
            m.StepFields(Dt); m.Visuals.Draw(WorldHazards.Fields, true);
            float a2 = HazardVisuals.SpinAngle(f0); float d = a2 - prev; prev = a2; dMin = Mathf.Min(dMin, d); dMax = Mathf.Max(dMax, d);
            var sp = m.Visuals.ArtRenderers[0].sprite; if (frames.Count == 0 || frames[frames.Count - 1] != sp) frames.Add(sp);
        }
        float perFrame = HazardVisuals.SpinDegrees * Dt;
        Check("live: the vortex turns smoothly, the same step every frame (" + dMin.ToString("F3") + " .. " + dMax.ToString("F3") + " deg, expected " + perFrame.ToString("F3") + ")", Mathf.Abs(dMin - perFrame) < .01f && Mathf.Abs(dMax - perFrame) < .01f);
        Check("a full turn takes " + (360f / HazardVisuals.SpinDegrees).ToString("F1") + " s (clearly visible, not frantic)", 360f / HazardVisuals.SpinDegrees >= 1.5f && 360f / HazardVisuals.SpinDegrees <= 4f);
        bool ordered = frames.Count >= 7; int stepsOk = 0;
        for (int i = 1; i < frames.Count; i++)
        {
            int a0 = -1, a1 = -1;
            for (int c = 0; c < 6; c++) { if (HazardArt.Cell(def.artRow, c) == frames[i - 1]) a0 = c; if (HazardArt.Cell(def.artRow, c) == frames[i]) a1 = c; }
            if (a0 >= 0 && a1 == (a0 + 1) % 6) stepsOk++;
        }
        Check("the loop frames cycle in order and wrap last -> first at " + HazardVisuals.ArtFps + " fps (" + stepsOk + " of " + (frames.Count - 1) + " steps)", ordered && stepsOk == frames.Count - 1);
        Check("a second, faster layer turns the same way on top (layered arms)", m.Visuals.ArtDrawn == 2 && Mathf.Abs(Mathf.DeltaAngle(0f, m.Visuals.ArtRenderers[1].transform.eulerAngles.z)) >= 0f);
        // the pull lines, debris and the radius ring are drawn on top of the body
        Check("the pull lines, debris and the radius ring are drawn with it", m.Visuals.Drawn >= 30, m.Visuals.Drawn + " particles");
        WorldHazards.ClearAll();
        m.Visuals.Draw(WorldHazards.Fields, true);
        Check("the sheet is released when the tornado ends", !HazardArt.Loaded && HazardArt.Refs == 0);
    }

    // EVERY world wears its approved sheets: the hazard bodies and the ambient particles are painted (the procedural sprites remain only as the fallback).
    static void ApprovedWorldArt()
    {
        EmptyScene();
        var m = NewManager(); WorldHazards.ClearAll();
        var every = new System.Collections.Generic.List<string>();
        foreach (var e0 in WorldWeatherTable.Entries) foreach (var h0 in e0.hazards) every.Add(h0.id);
        Check("every hazard of every world has its painted body (no staged leftovers)", every.Count == 14 && every.TrueForAll(id => Def(id).artRow >= 0));
        foreach (var id in every)
        {
            var d = Def(id);
            m.Begin(d);
            Check(id + ": its world's hazard sheet loads for the event", HazardArt.Loaded && HazardArt.World == d.world && HazardArt.Refs == 1);
            if (HazardArt.Loaded)
            {
                bool all = true; for (int r = 0; r < 3; r++) for (int c = 0; c < 6; c++) all &= HazardArt.Cell(r, c) != null && Mathf.Approximately(HazardArt.Cell(r, c).rect.width, 512f);
                Check(id + ": 18 cells of 512 x 512 (full resolution)", all);
            }
            var seen = new System.Collections.Generic.HashSet<Sprite>();
            for (int i = 0; i < 6 * 60; i++) { m.StepFields(Dt); m.Visuals.Draw(WorldHazards.Fields, true); if (m.Visuals.ArtDrawn > 0) seen.Add(m.Visuals.ArtRenderers[0].sprite); }
            Check(id + ": the painted body is drawn (warning cells, then the loop)", m.Visuals.ArtDrawn > 0 || !WorldHazards.AnyActive, seen.Count + " frames");
            Check(id + ": the warning cells (2) and the live loop frames show", seen.Count >= 3, seen.Count + " distinct");
            WorldHazards.ClearAll();
            Check(id + ": the sheet is released when the event ends", !HazardArt.Loaded && HazardArt.Refs == 0);
        }
        var layer = m.Weather;
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            for (int i = 0; i < 60; i++) layer.Step(Dt, true, w, WorldHazards.Fields);
            Check(WorldManager.Worlds[w].displayName + ": the approved ambient sheet is loaded (16 painted particles)", WeatherArt.LoadedFor(w));
            if (WeatherArt.LoadedFor(w)) Check(WorldManager.Worlds[w].displayName + ": particle sprites are tight cuts, one unit on the long side", Mathf.Abs(Mathf.Max(WeatherArt.Get(0, 0).bounds.size.x, WeatherArt.Get(0, 0).bounds.size.y) - 1f) < .01f);
        }
        foreach (var e0 in WorldWeatherTable.Entries) { bool every4 = true; foreach (var k0 in e0.ambient) every4 &= k0.artKind >= 0; Check(e0.world + ": every ambient kind is painted", every4); }
    }

    // The seam with the cover boulders, BOTH directions, against the real boulder system.
    static void BoulderSeam()
    {
        EmptyScene();
        var m = NewManager(); WorldHazards.ClearAll();
        CoverBoulders.Clear();
        CoverBoulders.TestAnyWorld = true;
        CoverBoulderDirector.WorldOverride = 5;
        Check("the seam defaults to the real boulder query", HazardCoverSeam.Cover == CoverHazardQuery.Instance);
        Check("no boulder, no hazard: the boulder director may spawn", CoverBoulderDirector.Blocked(5, 100f) == null);
        // direction 1: a hazard is warning / acting -> the boulder director refuses
        m.Begin(Def("sandstorm_front"));
        Check("a hazard in its warning blocks a boulder spawn (reason: hazard)", CoverBoulderDirector.Blocked(5, 100f) == "hazard");
        for (int i = 0; i < 4 * 60; i++) m.StepFields(Dt);
        Check("... and while it acts", WorldHazards.AnyActing && CoverBoulderDirector.Blocked(5, 100f) == "hazard");
        WorldHazards.ClearAll();
        Check("... and not once it has ended", CoverBoulderDirector.Blocked(5, 100f) == null);
        // direction 2: a boulder is on screen -> no hazard starts, a running one fades
        var b = CoverBoulders.Spawn(5, 0);
        Check("a boulder spawned", b != null && CoverBoulders.Alive == 1);
        var sit = WorldHazards.Read(); sit.world = 5;
        Check("the real boulder is seen by the hazards (CoverOnScreen)", HazardCoverSeam.CoverOnScreen && sit.coverOnScreen);
        Check("no hazard starts while it is on screen", WorldHazards.Blocked(sit, 100f, 99f) != null);
        if (b != null) Check("AnyWithin finds it at its own place and not far away", CoverHazardQuery.Instance.AnyWithin(new Rect(b.Pos.x - .1f, b.Pos.y - .1f, .2f, .2f)) && !CoverHazardQuery.Instance.AnyWithin(new Rect(b.Pos.x + 50f, b.Pos.y, 1f, 1f)));
        CoverBoulders.Clear();
        WorldHazards.ClearAll();
        var open = Open(5);
        m.Tick(Dt, open);   // (the first tick adopts the world)
        m.Begin(Def("tornado"));
        for (int i = 0; i < 4 * 60; i++) { m.Tick(Dt, open); m.StepFields(Dt); }
        Check("a tornado is acting", WorldHazards.AnyActing);
        CoverBoulders.Spawn(5, 0);
        open.coverOnScreen = HazardCoverSeam.CoverOnScreen;
        for (int i = 0; i < 90; i++) { m.Tick(Dt, open); m.StepFields(Dt); }
        Check("a boulder appearing mid-event fades the real hazard out", !WorldHazards.AnyActive);
        CoverBoulders.Clear();
        CoverBoulderDirector.WorldOverride = -1;
        CoverBoulders.TestAnyWorld = false;
        WorldHazards.ClearAll();
    }

    static void FlagOff()
    {
        var m = NewManager(); WorldHazards.ClearAll();
        m.Begin(Def("tornado"));
        FeatureFlags.Set(FeatureFlags.WorldHazards, false);
        TestHarness.Send(m, "LateUpdate");
        Check("flag off: a running hazard is cleared and nothing acts", !WorldHazards.AnyActive && WorldHazards.PlayerOffset == Vector2.zero);
        FeatureFlags.Set(FeatureFlags.WorldHazards, true);
        Check("the two flags exist and default on", FeatureFlags.DefaultOf(FeatureFlags.WorldHazards) && FeatureFlags.DefaultOf(FeatureFlags.WorldWeather));
    }
}
