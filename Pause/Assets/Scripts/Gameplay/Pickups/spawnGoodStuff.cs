using UnityEngine;
using System.Collections;

public class spawnGoodStuff : MonoBehaviour {

    public GameObject smStar;
    public GameObject midStar;
    public GameObject Atom;
    public GameObject redAtom;
    public GameObject cooldownAtom;
    public static bool AtomOnScreen;

    private float atomDelayTimer;
    private float redAtomDelayTimer;
    private float smStarTimer;
    private float midStarTimer;
    private float atomTimer;
    private float cooldownAtomDelayTimer;

    [Header("Blue atoms")]
    [Tooltip("How many blue atoms a planet may hand out, chosen at random " +
             "within this range. One of them is always held back for the end.")]
    public Vector2Int blueAtomsPerWorld = new Vector2Int(3, 5);

    [Tooltip("A blue atom is guaranteed once this many seconds remain in the " +
             "level, so nobody reaches the portal without a shot at one.")]
    public float guaranteeWhenSecondsLeft = 60f;

    [Tooltip("Most red (pause) atoms a planet will hand out, spread randomly " +
             "across the level.")]
    public int redAtomsPerWorld = 5;

    private int redBudget;
    private int redSpawned;
    private int blueBudget;
    private int blueSpawned;
    private int cooldownBudget;
    private int cooldownSpawned;
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
        if (cooldownAtom == null) cooldownAtom = Resources.Load<GameObject>("prefabs/cooldownAtom");
        resetBlueBudget();
        AtomSpacing.Reset();
	
	}

    // Update is called once per frame
    void Update() {
        // nothing new arrives during a planetfall's descent (Planetfall) or
        // a lift-off and its interlude (Liftoff)
        if (Planetfall.SuspendsSpawning || Liftoff.SuspendsSpawning) return;
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
        // x2 the inspector range (PickupRules.ShieldAtomRateMultiplier).
        blueBudget = PickupRules.ShieldAtomBudget(Random.Range(blueAtomsPerWorld.x, blueAtomsPerWorld.y + 1));
        blueSpawned = 0;
        redBudget = redAtomsPerWorld;
        redSpawned = 0;
        redAtomDelayTimer = Random.Range(15f, 40f);
        // violet capacitors: budget and delays in PickupRules
        cooldownBudget = Mathf.Max(0, PickupRules.CooldownAtomsPerWorld);
        cooldownSpawned = 0;
        cooldownAtomDelayTimer = PickupRules.CooldownAtomFirstDelay();
        blueGuaranteeUsed = false;
        atomTimer = Random.Range(20f, 45f);
    }

    void spawn() { spawn(Time.deltaTime); }

    // dt is explicit so a headless test can step a run (Time.deltaTime is 0
    // outside Play mode); PickupRulesTest drives it.
    void spawn(float dt)
    {
        // a new planet restores the allowance
        int world = WorldManager.Instance != null ? WorldManager.CurrentIndex : 0;
        if (world != lastWorld)
        {
            lastWorld = world;
            resetBlueBudget();
        }

        // Star dust runs x3 during a boss encounter, blue atoms x2 always
        // (PickupRules); red and violet atoms keep the plain clock.
        float dustDt = dt * PickupRules.StarDustRate();
        smStarTimer -= dustDt;
        midStarTimer -= dustDt;
        atomTimer -= dt * PickupRules.ShieldAtomRate;
        redAtomDelayTimer -= dt;
        cooldownAtomDelayTimer -= dt * PickupRules.CooldownAtomRate;
        // No two atoms (this spawner's or the green one's) within
        // AtomSpacing.Gap: one that comes due inside it waits its turn.
        AtomSpacing.Tick(dt);
        if (smStarTimer <= 0)
        {
            smStarTimer = Random.Range(5f, 7f);
            Vector3 randomStarPos = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), transform.position.y, transform.rotation.z);
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
            Vector3 randomStarPos = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), transform.position.y, transform.rotation.z);
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
        if (atomTimer <= 0 && blueSpawned < blueBudget - reserve && AtomSpacing.Ready)
        {
            atomDelayTimer = Random.Range(55f, 95f);
            spawnAtom();
            blueSpawned++;
            atomTimer = atomDelayTimer;
        }

        // The held-back one, released near the portal.
        if (!blueGuaranteeUsed && WorldManager.Instance != null &&
            WorldManager.Instance.SecondsLeftInWorld <= guaranteeWhenSecondsLeft &&
            blueSpawned < blueBudget && AtomSpacing.Ready)
        {
            blueGuaranteeUsed = true;
            spawnAtom();
            blueSpawned++;
            atomTimer = Random.Range(55f, 95f);
        }
        // Red atoms were arriving every 5-10 seconds, so pauses were effectively
        // unlimited. Now a fixed allowance per planet, spread across the level.
        if (redAtomDelayTimer <= 0 && redSpawned < redBudget && AtomSpacing.Ready)
        {
            redAtomDelayTimer = Random.Range(50f, 90f);
            spawnRedAtom();
            redSpawned++;
        }

        if (cooldownAtom != null && cooldownAtomDelayTimer <= 0 && cooldownSpawned < cooldownBudget && AtomSpacing.Ready)
        {
            cooldownAtomDelayTimer = PickupRules.CooldownAtomRepeatDelay();
            spawnCooldownAtom();
            cooldownSpawned++;
        }


    }
    // Each star in a cluster used to share vPos.x, so a whole burst landed
    // in one straight vertical line at a single horizontal spot. Rolling a
    // fresh x per star spreads the cluster across the lane instead, while
    // the vPos.y + pos stagger (unchanged) still keeps them from all
    // spawning on top of each other at once.
    void spawnSmStar(int pos, Vector3 vPos)
    {
        Vector3 spawner = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), vPos.y + pos, vPos.z);
        Place(smStar, spawner);
    }
    void spawnMidStar(int pos, Vector3 vPos)
    {
        Vector3 spawner = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), vPos.y + pos, vPos.z);
        Place(midStar, spawner);
    }
    // will make you invensiable for a few seconds.
    void spawnAtom()
    {

        Vector3 randomStarPos = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), transform.position.y, transform.rotation.z);
        // spawn 3 enimies at the same time
        AtomSpin.AddTo(Place(Atom, randomStarPos));
        AtomSpacing.Released();

    }
    void spawnRedAtom()
    {
        Vector3 randomStarPos = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), transform.position.y, transform.rotation.z);
        AtomSpin.AddTo(Place(redAtom, randomStarPos));
        AtomSpacing.Released();
    }
    void spawnCooldownAtom()
    {
        Vector3 randomStarPos = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), transform.position.y, transform.rotation.z);
        AtomSpin.AddTo(Place(cooldownAtom, randomStarPos));
        AtomSpacing.Released();
    }

    // Placement only (SpawnSpace): a pickup lands clear of the enemies'
    // footprints when there's room nearby (another x, or a short lift above
    // the spawn line), and reserves its spot so enemy spawns prefer to keep
    // off it. Soft both ways -- on a packed board it keeps its spot.
    GameObject Place(GameObject prefab, Vector3 pos)
    {
        // star dust flies PickupArt.StarDustScale bigger: its footprint too
        Vector2 half = prefab != null ? SpawnSpace.BodyHalf(prefab) * PickupArt.InGameScale(prefab) : Vector2.one * .2f;
        pos = SpawnSpace.PickupSpot(pos, half, -RailInset.PickupLaneHalf, RailInset.PickupLaneHalf);
        var go = Instantiate(prefab, pos, transform.rotation) as GameObject;
        PickupArt.ApplyInGameScale(go, prefab);
        PickupGlow.Dress(go);   // star dust's soft halo (atoms: AtomSpin.AddTo)
        SpawnFootprint.Attach(go, half, SpawnLayer.Pickup);
        return go;
    }
}
