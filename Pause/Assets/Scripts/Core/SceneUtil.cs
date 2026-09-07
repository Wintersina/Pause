using UnityEngine;
using UnityEngine.SceneManagement;

public static class SceneUtil
{
    // GameObject.Find only ever returns *active* objects, which silently breaks
    // any lookup for something the scene starts with switched off -- the shop's
    // Canvas and PopUpCanvas are both authored inactive. This searches the
    // loaded scenes including inactive objects.
    public static GameObject FindAny(string name)
    {
        var direct = GameObject.Find(name);
        if (direct != null) return direct;

        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root;

                // true == include inactive
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t.gameObject;
            }
        }
        return null;
    }
}
