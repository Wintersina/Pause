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
        AssetDatabase.Refresh();
        string expected = sceneName + ".unity";
        foreach (string guid in AssetDatabase.FindAssets(sceneName + " t:Scene"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith(expected))
            {
                EditorSceneManager.OpenScene(path, mode);
                return;
            }
        }
        Debug.LogError("[SceneLoader] Could not import or find Assets/Scenes/" + expected);
    }
}
