using UnityEngine;
using System.Collections;

public class slowMoveBackground : MonoBehaviour {

    public float startSpeed = .0001f;
    public static float speed;
    Vector2 offset;
    Material material;


    // Use this for initialization
    void Start()
    {

        speed = startSpeed;
        var r = GetComponent<Renderer>();
        material = r != null ? r.material : null;

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
        // Constant speed, so time x speed is exact here; only the per-frame
        // GetComponent/material lookup was wasteful.
        offset.y = Time.timeSinceLevelLoad * speed;
        if (material != null) material.mainTextureOffset = offset;
    }
    // game speeds up as the time progresses. 
  

}
