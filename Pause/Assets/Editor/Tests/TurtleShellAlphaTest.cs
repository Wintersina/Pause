using UnityEngine;

// The Turtle's Hard Shell dome is drawn 20% less transparent than before (alpha x1.2, clamped to 1).
public static class TurtleShellAlphaTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TSA] PASS  " : "[TSA] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        float want = Mathf.Min(1f, SecretPowerController.ShellAlphaWas * SecretPowerController.ShellAlphaBoost);
        Check("the old dome alpha was .85", Mathf.Approximately(SecretPowerController.ShellAlphaWas, .85f));
        Check("the boost is x1.2", Mathf.Approximately(SecretPowerController.ShellAlphaBoost, 1.2f));
        Check("the dome alpha is the old one x1.2 clamped to 1", Mathf.Approximately(SecretPowerController.ShellAlpha, want));
        Check("the dome alpha never exceeds 1", SecretPowerController.ShellAlpha <= 1f);
        Check("and is more opaque than before", SecretPowerController.ShellAlpha > SecretPowerController.ShellAlphaWas);
        return fails;
    }
}
