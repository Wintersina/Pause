using UnityEngine;
using System.Collections;

/*
    This script spawns enimies at different intervals on to the board. Each enimey is randomly generated. 

 */


public class enmiesOnBoard : MonoBehaviour {

    public GameObject[] astroid1 = new GameObject[5];
    public GameObject[] astroid2 = new GameObject[5];
    public GameObject[] astroid3 = new GameObject[5];
    public GameObject[] astroid4 = new GameObject[5];
    public GameObject[] astroid5 = new GameObject[5];
    public GameObject alien1;
    public GameObject rails;

    private float railDelayTimer;
    private float railTimer;

    private float smEnmDelayTimer;
    private float bigEnmDelayTimer;
    private float slowSmlEnmTimerBy;
    private float slowBigEnmTimerBy;
    private float slowSmlAstroidTimerBy;
    private float slowMidAstroidTimerBy;
    private float slowBigAstroidTimerBy;
    private float smallAstroidDelayTimer;
    private float midAstroidDelayTimer;
    private float bigAstroidDelayTimer;
    private float spawnAnimatedEnimeOneTimer;
    private float spawnAnimatedEnimeOneDelayTimer;

    private int astroidSelector; // level of the game
   

    // Use this for initialization
    void Start () {

        railTimer = 30;
        railDelayTimer = 30;
        astroidSelector = 0;
        bigEnmDelayTimer = 3f;
        smEnmDelayTimer = 8f;
        midAstroidDelayTimer = 13f;
        smallAstroidDelayTimer = 18f;
        bigAstroidDelayTimer = 21;
        spawnAnimatedEnimeOneDelayTimer = 3f;

    }
	
