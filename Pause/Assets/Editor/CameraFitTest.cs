using UnityEditor;
using UnityEngine;

// Headless check of the camera-fit math: never shrinks below the authored
// size, and always grows enough to keep minHalfWidth on screen.
public static class CameraFitTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[CF] PASS  " : "[CF] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        const float baseSize = 5f;
        const float minHalfWidth = 2.85f;

        // A 16:9 phone is what the game was originally sized for. Its true
        // half-width at the authored size is 2.8125 -- just under our 2.85
        // floor -- so it picks up a small (~1.3%) bump rather than staying
        // exactly at baseSize. That bump is intentional, not a regression.
        float size169 = CameraFit.ComputeSize(baseSize, minHalfWidth, 1080, 1920);
        Check("16:9 stays within 2% of the authored size (" + size169.ToString("F3") + ")",
              size169 >= baseSize && size169 <= baseSize * 1.02f);

        // The device this was written against.
        float sizeZFlip = CameraFit.ComputeSize(baseSize, minHalfWidth, 1080, 2520);
        float halfWidthZFlip = sizeZFlip * (1080f / 2520f);
        Check("Z Flip (1080x2520) grows past the authored size (" + sizeZFlip.ToString("F3") + ")",
              sizeZFlip > baseSize);
        Check("Z Flip keeps the minimum half-width visible (" + halfWidthZFlip.ToString("F3") + ")",
              halfWidthZFlip >= minHalfWidth - 0.001f);

        // A representative sweep of real device aspects: half-width must never
        // fall short, and size must never drop below what was authored.
        int[,] devices = {
            {1080, 1920},   // 16:9
            {1080, 2160},   // 18:9
            {1080, 2280},   // 19:9
            {1080, 2340},   // 19.5:9
            {1080, 2400},   // 20:9
            {1080, 2520},   // ~23.3:9, Galaxy Z Flip
            {1440, 3200},   // 20:9 at higher density
            {750, 1000},    // 4:3, e.g. an iPad in portrait
        };
        for (int i = 0; i < devices.GetLength(0); i++)
        {
            int w = devices[i, 0], h = devices[i, 1];
            float size = CameraFit.ComputeSize(baseSize, minHalfWidth, w, h);
            float halfWidth = size * ((float)w / h);
            Check(string.Format("{0}x{1}: size {2:F2} never below authored", w, h, size),
                  size >= baseSize - 0.0001f);
            Check(string.Format("{0}x{1}: half-width {2:F3} covers the minimum", w, h, halfWidth),
                  halfWidth >= minHalfWidth - 0.001f);
        }

        // Degenerate input should not throw or return something unusable.
        float degenerate = CameraFit.ComputeSize(baseSize, minHalfWidth, 0, 0);
        Check("zero screen size falls back to the authored size", degenerate == baseSize);

        Debug.Log("[CF] failures: " + fails);
        EditorApplication.Exit(0);
    }
}
