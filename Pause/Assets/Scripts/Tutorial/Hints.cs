using UnityEngine;
using UnityEngine.UI;

// Runs the tutorial: the ship flies in through a portal (the run-start
// arrival, always Neon Comet, damaged), then walks TutorialScript.Steps in
// order. Every step sets its scene up (a touch hint, dust / an atom / the wall
// rushing in from the top), has the robot say its line, and moves on ONLY when
// the player has really done the thing -- there is no timer that advances a
// step. An idle player is re-prompted (IdleHintSeconds). With the finger up
// the world is frozen (moveBackGround) and the tutorial waits with it: its
// idle clock stops, the line and the touch pulse stay up.
//
// After the last step the ship lifts off into the real game (TutorialLiftOff),
// or, with AutoLiftOff off, the Tutorial Complete card comes up first.
//
// The ship carries the real weapon (ShipPowerController), held -- no
// countdown, no firing, no red-atom free shot -- until the power step arms
// it for the one violet atom, so the player sees it go off once.
//
// Everything here runs on unscaled time: the world sits at timeScale 0 every
// time the player lifts their finger, and that is exactly when the robot has
// the most to say.
public class Hints : MonoBehaviour {

    [Tooltip("Minimum time a line stays up after it has been fully spoken, " +
             "even if the player already did what it asks.")]
    public float readSeconds = .6f;

    [Tooltip("Delay between the fly-in ending and the robot's first line.")]
    public float introDelay = TutorialScript.IntroSeconds;

    [Tooltip("How long after the closing line is read before the ship lifts off.")]
    public float liftOffSeconds = TutorialScript.EndingSeconds;

    // The player has gone this long with no progress (and the world running,
    // or a step that wants the finger up): the robot says the line again.
    public const float IdleHintSeconds = 7f;
    // A broken-up wall comes back after this long.
    public const float WallRetrySeconds = 1f;
    // Pauses the player always has at the start of a step (so letting go
    // always freezes the tutorial, as in a run).
    public const int PausesPerStep = 2;
    // After the last step the ship flies on into the game by itself; false:
    // the Tutorial Complete card (LIFT OFF / HOME) comes up first.
    public static bool AutoLiftOff = true;
    // Play the fly-in portal (tests turn it off to start at the first step).
    public static bool PlayFlyIn = true;

    public static bool reachedTheEndOfTut;

    // The old still robot sprite and its typed text box. Both are hidden at
    // start; RobotSpeaker draws the robot and its bubble instead.
    public GameObject playerIcon;
    public movePlayerInTut script;
    public Text startHintText;

    GameObject liftOffLeft;
    GameObject ship;
    GameObject pausedIcon;

    RobotSpeaker speaker;
    Canvas speakerCanvas;
    TutorialGuides guides;
    TutorialSkip skip;
    SpokenLine[] lines;
    SpokenLine closingLine, retryLine;
    Text pauseReadout;
    RectTransform hudPanel;
    readonly Vector3[] corners = new Vector3[4];

    int step = -1;
    TutorialSignals signals, stepStart;
    bool sampling;
    bool lastPressed;
    int lastPauseCounter, lastDustPickups, lastHeal, lastShield, lastRed, lastFired, lastLife;
    ShipPowerController power;
    float startedAt;
    float endedAt = -1f;
    bool panelShown, liftStarted;
    bool damagedApplied, healed;
    PortalArrival flyIn;
    float idleClock, lastProgress;
    float wallRetryLeft = -1f;   // seconds until a broken wall comes back (-1: none pending)
    TutorialWall wall;
    bool wallCounted;

    public int CurrentStep { get { return step; } }
    public TutorialSignals Signals { get { return signals; } }
    public bool FlyingIn { get { return flyIn != null && flyIn.State != PortalArrival.Stage.Done; } }
    public bool Ending { get { return endedAt >= 0f; } }
    public float IdleClock { get { return idleClock; } }
    public bool LiftOffStarted { get { return liftStarted; } }
    public RobotSpeaker Speaker { get { return speaker; } }

