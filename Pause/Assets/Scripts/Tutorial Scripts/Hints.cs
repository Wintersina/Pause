using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class Hints : MonoBehaviour {

    [Header("Pacing")]
    [Tooltip("Typing speed. Text used to advance one character per frame, so it " +
             "ran at whatever the display refresh happened to be.")]
    public float charsPerSecond = 45f;

    [Tooltip("Pause after a line finishes before it clears. Was hardcoded to 5s, " +
             "which is most of why the tutorial dragged.")]
    public float lineHoldSeconds = 1.8f;

    public static bool reachedTheEndOfTut;



    public GameObject playerIcon;
    private GameObject liftOffLeft;
    private GameObject ship5;
    private GameObject pausedIcon;
    public movePlayerInTut script;

    public Text startHintText;

    private float startHintTimer;
    private float killIconl;
    private float animationTimer;

    private float typeAccumulator;
    private int wordcount;
    private int dialougeCount;

    private string[] dialougeArray;

    // Use this for initialization
    void Start() {

        liftOffLeft = GameObject.Find("LiftOffLeft");
        ship5 = GameObject.Find("ship1");
        script = GameObject.Find("ship1").GetComponent<movePlayerInTut>();

        pausedIcon = GameObject.Find("paused");

        dialougeArray = new string[10];
        reachedTheEndOfTut = false;

        killIconl = 0f;
        playerIcon.gameObject.SetActive(false);


        wordcount = 0;
        startHintTimer = 1f;
        dialougeCount = 0;

        startHintText.gameObject.SetActive(true);
        startHintText.text = "";

        dialougeArray[0] = "Hello Commander. Welcome to Pause!";
        dialougeArray[1] = "Survive as long as you can. Collect STAR DUST and push your speed.";
        dialougeArray[2] = "In PAUSE, at anytime lift your finger to PAUSE the game.";
        dialougeArray[3] = "Here comes a red ATOM. Grabing it will increase your PAUSE counters by 2.";
        dialougeArray[4] = "Here comes a blue ATOM. Grabing it will give you a POWER UP.";
        dialougeArray[5] = "POWER UP is a temporary SHIELD, BOOST and INVINCIVILITY.";
        dialougeArray[6] = "Remember to Teleport by PAUSING and placing your finger anywhere on the screen.";
        dialougeArray[7] = "Collect STARS for STAR DUST. Spend it on new ships.";
        dialougeArray[8] = "Almost done. Next up: ALIENS and ASTEROIDS in the real thing.";
        dialougeArray[9] = "Lets Start the game... Good luck, Commander.";


    }

    // Update is called once per frame
    void Update() {

        animationTimer -= Time.deltaTime;
        // move only if there is a finger on the screen
        if (TouchInput.IsPressed)
        {
            startHintTimer -= Time.deltaTime;
            killIconl -= Time.deltaTime;
            startDialouge();
            reachedEnd();
        }
        else if (score.pauseCounter <= 0) {
            startHintTimer -= Time.deltaTime;
            killIconl -= Time.deltaTime;
            startDialouge();
            reachedEnd();
        }

    }


    // runns the dialouge
    void startDialouge()
    {
        // we will check a few things to be true for the dialouge to workd
        // first we make sure that each dialog only runs for max of 4 seconds
        // then we make sure that every letter of hte dialog is posted
        // lastly we will make sure that every dialouge from the dialoge array is lopped through
        // NOTE: the old condition read dialougeArray[dialougeCount] *before*
        // bounds-checking dialougeCount; it only survived because
        // reachedTheEndOfTut short-circuited first. Bounds check comes first now.
        if (!reachedTheEndOfTut && dialougeCount < dialougeArray.Length
            && startHintTimer <= 0 && wordcount < dialougeArray[dialougeCount].Length)
        {
            // we will turn on the player icon at the very bigenning
            if (dialougeCount == 0)
                playerIcon.gameObject.SetActive(true);

            typeAccumulator += charsPerSecond * Time.deltaTime;
            int burst = Mathf.FloorToInt(typeAccumulator);
            typeAccumulator -= burst;

            while (burst-- > 0 && wordcount < dialougeArray[dialougeCount].Length)
            {
                startHintText.text += dialougeArray[dialougeCount][wordcount];
                wordcount++;

                if (wordcount == dialougeArray[dialougeCount].Length)
                {
                    startHintTimer = lineHoldSeconds;
                    wordcount = 0;
                    typeAccumulator = 0f;
                    dialougeCount++;
                    onLineFinished();
                    break;
                }
            }
        }
        else if (startHintTimer <= .5)
        {
            startHintText.text = "";
        }
    }

    // cues that fire as each line lands
    void onLineFinished()
    {
        if (dialougeCount == 4)
        {
            spawnGoodStuffTut.redAtomDelayTimer = 1.75f;
        }
        else if (dialougeCount == 5)
        {
            spawnGoodStuffTut.atomDelayTimer = 1.75f;
        }
        else if (dialougeCount == 8)
        {
            spawnGoodStuffTut.midStarTimer = 2f;
            spawnGoodStuffTut.smStarTimer = 2f;
        }
        else if (dialougeCount == 10)
        {
            PlayerPrefs.SetString("HasDoneTut", "true");
            animationTimer = 7f;
            reachedTheEndOfTut = true;
            killIconl = 3.5f;
        }
    }

    // reached the end of tutorial
    void reachedEnd()
    {
        if (killIconl <= 0 && reachedTheEndOfTut) {

            script.enabled = false;
            ship5.transform.position = Vector3.MoveTowards(ship5.gameObject.transform.position, liftOffLeft.transform.position, .05f);


            if (animationTimer <= 0)
            {
                
                Destroy(pausedIcon);
                score.totalCurrency = 0;
                moveBackGround.speed = 0;
                startMenu.youAreInTutorial = false;
                tutButtonClicks.activeCanvis.gameObject.SetActive(true);
                playerIcon.gameObject.SetActive(false);
                //---------------Complete Tut ---------##19-----------
                achievementAPICalls.achievement_tutorial_completed();
                //-

            }


        }

    } 
}
