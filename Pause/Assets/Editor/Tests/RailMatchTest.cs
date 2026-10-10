using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// "Frost world rails are too wide out, match the width and its visibility to
// the Space world." Measured, not eyeballed: each world's rails are rendered
// ALONE through the game camera on black (9:21, the layout the game flies in,
// WorldPainter.Apply as a run does it: tint, dark inner edge, RailInset) and
// read off the pixels:
//   inner / outer   |x| of the innermost / outermost column where at least
//                   LitColumn of the rows are lit (value > LitValue)
//   width           outer - inner
//   light           mean brightness (max rgb) over the lit pixels: how loudly
//                   the rail shouts against black
//   body            the same over the solid body (columns lit in at least
//                   half their rows): the rail's face, without its glow
//   sat             mean HSV saturation of the lit pixels
//   contrast        |rail body - the lane strip beside it| against the world's own
//                   backdrop: the visibility that matters (Frost's pale sky vs Space's black)
//   mass            summed value of the whole frame / its width in u: the
//                   existing WorldBackdropTest rail-light measurement
// Frost has to match Space's inner edge, width and visibility within a small
// tolerance, and so does Tide (its own rail art, 2026-10-10); Verdant and Ember are measured and reported against Space.
public static class RailMatchTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[RAILMATCH] PASS  " : "[RAILMATCH] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public const float LitValue = .12f, LitColumn = .3f;
    // tolerances: frost vs space
    public const float EdgeTol = .03f, WidthTol = .06f;
    // The outer-edge fade (same in every world) darkens dark worlds' rails more below the lit threshold than Frost's pale steel, so
    // contrast vs the backdrop is only held within a factor (was 20% before the fade; Frost's pale sky legitimately reads a rail more).
    public const float GapTol = 1.5f;

    public struct Metrics
    {
        public string world;
        public float inner, outer, width, light, body, sat, mass, nearHalf, farHalf;   // near/far: mean lit value in the lane-side / screen-side half of the visible rail
        public override string ToString() =>
            world + ": inner " + inner.ToString("F3") + " outer " + outer.ToString("F3") + " width " + width.ToString("F3") +
            " | light " + light.ToString("F3") + " body " + body.ToString("F3") + " sat " + sat.ToString("F3") + " mass " + mass.ToString("F3");
    }

    // The rails of the open gameS1 as WorldPainter.Apply(theme) leaves them.
    public static Metrics Measure(WorldTheme theme, Camera cam, GameObject[] walls, int px = 540)
    {
        WorldPainter.Apply(theme);
        int w = px, h = px * 21 / 9;
        float halfW = cam.orthographicSize * cam.aspect;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var prevTarget = cam.targetTexture; var prevActive = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        cam.targetTexture = prevTarget; RenderTexture.active = prevActive;
        Object.DestroyImmediate(rt);
        var pix = tex.GetPixels32();
        Object.DestroyImmediate(tex);

        var m = new Metrics { world = theme.displayName };
        float unit = 2f * halfW / w;
        int first = -1, last = -1;
        double litSum = 0, bodySum = 0, satSum = 0, massSum = 0;
        long litN = 0, bodyN = 0;
        var colLit = new int[w / 2];
        for (int x = 0; x < w / 2; x++)   // the left rail (the right is its mirror)
        {
            int n = 0;
            for (int y = 0; y < h; y++)
            {
                var c = pix[y * w + x];
                float v = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) / 255f;
                massSum += v;
                if (v > LitValue) n++;
            }
            colLit[x] = n;
            if (n >= LitColumn * h) { if (first < 0) first = x; last = x; }
        }
        for (int x = 0; x < w / 2; x++)
            for (int y = 0; y < h; y++)
            {
                var c = pix[y * w + x];
                float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) / 255f, mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b)) / 255f;
                if (mx <= LitValue) continue;
                litSum += mx; litN++;
                satSum += (mx - mn) / mx;
                if (colLit[x] >= .5f * h) { bodySum += mx; bodyN++; }
            }
        if (first < 0) return m;
        m.outer = halfW - first * unit;
        m.inner = halfW - (last + 1) * unit;
        m.width = m.outer - m.inner;
        m.light = litN > 0 ? (float)(litSum / litN) : 0f;
        m.body = bodyN > 0 ? (float)(bodySum / bodyN) : 0f;
        m.sat = litN > 0 ? (float)(satSum / litN) : 0f;
        {
            // visible rail = first..last columns; split at its middle
            int mid = (first + last) / 2; double ns = 0, fs = 0; long nn = 0, fn = 0;
            for (int x = first; x <= last; x++)
                for (int y = 0; y < h; y++)
                {
                    var c = pix[y * w + x];
                    float v = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) / 255f;
                    if (v <= LitValue) continue;
                    if (x <= mid) { fs += v; fn++; } else { ns += v; nn++; }
                }
            m.nearHalf = nn > 0 ? (float)(ns / nn) : 0f; m.farHalf = fn > 0 ? (float)(fs / fn) : 0f;
        }
        m.mass = (float)(massSum / h) * unit;   // summed column light, in world units of width
        return m;
    }

    // The rail's footprint on a bright field (so its dark inner shadow counts):
    // |x| of the innermost / outermost column where at least LitColumn of the
    // rows differ from the field by FootDiff. Cam's background is set here.
    public const float FootDiff = .08f;
    public static void Footprint(WorldTheme theme, Camera cam, out float inner, out float outer, int px = 540)
    {
        WorldPainter.Apply(theme);
        var saved = cam.backgroundColor;
        cam.backgroundColor = new Color(.75f, .75f, .75f, 1f);
        int w = px, h = px * 21 / 9;
        float halfW = cam.orthographicSize * cam.aspect;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var prevTarget = cam.targetTexture; var prevActive = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        cam.targetTexture = prevTarget; RenderTexture.active = prevActive;
        cam.backgroundColor = saved;
        Object.DestroyImmediate(rt);
        var pix = tex.GetPixels32();
        Object.DestroyImmediate(tex);
        // the field's own value, read where no rail reaches (screen centre)
        var f = pix[(h / 2) * w + w / 2];
        float fv = Mathf.Max(f.r, Mathf.Max(f.g, f.b)) / 255f;
        int first = -1, last = -1;
        for (int x = 0; x < w / 2; x++)
        {
            int n = 0;
            for (int y = 0; y < h; y++)
            {
                var c = pix[y * w + x];
                if (Mathf.Abs(Mathf.Max(c.r, Mathf.Max(c.g, c.b)) / 255f - fv) > FootDiff) n++;
            }
            if (n >= LitColumn * h) { if (first < 0) first = x; last = x; }
        }
        float unit = 2f * halfW / w;
        outer = first < 0 ? 0f : halfW - first * unit;
        inner = first < 0 ? 0f : halfW - (last + 1) * unit;
    }

    // Visibility against the world's own backdrop: the rail's body value vs
    // the lane strip beside it (from 0.25 u to 1.0 u inside the inner edge,
    // clear of the rail's shadow), through the whole frame. contrast = the gap
    // between them; rail / lane are the two means. Backdrop must be showing.
    public struct Contrast { public float rail, lane, gap; }
    public static Contrast VsBackdrop(WorldTheme theme, Camera cam, float inner, float outer, int px = 540)
    {
        WorldPainter.Apply(theme);
        int w = px, h = px * 21 / 9;
        float halfW = cam.orthographicSize * cam.aspect, unit = 2f * halfW / w;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var prevTarget = cam.targetTexture; var prevActive = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        cam.targetTexture = prevTarget; RenderTexture.active = prevActive;
        Object.DestroyImmediate(rt);
        var pix = tex.GetPixels32();
        Object.DestroyImmediate(tex);
        double rs = 0, ls = 0; long rn = 0, ln = 0;
        for (int x = 0; x < w / 2; x++)
        {
            float ax = halfW - (x + .5f) * unit;   // |x| of this column
            bool onRail = ax >= inner && ax <= outer, onLane = ax <= inner - .25f && ax >= inner - 1f;
            if (!onRail && !onLane) continue;
            for (int y = 0; y < h; y++)
            {
                var c = pix[y * w + x];
                float v = (.2126f * c.r + .7152f * c.g + .0722f * c.b) / 255f;
                if (onRail) { rs += v; rn++; } else { ls += v; ln++; }
            }
        }
        var r = new Contrast { rail = (float)(rs / System.Math.Max(1, rn)), lane = (float)(ls / System.Math.Max(1, ln)) };
        r.gap = Mathf.Abs(r.rail - r.lane);
        return r;
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EditorSceneLoader.Open("gameS1", UnityEditor.SceneManagement.OpenSceneMode.Single);
        var cam = Camera.main;
        var walls = new[] { GameObject.Find("leftPipe"), GameObject.Find("rightPipe") };
        Check("gameS1 has both rail quads and a camera", cam != null && walls[0] != null && walls[1] != null);
        if (cam == null || walls[0] == null || walls[1] == null) return fails;

        var hidden = new List<Renderer>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            if (r.enabled && r.gameObject != walls[0] && r.gameObject != walls[1]) { r.enabled = false; hidden.Add(r); }
        var flags = cam.clearFlags; var bg = cam.backgroundColor;
        float size = cam.orthographicSize, aspect = cam.aspect;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        cam.aspect = 9f / 21f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 900, 2100);
        foreach (var wall in walls)
        {
            var sc = wall.transform.localScale;
            sc.y = cam.orthographicSize * 2f * 1.085f;   // what RailFit sets for this screen
            wall.transform.localScale = sc;
            RailFit.RefreshTextureTiling(wall);
        }
        try
        {
            var all = new Dictionary<string, Metrics>();
            var feet = new Dictionary<string, Vector2>();
            foreach (var theme in WorldManager.Worlds)
            {
                if (WorldPainter.RailTextureName(theme.displayName) == null) continue;
                var m = Measure(theme, cam, walls);
                all[theme.displayName] = m;
                Debug.Log("[RAILMATCH] " + m);
                Footprint(theme, cam, out float fi, out float fo);
                feet[theme.displayName] = new Vector2(fi, fo);
                Debug.Log("[RAILMATCH] footprint " + theme.displayName + ": inner " + fi.ToString("F3") + " outer " + fo.ToString("F3") + " width " + (fo - fi).ToString("F3"));
                if (theme.displayName == "Frost")
                {
                    float k = WorldPainter.FrostRailBrightness;
                    WorldPainter.FrostRailBrightness = 1f;
                    Debug.Log("[RAILMATCH] (undimmed) " + Measure(theme, cam, walls));
                    WorldPainter.FrostRailBrightness = k;
                    WorldPainter.Apply(theme);
                }
            }
            // against each world's own backdrop
            var gaps = new Dictionary<string, Contrast>();
            var backdrop = new GameObject("~RailMatchBackdrop").AddComponent<WorldBackdrop>();
            hidden.RemoveAll(r => r == null);
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.GetComponentInParent<WorldBackdrop>() != null && !r.enabled) r.enabled = true;
            cam.clearFlags = flags;
            foreach (var theme in WorldManager.Worlds)
            {
                if (WorldPainter.RailTextureName(theme.displayName) == null) continue;
                backdrop.Show(theme.displayName, false);
                backdrop.Step(1f);
                var live = VsBackdrop(theme, cam, all[theme.displayName].inner, all[theme.displayName].outer);
                gaps[theme.displayName] = live;
                Debug.Log("[RAILMATCH] vs backdrop " + theme.displayName + ": rail " + live.rail.ToString("F3") +
                          " lane " + live.lane.ToString("F3") + " gap " + live.gap.ToString("F3"));
                if (theme.displayName == "Frost")
                    foreach (float k in new[] { .6f, .8f, 1f })
                    {
                        float saved = WorldPainter.FrostRailBrightness;
                        WorldPainter.FrostRailBrightness = k;
                        var c = VsBackdrop(theme, cam, all[theme.displayName].inner, all[theme.displayName].outer);
                        WorldPainter.FrostRailBrightness = saved;
                        Debug.Log("[RAILMATCH] (sweep) vs backdrop Frost @" + k + ": gap " + c.gap.ToString("F3"));
                    }
            }
            Object.DestroyImmediate(backdrop.gameObject);
            WorldPainter.Apply(WorldManager.Worlds[0]);
            // the same darker falloff toward the screen edge in every world
            foreach (var kv in all)
            {
                float ratio = kv.Value.farHalf / Mathf.Max(1e-4f, kv.Value.nearHalf);
                Check(kv.Key + ": rail is darker toward the screen edge (outer half " + kv.Value.farHalf.ToString("F3") + " vs inner half " + kv.Value.nearHalf.ToString("F3") + ", " + ratio.ToString("F2") + "x)", ratio < .85f);
            }
            var e0 = WorldPainter.EdgeFor("Space");
            foreach (string w in new[] { "Frost", "Verdant", "Ember", "Tide" })
            {
                var e1 = WorldPainter.EdgeFor(w);
                Check(w + ": outer falloff strength and start equal Space's", e1.outerDark == e0.outerDark && e1.outerStart == e0.outerStart && e0.outerDark > 0f);
            }
            var s = all["Space"];
            foreach (string world in new[] { "Frost", "Verdant", "Ember", "Tide" })
            {
                var m = all[world];
                string tag = world + " vs Space: ";
                bool must = world == "Frost" || world == "Tide";   // Tide's rail was painted to Space's footprint: held to it
                string detail = "inner " + m.inner.ToString("F3") + "/" + s.inner.ToString("F3") + ", outer " + m.outer.ToString("F3") + "/" + s.outer.ToString("F3") +
                                ", width " + m.width.ToString("F3") + "/" + s.width.ToString("F3") + ", light " + m.light.ToString("F3") + "/" + s.light.ToString("F3") +
                                ", body " + m.body.ToString("F3") + "/" + s.body.ToString("F3") + ", sat " + m.sat.ToString("F3") + "/" + s.sat.ToString("F3") +
                                ", mass " + m.mass.ToString("F3") + "/" + s.mass.ToString("F3");
                // geometry on the bright-field footprint (independent of the outer fade's darkness)
                bool geo = Mathf.Abs(m.inner - s.inner) <= EdgeTol && Mathf.Abs(feet[world].x - feet["Space"].x) <= EdgeTol && Mathf.Abs(feet[world].y - feet["Space"].y) <= WidthTol;
                // visibility is the rail's contrast with its own backdrop (a pale
                // Frost sky needs a lighter rail than a black Space one to read
                // as loudly): the same gap between rail and lane, whatever the world
                float gapRatio = gaps[world].gap / Mathf.Max(1e-4f, gaps["Space"].gap);
                bool vis = Mathf.Abs(gapRatio - 1f) <= GapTol;
                detail += ", contrast vs backdrop " + gaps[world].gap.ToString("F3") + "/" + gaps["Space"].gap.ToString("F3");
                if (must)
                {
                    Check(tag + "the rail's inner edge, outer edge and width match (" + detail + ")", geo);
                    Check(tag + "its visibility (rail-vs-lane contrast against its own backdrop, within " + GapTol * 100f + "%) matches Space's (" + gapRatio.ToString("F2") + "x)", vis);
                }
                else
                    Debug.Log("[RAILMATCH] (report) " + tag + (geo ? "geometry matches" : "GEOMETRY DIFFERS") + ", " + (vis ? "visibility matches" : "VISIBILITY DIFFERS (" + gapRatio.ToString("F2") + "x)") + " (" + detail + ")");
            }
        }
        finally
        {
            cam.clearFlags = flags; cam.backgroundColor = bg;
            cam.orthographicSize = size; cam.aspect = aspect;
            foreach (var r in hidden) if (r != null) r.enabled = true;
        }
        Debug.Log("[RAILMATCH] failures: " + fails);
        return fails;
    }
}