    void Start() {

        liftOffLeft = GameObject.Find("LiftOffLeft");
        ship = GameObject.Find("ship1");
        if (ship != null) script = ship.GetComponent<movePlayerInTut>();
        pausedIcon = GameObject.Find("paused");
        if (ship != null)
        {
            power = ship.GetComponent<ShipPowerController>();
            if (power == null) power = ship.AddComponent<ShipPowerController>();
            power.chargeMode = ShipPowerController.ChargeMode.Held;
        }

        reachedTheEndOfTut = false;
        TutorialWall.Clear();

        if (playerIcon != null) playerIcon.SetActive(false);
        Font font = null;
        if (startHintText != null)
        {
            font = startHintText.font;
            startHintText.text = "";
            startHintText.gameObject.SetActive(false);
        }

        var steps = TutorialScript.Steps;
        lines = new SpokenLine[steps.Length];
        for (int i = 0; i < steps.Length; i++) lines[i] = TutorialScript.Speak(steps[i].line);
        closingLine = TutorialScript.Speak(TutorialScript.ClosingLine);
        retryLine = TutorialScript.Speak(TutorialScript.WallRetryLine);

        speaker = RobotSpeaker.Create(font);
        guides = TutorialGuides.Create(speaker.Root);

        pauseReadout = FindText("PausesRemainingText");
        hudPanel = TopPanelOf(pauseReadout);

        startedAt = Time.unscaledTime;

        // The ship flies in through the run-start portal (held and shielded
        // while it plays); the first line waits for it.
        if (PlayFlyIn && ship != null)
        {
            var theme = WorldManager.Worlds[0];
            flyIn = PortalArrival.SpawnDriven(ship.transform, theme.portalColor);
        }
    }

#if UNITY_EDITOR
    // Edit-mode tests step Update by hand; Time.unscaledDeltaTime is not real there.
    public static System.Func<float> DeltaOverride;
#endif
    static float Dt()
    {
#if UNITY_EDITOR
        if (DeltaOverride != null) return DeltaOverride();
#endif
        return Time.unscaledDeltaTime;
    }

    void Update() {

        if (speaker == null) return;
        float dt = Dt();
        Sample(dt);
        KeepClearOfTopUi();
        EnsureDamagedStart();

        if (FlyingIn)
        {
            flyIn.Step(dt);
            startedAt = Time.unscaledTime;
            return;
        }

        if (endedAt >= 0f)
        {
            RunEnding();
            return;
        }

        if (step < 0)
        {
            if (Time.unscaledTime - startedAt >= introDelay) BeginStep(0);
            return;
        }

        var current = TutorialScript.Steps[step];
        if (AtomFor(current.cue) != TutorialAtom.None) guides.PointAt(spawnGoodStuffTut.LiveAtom);
        else if (current.cue == TutorialCue.SpawnWall || current.cue == TutorialCue.PointAtWall) guides.PointAt(wall != null && wall.OnScreen && wall.Pieces.Count > 0 ? wall.Pieces[wall.Pieces.Count / 2] : null);
        // the wall still on its way in: a lifted finger gets the touch pulse (touch to bring it in)
        if (current.cue == TutorialCue.SpawnWall) guides.ShowTouch(wall == null || !wall.Jumpable);
        guides.PointAtStars(StarArrowsAllowed(current.cue) ? spawnGoodStuffTut.LiveStars : null);
        TickDust(dt);
        TickWall(dt);
        KeepHeartsRight();
        TickIdle(current, dt);

        if (TutorialScript.CanAdvance(current, stepStart, signals, speaker.LineFinished, speaker.SinceLineFinished, readSeconds))
        {
            EndStep(current);
            if (step + 1 < TutorialScript.Steps.Length) BeginStep(step + 1);
            else BeginEnding();
        }
    }

    // ---- What the player is doing ----

