using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Guards dead-code removal: deleting a script that a scene or prefab still
// uses leaves a "missing script" component behind, which compiles and builds
// fine and only shows up as a warning when that object loads. Every scene in
// Build Settings and every prefab under Assets must have none.
public static class MissingScriptsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[MS] PASS  " : "[MS] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        foreach (var scene in EditorBuildSettings.scenes)
        {
            var opened = EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);
            var broken = new List<string>();
            foreach (var root in opened.GetRootGameObjects())
                CollectMissing(root, broken);
            Check(scene.path + " has no missing scripts" + Describe(broken), broken.Count == 0);
        }

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            var broken = new List<string>();
            CollectMissing(prefab, broken);
            Check(path + " has no missing scripts" + Describe(broken), broken.Count == 0);
        }

        Debug.Log("[MS] failures: " + fails);
        return fails;
    }

    // Walks the whole hierarchy, inactive children included.
    static void CollectMissing(GameObject root, List<string> broken)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            if (missing > 0) broken.Add(PathOf(t) + " x" + missing);
        }
    }

    static string PathOf(Transform t)
    {
        string path = t.name;
        for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }

    static string Describe(List<string> broken)
    {
        return broken.Count == 0 ? "" : " (" + string.Join(", ", broken) + ")";
    }
}
