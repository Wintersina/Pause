using System.Reflection;
using UnityEngine;

// The game asks for 60 fps once, before the first scene loads. Without it
// phones run Unity's default 30 fps.
public static class FrameRateBootstrapTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[FR] PASS  " : "[FR] FAIL  ") + what);
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

        int frameRate = Application.targetFrameRate;
        int vSync = QualitySettings.vSyncCount;
        try
        {
            var apply = typeof(FrameRateBootstrap).GetMethod("Apply", BindingFlags.Static | BindingFlags.Public);
            var attr = apply == null ? null : apply.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
            Check("the bootstrap runs automatically at startup", attr != null);
            Check("before the first scene loads",
                  attr != null && attr.loadType == RuntimeInitializeLoadType.BeforeSceneLoad);

            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 1;
            FrameRateBootstrap.Apply();
            Check("it targets 60 fps", Application.targetFrameRate == 60);
            Check("and turns vsync off so the target applies", QualitySettings.vSyncCount == 0);
        }
        finally
        {
            Application.targetFrameRate = frameRate;
            QualitySettings.vSyncCount = vSync;
        }

        Debug.Log("[FR] failures: " + fails);
        return fails;
    }
}
