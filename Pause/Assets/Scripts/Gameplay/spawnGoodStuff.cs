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

    [Header("Blue atoms")]
    [Tooltip("How many blue atoms a planet may hand out, chosen at random " +
             "within this range. One of them is always held back for the end.")]
    public Vector2Int blueAtomsPerWorld = new Vector2Int(3, 5);

    [Tooltip("A blue atom is guaranteed once this many seconds remain in the " +
             "level, so nobody reaches the portal without a shot at one.")]
    public float guaranteeWhenSecondsLeft = 60f;

    private int blueBudget;
    private int blueSpawned;
    private bool blueGuaranteeUsed;
    private int lastWorld = -1;

    // used for random int for generating stars
    int max;

	// Use this for initialization
	void Start () {

        AtomOnScreen = false;
        smStarTimer = 7f;
        midStarTimer = 14f;
        atomTimer = Random.Range(20f, 45f);
        redAtomDelayTimer = 10f;
        resetBlueBudget();
	
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


    // Each planet gets its own allowance.
    void resetBlueBudget()
    {
        blueBudget = Random.Range(blueAtomsPerWorld.x, blueAtomsPerWorld.y + 1);
        blueSpawned = 0;
        blueGuaranteeUsed = false;
        atomTimer = Random.Range(20f, 45f);
    }

    void spawn()
    {
        // a new planet restores the allowance
        int world = WorldManager.Instance != null ? WorldManager.CurrentIndex : 0;
        if (world != lastWorld)
        {
            lastWorld = world;
            resetBlueBudget();
        }

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
        // Blue atoms used to arrive every 6-9 seconds, which made a shield and
        // boost routine. They are now a scarce, per-planet allowance.
        int reserve = blueGuaranteeUsed ? 0 : 1;   // always hold one back for the end
        if (atomTimer <= 0 && blueSpawned < blueBudget - reserve)
        {
            atomDelayTimer = Random.Range(55f, 95f);
            spawnAtom();
            blueSpawned++;
            atomTimer = atomDelayTimer;
        }

        // The held-back one, released near the portal.
        if (!blueGuaranteeUsed && WorldManager.Instance != null &&
            WorldManager.Instance.SecondsLeftInWorld <= guaranteeWhenSecondsLeft &&
            blueSpawned < blueBudget)
        {
            blueGuaranteeUsed = true;
            spawnAtom();
            blueSpawned++;
            atomTimer = Random.Range(55f, 95f);
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
