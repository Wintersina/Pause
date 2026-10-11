using UnityEngine;

// Spawns the tutorial's pickups when the script (Hints) asks for them: the
// star dust (a rush right ahead of the ship, then clusters), and each atom
// -- green (heal), blue (shield), red (pauses), violet (weapon charge) --
// EXACTLY ONCE, as its line starts (SpawnIntro). There are no other atoms in
// the tutorial: no respawn when one is missed (TutorialAtomDrift keeps it on
// screen until caught, and Hints removes it when its step ends), and the
// run's own pickup spawners (spawnGoodStuff, HealAtomSpawner) are not in
// this scene. Star clusters only run while the world is moving.
public enum TutorialAtom { None, Green, Blue, Red, Cooldown }

public class spawnGoodStuffTut: MonoBehaviour {

    public GameObject smStar;
    public GameObject midStar;
    public GameObject Atom;
    public GameObject redAtom;
    public static float smStarTimer;
    public static float midStarTimer;

    public GameObject cooldownAtom;

    // The atom the current step introduced (set by Hints, for its arrow).
    public static TutorialAtom keepAtomComing;
    public static Transform LiveAtom { get; private set; }
    // How many atoms of each kind this tutorial has spawned (each exactly once).
    static readonly int[] spawnedByKind = new int[5];
    public static int Spawned(TutorialAtom kind) { return spawnedByKind[(int)kind]; }
    public static int SpawnedTotal { get { int n = 0; foreach (int c in spawnedByKind) n += c; return n; } }
    const float Never = 1000f;

    // ---- The dust rush: the star step puts a short, easy stream right ahead ----

    // Star-dust pieces on screen that the tutorial arrows point at (rushed
    // ones and the normal clusters). Pruned of collected / gone pieces.
    static readonly System.Collections.Generic.List<Transform> liveStars = new System.Collections.Generic.List<Transform>();
    public static System.Collections.Generic.IReadOnlyList<Transform> LiveStars
    {
        get
        {
            liveStars.RemoveAll(t => t == null);
            return liveStars;
        }
    }

    public const int RushCount = 6;
    // First piece this far above the ship, then every RushSpacing: the last is
    // RushReach above it, so it arrives in RushReach / TutorialStarDrift.Speed.
    public const float RushFirstGap = 1.3f;
    public const float RushSpacing = .5f;
    public const float RushReach = RushFirstGap + (RushCount - 1) * RushSpacing;

    // Puts RushCount pieces in a gentle S just above the ship, in its lane,
    // each falling onto it (TutorialStarDrift); the last is a big one.
    // Returns how many were made.
    public int RushStars(Vector3 ship, float topEdge)
    {
        if (smStar == null) return 0;
        int made = 0;
        for (int i = 0; i < RushCount; i++)
        {
            float y = Mathf.Min(ship.y + RushFirstGap + i * RushSpacing, topEdge - .3f - (RushCount - 1 - i) * .35f);
            float x = Mathf.Clamp(ship.x + .25f * Mathf.Sin(i * 1.3f), -RailInset.PickupLaneHalf, RailInset.PickupLaneHalf);
            var prefab = i == RushCount - 1 && midStar != null ? midStar : smStar;
            var star = Instantiate(prefab, new Vector3(x, y, 0f), transform.rotation) as GameObject;
            if (star == null) continue;
            PickupGlow.Dress(star);
            TutorialStarDrift.AddTo(star);
            liveStars.Add(star.transform);
            made++;
        }
        return made;
    }

    // used for random int for generating stars
    int max;

	void Start () {
        smStarTimer = Never;
        midStarTimer = Never;
        keepAtomComing = TutorialAtom.None;
        LiveAtom = null;
        System.Array.Clear(spawnedByKind, 0, spawnedByKind.Length);
        liveStars.Clear();
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

    }
    // See spawnGoodStuff.spawnSmStar/spawnMidStar: was vPos.x for the whole
    // cluster (a straight vertical line at one x), now a fresh roll per star.
    void spawnSmStar(int pos, Vector3 vPos)
    {
        Vector3 spawner = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), vPos.y + pos, vPos.z);
        var star = Instantiate(smStar, spawner, transform.rotation) as GameObject;
        PickupGlow.Dress(star);
        if (star != null) liveStars.Add(star.transform);
    }

    void spawnMidStar(int pos, Vector3 vPos)
    {
        Vector3 spawner = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), vPos.y + pos, vPos.z);
        var star = Instantiate(midStar, spawner, transform.rotation) as GameObject;
        PickupGlow.Dress(star);
        if (star != null) liveStars.Add(star.transform);
    }

    // One scripted atom, dropped in as its step's line starts: closer to the
    // middle than stars so it is easy to reach. It drops in from just above
    // the top edge and hovers in the ship's lane until caught
    // (TutorialAtomDrift) instead of riding the world scroller, which barely
    // moves at the tutorial's near-zero speed. Returns it (null: no prefab).
    public GameObject SpawnIntro(TutorialAtom kind)
    {
        if (kind == TutorialAtom.None) return null;
        float x = Random.Range(-1.2f, 1.2f);
        Vector3 pos = new Vector3(x, transform.position.y, transform.rotation.z);
        GameObject atom;
        if (kind == TutorialAtom.Green) atom = HealAtom.Spawn(pos);
        else
        {
            GameObject prefab = kind == TutorialAtom.Blue ? Atom : kind == TutorialAtom.Red ? redAtom : CooldownPrefab();
            if (prefab == null) return null;
            atom = AtomSpin.AddTo(Instantiate(prefab, pos, transform.rotation) as GameObject);
        }
        if (atom == null) return null;
        TutorialAtomDrift.AddTo(atom, x);
        LiveAtom = atom.transform;
        keepAtomComing = kind;
        spawnedByKind[(int)kind]++;
        return atom;
    }

    GameObject CooldownPrefab()
    {
        if (cooldownAtom == null) cooldownAtom = Resources.Load<GameObject>("prefabs/cooldownAtom");
        return cooldownAtom;
    }

    // Removes the introduced atom if it is still out (its step ended).
    public static void RemoveLiveAtom()
    {
        if (LiveAtom != null)
        {
            var go = LiveAtom.gameObject;
            LiveAtom = null;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }
        LiveAtom = null;
    }
}
