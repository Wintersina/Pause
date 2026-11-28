using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Frost's living props (FrostProps.cs: whales, orcas, an icebreaker, a research station, an abandoned lab, a crashed plane;
// round 2: snowcats, a radar mast, an ice-fishing camp, a flare strip, a frozen-in tanker, polar bears, narwhals, a sonde,
// seals, a crashed heli and -- replacing the belly-landed airlift -- a MASSIVE crashed starship -- most in four variants, each
// copy picking one by hash):
//   * LOAD: the props atlas resolves with every kind's every 8-cell variant strip, the director builds its pool and lights;
//   * PLACED where they make sense (the user, 2026-11-28): whales, orcas, narwhals and the icebreaker on open water (mask W,
//     whole footprint and a clear rim); bears and seals on an ice edge (water with ice beside); the fishing camp on a flat top
//     by open water; the tanker frozen into the ice by a lead; the crashed heli on a snow field with little water near; the
//     starship's crater mostly on ice (a broken edge allowed), placed ahead of the landmarks and drawn ~400 px wide;
//     the snowcat, radar and sonde only beside a station or lab; the structures on a flat top like the landmarks; nothing
//     wet on the glacier (v4); pinned to their spot of the ground;
//   * KEEP-OUT / OVERLAP: no prop overlaps a landmark, a dock or another prop, nor their keep-out margins (KeepOut); no
//     smoke / steam column rises through one; no dock stands inside a prop's SiteKeepOut margin -- live, and forced (docks
//     asked for right on top of a prop);
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
            ForcedOverlap();
            StarshipAhead();
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
        string[] want = { "whale", "orca", "station", "lab", "icebreaker", "plane", "snowcat", "radar", "fishcamp", "flares", "tanker",
                          "bears", "narwhal", "sonde", "seals", "heli", "starship" };
        bool picks = names.Length == want.Length;
        foreach (string n in want) picks &= System.Array.IndexOf(names, n) >= 0;
        Check("seventeen props, the approved picks: " + string.Join(", ", names), picks);
        bool shapes = FrostPropsTuning.FrameMs.Length == names.Length && FrostPropsTuning.Where.Length == names.Length &&
                      FrostPropsTuning.OneShot.Length == names.Length && FrostPropsTuning.SizeK.Length == names.Length &&
                      FrostPropsTuning.Feet.Length == names.Length && FrostPropsTuning.Weights.Length == names.Length &&
                      FrostPropsTuning.Variants.Length == names.Length;
        foreach (var ms in FrostPropsTuning.FrameMs) { shapes &= ms.Length == FrostPropsTuning.Frames; foreach (int m in ms) shapes &= m >= 80; }
        Check("every prop has 8 frame timings (>= 80 ms), a size, a foot and a weight", shapes);
        // where each belongs (the user's rule, 2026-11-28)
        var place = new Dictionary<string, FrostPropsTuning.Place>
        {
            { "whale", FrostPropsTuning.Place.OpenWater }, { "orca", FrostPropsTuning.Place.OpenWater }, { "icebreaker", FrostPropsTuning.Place.OpenWater },
            { "narwhal", FrostPropsTuning.Place.OpenWater }, { "bears", FrostPropsTuning.Place.IceEdge }, { "seals", FrostPropsTuning.Place.IceEdge },
            { "fishcamp", FrostPropsTuning.Place.ByHole }, { "tanker", FrostPropsTuning.Place.FrozenIn }, { "heli", FrostPropsTuning.Place.SnowField },
            { "starship", FrostPropsTuning.Place.Crater }, { "station", FrostPropsTuning.Place.Flat }, { "lab", FrostPropsTuning.Place.Flat },
            { "plane", FrostPropsTuning.Place.Flat }, { "flares", FrostPropsTuning.Place.Flat }, { "snowcat", FrostPropsTuning.Place.Outfit },
            { "radar", FrostPropsTuning.Place.Outfit }, { "sonde", FrostPropsTuning.Place.Outfit },
        };
        const GroundClass ice = GroundClass.Open | GroundClass.Canopy;
        bool classes = true;
        string wrongRule = "";
        for (int k = 0; k < names.Length; k++)
        {
            var r = FrostPropsTuning.Rule(k);
            var w = FrostPropsTuning.Where[k];
            bool ok = place.ContainsKey(names[k]) && place[names[k]] == w;
            switch (w)
            {
                case FrostPropsTuning.Place.OpenWater: ok &= r.on == GroundClass.Water && r.need >= .999f && r.rim > 1f && r.rimNeed >= .9f; break;
                case FrostPropsTuning.Place.Flat: ok &= r.on == GroundClass.Open && r.need >= .999f && r.rim > 1f && r.rimNeed >= .9f; break;
                case FrostPropsTuning.Place.ByHole: ok &= r.on == GroundClass.Open && r.need >= .999f && r.near == GroundClass.Water; break;
                case FrostPropsTuning.Place.FrozenIn: ok &= r.on == ice && r.need >= .999f && r.near == GroundClass.Water; break;
                case FrostPropsTuning.Place.IceEdge: ok &= r.on == GroundClass.Water && r.need >= .5f && r.need < 1f && (r.near & ice) == ice; break;
                // relaxed in round 2 so the crash sites appear on every layout: mostly snow in a ring FarRim x the footprint
                case FrostPropsTuning.Place.SnowField: ok &= r.on == ice && r.need >= .999f && Mathf.Approximately(r.rim, FrostPropsTuning.FarRim) &&
                                                             Mathf.Approximately(r.rimNeed, FrostPropsTuning.FarRimNeed) && r.rim > 1f && r.rimNeed >= .8f; break;
                case FrostPropsTuning.Place.Crater: ok &= r.on == ice && r.need >= .6f && r.need < 1f && r.near == GroundClass.None; break;
                case FrostPropsTuning.Place.Outfit: ok &= r.on == ice && r.need >= .999f && r.rim > 1f && r.rimNeed >= .7f; break;
            }
            bool wet = w != FrostPropsTuning.Place.Flat && w != FrostPropsTuning.Place.SnowField && w != FrostPropsTuning.Place.Outfit && w != FrostPropsTuning.Place.Crater;
            ok &= FrostPropsTuning.Wet(k) == wet && FrostPropsTuning.Fits(k, 4) == !wet && FrostPropsTuning.Fits(k, 1);
            ok &= FrostPropsTuning.OneShot[k] == (names[k] == "whale" || names[k] == "orca" || names[k] == "narwhal");
            ok &= FrostPropsTuning.NeedsHost(k) == (names[k] == "snowcat" || names[k] == "radar" || names[k] == "sonde");
            if (!ok) wrongRule += names[k] + " ";
            classes &= ok;
        }
        Check("each prop where it makes sense: sea life / icebreaker on open water, bears and seals on an ice edge, the fishing camp by a hole, " +
              "the tanker frozen in, the crashed heli on a snow field away from water, the starship's crater mostly on ice, the snowcat / radar / sonde beside a station; " +
              "nothing wet on the glacier (" + (wrongRule.Length > 0 ? "wrong: " + wrongRule : "all") + ")", classes);
        bool vars = true;
        for (int k = 0; k < names.Length; k++)
        {
            int n = FrostPropsTuning.Variants[k];
            vars &= n == 1 || (n >= 2 && n <= 4);
            if (n <= 1) continue;
            var seen = new HashSet<int>();
            int last = -1;
            for (uint c = 0; c < 64; c++)
            {
                int v = FrostPropsTuning.PickVariant(k, c * 7919u, last);
                vars &= v >= 0 && v < n && v != last;
                seen.Add(v);
                last = v;
            }
            vars &= seen.Count == n;
        }
        Check("variants: 1 or 2-4 per kind; copies spread over every variant by hash, never the previous copy's", vars);
        Check("keep-out margins: KeepOut " + FrostPropsTuning.KeepOut + " of a prop's width, SiteKeepOut " + FrostPropsTuning.SiteKeepOut + " of a dock's",
              FrostPropsTuning.KeepOut >= .2f && FrostPropsTuning.SiteKeepOut >= .3f);
        float min = float.MaxValue, max = 0f;
        for (int k = 0; k < names.Length; k++)
        {
            if (FrostPropsTuning.Massive(k)) continue;
            min = Mathf.Min(min, FrostPropsTuning.Size(k)); max = Mathf.Max(max, FrostPropsTuning.Size(k) * (1f + FrostPropsTuning.SizeJitter));
        }
        Check("drawn at the landmarks' scale (" + min.ToString("F2") + " .. " + max.ToString("F2") + " u, landmarks " + FrostTuning.LandmarkMin.ToString("F2") +
              " .. " + FrostTuning.LandmarkMax.ToString("F2") + ", scaled by the ground zoom)",
              min >= .7f * FrostTuning.LandmarkMin && max <= FrostTuning.LandmarkMax && Mathf.Approximately(FrostPropsTuning.Size(0), FrostPropsTuning.SizeK[0] * FrostTuning.PieceScale));
        // MASSIVE: the starship ~1/3 .. 1/2 of a 1080 px phone's width (the view ~2 x 3.71 u wide), far bigger than the station /
        // tanker, under its own cap (above the landmarks'), from a 256 px cell (~1.5 px a texel: crisp, point-sampled)
        {
            int s = FrostPropsTuning.Starship;
            float pxu = 1080f / (2f * 3.71f), lo = FrostPropsTuning.Size(s) * (1f - FrostPropsTuning.SizeJitter) * pxu, hi = FrostPropsTuning.Size(s) * (1f + FrostPropsTuning.SizeJitter) * pxu;
            Check("the starship is MASSIVE: " + lo.ToString("F0") + " .. " + hi.ToString("F0") + " px on a 1080 px phone (320 .. 480), >= 4 x the station / tanker, " +
                  "capped at " + FrostPropsTuning.MaxSize(s) + " u (> the landmarks' " + BackdropCatalog.MaxLandmarkSize + "), 256 px cells",
                  lo >= 320f && hi <= 480f && FrostPropsTuning.Size(s) >= 4f * FrostPropsTuning.Size(FrostPropsTuning.Tanker) &&
                  FrostPropsTuning.Size(s) * (1f + FrostPropsTuning.SizeJitter) <= FrostPropsTuning.MaxSize(s) && FrostPropsTuning.MaxSize(s) > BackdropCatalog.MaxLandmarkSize &&
                  Mathf.Approximately(FrostPropsTuning.Cell(s), 256f) && FrostPropsTuning.Massive(s) && !FrostPropsTuning.Wet(s));
        }
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
            known &= l.variants >= 0 && l.variants < (1 << FrostPropsTuning.Variants[l.prop]) || l.variants == 0;
            known &= l.at.x >= 0f && l.at.x < FrostPropsTuning.Cell(l.prop) && l.at.y >= 0f && l.at.y < FrostPropsTuning.Cell(l.prop);
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
        for (int k = 0; k < names.Length; k++)
            for (int v = 0; v < FrostPropsTuning.Variants[k]; v++)
            {
                int n = 0;
                foreach (var l in FrostPropsTuning.Lights) if (l.prop == k && FrostPropsTuning.On(l, v)) n++;
                fit &= n <= FrostPropsTuning.MaxLights;
            }
        Check("lamps: the station's red mast beacon and amber windows, the lab's mast tip, the plane's red tail beacon, the icebreaker's deck lights; " +
              "round 2: the snowcats' headlights, the radar's and tanker's red beacons, the camp's windows, the flare pots, the sonde's beacon and strobe, " +
              "the crashed heli's and the starship's distress beacons (at most " + FrostPropsTuning.MaxLights + " a variant)",
              per[FrostPropsTuning.Station] >= 2 && per[FrostPropsTuning.Lab] >= 1 && per[FrostPropsTuning.Plane] >= 1 && per[FrostPropsTuning.Icebreaker] >= 2 &&
              per[FrostPropsTuning.Snowcat] >= 1 && per[FrostPropsTuning.Radar] >= 1 && per[FrostPropsTuning.Fishcamp] >= 2 && per[FrostPropsTuning.Flares] >= 2 &&
              per[FrostPropsTuning.Tanker] >= 1 && per[FrostPropsTuning.Sonde] >= 2 && per[FrostPropsTuning.Heli] >= 1 && per[FrostPropsTuning.Starship] >= 3 && fit && known);
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
        int strips = 0;
        foreach (int n in FrostPropsTuning.Variants) strips += n;
        bool all = atlas.Count == strips * FrostPropsTuning.Frames;
        var stripRects = new List<Rect>();
        var stripNames = new List<string>();
        var stripCells = new List<int>();
        for (int k = 0; k < FrostPropsTuning.Names.Length; k++)
            for (int v = 0; v < FrostPropsTuning.Variants[k]; v++)
            {
                var f = atlas.Frames(FrostPropsTuning.Strip(k, v));
                all &= f.Length == FrostPropsTuning.Frames;
                float cell = FrostPropsTuning.Cell(k);
                foreach (var s in f) all &= s != null && Mathf.Approximately(s.rect.width, cell) && Mathf.Approximately(s.rect.height, cell) &&
                                          FrostPropsTuning.KindOf(s.name) == k && FrostPropsTuning.VariantOf(s.name) == v;
                if (f.Length > 0 && f[0] != null) { stripRects.Add(f[0].rect); stripNames.Add(FrostPropsTuning.Strip(k, v)); stripCells.Add((int)FrostPropsTuning.Cell(k)); }
            }
        var scar = new BackdropAtlas(Resources.Load<Texture2D>(Folder + FrostPropsTuning.ScarAtlas), Resources.Load<TextAsset>(Folder + FrostPropsTuning.ScarAtlas));
        bool scarOk = scar.Count == FrostPropsTuning.ScarCells.Length;
        foreach (var n in FrostPropsTuning.ScarCells) { var c = scar.Get(n); scarOk &= c != null && Mathf.Approximately(c.rect.width, FrostPropsTuning.ScarTexels); }
        Check("the starship's crash-scar decals load: " + scar.Count + " cells of " + FrostPropsTuning.ScarTexels + " texels (drawings a / b, dry / wet)", scarOk);
        scar.Destroy();
        Check("the props atlas loads: " + strips + " strips (every kind's every variant) of 8 x 128 px cells, the starship's 256 (" + atlas.Count + " sprites), each cell naming its kind and variant", all);
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
        int scw, sch, scarPink = 0;
        var spx = Png(ArtDir + FrostPropsTuning.ScarAtlas + ".png", out scw, out sch);
        if (spx != null)
        {
            var sdrawn = BackdropGrade.Apply(spx, lift, sat);
            for (int i = 0; i < spx.Length; i++) if (spx[i].a > 0f && (WorldCue.IsPink(spx[i]) || WorldCue.IsPink(sdrawn[i]))) scarPink++;
        }
        Check("no pink in the crash scar, painted or drawn (" + scarPink + " px)", spx != null && scarPink == 0);
        Check("no pink in the props as painted (" + pinkPainted + " px) or as drawn, lifted x" + lift.ToString("F2") + " (" + pinkDrawn + " px)", pinkPainted == 0 && pinkDrawn == 0);
        float mean = MeanLum(drawn), lmean = MeanLum(ldrawn);
        Check("as drawn, the props sit near the landmarks' brightness (mean luminance " + mean.ToString("F3") + " <= 1.25 x the landmarks' " + lmean.ToString("F3") +
              ", under the set-piece ceiling .66)", mean <= lmean * 1.25f && mean <= .66f);
        // per strip (its 8 cells from its first cell's rect; texture rows run bottom-up, as the rects): under the ceiling, its
        // forms (value p90 of the opaque texels, the lamps a few px) a step above the landmarks' at most
        var lv = new List<float>();
        foreach (var c in ldrawn) if (c.a > .5f) lv.Add(Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
        lv.Sort();
        float lp90 = lv.Count > 0 ? lv[(int)(lv.Count * .9f)] : 0f;
        string rows = "";
        bool rowsOk = true;
        for (int k = 0; k < stripRects.Count; k++)
        {
            int y0 = (int)stripRects[k].y, x0 = (int)stripRects[k].x, cp = stripCells[k];
            var vals = new List<float>();
            double sw = 0, sl = 0;
            for (int y = y0; y < y0 + cp && y < h; y++)
                for (int x = x0; x < x0 + cp * FrostPropsTuning.Frames && x < w; x++)
                {
                    var c = drawn[y * w + x];
                    if (c.a > .5f) vals.Add(Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
                    if (c.a > .01f) { sw += c.a; sl += c.a * Lum(c); }
                }
            vals.Sort();
            float p90 = vals.Count > 0 ? vals[(int)(vals.Count * .9f)] : 1f;
            float ml = sw > 0 ? (float)(sl / sw) : 1f;
            rows += stripNames[k] + " p90 " + p90.ToString("F2") + " lum " + ml.ToString("F2") + "; ";
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
        int keepOut = 0, plumes = 0, docks = 0, hostless = 0;
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
                    if (FrostPropsTuning.Wet(k) && FrostTuning.IsGlacier(v)) floaters++;
                    if (FrostPropsTuning.NeedsHost(k) && !HostBeside(d, p)) hostless++;
                    overlaps += Overlaps(d, p);
                }
                // keep-out, every frame (a dock or a landmark dealt after the prop must keep clear of it too)
                for (int i = 0; i < d.Props.items.Count; i++)
                {
                    var p = d.Props.items[i];
                    if (!p.active) continue;
                    keepOut += KeptOut(d, p);
                    plumes += PlumesThrough(d, p);
                    docks += DocksBy(d, p);
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
        Check("v" + v + ": KEEP-OUT -- no ground piece inside a prop's keep-out margin (" + keepOut + " frames), no smoke / steam column through a prop (" +
              plumes + "), no dock inside a prop's dock margin (" + docks + ")", keepOut == 0 && plumes == 0 && docks == 0);
        Check("v" + v + ": every snowcat / radar / sonde dealt beside a station or lab (" + hostless + " without)", hostless == 0);
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

    // The host (a station / lab prop) beside outfit prop `p`: their drawings' gap at most HostGapMax of the host's width.
    static bool HostBeside(FrostDirector d, BackdropPiece p)
    {
        for (int i = 0; i < d.Props.items.Count; i++)
        {
            var q = d.Props.items[i];
            if (!q.active || q == p || !FrostPropsTuning.IsHost(d.PropKind(i))) continue;
            float reach = (q.size + p.size) * (FrostPropsTuning.ClaimCore + FrostPropsTuning.KeepOut) + q.size * FrostPropsTuning.HostGapMax;
            float dx = p.x - q.x, dy = (float)(p.gy - q.gy) / .8f;
            if (dx * dx + dy * dy <= reach * reach * 1.0001f) return true;
        }
        return false;
    }

    // Ground pieces (landmarks, docks, other props: their planner core) inside prop `p`'s claimed drawing + KeepOut margin.
    static int KeptOut(FrostDirector d, BackdropPiece p)
    {
        int n = 0;
        float px = FrostPropsTuning.ClaimHalf(p.size), py = px * .8f;
        var pools = new List<BackdropPool>(d.Landmarks) { d.Props };
        foreach (var pool in pools)
            foreach (var q in pool.items)
            {
                if (!q.active || q == p || q.sr.sprite == null) continue;
                float qx = q.size * Mathf.Max(q.core, GroundPlanner.MinCore), qy = qx * .8f;
                if (Mathf.Abs(p.x - q.x) < (px + qx) * .98f && System.Math.Abs(p.gy - q.gy) < (py + qy) * .98f) n++;
            }
        return n;
    }

    // Smoke / steam columns (FrostDirector.PlumeBox, without its own margin) of every ground piece crossing prop `p`'s drawing.
    static int PlumesThrough(FrostDirector d, BackdropPiece p)
    {
        int n = 0;
        float px = p.size * FrostPropsTuning.ClaimCore, py = px * .8f;
        foreach (var pool in d.Landmarks)
            foreach (var q in pool.items)
            {
                if (!q.active || q.sr.sprite == null) continue;
                float cx, hx, hy; double cy;
                for (int e = 0; FrostDirector.PlumeBox(q.sr.sprite.name, e, q.size, q.body.localScale.x < 0f, q.x, q.gy, out cx, out cy, out hx, out hy); e++)
                {
                    float k = 1f / (1f + FrostPropsTuning.PlumeKeepOut);
                    if (Mathf.Abs(p.x - cx) < px + hx * k && System.Math.Abs(p.gy - cy) < py + hy * k) n++;
                }
            }
        return n;
    }

    // Docks standing inside prop `p`'s dock margin (the dock's core + SiteKeepOut of its width, against the prop's claim).
    static int DocksBy(FrostDirector d, BackdropPiece p)
    {
        int n = 0;
        if (d.Sites == null) return 0;
        float px = FrostPropsTuning.ClaimHalf(p.size), py = px * .8f;
        foreach (var q in d.Sites.items)
        {
            if (!q.active) continue;
            float m = q.size * (Mathf.Max(q.core, GroundPlanner.MinCore) + FrostPropsTuning.SiteKeepOut) * .98f;
            if (Mathf.Abs(p.x - q.x) < px + m && System.Math.Abs(p.gy - q.gy) < py + m * .8f) n++;
        }
        return n;
    }

    // OVERLAP, forced: a prop of every kind placed in view, then docks asked for right on top of it (SpawnSite at its screen y,
    // many times) and the ground scrolled on with the landmarks dealt round it: no dock inside its margin, no landmark or
    // other prop in its keep-out, no plume column through it, at any step.
    static void ForcedOverlap()
    {
        int tried = 0, docksAsked = 0, docksPlaced = 0, bad = 0;
        string where = "";
        for (int k = 0; k < FrostPropsTuning.Names.Length; k++)
        {
            var wk = FrostPropsTuning.Where[k];
            int v = wk == FrostPropsTuning.Place.OpenWater || wk == FrostPropsTuning.Place.IceEdge ? 2 : 1;   // the widest water (v2), else the ice plates (v1)
            FrostDirector.SeedOverride = 151 + k;
            var wb = Fresh(v, 1.5f);
            var d = Director(wb);
            if (d == null || d.Props == null) { if (wb != null) Object.DestroyImmediate(wb.gameObject); continue; }
            d.ClearProps();
            double tr = d.Planner.Travel;
            float hh = wb.Current.HalfHeight;
            if (FrostPropsTuning.NeedsHost(k)) d.SpawnProp(FrostPropsTuning.Station, tr - hh * .4f, tr + hh * .6f, 800);
            BackdropPiece p = null;
            if (d.SpawnProp(k, tr - hh * .4f, tr + hh * .6f, 800))
                for (int i = 0; i < d.Props.items.Count; i++) if (d.Props.items[i].active && d.PropKind(i) == k) p = d.Props.items[i];
            if (p == null) { Object.DestroyImmediate(wb.gameObject); continue; }
            tried++;
            for (int a = 0; a < 6; a++)
            {
                docksAsked++;
                if (d.SpawnSite(p.y)) docksPlaced++;
            }
            for (float t = 0f; t < 20f && p.active; t += Dt)
            {
                wb.Step(Dt);
                for (int i = 0; i < d.Props.items.Count; i++)
                {
                    var q = d.Props.items[i];
                    if (!q.active) continue;
                    int b = KeptOut(d, q) + PlumesThrough(d, q) + DocksBy(d, q);
                    if (b > 0 && where.Length < 160) where += FrostPropsTuning.Names[d.PropKind(i)] + " ";
                    bad += b;
                }
            }
            Object.DestroyImmediate(wb.gameObject);
        }
        FrostDirector.SeedOverride = 0;
        Check("OVERLAP (forced): " + tried + " props, " + docksAsked + " docks asked for on top of them (" + docksPlaced + " placed clear), the ground scrolled 20 s: " +
              bad + " keep-out / plume / dock violations " + where, tried >= FrostPropsTuning.Names.Length - 4 && bad == 0);
    }

    // MASSIVE, placed first: the starship dealt the regular way (SpawnProp ahead) on every layout lands above the landmarks'
    // spawn band (out of sight, its keep-out clear of it), mostly on ice, and as it scrolls into view the landmarks, docks and
    // other props are dealt round it: nothing in its keep-out, no plume through it, no dock by it; drawn 320 .. 480 px wide.
    static void StarshipAhead()
    {
        const int Seeds = 4;
        int s = FrostPropsTuning.Starship, total = 0, bad = 0, notAhead = 0, seen = 0, scarBad = 0, scarWrong = 0;
        float scarPx = 0f;
        float minPx = float.MaxValue, maxPx = 0f;
        string per = "";
        bool every = true;
        for (int v = 1; v <= 4; v++)
        {
            int placed = 0;
            for (int seed = 0; seed < Seeds; seed++)
            {
                FrostDirector.SeedOverride = 401 + seed * 17 + v;
                var wb = Fresh(v, 2f);
                var d = Director(wb);
                if (d == null || d.Props == null) { if (wb != null) Object.DestroyImmediate(wb.gameObject); continue; }
                d.ClearProps();
                var set = wb.Current;
                double lo = d.Planner.Travel + set.HalfHeight + FrostTuning.SpawnAbove, hi = lo + FrostTuning.SpawnBand;
                BackdropPiece p = null;
                int slot = -1;
                if (d.SpawnProp(s, lo, hi, FrostPropsTuning.Tries, true))
                    for (int i = 0; i < d.Props.items.Count; i++) if (d.Props.items[i].active && d.PropKind(i) == s) { p = d.Props.items[i]; slot = i; }
                if (p == null) { Object.DestroyImmediate(wb.gameObject); continue; }
                placed++;
                // its crash scar: a decal under it (same material, one order below), the right drawing's cell
                var sc = d.PropScar(slot);
                int want = FrostPropsTuning.Variants[s] > 1 && d.PropVariant(slot) >= 2 ? 2 : 0;
                if (sc == null || !sc.enabled || sc.sprite == null || sc.sortingOrder >= p.sr.sortingOrder || sc.sharedMaterial != p.sr.sharedMaterial ||
                    (sc.sprite.name != FrostPropsTuning.ScarCells[want] && sc.sprite.name != FrostPropsTuning.ScarCells[want + 1]))
                    scarWrong++;
                else scarPx = Mathf.Max(scarPx, sc.bounds.size.x * 1080f / (2f * set.HalfWidth));
                if (p.gy - FrostPropsTuning.ClaimHalf(p.size) * .8f < hi || p.y - p.size * .5f < set.HalfHeight) notAhead++;
                float pxu = 1080f / (2f * set.HalfWidth);
                bool inView = false;
                for (float t = 0f; t < 40f && p.active && p.y > -set.HalfHeight * .2f; t += Dt)
                {
                    wb.Step(Dt);
                    if (!p.active) break;
                    if (p.y < set.HalfHeight * .5f) inView = true;
                    for (int i = 0; i < d.Props.items.Count; i++)
                    {
                        var q = d.Props.items[i];
                        if (q.active) bad += KeptOut(d, q) + PlumesThrough(d, q) + DocksBy(d, q);
                    }
                    scarBad += ScarCrossed(d, slot);
                }
                if (inView) seen++;
                float w = p.size * pxu;
                minPx = Mathf.Min(minPx, w); maxPx = Mathf.Max(maxPx, w);
                Object.DestroyImmediate(wb.gameObject);
            }
            total += placed;
            every &= placed >= 1;
            per += "v" + v + " " + placed + "/" + Seeds + " ";
        }
        FrostDirector.SeedOverride = 0;
        Check("STARSHIP placed ahead on every layout (" + per + "), above the landmarks' band out of sight (" + notAhead + " not), " + seen + " scrolled into view",
              every && total >= 2 * Seeds && notAhead == 0 && seen == total);
        Check("STARSHIP: the ground is dealt round its keep-out as it scrolls in (" + bad + " keep-out / plume / dock violations), drawn " +
              minPx.ToString("F0") + " .. " + maxPx.ToString("F0") + " px wide on a 1080 px phone (320 .. 480)",
              bad == 0 && total > 0 && minPx >= 320f && maxPx <= 480f);
        Check("STARSHIP CRASH SCAR: every copy lies on a decal under it (the furrow, banks, cracks, debris, soot; wet by water), " + scarWrong +
              " wrong, ~" + scarPx.ToString("F0") + " px across; nothing dealt onto its furrow's keep-out (" + scarBad + " crossings)",
              scarWrong == 0 && scarBad == 0 && scarPx >= 900f);
    }

    // Landmarks, docks and other props whose core lies on the crash-scar furrow boxes of the prop in `slot`.
    static int ScarCrossed(FrostDirector d, int slot)
    {
        int n = 0;
        var pools = new List<BackdropPool>(d.Landmarks) { d.Props };
        if (d.Sites != null && !pools.Contains(d.Sites)) pools.Add(d.Sites);
        for (int j = 0; j < FrostPropsTuning.ScarBoxes[0].Length; j++)
        {
            float bx, h; double by;
            if (!d.PropScarBox(slot, j, out bx, out by, out h)) break;
            foreach (var pool in pools)
                foreach (var q in pool.items)
                {
                    if (!q.active || q == d.Props.items[slot] || q.sr.sprite == null) continue;
                    float qx = q.size * Mathf.Max(q.core, GroundPlanner.MinCore), qy = qx * .8f;
                    if (Mathf.Abs(bx - q.x) < (h + qx) * .98f && System.Math.Abs(by - q.gy) < (h + qy) * .98f) n++;
                }
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
            var wk = FrostPropsTuning.Where[k];
            int[] order = wk == FrostPropsTuning.Place.OpenWater || wk == FrostPropsTuning.Place.IceEdge ? new[] { 2, 1, 3 } :
                          FrostPropsTuning.Wet(k) ? new[] { 1, 3, 2 } : new[] { 1, 3, 2, 4 };
            WorldBackdrop wb = null;
            FrostDirector d = null;
            int slot = -1, v = 0;
            foreach (int vv in order)
            {
                FrostDirector.SeedOverride = 77 + k;
                bool massive = FrostPropsTuning.Massive(k);
                wb = Fresh(v = vv, massive ? 2f : .25f);
                d = Director(wb);
                if (d == null || d.Props == null) break;
                d.ClearProps();
                double tr = d.Planner.Travel;
                float hh = wb.Current.HalfHeight;
                if (massive)
                {
                    // the starship is dealt ahead of the ground (the view is full of landmarks): placed above the landmarks'
                    // band, then scrolled into view
                    double lo = tr + hh + FrostTuning.SpawnAbove;
                    if (d.SpawnProp(k, lo, lo + FrostTuning.SpawnBand, 800, true))
                        for (int i = 0; i < d.Props.items.Count; i++) if (d.Props.items[i].active && d.PropKind(i) == k) slot = i;
                    for (float t = 0f; slot >= 0 && t < 40f && d.Props.items[slot].y > hh * .3f; t += Dt) wb.Step(Dt);
                    if (slot >= 0) break;
                    Object.DestroyImmediate(wb.gameObject);
                    wb = null;
                    continue;
                }
                // a station's outfit needs its station on the ground first
                if (FrostPropsTuning.NeedsHost(k)) d.SpawnProp(FrostPropsTuning.Station, tr - hh * .4f, tr + hh * .4f, 800);
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
                if (FrostPropsTuning.VariantOf(p.sr.sprite != null ? p.sr.sprite.name : null) != d.PropVariant(slot)) wrongSheet = true;
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
            Check(name + ": shows only its own variant's cells (variant " + d.PropVariant(slot) + ")", !wrongSheet);
            if (FrostPropsTuning.OneShot[k])
                Check(name + ": waits as a shadow " + FrostPropsTuning.WaitMin + " .. " + FrostPropsTuning.WaitMax + " s (surfaced at " + firstUp.ToString("F2") +
                      " s), surfaces through every cell (" + framesSeen.Count + "/8) and goes back under",
                      firstUp >= FrostPropsTuning.WaitMin - .05f && firstUp <= FrostPropsTuning.WaitMax + .05f && framesSeen.Count == 8 && back);
            else
                Check(name + ": loops through its cells (" + framesSeen.Count + "/8 in 3 s)", framesSeen.Count == 8);
            if (!FrostPropsTuning.OneShot[k] && FrostPropsTuning.HasLights(k))
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
