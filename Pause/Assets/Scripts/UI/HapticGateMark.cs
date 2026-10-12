using UnityEngine;

// The HapticGate brand art used outside the splash door: the open steel gate
// (two parted leaves, red light through the seam) and the same gate with the
// red "HapticGate" wordmark under it (the lockup). Both are cut from the brand
// animation's final frame (frame_0179) with alpha taken from brightness, so
// they sit on any dark ground exactly as they do on black. 512 px wide.
//
//   Resources/HapticGate/hapticgate_mark.png     512x379  gate + seam (splash final frame)
//   Resources/HapticGate/hapticgate_lockup.png   512x523  gate + seam + wordmark (credits)
//
// A missing file returns null and the caller simply skips the mark.
public static class HapticGateMark
{
    public const string MarkSlot = "HapticGate/hapticgate_mark";
    public const string LockupSlot = "HapticGate/hapticgate_lockup";

    public static Sprite Mark() { return Resources.Load<Sprite>(MarkSlot); }
    public static Sprite Lockup() { return Resources.Load<Sprite>(LockupSlot); }
}
