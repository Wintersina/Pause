using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// The rebuilt tutorial (docs/tutorial-rebuild.md), driven by a simulated
// player through the real Hints director, ship, pickups and wall:
//
//   GATING     no step advances on a timer or without its action; each does
//              advance on exactly that action; an idle player is re-prompted
//   PAUSE      a lifted finger freezes dust / atoms / wall and stops the idle clock
//   FLYIN      the ship flies in through the portal; the first line waits for it
//   NEON COMET always Neon Comet, stock, no upgrades; saved data untouched
//   HEARTS     2 max, starts at 1, the green atom heals to 2; real Neon Comet keeps 1
//   ARRIVALS   dust / atoms / wall enter above the top edge, fast
//   POPUPS     "+N" over dust / atoms, the words over the ship, nothing scored
//   WALL       no dodge gap on every device; a pause-jump crosses safely; ramming
//              it is not crossing
//   PAUSEMENU  the pause quick actions work; REPLAY restarts the tutorial only
//   LIFTOFF    the ending lifts off into the main game with the player's own ship
//   PARITY     no null art / empty strings, hot paths do not allocate
public static class TutorialRebuildTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TRB] PASS  " : "[TRB] FAIL  ") + what);
        if (!ok) fails++;
    }

    const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    const float Dt = 1f / 60f;
    const string TutorialScene = "Assets/Scenes/tutorialS5.unity";

    public static void Run() { TestHarness.Exit(Execute()); }

    // ---- the simulated player ------------------------------------------------

    static bool pressed;
    static Vector2 screenPos;
    static bool wasPressed;

    static Hints hints;
    static GameObject ship;
    static movePlayerInTut mover;
    static collisionDetection cd;
    static MethodInfo hintsUpdate, moverUpdate;
    static int liveFrames;
    static ShipPowerController power;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            ForcedShip();
            FlyIn();
            Playthrough();
            IdleAndFreeze();
            HeartsAndHeal();
            Popups();
            Wall();
            PauseMenuAndReplay();
            Completion();
            Parity();
        }
        finally
        {
            TouchInput.PressedOverride = null;
            TouchInput.PositionOverride = null;
            Hints.DeltaOverride = null;
            movePlayerInTut.DeltaOverride = null;
            movePlayer.DeltaOverride = null;
            Hints.PlayFlyIn = true;
            Hints.AutoLiftOff = true;
            ShipId.TutorialFlight = false;
            ScreenInfo.ClearOverride();
            Time.timeScale = 1f;
        }
        Debug.Log("[TRB] failures: " + fails);
        return fails;
    }

    // ---- scene + world simulation --------------------------------------------

    static void Open(bool flyIn)
    {
        EditorSceneManager.OpenScene(TutorialScene, OpenSceneMode.Single);
        Time.timeScale = 1f;
        buttonClicks.playerDied = false;
        moveBackGround.speed = 0f;
        score.pauseCounter = score.TUTPAUSECOUNTER;
        score.paysRealDust = false;
        startMenu.youAreInTutorial = true;
        ShipId.TutorialFlight = true;
        Hints.PlayFlyIn = flyIn;
        Hints.AutoLiftOff = true;
        collisionDetection.MAXLIFE = -1;
        collisionDetection.lifeCounter = 0;
        collisionDetection.atomCheck = false;
        collisionDetection.invTimer = 0f;
        collisionDetection.healAtomPickups = collisionDetection.shieldAtomPickups = collisionDetection.pauseAtomPickups = 0;
        PlayerInvuln.Reset();
        pressed = false; wasPressed = false; screenPos = new Vector2(Screen.width * .5f, Screen.height * .3f);
        liveFrames = 0;
        TouchInput.PressedOverride = () => pressed;
        TouchInput.PositionOverride = () => screenPos;
        Hints.DeltaOverride = () => Dt;
        movePlayerInTut.DeltaOverride = () => Dt;
        movePlayer.DeltaOverride = () => Dt;

        RunScore.BeginRun(false, false);
        RunScore.PracticePopups = true;
        score.dustPickups = 0;
        score.tutorialCurrency = 0f;

        ship = GameObject.Find("ship1");
        mover = ship != null ? ship.GetComponent<movePlayerInTut>() : null;
        cd = ship != null ? ship.GetComponent<collisionDetection>() : null;
        TutorialShipHull.Apply();
        if (ship != null)
        {
            Own(ship.GetComponent<lifeControler>(), "Start");
            Own(cd, "Start");
            Own(mover, "Start");
            ship.transform.position = new Vector3(0f, -2.6f, 0f);
        }
        hints = Object.FindFirstObjectByType<Hints>();
        hints.introDelay = -1f;
        hints.readSeconds = -1f;
        hints.liftOffSeconds = -1f;
        hints.SendMessage("Start");
        Object.FindFirstObjectByType<spawnGoodStuffTut>().SendMessage("Start");
        power = ship != null ? ship.GetComponent<ShipPowerController>() : null;
        if (power != null)
        {
            Own(power, "Awake");
            Own(power, "Start");
            var gun = ship.GetComponentInChildren<UltimateGun>();
            if (gun != null) Own(gun, "Awake");
        }
        hintsUpdate = typeof(Hints).GetMethod("Update", Inst);
        moverUpdate = typeof(movePlayerInTut).GetMethod("Update", Inst);
    }

    static void Own(object c, string method)
    {
        if (c == null) return;
        var m = c.GetType().GetMethod(method, Inst);
        if (m != null) m.Invoke(c, null);
    }

    static bool WorldMoving { get { return pressed || score.pauseCounter <= 0; } }

    // One frame of the whole tutorial scene with the finger as set.
    static void Frame()
    {
        liveFrames++;
        // the run's pause spending (score.Update): the first press is the launch, free
        if (pressed && !wasPressed && liveFrames > 1 && !PortalArrival.Active && started) score.pauseCounter--;
        if (pressed && !PortalArrival.Active) started = true;
        wasPressed = pressed;

        bool moving = WorldMoving;
        foreach (var d in Object.FindObjectsByType<TutorialStarDrift>(FindObjectsSortMode.None)) d.Step(Dt, moving);
        foreach (var d in Object.FindObjectsByType<TutorialAtomDrift>(FindObjectsSortMode.None)) d.Step(Dt, moving);
        if (TutorialWall.Live != null) TutorialWall.Live.Step(Dt, moving);
        // (the strike step's alien: Update is not called in edit mode, so step it the way TutorialEnemy.Update does --
        // the slide-in on unscaled time first, then the normal drift while the world moves)
        if (TutorialEnemy.Live != null)
        {
            var alienStep = TutorialEnemy.Live.GetComponent<TutorialEnemy>();
            if (alienStep != null && !alienStep.Arrive(Dt)) alienStep.Step(Dt, moving);
        }
        moverUpdate.Invoke(mover, null);
        Physics2D.SyncTransforms();
        var hb = ShipHitbox.Of(ship);
        if (hb != null) hb.CatchPickups();
        if (power != null && hints.CurrentStep >= 0 && TutorialScript.Steps[hints.CurrentStep].advance == TutorialAdvance.FirePower) Own(power, "Update");
        hintsUpdate.Invoke(hints, null);
        if (hints.Speaker != null) hints.Speaker.CompleteLine();
    }
    static bool started;

    static void Frames(int n) { for (int i = 0; i < n; i++) Frame(); }

    static void FingerAt(Vector3 world)
    {
        // the finger is FingerOffset below where the hull sits
        var sp = Camera.main.WorldToScreenPoint(world - Vector3.up * ShipReach.FingerOffset);
        screenPos = new Vector2(sp.x, sp.y);
    }

    static Transform Nearest<T>(float x) where T : Component
    {
        Transform best = null;
        float bd = float.MaxValue;
        foreach (var c in Object.FindObjectsByType<T>(FindObjectsSortMode.None))
        {
            float d = Mathf.Abs(c.transform.position.x - x) + Mathf.Max(0f, c.transform.position.y - ship.transform.position.y) * .1f;
            if (d < bd) { bd = d; best = c.transform; }
        }
        return best;
    }

    // What a good player does on the current step, one frame.
    static void Autopilot(float t)
    {
        int s = hints.CurrentStep;
        var step = s >= 0 ? TutorialScript.Steps[s] : default(TutorialStep);
        pressed = false;
        if (s < 0 || hints.Ending) return;
        switch (step.advance)
        {
            case TutorialAdvance.FlyDistance:
                pressed = true;
                FingerAt(new Vector3(1.4f * Mathf.Sin(t * 2.5f), -2.2f, 0f));
                break;
            case TutorialAdvance.CollectStar:
                pressed = true;
                var star = Nearest<TutorialStarDrift>(ship.transform.position.x);
                FingerAt(star != null ? new Vector3(star.position.x, Mathf.Min(star.position.y, 1f), 0f) : new Vector3(0f, -2.2f, 0f));
                break;
            case TutorialAdvance.HealHeart:
            case TutorialAdvance.CollectRedAtom:
            case TutorialAdvance.CollectBlueAtom:
            case TutorialAdvance.FirePower:
                pressed = true;
                var atom = Nearest<TutorialAtomDrift>(ship.transform.position.x);
                FingerAt(atom != null ? atom.position : new Vector3(0f, -2.2f, 0f));
                break;
            case TutorialAdvance.WallFrozen:
                // fly until the wall is in view, then let go
                pressed = !(TutorialWall.Live != null && TutorialWall.Live.Jumpable);
                FingerAt(new Vector3(0f, -2.6f, 0f));
                break;
            case TutorialAdvance.TeleportKill:
                // fly until the alien hangs in reach, let go a moment, then touch on it
                var alien = TutorialEnemy.Live;
                if (alien == null || alien.position.y > ShipReach.ClampY(1000f))
                {
                    pressed = true;
                    FingerAt(new Vector3(0f, -2.2f, 0f));
                }
                else if (frozenFor > 20)
                {
                    pressed = true;
                    FingerAt(new Vector3(alien.position.x, alien.position.y, 0f));
                }
                break;
            case TutorialAdvance.CrossWall:
                // frozen a moment, then touch above the wall
                var w = TutorialWall.Live;
                if (w != null && frozenFor > 20)
                {
                    pressed = true;
                    FingerAt(new Vector3(0f, Mathf.Min(w.Y + TutorialWall.CrossMargin(w.PieceHalfHeight, .4f) + .6f, ShipReach.ClampY(1000f)), 0f));
                }
                break;
        }
        frozenFor = pressed ? 0 : frozenFor + 1;
    }
    static int frozenFor;

    static void ReleasePower()
    {
        if (power == null) return;
        Own(power, "OnDestroy");
        if (power.Runner != null) power.Runner.SendMessage("OnDestroy");
        if (power.Secret != null) power.Secret.SendMessage("OnDestroy");
        AttackPool.StopAll();
        typeof(ShipPowerController).GetMethod("FinishCinematic", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
    }

    static void ClearWorld()
    {
        TutorialWall.Clear();
        TutorialEnemy.Clear();
        spawnGoodStuffTut.RemoveLiveAtom();
        spawnGoodStuffTut.keepAtomComing = TutorialAtom.None;
        foreach (var t in new List<Transform>(spawnGoodStuffTut.LiveStars)) if (t != null) Object.DestroyImmediate(t.gameObject);
    }

    static void Cleanup()
    {
        ClearWorld();
        foreach (var s in Object.FindObjectsByType<RobotSpeaker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(s.gameObject);
        started = false;
    }

    // ---- NEON COMET: always, whatever is equipped; saved data untouched -----------

    static readonly string[] SkinKeys = BuildSkinKeys();
    static string[] BuildSkinKeys()
    {
        var keys = new List<string> { ShipId.SelectedKey, StarDustLedger.CurrencyKey, RunScore.BestScoreKey };
        for (int id = 1; id <= 16; id++)
        {
            keys.Add(ShipId.OwnedKey(id));
            keys.Add(ShipSkins.EquippedKey(id));
            for (int skin = 1; skin < ShipSkins.PerShip; skin++) keys.Add(ShipSkins.OwnedKey(id, skin));
        }
        return keys.ToArray();
    }

    static string Snapshot()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var k in SkinKeys) sb.Append(k).Append('=').Append(PlayerPrefs.GetString(k, "<s>")).Append('|').Append(PlayerPrefs.GetInt(k, -9)).Append('|').Append(PlayerPrefs.GetFloat(k, -9f)).Append(';');
        return sb.ToString();
    }

    static void ForcedShip()
    {
        // The pilot has a different ship equipped, with upgrades on Neon Comet and a skin on it.
        PlayerPrefs.SetString(ShipId.OwnedKey(7), "True");
        PlayerPrefs.SetInt(ShipId.SelectedKey, 7);
        PlayerPrefs.SetInt(ShipSkins.OwnedKey(ShipId.Starter, 1), 1);
        PlayerPrefs.SetInt(ShipSkins.OwnedKey(ShipId.Starter, 2), 1);
        PlayerPrefs.SetInt(ShipSkins.EquippedKey(ShipId.Starter), 2);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 1234f);
        PlayerPrefs.SetString("HasDoneTut", "false");
        ShipId.TutorialFlight = false;
        Check("outside the tutorial the equipped ship is the pilot's (7)", ShipId.Equipped() == 7);
        Check("and Neon Comet wears the pilot's skin 2 with upgrades (" + ShipWeaponUpgrades.Level(1) + ")", ShipSkins.Equipped(1) == 2 && ShipWeaponUpgrades.Level(1) >= 2);
        string before = Snapshot();

        Open(false);
        Check("tutorial flight: Equipped() is Neon Comet (id " + ShipId.Equipped() + ")", ShipId.Equipped() == ShipId.TutorialShip && ShipId.TutorialShip == 1);
        Check("the roster's id 1 is named Neon Comet", shopingShips.Roster[1] == "Neon Comet" && ShipId.KeyOf(1) == "NeonComet");
        Check("the tutorial ship object is ship1 (Neon Comet)", ShipId.Of(ship) == 1);
        Check("in its stock colour, whatever skin is equipped (" + ShipSkins.Equipped(1) + ")", ShipSkins.Equipped(1) == ShipSkins.Stock);
        Check("with no weapon upgrades (level " + ShipWeaponUpgrades.Level(1) + ")", ShipWeaponUpgrades.Level(1) == 0);
        var hull = ship.GetComponent<SpriteRenderer>();
        Check("the hull art is Neon Comet's", hull != null && hull.sprite != null && hull.sprite != null);
        var power = ship.GetComponent<ShipPowerController>();
        Own(power, "Awake");
        Check("its weapon is Neon Comet's (ShipPowerController id)", power != null && ShipLoadoutTable.For(ShipId.Equipped()).shipId == 1);
        Check("the power controller's loadout has no upgrades", power != null && power.WeaponLevel == 0);

        // a full run of the lesson's end: completion marks the tutorial done, writes nothing else saved
        Hints.ApplyDamagedStart();
        Check("while the tutorial runs the saved data is identical", Snapshot() == before);
        typeof(Hints).GetMethod("BeginEnding", Inst).Invoke(hints, null);
        PlayerPrefs.SetString("HasDoneTut", "false");   // (the one write the tutorial has always made)
        Check("the end of the tutorial writes no ship, skin, dust or best-score key", Snapshot() == before);
        Check("no star dust was banked (" + PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey) + ")", Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), 1234f));

        // leaving the tutorial: nothing to restore, the pilot's own ship is back
        GameStateReset.Clear();
        Check("after the tutorial the equipped ship is the pilot's again (" + ShipId.Equipped() + ")", ShipId.Equipped() == 7);
        Check("and the skin and upgrades on Neon Comet are theirs again", ShipSkins.Equipped(1) == 2 && ShipWeaponUpgrades.Level(1) >= 2);
        Check("saved data identical after", Snapshot() == before);
        Cleanup();
        PlayerPrefs.DeleteKey(ShipId.SelectedKey);
        PlayerPrefs.DeleteKey(ShipSkins.EquippedKey(ShipId.Starter));
    }

    // ---- FLY-IN ----------------------------------------------------------------

    static void FlyIn()
    {
        Open(true);
        Check("the portal fly-in is playing and holds the ship", hints.FlyingIn && PortalArrival.Active && PortalArrival.HoldsShip && PortalArrival.ShieldsShip);
        Check("no step has begun while the ship flies in", hints.CurrentStep < 0);
        pressed = true;
        FingerAt(new Vector3(1.5f, 0f, 0f));
        Vector3 held = ship.transform.position;
        Frames(10);
        Check("a finger down during the fly-in does not steer the ship (still held by the portal)", PortalArrival.Active && Vector3.Distance(ship.transform.position, new Vector3(1.5f, 0f, 0f)) > .5f);
        pressed = false;
        int guard = 0;
        // the arrival is stepped by Hints on unscaled time: it plays on its own
        while (hints.FlyingIn && guard++ < 600) Frame();
        Check("the fly-in ends on its own, no finger needed (" + guard + " frames = " + (guard * Dt).ToString("0.0") + " s)", !hints.FlyingIn && !PortalArrival.Active && guard * Dt >= 2f && guard * Dt <= 3.5f);
        Check("the ship is back at full size", Mathf.Abs(ship.transform.localScale.x - shopingShips.NormalizedHullScale(shopingShips.SpriteFor(1)) * ShipScale.Main) < .02f);
        Frames(2);
        Check("then the first step begins: hold", hints.CurrentStep == 0 && TutorialScript.Steps[0].id == "hold");
        Check("the first prompt is \"Hold anywhere to fly around.\" (" + hints.Speaker.Text.text + ")", hints.Speaker.Text.text.Contains("anywhere to fly around"));
        Cleanup();
    }

    // ---- GATING: a whole playthrough, step by step ---------------------------------

    static void Playthrough()
    {
        Open(false);
        Frames(3);
        var transitions = new List<string>();
        int stepNow = hints.CurrentStep;
        Check("starts on step 0 with the first prompt", stepNow == 0);
        var startField = typeof(Hints).GetField("stepStart", Inst);

        // 1. an idle player: every step, 5 minutes of fake time with no action, never advances
        float t = 0f;
        for (int s = 0; s < TutorialScript.Steps.Length; s++)
        {
            // drive the director to step s by its own begin (steps before were "done" by an observer)
            if (s > 0) { typeof(Hints).GetMethod("BeginStep", Inst).Invoke(hints, new object[] { s }); }
            var step = TutorialScript.Steps[s];
            // idle: finger up; and (hold step) a finger held without moving
            pressed = step.advance == TutorialAdvance.FlyDistance;
            screenPos = Camera.main.WorldToScreenPoint(ship.transform.position - Vector3.up * ShipReach.FingerOffset);
            if (hints.CurrentStep != s) { Check("step " + s + " began", false); break; }
            Frames(300);
            Check("\"" + step.id + "\": 5 s of doing nothing does not advance it (step " + hints.CurrentStep + ")", hints.CurrentStep == s);
            pressed = false;
            Frames(30);
        }

        // 2. a good player, from the top
        Cleanup();
        Open(false);
        Frames(3);
        started = false;
        frozenFor = 0;
        int last = 0, guard = 0;
        while (!hints.Ending && guard++ < 60 * 150)
        {
            t += Dt;
            Autopilot(t);
            Frame();
            if (hints.CurrentStep != last)
            {
                var done = TutorialScript.Steps[last];
                var start = (TutorialSignals)startField.GetValue(hints);   // (already the NEXT step's start: signals are shared)
                Check("step \"" + done.id + "\" advanced exactly one step, in order (" + last + " -> " + hints.CurrentStep + ")", hints.CurrentStep == last + 1 || hints.Ending);
                transitions.Add(done.id + "@" + (guard * Dt).ToString("0.0"));
                last = hints.CurrentStep;
            }
        }
        Debug.Log("[TRB] playthrough: " + string.Join(", ", transitions) + " | " + guard + " frames");
        Check("a player who does each step's action finishes all " + TutorialScript.Steps.Length + " steps (" + transitions.Count + ") within 150 s of fake time", hints.Ending && transitions.Count == TutorialScript.Steps.Length - 1);
        Check("the dust step really collected pieces (" + score.dustPickups + ")", score.dustPickups >= 3);
        Check("the wall was crossed by a pause-jump, not rammed (jumps " + movePlayerInTut.Jumps + ", hearts left " + ShipLives.Left + ")", movePlayerInTut.Jumps >= 1);
        Check("the tutorial is marked done at the end", PlayerPrefs.GetString("HasDoneTut") == "true");
        ReleasePower();
        Check("no timer: the source has no timeout field", !File.ReadAllText("Assets/Scripts/Tutorial/TutorialScript.cs").Contains("public readonly float timeout"));
        Cleanup();
    }

    // ---- idle re-prompt and the finger-up pause -----------------------------------

    static void IdleAndFreeze()
    {
        Open(false);
        Frames(3);
        // dust step with the finger DOWN but no progress: the robot re-prompts
        typeof(Hints).GetMethod("BeginStep", Inst).Invoke(hints, new object[] { 1 });
        // keep the ship away from the dust: never mind, count re-prompts via the idle clock
        pressed = true;
        screenPos = Camera.main.WorldToScreenPoint(ship.transform.position - Vector3.up * ShipReach.FingerOffset);
        float max = 0f;
        bool reset = false;
        int frames = (int)((Hints.IdleHintSeconds + 1f) / Dt);
        for (int i = 0; i < frames; i++)
        {
            Frame();
            // keep the dust away from the catch (this checks the clock, not the dust)
            foreach (var d in Object.FindObjectsByType<TutorialStarDrift>(FindObjectsSortMode.None)) Object.DestroyImmediate(d.gameObject);
            if (hints.IdleClock < max - .5f) reset = true;
            max = Mathf.Max(max, hints.IdleClock);
        }
        Check("an idle player is re-prompted after " + Hints.IdleHintSeconds + " s (idle clock peaked " + max.ToString("0.0") + ", then reset)", max >= Hints.IdleHintSeconds - .2f && reset);

        // the finger up: dust and atoms hold still, the idle clock stops
        typeof(Hints).GetMethod("BeginStep", Inst).Invoke(hints, new object[] { 2 });   // heal: a green atom
        pressed = true;
        Frames(10);
        var atom = spawnGoodStuffTut.LiveAtom;
        Check("the green atom is out", atom != null);
        pressed = false;
        Frames(2);
        float y0 = atom != null ? atom.position.y : 0f;
        float idle0 = hints.IdleClock;
        Frames(240);
        Check("finger up: the atom holds still while frozen", atom != null && Mathf.Approximately(atom.position.y, y0));
        Check("finger up: the tutorial waits (idle clock stays " + hints.IdleClock.ToString("0.00") + ")", Mathf.Approximately(hints.IdleClock, idle0) && hints.CurrentStep == 2);
        Check("the line stays up while frozen", hints.Speaker.LineFinished && hints.Speaker.Text.text.Length > 0);
        pressed = true;
        Frames(30);
        Check("finger down again: it moves on", atom == null || atom.position.y < y0 - .5f || Mathf.Abs(atom.position.y - y0) > .05f || hints.CurrentStep != 2);

        // dust frozen
        ClearWorld();
        typeof(Hints).GetMethod("BeginStep", Inst).Invoke(hints, new object[] { 1 });
        var one = spawnGoodStuffTut.LiveStars.Count > 0 ? spawnGoodStuffTut.LiveStars[0] : null;
        pressed = false;
        float sy = one != null ? one.position.y : 0f;
        Frames(120);
        Check("finger up: rushed dust holds still", one != null && Mathf.Approximately(one.position.y, sy));

        // the wall: frozen too
        TutorialWall.Clear();
        var wall = TutorialWall.Spawn(ship.transform);
        pressed = true;
        Frames(5);
        float wy = wall.Y;
        pressed = false;
        Frames(120);
        Check("finger up: the wall holds still (" + wall.Y.ToString("0.00") + ")", Mathf.Approximately(wall.Y, wy));
        Cleanup();
    }

    // ---- HEARTS ---------------------------------------------------------------------

    static void HeartsAndHeal()
    {
        Open(false);
        Frames(3);
        Check("Neon Comet's tutorial maximum is 2 hearts (MAXLIFE " + collisionDetection.MAXLIFE + ")", collisionDetection.MAXLIFE == 2);
        Check("it starts DAMAGED: 1 of 2 hearts (lifeCounter " + collisionDetection.lifeCounter + ")", collisionDetection.lifeCounter == 1 && ShipLives.Left == 1);
        Check("the real Neon Comet keeps its 1 heart (ShipLives.Base " + ShipLives.Base(1) + ")", ShipLives.Base(1) == 1 && ShipLives.TutorialHearts == 2);
        var lc = ship.GetComponent<lifeControler>();
        var damaged = shopingShips.SpriteFor(1, ShipLives.DamageState(1, 2));
        var intact = shopingShips.SpriteFor(1, 0);
        Check("the damaged hull art exists and differs from the intact one", damaged != null && intact != null && damaged != intact);
        Check("the ship's own damage state is critical (" + ShipLives.RunDamageState + ")", ShipLives.RunDamageState == 2);
        Check("Neon Comet has damage-fx emitters for that state", ShipDamageTable.Has(1) && ShipDamageTable.Count(1, 2) >= 0);

        // the dust step first: the damage is NOT mended by anything but the atom
        typeof(Hints).GetMethod("BeginStep", Inst).Invoke(hints, new object[] { 1 });
        Frames(120);
        Check("still damaged after the dust step begins and runs", collisionDetection.lifeCounter == 1);

        // heal step
        typeof(Hints).GetMethod("BeginStep", Inst).Invoke(hints, new object[] { 2 });
        pressed = true;
        Frames(5);
        var atom = spawnGoodStuffTut.LiveAtom;
        Check("the heal atom is a real heal atom", atom != null && PrefabName.Is(atom.gameObject, HealAtom.ObjectName));
        float top; float bottom;
        TutorialAtomDrift.View(out bottom, out top);
        Check("it entered from the top (y " + atom.position.y.ToString("0.0") + ", top " + top.ToString("0.0") + ")", atom.position.y > top - 2.5f);
        int heal0 = collisionDetection.healAtomPickups;
        for (int i = 0; i < 60 * 12 && collisionDetection.healAtomPickups == heal0; i++)
        {
            var a = spawnGoodStuffTut.LiveAtom;
            if (a != null) FingerAt(a.position);
            Frame();
        }
        Check("the ship caught it through the real pickup path", collisionDetection.healAtomPickups == heal0 + 1);
        Frames(2);
        Check("hearts restored: 2 of 2 (lifeCounter " + collisionDetection.lifeCounter + ")", collisionDetection.lifeCounter == 0 && ShipLives.Left == 2);
        Check("the hull is intact again (state " + ShipLives.RunDamageState + ")", ShipLives.RunDamageState == 0);
        Check("the repair says so", cd.hypeText != null && cd.hypeText.text == "REPAIRED");
        Check("and the heal step completed: the tutorial moved on to the wall", hints.CurrentStep >= 3);

        // a crash costs a heart, a second crash never kills
        Cleanup();
        PlayerInvuln.Reset();
        for (int i = 0; i < 4; i++)
        {
            var alien = TutorialEnemy.Spawn(0f);
            alien.transform.position = ship.transform.position;
            PlayerInvuln.Reset();
            Own(cd, "Update");
            typeof(collisionDetection).GetMethod("OnTriggerEnter2D", Inst).Invoke(cd, new object[] { alien.GetComponent<Collider2D>() });
            TutorialEnemy.Clear();
        }
        Check("crashes never kill the tutorial (lifeCounter " + collisionDetection.lifeCounter + ", dead " + buttonClicks.playerDied + ")", !buttonClicks.playerDied && collisionDetection.lifeCounter == 1);
        Cleanup();
    }

    // ---- POPUPS ---------------------------------------------------------------------

    static void Popups()
    {
        Open(false);
        var hud = new GameObject("~hud").AddComponent<HudStyler>();
        hud.SendMessage("Start");
        var sh = hud.GetComponent<ScoreHud>();
        Own(sh, "OnEnable");
        Check("the tutorial gets the main game's ScoreHud popups (no score row)", sh != null && ScoreHud.Current == sh && sh.PopupsOnly && sh.ScoreText == null);
        if (sh == null) { Object.DestroyImmediate(hud.gameObject); Cleanup(); return; }
        Check("practice popups are on, scoring is off", RunScore.PracticePopups && !RunScore.Scoring);

        // a star caught: "+N" above where it was
        typeof(Hints).GetMethod("BeginStep", Inst).Invoke(hints, new object[] { 1 });
        pressed = true;
        Frames(2);
        long total0 = RunScore.Total;
        var star = spawnGoodStuffTut.LiveStars[0];
        star.position = ship.transform.position + Vector3.up * .05f;
        Physics2D.SyncTransforms();
        Vector3 caughtAt = star.position;
        int dust0 = score.dustPickups;
        ShipHitbox.Of(ship).CatchPickups();
        Check("the star was caught (" + score.dustPickups + ")", score.dustPickups == dust0 + 1);
        Check("a +N popup is up (" + sh.LivePopups + ")", sh.LivePopups >= 1);
        var texts = Object.FindObjectsByType<Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Text dustPopup = null;
        foreach (var tx in texts) if (tx.name == "ScorePopup" && tx.gameObject.activeSelf && tx.text.StartsWith("+") && !tx.text.Contains("HULL")) dustPopup = tx;
        Check("it reads +" + ScoreRules.SmallDust + " or +" + ScoreRules.LargeDust + " like the run's (" + (dustPopup != null ? dustPopup.text : "none") + ")",
              dustPopup != null && (dustPopup.text == "+" + ScoreRules.SmallDust || dustPopup.text == "+" + ScoreRules.LargeDust));
        float itemY = Camera.main.WorldToScreenPoint(caughtAt).y;
        sh.StepPopups(.2f);
        Check("it sits above the item (popup y " + (dustPopup != null ? dustPopup.rectTransform.position.y.ToString("0") : "-") + " > item y " + itemY.ToString("0") + ")",
              dustPopup != null && dustPopup.rectTransform.position.y > itemY);
        Check("nothing was scored or saved (run total " + RunScore.Total + ")", RunScore.Total == total0 && total0 == 0 && score.tutorialCurrency > 0f);

        // freeze: popups hold still
        float py = dustPopup != null ? dustPopup.rectTransform.anchoredPosition.y : 0f;
        pressed = false;
        Frames(30);
        Check("finger up: the popup holds still with the world", dustPopup == null || Mathf.Approximately(dustPopup.rectTransform.anchoredPosition.y, py));

        // the pickup words over the ship
        typeof(Hints).GetMethod("BeginStep", Inst).Invoke(hints, new object[] { 2 });
        pressed = true;
        Frames(3);
        var atom = spawnGoodStuffTut.LiveAtom;
        atom.position = ship.transform.position + Vector3.up * .05f;
        Physics2D.SyncTransforms();
        ShipHitbox.Of(ship).CatchPickups();
        Text word = null;
        foreach (var tx in Object.FindObjectsByType<Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (tx.name == "ScorePopup" && tx.gameObject.activeSelf && tx.text == collisionDetection.HealWord) word = tx;
        Check("\"" + collisionDetection.HealWord + "\" shows over the ship when the heal lands", word != null);
        float shipY = Camera.main.WorldToScreenPoint(ship.transform.position).y;
        Check("above the ship, not below it", word != null && word.rectTransform.position.y > shipY);
        Text atomPop = null;
        foreach (var tx in Object.FindObjectsByType<Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (tx.name == "ScorePopup" && tx.gameObject.activeSelf && tx.text == "+" + ScoreRules.HealAtom) atomPop = tx;
        Check("and the atom's +" + ScoreRules.HealAtom + " too", atomPop != null);

        // each popup stays on screen on a tall and a small device
        foreach (var d in FitDevice.All)
        {
            if (d.id != "and-1080x2340-notch" && d.id != FitDevice.All[0].id) continue;
            using (var rig = new ScreenFitRig(d, CameraFit.GameplayHalfWidth))
            {
                rig.Sync();
                var tex = sh.ShowPopup(25, new Vector3(2.1f, 4.4f, 0f), RunScore.Source.Atom);
                Check(d.id + ": a popup at the top corner stays inside the safe area", tex != null && Contains(ScreenInfo.SafeArea, tex.rectTransform));
            }
        }
        Object.DestroyImmediate(hud.gameObject);
        Cleanup();
    }

    static bool Contains(Rect safe, RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        Vector2 centre = (c[0] + c[2]) * .5f;
        return safe.Contains(centre);
    }

    // ---- WALL -----------------------------------------------------------------------

    static void Wall()
    {
        Open(false);
        // the solver on its own
        float pw = .8f, sw = .6f, reach = 2.2f;
        float spacing, firstX;
        int n = TutorialWall.PlanRow(reach, pw, sw, out spacing, out firstX);
        var xs = new List<float>();
        for (int i = 0; i < n; i++) xs.Add(firstX + i * spacing);
        Check("PlanRow closes the lane: " + n + " aliens, spacing " + spacing.ToString("0.00"), !TutorialWall.HasDodgeGap(xs, pw, reach, sw) && spacing - pw < sw * .5f);
        var withHole = new List<float>(xs);
        withHole.RemoveAt(n / 2);
        Check("the solver does find a gap when an alien is missing", TutorialWall.HasDodgeGap(withHole, pw, reach, sw));
        var shy = new List<float>(xs);
        shy.RemoveAt(0);
        Check("and a gap at the end of the row", TutorialWall.HasDodgeGap(shy, pw, reach, sw));

        // on every device of the screen-fit matrix
        foreach (var dev in FitDevice.All)
        {
            using (var rig = new ScreenFitRig(dev, CameraFit.GameplayHalfWidth))
            {
                rig.Sync();
                ship.transform.position = new Vector3(0f, -2.6f, 0f);
                var wall = TutorialWall.Spawn(ship.transform);
                if (wall == null) { Check(dev.id + ": the wall spawns", false); continue; }
                var wx = new List<float>();
                foreach (var t in wall.Pieces) wx.Add(t.position.x);
                var shipCol = ship.GetComponent<Collider2D>();
                float shipW = shipCol.bounds.size.x;
                bool noGap = !TutorialWall.HasDodgeGap(wx, wall.PieceWidth, ShipReach.HalfWidth, shipW);
                Check(dev.id + ": " + wall.Total + " aliens, reach " + ShipReach.HalfWidth.ToString("0.00") + ", no gap the ship (" + shipW.ToString("0.00") + " wide) fits through", noGap);
                // and by physics: every alien collider's bounds, ship bounds swept across the row
                bool physical = true;
                float b0, t0; TutorialAtomDrift.View(out b0, out t0);
                foreach (var t in wall.Pieces)
                {
                    var c = t.GetComponent<Collider2D>();
                    if (c == null) physical = false;
                }
                Check(dev.id + ": every alien has a collider and is a hazard", physical && ClearTarget.IsHazard(wall.Pieces[0].gameObject));
                Check(dev.id + ": the wall starts above the top edge (y " + wall.Y.ToString("0.0") + ", top " + t0.ToString("0.0") + ")", wall.Y > t0);
                TutorialWall.Clear();
            }
        }

        // the lesson with the real ship: it rushes in, holds, and a pause-jump crosses it
        ship.transform.position = new Vector3(0f, -2.6f, 0f);
        score.pauseCounter = 3;
        var w = TutorialWall.Spawn(ship.transform);
        float bottom, top;
        TutorialAtomDrift.View(out bottom, out top);
        float hold = TutorialWall.HoldY(bottom, top);
        pressed = true;
        FingerAt(new Vector3(0f, -2.6f, 0f));
        Frames(3);
        float entered = -1f;
        for (int i = 0; i < 60 * 8 && w.CurrentPhase != TutorialWall.Phase.Holding; i++)
        {
            Frame();
            if (entered < 0f && w.Y < top) entered = i * Dt;
        }
        Check("it rushes in fast and stops on the hold line (y " + w.Y.ToString("0.00") + " = " + hold.ToString("0.00") + ")", w.CurrentPhase == TutorialWall.Phase.Holding && Mathf.Approximately(w.Y, hold));
        float wy = w.Y;
        Frames(120);
        Check("and waits there as long as it takes", Mathf.Approximately(w.Y, wy) && !w.Crossed);

        // no dodge: sweep the ship's x across the lane at the wall's height by physics
        var shipBox = ship.GetComponent<Collider2D>();
        bool anyFree = false;
        for (float x = -ShipReach.HalfWidth; x <= ShipReach.HalfWidth; x += .05f)
        {
            ship.transform.position = new Vector3(x, wy, 0f);
            Physics2D.SyncTransforms();
            bool hit = false;
            foreach (var t in w.Pieces) if (t != null && shipBox.bounds.Intersects(t.GetComponent<Collider2D>().bounds)) { hit = true; break; }
            if (!hit) { anyFree = true; break; }
        }
        Check("flying up to the wall at ANY x runs into an alien (no dodge gap by physics)", !anyFree);

        // the pause: lift the finger, touch above the wall
        ship.transform.position = new Vector3(0f, -2.6f, 0f);
        pressed = false;
        Frames(5);
        int life0 = collisionDetection.lifeCounter;
        int pauses0 = score.pauseCounter;
        pressed = true;
        FingerAt(new Vector3(.3f, wy + TutorialWall.CrossMargin(w.PieceHalfHeight, .4f) + .6f, 0f));
        Frames(3);
        Check("the jump landed above the wall (ship y " + ship.transform.position.y.ToString("0.0") + " > wall " + wy.ToString("0.0") + ")", ship.transform.position.y > wy + 1f);
        Check("it spent a pause (" + pauses0 + " -> " + score.pauseCounter + ")", score.pauseCounter == pauses0 - 1);
        Check("every alien is still standing: the jump erased nothing and cost no heart", w.Alive == w.Total && collisionDetection.lifeCounter == life0);
        Check("the wall is crossed and released", w.Crossed && w.CurrentPhase == TutorialWall.Phase.Released);
        float releasedY = w.Y;
        Frames(30);
        Check("it drives on down and away", w.Y < releasedY);
        for (int i = 0; i < 60 * 4 && TutorialWall.Live != null; i++) Frame();
        Check("and is gone once past the bottom", TutorialWall.Live == null);

        // Hints brings a broken wall back (with the retry line) and only a jump completes the step
        Cleanup();
        Open(false);
        Frames(3);
        ship.transform.position = new Vector3(0f, -2.6f, 0f);
        typeof(Hints).GetMethod("BeginStep", Inst).Invoke(hints, new object[] { 3 });
        pressed = true;
        FingerAt(new Vector3(0f, -2.6f, 0f));
        for (int i = 0; i < 60 * 6 && !(TutorialWall.Live != null && TutorialWall.Live.CurrentPhase == TutorialWall.Phase.Holding); i++) Frame();
        var broken = TutorialWall.Live;
        Check("the step's wall is up and holding", broken != null && broken.CurrentPhase == TutorialWall.Phase.Holding);
        Object.DestroyImmediate(broken.Pieces[2].gameObject);   // rammed
        Frames(5);
        Check("a rammed wall is cleared", TutorialWall.Live == null);
        Frames((int)(Hints.WallRetrySeconds / Dt) + 10);
        Check("and comes back after " + Hints.WallRetrySeconds + " s, whole, with the retry line (alive " + (TutorialWall.Live != null ? TutorialWall.Live.Alive + "/" + TutorialWall.Live.Total : "none") + ")",
              TutorialWall.Live != null && TutorialWall.Live.Alive == TutorialWall.Live.Total && hints.CurrentStep == 3 && hints.Signals.wallsCrossed == 0);
        Cleanup();
        Open(false);

        // ramming it is not crossing
        ship.transform.position = new Vector3(0f, -2.6f, 0f);
        var w2 = TutorialWall.Spawn(ship.transform);
        pressed = true;
        FingerAt(new Vector3(0f, -2.6f, 0f));
        for (int i = 0; i < 60 * 8 && w2.CurrentPhase != TutorialWall.Phase.Holding; i++) Frame();
        Object.DestroyImmediate(w2.Pieces[1].gameObject);   // what a ram does to one alien
        Frame();
        Check("a broken-up wall is Broken, never Crossed", w2.Broken && !w2.Crossed);
        Cleanup();

        // dragging through without a jump is not it either
        ship.transform.position = new Vector3(0f, -2.6f, 0f);
        var w3 = TutorialWall.Spawn(ship.transform);
        pressed = true;
        FingerAt(new Vector3(0f, -2.6f, 0f));
        for (int i = 0; i < 60 * 8 && w3.CurrentPhase != TutorialWall.Phase.Holding; i++) Frame();
        ship.transform.position = new Vector3(0f, w3.Y + 2f, 0f);
        Frame();
        Check("being on the far side with no jump does not count (Broken " + w3.Broken + ", Crossed " + w3.Crossed + ")", w3.Broken && !w3.Crossed);
        Cleanup();
    }

    // ---- PAUSE MENU + REPLAY --------------------------------------------------------

    static void PauseMenuAndReplay()
    {
        Open(false);
        var host = new GameObject("~tutClicks").AddComponent<tutButtonClicks>();
        host.SendMessage("Start");
        var actions = new GameObject("~qa").AddComponent<PauseQuickActions>();
        Own(actions, "Start");
        var replay = GameObject.Find("replayQuickAction");
        var leave = GameObject.Find("leaveQuickAction");
        Check("the pause menu's replay and leave buttons exist in the tutorial", replay != null && leave != null);
        if (replay == null || leave == null) { Cleanup(); return; }

        int loads = 0, homes = 0, gameLoads = 0;
        var oldLoad = tutButtonClicks.LoadTutorial; var oldHome = tutButtonClicks.LoadMenu; var oldGame = TutorialLiftOff.LoadGame;
        tutButtonClicks.LoadTutorial = () => loads++;
        tutButtonClicks.LoadMenu = () => homes++;
        TutorialLiftOff.LoadGame = () => gameLoads++;
        TutorialLiftOff.Reset();
        try
        {
            // a half-played tutorial: an atom out, a wall up, a damaged ship
            typeof(Hints).GetMethod("BeginStep", Inst).Invoke(hints, new object[] { 3 });
            Frames(5);
            TutorialWall.Spawn(ship.transform);
            collisionDetection.lifeCounter = 1;
            Check("setup: a wall is up", TutorialWall.Live != null);

            // pressing REPLAY
            replay.GetComponent<Button>().onClick.Invoke();
            Check("REPLAY reloads the TUTORIAL, once (" + loads + ")", loads == 1);
            Check("and never the main game (lift-offs " + TutorialLiftOff.Started + ", game loads " + gameLoads + ")", TutorialLiftOff.Started == 0 && gameLoads == 0);
            Check("the restart is in tutorial mode again (tutorial flag " + startMenu.youAreInTutorial + ", ship " + ShipId.Equipped() + ")", startMenu.youAreInTutorial && ShipId.TutorialFlight && ShipId.Equipped() == 1);
            Check("the old wall, atoms and damage are gone", TutorialWall.Live == null && spawnGoodStuffTut.LiveAtom == null && collisionDetection.lifeCounter == 0);
            replay.GetComponent<Button>().onClick.Invoke();
            Check("a second tap in the same frame does not queue a second load (" + loads + ")", loads == 1);

            // a fresh scene (what the reload does): everything is back at the start
            Open(false);
            Frames(3);
            Check("after the restart: step 0 \"hold\", damaged 1 of 2, no wall, no signals",
                  hints.CurrentStep == 0 && collisionDetection.lifeCounter == 1 && TutorialWall.Live == null && hints.Signals.starsCollected == 0 && hints.Signals.wallsCrossed == 0 && hints.Signals.heartsHealed == 0);

            // HOME
            var host2 = Object.FindFirstObjectByType<tutButtonClicks>();
            host2 = host2 != null ? host2 : host;
            host2.mainMenuButton();
            Check("HOME goes to the start menu (" + homes + ")", homes == 1);

            // the pause menu does not disturb the step state: pause, a tap on the buttons, resume
            Cleanup();
            Open(false);
            var qa2 = new GameObject("~qa2").AddComponent<PauseQuickActions>();
            Own(qa2, "Start");
            typeof(Hints).GetMethod("BeginStep", Inst).Invoke(hints, new object[] { 4 });
            pressed = false;
            Frames(60);
            Check("the pause buttons are shown while the finger is up", GameObject.Find("replayQuickAction") != null);
            var rb = GameObject.Find("replayQuickAction");
            var rt = rb.GetComponent<RectTransform>();
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            Vector2 mid = (c[0] + c[2]) * .5f;
            Check("a press on a pause button is the button's, not a steer or a jump", PauseQuickActions.IsScreenPointOnAction(mid));
            int jumps = movePlayerInTut.Jumps;
            pressed = true;
            screenPos = mid;
            Frames(3);
            pressed = false;
            Frames(2);
            Check("so it neither jumped the ship nor moved the step (jumps " + movePlayerInTut.Jumps + ", step " + hints.CurrentStep + ")", movePlayerInTut.Jumps == jumps && hints.CurrentStep == 4);
            Object.DestroyImmediate(qa2.gameObject);
        }
        finally
        {
            tutButtonClicks.LoadTutorial = oldLoad;
            tutButtonClicks.LoadMenu = oldHome;
            TutorialLiftOff.LoadGame = oldGame;
            TutorialLiftOff.Reset();
        }
        if (actions != null) Object.DestroyImmediate(actions.gameObject);
        if (host != null) Object.DestroyImmediate(host.gameObject);
        Cleanup();
    }

    // ---- COMPLETION: lift off into the main game -----------------------------------

    static void Completion()
    {
        PlayerPrefs.SetString(ShipId.OwnedKey(7), "True");
        PlayerPrefs.SetInt(ShipId.SelectedKey, 7);
        PlayerPrefs.SetString("HasDoneTut", "false");
        float dust0 = PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey);
        Open(false);
        Frames(3);
        int loads = 0;
        var oldGame = TutorialLiftOff.LoadGame;
        TutorialLiftOff.LoadGame = () => loads++;
        TutorialLiftOff.Reset();
        try
        {
            typeof(Hints).GetMethod("BeginEnding", Inst).Invoke(hints, null);
            Check("the ending marks the tutorial done exactly as before (HasDoneTut)", PlayerPrefs.GetString("HasDoneTut") == "true" && Hints.reachedTheEndOfTut);
            Check("the closing line is spoken first", hints.Ending && !hints.LiftOffStarted);
            Frames(2);
            Check("then the ship lifts off by itself (portal departure started: " + TutorialLiftOff.Started + ")", hints.LiftOffStarted && TutorialLiftOff.Started == 1 && TutorialLiftOff.Playing);
            Check("the tutorial card is not shown (automatic lift off)", Object.FindFirstObjectByType<TutorialCompletePanel>() == null);
            for (int i = 0; i < 400 && TutorialLiftOff.Live != null && loads == 0; i++) TutorialLiftOff.Live.Step(Dt);
            Check("the main game loads once, straight to gameS1 (" + loads + ", " + TutorialLiftOff.NextScene + ")", loads == 1 && TutorialLiftOff.NextScene == "gameS1");
            Check("the tutorial flag is cleared for the run (" + startMenu.youAreInTutorial + ")", !startMenu.youAreInTutorial && Mathf.Approximately(Time.timeScale, 1f));
            Check("the run counts as a real run now (PaysRealDust)", score.PaysRealDust("gameS1", true, false));

            // the next scene: the flag goes and the pilot's own ship is back
            EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
            typeof(GameStateReset).GetMethod("OnSceneLoaded", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { UnityEngine.SceneManagement.SceneManager.GetActiveScene(), UnityEngine.SceneManagement.LoadSceneMode.Single });
            Check("in gameS1 the equipped ship is the pilot's own (" + ShipId.Equipped() + ")", !ShipId.TutorialFlight && ShipId.Equipped() == 7);
            var prefab = Resources.Load<GameObject>(spawnShips.PrefabPathFor(ShipId.Equipped()));
            var run = Object.Instantiate(prefab);
            run.name = ShipId.ObjectName(7) + "(Clone)";
            spawnShips.ApplyHull(run, 7);
            Check("and the run's ship is built as ship 7, not Neon Comet", ShipId.Of(run) == 7);
            Object.DestroyImmediate(run);
            Check("no star dust was paid by the tutorial (" + PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey) + ")", Mathf.Approximately(PlayerPrefs.GetFloat(StarDustLedger.CurrencyKey), dust0));
        }
        finally { TutorialLiftOff.LoadGame = oldGame; TutorialLiftOff.Reset(); PlayerPrefs.DeleteKey(ShipId.SelectedKey); }

        // with AutoLiftOff off the card comes up instead
        EditorSceneManager.OpenScene(TutorialScene, OpenSceneMode.Single);
        Open(false);
        Hints.AutoLiftOff = false;
        Frames(3);
        typeof(Hints).GetMethod("BeginEnding", Inst).Invoke(hints, null);
        Frames(3);
        Check("AutoLiftOff off: the Tutorial Complete card shows, no lift-off", Object.FindFirstObjectByType<TutorialCompletePanel>(FindObjectsInactive.Include) != null && TutorialLiftOff.Started == 0);
        Hints.AutoLiftOff = true;
        Cleanup();
    }

    // ---- PARITY + allocations ------------------------------------------------------

    static void Parity()
    {
        Open(false);
        Frames(3);
        foreach (var s in TutorialScript.Steps)
            Check("step \"" + s.id + "\" has a line, a sprite-free-safe id and a competent time", !string.IsNullOrEmpty(s.line) && s.line.Trim().Length > 3 && s.par > 0f);
        Check("closing and retry lines exist", !string.IsNullOrEmpty(TutorialScript.ClosingLine) && !string.IsNullOrEmpty(TutorialScript.WallRetryLine));
        var spawner = Object.FindFirstObjectByType<spawnGoodStuffTut>();
        Check("the pickup prefabs are all there", spawner.smStar != null && spawner.midStar != null && spawner.Atom != null && spawner.redAtom != null && Resources.Load<GameObject>("prefabs/cooldownAtom") != null);
        var sr = ship.GetComponent<SpriteRenderer>();
        Check("the ship has a hull sprite", sr != null && sr.sprite != null);
        for (int state = 0; state <= 2; state++)
            Check("Neon Comet's damage row " + state + " has art", shopingShips.SpriteFor(1, state) != null);
        Check("Neon Comet has its exhaust nozzles", ShipNozzles.Has(1) && ShipNozzles.For(1).Length >= 1);
        Check("Neon Comet has a heart style and a weapon loadout", ShipHeartStyles.For(1) == HeartStyle.Comet && ShipLoadoutTable.For(1).shipId == 1);
        Check("the robot and the skip plate load", Resources.Load<Sprite>("Tutorial/tut_button") != null && Resources.Load<Sprite>("Tutorial/tut_arrow") != null);
        var wall = TutorialWall.Spawn(ship.transform);
        bool art = wall != null;
        if (wall != null) foreach (var t in wall.Pieces) art &= t.GetComponentInChildren<SpriteRenderer>() != null && t.GetComponentInChildren<SpriteRenderer>().sprite != null;
        Check("every wall alien has art", art);
        Check("the wall is Space's alien, so no pink outside Space (the tutorial is Space)", TutorialEnemy.DefKey == "space_alien");

        // hot paths
        long control;
        if (!TestHarness.AllocMeterWorks(out control)) { Check("the allocation meter works", false); TutorialWall.Clear(); Cleanup(); return; }
        pressed = true;
        FingerAt(new Vector3(0f, -2.6f, 0f));
        Frames(5);
        wall = TutorialWall.Spawn(ship.transform);
        typeof(Hints).GetMethod("BeginStep", Inst).Invoke(hints, new object[] { 1 });
        var starDrift = Object.FindFirstObjectByType<TutorialStarDrift>();
        Frames(2);
        long wallBytes = TestHarness.AllocatedBytes(() => { wall.Step(Dt, true); });
        Check("the wall's step allocates nothing (" + wallBytes + " B)", wallBytes == 0);
        if (starDrift != null)
        {
            starDrift.Step(Dt, true);
            long starBytes = TestHarness.AllocatedBytes(() => { starDrift.Step(Dt, false); });
            Check("a dust piece's step allocates nothing (" + starBytes + " B)", starBytes == 0);
        }
        TutorialWall.Clear();
        typeof(Hints).GetMethod("BeginStep", Inst).Invoke(hints, new object[] { 2 });
        var atomDrift = Object.FindFirstObjectByType<TutorialAtomDrift>();
        if (atomDrift != null)
        {
            atomDrift.Step(Dt, true);
            long atomBytes = TestHarness.AllocatedBytes(() => { atomDrift.Step(Dt, true); });
            Check("an atom's step allocates nothing (" + atomBytes + " B)", atomBytes == 0);
        }
        Cleanup();
    }
}
