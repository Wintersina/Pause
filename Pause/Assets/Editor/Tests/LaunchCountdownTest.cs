using System.IO;
using UnityEngine;

// The launch countdown lasts exactly one second ("READY"), then "GO" hands over
// steering on that same frame; its text grows within a fixed range. It used to subtract Time.timeSinceLevelLoad every frame (so it
// finished within a few frames) and add 3 to the font size every frame for as
// long as the finger was down.
public static class LaunchCountdownTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[LC] PASS  " : "[LC] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();

        Check("countdown is exactly 1 second", movePlayer.CountdownSeconds == 1f);

        int frames60 = FramesToZero(movePlayer.CountdownSeconds, 1f / 60f);
        Check("at 60 fps the countdown takes 60 frames (" + frames60 + ")", frames60 == 60);
        int frames30 = FramesToZero(movePlayer.CountdownSeconds, 1f / 30f);
        Check("at 30 fps it takes 30 frames (" + frames30 + ")", frames30 == 30);

        foreach (float fps in new[] { 60f, 30f })
        {
            // Frame 1 is the first-touch frame; its delta is the time before the
            // finger landed, so touch -> control is (goFrame - 1) frames.
            float dt = 1f / fps;
            float left = movePlayer.CountdownSeconds;
            int goFrame = -1, controlFrame = -1, labelGoFrame = -1;
            bool readyBeforeGo = true;
            for (int frame = 1; frame <= 1000 && controlFrame < 0; frame++)
            {
                bool live = movePlayer.StepLaunch(ref left, dt);
                string label = movePlayer.LaunchLabel(left);
                if (left <= 0f && goFrame < 0) goFrame = frame;
                if (label == movePlayer.GoLabel && labelGoFrame < 0) labelGoFrame = frame;
                if (live && controlFrame < 0) controlFrame = frame;
                if (!live && label != movePlayer.ReadyLabel) readyBeforeGo = false;
            }
            float touchToControl = (controlFrame - 1) * dt;
            Check(fps + " fps: steering is enabled on the GO frame (go " + goFrame +
                  ", label " + labelGoFrame + ", control " + controlFrame + ")",
                  goFrame > 0 && controlFrame == goFrame && labelGoFrame == goFrame);
            Check(fps + " fps: READY shows until GO", readyBeforeGo);
            Check(fps + " fps: first touch -> control is <= 1.1s (" + touchToControl.ToString("F3") + "s)",
                  touchToControl <= 1.1f && touchToControl >= 0.9f);
        }

        float goFade = movePlayer.GoSeconds;
        Check("GO is a short fade, not a hold (" + goFade + "s)", goFade > 0f && goFade <= 1f);
        Check("GO starts fully visible", movePlayer.GoAlpha(movePlayer.GoSeconds) == 1f);
        Check("GO fades out", movePlayer.GoAlpha(movePlayer.GoSeconds * 0.5f) < 1f && movePlayer.GoAlpha(0f) == 0f);

        float afterHitch = movePlayer.TickCountdown(movePlayer.CountdownSeconds, 5f);
        Check("a long hitch frame doesn't swallow the countdown (" + afterHitch + ")", afterHitch >= 0.89f);
        float hitchRemaining = movePlayer.CountdownSeconds;
        Check("a hitch on the first frame doesn't hand over control",
              !movePlayer.StepLaunch(ref hitchRemaining, 5f));
        Check("a zero-length frame (timeScale 0 / first touch) is harmless",
              movePlayer.TickCountdown(0.5f, 0f) == 0.5f);
        Check("it never goes below zero", movePlayer.TickCountdown(0.01f, 0.05f) == 0f);

        const int baseSize = 40;
        int min = int.MaxValue, max = int.MinValue, previous = 0;
        bool monotonic = true;
        float remaining = movePlayer.CountdownSeconds;
        // Run well past the end, the way the old code kept growing the text.
        for (int i = 0; i < 600; i++)
        {
            remaining = movePlayer.TickCountdown(remaining, 1f / 60f);
            int size = movePlayer.CountdownFontSize(baseSize, remaining);
            if (i > 0 && size < previous) monotonic = false;
            min = Mathf.Min(min, size);
            max = Mathf.Max(max, size);
            previous = size;
        }
        Check("font size never drops below the authored size", min >= baseSize);
        Check("font size is capped (max " + max + ")",
              max <= Mathf.CeilToInt(baseSize * movePlayer.CountdownMaxGrowth));
        Check("font size actually animates up to the cap",
              max == Mathf.RoundToInt(baseSize * movePlayer.CountdownMaxGrowth));
        Check("font size only grows during the countdown", monotonic);
        Check("countdown starts at the authored size",
              movePlayer.CountdownFontSize(baseSize, movePlayer.CountdownSeconds) == baseSize);

        string source = File.ReadAllText("Assets/Scripts/Ship/movePlayer.cs");
        Check("the GO sound plays on the frame GO appears",
              source.Contains("startTimer.text = LaunchLabel(startTimerCounter);\n                    goClip.Play();"));
        Check("movePlayer no longer counts down by timeSinceLevelLoad",
              !source.Contains("-= Time.timeSinceLevelLoad"));
        Check("movePlayer no longer grows the font unbounded", !source.Contains("fontSize += "));

        Debug.Log("[LC] failures: " + fails);
        return fails;
    }

    static int FramesToZero(float seconds, float dt)
    {
        int frames = 0;
        while (seconds > 0f && frames < 100000)
        {
            seconds = movePlayer.TickCountdown(seconds, dt);
            frames++;
        }
        return frames;
    }
}
