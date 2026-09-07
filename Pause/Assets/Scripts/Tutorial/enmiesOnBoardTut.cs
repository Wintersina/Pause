using UnityEngine;
using System.Collections;

/*
    This script spawns enimies at different intervals on to the board. Each enimey is randomly generated. 

 */


public class enmiesOnBoardTut : MonoBehaviour {

    public GameObject enim;
    public GameObject enim2;
    public GameObject smallAstroid;
    public GameObject midAstroid;
    public GameObject largeAstroid;
    public GameObject animatedEniOne;

    public static float smEnmDelayTimer;
    public static float bigEnmDelayTimer;
    private float slowSmlEnmTimerBy;
    private float slowBigEnmTimerBy;
    private float slowSmlAstroidTimerBy;
    private float slowMidAstroidTimerBy;
    private float slowBigAstroidTimerBy;
    public static float smallAstroidDelayTimer;
    public static float midAstroidDelayTimer;
    public static float bigAstroidDelayTimer;
    private float spawnAnimatedEnimeOneTimer;
    public static float spawnAnimatedEnimeOneDelayTimer;
   

    // Use this for initialization
    void Start () {

        bigEnmDelayTimer = 60;
        smEnmDelayTimer = 60f;
        midAstroidDelayTimer = 60f;
        smallAstroidDelayTimer = 60f;
        bigAstroidDelayTimer = 60f;
        spawnAnimatedEnimeOneDelayTimer = 60f;

    }
	
	// Update is called once per frame
	void Update () {
        // Spawn call
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
       
        smEnmDelayTimer -= Time.deltaTime;
        bigEnmDelayTimer -= Time.deltaTime;
        smallAstroidDelayTimer -= Time.deltaTime;
        midAstroidDelayTimer -= Time.deltaTime;
        bigAstroidDelayTimer -= Time.deltaTime;
        spawnAnimatedEnimeOneDelayTimer -= Time.deltaTime;


        if (smEnmDelayTimer <= 0)
        {
            // change the spawn timer everytime
            slowSmlEnmTimerBy = Random.Range(2.5f, 3.5f);
            spawnSmallEnim();
            smEnmDelayTimer = slowSmlEnmTimerBy;
        }
        if (bigEnmDelayTimer <= 0)
        {
            // change the spawn timer everytime
            slowBigEnmTimerBy = Random.Range(2.1f, 4.9f);
            spawnLongEnim();
            bigEnmDelayTimer = slowBigEnmTimerBy;
        }
        if(smallAstroidDelayTimer <= 0 )
        {
            slowSmlAstroidTimerBy = Random.Range(2f, 7.5f);
            spawnSmallAstroid();
            smallAstroidDelayTimer = slowSmlAstroidTimerBy;
        }
        if (midAstroidDelayTimer <= 0)
        {
            slowMidAstroidTimerBy = Random.Range(2.1f,3.5f);
            spawnMidAstroid();
            midAstroidDelayTimer = slowMidAstroidTimerBy;
        }
        if (bigAstroidDelayTimer <= 0)
        {
            slowBigAstroidTimerBy = Random.Range(1.5f, 3.5f);
            spawnLargeAstroid();
            bigAstroidDelayTimer = slowBigAstroidTimerBy;
        }
        if(spawnAnimatedEnimeOneDelayTimer <= 0 )
        {
            spawnAnimatedEnimeOneTimer = Random.Range(3, 5);
            spawnAnimatedEnimeOne();
            spawnAnimatedEnimeOneDelayTimer = spawnAnimatedEnimeOneTimer;
        }
        /* May be used for something later

        */

    }
    // spawns small enimes through the board
    void spawnSmallEnim()
    {
        Vector3 randomStarPos = new Vector3(Random.Range(-2.2f, 2.2f), transform.position.y, transform.rotation.z);
        // spawn 3 enimies at the same time
        Instantiate(enim, randomStarPos, transform.rotation);

        //distroy only if touch is on the screen
        //Destroy(enermy, 60);

    }
    // this spawn larg enimes though the board
    void spawnLongEnim()
    {
        Vector3 bigPos1 = new Vector3(-1.18f, transform.position.y, transform.rotation.z);
        Vector3 bigPos2 = new Vector3(.95f, transform.position.y, transform.rotation.z);
        // spawn 3 enimies at the same time

        
        if (Random.Range(2, 11) % 2 == 0)
        {
            Instantiate(enim2, bigPos1, transform.rotation);
            //Destroy(enermy, 80);
        }
        else
        {
            Instantiate(enim2, bigPos2, transform.rotation);
            //Destroy(enermy, 80);
        }
    }
    void spawnAnimatedEnimeOne()
    {
        Vector3 randomStarPos = new Vector3(Random.Range(-2.5f, 1.9f), transform.position.y, transform.rotation.z);
        // spawn 3 enimies at the same time
        Instantiate(animatedEniOne, randomStarPos, transform.rotation);
         
    }
    // Next 3 functions spawn 3 different types of astroids.
    void spawnSmallAstroid()
    {
        Vector3 randomStarPos = new Vector3(Random.Range(-2.5f, 1.9f), transform.position.y, transform.rotation.z);
        // spawn 3 enimies at the same time
        Instantiate(smallAstroid, randomStarPos, transform.rotation);
        //Destroy(enermy, 60);
    }
    void spawnMidAstroid()
    {
        Vector3 randomStarPos = new Vector3(Random.Range(-2.1f, 2.1f), transform.position.y, transform.rotation.z);
        // spawn 3 enimies at the same time
        Instantiate(midAstroid, randomStarPos, transform.rotation);
        //Destroy(enermy, 60);
    }
    void spawnLargeAstroid()
    {
        Vector3 randomStarPos = new Vector3(Random.Range(-2.3f, 2.3f), transform.position.y, transform.rotation.z);
        // spawn 3 enimies at the same time
        Instantiate(largeAstroid, randomStarPos, transform.rotation);
        //Destroy(enermy, 60);
    }
}
