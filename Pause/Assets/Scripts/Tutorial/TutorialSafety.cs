using UnityEngine;

// The tutorial's pure decisions, kept apart from Hints so tests can drive them:
// where the "touch here" ring sits, and when a death in the tutorial is taken
// back (the ship is brought in again and the same step is retried).
public static class TutorialSafety
{
    // What the robot says when the player is set back.
    public const string TryAgainLine = "Whoa, whoa there! Let's try again.";

    // The ring for the jump step: on the far side of the wall (above it), in
    // the ship's lane. jumpTargetY is TutorialWall.JumpTargetY, anchored on the
    // wall, so it is above the row on every aspect ratio. (The ring used to
    // stay at its home spot, low on the screen, below the ship.)
    public static Vector3 JumpTouchPoint(float shipX, float jumpTargetY, float laneHalf)
    {
        return new Vector3(Mathf.Clamp(shipX, -laneHalf, laneHalf), jumpTargetY, 0f);
    }

    // A death in the tutorial scene is never final: it is taken back (the
    // normal death flow must not run) unless the lesson is over.
    public static bool ShouldRecover(bool inTutorial, bool playerDied, bool ending)
    {
        return inTutorial && playerDied && !ending;
    }

    // lifeCounter after a recovery: every heart back, except that the ship
    // stays damaged until the green atom has mended it.
    public static int LifeCounterAfterRecovery(bool healed, int maxLife)
    {
        return !healed && maxLife >= 2 ? maxLife - 1 : 0;
    }
}
