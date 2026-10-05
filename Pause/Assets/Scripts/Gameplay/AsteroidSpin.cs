using UnityEngine;

// Per-instance tumble for asteroid/meteor art.
//
// Speed and direction are rolled fresh in Start() from a range set per
// prefab (bigger rocks roll a slower range so they read as heavier), so a
// field of the same asteroid doesn't all spin in lockstep. Gated on the same
// "the board holds still while paused" rule moveEnimes already follows, so
// spin stops dead the instant the player lifts their finger, same as
// everything else on screen.
//
// Floating rocks (EnemyDef.floating: a chunk of a world's ground with its cap
// on top) set swayDegrees instead: they stay upright and rock gently either
// side of their spawn rotation, starting at a random point of the swing, so
// the cap always reads as "up".
public class AsteroidSpin : MonoBehaviour
{
    [Tooltip("Degrees/second range to roll a speed from. Direction (cw/ccw) is rolled separately.")]
    public Vector2 speedRange = new Vector2(20f, 140f);

    [Tooltip("Above zero: sway upright up to this many degrees either side instead of tumbling.")]
    public float swayDegrees = 0f;

    [Tooltip("Seconds for one full sway (there and back), when swaying.")]
    public float swayPeriod = 3.2f;

    float speed;
    float swayClock;
    Quaternion rest;

    public bool Sways => swayDegrees > 0f;

    void Start()
    {
        speed = Random.Range(speedRange.x, speedRange.y) * (Random.value < 0.5f ? -1f : 1f);
        rest = transform.rotation;
        swayClock = Random.value * swayPeriod;
    }

    void Update()
    {
        bool flying = !buttonClicks.playerDied &&
                      (TouchInput.IsPressed || score.pauseCounter <= 0);
        if (!flying) return;
        Advance(Time.deltaTime);
    }

    // One step of the tumble / sway (DeathCrash's slow motion steps it by hand).
    public void Advance(float dt)
    {
        if (Sways)
        {
            swayClock += dt;
            float a = Mathf.Sin(swayClock / Mathf.Max(.1f, swayPeriod) * 2f * Mathf.PI) * swayDegrees;
            transform.rotation = rest * Quaternion.Euler(0f, 0f, a);
            return;
        }
        transform.Rotate(0f, 0f, speed * dt);
    }
}