    void Sample(float dt)
    {
        // score sets the pause counter in its own Start; begin counting from
        // the first frame both have run.
        if (!sampling)
        {
            sampling = true;
            lastPauseCounter = score.pauseCounter;
            lastHeal = collisionDetection.healAtomPickups;
            lastShield = collisionDetection.shieldAtomPickups;
            lastRed = collisionDetection.pauseAtomPickups;
            lastDustPickups = score.dustPickups;
            lastPressed = TouchInput.IsPressed;
            lastFired = power != null ? power.UltimatesFired : 0;
            lastLife = collisionDetection.lifeCounter;
        }

        bool pressed = TouchInput.IsPressed && !buttonClicks.playerDied;
        bool moving = pressed || score.pauseCounter <= 0;
        if (pressed && !lastPressed) signals.presses++;
        lastPressed = pressed;
        signals.pressed = pressed;
        signals.worldMoving = moving;
        signals.flyUnits = movePlayerInTut.FlownUnits;
        signals.frozenSeconds = moving ? 0f : signals.frozenSeconds + dt;
        bool wallInView = TutorialWall.Live != null && TutorialWall.Live.Jumpable;
        signals.wallFrozenSeconds = !moving && wallInView ? signals.wallFrozenSeconds + dt : 0f;

        int pauses = score.pauseCounter;
        if (pauses < lastPauseCounter) signals.pausesSpent += lastPauseCounter - pauses;
        lastPauseCounter = pauses;

        int greens = Count(collisionDetection.healAtomPickups, ref lastHeal);
        signals.greenAtomsCollected += greens;
        // the green atom repaired a heart: the count of hearts left went up
        if (greens > 0 && collisionDetection.lifeCounter < lastLife) { signals.heartsHealed++; healed = true; }
        lastLife = collisionDetection.lifeCounter;
        signals.blueAtomsCollected += Count(collisionDetection.shieldAtomPickups, ref lastShield);
        signals.redAtomsCollected += Count(collisionDetection.pauseAtomPickups, ref lastRed);

        if (wall != null && wall.Crossed && !wallCounted) { wallCounted = true; signals.wallsCrossed++; }
        if (power != null) signals.powersFired += Count(power.UltimatesFired, ref lastFired);

        if (score.dustPickups != lastDustPickups)
        {
            signals.starsCollected += Mathf.Max(0, score.dustPickups - lastDustPickups);
            lastDustPickups = score.dustPickups;
        }
    }

    // Idle: no progress for IdleHintSeconds while the player is "on the
    // clock" (world running, or the step wants a lifted finger) -> the robot
    // repeats the line. A lifted finger on any other step pauses the clock.
    void TickIdle(TutorialStep current, float dt)
    {
        float progress = signals.flyUnits + signals.presses + signals.pausesSpent + signals.starsCollected
                       + signals.greenAtomsCollected + signals.blueAtomsCollected + signals.redAtomsCollected
                       + signals.powersFired + signals.wallsCrossed;
        if (!Mathf.Approximately(progress, lastProgress)) { lastProgress = progress; idleClock = 0f; return; }
        bool wantsFinger = current.advance == TutorialAdvance.WallFrozen || current.advance == TutorialAdvance.CrossWall;
        if (!signals.worldMoving && !wantsFinger) return;
        if (!speaker.LineFinished) return;
        idleClock += dt;
        if (idleClock >= IdleHintSeconds) { idleClock = 0f; Reprompt(); }
    }

    void Reprompt()
    {
        speaker.Say(lines[step]);
        guides.ShowTouch(true);
    }

    // ---- Star dust: the rush and its arrows ----

    // Every star-dust piece wears an arrow, except while another prompt owns
    // the arrow (a HUD readout, an atom, the alien).
    public static bool StarArrowsAllowed(TutorialCue cue)
    {
        return cue != TutorialCue.SpawnWall && cue != TutorialCue.PointAtWall && AtomFor(cue) == TutorialAtom.None;
    }

    public const float RushAgainSeconds = .6f;
    float noRushedDust;

    // Puts a short stream of dust right in front of the ship (a handful of
    // pieces, on it within about a second). Returns how many.
    public int RushDust()
    {
        var spawner = FindFirstObjectByType<spawnGoodStuffTut>();
        if (spawner == null) return 0;
        float bottom, top;
        TutorialAtomDrift.View(out bottom, out top);
        Vector3 at = ship != null ? ship.transform.position : new Vector3(0f, bottom + (top - bottom) * .3f, 0f);
        noRushedDust = 0f;
        return spawner.RushStars(at, top);
    }

    // During the dust step: if every rushed piece was missed and has fallen
    // away, rush a fresh stream so the step can always be finished. dt is
    // unscaled seconds.
    public void TickDust(float dt)
    {
        if (step < 0 || TutorialScript.Steps[step].cue != TutorialCue.SpawnStars) return;
        var live = spawnGoodStuffTut.LiveStars;
        bool rushed = false;
        for (int i = 0; i < live.Count && !rushed; i++)
            rushed = live[i] != null && live[i].GetComponent<TutorialStarDrift>() != null;
        if (rushed) { noRushedDust = 0f; return; }
        noRushedDust += dt;
        if (noRushedDust >= RushAgainSeconds && signals.starsCollected - stepStart.starsCollected < 1) RushDust();
    }

    // ---- Hearts: the ship starts damaged; the tutorial can never end in a death ----

