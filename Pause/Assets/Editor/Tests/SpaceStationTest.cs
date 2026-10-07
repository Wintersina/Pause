using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Space's stations hold still and blink (SpaceDirector + SpaceStationLights):
// no station cell (long stations, ring habitats, their minis, the moon with
// a station on it) ever turns, tilts or plays a flipbook; every station cell
// has lamps on its own opaque pixels; the lamps blink (on and off both
// happen, stations out of step with each other), freeze while the game is
// paused, ride along with their station, cost nothing per frame; and the
// Space elites' station launch sites still sit on the (now upright) hubs.
//
// (Ring stations used to wheel at 1.2-2.2 deg/s and every station held a
// random +-10 deg tilt; companions circled their planet. Now: upright, no
// spin, a station companion parks on the near side of its planet.)
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod SpaceStationTest.Run
public static class SpaceStationTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[STN] PASS  " : "[STN] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const string Dir = "Assets/Art/Backgrounds/Resources/Worlds/Space/Backdrop/";
    static readonly string[] Cells =
    {
        "station_00", "station_01", "station_02", "station_03",
        "ringstation_00", "ringstation_01", "ringstation_02", "ringstation_03", "moon",
    };

    [System.Serializable] class AtlasRect { public string n; public int x, y, w, h; }
    [System.Serializable] class AtlasManifest { public AtlasRect[] sprites; }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        float savedSpeed = moveBackGround.speed;
        try
        {
            Anchors();
            Patterns();
            Runtime();
        }
        finally
        {
            moveBackGround.speed = savedSpeed;
            Time.timeScale = 1f;
            LandingSites.Override = null;
        }
        Debug.Log("[STN] failures: " + fails);
        return fails;
    }

    // ---- the lamp table, against the art -------------------------------------------

    static void Anchors()
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(File.ReadAllBytes(Dir + "fx.png"));
        var px = tex.GetPixels32();
        int tw = tex.width;
        var m = JsonUtility.FromJson<AtlasManifest>(File.ReadAllText(Dir + "fx.json"));
        var rects = new Dictionary<string, AtlasRect>();
        foreach (var r in m.sprites) rects[r.n] = r;

        int lamps = 0, offArt = 0, missing = 0, fewPatterns = 0, noBeacon = 0, red = 0;
        string bad = "";
        foreach (string cell in Cells)
        {
            SpaceStationLights.Lamp[] list;
            AtlasRect r;
            list = SpaceStationLights.LampsOf(cell);
            if (list == null || !rects.TryGetValue(cell, out r))
            {
                missing++;
                bad += cell + " missing; ";
                continue;
            }
            int floor = cell == "moon" ? 6 : 10;
            if (list.Length < floor) { missing++; bad += cell + " has " + list.Length + " lamps; "; }
            var patterns = new HashSet<int>();
            bool beacon = false;
            foreach (var l in list)
            {
                lamps++;
                patterns.Add(l.pattern);
                beacon |= l.pattern == SpaceStationLights.Beacon;
                // lamp centre -> atlas pixel (texture rows run bottom-up, as the manifest's y)
                int x = Mathf.FloorToInt(r.x + r.w * 0.5f + l.x);
                int y = Mathf.FloorToInt(r.y + r.h * 0.5f + l.y);
                bool inside = x >= r.x && x < r.x + r.w && y >= r.y && y < r.y + r.h;
                if (!inside || px[y * tw + x].a < 128)
                {
                    offArt++;
                    if (bad.Length < 300) bad += cell + " lamp (" + l.x + "," + l.y + ") off the art; ";
                }
                float h, s, v;
                Color.RGBToHSV(SpaceStationLights.Colors[l.color], out h, out s, out v);
                if ((h * 360f >= 345f || h * 360f <= 15f) && s > 0.5f) red++;
            }
            if (patterns.Count < 3) fewPatterns++;
            if (!beacon && cell != "moon") noBeacon++;
        }
        Object.DestroyImmediate(tex);
        Check("every station cell has its lamps (" + Cells.Length + " cells, " + lamps + " lamps) " + bad,
              missing == 0);
        Check("every lamp sits on an opaque pixel of its cell (" + offArt + " off the art)", offArt == 0 && lamps > 0);
        Check("each station mixes at least three blink patterns, and every long / ring station has beacons",
              fewPatterns == 0 && noBeacon == 0);
        Check("no lamp is the player's red (docs/art-style.md)", red == 0);
        bool minis = true;
        foreach (string cell in Cells)
            if (cell != "moon") minis &= SpaceStationLights.CellOf("mini_" + cell) == cell;
        Check("the pre-shrunk minis share their full cell's lamps",
              minis && SpaceStationLights.CellOf("mini_rocky_00") == null && SpaceStationLights.CellOf("giant_00") == null);
    }

    // ---- the blink patterns (CPU mirror of the shader) -----------------------------

    static void Patterns()
    {
        string bad = "";
        for (int pattern = 0; pattern <= SpaceStationLights.Beacon; pattern++)
            for (int k = 0; k < 4; k++)
            {
                float seed = k * 0.27f;
                bool on = false, off = false, stepped = true;
                for (float t = 0f; t < 6f; t += 1f / 60f)
                {
                    float l = SpaceStationLights.Level(pattern, t, seed);
                    on |= l >= 0.99f;
                    off |= l <= 0.15f;
                    // hard steps only: full, a dim step or off
                    stepped &= l == 1f || l == 0.55f || l == 0.5f || l <= 0.15f;
                }
                if (!(on && off && stepped)) bad += "pattern " + pattern + " seed " + seed + " (on " + on + ", off " + off + ", stepped " + stepped + "); ";
            }
        Check("every blink pattern turns fully on and (nearly) off within 6 s, in hard steps " + bad, bad.Length == 0);

        bool seamless = SpaceStationLights.Periods.Length == SpaceStationLights.Beacon + 1;
        foreach (float period in SpaceStationLights.Periods)
        {
            float cycles = SpaceStationLights.ClockWrap / period;
            seamless &= Mathf.Abs(cycles - Mathf.Round(cycles)) < 1e-3f;
        }
        Check("the blink clock's wrap (" + SpaceStationLights.ClockWrap + " s) is a whole number of every period", seamless);
    }

    // ---- the real Space backdrop ---------------------------------------------------

    class Watch
    {
        public BackdropPiece piece;
        public Sprite sprite;
        public float age;               // the piece's age when last seen (a drop: a new life)
        public float worstTurn;         // degrees from upright, root and body
        public bool swapped;
        public Vector3 offset;          // a parked companion: from its planet
        public float drift;
    }

    static void Runtime()
    {
        Time.timeScale = 1f;
        moveBackGround.speed = 0.2f;
        var go = new GameObject("~SpaceStationTest");
        var wb = go.AddComponent<WorldBackdrop>();
        try
        {
            wb.Show("Space", false);
            var sd = wb.Current != null ? wb.Current.Director as SpaceDirector : null;
            Check("Space backdrop runs the SpaceDirector with station lights",
                  sd != null && sd.StationLights != null && sd.StationLights.Material != null &&
                  sd.StationLights.Material.shader.name == "Pause/BackdropStationLights");
            if (sd == null || sd.StationLights == null) return;
            var lights = sd.StationLights;

            const float dt = 1f / 30f;
            var watched = new Dictionary<BackdropPiece, Watch>();
            var done = new List<Watch>();
            int stationsSeen = 0, ringsSeen = 0, companions = 0, lit = 0, unlit = 0, wrongOrder = 0, notChild = 0;
            int wrongScale = 0;
            float worst = 0f, worstDrift = 0f;
            bool swaps = false;
            var siteList = new List<LandingSite>();
            int stationSites = 0, siteErrors = 0;
            string siteBad = "";
            for (int i = 0; i < 12 * 60 * 30; i++)        // 12 minutes of frames
            {
                moveBackGround.speed = Mathf.Repeat(i * 0.0002f, 0.62f);
                wb.Step(dt);
                foreach (var pool in sd.Bodies)
                    foreach (var p in pool.items)
                    {
                        if (!p.active || !SpaceDirector.IsStationArt(p.sr.sprite)) continue;
                        Watch w;
                        if (!watched.TryGetValue(p, out w) || p.age < w.age)
                        {
                            if (w != null) done.Add(w);
                            w = new Watch { piece = p, sprite = p.sr.sprite };
                            if (p.parent != null) w.offset = p.root.position - p.parent.root.position;
                            watched[p] = w;
                            stationsSeen++;
                            if (p.sr.sprite.name.Contains("ring")) ringsSeen++;
                            if (p.parent != null) companions++;
                        }
                        w.age = p.age;
                        if (p.sr.sprite != w.sprite || p.frames != null) w.swapped = true;
                        float turn = Mathf.Max(Quaternion.Angle(p.root.rotation, Quaternion.identity),
                                               Quaternion.Angle(p.body.rotation, Quaternion.identity));
                        w.worstTurn = Mathf.Max(w.worstTurn, turn);
                        if (p.parent != null && p.parent.active)
                            w.drift = Mathf.Max(w.drift, ((p.root.position - p.parent.root.position) - w.offset).magnitude);
                    }

                if (i % 15 != 0) continue;
                foreach (var r in lights.Rigs)
                {
                    var p = r.piece;
                    if (!p.active) continue;
                    bool station = SpaceDirector.IsStationArt(p.sr.sprite);
                    if (station != (r.renderer.enabled && r.cell != null)) unlit++;
                    if (!station) continue;
                    lit++;
                    if (r.renderer.sortingOrder != p.sr.sortingOrder + 1) wrongOrder++;
                    if (r.transform.parent != p.body) notChild++;
                    // a mini carries the full cell's lamps scaled to its bounds
                    Vector3 full = FullSize(wb.Current.Fx, r.cell);
                    Vector3 want = new Vector3(p.sr.sprite.bounds.size.x / full.x, p.sr.sprite.bounds.size.y / full.y, 1f);
                    if (Mathf.Abs(r.transform.localScale.x - want.x) > 1e-3f ||
                        Mathf.Abs(r.transform.localScale.y - want.y) > 1e-3f) wrongScale++;
                }

                siteList.Clear();
                sd.LandingSites(siteList);
                foreach (var s in siteList)
                {
                    if (s.kind != LandingKind.Station) continue;
                    stationSites++;
                    BackdropPiece host = null;
                    foreach (var p in sd.Bodies[1].items) if (p.root == s.anchor) host = p;
                    bool ok = host != null && host.parent == null && SpaceDirector.IsStationArt(host.sr.sprite);
                    if (ok)
                    {
                        Bounds b = host.sr.sprite.bounds;
                        Vector3 want = host.root.position + Vector3.Scale(host.root.lossyScale,
                            new Vector3(b.center.x + SpaceDirector.StationPad.x * b.size.x,
                                        b.center.y + SpaceDirector.StationPad.y * b.size.y, 0f));
                        // upright: the hub is straight below the centre, scaled, no rotation
                        ok = (s.Position - want).sqrMagnitude < 1e-8f && s.anchor.rotation == Quaternion.identity;
                    }
                    if (!ok) { siteErrors++; if (siteBad.Length < 200) siteBad += s.id + " "; }
                }
            }
            done.AddRange(watched.Values);
            foreach (var w in done)
            {
                worst = Mathf.Max(worst, w.worstTurn);
                swaps |= w.swapped;
                worstDrift = Mathf.Max(worstDrift, w.drift);
            }
            Check("stations, ring stations and station companions came by in 12 minutes (" + stationsSeen + " stations, " +
                  ringsSeen + " rings, " + companions + " parked companions)", stationsSeen >= 6 && ringsSeen >= 1);
            Check("no station ever turns: root and body stay upright for life (worst " + worst.ToString("F4") + " deg)",
                  worst < 1e-3f);
            Check("no station plays a flipbook or swaps its cell (stations keep one static frame)", !swaps);
            Check("a station companion holds station by its planet (drift from its parked offset " +
                  worstDrift.ToString("F4") + " u)", worstDrift < 1e-3f);
            Check("every visible station has its lamp renderer on, nothing else does (" + lit + " samples, " + unlit + " wrong)",
                  lit > 0 && unlit == 0);
            Check("lamps ride on the station's body, sort just above it, and scale with a mini (" + wrongOrder + " order, " +
                  notChild + " parent, " + wrongScale + " scale errors)", wrongOrder == 0 && notChild == 0 && wrongScale == 0);
            Check("Space elite station sites sit on the upright stations' hubs (" + stationSites + " site samples, " +
                  siteErrors + " wrong " + siteBad + ")", stationSites > 0 && siteErrors == 0);

            BlinkAndPause(wb, sd, lights);
            ZeroAlloc(wb, sd);
        }
        finally
        {
            Time.timeScale = 1f;
            Object.DestroyImmediate(go);
        }
    }

    static Vector3 FullSize(BackdropAtlas fx, string cell)
    {
        var s = fx.Get(cell);
        return s != null ? s.bounds.size : Vector3.one;
    }

    static List<SpaceStationLights.Rig> LitRigs(SpaceStationLights lights)
    {
        var list = new List<SpaceStationLights.Rig>();
        foreach (var r in lights.Rigs)
            if (r.piece.active && r.cell != null && r.renderer.enabled) list.Add(r);
        return list;
    }

    static void BlinkAndPause(WorldBackdrop wb, SpaceDirector sd, SpaceStationLights lights)
    {
        // Wait (up to 3 minutes) for two stations in view at once.
        var lit = LitRigs(lights);
        for (int i = 0; i < 180 * 30 && lit.Count < 2; i++)
        {
            wb.Step(1f / 30f);
            if (i % 10 == 0) lit = LitRigs(lights);
        }
        Check("two stations blink side by side at some point (" + lit.Count + ")", lit.Count >= 2);
        if (lit.Count == 0) return;

        // The renderer is fed this rig's clock; the stations are out of step.
        var mpb = new MaterialPropertyBlock();
        bool fed = true;
        var times = new HashSet<float>();
        foreach (var r in lit)
        {
            r.renderer.GetPropertyBlock(mpb);
            fed &= Mathf.Abs(mpb.GetFloat("_BlinkTime") - lights.TimeOf(r)) < 1e-4f && mpb.GetFloat("_Size") > 0f;
            times.Add(Mathf.Round(lights.TimeOf(r) * 100f));
        }
        Check("each station's lamps are fed its own blink clock, sized, through the property block", fed);
        Check("stations blink out of step with each other (" + times.Count + " distinct clocks for " + lit.Count + ")",
              times.Count == lit.Count);

        // Over 5 s every station shows lamps fully on and lamps off.
        var r0 = lit[0];
        var lamps = SpaceStationLights.LampsOf(r0.cell);
        bool on = false, off = false, changes = false;
        float prev = -1f;
        for (int i = 0; i < 150; i++)
        {
            wb.Step(1f / 30f);
            float t = lights.TimeOf(r0);
            float sum = 0f;
            foreach (var l in lamps)
            {
                float k = SpaceStationLights.Level(l.pattern, t, l.seed);
                on |= k >= 0.99f;
                off |= k <= 0.15f;
                sum += k;
            }
            if (prev >= 0f && sum != prev) changes = true;
            prev = sum;
        }
        Check("a station's lamps blink: on and off both within 5 s, and the picture changes", on && off && changes);

        // Paused: the blink clock, every lamp and every station hold still.
        Time.timeScale = 0f;
        float c0 = lights.Clock;
        var before = new List<Vector3>();
        foreach (var r in lit) before.Add(r.transform.position);
        var blink0 = new List<float>();
        foreach (var r in lit) { r.renderer.GetPropertyBlock(mpb); blink0.Add(mpb.GetFloat("_BlinkTime")); }
        for (int i = 0; i < 90; i++) wb.Step(1f / 30f);
        bool still = Mathf.Approximately(lights.Clock, c0);
        for (int i = 0; i < lit.Count; i++)
        {
            lit[i].renderer.GetPropertyBlock(mpb);
            still &= mpb.GetFloat("_BlinkTime") == blink0[i] && lit[i].transform.position == before[i];
        }
        Check("while paused (timeScale 0) the lamps freeze mid-blink with their stations", still);
        Time.timeScale = 1f;
        wb.Step(1f / 30f);
        Check("... and blink on when time resumes", lights.Clock != c0);
    }

    static void ZeroAlloc(WorldBackdrop wb, SpaceDirector sd)
    {
        for (int i = 0; i < 60; i++) wb.Step(1f / 30f);
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 120 * 30; i++)
        {
            moveBackGround.speed = Mathf.Repeat(i * 0.0002f, 0.62f);
            wb.Step(1f / 30f);
        }
        long used = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check("stations and their lamps allocate nothing over 2 minutes of frames and spawns (" + used + " bytes)", used == 0);
    }
}
