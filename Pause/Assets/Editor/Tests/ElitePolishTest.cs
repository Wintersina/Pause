using System.Collections.Generic;
using System.IO;
using UnityEngine;

// The 2026-10-18 elite polish (user feedback on the Space elite clips): smaller elites, damaged four-engine ships whose engines all
// burn (a damaged burn LOOP instead of one held cell with frozen painted flames), and subtle warning lines.
//
//   * sizes: the Space elites sit inside the lowered size-tier band and are well below the old sizes
//   * damaged burn loop: on every engine of every elite that has one, the flame region under the nozzle changes between the loop's
//     drawings (no engine's flames are held for more than a frame or two); elites WITHOUT a loop are listed (PaintedFlameDamaged)
//   * telegraphs: TelegraphStyle holds the one set of widths / alphas, none above its cap
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod ElitePolishTest.Run
public static class ElitePolishTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[POLISH] PASS  " : "[POLISH] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    static float Hull => shopingShips.ReferenceHullSize * ShipScale.Main;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        EliteCatalog.Reload();
        Sizes();
        DamagedLoops();
        PaintedFlameDamaged();
        Telegraphs();
        Debug.Log("[POLISH] failures: " + fails);
        return fails;
    }

    // ---- sizes ---------------------------------------------------------------------------------------------------------

    // key -> the drawn long side before the shrink (u); the elites must now be at most `maxShare` of that
    static readonly (string key, float old, float maxShare)[] SpaceSizes =
    {
        ("space_elite_eventide_bastion", 2.19f, .76f),
        ("space_elite_singularity_hauler", 2.10f, .76f),
        ("space_elite_rift_lancer", 1.91f, .85f),
        ("space_elite_orbit_reaver", 1.94f, .85f),
    };

    static void Sizes()
    {
        Check("the size-tier floors are 1.5 / 1.7 hulls", Mathf.Approximately(EliteDef.FloorHulls, 1.5f) && Mathf.Approximately(EliteDef.HeavyFloorHulls, 1.7f));
        foreach (var s in SpaceSizes)
        {
            var d = EliteCatalog.Find(s.key);
            if (d == null) { Check(s.key + ": defined", false); continue; }
            float drawn = ShipTierTest.DrawnLongSide(d);
            float floor = d.TierFloorUnits;
            Check(s.key + ": drawn " + drawn.ToString("0.00") + " u (was " + s.old.ToString("0.00") + ") is within the new floor " + floor.ToString("0.00") + " u and at most " + (s.maxShare * 100f).ToString("0") + "% of the old size",
                  drawn >= floor * .97f && drawn <= s.old * s.maxShare + .01f);
            Check(s.key + ": still clearly bigger than the player's ship (" + (drawn / Hull).ToString("0.0") + " x)", drawn >= Hull * 1.5f * .985f);
        }
        foreach (var d in EliteCatalog.All)
        {
            if (d.world != "frost") continue;
            float drawn = ShipTierTest.DrawnLongSide(d);
            Check(d.key + ": Frost elite " + drawn.ToString("0.00") + " u sits in the same band as Space's (<= 1.7 u)", drawn <= 1.7f && drawn >= d.TierFloorUnits * .97f);
        }
    }

    // ---- the damaged burn loop -----------------------------------------------------------------------------------------

    // The strip's pixels (rows bottom-up), the companion `_damaged` strip (cells after the main strip's) joined on.
    public static Color32[] LoadStrip(EliteDef d, out int w, out int h)
    {
        w = h = 0;
        string path = "Assets/Art/Resources/Elites/" + char.ToUpper(d.world[0]) + d.world.Substring(1) + "/" + d.key;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path + ".png"))) return null;
            w = tex.width; h = tex.height;
            var main = tex.GetPixels32();
            if (!File.Exists(path + "_damaged.png")) return main;
            if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path + "_damaged.png")) || tex.height != h) return null;
            var more = tex.GetPixels32();
            int w2 = tex.width, total = w + w2;
            var all = new Color32[total * h];
            for (int y = 0; y < h; y++)
            {
                System.Array.Copy(main, y * w, all, y * total, w);
                System.Array.Copy(more, y * w2, all, y * total + w, w2);
            }
            w = total;
            return all;
        }
        finally { Object.DestroyImmediate(tex); }
    }

    // The pixels of cell `cell` in a box under nozzle (nx, ny) (cell px, y down), as a hash.
    static ulong RegionHash(Color32[] px, int w, int S, int cell, int nx, int ny)
    {
        ulong hsh = 1469598103934665603UL;
        int x0 = Mathf.Max(0, nx - S / 14), x1 = Mathf.Min(S - 1, nx + S / 14), y0 = ny, y1 = Mathf.Min(S - 1, ny + S / 5);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var c = px[(S - 1 - y) * w + cell * S + x];
                hsh = (hsh ^ (ulong)(c.r | (c.g << 8) | (c.b << 16) | (c.a << 24))) * 1099511628211UL;
            }
        return hsh;
    }

    // Flame pixels (bright, opaque) under nozzle (nx, ny) in `cell` that are NOT in the parked cell (the bare hull).
    public static int FlamePixels(Color32[] px, int w, int S, int cell, int parked, int nx, int ny)
    {
        int n = 0;
        int x0 = Mathf.Max(0, nx - S / 20), x1 = Mathf.Min(S - 1, nx + S / 20), y0 = ny + S * 3 / 100, y1 = Mathf.Min(S - 1, ny + S * 22 / 100);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var a = px[(S - 1 - y) * w + cell * S + x];
                var b = px[(S - 1 - y) * w + parked * S + x];
                bool diff = Mathf.Abs(a.r - b.r) > 60 || Mathf.Abs(a.g - b.g) > 60 || Mathf.Abs(a.b - b.b) > 60 || Mathf.Abs(a.a - b.a) > 100;
                if (diff && a.a > 200 && Mathf.Max(a.r, Mathf.Max(a.g, a.b)) > 150) n++;
            }
        return n;
    }

    static void DamagedLoops()
    {
        int checkedShips = 0;
        foreach (var d in EliteCatalog.All)
        {
            var c = d.cells;
            if (!c.HasDamagedLoop) continue;
            int S = d.cellPixels, w, h;
            var px = LoadStrip(d, out w, out h);
            if (px == null) { Check(d.key + ": strip loads", false); continue; }
            Check(d.key + ": the strip holds every damaged-loop cell (" + (w / S) + " cells, needs " + c.Count + ")", w / S >= c.Count);
            int parked = c.Parked;
            bool all = true;
            string log = "";
            for (int n = 0; n < d.nozzles.Length; n++)
            {
                int nx = Mathf.RoundToInt(d.nozzles[n].x), ny = Mathf.RoundToInt(d.nozzles[n].y);
                var hashes = new HashSet<ulong>();
                int worstRun = 0, run = 0; ulong prev = 0;
                int lit = 0;
                for (int i = 0; i < c.damagedLoop.Length * 2; i++)   // (twice round: the wrap counts)
                {
                    int cell = c.damagedLoop[i % c.damagedLoop.Length];
                    ulong hh = RegionHash(px, w, S, cell, nx, ny);
                    hashes.Add(hh);
                    run = i > 0 && hh == prev ? run + 1 : 1; prev = hh; worstRun = Mathf.Max(worstRun, run);
                    if (i < c.damagedLoop.Length && FlamePixels(px, w, S, cell, parked, nx, ny) > 12) lit++;
                }
                // every engine: its flames change between drawings (>= 4 distinct), never the same drawing twice in a row, and burn in most of them
                bool ok = hashes.Count >= 4 && worstRun <= 1 && lit >= c.damagedLoop.Length - 2;
                all &= ok;
                log += " " + d.nozzles[n].name + ":" + hashes.Count + "/" + lit;
            }
            Check(d.key + ": damaged burn loop animates EVERY engine (distinct drawings / lit drawings per nozzle:" + log + ")", all && d.nozzles.Length > 0);
            checkedShips++;
        }
        Check("the Space elites all have a damaged burn loop (" + checkedShips + ")", checkedShips >= 4);
    }

    // The elites with NO damaged loop whose single damaged cell has flames painted into the art (a held drawing: its painted flames sit
    // frozen under the procedural plume). They are listed here so the debt is visible; a new elite must have a loop or be added (never
    // silently). Fixed so far: Space, then each world as its loops land (Space's strips are built by a script from the bare hull: Art/Enemies/src~/space_elites_engine_burn).
    static void PaintedFlameDamaged()
    {
        int painted = 0, withLoop = 0;
        var list = new List<string>();
        foreach (var d in EliteCatalog.All)
        {
            if (d.cells.damaged < 0) continue;
            if (d.cells.HasDamagedLoop) { withLoop++; continue; }
            int S = d.cellPixels, w, h;
            var px = LoadStrip(d, out w, out h);
            if (px == null) continue;
            int best = 0;
            foreach (var nz in d.nozzles)
                best = Mathf.Max(best, FlamePixels(px, w, S, d.cells.damaged, d.cells.Parked, Mathf.RoundToInt(nz.x), Mathf.RoundToInt(nz.y)));
            if (best > 120) { painted++; list.Add(d.key); }
        }
        Debug.Log("[POLISH] held damaged cell with painted flames (no loop yet): " + string.Join(", ", list));
        // The debt only shrinks: each world that gains loops moves its elites from the painted list to withLoop (29 = Space's 4 + 25 listed).
        Check("elites with a damaged burn loop: " + withLoop + " (Space and the worlds wired since); held damaged cells with painted flames still listed: " + painted, withLoop >= 4 && withLoop + painted >= 29);
    }

    // ---- telegraphs ----------------------------------------------------------------------------------------------------

    static void Telegraphs()
    {
        Check("TelegraphStyle: the warning line is " + TelegraphStyle.LineWidth + " u, capped at " + TelegraphStyle.MaxLineWidth + " u (<= 0.03)",
              TelegraphStyle.LineWidth >= .015f && TelegraphStyle.LineWidth <= TelegraphStyle.MaxLineWidth && TelegraphStyle.MaxLineWidth <= .03f + 1e-5f);
        Check("a wide ask is capped (Line(.2) = " + TelegraphStyle.Line(.2f).ToString("0.000") + ")", TelegraphStyle.Line(.2f) <= TelegraphStyle.MaxLineWidth + 1e-6f);
        Check("line alphas are low (" + TelegraphStyle.LineAlphaLow + " .. " + TelegraphStyle.LineAlpha + ", ring " + TelegraphStyle.RingAlphaLow + " .. " + TelegraphStyle.RingAlpha + ", dots " + TelegraphStyle.DotAlpha + ")",
              TelegraphStyle.LineAlpha <= .55f && TelegraphStyle.RingAlpha <= .55f && TelegraphStyle.DotAlpha <= .5f && TelegraphStyle.BoldDotAlpha <= .75f &&
              TelegraphStyle.LineAlphaLow < TelegraphStyle.LineAlpha && TelegraphStyle.RingAlphaLow < TelegraphStyle.RingAlpha);
        // the dotted outlines: the drawn core of a dot is a third of it
        float core = TelegraphStyle.DotSize * TelegraphStyle.BoldDotScale / 3f;
        Check("AttackPreview dots: core " + core.ToString("0.000") + " u even bold (<= the cap), spacing " + AttackPreview.DotSpacing + ", alpha " + AttackPreview.Alpha,
              core <= TelegraphStyle.MaxLineWidth && Mathf.Approximately(AttackPreview.DotSize, TelegraphStyle.DotSize) && AttackPreview.Alpha <= .5f && AttackPreview.BoldAlpha <= .75f);
        var tg = new EliteTelegraph();
        tg.Lane(new Vector2(0f, 2f), new Vector2(0f, -1f), .5f);
        tg.Ring(new Vector2(1f, 0f), .5f);
        tg.Show(1f, Color.white, 1);
        Check("a preview of a lane and a ring paints its dots at DotSize x bold (" + AttackPreview.LastDotSize.ToString("0.000") + " u) and at most " + TelegraphStyle.BoldDotAlpha + " alpha (" + AttackPreview.LastAlpha.ToString("0.00") + ")",
              AttackPreview.LastDotSize <= TelegraphStyle.DotSize * TelegraphStyle.BoldDotScale + 1e-5f && AttackPreview.LastAlpha <= TelegraphStyle.BoldDotAlpha + 1e-4f && tg.Showing);
        // the ring was drawn smaller (RingScale of its radius)
        float maxR = 0f;
        for (int i = 0; i < tg.Shape.LoopLength(1); i++) maxR = Mathf.Max(maxR, Vector2.Distance(tg.Shape.LoopAt(1, i), new Vector2(1f, 0f)));
        Check("a telegraph ring is drawn at " + TelegraphStyle.RingScale + " of its radius (" + maxR.ToString("0.00") + " of .5)", Mathf.Abs(maxR - .5f * TelegraphStyle.RingScale) < .01f);
        tg.End();
        // the marker ring sprite is a hairline: its core line is 2 px of 96 (a ring 1 u across is .02 u thick; one of 1.2 u across .025)
        var ring = EliteFxArt.MarkRing;
        Check("the marker ring sprite is 1 u across and thin (" + ring.bounds.size.x.ToString("0.00") + " u, core line " + (2f / 96f * 1.2f).ToString("0.000") + " u at 1.2 u)",
              Mathf.Abs(ring.bounds.size.x - 1f) < .02f && 2f / 96f * 1.2f <= TelegraphStyle.MaxLineWidth);
        Check("the sight sprite is the hollow-squares cell (1 u wide, a cell + gap tall)", Mathf.Abs(EliteFxArt.Sight.bounds.size.x - 1f) < 1e-4f && EliteFxArt.Sight == BeamKeyline.SquaresSprite && BeamKeyline.SquareEliteMin <= TelegraphStyle.MaxLineWidth * 2f);
    }
}
