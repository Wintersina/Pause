using UnityEngine;

// Reads the player's presses for the HapticGate splash.
//
// A press is discrete: only the frame a button / finger goes DOWN counts, never
// a held one, and any number of simultaneous fingers or the mouse that touch
// input emulates still count as ONE press that frame. Escape / Android back is
// not read here (only BackNavigatorRunner reads it): splashScene registers a
// BackNavigator layer that skips the card at once.
public static class SplashInput
{
    public struct Sample
    {
        public bool tap;
    }

    public static Sample ReadLegacy()
    {
        var s = new Sample();
        bool press = Input.GetMouseButtonDown(0);
        for (int i = 0; i < Input.touchCount; i++)
            if (Input.GetTouch(i).phase == TouchPhase.Began) press = true;
        if (!press && Input.anyKeyDown) press = true;   // any key
        s.tap = press;
        return s;
    }
}
