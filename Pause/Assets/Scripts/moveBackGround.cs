using UnityEngine;
using System.Collections;
using UnityEngine.UI;

public class moveBackGround : MonoBehaviour {

    public float startSpeed;
    public static float speed;
    private float offsetTimer;
    Vector2 offset;
     

    // Use this for initialization
    void Start () {
        offsetTimer = 0;
        speed = startSpeed;
        Screen.orientation = ScreenOrientation.Portrait;
    }
	
	// Update is called once per frame
	void Update () {
        // pauses when there is no touch on the touchscreen
        if (TouchInput.IsPressed && !buttonClicks.playerDied)
        {
            Time.timeScale = 1;
            offsetTimer = Time.timeSinceLevelLoad;
            moveBackground(offsetTimer);
            speedUp();
        }
        else if (score.pauseCounter <= 0 && !buttonClicks.playerDied)
        {
            Time.timeScale = 1;
            offsetTimer = Time.timeSinceLevelLoad;
            moveBackground(offsetTimer);
            speedUp();
        }
        else
        {
            Time.timeScale = 0;
        }



    }

    // this function moves background in the 'y' direction for illustion of player moving.
    void moveBackground(float ost)
    {
       offset = new Vector2(0, ost * speed);
       GetComponent<Renderer>().material.mainTextureOffset = offset;
    }
    // game speeds up as the time progresses. 
    void speedUp()
    {
        speed += .00005f;
    }
  
   
}
