using UnityEngine;

// The player-facing "perch" setting (Options > SHIP PERCH): where the ship
// sits relative to the finger, so a thumb can be kept out of the way of the
// baddies. Two choices:
//   Scale  how far the ship floats from the finger, a multiple of the stock
//          gap (ShipReach.BaseFingerOffset + 1 mm): MinScale .. MaxScale.
//   Tilt   straight above (the stock perch), above-left or above-right.
// ShipReach turns them into the offset every touch controller adds to the
// finger; the usual rail / floor / ceiling clamps keep the ship on screen.
//
// Persisted per device in PlayerPrefs (like SoundSettings; the right gap
// depends on the player's hand and screen), absent = the stock perch:
//   perchScale  float, absent = 1
//   perchTilt   int, absent = 0 (Above)
// Cached after the first read: touch code asks every frame.
public static class PerchSettings
{
    public const string ScaleKey = "perchScale", TiltKey = "perchTilt";
    public const float MinScale = .6f, MaxScale = 1.6f, DefaultScale = 1f;
    public const int Above = 0, AboveLeft = 1, AboveRight = 2, TiltCount = 3;
    // The lean away from straight up, in degrees; positive = to the right.
    public const float TiltDegrees = 35f;
    static readonly string[] Names = { "ABOVE", "ABOVE LEFT", "ABOVE RIGHT" };

    static bool loaded;
    static float scale = DefaultScale;
    static int tilt = Above;

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        scale = Mathf.Clamp(PlayerPrefs.GetFloat(ScaleKey, DefaultScale), MinScale, MaxScale);
        tilt = Mathf.Clamp(PlayerPrefs.GetInt(TiltKey, Above), 0, TiltCount - 1);
    }

    public static float Scale { get { Load(); return scale; } }
    public static int Tilt { get { Load(); return tilt; } }

    // 0 at Above exactly, so the stock perch is bit-for-bit what it always was.
    public static float TiltAngle { get { return TiltAngleOf(Tilt); } }
    public static float SinTilt { get { return Tilt == Above ? 0f : Mathf.Sin(TiltAngle * Mathf.Deg2Rad); } }
    public static float CosTilt { get { return Tilt == Above ? 1f : Mathf.Cos(TiltAngle * Mathf.Deg2Rad); } }

    public static float TiltAngleOf(int tilt)
    {
        return tilt == AboveLeft ? -TiltDegrees : (tilt == AboveRight ? TiltDegrees : 0f);
    }

    public static string TiltName(int tilt) { return Names[Mathf.Clamp(tilt, 0, TiltCount - 1)]; }

    public static void SetScale(float v)
    {
        Load();
        scale = Mathf.Clamp(v, MinScale, MaxScale);
        if (Mathf.Approximately(scale, DefaultScale)) { scale = DefaultScale; PlayerPrefs.DeleteKey(ScaleKey); }
        else PlayerPrefs.SetFloat(ScaleKey, scale);
    }

    public static void SetTilt(int v)
    {
        Load();
        tilt = ((v % TiltCount) + TiltCount) % TiltCount;
        if (tilt == Above) PlayerPrefs.DeleteKey(TiltKey); else PlayerPrefs.SetInt(TiltKey, tilt);
    }

    public static void Save() { PlayerPrefs.Save(); }

    public static void Reset() { SetScale(DefaultScale); SetTilt(Above); }

    // Slider 0..1 <-> scale.
    public static float ToSlider(float s) { return Mathf.InverseLerp(MinScale, MaxScale, s); }
    public static float FromSlider(float v) { return Mathf.Lerp(MinScale, MaxScale, v); }

    // Forget the cache (tests that wipe PlayerPrefs).
    public static void ReloadForTests() { loaded = false; }
}
