using UnityEngine;

// The death vibration's pure parts: the pattern (one strong brief thump, then a
// softer rumble, brief overall), the gate (setting, phone, no double fire, not
// paused), the saved on/off setting, and the Options row caption.
public static class DeathHapticsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[HAPTIC] PASS  " : "[HAPTIC] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        int saved = PlayerPrefs.GetInt(DeathHaptics.EnabledKey, 1);
        bool had = PlayerPrefs.HasKey(DeathHaptics.EnabledKey);
        Pattern();
        Gate();
        Setting();
        if (had) PlayerPrefs.SetInt(DeathHaptics.EnabledKey, saved); else PlayerPrefs.DeleteKey(DeathHaptics.EnabledKey);
        Debug.Log("[HAPTIC] failures: " + fails);
        return fails;
    }

    static void Pattern()
    {
        var t = DeathHaptics.Timings; var a = DeathHaptics.Amplitudes;
        Check("timings and amplitudes line up", t.Length == a.Length && t.Length == 4);
        Check("starts with a thump of 60-80 ms at full strength", t[0] == 0 && t[1] >= 60 && t[1] <= 80 && a[1] == 255);
        Check("then a gap and a softer, fading rumble", a[2] == 0 && a[3] > 0 && a[3] < a[1]);
        long total = DeathHaptics.TotalMs();
        Check("brief overall: 150-250 ms (" + total + ")", total >= 150 && total <= DeathHaptics.MaxTotalMs);
        bool valid = true;
        for (int i = 0; i < a.Length; i++) valid &= a[i] >= 0 && a[i] <= 255 && t[i] >= 0;
        Check("amplitudes are 0..255", valid);
    }

    static void Gate()
    {
        float d = DeathHaptics.DebounceSeconds;
        Check("fires when on, on a phone, running, first time", DeathHaptics.ShouldFire(true, true, 100f, float.NegativeInfinity, 1f));
        Check("not when the setting is off", !DeathHaptics.ShouldFire(false, true, 100f, float.NegativeInfinity, 1f));
        Check("not on a platform without a motor (Editor / desktop)", !DeathHaptics.ShouldFire(true, false, 100f, float.NegativeInfinity, 1f));
        Check("not while the game is paused", !DeathHaptics.ShouldFire(true, true, 100f, float.NegativeInfinity, 0f));
        Check("not twice for one death", !DeathHaptics.ShouldFire(true, true, 100f + d * .5f, 100f, 1f));
        Check("again for the next death", DeathHaptics.ShouldFire(true, true, 100f + d + .1f, 100f, 1f));
        DeathHaptics.OnFatalHit();   // Editor: must be a harmless no-op
        Check("OnFatalHit never throws in the Editor", true);
    }

    static void Setting()
    {
        PlayerPrefs.DeleteKey(DeathHaptics.EnabledKey);
        Check("vibration defaults ON", DeathHaptics.Enabled);
        DeathHaptics.Enabled = false;
        Check("can be switched off", !DeathHaptics.Enabled && PlayerPrefs.GetInt(DeathHaptics.EnabledKey, 1) == 0);
        DeathHaptics.Enabled = true;
        Check("switching on clears the key", DeathHaptics.Enabled && !PlayerPrefs.HasKey(DeathHaptics.EnabledKey));
        Check("row caption shows the state", VibrationOptions.Caption(true) == "VIBRATION  ON" && VibrationOptions.Caption(false) == "VIBRATION  OFF");
    }
}
