using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// Homing player shots (ShipTargets.CollectHoming / NearestHoming / CanHome):
// only inside the camera view, and only the boss while one is alive; plus the
// Gold Warden's shrunken impact art.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod HomingTargetTest.Run
public static class HomingTargetTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[HT] PASS  " : "[HT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

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
    }

    static GameObject Hazard(float x, float y, bool boss = false)
    {
        var go = new GameObject(boss ? "boss" : "hazard");
        go.tag = "Enimey";
        go.transform.position = new Vector3(x, y, 0f);
        ClearTarget.Ensure(go).SetRadius(.3f);
        if (boss) go.AddComponent<BossTarget>();
        return go;
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        var list = new List<ClearTarget>();

        // off-screen targets are ignored, no boss
        FreshScene();
        var near = Hazard(0f, 1f);
        var below = Hazard(0f, -8f);     // scrolled off the bottom
        var above = Hazard(0f, 9f);      // not yet in
        ShipTargets.CollectHoming(list);
        Check("homing collects only the on-screen hazard", list.Count == 1 && list[0].gameObject == near);
        Check("nearest homing mark ignores one below the screen",
              ShipTargets.NearestHoming(new Vector3(0f, -7f, 0f), 0f, null).gameObject == near);
        Check("CanHome: below / above the view are refused, on-screen accepted",
              !ShipTargets.CanHome(below.GetComponent<ClearTarget>()) &&
              !ShipTargets.CanHome(above.GetComponent<ClearTarget>()) &&
              ShipTargets.CanHome(near.GetComponent<ClearTarget>()));
        Check("no boss: BossAlive is false", !ShipTargets.BossAlive());

        // boss fight: the boss and nothing else
        var boss = Hazard(0f, 3.5f, true);
        Check("BossAlive sees the boss body", ShipTargets.BossAlive());
        ShipTargets.CollectHoming(list);
        Check("during a boss fight homing collects the boss only", list.Count == 1 && list[0].gameObject == boss);
        Check("nearest homing mark is the boss even with a minion right on the ship",
              ShipTargets.NearestHoming(near.transform.position, 0f, null).gameObject == boss);
        Check("a minion cannot be homed on while the boss is up", !ShipTargets.CanHome(near.GetComponent<ClearTarget>()));
        Check("the boss can", ShipTargets.CanHome(boss.GetComponent<ClearTarget>()));

        // boss off screen: nothing to home on (not the minions either)
        boss.transform.position = new Vector3(0f, 12f, 0f);
        ShipTargets.CollectHoming(list);
        Check("a boss outside the view is not targeted and minions stay off-limits",
              list.Count == 0 && ShipTargets.NearestHoming(Vector3.zero, 0f, null) == null);

        // the ship's own attack: missiles go to the boss
        FreshScene();
        PlayerPrefs.SetInt("spawnShip", 7);
        var minion = Hazard(0f, 0f);
        var b2 = Hazard(1f, 3f, true);
        var ship = new GameObject("ship7", typeof(SpriteRenderer));
        ship.transform.position = new Vector3(0f, -3f, 0f);
        var c = ship.AddComponent<ShipPowerController>();
        c.SendMessage("Awake");
        c.SendMessage("Start");
        var gun = ship.GetComponentInChildren<UltimateGun>();
        if (gun != null) gun.SendMessage("Awake");
        c.Runner.FireFree();
        Transform aimed = null;
        foreach (var p in Object.FindObjectsByType<AttackProjectile>(FindObjectsSortMode.None))
            if (p.Active) { aimed = p.TargetTransform; break; }
        Check("a free homing shot during a boss fight locks on the boss", aimed != null && aimed == b2.transform);
        c.SendMessage("OnDestroy");
        if (c.Runner != null) c.Runner.SendMessage("OnDestroy");
        if (c.Secret != null) c.Secret.SendMessage("OnDestroy");
        Object.DestroyImmediate(ship);
        AttackPool.StopAll();
        Object.DestroyImmediate(minion);

        // Gold Warden impact art at 30%
        Check("Gold Warden blast burst is 30% of the old size",
              Mathf.Approximately(ShipVolley.BlastBurstSize(7, .95f), .95f * 2.3f * .3f * ShipFxArt.BurstScale));
        Check("other ships' blast burst is only the global polish scale",
              Mathf.Approximately(ShipVolley.BlastBurstSize(1, .95f), .95f * 2.3f * ShipFxArt.BurstScale));
        Check("Warden impact scale constant is .3 and ship 7 is the Gold Warden",
              Mathf.Approximately(ShipVolley.GoldWardenImpactScale, .3f) && ShipId.KeyOf(ShipVolley.GoldWardenShip) == "GoldWarden");

        // No weapon's blast burst is oversized (hit radii untouched)
        float worst = 0f;
        foreach (int id in ShipId.All)
        {
            var l = ShipLoadoutTable.For(id);
            if (l.volley == VolleyKind.Blast)
                worst = Mathf.Max(worst, ShipVolley.BlastBurstSize(id, l.width));
        }
        Check("largest blast burst is under " + ShipVolley.MaxBurstWorld + " world units (" + worst + ")",
              worst > 0f && worst <= ShipVolley.MaxBurstWorld);

        Debug.Log("[HT] failures: " + fails);
        return fails;
    }
}
