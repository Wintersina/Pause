using UnityEngine;

// A hazard that actually hunts the player instead of just scrolling past.
//
// Spawned from below the visible board (enmiesOnBoard.spawnChaser), it
// accelerates up toward the player's current position -- including
// following left/right dodges -- for a few seconds, then gives up and
// drifts in a loose orbit around wherever the chase left off. It stays on
// screen as a lingering hazard from there: the player can still run into
// it, and it carries the same "Enimey" tag every other hazard does, so
// ShipPowerController's ultimate (laser/missiles/shockwave/railgun) can
// target it exactly like an asteroid.
public class ChaserEnemy : MonoBehaviour
{
    [Tooltip("How long, in seconds, this actively closes in on the player before giving up and wandering.")]
    public float chaseSeconds = 3.5f;

    [Tooltip("World units/second while closing in -- ramps from startChaseSpeed up to this over the chase.")]
    public float chaseSpeed = 2.4f;

    [Tooltip("Chase speed at the moment it spawns, before it has had a chance to accelerate.")]
    public float startChaseSpeed = 0.9f;

    [Tooltip("World units/second while idly drifting after the chase ends.")]
    public float wanderSpeed = 1.1f;

    [Tooltip("Radius of the idle drift loop around the point the chase left off.")]
    public float wanderRadius = 0.7f;

    Transform player;
    float chaseTimer;
    float wanderAngle;
    Vector3 wanderCenter;
    bool wandering;

    void Start()
    {
        chaseTimer = chaseSeconds;
        wanderAngle = Random.value * Mathf.PI * 2f;
        var mover = Object.FindFirstObjectByType<movePlayer>();
        if (mover != null) player = mover.transform;
    }

    void Update()
    {
        bool flying = !buttonClicks.playerDied &&
                      (TouchInput.IsPressed || score.pauseCounter <= 0);
        if (!flying) return;

        if (!wandering)
        {
            chaseTimer -= Time.deltaTime;
            if (player != null)
            {
                float k = 1f - Mathf.Clamp01(chaseTimer / Mathf.Max(0.01f, chaseSeconds));
                float speed = Mathf.Lerp(startChaseSpeed, chaseSpeed, k);
                Vector3 toPlayer = player.position - transform.position;
                if (toPlayer.sqrMagnitude > 0.0001f)
                    transform.position += toPlayer.normalized * speed * Time.deltaTime;
            }
            else
            {
                // No player to chase (e.g. it just died) -- keep drifting up
                // rather than stalling in place.
                transform.position += Vector3.up * startChaseSpeed * Time.deltaTime;
            }

            if (chaseTimer <= 0f)
            {
                wandering = true;
                wanderCenter = transform.position;
            }
        }
        else
        {
            wanderAngle += Time.deltaTime * 1.4f;
            Vector3 target = wanderCenter + new Vector3(Mathf.Cos(wanderAngle), Mathf.Sin(wanderAngle) * 0.6f, 0f) * wanderRadius;
            transform.position = Vector3.MoveTowards(transform.position, target, wanderSpeed * Time.deltaTime);
        }

        // A safety net, not the normal exit: colliding with the player or
        // the ultimate destroying it are the expected ways this goes away.
        var cam = Camera.main;
        if (cam != null && cam.orthographic &&
            transform.position.y > cam.transform.position.y + cam.orthographicSize + 6f)
            Destroy(gameObject);
    }
}
