using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: two player attacks show where they will land before they land (FxLinks).
//   Lightning Sky javelins: a thin crackling line down the lane shortly before the javelin strikes.
//   Paragon warheads: a faint curved trail behind each falling warhead and a flicker at the impact point.
// And nothing is outlined: no rings, boxes or brackets round an enemy.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod FxLinksTest.Run
public static class FxLinksTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[FXLINK] PASS  " : "[FXLINK] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        JavelinTelegraphComesBeforeTheStrike();
        JavelinFlashesAndFades();
        MissileTrailFollowsItsMissile();
        MissileTelegraphShowsBeforeImpact();
        LinesAreThinPooledAndNeverOutlineAnEnemy();
        AttackPool.StopAll();
        Debug.Log("[FXLINK] failures: " + fails);
        return fails;
    }

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
        moveBackGround.speed = .2f;
        Time.timeScale = 1f;
        FxLinks.StopAll();
    }

    static GameObject Hazard(float x, float y)
    {
        var go = new GameObject("hazard");
        go.tag = "Enimey";
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
    }

    static int ActiveJavelins()
    {
        int n = 0;
        foreach (var p in AttackPool.Projectiles) if (p.Active && p.CurrentKind == AttackProjectile.Kind.Javelin) n++;
        return n;
    }

    static LinkLine First(LinkKind kind)
    {
        foreach (var l in FxLinks.Pool) if (l.Active && l.Kind == kind) return l;
        return null;
    }

    // ---- javelins ---------------------------------------------------------

    static void JavelinTelegraphComesBeforeTheStrike()
    {
        FreshScene();
        var light = Ship(8);
        var target = Hazard(0f, 1f);   // on the centre lane
        light.Runner.Fire();
        LinkLine tele = null;
        for (int i = 0; i < 300 && tele == null && ActiveJavelins() == 0; i++)
        {
            light.Runner.Step(.01f);
            tele = First(LinkKind.JavelinTelegraph);
        }
        Check("a crackle line appears while the javelin has not manifested yet", tele != null && ActiveJavelins() == 0);
        Check("and nothing has been struck yet", target != null);
        if (tele != null)
        {
            // one of the lanes the run computes (centre-out, evenly spaced about the ship, inside the rails)
            var l = ShipLoadoutTable.For(8);
            int count = Mathf.Clamp(l.shots, 1, SkyJavelinLanes.MaxCount);
            float minX, maxX;
            SkyJavelinLanes.Limits(ShipTargets.View(), out minX, out maxX);
            float pitch = SkyJavelinLanes.Pitch(count, l.range, LaneMetrics.Density, minX, maxX);
            bool onLane = false;
            for (int i = 0; i < count; i++)
                onLane |= Mathf.Abs(SkyJavelinLanes.LaneX(i, count, light.transform.position.x, pitch, minX, maxX) - tele.First.x) < .001f;
            Check("the line sits at a javelin lane's x (" + tele.First.x.ToString("F2") + ")", onLane);
            Check("it is vertical at both ends and runs down to the impact point", Mathf.Abs(tele.First.x - tele.Last.x) < .001f && tele.First.y > tele.Last.y + 3f);
            Check("it stops above the hull", tele.Last.y > light.transform.position.y);
            Check("and is thin (" + tele.Width.ToString("F3") + ")", tele.Width <= .05f);
        }
        // the telegraph leads: the javelin only strikes after it
        bool teleSeenFirst = tele != null;
        for (int i = 0; i < 300 && ActiveJavelins() == 0; i++) light.Runner.Step(.01f);
        Check("the javelin manifests after its telegraph", teleSeenFirst && ActiveJavelins() > 0);
        Teardown(light);
        Object.DestroyImmediate(target);
    }

    static void JavelinFlashesAndFades()
    {
        FreshScene();
        var light = Ship(8);
        var far = Hazard(3f, 4f);
        light.Runner.Fire();
        for (int i = 0; i < 300 && ActiveJavelins() == 0; i++) light.Runner.Step(.01f);
        var flash = First(LinkKind.JavelinFlash);
        Check("on the strike the crackle flashes", flash != null && ActiveJavelins() > 0);
        if (flash != null)
        {
            Check("wider than the telegraph", flash.Width > FxLinks.JavelinWidth);
            flash.Advance(FxLinks.JavelinFlashSeconds + .05f);
            Check("and fades out", !flash.Active);
        }
        Teardown(light);
        Object.DestroyImmediate(far);
    }

    // ---- missiles ----------------------------------------------------------

    static void MissileTrailFollowsItsMissile()
    {
        FreshScene();
        var p = Ship(16);
        var foe = Hazard(0f, 1.2f);
        p.Runner.FireMiniWarheads(2);
        int checkedTrails = 0;
        bool follows = true;
        for (int s = 0; s < 120; s++)
        {
            p.Runner.Step(.01f);
            for (int i = 0; i < 2; i++)
            {
                Vector3 at; LinkLine trail, spark;
                if (!p.Runner.WarheadLinks(i, out at, out trail, out spark) || trail == null || !trail.Active) continue;
                checkedTrails++;
                follows &= ((Vector2)(trail.Last - at)).sqrMagnitude < .0001f && trail.Kind == LinkKind.MissileTrail
                           && trail.First.y >= trail.Last.y - .001f;
            }
        }
        Check("each warhead drew a trail while it fell (" + checkedTrails + " samples)", checkedTrails > 4);
        Check("the trail's head sits on its warhead every step, tail behind it", follows);
        Teardown(p);
        Object.DestroyImmediate(foe);
    }

    static void MissileTelegraphShowsBeforeImpact()
    {
        FreshScene();
        var p = Ship(16);
        var foe = Hazard(0f, 1.2f);
        p.Runner.FireMiniWarheads(1);
        LinkLine spark = null;
        Vector3 impact = p.Runner.Nose + Vector3.up * ShipAttackRunner.MiniRange;
        for (int s = 0; s < 120 && spark == null && p.Runner.MiniDetonations == 0; s++)
        {
            p.Runner.Step(.01f);
            spark = First(LinkKind.ImpactSpark);
        }
        Check("a spark flickers at the impact point before the warhead lands", spark != null && p.Runner.MiniDetonations == 0);
        if (spark != null)
        {
            Vector3 mid = (spark.First + spark.Last) * .5f;
            Check("centred on the impact point", ((Vector2)(mid - impact)).magnitude < .05f);
            float spanX = 0f;
            for (int i = 0; i < LinkLine.Points; i++) spanX = Mathf.Max(spanX, Mathf.Abs(spark.Point(i).x - impact.x));
            Check("a short vertical spark, no crosshair (x span " + spanX.ToString("F2") + ")",
                  Mathf.Abs(spark.First.x - spark.Last.x) < .001f && spanX < .12f && Mathf.Abs(spark.First.y - spark.Last.y) <= FxLinks.SparkLength + .001f);
        }
        for (int s = 0; s < 120 && p.Runner.MiniDetonations == 0; s++) p.Runner.Step(.01f);
        Check("the warhead then lands", p.Runner.MiniDetonations == 1);
        Check("and the spark is gone", First(LinkKind.ImpactSpark) == null);
        Teardown(p);
        Object.DestroyImmediate(foe);
    }

    // ---- art rules ----------------------------------------------------------

    static void LinesAreThinPooledAndNeverOutlineAnEnemy()
    {
        FreshScene();
        var light = Ship(8);
        var par = Ship(16);
        var enemies = new List<GameObject>();
        for (int i = 0; i < 6; i++) enemies.Add(Hazard(-1.5f + i * .6f, 1f + (i % 3) * .8f));
        light.Runner.Fire();
        par.Runner.FireMiniWarheads(4);
        bool thin = true, open = true, noBoxes = true, cap = true, plain = true;
        for (int s = 0; s < 120; s++)
        {
            light.Runner.Step(.01f);
            par.Runner.Step(.01f);
            cap &= FxLinks.ActiveCount <= FxLinks.MaxLines;
            foreach (var l in FxLinks.Pool)
            {
                if (!l.Active) continue;
                thin &= l.Width <= .1f;
                if (l.Kind != LinkKind.MissileTrail) open &= ((Vector2)(l.First - l.Last)).magnitude > .3f;   // never a closed ring
                // a line only ever bows a little off its chord, so it cannot wrap round an enemy (no ring, box or bracket)
                Vector2 c0 = l.First, c1 = l.Last;
                Vector2 chord = c1 - c0;
                for (int i = 0; i < LinkLine.Points; i++)
                {
                    Vector2 q = (Vector2)l.Point(i) - c0;
                    float dev = chord.sqrMagnitude < 1e-6f ? q.magnitude : Mathf.Abs(q.x * chord.y - q.y * chord.x) / chord.magnitude;
                    noBoxes &= dev < .2f;
                }
                var lr = l.GetComponent<LineRenderer>();
                plain &= lr != null && !lr.loop && lr.positionCount == LinkLine.Points;
            }
        }
        Check("every line is thin (<= 0.1)", thin);
        Check("no line closes on itself (no ring, no box)", open && plain);
        Check("nothing is drawn around an enemy", noBoxes);
        Check("the pool never exceeds its cap (" + FxLinks.MaxLines + ")", cap && FxLinks.Pool.Count <= FxLinks.MaxLines);
        bool noBracketArt = true;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string n = t.name.ToLowerInvariant();
            noBracketArt &= !(n.Contains("outline") || n.Contains("bracket") || n.Contains("lockon") || n.Contains("border"));
        }
        Check("no outline / bracket / border objects exist", noBracketArt);
        Teardown(light);
        Teardown(par);
        foreach (var e in enemies) if (e != null) Object.DestroyImmediate(e);
    }
}