	// Update is called once per frame
	void Update () {
        // Spawn call
        // astroid selctor will change baised on the players speed. Therefore, spawning correct astroid
        if (moveBackGround.speed < .2)
        {
            astroidSelector = 0;
        }
        else if (moveBackGround.speed < .3)
        {
            astroidSelector = 1;
        }
        else if (moveBackGround.speed < .4)
        {
            astroidSelector = 2;
        }
        else if (moveBackGround.speed < .5)
        {
            astroidSelector = 3;
        }
        else astroidSelector = 4;

        if (TouchInput.IsPressed && !buttonClicks.playerDied)
        {
            spawn();
        }
        else if (score.pauseCounter <= 0 && !buttonClicks.playerDied)
        {
            spawn();
        }
    }
    // Spawns enimies and things the player will have collition with
    // dedecated spawn function
    void spawn()
    {
        railDelayTimer -= Time.deltaTime;
        railTimer -= Time.deltaTime;
        smEnmDelayTimer -= Time.deltaTime;
        bigEnmDelayTimer -= Time.deltaTime;
        smallAstroidDelayTimer -= Time.deltaTime;
        midAstroidDelayTimer -= Time.deltaTime;
        bigAstroidDelayTimer -= Time.deltaTime;
        spawnAnimatedEnimeOneDelayTimer -= Time.deltaTime;

        if(railDelayTimer <= 0)
        {
            railTimer = (moveBackGround.speed < .35) ? Random.Range(.5f,.8f) : Random.Range(.5f, 1f);
            // change the spawn timer everytime
            //slowSmlEnmTimerBy = Random.Range(2.5f, 3.5f);
            spawnRails();
            railDelayTimer = railTimer;
        }


        if (smEnmDelayTimer <= 0)
        {
            slowSmlEnmTimerBy = (moveBackGround.speed < .35) ? Random.Range(1f, 5f) : Random.Range(.5f, 1f);
            // change the spawn timer everytime
            //slowSmlEnmTimerBy = Random.Range(2.5f, 3.5f);
            spawnAstroid2();
            smEnmDelayTimer = slowSmlEnmTimerBy;
        }
        if (bigEnmDelayTimer <= 0)
        {
            // change the spawn timer everytime
            slowBigEnmTimerBy = (moveBackGround.speed < .35) ? Random.Range(1f, 5f) : Random.Range(.5f, 1f);
            spawnAstroid1();
            bigEnmDelayTimer = slowBigEnmTimerBy;
        }
        if(smallAstroidDelayTimer <= 0)
        {
            // change the spawn timer everytime
            slowSmlAstroidTimerBy = (moveBackGround.speed < .35) ? Random.Range(1f, 5f) : Random.Range(.5f, 1f);
            spawnSmallAstroid();
            smallAstroidDelayTimer = slowSmlAstroidTimerBy;
        }
        if (midAstroidDelayTimer <= 0)
        {
            // change the spawn timer everytime
            slowMidAstroidTimerBy = (moveBackGround.speed < .35) ? Random.Range(1f, 5f) : Random.Range(.5f, 1f);
            spawnMidAstroid();
            midAstroidDelayTimer = slowMidAstroidTimerBy;
        }
        if (bigAstroidDelayTimer <= 0)
        {
            // change the spawn timer everytime
            slowBigAstroidTimerBy = (moveBackGround.speed < .35) ? Random.Range(1f, 5f) : Random.Range(.5f, 1f);
            spawnLargeAstroid();
            bigAstroidDelayTimer = slowBigAstroidTimerBy;
        }
        if(spawnAnimatedEnimeOneDelayTimer <= 0)
        {
            // change the spawn timer everytime
            spawnAnimatedEnimeOneTimer = (moveBackGround.speed < .2) ? Random.Range(1f, 2f) : Random.Range(2f, 3f);
            spawnAnimatedEnimeOne();
            spawnAnimatedEnimeOneDelayTimer = spawnAnimatedEnimeOneTimer;
        }

    }
    // spawns small enimes through the board
    void spawnAstroid2()
    {
        Vector3 randomEnmPosition = new Vector3(Random.Range(-2.2f, 2.4f), transform.position.y, transform.rotation.z);
        // spawn 3 enimies at the same time
        Instantiate(astroid2[astroidSelector], randomEnmPosition, transform.rotation);

        //distroy only if touch is on the screen
        //Destroy(enermy, 60);

    }
    // this spawn larg enimes though the board
    void spawnAstroid1()
    {
        Vector3 randomEnmPosition = new Vector3(Random.Range(-2.2f, 2.4f), transform.position.y, transform.rotation.z);

        Instantiate(astroid1[astroidSelector], randomEnmPosition, transform.rotation);     

    }
    // will create a line of animated enimies that the player is able to doge through
    void spawnAnimatedEnimeOne()
    {
        Vector3 randomEnmPosition = new Vector3(Random.Range(-2.3f, 2f), transform.position.y, transform.rotation.z);
        int max = Random.Range(1, 5);
        // spawn 3 enimies at the same time
        for (int i = 0; i < max; i++)
        {
            Vector3 newPositionForAnimatedAliean= new Vector3(randomEnmPosition.x + (i+.5f), randomEnmPosition.y, randomEnmPosition.z);
            if (newPositionForAnimatedAliean.x >= -2.4 && newPositionForAnimatedAliean.x <= 2.2)
                Instantiate(alien1, newPositionForAnimatedAliean, transform.rotation);
        }
    }
    // Next 3 functions spawn 3 different types of astroids.
    void spawnSmallAstroid()
    {
        Vector3 randomEnmPosition = new Vector3(Random.Range(-2.3f, 2.3f), transform.position.y, transform.rotation.z);
        // spawn 3 enimies at the same time
        Instantiate(astroid3[astroidSelector], randomEnmPosition, transform.rotation);
        
    }
    void spawnMidAstroid()
    {
        Vector3 randomEnmPosition = new Vector3(Random.Range(-2.3f, 2f), transform.position.y, transform.rotation.z);

        // spawn 3 enimies at the same time
             Instantiate(astroid4[astroidSelector], randomEnmPosition, transform.rotation);
                
    }
    void spawnLargeAstroid()
    {
        Vector3 randomEnmPosition    = new Vector3(Random.Range(-2.3f, 2.3f), transform.position.y, transform.rotation.z);
        // spawn 3 enimies at the same time
        Instantiate(astroid5[astroidSelector], randomEnmPosition, transform.rotation);
        
    }
    void spawnRails()
    {
        Vector3 left = new Vector3(-2.75f, transform.position.y, transform.rotation.z);
        Vector3 right = new Vector3(2.65f, transform.position.y, transform.rotation.z);
        // spawn 3 enimies at the same time

        if(Random.Range(1,10) %2 == 0)
        {
            Instantiate(rails, right, transform.rotation);
        }
        else
            Instantiate(rails, left, transform.rotation);
        
    }
}
