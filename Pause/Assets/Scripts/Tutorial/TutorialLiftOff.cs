using UnityEngine;
using UnityEngine.SceneManagement;

// LIFT OFF on the Tutorial Complete card: the ship flies into a portal (the
// world-entry gateway played backwards, PortalArrival.SpawnDeparture), and
// when it has gone in the first level loads -- where the run begins with the
// usual portal arrival (WorldEntry.Plan: a run that is not a REPLAY plays it),
// so the ship zooms through one gateway and comes out of the next.
//
// It plays exactly once: a second press (or a tap on HOME / the scene's
// other buttons mid-flight) does nothing, and the level is loaded exactly
// once, straight to gameS1 -- never via the menu. Unscaled time throughout:
// the tutorial's world sits at timeScale 0.
public class TutorialLiftOff : MonoBehaviour
{
    public const string NextScene = "gameS1";
    // How long the departure takes (the arrival's own 2.6 s, a little quicker).
    public const float Seconds = 2f;

    public static TutorialLiftOff Live { get; private set; }
    // Departures started / levels loaded since the last Reset (tests).
    public static int Started { get; private set; }
    public static int Loaded { get; private set; }
    // Swapped by tests; the game loads the scene.
    public static System.Action LoadGame = () => SceneManager.LoadScene(NextScene);

    PortalArrival portal;
    float clock;
    bool done;

    public static bool Playing { get { return Live != null && !Live.done; } }
    public PortalArrival Portal { get { return portal; } }
    public float Clock { get { return clock; } }

    public static void Reset() { Started = 0; Loaded = 0; Live = null; }

    // Starts the departure (once). Returns the running one.
    public static TutorialLiftOff Begin()
    {
        if (Live != null) return Live;
        Started++;
        var go = new GameObject("~TutorialLiftOff");
        var l = go.AddComponent<TutorialLiftOff>();
        Live = l;

        // the card goes (a tap during the flight must not reach its buttons)
        var card = Object.FindFirstObjectByType<TutorialCompletePanel>(FindObjectsInactive.Include);
        if (card != null) card.gameObject.SetActive(false);

        var ship = Object.FindFirstObjectByType<movePlayerInTut>();
        if (ship == null) { l.Finish(); return l; }
        float bottom, top;
        TutorialAtomDrift.View(out bottom, out top);
        var from = ship.transform.position;
        // the gateway opens a little way ahead of the ship, well inside the view
        var at = new Vector3(from.x, Mathf.Min(from.y + 2.4f, top - 1.5f), from.z);
        var theme = WorldManager.Worlds[0];
        l.portal = PortalArrival.SpawnDeparture(ship.transform, theme.portalColor, at);
        if (l.portal == null) l.Finish();
        return l;
    }

    void Update() { Step(Time.unscaledDeltaTime); }

    // One frame (unscaled seconds); public so tests and the preview can run it.
    public void Step(float dt)
    {
        if (done || dt <= 0f) return;
        clock += Mathf.Min(dt, PortalArrival.MaxStep);
        if (portal != null)
        {
            // the portal's own clock runs PortalArrivalTimeline.Seconds in `Seconds`
            portal.Step(Mathf.Min(dt, PortalArrival.MaxStep) * (PortalArrivalTimeline.Seconds / Seconds));
            if (portal == null || portal.State == PortalArrival.Stage.Done) { Finish(); return; }
        }
        if (clock >= Seconds + .5f) Finish();
    }

    // The ship is in: the run begins (what REPLAY used to do at once).
    void Finish()
    {
        if (done) return;
        done = true;
        Loaded++;
        startMenu.youAreInTutorial = false;
        moveBackGround.speed = 0f;
        score.totalCurrency = 0;
        Time.timeScale = 1f;
        LoadGame();
    }

    void OnDestroy() { if (Live == this) Live = null; }
}
