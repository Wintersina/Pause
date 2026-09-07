using UnityEngine;

public class moveBackGround : MonoBehaviour {

    public float startSpeed;

    [Header("Difficulty ramp")]
    [Tooltip("Speed gained per second of active play. The original code added a " +
             "flat amount every frame, so the ramp ran twice as fast on a 120Hz " +
             "display as on the 60Hz phones this was tuned for.")]
    public float speedRampPerSecond = 0.002f;

    [Tooltip("Ramp stops here. Enemy phases top out at 0.5, so this leaves a " +
             "little headroom past the final phase.")]
    public float maxSpeed = 0.6f;

    public static float speed;
    private float offsetTimer;
    Vector2 offset;

    void Start () {
        offsetTimer = 0;
        speed = startSpeed;
        Screen.orientation = ScreenOrientation.Portrait;
    }

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
        if (speed >= maxSpeed) return;
        speed = Mathf.Min(speed + speedRampPerSecond * Time.deltaTime, maxSpeed);
    }
}
