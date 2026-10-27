using UnityEngine;
using System.Collections;

public class spawnGoodStuff : MonoBehaviour {

    public GameObject smStar;
    public GameObject midStar;
    public GameObject Atom;
    public GameObject redAtom;
    public GameObject cooldownAtom;
    public GameObject magnetAtom;
    public static bool AtomOnScreen;

    private float atomDelayTimer;
    private float redAtomDelayTimer;
    private float smStarTimer;
    private float midStarTimer;
    private float atomTimer;
    private float cooldownAtomDelayTimer;
    private float magnetAtomDelayTimer;
    private int magnetSpawned;

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
        if (magnetAtom == null) magnetAtom = Resources.Load<GameObject>("prefabs/" + MagnetPull.ObjectName);
        resetBlueBudget();
        AtomSpacing.Reset();
        AtomSpawnBias.Reset();
	
	}

    // Update is called once per frame
    void Update() {
        // nothing new arrives during a planetfall's descent (Planetfall) or
        // a lift-off and its interlude (Liftoff)
        if (Planetfall.SuspendsSpawning || Liftoff.SuspendsSpawning || PortalArrival.SuspendsSpawning) return;
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
        // (x1.5 with the fusion spawn multiplier: PickupRules.PauseAtomBudget)
        redBudget = PickupRules.PauseAtomBudget(redAtomsPerWorld);
        redSpawned = 0;
        redAtomDelayTimer = Random.Range(15f, 40f);
        // violet capacitors: budget and delays in PickupRules
        cooldownBudget = Mathf.Max(0, PickupRules.CooldownAtomsPerWorld);
        cooldownSpawned = 0;
        cooldownAtomDelayTimer = PickupRules.CooldownAtomFirstDelay();
        // magnetic atoms: budget and delays in PickupRules
        magnetSpawned = 0;
        magnetAtomDelayTimer = PickupRules.MagnetAtomFirstDelay();
        blueGuaranteeUsed = false;
        atomTimer = Random.Range(20f, 45f);
        AtomSpawnBias.NewWorld();   // a fresh complement allowance
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
        float dustDt = dt * PickupRules.StarDustRate() * AdaptiveDifficulty.DustScale;
        smStarTimer -= dustDt;
        midStarTimer -= dustDt;
        atomTimer -= dt * PickupRules.ShieldAtomRate;
        redAtomDelayTimer -= dt * AdaptiveDifficulty.RecoveryScale * PickupRules.PauseAtomRate;
        cooldownAtomDelayTimer -= dt * PickupRules.CooldownAtomRate;
        magnetAtomDelayTimer -= dt * PickupRules.MagnetAtomRate;
        // No two atoms (this spawner's or the green one's) within
        // AtomSpacing.Gap: one that comes due inside it waits its turn.
        AtomSpacing.Tick(dt);
        // ...and the same atom not twice in a row within FusionTuning.SameKindGap,
        // plus the occasional fusion partner after a catch (AtomSpawnBias)
        AtomSpawnBias.Tick(dt);
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
        // DEV: a fusion combo picked in Options (FusionTestMode, developer mode only):
        // only that combo's atoms, steadily, instead of the normal allowances.
        if (FusionTestMode.Active)
        {
            AtomKind devKind;
            if (FusionTestMode.Due(dt, out devKind)) spawnDevAtom(devKind);
            return;
        }
        // Blue atoms used to arrive every 6-9 seconds, which made a shield and
        // boost routine. They are now a scarce, per-planet allowance.
        int reserve = blueGuaranteeUsed ? 0 : 1;   // always hold one back for the end
        if (atomTimer <= 0 && blueSpawned < blueBudget - reserve && AtomSpacing.Ready && AtomSpawnBias.Allows(AtomKind.Shield))
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
        // (Held back through a boss encounter: the fight hands out exactly
        // BossFreeShotAtoms.Count of its own, below.)
        if (redAtomDelayTimer <= 0 && redSpawned < redBudget && AtomSpacing.Ready && !BossEncounter.Running && AtomSpawnBias.Allows(AtomKind.Pause))
        {
            redAtomDelayTimer = Random.Range(50f, 90f);
            spawnRedAtom();
            redSpawned++;
        }

        // The boss fight's free-shot atoms (BossFreeShotAtoms): a due one
        // waits out the spacing gap like any atom. Not part of the allowance.
        if (BossEncounter.FreeAtomDue && AtomSpacing.Ready)
        {
            var kind = BossEncounter.FreeAtomKind;
            BossEncounter.FreeAtomReleased();
            BossAtomsReleased[(int)kind]++;
            switch (kind)
            {
                case BossFreeShotAtoms.Kind.Shield: spawnAtom(); break;
                case BossFreeShotAtoms.Kind.Capacitor: if (cooldownAtom != null) spawnCooldownAtom(); else spawnAtom(); break;
                case BossFreeShotAtoms.Kind.Heal: spawnHealAtom(); break;
                default: spawnRedAtom(); break;
            }
        }

        if (cooldownAtom != null && cooldownAtomDelayTimer <= 0 && cooldownSpawned < cooldownBudget && AtomSpacing.Ready &&
            AtomSpawnBias.Allows(AtomKind.Capacitor))
        {
            cooldownAtomDelayTimer = PickupRules.CooldownAtomRepeatDelay();
            spawnCooldownAtom();
            cooldownSpawned++;
        }

        // The magnetic atom: a fixed allowance per planet, held back through a
        // boss fight (the delay keeps running, so it arrives right after).
        if (magnetAtom != null && magnetAtomDelayTimer <= 0 && magnetSpawned < PickupRules.MagnetAtomsPerWorld &&
            AtomSpacing.Ready && !BossEncounter.Running && AtomSpawnBias.Allows(AtomKind.Magnet))
        {
            magnetAtomDelayTimer = PickupRules.MagnetAtomRepeatDelay();
            spawnMagnetAtom();
            magnetSpawned++;
        }

        // A fusion partner asked for after a catch (AtomSpawnBias), out of
        // its own allowance, after the same atom spacing as any atom.
        if (AtomSpawnBias.Due && AtomSpacing.Ready) spawnComplement();

        // A power-up refund (PowerUpRefund): the guaranteed drop its use
        // meter earned, after the same atom spacing as any atom.
        if (PowerUpRefund.Due && AtomSpacing.Ready) spawnRefund();
    }

    // One atom of the dev fusion test's combo (no allowance, no boss limits).
    void spawnDevAtom(AtomKind kind)
    {
        Vector3 pos = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), transform.position.y, transform.rotation.z);
        switch (kind)
        {
            case AtomKind.Shield: spawnAtom(); break;
            case AtomKind.Pause: spawnRedAtom(); break;
            case AtomKind.Capacitor: if (cooldownAtom != null) spawnCooldownAtom(); break;
            case AtomKind.Magnet: if (magnetAtom != null) spawnMagnetAtom(); break;
            case AtomKind.Heal:
                {
                    Vector2 half = Vector2.one * HealAtom.TargetDiameter * .5f;
                    Vector3 at = SpawnSpace.PickupSpot(new Vector3(pos.x, CameraFit.ViewTop + .5f, 0f), half, -RailInset.PickupLaneHalf, RailInset.PickupLaneHalf);
                    SpawnFootprint.Attach(HealAtom.Spawn(at), half, SpawnLayer.Pickup);
                    AtomSpacing.Released();
                    AtomSpawnBias.Released(AtomKind.Heal);
                }
                break;
        }
    }

    // The partner AtomSpawnBias asked for: pulled forward out of that atom's
    // own per-world allowance (never extra), never in a boss fight; its own
    // clock restarts from the usual repeat delay. Dropped if the allowance is spent.
    void spawnComplement()
    {
        bool boss = BossEncounter.Running;
        bool released = false;
        switch (AtomSpawnBias.PendingKind)
        {
            case AtomKind.Shield:
                if (!boss && Atom != null && blueSpawned < blueBudget - (blueGuaranteeUsed ? 0 : 1))
                {
                    spawnAtom();
                    blueSpawned++;
                    atomTimer = Random.Range(55f, 95f);
                    released = true;
                }
                break;
            case AtomKind.Pause:
                if (!boss && redAtom != null && redSpawned < redBudget)
                {
                    spawnRedAtom();
                    redSpawned++;
                    redAtomDelayTimer = Random.Range(50f, 90f);
                    released = true;
                }
                break;
            case AtomKind.Capacitor:
                if (!boss && cooldownAtom != null && cooldownSpawned < cooldownBudget)
                {
                    spawnCooldownAtom();
                    cooldownSpawned++;
                    cooldownAtomDelayTimer = PickupRules.CooldownAtomRepeatDelay();
                    released = true;
                }
                break;
            case AtomKind.Magnet:
                if (!boss && magnetAtom != null && magnetSpawned < PickupRules.MagnetAtomsPerWorld)
                {
                    spawnMagnetAtom();
                    magnetSpawned++;
                    magnetAtomDelayTimer = PickupRules.MagnetAtomRepeatDelay();
                    released = true;
                }
                break;
        }
        AtomSpawnBias.Resolve(released);
    }

    // The refund's drop: an ordinary atom (same prefab, spawn and visuals),
    // marked so collecting and using it never feeds the meter again.
    void spawnRefund()
    {
        var kind = PowerUpRefund.Pick(score.pauseCounter, collisionDetection.atomCheck, Random.value);
        GameObject prefab = kind == PowerUpRefund.Kind.Pause ? redAtom
            : kind == PowerUpRefund.Kind.Shield ? Atom
            : kind == PowerUpRefund.Kind.Capacitor ? cooldownAtom : null;
        if (prefab == null) return;
        Vector3 pos = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), transform.position.y, transform.rotation.z);
        var go = Place(prefab, pos);
        AtomSpin.AddTo(go);
        go.AddComponent<RefundMark>().kind = kind;
        AtomSpacing.Released();
        PowerUpRefund.OnIssued(kind);
    }
    // Each star in a cluster used to share vPos.x, so a whole burst landed
    // in one straight vertical line at a single horizontal spot. Rolling a
    // fresh x per star spreads the cluster across the lane instead, while
    // the vPos.y + pos stagger (unchanged) still keeps them from all
    // spawning on top of each other at once.
    // A star's x: random across the lane, pulled toward the ship while the
    // pilot is doing well (AdaptiveDifficulty.LaneFollow) so the chain has
    // something in reach. 0 follow (the default) is the plain roll.
    static collisionDetection laneShip;
    static float StarX()
    {
        float x = Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf);
        float follow = AdaptiveDifficulty.LaneFollow;
        if (follow <= 0f) return x;
        if (laneShip == null) laneShip = FindFirstObjectByType<collisionDetection>();
        if (laneShip == null) return x;
        return Mathf.Clamp(Mathf.Lerp(x, laneShip.transform.position.x, follow), -RailInset.PickupLaneHalf, RailInset.PickupLaneHalf);
    }

    void spawnSmStar(int pos, Vector3 vPos)
    {
        Vector3 spawner = new Vector3(StarX(), vPos.y + pos, vPos.z);
        Place(smStar, spawner);
    }
    void spawnMidStar(int pos, Vector3 vPos)
    {
        Vector3 spawner = new Vector3(StarX(), vPos.y + pos, vPos.z);
        Place(midStar, spawner);
    }
    // will make you invensiable for a few seconds.
    void spawnAtom()
    {

        Vector3 randomStarPos = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), transform.position.y, transform.rotation.z);
        // spawn 3 enimies at the same time
        AtomSpin.AddTo(Place(Atom, randomStarPos));
        AtomSpacing.Released();
        AtomSpawnBias.Released(AtomKind.Shield);

    }
    // Boss-fight plan atoms released this session, by BossFreeShotAtoms.Kind (tests).
    public static readonly int[] BossAtomsReleased = new int[4];

    void spawnHealAtom()
    {
        if (!BossEncounter.TryReserveHealAtom()) return;   // boss fight: max 2 heal atoms
        Vector2 half = Vector2.one * HealAtom.TargetDiameter * .5f;
        Vector3 at = SpawnSpace.PickupSpot(new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), CameraFit.ViewTop + .5f, 0f), half, -RailInset.PickupLaneHalf, RailInset.PickupLaneHalf);
        SpawnFootprint.Attach(HealAtom.Spawn(at), half, SpawnLayer.Pickup);
        AtomSpacing.Released();
        AtomSpawnBias.Released(AtomKind.Heal);
    }

    void spawnRedAtom()
    {
        if (!BossEncounter.TryReserveRedAtom()) return;   // boss fight: max 2 red atoms
        Vector3 randomStarPos = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), transform.position.y, transform.rotation.z);
        AtomSpin.AddTo(Place(redAtom, randomStarPos));
        AtomSpacing.Released();
        AtomSpawnBias.Released(AtomKind.Pause);
    }
    void spawnCooldownAtom()
    {
        Vector3 randomStarPos = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), transform.position.y, transform.rotation.z);
        AtomSpin.AddTo(Place(cooldownAtom, randomStarPos));
        AtomSpacing.Released();
        AtomSpawnBias.Released(AtomKind.Capacitor);
    }

    void spawnMagnetAtom()
    {
        Vector3 randomStarPos = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), transform.position.y, transform.rotation.z);
        AtomSpin.AddTo(Place(magnetAtom, randomStarPos));
        AtomSpacing.Released();
        AtomSpawnBias.Released(AtomKind.Magnet);
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
