using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The elite ships (Scripts/Gameplay/Elites): data, life cycle, the cell
// maps (Ember layout, flight layout), the personalities and attacks (six
// Ember, Frost's Rimebreaker, Verdant's Resin Warden; the four Space elites'
// own brains / attacks and their launches out of stations, planets and
// asteroids are SpaceEliteTest's), shots from muzzles,
// friendly fire, dodging and baited crashes, perception after a teleport,
// hearts, every damage source, rewards, the director's limits, Frost and
// Verdant landing sites, freezing, allocations and the codex silhouettes.
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod EliteTest.Run
public static class EliteTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ELT] PASS  " : "[ELT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    static readonly MethodInfo Trigger = typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst);

    static Transform pilot;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EliteCatalog.Reload();
            Data();
            LifeCycle();
            Interceptor();
            Gunship();
            Striker();
            Hauler();
            Skirmisher();
            Siege();
            CellMaps();
            Breaker();
            Warden();
            ShotsLeaveMuzzles();
            FriendlyFire();
            Dodging();
            NoTeleportWithPilot();
            Hearts();
            DamageSources();
            Rewards();
            Director();
            WorldSites();
            Frozen();
            Allocations();
            Codex();
        }
        finally
        {
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            LandingSites.Override = null;
            RunScore.EndRun(RunScore.RunId);
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
            startMenu.youAreInTutorial = false;
        }
        Debug.Log("[ELT] failures: " + fails);
        return fails;
    }

    // ---- fixtures -----------------------------------------------------------

    static void Fresh(float speed = .25f)
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = speed;
        collisionDetection.lifeCounter = 0;
        collisionDetection.atomCheck = false;
        collisionDetection.cloakTimer = 0f;
        PlayerInvuln.Reset();
        RunScore.BeginRun(true, true);
        pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -2.5f, 0f);
        EliteSystem.PlayerOverride = pilot;
        LandingSites.Override = null;
        Random.InitState(1234);
    }

    static EliteDef Def(string brain)
    {
        foreach (var d in EliteCatalog.All) if (d.brain == brain) return d;
        return null;
    }

    static EliteShip InPlay(string brain, Vector2 at)
    {
        var e = EliteShip.CreateInPlay(Def(brain), at);
        e.AttackCooldown = 99f;
        return e;
    }

    static void Step(float seconds, System.Action each = null)
    {
        for (float t = 0f; t < seconds - 1e-4f; t += Dt)
        {
            if (each != null) each();
            EliteSystem.Step(Dt);
        }
    }

    // A board hazard (a rock) the test moves itself at the scroll speed.
    static GameObject Rock(Vector2 at)
    {
        var def = EnemyRoster.One(3, EnemyRole.Rock);
        var go = EnemyFactory.Create(def, at, Quaternion.identity);
        ClearTarget.Ensure(go);
        return go;
    }

    static GameObject Enemy(EnemyRole role, Vector2 at)
    {
        var go = EnemyFactory.Create(EnemyRoster.One(3, role), at, Quaternion.identity);
        ClearTarget.Ensure(go);
        return go;
    }

    static void Fall(GameObject go, float dt)
    {
        if (go != null) go.transform.position += Vector3.down * SpawnSpace.ScrollSpeed * dt;
    }

    static float Hue(Color c) { float h, s, v; Color.RGBToHSV(c, out h, out s, out v); return h * 360f; }
    static float HueGap(Color a, Color b) { float d = Mathf.Abs(Hue(a) - Hue(b)) % 360f; return Mathf.Min(d, 360f - d); }
    static readonly Color PlayerRed = new Color32(255, 62, 78, 255);

    // ---- data ----------------------------------------------------------------

    static void Data()
    {
        var ember = new List<EliteDef>();
        int n = EliteCatalog.ForWorld(3, ember);
        Check("six Ember elites are defined (" + n + ")", n == 6);
        var frost = new List<EliteDef>();
        var verdant = new List<EliteDef>();
        EliteCatalog.ForWorld(1, frost);
        EliteCatalog.ForWorld(2, verdant);
        Check("Frost has its own elite: the Rimebreaker (" + frost.Count + ")", frost.Count == 1 && frost[0].key == "frost_elite_rimebreaker");
        Check("Verdant has its own elite: the Resin Warden (" + verdant.Count + ")", verdant.Count == 1 && verdant[0].key == "verdant_elite_resin_warden");
        var space = new List<EliteDef>();
        EliteCatalog.ForWorld(0, space);
        bool spaceSet = space.Count == 4;
        foreach (var d in space) spaceSet &= LandingSite.KindOf(d.launchFrom) != null;
        Check("Space has its four elites, each launching from a station, a planet or an asteroid (" + space.Count + ")", spaceSet);
        var brains = new HashSet<string>();
        var attacks = new HashSet<string>();
        foreach (var d in ember) { brains.Add(d.brain); attacks.Add(d.attack); }
        Check("six different Ember brains", brains.Count == 6);
        Check("six different Ember attacks", attacks.Count == 6);
        brains.Clear();
        attacks.Clear();
        foreach (var d in EliteCatalog.All)
        {
            brains.Add(d.brain);
            attacks.Add(d.attack);
            var frames = EliteArt.Frames(d);
            Check(d.key + ": strip loads as 7 cells, enough for its cell map", frames != null && frames.Length == EliteArt.FrameCount && frames[0] != null && frames.Length >= d.cells.Count);
            Check(d.key + ": a known brain and attack", System.Array.IndexOf(EliteBrains.Ids, d.brain) >= 0 && System.Array.IndexOf(EliteAttacks.Ids, d.attack) >= 0);
            Check(d.key + ": two hearts", d.hearts == 2);
            Check(d.key + ": hearts are not the player's red (hue gap " + HueGap(d.HeartColor, PlayerRed).ToString("0") + ")",
                  HueGap(d.HeartColor, PlayerRed) > 40f && HueGap(d.HeartColor, AkiraPalette.Red) > 40f);
            Check(d.key + ": shots are not the player's red", HueGap(d.ShotColor, PlayerRed) > 30f && HueGap(d.ShotCore, PlayerRed) > 30f &&
                  HueGap(d.ShotColor, AkiraPalette.Red) > 30f);
            Check(d.key + ": has muzzles and nozzles", d.muzzles.Length > 0 && d.nozzles.Length > 0);
            Check(d.key + ": a codex id", !string.IsNullOrEmpty(d.codexId) && d.codexId.StartsWith("elite_"));

            // every muzzle sits on the drawing (the action / tell cell, else
            // the flight frame), every nozzle on the flight frame
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(CopyPath(d)));
            bool onArt = true;
            var c = d.cells;
            foreach (var m in d.muzzles)
                onArt &= (c.action >= 0 && Opaque(tex, d, c.action, m)) || (c.tell >= 0 && Opaque(tex, d, c.tell, m)) || Opaque(tex, d, c.Flight0, m);
            foreach (var z in d.nozzles) onArt &= Opaque(tex, d, c.Flight0, z);
            Object.DestroyImmediate(tex);
            Check(d.key + ": muzzles and nozzles land on the art", onArt);
            string src = SourcePath(d);
            Check(d.key + ": the original art is untouched (Resources copy matches " + src + ")",
                  File.Exists(src) && File.ReadAllBytes(CopyPath(d)).Length == File.ReadAllBytes(src).Length);
        }
        Check("every elite has its own brain (" + brains.Count + ")", brains.Count == EliteCatalog.All.Length);
        Check("every elite has its own attack (" + attacks.Count + ")", attacks.Count == EliteCatalog.All.Length);
        Check("the elite heart is white art (tinted per elite)", Resources.Load<Sprite>(EliteHearts.SpritePath) != null);
    }

    static string CopyPath(EliteDef d) => "Assets/Art/Resources/Elites/" + EliteArt.WorldFolder(d) + "/" + d.key + ".png";

    // Codex's original: <world>_elite_<name>.png, or delivered bare (<name>.png).
    static string SourcePath(EliteDef d)
    {
        string dir = "Assets/Art/Enemies/Elite/" + EliteArt.WorldFolder(d) + "/";
        if (File.Exists(dir + d.key + ".png")) return dir + d.key + ".png";
        return dir + d.key.Substring(d.key.IndexOf("_elite_") + 7) + ".png";
    }

    static bool Opaque(Texture2D tex, EliteDef d, int cell, ElitePoint p)
    {
        int cp = d.cellPixels;
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                int x = cell * cp + Mathf.RoundToInt(p.x) + dx, y = cp - 1 - (Mathf.RoundToInt(p.y) + dy);
                if (x < 0 || y < 0 || x >= tex.width || y >= tex.height) continue;
                if (tex.GetPixel(x, y).a > .5f) return true;
            }
        return false;
    }

    // ---- life cycle ------------------------------------------------------------

    static LandingSite Site(Vector2 at, int id = 1)
    {
        var anchor = new GameObject("~Pad" + id).transform;
        anchor.position = at;
        return new LandingSite { anchor = anchor, local = Vector3.zero, scale = .3f, order = -420, id = id };
    }

    static void LifeCycle()
    {
        foreach (var def in EliteCatalog.All)
        {
            Fresh();
            pilot.position = new Vector3(0f, -3f, 0f);
            var site = Site(new Vector2(.6f, 3f));
            var e = EliteShip.Create(def, site, 2.5f, new Vector2(-1.5f, 1.5f));
            string k = def.key + ": ";
            Check(k + "starts parked on its site, no collider, untagged, not a target",
                  e.State == EliteState.Parked && !e.Collider.enabled && !e.CompareTag("Enimey") && e.GetComponent<ClearTarget>() == null);
            Check(k + "parked: small, hazy, behind the gameplay layer",
                  e.HullScale < .5f && e.Hull.sortingOrder < 0 && e.Hull.color.b > e.Hull.color.r && e.Hull.color.r < .6f);
            bool lightsEarly = false, lightsLate = false;
            int guard = 0;
            while (e.State == EliteState.Parked && guard++ < 400)
            {
                EliteSystem.Step(Dt);
                if (e.State != EliteState.Parked) break;
                if (e.StateTime < e.ParkSeconds - EliteShip.EngineTellSeconds - .05f) lightsEarly |= e.EngineLightsOn;
                else lightsLate |= e.EngineLightsOn;
            }
            Check(k + "engine lights blink on only in its last parked second (tell)", !lightsEarly && lightsLate);
            Check(k + "lifts off", e.State == EliteState.LiftOff);
            bool colliderOff = true, scaleUp = true;
            float lastScale = e.HullScale;
            int firstOrder = e.Hull.sortingOrder;
            guard = 0;
            while (e.State == EliteState.LiftOff && guard++ < 200)
            {
                colliderOff &= !e.Collider.enabled && !e.CompareTag("Enimey");
                EliteSystem.Step(Dt);
                if (e.State == EliteState.LiftOff) { scaleUp &= e.HullScale >= lastScale - 1e-4f; lastScale = e.HullScale; }
            }
            Check(k + "lift-off: collider stays off until it is fully in the play layer", colliderOff);
            Check(k + "lift-off: grows from background size towards play size", scaleUp);
            Check(k + "joins: collider on, a hazard, sorted in the play layer, full size",
                  e.State == EliteState.Join && e.Collider.enabled && e.CompareTag("Enimey") && e.Hull.sortingOrder == EliteShip.PlayOrder &&
                  firstOrder < e.Hull.sortingOrder && Mathf.Approximately(e.HullScale, 1f) && e.GetComponent<ClearTarget>().enabled);
            Check(k + "joins well away from the pilot (" + Vector2.Distance(e.Position, pilot.position).ToString("0.0") + ")",
                  Vector2.Distance(e.Position, pilot.position) >= 2f);
            Check(k + "an escape window before any attack", e.EscapeLeft > 1.5f);
            guard = 0;
            while (e.State == EliteState.Join && guard++ < 100) EliteSystem.Step(Dt);
            Check(k + "follows", e.State == EliteState.Follow);
            bool told = false, acted = false, resolved = false, glow = false;
            int tellFrame = -1, actFrame = -1, flashes = e.MuzzleFlashesShown;
            for (int i = 0; i < 30 * 25 && !resolved; i++)
            {
                // keep it alive and clear of the pilot for this part
                pilot.position = new Vector3(Mathf.Sin(i * .02f) * .8f, -2.8f, 0f);
                EliteSystem.Step(Dt);
                if (e == null) break;
                if (e.Telling) { told = true; tellFrame = e.CurrentFrame; glow |= e.ChargeGlowOn; }
                if (e.Acting) { acted = true; actFrame = e.CurrentFrame; }
                if (acted && e.State == EliteState.Follow) resolved = true;
            }
            var cm = def.cells;
            bool tellOk = cm.tell >= 0 ? tellFrame == cm.tell || tellFrame == cm.hit : IsFlightCell(def, tellFrame) && glow;
            bool actOk = cm.action >= 0 ? actFrame == cm.action || actFrame == cm.hit : IsFlightCell(def, actFrame) && e != null && e.MuzzleFlashesShown > flashes;
            Check(k + "attacks: the tell drawing, then the action drawing, then back to following (" + tellFrame + "," + actFrame +
                  (cm.tell < 0 ? ", procedural: charge glow " + glow : "") + ")", told && acted && resolved && tellOk && actOk);
        }
    }

    static bool IsFlightCell(EliteDef d, int frame)
    {
        var c = d.cells;
        return System.Array.IndexOf(c.flight, frame) >= 0 || frame == c.bankLeft || frame == c.bankRight || frame == c.damaged;
    }

    // ---- personalities ---------------------------------------------------------

    static void Interceptor()
    {
        Fresh(.05f);
        pilot.position = new Vector3(0f, -1f, 0f);
        var e = InPlay("interceptor", new Vector2(.5f, -4f));
        Step(2f);
        Check("interceptor stalks from behind (below the pilot: " + e.Position.y.ToString("0.0") + ")", e.Position.y < pilot.position.y - .8f);
        e.ForceAttack();
        float tellSpeed = 0f;
        while (e.Telling) { EliteSystem.Step(Dt); if (e.Telling) tellSpeed = Mathf.Max(tellSpeed, e.Velocity.magnitude); }
        Vector2 dir = e.Attack.Dir;
        Vector2 toPilot = ((Vector2)pilot.position - e.Position).normalized;
        float dash = e.Velocity.magnitude;
        Check("interceptor: winds up nearly still, then dashes at dashSpeed (" + tellSpeed.ToString("0.0") + " -> " + dash.ToString("0.0") + ")",
              tellSpeed < 1.5f && dash > e.Def.dashSpeed * .85f);
        Check("interceptor: the dash goes straight at where the pilot was", Vector2.Dot(e.Velocity.normalized, dir) > .99f && Vector2.Dot(dir, toPilot) > .8f);
    }

    static void Gunship()
    {
        Fresh(.05f);
        pilot.position = new Vector3(-.3f, -2f, 0f);
        var e = InPlay("gunship", new Vector2(1.5f, 0f));
        Step(4f);
        float dx = Mathf.Abs(e.Position.x - pilot.position.x), dy = Mathf.Abs(e.Position.y - pilot.position.y);
        Check("gunship holds a lane beside the pilot (dx " + dx.ToString("0.00") + ", dy " + dy.ToString("0.00") + ")",
              Mathf.Abs(dx - e.Def.laneOffset) < .45f && dy < .5f);
        int before = EliteSystem.Shots.Launched;
        e.ForceAttack();
        float laneY = e.Position.y;
        bool horizontal = true, stays = true;
        int guard = 0;
        while (e.State == EliteState.Attack && guard++ < 200)
        {
            EliteSystem.Step(Dt);
            stays &= Mathf.Abs(e.Position.y - laneY) < .6f;
        }
        int fired = EliteSystem.Shots.Launched - before;
        int left = 0, right = 0;
        foreach (var s in EliteSystem.Shots.All)
        {
            if (s.Velocity == Vector2.zero) continue;   // never launched
            horizontal &= Mathf.Abs(s.Velocity.y) < Mathf.Abs(s.Velocity.x) * .25f;
            if (s.Velocity.x < 0f) left++; else right++;
        }
        Check("gunship strafes: volleys of shots (" + fired + ")", fired >= e.Def.shotCount * 2);
        Check("gunship strafes sideways out of both cannons (" + left + " / " + right + ")", horizontal && left > 0 && right > 0);
        Check("gunship keeps its lane while firing", stays);
    }

    static void Striker()
    {
        Fresh(.05f);
        pilot.position = new Vector3(0f, -1.5f, 0f);
        var e = InPlay("striker", new Vector2(1.8f, -1f));
        float minD = 9f, maxD = 0f;
        Step(4f, () => { float d = Vector2.Distance(e.Position, pilot.position); minD = Mathf.Min(minD, d); maxD = Mathf.Max(maxD, d); });
        var brain = (StrikerBrain)e.Brain;
        Check("striker circles the pilot (lapped " + brain.Lapped.ToString("0.0") + " rad, " + minD.ToString("0.0") + "-" + maxD.ToString("0.0") + " u)",
              brain.Lapped > Mathf.PI && minD > e.Def.circleRadius * .5f && maxD < e.Def.circleRadius * 1.6f);
        Check("striker wants to dive after half a lap", brain.WantsAttack(pilot.position));
        e.ForceAttack();
        Vector2 aim = e.Attack.Aim;
        float closest = 9f;
        int guard = 0;
        while (e != null && e.State == EliteState.Attack && guard++ < 200)
        {
            EliteSystem.Step(Dt);
            if (e != null) closest = Mathf.Min(closest, Vector2.Distance(e.Position, aim));
        }
        Check("striker dives claw-first through the pilot's spot (closest " + closest.ToString("0.00") + ")", closest < .6f);
    }

    static void Hauler()
    {
        Fresh(.05f);
        pilot.position = new Vector3(.8f, -2.5f, 0f);
        var e = InPlay("hauler", new Vector2(-1.5f, 1f));
        float top = 0f;
        Step(7f, () => top = Mathf.Max(top, e.Velocity.magnitude));
        Check("hauler is slow (top " + top.ToString("0.0") + " u/s, interceptor cruises at " + Def("interceptor").speed + ")",
              top <= e.Def.speed * 1.6f && e.Def.speed < Def("interceptor").speed * .7f);
        Check("hauler drifts into the pilot's lane, ahead of it (blocks it)",
              Mathf.Abs(e.Position.x - pilot.position.x) < .5f && e.Position.y > pilot.position.y + 1.5f);
        Check("hauler is armoured", e.Def.armored);
        var rock = Rock(e.Position + Vector2.up * .2f);
        EliteSystem.Step(Dt);
        Check("hauler smashes a rock without losing a heart", rock == null && e.Hearts == 2);
        e.ForceAttack();
        int before = EliteSystem.Shots.Launched;
        Step(2f);
        bool slag = false;
        foreach (var s in EliteSystem.Shots.All) slag |= s.Active && s.Kind == EliteShots.Kind.Slag && s.Velocity.y < 0f;
        Check("hauler dumps slag that sinks down its lane", EliteSystem.Shots.Launched - before >= e.Def.shotCount && slag);
    }

    static void Skirmisher()
    {
        Fresh(.05f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var e = InPlay("skirmisher", new Vector2(0f, -1.6f));
        Step(4f);
        float d = Vector2.Distance(e.Position, pilot.position);
        Check("skirmisher keeps its distance (" + d.ToString("0.0") + " vs " + e.Def.keepDistance + ")", Mathf.Abs(d - e.Def.keepDistance) < .8f);
        // something about to hit it: it blinks out
        int blinks = e.Blinks;
        moveBackGround.speed = .3f;
        var rock = Rock(e.Position + Vector2.up * 1.2f);
        Step(.5f, () => Fall(rock, Dt));
        Check("skirmisher blinks out of an incoming rock", e.Blinks > blinks && e.Hearts == 2);
        if (rock != null) Object.DestroyImmediate(rock);
        moveBackGround.speed = .05f;
        Step(1.2f);
        int b2 = e.Blinks;
        int before = EliteSystem.Shots.Launched;
        e.ForceAttack();
        Step(1.2f);
        Check("skirmisher's attack blinks, then fans shards", e.Blinks > b2 && EliteSystem.Shots.Launched - before >= e.Def.shotCount);
    }

    static void Siege()
    {
        Fresh(.05f);
        pilot.position = new Vector3(.5f, -2.5f, 0f);
        var e = InPlay("siege", new Vector2(-1f, 0f));
        Step(6f);
        // (near the top, no higher than the player's reach: HostileReach, EliteShip.ReachY)
        float want = ((SiegeBrain)e.Brain).Height;
        Check("siege holds high, as high as the player's reach lets it (" + e.Position.y.ToString("0.0") + " vs " + want.ToString("0.0") + ", top of view less margin " +
              (EliteSystem.ViewTop - e.Def.topMargin).ToString("0.0") + ")",
              Mathf.Abs(e.Position.y - want) < .45f && want <= EliteSystem.ViewTop - e.Def.topMargin + 1e-3f && e.Position.y > pilot.position.y + 1.5f);
        Check("siege tracks the pilot's lane", Mathf.Abs(e.Position.x - pilot.position.x) < .6f);
        Check("siege faces down", Mathf.Abs(Mathf.DeltaAngle(e.Facing, -90f)) < 10f);
        e.ForceAttack();
        bool sight = false;
        while (e.Telling) { EliteSystem.Step(Dt); sight |= e.transform.Find("Sight").GetComponent<SpriteRenderer>().enabled; }
        int before = EliteSystem.Shots.Launched;
        Step(.4f);
        EliteShot shell = null;
        foreach (var s in EliteSystem.Shots.All) if (s.Active && s.Kind == EliteShots.Kind.Shell) shell = s;
        Check("siege: a sight line down its lane through the charge", sight);
        Check("siege fires a charged shell straight down the lane",
              EliteSystem.Shots.Launched > before && shell != null && shell.Velocity.y < -5f && Mathf.Abs(shell.Velocity.x) < .05f);
    }

    // ---- cell maps ---------------------------------------------------------------

    static void CellMaps()
    {
        // the Ember six keep the default layout, cell for cell
        foreach (var d in EliteCatalog.All)
        {
            if (d.world != "ember") continue;
            var c = d.cells;
            Check(d.key + ": the default (Ember) cell layout: idle 0-3, tell 4, action 5, hit 6, no parked / lift-off / bank / damaged cells",
                  c.flight.Length == 4 && c.flight[0] == 0 && c.flight[3] == 3 && c.tell == EliteArt.Tell && c.action == EliteArt.Action &&
                  c.hit == EliteArt.Hit && c.parked < 0 && c.parkedIdle < 0 && c.liftoff < 0 && !c.Banks && c.damaged < 0 && c.Parked == 0);
        }
        Fresh(.05f);
        var sun = EliteShip.CreateInPlay(EliteCatalog.Find("ember_elite_sunstoke"), new Vector2(0f, 1f));
        sun.AttackCooldown = 99f;
        var seen = new HashSet<int>();
        Step(1.5f, () => seen.Add(sun.CurrentFrame));
        Check("Ember: still loops idle 0-3 (" + string.Join(",", seen) + ")", seen.SetEquals(new[] { 0, 1, 2, 3 }));
        sun.TakeHit(EliteDamage.PlayerWeapon, sun.transform.position + Vector3.left);
        EliteSystem.Step(Dt);
        Check("Ember: a hit shows the hit cell, hull not tinted", sun.CurrentFrame == EliteArt.Hit && sun.Hull.color == Color.white);
        Step(.5f);
        Check("Ember: back on its idle loop after the hit (no damaged cell)", sun.CurrentFrame < 4 && !sun.Damaged);

        foreach (string key in new[] { "frost_elite_rimebreaker", "verdant_elite_resin_warden" })
        {
            var def = EliteCatalog.Find(key);
            if (def == null) { Check(key + " is defined", false); continue; }
            string k = def.displayName + ": ";
            var c = def.cells;
            Check(k + "its flight cell map (parked 0, engines 1, lift-off 2, flight 3, banks 4 / 5, damaged 6; no tell / action / hit cells)",
                  c.parked == 0 && c.parkedIdle == 1 && c.liftoff == 2 && c.flight.Length == 1 && c.flight[0] == 3 && c.bankLeft == 4 &&
                  c.bankRight == 5 && c.damaged == 6 && c.tell < 0 && c.action < 0 && c.hit < 0);

            // parked -> engines blink -> lift-off -> flight
            Fresh(.05f);
            pilot.position = new Vector3(0f, -3f, 0f);
            var e = EliteShip.Create(def, Site(new Vector2(.6f, 3f)), 2.5f, new Vector2(-1.5f, 1.5f));
            var parked = new HashSet<int>();
            bool blinkSync = true;
            int guard = 0;
            while (e.State == EliteState.Parked && guard++ < 400)
            {
                EliteSystem.Step(Dt);
                if (e.State != EliteState.Parked) break;
                parked.Add(e.CurrentFrame);
                blinkSync &= (e.CurrentFrame == c.parkedIdle) == e.EngineLightsOn;
            }
            Check(k + "parked on its landed cell, the grounded-idle cell blinking with the engine lights (" + string.Join(",", parked) + ")",
                  parked.SetEquals(new[] { 0, 1 }) && blinkSync);
            var lift = new List<int>();
            guard = 0;
            while (e.State == EliteState.LiftOff && guard++ < 200)
            {
                EliteSystem.Step(Dt);
                if (e.State == EliteState.LiftOff && (lift.Count == 0 || lift[lift.Count - 1] != e.CurrentFrame)) lift.Add(e.CurrentFrame);
            }
            Check(k + "lift-off: the ignition cell, then flight (" + string.Join(",", lift) + ")",
                  lift.Count >= 2 && lift[0] == c.liftoff && IsFlightCell(def, lift[lift.Count - 1]) && lift[lift.Count - 1] != c.liftoff);
            Object.DestroyImmediate(e.gameObject);

            // steering frames: banks by sideways speed
            Fresh(.05f);
            e = EliteShip.CreateInPlay(def, new Vector2(0f, 1f));
            e.AttackCooldown = 99f;
            Step(.3f, () => { e.SetSeen(e.Position + Vector2.down * 2f); e.Velocity = Vector2.zero; });
            int straight = e.CurrentFrame;
            Step(.3f, () => e.Velocity = new Vector2(-def.speed, 0f));
            int left = e.CurrentFrame;
            Step(.3f, () => e.Velocity = new Vector2(def.speed, 0f));
            int right = e.CurrentFrame;
            Step(.3f, () => e.Velocity = new Vector2(def.speed * .1f, def.speed));
            int ahead = e.CurrentFrame;
            Check(k + "flies on its hover cell, banks left / right when sliding sideways (" + straight + "," + left + "," + right + "," + ahead + ")",
                  straight == c.Flight0 && left == c.bankLeft && right == c.bankRight && ahead == c.Flight0);

            // a hit: the current frame flashes; the damaged cell for good
            e.Velocity = Vector2.zero;
            e.TakeHit(EliteDamage.PlayerWeapon, e.transform.position + Vector3.left);
            bool tinted = false;
            int flashFrame = -1;
            Step(EliteShip.HitFlashSeconds, () => { tinted |= e.Hull.color != Color.white; flashFrame = e.CurrentFrame; });
            Check(k + "a hit flashes the frame it is on (no hit cell; the damaged cell from that hit on)", tinted && flashFrame == c.damaged);
            var after = new HashSet<int>();
            Step(.4f, () => e.Velocity = new Vector2(-def.speed, 0f));
            after.Add(e.CurrentFrame);
            Step(1.5f, () => after.Add(e.CurrentFrame));
            Check(k + "after its first lost heart: the damaged cell, steady (" + string.Join(",", after) + ")",
                  e.Hearts == 1 && e.Damaged && after.Count == 1 && after.Contains(c.damaged) && e.Hull.color == Color.white);
            Check(k + "the codex weaves through flight and banks; its locked card uses the flight cell",
                  c.CodexLoop.Length == 4 && c.CodexLoop[1] == c.bankLeft && c.CodexLoop[3] == c.bankRight &&
                  global::Codex.Find(def.codexId) != null && global::Codex.Find(def.codexId).Sprite == EliteArt.Frame(def, c.Flight0));
        }
    }

    // ---- Rimebreaker: breaker + ice_ram ---------------------------------------------

    static void Breaker()
    {
        Fresh(.05f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var e = InPlay("breaker", new Vector2(1.5f, 1f));
        if (e == null || e.Def == null) { Check("breaker elite exists", false); return; }
        var def = e.Def;
        float minDx = 9f, minY = 9f;
        Step(5f, () => { minDx = Mathf.Min(minDx, Mathf.Abs(e.Position.x - pilot.position.x)); minY = Mathf.Min(minY, e.Position.y - pilot.position.y); });
        Check("breaker prowls ahead of the pilot (at least " + minY.ToString("0.0") + " u above)", minY > def.followDistance * .5f);
        Check("breaker sweeps across the pilot's lane (closest " + minDx.ToString("0.00") + " u)", minDx < .4f);

        // line it up and force the ram
        e.transform.position = new Vector3(.2f, pilot.position.y + def.followDistance, 0f);
        e.Velocity = Vector2.zero;
        Check("breaker wants to ram when over the pilot's lane", e.Brain.WantsAttack(pilot.position));
        e.ForceAttack();
        var ram = (IceRamAttack)e.Attack;
        int launched = EliteSystem.Shots.Launched;
        float tellSpeed = 0f, squash = 0f;
        bool glow = false;
        while (e.Telling)
        {
            EliteSystem.Step(Dt);
            if (!e.Telling) break;
            tellSpeed = Mathf.Max(tellSpeed, e.Velocity.magnitude);
            glow |= e.ChargeGlowOn;
            squash = Mathf.Max(squash, e.Squash);
        }
        Check("ice_ram tell: holds still, the prow charges (glow) and the hull squats (" + tellSpeed.ToString("0.0") + ", squash " + squash.ToString("0.00") + ")",
              tellSpeed < 1.2f && glow && squash > .03f);
        EliteSystem.Step(Dt);
        Check("ice_ram: ploughs straight down its locked lane at dashSpeed (" + e.Velocity + ")",
              e.Velocity.y < -def.dashSpeed * .85f && Mathf.Abs(e.Velocity.x) < .05f && Mathf.Abs(ram.LaneX - .2f) < .3f);
        int left = 0, right = 0;
        foreach (var s in EliteSystem.Shots.All)
            if (s.Active && s.Kind == EliteShots.Kind.Shard) { if (s.Velocity.x < 0f) left++; else right++; }
        Check("ice_ram: two frost shards off the prow, down-left and down-right (" + (EliteSystem.Shots.Launched - launched) + ": " + left + "/" + right + ")", EliteSystem.Shots.Launched - launched == 2 && left == 1 && right == 1);
        Check("ice_ram: a muzzle flash and a recoil on the hover frame", e.MuzzleFlashesShown > 0 && e.RecoilOffset > .01f && IsFlightCell(def, e.CurrentFrame));

        // a rock in its lane: broken through, no heart lost, more shards
        var rock = Rock(e.Position + Vector2.down * .5f);
        int before = EliteSystem.Shots.Launched;
        Step(.2f);
        Check("ice_ram: breaks through a rock in its lane without losing a heart",
              rock == null && e != null && e.Hearts == 2 && e.Ploughed == 1 && EliteSystem.Shots.Launched - before == 2 && e.Velocity.y < -def.dashSpeed * .3f);
        Step(1f);
        Check("ice_ram: back to prowling after the ram", e.State == EliteState.Follow);
        var rock2 = Rock(e.Position);
        EliteSystem.Step(Dt);
        Check("outside the ram a rock still costs it a heart (not armoured)", rock2 == null && e.Hearts == 1 && !def.armored);

        // frost shards glance off the rails once, then break on them
        Fresh(.05f);
        e = InPlay("breaker", new Vector2(0f, 3f));
        var shard = EliteSystem.Shots.Fire(e, e.Def, EliteShots.Kind.Shard, new Vector2(EliteSystem.RailEdge - .5f, 0f), new Vector2(5f, -.5f));
        bool back = false;
        Step(.5f, () => back |= shard.Active && shard.Velocity.x < 0f);
        Check("frost shards glance off a rail and come back across the board", back && shard.Bounced == 1);
        Step(2.5f);
        Check("... and break on the second rail they meet", !shard.Active && shard.EndReason == 2);
    }

    // ---- Resin Warden: warden + resin_mortar -------------------------------------------

    static void Warden()
    {
        Fresh(.02f);
        pilot.position = new Vector3(1f, -2.5f, 0f);
        var e = InPlay("warden", new Vector2(0f, 1f));
        if (e == null || e.Def == null) { Check("warden elite exists", false); return; }
        var def = e.Def;
        var brain = (WardenBrain)e.Brain;
        Step(4f);
        Check("warden takes a station high on the side away from the pilot (" + e.Position + ")",
              brain.Side < 0f && e.Position.x < -.5f && Mathf.Abs(e.Position.y - brain.Station.y) < .4f &&
              brain.Station.y <= EliteSystem.ViewTop - def.topMargin + 1e-3f && brain.Settled);
        Check("warden wants to attack once planted", brain.WantsAttack(pilot.position));
        e.ForceAttack();
        var mortar = (ResinMortarAttack)e.Attack;
        bool glow = false;
        float tellSpeed = 0f;
        while (e.Telling)
        {
            EliteSystem.Step(Dt);
            if (!e.Telling) break;
            glow |= e.ChargeGlowOn;
            tellSpeed = Mathf.Max(tellSpeed, e.Velocity.magnitude);
        }
        Check("resin_mortar tell: planted, the pods charge (" + tellSpeed.ToString("0.0") + ")", glow && tellSpeed < 1.2f);
        Step(def.shotInterval * (def.shotCount - 1) + Dt * 2f);
        var globs = new List<EliteShot>();
        foreach (var s in EliteSystem.Shots.All) if (s.Active && s.Kind == EliteShots.Kind.Glob) globs.Add(s);
        bool air = globs.Count == def.shotCount;
        foreach (var g in globs) air &= g.Airborne && g.MarkShown && !g.Hitbox.GetComponent<Collider2D>().enabled;
        Check("resin_mortar lobs " + def.shotCount + " globs: in the air, harmless, each landing spot ringed (" + globs.Count + ")", air);
        globs.Sort((a, b) => a.LobTarget.x.CompareTo(b.LobTarget.x));
        bool row = globs.Count == def.shotCount;
        for (int i = 1; row && i < globs.Count; i++)
            row &= Mathf.Abs(globs[i].LobTarget.x - globs[i - 1].LobTarget.x - def.lobSpacing) < .05f &&
                   Mathf.Abs(globs[i].LobTarget.y - globs[0].LobTarget.y) < .1f;
        string rowInfo = "";
        foreach (var g in globs) rowInfo += g.LobTarget.ToString("0.00") + " ";
        Check("... onto a row across the lane, lobSpacing apart, ahead of the pilot (" + rowInfo + "centre " + mortar.RowCentre + ")",
              row && globs[0].LobTarget.y > pilot.position.y + def.lobAhead * .6f &&
              Mathf.Abs(mortar.RowCentre.x - pilot.position.x) < .6f);
        Vector2 spot = globs.Count > 0 ? globs[0].LobTarget : Vector2.zero;
        Step(def.lobSeconds + .1f);
        bool pooled = globs.Count > 0;
        foreach (var g in globs) pooled &= g.Pooled && !g.MarkShown && g.Hitbox.GetComponent<Collider2D>().enabled &&
                                           Mathf.Abs(g.Velocity.y + EliteSystem.Scroll) < 1e-3f;
        Check("globs land as sticky pools that ride the board, now hazardous", pooled && Mathf.Abs(globs[0].transform.position.x - spot.x) < .05f);
        Step(.5f);
        Check("after the barrage the warden crosses to its other station", brain.Side > 0f && e.State == EliteState.Follow);
        Step(def.poolSeconds - 1.5f);
        int live = 0;
        foreach (var g in globs) if (g.Pooled) live++;
        Check("pools linger for poolSeconds (" + live + " still there)", live == globs.Count);
        // shooting one out of the way
        if (globs.Count > 0) globs[0].Hitbox.GetComponent<EliteShotHitbox>().TakeShipAttack(3, 1f, globs[0].transform.position);
        Check("a player weapon clears a pool", globs.Count > 0 && !globs[0].Active);
        Step(2f);
        live = 0;
        foreach (var g in globs) if (g.Active) live++;
        Check("... and the rest dry up", live == 0);

        // a pool on the pilot's path costs a heart like any enemy shot
        Fresh(.05f);
        e = InPlay("warden", new Vector2(0f, 3f));
        var glob = EliteSystem.Shots.Fire(e, e.Def, EliteShots.Kind.Glob, e.MuzzleWorld(0), Vector2.zero);
        glob.Lob(new Vector2(0f, .6f), .2f);
        Step(.3f);
        var rig = PlayerRig(glob.transform.position);
        rig.Touch(glob.Hitbox);
        Check("a resin pool costs the pilot a heart", glob != null && collisionDetection.lifeCounter == 1);
        rig.Dispose();
    }

    // ---- shots ---------------------------------------------------------------

    static void ShotsLeaveMuzzles()
    {
        foreach (var def in EliteCatalog.All)
        {
            Fresh(.05f);
            pilot.position = new Vector3(0f, -2.5f, 0f);
            var e = EliteShip.CreateInPlay(def, new Vector2(.4f, .5f));
            e.AttackCooldown = 99f;
            Step(.3f);
            e.ForceAttack();
            int before = EliteSystem.Shots.Launched;
            float best = 9f, fromCentre = 0f;
            int guard = 0;
            while (e != null && e.State == EliteState.Attack && guard++ < 200)
            {
                EliteSystem.Step(Dt);
                if (e == null) break;
                if (EliteSystem.Shots.Launched > before && best > 8f)
                {
                    // compare with the muzzles where the ship was when it fired
                    foreach (var s in EliteSystem.Shots.All)
                    {
                        if (!s.Active) continue;
                        for (int m = 0; m < def.muzzles.Length; m++)
                        {
                            Vector2 mz = e.MuzzleWorld(m) - e.Velocity * Dt;
                            best = Mathf.Min(best, Vector2.Distance(s.LaunchedAt, mz));
                        }
                        fromCentre = Mathf.Max(fromCentre, Vector2.Distance(s.LaunchedAt, e.Position - e.Velocity * Dt));
                    }
                }
            }
            Check(def.key + ": its shots leave a measured muzzle (" + best.ToString("0.00") + " u off, " + fromCentre.ToString("0.00") + " u from the centre)",
                  best < .2f && fromCentre > .15f);
        }
    }

    static void FriendlyFire()
    {
        Fresh(.05f);
        var e = InPlay("siege", new Vector2(0f, 2f));
        var shots = EliteSystem.Shots;
        var def = e.Def;
        float total = RunScore.Total;
        int ff = EliteShip.FriendlyKills;
        var rock = Rock(new Vector2(-1.5f, 0f));
        var fighter = Enemy(EnemyRole.Fighter, new Vector2(0f, 0f));
        var mine = Enemy(EnemyRole.Mine, new Vector2(1.5f, 0f));
        // (past their spawn-in protection: hostile fire spares a target for its first second on the board)
        global::FriendlyFire.Settle(rock); global::FriendlyFire.Settle(fighter); global::FriendlyFire.Settle(mine);
        shots.Fire(e, def, EliteShots.Kind.Bolt, new Vector2(-1.5f, 1f), Vector2.down * 6f);
        shots.Fire(e, def, EliteShots.Kind.Bolt, new Vector2(0f, 1f), Vector2.down * 6f);
        shots.Fire(e, def, EliteShots.Kind.Bolt, new Vector2(1.5f, 1f), Vector2.down * 6f);
        Step(.5f);
        Check("friendly fire: an elite's shots destroy a rock, an enemy and a mine", rock == null && fighter == null && mine == null);
        Check("... counted as friendly kills (" + (EliteShip.FriendlyKills - ff) + ")", EliteShip.FriendlyKills - ff == 3);
        Check("... and pay the pilot nothing", Mathf.Approximately(RunScore.Total, total));
        var other = InPlay("gunship", new Vector2(-1f, -1.5f));
        shots.Fire(e, def, EliteShots.Kind.Bolt, new Vector2(-1f, -.4f), Vector2.down * 6f);
        Step(.4f);
        Check("friendly fire: another elite loses a heart", other != null && other.Hearts == 1 && other.LastHitCause == EliteDamage.FriendlyFire);
    }

    // ---- dodging and baited crashes ---------------------------------------------

    // One rock falling at an interceptor stalking a still pilot.
    // (`ahead` > 0: the rock appears that far above it instead of at the top of the view.)
    static int RockRun(float speed, out bool dodged, float ahead = -1f)
    {
        Fresh(speed);
        Random.InitState(77);
        pilot.position = new Vector3(0f, -1f, 0f);
        var e = InPlay("interceptor", new Vector2(0f, -3f));
        Step(1.5f);
        int crashes = EliteShip.Crashes;
        var rock = Rock(new Vector2(e.Position.x, ahead > 0f ? e.Position.y + ahead : EliteSystem.ViewTop + .5f));
        Step(2.5f, () => Fall(rock, Dt));
        dodged = rock != null;
        int c = EliteShip.Crashes - crashes;
        if (rock != null) Object.DestroyImmediate(rock);
        return c;
    }

    static void Dodging()
    {
        bool dodged;
        int slow = RockRun(.15f, out dodged);
        Check("dodging: a rock coming down at it is usually dodged (slow board: " + slow + " crashes)", slow == 0 && dodged);
        // (it reads the board ahead now -- EliteEvasion -- so a rock from the top of the view no
        // longer catches it; one that arrives inside its reaction time still does)
        int fast = RockRun(.6f, out dodged, 2f);
        Check("dodging: what arrives inside its reaction time still catches it (" + fast + " crash)", fast >= 1);

        // baited: an interceptor locks its dash on the pilot, the pilot blinks
        // away, and the dash runs into a rock the pilot was hiding behind
        Fresh(.02f);
        Random.InitState(5);
        pilot.position = new Vector3(0f, 0f, 0f);
        var e = InPlay("interceptor", new Vector2(0f, -3.2f));
        Step(.2f);
        e.ForceAttack();
        while (e.Telling) EliteSystem.Step(Dt);
        var rock = Rock(new Vector2(e.Position.x + e.Attack.Dir.x * 1.6f, e.Position.y + e.Attack.Dir.y * 1.6f));
        pilot.position = new Vector3(2f, 3f, 0f);   // the pause-teleport
        TeleportFx.Strike(pilot.position);
        int crashes = EliteShip.Crashes;
        Step(.5f);
        Check("baited: a committed dash after a teleport crashes into what was behind the pilot",
              EliteShip.Crashes > crashes && (e == null || e.Hearts < 2) && rock == null);
    }

    static void NoTeleportWithPilot()
    {
        Fresh(.05f);
        pilot.position = new Vector3(-1f, -2f, 0f);
        var e = InPlay("interceptor", new Vector2(-1f, -4f));
        Step(1f);
        Vector2 before = e.Position;
        pilot.position = new Vector3(1.8f, 2.5f, 0f);
        EliteSystem.Step(Dt);
        Check("elites don't teleport with the pilot: it loses the ship", e.PlayerLost);
        Check("... its idea of the pilot stays near the old spot", Vector2.Distance(e.Seen, new Vector2(-1f, -2f)) < .2f);
        Check("... and it keeps flying its own path (moved " + Vector2.Distance(e.Position, before).ToString("0.00") + ")",
              Vector2.Distance(e.Position, before) < e.Def.speed * Dt * 3f + .2f);
        Step(1f);
        Check("... finding the ship again only at its perception speed",
              Vector2.Distance(e.Seen, pilot.position) > 1f && e.PlayerLost);
    }

    // ---- hearts ---------------------------------------------------------------

    static void Hearts()
    {
        Fresh(.05f);
        var e = InPlay("gunship", new Vector2(0f, 1f));
        var h = e.GetComponent<EliteHearts>();
        Check("an elite wears its hearts (EliteHearts, the shared HeartOrbit)", h != null && h is HeartOrbit);
        Check("two hearts", h != null && h.Hearts.Length == 2 && h.ShownCount == 2);
        var r = h.Hearts[0].GetComponent<SpriteRenderer>();
        Check("enemy-coloured hearts, never the player's red", HueGap(r.color, PlayerRed) > 40f && r.sprite.name.Contains("eliteHeart"));
        Vector3 p0 = h.Hearts[0].position;
        for (int i = 0; i < 20; i++) h.Place(Dt, Dt);
        Check("the hearts orbit", Vector3.Distance(p0, h.Hearts[0].position) > .01f);
        var player = HeartsPlacementTest.Build(ShipId.Starter, new Vector3(0f, -3f, 0f), false, 3);
        player.hearts.Place(0f, 0f);
        float playerOrbit = player.hearts.OrbitRadius;
        Check("a smaller orbit than the player ship's (" + h.OrbitRadius.ToString("0.00") + " vs " + playerOrbit.ToString("0.00") + ")",
              h.OrbitRadius < playerOrbit && h.OrbitRadius > e.Def.hullRadius);
        Check("smaller hearts than the player's", h.heartSize < player.hearts.heartSize);
        foreach (var d in EliteCatalog.All)
        {
            float orbitR = d.hullRadius * d.heartOrbit + d.heartSize * HeartOrbit.OrbitReach;
            Check(d.key + ": its heart orbit (" + orbitR.ToString("0.00") + ") is tighter than the player's", orbitR < playerOrbit && d.heartSize < player.hearts.heartSize);
        }
        e.TakeHit(EliteDamage.PlayerWeapon, e.transform.position + Vector3.left);
        Check("a hit: one heart darts out and crumbles (shield)", h.ShownCount == 1 && h.ActiveBreaks == 1);
    }

    // ---- damage ---------------------------------------------------------------

    sealed class Rig
    {
        public GameObject ship;
        public collisionDetection cd;
        // each touch is a fresh hit: the 2 s post-hit window is cleared first
        public void Touch(GameObject other) { PlayerInvuln.Reset(); TouchRaw(other); }
        public void TouchRaw(GameObject other) { Trigger.Invoke(cd, new object[] { other.GetComponent<Collider2D>() }); }
        public void Dispose()
        {
            foreach (var go in new[] { cd.explosionAnimation, cd.boost, cd.boostText.gameObject, cd.hypeText.gameObject })
                if (go != null) Object.DestroyImmediate(go);
            Object.DestroyImmediate(ship);
            collisionDetection.atomCheck = false;
            collisionDetection.lifeCounter = 0;
        }
    }

    static Rig PlayerRig(Vector2 at)
    {
        var r = new Rig();
        r.ship = new GameObject("ship3(Clone)", typeof(SpriteRenderer));
        r.ship.transform.position = at;
        r.ship.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        r.ship.AddComponent<BoxCollider2D>().isTrigger = true;
        r.cd = r.ship.AddComponent<collisionDetection>();
        r.cd.explosionAnimation = new GameObject("~TestExplosion");
        r.cd.boostSound = r.ship.AddComponent<AudioSource>();
        r.cd.boostText = new GameObject("~boostText", typeof(RectTransform)).AddComponent<Text>();
        r.cd.hypeText = new GameObject("~hypeText", typeof(RectTransform)).AddComponent<Text>();
        r.cd.boost = new GameObject("~boost");
        r.cd.boost.SetActive(false);
        collisionDetection.MAXLIFE = 3;
        collisionDetection.lifeCounter = 0;
        return r;
    }

    static void DamageSources()
    {
        Fresh(.05f);
        var e = InPlay("gunship", new Vector2(0f, 1f));
        ShipAttackHits.Hit(e.gameObject, 3);
        Check("a player weapon / the ultimate / the red atom's shot (ShipAttackHits) takes one heart, not the ship",
              e != null && e.Hearts == 1 && e.LastHitCause == EliteDamage.PlayerWeapon);
        ShipAttackHits.Hit(e.gameObject, 3);
        Check("... and none during its grace", e != null && e.Hearts == 1);

        Fresh(.05f);
        e = InPlay("gunship", new Vector2(0f, 1f));
        int killsBefore = EliteShip.Kills;
        TeleportFx.Strike(new Vector3(.3f, 1f, 0f));
        // (was one heart; the user: "when pause teleport on them it does 2 heart damage" -- HostileReachTest has every elite)
        Check("a pause jump landing on it (TeleportFx.Strike) takes both hearts: a kill",
              (e == null || (e.Hearts == 0 && e.State == EliteState.Dead)) && EliteShip.Kills == killsBefore + 1 && EliteShip.LastKillCause == EliteDamage.Teleport);

        Fresh(.05f);
        e = InPlay("gunship", new Vector2(0f, 1f));
        var rig = PlayerRig(new Vector2(0f, .6f));
        rig.Touch(e.gameObject);
        Check("contact costs the pilot a heart like any enemy", collisionDetection.lifeCounter == 1);
        Check("... and the elite one heart, without destroying it", e != null && e.Hearts == 1 && e.LastHitCause == EliteDamage.PlayerContact);
        // inside the pilot's 2 s post-hit window: no heart either way
        Step(EliteShip.GraceSeconds + .1f);
        Check("the post-hit window is running", PlayerInvuln.Active);
        rig.TouchRaw(e.gameObject);
        Check("during the pilot's post-hit invulnerability an elite neither hurts nor is rammed",
              collisionDetection.lifeCounter == 1 && e != null && e.Hearts == 1);
        var shot0 = EliteSystem.Shots.Fire(e, e.Def, EliteShots.Kind.Bolt, new Vector2(0f, .6f), Vector2.down);
        rig.TouchRaw(shot0.Hitbox);
        Check("... nor does its shot", collisionDetection.lifeCounter == 1);
        rig.Dispose();
        PlayerInvuln.Reset();

        // the fatal contact: the elite is the killer DeathCrash tumbles into a rail
        Fresh(.05f);
        e = InPlay("gunship", new Vector2(0f, 1f));
        rig = PlayerRig(new Vector2(0f, .6f));
        collisionDetection.lifeCounter = collisionDetection.MAXLIFE - 1;
        int paid = EliteRewards.Paid;
        rig.Touch(e.gameObject);
        var crash = DeathCrash.Instance;
        Check("a fatal elite contact starts the death crash with the elite as its killer",
              crash != null && DeathCrash.Running && crash.Killer == DeathCrash.KillerKind.Physical);
        Check("... the killer elite pays nothing (the run is over)", EliteRewards.Paid == paid);
        if (crash != null) Object.DestroyImmediate(crash.gameObject);
        if (e != null) Object.DestroyImmediate(e.gameObject);
        buttonClicks.playerDied = false;
        rig.Dispose();

        Fresh(.05f);
        e = InPlay("gunship", new Vector2(0f, 1f));
        rig = PlayerRig(new Vector2(0f, .6f));
        collisionDetection.atomCheck = true;
        int kills = EliteShip.Kills;
        rig.Touch(e.gameObject);
        Check("a shielded ram takes both hearts (a kill) and costs the pilot nothing",
              e == null && EliteShip.Kills == kills + 1 && EliteShip.LastKillCause == EliteDamage.ShieldRam && collisionDetection.lifeCounter == 0);
        rig.Dispose();

        Fresh(.05f);
        e = InPlay("gunship", new Vector2(0f, 1f));
        var rock = Rock(e.Position);
        EliteSystem.Step(Dt);
        Check("a crash into a rock takes one heart (and the rock)", e != null && e.Hearts == 1 && e.LastHitCause == EliteDamage.Crash && rock == null);

        Fresh(.05f);
        e = InPlay("gunship", new Vector2(0f, 1f));
        e.transform.position = new Vector3(EliteSystem.RailEdge, 1f, 0f);
        EliteSystem.Step(Dt);
        Check("a rail takes one heart and bounces it back in", e != null && e.Hearts == 1 && e.LastHitCause == EliteDamage.Rail && e.Velocity.x < 0f);

        Fresh(.05f);
        e = InPlay("gunship", new Vector2(-1f, 1f));
        var other = InPlay("hauler", new Vector2(-1f + .3f, 1f));
        EliteSystem.Step(Dt);
        Check("two elites colliding both lose a heart", e != null && other != null && e.Hearts == 1 && other.Hearts == 1);

        Fresh(.05f);
        e = InPlay("gunship", new Vector2(0f, 1f));
        ShipAttackHits.Hit(e.gameObject, 3);
        Step(EliteShip.GraceSeconds + .1f);
        kills = EliteShip.Kills;
        ShipAttackHits.Hit(e.gameObject, 3);
        Check("two hits kill", e == null && EliteShip.Kills == kills + 1);

        // an elite's shot hitting the pilot through the normal enemy path
        Fresh(.05f);
        e = InPlay("siege", new Vector2(0f, 3f));
        var shot = EliteSystem.Shots.Fire(e, e.Def, EliteShots.Kind.Bolt, new Vector2(0f, .6f), Vector2.down);
        rig = PlayerRig(new Vector2(0f, .6f));
        rig.Touch(shot.Hitbox);
        Check("an elite's shot costs the pilot a heart", collisionDetection.lifeCounter == 1);
        rig.Dispose();
    }

    // ---- rewards --------------------------------------------------------------

    static void Rewards()
    {
        Fresh(.05f);
        var e = InPlay("gunship", new Vector2(0f, 1f));
        long total = RunScore.Total;
        float dust = score.totalCurrency + score.tutorialCurrency;
        int popups = 0; RunScore.Source src = RunScore.Source.Distance; int pts = 0;
        System.Action<int, Vector3, RunScore.Source> spy = (p, w, s) => { popups++; src = s; pts = p; };
        RunScore.Scored += spy;
        try
        {
            ShipAttackHits.Hit(e.gameObject, 3);
            Step(EliteShip.GraceSeconds + .1f);
            ShipAttackHits.Hit(e.gameObject, 3);
        }
        finally { RunScore.Scored -= spy; }
        Check("an elite kill pays " + ScoreRules.EliteDown + " points (" + (RunScore.Total - total) + ")", RunScore.Total - total == ScoreRules.EliteDown);
        Check("... and " + ScoreRules.EliteDownDust + " star dust", Mathf.Abs(score.totalCurrency + score.tutorialCurrency - dust - ScoreRules.EliteDownDust) < .01f);
        Check("... with the ELITE DOWN popup", src == RunScore.Source.Elite && pts == ScoreRules.EliteDown &&
              ScoreHud.StyleFor(RunScore.Source.Elite, false).suffix.Contains("ELITE DOWN"));

        // a lure: two crashes, no pilot weapon involved
        Fresh(.05f);
        e = InPlay("gunship", new Vector2(0f, 1f));
        total = RunScore.Total;
        Rock(e.Position);
        EliteSystem.Step(Dt);
        Step(EliteShip.GraceSeconds + .1f);
        if (e != null) Rock(e.Position);
        EliteSystem.Step(Dt);
        Check("a lured crash kill pays the same", e == null && EliteShip.LastKillCause == EliteDamage.Crash && RunScore.Total - total == ScoreRules.EliteDown);

        foreach (string brain in new[] { "breaker", "warden", "bastion", "reaver", "lancer", "tug" })
        {
            Fresh(.05f);
            e = InPlay(brain, new Vector2(0f, 1f));
            total = RunScore.Total;
            dust = score.totalCurrency + score.tutorialCurrency;
            ShipAttackHits.Hit(e.gameObject, 3);
            Step(EliteShip.GraceSeconds + .1f);
            ShipAttackHits.Hit(e.gameObject, 3);
            Check(Def(brain).displayName + " down pays " + ScoreRules.EliteDown + " points and " + ScoreRules.EliteDownDust + " dust",
                  e == null && RunScore.Total - total == 50 && ScoreRules.EliteDown == 50 && ScoreRules.EliteDownDust == 15f &&
                  Mathf.Abs(score.totalCurrency + score.tutorialCurrency - dust - 15f) < .01f);
        }
    }

    // ---- the director -----------------------------------------------------------

    static void Director()
    {
        Fresh(.05f);
        var pads = new List<LandingSite>();
        for (int i = 0; i < 6; i++) pads.Add(Site(new Vector2(-2f + i * .8f, 3f), 10 + i));
        LandingSites.Override = list => list.AddRange(pads);
        var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
        int a = dir.SpawnGroup(3, 3);
        int b = dir.SpawnGroup(3, 3);
        Check("at most three elites alive (" + a + " + " + b + ")", a == 3 && b == 0 && EliteDirector.AliveCount == 3);
        Check("groups park on distinct sites", EliteShip.Live[0].Site.id != EliteShip.Live[1].Site.id && EliteShip.Live[1].Site.id != EliteShip.Live[2].Site.id);
        Check("no sites, no elites", dir.SpawnGroup(3, 1) == 0);
        EliteSystem.Clear();
        LandingSites.Override = list => { };
        Check("a world without landing sites spawns nothing", dir.SpawnGroup(3, 2) == 0);
        LandingSites.Override = list => list.AddRange(pads);

        Check("none in the first 20 s of a world", EliteDirector.Blocked(3, 10f) == "too early");
        Check("allowed after that", EliteDirector.Blocked(3, 25f) == null);
        Check("none in a world without elites", EliteDirector.Blocked(EnemyRoster.WorldKeys.Length, 60f) == "no elites");
        startMenu.youAreInTutorial = true;
        Check("none in the tutorial", EliteDirector.Blocked(3, 60f) == "tutorial");
        startMenu.youAreInTutorial = false;
        bool began = BossEncounter.Begin(3, null);
        Check("none during a boss (" + began + ")", BossEncounter.Running && EliteDirector.Blocked(3, 60f) == "boss");
        dir.SpawnGroup(3, 2);
        dir.Tick(Dt);
        Check("a boss clears the parked ones", EliteDirector.AliveCount == 0);
        if (BossEncounter.Instance != null) Object.DestroyImmediate(BossEncounter.Instance.gameObject);
        Check("join points keep away from the pilot",
              Vector2.Distance(EliteDirector.JoinPoint(Def("interceptor"), Vector2.down, Vector3.zero), pilot.position) >= EliteShip.MinJoinDistance);
        RunLoop.Reset();
        float g0 = 0f;
        Random.InitState(9); for (int i = 0; i < 50; i++) g0 += EliteDirector.NextGap();
        Object.DestroyImmediate(dir.gameObject);
        LandingSites.Override = null;
        Check("gaps between groups are random in " + EliteDirector.GapSeconds, g0 / 50f > EliteDirector.GapSeconds.x && g0 / 50f < EliteDirector.GapSeconds.y);
    }

    // ---- Frost / Verdant landing sites ------------------------------------------------

    static void WorldSites()
    {
        foreach (int w in new[] { 1, 2 })
        {
            Fresh(.3f);
            string world = WorldManager.Worlds[w].displayName;
            var wb = WorldBackdrop.Create(world);
            wb.Show(world, false);
            var sites = new List<LandingSite>();
            for (int i = 0; i < 600 && LandingSites.Collect(sites) == 0; i++) wb.Step(1f / 15f);
            bool background = sites.Count > 0;
            foreach (var st in sites) background &= st.order < 0 && st.scale < .5f && st.Valid && st.Position.y > -CameraFit.ViewTop * .5f;
            Check(world + ": its backdrop reports elite landing sites on its terrain, at background depth (" + sites.Count + ")", background);
            Check(world + ": elites allowed there now (after 20 s)", EliteDirector.Blocked(w, 60f) == null && EliteDirector.Blocked(w, 10f) == "too early");
            var dir = new GameObject("~Dir").AddComponent<EliteDirector>();
            int made = dir.SpawnGroup(w, 3);
            bool own = made > 0;
            foreach (var e in EliteShip.Live) own &= e.Def.WorldIndex == w && e.State == EliteState.Parked;
            Check(world + ": spawns its own elites, parked on those sites (" + made + ")", own && made == Mathf.Min(3, sites.Count));
            var pad = EliteShip.Live.Count > 0 ? EliteShip.Live[0] : null;
            Check(world + ": a parked elite rides its landmark", pad != null && Vector2.Distance(pad.transform.position, pad.Site.Position) < .01f);
            Object.DestroyImmediate(dir.gameObject);
            Object.DestroyImmediate(wb.gameObject);
        }
        Check("Space allows elites too (its sites: SpaceEliteTest)", EliteDirector.Blocked(0, 60f) == null);
    }

    // ---- frozen, allocations ------------------------------------------------------

    static void Frozen()
    {
        Fresh(.05f);
        var e = InPlay("interceptor", new Vector2(0f, -1f));
        Step(.5f);
        Vector2 p = e.Position, v = e.Velocity;
        float t = e.StateTime;
        int frame = e.CurrentFrame;
        var shot = EliteSystem.Shots.Fire(e, e.Def, EliteShots.Kind.Bolt, new Vector2(1f, 0f), Vector2.down * 5f);
        Vector3 sp = shot.transform.position;
        for (int i = 0; i < 30; i++) EliteSystem.Step(0f);   // timeScale 0: dt 0
        Check("frozen at timeScale 0: the elite, its clock, drawing and shots hold still",
              e.Position == p && e.Velocity == v && Mathf.Approximately(e.StateTime, t) && e.CurrentFrame == frame && shot.transform.position == sp);
    }

    static void Allocations()
    {
        Fresh(.1f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var a = InPlay("interceptor", new Vector2(-1.5f, -4f));
        var b = InPlay("gunship", new Vector2(1.5f, -2f));
        var c = InPlay("siege", new Vector2(0f, 3f));
        a.AttackCooldown = b.AttackCooldown = c.AttackCooldown = 0f;
        Step(3f);   // warm up: every attack, shot and puff once
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 90; i++)
        {
            pilot.position = new Vector3(Mathf.Sin(i * .05f), -2.5f, 0f);
            EliteSystem.Step(Dt);
        }
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check("zero per-frame allocations stepping three elites (" + allocated + " bytes over 90 frames)", allocated == 0);

        Fresh(.1f);
        pilot.position = new Vector3(0f, -2.5f, 0f);
        var r = InPlay("breaker", new Vector2(-1f, 1f));
        var w = InPlay("warden", new Vector2(1f, 2f));
        r.AttackCooldown = w.AttackCooldown = 0f;
        Step(4f);   // warm up: rams, shards, lobs, pools
        before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 90; i++)
        {
            pilot.position = new Vector3(Mathf.Sin(i * .05f), -2.5f, 0f);
            EliteSystem.Step(Dt);
        }
        allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check("zero per-frame allocations stepping the Rimebreaker and Resin Warden (" + allocated + " bytes over 90 frames)", allocated == 0);
    }

    // ---- codex -------------------------------------------------------------------

    static void Codex()
    {
        foreach (var d in EliteCatalog.All)
        {
            var entry = global::Codex.Find(d.codexId);
            Check(d.key + ": a codex entry in Enemies", entry != null && entry.category == CodexCategory.Enemies && entry.Sprite != null);
            var anim = CodexAnimations.For(entry);
            Check(d.key + ": the codex animates its idle loop", anim != null && anim.HasArt && anim.idle.Length == 4);
        }
        Check("an elite object resolves to its entry", global::Codex.IdForName("ember_elite_sunstoke") == "elite_ember_sunstoke");

        // the locked cards: readable solid silhouettes (CodexTest's renderer)
        EditorSceneManager.OpenScene("Assets/Scenes/startS4.unity", OpenSceneMode.Single);
        PlayerPrefs.SetString(global::Codex.PrefsKey, "enemy_space_fighter_1");
        global::Codex.Reload();
        var entryButton = Object.FindFirstObjectByType<CodexHomeButton>();
        if (entryButton == null) { Check("codex panel opens", false); return; }
        entryButton.Build();
        entryButton.Button.onClick.Invoke();
        var panel = CodexPanel.Current;
        panel.SkipAnimations();
        panel.ShowCategory(CodexCategory.Enemies);
        panel.SkipAnimations();
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            Check("rendered silhouettes need a graphics device", false);
            return;
        }
        int tested = 0;
        for (int i = 0; i < panel.VisibleCards; i++)
        {
            var e = panel.CardEntry(i);
            if (EliteCatalog.FindByCodexId(e.id) == null) continue;
            tested++;
            float centre = -panel.CardRect(i).anchoredPosition.y;
            panel.SetScrollY(Mathf.Clamp(centre - panel.Viewport.rect.height * .5f, 0f, panel.MaxScroll));
            var a = panel.CardAnimator(i);
            var r = CodexTest.RenderArt(panel, new Graphic[] { a.Image, a.Overlay });
            Color32 ink = (Color)CodexUi.Silhouette;
            bool inkOk = Mathf.Abs(r.first.r - ink.r) <= 1 && Mathf.Abs(r.first.g - ink.g) <= 1 && Mathf.Abs(r.first.b - ink.b) <= 1;
            Check(e.id + ": locked card is a readable flat silhouette (" + r.opaque + "/" + r.covered + " px, " + r.colours + " colour)",
                  panel.CardName(i).text == "???" && r.rendered && r.opaque >= 200 && r.colours == 1 && inkOk);
        }
        Check("rendered every elite's locked card (" + tested + ")", tested == EliteCatalog.All.Length);
    }
}
