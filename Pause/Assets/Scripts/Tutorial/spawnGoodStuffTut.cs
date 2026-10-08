using UnityEngine;

// Spawns the tutorial's pickups when the script (Hints) asks for them: star
// clusters once the star-dust step starts, then each atom -- green (heal),
// blue (shield), red (pauses) -- the moment its line is spoken, coming back
// until the player catches one. For the power step (Charge) it keeps green
// and blue atoms coming in turn, sooner after each catch, until the weapon
// they charge goes off. Like the real spawner it only runs while the world
// is moving.
public enum TutorialAtom { None, Green, Blue, Red, Charge }

public class spawnGoodStuffTut: MonoBehaviour {

    public GameObject smStar;
    public GameObject midStar;
    public GameObject Atom;
    public GameObject redAtom;
    public static float smStarTimer;
    public static float midStarTimer;

    // Set by Hints for an atom step. While set, that atom is spawned whenever
    // none is on screen (the last one was caught or scrolled off).
    public static TutorialAtom keepAtomComing;
    public static Transform LiveAtom { get; private set; }
    static float atomDelay;
    static int chargeAtomsSpawned;

    const float AtomRespawnSeconds = 1.2f;
    // The power step needs several atoms in a row: the next one follows
    // the last catch quicker.
    public const float ChargeRespawnSeconds = .5f;
    const float Never = 1000f;

    // used for random int for generating stars
    int max;

	void Start () {
        smStarTimer = Never;
        midStarTimer = Never;
        keepAtomComing = TutorialAtom.None;
        LiveAtom = null;
        atomDelay = .6f;
        chargeAtomsSpawned = 0;
	}

    // Starts the star clusters (they then repeat on their own timers).
    public static void StartStars()
    {
        smStarTimer = Mathf.Min(smStarTimer, .4f);
        midStarTimer = Mathf.Min(midStarTimer, 1.6f);
    }

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

        if(smStarTimer <= 0)
        {
            smStarTimer = Random.Range(5f, 7f);
            Vector3 randomStarPos = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), transform.position.y, transform.rotation.z);
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
            Vector3 randomStarPos = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), transform.position.y, transform.rotation.z);
            // spawn up to 2-4 mid stars for collecting
            max = Random.Range(2, 4);
            for (int i = 0; i < max; i++)
            {
                spawnMidStar(i, randomStarPos);
            }
        }

        if (keepAtomComing != TutorialAtom.None && LiveAtom == null)
        {
            atomDelay -= Time.deltaTime;
            if (atomDelay <= 0f)
            {
                atomDelay = keepAtomComing == TutorialAtom.Charge ? ChargeRespawnSeconds : AtomRespawnSeconds;
                spawnAtom(keepAtomComing);
            }
        }
    }
    // See spawnGoodStuff.spawnSmStar/spawnMidStar: was vPos.x for the whole
    // cluster (a straight vertical line at one x), now a fresh roll per star.
    void spawnSmStar(int pos, Vector3 vPos)
    {
        Vector3 spawner = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), vPos.y + pos, vPos.z);
        Instantiate(smStar, spawner, transform.rotation);
    }

    void spawnMidStar(int pos, Vector3 vPos)
    {
        Vector3 spawner = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), vPos.y + pos, vPos.z);
        Instantiate(midStar, spawner, transform.rotation);
    }

    // Kept closer to the middle than stars so it is easy to reach. It drops in
    // from just above the top edge and hovers in the ship's lane until caught
    // (TutorialAtomDrift) instead of riding the world scroller, which barely
    // moves at the tutorial's near-zero speed.
    void spawnAtom(TutorialAtom kind)
    {
        // Charge atoms take turns: green, blue, green ... (both cut the
        // weapon's charge by the same ShipPowerController.AtomCutSeconds).
        if (kind == TutorialAtom.Charge)
            kind = chargeAtomsSpawned++ % 2 == 0 ? TutorialAtom.Green : TutorialAtom.Blue;
        float x = Random.Range(-1.6f, 1.6f);
        Vector3 pos = new Vector3(x, transform.position.y, transform.rotation.z);
        GameObject atom;
        if (kind == TutorialAtom.Green) atom = HealAtom.Spawn(pos);
        else atom = AtomSpin.AddTo(Instantiate(kind == TutorialAtom.Blue ? Atom : redAtom, pos, transform.rotation) as GameObject);
        TutorialAtomDrift.AddTo(atom, x);
        LiveAtom = atom != null ? atom.transform : null;
    }
}
