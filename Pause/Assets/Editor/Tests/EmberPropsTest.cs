using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Ember's living props and mountains (EmberProps.cs, r1 2026-11-28):
//   * TABLE: every kind is one data row; each layout atlas (v<n>/props) carries its strips -- 8 frames, four variants of every kind;
//     keep-out = .42 x px + 22 (1080 px screen); the placement classes (plant, foundry, bunker flat; rig, vents, starship open; quarry,
//     domes, columns, springs rocky; ore train, stilts, crawler IN THE LAVA; the mountains rocky or open);
//   * ODDS: on every layout's real mask and context grid every kind has real room (a kind that almost never appears is a bug);
//   * ART: no pink texel (WorldCue.IsPink), the lights' frame-to-frame brightness change small, the props' brightness under the landmark
//     ceiling as drawn (mean / p90), and every prop BLENDS into its ground: its mean luminance and saturation within bounds of the
//     tile's own, and a feathered base (no hard cut-out edge along its foot);
//   * LIVE: each prop on its context (flat / rocky / lava / open), never on a painted feature, no two keep-outs overlapping (they may
//     touch), PINNED (y + travel = gy, the mid tile's rate, never mirrored or turned), each copy's variant never the previous copy's of
//     its kind; NOTHING MOVING TOUCHES A PROP: no eruption (its whole cell: the plume's reach), ash pall (its whole drift box) or
//     launch site inside a keep-out, ever; the starship's scar is its own keep-out;
//   * FORCED OVERLAP (as DesertPropsTest / FrostPropsTest): a prop cannot be planted into another's keep-out or an eruption's cell, an
//     eruption cannot be dealt into a prop, the scar's boxes are kept out of;
//   * dt 0 is a still frame; no allocation per frame.
public static class EmberPropsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[EPR] PASS  " : "[EPR] FAIL  ") + what);
        if (!ok) fails++;
    }
    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 20f;
    const string ArtDir = "Assets/Art/Backgrounds/Resources/Worlds/Ember/Backdrop3/";
    static BackdropVariants Sel => BackdropVariants.For("Ember");

    // BLEND bounds: a prop (alpha-weighted mean) against its own layout's tile
    public const float BlendLumRatioMax = 3.6f, BlendLumAdd = .06f, BlendChromaAdd = .22f, BaseEdgeMax = .045f;
    public const float PropMeanLumMax = .36f, PropP90Max = .75f, BlendLumMin = .07f, BlendSatMax = .70f;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        float savedSpeed = moveBackGround.speed;
        try
        {
            Table();
            for (int v = 1; v <= 4; v++) Art(v);
            for (int v = 1; v <= 4; v++) Live(v);
            Forced();
            Scar();
            Still();
        }
        finally
        {
            EmberDirector.SeedOverride = 0;
            moveBackGround.speed = savedSpeed;
            Time.timeScale = 1f;
            Sel.Reset();
            EliteSystem.Clear();
            if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
        }
        Debug.Log("[EPR] failures: " + fails);
        return fails;
    }

    static WorldBackdrop Fresh(int variant, float speed, bool reset = true)
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        Time.timeScale = 1f;
        moveBackGround.speed = speed;
        if (reset) { Sel.Reset(); Sel.Seed(11); }
        Sel.Force = variant;
        var cam = Camera.main;
        cam.orthographic = true;
        cam.aspect = 1080f / 2400f;
        cam.orthographicSize = CameraFit.ComputeSize(5f, CameraFit.GameplayHalfWidth, 1080, 2400);
        var wb = WorldBackdrop.Create("Ember");
        wb.Show("Ember", false);
        return wb;
    }

    static EmberDirector Director(WorldBackdrop wb) { return wb.Current != null ? wb.Current.Director as EmberDirector : null; }

    static void Run(WorldBackdrop wb, float seconds, System.Action each = null)
    {
        for (float t = 0f; t < seconds; t += Dt) { wb.Step(Dt); if (each != null) each(); }
    }

    static BackdropAtlas Atlas(int v)
    {
        string f = BackdropCatalog.TileFolder("Ember", v);
        return new BackdropAtlas(Resources.Load<Texture2D>(f + EmberPropsTuning.Atlas), Resources.Load<TextAsset>(f + EmberPropsTuning.Atlas));
    }

    static float RelLum(Color c)
    {
        return 0.2126f * Linear(c.r) + 0.7152f * Linear(c.g) + 0.0722f * Linear(c.b);
    }
    static float Linear(float v) { return v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f); }

    // ---- the table -----------------------------------------------------------------------------------------------------------

    static void Table()
    {
        var ks = EmberPropsTuning.Kinds;
        bool names = true;
        var seen = new HashSet<string>();
        foreach (var k in ks) names &= seen.Add(k.name) && k.px > 0f && k.perThree > 0f;
        Check("the props table: " + ks.Length + " kinds, one data row each (unique names, sizes, rates)", ks.Length == 16 && names);
        Check("the mountains: volcano 350, caldera 360, spires 330 px, rocky or open ground, .6 - .7 per 3 screens together; the starship 330 px, rare",
              ks[EmberPropsTuning.Volcano].px == 350f && ks[EmberPropsTuning.Caldera].px == 360f && ks[EmberPropsTuning.Spires].px == 330f &&
              ks[EmberPropsTuning.Volcano].mountain && ks[EmberPropsTuning.Spires].on == EmberPropsTuning.Place.Mountain &&
              EmberPropsTuning.TotalPerThree(true) >= .6f - 1e-4f && EmberPropsTuning.TotalPerThree(true) <= .7f + 1e-4f &&
              ks[EmberPropsTuning.Starship].px == 330f && ks[EmberPropsTuning.Starship].perThree <= .6f);
        float hw = 2.7f;
        Check("drawn at the altitude: the approved .41 px x " + EmberPropsTuning.AltitudeK.ToString("F3") + " (GroundZoom " + EmberTuning.GroundZoom +
              "), the copies per screen thinned by the area (x" + EmberPropsTuning.DensityK.ToString("F3") + ")",
              Mathf.Abs(EmberPropsTuning.AltitudeK - .49f / .41f) < 1e-4f && Mathf.Abs(EmberPropsTuning.DrawnPx(EmberPropsTuning.Geoplant) - 190f * .49f / .41f) < 1e-3f &&
              Mathf.Abs(EmberPropsTuning.DensityK - (.41f / .49f) * (.41f / .49f)) < 1e-4f);
        Check("keep-out = .42 x drawn px + 22 on a 1080 px screen (geoplant " + EmberPropsTuning.Keep(EmberPropsTuning.Geoplant, hw).ToString("F3") + " units)",
              Mathf.Abs(EmberPropsTuning.Keep(EmberPropsTuning.Geoplant, hw) - (190f * .49f / .41f * .42f + 22f) / 1080f * 2f * hw) < 1e-4f);
        var P = EmberPropsTuning.Place.Flat;
        Check("placement rules: plant, foundry, bunker on flat crust; rig, vents, starship on open crust; quarry, domes, columns, springs on rocky ground; " +
              "ore train, stilts, crawler IN the lava (GroundClass.Water)",
              ks[EmberPropsTuning.Geoplant].on == P && ks[EmberPropsTuning.Foundry].on == P && ks[EmberPropsTuning.Bunker].on == P &&
              ks[EmberPropsTuning.Miningrig].on == EmberPropsTuning.Place.Open && ks[EmberPropsTuning.Vents].on == EmberPropsTuning.Place.Open && ks[EmberPropsTuning.Starship].on == EmberPropsTuning.Place.Open &&
              ks[EmberPropsTuning.Quarry].on == EmberPropsTuning.Place.Rocky && ks[EmberPropsTuning.Domes].on == EmberPropsTuning.Place.Rocky &&
              ks[EmberPropsTuning.Columns].on == EmberPropsTuning.Place.Rocky && ks[EmberPropsTuning.Springs].on == EmberPropsTuning.Place.Rocky &&
              EmberPropsTuning.Lava(EmberPropsTuning.Orehauler) && EmberPropsTuning.Lava(EmberPropsTuning.Stilts) && EmberPropsTuning.Lava(EmberPropsTuning.Crawler) &&
              EmberPropsTuning.On(EmberPropsTuning.Place.Lava) == GroundClass.Water);
        for (int v = 1; v <= 4; v++)
        {
            var a = Atlas(v);
            bool strips = true;
            string bad = "";
            for (int k = 0; k < ks.Length; k++)
            {
                int variants = 0;
                for (int i = 0; i < 16; i++)
                {
                    var f = a.Frames(EmberPropsTuning.Strip(k, i));
                    if (f.Length == 0) break;
                    variants++;
                    if (f.Length != 8 && !(ks[k].mountain && f.Length == 1)) { strips = false; bad += ks[k].name + "_" + i + " "; }
                }
                // (a layout without a lava worth the name -- v3's threads -- carries no lava kinds, as Desert's skiffs need a shore)
                bool want = !EmberPropsTuning.Lava(k) || variants > 0;
                if (want ? variants != 4 : false) { strips = false; bad += ks[k].name + " x" + variants + " "; }
            }
            Check("v" + v + " props atlas: every kind's four variants, 8 frames each (the lava kinds only where the layout has lava) " + bad, a.Count > 0 && strips);
            a.Destroy();
        }
        // the scar decals: one per starship livery
        var sa = new BackdropAtlas(Resources.Load<Texture2D>("Worlds/Ember/Backdrop3/" + EmberPropsTuning.ScarAtlas), Resources.Load<TextAsset>("Worlds/Ember/Backdrop3/" + EmberPropsTuning.ScarAtlas));
        bool cells = true;
        foreach (string c in EmberPropsTuning.ScarCells) cells &= sa.Has(c);
        Check("the starship's scar atlas carries scar_0 .. scar_3 (one per livery), four keep-out boxes each, shrinking along the furrow",
              cells && EmberPropsTuning.ScarCells.Length == 4 && EmberPropsTuning.ScarBoxes.Length == 4 && EmberPropsTuning.ScarBoxes[0].Length == 4 &&
              EmberPropsTuning.ScarBoxes[0][0].z > EmberPropsTuning.ScarBoxes[0][3].z);
        sa.Destroy();
    }

    // ---- odds: real room on the real masks --------------------------------------------------------------------------------------

    static void Odds(EmberDirector d, int v)
    {
        float hw = d.ViewHalfWidth;
        var pl = d.Planner;
        float h = pl.Tile.TileHeight;
        double g0 = pl.Travel;
        var ks = EmberPropsTuning.Kinds;
        string low = "", line = "";
        int worst = 0; float worstOdds = 1f;
        for (int k = 0; k < ks.Length; k++)
        {
            if (!d.HasPropArt(k)) { line += ks[k].name + " (not in this layout) "; continue; }
            int ok = 0, n = 0;
            for (double gy = g0; gy < g0 + h; gy += .2)
                for (float x = -2.4f; x <= 2.4f; x += .2f) { n++; if (EmberPropsTuning.OnContext(pl, k, x, gy, hw)) ok++; }
            float odds = ok / (float)Mathf.Max(1, n);
            line += ks[k].name + " " + (odds * 100f).ToString("F1") + "% ";
            if (odds < worstOdds) { worstOdds = odds; worst = k; }
            if (odds < .002f) low += ks[k].name + " ";
        }
        Debug.Log("[EPR] v" + v + " odds of a centre on its context: " + line);
        Check("v" + v + ": every kind has real room on this layout's mask and context grid (the least: " + ks[worst].name + " " + (worstOdds * 100f).ToString("F2") + "%) " + low,
              low.Length == 0);
    }

    // ---- the art ------------------------------------------------------------------------------------------------------------

    [System.Serializable] class JRect { public string n; public int x, y, w, h; }
    [System.Serializable] class JAtlas { public JRect[] sprites; }

    static void Art(int v)
    {
        string png = ArtDir + "v" + v + "/" + EmberPropsTuning.Atlas + ".png";
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(png));
        var px = tex.GetPixels();
        int W = tex.width;
        Object.DestroyImmediate(tex);
        var json = JsonUtility.FromJson<JAtlas>(File.ReadAllText(Path.ChangeExtension(png, ".json")));
        int pink = 0;
        foreach (var c in px) if (c.a > .1f && WorldCue.IsPink(c)) pink++;
        // the layout's ground (its mean luminance and chroma, as the tile is drawn)
        var gt = new Texture2D(2, 2);
        gt.LoadImage(File.ReadAllBytes(ArtDir + "v" + v + "/mid.png"));
        var gp = gt.GetPixels();
        Object.DestroyImmediate(gt);
        double gl = 0, gc = 0; int gn = 0;
        for (int i = 0; i < gp.Length; i += 5)
        {
            var c = gp[i];
            gl += .2126f * c.r + .7152f * c.g + .0722f * c.b; gc += Mathf.Max(c.r, Mathf.Max(c.g, c.b)) - Mathf.Min(c.r, Mathf.Min(c.g, c.b)); gn++;
        }
        float groundLum = (float)(gl / gn), groundChroma = (float)(gc / gn);

        var strips = new Dictionary<string, List<JRect>>();
        foreach (var r in json.sprites)
        {
            int cut = r.n.LastIndexOf('_');
            string key = r.n.Substring(0, cut);
            List<JRect> l;
            if (!strips.TryGetValue(key, out l)) strips[key] = l = new List<JRect>();
            l.Add(r);
        }
        float worstStep = 0f, worstEdge = 0f;
        string worst = "", worstEdgeAt = "", dim = "", harsh = "", loud = "";
        foreach (var kv in strips)
        {
            var means = new List<float>();
            foreach (var r in kv.Value)
            {
                double s = 0; int n = 0;
                for (int y = r.y; y < r.y + r.h; y++)
                    for (int x = r.x; x < r.x + r.w; x++) { var c = px[y * W + x]; s += (.2126f * c.r + .7152f * c.g + .0722f * c.b) * c.a; n++; }
                means.Add((float)(s / Mathf.Max(1, n)));
            }
            for (int i = 0; i < means.Count; i++)
            {
                float step = Mathf.Abs(means[i] - means[(i + 1) % means.Count]) / Mathf.Max(.02f, means[i]);
                if (step > worstStep) { worstStep = step; worst = kv.Key; }
            }
            // the first frame of the strip: luminance, p90, chroma, and the hardness of its foot
            var f0 = kv.Value[0];
            foreach (var r in kv.Value) if (r.n.EndsWith("_00")) f0 = r;
            var lums = new List<float>();
            double chroma = 0, wsum = 0, lsum = 0, satSum = 0, baseL = 0, baseN = 0;
            int band = Mathf.Max(1, f0.h / 10);
            for (int y = f0.y; y < f0.y + f0.h; y++)
                for (int x = f0.x; x < f0.x + f0.w; x++)
                {
                    var c = px[y * W + x];
                    if (c.a <= .5f) continue;
                    float l = .2126f * c.r + .7152f * c.g + .0722f * c.b;
                    float mx0 = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), mn0 = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                    lums.Add(l); lsum += l; wsum += 1; chroma += mx0 - mn0; satSum += mx0 > 0f ? (mx0 - mn0) / mx0 : 0f;
                    if (y < f0.y + band) { baseL += l; baseN += 1; }
                }
            if (lums.Count == 0) continue;
            lums.Sort();
            float mean = (float)(lsum / wsum), p90 = lums[(int)(lums.Count * .9f)], ch = (float)(chroma / wsum);
            if (mean * EmberPropsTuning.PropTint.r > PropMeanLumMax || p90 * EmberPropsTuning.PropTint.r > PropP90Max) dim += kv.Key + " ";
            // BLEND: not much brighter / more saturated than the paint around it
            float sat = (float)(satSum / wsum), baseMean = baseN > 0 ? (float)(baseL / baseN) : mean;
            // (the user's rule: not brighter / duller / more saturated than the paint around it, a scorched (darker) base)
            if (mean > groundLum * BlendLumRatioMax + BlendLumAdd || ch > groundChroma + BlendChromaAdd || mean < BlendLumMin || mean > PropMeanLumMax || sat > BlendSatMax || baseMean > mean + .05f) loud += kv.Key + "(lum " + mean.ToString("F2") + " sat " + sat.ToString("F2") + " base " + baseMean.ToString("F2") + ") ";
            // a feathered foot: along the lowest 30% of the opaque rows, the share of opaque pixels that border full transparency
            int top = f0.y + f0.h, bottom = f0.y;
            for (int y = f0.y; y < f0.y + f0.h; y++)
                for (int x = f0.x; x < f0.x + f0.w; x++) if (px[y * W + x].a > .5f) { top = Mathf.Min(top, y); bottom = Mathf.Max(bottom, y); }
            // (texture rows run bottom-up: the foot is the LOW y rows)
            int lo = f0.y, hi = f0.y + f0.h;
            int footTop = 0, footBottom = 0; bool any = false;
            for (int y = lo; y < hi; y++)
                for (int x = f0.x; x < f0.x + f0.w; x++) if (px[y * W + x].a > .5f) { if (!any) { footBottom = y; any = true; } footTop = y; }
            if (!any) continue;
            int footEnd = footBottom + Mathf.Max(2, (footTop - footBottom) * 3 / 10);
            int hard = 0, body = 0;
            for (int y = footBottom; y <= footEnd; y++)
                for (int x = f0.x; x < f0.x + f0.w; x++)
                {
                    if (px[y * W + x].a <= .9f) continue;
                    body++;
                    bool edge = false;
                    for (int dy = -1; dy <= 1 && !edge; dy++)
                        for (int dx = -1; dx <= 1 && !edge; dx++)
                        {
                            if ((dx == 0) == (dy == 0)) continue;
                            int xx = x + dx, yy = y + dy;
                            if (xx < f0.x || xx >= f0.x + f0.w || yy < f0.y || yy >= f0.y + f0.h || px[yy * W + xx].a < .1f) edge = true;
                        }
                    if (edge) hard++;
                }
            float hardness = body > 0 ? hard / (float)body : 0f;
            if (hardness > worstEdge) { worstEdge = hardness; worstEdgeAt = kv.Key; }
            if (hardness > BaseEdgeMax) harsh += kv.Key + " ";
        }
        Check("v" + v + " props atlas: no pink texel (" + pink + ")", pink == 0);
        Check("v" + v + " the lights blink gently: no strip's mean brightness steps more than 25% between frames (worst " + worst + " " + worstStep.ToString("P0") + ")",
              worstStep <= .25f);
        Check("v" + v + " props drawn under the landmark ceiling (mean <= " + PropMeanLumMax + ", p90 <= " + PropP90Max + " x tint " + EmberPropsTuning.PropTint.r + ") and dimmer than painted (tint < 1): " + dim,
              dim.Length == 0 && EmberPropsTuning.PropTint.r < 1f);
        Check("v" + v + " BLEND: no prop much brighter or more saturated than its ground (tile mean " + groundLum.ToString("F3") + ", chroma " + groundChroma.ToString("F3") +
              "; bounds x" + BlendLumRatioMax + " + " + BlendLumAdd + ", + " + BlendChromaAdd + ", lum " + BlendLumMin + " .. " + PropMeanLumMax + ", saturation <= " + BlendSatMax + ", a base no brighter than the body): " + loud, loud.Length == 0);
        Check("v" + v + " BLEND: every prop's foot is feathered into the ground, no hard cut-out edge (worst " + worstEdgeAt + " " + worstEdge.ToString("F3") + " <= " + BaseEdgeMax + "): " + harsh,
              harsh.Length == 0);
    }

    // ---- live ---------------------------------------------------------------------------------------------------------------

    static void Live(int v)
    {
        EmberDirector.SeedOverride = 4100 + v * 13;
        var wb = Fresh(v, 1.2f);
        var d = Director(wb);
        Check("v" + v + " director with its props atlas", d != null && d.HasPropArt(EmberPropsTuning.Geoplant));
        if (d == null) return;
        Odds(d, v);
        float hw = d.ViewHalfWidth;
        int seen = 0, offContext = 0, onArt = 0, keepOverlap = 0, eruptIn = 0, pallIn = 0, siteIn = 0, notPinned = 0, repeat = 0, mountains = 0, scarSeen = 0, scarWrong = 0;
        var kinds = new HashSet<int>();
        var known = new HashSet<BackdropPiece>();
        var lastVariant = new Dictionary<int, int>();
        string bad = "";
        float rate = wb.Current.Spec.Rate("mid");
        var all = new List<BackdropPiece>();
        System.Action audit = () =>
        {
            all.Clear();
            foreach (var p in d.Mountains.items) if (p.active) all.Add(p);
            foreach (var p in d.Props.items) if (p.active) all.Add(p);
            known.RemoveWhere(p => !p.active);
            foreach (var p in all)
            {
                float keep = d.PropKeep(p.kind);
                if (!known.Contains(p))
                {
                    known.Add(p);
                    seen++; kinds.Add(p.kind);
                    if (EmberPropsTuning.Kinds[p.kind].mountain) mountains++;
                    if (!EmberPropsTuning.OnContext(d.Planner, p.kind, p.x, p.gy, hw)) { offContext++; if (bad.Length < 300) bad += EmberPropsTuning.Kinds[p.kind].name + "@ctx "; }
                    if (d.Planner.Match(p.x, p.gy, keep * EmberPropsTuning.ContextShare, keep * EmberPropsTuning.ContextShare, GroundClass.Canopy) > 0f) onArt++;
                    int last;
                    if (lastVariant.TryGetValue(p.kind, out last) && last == p.tier && d.PropVariants(p.kind) > 1) repeat++;
                    lastVariant[p.kind] = p.tier;
                    if (EmberPropsTuning.HasScar(p.kind))
                    {
                        scarSeen++;
                        var sr = d.PropScar(d.Props.items.IndexOf(p));
                        if (sr == null || !sr.enabled || sr.sprite == null || sr.sprite.name != EmberPropsTuning.ScarCells[p.tier]) scarWrong++;
                    }
                }
                if (Mathf.Abs((float)(p.y + d.Planner.Travel - p.gy)) > 1e-3f || p.rate != rate || p.body.localScale != Vector3.one || p.body.localRotation != Quaternion.identity) notPinned++;
                foreach (var q in all)
                    if (q != p && q.GetHashCode() > p.GetHashCode() &&
                        Mathf.Abs(q.x - p.x) < (keep + d.PropKeep(q.kind)) * .98f && System.Math.Abs(q.gy - p.gy) < (keep + d.PropKeep(q.kind)) * .98f) keepOverlap++;
                // eruptions: their whole drawn cell (the plume's reach)
                foreach (var e in d.Fires.items)
                {
                    if (!e.active) continue;
                    float k = e.size * EmberTuning.FireKeepShare;
                    if (Mathf.Abs(e.x - p.x) < (keep + k) * .98f && System.Math.Abs(e.gy - p.gy) < (keep + k) * .98f) { eruptIn++; if (bad.Length < 300) bad += "eruption@" + EmberPropsTuning.Kinds[p.kind].name + " "; }
                }
                // ash palls: the sprite as drawn now (they rise and lean over their life)
                foreach (var pl in d.Palls.items)
                {
                    if (!pl.active) continue;
                    double pgy = pl.gy + pl.vy * pl.age;
                    float half = pl.size * .5f;
                    if (Mathf.Abs(pl.x - p.x) < keep + half && System.Math.Abs(pgy - p.gy) < keep + half) pallIn++;
                }
                foreach (var s in d.Sites.items)
                {
                    if (!s.active || s.sr.sprite == null) continue;
                    var rule = EmberTuning.Rule(s.sr.sprite.name);
                    if (Mathf.Abs(s.x - p.x) < (keep + GroundPlanner.HalfX(rule, s.size)) * .98f && System.Math.Abs(s.gy - p.gy) < (keep + GroundPlanner.HalfY(rule, s.size)) * .98f) siteIn++;
                }
            }
        };
        audit();
        Run(wb, 240f, audit);
        Check("v" + v + ": " + seen + " props over 240 s (" + kinds.Count + " kinds, " + mountains + " mountains; " + d.PropsSpawned + " spawned, " + d.Clusters + " eruption clusters)",
              seen >= 30 && kinds.Count >= 9 && mountains >= 1 && d.Clusters >= 3);
        Check("v" + v + ": every prop on its own ground (flat / rocky / lava / open), none on a painted feature (" + offContext + " off, " + onArt + " on art) " + bad,
              offContext == 0 && onArt == 0);
        Check("v" + v + ": no two keep-outs overlap (" + keepOverlap + ")", keepOverlap == 0);
        Check("v" + v + ": nothing moving touches a prop: eruptions " + eruptIn + ", ash palls " + pallIn + ", sites " + siteIn, eruptIn == 0 && pallIn == 0 && siteIn == 0);
        Check("v" + v + ": pinned to the ground (rate, y + travel = gy), never mirrored or turned (" + notPinned + ")", notPinned == 0);
        Check("v" + v + ": no copy wears the previous copy's variant of its kind (" + repeat + ")", repeat == 0);
        Check("v" + v + ": every starship (" + scarSeen + ") drags the scar of its own livery (" + scarWrong + " wrong)", scarWrong == 0);
        Object.DestroyImmediate(wb.gameObject);
        EmberDirector.SeedOverride = 0;
    }

    // ---- forced overlap -----------------------------------------------------------------------------------------------------

    static void Forced()
    {
        var wb = Fresh(1, .25f);
        var d = Director(wb);
        Run(wb, 14f);
        d.ClearEruptions(); d.ClearProps(); d.HoldProps(1e6f); d.HoldGround(1e6f);
        int plant = EmberPropsTuning.Geoplant, foundry = EmberPropsTuning.Foundry;
        double gy = d.Planner.GroundY(0f);
        var a = d.PlantProp(plant, 0f, gy);
        var b = d.PlantProp(foundry, d.PropKeep(plant) * .5f, gy + d.PropKeep(plant) * .3f);
        var c = d.PlantProp(foundry, d.PropKeep(plant) + d.PropKeep(foundry) + .01f, gy);
        Check("a prop cannot be planted into another's keep-out (" + (a != null) + ", into it " + (b != null) + ", beside it " + (c != null) + ")", a != null && b == null && c != null);

        // eruptions are dealt round a prop: none may land in its keep-out (their whole cell is claimed), and a prop cannot be planted into one
        int clusters = 0, touching = 0;
        for (int i = 0; i < 60; i++) { d.SpawnCluster(gy - 1.5, gy + 1.5); clusters++; }
        foreach (var e in d.Fires.items)
        {
            if (!e.active) continue;
            float k = e.size * EmberTuning.FireKeepShare;
            foreach (var p in d.Props.items)
                if (p.active && Mathf.Abs(e.x - p.x) < (d.PropKeep(p.kind) + k) * .98f && System.Math.Abs(e.gy - p.gy) < (d.PropKeep(p.kind) + k) * .98f) touching++;
        }
        BackdropPiece onFire = null;
        foreach (var e in d.Fires.items) if (e.active) { onFire = d.PlantProp(EmberPropsTuning.Bunker, e.x, e.gy); break; }
        Check("an eruption cannot be dealt into a prop's keep-out, and no prop can be planted into an eruption's cell (" + d.Fires.ActiveCount + " eruption pieces, " +
              touching + " touching, planted on one " + (onFire != null) + ")", d.Fires.ActiveCount >= 1 && touching == 0 && onFire == null);
        bool touchesQuery = a != null && d.TouchesProp(a.x, a.gy, .01f, .01f) && !d.TouchesProp(a.x + 50f, a.gy, .01f, .01f);
        Check("TouchesProp answers for movers: true inside a prop's keep-out, false far away", touchesQuery);
        Object.DestroyImmediate(wb.gameObject);
    }

    // ---- the starship's scar --------------------------------------------------------------------------------------------------

    static void Scar()
    {
        var wb = Fresh(2, 1.5f);
        var d = Director(wb);
        Run(wb, 14f);
        d.ClearEruptions(); d.ClearProps(); d.HoldProps(1e6f); d.HoldGround(1e6f);
        double gy = d.Planner.GroundY(0f);
        var ship = d.PlantProp(EmberPropsTuning.Starship, .4f, gy, 2);
        int slot = ship != null ? d.Props.items.IndexOf(ship) : -1;
        var sr = d.PropScar(slot);
        Check("a starship is planted with its scar decal (variant 2 -> scar_2, enabled, under its body)",
              ship != null && sr != null && sr.enabled && sr.sprite != null && sr.sprite.name == "scar_2" && sr.sortingOrder < ship.sr.sortingOrder);
        bool boxes = ship != null;
        int blocked = 0;
        for (int j = 0; j < EmberPropsTuning.ScarBoxes[0].Length && ship != null; j++)
        {
            float bx; double by; float h;
            boxes &= d.PropScarBox(slot, j, out bx, out by, out h);
            if (!boxes) break;
            if (d.PlantProp(EmberPropsTuning.Bunker, bx, by) == null) blocked++;
            boxes &= d.TouchesProp(bx, by, .01f, .01f);
        }
        Check("the scar's keep-out boxes are claimed: nothing is planted into the furrow (" + blocked + " of " + EmberPropsTuning.ScarBoxes[0].Length + " refused), TouchesProp sees them", boxes && blocked == EmberPropsTuning.ScarBoxes[0].Length);
        // it leaves with the wreck: the decal is off and the boxes free again
        Run(wb, 150f);
        bool gone = ship != null && !ship.active && sr != null && !sr.enabled;
        Check("the decal and its boxes go when the wreck scrolls off (" + gone + ")", gone);
        Object.DestroyImmediate(wb.gameObject);
    }

    // ---- still frame, allocation ----------------------------------------------------------------------------------------------

    static void Still()
    {
        var wb = Fresh(3, .8f);
        var d = Director(wb);
        Run(wb, 30f);
        var pos = new List<Vector3>(); var spr = new List<Sprite>();
        foreach (var p in d.Props.items) { pos.Add(p.root.localPosition); spr.Add(p.sr.sprite); }
        for (int i = 0; i < 20; i++) wb.Step(0f);
        bool still = true;
        for (int i = 0; i < d.Props.items.Count; i++) still &= d.Props.items[i].root.localPosition == pos[i] && d.Props.items[i].sr.sprite == spr[i];
        Check("a paused frame (dt 0) is a still frame: props keep their spot and cell", still);
        // (sampled every step: the 8 frames at FrameMs 125 loop once a second, so a prop is back on its first cell after exactly 1 s)
        var before = new Dictionary<BackdropPiece, Sprite>();
        var moved = new HashSet<BackdropPiece>();
        foreach (var p in d.Props.items) if (p.active) before[p] = p.sr.sprite;
        Run(wb, 1f, () => { foreach (var kv in before) if (kv.Key.active && kv.Key.sr.sprite != kv.Value) moved.Add(kv.Key); });
        int animated = moved.Count;
        Check("the props animate from their age (" + animated + " of " + before.Count + " changed cell within 1 s)", animated >= 1);
        long control;
        bool meter = TestHarness.AllocMeterWorks(out control);
        long used = TestHarness.AllocatedBytes(() => Run(wb, 60f));
        Check("the props allocate nothing per frame (" + used + " bytes)", meter && used >= 0 && used <= 256);
        Object.DestroyImmediate(wb.gameObject);
    }
}
