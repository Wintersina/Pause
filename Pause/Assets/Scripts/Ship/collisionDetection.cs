using UnityEngine;
using UnityEngine.UI;
using System.Collections;


public class collisionDetection : MonoBehaviour {

    public static bool atomCheck;

    // Phase Cloak (SecretPowerController.BeginPhaseCloak; Shield Pulse and
    // Blink Dash borrow it for their brief invulnerability). Its own clock, separate from
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
    public static int healAtomPickups, shieldAtomPickups, pauseAtomPickups, cooldownAtomPickups;
    public static int MAXLIFE;

    public GameObject shield;
    public GameObject explosionAnimation;
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

    // Every way the pilot destroys a hazard ends here -- weapons, the
    // ultimate, secret powers, ramming it shielded, blinking onto it with the
    // pause-teleport -- so each pays the same: codex, secret meter, run score
    // (with the kill chain and speed multiplier, plus `bonusPoints`), the
    // kill achievements and a little star dust.
    public static void AwardDestroyedTarget(GameObject target, int bonusPoints = 0)
    {
        if (target == null || (!target.CompareTag("Enimey") && !target.CompareTag("Astr"))) return;
        // Score first: nothing below (a codex toast, the meter) may cost points.
        RunScore.OnKill(target, bonusPoints);   // run score (ScoreRules), with the kill chain
        Codex.Discover(target);   // ultimate kills count as meeting it too
        SecretPowerController.OnKill();   // kills fill the secret power's meter
        RecordKillAchievement(target);
        var player = Object.FindFirstObjectByType<collisionDetection>();
        if (player != null) player.awardDust(player.enemyDustValue);
    }

    // Kill 5/25/50/150/1000/3500 Aliens (##08-13), destroy 5/25/50/100/1500
    // Asteroids (##14-18). Used to fire for shielded rams only, so weapon,
    // ultimate and teleport kills never counted.
    public static void RecordKillAchievement(GameObject target)
    {
        if (target == null) return;
        if (PrefabName.Is(target, "alien1")) achievementAPICalls.alien_killed();
        if (target.CompareTag("Astr")) achievementAPICalls.asteroid_destroyed();
    }

    // Shaves time off the ultimate's countdown on pickup -- a little for
    // star dust, a lot more for an atom (blue shield and green heal atoms
    // count the same; the red pause atom cuts less -- secondsPerRedAtom).
    // No-ops outside gameS1, where there is no ShipPowerController to speed up.
    void BoostUltimate(bool dust, bool red = false, bool bright = false)
    {
        var power = ShipPowerController.Instance;
        if (power == null) return;
        power.ReduceTimer(bright ? power.secondsPerBrightStar
                        : dust ? power.secondsPerDust
                        : red ? power.secondsPerRedAtom : power.secondsPerAtom);
    }

