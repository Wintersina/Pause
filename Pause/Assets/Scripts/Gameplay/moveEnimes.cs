using UnityEngine;
using System.Collections;

public class moveEnimes : MonoBehaviour {

    private float itemSpeed;
    private float randPos;
    private bool alreadyMoved;

    // Use this for initialization
    void Start () {
        itemSpeed = 30;
        alreadyMoved = true;
        randPos = Random.Range(-1.15f, 2.45f);

   

  


    }
	
	// Update is called once per frame
	void Update () {

            if (TouchInput.IsPressed && !buttonClicks.playerDied)
            moveEnim();
        else if (score.pauseCounter <= 0 && !buttonClicks.playerDied)
            moveEnim();

        

    }
    void moveEnim()
    {
        // Translate() defaults to local space. That was harmless while
        // nothing ever rotated this transform, but AsteroidSpin now does --
        // and a local-space "down" rotates right along with the object, so a
        // spinning asteroid's actual travel direction swings away from
        // straight down and can point back up the screen for part of its
        // spin, reading as moving backwards. World space keeps travel tied
        // to the screen, independent of whatever the sprite is doing.
        transform.Translate(new Vector2(0, -1) * moveBackGround.speed * Time.deltaTime * itemSpeed, Space.World);
        if (transform.position.x <= 2.4 && transform.position.x >= -2.4 && alreadyMoved)
        {
            transform.position = new Vector3(Mathf.PingPong(Time.time, randPos), transform.position.y, transform.position.z);
        }
 

    }
}
