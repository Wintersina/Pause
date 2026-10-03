using System.IO;
using UnityEngine;

// The launch countdown lasts its full two seconds and its text grows within a
// fixed range. It used to subtract Time.timeSinceLevelLoad every frame (so it
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

        int frames60 = FramesToZero(movePlayer.CountdownSeconds, 1f / 60f);
        Check("at 60 fps the countdown takes ~120 frames (" + frames60 + ")", frames60 >= 119 && frames60 <= 121);
        int frames30 = FramesToZero(movePlayer.CountdownSeconds, 1f / 30f);
        Check("at 30 fps it takes ~60 frames (" + frames30 + ")", frames30 >= 59 && frames30 <= 61);
        int framesGo = FramesToZero(movePlayer.GoSeconds, 1f / 60f);
        Check("GO stays up ~2s at 60 fps (" + framesGo + " frames)", framesGo >= 119 && framesGo <= 121);

        float afterHitch = movePlayer.TickCountdown(movePlayer.CountdownSeconds, 5f);
        Check("a long hitch frame doesn't swallow the countdown", afterHitch > 1.8f);
        Check("a zero-length frame (timeScale 0 / first touch) is harmless",
              movePlayer.TickCountdown(1f, 0f) == 1f);
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
