using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Frost's living props (FrostProps.cs: whales, orcas, an icebreaker, a research station, an abandoned lab, a crashed plane):
//   * LOAD: the props atlas resolves with all six 8-cell strips (no seals), the director builds its pool and lights;
//   * PLACED where they belong: the whales, orcas and icebreaker only on open water (mask W, their whole footprint and a
//     clear rim), the structures only on a flat top (mask O) like the landmarks; nothing that floats on the glacier (v4);
//     never overlapping a landmark, a dock or another prop; pinned to their spot of the ground;
//   * RARE: at most two in view at once, and on average well under that;
//   * ANIMATED: the structures and the icebreaker loop through their cells, the whales / orcas wait as a shadow WaitMin..
//     WaitMax s, then surface, blow and dive; their lamps glow / blink in step with the drawing; dt 0 is a still frame;
//   * COLOUR: no pink anywhere (WorldCue.IsPink: the hostile cue outside Space) -- the art as DRAWN (lifted by the Frost
//     grade) and the lights; brightness inside the landmark rules (as drawn: mean luminance under the set-piece ceiling,
//     close to the landmarks' own);
//   * no allocation per frame.
public static class FrostPropsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[FPR] PASS  " : "[FPR] FAIL  ") + what);
        if (!ok) fails++;
    }
    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 20f;
    const string Folder = "Worlds/Frost/Backdrop3/";
    const string ArtDir = "Assets/Art/Backgrounds/Resources/Worlds/Frost/Backdrop3/";

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        float savedSpeed = moveBackGround.speed;
        try
        {
            Table();
            Art();
            for (int v = 1; v <= 4; v++) Live(v);
            Forced();
        }
        finally
        {
            FrostDirector.SeedOverride = 0;
            moveBackGround.speed = savedSpeed;
            Time.timeScale = 1f;
            FrostBackdropSelection.Reset();
            EliteSystem.Clear();
            if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
        }
        Debug.Log("[FPR] failures: " + fails);
        return fails;
    }

    static WorldBackdrop Fresh(int variant, float speed)
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        Time.timeScale = 1f;
        moveBackGround.speed = speed;
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

    static FrostDirector Director(WorldBackdrop wb) { return wb.Current != null ? wb.Current.Director as FrostDirector : null; }

    // ---- the table ---------------------------------------------------------------------------

    static void Table()
    {
        var names = FrostPropsTuning.Names;
        Check("six props, the approved picks (no seals): " + string.Join(", ", names),
              names.Length == 6 && System.Array.IndexOf(names, "seals") < 0 && System.Array.IndexOf(names, "whale") >= 0 &&
              System.Array.IndexOf(names, "orca") >= 0 && System.Array.IndexOf(names, "station") >= 0 && System.Array.IndexOf(names, "lab") >= 0 &&
              System.Array.IndexOf(names, "icebreaker") >= 0 && System.Array.IndexOf(names, "plane") >= 0);
        bool shapes = FrostPropsTuning.FrameMs.Length == names.Length && FrostPropsTuning.Water.Length == names.Length &&
                      FrostPropsTuning.OneShot.Length == names.Length && FrostPropsTuning.SizeK.Length == names.Length &&
                      FrostPropsTuning.Feet.Length == names.Length && FrostPropsTuning.Weights.Length == names.Length;
        foreach (var ms in FrostPropsTuning.FrameMs) { shapes &= ms.Length == FrostPropsTuning.Frames; foreach (int m in ms) shapes &= m >= 80; }
        Check("every prop has 8 frame timings (>= 80 ms), a size, a foot and a weight", shapes);
        bool classes = true;
        for (int k = 0; k < names.Length; k++)
        {
            var r = FrostPropsTuning.Rule(k);
            bool water = names[k] == "whale" || names[k] == "orca" || names[k] == "icebreaker";
            classes &= FrostPropsTuning.Water[k] == water;
            classes &= r.on == (water ? GroundClass.Water : GroundClass.Open) && r.need >= .999f && r.rim > 1f && r.rimNeed >= .9f;
            classes &= FrostPropsTuning.Fits(k, 4) == !water && FrostPropsTuning.Fits(k, 1);
            classes &= FrostPropsTuning.OneShot[k] == (names[k] == "whale" || names[k] == "orca");
        }
        Check("whales, orcas and the icebreaker on open water only (W, whole footprint + rim), the structures on a flat top only (O); nothing floats on the glacier", classes);
        float min = float.MaxValue, max = 0f;
        for (int k = 0; k < names.Length; k++) { min = Mathf.Min(min, FrostPropsTuning.Size(k)); max = Mathf.Max(max, FrostPropsTuning.Size(k) * (1f + FrostPropsTuning.SizeJitter)); }
        Check("drawn at the landmarks' scale (" + min.ToString("F2") + " .. " + max.ToString("F2") + " u, landmarks " + FrostTuning.LandmarkMin.ToString("F2") +
              " .. " + FrostTuning.LandmarkMax.ToString("F2") + ", scaled by the ground zoom)",
              min >= .7f * FrostTuning.LandmarkMin && max <= FrostTuning.LandmarkMax && Mathf.Approximately(FrostPropsTuning.Size(0), FrostPropsTuning.SizeK[0] * FrostTuning.PieceScale));
        Check("rare: a pool of at most two (" + FrostPropsTuning.Pool + "), " + FrostPropsTuning.GapMin + " .. " + FrostPropsTuning.GapMax + " u of ground between two",
              FrostPropsTuning.Pool <= 2 && FrostPropsTuning.GapMin >= 6f);
        Check("a whale waits 3 .. 8 s between surfacings (" + FrostPropsTuning.WaitMin + " .. " + FrostPropsTuning.WaitMax + ")",
              Mathf.Approximately(FrostPropsTuning.WaitMin, 3f) && Mathf.Approximately(FrostPropsTuning.WaitMax, 8f));

        bool pink = WorldCue.IsPink(FrostPropsTuning.Red), lit = true, known = true;
        var per = new int[names.Length];
        foreach (var l in FrostPropsTuning.Lights)
        {
            pink |= WorldCue.IsPink(FrostPropsTuning.ColorOf(l.kind));
            known &= "xws".IndexOf(l.kind) >= 0 && l.prop >= 0 && l.prop < names.Length && !FrostPropsTuning.OneShot[l.prop];
            known &= l.at.x >= 0f && l.at.x < 128f && l.at.y >= 0f && l.at.y < 128f;
            per[l.prop]++;
            if (l.kind == 'x')
            {
                // photosensitivity: the beacon's rises per loop at the loop's own pace
                int rises = 0;
                for (int f = 0; f < 8; f++) if (((l.litMask >> f) & 1) != 0 && ((l.litMask >> ((f + 7) % 8)) & 1) == 0) rises++;
                lit &= l.litMask != 0 && rises / FrostPropsTuning.ShowSeconds(l.prop) <= FrostGroundLightsTuning.MaxHz;
            }
        }
        bool fit = true;
        foreach (int n in per) fit &= n <= FrostPropsTuning.MaxLights;
        Check("lamps: the station's red mast beacon and amber windows, the lab's mast tip, the plane's red tail beacon, the icebreaker's deck lights",
              per[FrostPropsTuning.Station] >= 2 && per[FrostPropsTuning.Lab] >= 1 && per[FrostPropsTuning.Plane] >= 1 && per[FrostPropsTuning.Icebreaker] >= 2 && fit && known);
        Check("no lamp is pink, beacons blink no faster than " + FrostGroundLightsTuning.MaxHz + " Hz, peak alpha <= .9", !pink && lit && FrostPropsTuning.Peak <= .9f);
    }

    // ---- the art -----------------------------------------------------------------------------

    static Color[] Png(string file, out int w, out int h)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        w = h = 0;
        if (!File.Exists(file) || !tex.LoadImage(File.ReadAllBytes(file))) { Object.DestroyImmediate(tex); return null; }
        w = tex.width; h = tex.height;
        var px = tex.GetPixels();
        Object.DestroyImmediate(tex);
        return px;
    }

    static float Lum(Color c) { return .2126f * c.r + .7152f * c.g + .0722f * c.b; }

    static float MeanLum(Color[] px)
    {
        double w = 0, l = 0;
        foreach (var c in px) { if (c.a <= .01f) continue; w += c.a; l += c.a * Lum(c); }
        return w > 0 ? (float)(l / w) : 0f;
    }

    static void Art()
    {
        var atlas = new BackdropAtlas(Resources.Load<Texture2D>(Folder + FrostPropsTuning.Atlas), Resources.Load<TextAsset>(Folder + FrostPropsTuning.Atlas));
        bool all = atlas.Count == FrostPropsTuning.Names.Length * FrostPropsTuning.Frames;
        foreach (string n in FrostPropsTuning.Names)
        {
            var f = atlas.Frames(n);
            all &= f.Length == FrostPropsTuning.Frames;
            foreach (var s in f) all &= s != null && Mathf.Approximately(s.rect.width, 128f) && Mathf.Approximately(s.rect.height, 128f);
        }
        Check("the props atlas loads: six strips of 8 x 128 px cells (" + atlas.Count + " sprites)", all);
        atlas.Destroy();

        var spec = BackdropCatalog.For("Frost");
        var layer = spec.Find(FrostPropsTuning.Layer);
        Check("the props layer is pinned to the mid tile, a ground (landmark) layer graded like the landmarks",
              layer.pinTo == "mid" && layer.rate == spec.Rate("mid") && layer.role == BackdropCatalog.Role.Landmark && layer.grade == spec.Find("landmarks").grade);
        float lift = BackdropGrade.Lift(spec, layer), sat = BackdropGrade.Saturation(spec, lift);
        int w, h, lw, lh;
        var px = Png(ArtDir + "props.png", out w, out h);
        var lpx = Png(ArtDir + "landmarks.png", out lw, out lh);
        if (px == null || lpx == null) { Check("props.png and landmarks.png read", false); return; }
        var drawn = BackdropGrade.Apply(px, lift, sat);
        var ldrawn = BackdropGrade.Apply(lpx, BackdropGrade.Lift(spec, spec.Find("landmarks")), sat);
        int pinkPainted = 0, pinkDrawn = 0;
        for (int i = 0; i < px.Length; i++)
        {
            if (px[i].a <= 0f) continue;
            if (WorldCue.IsPink(px[i])) pinkPainted++;
            if (WorldCue.IsPink(drawn[i])) pinkDrawn++;
        }
        Check("no pink in the props as painted (" + pinkPainted + " px) or as drawn, lifted x" + lift.ToString("F2") + " (" + pinkDrawn + " px)", pinkPainted == 0 && pinkDrawn == 0);
        float mean = MeanLum(drawn), lmean = MeanLum(ldrawn);
        Check("as drawn, the props sit near the landmarks' brightness (mean luminance " + mean.ToString("F3") + " <= 1.25 x the landmarks' " + lmean.ToString("F3") +
              ", under the set-piece ceiling .66)", mean <= lmean * 1.25f && mean <= .66f);
        // per prop (a row of the sheet; texture rows run bottom-up): under the ceiling, its forms (value p90 of the opaque
        // texels, the lamps a few px) a step above the landmarks' at most
        var lv = new List<float>();
        foreach (var c in ldrawn) if (c.a > .5f) lv.Add(Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
        lv.Sort();
        float lp90 = lv.Count > 0 ? lv[(int)(lv.Count * .9f)] : 0f;
        string rows = "";
        bool rowsOk = true;
        for (int k = 0; k < FrostPropsTuning.Names.Length; k++)
        {
            int y0 = h - (k + 1) * 128;
            var vals = new List<float>();
            double sw = 0, sl = 0;
            for (int y = y0; y < y0 + 128; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = drawn[y * w + x];
                    if (c.a > .5f) vals.Add(Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
                    if (c.a > .01f) { sw += c.a; sl += c.a * Lum(c); }
                }
            vals.Sort();
            float p90 = vals.Count > 0 ? vals[(int)(vals.Count * .9f)] : 1f;
            float ml = sw > 0 ? (float)(sl / sw) : 1f;
            rows += FrostPropsTuning.Names[k] + " p90 " + p90.ToString("F2") + " lum " + ml.ToString("F2") + "; ";
            rowsOk &= p90 <= lp90 + .15f && ml <= .66f;
        }
        Check("every prop's forms within the landmark rules as drawn (value p90 <= landmarks' " + lp90.ToString("F2") + " + .15, luminance <= .66): " + rows, rowsOk);
    }

    // ---- live: placement, rarity, animation, lights --------------------------------------------

    struct Box { public float x, rx, ry; public double gy; }

    static bool InView(BackdropPiece p, float halfW, float halfH)
    {
        return p.y + p.size * .5f > -halfH && p.y - p.size * .5f < halfH && Mathf.Abs(p.x) - p.size * .5f < halfW;
    }

    static void Live(int v)
    {
        const int Runs = 3;
        const float Seconds = 150f;
        int dealt = 0, wrong = 0, overlaps = 0, drift = 0, floaters = 0, maxInView = 0, frames = 0, inViewSum = 0, runs = 0;
        string bad = "";
        var kinds = new HashSet<string>();
        for (int run = 0; run < Runs; run++)
        {
            FrostDirector.SeedOverride = 301 + run * 13;
            var wb = Fresh(v, 2f);
            var d = Director(wb);
            if (d == null || d.Props == null || d.Planner == null || d.Planner.Mask == null) { Check("v" + v + ": Frost director with its props pool and a ground mask", false); continue; }
            runs++;
            var set = wb.Current;
            var seen = new Dictionary<BackdropPiece, double>();
            for (float t = 0f; t < Seconds; t += Dt)
            {
                if (t > 0f) wb.Step(Dt);
                frames++;
                int inView = 0;
                for (int i = 0; i < d.Props.items.Count; i++)
                {
                    var p = d.Props.items[i];
                    if (!p.active) continue;
                    if (InView(p, set.HalfWidth, set.HalfHeight)) inView++;
                    if (Mathf.Abs((float)(p.y + d.Planner.Travel - p.gy)) > 1e-3f) drift++;
                    double was;
                    if (seen.TryGetValue(p, out was) && System.Math.Abs(was - p.gy) < 1e-6) continue;
                    seen[p] = p.gy;
                    dealt++;
                    int k = d.PropKind(i);
                    if (k < 0) { wrong++; continue; }
                    kinds.Add(FrostPropsTuning.Names[k]);
                    var rule = FrostPropsTuning.Rule(k);
                    if (!d.Planner.Accepts(rule, p.x, p.gy, p.size)) { wrong++; if (bad.Length < 200) bad += FrostPropsTuning.Names[k] + "@" + d.Planner.ClassAt(p.x, p.gy) + " "; }
                    if (FrostPropsTuning.Water[k] && FrostTuning.IsGlacier(v)) floaters++;
                    overlaps += Overlaps(d, p);
                }
                maxInView = Mathf.Max(maxInView, inView);
                inViewSum += inView;
            }
            Object.DestroyImmediate(wb.gameObject);
        }
        FrostDirector.SeedOverride = 0;
        float mean = frames > 0 ? inViewSum / (float)frames : 0f;
        Check("v" + v + ": " + runs + " runs dealt " + dealt + " props (" + string.Join(", ", kinds) + "), none on ground its rule forbids (" + wrong + " " + bad + ")",
              runs == Runs && dealt >= Runs * 2 && wrong == 0);
        Check("v" + v + ": no prop overlaps a landmark, a dock or another prop (" + overlaps + ")", overlaps == 0);
        Check("v" + v + ": every prop stays pinned to its spot of the ground (" + drift + " frames drifted)", drift == 0);
        Check("v" + v + ": rare -- at most 2 in view at once (max " + maxInView + "), " + mean.ToString("F2") + " on average (<= 1.6)", maxInView <= 2 && mean <= 1.6f);
        if (FrostTuning.IsGlacier(v)) Check("v" + v + " (glacier): no whale, orca or icebreaker over the crevasses (" + floaters + ")", floaters == 0);
    }

    // Prop `p`'s claimed drawing against every other ground piece's footprint (landmarks, docks: their planner core; props: theirs).
    static int Overlaps(FrostDirector d, BackdropPiece p)
    {
        int n = 0;
        float px = p.size * FrostPropsTuning.ClaimCore, py = px * .8f;
        var pools = new List<BackdropPool>(d.Landmarks) { d.Props };
        foreach (var pool in pools)
            foreach (var q in pool.items)
            {
                if (!q.active || q == p || q.sr.sprite == null) continue;
                float qx = GroundPlanner.HalfX(new PieceRule(GroundClass.Any, 0f, q.core), q.size), qy = qx * .8f;
                if (Mathf.Abs(p.x - q.x) < (px + qx) * .98f && System.Math.Abs(p.gy - q.gy) < (py + qy) * .98f) n++;
            }
        return n;
    }

    // Each kind forced into view: it animates (a loop through its cells; a whale / orca's wait then surfacing), its lamps
    // light up (never pink, alpha-capped, a readable size), dt 0 freezes it all, no allocation per frame.
    static void Forced()
    {
        for (int k = 0; k < FrostPropsTuning.Names.Length; k++)
        {
            string name = FrostPropsTuning.Names[k];
            // the water props where the open water is widest (v2), the structures on the ice plates first (v1)
            int[] order = FrostPropsTuning.Water[k] ? new[] { 2, 1, 3 } : new[] { 1, 3, 2, 4 };
            WorldBackdrop wb = null;
            FrostDirector d = null;
            int slot = -1, v = 0;
            foreach (int vv in order)
            {
                FrostDirector.SeedOverride = 77 + k;
                wb = Fresh(v = vv, .25f);
                d = Director(wb);
                if (d == null || d.Props == null) break;
                d.ClearProps();
                double tr = d.Planner.Travel;
                float hh = wb.Current.HalfHeight;
                for (int a = 0; a < 6 && slot < 0; a++)
                    if (d.SpawnProp(k, tr - hh * .5f, tr + hh * .6f, 800))
                        for (int i = 0; i < d.Props.items.Count; i++) if (d.Props.items[i].active && d.PropKind(i) == k) slot = i;
                if (slot >= 0) break;
                Object.DestroyImmediate(wb.gameObject);
                wb = null;
            }
            Check(name + ": placed in view (v" + v + ", on " + (slot >= 0 ? d.Planner.ClassAt(d.Props.items[slot].x, d.Props.items[slot].gy).ToString() : "-") + ")", slot >= 0);
            if (slot < 0) { if (wb != null) Object.DestroyImmediate(wb.gameObject); continue; }
            var set = wb.Current;
            var p = d.Props.items[slot];
            var lights = d.PropLightRenderers(slot);
            float pxPerUnit = 1080f / (2f * set.HalfWidth);
            var framesSeen = new HashSet<int>();
            float firstUp = -1f, maxA = 0f, minPx = float.MaxValue, maxPx = 0f;
            bool pink = false, back = false, wrongSheet = false;
            int lastFrame = 0;
            float span = FrostPropsTuning.OneShot[k] ? FrostPropsTuning.WaitMax + FrostPropsTuning.ShowSeconds(k) + 1.5f : 3f;
            for (float t = 0f; t < span; t += 1f / 60f)
            {
                wb.Step(1f / 60f);
                if (!p.active) break;
                int f = FrostPropsTuning.FrameOf(p.sr.sprite != null ? p.sr.sprite.name : null);
                if (FrostPropsTuning.KindOf(p.sr.sprite != null ? p.sr.sprite.name : null) != k) wrongSheet = true;
                framesSeen.Add(f);
                if (f > 0 && firstUp < 0f) firstUp = p.age;
                if (f == 0 && lastFrame > 0) back = true;
                lastFrame = f;
                foreach (var sr in lights)
                {
                    if (!sr.enabled) continue;
                    maxA = Mathf.Max(maxA, sr.color.a);
                    pink |= WorldCue.IsPink(sr.color);
                    float w = Mathf.Abs(sr.transform.lossyScale.x) * pxPerUnit;
                    minPx = Mathf.Min(minPx, w);
                    maxPx = Mathf.Max(maxPx, w);
                }
            }
            Check(name + ": shows only its own cells", !wrongSheet);
            if (FrostPropsTuning.OneShot[k])
                Check(name + ": waits as a shadow " + FrostPropsTuning.WaitMin + " .. " + FrostPropsTuning.WaitMax + " s (surfaced at " + firstUp.ToString("F2") +
                      " s), surfaces through every cell (" + framesSeen.Count + "/8) and goes back under",
                      firstUp >= FrostPropsTuning.WaitMin - .05f && firstUp <= FrostPropsTuning.WaitMax + .05f && framesSeen.Count == 8 && back);
            else
                Check(name + ": loops through its cells (" + framesSeen.Count + "/8 in 3 s)", framesSeen.Count == 8);
            if (!FrostPropsTuning.OneShot[k])
                Check(name + ": its lamps light up (peak alpha " + maxA.ToString("F2") + ", " + minPx.ToString("F1") + " .. " + maxPx.ToString("F1") +
                      " px on a 1080 px phone), never pink, alpha <= .9", maxA >= .5f && maxA <= .9f + 1e-4f && !pink && minPx >= 3f && maxPx <= 16f);

            // dt 0 is a still frame
            var sprite = p.sr.sprite;
            var cols = new Color[lights.Length];
            var on = new bool[lights.Length];
            for (int j = 0; j < lights.Length; j++) { cols[j] = lights[j].color; on[j] = lights[j].enabled; }
            float age = p.age, y = p.y;
            for (int i = 0; i < 30; i++) wb.Step(0f);
            bool still = p.sr.sprite == sprite && Mathf.Approximately(p.age, age) && Mathf.Approximately(p.y, y);
            for (int j = 0; j < lights.Length; j++) still &= lights[j].color == cols[j] && lights[j].enabled == on[j];
            Check(name + ": dt 0 (paused) is a still frame -- cell, lamps and spot", still);

            if (k == 0)
            {
                long bytes = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 60; i++) wb.Step(1f / 60f); });
                Check("no allocation per frame with props live (" + bytes + " B over 60 steps, <= 256 like FrostBackdropTest's meter)", bytes <= 256);
            }
            Object.DestroyImmediate(wb.gameObject);
        }
        FrostDirector.SeedOverride = 0;
    }
}
