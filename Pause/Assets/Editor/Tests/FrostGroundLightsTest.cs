using UnityEditor.SceneManagement;
using UnityEngine;

// Frost's ground lights (FrostGroundLights): the lamps the mid tiles paint glow, flicker, pulse and blink.
//   * every variant has its lamps (FrostGroundLightsTuning.Points, frost_redraw/ground_lights.py), all of known kinds,
//     inside the tile;
//   * PINNED: every light drawn sits on its own spot of the mid tile (GroundPlanner.UV of where it is drawn = its point),
//     through scrolling, every frame, on every copy of the tile;
//   * small: drawn 3..16 px wide on a 1080 px phone; under its alpha cap; never pink (WorldCue.IsPink) -- pink is the
//     hostile cue outside Space;
//   * photosensitivity: no light swings by more than .3 of its range faster than MaxHz; a strobe flashes at most
//     every StrobeEveryMin s;
//   * a paused game (dt 0) is a still frame; the pool holds what a phone sees; no allocation per frame.
public static class FrostGroundLightsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[FGL] PASS  " : "[FGL] FAIL  ") + what);
        if (!ok) fails++;
    }
    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        float savedSpeed = moveBackGround.speed;
        try
        {
            Table();
            for (int v = 1; v <= 4; v++) Live(v);
            Rates();
        }
        finally
        {
            moveBackGround.speed = savedSpeed;
            Time.timeScale = 1f;
            FrostBackdropSelection.Reset();
            EliteSystem.Clear();
            if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
        }
        Debug.Log("[FGL] failures: " + fails);
        return fails;
    }

    static WorldBackdrop Fresh(int variant)
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        Time.timeScale = 1f;
        moveBackGround.speed = .6f;
        FrostBackdropSelection.Reset();
        FrostBackdropSelection.Seed(11);
        FrostBackdropSelection.Force = variant;
        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = 1080f / 2400f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 1080, 2400);
        var wb = WorldBackdrop.Create("Frost");
        wb.Show("Frost", false);
        return wb;
    }

    static void Table()
    {
        var pts = FrostGroundLightsTuning.Points;
        Check("a lamp table for every variant (" + pts.Length + " entries, index 0 unused)", pts.Length == 5);
        for (int v = 1; v < pts.Length; v++)
        {
            int n = FrostGroundLights.Count(v);
            bool ok = true;
            foreach (var it in pts[v].Split(';'))
            {
                var f = it.Split(',');
                ok &= f.Length == 3 && FrostGroundLightsTuning.Kinds.IndexOf(f[2]) >= 0;
                if (f.Length == 3)
                {
                    float x = float.Parse(f[0], System.Globalization.CultureInfo.InvariantCulture);
                    float y = float.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture);
                    ok &= x >= 0f && x < 896f && y >= 0f && y < 1792f;
                }
            }
            Check("v" + v + ": " + n + " painted lamps (>= 10), each of a known kind, inside the 896 x 1792 tile", n >= 10 && ok);
        }
        bool pink = false;
        foreach (char k in FrostGroundLightsTuning.Kinds) pink |= WorldCue.IsPink(FrostGroundLightsTuning.ColorOf(k));
        Check("no light is drawn in the hostile pink", !pink);
        Check("photosensitivity: flicker / pulse rates <= MaxHz " + FrostGroundLightsTuning.MaxHz + ", strobes >= 2 s apart",
              FrostGroundLightsTuning.FireHz <= FrostGroundLightsTuning.MaxHz && FrostGroundLightsTuning.WindowHz <= FrostGroundLightsTuning.MaxHz &&
              FrostGroundLightsTuning.BeaconHzMax <= FrostGroundLightsTuning.MaxHz && 1f / FrostGroundLightsTuning.RimPeriod <= FrostGroundLightsTuning.MaxHz &&
              FrostGroundLightsTuning.StrobeEveryMin >= 2f);
    }

    static void Live(int v)
    {
        var wb = Fresh(v);
        var d = wb.Current != null ? wb.Current.Director as FrostDirector : null;
        if (d == null || d.GroundLights == null || d.Planner == null) { Check("v" + v + ": Frost director with its ground lights", false); return; }
        var gl = d.GroundLights;
        var planner = d.Planner;
        float pxPerUnit = 1080f / (2f * wb.Current.HalfWidth);
        int peak = 0, offSpot = 0, frames = 0, overflow = 0;
        float minPx = float.MaxValue, maxPx = 0f, maxA = 0f;
        bool pink = false;
        for (float t = 0f; t < 20f; t += Dt)
        {
            wb.Step(Dt);
            frames++;
            peak = Mathf.Max(peak, gl.Live);
            overflow = Mathf.Max(overflow, gl.Overflow);
            for (int i = 0; i < gl.Capacity; i++)
            {
                Vector2 uv; Vector3 local; Color c; float w; char k;
                if (!gl.Probe(i, out uv, out local, out c, out w, out k)) continue;
                // where it is drawn, read back as a spot of the tile, is its own point (both wrap)
                var at = planner.UV(local.x, planner.GroundY(local.y));
                float du = Mathf.Abs(at.x - uv.x), dv = Mathf.Abs(at.y - uv.y);
                du = Mathf.Min(du, 1f - du); dv = Mathf.Min(dv, 1f - dv);
                if (du * planner.Tile.TileWidth > .002f || dv * planner.Tile.TileHeight > .002f) offSpot++;
                if (c.a > 0f)
                {
                    minPx = Mathf.Min(minPx, w * pxPerUnit);
                    maxPx = Mathf.Max(maxPx, w * pxPerUnit);
                    maxA = Mathf.Max(maxA, c.a);
                    pink |= WorldCue.IsPink(c);
                }
            }
        }
        Check("v" + v + ": the painted lamps light up (peak " + peak + " in view, " + gl.PointCount + " a tile)", peak >= gl.PointCount * 2);
        Check("v" + v + ": every light stays pinned on its lamp of the mid tile (" + offSpot + " off over " + frames + " frames)", offSpot == 0);
        Check("v" + v + ": small, never a blob or a speck (" + minPx.ToString("F1") + " .. " + maxPx.ToString("F1") + " px on a 1080 px phone, 3 .. 16)",
              minPx >= 3f && maxPx <= 16f);
        Check("v" + v + ": alpha capped (" + maxA.ToString("F2") + " <= .9), no pink", maxA <= .9f && !pink);
        Check("v" + v + ": the pool holds a phone's view (" + overflow + " lights without a renderer)", overflow == 0);

        // a paused game is a still frame
        float clock = gl.Clock;
        var before = new Color[gl.Capacity];
        for (int i = 0; i < gl.Capacity; i++) { Vector2 uv; Vector3 l; float w; char k; gl.Probe(i, out uv, out l, out before[i], out w, out k); }
        wb.Step(0f);
        bool still = Mathf.Approximately(clock, gl.Clock);
        for (int i = 0; i < gl.Capacity && still; i++) { Vector2 uv; Vector3 l; Color c; float w; char k; gl.Probe(i, out uv, out l, out c, out w, out k); still &= c == before[i]; }
        Check("v" + v + ": dt 0 is a still frame (no light changes)", still);

        long bytes = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 60; i++) gl.Step(Dt); });
        Check("v" + v + ": no allocation per frame (" + bytes + " B over 60 steps)", bytes == 0);
        Object.DestroyImmediate(wb.gameObject);
    }

    // Every kind, many seeds: count the big swings (rising through the middle of a >= .3 range) per second.
    static void Rates()
    {
        float worst = 0f, strobeGap = float.MaxValue;
        for (int kind = 0; kind < FrostGroundLightsTuning.Kinds.Length; kind++)
            for (uint seed = 1; seed < 40; seed++)
            {
                int rises = 0;
                float lastFlash = -10f;
                bool high = false;
                const float T = 20f;
                for (float t = 0f; t < T; t += 1f / 120f)
                {
                    float l = FrostGroundLights.Level(kind, seed * 2654435761u, t, .3f, .6f);
                    if (!high && l > .65f) { high = true; rises++; if (FrostGroundLightsTuning.Kinds[kind] == 's') { if (lastFlash >= 0f) strobeGap = Mathf.Min(strobeGap, t - lastFlash); lastFlash = t; } }
                    else if (high && l < .35f) high = false;
                }
                worst = Mathf.Max(worst, rises / T);
            }
        Check("no light swings faster than " + FrostGroundLightsTuning.MaxHz + " Hz (worst " + worst.ToString("F2") + " Hz)", worst <= FrostGroundLightsTuning.MaxHz);
        Check("strobes flash >= " + FrostGroundLightsTuning.StrobeEveryMin + " s apart (closest " + strobeGap.ToString("F2") + " s)",
              strobeGap >= FrostGroundLightsTuning.StrobeEveryMin - .02f);
    }
}
