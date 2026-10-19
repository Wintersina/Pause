using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders each elite's whole fight for review (the elite-overhaul GIFs), a world at a time (CaptureSpace, CaptureFrost): its home dock in
// the world's real backdrop (Space: one of the drifting gantry / carrier / gate / derelict docks, SpaceDocks.cs; Frost: the hangar /
// rig bay / pad ring / crawler bay / silo hatch on the ground, FrostDirector; brought into view like the elite director does), the launch (the
// one small blinking light at the door / pad, the dock's doors opening, the elite simply leaving), the hull's animated lights, then the real brain and attacks at full speed against a scripted pilot
// that dodges what it can see (hazards on their paths, locked lines and rings, the curtain's gap) and shoots when it has a clear
// moment -- one guaranteed homing hit every ShootEvery seconds while the elite is on the board -- through the hide, the death or the
// escape. Frames are 540 px wide portrait PNGs at 15 fps.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod EliteCapture.CaptureSpace   (or CaptureFrost)
//   (frames to $ELITE_CAPTURE_DIR/<name>/NNNN.png, else Builds/EliteCapture; $ELITE_CAPTURE_ONLY=<name>[,<name>] renders just those;
//    $ELITE_CAPTURE_SECONDS caps a clip, default 70)
//
// then scripts/elite-gifs.sh <dir> [out-dir] turns every frame folder into /Users/sina/Developer/Pause/.claude/elite-gifs/<world>/<name>.gif and
// <name>.mp4.
// Needs a rendering editor (graphics on: no -nographics), as ElitePreview does. This renders the elite stack over the real world
// backdrop with stand-in rails and the pilot's hull; it is not the full game scene (no HUD, spawners, weapons or boss).
public static class EliteCapture
{
    const float Fps = 15f, SimDt = 1f / 60f, ShootEvery = 4.5f;
    const int Width = 540;
    const float HalfH = 6.4f, HalfW = 2.95f;
    const float PilotSpeed = 5.2f, PilotHomeY = -3.1f;

    static readonly (string name, string key)[] SpaceClips =
    {
        ("rift_lancer", "space_elite_rift_lancer"),
        ("orbit_reaver", "space_elite_orbit_reaver"),
        ("eventide_bastion", "space_elite_eventide_bastion"),
        ("singularity_hauler", "space_elite_singularity_hauler"),
    };

    static readonly (string name, string key)[] FrostClips =
    {
        ("rimebreaker", "frost_elite_rimebreaker"),
        ("floe_harrower", "frost_elite_floe_harrower"),
        ("cryo_siren", "frost_elite_cryo_siren"),
        ("glacier_tender", "frost_elite_glacier_tender"),
        ("whiteout_sentinel", "frost_elite_whiteout_sentinel"),
    };

    static string world = "Space";

    public static void CaptureSpace() { Capture("Space", SpaceClips); }
    public static void CaptureFrost() { Capture("Frost", FrostClips); }

    // Just the launch (a short, zoomed clip of the dock / pad: the blinking light, the doors, the elite leaving): a Space station, a Space
    // dock and a Frost ground pad. $ELITE_CAPTURE_DIR/<name>/NNNN.png as the others.
    static readonly (string name, string key, string world, string kind)[] LaunchClips =
    {
        ("launch_space_station", "space_elite_rift_lancer", "Space", "station"),
        ("launch_space_dock", "space_elite_orbit_reaver", "Space", "carrier"),
        ("launch_frost_pad", "frost_elite_whiteout_sentinel", "Frost", "padring"),
    };
    static string launchFrom;      // the launch clips' site kind (null: the def's own launchFrom)
    static bool launchOnly;

    public static void CaptureLaunch()
    {
        launchOnly = true;
        foreach (var c in LaunchClips)
        {
            launchFrom = c.kind;
            Capture(c.world, new[] { (c.name, c.key) }, false);
        }
        EditorApplication.Exit(0);
    }

