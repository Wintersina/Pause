using UnityEngine;
using UnityEngine.SceneManagement;

// Lift-off hand-off for the space dock (lives on shopS6's UIManager).
//
// This used to steer every docked ship around by hand each frame. The dock's
// berths, selection and launch animation now belong to SpaceDock; what stays
// here is the static state other scripts still read and set, and the
// flyOffChecker hand-off: anything that arms it (menuButton.play()) gets the
// equipped ship launched by the dock.
public class rotateRight : MonoBehaviour {

    public static bool flyOffChecker;
    public static float flyOffTimer;
    public static int shipSelected;

    void Start()
    {
        flyOffChecker = false;
        flyOffTimer = -1f;
        shipSelected = 0;
    }

    void Update()
    {
        if (!flyOffChecker) return;
        var dock = SpaceDock.Instance;
        if (dock != null)
        {
            flyOffChecker = false;
            if (!dock.Launching) dock.LiftOff();
            return;
        }
        // No dock (should not happen in shopS6): fall back to the old timer.
        flyOffTimer -= Time.unscaledDeltaTime;
        if (flyOffTimer > 0f) return;
        flyOffChecker = false;
        Time.timeScale = 1f;
        SceneManager.LoadScene("gameS1");
    }
}
