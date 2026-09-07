using UnityEngine;
using System.Collections;

// Aurtur: Sina Serati
// This script allows the item/ object to move towards the player in a straight line, when there is a finger on the screen.
// if pauses are used up, they will freely move towards the player and current speed calcuated in moveBackGround script.

public class moveItemEnmInStrightLine : MonoBehaviour {


	
	// Update is called once per frame
	void Update () {
        if (TouchInput.IsPressed && !buttonClicks.playerDied)
            transform.Translate(new Vector2(0, -1) * moveBackGround.speed * Time.deltaTime * 30);
        else if (score.pauseCounter <= 0 && !buttonClicks.playerDied)
            transform.Translate(new Vector2(0, -1) * moveBackGround.speed * Time.deltaTime * 30);
    }
}
