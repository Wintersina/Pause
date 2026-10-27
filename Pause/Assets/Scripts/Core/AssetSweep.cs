using UnityEngine;

// Resources.UnloadUnusedAssets() is a mark-and-sweep over every loaded object:
// on a phone it stalls the main thread for a good fraction of a frame or more,
// so run in the middle of play it is a visible hitch. The backdrop crossfade
// used to run it the frame the outgoing world's set was destroyed, which on a
// portal transition is a frame the player is flying through. Instead the
// sweep is DEFERRED: whoever frees something big asks for one (Defer), and it
// runs the next time the view is covered (Covered: the planetfall and lift-off
// clouds, each frame they hide the whole view, which is a no-op while nothing
// is pending), or is dropped when a scene load does the same work anyway
// (Drop). Now() is for a caller that is itself under cover (Liftoff.Swap).
//
// Pure bookkeeping apart from the sweep itself, which tests replace (Unload).
public static class AssetSweep
{
    public static bool Pending { get; private set; }
    // Sweeps actually run (tests, dev overlays).
    public static int Runs { get; private set; }
    public static System.Action Unload = DefaultUnload;
    static void DefaultUnload() { if (Application.isPlaying) Resources.UnloadUnusedAssets(); }

    // Something big was just released: sweep at the next covered moment.
    public static void Defer() { Pending = true; }

    // The whole view is hidden this frame: run a pending sweep now.
    public static void Covered()
    {
        if (!Pending) return;
        Pending = false;
        Runs++;
        Unload();
    }

    // Under cover already: sweep now, pending or not.
    public static void Now()
    {
        Pending = false;
        Runs++;
        Unload();
    }

    // A scene load frees unused assets itself: nothing left to sweep.
    public static void Drop() { Pending = false; }

    // Tests.
    public static void Reset() { Pending = false; Runs = 0; Unload = DefaultUnload; }
}
