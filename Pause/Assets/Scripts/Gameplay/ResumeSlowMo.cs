using UnityEngine;
using UnityEngine.SceneManagement;

// "After speed 15, once the player comes back from a pause, slow time down by
// 40% for about half a second, then ramp back up to the current speed."
//
// Resume is the frame the world goes from frozen (finger up, pauses left) to
// running again (finger down). moveBackGround owns Time.timeScale and asks this
// class for the scale on every frame:
//
//   running frame -> Time.timeScale = ResumeSlowMo.Apply(1f);
//   frozen frame  -> Time.timeScale = ResumeSlowMo.Freeze();   // always 0
//
// Apply() spots the frozen -> running edge itself and calls OnResume(), so the
// hook in moveBackGround stays a three-line change.
//
// Timing (real / unscaled seconds from the resume frame):
//   0.0 - 0.5   timeScale 0.6 (40% slower), flat
//   0.5 - 0.9   ease-out back to 1.0:  0.6 + 0.4 * (1 - (1 - t)^2)
// Quadratic ease-out: most of the speed comes back early in the ramp (the game
// "kicks" back into motion) and the last few percent settle in gently, so the
// hand-off to full speed has no visible step. 0.4s sits in the middle of the
// 0.3-0.5s window; shorter felt like a snap, longer started to feel like lag.
//
// Rules:
// - Only when the HUD's SPEED (round(moveBackGround.speed * 100), as score.cs
//   and HudStyler show it) is >= 15 at the moment of the resume.
// - Not in the tutorial (scene tutorialS5 / startMenu.youAreInTutorial).
// - The launch touch and anything during the 1s READY countdown is not a resume.
// - Refreezing cancels it (timeScale 0 as always); the next resume starts fresh.
// - Death cancels it.
// - The ultimate's CinematicClear always wins: moveBackGround applies the
//   cinematic scale without asking us, and seeing the cinematic (directly, or
//   as a gap in our per-frame calls) cancels the effect so it never resumes
//   underneath the cinematic's own ease back to 1x.
// - Out of pauses the world never freezes, so there is no resume to react to.
public static class ResumeSlowMo
{
    public const int MinHudSpeed = 15;
    public const float SlowScale = 0.6f;
    public const float HoldSeconds = 0.5f;
    public const float RampSeconds = 0.4f;
    public const float TotalSeconds = HoldSeconds + RampSeconds;

    // Same clamp the launch countdown uses, so a hitch can't skip it.
    const float MaxStep = 0.1f;

    // Tests drive time through this; null means Time.unscaledTime.
    public static System.Func<float> ClockOverride;
    static float Now => ClockOverride != null ? ClockOverride() : Time.unscaledTime;

    static bool active;
    static float startedAt;
    // A run starts frozen: the world waits for the launch touch.
    static bool frozen = true;
    static bool launched;
    static float runSeconds;      // real time the world has run since launch
    static float lastTick = -1f;  // unscaled time of the last running frame
    static int lastFrame = -1;

    public static bool IsActive => active;

    // The effect's own multiplier: 1 when idle.
    public static float CurrentScale => ScaleAt(active ? Now - startedAt : TotalSeconds);

    public static float ScaleAt(float elapsed)
    {
        if (elapsed < HoldSeconds) return SlowScale;
        float t = Mathf.Clamp01((elapsed - HoldSeconds) / RampSeconds);
        float eased = 1f - (1f - t) * (1f - t);
        return Mathf.Lerp(SlowScale, 1f, eased);
    }

    public static bool InTutorial =>
        startMenu.youAreInTutorial || SceneManager.GetActiveScene().name == score.TutorialScene;

    public static bool SpeedQualifies =>
        Mathf.RoundToInt(moveBackGround.speed * 100f) >= MinHudSpeed;

    // ---- hooks ------------------------------------------------------------

    // moveBackGround's running branch. Safe to call from several instances
    // in one frame: only the first sees the frozen -> running edge.
    public static float Apply(float baseScale)
    {
        int frame = Time.frameCount;
        // moveBackGround skips us while the cinematic runs; a gap in our
        // frames means it took over, and its ease-out owns the way back.
        if (active && lastFrame >= 0 && frame - lastFrame > 1) Cancel();
        lastFrame = frame;

        if (ShipPowerController.CinematicClearActive)
        {
            Cancel();
            return ShipPowerController.CinematicTimeScale;
        }
        if (buttonClicks.playerDied)
        {
            Cancel();
            return baseScale;
        }

        float now = Now;
        if (frozen)
        {
            frozen = false;
            OnResume();
        }
        else if (lastTick >= 0f)
        {
            runSeconds += Mathf.Clamp(now - lastTick, 0f, MaxStep);
        }
        lastTick = now;

        float scale = CurrentScale;
        if (active && (scale >= 1f || now - startedAt >= TotalSeconds)) active = false;
        return baseScale * scale;
    }

    // moveBackGround's frozen branch (finger up, or dead).
    public static float Freeze()
    {
        lastFrame = Time.frameCount;
        OnFreeze();
        return 0f;
    }

    // The world just started running again after a freeze.
    public static void OnResume()
    {
        frozen = false;
        if (!launched)
        {
            // The launch touch starts the run; it is not a resume.
            launched = true;
            runSeconds = 0f;
            return;
        }
        // Lifting and retouching during READY is still the launch.
        if (runSeconds < movePlayer.CountdownSeconds) return;
        if (buttonClicks.playerDied || ShipPowerController.CinematicClearActive) return;
        if (InTutorial || !SpeedQualifies) return;

        active = true;
        startedAt = Now;
    }

    public static void OnFreeze()
    {
        Cancel();
        frozen = true;
        lastTick = -1f;
    }

    public static void Cancel()
    {
        active = false;
    }

    // Fresh run: the next running frame is the launch.
    public static void ResetRun()
    {
        active = false;
        frozen = true;
        launched = false;
        runSeconds = 0f;
        lastTick = -1f;
        lastFrame = -1;
    }

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ResetRun();
}
