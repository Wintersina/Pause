using UnityEngine;
using UnityEngine.UI;
using System.Collections;


public class collisionDetection : MonoBehaviour {

    public static bool atomCheck;

    // Phase Cloak (ShipPowerController.DoCloak). Its own clock, separate from
    // the blue atom's invTimer: Cloak makes the ship invulnerable without
    // raising the shield, the boost or the boost music, and neither state can
    // shorten or stretch the other. Ticks on the same running-world clock as
    // invTimer (turnTextsOff), so it never drains while the game is paused.
    public static float cloakTimer;
    public static bool Cloaked { get { return cloakTimer > 0f; } }

    // What hazards check: the blue-atom shield, Cloak, or both.
    public static bool Invulnerable { get { return atomCheck || Cloaked; } }

    // Starts (or refreshes) Cloak; never shortens one already running.
    public static void BeginCloak(float seconds)
    {
        cloakTimer = Mathf.Max(cloakTimer, seconds);
    }

    // Runs the cloak clock down; clamps at zero so it can never read as
    // still running once it has ended.
    public static void TickCloak(float dt)
    {
        if (cloakTimer > 0f) cloakTimer = Mathf.Max(0f, cloakTimer - dt);
    }


    private static float savedTimer;
    public static float invTimer;
    private float boostTimer;
    public Text hypeText;
    public Text boostText;
    private int atomCounter;
    public static int lifeCounter;
    // Atoms picked up this session, by kind. The tutorial watches these to
    // know the player caught the atom it just introduced.
    public static int healAtomPickups, shieldAtomPickups, pauseAtomPickups;
    public static int MAXLIFE;

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

    [Tooltip("Small star-dust payout for an enemy or asteroid destroyed.")]
    public float enemyDustValue = 0.12f;

    // Explosions used to hang in space while the world scrolled past them, so a
    // blast appeared to race forward alongside the ship. Giving them the same
    // scroller everything else uses keeps them pinned to the point of impact.
    static GameObject ScrollWithWorld(GameObject go)
    {
        if (go != null && go.GetComponent<moveItemEnmInStrightLine>() == null)
            go.AddComponent<moveItemEnmInStrightLine>();
        return go;
    }

    // The explosion sound only ever fired for asteroids destroyed while boosted,
    // so most blasts on screen were silent. Every explosion now makes a noise,
    // with a little pitch variation so repeats do not grate.
    public static void PlayExplosion()
    {
        var src = ExplosionSource();
        if (src == null) return;
        src.pitch = Random.Range(0.92f, 1.08f);
        src.PlayOneShot(src.clip, 0.9f);
    }

    static AudioSource cachedExplosionSource;

    static AudioSource ExplosionSource()
    {
        if (cachedExplosionSource != null) return cachedExplosionSource;
        var go = SceneUtil.FindAny("AstroidExplotionSound");
        cachedExplosionSource = go != null ? go.GetComponent<AudioSource>() : null;
        return cachedExplosionSource;
    }

    // Tutorial earnings are tracked separately so a practice run cannot be
    // farmed for real currency.
    void awardDust(float amount)
    {
        score.AwardStarDust(amount);
    }

    public static void AwardDestroyedTarget(GameObject target)
    {
        if (target == null || (!target.CompareTag("Enimey") && !target.CompareTag("Astr"))) return;
        Codex.Discover(target);   // ultimate kills count as meeting it too
        var player = Object.FindFirstObjectByType<collisionDetection>();
        if (player != null) player.awardDust(player.enemyDustValue);
    }

    // Shaves time off the ultimate's countdown on pickup -- a little for
    // star dust, a lot more for an atom (blue, red or the green heal atom
    // all count the same). No-ops outside gameS1, where there is no
    // ShipPowerController to speed up.
    void BoostUltimate(bool dust)
    {
        var power = ShipPowerController.Instance;
        if (power == null) return;
        power.ReduceTimer(dust ? power.secondsPerDust : power.secondsPerAtom);
    }

