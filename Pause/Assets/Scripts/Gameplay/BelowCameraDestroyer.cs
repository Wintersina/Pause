using UnityEngine;
using UnityEngine.SceneManagement;

// Keeps gameS1's "Destroyer" (the trigger strip that removes whatever scrolls
// off the bottom, destoryAnything) below whatever the camera currently shows.
//
// It sat at a fixed y of -7: 2 u under the authored size-5 view. CameraFit
// grows the view on tall phones (bottom edge ~-7.6 on 9:24), so hazards and
// pickups were removed while still visible. It now sits Clearance below
// CameraFit.ViewBottom on every screen (-8 on the authored view: one unit
// lower than before, so a chaser spawned just under the view
// -- enmiesOnBoard.TrySpawnChaser, up to ViewBottom - 2 -- starts clear of it).
public class BelowCameraDestroyer : MonoBehaviour
{
    public const float Clearance = 3f;

    int lastScreenW = -1, lastScreenH = -1;

    void Start()
    {
        Reposition();
    }

    void Update()
    {
        if (ScreenInfo.Width != lastScreenW || ScreenInfo.Height != lastScreenH)
            Reposition();
    }

    public void Reposition()
    {
        lastScreenW = ScreenInfo.Width;
        lastScreenH = ScreenInfo.Height;
        var p = transform.position;
        transform.position = new Vector3(p.x, CameraFit.ViewBottom - Clearance, p.z);
    }
}

// Attaches to gameS1's Destroyer when the scene loads (no scene edit needed).
public static class BelowCameraDestroyerBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "gameS1") return;
        var go = SceneUtil.FindAny("Destroyer");
        if (go == null || go.GetComponent<BelowCameraDestroyer>() != null) return;
        go.AddComponent<BelowCameraDestroyer>();
    }
}
