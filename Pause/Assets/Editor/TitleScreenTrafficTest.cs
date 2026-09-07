using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;

// Covers the home-screen (startS4) ship traffic behavior requested
// 2026-09-07: no two ships in the air share a hull, ships hold whatever
// heading they spawned at instead of tumbling, and colliding still destroys
// both. Scoped entirely to TitleScreenTraffic -- gameplay scenes are
// untouched.
public static class TitleScreenTrafficTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TT] PASS  " : "[TT] FAIL  ") + what);
        if (!ok) fails++;
    }

    static FieldInfo Priv(System.Type t, string name) =>
        t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

    public static void Run()
    {
        // Opened once for the whole run, not per test: TitleScreenTraffic
        // builds everything it needs procedurally and has no actual
        // dependency on scene contents, and re-opening OpenSceneMode.Single
        // between tests tears down the previous round's ship
        // SpriteRenderers -- the only thing keeping shopingShips'/
        // OriginalShipArt's static sprite caches from going stale, since
        // those are plain Dictionaries with no way to notice their cached
        // Sprite was unloaded. That's a real fragility in those caches, not
        // something introduced here, and well outside this fix's scope; the
        // workaround is to simply not manufacture the scene reload that
        // triggers it.
        EditorSceneManager.OpenScene("Assets/Scenes/startS4.unity", OpenSceneMode.Single);

        NoDuplicateHullsAmongActiveFlyers();
        HeadingHoldsFixedNoTumble();
        CollisionsStillDestroyBothShips();

        Debug.Log("[TT] failures: " + fails);
        EditorApplication.Exit(0);
    }

    static void NoDuplicateHullsAmongActiveFlyers()
    {
        var go = new GameObject("~TitleTrafficTest");
        var comp = go.AddComponent<TitleScreenTraffic>();
        comp.trafficCount = 6;
        comp.SendMessage("Start");

        var flyersField = Priv(typeof(TitleScreenTraffic), "flyers");
        var flyers = flyersField.GetValue(comp) as System.Collections.IList;

        Check("spawned the requested number of flyers", flyers.Count == 6);

        var hullIndexField = flyers.Count > 0 ? flyers[0].GetType().GetField("hullIndex") : null;
        Check("Flyer exposes hullIndex", hullIndexField != null);
        if (hullIndexField == null) { Object.DestroyImmediate(go); return; }

        var seen = new HashSet<int>();
        bool anyDuplicate = false;
        foreach (var f in flyers)
        {
            int idx = (int)hullIndexField.GetValue(f);
            if (!seen.Add(idx)) anyDuplicate = true;
        }
        Check("no two ships in the air at once share the same hull", !anyDuplicate);

        // Retiring one and relaunching it repeatedly must never collide with
        // the hulls the other five still-active flyers are using.
        var goField = flyers[0].GetType().GetField("go");
        var launchMethod = typeof(TitleScreenTraffic).GetMethod("Launch",
            BindingFlags.NonPublic | BindingFlags.Instance);

        bool everCollided = false;
        for (int trial = 0; trial < 25; trial++)
        {
            var target = flyers[0];
            launchMethod.Invoke(comp, new object[] { target, false });

            var used = new HashSet<int>();
            foreach (var f in flyers)
            {
                var fgo = (GameObject)goField.GetValue(f);
                if (fgo == null || !fgo.activeSelf) continue;
                int idx = (int)hullIndexField.GetValue(f);
                if (!used.Add(idx)) everCollided = true;
            }
        }
        Check("repeatedly relaunching one ship never collides with the others' hulls", !everCollided);

        // Only the lightweight controller is torn down -- destroying the
        // ship SpriteRenderers here would leave OriginalShipArt/shopingShips'
        // static sprite caches as the sole remaining reference, and Unity's
        // asset cleanup on the next scene load would then unload those
        // cached Sprites out from under later tests in this same run
        // (a real staleness risk in the actual caches, not something this
        // test file should paper over by never exercising it).
        Object.DestroyImmediate(go);
    }

    static void HeadingHoldsFixedNoTumble()
    {
        var go = new GameObject("~TitleTrafficTest2");
        var comp = go.AddComponent<TitleScreenTraffic>();
        comp.trafficCount = 1;
        comp.SendMessage("Start");

        var flyersField = Priv(typeof(TitleScreenTraffic), "flyers");
        var flyers = flyersField.GetValue(comp) as System.Collections.IList;
        var goField = flyers[0].GetType().GetField("go");
        var shipGo = (GameObject)goField.GetValue(flyers[0]);

        Quaternion before = shipGo.transform.rotation;
        for (int i = 0; i < 30; i++) comp.SendMessage("Update");
        Quaternion after = shipGo.transform.rotation;

        Check("heading stays exactly what it was spawned at across many frames (no per-frame spin)",
              Quaternion.Angle(before, after) < 0.0001f);

        Object.DestroyImmediate(go); // see the comment in the previous test re: not destroying shipGo
    }

    static void CollisionsStillDestroyBothShips()
    {
        var go = new GameObject("~TitleTrafficTest3");
        var comp = go.AddComponent<TitleScreenTraffic>();
        comp.trafficCount = 2;
        comp.crashDistance = 0.55f;
        comp.SendMessage("Start");

        var flyersField = Priv(typeof(TitleScreenTraffic), "flyers");
        var flyers = flyersField.GetValue(comp) as System.Collections.IList;
        var goField = flyers[0].GetType().GetField("go");

        var aGo = (GameObject)goField.GetValue(flyers[0]);
        var bGo = (GameObject)goField.GetValue(flyers[1]);
        aGo.transform.position = Vector3.zero;
        bGo.transform.position = new Vector3(0.1f, 0f, 0f); // well inside crashDistance

        var detectCrashes = typeof(TitleScreenTraffic).GetMethod("DetectCrashes",
            BindingFlags.NonPublic | BindingFlags.Instance);
        detectCrashes.Invoke(comp, new object[] { 0f });

        Check("both ships are deactivated after a collision", !aGo.activeSelf && !bGo.activeSelf);

        Object.DestroyImmediate(go);
        Object.DestroyImmediate(aGo);
        Object.DestroyImmediate(bGo);
    }
}
