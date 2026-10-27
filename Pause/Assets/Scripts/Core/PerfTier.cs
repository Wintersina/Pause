using UnityEngine;

// The ONE low-end phone decision. Optional effects ask here (PerfTier.LowEnd) instead of repeating the memory
// thresholds: CloudShadowLayer.TierFor (shadow count), WorldArtPreload.BeginRunStart
// (no menu-time hold). The thresholds themselves are KillCamRecorder.LowSystemMemoryMb
// (3600 MB RAM) and LowGraphicsMemoryMb (1024 MB GPU budget); KillCamRecorder.Gap keeps its own copy of the same two comparisons (PerfPass2Test pins the two to each other over a grid of
// memory sizes); a value of 0 or less means "unknown" and is not low.
// Mid and high-end phones are never affected by anything that asks this.
public static class PerfTier
{
    public static bool IsLowEnd(int systemMemoryMb, int graphicsMemoryMb)
    {
        return (systemMemoryMb > 0 && systemMemoryMb < KillCamRecorder.LowSystemMemoryMb) ||
               (graphicsMemoryMb > 0 && graphicsMemoryMb < KillCamRecorder.LowGraphicsMemoryMb);
    }

    static int overrideState;   // 0 none, 1 forced low, 2 forced not low (tests)
    static bool? cached;

    // This device. Tests may force it with Force(); Force(null) restores the real answer.
    public static bool LowEnd
    {
        get
        {
            if (overrideState != 0) return overrideState == 1;
            if (cached == null) cached = IsLowEnd(SystemInfo.systemMemorySize, SystemInfo.graphicsMemorySize);
            return cached.Value;
        }
    }

    public static void Force(bool? low) { overrideState = low == null ? 0 : (low.Value ? 1 : 2); }
}
