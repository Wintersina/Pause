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
        transform.Translate(new Vector2(0, -1) * moveBackGround.speed * Time.deltaTime * itemSpeed);
        if (transform.position.x <= 2.4 && transform.position.x >= -2.4 && alreadyMoved)
        {
            transform.position = new Vector3(Mathf.PingPong(Time.time, randPos), transform.position.y, transform.position.z);
        }
 

    }
}
