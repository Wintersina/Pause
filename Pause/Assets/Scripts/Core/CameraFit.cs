using UnityEngine;
using UnityEngine.SceneManagement;

// Keeps the play field on screen on very tall/narrow phones.
//
// Every scene's camera is orthographic size 5, sized for roughly a 16:9 phone
// (half-width ~2.81 world units at that aspect). The player's own movement
// bounds are +/-2.4 and the walls sit at +/-2.5 to +/-3.9. Modern phones run
// tall enough that half-width shrinks below the player's own reach -- on a
// 1080x2520 phone (Galaxy Z Flip, aspect ~0.4286) half-width at size 5 is only
// ~2.14, so the ship can drift off the visible edge of the screen and
// everything on screen reads as oversized/cropped, which is the "zoomed in,
// doesn't fit" the game visibly does on tall devices.
//
// Fix: grow the orthographic size (never shrink it) so the visible half-width
// never drops below a floor that covers the player's reach and the walls'
// inner edge, whatever the device aspect. Extra height on very tall screens is
// a straight gameplay upside for a vertical scroller -- more warning before an
// obstacle arrives -- so only width is protected.
public class CameraFit : MonoBehaviour
{
    [Tooltip("Minimum visible half-width, in world units. The player reaches " +
             "+/-2.4 and the walls' inner edge sits at about +/-2.5.")]
    public float minHalfWidth = 2.85f;

    Camera cam;
    int lastScreenW = -1, lastScreenH = -1;
    float baseSize;

    void Awake()
    {
        cam = GetComponent<Camera>();
        baseSize = cam != null ? cam.orthographicSize : 5f;
    }

    void Start()
    {
        Apply();
    }

    void Update()
    {
        // Screen.width/height change on rotation, window resize, or -- on a
        // foldable like the device this was written for -- folding and
        // unfolding mid-session, so this is checked continuously rather than
        // once. The check itself is two int compares; recomputing only runs
        // on an actual change.
        if (Screen.width != lastScreenW || Screen.height != lastScreenH)
            Apply();
    }

    void Apply()
    {
        if (cam == null || !cam.orthographic) return;
        if (Screen.width <= 0 || Screen.height <= 0) return;

        lastScreenW = Screen.width;
        lastScreenH = Screen.height;

        float size = ComputeSize(baseSize, minHalfWidth, Screen.width, Screen.height);
        if (!Mathf.Approximately(size, cam.orthographicSize))
            Debug.Log(string.Format("[CameraFit] {0} {1}x{2} -> orthographicSize {3:F3}",
                gameObject.scene.name, Screen.width, Screen.height, size));
        cam.orthographicSize = size;
    }

    // Pure and testable without entering Play mode: never shrinks below
    // baseSize, and grows exactly enough to guarantee minHalfWidth stays
    // visible at the given screen dimensions.
    public static float ComputeSize(float baseSize, float minHalfWidth, int screenW, int screenH)
    {
        if (screenW <= 0 || screenH <= 0) return baseSize;

        float aspect = (float)screenW / screenH;
        float requiredSize = minHalfWidth / Mathf.Max(aspect, 0.0001f);
        return Mathf.Max(baseSize, requiredSize);
    }
}

// Attaches CameraFit to the main camera of every scene, so no scene needs
// hand editing and no scene can be added later without picking it up.
public static class CameraFitBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        // the very first scene has already loaded by the time this runs
        Attach();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Attach();
    }

    static void Attach()
    {
        var cam = Camera.main;
        if (cam == null) return;
        if (cam.GetComponent<CameraFit>() == null)
            cam.gameObject.AddComponent<CameraFit>();
    }
}