    // The ship ALWAYS starts the lesson damaged: one of its two hearts is
    // gone (damaged hull art, one heart left on the ring), so the green atom
    // has something to repair. Applied once, as soon as the ship's own Start
    // has set its hearts (collisionDetection.MAXLIFE).
    public static void ApplyDamagedStart()
    {
        if (collisionDetection.MAXLIFE < 2) return;
        collisionDetection.lifeCounter = collisionDetection.MAXLIFE - 1;
    }

    void EnsureDamagedStart()
    {
        if (damagedApplied || collisionDetection.MAXLIFE <= 0) return;
        damagedApplied = true;
        ApplyDamagedStart();
        lastLife = collisionDetection.lifeCounter;
    }

    // The tutorial's hearts are all back (a crash cost one: lifeCounter 1).
    public static void TopUpHearts()
    {
        if (collisionDetection.lifeCounter > 0) collisionDetection.lifeCounter = 0;
    }

    // Before the heal the ship stays damaged (nothing can mend it but the
    // green atom); after it a crash is mended between beats.
    void KeepHeartsRight()
    {
        if (!healed && collisionDetection.MAXLIFE >= 2 && collisionDetection.lifeCounter < 1)
            collisionDetection.lifeCounter = collisionDetection.MAXLIFE - 1;
    }

    // ---- The wall ----

    // Brings the broken wall back after a moment (with the retry line), and
    // puts the pause counter right so the lesson can always be finished.
    void TickWall(float dt)
    {
        if (wall != null && wall.Broken && wallRetryLeft < 0f)
        {
            TutorialWall.Clear();
            wall = null;
            wallRetryLeft = WallRetrySeconds;
            if (healed) TopUpHearts();
        }
        if (wallRetryLeft >= 0f)
        {
            wallRetryLeft -= dt;
            if (wallRetryLeft >= 0f) return;
            wallRetryLeft = -1f;
            if (score.pauseCounter < PausesPerStep) score.pauseCounter = PausesPerStep;
            wall = TutorialWall.Spawn(ship != null ? ship.transform : null);
            wallCounted = false;
            speaker.Say(retryLine);
        }
    }

    // ---- Steps ----

    void BeginStep(int index)
    {
        step = index;
        idleClock = 0f;
        stepStart = signals;
        var s = TutorialScript.Steps[index];
        speaker.Say(lines[index]);

        guides.Clear();
        if (healed) TopUpHearts();   // whatever the last beat cost, the next one starts with every heart
        // letting go always freezes the tutorial: never out of pauses at a step's start
        int minPauses = s.cue == TutorialCue.SpawnRedAtom ? 1 : PausesPerStep;
        if (score.pauseCounter < minPauses) score.pauseCounter = minPauses;
        switch (s.cue)
        {
            case TutorialCue.TouchPulse:
                guides.ShowTouch(true);
                break;
            case TutorialCue.SpawnStars:
                // The HUD has no star dust read-out any more (the run's dust
                // shows on the Tutorial Complete card): every piece wears its
                // own arrow instead (PointAtStars). The dust rushes in from the top.
                spawnGoodStuffTut.StartStars();
                if (RushDust() == 0) noRushedDust = 0f;
                break;
            case TutorialCue.SpawnGreenAtom:
            case TutorialCue.SpawnBlueAtom:
            case TutorialCue.SpawnRedAtom:
                IntroduceAtom(AtomFor(s.cue));
                break;
            case TutorialCue.SpawnCapacitorAtom:
                // an empty charge that the one violet atom fills
                if (power != null) power.Arm(TutorialScript.ArmSeconds);
                else signals.powersFired++;   // no weapon to charge: never stall here
                IntroduceAtom(AtomFor(s.cue));
                break;
            case TutorialCue.SpawnWall:
                wallRetryLeft = -1f;
                wallCounted = false;
                wall = TutorialWall.Spawn(ship != null ? ship.transform : null);
                if (wall == null) { signals.wallFrozenSeconds = 99f; signals.wallsCrossed++; }   // no wall to show: never stall here
                break;
            case TutorialCue.PointAtWall:
                guides.ShowTouch(true);
                break;
        }
    }

    void EndStep(TutorialStep s)
    {
        guides.Clear();
        // an atom nobody caught goes with its step: nothing stray is left to
        // be picked up unexplained later
        spawnGoodStuffTut.RemoveLiveAtom();
        spawnGoodStuffTut.keepAtomComing = TutorialAtom.None;
    }

