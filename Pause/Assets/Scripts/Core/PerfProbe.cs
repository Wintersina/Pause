using UnityEngine;

// Counts the two kinds of work that must never land in the middle of a fight: a texture's pixels read back to
// the CPU (a GPU sync on a phone, plus a managed array the size of the drawing) and a slice made at play time
// that nobody warmed. Every site that reads pixels reports here, so a soak (PerfSoakRunner) and the suites
// (PerfHitchTest) can say WHO did it and fail when one happens after the warm-up.
//
// Costs one increment per readback; the listener (tests, the soak) is only called when one is set.
public static class PerfProbe
{
    // Pixel reads since the last Reset: all of them, and the ones that went through the GPU.
    public static int PixelReads { get; private set; }
    public static int GpuReadbacks { get; private set; }
    public static long PixelsRead { get; private set; }

    // (site, the drawing or texture, pixels, through the GPU)
    public static System.Action<string, Object, int, bool> OnPixelRead;

    public static void PixelRead(string site, Object what, int pixels, bool gpu)
    {
        PixelReads++;
        PixelsRead += pixels;
        if (gpu) GpuReadbacks++;
        if (OnPixelRead != null) OnPixelRead(site, what, pixels, gpu);
    }

    public static void Reset()
    {
        PixelReads = 0;
        GpuReadbacks = 0;
        PixelsRead = 0;
    }
}
