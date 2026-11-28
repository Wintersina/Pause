using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tide's living props (TideProps.cs, r1 2026-11-28):
//   * TABLE: eleven kinds, one data row each; Backdrop3/props carries every kind's four variants with their frames (8, the whales
//     and mantas 16) and each kind's frame time (props.json "frameMs"); keep-out = .42 x px + 22 (1080 px screen);
//   * ART: no pink texel (WorldCue.IsPink), the props' brightness as drawn (mean <= .34, p90 <= .72), the night tint darker and
//     cooler, the atlas ASTC 4x4 uncapped and within its 12 MB;
//   * LIVE, every ground set: marine life only on water, every prop on its context and NEVER on a painted deck / block / feature
//     (Built / Canopy), no two keep-outs overlapping, PINNED (y + travel = gy, the mid tile's rate, never mirrored or turned), each
//     copy's variant never the previous copy's of its kind; NOTHING TOUCHES A PROP: no piece, cluster, pipe run or launch site in a
//     keep-out, no smoke column rising through one, no ocean crest or smoke pall (its whole drift) in one;
//   * FORCED OVERLAP (as FrostPropsTest): a prop cannot be planted into another's keep-out; pieces, clusters, launch sites, crests and
//     smoke palls forced onto a planted prop are refused, nothing is placed in it; a prop is not placed where a pall will drift;
//   * dt 0 is a still frame; no allocation per frame.
public static class TidePropsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TPR] PASS  " : "[TPR] FAIL  ") + what);
        if (!ok) fails++;
    }
    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 20f;
    const string ArtDir = "Assets/Art/Backgrounds/Resources/Worlds/Tide/Backdrop3/";
    static BackdropVariants Sel => BackdropVariants.For("Tide");

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
            Still();
        }
        finally
        {
            TideDirector.SeedOverride = 0;
            moveBackGround.speed = savedSpeed;
            Time.timeScale = 1f;
            Sel.Reset();
            EliteSystem.Clear();
            if (WorldBackdrop.Instance != null) Object.DestroyImmediate(WorldBackdrop.Instance.gameObject);
        }
        Debug.Log("[TPR] failures: " + fails);
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
        var wb = WorldBackdrop.Create("Tide");
        wb.Show("Tide", false);
        return wb;
    }

    static TideDirector Director(WorldBackdrop wb) { return wb.Current != null ? wb.Current.Director as TideDirector : null; }

    static void Run(WorldBackdrop wb, float seconds, System.Action each = null)
    {
        for (float t = 0f; t < seconds; t += Dt) { wb.Step(Dt); if (each != null) each(); }
    }

    [System.Serializable] class JRect { public string n; public int x, y, w, h; }
    [System.Serializable]
    class JFrameMs { public int lighthouse, platform, village, freighter, wreck, atoll, buoys, farm, dock, whales, mantas; }
    [System.Serializable] class JAtlas { public JRect[] sprites; public JFrameMs frameMs; }

    static IEnumerable<BackdropPool> PiecePools(TideDirector d)
    {
        yield return d.Pipes; yield return d.Fires; yield return d.Ground; yield return d.Sites;
    }

    // ---- the table -----------------------------------------------------------------------------------------------------------

    static void Table()
    {
        var ks = TidePropsTuning.Kinds;
        bool names = true;
        var seen = new HashSet<string>();
        foreach (var k in ks) names &= seen.Add(k.name) && k.px > 0f && k.perThree > 0f && k.ms >= 80;
        Check("the props table: " + ks.Length + " kinds, one data row each (unique names, sizes, rates, frame times)", ks.Length == 15 && names);
        float hw = 2.7f;
        Check("keep-out = .42 x drawn px + 22 on a 1080 px screen (atoll " + TidePropsTuning.Keep(TidePropsTuning.Atoll, hw).ToString("F3") + " units)",
              Mathf.Abs(TidePropsTuning.Keep(TidePropsTuning.Atoll, hw) - (270f * .42f + 22f) / 1080f * 2f * hw) < 1e-4f);
        Check("drawn px: lighthouse 160, platform 170, village 200, freighter 260, wreck 180, atoll 270, buoys 180, farm 210, dock 220, whales 190, mantas 180, turbines 200, stack 320, cliffs 360, starship 360",
              ks[TidePropsTuning.Lighthouse].px == 160f && ks[TidePropsTuning.Platform].px == 170f && ks[TidePropsTuning.Village].px == 200f &&
              ks[TidePropsTuning.Freighter].px == 260f && ks[TidePropsTuning.Wreck].px == 180f && ks[TidePropsTuning.Atoll].px == 270f &&
              ks[TidePropsTuning.Buoys].px == 180f && ks[TidePropsTuning.Farm].px == 210f && ks[TidePropsTuning.Dock].px == 220f &&
              ks[TidePropsTuning.Whales].px == 190f && ks[TidePropsTuning.Mantas].px == 180f && ks[TidePropsTuning.Turbines].px == 200f &&
              ks[TidePropsTuning.Stack].px == 320f && ks[TidePropsTuning.Cliffs].px == 360f && ks[TidePropsTuning.Starship].px == 360f &&
              TidePropsTuning.Big(TidePropsTuning.Stack) && TidePropsTuning.Big(TidePropsTuning.Cliffs) && TidePropsTuning.Big(TidePropsTuning.Starship) && !TidePropsTuning.Big(TidePropsTuning.Lighthouse) &&
              TidePropsTuning.IndexOf("kelp") < 0);
        Check("placement: freighter and buoys on deep water, whales and mantas only in water, the rest on water or shoal; farm and dock like the village",
              ks[TidePropsTuning.Freighter].on == TidePropsTuning.Place.Deep && ks[TidePropsTuning.Buoys].on == TidePropsTuning.Place.Deep &&
              ks[TidePropsTuning.Whales].on == TidePropsTuning.Place.Marine && ks[TidePropsTuning.Mantas].on == TidePropsTuning.Place.Marine &&
              ks[TidePropsTuning.Lighthouse].on == TidePropsTuning.Place.Shoal && ks[TidePropsTuning.Village].on == TidePropsTuning.Place.Shoal &&
              ks[TidePropsTuning.Farm].near != null && ks[TidePropsTuning.Dock].near != null &&
              (TidePropsTuning.On(TidePropsTuning.Place.Marine) & (GroundClass.Built | GroundClass.Canopy | GroundClass.Open)) == 0 &&
              (TidePropsTuning.On(TidePropsTuning.Place.Shoal) & TidePropsTuning.Painted) == 0);
        Check("whales 140 ms and mantas 110 ms a frame, the rest 125",
              ks[TidePropsTuning.Whales].ms == 140 && ks[TidePropsTuning.Mantas].ms == 110 && ks[TidePropsTuning.Lighthouse].ms == 125 && ks[TidePropsTuning.Dock].ms == 125);

        var a = new BackdropAtlas(Resources.Load<Texture2D>("Worlds/Tide/Backdrop3/" + TidePropsTuning.Atlas), Resources.Load<TextAsset>("Worlds/Tide/Backdrop3/" + TidePropsTuning.Atlas));
        bool strips = true;
        string bad = "";
        for (int k = 0; k < ks.Length; k++)
        {
            int variants = 0;
            int want = ks[k].frames;
            for (int i = 0; i < 16; i++)
            {
                var f = a.Frames(TidePropsTuning.Strip(k, i));
                if (f.Length == 0) break;
                variants++;
                if (f.Length != want) { strips = false; bad += ks[k].name + "_" + i + " "; }
            }
            if (variants != 4) { strips = false; bad += ks[k].name + " x" + variants + " "; }
        }
        Check("props atlas: every kind has 4 variants and its frames (8; the whales and mantas 16; the stack and cliffs 1) " + bad, a.Count > 0 && strips);
        a.Destroy();

        var json = JsonUtility.FromJson<JAtlas>(File.ReadAllText(ArtDir + "props.json"));
        var ms = json.frameMs;
        Check("props.json frameMs matches the table (whales " + ms.whales + ", mantas " + ms.mantas + ")",
              ms.lighthouse == ks[0].ms && ms.platform == ks[1].ms && ms.village == ks[2].ms && ms.freighter == ks[3].ms && ms.wreck == ks[4].ms &&
              ms.atoll == ks[5].ms && ms.buoys == ks[6].ms && ms.farm == ks[7].ms && ms.dock == ks[8].ms && ms.whales == ks[9].ms && ms.mantas == ks[10].ms);
    }

    // ---- the art ------------------------------------------------------------------------------------------------------------

    static void Art()
    {
        string png = ArtDir + "props.png";
        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(png));
        var px = tex.GetPixels();
        int width = tex.width, height = tex.height;
        Object.DestroyImmediate(tex);
        int pink = 0, n = 0;
        var lums = new List<float>();
        double lum = 0;
        foreach (var c in px)
        {
            if (WorldCue.IsPink(c)) pink++;
            if (c.a <= .1f) continue;
            float l = .2126f * c.r + .7152f * c.g + .0722f * c.b;
            lum += l; n++;
            lums.Add(l);
        }
        lums.Sort();
        float mean = (float)(lum / System.Math.Max(1, n)) * TidePropsTuning.PropTint.r;
        float p90 = lums.Count > 0 ? lums[Mathf.Min(lums.Count - 1, (int)(lums.Count * .9f))] * TidePropsTuning.PropTint.r : 0f;
        Check("props atlas (" + width + " x " + height + "): no pink texel (" + pink + ")", pink == 0);
        Check("props as drawn: mean luminance " + mean.ToString("F3") + " <= .34, p90 " + p90.ToString("F3") + " <= .72, dimmer than painted (tint " + TidePropsTuning.PropTint.r + " < 1)",
              mean <= .34f && p90 <= .72f && TidePropsTuning.PropTint.r < 1f);
        var day = TidePropsTuning.TintFor(false); var night = TidePropsTuning.TintFor(true);
        Check("the night side tints the props darker and cooler (" + night.r.ToString("F2") + "," + night.g.ToString("F2") + "," + night.b.ToString("F2") + " vs day " + day.r.ToString("F2") + ")",
              night.r <= day.r * .55f && night.g <= day.g * .62f && night.b <= day.b * .68f && night.b > night.r);
        Blend(px, width);
        // texture budget: ASTC 4x4, never clamped, no mips
        long astc = ((width + 5) / 6) * ((height + 5) / 6) * 16L;
        var imp = (TextureImporter)AssetImporter.GetAtPath(png);
        Check("props atlas texture memory " + (astc / 1024) + " KB ASTC 6x6 <= 12288 KB (4x4 would be " + (((width + 3) / 4) * ((height + 3) / 4) * 16L / 1024) + " KB) (the world's total is WorldBackdropTest's line)", astc <= 12L * 1024 * 1024);
        Check("props atlas import: ASTC 6x6 on Android, not clamped below 4096, no mipmaps, compressed",
              imp != null && imp.GetPlatformTextureSettings("Android").format == TextureImporterFormat.ASTC_6x6 && imp.maxTextureSize >= 4096 &&
              !imp.mipmapEnabled && imp.textureCompression != TextureImporterCompression.Uncompressed);
    }

    // BLEND (user, 2026-11-28: every prop blended into its world): per kind, over its opaque texels, the mean luminance within [.15, .36]
    // (no prop brighter or duller than the sea around it), the mean saturation <= .60, a wet base (the lowest tenth of each cell's
    // rows darker than the kind's overall mean), and a soft base: the bottom row has no hard opaque edge
    static void Blend(Color[] px, int width)
    {
        var json = JsonUtility.FromJson<JAtlas>(File.ReadAllText(ArtDir + "props.json"));
        var ks = TidePropsTuning.Kinds;
        string bad = "";
        for (int k = 0; k < ks.Length; k++)
        {
            double lum = 0, sat = 0, baseLum = 0; long n = 0, nb = 0, hardRows = 0, rows = 0;
            string prefix = ks[k].name + "_";
            foreach (var r in json.sprites)
            {
                if (!r.n.StartsWith(prefix)) continue;
                int band = Mathf.Max(1, r.h / 10);
                for (int y = r.y; y < r.y + r.h; y++)
                    for (int x = r.x; x < r.x + r.w; x++)
                    {
                        var c = px[y * width + x];
                        if (c.a <= .1f) continue;
                        float l = .2126f * c.r + .7152f * c.g + .0722f * c.b;
                        float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                        lum += l; sat += mx > 0f ? (mx - mn) / mx : 0f; n++;
                        if (y < r.y + band) { baseLum += l; nb++; }
                    }
                // the lowest row of the cut: no long run of fully opaque texels (a square block side)
                int run = 0;
                for (int x = r.x; x < r.x + r.w; x++) { if (px[r.y * width + x].a > .95f) run++; }
                rows++; if (run > r.w * .6f) hardRows++;
            }
            if (n == 0) { bad += ks[k].name + ":empty "; continue; }
            float ml = (float)(lum / n), ms = (float)(sat / n), mb = nb > 0 ? (float)(baseLum / nb) : ml;
            if (ml < .15f || ml > .36f || ms > .60f || (mb >= ml && !TidePropsTuning.Marine(k)) || hardRows * 4 > rows) bad += ks[k].name + "(lum " + ml.ToString("F2") + " sat " + ms.ToString("F2") + " base " + mb.ToString("F2") + " hard " + hardRows + "/" + rows + ") ";
        }
        Check("BLEND: every kind's mean luminance in [.15, .36], saturation <= .60, a darker wet base (not for the marine life), no hard square base edge " + bad, bad.Length == 0);
    }

    // ---- live ---------------------------------------------------------------------------------------------------------------

    static void Live(int v)
    {
        TideDirector.SeedOverride = 6100 + v * 13;
        var wb = Fresh(v, 8f);
        var d = Director(wb);
        Check("v" + v + " director with its props atlas", d != null && d.HasPropArt(TidePropsTuning.Lighthouse) && d.HasPropArt(TidePropsTuning.Mantas));
        if (d == null) return;
        float hw = d.ViewHalfWidth;
        bool openSea = TideTuning.OpenSea[v];
        int seen = 0, offContext = 0, onPainted = 0, marineDry = 0, keepOverlap = 0, pieceIn = 0, columnIn = 0, crestIn = 0, pallIn = 0, notPinned = 0, repeat = 0;
        var kinds = new HashSet<int>();
        var known = new HashSet<BackdropPiece>();
        var lastVariant = new Dictionary<int, int>();
        string bad = "";
        bool opening = true;      // (the opening view is dealt in one frame: its copies are seen in pool order, not spawn order)
        float rate = wb.Current.Spec.Rate("mid");
        var all = new List<BackdropPiece>();
        System.Action audit = () =>
        {
            known.RemoveWhere(q => !q.active);      // (a pool slot is reused: a new copy in it is new)
            all.Clear();
            foreach (var p in d.Bigs.items) if (p.active) all.Add(p);
            foreach (var p in d.Props.items) if (p.active) all.Add(p);
            foreach (var p in all)
            {
                float keep = d.PropKeep(p.kind);
                if (!known.Contains(p))
                {
                    known.Add(p);
                    seen++; kinds.Add(p.kind);
                    if (!TidePropsTuning.OnContext(d.Planner, p.kind, p.x, p.gy, hw, d.ShoalBuilt)) { offContext++; if (bad.Length < 300) bad += TidePropsTuning.Kinds[p.kind].name + "@ctx "; }
                    float r = keep * TidePropsTuning.ContextShare;
                    if (d.Planner.Match(p.x, p.gy, r, r, TidePropsTuning.PaintedOn(d.ShoalBuilt)) > 0f) onPainted++;
                    if (TidePropsTuning.Marine(p.kind) && d.Planner.Match(p.x, p.gy, r, r, GroundClass.Water) < 1f) marineDry++;
                    int last;
                    if (!opening && lastVariant.TryGetValue(p.kind, out last) && last == p.tier && d.PropVariants(p.kind) > 1) repeat++;
                    lastVariant[p.kind] = p.tier;
                }
                if (Mathf.Abs((float)(p.y + d.Planner.Travel - p.gy)) > 1e-3f || p.rate != rate || p.body.localScale != Vector3.one || p.body.localRotation != Quaternion.identity) notPinned++;
                foreach (var q in all)
                    if (q != p && q.GetHashCode() > p.GetHashCode() &&
                        Mathf.Abs(q.x - p.x) < (keep + d.PropKeep(q.kind)) * .98f && System.Math.Abs(q.gy - p.gy) < (keep + d.PropKeep(q.kind)) * .98f) keepOverlap++;
                foreach (var pool in PiecePools(d))
                    foreach (var q in pool.items)
                    {
                        if (!q.active || q.sr.sprite == null) continue;
                        var rule = TideTuning.Rule(q.sr.sprite.name, openSea);
                        if (Mathf.Abs(q.x - p.x) < keep + GroundPlanner.HalfX(rule, q.size) - 1e-3f && System.Math.Abs(q.gy - p.gy) < keep + GroundPlanner.HalfY(rule, q.size) - 1e-3f)
                        { pieceIn++; if (bad.Length < 300) bad += q.sr.sprite.name + "@" + TidePropsTuning.Kinds[p.kind].name + " "; }
                        if (d.PlumeBoxTouchesProp(q.x, q.gy, Mathf.Min(q.size, BackdropCatalog.MaxLandmarkSize))) columnIn++;
                    }
                foreach (var c in d.Crests.items)
                {
                    if (!c.active) continue;
                    float kc = c.size * TideTuning.CrestKeep;
                    if (Mathf.Abs(c.x - p.x) < keep + kc - 1e-3f && System.Math.Abs(c.gy - p.gy) < keep + kc * .8f - 1e-3f) crestIn++;
                }
                foreach (var pa in d.Palls.items)
                {
                    if (!pa.active) continue;
                    float h = pa.size * TideTuning.PallKeep;
                    double pgy = pa.gy + pa.vy * pa.age;
                    if (Mathf.Abs(pa.x - p.x) < keep + h && System.Math.Abs(pgy - p.gy) < keep + h) pallIn++;
                }
            }
        };
        audit();
        opening = false;
        Run(wb, 240f, audit);
        Check("v" + v + ": " + seen + " props over 240 s (" + kinds.Count + " kinds)", seen >= 10 && kinds.Count >= 7);   // (the ground creeps at .49: ~10 copies in four minutes, as Desert's)
        Check("v" + v + ": every prop on its own ground (water / shoal; deep-water kinds on water), marine life only in water, none on a painted deck / block / feature (" +
              offContext + " off, " + onPainted + " painted, " + marineDry + " dry) " + bad, offContext == 0 && onPainted == 0 && marineDry == 0);
        Check("v" + v + ": no two keep-outs overlap (" + keepOverlap + ")", keepOverlap == 0);
        Check("v" + v + ": pinned to the ground (rate, y + travel = gy), never mirrored or turned (" + notPinned + ")", notPinned == 0);
        Check("v" + v + ": no copy wears the previous copy's variant of its kind (" + repeat + ")", repeat == 0);
        Object.DestroyImmediate(wb.gameObject);
        TideDirector.SeedOverride = 0;
    }

    // ---- forced overlap -----------------------------------------------------------------------------------------------------

    static void Forced()
    {
        int planted = 0, clusters = 0, clusterBad = 0, sitesAsked = 0, sitesBad = 0, crestsAsked = 0, crestsTaken = 0, crestsOut = 0, columnBad = 0, pallBad = 0, pallsSeen = 0;
        int refused = 0, refusedAsked = 0, crestsIn = 0;
        foreach (int v in new[] { 1, 2, 3, 4 })
        {
            TideDirector.SeedOverride = 7300 + v;
            var wb = Fresh(v, .25f);
            var d = Director(wb);
            Run(wb, 8f);
            d.Showcase("oilrig_00", .6f, 9f, 9f);      // every piece and prop cleared, nothing else spawns
            d.ClearProps(); d.HoldProps(1e6f);
            float hw = d.ViewHalfWidth;
            bool openSea = TideTuning.OpenSea[v];
            double gy = d.Planner.GroundY(0f);
            // a prop cannot be planted into another's keep-out
            var a = d.PlantProp(TidePropsTuning.Atoll, 0f, gy);
            var b = d.PlantProp(TidePropsTuning.Lighthouse, d.PropKeep(TidePropsTuning.Atoll) * .5f, gy + d.PropKeep(TidePropsTuning.Atoll) * .3f);
            var c = d.PlantProp(TidePropsTuning.Lighthouse, d.PropKeep(TidePropsTuning.Atoll) + d.PropKeep(TidePropsTuning.Lighthouse) + .01f, gy);
            Check("v" + v + ": a prop cannot be planted into another's keep-out (" + (a != null) + ", into it " + (b != null) + ", beside it " + (c != null) + ")", a != null && b == null && c != null);
            if (a == null) { Object.DestroyImmediate(wb.gameObject); continue; }
            planted++;
            float keep = d.PropKeep(TidePropsTuning.Atoll);
            // pieces, clusters and pipe runs dealt onto it
            for (int i = 0; i < 40; i++)
            {
                double lo = gy - keep * 1.2, hi = gy + keep * 1.2;
                for (int kind = 0; kind <= 4; kind++)
                {
                    clusters++;
                    d.SpawnCluster(lo, hi, kind);
                }
                foreach (var pool in PiecePools(d))
                    foreach (var q in pool.items)
                    {
                        if (!q.active || q.sr.sprite == null) continue;
                        var rule = TideTuning.Rule(q.sr.sprite.name, openSea);
                        if (Mathf.Abs(q.x - a.x) < keep + GroundPlanner.HalfX(rule, q.size) - 1e-3f && System.Math.Abs(q.gy - a.gy) < keep + GroundPlanner.HalfY(rule, q.size) - 1e-3f) clusterBad++;
                        if (d.PlumeBoxTouchesProp(q.x, q.gy, Mathf.Min(q.size, BackdropCatalog.MaxLandmarkSize))) columnBad++;
                    }
                foreach (var pa in d.Palls.items)
                {
                    if (!pa.active) continue;
                    pallsSeen++;
                    float h = pa.size * TideTuning.PallKeep;
                    for (float t = 0f; t <= pa.life; t += 1f)
                        if (Mathf.Abs(pa.x + pa.vx * t - a.x) < keep + h && System.Math.Abs(pa.gy + pa.vy * t - a.gy) < keep + h) pallBad++;
                }
                // (a launch site asked for, again and again, on the prop's row)
                for (int s = 0; s < TideTuning.SiteNames.Length; s++)
                {
                    sitesAsked++;
                    d.SpawnSite(d.Planner.Y(gy), s);
                    foreach (var q in d.Sites.items)
                    {
                        if (!q.active || q.sr.sprite == null) continue;
                        var rule = TideTuning.Rule(q.sr.sprite.name, openSea);
                        if (Mathf.Abs(q.x - a.x) < keep + GroundPlanner.HalfX(rule, q.size) - 1e-3f && System.Math.Abs(q.gy - a.gy) < keep + GroundPlanner.HalfY(rule, q.size) - 1e-3f) sitesBad++;
                    }
                }
                // clear the pieces for the next round (the prop stays); the pieces' footprints are released
                foreach (var pool in PiecePools(d))
                    foreach (var q in pool.items)
                        if (q.active) { d.Planner.Release(q.footprint); q.footprint = -1; q.Show(false); }
            }
            // crests forced onto it, all round it and beyond
            for (int i = 0; i < 24; i++)
            {
                float ang = i * Mathf.PI / 12f;
                bool inside = i % 2 == 0;     // (inside: on the prop; outside: just beyond its keep-out, where a crest may live)
                float rr = inside ? keep * (.3f + .05f * i) : keep + TideTuning.CrestMax * .7f;
                crestsAsked++;
                if (d.TryCrestAt(a.x + Mathf.Cos(ang) * rr, a.gy + Mathf.Sin(ang) * rr * (inside ? .8f : 1f)))
                { crestsTaken++; if (inside) crestsIn++; }
            }
            foreach (var cr in d.Crests.items)
            {
                if (!cr.active) continue;
                float kc = cr.size * TideTuning.CrestKeep;
                if (Mathf.Abs(cr.x - a.x) < keep + kc && System.Math.Abs(cr.gy - a.gy) < keep + kc * .8f) crestsOut++;
            }
            // a prop forced onto the live crests / a pall's path is refused where it would touch
            foreach (var cr in d.Crests.items)
            {
                if (!cr.active) continue;
                refusedAsked++;
                if (d.PlantProp(TidePropsTuning.Dock, cr.x, cr.gy) == null) refused++;
            }
            Object.DestroyImmediate(wb.gameObject);
        }
        TideDirector.SeedOverride = 0;
        Check("OVERLAP (forced): " + planted + " props planted, " + clusters + " clusters / pipe runs and " + sitesAsked + " launch sites forced onto them: " +
              clusterBad + " pieces in a keep-out, " + sitesBad + " sites, " + columnBad + " smoke columns through one, " + pallBad + " pall drifts through one (" + pallsSeen + " palls seen)",
              planted == 4 && clusterBad == 0 && sitesBad == 0 && columnBad == 0 && pallBad == 0);
        Check("OVERLAP (forced): " + crestsAsked + " crests asked for round the props (" + crestsTaken + " taken, " + crestsIn + " of them on a prop), none in a keep-out (" + crestsOut + "); " +
              refused + " of " + refusedAsked + " props planted on a live crest were refused", crestsOut == 0 && crestsIn == 0 && crestsTaken >= 1 && refused == refusedAsked);
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
        int animated = 0, active = 0;
        var before = new Dictionary<BackdropPiece, Sprite>();
        foreach (var p in d.Props.items) if (p.active) { before[p] = p.sr.sprite; active++; }
        Run(wb, 1f);
        foreach (var kv in before) if (kv.Key.active && kv.Key.sr.sprite != kv.Value) animated++;
        Check("the props animate from their age (" + animated + " of " + active + " changed cell in 1 s)", active == 0 || animated >= 1);
        long control;
        bool meter = TestHarness.AllocMeterWorks(out control);
        long used = TestHarness.AllocatedBytes(() => Run(wb, 60f));
        Check("the props allocate nothing per frame (" + used + " bytes)", meter && used >= 0 && used <= 256);
        Object.DestroyImmediate(wb.gameObject);
    }
}
