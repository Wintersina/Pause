using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Desert's living props and mountains (DesertProps.cs, r1 2026-11-28):
//   * TABLE: every kind is one data row; each layout atlas (v<n>/props) carries its strips -- 8 frames (the mountains 1), all four
//     variants of the mountains, the skiffs only where the layout has a shore (v2 / v3); keep-out = .42 x px + 22 (1080 px screen);
//   * ART: no pink texel (WorldCue.IsPink), the lights' frame-to-frame brightness change small (no strobing field), the props'
//     brightness under the landmark ceiling as drawn (PropTint < 1: every shot's core stays brighter), v4's night grade darker and
//     moonlit blue-grey (never purple), its mountains with no pale apron halo;
//   * LIVE, every layout with worms forced often: each prop on its context (flat / rocky / shore / open) and never on a painted
//     feature, the NEAR kinds beside their hosts, no two keep-outs overlapping (they may touch), PINNED (y + travel = gy, the mid
//     tile's rate, never mirrored or turned), each copy's variant never the previous copy's of its kind; NOTHING MOVING TOUCHES A
//     PROP: no worm (ridge or eruption), dust devil / grit gust / mirage glint or launch site inside a keep-out, ever;
//   * FORCED OVERLAP (as FrostPropsTest): a prop cannot be planted into another's keep-out, nor placed on a live worm's path; a worm
//     forced into a prop or a mountain dives and fades and the prop stands (never razed);
//   * BALLOONS: the balloon field leads each landing with another livery;
//   * dt 0 is a still frame; no allocation per frame.
public static class DesertPropsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DPR] PASS  " : "[DPR] FAIL  ") + what);
        if (!ok) fails++;
    }
    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 20f;
    const string ArtDir = "Assets/Art/Backgrounds/Resources/Worlds/Desert/Backdrop3/";
    static BackdropVariants Sel => BackdropVariants.For("Desert");

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        float savedSpeed = moveBackGround.speed;
        float first = DesertTuning.WormFirst, gmin = DesertTuning.WormGapMin, gmax = DesertTuning.WormGapMax;
        try
        {
            Table();
            for (int v = 1; v <= 4; v++) Art(v);
            for (int v = 1; v <= 4; v++) Live(v);
            Forced();
            Balloons();
            Still();
        }
        finally
        {
            DesertTuning.WormFirst = first; DesertTuning.WormGapMin = gmin; DesertTuning.WormGapMax = gmax;
            DesertDirector.SeedOverride = 0;
            moveBackGround.speed = savedSpeed;
            Time.timeScale = 1f;
            Sel.Reset();
            EliteSystem.Clear();
            if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
        }
        Debug.Log("[DPR] failures: " + fails);
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
        var wb = WorldBackdrop.Create("Desert");
        wb.Show("Desert", false);
        return wb;
    }

    static DesertDirector Director(WorldBackdrop wb) { return wb.Current != null ? wb.Current.Director as DesertDirector : null; }

    static void Run(WorldBackdrop wb, float seconds, System.Action each = null)
    {
        for (float t = 0f; t < seconds; t += Dt) { wb.Step(Dt); if (each != null) each(); }
    }

    static BackdropAtlas Atlas(int v)
    {
        string f = BackdropCatalog.TileFolder("Desert", v);
        return new BackdropAtlas(Resources.Load<Texture2D>(f + DesertPropsTuning.Atlas), Resources.Load<TextAsset>(f + DesertPropsTuning.Atlas));
    }

    // ---- the table -----------------------------------------------------------------------------------------------------------

    static void Table()
    {
        var ks = DesertPropsTuning.Kinds;
        bool names = true;
        var seen = new HashSet<string>();
        foreach (var k in ks) names &= seen.Add(k.name) && k.px > 0f && k.perThree > 0f;
        Check("the props table: " + ks.Length + " kinds, one data row each (unique names, sizes, rates)", ks.Length == 21 && names);
        Check("the mountains: mesa 340, ridge 360, hoodoo 320, volcano 350 px, on rocky or open ground, .6 - .7 per 3 screens together",
              ks[DesertPropsTuning.Mesa].px == 340f && ks[DesertPropsTuning.Ridge].px == 360f && ks[DesertPropsTuning.Hoodoo].px == 320f &&
              ks[DesertPropsTuning.Volcano].px == 350f && ks[DesertPropsTuning.Mesa].mountain && ks[DesertPropsTuning.Volcano].on == DesertPropsTuning.Place.Mountain &&
              DesertPropsTuning.TotalPerThree(true) >= .6f - 1e-4f && DesertPropsTuning.TotalPerThree(true) <= .7f + 1e-4f);
        float hw = 2.7f;
        Check("keep-out = .42 x px + 22 on a 1080 px screen (oasis " + DesertPropsTuning.Keep(DesertPropsTuning.Oasis, hw).ToString("F3") + " units)",
              Mathf.Abs(DesertPropsTuning.Keep(DesertPropsTuning.Oasis, hw) - (150f * .42f + 22f) / 1080f * 2f * hw) < 1e-4f);
        Check("placement rules: skiffs on the shore; camp, caravan, solar, station, spaceport, market, zeppelin, balloons on flat ground; dig, ruins, " +
              "observatory on rocky; crawler, rig, wind on open dunes; caravan beside camp / oasis / market, balloons beside camp / station, market beside the oasis",
              ks[DesertPropsTuning.Skiffs].on == DesertPropsTuning.Place.Shore &&
              ks[DesertPropsTuning.Camp].on == DesertPropsTuning.Place.Flat && ks[DesertPropsTuning.Caravan].on == DesertPropsTuning.Place.Flat &&
              ks[DesertPropsTuning.Solar].on == DesertPropsTuning.Place.Flat && ks[DesertPropsTuning.Station].on == DesertPropsTuning.Place.Flat &&
              ks[DesertPropsTuning.Spaceport].on == DesertPropsTuning.Place.Flat && ks[DesertPropsTuning.Market].on == DesertPropsTuning.Place.Flat &&
              ks[DesertPropsTuning.Zeppelin].on == DesertPropsTuning.Place.Flat &&
              ks[DesertPropsTuning.Dig].on == DesertPropsTuning.Place.Rocky && ks[DesertPropsTuning.Ruins].on == DesertPropsTuning.Place.Rocky &&
              ks[DesertPropsTuning.Observatory].on == DesertPropsTuning.Place.Rocky &&
              ks[DesertPropsTuning.Crawler].on == DesertPropsTuning.Place.Open && ks[DesertPropsTuning.Rig].on == DesertPropsTuning.Place.Open &&
              ks[DesertPropsTuning.Wind].on == DesertPropsTuning.Place.Open &&
              Same(ks[DesertPropsTuning.Caravan].near, DesertPropsTuning.Camp, DesertPropsTuning.Oasis, DesertPropsTuning.Market) &&
              Same(ks[DesertPropsTuning.Balloon].near, DesertPropsTuning.Camp, DesertPropsTuning.Station) &&
              Same(ks[DesertPropsTuning.Market].near, DesertPropsTuning.Oasis));
        for (int v = 1; v <= 4; v++)
        {
            var a = Atlas(v);
            bool strips = true;
            string bad = "";
            for (int k = 0; k < ks.Length; k++)
            {
                bool shore = v == 2 || v == 3;
                int variants = 0;
                for (int i = 0; i < 16; i++) { var f = a.Frames(DesertPropsTuning.Strip(k, i)); if (f.Length == 0) break; variants++; if (f.Length != (ks[k].mountain ? 1 : 8)) { strips = false; bad += ks[k].name + "_" + i + " "; } }
                bool want = k != DesertPropsTuning.Skiffs || shore;
                if (want ? variants < 1 : variants != 0) { strips = false; bad += ks[k].name + " x" + variants + " "; }
                if (ks[k].mountain && variants != 4) { strips = false; bad += ks[k].name + " needs 4 "; }
            }
            Check("v" + v + " props atlas: every kind's strips (8 frames, the mountains 1 and all four variants; skiffs only with a shore) " + bad,
                  a.Count > 0 && strips);
            a.Destroy();
        }
    }

    static bool Same(int[] a, params int[] b)
    {
        if (a == null || a.Length != b.Length) return false;
        foreach (int x in b) if (System.Array.IndexOf(a, x) < 0) return false;
        return true;
    }

    // ---- the art ------------------------------------------------------------------------------------------------------------

    [System.Serializable] class JRect { public string n; public int x, y, w, h; }
    [System.Serializable] class JAtlas { public JRect[] sprites; }

    static float dayLum, dayMountain;

    static void Art(int v)
    {
        string png = ArtDir + "v" + v + "/" + DesertPropsTuning.Atlas + ".png";
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(png));
        var px = tex.GetPixels();
        int W = tex.width;
        Object.DestroyImmediate(tex);
        var json = JsonUtility.FromJson<JAtlas>(File.ReadAllText(Path.ChangeExtension(png, ".json")));
        int pink = 0;
        double lum = 0, wsum = 0, mLum = 0, mW = 0, rSum = 0, bSum = 0;
        foreach (var c in px)
        {
            if (c.a <= .1f) continue;
            if (WorldCue.IsPink(c)) pink++;
            float l = .2126f * c.r + .7152f * c.g + .0722f * c.b;
            lum += l * c.a; wsum += c.a; rSum += c.r * c.a; bSum += c.b * c.a;
        }
        // per strip: mean premultiplied luminance of each frame; the largest step between consecutive frames
        var strips = new Dictionary<string, List<JRect>>();
        foreach (var r in json.sprites)
        {
            int cut = r.n.LastIndexOf('_');
            string key = r.n.Substring(0, cut);
            List<JRect> l;
            if (!strips.TryGetValue(key, out l)) strips[key] = l = new List<JRect>();
            l.Add(r);
        }
        float worstStep = 0f;
        string worst = "";
        foreach (var kv in strips)
        {
            bool mountain = false;
            foreach (int m in new[] { DesertPropsTuning.Mesa, DesertPropsTuning.Ridge, DesertPropsTuning.Hoodoo, DesertPropsTuning.Volcano })
                mountain |= kv.Key.StartsWith(DesertPropsTuning.Kinds[m].name + "_");
            var means = new List<float>();
            foreach (var r in kv.Value)
            {
                double s = 0; int n = 0;
                for (int y = r.y; y < r.y + r.h; y++)
                    for (int x = r.x; x < r.x + r.w; x++)
                    {
                        var c = px[y * W + x];
                        float l = (.2126f * c.r + .7152f * c.g + .0722f * c.b) * c.a;
                        s += l; n++;
                        if (mountain && c.a > .1f) { mLum += l; mW += c.a; }
                    }
                means.Add((float)(s / Mathf.Max(1, n)));
            }
            for (int i = 0; i < means.Count; i++)
            {
                float step = Mathf.Abs(means[i] - means[(i + 1) % means.Count]) / Mathf.Max(.02f, means[i]);
                if (step > worstStep) { worstStep = step; worst = kv.Key; }
            }
        }
        float mean = (float)(lum / System.Math.Max(1e-6, wsum));
        float mountainLum = (float)(mLum / System.Math.Max(1e-6, mW));
        Check("v" + v + " props atlas: no pink texel (" + pink + ")", pink == 0);
        Check("v" + v + " the lights blink gently: no strip's mean brightness steps more than 25% between frames (worst " + worst + " " + worstStep.ToString("P0") + ")",
              worstStep <= .25f);
        Check("v" + v + " props drawn under the landmark ceiling (mean " + mean.ToString("F3") + " x tint " + DesertPropsTuning.PropTint.r + " <= .66) and dimmer than painted (tint < 1)",
              mean * DesertPropsTuning.PropTint.r <= .66f && DesertPropsTuning.PropTint.r < 1f);
        if (v == 1) { dayLum = mean; dayMountain = mountainLum; }
        if (v == 4)
        {
            float r = (float)(rSum / wsum), b = (float)(bSum / wsum);
            Check("v4 the night grade: darker than the day (" + mean.ToString("F3") + " < .6 x " + dayLum.ToString("F3") + "), moonlit blue-grey, never purple (mean r " +
                  r.ToString("F3") + " <= b " + b.ToString("F3") + ")", mean < dayLum * .6f && r <= b + .01f);
            Check("v4 the mountains carry no pale apron halo (mean " + mountainLum.ToString("F3") + " < .55 x day " + dayMountain.ToString("F3") + ")",
                  mountainLum < dayMountain * .55f);
        }
    }

    // ---- live ---------------------------------------------------------------------------------------------------------------

    static void Live(int v)
    {
        DesertTuning.WormFirst = 2f; DesertTuning.WormGapMin = .5f; DesertTuning.WormGapMax = 1f;
        DesertDirector.SeedOverride = 4100 + v * 13;
        var wb = Fresh(v, 1.2f);
        var d = Director(wb);
        Check("v" + v + " director with its props atlas", d != null && d.HasPropArt(DesertPropsTuning.Oasis));
        if (d == null) return;
        float hw = d.ViewHalfWidth;
        int seen = 0, offContext = 0, onArt = 0, farFromHost = 0, keepOverlap = 0, wormIn = 0, wandererIn = 0, siteIn = 0, notPinned = 0, repeat = 0, mountains = 0;
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
            foreach (var p in all)
            {
                float keep = d.PropKeep(p.kind);
                if (!known.Contains(p))
                {
                    known.Add(p);
                    seen++; kinds.Add(p.kind);
                    if (DesertPropsTuning.Kinds[p.kind].mountain) mountains++;
                    if (!DesertPropsTuning.OnContext(d.Planner, p.kind, p.x, p.gy, hw)) { offContext++; if (bad.Length < 300) bad += DesertPropsTuning.Kinds[p.kind].name + "@ctx "; }
                    if (d.Planner.Match(p.x, p.gy, keep * DesertPropsTuning.ContextShare, keep * DesertPropsTuning.ContextShare, GroundClass.Canopy) > 0f) onArt++;
                    var near = DesertPropsTuning.Kinds[p.kind].near;
                    if (near != null)
                    {
                        bool host = false;
                        foreach (var q in all)
                            if (q != p && System.Array.IndexOf(near, q.kind) >= 0 &&
                                Vector2.Distance(new Vector2(q.x, (float)q.gy), new Vector2(p.x, (float)p.gy)) <= DesertPropsTuning.NearMax * (d.PropKeep(q.kind) + keep * .6f) + 1e-3f) host = true;
                        if (!host) { farFromHost++; if (bad.Length < 300) bad += DesertPropsTuning.Kinds[p.kind].name + "@alone "; }
                    }
                    int last;
                    if (lastVariant.TryGetValue(p.kind, out last) && last == p.tier && d.PropVariants(p.kind) > 1) repeat++;
                    lastVariant[p.kind] = p.tier;
                }
                if (Mathf.Abs((float)(p.y + d.Planner.Travel - p.gy)) > 1e-3f || p.rate != rate || p.body.localScale != Vector3.one || p.body.localRotation != Quaternion.identity) notPinned++;
                foreach (var q in all)
                    if (q != p && q.GetHashCode() > p.GetHashCode() &&
                        Mathf.Abs(q.x - p.x) < (keep + d.PropKeep(q.kind)) * .98f && System.Math.Abs(q.gy - p.gy) < (keep + d.PropKeep(q.kind)) * .98f) keepOverlap++;
                foreach (var w in d.Worms.items)
                {
                    if (!w.active) continue;
                    float rx, ry;
                    bool erupting = w.tier == DesertDirector.WormEmerge || w.tier == DesertDirector.WormArc || w.tier == DesertDirector.WormDive;
                    DesertDirector.WormFoot(w.kind, erupting, out rx, out ry);
                    if (Mathf.Abs(w.x - p.x) < keep + rx && System.Math.Abs(w.gy - p.gy) < keep + ry) { wormIn++; if (bad.Length < 300) bad += "worm@" + DesertPropsTuning.Kinds[p.kind].name + " "; }
                }
                foreach (var w in d.Wanderers.items)
                {
                    if (!w.active) continue;
                    float k = DesertDirector.WandererKeepOf(w);
                    if (Mathf.Abs(w.x - p.x) < (keep + k) * .98f && System.Math.Abs(w.gy - p.gy) < (keep + k) * .98f) wandererIn++;
                }
                foreach (var s in d.Sites.items)
                {
                    if (!s.active || s.sr.sprite == null) continue;
                    var rule = DesertTuning.Rule(s.sr.sprite.name);
                    if (Mathf.Abs(s.x - p.x) < (keep + GroundPlanner.HalfX(rule, s.size)) * .98f && System.Math.Abs(s.gy - p.gy) < (keep + GroundPlanner.HalfY(rule, s.size)) * .98f) siteIn++;
                }
            }
        };
        audit();
        Run(wb, 240f, audit);
        Check("v" + v + ": " + seen + " props over 240 s (" + kinds.Count + " kinds, " + mountains + " mountains, " + d.WormsSpawned + " worms)",
              seen >= 40 && kinds.Count >= 10 && mountains >= 1 && d.WormsSpawned >= 5);
        Check("v" + v + ": every prop on its own ground (flat / rocky / shore / open), none on a painted feature (" + offContext + " off, " + onArt + " on art) " + bad,
              offContext == 0 && onArt == 0);
        Check("v" + v + ": caravans, balloons and markets stand beside their hosts (" + farFromHost + " alone)", farFromHost == 0);
        Check("v" + v + ": no two keep-outs overlap (" + keepOverlap + ")", keepOverlap == 0);
        Check("v" + v + ": nothing moving touches a prop: worms " + wormIn + ", wanderers " + wandererIn + ", sites " + siteIn, wormIn == 0 && wandererIn == 0 && siteIn == 0);
        Check("v" + v + ": pinned to the ground (rate, y + travel = gy), never mirrored or turned (" + notPinned + ")", notPinned == 0);
        Check("v" + v + ": no copy wears the previous copy's variant of its kind (" + repeat + ")", repeat == 0);
        Object.DestroyImmediate(wb.gameObject);
        DesertDirector.SeedOverride = 0;
    }

    // ---- forced overlap -----------------------------------------------------------------------------------------------------

    static void Forced()
    {
        var wb = Fresh(1, .25f);
        var d = Director(wb);
        Run(wb, 14f);
        d.ClearWorms(); d.ClearItems(); d.ClearProps(); d.HoldProps(1e6f);
        int camp = DesertPropsTuning.Camp, oasis = DesertPropsTuning.Oasis;
        double gy = d.Planner.GroundY(0f);
        var a = d.PlantProp(camp, 0f, gy);
        var b = d.PlantProp(oasis, d.PropKeep(camp) * .5f, gy + d.PropKeep(camp) * .3f);
        var c = d.PlantProp(oasis, d.PropKeep(camp) + d.PropKeep(oasis) + .01f, gy);
        Check("a prop cannot be planted into another's keep-out (" + (a != null) + ", into it " + (b != null) + ", beside it " + (c != null) + ")", a != null && b == null && c != null);

        d.ClearProps();
        bool spawned = d.SpawnWorm(1, 0f, false, 0f, true);
        var w = d.Worms.items[0];
        int blockedBefore = d.PropsBlockedByWorm;
        double ahead = w.gy + w.vy * 1.5f;
        float keep = d.PropKeep(DesertPropsTuning.Wind);
        bool clear = d.WormPathClear(w.x + w.vx * 1.5f, ahead, keep);
        int landed = 0;
        for (int i = 0; i < 20; i++)
            if (d.SpawnProp(DesertPropsTuning.Wind, ahead - .02, ahead + .02)) landed++;
        bool onPath = false;
        foreach (var p in d.Props.items)
            if (p.active && Mathf.Abs(p.x - (w.x + w.vx * 1.5f)) < keep) onPath = true;
        Check("no prop is placed on a live worm's path (worm " + spawned + ", path clear " + clear + ", " + landed + " placed, " +
              (d.PropsBlockedByWorm - blockedBefore) + " candidates refused, on the path " + onPath + ")", spawned && !clear && !onPath);

        foreach (int kind in new[] { DesertPropsTuning.Solar, DesertPropsTuning.Mesa })
        {
            d.ClearWorms(); d.ClearProps();
            d.SpawnWorm(kind == DesertPropsTuning.Mesa ? 0 : 1, 0f, false, 0f, true);
            var worm = d.Worms.items[0];
            var prop = d.PlantProp(kind, worm.x, worm.gy + d.PropKeep(kind) + DesertTuning.WormCell[1] * .4f);
            int stopped = d.WormsStoppedByProp;
            bool dived = false, intact = prop != null;
            for (int i = 0; i < 20 * 40 && !dived && prop != null; i++)
            {
                wb.Step(Dt);
                if (worm.active && worm.tier == DesertDirector.WormFading) dived = true;
                intact &= prop.active && prop.dyingT < 0f;
            }
            Check("a worm forced into a " + DesertPropsTuning.Kinds[kind].name + " dives and fades; the prop stands (dived " + dived + ", intact " + intact + ")",
                  prop != null && dived && intact && d.WormsStoppedByProp > stopped);
        }
        Object.DestroyImmediate(wb.gameObject);
    }

    // ---- balloons per landing -----------------------------------------------------------------------------------------------

    static void Balloons()
    {
        var firsts = new List<int>();
        Sel.Reset(); Sel.Seed(5);
        for (int landing = 0; landing < 4; landing++)
        {
            var wb = Fresh(1, .25f, false);
            var d = Director(wb);
            d.ClearProps(); d.ClearWorms(); d.ClearItems(); d.HoldProps(1e6f);
            double gy = d.Planner.GroundY(0f);
            d.PlantProp(DesertPropsTuning.Camp, -1f, gy);
            var b = d.PlantProp(DesertPropsTuning.Balloon, .9f, gy + .1, -1);
            firsts.Add(b != null ? b.tier : -1);
            Object.DestroyImmediate(wb.gameObject);
        }
        bool differ = !firsts.Contains(-1);
        for (int i = 1; i < firsts.Count; i++) differ &= firsts[i] != firsts[i - 1];
        Check("the balloon field leads each landing with another livery (" + string.Join(",", firsts) + ")", differ);
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
        int animated = 0;
        var before = new Dictionary<BackdropPiece, Sprite>();
        foreach (var p in d.Props.items) if (p.active) before[p] = p.sr.sprite;
        Run(wb, 1f);
        foreach (var kv in before) if (kv.Key.active && kv.Key.sr.sprite != kv.Value) animated++;
        Check("the props animate from their age (" + animated + " changed cell in 1 s)", animated >= 1);
        long control;
        bool meter = TestHarness.AllocMeterWorks(out control);
        long used = TestHarness.AllocatedBytes(() => Run(wb, 60f));
        Check("the props allocate nothing per frame (" + used + " bytes)", meter && used >= 0 && used <= 256);
        Object.DestroyImmediate(wb.gameObject);
    }
}
