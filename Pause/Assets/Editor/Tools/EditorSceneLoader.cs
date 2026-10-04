using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor tests are sometimes started from the workspace wrapper rather than
// the Unity project folder. Resolve scenes through AssetDatabase instead of
// assuming a caller's working directory or passing a `Pause/Assets/...` path
// (Unity only accepts project-relative `Assets/...` paths).
public static class EditorSceneLoader
{
    public static void Open(string sceneName, OpenSceneMode mode = OpenSceneMode.Single)
    {
        // The editor refreshes the AssetDatabase at launch (and on focus), so
        // a scene is normally found straight away; only a miss -- a scene
        // written since -- pays for a Refresh (0.1-0.3s each; the suites
        // open ~100 scenes per run).
        string path = Find(sceneName);
        if (path == null)
        {
            AssetDatabase.Refresh();
            path = Find(sceneName);
        }
        if (path != null)
        {
            EditorSceneManager.OpenScene(path, mode);
            return;
        }
        Debug.LogError("[SceneLoader] Could not import or find Assets/Scenes/" + sceneName + ".unity");
    }

    static string Find(string sceneName)
    {
        string expected = sceneName + ".unity";
        foreach (string guid in AssetDatabase.FindAssets(sceneName + " t:Scene"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith(expected)) return path;
        }
        return null;
    }
}
