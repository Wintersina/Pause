using UnityEngine;
using UnityEngine.UI;
public class movePlayer : MonoBehaviour
{

    Vector3 fingerPos;
    Vector3 textPos;
    private Text startTimer;
    //private RectTransform atomTimerText;
    private RectTransform boostText;
    private RectTransform hypeText;
    float startTimerCounter, goTimer;
    private AudioSource goClip;
    private bool playoneshot;
    private bool teleported;

    void Start()
    {
        teleported = false;
        //shild timer;
        //atomTimerText = GameObject.Find("gotAtomText").GetComponent<RectTransform>();
        boostText = GameObject.Find("boostText").GetComponent<RectTransform>();
        hypeText = GameObject.Find("hypeText").GetComponent<RectTransform>();
        startTimer = GameObject.Find("goText").GetComponent<Text>();
        goClip = GameObject.Find("GOSound").GetComponent<AudioSource>();
        startTimerCounter = 2f;
        goTimer = 2f;
        playoneshot = true;

        startTimer.text = startTimerCounter.ToString("F2");
        

    }

    // Update is called once per frame
    void Update()
    {
        if (TouchInput.IsPressed)
        {

            // show start timer, give player 2 seconds to prep
            startTimerCounter -= Time.timeSinceLevelLoad;
            startTimer.fontSize += 3;

            if (startTimerCounter <= 0)
            {

                startTimer.text = "GO!!!!";
                if (playoneshot)
                {
                    goClip.Play();
                    playoneshot = false;
                }

                goTimer -= Time.timeSinceLevelLoad;
                //"GO!" end "GO" and start game
                if (goTimer <= 0)
                {
                    startTimer.gameObject.SetActive(false);

                }
                if (TouchInput.IsPressed)
                {
                    fingerPos = Camera.main.ScreenToWorldPoint(new Vector3(TouchInput.Position.x, TouchInput.Position.y, 0));
                    textPos = Camera.main.WorldToScreenPoint(transform.position);
                }
                else
                    teleported = true;
                moveLeft_Right(fingerPos);
            }
            else
            {
                startTimer.text = startTimerCounter.ToString("F2");
            }

        }
        else
            teleported = true;

    }

    // will move the player left and right baised on touch positions.
    void moveLeft_Right(Vector3 fingerPos)
    {
        //gets a position of the finger on the screen
        //checks position of finger is in bound box
        if (fingerPos.x <= 2.4 && fingerPos.x > -2.4 && fingerPos.y <= 3f)
        {

            this.transform.position = new Vector3(fingerPos.x, fingerPos.y + 1f);
            // Allow text to follow player----------------------------

            //atomTimerText.gameObject.SetActive(true);
            hypeText.gameObject.SetActive(true);
            boostText.gameObject.SetActive(true);
            //atomTimerText.transform.position = textPos + new Vector3(0, 70, 0);
            hypeText.transform.position = textPos + new Vector3(0, 75, 0);
            boostText.transform.position = textPos + new Vector3(0, -60, 0);

            //--------------------------------------------------------
        }
        else if (fingerPos.x > 2.4)  
        {
            if (fingerPos.y > 3f)
                this.transform.position = new Vector3(2.4f, 4.5f);
             else
                this.transform.position = new Vector3(2.4f, fingerPos.y + 1f);
        }
        else if (fingerPos.x < -2.4)
        { 
            if (fingerPos.y > 3f)
                this.transform.position = new Vector3(-2.4f, 4.5f);
            else
                this.transform.position = new Vector3(-2.4f, fingerPos.y + 1f);
        }
        else if(fingerPos.y > 3f)
        {
            this.transform.position = new Vector3(fingerPos.x, 4.5f);
        }
    }
}
