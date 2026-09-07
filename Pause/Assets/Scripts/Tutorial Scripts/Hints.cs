using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class Hints : MonoBehaviour {

    public static bool reachedTheEndOfTut;



    public GameObject playerIcon;
    private GameObject liftOffLeft;
    private GameObject ship5;
    private GameObject pausedIcon;
    public movePlayerInTut script;

    public Text startHintText;

    private float startHintTimer;
    private float delayHintTimer;
    private float killIconl;
    private float animationTimer;

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
        dialougeArray[1] = "Your goal is to survive as long as possible, to accumulate STAR DUST, and reach the highest speed you can.";
        dialougeArray[2] = "In PAUSE, at anytime lift your finger to PAUSE the game.";
        dialougeArray[3] = "Here comes a red ATOM. Grabing it will increase your PAUSE counters by 2.";
        dialougeArray[4] = "Here comes a blue ATOM. Grabing it will give you a POWER UP.";
        dialougeArray[5] = "POWER UP is a temporary SHIELD, BOOST and INVINCIVILITY.";
        dialougeArray[6] = "Remember to Teleport by PAUSING and placing your finger anywhere on the screen.";
        dialougeArray[7] = "Here are some STARS. Collect them for STARDUST! You Can use STARDUST to buy other Ships.";
        dialougeArray[8] = "The Tutorial will end soon and you will be able to kill and dodge some ALIENS and ASTEROIDS in the main game.";
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
        if (!reachedTheEndOfTut && startHintTimer <= 0 && wordcount < dialougeArray[dialougeCount].Length && dialougeCount < dialougeArray.Length)
        {
            // we will turn on the player icon at the very bigenning
            if (dialougeCount == 0)
                playerIcon.gameObject.SetActive(true);
            // load up the text box with the first string
            startHintText.text += dialougeArray[dialougeCount][wordcount].ToString();
            wordcount++;
            delayHintTimer = 5f;
            if (wordcount == dialougeArray[dialougeCount].Length)
            {

                startHintTimer = delayHintTimer;
                wordcount = 0;
                dialougeCount++;

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
        }
        else if (startHintTimer <= .5)
        {
            startHintText.text = "";
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
