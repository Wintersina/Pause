using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: Jade Phantom's FROST NOVA chain reaction (ShipAttackRunner.ChainFrom): when a frozen enemy shatters,
// every ordinary enemy in range freezes too and shatters ChainLinkSeconds later, passing the ice on, for as long as
// a new enemy is in range. Each enemy is caught once per firing; a boss takes one hit and never freezes.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod JadeChainReactionTest.Run
public static class JadeChainReactionTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[JADECHAIN] PASS  " : "[JADECHAIN] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        Constants();
        SpreadsAlongALine();
        StopsAtAGap();
        BossTakesOneHit();
        EndsWhileEnemiesKeepArriving();
        LeavesNothingFrozen();
        AttackPool.StopAll();
        Debug.Log("[JADECHAIN] failures: " + fails);
        return fails;
    }

    // ---- fixtures ------------------------------------------------------

    sealed class FakeBoss : MonoBehaviour, IShipAttackTarget
    {
        public int hits;
        public void TakeShipAttack(int ship, float w, Vector3 at) { hits++; }
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
        moveBackGround.speed = 0f;
        Time.timeScale = 1f;
        EnemyShove.Clear();
    }

    static GameObject Hazard(float x, float y)
    {
        var go = new GameObject("hazard");
        go.tag = "Enimey";
        go.transform.position = new Vector3(x, y, 0f);
        ClearTarget.Ensure(go).SetRadius(.3f);
        return go;
    }

    static bool Alive(GameObject go) { return go != null; }

    static ShipPowerController Ship()
    {
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        for (int n = 1; n < ShipSkins.PerShip; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(6, n));
        PlayerPrefs.SetInt("spawnShip", 6);
        var go = new GameObject("ship6", typeof(SpriteRenderer));
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
        for (float t = 0f; t < seconds - 1e-4f; t += dt) r.Step(dt);
    }

    // Fires, lets the ring pass over `seed` (frozen, shatters ~1.2 s in), then adds `late` after the ring is done so
    // only a chain can reach them.
    static ShipPowerController FireAt(out GameObject seed)
    {
        FreshScene();
        var jade = Ship();
        seed = Hazard(0f, -1f);
        jade.Runner.Fire();
        Steps(jade.Runner, 1.04f);
        return jade;
    }

    // ---- cases ---------------------------------------------------------

    static void Constants()
    {
        Check("the link delay is a short readable beat", ShipAttackRunner.ChainLinkSeconds >= .1f && ShipAttackRunner.ChainLinkSeconds <= .15f);
        Check("the chain has a stock reach and a hard window", ShipAttackRunner.ChainRadius > 0f && ShipAttackRunner.ChainWindowSeconds > 0f);
    }

    static void SpreadsAlongALine()
    {
        GameObject a;
        var jade = FireAt(out a);
        var line = new GameObject[4];
        for (int i = 0; i < line.Length; i++) line[i] = Hazard(0f, -1f + 1.2f * (i + 1));
        int guard = 0;
        while (Alive(a) && guard++ < 100) jade.Runner.Step(.02f);
        Check("the seed shatters", !Alive(a) && jade.Runner.ShatterCount == 1);
        Steps(jade.Runner, .04f);
        var fs = line[0].GetComponent<FrozenState>();
        Check("the next enemy in range is iced, not popped on the same frame", Alive(line[0]) && fs != null && fs.IsFrozen);
        Check("the one after it is not touched yet", Alive(line[1]) && line[1].GetComponent<FrozenState>() == null);
        Steps(jade.Runner, ShipAttackRunner.ChainLinkSeconds + .06f);
        Check("a link later it shatters and the next one freezes", !Alive(line[0]) && Alive(line[1]) && line[1].GetComponent<FrozenState>() != null);
        Steps(jade.Runner, 1f);
        bool allGone = true;
        foreach (var g in line) allGone &= !Alive(g);
        Check("the chain carries down the whole line", allGone);
        Check("each enemy is caught once: 1 seed + 4 links, 5 shatters", jade.Runner.ChainLinks == 4 && jade.Runner.ShatterCount == 5);
        Check("the run ends and nothing stays frozen", jade.Runner.ActiveRuns == 0 && jade.Runner.FrozenNow == 0);
        Teardown(jade);
    }

    static void StopsAtAGap()
    {
        GameObject a;
        var jade = FireAt(out a);
        float reach = ShipAttackRunner.ChainRadius;
        var near = Hazard(0f, -1f + reach * .8f);
        var beyondGap = Hazard(0f, -1f + reach * .8f + reach + .6f);   // farther than the reach from the last shatter
        Steps(jade.Runner, 2f);
        Check("in range of the seed: caught", !Alive(near));
        Check("past the gap: untouched, not frozen", Alive(beyondGap) && beyondGap.GetComponent<FrozenState>() == null);
        Check("the run ends", jade.Runner.ActiveRuns == 0 && jade.Runner.ChainLinks == 1);
        Object.DestroyImmediate(beyondGap);
        Teardown(jade);
    }

    static void BossTakesOneHit()
    {
        GameObject a;
        var jade = FireAt(out a);
        var boss = Hazard(0f, 0f);
        var fake = boss.AddComponent<FakeBoss>();
        var e1 = Hazard(0.6f, .8f);
        var e2 = Hazard(-.6f, 1.6f);
        Steps(jade.Runner, 3f);
        Check("a boss next to several shatters takes one hit in the firing, and never freezes", fake.hits == 1 && boss.GetComponent<FrozenState>() == null);
        Check("the enemies beside it still chain", !Alive(e1) && !Alive(e2));
        Object.DestroyImmediate(boss);
        Teardown(jade);
    }

    static void EndsWhileEnemiesKeepArriving()
    {
        GameObject a;
        var jade = FireAt(out a);
        // a fresh enemy next to the last shatter every .05 s (so a link is always pending), far longer than the chain window
        float t = 0f;
        int spawned = 0;
        while (t < ShipAttackRunner.ChainWindowSeconds + 4f)
        {
            Hazard(0f, 0f);
            spawned++;
            Steps(jade.Runner, .05f);
            t += .05f;
        }
        Steps(jade.Runner, 1f);
        Check("a stream of arrivals cannot keep the chain going: the run ended", jade.Runner.ActiveRuns == 0 && jade.Runner.FrozenNow == 0);
        Check("it did chain onto arrivals meanwhile", jade.Runner.ChainLinks > 3 && jade.Runner.ChainLinks <= spawned);
        foreach (var h in Object.FindObjectsByType<ClearTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (h != null) Object.DestroyImmediate(h.gameObject);
        Teardown(jade);
    }

    static void LeavesNothingFrozen()
    {
        GameObject a;
        var jade = FireAt(out a);
        var line = new[] { Hazard(0f, .2f), Hazard(0f, 1.4f), Hazard(0f, 2.6f) };
        int guard = 0;
        while (Alive(a) && guard++ < 100) jade.Runner.Step(.02f);
        Steps(jade.Runner, .04f);
        Check("mid-chain: a link is frozen", jade.Runner.FrozenNow >= 1);
        // a frozen link is destroyed by something else: forgotten, the chain just ends there
        Object.DestroyImmediate(line[0]);
        Steps(jade.Runner, 1f);
        Check("an enemy destroyed mid-chain does not break it", jade.Runner.FrozenNow == 0 && jade.Runner.ActiveRuns == 0);
        Teardown(jade);

        // the ship stopping mid-chain (death, scene change, planetfall) thaws what is still frozen
        jade = FireAt(out a);
        var b = Hazard(0f, .2f);
        var c = Hazard(0f, 1.4f);
        int g2 = 0;
        while (Alive(a) && g2++ < 100) jade.Runner.Step(.02f);
        Steps(jade.Runner, .04f);
        var fsB = b.GetComponent<FrozenState>();
        Check("a link is frozen before the cut", fsB != null && fsB.IsFrozen);
        jade.Runner.SendMessage("OnDestroy");
        Check("stopping the runner thaws it and clears the bookkeeping", !fsB.IsFrozen && jade.Runner.FrozenNow == 0 && Alive(b) && Alive(c));
        Object.DestroyImmediate(b);
        Object.DestroyImmediate(c);
        Teardown(jade);
    }
}
