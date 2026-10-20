using System;
using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// The boss's ENTRANCE (BossEntrance, BossEncounter's Intro), every boss:
//
//   * slow motion for ~4 s, eased back to 1x by the time the fight starts;
//     timeScale is back to normal on every way out -- the fight, the pilot's
//     death, a scene reset -- and a lifted finger still freezes the world
//   * the boss song starts on the entrance's first frame (not at the fight)
//     and AudioSource pitch is never tied to timeScale
//   * the boss rises out of the world's backdrop (a landing site of the
//     backdrop's own set pieces, or a far point in the sky), drawn behind
//     the near layers, small and hazed, and grows into the play layer
//   * it lands with a shockwave that clears the board: hazards die as
//     FRIENDLY FIRE (the hostile-fire attribution, the word, no pilot
//     credit), elites in play go down, hostile shots pop
//   * the pilot is untouchable from the first frame to the fight, and the
//     guard never outlives the intro; the other invulnerabilities (post-hit
//     window, shield, Cloak) are left exactly as they were
//   * spawning stays suspended, scoring earns nothing in the entrance and
//     the fight / outro / achievements are unchanged
public static class BossEntranceTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ENTRANCE] PASS  " : "[ENTRANCE] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            SlowMotionCurve();
            SlowMotionEndsWithTheFight();
            SlowMotionEndsOnDeath();
            SlowMotionEndsOnSceneReset();
            FingerUpStillFreezes();
            SongStartsOnTheFirstFrame();
            RisesOutOfTheBackdrop();
            RisesFromALandingSite();
            ShockwaveClearsTheBoardAsFriendlyFire();
            ShockwaveDownsElitesAndPopsShots();
            PilotIsUntouchableUntilTheFight();
            OtherInvulnerabilitiesUntouched();
            EveryBossHasAnEntrance();
            SharedCodeIsWired();
        }
        finally
        {
            BossEncounter.ResetRun();
            LandingSites.Override = null;
            FriendlyFire.WordHook = null;
            FriendlyFire.OnSceneLoaded("");
            buttonClicks.playerDied = false;
            Time.timeScale = 1f;
        }
        Debug.Log("[ENTRANCE] failures: " + fails);
        return fails;
    }

    // ---- fixtures ----------------------------------------------------------

    static void FreshScene(int world = 0)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        BossRails.Reset();
        PlayField.Reset();
        LandingSites.Override = null;
        ResumeSlowMo.ResetRun();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;   // the world runs without a touch in batch mode
        moveBackGround.speed = .37f;
        Time.timeScale = 1f;
        startMenu.youAreInTutorial = false;
        FriendlyFire.OnSceneLoaded("gameS1");
        FriendlyFire.ResetHostileCounters();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
    }

    static BossEncounter Begin(int world = 0)
    {
        BossEncounter.Begin(world, null);
        var e = BossEncounter.Instance;
        e.Step(.02f, 1f);   // Pending -> Intro
        return e;
    }

    static void RunTo(BossEncounter e, float introClock, float dt = .05f)
    {
        for (int i = 0; i < 4000 && e.State == BossEncounter.Phase.Intro && e.IntroClock < introClock; i++) e.Step(dt, 1f);
    }

    static void RunWhile(BossEncounter e, BossEncounter.Phase phase, float dt = .1f)
    {
        for (int i = 0; i < 2000 && e.State == phase; i++) e.Step(dt, 1f);
    }

    static GameObject Hazard(string name, string tag, Vector2 at)
    {
        var go = new GameObject(name, typeof(CircleCollider2D));
        go.tag = tag;
        go.transform.position = at;
        ClearTarget.Ensure(go).SetRadius(.3f);
        FriendlyFire.Settle(go);
        return go;
    }

    // ---- 1. slow motion ------------------------------------------------------

    static void SlowMotionCurve()
    {
        float slow = BossConfig.EntranceSlowScale;
        Check("the entrance slows the game right down (x" + slow + ")", slow > 0f && slow <= .5f);
        Check("it starts from full speed and is fully slowed after the ease in",
              BossEntrance.TimeScaleAt(0f) == 1f && Mathf.Approximately(BossEntrance.TimeScaleAt(BossConfig.SlowInSeconds + .01f), slow));
        Check("the slow motion holds for about four seconds (" + BossConfig.SlowOutAt + " s)",
              BossConfig.SlowOutAt >= 3.5f && BossConfig.SlowOutAt <= 4.5f &&
              Mathf.Approximately(BossEntrance.TimeScaleAt(BossConfig.SlowOutAt - .01f), slow));
        bool rising = true;
        float prev = slow;
        for (float t = BossConfig.SlowOutAt; t <= BossConfig.IntroSeconds; t += .02f)
        {
            float s = BossEntrance.TimeScaleAt(t);
            rising &= s >= prev - 1e-5f && s <= 1f;
            prev = s;
        }
        Check("then eases back up to 1x, never overshooting", rising);
        Check("and is exactly 1x when the fight begins (IntroSeconds = SlowOutAt + SlowOutSeconds)",
              BossEntrance.TimeScaleAt(BossConfig.IntroSeconds) == 1f &&
              Mathf.Approximately(BossConfig.IntroSeconds, BossConfig.SlowOutAt + BossConfig.SlowOutSeconds));
        Check("the boss lands (and the shockwave goes) inside the slow motion, before it eases out",
              BossConfig.LandAt > BossConfig.BossArriveAt && BossConfig.LandAt <= BossConfig.SlowOutAt &&
              BossConfig.LandAt + BossConfig.ShockwaveSeconds <= BossConfig.IntroSeconds);
    }

    static void SlowMotionEndsWithTheFight()
    {
        FreshScene();
        var bg = new GameObject("bg").AddComponent<moveBackGround>();
        var e = Begin();
        RunTo(e, 1.5f);
        bg.SendMessage("Update");
        Check("mid-entrance the world runs slowed (" + Time.timeScale.ToString("F2") + ")",
              BossEncounter.ScriptedEntrance && Mathf.Approximately(Time.timeScale, BossConfig.EntranceSlowScale));
        RunWhile(e, BossEncounter.Phase.Intro);
        bg.SendMessage("Update");
        Check("the fight begins at full speed", e.State == BossEncounter.Phase.Fight && Time.timeScale == 1f &&
              !BossEncounter.ScriptedEntrance && BossEncounter.EntranceTimeScale == 1f);
        Check("at the fight speed (HUD 20)", Mathf.Approximately(moveBackGround.speed, BossConfig.FightSpeed));
        Object.DestroyImmediate(bg.gameObject);
    }

    static void SlowMotionEndsOnDeath()
    {
        FreshScene();
        var bg = new GameObject("bg").AddComponent<moveBackGround>();
        var e = Begin();
        RunTo(e, 1f);
        buttonClicks.playerDied = true;
        e.Step(.02f, 1f);
        Check("a death mid-entrance aborts it", e.State == BossEncounter.Phase.Aborted);
        Check("... the slow motion and the guard go with it",
              !BossEncounter.ScriptedEntrance && BossEncounter.EntranceTimeScale == 1f && !BossEncounter.ShieldsShip);
        bg.SendMessage("Update");
        Check("... the death's own freeze takes over (the game-over panel works as ever)", Time.timeScale == 0f);
        GameStateReset.Clear();
        Check("... and leaving the run restores timeScale 1", Time.timeScale == 1f && !buttonClicks.playerDied);
        Check("the shockwave ring never outlives an abort", GameObject.Find("~BossShockwave") == null);
        Object.DestroyImmediate(bg.gameObject);
    }

    static void SlowMotionEndsOnSceneReset()
    {
        FreshScene();
        var e = Begin();
        RunTo(e, 1f);
        Check("entrance running before the scene goes", BossEncounter.ScriptedEntrance && BossEncounter.ShieldsShip);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);   // a Replay / Home: the encounter goes with the scene
        Check("a scene load ends the entrance, its slow motion and the guard",
              BossEncounter.Instance == null && !BossEncounter.ScriptedEntrance &&
              BossEncounter.EntranceTimeScale == 1f && !BossEncounter.ShieldsShip);
        Time.timeScale = 0f;   // as a frozen frame would leave it
        GameStateReset.Clear();   // what Replay / Home run on the way out
        Check("leaving the run mid-entrance restores timeScale 1", Time.timeScale == 1f);
    }

    static void FingerUpStillFreezes()
    {
        FreshScene();
        var bg = new GameObject("bg").AddComponent<moveBackGround>();
        int pauses = 3;
        score.pauseCounter = pauses;   // no finger in batch mode: a lifted finger with pauses left
        var e = Begin();
        RunTo(e, 1f);
        bg.SendMessage("Update");
        Check("a lifted finger freezes the world through the entrance too (the pause overlay case)", Time.timeScale == 0f);
        Check("... and costs no pause, the entrance is scripted", BossEncounter.FreePress && score.pauseCounter == pauses);
        RunWhile(e, BossEncounter.Phase.Intro);
        Check("the entrance still plays out on real time", e.State == BossEncounter.Phase.Fight);
        bg.SendMessage("Update");
        Check("after it the frozen world is the player's ordinary pause", Time.timeScale == 0f && score.pauseCounter == pauses);
        score.pauseCounter = 0;
        Object.DestroyImmediate(bg.gameObject);
    }

    // ---- 2. the song --------------------------------------------------------

    static void SongStartsOnTheFirstFrame()
    {
        FreshScene();
        var music = new GameObject("MovingMusic").AddComponent<AudioSource>();
        var clip = AudioClip.Create("world", 4410, 1, 44100, false);
        music.clip = clip;
        BossEncounter.Begin(0, null);
        var e = BossEncounter.Instance;
        Check("nothing before the encounter starts", !WorldMusic.BossTrackPlaying && !WorldMusic.BossFallbackActive);
        e.Step(.02f, 1f);
        Check("the boss song is on from the entrance's first frame (" + e.IntroClock.ToString("F2") + " s in), not the fight's",
              e.State == BossEncounter.Phase.Intro && e.IntroClock < .1f &&
              (WorldMusic.BossTrackPlaying || WorldMusic.BossFallbackActive));
        Check("the slow motion never touches the music's pitch (only the no-clip fallback nudge)",
              music.pitch == 1f || (WorldMusic.BossFallbackActive && Mathf.Approximately(music.pitch, WorldMusic.BossFallbackPitch)));
        string src = File.ReadAllText("Assets/Scripts/Worlds/WorldMusic.cs") + File.ReadAllText("Assets/Scripts/Audio/musicControl.cs");
        Check("no audio code ties pitch to timeScale", !System.Text.RegularExpressions.Regex.IsMatch(src, @"pitch\s*=[^;]*timeScale"));
        Check("musicControl keeps the song up through the entrance even with the finger up",
              File.ReadAllText("Assets/Scripts/Audio/musicControl.cs").Contains("TouchInput.IsPressed || BossEncounter.ScriptedEntrance"));
        Check("the BOSS INCOMING warning prewarms the boss song",
              File.ReadAllText("Assets/Scripts/Bosses/BossWarningHud.cs").Contains("WorldMusic.PrewarmBoss(WorldManager.Current)"));
        WorldMusic.EndBoss();
        Object.DestroyImmediate(music.gameObject);
        Object.DestroyImmediate(clip);
    }

    // ---- 3. out of the backdrop ---------------------------------------------

    static void RisesOutOfTheBackdrop()
    {
        FreshScene();
        var e = Begin();
        RunTo(e, BossConfig.BossArriveAt + .05f);
        var a = e.Actor;
        Check("the boss is on its way in", a != null && a.State == BossActor.Mode.Arriving);
        if (a == null) return;
        var o = a.ArrivalOrigin;
        Check("no backdrop sites (a bare scene): it rises from a far point in the sky, inside the view",
              !o.hasSite && o.position.y < CameraFit.ViewTop && o.position.y > CameraFit.ViewBottom);
        Check("... small, far off (x" + a.ArrivalScale.ToString("F2") + ")", a.ArrivalScale < .3f);
        Check("... drawn among the backdrop's layers, under the play layer (order " + a.BodySortingOrder + ")",
              a.BodySortingOrder <= BackdropCatalog.BaseOrder + 200 && a.BodySortingOrder < BossActor.ApproachOrder);
        Check("... hazed like the scenery", a.BodyColor.r < .9f);
        float prevScale = a.ArrivalScale;
        bool grows = true;
        for (int i = 0; i < 400 && !a.ArrivalLanded; i++)
        {
            e.Step(.05f, 1f);
            grows &= a.ArrivalScale >= prevScale - 1e-4f || a.ArrivalProgress01 > BossActor.PlayShare;
            prevScale = a.ArrivalScale;
        }
        Check("it grows as it approaches", grows);
        Check("it lands at its resting place, at play size, in the play layer, in full colour",
              a.ArrivalLanded && Mathf.Abs(a.transform.position.y - BossConfig.BossY) < .01f &&
              Mathf.Approximately(a.ArrivalScale, 1f) && a.BodySortingOrder == BossActor.PlayOrder && a.BodyColor == Color.white);
        Check("the landing is the shockwave's moment", e.Landed && e.Shockwave != null);
        RunWhile(e, BossEncounter.Phase.Intro);
        Check("the fight starts with the boss in place at play size",
              e.State == BossEncounter.Phase.Fight && a.transform.localScale == Vector3.one && a.BodyHitbox != null);
    }

    static void RisesFromALandingSite()
    {
        FreshScene();
        var piece = new GameObject("~station");
        piece.transform.position = new Vector3(.8f, 2.4f, 0f);
        var low = new GameObject("~lowpad");
        low.transform.position = new Vector3(0f, -2f, 0f);
        LandingSites.Override = into =>
        {
            into.Add(new LandingSite { anchor = low.transform, local = Vector3.zero, scale = .4f, order = -455, id = 1, kind = LandingKind.Ground });
            into.Add(new LandingSite { anchor = piece.transform, local = new Vector3(0f, .1f, 0f), scale = .3f, order = -405, id = 2,
                                       kind = LandingKind.Station, emerge = true });
        };
        var e = Begin();
        RunTo(e, BossConfig.BossArriveAt + .02f);
        var a = e.Actor;
        var o = a.ArrivalOrigin;
        Check("with sites in view it rises out of the backdrop piece highest up the screen",
              o.hasSite && o.site.id == 2 && Mathf.Abs(o.position.y - 2.5f) < .01f);
        Check("... at that piece's depth and draw order (" + o.scale.ToString("F2") + ", " + a.BodySortingOrder + ")",
              o.scale < .3f && a.BodySortingOrder == -405);
        Check("... out of its hangar: fading in from it", o.emerge && a.BodyColor.a < 1f);
        Check("... and not where the legacy warp started (above the view)", a.transform.position.y < CameraFit.ViewTop);
        LandingSites.Override = null;
        Object.DestroyImmediate(piece);
        Object.DestroyImmediate(low);
    }

    // ---- 4. the shockwave ----------------------------------------------------

    static void ShockwaveClearsTheBoardAsFriendlyFire()
    {
        FreshScene();
        int words = 0;
        FriendlyFire.WordHook = (w, at) => { if (w == "FRIENDLY FIRE") words++; };
        long scoreBefore = RunScore.Total;
        int discovered = Codex.DiscoveredCount;
        var e = Begin();
        var near = Hazard("rock_near", "Astr", new Vector2(0f, 2f));
        var far = Hazard("alien_far(Clone)", "Enimey", new Vector2(-2f, -4.5f));
        var above = Hazard("rock_above", "Astr", new Vector2(1f, 12f));   // not in view yet
        var pickup = new GameObject("atom3a(Clone)", typeof(CircleCollider2D));
        pickup.tag = "pickUp";
        ClearTarget.Ensure(pickup);
        RunTo(e, BossConfig.LandAt - .1f);
        Check("nothing is cleared before the boss lands (it lands into a live board)", near != null && far != null);
        RunTo(e, BossConfig.LandAt + .02f);
        var wave = e.Shockwave;
        Check("it lands: the shockwave is out", e.Landed && wave != null && wave.Visual != null);
        float r0 = wave != null ? wave.Radius : 0f;
        e.Step(.1f, 1f);
        Check("its front runs outward (" + r0.ToString("F2") + " -> " + (wave != null ? wave.Radius.ToString("F2") : "-") + ")",
              wave != null && (wave.Radius > r0 || wave.Finished));
        RunTo(e, BossConfig.LandAt + BossConfig.ShockwaveSeconds + .05f);
        Check("everything in view is gone when the front has passed", near == null && far == null);
        Check("... and the rest of the board too (above the view)", above == null);
        Check("pickups are spared", pickup != null);
        Check("every kill is FRIENDLY FIRE (" + FriendlyFire.HostileKills + " hostile kills, " + FriendlyFire.ShockwaveKills + " by the wave)",
              FriendlyFire.ShockwaveKills == 3 && FriendlyFire.HostileKills == 3 && FriendlyFire.Kills >= 3);
        Check("the FRIENDLY FIRE word shows above EVERY kill of the boss fight, not one a gap (" + words + " words for 3 kills)",
              BossEncounter.Running && words == 3 && FriendlyFire.Popups == 3);
        int before = words;
        for (int i = 0; i < 10; i++) FriendlyFire.Popup(new Vector3(i * .1f, 1f, 0f));
        Check("a mass kill is bounded per frame (" + (words - before) + " of 10 words, cap " + FriendlyFire.BossPopupsPerFrame + ")",
              words - before == FriendlyFire.BossPopupsPerFrame);
        Check("the pilot is paid nothing for them (no score, as any hostile-fire kill)",
              RunScore.Total == scoreBefore && !FriendlyFire.HostileFireAwardsPlayerCredit);
        Check("... and meets nothing new in the codex through them", Codex.DiscoveredCount == discovered);
        Check("the ring is put away", wave != null && wave.Finished && wave.Visual == null && GameObject.Find("~BossShockwave") == null);
        Check("the attribution is the hostile-fire source during the kill only", !FriendlyFire.HostileKillInProgress);
        if (pickup != null) Object.DestroyImmediate(pickup);
        FriendlyFire.WordHook = null;
    }

    static void ShockwaveDownsElitesAndPopsShots()
    {
        FreshScene();
        EliteDef def = null;
        foreach (var d in EliteCatalog.All) if (d != null) { def = d; break; }
        var e = Begin();
        EliteShip elite = def != null ? EliteShip.CreateInPlay(def, new Vector2(1f, 0f)) : null;
        if (elite != null) FriendlyFire.Settle(elite.gameObject);
        Check("an elite is in play when the boss comes", elite != null && elite.InPlay);
        RunWhile(e, BossEncounter.Phase.Intro);
        Check("the shockwave brings it down (friendly fire on an elite: " + FriendlyFire.ShockwaveEliteKills + ")",
              FriendlyFire.ShockwaveEliteKills == 1 && (elite == null || elite.State == EliteState.Dead));
        Check("... under the hostile-fire attribution (it lost its hearts as an elite hit by friendly fire)",
              elite == null || elite.LastHitCause == EliteDamage.FriendlyFire);
        if (elite != null) Object.DestroyImmediate(elite.gameObject);
        EliteSystem.Clear();

        // one in its grace (just hit) still goes down, through its ordinary death
        FreshScene();
        var e3 = Begin();
        EliteShip graced = def != null ? EliteShip.CreateInPlay(def, new Vector2(-1f, .5f)) : null;
        bool inGrace = false;
        if (graced != null)
        {
            FriendlyFire.Settle(graced.gameObject);
            if (graced.Hearts > 1) inGrace = graced.TakeHit(EliteDamage.Crash, graced.transform.position) && graced.Grace > 0f;
        }
        int kills = EliteShip.Kills;
        RunWhile(e3, BossEncounter.Phase.Intro);
        Check("an elite in its grace (or behind armour) still dies to the shockwave, by friendly fire, through its own death",
              !inGrace || (EliteShip.Kills == kills + 1 && EliteShip.LastKillCause == EliteDamage.FriendlyFire &&
                           FriendlyFire.ShockwaveEliteKills == 1));
        if (graced != null) Object.DestroyImmediate(graced.gameObject);
        EliteSystem.Clear();

        // hostile shots: a shot on the board pops
        FreshScene();
        var e2 = Begin();
        var shot = e2.Pool != null ? e2.Pool.Fire(e2.Boss, BossShotStyle.Bolt, new Vector3(-1f, -1f, 0f), Vector2.down * .01f) : null;
        Check("a hostile shot is on the board", shot != null && shot.Active);
        RunWhile(e2, BossEncounter.Phase.Intro);
        Check("hostile shots on the board pop (" + (e2.Shockwave != null ? e2.Shockwave.ShotsPopped : -1) + ")",
              shot != null && !shot.Active && e2.Shockwave != null && e2.Shockwave.ShotsPopped >= 1);
    }

    // ---- 5. the pilot is untouchable ------------------------------------------

    sealed class Ship
    {
        public GameObject go;
        public collisionDetection cd;
        public Action<Collider2D> trigger;

        public Ship()
        {
            go = new GameObject(ShipId.ObjectName(3) + "(Clone)", typeof(SpriteRenderer));
            go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            go.AddComponent<BoxCollider2D>().isTrigger = true;
            cd = go.AddComponent<collisionDetection>();
            cd.explosionAnimation = new GameObject("~TestExplosion");
            cd.boostSound = go.AddComponent<AudioSource>();
            cd.boostText = new GameObject("~boostText", typeof(RectTransform)).AddComponent<Text>();
            cd.hypeText = new GameObject("~hypeText", typeof(RectTransform)).AddComponent<Text>();
            cd.boost = new GameObject("~boost");
            cd.boost.SetActive(false);
            trigger = (Action<Collider2D>)Delegate.CreateDelegate(typeof(Action<Collider2D>), cd,
                typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst));
            collisionDetection.MAXLIFE = 3;
            collisionDetection.lifeCounter = 0;
            collisionDetection.atomCheck = false;
            collisionDetection.invTimer = 0f;
            collisionDetection.cloakTimer = 0f;
            PlayerInvuln.Reset();
        }

        public void Hit(string name, string tag)
        {
            var h = new GameObject(name, typeof(CircleCollider2D));
            h.tag = tag;
            h.transform.position = go.transform.position + Vector3.right * .3f;
            trigger(h.GetComponent<Collider2D>());
            if (h != null) Object.DestroyImmediate(h);
        }

        public void Dispose()
        {
            foreach (var o in new[] { cd.explosionAnimation, cd.boost, cd.boostText.gameObject, cd.hypeText.gameObject })
                if (o != null) Object.DestroyImmediate(o);
            if (go != null) Object.DestroyImmediate(go);
            collisionDetection.lifeCounter = 0;
            collisionDetection.atomCheck = false;
            collisionDetection.invTimer = 0f;
            collisionDetection.cloakTimer = 0f;
            PlayerInvuln.Reset();
            buttonClicks.playerDied = false;
        }
    }

    static void PilotIsUntouchableUntilTheFight()
    {
        FreshScene();
        var ship = new Ship();
        try
        {
            BossEncounter.Begin(0, null);
            var e = BossEncounter.Instance;
            e.Step(.02f, 1f);
            Check("untouchable from the entrance's first frame", BossEncounter.ShieldsShip && e.IntroClock < .1f);
            ship.Hit("rock", "Astr");
            ship.Hit("alien1(Clone)", "Enimey");
            ship.Hit("BossShot", "Enimey");
            Check("rocks, enemies and shots cost nothing in the entrance", collisionDetection.lifeCounter == 0 && !buttonClicks.playerDied);
            Check("... and it is not a shield, Cloak or post-hit window (none of them was started)",
                  !collisionDetection.atomCheck && !collisionDetection.Cloaked && !PlayerInvuln.Active);
            RunTo(e, BossConfig.LandAt + .3f);
            ship.Hit("rock2", "Astr");
            Check("still untouchable after it lands, until the fight begins", collisionDetection.lifeCounter == 0 && BossEncounter.ShieldsShip);
            RunWhile(e, BossEncounter.Phase.Intro);
            Check("the guard ends the moment the fight begins", e.State == BossEncounter.Phase.Fight && !BossEncounter.ShieldsShip);
            ship.Hit("rock3", "Astr");
            Check("... and hits count again in the fight", collisionDetection.lifeCounter == 1 && PlayerInvuln.Active);
        }
        finally { ship.Dispose(); }
    }

    static void OtherInvulnerabilitiesUntouched()
    {
        FreshScene();
        var ship = new Ship();
        try
        {
            collisionDetection.atomCheck = true;
            collisionDetection.invTimer = 3f;
            collisionDetection.cloakTimer = 1.5f;
            PlayerInvuln.Begin(1f);
            var e = Begin();
            RunWhile(e, BossEncounter.Phase.Intro);
            Check("a shield, Cloak and post-hit window running into the entrance are left exactly as they were",
                  collisionDetection.atomCheck && collisionDetection.invTimer == 3f && collisionDetection.cloakTimer == 1.5f &&
                  Mathf.Approximately(PlayerInvuln.Remaining, 1f));
            Check("the boss fight (and its achievements) start as ever", e.State == BossEncounter.Phase.Fight && e.HeartsLeft == BossConfig.Hearts);
        }
        finally { ship.Dispose(); }
    }

    // ---- 6. every boss ------------------------------------------------------

    static void EveryBossHasAnEntrance()
    {
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            FreshScene(w);
            var rock = Hazard("rock", "Astr", new Vector2(0f, 0f));
            BossEncounter.Begin(w, null);
            var e = BossEncounter.Instance;
            e.Step(.02f, 1f);
            bool slowed = BossEncounter.ScriptedEntrance && BossEncounter.ShieldsShip;
            RunTo(e, 1.5f);
            slowed &= Mathf.Approximately(BossEncounter.EntranceTimeScale, BossConfig.EntranceSlowScale);
            bool risen = e.Actor != null && e.Actor.State == BossActor.Mode.Arriving && e.Actor.ArrivalScale < 1f;
            RunWhile(e, BossEncounter.Phase.Intro);
            string name = WorldManager.Worlds[w].displayName;
            Check(name + ": slow motion, untouchable, rises from the backdrop, lands with a clearing shockwave, then the fight",
                  slowed && risen && e.Landed && rock == null && e.State == BossEncounter.Phase.Fight &&
                  !BossEncounter.ShieldsShip && BossEncounter.EntranceTimeScale == 1f);
            BossEncounter.ResetRun();
        }
    }

    // ---- 7. the shared code ---------------------------------------------------

    static void SharedCodeIsWired()
    {
        string collision = File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs");
        Check("collisionDetection honours the entrance's guard", collision.Contains("if (BossEncounter.ShieldsShip) return;"));
        string bg = File.ReadAllText("Assets/Scripts/Gameplay/moveBackGround.cs");
        Check("moveBackGround asks the entrance for its scale every frame (never stores it)",
              bg.Contains("Time.timeScale = BossEncounter.EntranceTimeScale;"));
        Check("the ultimate's charge holds through the entrance",
              File.ReadAllText("Assets/Scripts/Gameplay/Weapons/ShipPowerController.cs").Contains("TransitionFrozen || BossEncounter.ScriptedEntrance"));
        Check("spawning is still suspended for the whole encounter",
              File.ReadAllText("Assets/Scripts/Gameplay/Spawning/enmiesOnBoard.cs").Contains("if (flying && !BossEncounter.SuspendsSpawning)"));
        string shock = File.ReadAllText("Assets/Scripts/Bosses/BossEntrance.cs");
        Check("the shockwave is its own code (it does not lean on the shield's shockwave)", !shock.Contains("ShieldShockwave"));
    }
}
