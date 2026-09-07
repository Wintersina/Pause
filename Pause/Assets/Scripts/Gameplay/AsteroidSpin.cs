using UnityEngine;

// Per-instance tumble for asteroid/meteor art.
//
// Speed and direction are rolled fresh in Start() from a range set per
// prefab (bigger rocks roll a slower range so they read as heavier), so a
// field of the same asteroid doesn't all spin in lockstep. Gated on the same
// "the board holds still while paused" rule moveEnimes already follows, so
// spin stops dead the instant the player lifts their finger, same as
// everything else on screen.
public class AsteroidSpin : MonoBehaviour
{
    [Tooltip("Degrees/second range to roll a speed from. Direction (cw/ccw) is rolled separately.")]
    public Vector2 speedRange = new Vector2(20f, 140f);

    float speed;

    void Start()
    {
        speed = Random.Range(speedRange.x, speedRange.y) * (Random.value < 0.5f ? -1f : 1f);
    }

    void Update()
    {
        bool flying = !buttonClicks.playerDied &&
                      (TouchInput.IsPressed || score.pauseCounter <= 0);
        if (!flying) return;

        transform.Rotate(0f, 0f, speed * Time.deltaTime);
    }
}
