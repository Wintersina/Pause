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
    Renderer wallRenderer;

    // The rail art rides the BOARD's scroll: `speed` x BoardScroll world
    // units a second, the rate every hazard, pickup and rail mine falls at
    // (SpawnSpace.ScrollSpeed). It used to advance the texture offset by
    // `speed` tiles a second whatever a tile measured -- about 5.2 u
    // (Frost, Verdant) or 6.8 u (Space, Ember) with the wide rails, 14 u
    // before them -- so a mine clamped to a rail slid along the rail's own
    // art. One switch back: RailRidesBoard = false.
    //
    // The scroll itself is BoardRoll: one distance for the rail art and the
    // rail mines, drawn on whole screen pixels (see BoardRoll).
    public const float BoardScroll = BoardRoll.BoardScroll;
    public static bool RailRidesBoard
    {
        get { return !BoardRoll.LegacyTileRate; }
        set { BoardRoll.LegacyTileRate = !value; }
    }

    // Texture tiles a second the wall scrolls at `speed`, for a wall whose
    // art repeats `tiles` times over `worldHeight` units.
    public static float RailTilesPerSecond(float speed, float tiles, float worldHeight)
    {
        if (!RailRidesBoard || worldHeight <= 0f || tiles <= 0f) return speed;
        return speed * BoardScroll * BoardRoll.RailRate * tiles / worldHeight;
    }

    // Draws this wall's art where the board is (BoardRoll.RailOffset).
    public static void ApplyRoll(Material wall, Renderer renderer)
    {
        if (wall == null || renderer == null) return;
        float offset = BoardRoll.RailOffset(Mathf.Abs(wall.mainTextureScale.y), renderer.bounds.size.y);
        wall.mainTextureOffset = new Vector2(0f, offset);
    }

    void Start () {
        offsetY = 0f;
        // gameS1 (a WorldManager is up): the equipped ship and colour's
        // start speed (ShipStartSpeed). The tutorial keeps its own.
        speed = WorldManager.Instance != null ? WorldManager.RunStartSpeed(startSpeed) : startSpeed;
        var r = GetComponent<Renderer>();
        wallRenderer = r;
        wallMaterial = r != null ? r.material : null;
        Screen.orientation = ScreenOrientation.Portrait;
    }

    void Update () {
        // The boss intro freezes the world even with a finger down; it is
        // scripted, so it never counts as a pause (BossEncounter).
        if (BossEncounter.ScriptedFreeze)
        {
            Time.timeScale = ResumeSlowMo.Freeze();
            return;
        }
        if (ShipPowerController.CinematicClearActive)
        {
            Time.timeScale = ShipPowerController.CinematicTimeScale;
            // the board keeps rolling in the ultimate's slow motion (the rail
            // lanes and their mines do): the rail art rolls with it
            moveBackground();
            return;
        }
        // pauses when there is no touch on the touchscreen
        if (TouchInput.IsPressed && !buttonClicks.playerDied)
        {
            Time.timeScale = ResumeSlowMo.Apply(1f) * WorldTimeFx.Scale; // hit-stop, Time Bubble
            moveBackground();
            if (!BossEncounter.SpeedLocked && !WorldEntry.Active) speedUp(); // a boss holds speed at 20; the entry waits
        }
        else if (score.pauseCounter <= 0 && !buttonClicks.playerDied)
        {
            Time.timeScale = ResumeSlowMo.Apply(1f) * WorldTimeFx.Scale; // hit-stop, Time Bubble
            moveBackground();
            if (!BossEncounter.SpeedLocked && !WorldEntry.Active) speedUp(); // a boss holds speed at 20; the entry waits
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
        // both walls, and every rail lane, share the one roll (frame-guarded)
        BoardRoll.Advance(speed, Time.deltaTime);
        ApplyRoll(wallMaterial, wallRenderer);
        if (wallMaterial != null) offsetY = wallMaterial.mainTextureOffset.y;
    }

    // game speeds up as the time progresses. Both walls call this; SpeedRamp
    // ticks once per frame, so the ramp doesn't run once per wall.
    void speedUp()
    {
        SpeedRamp.Tick(speedRampPerSecond, maxSpeed);
    }
}
