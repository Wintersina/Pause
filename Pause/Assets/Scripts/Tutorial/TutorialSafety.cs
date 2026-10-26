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

    // ---- Crashes: play-friendly, never punishing, always recoverable ----

    public enum CrashReply { None, Ouch, Playful, Recover }

    public const string OuchLine = "Ouch! Try to steer around them.";
    public const string PlayfulLine = "Okay, okay, you can hit them, but let's try the real way!";
    // Seconds between two crash lines (no spam).
    public const float CrashLineCooldown = 8f;

    // What the robot does about a crash. crashes: crashes since the last
    // recovery (this one included). atCap: the hit took the last spare heart
    // (the ship cannot be lost, so it is set back instead). A line is never
    // said over another line, inside the cooldown, or during the fly-in.
    public static CrashReply DecideCrash(int crashes, int recoveries, bool atCap, float sinceLastCrashLine, bool speakerBusy, bool flyingIn)
    {
        if (flyingIn || crashes < 1) return CrashReply.None;
        if (atCap && crashes >= 2) return CrashReply.Recover;
        if (crashes != 1 || speakerBusy || sinceLastCrashLine < CrashLineCooldown) return CrashReply.None;
        return recoveries == 0 ? CrashReply.Ouch : CrashReply.Playful;
    }

    // ---- Ship guard: a fling can never lose the ship ----

    public static bool IsFinite(Vector3 v)
    {
        return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
    }

    public static bool IsFinite(float f)
    {
        return !float.IsNaN(f) && !float.IsInfinity(f);
    }

    // A screen point from the touch layer: NaN / infinity falls back to the
    // last good point, and a point flung beyond the screen is pinned to it.
    public static Vector2 SafeScreenPoint(Vector2 raw, Vector2 lastGood, float width, float height)
    {
        if (!IsFinite(raw.x) || !IsFinite(raw.y)) raw = lastGood;
        return new Vector2(Mathf.Clamp(raw.x, 0f, Mathf.Max(1f, width)), Mathf.Clamp(raw.y, 0f, Mathf.Max(1f, height)));
    }

    // Where the ship should be: a non-finite position goes back to the last
    // valid one (or the start); one outside the hard box (the playable reach
    // plus `slack`) is pinned to its edge. z is kept.
    public static Vector3 RecoverShip(Vector3 pos, Vector3 lastValid, Vector3 start,
                                      float halfWidth, float bottom, float top, float slack)
    {
        if (!IsFinite(pos)) pos = IsFinite(lastValid) ? lastValid : start;
        if (!IsFinite(pos)) pos = Vector3.zero;
        float hw = Mathf.Abs(halfWidth) + slack;
        return new Vector3(Mathf.Clamp(pos.x, -hw, hw), Mathf.Clamp(pos.y, Mathf.Min(bottom, top) - slack, Mathf.Max(bottom, top) + slack), pos.z);
    }

    // ---- Step watchdog: no step can stay stuck ----

    public enum WatchdogAction { None, Reset, OfferSkip, Advance }

    // Real (unscaled) seconds a step may go without progress before its objects
    // are set up again, and how many failed attempts lead to the SKIP prompt and
    // to moving on by itself.
    public const float StuckSeconds = 25f;
    public const float TargetMissingSeconds = 2f;
    public const int OfferSkipAfter = 3;
    public const int AdvanceAfter = 5;
    public const string SkipOfferLine = "Stuck? Tap *SKIP* to go on.";

    public struct Watchdog
    {
        public float sinceProgress, targetMissing;
        public int attempts;

        // One frame. progressed: the player advanced the step; targetMissing:
        // what the step needs (atom / wall / alien) is gone and nothing is
        // bringing it back. dt: unscaled seconds (the world may be frozen).
        public WatchdogAction Tick(float dt, bool progressed, bool missing)
        {
            if (!IsFinite(dt) || dt < 0f) dt = 0f;
            dt = Mathf.Min(dt, 1f);   // a stalled frame (backgrounded app) is not 25 s of being stuck
            if (progressed) { sinceProgress = 0f; targetMissing = 0f; attempts = 0; return WatchdogAction.None; }
            sinceProgress += dt;
            targetMissing = missing ? targetMissing + dt : 0f;
            if (targetMissing < TargetMissingSeconds && sinceProgress < StuckSeconds) return WatchdogAction.None;
            sinceProgress = 0f;
            targetMissing = 0f;
            attempts++;
            if (attempts >= AdvanceAfter) return WatchdogAction.Advance;
            if (attempts == OfferSkipAfter) return WatchdogAction.OfferSkip;
            return WatchdogAction.Reset;
        }
    }
}
