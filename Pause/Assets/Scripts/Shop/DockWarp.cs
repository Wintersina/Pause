using UnityEngine;

// The warp between two destination planets in the Space Dock: an 18-frame star-streak strip
// (Worlds/Dock/warp_streaks, 576x1024 frames: a strip or a grid, frame 1 top-left, the layout is read from the
// texture) and its per-frame curves (Worlds/Dock/warp_scale.json):
//
//   frames 1..8    the OLD planet shrinks (outScale 1 -> .05) and fades (outAlpha 1 -> 0) while the streaks build
//   frames 9..18   the NEW planet zips in from small (inScale .05 -> 1.04 -> 1, a small overshoot) and fades in
//
// All pure data and maths here (no scene objects); DockPlanet plays it. The curves are sampled BETWEEN frames, so
// the motion is smooth at any frame rate; the streak sprite itself steps at the strip's own rate.
public sealed class DockWarp
{
    public const string ScalePath = "Worlds/Dock/warp_scale";
    public const string StreaksPath = "Worlds/Dock/warp_streaks";
    public const int Frames = 18;
    // Frame aspect (height / width) of the streak art: 576 x 1024.
    public const float Aspect = 1024f / 576f;
    // Total seconds of a warp, and the frame index (0-based, fractional) at which the old planet has gone and the new
    // one starts: a warp waits here for the new planet's art if it has not arrived yet.
    public static float Seconds = .7f;
    public const float HoldIndex = 7f;

    [System.Serializable] class Row { public float outScale, inScale, outAlpha, inAlpha; }
    [System.Serializable] class Rows { public Row[] f; }

    public readonly float[] outScale = new float[Frames], inScale = new float[Frames];
    public readonly float[] outAlpha = new float[Frames], inAlpha = new float[Frames];
    public bool FromFile { get; private set; }

    // The art's own curves, or (file missing / short) the first drop's numbers.
    public static DockWarp Load()
    {
        var w = new DockWarp();
        w.Defaults();
        var json = Resources.Load<TextAsset>(ScalePath);
        if (json != null)
        {
            // a bare JSON array: JsonUtility needs an object round it
            var rows = JsonUtility.FromJson<Rows>("{\"f\":" + json.text + "}");
            if (rows != null && rows.f != null && rows.f.Length >= Frames)
            {
                for (int i = 0; i < Frames; i++)
                {
                    w.outScale[i] = rows.f[i].outScale; w.inScale[i] = rows.f[i].inScale;
                    w.outAlpha[i] = rows.f[i].outAlpha; w.inAlpha[i] = rows.f[i].inAlpha;
                }
                w.FromFile = true;
            }
            Resources.UnloadAsset(json);
        }
        return w;
    }

    static readonly float[] DefOutScale = { 1f, .84f, .68f, .53f, .39f, .27f, .15f, .05f, .05f, .05f, .05f, .05f, .05f, .05f, .05f, .05f, .05f, .05f };
    static readonly float[] DefOutAlpha = { 1f, .84f, .68f, .52f, .37f, .23f, .1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
    static readonly float[] DefInScale = { .05f, .05f, .05f, .05f, .05f, .05f, .05f, .05f, .16f, .31f, .49f, .68f, .84f, .96f, 1.04f, 1.02f, 1f, 1f };
    static readonly float[] DefInAlpha = { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, .08f, .2f, .36f, .54f, .7f, .84f, .93f, .98f, 1f, 1f };

    void Defaults()
    {
        System.Array.Copy(DefOutScale, outScale, Frames); System.Array.Copy(DefOutAlpha, outAlpha, Frames);
        System.Array.Copy(DefInScale, inScale, Frames); System.Array.Copy(DefInAlpha, inAlpha, Frames);
    }

    // Warp time (seconds) -> a fractional 0-based frame index 0 .. Frames - 1.
    public static float IndexAt(float seconds) { return Mathf.Clamp01(seconds / Mathf.Max(.05f, Seconds)) * (Frames - 1); }
    public static float SecondsAt(float index) { return index / (Frames - 1) * Seconds; }

    static float Lerp(float[] a, float index)
    {
        int i = Mathf.Clamp((int)index, 0, Frames - 1), j = Mathf.Min(Frames - 1, i + 1);
        return Mathf.Lerp(a[i], a[j], index - i);
    }

    public float OutScale(float index) { return Lerp(outScale, index); }
    public float OutAlpha(float index) { return Lerp(outAlpha, index); }
    public float InScale(float index) { return Lerp(inScale, index); }
    public float InAlpha(float index) { return Lerp(inAlpha, index); }
}
