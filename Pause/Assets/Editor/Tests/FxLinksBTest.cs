using UnityEditor.SceneManagement;
using UnityEngine;

// Player attacks that visibly connect to what they affect (branch fx-links-b):
//   Gold Warden "Warden Sentries": each firing turret draws a thin aim line to its current mark, an idle one a faint
//   sweeping scan line, and the line jumps (brighter, thicker) when the mark changes.
//   UFO "Abduct & Hurl": the beam narrows onto the lifted body, which shakes; with no enemy in reach it takes a rock
//   (then scenery / debris) and hurls that, and the hurled body damages what it hits.
// No line is ever drawn on an enemy or parented to one (no outlines).
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod FxLinksBTest.Run
public static class FxLinksBTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[FXB] PASS  " : "[FXB] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        WardenAimLinesPickTheRightEnemy();
        WardenIdleSweeps();
        AbductNarrowsAndShakes();
        AbductFallsBackToRocks();
        AbductFallsBackToScenery();
        AbductPrefersEnemies();

        AttackPool.StopAll();
        Debug.Log("[FXB] failures: " + fails);
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

    static void Steps(ShipAttackRunner r, float seconds, float dt = .02f)
    {
        for (float t = 0f; t < seconds; t += dt) { r.Step(dt); EnemyShove.Step(dt); }
    }

    // Steps until all three sentries are firing-stage and have drawn their line (before the first bolt can land).
    static bool StepUntilLines(ShipAttackRunner r)
    {
        for (float t = 0f; t < 3f; t += .02f)
        {
            r.Step(.02f);
            bool all = true;
            for (int i = 0; i < 3; i++) { var l = r.SentryLine(i); all &= l != null && l.Visible; }
            if (all) return true;
        }
        return false;
    }

    static GameObject NearestTo(Vector3 p, params GameObject[] set)
    {
        GameObject best = null;
        float bd = float.MaxValue;
        foreach (var g in set)
        {
            if (g == null) continue;
            float d = ((Vector2)(g.transform.position - p)).sqrMagnitude;
            if (d < bd) { bd = d; best = g; }
        }
        return best;
    }

    // ---- Warden ------------------------------------------------------------

    static void WardenAimLinesPickTheRightEnemy()
    {
        FreshScene();
        var gold = Ship(7);
        var a = Hazard(-2.2f, 3f);
        var b = Hazard(2.2f, 3.4f);
        gold.Runner.Fire();
        bool ok = StepUntilLines(gold.Runner);
        Check("every sentry draws an aim line once it is firing", ok);
        bool right = ok;
        for (int i = 0; ok && i < 3; i++)
        {
            var line = gold.Runner.SentryLine(i);
            var want = NearestTo(gold.Runner.SentryTurretOf(i).Pivot + Vector3.up * .35f, a, b);
            bool end = Vector2.Distance(line.To, want.transform.position) < .01f;
            bool start = Vector2.Distance(line.From, gold.Runner.SentryMuzzle(i)) < .01f;
            if (!end || !start) right = false;
            Check("sentry " + i + " aims at its nearest enemy from its muzzle", end && start);
        }
        Check("aim lines are thin (width <= " + ShipAttackRunner.AimJumpWidth + ")", right && gold.Runner.SentryLine(0).Thickness <= ShipAttackRunner.AimJumpWidth + 1e-4f);

        // no outlines: the line roots hang off nothing and nothing is parented under the enemies
        bool clean = a.transform.childCount == 0 && b.transform.childCount == 0 && a.GetComponent<SpriteRenderer>() == null;
        for (int i = 0; i < 3; i++) clean &= gold.Runner.SentryLine(i).Root.parent == null;
        Check("no outline or border on enemies; the lines are free-standing", clean);

        // the mark dies: every line jumps to the other enemy, brighter and thicker for a moment
        var line0 = gold.Runner.SentryLine(0);
        var first = NearestTo(gold.Runner.SentryTurretOf(0).Pivot + Vector3.up * .35f, a, b);
        var other = first == a ? b : a;
        Object.DestroyImmediate(first);
        gold.Runner.Step(.02f);
        Check("the aim line jumps to the new mark", Vector2.Distance(line0.To, other.transform.position) < .01f);
        Check("and flashes brighter and thicker on the jump", line0.Thickness > ShipAttackRunner.AimLineWidth + 1e-4f && line0.Alpha > ShipAttackRunner.AimLineAlpha + 1e-4f);
        Steps(gold.Runner, .25f);
        Check("the flash settles back to a thin line", line0.Thickness <= ShipAttackRunner.AimLineWidth + 1e-4f || other == null);
        Check("there are never more than 3 line renderers for 3 sentries", CountLinks() <= 3);
        if (other != null) Object.DestroyImmediate(other);
        Teardown(gold);
        Check("the lines are cleaned up with the ship", CountLinks() == 0);
    }

    static int CountLinks()
    {
        int n = 0;
        foreach (var go in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (go.gameObject.name == "~attackLink") n++;
        return n;
    }

    static void WardenIdleSweeps()
    {
        FreshScene();
        var gold = Ship(7);
        gold.Runner.Fire();
        bool ok = StepUntilLines(gold.Runner);
        Check("with no target every sentry still shows a line", ok);
        var line = gold.Runner.SentryLine(0);
        Check("it is the faint scan line (no target)", ok && Mathf.Approximately(line.Alpha, ShipAttackRunner.ScanLineAlpha) && Mathf.Approximately(line.Thickness, ShipAttackRunner.ScanLineWidth));
        float a0 = Mathf.Atan2((line.To - line.From).y, (line.To - line.From).x) * Mathf.Rad2Deg;
        Steps(gold.Runner, .6f);
        var d = line.To - line.From;
        float a1 = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        Check("the scan line sweeps (" + a0.ToString("0.0") + " -> " + a1.ToString("0.0") + ")", line.Visible && Mathf.Abs(Mathf.DeltaAngle(a0, a1)) > 1f);
        Check("the scan line starts at the muzzle", Vector2.Distance(line.From, gold.Runner.SentryMuzzle(0)) < .05f);
        Teardown(gold);
    }

    // ---- UFO ---------------------------------------------------------------

    static void AbductNarrowsAndShakes()
    {
        FreshScene();
        var c = Ship(13);
        // the hold point is 1.5 u above the nose, so a body just above the nose is lifted (one higher up is drawn down to it)
        var grabbed = Hazard(0f, c.Runner.Nose.y + .2f);
        grabbed.AddComponent<BoxCollider2D>();
        c.Runner.Fire();
        Steps(c.Runner, .1f);
        Vector3 early = grabbed.transform.position;
        Steps(c.Runner, .2f);
        Check("the lifted body rises", grabbed != null && grabbed.transform.position.y > early.y);
        var link = LinkNamed();
        Check("a thin tractor core joins the ship to the body", link != null && link.activeInHierarchy && link.transform.localScale.x < .08f);
        float w1 = link != null ? link.transform.localScale.x : 0f;
        Steps(c.Runner, .12f);
        float w2 = link != null && grabbed != null ? link.transform.localScale.x : 0f;
        Check("the core narrows as the body is drawn in (" + w1.ToString("0.000") + " -> " + w2.ToString("0.000") + ")", w2 < w1);
        Teardown(c);
        if (grabbed != null) Object.DestroyImmediate(grabbed);
    }

    static GameObject LinkNamed()
    {
        foreach (var sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (sr.gameObject.name == "~attackLink") return sr.gameObject;
        return null;
    }

    static void AbductFallsBackToRocks()
    {
        FreshScene();
        var c = Ship(13);
        var rock = Hazard(0f, 0f, "Astr");
        var box = rock.AddComponent<BoxCollider2D>();
        var other = Hazard(1f, 2.5f, "Astr");
        int kills = ShipAttackHits.Kills;
        c.Runner.Fire();
        Steps(c.Runner, .25f);
        Check("with no enemy in reach the beam lifts a rock instead", rock != null && rock.transform.position.y < -.1f && !box.enabled);
        Steps(c.Runner, 2f);
        Check("the hurled rock damages what it hits (kills " + (ShipAttackHits.Kills - kills) + ")", other == null && rock == null && ShipAttackHits.Kills - kills == 2);
        Check("the run is over", c.Runner.ActiveRuns == 0);
        Teardown(c);
        if (other != null) Object.DestroyImmediate(other);
    }

    static void AbductFallsBackToScenery()
    {
        FreshScene();
        var c = Ship(13);
        var debris = Hazard(0f, 0f, "Astr");
        debris.GetComponent<ClearTarget>().Scenery = true;
        var box = debris.AddComponent<BoxCollider2D>();
        c.Runner.Fire();
        Steps(c.Runner, .25f);
        Check("with only backdrop debris around it is lifted", debris != null && debris.transform.position.y < -.1f && !box.enabled);
        Teardown(c);
        if (debris != null) Object.DestroyImmediate(debris);
    }

    static void AbductPrefersEnemies()
    {
        FreshScene();
        var c = Ship(13);
        var nearRock = Hazard(0f, 0f, "Astr");
        var enemy = Hazard(1f, 1.2f);
        enemy.AddComponent<BoxCollider2D>();
        nearRock.AddComponent<BoxCollider2D>();
        c.Runner.Fire();
        Steps(c.Runner, .25f);
        Check("an enemy is grabbed before a nearer rock", enemy.transform.position.y < 1.1f && Mathf.Approximately(nearRock.transform.position.y, 0f));
        Teardown(c);
        if (enemy != null) Object.DestroyImmediate(enemy);
        if (nearRock != null) Object.DestroyImmediate(nearRock);
    }
}
