using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

// Every elite banks with its own drawings, and its engines follow them (user, Oct 25: "have Codex create the left and right
// animation frames ... make sure the left and right frames' jet engines are correctly behaving too"; Space's four got Codex
// bank drawings with animated 3-drawing bank loops; the white outline on three Frost elites was peeled off).
//
//  - every def has bank cells (Space: bank loops 17-19 / 20-22 in the <key>_banks companion strip), the lights strip covers
//    every hull cell (the bank drawings included), and nothing in the cell map runs past the strip
//  - every nozzle carries per-cell offsets (ElitePoint.offsets) for every cell, and on every flight, bank and lift-off cell the
//    moved point lands on the drawing (the bell / its plume), well inside the cell
//  - no elite drawing carries a baked pale outline ring (the old outline pass's world tones on the silhouette's edge)
//  - the Codex source copies and the Resources copies of the changed strips are byte-identical (EliteArtSync copies byte for byte)
//  - in play: sliding sideways plays the bank loop (animated where it has one), the engine glows sit on the displayed cell's bells,
//    the lights wear the same cell, and straight flight brings the hover cell back
//
//   Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod EliteBankTest.Run
public static class EliteBankTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[EBK] PASS  " : "[EBK] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 30f;
    static readonly string[] SpaceKeys = { "space_elite_rift_lancer", "space_elite_eventide_bastion", "space_elite_orbit_reaver", "space_elite_singularity_hauler" };
    static readonly string[] Unoutlined = { "frost_elite_floe_harrower", "frost_elite_glacier_tender", "frost_elite_whiteout_sentinel" };

    public static int Execute()
    {
        fails = 0;
        CellMaps();
        Nozzles();
        NoOutlineRing();
        SourceCopies();
        InPlay();
        Debug.Log("[EBK] failures: " + fails);
        return fails;
    }

    static string Dir(EliteDef d) => "Assets/Art/Resources/Elites/" + EliteArt.WorldFolder(d) + "/";

    // ---- the cell maps ---------------------------------------------------------------------------------------

    static void CellMaps()
    {
        int n = 0;
        foreach (var d in EliteCatalog.All)
        {
            n++;
            var c = d.cells;
            var frames = EliteArt.Frames(d);
            Check(d.key + ": banks with its own drawings (left " + c.bankLeft + ", right " + c.bankRight + "), not its hover cell",
                  c.Banks && c.bankLeft != c.bankRight && System.Array.IndexOf(c.flight, c.bankLeft) < 0 && System.Array.IndexOf(c.flight, c.bankRight) < 0);
            Check(d.key + ": the strip and its companions hold every mapped cell (" + (frames != null ? frames.Length : 0) + " >= " + c.Count + ")",
                  frames != null && frames.Length >= c.Count);
            var lights = EliteArt.ExtraFrames(d, EliteArt.Extra.Lights);
            if (lights != null)
                Check(d.key + ": the lights cover every hull cell, the bank drawings included (" + lights.Length + " vs " + (frames != null ? frames.Length : 0) + ")",
                      frames != null && lights.Length == frames.Length);
            int[] l = c.BankLoop(-1), r = c.BankLoop(1);
            Check(d.key + ": its bank loops start on the bank cells and stay out of the flight loop",
                  l != null && r != null && l[0] == c.bankLeft && r[0] == c.bankRight && System.Array.TrueForAll(l, x => System.Array.IndexOf(c.flight, x) < 0 && c.IsBankCell(x)) &&
                  System.Array.TrueForAll(r, x => System.Array.IndexOf(c.flight, x) < 0 && c.IsBankCell(x)));
        }
        Check("all 36 elites walked (" + n + ")", n >= 36);
        foreach (var k in SpaceKeys)
        {
            var d = EliteCatalog.Find(k);
            if (d == null) { Check(k + " is defined", false); continue; }
            var c = d.cells;
            Check(k + ": Codex bank drawings, each an animated 3-drawing loop (17-19 left, 20-22 right) in the <key>_banks companion",
                  c.bankLeftLoop != null && c.bankLeftLoop.Length == 3 && c.bankLeftLoop[0] == 17 && c.bankRightLoop != null && c.bankRightLoop.Length == 3 &&
                  c.bankRightLoop[0] == 20 && File.Exists(Dir(d) + k + "_banks.png") && File.Exists(Dir(d) + k + "_lights_banks.png"));
            var t = Read(Dir(d) + k + "_banks.png");
            Check(k + ": the bank strip is six square cells of " + d.cellPixels + " px, under 4096 px wide (no import downscale)",
                  t != null && t.height == d.cellPixels && t.width == 6 * d.cellPixels);
            if (t != null) Object.DestroyImmediate(t);
        }
    }

    // ---- per-cell nozzle mounts ------------------------------------------------------------------------------

    // The hull strip and its companions as one list of cells (EliteArt.Companions order), read from disk.
    struct Strip { public Color32[] px; public int w, h; }
    static List<Strip> Cells(EliteDef d, out int count)
    {
        var parts = new List<Strip>();
        count = 0;
        foreach (string suffix in new[] { "" }.Concat(EliteArt.Companions))
        {
            var t = Read(Dir(d) + d.key + suffix + ".png");
            if (t == null) continue;
            parts.Add(new Strip { px = t.GetPixels32(), w = t.width, h = t.height });
            count += t.width / t.height;
            Object.DestroyImmediate(t);
        }
        return parts;
    }

    static bool Painted(List<Strip> parts, int cell, int x, int y, int reach)
    {
        foreach (var s in parts)
        {
            int n = s.w / s.h;
            if (cell >= n) { cell -= n; continue; }
            for (int dy = -reach; dy <= reach; dy++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int px = x + dx, py = y + dy;
                    if (px < 0 || py < 0 || px >= s.h || py >= s.h) continue;
                    if (s.px[(s.h - 1 - py) * s.w + cell * s.h + px].a > 128) return true;
                }
            return false;
        }
        return false;
    }

    static void Nozzles()
    {
        foreach (var d in EliteCatalog.All)
        {
            int count;
            var parts = Cells(d, out count);
            var c = d.cells;
            var check = new List<int>(c.flight);
            check.AddRange(c.BankLoop(-1) ?? new int[0]);
            check.AddRange(c.BankLoop(1) ?? new int[0]);
            if (c.liftoff >= 0) check.Add(c.liftoff);
            bool sized = true, on = true, inside = true, moves = false;
            string bad = "";
            foreach (var nz in d.nozzles)
            {
                sized &= nz.offsets != null && nz.offsets.Length == 2 * count;
                foreach (int cell in check)
                {
                    Vector2 p = nz.OnCell(cell);
                    int x = Mathf.RoundToInt(p.x), y = Mathf.RoundToInt(p.y);
                    if (!Painted(parts, cell, x, y, 3)) { on = false; if (bad.Length < 120) bad += nz.name + "@" + cell + " "; }
                    inside &= p.x > d.cellPixels * .04f && p.x < d.cellPixels * .96f && p.y > d.cellPixels * .04f && p.y < d.cellPixels * .96f;
                    if (c.IsBankCell(cell) && (p.x != nz.x || p.y != nz.y)) moves = true;
                }
            }
            Check(d.key + ": every nozzle has a mount for each of the " + count + " cells", sized && d.nozzles.Length > 0);
            Check(d.key + ": on every flight, bank and lift-off cell the moved nozzle sits on the drawn bell / plume " + bad, on);
            Check(d.key + ": ... well inside the cell", inside);
            Check(d.key + ": the bank cells move the engine mounts (the hull leans, the bells go with it)", moves);
        }
    }

    // ---- no baked pale outline ring ----------------------------------------------------------------------------

    // The old outline pass's tones (Art/Enemies/src~/ship_scale_border~/add_border.py TONE), drawn 1-3 px outside the hull's ink.
    static readonly Color32[] RingTones =
    {
        new Color32(221, 243, 255, 255), new Color32(207, 224, 255, 255), new Color32(214, 226, 246, 255), new Color32(210, 244, 230, 255),
        new Color32(246, 226, 176, 255), new Color32(255, 226, 184, 255), new Color32(228, 246, 184, 255),
    };
    // before the fix the three Frost elites had 20-36% of their silhouette edge in the ring colour; every elite strip is now under 0.2%
    const float MaxRingShare = .02f;

    static void NoOutlineRing()
    {
        int strips = 0;
        string worst = "";
        float worstShare = 0f;
        foreach (var d in EliteCatalog.All)
            foreach (string suffix in new[] { "", "_damaged", "_banks", "_death" })
            {
                var t = Read(Dir(d) + d.key + suffix + ".png");
                if (t == null) continue;
                strips++;
                var px = t.GetPixels32();
                int w = t.width, h = t.height, edge = 0, ring = 0;
                Object.DestroyImmediate(t);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        var p = px[y * w + x];
                        if (p.a == 0) continue;
                        bool touches = (x > 0 && px[y * w + x - 1].a == 0) || (x < w - 1 && px[y * w + x + 1].a == 0) ||
                                       (y > 0 && px[(y - 1) * w + x].a == 0) || (y < h - 1 && px[(y + 1) * w + x].a == 0);
                        if (!touches) continue;
                        edge++;
                        foreach (var r in RingTones)
                            if (Mathf.Abs(p.r - r.r) + Mathf.Abs(p.g - r.g) + Mathf.Abs(p.b - r.b) <= 6) { ring++; break; }
                    }
                float share = edge > 0 ? ring / (float)edge : 0f;
                if (share > worstShare) { worstShare = share; worst = d.key + suffix; }
                Check(d.key + suffix + ": no pale outline ring on the silhouette (" + ring + " of " + edge + " edge px)", share < MaxRingShare);
            }
        Check("every elite strip was scanned for the outline ring (" + strips + ", worst " + worst + " " + (worstShare * 100f).ToString("0.00") + "%)", strips >= 36 * 2);
    }

    // ---- Codex source == Resources copy for everything this change touched -------------------------------------

    static void SourceCopies()
    {
        var pairs = new List<string>();
        foreach (var k in Unoutlined) { pairs.Add("Frost/" + k + ".png"); pairs.Add("Frost/" + k + "_damaged.png"); }
        foreach (var k in SpaceKeys)
            foreach (var s in new[] { "_banks", "_lights", "_lights_damaged", "_lights_banks" }) pairs.Add("Space/" + k + s + ".png");
        foreach (var rel in pairs)
        {
            string src = "Assets/Art/Enemies/Elite/" + rel, live = "Assets/Art/Resources/Elites/" + rel;
            Check(rel + ": the Resources copy is byte-identical to the Codex source", File.Exists(src) && File.Exists(live) &&
                  File.ReadAllBytes(src).SequenceEqual(File.ReadAllBytes(live)));
        }
    }

    // ---- in play ----------------------------------------------------------------------------------------------

    static Transform pilot;

    static void Fresh()
    {
        EliteSystem.Clear();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .05f;
        collisionDetection.lifeCounter = 0;
        collisionDetection.atomCheck = false;
        collisionDetection.cloakTimer = 0f;
        PlayerInvuln.Reset();
        RunScore.BeginRun(true, true);
        pilot = new GameObject("~Pilot").transform;
        pilot.position = new Vector3(0f, -3f, 0f);
        EliteSystem.PlayerOverride = pilot;
        LandingSites.Override = null;
        Random.InitState(1234);
    }

    static void Step(float seconds, System.Action each)
    {
        for (float t = 0f; t < seconds - 1e-4f; t += Dt)
        {
            each();
            EliteSystem.Step(Dt);
        }
    }

    static void InPlay()
    {
        var keys = new List<string>(SpaceKeys);
        keys.AddRange(Unoutlined);
        keys.Add("desert_elite_caravan_hauler");
        foreach (var k in keys)
        {
            var def = EliteCatalog.Find(k);
            if (def == null) { Check(k + " is defined", false); continue; }
            foreach (int side in new[] { -1, 1 })
            {
                // (a fresh ship per side, mid-board: a long slide would reach the rails)
                Fresh();
                var e = EliteShip.CreateInPlay(def, new Vector2(-side * .8f, 1f));
                e.AttackCooldown = 99f;
                var c = e.Def.cells;
                Step(.4f, () => { e.SetSeen(e.Position + Vector2.down * 2f); e.Velocity = Vector2.zero; });
                int[] loop = c.BankLoop(side);
                var shown = new HashSet<int>();
                bool glowsFollow = true, lightsFollow = true;
                int samples = 0;
                Step(.7f, () =>
                {
                    e.SetSeen(e.Position + Vector2.down * 2f);
                    e.Velocity = new Vector2(side * e.Def.speed, 0f);
                    if (samples++ < 3) return;      // (the frame the slide starts on is still the hover drawing)
                    shown.Add(e.CurrentFrame);
                    for (int i = 0; i < e.NozzleCount; i++)
                        glowsFollow &= (e.GlowLocal(i) - e.Def.NozzleLocal(i, e.CurrentFrame)).sqrMagnitude < 1e-8f && e.MountedCell == e.CurrentFrame;
                    if (e.HasLights && e.LightCell >= 0) lightsFollow &= e.LightCell == e.CurrentFrame;
                });
                string sideName = side < 0 ? "left" : "right";
                bool inLoop = !e.Damaged;
                foreach (int f in shown) inLoop &= System.Array.IndexOf(loop, f) >= 0;
                Check(k + ": sliding " + sideName + " plays its bank drawings (" + string.Join(",", shown) + ")" + (loop.Length > 1 ? ", animated" : ""),
                      inLoop && shown.Count >= Mathf.Min(2, loop.Length));
                Check(k + ": banking " + sideName + ", the engine glows sit on that cell's bells", glowsFollow);
                Check(k + ": banking " + sideName + ", the lights wear the same cell", lightsFollow);
                var back = new List<int>();
                Step(.6f, () => { e.SetSeen(e.Position + Vector2.down * 2f); e.Velocity = Vector2.zero; back.Add(e.CurrentFrame); });
                Check(k + ": flying straight again brings the hover drawings back (" + string.Join(",", back) + ")",
                      !e.Damaged && back.Count > 0 && System.Array.IndexOf(c.flight, back[back.Count - 1]) >= 0);
                Object.DestroyImmediate(e.gameObject);
            }
        }
        EliteSystem.Clear();
        EliteSystem.PlayerOverride = null;
    }

    static Texture2D Read(string path)
    {
        if (!File.Exists(path)) return null;
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        return t.LoadImage(File.ReadAllBytes(path)) ? t : null;
    }
}
