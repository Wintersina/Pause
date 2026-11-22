using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// The link lines of three player attacks (ShipLinkLines.cs): Paranoid's Arcane Eye tethers (and the no-enemy fallback),
// Saboteur's mine wires and the blast chain along them, Jade Phantom's ice cracks and the shatter chain. Every line
// joins two objects (never a ring or a box around one), the pool is capped, nothing outlines an enemy.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ShipLinksTest.Run
public static class ShipLinksTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[LINKS] PASS  " : "[LINKS] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PoolIsCappedAndNeverOutlines();
        EyeTethersThenPopsInOrder();
        EyeFallsBackToRocks();
        MineWiresAndChain();
        IceCracksAndShatterChain();
        AttackPool.StopAll();
        Debug.Log("[LINKS] failures: " + fails);
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
        moveBackGround.speed = 0f;
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

    static void SetLevel(int id, int level)
    {
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        for (int n = 1; n < ShipSkins.PerShip; n++)
        {
            if (n <= level) PlayerPrefs.SetInt(ShipSkins.OwnedKey(id, n), 1);
            else PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(id, n));
        }
    }

    static ShipPowerController Ship(int id, int level = 0)
    {
        SetLevel(id, level);
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

    static void Teardown(ShipPowerController c, int id)
    {
        c.SendMessage("OnDestroy");
        if (c.Runner != null) c.Runner.SendMessage("OnDestroy");
        if (c.Secret != null) c.Secret.SendMessage("OnDestroy");
        Object.DestroyImmediate(c.gameObject);
        AttackPool.StopAll();
        EnemyShove.Clear();
        collisionDetection.cloakTimer = 0f;
        collisionDetection.lifeCounter = 0;
        SetLevel(id, 0);
    }

    static void Destroy(params GameObject[] list)
    {
        foreach (var g in list) if (g != null) Object.DestroyImmediate(g);
    }

    static float DistXY(Vector3 a, Vector3 b) { return ((Vector2)(a - b)).magnitude; }

    // A line end sits on `go` (inside its radius plus a little: ends are pulled in, never wrapped around).
    static bool EndsOn(Vector3 p, GameObject go, float slack = .25f)
    {
        return go != null && DistXY(p, go.transform.position) <= .3f + slack;
    }

    // every drawn line: two distinct ends, not a ring or a box; and every pooled renderer is an open line
    static bool OnlyOpenLinesBetweenTwoPoints(ShipAttackRunner r)
    {
        var lines = r.Links;
        for (int i = 0; i < lines.LastCount; i++)
            if (DistXY(lines.Last[i].a, lines.Last[i].b) < .03f) return false;
        var root = r.transform.Find("ShipLinkLines");
        if (root == null) return true;
        foreach (var lr in root.GetComponentsInChildren<LineRenderer>(true))
            if (lr.loop || lr.positionCount < 2) return false;
        return true;
    }

    // ---- cases ---------------------------------------------------------

    static void PoolIsCappedAndNeverOutlines()
    {
        var links = new ShipLinkLines(null);
        int accepted = 0;
        for (int i = 0; i < 60; i++)
            if (links.Draw(ShipLinkLines.Kind.Ice, new Vector3(i * .1f, 0f, 0f), new Vector3(i * .1f, 1f + i * .01f, 0f), .02f, Color.white, .1f, i)) accepted++;
        links.EndFrame(.02f);
        Check("the pooled renderers cap the links on screen (" + accepted + " of 60)", accepted == ShipLinkLines.MaxLines && links.LastCount == ShipLinkLines.MaxLines);
        links.EndFrame(.02f);
        Check("a frame nobody drew into hides every line", links.LastCount == 0);
        Check("a zero-length line is refused (never a ring or a dot)", !links.Draw(ShipLinkLines.Kind.Wire, Vector3.zero, Vector3.zero, .02f, Color.white, .1f, 0));
        bool refusedTouching = !links.Link(ShipLinkLines.Kind.Wire, Vector3.zero, new Vector3(.2f, 0f, 0f), .3f, .3f, .02f, Color.white, .1f, 0);
        Check("two objects overlapping get no line", refusedTouching);
        links.Dispose();
    }

    static void EyeTethersThenPopsInOrder()
    {
        FreshScene();
        var c = Ship(10);
        var r = c.Runner;
        Vector3 eye = r.Nose + Vector3.up * ShipAttackRunner.EyeAhead;
        var foes = new[] { Hazard(eye.x - 1.6f, eye.y + 2.5f), Hazard(eye.x, eye.y + 2.5f), Hazard(eye.x + 1.6f, eye.y + 2.5f) };
        r.Fire();
        int maxTethers = 0, maxSnaps = 0;
        bool endsRight = true, fromEye = true, outlines = false;
        var diedAt = new float[foes.Length];
        float t = 0f;
        for (int step = 0; step < 200; step++)
        {
            r.Step(.02f);
            t += .02f;
            var lines = r.Links;
            for (int i = 0; i < lines.LastCount; i++)
            {
                var l = lines.Last[i];
                if (l.kind != ShipLinkLines.Kind.Tether) continue;
                fromEye &= DistXY(l.a, eye) < .05f;
                bool onSome = false;
                foreach (var f in foes) onSome |= EndsOn(l.b, f);
                endsRight &= onSome;
            }
            outlines |= !OnlyOpenLinesBetweenTwoPoints(r);
            maxTethers = Mathf.Max(maxTethers, lines.CountOf(ShipLinkLines.Kind.Tether));
            maxSnaps = Mathf.Max(maxSnaps, lines.CountOf(ShipLinkLines.Kind.Snap));
            for (int i = 0; i < foes.Length; i++) if (foes[i] == null && diedAt[i] == 0f) diedAt[i] = t;
        }
        Check("a tether runs from the eye to each marked enemy (up to " + maxTethers + " at once)", maxTethers >= 2);
        Check("tethers start at the eye and end on a marked enemy", fromEye && endsRight);
        Check("the tethers snap as the marks pop", maxSnaps >= 1);
        bool all = diedAt[0] > 0f && diedAt[1] > 0f && diedAt[2] > 0f;
        Check("all three marks blew", all);
        bool apart = true;
        var sorted = (float[])diedAt.Clone();
        System.Array.Sort(sorted);
        for (int i = 1; i < sorted.Length; i++) apart &= sorted[i] - sorted[i - 1] >= ShipAttackRunner.EyePopGap - .03f;
        Check("they pop one after another, at least " + ShipAttackRunner.EyePopGap + " s apart", apart);
        Check("every tether is an open two-point line, nothing is drawn around an enemy", !outlines);
        Check("no tether is left once the eye is done", r.ActiveRuns == 0 && r.Links.TimedLive == 0 && r.Links.LastCount == 0);
        Teardown(c, 10);
        Destroy(foes);
    }

    static void EyeFallsBackToRocks()
    {
        FreshScene();
        var c = Ship(10);
        var r = c.Runner;
        Vector3 eye = r.Nose + Vector3.up * ShipAttackRunner.EyeAhead;
        // outside the gaze's cone (70 degrees off the nose line): no enemy, only rocks and a mine
        var rock1 = Hazard(eye.x + 2.6f, eye.y + .9f, "Astr");
        var rock2 = Hazard(eye.x - 2.6f, eye.y + .9f, "Astr");
        var mine = Hazard(eye.x - 2.4f, eye.y + 2.2f);
        int kills = ShipAttackHits.Kills;
        r.Fire();
        int maxTethers = 0;
        bool onRocks = true;
        for (int step = 0; step < 250; step++)
        {
            r.Step(.02f);
            var lines = r.Links;
            maxTethers = Mathf.Max(maxTethers, lines.CountOf(ShipLinkLines.Kind.Tether));
            for (int i = 0; i < lines.LastCount; i++)
            {
                if (lines.Last[i].kind != ShipLinkLines.Kind.Tether) continue;
                onRocks &= EndsOn(lines.Last[i].b, rock1) || EndsOn(lines.Last[i].b, rock2) || EndsOn(lines.Last[i].b, mine);
            }
        }
        Check("with no enemy in the cone the eye tethers the rocks and the mine (" + maxTethers + " tethers)", maxTethers >= 2 && onRocks);
        Check("and detonating destroys them (" + (ShipAttackHits.Kills - kills) + " kills)", rock1 == null && rock2 == null && mine == null);
        Check("the eye closes", r.ActiveRuns == 0);
        Teardown(c, 10);
        Destroy(rock1, rock2, mine);

        // an empty sky: nothing to tether, the eye just closes
        FreshScene();
        c = Ship(10);
        c.Runner.Fire();
        for (int step = 0; step < 250; step++) c.Runner.Step(.02f);
        Check("an empty sky draws no tether and the eye closes", c.Runner.ActiveRuns == 0 && c.Runner.Links.LastCount == 0);
        Teardown(c, 10);
    }

    static void MineWiresAndChain()
    {
        // level 0: the four mines are farther apart than the wire reach, so no wires
        FreshScene();
        var c = Ship(12, 0);
        c.Runner.Fire();
        for (int i = 0; i < 40; i++) c.Runner.Step(.02f);
        Check("mines farther apart than " + ShipAttackRunner.MineWireReach + " are not wired", c.Runner.Links.CountOf(ShipLinkLines.Kind.Wire) == 0);
        Teardown(c, 12);

        // level 4: the wire reach is the chain reach: nearest-neighbour wires join the fan
        FreshScene();
        c = Ship(12, 4);
        Vector3 nose = c.Runner.Nose;
        Vector3 spot = nose + Quaternion.Euler(0f, 0f, 35f) * Vector3.up * 2.2f;   // the first mine lands here
        c.Runner.Fire();
        int launched = c.Runner.LiveMines;
        for (int i = 0; i < 3; i++) c.Runner.Step(.02f);   // the mines are still in flight: no wire yet
        Check("a mine in flight has no wire", c.Runner.Links.CountOf(ShipLinkLines.Kind.Wire) == 0);
        float t = .06f;
        for (int i = 0; i < 25; i++) { c.Runner.Step(.02f); t += .02f; }   // all landed and armed, nothing near
        var armed = c.Runner.Links;
        int maxWires = armed.CountOf(ShipLinkLines.Kind.Wire), maxBurn = 0;
        bool endsOnMines = true;
        for (int i = 0; i < armed.LastCount; i++)
        {
            var l = armed.Last[i];
            if (l.kind != ShipLinkLines.Kind.Wire) continue;
            // both ends sit (nearly) on one of the four landing spots
            bool a = false, b = false;
            for (int m = 0; m < 4; m++)
            {
                float ang = (m - 1.5f) * (70f / 3f);
                float dist = 2.2f + 2.8f * ((m * .618f) % 1f);
                Vector3 land = nose + Quaternion.Euler(0f, 0f, -ang) * Vector3.up * dist;
                a |= DistXY(l.a, land) < .2f; b |= DistXY(l.b, land) < .2f;
            }
            endsOnMines &= a && b;
        }
        var trip = Hazard(spot.x, spot.y);   // something walks into the first mine
        float firstGone = -1f, allGone = -1f;
        bool open = true;
        for (int step = 0; step < 100; step++)
        {
            c.Runner.Step(.02f);
            t += .02f;
            var lines = c.Runner.Links;
            maxBurn = Mathf.Max(maxBurn, lines.CountOf(ShipLinkLines.Kind.Burn));
            open &= OnlyOpenLinesBetweenTwoPoints(c.Runner);
            if (firstGone < 0f && c.Runner.LiveMines < launched) firstGone = t;
            if (allGone < 0f && c.Runner.LiveMines == 0) allGone = t;
        }
        Check("armed mines in reach are joined by wires (" + maxWires + ") and never more than the cap", maxWires >= 2 && maxWires <= ShipAttackRunner.MineWireCap);
        Check("each wire joins two mines", endsOnMines);
        Check("a blowing mine's wire burns (" + maxBurn + " fuse lines)", maxBurn >= 1);
        Check("the first mine blows (t " + firstGone.ToString("F2") + ") and the linked ones follow in quick succession (all gone t " + allGone.ToString("F2") + ")",
              firstGone > 0f && allGone > firstGone && allGone - firstGone <= 1f);
        Check("wires are open two-point lines", open);
        Teardown(c, 12);
        Destroy(trip);
    }

    static void IceCracksAndShatterChain()
    {
        FreshScene();
        var c = Ship(6);
        var r = c.Runner;
        // a column of frozen enemies, each within the link reach of the next, the nearest to the ship freezes first
        var col = new[] { Hazard(0f, 0f), Hazard(0f, 1.2f), Hazard(0f, 2.4f), Hazard(0f, 3.6f) };
        r.Fire();
        int maxIce = 0, maxCrack = 0;
        bool iceOnColumn = true, open = true;
        var diedAt = new float[col.Length];
        float t = 0f;
        for (int step = 0; step < 300; step++)
        {
            r.Step(.02f);
            t += .02f;
            var lines = r.Links;
            maxIce = Mathf.Max(maxIce, lines.CountOf(ShipLinkLines.Kind.Ice));
            maxCrack = Mathf.Max(maxCrack, lines.CountOf(ShipLinkLines.Kind.Crack));
            for (int i = 0; i < lines.LastCount; i++)
            {
                var l = lines.Last[i];
                if (l.kind != ShipLinkLines.Kind.Ice) continue;
                bool a = false, b = false;
                foreach (var f in col) { a |= EndsOn(l.a, f, .4f); b |= EndsOn(l.b, f, .4f); }
                iceOnColumn &= a && b;
            }
            open &= OnlyOpenLinesBetweenTwoPoints(r);
            for (int i = 0; i < col.Length; i++) if (col[i] == null && diedAt[i] == 0f) diedAt[i] = t;
        }
        Check("frozen enemies within reach are linked by ice cracks (" + maxIce + " at once, cap " + ShipAttackRunner.IceLinkCap + ")",
              maxIce >= 2 && maxIce <= ShipAttackRunner.IceLinkCap);
        Check("each ice link joins two frozen enemies", iceOnColumn);
        Check("a shatter runs a crack through the linked frozen (" + r.CrackedLinks + " pulled in, " + maxCrack + " cracks drawn)",
              r.CrackedLinks == 3 && maxCrack >= 1);
        bool all = true;
        foreach (var d in diedAt) all &= d > 0f;
        Check("the whole column shatters", all);
        Check("they shatter nearest-first, one link after the other", diedAt[0] <= diedAt[1] && diedAt[1] <= diedAt[2] && diedAt[2] <= diedAt[3]);
        float spread = diedAt[3] - diedAt[0];
        Check("in a quick chain (" + spread.ToString("F2") + " s from first to last)", spread <= 3f * ShipAttackRunner.IceCrackSeconds + .1f);
        Check("ice links are open two-point lines, nothing outlines an enemy", open);
        Check("nothing is left drawn or frozen", r.ActiveRuns == 0 && r.FrozenNow == 0 && r.Links.LastCount == 0);
        Teardown(c, 6);
        Destroy(col);

        // the link cap: more frozen pairs than IceLinkCap never draw more than the cap
        FreshScene();
        c = Ship(6);
        var many = new List<GameObject>();
        for (int i = 0; i < 20; i++) many.Add(Hazard(-2.4f + (i % 5) * 1.2f, -1f + (i / 5) * 1.0f));
        c.Runner.Fire();
        int most = 0;
        for (int step = 0; step < 60; step++)
        {
            c.Runner.Step(.02f);
            most = Mathf.Max(most, c.Runner.Links.CountOf(ShipLinkLines.Kind.Ice));
        }
        Check("a crowd of frozen enemies draws at most " + ShipAttackRunner.IceLinkCap + " ice links (" + most + ")", most >= 1 && most <= ShipAttackRunner.IceLinkCap);
        Teardown(c, 6);
        Destroy(many.ToArray());
    }
}
