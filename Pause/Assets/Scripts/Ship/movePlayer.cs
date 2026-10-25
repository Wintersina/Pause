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

    // The launch glide. A finger already down when GO fires used to teleport
    // the ship onto it (the first steered frame was a plain placement, or a
    // full blink with FX when the finger had lifted and re-landed during
    // READY). Now the ship glides there instead: an exponential ease-out
    // toward the live finger, speed capped so a far finger is still a fast
    // glide, then it hands over to the normal 1:1 follow. Only the first GO of
    // this ship's life starts one (playoneshot); lifting the finger cancels it
    // (that is the pause), so pause-teleport works as ever, mid-glide too.
    public const float GlideRate = 14f;        // 1/s: ~95% of the gap in 0.2 s
    public const float GlideMaxSpeed = 60f;    // world units / s
    public const float GlideSettleDistance = 0.05f;
    public const float GlideMaxSeconds = 0.6f; // hard end; rate ramps up from 0.35 s
    const float GlideRampStart = 0.35f;
    const float GlideRampPerSecond = 60f;
    const float GlideMaxStep = 0.05f;          // dt clamp (hitch frames)
    private bool gliding;
    private float glideElapsed;

    // The pause-jump is a targeting move: the ship lands ON the finger so the
    // landing blast hits what the finger is on, then slides back up to its
    // usual perch above the finger. The slide is an offset (landing point minus
    // the live follow target) that eases to zero, added to the live follow
    // target, so a finger that keeps moving is followed 1:1 the whole way and
    // there is no jump when the slide ends. Unscaled time, like the glide.
    public const float ReturnSeconds = 0.35f;
    private bool returning;
    private float returnElapsed;
    private Vector3 returnOffset;

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
        // READY / GO!!!! wear the PAUSED lettering (LaunchBanner).
        if (startTimer.GetComponent<LaunchBanner>() == null) startTimer.gameObject.AddComponent<LaunchBanner>();
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
            goTimer = TickCountdown(goTimer, Dt());
            Color c = startTimerBaseColor;
            c.a *= GoAlpha(goTimer);
            startTimer.color = c;
            if (goTimer <= 0f) startTimer.gameObject.SetActive(false);
        }

        // A planetfall's descent and a lift-off's rise fly the ship
        // (Planetfall, Liftoff): no steering and
        // no teleport. A touch held into the hand-back steers on from where
        // the descent left the ship (under the finger), not as an arrival.
        if (Planetfall.HoldsShip || Liftoff.HoldsShip || PortalArrival.HoldsShip)
        {
            teleported = false;
            teleportLockedUntilRelease = false;
            gliding = false;
            returning = false;
            return;
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
            bool live = StepLaunch(ref startTimerCounter, Dt());
            startTimer.fontSize = CountdownFontSize(startTimerBaseFontSize, startTimerCounter);

            if (live)
            {
                if (playoneshot)
                {
                    // GO text, GO sound and control all land on this frame.
                    startTimer.text = LaunchLabel(startTimerCounter);
                    goClip.Play();
                    playoneshot = false;
                    // The launch never blinks: glide to the finger instead.
                    gliding = true;
                    glideElapsed = 0f;
                    returning = false;
                    teleported = false;
                    teleportLockedUntilRelease = false;
                }

                if (TouchInput.IsPressed)
                {
                    fingerPos = Camera.main.ScreenToWorldPoint(new Vector3(TouchInput.Position.x, TouchInput.Position.y, 0));
                    textPos = Camera.main.WorldToScreenPoint(transform.position);
                }
                else
                    teleported = true;

                if (gliding)
                {
                    float dt = Mathf.Min(Dt(), GlideMaxStep);
                    Vector3 target = FollowTarget(fingerPos);
                    bool done;
                    Vector3 next = GlideStep(transform.position, target, glideElapsed, dt, out done);
                    glideElapsed += dt;
                    PlaceShip(next);
                    if (done) gliding = false;
                }
                else if (teleported)
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
                    Vector3 rest = FollowTarget(fingerPos);
                    Vector3 land = LandingPoint(fingerPos);
                    PlaceShip(land);
                    returnOffset = land - rest;
                    returnElapsed = 0f;
                    returning = true;
                    MarkTeleport();
                    TeleportFx.Play(before, land);
                    // scored by the jump to the perch, as before the slide-back
                    RunScore.OnTeleport(before, rest);
                    AchievementTracker.OnTeleport(before, rest);
                }
                else if (returning)
                {
                    float dt = Mathf.Min(Dt(), GlideMaxStep);
                    returnElapsed += dt;
                    PlaceShip(ReturnPosition(FollowTarget(fingerPos), returnOffset, returnElapsed));
                    if (returnElapsed >= ReturnSeconds) returning = false;
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
            // Finger up = the pause: a glide in flight is cancelled where it
            // is, and the next touch is an ordinary pause-teleport.
            gliding = false;
            returning = false;
            teleported = true;
            teleportLockedUntilRelease = false;
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

    // Where the hull sits for a finger at world position `finger`.
    public static Vector3 FollowTarget(Vector3 finger)
    {
        float x = ShipReach.ClampX(finger.x + ShipReach.FingerOffsetX + SecretPowerController.DashOffsetX + WorldHazards.PlayerOffsetX + EliteWell.Offset.x);
        float y = ClampPlayerY(finger.y + ShipReach.FingerOffset + WorldHazards.PlayerOffsetY + EliteWell.Offset.y);
        return new Vector3(x, y);
    }

    // One frame of the launch glide from `pos` toward the live `target`.
    // Exponential ease-out, capped at GlideMaxSpeed, with the rate ramping up
    // late so a moving finger can't hold it open past GlideMaxSeconds. `done`
    // lands exactly on the target (within GlideSettleDistance or out of time).
    public static Vector3 GlideStep(Vector3 pos, Vector3 target, float elapsed, float dt, out bool done)
    {
        float rate = GlideRate + GlideRampPerSecond * Mathf.Max(0f, elapsed - GlideRampStart);
        float gap = Vector3.Distance(pos, target);
        float step = Mathf.Min(gap * (1f - Mathf.Exp(-rate * dt)), GlideMaxSpeed * dt);
        Vector3 next = gap <= 1e-6f ? target : Vector3.MoveTowards(pos, target, step);
        done = Vector3.Distance(next, target) <= GlideSettleDistance || elapsed + dt >= GlideMaxSeconds;
        if (done) next = target;
        return next;
    }

    // Where a pause-jump lands: on the finger itself (the perch offsets are
    // not applied), kept inside the same reach as the follow.
    public static Vector3 LandingPoint(Vector3 finger)
    {
        return new Vector3(ShipReach.ClampX(finger.x), ClampPlayerY(finger.y));
    }

    // The slide back to the perch: `target` plus the landing offset eased out
    // (smoothstep: no pop at either end, never overshoots).
    public static Vector3 ReturnPosition(Vector3 target, Vector3 offset, float elapsed)
    {
        float t = Mathf.Clamp01(elapsed / ReturnSeconds);
        float ease = t * t * (3f - 2f * t);
        return target + offset * (1f - ease);
    }

    void PlaceShip(Vector3 p)
    {
        this.transform.position = p;
        hypeText.gameObject.SetActive(true);
        boostText.gameObject.SetActive(true);
        hypeText.transform.position = textPos + new Vector3(0, 75, 0);
        boostText.transform.position = textPos + new Vector3(0, -60, 0);
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
        float x = ShipReach.ClampX(fingerPos.x + ShipReach.FingerOffsetX + SecretPowerController.DashOffsetX + WorldHazards.PlayerOffsetX + EliteWell.Offset.x);
        float y = ClampPlayerY(fingerPos.y + ShipReach.FingerOffset + WorldHazards.PlayerOffsetY + EliteWell.Offset.y);
        this.transform.position = new Vector3(x, y);
            // Allow text to follow player----------------------------

            hypeText.gameObject.SetActive(true);
            boostText.gameObject.SetActive(true);
            hypeText.transform.position = textPos + new Vector3(0, 75, 0);
            boostText.transform.position = textPos + new Vector3(0, -60, 0);
    }

    // The old movement only capped the top edge. A low touch could therefore
    // place the ship below the visible board. Keep the full hull in play --
    // in the view this device shows (ShipReach: shares of the view, clear of
    // the home indicator; it used to be the constant -4.15 .. 4.5).
    public static float ClampPlayerY(float y)
    {
        return ShipReach.ClampY(y);
    }
}
