using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The elite ships (Scripts/Gameplay/Elites): data, life cycle, the six
// personalities and attacks, shots from muzzles, friendly fire, dodging and
// baited crashes, perception after a teleport, hearts, every damage
// source, rewards, the director's limits, freezing, allocations and the
// codex silhouettes.
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
            ShotsLeaveMuzzles();
            FriendlyFire();
            Dodging();
            NoTeleportWithPilot();
            Hearts();
            DamageSources();
            Rewards();
            Director();
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
        Check("no elites in Space / Frost / Verdant yet", !EliteCatalog.WorldHasElites(0) && !EliteCatalog.WorldHasElites(1) && !EliteCatalog.WorldHasElites(2));
        var brains = new HashSet<string>();
        var attacks = new HashSet<string>();
        foreach (var d in ember)
        {
            brains.Add(d.brain);
            attacks.Add(d.attack);
            var frames = EliteArt.Frames(d);
            Check(d.key + ": strip loads as 7 cells", frames != null && frames.Length == EliteArt.FrameCount && frames[0] != null);
            Check(d.key + ": a known brain and attack", System.Array.IndexOf(EliteBrains.Ids, d.brain) >= 0 && System.Array.IndexOf(EliteAttacks.Ids, d.attack) >= 0);
            Check(d.key + ": two hearts", d.hearts == 2);
            Check(d.key + ": hearts are not the player's red (hue gap " + HueGap(d.HeartColor, PlayerRed).ToString("0") + ")",
                  HueGap(d.HeartColor, PlayerRed) > 40f && HueGap(d.HeartColor, AkiraPalette.Red) > 40f);
            Check(d.key + ": shots are not the player's red", HueGap(d.ShotColor, PlayerRed) > 30f);
            Check(d.key + ": has muzzles and nozzles", d.muzzles.Length > 0 && d.nozzles.Length > 0);
            Check(d.key + ": a codex id", !string.IsNullOrEmpty(d.codexId) && d.codexId.StartsWith("elite_"));

            // every muzzle sits on the drawing (the action frame), every
            // nozzle on idle 0
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes("Assets/Art/Resources/Elites/Ember/" + d.key + ".png"));
            bool onArt = true;
            foreach (var m in d.muzzles) onArt &= Opaque(tex, d, 5, m) || Opaque(tex, d, 4, m);
            foreach (var z in d.nozzles) onArt &= Opaque(tex, d, 0, z);
            Object.DestroyImmediate(tex);
            Check(d.key + ": muzzles and nozzles land on the art", onArt);
            Check(d.key + ": the original art is untouched (Resources copy matches)",
                  File.ReadAllBytes("Assets/Art/Resources/Elites/Ember/" + d.key + ".png").Length ==
                  File.ReadAllBytes("Assets/Art/Enemies/Elite/Ember/" + d.key + ".png").Length);
        }
        Check("six different brains", brains.Count == 6);
        Check("six different attacks", attacks.Count == 6);
        Check("the elite heart is white art (tinted per elite)", Resources.Load<Sprite>(EliteHearts.SpritePath) != null);
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
            bool told = false, acted = false, resolved = false;
            int tellFrame = -1, actFrame = -1;
            for (int i = 0; i < 30 * 25 && !resolved; i++)
            {
                // keep it alive and clear of the pilot for this part
                pilot.position = new Vector3(Mathf.Sin(i * .02f) * .8f, -2.8f, 0f);
                EliteSystem.Step(Dt);
                if (e == null) break;
                if (e.Telling) { told = true; tellFrame = e.CurrentFrame; }
                if (e.Acting) { acted = true; actFrame = e.CurrentFrame; }
                if (acted && e.State == EliteState.Follow) resolved = true;
            }
            Check(k + "attacks: the tell drawing, then the action drawing, then back to following (" + tellFrame + "," + actFrame + ")",
                  told && acted && resolved && (tellFrame == EliteArt.Tell || tellFrame == EliteArt.Hit) && (actFrame == EliteArt.Action || actFrame == EliteArt.Hit));
        }
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
        float want = EliteSystem.ViewTop - e.Def.topMargin;
        Check("siege holds near the top (" + e.Position.y.ToString("0.0") + " vs " + want.ToString("0.0") + ")", Mathf.Abs(e.Position.y - want) < .45f);
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
    static int RockRun(float speed, out bool dodged)
    {
        Fresh(speed);
        Random.InitState(77);
        pilot.position = new Vector3(0f, -1f, 0f);
        var e = InPlay("interceptor", new Vector2(0f, -3f));
        Step(1.5f);
        int crashes = EliteShip.Crashes;
        var rock = Rock(new Vector2(e.Position.x, EliteSystem.ViewTop + .5f));
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
        int fast = RockRun(1.6f, out dodged);
        Check("dodging: a very fast board still catches it (" + fast + " crash)", fast >= 1);

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
        Check("a smaller orbit than a player ship's (" + h.OrbitRadius.ToString("0.00") + ")", h.OrbitRadius < .85f);
        e.TakeHit(EliteDamage.PlayerWeapon, e.transform.position + Vector3.left);
        Check("a hit: one heart darts out and crumbles (shield)", h.ShownCount == 1 && h.ActiveBreaks == 1);
    }

    // ---- damage ---------------------------------------------------------------

    sealed class Rig
    {
        public GameObject ship;
        public collisionDetection cd;
        public void Touch(GameObject other) { Trigger.Invoke(cd, new object[] { other.GetComponent<Collider2D>() }); }
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
        TeleportFx.Strike(new Vector3(.3f, 1f, 0f));
        Check("a blink landing on it (TeleportFx.Strike) takes one heart", e != null && e.Hearts == 1 && e.LastHitCause == EliteDamage.Teleport);
        Check("... and flings it clear of the landing", Vector2.Distance(e.Position, new Vector2(.3f, 1f)) > TeleportFx.BlastRadius);

        Fresh(.05f);
        e = InPlay("gunship", new Vector2(0f, 1f));
        var rig = PlayerRig(new Vector2(0f, .6f));
        rig.Touch(e.gameObject);
        Check("contact costs the pilot a heart like any enemy", collisionDetection.lifeCounter == 1);
        Check("... and the elite one heart, without destroying it", e != null && e.Hearts == 1 && e.LastHitCause == EliteDamage.PlayerContact);
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
        Check("none in a world without elites", EliteDirector.Blocked(0, 60f) == "no elites");
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
