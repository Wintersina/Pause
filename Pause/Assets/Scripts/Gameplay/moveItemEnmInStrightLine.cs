using UnityEngine;
using System.Collections;

// Aurtur: Sina Serati
// This script allows the item/ object to move towards the player in a straight line, when there is a finger on the screen.
// if pauses are used up, they will freely move towards the player and current speed calcuated in moveBackGround script.
//
// As a SpawnSpace pattern it is the plain scroller: its sweep is its body.

public class moveItemEnmInStrightLine : MonoBehaviour, IMovementFootprint {

    void Awake() { ClearTarget.Ensure(gameObject); } // ultimate's early-clear registry

	// Update is called once per frame
	void Update () {
        if (TouchInput.IsPressed && !buttonClicks.playerDied)
            Step(Time.deltaTime);
        else if (score.pauseCounter <= 0 && !buttonClicks.playerDied)
            Step(Time.deltaTime);
    }

    // One frame of flight (dt explicit for headless simulations).
    public void Step(float dt)
    {
        transform.Translate(new Vector2(0, -1) * moveBackGround.speed * dt * 30);
    }

    public Rect SweptBounds(Vector2 center, Vector2 half, float from, float to)
    {
        return SpawnSpace.BodyRect(center, half);
    }

    public bool SelfSteering => false;
}
