using UnityEngine;
using UnityEngine.SceneManagement;

// Keeps leftPipe/rightPipe spanning the full visible height of the screen.
//
// Both rails are a quad scaled to localScale.y = 10.85, baked for the
// camera's baseline orthographicSize of 5 (visible full height 10, so the
// rail overshoots it by ~8.5% -- a small margin against seams). CameraFit
// grows orthographicSize on phones (6.6 on 16:9, 8.7 on a 21:9 Galaxy Z
// Flip, ~9.1 on 22:9) without anything telling the rails to grow too, so on those devices
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
    // The camera's size is what the rail is fitted to: CameraFit may settle
    // it after this object's own Start / Update in the same frame, and a
    // screen-size check alone would then never refit the rail.
    float lastCamSize = -1f;

    void Start()
    {
        meshFilter = GetComponent<MeshFilter>();
        Reposition();
    }

    void Update()
    {
        if (ScreenInfo.Width != lastScreenW || ScreenInfo.Height != lastScreenH || CameraSizeChanged())
            Reposition();
    }

    bool CameraSizeChanged()
    {
        var cam = Camera.main;
        return cam != null && cam.orthographic && !Mathf.Approximately(cam.orthographicSize, lastCamSize);
    }

    void Reposition()
    {
        var cam = Camera.main;
        if (cam == null || !cam.orthographic) return;
        if (meshFilter == null || meshFilter.sharedMesh == null) return;
        lastCamSize = cam.orthographicSize;

        float meshHeight = meshFilter.sharedMesh.bounds.size.y;
        if (meshHeight <= 0f) return;

        lastScreenW = ScreenInfo.Width;
        lastScreenH = ScreenInfo.Height;

        float requiredWorldHeight = cam.orthographicSize * 2f * coverageMultiplier;
        var scale = transform.localScale;
        scale.y = requiredWorldHeight / meshHeight;
        transform.localScale = scale;
        RefreshTextureTiling(gameObject);
    }

    // Extending the mesh to cover a taller screen must repeat the art rather
    // than stretch its bolts, pipes and lamps. Recompute after world changes
    // too, because each texture has a different transparent canvas width.
    public static void RefreshTextureTiling(GameObject wall)
    {
        var renderer = wall.GetComponent<Renderer>();
        var mesh = wall.GetComponent<MeshFilter>();
        if (renderer == null || mesh == null || mesh.sharedMesh == null) return;
        var mat = renderer.material;
        var texture = mat.mainTexture;
        if (texture == null || texture.width == 0) return;
        var bounds = mesh.sharedMesh.bounds.size;
        float width = Mathf.Abs(bounds.x * wall.transform.lossyScale.x);
        float height = Mathf.Abs(bounds.y * wall.transform.lossyScale.y);
        float overlap = mat.HasProperty("_Overlap") ? mat.GetFloat("_Overlap") : 0f;
        float tileHeight = width * texture.height / texture.width * (1f - overlap);
        if (tileHeight <= 0) return;
        var tiling = mat.mainTextureScale;
        tiling.y = height / tileHeight;
        mat.mainTextureScale = tiling;
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
