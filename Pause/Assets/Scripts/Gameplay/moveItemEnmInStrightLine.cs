using UnityEngine;
using System.Collections;

// Aurtur: Sina Serati
// This script allows the item/ object to move towards the player in a straight line, when there is a finger on the screen.
// if pauses are used up, they will freely move towards the player and current speed calcuated in moveBackGround script.
//
// Pickup atoms (anything carrying AtomSpin) are the exception: they hand their
// motion to AtomWander, which floats them down with a playful wander, never
// above a ceiling inside the ship's reach, and removes them once they are past
// the bottom of the view. The plain Translate is in local space and AtomSpin
// turns atoms, which used to fly them in circles back off the top of the
// screen (see AtomWander).
//
// As a SpawnSpace pattern it is the plain scroller: its sweep is its body.
// (Atoms are pickups, which SpawnSpace keeps apart only softly at spawn; it
// never binds a pattern to them.)

public class moveItemEnmInStrightLine : MonoBehaviour, IMovementFootprint {

    AtomWander wander;
    bool atomChecked;

    // The atom's wander, once it has moved (null for everything else).
    public AtomWander Wander { get { return wander; } }

    void Awake() { ClearTarget.Ensure(gameObject); } // ultimate's early-clear registry



	// Update is called once per frame
	void Update () {
        if (TouchInput.IsPressed && !buttonClicks.playerDied)
            Step(Time.deltaTime, moveBackGround.speed, CameraFit.ViewTop, CameraFit.ViewBottom);
        else if (score.pauseCounter <= 0 && !buttonClicks.playerDied)
            Step(Time.deltaTime, moveBackGround.speed, CameraFit.ViewTop, CameraFit.ViewBottom);
    }

    // One frame of world movement (only called while the world moves).
    // Returns false once an atom has left the bottom and been destroyed.
    public bool Step(float dt, float worldSpeed, float viewTop, float viewBottom)
    {
        // AtomSpin is added right after Instantiate, so look on the first step.
        if (!atomChecked)
        {
            atomChecked = true;
            if (GetComponent<AtomSpin>() != null) wander = AtomWander.Roll();
        }
        if (wander == null)
        {
            transform.Translate(new Vector2(0, -1) * worldSpeed * dt * 30);
            return true;
        }

        transform.position = wander.Advance(transform.position, dt, worldSpeed, viewTop);
        if (!AtomWander.Gone(transform.position.y, viewBottom)) return true;
        if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
        return false;
    }

    // One frame at the current board speed (headless simulations).
    public bool Step(float dt)
    {
        return Step(dt, moveBackGround.speed, CameraFit.ViewTop, CameraFit.ViewBottom);
    }

    // Tests: give an atom a seeded wander before its first step.
    public void SetWander(AtomWander w)
    {
        wander = w;
        atomChecked = true;
    }

    public Rect SweptBounds(Vector2 center, Vector2 half, float from, float to)
    {
        return SpawnSpace.BodyRect(center, half);
    }

    public bool SelfSteering => false;
}
