using UnityEngine;

// Runs the game at 60 fps on phones.
//
// iOS and Android default to 30 fps when targetFrameRate is left at -1, which
// made the scrolling and the held-touch steering visibly choppy. Mobile
// ignores vSyncCount, but desktop and the editor honour it over
// targetFrameRate, so it is cleared to make the cap apply everywhere.
public static class FrameRateBootstrap
{
    public const int TargetFrameRate = 60;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Apply()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = TargetFrameRate;
    }
}
