using UnityEngine;
using UnityEngine.UI;

// Runs the tutorial: walks TutorialScript.Steps in order, has the robot say
// each line, sets up whatever the step needs (a touch hint, an arrow, stars,
// a red atom), and moves on when the player has actually done the thing --
// not on a timer. After the last step the ship lifts off and the Tutorial
// Complete card comes up.
//
// It used to type eleven long lines into a Text box, one character per
// frame, and only while a finger was down, with fixed holds between lines;
// most of what it said was not needed to play. The script is now seven short
// lines in one table (TutorialScript.cs), spoken by RobotSpeaker.
//
// Everything here runs on unscaled time: the world sits at timeScale 0 every
// time the player lifts their finger, and that is exactly when the robot has
// the most to say.
public class Hints : MonoBehaviour {

    [Tooltip("Minimum time a line stays up after it has been fully spoken, " +
             "even if the player already did what it asks.")]
    public float readSeconds = .7f;

    [Tooltip("Delay before the robot pops in and says the first line.")]
    public float introDelay = .6f;

    [Tooltip("How long the ship flies off toward LiftOffLeft before the " +
             "Tutorial Complete card appears.")]
    public float liftOffSeconds = 1.4f;

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
    TutorialSignals signals, stepStart;
    bool sampling;
    bool lastPressed;
    int lastPauseCounter, lastDustPickups, lastHeal, lastShield, lastRed;
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

        if (speaker.LineFinished && speaker.SinceLineFinished >= readSeconds
            && TutorialScript.IsMet(current, stepStart, signals))
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

        if (score.dustPickups != lastDustPickups)
        {
            signals.starsCollected += Mathf.Max(0, score.dustPickups - lastDustPickups);
            lastDustPickups = score.dustPickups;
        }
    }

    // ---- Steps ----

    void BeginStep(int index)
    {
        step = index;
        stepStart = signals;
        var s = TutorialScript.Steps[index];
        speaker.Say(lines[index]);

        guides.Clear();
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
                // No arrow: the HUD has no star dust read-out any more (the
                // run's dust shows on the Tutorial Complete card).
                spawnGoodStuffTut.StartStars();
                break;
            case TutorialCue.SpawnGreenAtom:
            case TutorialCue.SpawnBlueAtom:
            case TutorialCue.SpawnRedAtom:
                spawnGoodStuffTut.keepAtomComing = AtomFor(s.cue);
                break;
        }
    }

    void EndStep(TutorialStep s)
    {
        guides.Clear();
        spawnGoodStuffTut.keepAtomComing = TutorialAtom.None;
    }

    // Which atom a step's cue introduces (None for the other cues).
    public static TutorialAtom AtomFor(TutorialCue cue)
    {
        switch (cue)
        {
            case TutorialCue.SpawnGreenAtom: return TutorialAtom.Green;
            case TutorialCue.SpawnBlueAtom: return TutorialAtom.Blue;
            case TutorialCue.SpawnRedAtom: return TutorialAtom.Red;
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
        spawnGoodStuffTut.keepAtomComing = TutorialAtom.None;
        if (skip != null) skip.SetVisible(false);
        if (script != null) script.enabled = false;

        // Marks the tutorial complete for the menu. It does not affect this
        // run: score decided once, at load, that the tutorial scene never
        // pays real star dust.
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.Save();
        //---------------Complete Tut ---------##19-----------
        achievementAPICalls.achievement_tutorial_completed();
    }

    void RunEnding()
    {
        if (ship != null && liftOffLeft != null)
            ship.transform.position = Vector3.MoveTowards(ship.transform.position, liftOffLeft.transform.position,
                                                          3.2f * Time.unscaledDeltaTime);

        if (panelShown || Time.unscaledTime - endedAt < liftOffSeconds) return;
        panelShown = true;

        if (pausedIcon != null) Destroy(pausedIcon);
        float practiceDust = score.tutorialCurrency;
        score.totalCurrency = 0;
        moveBackGround.speed = 0;
        startMenu.youAreInTutorial = false;
        if (tutButtonClicks.activeCanvis != null) tutButtonClicks.activeCanvis.gameObject.SetActive(true);
        TutorialCompletePanel.Show(practiceDust, score.RealRunPauses);
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
