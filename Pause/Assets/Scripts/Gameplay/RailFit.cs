using UnityEngine;
using UnityEngine.SceneManagement;

// Keeps leftPipe/rightPipe spanning the full visible height of the screen.
//
// Both rails are a quad scaled to localScale.y = 10.85, baked for the
// camera's baseline orthographicSize of 5 (visible full height 10, so the
// rail overshoots it by ~8.5% -- a small margin against seams). CameraFit
// grows orthographicSize on tall phones (up to 6.65 confirmed on a Galaxy Z
// Flip) without anything telling the rails to grow too, so on those devices
// the fixed-height rail no longer reaches the top/bottom of the screen and
// reads as floating in the middle instead of running edge to edge.
//
// Rescales localScale.y from the mesh's own height so the same ~8.5%
// overshoot holds at any camera size, recomputed whenever the screen size
// changes -- the same pattern CameraFit and SpawnAboveCamera already use.
public class RailFit : MonoBehaviour
{
    [Tooltip("Coverage beyond the camera's full visible height, as a multiplier. " +
             "1.085 matches the original baseline's built-in overshoot (10.85 / 10).")]
    public float coverageMultiplier = 1.085f;

    MeshFilter meshFilter;
    int lastScreenW = -1, lastScreenH = -1;

    void Start()
    {
        meshFilter = GetComponent<MeshFilter>();
        Reposition();
    }

    void Update()
    {
        if (Screen.width != lastScreenW || Screen.height != lastScreenH)
            Reposition();
    }

    void Reposition()
    {
        var cam = Camera.main;
        if (cam == null || !cam.orthographic) return;
        if (meshFilter == null || meshFilter.sharedMesh == null) return;

        float meshHeight = meshFilter.sharedMesh.bounds.size.y;
        if (meshHeight <= 0f) return;

        lastScreenW = Screen.width;
        lastScreenH = Screen.height;

        float requiredWorldHeight = cam.orthographicSize * 2f * coverageMultiplier;
        var scale = transform.localScale;
        scale.y = requiredWorldHeight / meshHeight;
        transform.localScale = scale;
    }
}

// Attaches to leftPipe/rightPipe whenever gameS1 or tutorialS5 loads, so
// neither scene needed hand-editing.
public static class RailFitBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "gameS1" && scene.name != "tutorialS5") return;

        foreach (var name in new[] { "leftPipe", "rightPipe" })
        {
            var go = SceneUtil.FindAny(name);
            if (go == null) continue;
            if (go.GetComponent<RailFit>() == null)
                go.AddComponent<RailFit>();
        }
    }
}
