using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// The v3 verbs of the cheaper hulls (ShipAttacksV3.cs): Arcane Eye (Paranoid), Heat Wave (Solar Fang),
// Boomerang Star (Ninja), Proximity Mines (Saboteur), Abduct & Hurl (UFO), Gale Wings (Dove), and the rule
// that no two ships share an attack.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShipAttacksV3CheapTest.Run
public static class ShipAttacksV3CheapTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[V3] PASS  " : "[V3] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        EveryShipHasItsOwnAttack();
        ParanoidSpecialSkinArt();
        ArcaneEyeMarksThenDetonates();
        HeatWaveBurnsOnce();
        BoomerangHitsBothLegs();
        BoomerangRearAndUpgrades();
        MinesWaitAndFizzle();
        AbductLiftsAndHurls();
        GaleShovesAndCollides();
        GaleTurnsShots();
        UpgradesScale();

        AttackPool.StopAll();
        EnemyShove.Clear();
        Debug.Log("[V3] failures: " + fails);
        return fails;
    }

    // ---- fixtures ------------------------------------------------------

    static void FreshScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.aspect = .5625f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        collisionDetection.cloakTimer = 0f;
        collisionDetection.atomCheck = false;
        collisionDetection.lifeCounter = 0;
        moveBackGround.speed = 0f;   // a still world: mines and bodies stay where the test put them
        Time.timeScale = 1f;
        EnemyShove.Clear();
    }

    static GameObject Hazard(float x, float y, string tag = "Enimey")
    {
        var go = new GameObject("hazard");
        go.tag = tag;
        go.transform.position = new Vector3(x, y, 0f);
        ClearTarget.Ensure(go).SetRadius(.3f);
        return go;
    }

    static ShipPowerController Ship(int id)
    {
        PlayerPrefs.SetInt("spawnShip", id);
        var go = new GameObject("ship" + id, typeof(SpriteRenderer));
        go.transform.position = new Vector3(0f, -3f, 0f);
        var c = go.AddComponent<ShipPowerController>();
        c.SendMessage("Awake");
        c.SendMessage("Start");
        var gun = go.GetComponentInChildren<UltimateGun>();
        if (gun != null) gun.SendMessage("Awake");
        return c;
    }

    static void Teardown(ShipPowerController c)
    {
        if (c == null) return;
        c.SendMessage("OnDestroy");
        if (c.Runner != null) c.Runner.SendMessage("OnDestroy");
        if (c.Secret != null) c.Secret.SendMessage("OnDestroy");
        Object.DestroyImmediate(c.gameObject);
        AttackPool.StopAll();
        EnemyShove.Clear();
        collisionDetection.cloakTimer = 0f;
        collisionDetection.lifeCounter = 0;
    }

    static void SetLevel(int id, int level)
    {
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        for (int n = 1; n < ShipSkins.PerShip; n++)
        {
            if (n <= level) PlayerPrefs.SetInt(ShipSkins.OwnedKey(id, n), 1);
            else PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(id, n));
        }
    }

    // Steps the runner and the shoves it feeds (the game drives EnemyShove from ShieldShockwaveFx).
    static void Steps(ShipAttackRunner r, float seconds, float dt = .02f)
    {
        for (float t = 0f; t < seconds; t += dt) { r.Step(dt); EnemyShove.Step(dt); }
    }

    static bool Alive(GameObject go) => go != null;

    class FakeBoss : MonoBehaviour, IShipAttackTargetUncapped
    {
        public float weight;
        public int hits;
        public void TakeShipAttack(int ship, float w, Vector3 at) { weight += w; hits++; }
    }

    static FakeBoss Boss(float x, float y)
    {
        var go = Hazard(x, y);
        go.GetComponent<ClearTarget>().SetRadius(.8f);
        return go.AddComponent<FakeBoss>();
    }

    // ---- cases -----------------------------------------------------------

    static void EveryShipHasItsOwnAttack()
    {
        var seen = new HashSet<ShipAttack>();
        bool distinct = true;
        foreach (int id in ShipId.All)
        {
            var a = ShipLoadoutTable.For(id).attack;
            distinct &= seen.Add(a);
        }
        Check("no two ships share a ShipAttack", distinct);
        Check("the six v3 attacks are the ships' own",
              ShipLoadoutTable.AttackFor(3) == ShipAttack.HeatWave && ShipLoadoutTable.AttackFor(10) == ShipAttack.ArcaneEye &&
              ShipLoadoutTable.AttackFor(11) == ShipAttack.BoomerangStar && ShipLoadoutTable.AttackFor(12) == ShipAttack.ProximityMines &&
              ShipLoadoutTable.AttackFor(13) == ShipAttack.AbductHurl && ShipLoadoutTable.AttackFor(14) == ShipAttack.GaleWings);
        Check("their art strips resolve", ShipFxArt.Strip(10, "attack_mark") != null && ShipFxArt.AttackLoop(3, 0) != null &&
              ShipFxArt.Extra(10, "mark", true, 3) != null);
    }

    static void ParanoidSpecialSkinArt()
    {
        var normal = ShipFxArt.Strip(10, "attack");
        var normalMark = ShipFxArt.Strip(10, "attack_mark");
        bool ok = normal != null && normalMark != null;
        for (int skin = 0; skin < ShipSkins.PerShip; skin++)
        {
            ShipSkins.SetPreview(10, skin);
            var eye = ShipFxArt.Strip(10, "attack");
            var mark = ShipFxArt.Strip(10, "attack_mark");
            bool special = skin == ShipSkins.Special;
            bool shown = ShipSkins.Shown(10) == skin;
            ok &= shown && eye != null && mark != null && ((eye != normal) == special) && ((mark != normalMark) == special);
            if (special) ok &= eye[0].texture.name == "Paranoid_attack_special" && mark[0].texture.name == "Paranoid_attack_mark_special";
        }
        ShipSkins.ClearPreview();
        Check("Paranoid's Special skin uses the _special eye and mark strips, every other skin the normal ones", ok);
        Check("a ship without a _special strip is unchanged by the Special skin",
              ShipFxArt.Strip(12, "attack") != null);
    }

    static void ArcaneEyeMarksThenDetonates()
    {
        FreshScene();
        var c = Ship(10);
        var foes = new List<GameObject>();
        for (int i = 0; i < 6; i++) foes.Add(Hazard(-2.5f + i, 1f + (i % 2) * 1.5f));   // spaced beyond the blast
        int kills = ShipAttackHits.Kills;
        c.Runner.Fire();
        Steps(c.Runner, .7f);
        Check("the marks do not go off at once", ShipAttackHits.Kills == kills && c.Runner.ActiveRuns == 1);
        Steps(c.Runner, 2f);
        Check("four of six are marked and killed (" + (ShipAttackHits.Kills - kills) + ")", ShipAttackHits.Kills - kills == 4);
        Check("and the eye closes", c.Runner.ActiveRuns == 0);
        Teardown(c);
        foreach (var f in foes) if (f != null) Object.DestroyImmediate(f);

        // The blast: neighbours in reach take half weight once; the marked target the full hit; out of reach, nothing.
        FreshScene();
        c = Ship(10);
        // placed from the eye (the cone is 40 degrees to a side, so the mark sits just inside it, 36 degrees)
        Vector3 eye = c.Runner.Nose + Vector3.up * ShipAttackRunner.EyeAhead;
        var marked = Boss(eye.x + 2.9f, eye.y + 4f);
        var near = Boss(eye.x + 3.8f, eye.y + 4f);      // outside the cone (never marked), .9 from the mark
        var far = Boss(eye.x + 4.5f, eye.y + 4f);       // outside the cone, 1.6 from the mark
        foreach (var b in new[] { near, far }) b.GetComponent<ClearTarget>().SetRadius(.3f);
        c.Runner.Fire();
        Steps(c.Runner, 2f);
        Check("the marked target takes the full hit once (weight " + marked.weight.ToString("F3") + ", " + marked.hits + " hits)",
              marked.hits == 1 && Mathf.Abs(marked.weight - .25f) < 1e-3f);
        Check("a neighbour inside the blast takes one half-weight hit (weight " + near.weight.ToString("F3") + ", " + near.hits + " hits)",
              near.hits == 1 && Mathf.Abs(near.weight - .125f) < 1e-3f);
        Check("one outside the blast radius is untouched", far.hits == 0);
        Teardown(c);
        Object.DestroyImmediate(marked.gameObject);
        Object.DestroyImmediate(near.gameObject);
        Object.DestroyImmediate(far.gameObject);

        // Two marks next to each other: each takes its own full hit and no splash on top.
        FreshScene();
        c = Ship(10);
        eye = c.Runner.Nose + Vector3.up * ShipAttackRunner.EyeAhead;
        var m1 = Boss(eye.x + 2.9f, eye.y + 4f);
        var m2 = Boss(eye.x + 2.5f, eye.y + 4.9f);
        foreach (var b in new[] { m1, m2 }) b.GetComponent<ClearTarget>().SetRadius(.3f);
        c.Runner.Fire();
        Steps(c.Runner, 2f);
        Check("two adjacent marks each take exactly one full hit (" + m1.hits + "/" + m1.weight.ToString("F3") + ", " +
              m2.hits + "/" + m2.weight.ToString("F3") + ")",
              m1.hits == 1 && m2.hits == 1 && Mathf.Abs(m1.weight - .25f) < 1e-3f && Mathf.Abs(m2.weight - .25f) < 1e-3f);
        Teardown(c);
        Object.DestroyImmediate(m1.gameObject);
        Object.DestroyImmediate(m2.gameObject);

        FreshScene();
        c = Ship(10);
        var behind = Hazard(0f, -4f);
        var wide = Hazard(4f, -1.5f);
        c.Runner.Fire();
        Steps(c.Runner, 2f);
        Check("nothing behind the ship or outside the cone is marked", Alive(behind) && Alive(wide));
        Teardown(c);
        Object.DestroyImmediate(behind);
        Object.DestroyImmediate(wide);

        FreshScene();
        c = Ship(10);
        var body = Hazard(0f, 1f);
        body.AddComponent<BossTarget>();
        float w0 = ShipAttackHits.AttackTargetWeight;
        c.Runner.Fire();
        Steps(c.Runner, 2f);
        Check("a mark is 1/4 of a boss hit", body != null && Mathf.Abs(ShipAttackHits.AttackTargetWeight - w0 - .25f) < 1e-3f);
        Teardown(c);
        Object.DestroyImmediate(body);
    }

    static void HeatWaveBurnsOnce()
    {
        FreshScene();
        var c = Ship(3);
        var hot = new[] { Hazard(-1f, -1f), Hazard(1f, 1f), Hazard(0f, 3f) };
        var behind = Hazard(0f, -4f);
        int kills = ShipAttackHits.Kills;
        c.Runner.Fire();
        Steps(c.Runner, .2f);
        Check("the wave rolls up: what is far above is not burnt yet", Alive(hot[2]));
        Steps(c.Runner, 4f);
        Check("it burns everything it passes (" + (ShipAttackHits.Kills - kills) + ")",
              !Alive(hot[0]) && !Alive(hot[1]) && !Alive(hot[2]) && ShipAttackHits.Kills - kills == 3);
        Check("but not what is behind the ship", Alive(behind));
        Check("and it ends once it is off screen", c.Runner.ActiveRuns == 0);
        Teardown(c);
        Object.DestroyImmediate(behind);

        FreshScene();
        c = Ship(3);
        var boss = Boss(0f, 1f);
        c.Runner.Fire();
        Steps(c.Runner, 4f);
        Check("a wave is one boss hit", boss.hits == 1 && Mathf.Approximately(boss.weight, 1f));
        Teardown(c);
        Object.DestroyImmediate(boss.gameObject);

        SetLevel(3, ShipWeaponUpgrades.MaxLevel);
        FreshScene();
        c = Ship(3);
        boss = Boss(0f, 1f);
        c.Runner.Fire();
        Steps(c.Runner, 4f);
        Check("with a second wave the boss still takes one hit in all (" + boss.hits + " x " + boss.weight + ")",
              boss.hits == 2 && Mathf.Abs(boss.weight - 1f) < 1e-3f);
        Teardown(c);
        Object.DestroyImmediate(boss.gameObject);
        SetLevel(3, 0);
    }

    static void BoomerangHitsBothLegs()
    {
        FreshScene();
        var c = Ship(11);
        var l = ShipLoadoutTable.For(11);
        Vector3 nose = c.Runner.Nose;
        var outLeg = Hazard(nose.x + 1.9f, nose.y + l.range * .5f);
        var backLeg = Hazard(nose.x - 1.9f, nose.y + l.range * .5f);
        var apart = Hazard(nose.x + 1.9f, nose.y + 6.5f);
        c.Runner.Fire();
        Steps(c.Runner, ShipAttackRunner.StarSeconds(l) * .5f);
        Check("it kills on the way out", !Alive(outLeg) && Alive(backLeg));
        Steps(c.Runner, ShipAttackRunner.StarSeconds(l) * .6f);
        Check("and again on the way back", !Alive(backLeg) && Alive(apart));
        Check("then it is done, back at the ship", c.Runner.ActiveRuns == 0);
        Check("the return ends at the ship", ShipAttackRunner.StarOffset(l, 1f, 1f).sqrMagnitude < 1e-6f &&
              Mathf.Abs(ShipAttackRunner.StarOffset(l, 1f, .5f).y - l.range) < 1e-4f);
        Teardown(c);
        Object.DestroyImmediate(apart);

        FreshScene();
        c = Ship(11);
        c.Runner.Fire();
        Check("the base attack throws two stars (front + tail)", AttackPool.ActiveSprites == 2);
        Teardown(c);
        SetLevel(11, 0);
    }

    // The rear star, the per-step star counts, and the view / lane clamp.
    static void BoomerangRearAndUpgrades()
    {
        int[] expect = { 2, 3, 4, 5, 6 };
        for (int lv = 0; lv <= ShipWeaponUpgrades.MaxLevel; lv++)
        {
            SetLevel(11, lv);
            FreshScene();
            var c = Ship(11);
            var l = ShipWeaponUpgrades.LoadoutFor(11);
            Check("level " + lv + " throws " + expect[lv] + " stars (" + ShipAttackRunner.StarCount(l) + ")",
                  ShipAttackRunner.StarCount(l) == expect[lv]);
            c.Runner.Fire();
            Check("level " + lv + " draws " + expect[lv] + " stars (" + AttackPool.ActiveSprites + ")",
                  AttackPool.ActiveSprites == expect[lv]);
            Teardown(c);
        }
        Check("the star count never passes the max",
              ShipAttackRunner.StarMax == 6 && ShipAttackRunner.StarMax <= AttackV3.Max &&
              ShipAttackRunner.StarCount(new ShipLoadout { shots = 99 }) == ShipAttackRunner.StarMax);

        // the rear star travels behind the ship first and ends back at it
        SetLevel(11, 0);
        var lo = ShipWeaponUpgrades.LoadoutFor(11);
        Check("star 1 is the rear one", !ShipAttackRunner.StarIsRear(0) && ShipAttackRunner.StarIsRear(1));
        Check("the rear star goes backwards and comes home",
              ShipAttackRunner.StarPath(lo, 1, .25f).y < -1f && ShipAttackRunner.StarPath(lo, 1, 1f).sqrMagnitude < 1e-6f);
        Check("it bulges to the other side of the front star",
              ShipAttackRunner.StarPath(lo, 1, .25f).x * ShipAttackRunner.StarPath(lo, 0, .25f).x < 0f);

        // a hazard behind the ship is cut by the rear star
        FreshScene();
        var ship = Ship(11);
        Vector3 nose = ship.Runner.Nose;
        Vector3 tail = nose - Vector3.up * ((nose.y - ship.transform.position.y) * 1.8f);
        Vector3 spot = tail + ShipAttackRunner.StarPath(lo, 1, .15f);
        var behind = Hazard(spot.x, spot.y);
        ship.Runner.Fire();
        Steps(ship.Runner, ShipAttackRunner.StarSeconds(lo) * .6f);
        Check("a hazard behind the ship is hit by the rear star", !Alive(behind));
        Steps(ship.Runner, ShipAttackRunner.StarSeconds(lo));
        Teardown(ship);
        Object.DestroyImmediate(behind);

        // every star of every step stays inside the view and the lane at several aspect ratios
        var view = new Rect(-3f, -5f, 6f, 10f);
        foreach (var half in new[] { 3f, 4.5f, 6f })
        {
            view = new Rect(-half, -5f, half * 2f, 10f);
            bool inside = true;
            for (int lv = 0; lv <= ShipWeaponUpgrades.MaxLevel; lv++)
            {
                var ll = ShipWeaponUpgrades.LoadoutAt(11, lv);
                for (int k = 0; k < ShipAttackRunner.StarCount(ll); k++)
                    for (float u = 0f; u <= 1f; u += .05f)
                        for (float y0 = -5f; y0 <= 5f; y0 += 2.5f)
                        {
                            var p = ShipAttackRunner.StarClamp(new Vector3(0f, y0, 0f) + ShipAttackRunner.StarPath(ll, k, u), view);
                            inside &= p.x >= view.xMin && p.x <= view.xMax && p.y >= view.yMin && p.y <= view.yMax;
                        }
            }
            Check("every star stays in a view " + half * 2f + " wide", inside);
        }
        SetLevel(11, 0);
    }

    static void MinesWaitAndFizzle()
    {
        FreshScene();
        var c = Ship(12);
        c.Runner.Fire();
        Check("four mines leave the nose", c.Runner.LiveMines == 4);
        Steps(c.Runner, 3f);
        Check("they wait while nothing is near", c.Runner.LiveMines == 4 && c.Runner.ActiveRuns == 1);
        Steps(c.Runner, 3.3f);
        Check("and fizzle out after about 6 s without damage", c.Runner.LiveMines == 0 && c.Runner.ActiveRuns == 0);
        Teardown(c);

        FreshScene();
        c = Ship(12);
        Vector3 nose = c.Runner.Nose;
        // the first mine lands at the left of the fan, 2.2 up
        Vector3 spot = nose + Quaternion.Euler(0f, 0f, 35f) * Vector3.up * 2.2f;
        var trip = Hazard(spot.x, spot.y);
        var near = Hazard(spot.x - .4f, spot.y + .5f);
        var far = Hazard(0f, 4f);
        int kills = ShipAttackHits.Kills;
        c.Runner.Fire();
        Steps(c.Runner, MineFlightPlus());
        Check("a mine blows when a body comes close and takes what is within its blast (" + (ShipAttackHits.Kills - kills) + ")",
              !Alive(trip) && !Alive(near) && Alive(far));
        Check("the other mines stay", c.Runner.LiveMines == 3);
        Teardown(c);
        Object.DestroyImmediate(far);

        // mines hold their screen position even while the world scrolls; a hazard falling in sets one off
        FreshScene();
        moveBackGround.speed = .2f;
        c = Ship(12);
        nose = c.Runner.Nose;
        spot = nose + Quaternion.Euler(0f, 0f, 35f) * Vector3.up * 2.2f;
        var faller = Hazard(spot.x, spot.y + 3f);
        c.Runner.Fire();
        Steps(c.Runner, 1f);
        Check("mines do not drift with the world scroll", c.Runner.LiveMines == 4 && Alive(faller));
        for (int i = 0; i < 40 && Alive(faller); i++)
        {
            faller.transform.position += Vector3.down * .1f;
            Steps(c.Runner, .02f);
        }
        Check("a hazard that falls into a mine sets it off", !Alive(faller) && c.Runner.LiveMines == 3);
        Teardown(c);

        FreshScene();
        c = Ship(12);
        SetLevel(12, ShipWeaponUpgrades.MaxLevel);
        for (int i = 0; i < 4; i++) c.Runner.Fire();
        Check("the live mines are capped (" + c.Runner.LiveMines + ")", c.Runner.LiveMines <= ShipAttackRunner.MineCap);
        Check("and so are the drawn effects", AttackPool.ActiveSprites <= AttackPool.MaxSprites);
        Teardown(c);
        SetLevel(12, 0);

        FreshScene();
        c = Ship(12);
        var boss = Boss(0f, 0f);
        c.Runner.Fire();
        Steps(c.Runner, 7f);
        Check("the mines are one boss hit in all (" + boss.weight + ")", boss.hits >= 1 && boss.weight <= 1f + 1e-4f);
        Teardown(c);
        Object.DestroyImmediate(boss.gameObject);
    }

    static float MineFlightPlus() { return ShipAttackRunner.MineFlight + .3f; }

    static void AbductLiftsAndHurls()
    {
        FreshScene();
        var c = Ship(13);
        var grabbed = Hazard(0f, 0f);
        var box = grabbed.AddComponent<BoxCollider2D>();
        var b = Hazard(1f, 2.5f);
        var cc = Hazard(1.2f, 2.9f);
        var behind = Hazard(0f, -4f);
        int kills = ShipAttackHits.Kills;
        c.Runner.Fire();
        Steps(c.Runner, .25f);
        Check("the nearest enemy is lifted towards the hold point", Alive(grabbed) && grabbed.transform.position.y < -.1f && !box.enabled);
        Steps(c.Runner, 2f);
        int dead = (!Alive(b) ? 1 : 0) + (!Alive(cc) ? 1 : 0);
        Check("it is hurled at the crowd: it kills one body and dies itself (" + (ShipAttackHits.Kills - kills) + ")",
              !Alive(grabbed) && dead == 1 && ShipAttackHits.Kills - kills == 2);
        Check("nothing behind the ship was touched", Alive(behind));
        Check("the run is over", c.Runner.ActiveRuns == 0);
        Teardown(c);
        foreach (var g in new[] { b, cc, behind }) if (g != null) Object.DestroyImmediate(g);

        // interrupted: the held body is let go whole
        FreshScene();
        c = Ship(13);
        grabbed = Hazard(0f, 0f);
        box = grabbed.AddComponent<BoxCollider2D>();
        c.Runner.Fire();
        Steps(c.Runner, .1f);
        bool held = !box.enabled;
        c.Runner.StopAll();
        Check("a cut-off grab returns the body's collider", held && box.enabled && Alive(grabbed));
        Teardown(c);
        Object.DestroyImmediate(grabbed);

        // two grabs at max level
        SetLevel(13, ShipWeaponUpgrades.MaxLevel);
        FreshScene();
        c = Ship(13);
        var one = Hazard(-.8f, 0f);
        var two = Hazard(.8f, .5f);
        c.Runner.Fire();
        Steps(c.Runner, .2f);
        Check("two enemies are grabbed at max level", one.transform.position.y < -.05f && two.transform.position.y < .45f);
        Teardown(c);
        Object.DestroyImmediate(one);
        Object.DestroyImmediate(two);
        SetLevel(13, 0);
    }

    static void GaleShovesAndCollides()
    {
        FreshScene();
        var c = Ship(14);
        var h = Hazard(0f, 0f);
        c.Runner.Fire();
        Steps(c.Runner, 1.8f);
        Check("a body in the gust is shoved up (y " + h.transform.position.y.ToString("0.0") + ")",
              Alive(h) && h.transform.position.y > 2.3f && h.GetComponent<ClearTarget>().isActiveAndEnabled);
        Check("and the gust is over", c.Runner.ActiveRuns == 0);
        Teardown(c);
        Object.DestroyImmediate(h);

        FreshScene();
        c = Ship(14);
        var rammer = Hazard(0f, 0f);
        var rammed = Hazard(0f, 3f);
        int kills = ShipAttackHits.Kills;
        c.Runner.Fire();
        Steps(c.Runner, 1.8f);
        Check("a body shoved into another breaks both (" + (ShipAttackHits.Kills - kills) + ")",
              !Alive(rammer) && !Alive(rammed));
        Teardown(c);

        FreshScene();
        c = Ship(14);
        var topper = Hazard(0f, 3.2f);
        c.Runner.Fire();
        Steps(c.Runner, 1.8f);
        Check("a body shoved into the screen top takes a hit", !Alive(topper));
        Teardown(c);

        FreshScene();
        c = Ship(14);
        var boss = Hazard(0f, 0f);
        boss.AddComponent<BossTarget>();
        var behind = Hazard(0f, -4f);
        c.Runner.Fire();
        Steps(c.Runner, 1.8f);
        Check("a boss is never moved and nothing behind the ship is", Mathf.Approximately(boss.transform.position.y, 0f) &&
              Mathf.Approximately(behind.transform.position.y, -4f));
        Teardown(c);
        Object.DestroyImmediate(boss);
        Object.DestroyImmediate(behind);
    }

    static void GaleTurnsShots()
    {
        Vector2 ship = Vector2.zero;
        Vector2 toBoss = ShipAttackRunner.GaleReturn(ship, new Vector2(1f, 3f), true, new Vector2(0f, 8f));
        Check("a turned shot heads for the boss at return speed",
              toBoss.y > 0f && toBoss.x < 0f && Mathf.Approximately(toBoss.magnitude, ShipAttackRunner.ReturnSpeed));
        Vector2 away = ShipAttackRunner.GaleReturn(ship, new Vector2(2f, 3f), false, Vector2.zero);
        Check("with no boss it flies up and outward, far faster than a body is knocked",
              away.y > 0f && away.x > 0f &&
              ShipAttackRunner.ReturnSpeed > ShipAttackRunner.KnockDistance / ShipAttackRunner.KnockSeconds * 2f);
        Check("a shot inside the gust cone is caught",
              ShipAttackRunner.InGust(ship, new Vector2(0f, 3f), .3f, 6f, 4f, 60f));
        Check("beyond the gust front, past its range or behind the ship it is not",
              !ShipAttackRunner.InGust(ship, new Vector2(0f, 3f), .3f, 6f, 1f, 60f) &&
              !ShipAttackRunner.InGust(ship, new Vector2(0f, 8f), .3f, 6f, 8f, 60f) &&
              !ShipAttackRunner.InGust(ship, new Vector2(0f, -3f), .3f, 6f, 4f, 60f));
        Check("a return shot is worth a whole boss hit and half a hit elsewhere",
              ShipAttackRunner.ReturnBossWeight >= 1f && ShipAttackRunner.ReturnWeight <= .5f);
    }

    static void UpgradesScale()
    {
        int max = ShipWeaponUpgrades.MaxLevel;
        Check("Paranoid marks 4, then 6 at max level",
              ShipWeaponUpgrades.LoadoutAt(10, 0).shots == 4 && ShipWeaponUpgrades.LoadoutAt(10, max).shots == 6);
        Check("Solar Fang: thicker band, a second wave",
              ShipWeaponUpgrades.LoadoutAt(3, max).width > ShipWeaponUpgrades.LoadoutAt(3, 0).width &&
              ShipWeaponUpgrades.LoadoutAt(3, max).shots == 2);
        Check("Ninja: two stars (front and rear), one more per colour, six at max level",
              ShipWeaponUpgrades.LoadoutAt(11, 0).shots == 2 && ShipWeaponUpgrades.LoadoutAt(11, max).shots == 6);
        Check("Saboteur: 4 mines, 6 at max level",
              ShipWeaponUpgrades.LoadoutAt(12, 0).shots == 4 && ShipWeaponUpgrades.LoadoutAt(12, max).shots == 6);
        Check("UFO: pierces one more, two grabs",
              ShipWeaponUpgrades.LoadoutAt(13, max).maxHits == 2 && ShipWeaponUpgrades.LoadoutAt(13, max).shots == 2);
        Check("Dove: wider and longer gust",
              ShipWeaponUpgrades.LoadoutAt(14, max).width > ShipWeaponUpgrades.LoadoutAt(14, 0).width &&
              ShipWeaponUpgrades.LoadoutAt(14, max).range > ShipWeaponUpgrades.LoadoutAt(14, 0).range);
    }
}
