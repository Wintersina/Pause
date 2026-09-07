using UnityEngine;

// Single source of truth for "is the player holding the screen".
//
// The whole game -- movement, scrolling, scoring, spawning, music -- was gated
// on Input.touchCount, which is always 0 on desktop. That made the game
// unplayable and untestable outside a phone, including in the editor.
//
// These fall back to the mouse when there is no touchscreen, so the same build
// works on iOS, Android, desktop and in Play mode.
public static class TouchInput
{
    // True while exactly one finger is down, or the left mouse button is held.
    // Matches the original "touchCount > 0 && touchCount <= 1" intent.
    public static bool IsPressed
    {
        get
        {
            if (Input.touchSupported && Input.touchCount > 0)
                return Input.touchCount == 1;

            return Input.GetMouseButton(0);
        }
    }

    // A second finger on the screen -- the game's only spare input, used to
    // trigger the active ship's power. Falls back to the right mouse button
    // (or space) so it is reachable on desktop and in the editor.
    public static bool SecondaryPressed
    {
        get
        {
            if (Input.touchSupported && Input.touchCount > 0)
                return Input.touchCount >= 2;

            return Input.GetMouseButton(1) || Input.GetKey(KeyCode.Space);
        }
    }

    // True only on the frame the second finger first lands.
    public static bool SecondaryPressedThisFrame
    {
        get
        {
            if (Input.touchSupported && Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                    if (i >= 1 && Input.GetTouch(i).phase == TouchPhase.Began)
                        return true;
                return false;
            }

            return Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Space);
        }
    }

    // Screen-space position of the active finger, or the mouse cursor.
    public static Vector2 Position
    {
        get
        {
            if (Input.touchSupported && Input.touchCount > 0)
                return Input.GetTouch(0).position;

            return Input.mousePosition;
        }
    }
}
