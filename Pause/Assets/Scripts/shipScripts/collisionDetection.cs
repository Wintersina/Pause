using UnityEngine;
using UnityEngine.UI;


public class collisionDetection : MonoBehaviour {

    public static bool atomCheck;


    private static float savedTimer;
    public static float invTimer;
    private float boostTimer;
    public Text atomTimerText;
    public Text hypeText;
    public Text boostText;
    private int atomCounter;
    public static int lifeCounter;
    public static int MAXLIFE;
    //private string[] savedString = new string[12];

    public GameObject shield;
    public GameObject explosionAnimation;
    public GameObject blueExp;
    public GameObject redExp;
    public GameObject boost;
    // Use this for initialization

    public AudioSource boostSound, astroidExpSound;

    [Header("Star dust payouts")]
    [Tooltip("Pickups are where nearly all star dust comes from -- the passive " +
             "trickle in score.cs is under 1% of income. Tune the economy here.")]
    public float smallStarValue = 0.5f;
    public float largeStarValue = 1f;
    public float blueAtomValue = 2f;

    // Tutorial earnings are tracked separately so a practice run cannot be
    // farmed for real currency.
    void awardDust(float amount)
    {
        if (PlayerPrefs.GetString("HasDoneTut") == "true")
            score.totalCurrency += amount;
        else
            score.tutorialCurrency += amount;
    }

	void Start () {

        MAXLIFE = 3;
        // Fills the needed componets for this player.
        atomTimerText = GameObject.Find("gotAtomText").GetComponent<Text>();
        hypeText = GameObject.Find("hypeText").GetComponent<Text>();
        boostText = GameObject.Find("boostText").GetComponent<Text>();
        //destructionComboText = GameObject.Find("DestructionText").GetComponent<Text>();
        boost = GameObject.FindGameObjectWithTag("boost");
        boost.gameObject.SetActive(false);
        boostSound = GameObject.Find("RocketsSound").GetComponent<AudioSource>();
        astroidExpSound = GameObject.Find("AstroidExplotionSound").GetComponent<AudioSource>();


        //destructionComboText.gameObject.SetActive(false);
    

        //source.clip = boostSound;

        // making sure atom is not active until player picks it up
        atomCheck = false;

        // empty out any text or counters
        atomTimerText.text = "";
        atomCounter = 0;
        lifeCounter = 0;

        // create an array of  string for  hyped words
        /*
        savedString[0] = "POOF!";
        savedString[1] = "DANG!";
        savedString[2] = "BOOM!!";
        savedString[3] = "MAYHEM!";
        savedString[4] = "DESTROYER!";
        savedString[5] = "DOMINATION!";
        savedString[6] = "SAVAGE!";
        savedString[7] = "ANNIHILATOR!";
        savedString[8] = "DISPOSER!";
        savedString[9] = "HOLYYY!!";
        savedString[10] = "EXTERMINATOR!";
        // killing it
        // How??
        */
    }
	
	// Update is called once per frame
	void Update () {
        if (TouchInput.IsPressed)
        {
            turnTextsOff();
        }
        else if (score.pauseCounter <= 0 && !buttonClicks.playerDied)
        {
            turnTextsOff();
        }
    }

    // this function stops the background music by lowering its vol and raising the boost music by increasing its vol
    