    // Drops the step's one atom in (see spawnGoodStuffTut.SpawnIntro). With
    // no atom to show (a missing prefab) the step is not left waiting.
    void IntroduceAtom(TutorialAtom kind)
    {
        var spawner = FindFirstObjectByType<spawnGoodStuffTut>();
        if (spawner != null && spawner.SpawnIntro(kind) != null) return;
        switch (kind)
        {
            case TutorialAtom.Green: signals.greenAtomsCollected++; signals.heartsHealed++; healed = true; break;
            case TutorialAtom.Blue: signals.blueAtomsCollected++; break;
            case TutorialAtom.Red: signals.redAtomsCollected++; break;
        }
    }

    // Which atom a step's cue introduces (None for the other cues).
    public static TutorialAtom AtomFor(TutorialCue cue)
    {
        switch (cue)
        {
            case TutorialCue.SpawnGreenAtom: return TutorialAtom.Green;
            case TutorialCue.SpawnBlueAtom: return TutorialAtom.Blue;
            case TutorialCue.SpawnRedAtom: return TutorialAtom.Red;
            case TutorialCue.SpawnCapacitorAtom: return TutorialAtom.Cooldown;
            default: return TutorialAtom.None;
        }
    }

    static int Count(int now, ref int last)
    {
        int d = Mathf.Max(0, now - last);
        last = now;
        return d;
    }

    // ---- The end: the closing line, then the ship lifts off into the game ----

    void BeginEnding()
    {
        endedAt = Time.unscaledTime;
        reachedTheEndOfTut = true;
        guides.Clear();
        spawnGoodStuffTut.RemoveLiveAtom();
        spawnGoodStuffTut.keepAtomComing = TutorialAtom.None;
        TutorialWall.Clear();
        wall = null;
        wallRetryLeft = -1f;
        if (healed) TopUpHearts();
        if (power != null) power.chargeMode = ShipPowerController.ChargeMode.Held;
        if (skip != null) skip.SetVisible(false);
        speaker.Say(closingLine);

        // Marks the tutorial complete for the menu. It does not affect this
        // run: score decided once, at load, that the tutorial scene never
        // pays real star dust.
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.Save();
        AchievementTracker.OnTutorialDone();
    }

    void RunEnding()
    {
        if (panelShown || liftStarted) return;
        if (!speaker.LineFinished || speaker.SinceLineFinished < liftOffSeconds) return;
        if (AutoLiftOff)
        {
            // the ship flies on into the first level (TutorialLiftOff plays
            // the portal departure, then loads gameS1, whose run begins with
            // its usual arrival and the PLAYER'S ship)
            liftStarted = true;
            if (script != null) script.enabled = false;
            speaker.HideAll();
            TutorialLiftOff.Begin();
            return;
        }
        panelShown = true;
        speaker.HideAll();
        if (script != null) script.enabled = false;
        if (pausedIcon != null) Destroy(pausedIcon);
        score.totalCurrency = 0;
        moveBackGround.speed = 0;
        startMenu.youAreInTutorial = false;
        if (tutButtonClicks.activeCanvis != null) tutButtonClicks.activeCanvis.gameObject.SetActive(true);
        TutorialCompletePanel.Show();
    }

    // ---- Layout: keep the robot under the HUD and the skip button ----

    void KeepClearOfTopUi()
    {
        if (skip == null) skip = FindFirstObjectByType<TutorialSkip>();
        if (speakerCanvas == null) speakerCanvas = speaker.GetComponent<Canvas>();
        float sf = Mathf.Max(speakerCanvas.scaleFactor, .0001f);
        float safeTop = ScreenInfo.SafeArea.yMax;
        float lowest = safeTop;   // lowest screen y (px) taken from the top
        if (hudPanel != null && hudPanel.gameObject.activeInHierarchy)
        {
            hudPanel.GetWorldCorners(corners);   // overlay canvas: screen pixels
            lowest = Mathf.Min(lowest, corners[0].y);
        }
        if (skip != null && skip.ButtonRect != null)
        {
            skip.ButtonRect.GetWorldCorners(corners);
            lowest = Mathf.Min(lowest, corners[0].y);
        }
        speaker.SetTopBlocked(Mathf.Round((safeTop - lowest) / sf));
    }

    static Text FindText(string name)
    {
        var go = SceneUtil.FindAny(name);
        return go != null ? go.GetComponent<Text>() : null;
    }

    // The HUD readouts sit two panels deep under the HUD canvas; the outer
    // panel is the block the robot has to stay below.
    static RectTransform TopPanelOf(Component c)
    {
        if (c == null) return null;
        Transform t = c.transform;
        while (t.parent != null && t.parent.GetComponent<Canvas>() == null) t = t.parent;
        return t as RectTransform;
    }
}
