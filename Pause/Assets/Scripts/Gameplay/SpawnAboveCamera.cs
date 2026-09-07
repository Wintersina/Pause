using UnityEngine;

// Keeps a spawn point above whatever the camera currently shows.
//
// "Enemey_Item_Position" (the shared object enmiesOnBoard/spawnGoodStuff
// spawn from, in both gameS1 and tutorialS5) sat at a fixed world Y -- 5.5 in
// gameS1, tuned for the camera's default orthographicSize of 5, half a unit
// of headroom above its visible top edge. CameraFit grows that size on tall
// phones (up to 6.65 on the device it was written against), and nothing
// repositioned the spawn point to match, so enemies and pickups started
// appearing already inside the visible area on exactly the devices CameraFit
// exists to support.
//
// Recomputes Y as the camera's current visible top edge plus the same 0.5
// margin the original 5.5 implied, so it keeps the original off-screen feel
// at any camera size rather than a value only correct for the default one.
public class SpawnAboveCamera : MonoBehaviour
{
    [Tooltip("Clearance above the camera's actual visible top edge.")]
    public float margin = 0.5f;

    int lastScreenW = -1, lastScreenH = -1;

    void Start()
    {
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

        lastScreenW = Screen.width;
        lastScreenH = Screen.height;

        // Camera y is not assumed to be exactly 0 -- add its own position so
        // this is correct even if a scene's camera is not perfectly centred.
        float visibleTop = cam.transform.position.y + cam.orthographicSize;
        transform.position = new Vector3(transform.position.x, visibleTop + margin, transform.position.z);
    }
}

// Attaches to the shared spawn point whenever gameS1 or tutorialS5 loads, so
// neither scene needed hand-editing.
public static class SpawnAboveCameraBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                              UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (scene.name != "gameS1" && scene.name != "tutorialS5") return;

        var go = SceneUtil.FindAny("Enemey_Item_Position");
        if (go == null) return;
        if (go.GetComponent<SpawnAboveCamera>() == null)
            go.AddComponent<SpawnAboveCamera>();
    }
}
