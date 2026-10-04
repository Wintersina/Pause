using UnityEngine;
using System.Collections;

// The rocks' and aliens' mover: scrolls down with the board and weaves
// sideways. The weave is WeavePlan: x = PingPong(clock, amplitude), set
// from the global clock every frame, so where the enemy was spawned on x
// never mattered -- its weave amplitude decides where it flies. The spawner
// now picks that amplitude through SpawnSpace (SetWeave) so the band it
// weaves across stays clear of every other enemy; left unset it rolls the
// original Random.Range(-1.15, 2.45) in Start.
public class moveEnimes : MonoBehaviour, IMovementFootprint {

    private float itemSpeed;
    private float randPos;
    private bool alreadyMoved;
    private bool weaveSet;
    private bool started;

    // the original roll for the weave amplitude
    public const float MinAmplitude = -1.15f, MaxAmplitude = 2.45f;

    void Awake() { ClearTarget.Ensure(gameObject); } // ultimate's early-clear registry

    public float WeaveAmplitude => randPos;

    // Fix the weave before the first frame (the spawner's planned amplitude).
    public void SetWeave(float amplitude)
    {
        randPos = WeavePlan.Safe(amplitude);
        weaveSet = true;
    }

    // Use this for initialization
    void Start () {
        started = true;
        itemSpeed = 30;
        alreadyMoved = true;
        if (!weaveSet) randPos = WeavePlan.Safe(Random.Range(MinAmplitude, MaxAmplitude));
        weaveSet = true;
    }

	// Update is called once per frame
	void Update () {
        if (TouchInput.IsPressed && !buttonClicks.playerDied)
            Step(Time.deltaTime, SpawnSpace.Clock);
        else if (score.pauseCounter <= 0 && !buttonClicks.playerDied)
            Step(Time.deltaTime, SpawnSpace.Clock);
    }

    // One frame of flight; dt and the weave clock are explicit so a headless
    // simulation can step it (Time.deltaTime is 0 outside Play mode).
    public void Step(float dt, float clock)
    {
        if (!started) Start();
        // Translate() defaults to local space. That was harmless while
        // nothing ever rotated this transform, but AsteroidSpin now does --
        // and a local-space "down" rotates right along with the object, so a
        // spinning asteroid's actual travel direction swings away from
        // straight down and can point back up the screen for part of its
        // spin, reading as moving backwards. World space keeps travel tied
        // to the screen, independent of whatever the sprite is doing.
        transform.Translate(new Vector2(0, -1) * moveBackGround.speed * dt * itemSpeed, Space.World);
        if (transform.position.x <= 2.4 && transform.position.x >= -2.4 && alreadyMoved)
        {
            transform.position = new Vector3(WeavePlan.X(randPos, clock), transform.position.y, transform.position.z);
        }
    }

    // IMovementFootprint: the whole weave band (the clock runs while the
    // board is paused, so the timing can't be predicted -- only the band).
    public Rect SweptBounds(Vector2 center, Vector2 half, float from, float to)
    {
        return WeavePlan.Band(randPos, center, half);
    }

    public bool SelfSteering => false;
}

// The weave both as a pure function (the mover uses it) and as a reusable
// spawn-candidate pattern (the spawner sets `amplitude` per try).
public sealed class WeavePlan : IMovementFootprint
{
    public float amplitude;

    // Mathf.PingPong with a negative length holds at 2 x length; with a
    // positive one it sweeps [0, length]. Length 0 would be NaN.
    public static float Safe(float amplitude)
    {
        return Mathf.Abs(amplitude) < .001f ? .001f : amplitude;
    }

    public static float X(float amplitude, float clock)
    {
        return Mathf.PingPong(clock, amplitude);
    }

    // The x range PingPong(clock, amplitude) can take.
    public static void Range(float amplitude, out float lo, out float hi)
    {
        if (amplitude < 0f) { lo = hi = 2f * amplitude; }
        else { lo = 0f; hi = amplitude; }
    }

    public static Rect Band(float amplitude, Vector2 center, Vector2 half)
    {
        float lo, hi;
        Range(amplitude, out lo, out hi);
        // the body is where it is now too (before its first weave step)
        lo = Mathf.Min(lo, center.x);
        hi = Mathf.Max(hi, center.x);
        return Rect.MinMaxRect(lo - half.x, center.y - half.y, hi + half.x, center.y + half.y);
    }

    public Rect SweptBounds(Vector2 center, Vector2 half, float from, float to)
    {
        return Band(amplitude, center, half);
    }

    public bool SelfSteering => false;
}
