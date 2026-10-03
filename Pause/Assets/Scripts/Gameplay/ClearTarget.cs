using System.Collections.Generic;
using UnityEngine;

// Live registry of everything the ultimate's cinematic clear could be aiming
// at, so ShipPowerController can tell the instant the screen is empty and end
// the slow-motion early without a FindObjectsByType scan every frame.
//
// The hazard movers (moveEnimes, moveItemEnmInStrightLine) call Ensure() from
// Awake, which covers every enemy, asteroid, alien, chaser and rail mine
// prefab; CinematicClear also Ensure()s each target it snapshots, so anything
// tagged that slipped past a mover still counts. Registration follows the
// component's enabled state, and OnDisable also runs on Destroy, so the list
// never holds a dead object for longer than the frame it died in.
//
// [ExecuteAlways] only so edit-mode tests see the same OnEnable/OnDisable
// bookkeeping the game does; there is no per-frame work here.
[ExecuteAlways]
[DisallowMultipleComponent]
public class ClearTarget : MonoBehaviour
{
    static readonly List<ClearTarget> live = new List<ClearTarget>();

    // Registered entries, hazard-tagged or not (movers sit on pickups too).
    public static int Count => live.Count;

    public static ClearTarget Ensure(GameObject go)
    {
        if (go == null) return null;
        var existing = go.GetComponent<ClearTarget>();
        return existing != null ? existing : go.AddComponent<ClearTarget>();
    }

    // Called by a hit that is about to Destroy() the object: Destroy is
    // deferred to the end of the frame, and the clear check should see the
    // target gone on the frame it was actually hit.
    public static void Release(GameObject go)
    {
        if (go == null) return;
        var t = go.GetComponent<ClearTarget>();
        if (t != null) t.enabled = false;
    }

    public static bool IsHazard(GameObject go)
    {
        return go != null && (go.CompareTag("Enimey") || go.CompareTag("Astr"));
    }

    // Hazards whose centre is inside the camera's view. Anything above the
    // top edge, scrolled out the bottom, or already destroyed doesn't count.
    // With no camera there is no "screen", so every hazard counts.
    public static int CountOnScreen(Camera cam)
    {
        int n = 0;
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var t = live[i];
            if (t == null) { live.RemoveAt(i); continue; }
            if (!IsHazard(t.gameObject)) continue;
            if (cam == null || OnScreen(cam, t.transform.position)) n++;
        }
        return n;
    }

    static bool OnScreen(Camera cam, Vector3 world)
    {
        Vector3 v = cam.WorldToViewportPoint(world);
        return v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;
    }

    void OnEnable()
    {
        if (!live.Contains(this)) live.Add(this);
    }

    void OnDisable()
    {
        live.Remove(this);
    }
}
