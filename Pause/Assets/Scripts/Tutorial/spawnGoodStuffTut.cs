using UnityEngine;
using System.Collections;

public class spawnGoodStuffTut: MonoBehaviour {

    public GameObject smStar;
    public GameObject midStar;
    public GameObject Atom;
    public GameObject redAtom;
    public static float redAtomDelayTimer;
    public static float atomDelayTimer;
    public static float smStarTimer;
    public static float midStarTimer;


    // used for random int for generating stars
    int max;

	// Use this for initialization
	void Start () {

        smStarTimer = 1000;
        midStarTimer = 1000;
        atomDelayTimer = 1000;
        redAtomDelayTimer = 1000;
	}
	
	// Update is called once per frame
	void Update () {
	    if((TouchInput.IsPressed) && !buttonClicks.playerDied)
        {
            spawn();
        }
        else if (score.pauseCounter <= 0 && !buttonClicks.playerDied)
        {
            spawn();
        }

    }
    void spawn()
    {
        smStarTimer -= Time.deltaTime;
        midStarTimer -= Time.deltaTime;
        atomDelayTimer -= Time.deltaTime;
        redAtomDelayTimer -= Time.deltaTime;

        if(smStarTimer <= 0)
        {
            smStarTimer = Random.Range(5f, 7f);
            Vector3 randomStarPos = new Vector3(Random.Range(-2.2f, 2.2f), transform.position.y, transform.rotation.z);
            // spawn up tp 5 sm stars in a row for collecting
            max = Random.Range(2, 8);
            for (int i = 0; i < max; i++)
            {
                spawnSmStar(i, randomStarPos);
            }
           
        }
        if(midStarTimer <= 0)
        {

            midStarTimer = Random.Range(10f, 14f);
            Vector3 randomStarPos = new Vector3(Random.Range(-2.2f, 2.2f), transform.position.y, transform.rotation.z);
            // spawn up to 2-4 mid stars for collecting
            max = Random.Range(2, 4);
            for (int i = 0; i < max; i++)
            {
                spawnMidStar(i, randomStarPos);
            }  
        }
        if(atomDelayTimer <= 0 )
        {
            atomDelayTimer = Random.Range(7f,12f);
            spawnBlueAtom();       
        }
        if( redAtomDelayTimer <= 0)
        {
            redAtomDelayTimer = Random.Range(5, 10);
            spawnRedAtom();
        }

    }
    // See spawnGoodStuff.spawnSmStar/spawnMidStar: was vPos.x for the whole
    // cluster (a straight vertical line at one x), now a fresh roll per star.
    void spawnSmStar(int pos, Vector3 vPos)
    {
        Vector3 spawner = new Vector3(Random.Range(-2.2f, 2.2f), vPos.y + pos, vPos.z);
        Instantiate(smStar, spawner, transform.rotation);
    }

    void spawnMidStar(int pos, Vector3 vPos)
    {
        Vector3 spawner = new Vector3(Random.Range(-2.2f, 2.2f), vPos.y + pos, vPos.z);
        Instantiate(midStar, spawner, transform.rotation);
    }
    // will make you invensiable for a few seconds.
    void spawnBlueAtom()
    {
        Vector3 randomStarPos = new Vector3(Random.Range(-2.2f, 2.2f), transform.position.y, transform.rotation.z);
        Instantiate(Atom, randomStarPos, transform.rotation);
    }

    void spawnRedAtom()
    {
        Vector3 randomStarPos = new Vector3(Random.Range(-2.2f, 2.2f), transform.position.y, transform.rotation.z);
        Instantiate(redAtom, randomStarPos, transform.rotation);
    }
}