    static void Capture(string worldName, (string name, string key)[] clips, bool exit = true)
    {
        world = worldName;
        string dir = System.Environment.GetEnvironmentVariable("ELITE_CAPTURE_DIR");
        if (string.IsNullOrEmpty(dir)) dir = "Builds/EliteCapture";
        string only = System.Environment.GetEnvironmentVariable("ELITE_CAPTURE_ONLY");
        Directory.CreateDirectory(dir);
        int code = 0;
        using (new TestHarness.Sandbox())
        {
            EliteCatalog.Reload();
            foreach (var c in clips)
            {
                if (!string.IsNullOrEmpty(only) && System.Array.IndexOf(only.Split(','), c.name) < 0) continue;
                try { Clip(dir, c.name, EliteCatalog.Find(c.key)); }
                catch (System.Exception e) { Debug.LogError("[EliteCapture] " + c.name + ": " + e); code = 1; }
            }
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            LandingSites.Override = null;
        }
        if (exit) EditorApplication.Exit(code);
    }

    // ---- the scene ---------------------------------------------------------------------------------

    static Camera cam;
    static Transform pilot;
    static SpriteRenderer pilotArt;
    static RenderTexture rt;
    static Texture2D png;
    static Sprite white;

    static Sprite White()
    {
        if (white != null) return white;
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, Color.white);
        t.Apply();
        white = Sprite.Create(t, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1f);
        return white;
    }

    static void Fresh(EliteDef def)
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .3f;
        RunScore.BeginRun(true, false);
        Random.InitState(def.key.Length * 131 + 7);

        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(.04f, .03f, .09f);
        cam.orthographicSize = HalfH;
        cam.aspect = HalfW / HalfH;
        cam.transform.position = new Vector3(0f, 0f, -10f);

        if (world == "Frost") { FrostBackdropSelection.Reset(); FrostBackdropSelection.Seed(def.key.Length * 7 + 3); }
        var wb = WorldBackdrop.Create(world);
        wb.Show(world, false);
        Rails();
        pilot = new GameObject("~Pilot").transform;
        pilotArt = pilot.gameObject.AddComponent<SpriteRenderer>();
        pilotArt.sprite = shopingShips.SpriteFor(1, 0);
        pilotArt.sortingOrder = 6;
        float s = shopingShips.NormalizedHullScale(pilotArt.sprite);
        pilot.localScale = new Vector3(s, s, 1f);
        pilot.position = new Vector3(0f, PilotHomeY, 0f);
        EliteSystem.PlayerOverride = pilot;
        LandingSites.Override = null;
        PilotState.Clear();
    }

    static void Rails()
    {
        float edge = EliteSystem.RailEdge;
        for (int side = -1; side <= 1; side += 2)
        {
            var go = new GameObject("~Rail");
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = White();
            r.color = new Color(.22f, .16f, .2f);
            r.sortingOrder = 0;
            go.transform.position = new Vector3(side * (edge + .5f), 0f, 0f);
            go.transform.localScale = new Vector3(1f, HalfH * 2f, 1f);
            for (int i = 0; i < 9; i++)
            {
                var lamp = new GameObject("~Lamp").AddComponent<SpriteRenderer>();
                lamp.sprite = White();
                lamp.color = i % 3 == 0 ? new Color(.4f, .95f, 1f) : new Color(1f, .3f, .55f);
                lamp.sortingOrder = 1;
                lamp.transform.position = new Vector3(side * (edge + .12f), -HalfH + 1.4f * i + .3f, 0f);
                lamp.transform.localScale = new Vector3(.08f, i % 3 == 0 ? .5f : .18f, 1f);
            }
        }
    }

    // ---- the pilot: dodges, shoots ---------------------------------------------------------------------

    // A hazard the pilot steers clear of: a moving circle (position, velocity, radius) or, with `b` set, a segment a -> b `radius` wide.
    struct Danger { public Vector2 a, v, b; public float radius, until; public bool line; }

    static readonly List<Danger> dangers = new List<Danger>(64);

    sealed class Bolt
    {
        public SpriteRenderer sr;
        public Vector2 pos, vel;
        public bool hurts, live;
        public float age;
        public GameObject drone;   // a bolt that goes for one of the Tender's drones
    }

    static readonly List<Bolt> bolts = new List<Bolt>();
    static Vector2 pilotGoal = new Vector2(0f, PilotHomeY);
    static int hitsTaken, shotsHit;
    static float lastShot = 3f, lastCosmetic, lastDroneShot;
    static float flash;

    static class PilotState
    {
        public static void Clear()
        {
            foreach (var b in bolts) if (b.sr != null) Object.DestroyImmediate(b.sr.gameObject);
            bolts.Clear();
            dangers.Clear();
            hitsTaken = shotsHit = 0;
            lastShot = 3f;
            lastDroneShot = 0f;
            lastCosmetic = 0f;
            flash = 0f;
            pilotGoal = new Vector2(0f, PilotHomeY);
        }
    }

    // Everything on the board that could hurt, as dangers over the next moments.
    static void Sense(EliteShip e)
    {
        dangers.Clear();
        var shots = EliteSystem.ShotsIfAny;
        if (shots != null)
        {
            foreach (var s in shots.All)
            {
                if (!s.Active || s.Airborne) continue;
                var d = new Danger { a = s.transform.position, v = s.Velocity, radius = s.Radius + .35f, until = 9f };
                dangers.Add(d);
                if (s.IsMine)
                {
                    // a mine: its body, and the cross / ring it is about to open into (lines from where it is)
                    float reach = s.MineArmSpeed * s.MineArmSeconds + .4f;
                    if (s.FuseLeft < 1.5f)
                        for (int k = 0; k < s.MineArms; k++)
                        {
                            float r = (k * 360f / s.MineArms) * Mathf.Deg2Rad;
                            Vector2 a = s.transform.position;
                            dangers.Add(new Danger { a = a, b = a + new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * reach, radius = .5f, line = true, until = 9f });
                        }
                    else dangers.Add(new Danger { a = s.transform.position, v = s.Velocity, radius = 1.2f, until = 9f });
                }
            }
        }
        foreach (var r in ThrownRock.All)
        {
            if (r == null) continue;
            var ct = r.GetComponent<ClearTarget>();
            float rad = (ct != null ? ct.Radius : .5f) + .4f;
            dangers.Add(new Danger { a = r.transform.position, v = r.Flung ? r.Velocity : Vector2.zero, radius = rad, until = 9f });
        }
        if (e == null || e.State == EliteState.Dead) return;
        // the telegraphs the elites draw, read by the pilot as a player would
        var snipe = e.Attack as RiftSnipeAttack;
        if (snipe != null && (e.Telling || e.Acting) && snipe.Locked)
        {
            Vector2 m = e.MuzzleWorld(0);
            dangers.Add(new Danger { a = m, b = m + snipe.Dir * RiftSnipeAttack.SightLength, radius = .75f, line = true, until = 9f });
        }
        var swoop = e.Attack as SwoopVolleyAttack;
        if (swoop != null && (e.Telling || e.Acting))
            dangers.Add(new Danger { a = swoop.LockedAt, radius = 1.15f, until = 9f });
        var tow = e.Attack as TowSlingAttack;
        if (tow != null && tow.Lane.Showing)
            dangers.Add(new Danger { a = tow.ClawPoint, b = tow.ClawPoint + tow.FlingDir * 14f, radius = tow.RockRadius + .55f, line = true, until = 9f });
        if (tow != null && tow.SlingCast && (e.Telling || e.Acting))
            dangers.Add(new Danger { a = tow.Well, radius = 1.3f, until = 9f });
        var barrage = e.Attack as WardBarrageAttack;
        if (barrage != null && barrage.MineDrop && (e.Telling || e.Acting))
            for (int k = 0; k < barrage.Mines; k++) if (barrage.Marks.Shown(k)) dangers.Add(new Danger { a = barrage.Marks.At(k), radius = 1.1f, until = 9f });
        SenseFrost(e);
    }

    // Frost's telegraphs, read as a player would: the ice-crack lane and the ship ploughing down it, the skid's path, the lance's sight,
    // the beam's arc, a mine's ring, the fog cone and the lanes marked with icicle glyphs.
    static void SenseFrost(EliteShip e)
    {
        var run = e.Attack as IceRunAttack;
        if (run != null)
        {
            if (e.Telling) dangers.Add(new Danger { a = new Vector2(run.LaneX, e.Position.y), b = new Vector2(run.LaneX, -HalfH - 1f), radius = e.Def.hullRadius + .55f, line = true, until = 9f });
            else if (e.Acting) dangers.Add(new Danger { a = e.Position, v = e.Velocity, radius = e.Def.hullRadius + .55f, until = 9f });
        }
        var barge = e.Attack as FloeBargeAttack;
        if (barge != null)
        {
            if (barge.Skid && (e.Telling || e.Acting))
            {
                dangers.Add(new Danger { a = barge.PathFrom, b = barge.BounceAt, radius = .85f, line = true, until = 9f });
                dangers.Add(new Danger { a = barge.BounceAt, b = barge.PathEnd, radius = .85f, line = true, until = 9f });
            }
            else if (!barge.Skid && (e.Telling || e.Acting) && !barge.Lanced)
            {
                Vector2 k = e.MuzzleWorld(FloeCastAttack.Keel);
                dangers.Add(new Danger { a = k, b = barge.LanceAim, radius = .6f, line = true, until = 9f });
            }
        }
        var bloom = e.Attack as FrostBloomAttack;
        if (bloom != null && bloom.Beam && (e.Telling || e.Acting))
        {
            Vector2 m = e.MuzzleWorld(0);
            for (int k = 0; k <= 4; k++)
            {
                float r = Mathf.Lerp(bloom.BeamFrom, bloom.BeamTo, k / 4f) * Mathf.Deg2Rad;
                dangers.Add(new Danger { a = m, b = m + new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * 9f, radius = .5f, line = true, until = 9f });
            }
        }
        var rig = e.Attack as TenderRigAttack;
        if (rig != null)
        {
            if (rig.MinePending) dangers.Add(new Danger { a = rig.MineSpot, radius = 1.1f, until = 9f });
            if (rig.Venting && (e.Telling || e.Acting))
                dangers.Add(new Danger { a = rig.FogOrigin, b = rig.FogOrigin + Vector2.down * TenderRigAttack.FogLength, radius = TenderRigAttack.FogTip + .55f, line = true, until = 9f });
        }
        var icicles = e.Attack as IcicleBarrageAttack;
        if (icicles != null && icicles.Planned && (e.Telling || e.Acting))
            for (int c = 0; c < icicles.Columns; c++)
                if (icicles.Marked(c)) dangers.Add(new Danger { a = new Vector2(icicles.ColumnX(c), HalfH), b = new Vector2(icicles.ColumnX(c), -HalfH), radius = .5f, line = true, until = 9f });
    }

    static float Clearance(Vector2 from, Vector2 to, float speed)
    {
        float best = 9f;
        float travel = Vector2.Distance(from, to);
        for (float t = 0f; t <= 1.3f; t += .1f)
        {
            float along = Mathf.Min(travel, speed * t);
            Vector2 p = travel > 1e-4f ? from + (to - from) * (along / travel) : from;
            for (int i = 0; i < dangers.Count; i++)
            {
                var d = dangers[i];
                float c;
                if (d.line)
                {
                    Vector2 ab = d.b - d.a;
                    float k = Mathf.Clamp01(Vector2.Dot(p - d.a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                    c = Vector2.Distance(p, d.a + ab * k) - d.radius;
                }
                else c = Vector2.Distance(p, d.a + d.v * t) - d.radius;
                if (c < best) best = c;
            }
        }
        return best;
    }

    // Where the pilot wants to be: the curtain's gap when there is one to read, else the safest of a grid near its home.
    static Vector2 ChooseGoal(EliteShip e, float clock)
    {
        Vector2 here = pilot.position;
        float homeX = Mathf.Sin(clock * .45f) * 1.1f;
        var barrage = e != null ? e.Attack as WardBarrageAttack : null;
        if (barrage != null && !barrage.MineDrop && (e.Telling || e.Acting))
        {
            // the gap, read off the rings; the second gap once the first row has gone by
            int g = barrage.SecondRow ? barrage.Gap2 : barrage.Gap;
            homeX = barrage.Spot(g, g).x;
        }
        var icicles = e != null ? e.Attack as IcicleBarrageAttack : null;
        if (icicles != null && icicles.Planned && (e.Telling || e.Acting)) homeX = icicles.ColumnX(icicles.Safe);   // the bare lane
        Vector2 home = new Vector2(Mathf.Clamp(homeX, -2.1f, 2.1f), PilotHomeY);
        Vector2 best = here;
        float bestScore = float.MinValue;
        for (float x = -2.15f; x <= 2.151f; x += .18f)
            for (float y = -3.6f; y <= -1.8f; y += .9f)
            {
                var c = new Vector2(x, y);
                float clear = Clearance(here, c, PilotSpeed);
                float score = Mathf.Min(clear, 1.2f) * 3f - Vector2.Distance(c, home) * .45f - Vector2.Distance(c, here) * .08f;
                if (clear < 0f) score -= 8f;
                if (score > bestScore) { bestScore = score; best = c; }
            }
        return best;
    }

    static Bolt SpawnBolt(Vector2 at, Vector2 vel, bool hurts)
    {
        Bolt b = null;
        foreach (var x in bolts) if (!x.live) { b = x; break; }
        if (b == null)
        {
            b = new Bolt();
            var go = new GameObject("~Bolt");
            b.sr = go.AddComponent<SpriteRenderer>();
            b.sr.sprite = EliteFxArt.Bolt;
            b.sr.sortingOrder = 7;
            bolts.Add(b);
        }
        b.live = true;
        b.drone = null;
        b.hurts = hurts;
        b.pos = at;
        b.vel = vel;
        b.age = 0f;
        b.sr.enabled = true;
        b.sr.color = hurts ? new Color(.6f, 1f, 1f) : new Color(.45f, .85f, 1f, .75f);
        b.sr.transform.localScale = Vector3.one * (hurts ? 1.2f : .8f) * .45f;
        return b;
    }

    static void StepPilot(EliteShip e, float dt, float clock, int frame)
    {
        if (frame % 6 == 0) { Sense(e); pilotGoal = ChooseGoal(e, clock); }
        pilot.position = Vector2.MoveTowards(pilot.position, pilotGoal, PilotSpeed * dt);
        Vector2 p = pilot.position;
        // taking a hit (diagnostics: the scripted pilot is never actually hurt)
        if (flash > 0f) flash -= dt;
        var shots = EliteSystem.ShotsIfAny;
        if (shots != null && flash <= 0f)
            foreach (var s in shots.All)
                if (s.Active && !s.Airborne && Vector2.Distance(s.transform.position, p) < s.Radius + .22f) { hitsTaken++; flash = .5f; break; }
        if (flash <= 0f)
            foreach (var r in ThrownRock.All)
                if (r != null && Vector2.Distance(r.transform.position, p) < r.GetComponent<ClearTarget>().Radius + .22f) { hitsTaken++; flash = .5f; break; }
        pilotArt.color = flash > 0f ? new Color(1f, .5f, .5f) : Color.white;

        // the pilot's fire: a cosmetic stream, and every ShootEvery seconds a homing bolt that connects while the elite can be hit
        lastCosmetic += dt;
        if (lastCosmetic >= .22f) { lastCosmetic = 0f; SpawnBolt(p + Vector2.up * .4f, Vector2.up * 11f, false); }
        lastShot += dt;
        bool hittable = e != null && e.InPlay && !e.Offstage && !e.Hidden && e.State != EliteState.Retreat;
        var shielded = e != null ? e.Attack as DroneDeployAttack : null;
        if (shielded != null && shielded.Linked) hittable = false;   // (its shield link would soak it: the drones first)
        // (the pilot lets it show what it does first: two attacks before the first hit, one more before each next)
        int needed = shotsHit == 0 ? 2 : shotsHit + 2;
        if (hittable && lastShot >= ShootEvery && e.Attacks >= needed) { lastShot = 0f; SpawnBolt(p + Vector2.up * .4f, Vector2.up * 11f, true); }
        // the Tender's shield is shot away the way a player does it: a bolt at a drone now and then (the drones die as they do to a pilot)
        lastDroneShot += dt;
        var rig = e != null ? e.Attack as TenderRigAttack : null;
        if (rig != null && e.InPlay && rig.Live >= 1 && e.Attacks >= 1 && lastDroneShot >= 2.2f)
        {
            GameObject near = null;
            float nd = float.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                var d = rig.Drone(k);
                if (d == null) continue;
                float dd = Vector2.Distance(d.transform.position, p);
                if (dd < nd) { nd = dd; near = d; }
            }
            if (near != null) { lastDroneShot = 0f; SpawnBolt(p + Vector2.up * .4f, Vector2.up * 11f, true).drone = near; }
        }
        for (int i = 0; i < bolts.Count; i++)
        {
            var b = bolts[i];
            if (!b.live) continue;
            b.age += dt;
            if (b.hurts && b.drone != null)
            {
                if (b.drone.activeInHierarchy)
                {
                    Vector2 toD = (Vector2)b.drone.transform.position - b.pos;
                    b.vel = Vector2.MoveTowards(b.vel, toD.normalized * 11f, 60f * dt);
                    if (toD.magnitude < .35f) { EliteShip.FriendlyKill(b.drone); b.live = false; b.sr.enabled = false; continue; }
                }
                else { b.live = false; b.sr.enabled = false; continue; }
            }
            else if (b.hurts && e != null && e.InPlay && !e.Offstage)
            {
                // homing: bends toward the elite
                Vector2 to = (Vector2)e.Position - b.pos;
                Vector2 want = to.normalized * 11f;
                b.vel = Vector2.MoveTowards(b.vel, want, 60f * dt);
                if (to.magnitude < e.Def.hullRadius + .2f)
                {
                    e.TakeShipAttack(0, 1f, b.pos);
                    shotsHit++;
                    b.live = false;
                    b.sr.enabled = false;
                    continue;
                }
            }
            b.pos += b.vel * dt;
            b.sr.transform.position = new Vector3(b.pos.x, b.pos.y, 0f);
            b.sr.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.vel.y, b.vel.x) * Mathf.Rad2Deg - 90f);
            // a mine takes a shot as any shot does
            if (shots != null && b.live)
                foreach (var s in shots.All)
                {
                    if (!s.Active || !s.IsMine) continue;
                    if (Vector2.Distance(s.transform.position, b.pos) < s.Radius + .3f) { s.Struck(); b.live = false; b.sr.enabled = false; break; }
                }
            if (b.age > 2.5f || b.pos.y > HalfH + 1f) { b.live = false; b.sr.enabled = false; }
        }
    }

    // ---- a clip ---------------------------------------------------------------------------------------------

    static void Clip(string root, string name, EliteDef def)
    {
        Fresh(def);
        string dir = Path.Combine(root, name);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        float maxSeconds = 70f;
        float.TryParse(System.Environment.GetEnvironmentVariable("ELITE_CAPTURE_SECONDS"), out maxSeconds);
        if (maxSeconds < 5f) maxSeconds = 70f;
        int px = Width, py = Mathf.RoundToInt(Width / cam.aspect);
        rt = new RenderTexture(px, py, 24);
        png = new Texture2D(px, py, TextureFormat.RGB24, false);

        // the dock: its home (the first kind of its launchFrom: Space's gantry / carrier / gate / derelict dock, Frost's hangar / rig bay /
        // pad ring / crawler bay / hatch, brought into view as the elite director does a few seconds before a launch)
        var sites = new List<LandingSite>();
        string from = launchFrom ?? def.launchFrom;
        var home = LandingSite.KindOf(from);
        LandingSites.BringHome(from);
        int pick = -1;
        for (int i = 0; i < 2400 && pick < 0; i++)
        {
            WorldBackdrop.Instance.Step(1f / 15f);
            if (i % 30 == 29) LandingSites.BringHome(from);   // (Frost's ground may have had no room at the top the first time: ask again)
            LandingSites.Collect(sites);
            for (int k = 0; k < sites.Count && pick < 0; k++) if (home == null || sites[k].kind == home.Value) pick = k;
        }
        if (pick < 0) throw new System.Exception("the " + world + " backdrop never offered a " + home + " dock");
        var site = sites[pick];
        var e = EliteShip.Create(def, site, 2.4f, EliteDirector.JoinPoint(def, EliteBrains.Create(def.brain).JoinFrom, site.Position));
        var title = Label(def.displayName.ToUpperInvariant(), HalfH - .2f, .06f, new Color(.75f, .95f, 1f));
        var state = Label("", HalfH - .75f, .042f, new Color(1f, .75f, .95f));

        int frame = 0, simFrame = 0;
        float launchAfter = 0f;
        float clock = 0f, afterDeath = 0f;
        var log = new StringBuilder();
        var seenStates = new HashSet<string>();
        int hearts0 = def.hearts;
        string last = "";
        int framesPerShot = Mathf.RoundToInt(1f / Fps / SimDt);
        for (int step = 0; step < maxSeconds / SimDt; step++)
        {
            clock += SimDt;
            if (launchOnly)
            {
                // zoomed on the dock / pad, following it
                cam.orthographicSize = 1.9f;
                Vector2 sp = site.Valid ? site.Position : (Vector2)e.transform.position;
                cam.transform.position = new Vector3(sp.x, sp.y + .3f, -10f);
                if (e.State == EliteState.Join || e.State == EliteState.Follow || e.State == EliteState.Attack) { launchAfter += SimDt; if (launchAfter > .8f) break; }
            }
            else StepPilot(e, SimDt, clock, simFrame++);
            WorldBackdrop.Instance.Step(SimDt);
            EliteSystem.Step(SimDt);
            bool gone = e == null || e.State == EliteState.Dead;
            if (step % framesPerShot == 0)
            {
                float fdt = 1f / Fps;
                var h = e != null ? e.GetComponent<EliteHearts>() : null;
                if (h != null) { h.Place(fdt, fdt); h.StepBreaks(fdt); }
                string s = gone ? "DOWN" : Describe(e);
                state.text = s + (gone ? "" : "   HEARTS " + e.Hearts + "/" + hearts0);
                if (s != last) { last = s; seenStates.Add(s); log.Append((frame / Fps).ToString("0.0") + "s " + s + "; "); }
                if (launchOnly)
                {
                    Vector2 sp = site.Valid ? site.Position : (Vector2)e.transform.position;
                    cam.orthographicSize = 1.9f;
                    cam.transform.position = new Vector3(sp.x, sp.y + .3f, -10f);
                }
                Shoot(Path.Combine(dir, frame.ToString("0000") + ".png"), px, py);
                frame++;
            }
            if (gone) { afterDeath += SimDt; if (afterDeath > 2.2f) break; }
        }
        int escapes = EliteShip.Escapes;
        Debug.Log("[EliteCapture] " + name + ": " + frame + " frames -> " + dir + "; pilot hits taken " + hitsTaken + ", player hits landed " + shotsHit +
                  ", elite " + (e == null ? "gone" : e.State.ToString()) + ", escapes " + escapes + "; timeline: " + log);
        File.WriteAllText(Path.Combine(dir, "meta.txt"), "frames " + frame + "\nfps " + Fps + "\nhitsTaken " + hitsTaken + "\nshotsHit " + shotsHit + "\ntimeline " + log + "\n");
        Object.DestroyImmediate(title);
        Object.DestroyImmediate(state);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(png);
    }

    static string Describe(EliteShip e)
    {
        switch (e.State)
        {
            case EliteState.Parked: return e.EngineLightsOn ? "DOCKED: LAUNCH TELL" : "DOCKED";
            case EliteState.LiftOff: return "LAUNCH";
            case EliteState.Join: return "JOINING";
            case EliteState.Hide: return "HIDING";
            case EliteState.Retreat: return "ESCAPING";
            case EliteState.Attack: return (e.Telling ? "TELL: " : "ATTACK: ") + e.Attack.Id.ToUpperInvariant();
            default: return e.Offstage ? "OFF-SCREEN" : "ON THE BOARD";
        }
    }

    static TextMesh Label(string text, float y, float size, Color color)
    {
        var go = new GameObject("~Label");
        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        go.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
        go.GetComponent<MeshRenderer>().sortingOrder = 100;
        tm.characterSize = size;
        tm.fontSize = 48;
        tm.anchor = TextAnchor.UpperCenter;
        tm.color = color;
        go.transform.position = new Vector3(0f, y, -2f);
        return tm;
    }

    static void Shoot(string path, int px, int py)
    {
        cam.targetTexture = rt;
        cam.Render();
        var old = RenderTexture.active;
        RenderTexture.active = rt;
        png.ReadPixels(new Rect(0, 0, px, py), 0, 0);
        png.Apply();
        File.WriteAllBytes(path, png.EncodeToPNG());
        RenderTexture.active = old;
        cam.targetTexture = null;
    }
}
