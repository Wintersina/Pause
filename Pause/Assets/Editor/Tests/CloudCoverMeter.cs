using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// How much of the view a planet backdrop's clouds cover, measured from the
// live pools exactly as they draw: every cloud piece's own art alpha (read
// from its atlas PNG), thickened by its material's _AlphaLift the way
// BackdropGrade.shader does, times its draw alpha, composited over a grid of
// the view (opacity = 1 - prod(1 - a)). A cell COUNTS AS COVERED once its
// cloud opacity reaches Veil -- a light veil you can see the ground through
// stays under it. Used by FrostBackdropTest / VerdantBackdropTest
// (CloudDensity, the ceiling's timeline) and CloudCoverPreview.
public static class CloudCoverMeter
{
    public const float Veil = .1f;
    public const int Cols = 30, Rows = 64;
    public const float LaneHalf = .5f;                 // the centre lane: |x| <= this (world units)
    public static readonly string[] CloudLayers = { "ceiling", "wisps", "mist" };
    public static readonly string[] CeilingOnly = { "ceiling" };

    public struct Reading
    {
        public float covered;      // share of the view's cells covered (opacity >= Veil)
        public float mean;         // mean cloud opacity over the view
    }

    class Art { public Color32[] px; public int w, h; public float sx, sy; }
    static readonly Dictionary<Texture2D, Art> arts = new Dictionary<Texture2D, Art>();

    static Art ArtOf(Texture2D tex)
    {
        if (tex == null) return null;
        if (arts.TryGetValue(tex, out var a)) return a;
        string path = AssetDatabase.GetAssetPath(tex);
        a = null;
        if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
        {
            var png = new Texture2D(2, 2);
            png.LoadImage(System.IO.File.ReadAllBytes(path));
            a = new Art { px = png.GetPixels32(), w = png.width, h = png.height,
                          sx = png.width / (float)tex.width, sy = png.height / (float)tex.height };
            Object.DestroyImmediate(png);
        }
        if (a == null) Debug.LogWarning("[CloudCoverMeter] no readable art for " + tex.name + " (" + path + "): measured as clear");
        arts[tex] = a;
        return a;
    }

    static readonly List<BackdropPiece> live = new List<BackdropPiece>();
    static readonly List<float> lifts = new List<float>();

    static void Gather(BackdropSet set, string[] layers)
    {
        live.Clear(); lifts.Clear();
        foreach (var pool in set.Director.Pools)
        {
            if (System.Array.IndexOf(layers, pool.name) < 0) continue;
            foreach (var p in pool.items)
            {
                if (!p.active || !p.sr.enabled || p.sr.sprite == null || p.sr.color.a <= 0f) continue;
                var m = p.sr.sharedMaterial;
                live.Add(p);
                lifts.Add(m != null && m.HasProperty("_AlphaLift") ? m.GetFloat("_AlphaLift") : 1f);
            }
        }
    }

    // Cloud opacity at view point (x, y) (relative to the set's root).
    static float Opacity(BackdropSet set, float x, float y)
    {
        Vector3 world = set.Root.position + new Vector3(x, y, 0f);
        float clear = 1f;
        for (int i = 0; i < live.Count; i++)
        {
            var p = live[i];
            Bounds b = p.sr.bounds;
            if (world.x < b.min.x || world.x > b.max.x || world.y < b.min.y || world.y > b.max.y) continue;
            var s = p.sr.sprite;
            var art = ArtOf(s.texture);
            if (art == null) continue;
            Vector3 l = p.sr.transform.InverseTransformPoint(world);
            float u = s.rect.x + s.pivot.x + l.x * s.pixelsPerUnit;
            float v = s.rect.y + s.pivot.y + l.y * s.pixelsPerUnit;
            if (u < s.rect.xMin || u >= s.rect.xMax || v < s.rect.yMin || v >= s.rect.yMax) continue;
            int px = Mathf.Clamp((int)(u * art.sx), 0, art.w - 1), py = Mathf.Clamp((int)(v * art.sy), 0, art.h - 1);
            float a = art.px[py * art.w + px].a / 255f;
            a = 1f - Mathf.Pow(Mathf.Clamp01(1f - a), lifts[i]);
            clear *= 1f - Mathf.Clamp01(a * p.sr.color.a);
        }
        return 1f - clear;
    }

    public static Reading Measure(BackdropSet set, string[] layers = null)
    {
        Gather(set, layers ?? CloudLayers);
        float hw = set.HalfWidth, hh = set.HalfHeight;
        int covered = 0; float sum = 0f;
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                float o = Opacity(set, -hw + (c + .5f) * 2f * hw / Cols, -hh + (r + .5f) * 2f * hh / Rows);
                sum += o;
                if (o >= Veil) covered++;
            }
        int n = Rows * Cols;
        return new Reading { covered = covered / (float)n, mean = sum / n };
    }

    // The centre lane's cells (3 columns x Rows) covered right now, into `into`.
    public const int LaneCols = 3;
    public static void Lane(BackdropSet set, bool[] into, string[] layers = null)
    {
        Gather(set, layers ?? CloudLayers);
        float hh = set.HalfHeight;
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < LaneCols; c++)
                into[r * LaneCols + c] = Opacity(set, -LaneHalf + c * LaneHalf, -hh + (r + .5f) * 2f * hh / Rows) >= Veil;
    }

    // Re-seeds a live director's random stream (its spawns from here on).
    public static void Reseed(BackdropDirector d, int seed)
    {
        typeof(BackdropDirector).GetField("rng", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .SetValue(d, new System.Random(seed));
    }

    public struct Steady
    {
        public float covered;      // mean covered share over the samples
        public float worst;        // the worst single sample
        public float laneRun;      // longest time any centre-lane cell stayed covered (s), after `from`
        public string samples;
    }

    public static readonly float[] SampleAt = { 12f, 20f, 40f, 70f };

    // Steps `wb` (a fresh landing: its clock counted from 0 here) to the last sample time,
    // measuring the view at SampleAt and the centre lane every frame from
    // the first sample on.
    public static Steady Run(WorldBackdrop wb, float dt)
    {
        var st = new Steady { samples = "" };
        var lane = new bool[Rows * LaneCols];
        var run = new float[Rows * LaneCols];
        int next = 0;
        float sum = 0f, t = 0f;
        while (next < SampleAt.Length)
        {
            wb.Step(dt);
            t += dt;
            if (t >= SampleAt[0])
            {
                Lane(wb.Current, lane);
                for (int i = 0; i < lane.Length; i++)
                {
                    run[i] = lane[i] ? run[i] + dt : 0f;
                    st.laneRun = Mathf.Max(st.laneRun, run[i]);
                }
            }
            if (t >= SampleAt[next])
            {
                var r = Measure(wb.Current);
                sum += r.covered;
                st.worst = Mathf.Max(st.worst, r.covered);
                st.samples += (st.samples.Length > 0 ? " " : "") + r.covered.ToString("F2");
                next++;
            }
        }
        st.covered = sum / SampleAt.Length;
        return st;
    }
}
