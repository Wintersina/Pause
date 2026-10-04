using UnityEngine;
using UnityEngine.UI;
public class movePlayer : MonoBehaviour
{

    Vector3 fingerPos;
    Vector3 textPos;
    private Text startTimer;
    private RectTransform boostText;
    private RectTransform hypeText;
    float startTimerCounter, goTimer;
    int startTimerBaseFontSize;
    Color startTimerBaseColor;
    private AudioSource goClip;
    private bool playoneshot;
    private bool teleported;
    // A denied no-pause jump must remain denied until the finger lifts. Without
    // this, the first frame restored the ship but the held touch moved it again
    // on the next frame, which still looked and played like a free teleport.
    private bool teleportLockedUntilRelease;

    [Header("Teleport")]
    [Tooltip("Minimum gap between teleports once the player is out of pauses. " +
             "While they still have pauses in hand there is no cooldown -- the " +
             "pause itself is the cost.")]
    public float teleportCooldown = 1f;

    private float nextTeleportAt;

    // The launch countdown: one second of "READY" from the first touch, then
    // "GO!!!!" and the ship steers on that same frame. GO only lingers as a
    // fade-out; it never holds control back. (It used to be a 2s count plus a
    // 2s GO banner, so the start felt about three seconds long.)
    public const float CountdownSeconds = 1f;
    public const float GoSeconds = 0.5f;
    public const string ReadyLabel = "READY";
    public const string GoLabel = "GO!!!!";
    // The countdown text grows to this multiple of its authored size.
    public const float CountdownMaxGrowth = 1.6f;
    // One long hitch (e.g. the first frame after a scene load) must not eat
    // the whole countdown.
    const float MaxCountdownStep = 0.1f;
    // Float drift after N steps of 1/N must not cost an extra frame.
    const float CountdownEpsilon = 1e-4f;

    void Start()
    {
        teleported = false;
        //shild timer;
        boostText = GameObject.Find("boostText").GetComponent<RectTransform>();
        hypeText = GameObject.Find("hypeText").GetComponent<RectTransform>();
        startTimer = GameObject.Find("goText").GetComponent<Text>();
        goClip = GameObject.Find("GOSound").GetComponent<AudioSource>();
        startTimerCounter = CountdownSeconds;
        goTimer = GoSeconds;
        startTimerBaseFontSize = startTimer.fontSize;
        startTimerBaseColor = startTimer.color;
        playoneshot = true;

        startTimer.text = LaunchLabel(startTimerCounter);

    }

    // Update is called once per frame
    void Update()
    {
        // GO is purely visual: it fades on real time whether or not the finger
        // is down, and steering never waits for it.
        if (!playoneshot && goTimer > 0f)
        {
            goTimer = TickCountdown(goTimer, Time.unscaledDeltaTime);
            Color c = startTimerBaseColor;
            c.a *= GoAlpha(goTimer);
            startTimer.color = c;
            if (goTimer <= 0f) startTimer.gameObject.SetActive(false);
        }

        if (TouchInput.IsPressed)
        {
            // A press on Replay or Menu belongs to the UI. Without this
            // guard, that same press was consumed as a teleport target before
            // the UI click completed, making the pause actions appear broken.
            if (PauseQuickActions.IsScreenPointOnAction(TouchInput.Position)) return;

            // show start timer, give player 1 second to prep. This used to
            // subtract Time.timeSinceLevelLoad (the whole time since load)
            // every frame, so the countdown was gone in a few frames, and the
            // font grew by 3 every frame without limit. Unscaled: the world is
            // still frozen at timeScale 0 on the frame the finger lands.
            bool live = StepLaunch(ref startTimerCounter, Time.unscaledDeltaTime);
            startTimer.fontSize = CountdownFontSize(startTimerBaseFontSize, startTimerCounter);

            if (live)
            {
                if (playoneshot)
                {
                    // GO text, GO sound and control all land on this frame.
                    startTimer.text = LaunchLabel(startTimerCounter);
                    goClip.Play();
                    playoneshot = false;
                }

                if (TouchInput.IsPressed)
                {
                    fingerPos = Camera.main.ScreenToWorldPoint(new Vector3(TouchInput.Position.x, TouchInput.Position.y, 0));
                    textPos = Camera.main.WorldToScreenPoint(transform.position);
                }
                else
                    teleported = true;

                if (teleported)
                {
                    // `teleported` is set the moment the finger lifts, so the
                    // next touch is an arrival rather than a drag. Do the
                    // cooldown check *before* moving: restoring position after
                    // a move only blocked one frame of a held touch.
                    teleported = false;
                    if (!CanTeleport())
                    {
                        teleportLockedUntilRelease = true;
                        TeleportFx.Denied(transform.position);
                        return;
                    }

                    Vector3 before = transform.position;
                    moveLeft_Right(fingerPos);
                    MarkTeleport();
                    TeleportFx.Play(before, transform.position);
                    RunScore.OnTeleport(before, transform.position);
                }
                else if (!teleportLockedUntilRelease)
                {
                    // Normal held-touch steering remains responsive. A touch
                    // that was refused by the no-pause cooldown cannot steer
                    // or blink until it has been released.
                    moveLeft_Right(fingerPos);
                }
            }
            else
            {
                startTimer.text = LaunchLabel(startTimerCounter);
            }

        }
        else
        {
            teleported = true;
            teleportLockedUntilRelease = false;
        }

    }

