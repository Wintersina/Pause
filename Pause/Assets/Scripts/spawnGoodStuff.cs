using UnityEngine;
using System.Collections;

public class spawnGoodStuff : MonoBehaviour {

    public GameObject smStar;
    public GameObject midStar;
    public GameObject Atom;
    public GameObject redAtom;
    public static bool AtomOnScreen;

    private float atomDelayTimer;
    private float redAtomDelayTimer;
    private float smStarTimer;
    private float midStarTimer;
    private float atomTimer;

    // used for random int for generating stars
    int max;

	// Use this for initialization
	void Start () {

        AtomOnScreen = false;
        smStarTimer = 7f;
        midStarTimer = 14f;
        atomTimer = 1f;
        redAtomDelayTimer = 10f;
	
	}

    // Update is called once per frame
    void Update() {
        if (TouchInput.IsPressed && !buttonClicks.playerDied)
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
        atomTimer -= Time.deltaTime;
        redAtomDelayTimer -= Time.deltaTime;
        if (smStarTimer <= 0)
        {
            smStarTimer = Random.Range(5f, 7f);
            Vector3 randomStarPos = new Vector3(Random.Range(-2.2f, 2.2f), transform.position.y, transform.rotation.z);
            // spawn up tp 5 sm stars in a row for collecting
            max = Random.Range(4, 10);
            for (int i = 0; i < max; i++)
            {
                spawnSmStar(i, randomStarPos);
            }
           
            
        }
        if(midStarTimer <= 0)
        {

            midStarTimer = Random.Range(10f, 14f);
            Vector3 randomStarPos = new Vector3(Random.Range(-2.2f, 2.2f), transform.position.y, transform.rotation.z);
            // spawn up to 3 mid stars for collecting
            max = Random.Range(3, 6);
            for (int i = 0; i < max; i++)
            {
                spawnMidStar(i, randomStarPos);
            }  
        }
        if(atomTimer <= 0)
        {
            // will change spawn time depending on speed
            atomDelayTimer = (moveBackGround.speed < .4) ? Random.Range(8f, 9f) : Random.Range(6f, 6.5f);
            spawnAtom();
            atomTimer = atomDelayTimer;

        }
        if (redAtomDelayTimer <= 0)
        {
            redAtomDelayTimer = Random.Range(5, 10);
            spawnRedAtom();
        }


    }
    void spawnSmStar(int pos, Vector3 vPos)
    {
        Vector3 spawner = new Vector3(vPos.x, vPos.y + pos, vPos.z);   
        // spawn 3 enimies at the same time
        Instantiate(smStar,spawner , transform.rotation);
    }
    void spawnMidStar(int pos, Vector3 vPos)
    {
        Vector3 spawner = new Vector3(vPos.x, vPos.y + pos, vPos.z);
        // spawn 3 enimies at the same time
        Instantiate(midStar, spawner, transform.rotation);

    }
    // will make you invensiable for a few seconds.
    void spawnAtom()
    {

        Vector3 randomStarPos = new Vector3(Random.Range(-2.2f, 2.2f), transform.position.y, transform.rotation.z);
        // spawn 3 enimies at the same time
        Instantiate(Atom, randomStarPos, transform.rotation);

    }
    void spawnRedAtom()
    {
        Vector3 randomStarPos = new Vector3(Random.Range(-2.2f, 2.2f), transform.position.y, transform.rotation.z);
        Instantiate(redAtom, randomStarPos, transform.rotation);
    }
}
