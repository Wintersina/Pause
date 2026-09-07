using UnityEngine;
using System.Collections;
using UnityEngine.UI;

public class moveStarsBackground : MonoBehaviour {

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
    }

    // Update is called once per frame
    void Update()
    {
        // These scene objects are templates for PauseQuickActions. Keeping
        // them hidden avoids duplicate controls and, crucially, leaves the
        // tappable copies above the death popup's blocking panel.
        if (replyB != null) replyB.gameObject.SetActive(false);
        if (mainMenuB != null) mainMenuB.gameObject.SetActive(false);

        if (buttonClicks.playerDied)
        {
            starBackgroundSpeed = .005f;
        }
        // pauses when there is no touch on the touchscreen
        if (TouchInput.IsPressed && !buttonClicks.playerDied)
        {
            showPaused(false);
            moveBackground();
        }
        else if (score.pauseCounter <= 0 && !buttonClicks.playerDied)
        {
            showPaused(false);
            moveBackground();
        }
        else
        {
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

    // Shows or hides the paused glow icon only. Replay/menu are handled
    // entirely in Update() now -- they are a death-only concern, not a
    // pause-display one, and coupling them here was what made them appear
    // on every ordinary pause and out-of-pauses stretch, not just death.
    void showPaused(bool show)
    {
        if (buttonClicks.playerDied || pauseText == null) return;

        // Roll a new glow only on the hidden -> shown transition, not every
        // frame the finger stays lifted.
        if (show && !wasShowingPause) PickRandomPauseGlow();
        wasShowingPause = show;

        pauseText.gameObject.SetActive(show);
    }

}
