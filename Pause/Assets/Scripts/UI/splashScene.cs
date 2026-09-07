using UnityEngine;
using UnityEngine.SceneManagement;

// The Haptic Gate title card.
//
// It used to hold for a flat three seconds with no way past it, which is a long
// time to stare at a logo you have already seen -- especially on a game whose
// runs are a couple of minutes. It is shorter now, and a tap skips it.
public class splashScene : MonoBehaviour
{
    [Tooltip("How long the card holds if the player does not skip it.")]
    public float holdSeconds = 1.25f;

    [Tooltip("Ignore input for a moment so a stray tap carried over from a " +
             "previous screen cannot skip the card before it is even seen.")]
    public float skipLockout = 0.15f;

    float elapsed;
    bool leaving;

    void Update()
    {
        // Unscaled: this is the first scene, and a timeScale left at 0 by a
        // previous run would otherwise stall the card indefinitely.
        elapsed += Time.unscaledDeltaTime;

        if (elapsed >= holdSeconds || (elapsed >= skipLockout && Skipped()))
            Leave();
    }

    static bool Skipped()
    {
        return Input.GetMouseButtonDown(0)
            || Input.touchCount > 0
            || Input.anyKeyDown;
    }

    void Leave()
    {
        if (leaving) return;
        leaving = true;
        Time.timeScale = 1f;
        SceneManager.LoadScene("startS4");
    }
}
