using UnityEngine;
using System.Collections;
using UnityEngine.UI;

public class movePlayerInTut : MonoBehaviour
{

    Vector3 fingerPos;
    Vector3 textPos;

    private RectTransform boostText;
    private RectTransform hypeText;


    void OnDestroy() { ShipSpawn.Forget(gameObject); }

    void Start()
    {
        ShipSpawn.Announce(gameObject);   // the thruster and the hearts attach to it from here
        ResetCounters();
        //will move pos of texts to follow ship
        boostText = GameObject.Find("boostText").GetComponent<RectTransform>();
        hypeText = GameObject.Find("hypeText").GetComponent<RectTransform>();

    }

    // True from a press until the finger lifts: the first frame of a press is
    // an arrival (a pause-jump), every later one is steering.
    bool held;
    // The first press of the lesson glides the ship to the finger (like a
    // run's launch, movePlayer.GlideStep) instead of blinking it there.
    bool glided;
    bool gliding;
    float glideElapsed;

    // Total distance the ship has been steered (world units) since the
    // scene started; the "hold anywhere to fly" step watches it. Pause-jumps
    // do not count: it is flying, not blinking.
    public static float FlownUnits { get; private set; }
    // Pause-jumps made (Arrive) since the scene started, and the last one.
    public static int Jumps { get; private set; }
    public static Vector3 LastJumpFrom { get; private set; }
    public static Vector3 LastJumpTo { get; private set; }
    public static void ResetCounters() { FlownUnits = 0f; Jumps = 0; }

    // Update is called once per frame
    Vector3 lastValid, startPos;
    bool guardReady;
    Vector2 lastGoodScreen;

    // A fling can never lose the ship: a non-finite position goes back to the
    // last valid one (or where the ship started), and one far outside the
    // playable view is pinned back in. The fly-in / lift-off move the ship
    // on purpose, so only the non-finite check runs while they hold it.
    void GuardShip(bool held)
    {
        if (!guardReady) { guardReady = true; startPos = lastValid = transform.position; }
        float bottom, top;
        TutorialAtomDrift.View(out bottom, out top);
        float slack = held ? 1000f : 3f;
        Vector3 p = TutorialSafety.RecoverShip(transform.position, lastValid, startPos, ShipReach.HalfWidth, bottom, top, slack);
        if (p != transform.position) transform.position = p;
        lastValid = p;
        var s = transform.localScale;
        if (!TutorialSafety.IsFinite(s) || s == Vector3.zero) transform.localScale = Vector3.one;
    }

    void Update()
    {
        GuardShip(Planetfall.HoldsShip || Liftoff.HoldsShip || PortalArrival.HoldsShip);
        // The fly-in's portal and the lift-off hold the ship: no steering.
        if (Planetfall.HoldsShip || Liftoff.HoldsShip || PortalArrival.HoldsShip)
        {
            held = false;
            gliding = false;
            return;
        }

        if (TouchInput.IsPressed)
        {
            // A press on the pause quick actions (replay / home) belongs to
            // the UI, never a steer or a jump (movePlayer does the same).
            if (PauseQuickActions.IsScreenPointOnAction(TouchInput.Position)) return;

            var cam = Camera.main;
            if (cam == null) return;
            // a fling can report a point off the screen or a non-finite one
            lastGoodScreen = TutorialSafety.SafeScreenPoint(TouchInput.Position, lastGoodScreen, Screen.width, Screen.height);
            fingerPos = cam.ScreenToWorldPoint(new Vector3(lastGoodScreen.x, lastGoodScreen.y, 0));
            textPos = Camera.main.WorldToScreenPoint(transform.position);

            Vector3 before = transform.position;
            if (!held && !glided)
            {
                glided = true;
                gliding = true;
                glideElapsed = 0f;
            }
            if (gliding)
            {
                float dt = Mathf.Min(Dt(), .05f);
                bool done;
                transform.position = movePlayer.GlideStep(transform.position, movePlayer.FollowTarget(fingerPos), glideElapsed, dt, out done);
                glideElapsed += dt;
                if (done) gliding = false;
                FollowTexts();
            }
            else
            {
                moveLeft_Right(fingerPos);
                if (!held) Arrive(before, transform.position);
                else FlownUnits += Vector3.Distance(before, transform.position);
            }
            if (gliding) FlownUnits += Vector3.Distance(before, transform.position);
            held = true;
        }
        else
        {
            held = false;
            gliding = false;
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

    void FollowTexts()
    {
        if (hypeText == null || boostText == null) return;
        hypeText.gameObject.SetActive(true);
        boostText.gameObject.SetActive(true);
        hypeText.transform.position = textPos + new Vector3(0, 70, 0);
        boostText.transform.position = textPos + new Vector3(0, -40, 0);
    }

    // The pause-jump, as in a run (movePlayer): the portal opens where the
    // ship left and where it lands, and the landing erases what it lands on
    // (the tutorial alien too). TeleportFx ignores nudges shorter than
    // TeleportFx.MinimumJump. The tutorial has no teleport cooldown.
    public static bool Arrive(Vector3 from, Vector3 to)
    {
        if (Vector3.Distance(from, to) < TeleportFx.MinimumJump) return false;
        Jumps++;
        LastJumpFrom = from;
        LastJumpTo = to;
        TeleportFx.Play(from, to);
        return true;
    }

    // will move the player left and right baised on touch positions.
    void moveLeft_Right(Vector3 fingerPos)
    {
        //gets a position of the finger on the screen
        //checks position of finger is in bound box
        // (vertically: the same reach as the run, ShipReach, in the view this
        // device shows; it was the constant -4.15 .. 4.5)
        // (sideways: ShipReach's reach too, which follows the rails)
        float reach = ShipReach.HalfWidth;
        if (fingerPos.x <= reach && fingerPos.x > -reach)
        {
            this.transform.position = new Vector3(fingerPos.x,
                ShipReach.ClampY(fingerPos.y + ShipReach.FingerOffset));

            // Allow text to follow player----------------------------

            FollowTexts();

            //--------------------------------------------------------
        }
        else
        {
            this.transform.position = new Vector3(ShipReach.ClampX(fingerPos.x),
                ShipReach.ClampY(fingerPos.y + ShipReach.FingerOffset));
        }

    }

}