    void OnTriggerEnter2D(Collider2D hit)
    {
        
        #region
        //------------------------- Colliding with Enimies ---------------------------------------------
        if (hit.gameObject.tag == "Enimey" || hit.gameObject.tag == "Astr" )
        {

            // creating different explotions for different enims
            if (hit.gameObject.name == "rail3(Clone)")
            {
                GameObject BlueExp = Instantiate(blueExp, hit.gameObject.transform.position, hit.gameObject.transform.rotation) as GameObject;
                Destroy(BlueExp, 2);
            }
            else if (hit.gameObject.name == "mine(Clone)")
            {
                GameObject RedExp = Instantiate(redExp, hit.gameObject.transform.position, hit.gameObject.transform.rotation) as GameObject;
                Destroy(RedExp, 2);
            }

            if (atomCheck)
            {
                // acchivment reporting
                if (hit.gameObject.name == "alien1(Clone)")
                {
                    //------------------------- Kill 5 Alieans ------------##08-------------------
                    achievementAPICalls.achievement_aliens();
                    //------------------------- Kill 25 Alieans ---------------##09----------------
                    achievementAPICalls.achievement_aliens_2();
                    //------------------------- Kill 50 Alieans -------------------##10------------
                    achievementAPICalls.achievement_aliens_3();
                    //------------------------- Kill 150 Alieans -----------------##11--------------
                    achievementAPICalls.achievement_aliens_4();
                    //------------------------- Kill 1000 Alieans --------------------##12-----------
                    achievementAPICalls.achievement_aliens_5();
                    //------------------------- Kill 3500 Alieans ------------------------##13-------
                    achievementAPICalls.achievement_aliens_6();
                }if(hit.gameObject.tag == "Astr")
                {
                    astroidExpSound.Play();
                    //------------------------- Destroy 5 Astroid ------------##14-------------------
                    achievementAPICalls.achievement_destroyer();
                    //------------------------- Destroy 25 Astroid------------##15-------------------
                    achievementAPICalls.achievement_destroyer_2();
                    //------------------------- Destroy 50 Astroid ------------##16-------------------
                    achievementAPICalls.achievement_destroyer_3();
                    //------------------------- Destroy 100 Astroid ------------##17-------------------
                    achievementAPICalls.achievement_destroyer_4();
                    //------------------------- Destroy 1500 Astroid ------------##18-------------------
                    achievementAPICalls.achievement_destroyer_5();

                }
                // show the texts for only half of a second.
                savedTimer = .4f;


                //show random texts as user hits and destroyes obsticals

                //hypeText.text = savedString[Random.Range(0,10)];

                // create explotion and show it on the objets position.
                GameObject exp = Instantiate(explosionAnimation) as GameObject;
                exp.transform.position = hit.gameObject.transform.position;

                Destroy(exp, 2);
                Destroy(hit.gameObject);
                
                
                
            
                // open memory and remove leftovers
            }
            else {
                lifeCounter += 1;
                //change sprite



                Vector3 shipPos = this.gameObject.transform.position;
                Quaternion shipRot = this.gameObject.transform.rotation;

                // kill the player
                GameObject exp = Instantiate(explosionAnimation, shipPos, shipRot) as GameObject;


                //exp.transform.position = hit.gameObject.transform.position;
                if (lifeCounter >= MAXLIFE)
                {
                    buttonClicks.playerDied = true;
                    //--------------------FIRST DEATH ACHIVEMENT-------##01----------------------------------------
                    achievementAPICalls.achievement_first_death();
                    //-------------------------------------------------------------------------------------------
                    //--------------------5th DEATH ACHIVEMENT-------##02----------------------------------------
                    achievementAPICalls.achievement_death_2();
                    //--------------------------------------------------------------------------------------------
                    //--------------------10th DEATH ACHIVEMENT-------##03----------------------------------------
                    achievementAPICalls.achievement_death_3();
                    //--------------------------------------------------------------------------------------------
                    //--------------------50th DEATH ACHIVEMENT-------##04----------------------------------------
                    achievementAPICalls.achievement_death_4();
                    //--------------------100th DEATH ACHIVMENT---------------------------------------------------
                    achievementAPICalls.achievement_death_5();

                    achievementAPICalls.leaderboard_highest_speed_reached(Mathf.Round(moveBackGround.speed * 100));
                    Destroy(gameObject);
                }
                Destroy(hit.gameObject);
                Destroy(exp, 2);

            }
        }
        #endregion
        #region
        //-------------------- PICK UP ITEMS, Such as STARS, and ATOMS ------------------------------------------
        else if (hit.gameObject.tag == "pickUp")
        {

            if (hit.gameObject.name == "smStar 1(Clone)" || hit.gameObject.name == "LargeStar 1(Clone)")
            {
                //--------------------PicUp Stars 150-------##06----------------------------------------
                achievementAPICalls.achievement_stars();
                //--------------------PickUp Stats 1000-------##07----------------------------------------
                achievementAPICalls.achievement_stars_2();
            }

                // calculate different scores for each items.
                if (hit.gameObject.name == "smStar 1(Clone)")
            {
                awardDust(smallStarValue);
                Destroy(hit.gameObject);
            }
            else if(hit.gameObject.name == "LargeStar 1(Clone)")
            {
                awardDust(largeStarValue);
                Destroy(hit.gameObject);
            }
            else if (hit.gameObject.name == "pauseAtom(Clone)")
            {
                score.incromentPause(); 
                Destroy(hit.gameObject);
            }
            else if(hit.gameObject.name == "atom3a(Clone)")
            {
                boostSound.Play();
                // ---------------------------
                //   Music control section!
                musicControl.boostMusicChanger = true;
                // ----------------------------

                shield.SetActive(true);
                awardDust(blueAtomValue);
                atomTimerText.text = "0.00";
                boostText.text = "Boost!";
                Destroy(hit.gameObject);

                // turn off inv text after  timer runs out.
                atomCheck = true;
                boost.SetActive(true);
                moveBackGround.speed += .05f;
                invTimer = 5.8f;
                boostTimer = 1f;
                atomCounter++;
            }
            #endregion
        }
        //------------------------------------------------------------------------------------------------------------------
    }
    // simply turns of the texts
    void turnTextsOff()
    {
        invTimer -= Time.deltaTime;
        savedTimer -= Time.deltaTime;
        boostTimer -= Time.deltaTime;
        if (atomCheck)
        {
            atomTimerText.text = invTimer.ToString("F2");     
           
        }

        // check if atom is captrured and its time to reduce it.
        if (atomCheck && invTimer <= 0)
        {
            // turn shields off
            shield.SetActive(false);
            boost.SetActive(false);
            // let player know shild is off
            atomTimerText.text = "";
            atomCheck = false;
            moveBackGround.speed -= .05f * atomCounter;
            atomCounter = 0;

            // ---------------------------
            //   Music control section!
            musicControl.boostMusicChanger = false;
            //----------------------------
        }
        else if( savedTimer <=0)
        {
            hypeText.text = "";
        }
        if (boostTimer <= 0)
        {
            boostText.text = "";
        }
    }
}
