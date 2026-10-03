using UnityEngine;

public class moveBackGround : MonoBehaviour {

    public float startSpeed;

    [Header("Difficulty ramp")]
    [Tooltip("Speed gained per second of active play. The original code added a " +
             "flat amount every frame, so the ramp ran twice as fast on a 120Hz " +
             "display as on the 60Hz phones this was tuned for.")]
    public float speedRampPerSecond = 0.00115f;

    [Tooltip("Ramp stops here. Enemy phases top out at 0.5, so this leaves a " +
             "little headroom past the final phase.")]
    public float maxSpeed = 0.58f;

    public static float speed;
    float offsetY;
    Material wallMaterial;

    void Start () {
        offsetY = 0f;
        speed = startSpeed;
        var r = GetComponent<Renderer>();
        wallMaterial = r != null ? r.material : null;
        Screen.orientation = ScreenOrientation.Portrait;
    }

    void Update () {
        if (ShipPowerController.CinematicClearActive)
        {
            Time.timeScale = ShipPowerController.CinematicTimeScale;
            return;
        }
        // pauses when there is no touch on the touchscreen
        if (TouchInput.IsPressed && !buttonClicks.playerDied)
        {
            Time.timeScale = ResumeSlowMo.Apply(1f);
            moveBackground();
            speedUp();
        }
        else if (score.pauseCounter <= 0 && !buttonClicks.playerDied)
        {
            Time.timeScale = ResumeSlowMo.Apply(1f);
            moveBackground();
            speedUp();
        }
        else
        {
            Time.timeScale = ResumeSlowMo.Freeze();
        }
    }

    // Scrolls the wall texture in 'y' for the illusion of the player moving.
    // The offset is integrated (speed x dt); it used to be time x speed, which
    // made the walls jump whenever speed changed and scroll faster than
    // `speed` the longer a run went on.
    void moveBackground()
    {
        offsetY = Mathf.Repeat(offsetY + speed * Time.deltaTime, 1f);
        if (wallMaterial != null) wallMaterial.mainTextureOffset = new Vector2(0f, offsetY);
    }

    // game speeds up as the time progresses. Both walls call this; SpeedRamp
    // ticks once per frame, so the ramp doesn't run once per wall.
    void speedUp()
    {
        SpeedRamp.Tick(speedRampPerSecond, maxSpeed);
    }
}