	void Start () {

        // This ship's own lives (ShipLives: 2 to 5 by price tier).
        MAXLIFE = ShipLives.Max(ShipId.Of(gameObject, ShipId.Equipped()));
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
        PlayerInvuln.Reset();

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
        // A pickup already collected this step (ShipHitbox's pickup radius got
        // there first, or a second contact before Destroy lands) is spent.
        if (!hit.enabled) return;
        Codex.Discover(hit.gameObject);   // first touch unlocks its codex entry (enemy, rock, atom, portal)

        #region
        //------------------------- Colliding with Enimies ---------------------------------------------
        if (hit.gameObject.CompareTag("Enimey") || hit.gameObject.CompareTag("Astr"))
        {
            // Post-hit invulnerability (PlayerInvuln): unless a shield or
            // Cloak is also up, the ship passes through harmlessly -- no
            // heart, no ram kill, no secret power spent.
            if (PlayerInvuln.Active && !Invulnerable) return;
            // A full secret meter whose power answers a hit (Shield Pulse,
            // Phase Cloak, Blink Dash) spends itself now, and a Hard Shell
            // eats the hit: either way it lands as a shielded hit.
            bool safe = Invulnerable || SecretPowerController.InterceptHit(hit.gameObject);
            // The power may already have destroyed it (Shield Pulse): don't
            // blow it up, or pay for it, twice.
            if (safe && ShipAttackHits.AlreadyHit(hit.gameObject)) return;

            // creating different explotions for different enims
            // Under the boost shield (or Cloak) the player destroys the
            // mine, and the weapon explosion below covers it.
            // The last life: the ship breaks up and crashes into the rails
            // (DeathCrash), and the killer with it -- a mine tumbles into a
            // rail rather than bursting here.
            bool fatal = !safe && lifeCounter + 1 >= MAXLIFE;
            // (its burst frame, friendly-fire blast and explosion: RamKill, below)
            if (PrefabName.Is(hit.gameObject, "mine") && !safe && !fatal)
                EnemyDeathAudio.Play(hit.gameObject);

            if (safe)
            {
                // (kill achievements: AwardDestroyedTarget, below)
                // Shows the hit on the shield; a no-op under Cloak alone,
                // where no shield is up.
                ShipShield.For(gameObject).Absorb(hit.transform.position);
                // show the texts for only half of a second.
                savedTimer = .4f;
                // An elite rammed shielded loses both hearts; its shot is absorbed (EliteShip).
                if (EliteShip.ShieldRam(hit.gameObject, transform.position)) return;

                // The player destroyed it: the pooled cartoon target
                // explosion (metal / rock / mine), flashed in this ship's
                // weapon colour.
                TargetExplosion.Spawn(hit.gameObject, ShipId.Of(gameObject, ShipId.Equipped()));
                EnemyDeathAudio.Play(hit.gameObject);

                AwardDestroyedTarget(hit.gameObject);
                Destroy(hit.gameObject);
                
                
                
            
                // open memory and remove leftovers
            }
            else {
                // The target still breaks on an unshielded collision, even
                // though the player's own explosion is the visual focus.
                if (!PrefabName.Is(hit.gameObject, "mine")) EnemyDeathAudio.Play(hit.gameObject);
                lifeCounter += 1;
                // the heart this costs darts out to shield against it
                ShipLivesIndicator.Impact(hit.transform.position);
                //change sprite



                Vector3 shipPos = this.gameObject.transform.position;
                Quaternion shipRot = this.gameObject.transform.rotation;

                if (lifeCounter >= MAXLIFE)
                {
                    buttonClicks.playerDied = true;
                    //--------------------1st/5th/10th/50th/100th DEATH ---##01-04-----------------
                    achievementAPICalls.player_died();

                    achievementAPICalls.leaderboard_highest_speed_reached(Mathf.Round(moveBackGround.speed * 100));
                    // End of the run: flush the batched achievement counters.
                    PrefsSaver.SaveNow();
                    PlayExplosion();
                    // The crash sequence takes over: the hull breaks up, its
                    // pieces (and a solid killer) slam into the rails, and
                    // only then is the ship destroyed and the Flight Complete
                    // panel shown (DeathCrash.PanelReady).
                    DeathCrash.Begin(gameObject, hit.gameObject);
                }
                else
                {
                    GameObject exp = ScrollWithWorld(Instantiate(explosionAnimation, shipPos, shipRot) as GameObject);
                    PlayExplosion();
                    Destroy(exp, 2);
                    PlayerInvuln.BeginPostHit();   // a heart lost: 2 s of blinking i-frames
                }
                // An elite survives a non-fatal contact with a heart less
                // (EliteShip); on the fatal one it is the killer DeathCrash
                // tumbles into the rail, so it goes like any other. Anything
                // else rammed on a non-fatal hit dies like any kill -- its
                // own blast, a mine's burst, sometimes spinning pieces --
                // unpaid (RamKill).
                if (fatal) Destroy(hit.gameObject);
                else if (!EliteShip.Rammed(hit.gameObject, shipPos))
                {
                    RamKill.Blast(hit.gameObject, ShipId.Of(gameObject, ShipId.Equipped()));
                    Destroy(hit.gameObject);
                }

            }
        }
        #endregion
        //-------------------- PICK UP ITEMS, Such as STARS, and ATOMS ------------------------------------------
        else if (hit.gameObject.CompareTag("pickUp"))   // CompareTag: no string per contact
        {
            // Spent as it is collected: its collider goes off, so neither
            // route can pay for it twice.
            hit.enabled = false;
            // Every pickup pops in its own pixel-art burst where it was caught.
            PickupBurst.Play(hit.gameObject);


            if (PrefabName.Is(hit.gameObject, "smStar1") || PrefabName.Is(hit.gameObject, "LargeStar1"))
            {
                //--------------------PickUp Stars 150/1000-------##06-07--------------------
                achievementAPICalls.star_collected();
            }

                // calculate different scores for each items.
                if (PrefabName.Is(hit.gameObject, "smStar1"))
            {
                awardDust(smallStarValue);
                RunScore.OnDust(false, hit.transform.position);
                BoostUltimate(dust: true);
                SecretPowerController.OnDust(large: false);
                Destroy(hit.gameObject);
            }
            else if(PrefabName.Is(hit.gameObject, "LargeStar1"))
            {
                awardDust(largeStarValue);
                RunScore.OnDust(true, hit.transform.position);
                BoostUltimate(dust: true, bright: true);
                SecretPowerController.OnDust(large: true);
                Destroy(hit.gameObject);
            }
            else if (PrefabName.Is(hit.gameObject, HealAtom.ObjectName))
            {
                healAtomPickups++;
                RunScore.OnAtom(RunScore.Atom.Heal, hit.transform.position);
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
                RunScore.OnAtom(RunScore.Atom.Pause, hit.transform.position);
                score.incromentPause();
                BoostUltimate(dust: false, red: true);
                // ...and a free shot of the main weapon (the charge timer
                // keeps its progress; ShipPowerController.FreeShot).
                if (ShipPowerController.Instance != null) ShipPowerController.Instance.FreeShot();
                Destroy(hit.gameObject);
            }
            else if (PrefabName.Is(hit.gameObject, "cooldownAtom"))
            {
                cooldownAtomPickups++;
                RunScore.OnAtom(RunScore.Atom.Cooldown, hit.transform.position);
                // cuts min(12 s, what's left) off the charge; the word says
                // which: "WEAPON CHARGED" or "-12s CHARGE"
                if (ShipPowerController.Instance != null)
                {
                    string word = ShipPowerController.Instance.CollectCooldownAtom();
                    if (hypeText != null) hypeText.text = word;
                }
                Destroy(hit.gameObject);
            }
            else if(PrefabName.Is(hit.gameObject, "atom3a"))
            {
                shieldAtomPickups++;
                RunScore.OnAtom(RunScore.Atom.Shield, hit.transform.position);
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
                // A boss holds speed at 20: no +0.05 boost (BossEncounter).
                if (!BossEncounter.SpeedLocked) { moveBackGround.speed += .05f; atomCounter++; }
                invTimer = 5.8f;
                boostTimer = 1f;
            }
        }
        //------------------------------------------------------------------------------------------------------------------
    }

    // ShipHitbox's pickup radius reached a pickup: collected exactly as if it
    // had touched the hull. False if it was already spent.
    public bool CollectPickup(Collider2D hit)
    {
        if (hit == null || !hit.enabled || !hit.gameObject.CompareTag("pickUp")) return false;
        OnTriggerEnter2D(hit);
        return true;
    }
    // simply turns of the texts
    void turnTextsOff()
    {
        invTimer -= Time.deltaTime;
        savedTimer -= Time.deltaTime;
        boostTimer -= Time.deltaTime;
        TickCloak(Time.deltaTime);
        PlayerInvuln.Tick(Time.deltaTime);

        // check if atom is captrured and its time to reduce it.
        if (atomCheck && invTimer <= 0)
        {
            // turn shields off
            ShipShield.For(gameObject).Hide();
            boost.SetActive(false);
            atomCheck = false;
            moveBackGround.speed -= BossEncounter.FilterSpeedChange(.05f * atomCounter);
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
