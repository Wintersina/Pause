using UnityEngine;
using UnityEngine.UI;
using System.Collections;


public class collisionDetection : MonoBehaviour {

    public static bool atomCheck;


    private static float savedTimer;
    public static float invTimer;
    private float boostTimer;
    public Text atomTimerText;
    public Text hypeText;
    public Text boostText;
    private int atomCounter;
    public static int lifeCounter;
    public static int MAXLIFE;

    public GameObject shield;

    [Tooltip("How far the shield's edge sits past the hull, as a fraction " +
             "of the hull's own radius. 0 would hug the hull exactly.")]
    public float shieldPadding = 0.35f;
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

    // The shield prefab was authored at a fixed size per ship and had drifted
    // out of sync -- one ship's bubble measured twice the world size of every
    // other ship's despite an identical padding intent. Sized here instead
    // from the hull's own current sprite bounds, so it always matches
    // whatever ship (and whatever future ship) is actually equipped.
    //
    // Ships 1-3 (the original damage-frame ships) start with no sprite baked
    // into the prefab at all -- lifeControler assigns it in its own Start(),
    // and Unity does not guarantee that runs before this one on the same
    // object. Polling for a frame or two rather than reading it once in
    // Start() avoids depending on component execution order.
    IEnumerator FitShieldToHullWhenReady()
    {
        var hullSprite = GetComponent<SpriteRenderer>();
        float giveUp = 1f;
        while (hullSprite != null && hullSprite.sprite == null && giveUp > 0f)
        {
            giveUp -= Time.unscaledDeltaTime;
            yield return null;
        }
        FitShieldToHull();
    }

    void FitShieldToHull()
    {
        if (shield == null) return;
        var hullSprite = GetComponent<SpriteRenderer>();
        var shieldRenderer = shield.GetComponent<SpriteRenderer>();
        if (hullSprite == null || hullSprite.sprite == null ||
            shieldRenderer == null || shieldRenderer.sprite == null) return;

        float parentWorldScale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y);
        float neededLocalScale = ComputeShieldLocalScale(
            hullSprite.sprite.bounds.extents, parentWorldScale,
            shieldRenderer.sprite.bounds.extents, shieldPadding);
        if (neededLocalScale <= 0f) return;

        shield.transform.localScale = new Vector3(neededLocalScale, neededLocalScale, shield.transform.localScale.z);
    }

    // Pure and testable without Play mode: extents are each sprite's own
    // unscaled local bounds (Sprite.bounds.extents), hullParentWorldScale is
    // the hull's transform.lossyScale (which the shield, as its child,
    // inherits before its own localScale multiplies in).
    public static float ComputeShieldLocalScale(
        Vector2 hullLocalExtents, float hullParentWorldScale,
        Vector2 shieldLocalExtents, float padding)
    {
        float hullLocalR = Mathf.Max(hullLocalExtents.x, hullLocalExtents.y);
        float hullWorldR = hullLocalR * hullParentWorldScale;
        float targetWorldR = hullWorldR * (1f + padding);

        float shieldLocalR = Mathf.Max(shieldLocalExtents.x, shieldLocalExtents.y);
        if (shieldLocalR <= 0f || hullParentWorldScale <= 0f) return 0f;

        return targetWorldR / (shieldLocalR * hullParentWorldScale);
    }

	void Start () {

        MAXLIFE = 3;
        // Fills the needed componets for this player.
        atomTimerText = GameObject.Find("gotAtomText").GetComponent<Text>();
        hypeText = GameObject.Find("hypeText").GetComponent<Text>();
        boostText = GameObject.Find("boostText").GetComponent<Text>();
        //destructionComboText = GameObject.Find("DestructionText").GetComponent<Text>();
        boost = GameObject.FindGameObjectWithTag("boost");
        boost.gameObject.SetActive(false);
        boostSound = GameObject.Find("RocketsSound").GetComponent<AudioSource>();
        astroidExpSound = GameObject.Find("AstroidExplotionSound").GetComponent<AudioSource>();

        StartCoroutine(FitShieldToHullWhenReady());
        shield = ShipShieldBubble.For(gameObject).Visual;


        //destructionComboText.gameObject.SetActive(false);
    

        //source.clip = boostSound;

        // making sure atom is not active until player picks it up
        atomCheck = false;

        // empty out any text or counters
        atomTimerText.text = "";
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
            if (PrefabName.Is(hit.gameObject, "mine"))
            {
                PlayExplosion();
                GameObject RedExp = ScrollWithWorld(Instantiate(redExp, hit.gameObject.transform.position, hit.gameObject.transform.rotation) as GameObject);
                Destroy(RedExp, 2);
            }

            if (atomCheck)
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
                // show the texts for only half of a second.
                savedTimer = .4f;

                // create explotion and show it on the objets position.
                GameObject exp = Instantiate(explosionAnimation) as GameObject;
                exp.transform.position = hit.gameObject.transform.position;
                ScrollWithWorld(exp);
                PlayExplosion();

                Destroy(exp, 2);
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
                // repairs one point of hull damage; lifeControler picks the
                // sprite back up from lifeCounter on the next frame
                if (lifeCounter > 0) lifeCounter--;
                if (hypeText != null) hypeText.text = "REPAIRED";
                BoostUltimate(dust: false);
                Destroy(hit.gameObject);
            }
            else if (PrefabName.Is(hit.gameObject, "pauseAtom"))
            {
                score.incromentPause();
                BoostUltimate(dust: false);
                Destroy(hit.gameObject);
            }
            else if(PrefabName.Is(hit.gameObject, "atom3a"))
            {
                BoostUltimate(dust: false);
                boostSound.Play();
                // ---------------------------
                //   Music control section!
                musicControl.boostMusicChanger = true;
                // ----------------------------

                ShipShieldBubble.For(gameObject).Show(shieldPadding);
                awardDust(blueAtomValue);
                atomTimerText.text = "0.00";
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
        if (atomCheck)
        {
            atomTimerText.text = invTimer.ToString("F2");     
           
        }

        // check if atom is captrured and its time to reduce it.
        if (atomCheck && invTimer <= 0)
        {
            // turn shields off
            ShipShieldBubble.For(gameObject).Hide();
            boost.SetActive(false);
            // let player know shild is off
            atomTimerText.text = "";
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
