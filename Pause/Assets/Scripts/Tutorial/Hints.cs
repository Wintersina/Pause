using UnityEngine;
using UnityEngine.UI;

// Runs the tutorial: walks TutorialScript.Steps in order, has the robot say
// each line, sets up whatever the step needs (a touch hint, an arrow, stars,
// an atom, the one alien, the weapon's charge), and moves on when the player has actually done the thing --
// not on a timer. After the last step the ship lifts off and the Tutorial
// Complete card comes up.
//
// It used to type eleven long lines into a Text box, one character per
// frame, and only while a finger was down, with fixed holds between lines;
// most of what it said was not needed to play. The script is now a few short
// lines in one table (TutorialScript.cs), spoken by RobotSpeaker.
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

    [Tooltip("Delay before the robot pops in and says the first line.")]
    public float introDelay = TutorialScript.IntroSeconds;

    [Tooltip("How long the ship flies off toward LiftOffLeft before the " +
             "Tutorial Complete card appears.")]
    public float liftOffSeconds = TutorialScript.EndingSeconds;

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
    Text pauseReadout;
    RectTransform hudPanel;
    readonly Vector3[] corners = new Vector3[4];

    int step = -1;
    float stepClock;   // unscaled seconds since the step began (its timeout)
    TutorialSignals signals, stepStart;
    bool sampling;
    bool lastPressed;
    int lastPauseCounter, lastDustPickups, lastHeal, lastShield, lastRed, lastFired;
    bool enemyOut;
    ShipPowerController power;
    float startedAt;
    float endedAt = -1f;
    bool panelShown;

    public int CurrentStep { get { return step; } }
    public TutorialSignals Signals { get { return signals; } }

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

        speaker = RobotSpeaker.Create(font);
        guides = TutorialGuides.Create(speaker.Root);

        pauseReadout = FindText("PausesRemainingText");
        hudPanel = TopPanelOf(pauseReadout);

        startedAt = Time.unscaledTime;
    }

    void Update() {

        if (speaker == null) return;
        Sample(Time.unscaledDeltaTime);
        KeepClearOfTopUi();

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
        else if (current.cue == TutorialCue.SpawnEnemy) guides.PointAt(TutorialEnemy.Live);
        guides.PointAtStars(StarArrowsAllowed(current.cue) ? spawnGoodStuffTut.LiveStars : null);
        TickDust(Time.unscaledDeltaTime);
        KeepHeartsUp();

        stepClock += Time.unscaledDeltaTime;
        if (TutorialScript.CanAdvance(current, stepStart, signals, speaker.LineFinished, speaker.SinceLineFinished, readSeconds, stepClock))
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
        }

        bool pressed = TouchInput.IsPressed && !buttonClicks.playerDied;
        bool moving = pressed || score.pauseCounter <= 0;
        if (pressed && !lastPressed) signals.presses++;
        lastPressed = pressed;
        signals.pressed = pressed;
        signals.worldMoving = moving;
        if (pressed) signals.flySeconds += dt;
        signals.frozenSeconds = moving ? 0f : signals.frozenSeconds + dt;

        int pauses = score.pauseCounter;
        if (pauses < lastPauseCounter) signals.pausesSpent += lastPauseCounter - pauses;
        lastPauseCounter = pauses;

        signals.greenAtomsCollected += Count(collisionDetection.healAtomPickups, ref lastHeal);
        signals.blueAtomsCollected += Count(collisionDetection.shieldAtomPickups, ref lastShield);
        signals.redAtomsCollected += Count(collisionDetection.pauseAtomPickups, ref lastRed);

        // The tutorial alien is gone once it is destroyed (rammed, teleported
        // onto, shot) or has left past the bottom edge.
        if (enemyOut && TutorialEnemy.Live == null)
        {
            enemyOut = false;
            signals.enemiesGone++;
        }
        if (power != null) signals.powersFired += Count(power.UltimatesFired, ref lastFired);

        if (score.dustPickups != lastDustPickups)
        {
            signals.starsCollected += Mathf.Max(0, score.dustPickups - lastDustPickups);
            lastDustPickups = score.dustPickups;
        }
    }

    // ---- Star dust: the rush and its arrows ----

    // Every star-dust piece wears an arrow, except while another prompt owns
    // the arrow (a HUD readout, an atom, the alien).
    public static bool StarArrowsAllowed(TutorialCue cue)
    {
        return cue != TutorialCue.PointAtPauses && cue != TutorialCue.SpawnEnemy && AtomFor(cue) == TutorialAtom.None;
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

    // ---- Hearts: the tutorial can never end in a death ----

    // The tutorial's hearts are all back (the crash cost one: lifeCounter 1).
    public static void TopUpHearts()
    {
        if (collisionDetection.lifeCounter > 0) collisionDetection.lifeCounter = 0;
    }

    // Safety net between beats: never down to the last heart.
    void KeepHeartsUp()
    {
        if (collisionDetection.MAXLIFE > 0 && collisionDetection.lifeCounter >= collisionDetection.MAXLIFE - 1
            && collisionDetection.lifeCounter > 0)
            collisionDetection.lifeCounter = Mathf.Max(0, collisionDetection.MAXLIFE - 2);
    }

    // ---- Steps ----

    void BeginStep(int index)
    {
        step = index;
        stepClock = 0f;
        stepStart = signals;
        var s = TutorialScript.Steps[index];
        speaker.Say(lines[index]);

        guides.Clear();
        TopUpHearts();   // whatever the last beat cost, the next one starts with every heart
        switch (s.cue)
        {
            case TutorialCue.TouchPulse:
                guides.ShowTouch(true);
                break;
            case TutorialCue.PointAtPauses:
                guides.ShowTouch(true);
                guides.PointAt(pauseReadout);
                break;
            case TutorialCue.SpawnStars:
                // The HUD has no star dust read-out any more (the run's dust
                // shows on the Tutorial Complete card): every piece wears its
                // own arrow instead (PointAtStars).
                spawnGoodStuffTut.StartStars();
                // ...but the dust itself comes right now, in the ship's lane
                if (RushDust() == 0) noRushedDust = 0f;
                break;
            case TutorialCue.GrantHearts:
                guides.ShowTouch(true);
                break;
            case TutorialCue.SpawnGreenAtom:
                // the ship takes a dent first, so the repair is seen working
                DentShip();
                IntroduceAtom(AtomFor(s.cue));
                break;
            case TutorialCue.SpawnBlueAtom:
            case TutorialCue.SpawnRedAtom:
                IntroduceAtom(AtomFor(s.cue));
                break;
            case TutorialCue.SpawnEnemy:
                // straight down at where the ship is: dodge it or blink onto it
                enemyOut = TutorialEnemy.Spawn(ship != null ? ship.transform.position.x : 0f) != null;
                if (!enemyOut) signals.enemiesGone++;   // no alien to show: never stall here
                break;
            case TutorialCue.SpawnCapacitorAtom:
                // an empty charge that the one violet atom fills
                if (power != null) power.Arm(TutorialScript.ArmSeconds);
                else signals.powersFired++;   // no weapon to charge: never stall here
                IntroduceAtom(AtomFor(s.cue));
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
            case TutorialAtom.Green: signals.greenAtomsCollected++; break;
            case TutorialAtom.Blue: signals.blueAtomsCollected++; break;
            case TutorialAtom.Red: signals.redAtomsCollected++; break;
        }
    }

    // One hit taken, shown on the orbiting hearts: the green atom has a heart to repair.
    void DentShip()
    {
        if (collisionDetection.MAXLIFE < 2) return;
        collisionDetection.lifeCounter = 1;
        Vector3 at = ship != null ? ship.transform.position : Vector3.zero;
        ShipLivesIndicator.Impact(at + Vector3.up * .6f);
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

    // ---- The end: lift off, then the Tutorial Complete card ----

    void BeginEnding()
    {
        endedAt = Time.unscaledTime;
        reachedTheEndOfTut = true;
        speaker.HideAll();
        guides.Clear();
        spawnGoodStuffTut.RemoveLiveAtom();
        spawnGoodStuffTut.keepAtomComing = TutorialAtom.None;
        TopUpHearts();
        if (power != null) power.chargeMode = ShipPowerController.ChargeMode.Held;
        TutorialEnemy.Clear();   // a Skip mid-step leaves nothing to fly into
        enemyOut = false;
        if (skip != null) skip.SetVisible(false);
        if (script != null) script.enabled = false;

        // Marks the tutorial complete for the menu. It does not affect this
        // run: score decided once, at load, that the tutorial scene never
        // pays real star dust.
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.Save();
        AchievementTracker.OnTutorialDone();
    }

    void RunEnding()
    {
        // (the ship stays where it is: LIFT OFF flies it into the portal, TutorialLiftOff)
        if (panelShown || Time.unscaledTime - endedAt < liftOffSeconds) return;
        panelShown = true;

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
