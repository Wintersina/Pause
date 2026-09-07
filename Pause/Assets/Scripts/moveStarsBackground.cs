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

    private Vector2 offset;

    // Use this for initialization
    void Start()
    {
        speedCap = 1;
        replyB = GameObject.Find("replayWhenPausedButton").GetComponent<Button>();
        mainMenuB = GameObject.Find("mainMenuWhenPausedButton").GetComponent<Button>();
        pauseText = GameObject.Find("paused");
        replyB.gameObject.SetActive(false);
        mainMenuB.gameObject.SetActive(false);

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
            replyB.gameObject.SetActive(true);
            mainMenuB.gameObject.SetActive(true);
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
    void showPaused(bool show)
    {
        if (!buttonClicks.playerDied)
        {
            pauseText.gameObject.SetActive(show);
        }
        if (!show)
        {
            if (delayPauseMenuTimer <= 0)
            {
                if (!buttonClicks.playerDied)
                {

                    pauseMenuTimer = .35f;
                    replyB.gameObject.SetActive(show);
                    mainMenuB.gameObject.SetActive(show);
                    delayPauseMenuTimer = pauseMenuTimer;
                }
            }
        }
        else
        {
            if (!buttonClicks.playerDied)
            {
                replyB.gameObject.SetActive(show);
                mainMenuB.gameObject.SetActive(show);

            }
        }
    }

}
