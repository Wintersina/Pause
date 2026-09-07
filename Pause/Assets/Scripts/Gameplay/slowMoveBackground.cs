using UnityEngine;
using System.Collections;

public class slowMoveBackground : MonoBehaviour {

    public float startSpeed = .0001f;
    public static float speed;
    Vector2 offset;


    // Use this for initialization
    void Start()
    {

        speed = startSpeed;

        Screen.orientation = ScreenOrientation.Portrait;
    }

    // Update is called once per frame
    void Update()
    {
        // pauses when there is no touch on the touchscreen
        //if (TouchInput.IsPressed && !buttonClicks.playerDied)
        {
            moveBackground();
            
        }

    }

    // this function moves background in the 'y' direction for illustion of player moving.
    void moveBackground()
    {
        offset = new Vector2(0, Time.timeSinceLevelLoad * speed);
        //GetComponent<Renderer>().material.mainTextureOffset = offset;
        GetComponent<Renderer>().material.mainTextureOffset = offset;
    }
    // game speeds up as the time progresses. 
  

}
