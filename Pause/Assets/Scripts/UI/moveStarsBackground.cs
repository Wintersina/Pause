using UnityEngine;
using System.Collections;
using UnityEngine.UI;

public class moveStarsBackground : MonoBehaviour {

    private float delayPauseMenuTimer;
    private float pauseMenuTimer;
    private float starBackgroundSpeed;
    //public Animator player;
    public GameObject pauseText;
    public Button replyB;
    public Button mainMenuB;
    private float speedCap;

    // "Paused" used to be a fixed "PAUSED" wordmark sprite; now a randomly
    // picked one of two red-glow variants, re-rolled once per pause rather
    // than every frame it stays visible.
    private Sprite[] pauseGlowSprites;
    private SpriteRenderer pauseRenderer;
    private bool wasShowingPause;

    private Vector2 offset;

    // Use this for initialization
    void Start()
    {
        speedCap = 1;
        // A null from any of these used to throw and abort Start(), which took
        // the whole pause menu down with it.
        GameObject replayGo = SceneUtil.FindAny("replayWhenPausedButton");
        GameObject menuGo = SceneUtil.FindAny("mainMenuWhenPausedButton");
        replyB = replayGo != null ? replayGo.GetComponent<Button>() : null;
        mainMenuB = menuGo != null ? menuGo.GetComponent<Button>() : null;
        pauseText = SceneUtil.FindAny("paused");
        pauseRenderer = pauseText != null ? pauseText.GetComponent<SpriteRenderer>() : null;
        pauseGlowSprites = Resources.LoadAll<Sprite>("PauseGlow");

        if (replyB != null) replyB.gameObject.SetActive(false);
        if (mainMenuB != null) mainMenuB.gameObject.SetActive(false);

        starBackgroundSpeed = .005f;
        pauseMenuTimer = 0f;
        delayPauseMenuTimer = .5f;

    }

    // Update is called once per frame
    void Update()
    {
        if (buttonClicks.playerDied)
        {
            starBackgroundSpeed = .005f;

            // Replay and menu were only revealed when the player ran *out of
            // pauses*, so dying with pauses left showed no way to restart.
            if (replyB != null) replyB.gameObject.SetActive(true);
            if (mainMenuB != null) mainMenuB.gameObject.SetActive(true);
        }
        // pauses when there is no touch on the touchscreen
        if (TouchInput.IsPressed && !buttonClicks.playerDied)

        {
            delayPauseMenuTimer -= Time.deltaTime;
            showPaused(false);
            moveBackground();
        }// show replayand menu button when player runs out of pauses
        else if (score.pauseCounter <= 0 && !buttonClicks.playerDied)
        {
            delayPauseMenuTimer -= Time.deltaTime;
            showPaused(false);
            if (replyB != null) replyB.gameObject.SetActive(true);
            if (mainMenuB != null) mainMenuB.gameObject.SetActive(true);
            moveBackground();
        }
        else {
            
            showPaused(true);
        }

    }

    // this function moves background in the 'y' direction for illustion of player moving.
    void moveBackground()
    {
        if(moveBackGround.speed >= .04f)
        {
            speedCap = .04f;
        }
        else
        {
            speedCap = moveBackGround.speed;
        }
        offset = new Vector2(0, Time.timeSinceLevelLoad *  starBackgroundSpeed * (speedCap+1) );
        GetComponent<Renderer>().material.mainTextureOffset = offset;

    }

    // show pause or not
    void PickRandomPauseGlow()
    {
        if (pauseRenderer == null || pauseGlowSprites == null || pauseGlowSprites.Length == 0) return;
        pauseRenderer.sprite = pauseGlowSprites[Random.Range(0, pauseGlowSprites.Length)];
    }

    void showPaused(bool show)
    {
        if (!buttonClicks.playerDied && pauseText != null)
        {
            // Roll a new glow only on the hidden -> shown transition, not
            // every frame the finger stays lifted.
            if (show && !wasShowingPause) PickRandomPauseGlow();
            wasShowingPause = show;

            pauseText.gameObject.SetActive(show);
        }
        if (!show)
        {
            if (delayPauseMenuTimer <= 0)
            {
                if (!buttonClicks.playerDied)
                {

                    pauseMenuTimer = .35f;
                    if (replyB != null) replyB.gameObject.SetActive(show);
                    if (mainMenuB != null) mainMenuB.gameObject.SetActive(show);
                    delayPauseMenuTimer = pauseMenuTimer;
                }
            }
        }
        else
        {
            if (!buttonClicks.playerDied)
            {
                if (replyB != null) replyB.gameObject.SetActive(show);
                if (mainMenuB != null) mainMenuB.gameObject.SetActive(show);
            }
        }
    }

}