	void Start () {

        MAXLIFE = 3;
        // Fills the needed componets for this player.
        hypeText = GameObject.Find("hypeText").GetComponent<Text>();
        boostText = GameObject.Find("boostText").GetComponent<Text>();
        //destructionComboText = GameObject.Find("DestructionText").GetComponent<Text>();
        boost = GameObject.FindGameObjectWithTag("boost");
        boost.gameObject.SetActive(false);
        boostSound = GameObject.Find("RocketsSound").GetComponent<AudioSource>();
        astroidExpSound = GameObject.Find("AstroidExplotionSound").GetComponent<AudioSource>();

        shield = ShipShield.For(gameObject).Visual;


        //destructionComboText.gameObject.SetActive(false);
    

        //source.clip = boostSound;

        // making sure atom is not active until player picks it up
        atomCheck = false;
        cloakTimer = 0f;

        // empty out any counters
        atomCounter = 0;
        lifeCounter = 0;
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
        Codex.Discover(hit.gameObject);   // first touch unlocks its codex entry (enemy, rock, atom, portal)

        #region
        //------------------------- Colliding with Enimies ---------------------------------------------
        if (hit.gameObject.tag == "Enimey" || hit.gameObject.tag == "Astr" )
        {

            // creating different explotions for different enims
            // Under the boost shield (or Cloak) the player destroys the
            // mine, and the weapon explosion below covers it.
            if (PrefabName.Is(hit.gameObject, "mine") && !Invulnerable)
            {
                PlayExplosion();
                GameObject RedExp = ScrollWithWorld(Instantiate(redExp, hit.gameObject.transform.position, hit.gameObject.transform.rotation) as GameObject);
                Destroy(RedExp, 2);
            }

            if (Invulnerable)
            {
                // acchivment reporting
                if (PrefabName.Is(hit.gameObject, "alien1"))
                {
                    //------------------------- Kill 5/25/50/150/1000/3500 Aliens ---##08-13---
                    achievementAPICalls.alien_killed();
                }
                if (hit.gameObject.tag == "Astr")
                {
                    //------------------------- Destroy 5/25/50/100/1500 Asteroids ---##14-18---
                    achievementAPICalls.asteroid_destroyed();
                }
                // Shows the hit on the shield; a no-op under Cloak alone,
                // where no shield is up.
                ShipShield.For(gameObject).Absorb(hit.transform.position);
                // show the texts for only half of a second.
                savedTimer = .4f;

                // The player destroyed it: the pooled cartoon target
                // explosion (metal / rock / mine), flashed in this ship's
                // weapon colour.
                TargetExplosion.Spawn(hit.gameObject, ShipId.Of(gameObject, ShipId.Equipped()));
                PlayExplosion();

                AwardDestroyedTarget(hit.gameObject);
                Destroy(hit.gameObject);
                
                
                
            
                // open memory and remove leftovers
            }
            else {
                lifeCounter += 1;
                //change sprite



                Vector3 shipPos = this.gameObject.transform.position;
                Quaternion shipRot = this.gameObject.transform.rotation;

                // kill the player
                GameObject exp = ScrollWithWorld(Instantiate(explosionAnimation, shipPos, shipRot) as GameObject);
                PlayExplosion();


                //exp.transform.position = hit.gameObject.transform.position;
                if (lifeCounter >= MAXLIFE)
                {
                    buttonClicks.playerDied = true;
                    //--------------------1st/5th/10th/50th/100th DEATH ---##01-04-----------------
                    achievementAPICalls.player_died();

                    achievementAPICalls.leaderboard_highest_speed_reached(Mathf.Round(moveBackGround.speed * 100));
                    // End of the run: flush the batched achievement counters.
                    PrefsSaver.SaveNow();
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

            if (PrefabName.Is(hit.gameObject, "smStar1") || PrefabName.Is(hit.gameObject, "LargeStar1"))
            {
                //--------------------PickUp Stars 150/1000-------##06-07--------------------
                achievementAPICalls.star_collected();
            }

                // calculate different scores for each items.
                if (PrefabName.Is(hit.gameObject, "smStar1"))
            {
                awardDust(smallStarValue);
                BoostUltimate(dust: true);
                Destroy(hit.gameObject);
            }
            else if(PrefabName.Is(hit.gameObject, "LargeStar1"))
            {
                awardDust(largeStarValue);
                BoostUltimate(dust: true);
                Destroy(hit.gameObject);
            }
            else if (PrefabName.Is(hit.gameObject, HealAtom.ObjectName))
            {
                healAtomPickups++;
                // repairs one point of hull damage; lifeControler picks the
                // sprite back up from lifeCounter on the next frame
                if (lifeCounter > 0) lifeCounter--;
                if (hypeText != null) hypeText.text = "REPAIRED";
                BoostUltimate(dust: false);
                Destroy(hit.gameObject);
            }
            else if (PrefabName.Is(hit.gameObject, "pauseAtom"))
            {
                pauseAtomPickups++;
                score.incromentPause();
                BoostUltimate(dust: false);
                Destroy(hit.gameObject);
            }
            else if(PrefabName.Is(hit.gameObject, "atom3a"))
            {
                shieldAtomPickups++;
                BoostUltimate(dust: false);
                boostSound.Play();
                // ---------------------------
                //   Music control section!
                musicControl.boostMusicChanger = true;
                // ----------------------------

                ShipShield.For(gameObject).Show();
                awardDust(blueAtomValue);
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
        TickCloak(Time.deltaTime);

        // check if atom is captrured and its time to reduce it.
        if (atomCheck && invTimer <= 0)
        {
            // turn shields off
            ShipShield.For(gameObject).Hide();
            boost.SetActive(false);
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