    public static float TickCountdown(float remaining, float deltaTime)
    {
        float next = remaining - Mathf.Clamp(deltaTime, 0f, MaxCountdownStep);
        return next <= CountdownEpsilon ? 0f : next;
    }

    // One held-touch frame of the launch. Returns true when the player has
    // control this frame -- which includes the frame the countdown hits zero
    // (the GO frame); there is no extra hold after GO.
    public static bool StepLaunch(ref float remaining, float deltaTime)
    {
        remaining = TickCountdown(remaining, deltaTime);
        return remaining <= 0f;
    }

    public static string LaunchLabel(float remaining)
    {
        return remaining <= 0f ? GoLabel : ReadyLabel;
    }

    // GO fades from full to nothing over GoSeconds.
    public static float GoAlpha(float goRemaining)
    {
        return Mathf.Clamp01(goRemaining / GoSeconds);
    }

    // Grows from the authored size to CountdownMaxGrowth times it as the
    // countdown runs out, then holds there for GO.
    public static int CountdownFontSize(int baseSize, float remaining)
    {
        float t = 1f - Mathf.Clamp01(remaining / CountdownSeconds);
        return Mathf.RoundToInt(Mathf.Lerp(baseSize, baseSize * CountdownMaxGrowth, t));
    }

    // A teleport is free while the player still holds pauses -- spending one is
    // already the cost. Once they are out, blinking across the screen was
    // unlimited and free, so it is rate limited instead.
    bool CanTeleport()
    {
        return score.pauseCounter > 0 || Time.unscaledTime >= nextTeleportAt;
    }

    void MarkTeleport()
    {
        // This timestamp deliberately updates even while pauses remain. It has
        // no effect until the final pause is gone, at which point the next
        // blink is correctly one second after the previous one.
        nextTeleportAt = Time.unscaledTime + teleportCooldown;
    }

    // will move the player left and right baised on touch positions.
    void moveLeft_Right(Vector3 fingerPos)
    {
        // Blink Dash (a secret power) throws the hull sideways off the finger
        // for a moment, then eases it back.
        float x = Mathf.Clamp(fingerPos.x + SecretPowerController.DashOffsetX, -2.4f, 2.4f);
        float y = ClampPlayerY(fingerPos.y + 1f);
        this.transform.position = new Vector3(x, y);
            // Allow text to follow player----------------------------

            hypeText.gameObject.SetActive(true);
            boostText.gameObject.SetActive(true);
            hypeText.transform.position = textPos + new Vector3(0, 75, 0);
            boostText.transform.position = textPos + new Vector3(0, -60, 0);
    }

    // The old movement only capped the top edge. A low touch could therefore
    // place the ship below the visible board. Keep the full hull in play.
    public static float ClampPlayerY(float y)
    {
        return Mathf.Clamp(y, -4.15f, 4.5f);
    }
}
