using UnityEngine;

// The HapticGate brand logo outside the splash: the closed steel gate in smoke with
// the red "HapticGate" wordmark under it (the lockup, approved closed-gate logo from
// HapticGate-brand/closed). One file, one place: replace the PNG to change the logo.
//
//   Resources/HapticGate/hapticgate_lockup.png   640x640  closed gate + smoke + wordmark (credits)
//
// A missing file returns null and the caller simply skips the logo.
public static class HapticGateMark
{
    public const string LockupSlot = "HapticGate/hapticgate_lockup";

    public static Sprite Lockup() { return Resources.Load<Sprite>(LockupSlot); }
}
