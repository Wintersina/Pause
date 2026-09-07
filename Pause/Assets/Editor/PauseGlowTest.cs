using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PauseGlowTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PG] PASS  " : "[PG] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        var a = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/PauseGlow/pausedGlow_a.png");
        var b = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/PauseGlow/pausedGlow_b.png");
        Check("pausedGlow_a imported as a Sprite", a != null);
        Check("pausedGlow_b imported as a Sprite", b != null);
        Check("the two variants are different sprites", a != b);

        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        var go = SceneUtil.FindAny("paused");
        Check("'paused' object exists", go != null);
        if (go != null)
        {
            var sr = go.GetComponent<SpriteRenderer>();
            Check("'paused' has a SpriteRenderer to swap", sr != null);
        }

        Debug.Log("[PG] failures: " + fails);
        EditorApplication.Exit(0);
    }
}
