using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Storm's living props (StormProps.cs), two depths:
//   * LOAD: the props atlas (ground kinds) and the aloft atlas resolve with every livery's 8-cell strip; the director builds
//     its pools (ground props and cells on the ground plane, one aloft piece on the aloft layer);
//   * PLACED where they belong (ground): refinery, mast and cell on the lit cloud tops (mask O), the wreck in a dark lane (W),
//     the vortex on lane / deck (W | C); never overlapping a landmark, a dock or another prop (the cell is light: it claims
//     nothing); pinned to their spot of the ground; never two vortices in view;
//   * ALOFT: drawn 1.6 - 2.5x their ground size, scrolling 1.3 - 1.6x the ground's rate, not pinned, its own drift; at most
//     one in view and often none; hazed under every weather layer and behind gameplay;
//   * LIVERY: every copy wears a livery hashed from its spawn, never the previous copy's; its animation has its own phase;
//   * ANIMATED from the piece's age: every cell shows; dt 0 is a still frame (cell, spot, spin, bob);
//   * PHOTOSENSITIVITY: the cell's and the vortex's flashes rise no closer than StormTuning.MinPulseGap and under 3 / s;
//   * COLOUR: no pink anywhere (WorldCue.IsPink), as painted and as drawn; brightness inside the landmark rules, the aloft
//     pieces dimmer than every shot;
//   * no allocation per frame.
public static class StormPropsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SPR] PASS  " : "[SPR] FAIL  ") + what);
        if (!ok) fails++;
    }
    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 20f;
    const string Folder = "Worlds/Storm/Backdrop3/";
    const string ArtDir = "Assets/Art/Backgrounds/Resources/Worlds/Storm/Backdrop3/";
    static BackdropVariants Sel => BackdropVariants.For("Storm");

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        float savedSpeed = moveBackGround.speed;
        try
        {
            Table();
            Art();
            Flashes();
            for (int v = 1; v <= StormTuning.Variants; v++) Live(v);
            Forced();
            Aloft();
            Clear(2);
            Clear(4);
            Moorings();
        }
        finally
        {
            StormDirector.SeedOverride = 0;
            moveBackGround.speed = savedSpeed;
            Time.timeScale = 1f;
            Sel.Reset();
            EliteSystem.Clear();
            if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
        }
        Debug.Log("[SPR] failures: " + fails);
        return fails;
    }

    static WorldBackdrop Fresh(int variant, float speed)
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        Time.timeScale = 1f;
        moveBackGround.speed = speed;
        Sel.Reset();
        Sel.Seed(11);
        Sel.Force = variant;
        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = 1080f / 2400f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 1080, 2400);
        var wb = WorldBackdrop.Create("Storm");
        wb.Show("Storm", false);
        return wb;
    }

    static StormDirector Director(WorldBackdrop wb) { return wb.Current != null ? wb.Current.Director as StormDirector : null; }

    // "<kind>_<variant>_<frame>" -> kind, variant, frame
    static bool Parse(string sprite, out string kind, out int variant, out int frame)
    {
        kind = null; variant = frame = -1;
        if (string.IsNullOrEmpty(sprite)) return false;
        var parts = sprite.Split('_');
        if (parts.Length != 3) return false;
        kind = parts[0];
        return int.TryParse(parts[1], out variant) && int.TryParse(parts[2], out frame);
    }

    // ---- the table ---------------------------------------------------------------------------

    static void Table()
    {
        Check("ground kinds: refinery, mast, wreck, vortex, cell, gunship, tug, rods, buoys, skimmer (" + string.Join(", ", StormPropsTuning.Ground) + ")",
              string.Join(",", StormPropsTuning.Ground) == "refinery,mast,wreck,vortex,cell,gunship,tug,rods,buoys,skimmer");
        Check("aloft kinds: aerostat, convoy, jelly, rays -- the moored skimmer rides the ground moorings now (" + string.Join(", ", StormPropsTuning.Aloft) + ")",
              string.Join(",", StormPropsTuning.Aloft) == "aerostat,convoy,jelly,rays");
        Check("liveries (all of each approved prop): refinery / mast / wreck / gunship / tug / rods / buoys / skimmer 4, vortex / cell 3; aerostat / rays 4, convoy 3, jelly 7",
              string.Join(",", StormPropsTuning.GroundVariants) == "4,4,4,3,3,4,4,4,4,4" && string.Join(",", StormPropsTuning.AloftVariants) == "4,3,7,4");
        bool shapes = StormPropsTuning.GroundMs.Length == StormPropsTuning.Ground.Length && StormPropsTuning.GroundPx.Length == StormPropsTuning.Ground.Length &&
                      StormPropsTuning.AloftMs.Length == StormPropsTuning.Aloft.Length;
        foreach (var w in StormPropsTuning.GroundWeights) shapes &= w.Length == StormPropsTuning.Ground.Length;
        foreach (var w in StormPropsTuning.AloftWeights) shapes &= w.Length == StormPropsTuning.Aloft.Length;
        foreach (var ms in StormPropsTuning.GroundMs) { shapes &= ms.Length == StormPropsTuning.Frames; foreach (int m in ms) shapes &= m >= 80; }
        foreach (int m in StormPropsTuning.AloftMs) shapes &= m >= 80;
        Check("every kind has 8 frame timings (>= 80 ms), a size and a weight per ground set", shapes &&
              StormPropsTuning.GroundWeights.Length == StormTuning.Variants + 1 && StormPropsTuning.AloftWeights.Length == StormTuning.Variants + 1);

        // mask classes
        var R = new System.Func<int, PieceRule>(StormPropsTuning.GroundRule);
        bool classes = R(StormPropsTuning.Refinery).on == GroundClass.Open && R(StormPropsTuning.Mast).on == GroundClass.Open &&
                       R(StormPropsTuning.Cell).on == GroundClass.Open && R(StormPropsTuning.Wreck).on == GroundClass.Water &&
                       R(StormPropsTuning.Vortex).on == (GroundClass.Water | GroundClass.Canopy) &&
                       R(StormPropsTuning.Gunship).on == (GroundClass.Water | GroundClass.Canopy) && R(StormPropsTuning.Tug).on == (GroundClass.Water | GroundClass.Canopy) &&
                       R(StormPropsTuning.Buoys).on == (GroundClass.Water | GroundClass.Canopy) && R(StormPropsTuning.Rods).on == GroundClass.Open &&
                       R(StormPropsTuning.Skimmer).on == (GroundClass.Water | GroundClass.Canopy);
        for (int k = 0; k < StormPropsTuning.Ground.Length; k++) classes &= R(k).need >= .45f && R(k).core > 0f;
        Check("mask classes: refinery, mast and cell on the lit cloud tops (O), the wreck in a dark lane (W), rods on the tops (O); the vortex, the crashed gunship, the moored tug, skimmer and buoy chain on lane / deck (W|C)", classes);
        bool claims = true, keep = StormPropsTuning.KeepK.Length == StormPropsTuning.Ground.Length;
        for (int k = 0; k < StormPropsTuning.Ground.Length; k++) { claims &= StormPropsTuning.Claims(k); if (keep) keep &= StormPropsTuning.KeepK[k] >= 1.05f; }
        if (keep) for (int k = 0; k < StormPropsTuning.Ground.Length; k++) keep &= StormPropsTuning.KeepK[StormPropsTuning.Gunship] >= StormPropsTuning.KeepK[k];
        Check("every ground prop (the cell too) claims a keep-out box > its footprint, the crashed gunship the widest", claims && keep);
        Check("the moorings come together: tug / skimmer beside a buoy chain, a buoy chain beside a tug or skimmer, the rods beside a refinery",
              StormPropsTuning.Companion(StormPropsTuning.Tug, true) == StormPropsTuning.Buoys && StormPropsTuning.Companion(StormPropsTuning.Skimmer, false) == StormPropsTuning.Buoys &&
              StormPropsTuning.Companion(StormPropsTuning.Buoys, true) == StormPropsTuning.Tug && StormPropsTuning.Companion(StormPropsTuning.Buoys, false) == StormPropsTuning.Skimmer &&
              StormPropsTuning.Companion(StormPropsTuning.Rods, true) == StormPropsTuning.Refinery && StormPropsTuning.Companion(StormPropsTuning.Gunship, true) < 0);
        // the user's rule: nothing flies around that the player cannot shoot -- only the jellies (creatures) drift
        bool still = true;
        for (int k = 0; k < StormPropsTuning.Aloft.Length; k++) still &= StormPropsTuning.Drifts(k) == (k == StormPropsTuning.Jelly || k == StormPropsTuning.Rays);
        Check("no vehicle travels: aerostats tethered, the convoy moored (bob / sway only); only the creatures (jellies, rays) drift", still &&
              StormPropsTuning.SwayDeg[StormPropsTuning.Aerostat] > 0f && StormPropsTuning.SwayDeg[StormPropsTuning.Convoy] > 0f);

        // sizes
        float gmin = float.MaxValue, gmax = 0f;
        for (int k = 0; k < StormPropsTuning.Ground.Length; k++) { gmin = Mathf.Min(gmin, StormPropsTuning.GroundSize(k)); gmax = Mathf.Max(gmax, StormPropsTuning.GroundSize(k)); }
        Check("ground props at the zoomed ground's scale (" + gmin.ToString("F2") + " .. " + gmax.ToString("F2") + " u; landmarks " +
              StormTuning.LandmarkMin.ToString("F2") + " .. " + StormTuning.LandmarkMax.ToString("F2") + ", cap " + BackdropCatalog.MaxLandmarkSize + ")",
              gmin >= .5f * StormTuning.LandmarkMin && gmax <= BackdropCatalog.MaxLandmarkSize);
        bool aloftSize = true;
        string sizes = "";
        for (int k = 0; k < StormPropsTuning.Aloft.Length; k++)
        {
            float ratio = StormPropsTuning.AloftSize(k) / (StormPropsTuning.AloftBasePx[k] / StormPropsTuning.PxPerUnit);
            sizes += StormPropsTuning.Aloft[k] + " x" + ratio.ToString("F2") + " (" + StormPropsTuning.AloftSize(k).ToString("F2") + " u) ";
            aloftSize &= ratio >= 1.6f && ratio <= 2.5f && StormPropsTuning.AloftSize(k) * (1f + StormPropsTuning.SizeJitter) <= 2.3f;
        }
        Check("aloft pieces drawn 1.6 - 2.5x their ground size: " + sizes, aloftSize);

        // the aloft layer
        var spec = BackdropCatalog.For("Storm");
        var layer = spec.Find(StormPropsTuning.AloftLayer);
        int at = System.Array.IndexOf(spec.layers, layer), flow = System.Array.IndexOf(spec.layers, spec.Find("flow")), glow = System.Array.IndexOf(spec.layers, spec.Find("glow")),
            pinnedPlane = System.Array.IndexOf(spec.layers, spec.Find("ground"));
        float k2 = layer.rate / spec.Rate("ground");
        Check("the aloft layer: its own, not pinned, scrolling " + k2.ToString("F2") + "x the ground (1.3 - 1.6x), above the vapour and the pinned plane, under the lightning and every weather layer, behind gameplay",
              spec.Has(StormPropsTuning.AloftLayer) && string.IsNullOrEmpty(layer.pinTo) && layer.role != BackdropCatalog.Role.Landmark &&
              k2 >= 1.3f && k2 <= 1.6f && flow < pinnedPlane && at == pinnedPlane + 1 && glow == at + 1 && spec.Order(StormPropsTuning.AloftLayer) < 0);
        Check("rare: one aloft piece at most (pool " + StormPropsTuning.AloftPool + "), " + StormPropsTuning.AloftGapMin + " .. " + StormPropsTuning.AloftGapMax +
              " u of its own scroll between two (a view is ~15 u); ground props: pool " + StormPropsTuning.GroundPool + ", " + StormPropsTuning.GroundGapMin + " .. " + StormPropsTuning.GroundGapMax + " u apart",
              StormPropsTuning.AloftPool == 1 && StormPropsTuning.AloftGapMin >= 14f && StormPropsTuning.GroundPool <= 8 && StormPropsTuning.GroundGapMin >= 2f);

        // the livery hash: never the previous copy's, and every livery turns up
        bool never = true, every = true;
        for (int n = 3; n <= 7; n++)
        {
            var seen = new HashSet<int>();
            int prev = -1;
            for (int s = 0; s < 200; s++)
            {
                int v = StormPropsTuning.Variant(s, 2, n, prev);
                never &= v != prev && v >= 0 && v < n;
                seen.Add(v);
                prev = v;
            }
            every &= seen.Count == n;
        }
        Check("a copy's livery is hashed from its spawn, never the previous copy's, and every livery turns up", never && every);
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

    struct Stat { public float mean, p90, peak; }

    // the sprites of one kind in an atlas: alpha-weighted mean luminance, value p90 of the opaque texels, peak alpha x luminance
    static Stat Measure(Color[] px, int w, int h, BackdropAtlas atlas, string kind, int variants)
    {
        double sw = 0, sl = 0;
        float peak = 0f;
        var vals = new List<float>();
        for (int v = 0; v < variants; v++)
            foreach (var s in atlas.Frames(StormPropsTuning.Strip(kind, v)))
            {
                var r = s.rect;
                for (int y = (int)r.y; y < (int)(r.y + r.height); y++)
                    for (int x = (int)r.x; x < (int)(r.x + r.width); x++)
                    {
                        var c = px[y * w + x];
                        if (c.a <= .01f) continue;
                        float l = Lum(c);
                        sw += c.a; sl += c.a * l;
                        peak = Mathf.Max(peak, c.a * l);
                        if (c.a > .5f) vals.Add(Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
                    }
            }
        vals.Sort();
        return new Stat { mean = sw > 0 ? (float)(sl / sw) : 0f, p90 = vals.Count > 0 ? vals[(int)(vals.Count * .9f)] : 0f, peak = peak };
    }

    static void Art()
    {
        var spec = BackdropCatalog.For("Storm");
        foreach (var sheet in new[] { StormPropsTuning.Atlas, StormPropsTuning.AloftAtlas })
        {
            bool ground = sheet == StormPropsTuning.Atlas;
            var kinds = ground ? StormPropsTuning.Ground : StormPropsTuning.Aloft;
            var counts = ground ? StormPropsTuning.GroundVariants : StormPropsTuning.AloftVariants;
            var atlas = new BackdropAtlas(Resources.Load<Texture2D>(Folder + sheet), Resources.Load<TextAsset>(Folder + sheet));
            int want = 0;
            bool all = true;
            for (int k = 0; k < kinds.Length; k++)
                for (int v = 0; v < counts[k]; v++)
                {
                    want += StormPropsTuning.Frames;
                    var f = atlas.Frames(StormPropsTuning.Strip(kinds[k], v));
                    all &= f.Length == StormPropsTuning.Frames;
                    foreach (var s in f) all &= s != null && s.rect.width >= 96f && Mathf.Approximately(s.rect.width, s.rect.height);
                }
            Check("the " + sheet + " atlas loads: every livery's 8-cell strip (" + atlas.Count + " / " + want + " sprites)", all && atlas.Count == want);

            int w, h, lw, lh;
            var px = Png(ArtDir + sheet + ".png", out w, out h);
            var lpx = Png(ArtDir + "landmarks.png", out lw, out lh);
            if (px == null || lpx == null) { Check(sheet + ".png and landmarks.png read", false); atlas.Destroy(); continue; }
            var layer = spec.Find(ground ? "ground" : StormPropsTuning.AloftLayer);
            float lift = BackdropGrade.Lift(spec, layer), sat = BackdropGrade.Saturation(spec, lift);
            var drawn = BackdropGrade.Apply(px, lift, sat);
            if (ReferenceEquals(drawn, px)) drawn = (Color[])px.Clone();   // (Apply hands back `px` itself at lift 1: tint a copy)
            if (!ground)
                for (int i = 0; i < drawn.Length; i++) { var c = drawn[i] * StormPropsTuning.AloftTint; drawn[i] = c; }
            var ldrawn = BackdropGrade.Apply(lpx, BackdropGrade.Lift(spec, spec.Find("ground")), sat);
            int pinkPainted = 0, pinkDrawn = 0;
            for (int i = 0; i < px.Length; i++)
            {
                if (px[i].a <= 0f) continue;
                if (WorldCue.IsPink(px[i])) pinkPainted++;
                if (WorldCue.IsPink(drawn[i])) pinkDrawn++;
            }
            Check("no pink in the " + sheet + " as painted (" + pinkPainted + " px) or as drawn x" + lift.ToString("F2") + (ground ? "" : " and hazed") + " (" + pinkDrawn + " px)",
                  pinkPainted == 0 && pinkDrawn == 0);

            var land = new Stat();
            // (the landmarks: every opaque texel of their sheet)
            {
                double sw = 0, sl = 0; var lv = new List<float>();
                foreach (var c in ldrawn) { if (c.a > .01f) { sw += c.a; sl += c.a * Lum(c); } if (c.a > .5f) lv.Add(Mathf.Max(c.r, Mathf.Max(c.g, c.b))); }
                lv.Sort();
                land.mean = sw > 0 ? (float)(sl / sw) : 0f;
                land.p90 = lv.Count > 0 ? lv[(int)(lv.Count * .9f)] : 0f;
            }
            string rows = "";
            bool ok = true;
            for (int k = 0; k < kinds.Length; k++)
            {
                var s = Measure(drawn, w, h, atlas, kinds[k], counts[k]);
                rows += kinds[k] + " lum " + s.mean.ToString("F2") + " p90 " + s.p90.ToString("F2") + " peak " + s.peak.ToString("F2") + "; ";
                if (ground && k == StormPropsTuning.Cell) ok &= s.peak * StormPropsTuning.CellAlpha <= .66f;   // light: its brightest texel, drawn
                else if (ground) ok &= s.mean <= land.mean * 1.25f && s.p90 <= land.p90 + .15f;
                else ok &= s.mean <= land.mean * 1.3f && s.p90 <= .75f && s.mean <= .5f;   // aloft: hazed, dimmer than every shot
            }
            Check(sheet + " as drawn within the landmark rules (landmarks lum " + land.mean.ToString("F2") + " p90 " + land.p90.ToString("F2") + "): " + rows, ok);
            atlas.Destroy();
        }
    }

    // ---- photosensitivity: the flashing props ----------------------------------------------------

    static void Flashes()
    {
        var atlas = new BackdropAtlas(Resources.Load<Texture2D>(Folder + StormPropsTuning.Atlas), Resources.Load<TextAsset>(Folder + StormPropsTuning.Atlas));
        int w, h;
        var px = Png(ArtDir + StormPropsTuning.Atlas + ".png", out w, out h);
        if (px == null) { Check("props.png read (flashes)", false); return; }
        foreach (int k in new[] { StormPropsTuning.Cell, StormPropsTuning.Vortex })
        {
            var ms = StormPropsTuning.GroundMs[k];
            float cycle = StormPropsTuning.CycleMs(ms) / 1000f;
            float worstGap = float.MaxValue, worstRate = 0f;
            for (int v = 0; v < StormPropsTuning.GroundVariants[k]; v++)
            {
                var f = atlas.Frames(StormPropsTuning.Strip(StormPropsTuning.Ground[k], v));
                if (f.Length < 8) continue;
                var e = new float[8];
                float lo = float.MaxValue, hi = 0f;
                for (int i = 0; i < 8; i++)
                {
                    double sum = 0; var r = f[i].rect;
                    for (int y = (int)r.y; y < (int)(r.y + r.height); y++)
                        for (int x = (int)r.x; x < (int)(r.x + r.width); x++) { var c = px[y * w + x]; sum += c.a * Lum(c); }
                    e[i] = (float)(sum / (r.width * r.height));
                    lo = Mathf.Min(lo, e[i]); hi = Mathf.Max(hi, e[i]);
                }
                // a FLASH: a frame whose light rises by more than a third of the strip's range over the frame before it
                var rises = new List<float>();
                float t = 0f;
                for (int i = 0; i < 8; i++)
                {
                    if (e[i] - e[(i + 7) % 8] > (hi - lo) / 3f && hi - lo > .002f) rises.Add(t);
                    t += ms[i] / 1000f;
                }
                for (int i = 0; i < rises.Count; i++)
                {
                    float gap = i + 1 < rises.Count ? rises[i + 1] - rises[i] : rises[0] + cycle - rises[i];
                    worstGap = Mathf.Min(worstGap, gap);
                }
                worstRate = Mathf.Max(worstRate, rises.Count / cycle);
            }
            Check(StormPropsTuning.Ground[k] + ": its flashes rise no closer than " + StormTuning.MinPulseGap + " s (closest " + worstGap.ToString("F2") + " s) and under 3 / s (" +
                  worstRate.ToString("F2") + " / s)", worstGap >= StormTuning.MinPulseGap && worstRate < 3f);
        }
        Check("the cell's light is capped (alpha " + StormPropsTuning.CellAlpha + " <= .9) and the vortex turns slowly (" + StormPropsTuning.VortexSpin + " deg / s <= 12)",
              StormPropsTuning.CellAlpha <= .9f && StormPropsTuning.VortexSpin <= 12f);
        atlas.Destroy();
    }

    // ---- live: placement, rarity, pinning, aloft parallax ----------------------------------------

    static bool InView(BackdropPiece p, float halfW, float halfH)
    {
        return p.y + p.size * .5f > -halfH && p.y - p.size * .5f < halfH && Mathf.Abs(p.x) - p.size * .5f < halfW;
    }

    static void Live(int v)
    {
        const int Runs = 2;
        const float Seconds = 150f;
        int dealt = 0, wrong = 0, overlaps = 0, drift = 0, maxInView = 0, maxAloft = 0, frames = 0, inViewSum = 0, aloftFrames = 0, runs = 0, twoVortex = 0, aloftDealt = 0, badRate = 0;
        string bad = "";
        var kinds = new HashSet<string>();
        for (int run = 0; run < Runs; run++)
        {
            StormDirector.SeedOverride = 401 + run * 13 + v;
            var wb = Fresh(v, 2f);
            var d = Director(wb);
            if (d == null || d.Props == null || d.AloftPool == null || d.Planner == null || d.Planner.Mask == null) { Check("v" + v + ": Storm director with its prop pools and a ground mask", false); continue; }
            runs++;
            var set = wb.Current;
            var seen = new Dictionary<BackdropPiece, double>();
            var aloftSeen = new HashSet<BackdropPiece>();
            float groundRate = set.Spec.Rate("ground"), aloftRate = set.Spec.Rate(StormPropsTuning.AloftLayer);
            for (float t = 0f; t < Seconds; t += Dt)
            {
                if (t > 0f) wb.Step(Dt);
                frames++;
                int inView = 0, vort = 0;
                foreach (var pool in new[] { d.Props, d.Cells })
                    foreach (var p in pool.items)
                    {
                        if (!p.active) continue;
                        if (InView(p, set.HalfWidth, set.HalfHeight)) { inView++; if (p.kind == StormPropsTuning.Vortex) vort++; }
                        if (Mathf.Abs((float)(p.y + d.Planner.Travel - p.gy)) > 1e-3f) drift++;
                        double was;
                        if (seen.TryGetValue(p, out was) && System.Math.Abs(was - p.gy) < 1e-6) continue;
                        seen[p] = p.gy;
                        dealt++;
                        int k = p.kind;
                        if (k < 0 || k >= StormPropsTuning.Ground.Length) { wrong++; continue; }
                        kinds.Add(StormPropsTuning.Ground[k]);
                        if (!d.Planner.Accepts(StormPropsTuning.GroundRule(k), p.x, p.gy, p.size)) { wrong++; if (bad.Length < 200) bad += StormPropsTuning.Ground[k] + "@" + d.Planner.ClassAt(p.x, p.gy) + " "; }
                        if (StormPropsTuning.Claims(k)) overlaps += Overlaps(d, p);
                    }
                if (vort > 1) twoVortex++;
                int up = 0;
                foreach (var p in d.AloftPool.items)
                {
                    if (!p.active) continue;
                    if (aloftSeen.Add(p) || p.age < Dt * 1.5f) aloftDealt++;
                    if (InView(p, set.HalfWidth, set.HalfHeight)) up++;
                    if (!Mathf.Approximately(p.rate, aloftRate)) badRate++;
                }
                if (up > 0) aloftFrames++;
                maxAloft = Mathf.Max(maxAloft, up);
                maxInView = Mathf.Max(maxInView, inView);
                inViewSum += inView;
            }
            if (aloftRate / groundRate < 1.3f || aloftRate / groundRate > 1.6f) badRate++;
            Object.DestroyImmediate(wb.gameObject);
        }
        StormDirector.SeedOverride = 0;
        float mean = frames > 0 ? inViewSum / (float)frames : 0f, aloftShare = frames > 0 ? aloftFrames / (float)frames : 1f;
        Check("v" + v + ": " + runs + " runs dealt " + dealt + " ground props (" + string.Join(", ", kinds) + "), none on ground its rule forbids (" + wrong + " " + bad + ")",
              runs == Runs && dealt >= Runs * 3 && wrong == 0);
        Check("v" + v + ": no ground prop overlaps a landmark, a dock or another prop (" + overlaps + "), never two vortices in view (" + twoVortex + ")", overlaps == 0 && twoVortex == 0);
        Check("v" + v + ": every ground prop stays pinned to its spot of the ground (" + drift + " frames drifted)", drift == 0);
        Check("v" + v + ": ground props are accents -- at most 8 in view (moorings come in pairs; max " + maxInView + "), " + mean.ToString("F2") + " on average (<= 4.5)", maxInView <= 8 && mean <= 4.5f);
        Check("v" + v + ": aloft -- at most one in view (max " + maxAloft + "), in view " + (aloftShare * 100f).ToString("F0") + "% of the time (often none: <= 60%), " +
              aloftDealt + " came by, at the aloft rate (" + badRate + " off)", maxAloft <= 1 && aloftShare <= .6f && aloftDealt >= 1 && badRate == 0);
    }

    // Prop `p`'s footprint against every other ground piece's (landmarks, docks: their planner core; props: theirs).
    static int Overlaps(StormDirector d, BackdropPiece p)
    {
        int n = 0;
        float px = GroundPlanner.HalfX(new PieceRule(GroundClass.Any, 0f, p.core), p.size), py = px * .8f;
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

    // ---- the SIMPLE OVERLAP TEST (user rule 2026-11-28: props stand where they make sense; nothing moving passes through or touches
    // one). A live run, every frame, everything in view: no two pinned pieces' boxes meet (a prop's keep-out box against any
    // landmark / site / prop footprint), and no moving piece -- aloft prop, fork bolt / arc, a strike's bolt (but on its own mast),
    // tumbling debris -- touches a pinned piece. The vapour strip is drawn under the pinned plane. ----

    static bool InViewY(float y, float h, float halfH) { return y + h > -halfH && y - h < halfH; }

    static void Clear(int v)
    {
        const float Seconds = 120f;
        int pinnedHits = 0, movingHits = 0, aloftSeen = 0, boltsSeen = 0, debrisSeen = 0, frames = 0;
        string bad = "";
        StormDirector.SeedOverride = 777 + v;
        var wb = Fresh(v, .6f);
        var d = Director(wb);
        if (d == null || d.Props == null) { Check("v" + v + ": Storm director for the overlap test", false); if (wb != null) Object.DestroyImmediate(wb.gameObject); return; }
        float hh = wb.Current.HalfHeight;
        var pinned = new List<BackdropPool>(d.Landmarks);
        if (!pinned.Contains(d.Sites)) pinned.Add(d.Sites);
        pinned.Add(d.Props); pinned.Add(d.Cells);
        var aloftFirst = new HashSet<BackdropPiece>();
        for (float t = 0f; t < Seconds; t += Dt)
        {
            if (t > 0f) wb.Step(Dt);
            frames++;
            // pinned vs pinned: every prop's keep-out box against every other pinned piece's footprint
            foreach (var pp in new[] { d.Props, d.Cells })
                foreach (var a in pp.items)
                {
                    if (!a.active || !InViewY(a.y, a.size, hh)) continue;
                    float ha = d.PinnedHalf(a, true);
                    foreach (var pool in pinned)
                    {
                        bool prop = pool == d.Props || pool == d.Cells;
                        foreach (var b in pool.items)
                        {
                            if (!b.active || b == a || b.sr.sprite == null) continue;
                            float hb = prop ? GroundPlanner.HalfX(new PieceRule(GroundClass.Any, 0f, b.core), b.size) : d.PinnedHalf(b, false);
                            if (Mathf.Abs(a.x - b.x) < (ha + hb) * .97f && System.Math.Abs(a.gy - b.gy) < (ha + hb) * .8f * .97f)
                            { pinnedHits++; if (bad.Length < 240) bad += StormPropsTuning.Ground[a.kind] + "x" + b.sr.sprite.name + " "; }
                        }
                    }
                }
            // moving vs pinned (boxes shrunk 8 %: bob, sway and a frame's step)
            foreach (var p in d.AloftPool.items)
            {
                if (!p.active || !InViewY(p.y, p.size, hh)) continue;
                if (aloftFirst.Add(p)) aloftSeen++;
                float h = p.size * StormPropsTuning.BodyShare * .92f;
                if (Touches(d, pinned, p.x, p.y, h, h, null)) { movingHits++; if (bad.Length < 240) bad += "aloft:" + StormPropsTuning.Aloft[p.kind] + " "; }
            }
            foreach (var p in d.Bolts.items)
            {
                if (!p.active || !InViewY(p.y, p.size * .5f, hh)) continue;
                boltsSeen++;
                BackdropPiece mast = p.parent != null && p.parent.active ? p.parent : null;
                if (Touches(d, pinned, p.x, p.y, p.size * StormPropsTuning.BoltHalfX * .92f, p.size * StormPropsTuning.BoltHalfY * .92f, mast))
                { movingHits++; if (bad.Length < 240) bad += (mast != null ? "strike " : "bolt ") + (p.sr.sprite != null ? p.sr.sprite.name : "") + " "; }
            }
            foreach (var p in d.Debris.items)
            {
                if (!p.active || !InViewY(p.y, p.size, hh)) continue;
                debrisSeen++;
                float h = p.size * StormPropsTuning.BodyShare * .92f;
                if (Touches(d, pinned, p.x, p.y, h, h, null)) { movingHits++; if (bad.Length < 240) bad += "debris "; }
            }
        }
        Object.DestroyImmediate(wb.gameObject);
        StormDirector.SeedOverride = 0;
        Check("v" + v + " overlap test, " + frames + " frames: no pinned piece meets a prop's keep-out box (" + pinnedHits + ") and nothing moving touches a pinned piece (" +
              movingHits + "): " + aloftSeen + " aloft, " + boltsSeen + " bolt frames, " + debrisSeen + " debris frames in view " + bad,
              pinnedHits == 0 && movingHits == 0);
        Check("v" + v + ": the weather still happens around the props (debris tumbles by: " + debrisSeen + " frames)", debrisSeen > 0);
        var spec = BackdropCatalog.For("Storm");
        Check("the vapour strip runs UNDER the pinned plane (flow drawn before ground)",
              System.Array.IndexOf(spec.layers, spec.Find("flow")) < System.Array.IndexOf(spec.layers, spec.Find("ground")));
    }

    static bool Touches(StormDirector d, List<BackdropPool> pinned, float x, float y, float hx, float hy, BackdropPiece except)
    {
        foreach (var pool in pinned)
        {
            bool prop = pool == d.Props || pool == d.Cells;
            foreach (var b in pool.items)
            {
                if (!b.active || b == except) continue;
                float hb = d.PinnedHalf(b, prop);
                if (Mathf.Abs(b.x - x) < hx + hb && Mathf.Abs(b.y - y) < hy + hb * .8f) return true;
            }
        }
        return false;
    }

    // every mooring kind placed on its own comes with its companion beside it (or not at all)
    static void Moorings()
    {
        foreach (int k in new[] { StormPropsTuning.Tug, StormPropsTuning.Skimmer, StormPropsTuning.Buoys, StormPropsTuning.Rods })
        {
            string name = StormPropsTuning.Ground[k];
            int placed = 0, lonely = 0;
            for (int seed = 0; seed < 4; seed++)
            {
                StormDirector.SeedOverride = 61 + seed * 7 + k;
                var wb = Fresh(1 + seed % 4, .25f);
                var d = Director(wb);
                if (d == null || d.Props == null) { if (wb != null) Object.DestroyImmediate(wb.gameObject); continue; }
                d.ClearProps();
                d.HoldProps(1e6f, 1e6f);
                double tr = d.Planner.Travel;
                float hh = wb.Current.HalfHeight;
                for (int a = 0; a < 4; a++) d.SpawnProp(k, tr - hh * .6f, tr + hh * .6f, -1, 400);
                foreach (var p in d.Props.items)
                {
                    if (!p.active || p.kind != k) continue;
                    placed++;
                    bool near = false;
                    foreach (var q in d.Props.items)
                    {
                        if (!q.active || q == p) continue;
                        bool fits = k == StormPropsTuning.Buoys ? (q.kind == StormPropsTuning.Tug || q.kind == StormPropsTuning.Skimmer)
                                  : q.kind == StormPropsTuning.Companion(k, true);
                        if (!fits) continue;
                        float gap = Mathf.Abs(p.x - q.x) - (StormDirector.KeepX(p.kind, p.size) + StormDirector.KeepX(q.kind, q.size));
                        if (gap <= StormPropsTuning.MoorReach + .05f && System.Math.Abs(p.gy - q.gy) < (p.size + q.size)) near = true;
                    }
                    if (!near) lonely++;
                }
                Object.DestroyImmediate(wb.gameObject);
            }
            StormDirector.SeedOverride = 0;
            Check(name + ": placed only beside its companion (" + placed + " placed, " + lonely + " alone)", placed > 0 && lonely == 0);
        }
    }

    // ---- each ground kind forced into view: its own strip, every cell, a still frame at dt 0, no allocation ----

    static void Forced()
    {
        for (int k = 0; k < StormPropsTuning.Ground.Length; k++)
        {
            string name = StormPropsTuning.Ground[k];
            WorldBackdrop wb = null;
            StormDirector d = null;
            BackdropPiece p = null;
            int v = 0;
            foreach (int vv in new[] { 2, 1, 4, 3 })
            {
                StormDirector.SeedOverride = 91 + k;
                wb = Fresh(v = vv, .25f);
                d = Director(wb);
                if (d == null || d.Props == null) break;
                d.ClearProps();
                d.HoldProps(1e6f, 1e6f);
                float hh = wb.Current.HalfHeight;
                double tr = d.Planner.Travel;
                for (int a = 0; a < 6 && p == null; a++)
                    if (d.SpawnProp(k, tr - hh * .5f, tr + hh * .6f, -1, 800))
                        foreach (var pool in new[] { d.Props, d.Cells })
                            foreach (var q in pool.items) if (q.active && q.kind == k) p = q;
                if (p != null) break;
                Object.DestroyImmediate(wb.gameObject);
                wb = null;
            }
            Check(name + ": placed in view (v" + v + ", on " + (p != null ? d.Planner.ClassAt(p.x, p.gy).ToString() : "-") + ")", p != null);
            if (p == null) { if (wb != null) Object.DestroyImmediate(wb.gameObject); continue; }
            var framesSeen = new HashSet<int>();
            bool wrongSheet = false;
            int variant = -1;
            float span = StormPropsTuning.CycleMs(StormPropsTuning.GroundMs[k]) / 1000f + .5f;
            for (float t = 0f; t < span; t += 1f / 60f)
            {
                wb.Step(1f / 60f);
                if (!p.active) break;
                string kind; int vr, f;
                if (!Parse(p.sr.sprite != null ? p.sr.sprite.name : null, out kind, out vr, out f) || kind != name || (variant >= 0 && vr != variant)) wrongSheet = true;
                variant = vr;
                framesSeen.Add(f);
            }
            Check(name + ": shows only its own livery's cells and loops through all of them (" + framesSeen.Count + "/8)", !wrongSheet && framesSeen.Count == 8);

            var sprite = p.sr.sprite;
            float age = p.age, y = p.y;
            var rot = p.body.localRotation;
            Color col = p.sr.color;
            for (int i = 0; i < 30; i++) wb.Step(0f);
            Check(name + ": dt 0 (paused) is a still frame -- cell, spot, spin, light",
                  p.sr.sprite == sprite && Mathf.Approximately(p.age, age) && Mathf.Approximately(p.y, y) && p.body.localRotation == rot && p.sr.color == col);
            if (k == 0)
            {
                long bytes = TestHarness.AllocatedBytes(() => { for (int i = 0; i < 60; i++) wb.Step(1f / 60f); });
                Check("no allocation per frame with props live (" + bytes + " B over 60 steps, <= 256)", bytes <= 256);
            }
            Object.DestroyImmediate(wb.gameObject);
        }
        StormDirector.SeedOverride = 0;
    }

    // ---- each aloft kind: size, parallax, moored (vehicles) or drifting (jellies), a still frame at dt 0, no pink as drawn ----

    static void Aloft()
    {
        for (int k = 0; k < StormPropsTuning.Aloft.Length; k++)
        {
            string name = StormPropsTuning.Aloft[k];
            StormDirector.SeedOverride = 133 + k;
            var wb = Fresh(1, 1f);
            var d = Director(wb);
            if (d == null || d.AloftPool == null) { Check(name + ": Storm director with an aloft pool", false); if (wb != null) Object.DestroyImmediate(wb.gameObject); continue; }
            d.ClearProps();
            d.HoldProps(1e6f, 1e6f);
            bool placed = d.SpawnAloft(k, 0f, wb.Current.HalfHeight * .4f);
            BackdropPiece p = null;
            foreach (var q in d.AloftPool.items) if (q.active) p = q;
            Check(name + ": spawns aloft", placed && p != null && p.kind == k);
            if (p == null) { Object.DestroyImmediate(wb.gameObject); continue; }
            float ground = BackdropCatalog.For("Storm").Rate("ground");
            float ratio = p.size / (StormPropsTuning.AloftBasePx[k] / StormPropsTuning.PxPerUnit);
            Check(name + ": drawn " + ratio.ToString("F2") + "x its ground size (1.6 - 2.5x), at " + (p.rate / ground).ToString("F2") + "x the ground's rate (1.3 - 1.6x), not pinned",
                  ratio >= 1.5f && ratio <= 2.6f && p.rate / ground >= 1.3f && p.rate / ground <= 1.6f && p.footprint < 0);
            // its motion over 3 s against what the parallax alone would do
            float x0 = p.x, y0 = p.y, by0 = p.body.localPosition.y, swayMax = 0f;
            float v = WorldBackdrop.ScrollVelocity(moveBackGround.speed) * WorldBackdrop.ScrollBoost;
            float bobMax = 0f;
            var framesSeen = new HashSet<int>();
            for (int i = 0; i < 180 && p.active; i++)
            {
                wb.Step(1f / 60f);
                bobMax = Mathf.Max(bobMax, Mathf.Abs(p.body.localPosition.y - by0));
                swayMax = Mathf.Max(swayMax, Mathf.Abs(Mathf.DeltaAngle(0f, p.body.localEulerAngles.z)));
                string kind; int vr, f;
                if (Parse(p.sr.sprite != null ? p.sr.sprite.name : null, out kind, out vr, out f) && kind == name) framesSeen.Add(f);
            }
            float parallax = -p.rate * v * 3f, own = (p.y - y0) - parallax, side = p.x - x0;
            bool jelly = StormPropsTuning.Drifts(k);
            bool moves = jelly
                ? bobMax > 0f || Mathf.Abs(side) > .001f
                : Mathf.Abs(own) < .05f && Mathf.Abs(side) < .05f && (bobMax > 0f || swayMax > .1f);
            Check(name + (jelly ? ": drifts slowly" : ": MOORED -- rides the parallax only, bobs / sways on its lines") + " (" + side.ToString("F3") + " u across, " + own.ToString("F3") +
                  " u beyond the parallax, bob " + bobMax.ToString("F3") + ", sway " + swayMax.ToString("F1") + " deg) and animates (" + framesSeen.Count + " cells)",
                  moves && framesSeen.Count >= 6);
            Check(name + ": hazed (tint " + p.sr.color + ") -- never brighter than the haze, never pink",
                  p.sr.color.r <= StormPropsTuning.AloftTint.r + 1e-3f && p.sr.color.a <= StormPropsTuning.AloftTint.a + 1e-3f && !WorldCue.IsPink(p.sr.color));
            var sprite = p.sr.sprite;
            Vector3 pos = p.root.localPosition, body = p.body.localPosition;
            for (int i = 0; i < 30; i++) wb.Step(0f);
            var rot = p.body.localRotation;
            for (int i = 0; i < 30; i++) wb.Step(0f);
            Check(name + ": dt 0 (paused) is a still frame -- cell, spot, bob, sway", p.sr.sprite == sprite && p.root.localPosition == pos && p.body.localPosition == body && p.body.localRotation == rot);
            Object.DestroyImmediate(wb.gameObject);
        }
        StormDirector.SeedOverride = 0;
    }
}
