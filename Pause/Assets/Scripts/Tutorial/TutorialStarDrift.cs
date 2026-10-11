using UnityEngine;

// How a rushed tutorial star-dust piece moves: straight down at a steady,
// quick speed (the tutorial's world scroller sits near zero, so the normal
// scroller would leave it hanging), only while the world is moving -- the same
// gate as every other tutorial mover, so letting go freezes it. Gone once it
// leaves the bottom edge.
//
// Only pieces spawnGoodStuffTut.RushStars makes get this; real-game dust is
// untouched.
public class TutorialStarDrift : MonoBehaviour
{
    // Fast enough that a piece spawned within DustRush.Reach of the ship is on
    // it in about a second.
    public const float Speed = 4.2f;
    // Gone once this far below the bottom edge.
    public const float LeaveBelow = .6f;

    public static TutorialStarDrift AddTo(GameObject star)
    {
        if (star == null) return null;
        var scroller = star.GetComponent<moveItemEnmInStrightLine>();
        if (scroller != null) scroller.enabled = false;
        var d = star.GetComponent<TutorialStarDrift>();
        return d != null ? d : star.AddComponent<TutorialStarDrift>();
    }

    void Update()
    {
        Step(Time.deltaTime, (TouchInput.IsPressed || score.pauseCounter <= 0) && !buttonClicks.playerDied);
    }

    // One frame; dt is scaled time. Returns false once the piece is gone.
    public bool Step(float dt, bool worldMoving)
    {
        if (!worldMoving || dt <= 0f) return true;
        var p = transform.position;
        p.y -= Speed * dt;
        transform.position = p;
        float bottom, top;
        TutorialAtomDrift.View(out bottom, out top);
        if (p.y >= bottom - LeaveBelow) return true;
        if (Application.isPlaying) Destroy(gameObject);
        else DestroyImmediate(gameObject);
        return false;
    }
}
