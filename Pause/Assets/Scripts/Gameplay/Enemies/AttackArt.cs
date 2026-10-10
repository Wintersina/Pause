using System.Collections.Generic;
using UnityEngine;

// THE LOADER FOR THE THEMED ATTACK ART (docs/world-attacks-art.md).
//
// Codex's atlases live at Resources/Attacks/<World>/<w>_attack_<file>.png
// (64 px authored, shipped x2 on a 128 px cell, nearest-neighbour, 128 px per
// world unit). This class reads one by (world, file), slices its cells into
// sprites (point-filtered, pivot centre, 128 px per unit) and caches them:
// nothing is made per shot or per frame after the first look-up.
//
// EVERY LOOK-UP HAS A FALLBACK. A missing file, or a cell outside the grid, is
// null -- never an exception -- and the caller keeps its procedural drawing
// (ShotSkins.Procedural, the EliteFx particles), so code ships and passes its
// tests before the art does, and a world whose art is half delivered still
// plays. Space has no files at all (its attacks are procedural by design).
//
// Files and grids (docs/world-attacks-art.md section 3):
//   shots   8 x 2 cells of 128         ShotCellName: row 0 bolt a,b shard a,b shell a,b slag a,b; row 1 pool a,b sigA sigB sigC
//   fx      8 x 2 cells of 128
//   strike  6 x 128x384 + 6 x 128x128 (Frost, Ember, Tide)
//   jet     Ember 6 x 128x256 + 6 x 128; Tide 6 x 128x320 + 6 x 128
//   wave    512 x 480 (four 512x96 bodies, one 96 cell row)
//   ring    8 x 128 (Frost)
//   beam    8 x 128 (every themed world): the mine laser's skin
//   lash    8 x 128 (Verdant, Tide)
//   log     4 x 2 cells of 256x128 (Verdant)
public static class AttackArt
{
    public const float PixelsPerUnit = 128f;     // a 128 px cell is 1 u
    public const float ThemedDrawScale = 2f;     // themed shots are drawn 2x their nominal size; the hit radius is unchanged
    public const string Folder = "Attacks";

    public enum ShotCellName
    {
        BoltA, BoltB, ShardA, ShardB, ShellA, ShellB, SlagA, SlagB,
        PoolA, PoolB, SigAA, SigAB, SigBA, SigBB, SigCA, SigCB,
    }

    // world keys, matching ShotSkins.WorldKeys; Space has no atlases
    public static string KeyOf(int world) => ShotSkins.WorldKeys[Mathf.Clamp(world, 0, ShotSkins.WorldKeys.Length - 1)];
    public static string FolderOf(int world) => Folder + "/" + char.ToUpperInvariant(KeyOf(world)[0]) + KeyOf(world).Substring(1);
    public static string PathOf(int world, string file) => FolderOf(world) + "/" + KeyOf(world) + "_attack_" + file;

    static readonly Dictionary<string, Texture2D> atlases = new Dictionary<string, Texture2D>();
    static readonly Dictionary<string, Sprite> cells = new Dictionary<string, Sprite>();
    // tests: an atlas served in place of the Resources file
    static readonly Dictionary<string, Texture2D> injected = new Dictionary<string, Texture2D>();

    public static Texture2D Atlas(int world, string file)
    {
        string path = PathOf(world, file);
        Texture2D t;
        if (injected.TryGetValue(path, out t)) return t;
        if (atlases.TryGetValue(path, out t)) return t;   // (a miss is cached too: null)
        t = Resources.Load<Texture2D>(path);
        atlases[path] = t;
        return t;
    }

    public static bool Has(int world, string file) => Atlas(world, file) != null;

    // The sprite at (col, row) of a grid of cellW x cellH pixel cells, rows counted from the TOP of the image.
    // Null if the atlas is missing or the cell is outside it.
    public static Sprite Cell(int world, string file, int col, int row, int cellW = 128, int cellH = 128)
    {
        var tex = Atlas(world, file);
        if (tex == null) return null;
        int x = col * cellW, yTop = row * cellH;
        if (col < 0 || row < 0 || x + cellW > tex.width || yTop + cellH > tex.height) return null;
        string key = PathOf(world, file) + "#" + col + "," + row + "," + cellW + "x" + cellH;
        Sprite s;
        if (cells.TryGetValue(key, out s) && s != null) return s;
        tex.filterMode = FilterMode.Point;
        s = Sprite.Create(tex, new Rect(x, tex.height - yTop - cellH, cellW, cellH), new Vector2(.5f, .5f), PixelsPerUnit, 0, SpriteMeshType.FullRect);
        s.name = key;
        cells[key] = s;
        return s;
    }

    // A sprite at an exact pixel rectangle (x, yTop counted from the TOP of the image): for the strips whose rows are not
    // a multiple of their cell height (the jet's nozzle row, the wave's cap row). Null if missing or outside.
    public static Sprite CellAt(int world, string file, int x, int yTop, int w, int h)
    {
        var tex = Atlas(world, file);
        if (tex == null) return null;
        if (x < 0 || yTop < 0 || x + w > tex.width || yTop + h > tex.height) return null;
        string key = PathOf(world, file) + "@" + x + "," + yTop + "," + w + "x" + h;
        Sprite s;
        if (cells.TryGetValue(key, out s) && s != null) return s;
        tex.filterMode = FilterMode.Point;
        s = Sprite.Create(tex, new Rect(x, tex.height - yTop - h, w, h), new Vector2(.5f, .5f), PixelsPerUnit, 0, SpriteMeshType.FullRect);
        s.name = key;
        cells[key] = s;
        return s;
    }

    // A cell of the shots atlas by its role.
    public static Sprite ShotCell(int world, ShotCellName name)
    {
        int i = (int)name;
        return Cell(world, "shots", i % 8, i / 8);
    }

    // The mine laser's skin (docs/world-attacks-art.md 3.7): aim, beam a/b, flash a/b, spark a-c. Null: use MineLaserArt's own.
    public static Sprite[] Beam(int world)
    {
        var tex = Atlas(world, "beam");
        if (tex == null) return null;
        var list = new Sprite[8];
        for (int i = 0; i < 8; i++)
        {
            list[i] = Cell(world, "beam", i, 0);
            if (list[i] == null) return null;
        }
        return list;
    }

    // ember_attack_mineflame.png (768 x 768, docs/world-attacks-art.md 3.7b): the Ember mine's flame-thrower. Row 0: six 128 x 384 beam
    // frames (hot at the bottom = the muzzle, flowing up); row 1 (y 384): pilot flame a-d (the charge-up, base at the cell's bottom, tip up),
    // aim a,b (a 128 tile, content 12 px wide); row 2 (y 512): muzzle burst a-d, impact a,b; row 3 (y 640): impact c, haze a,b (a soft warm
    // veil, 128 x 128), three reserved. Any missing cell leaves that slot procedural (MineFlameArt); null when the file is missing.
    public static MineFlameArt MineFlame(int world)
    {
        if (!Has(world, "mineflame")) return null;
        var a = new MineFlameArt();
        a.painted = true;
        var proc = MineFlameArt.Build();
        for (int i = 0; i < a.beam.Length; i++) a.beam[i] = Cell(world, "mineflame", i, 0, 128, 384) ?? proc.beam[i];
        for (int i = 0; i < a.pilot.Length; i++) a.pilot[i] = CellAt(world, "mineflame", i * 128, 384, 128, 128) ?? proc.pilot[i];
        a.sight = CellAt(world, "mineflame", 4 * 128, 384, 128, 128) ?? proc.sight;
        for (int i = 0; i < a.flash.Length; i++) a.flash[i] = CellAt(world, "mineflame", i * 128, 512, 128, 128) ?? proc.flash[i];
        a.spark[0] = CellAt(world, "mineflame", 4 * 128, 512, 128, 128) ?? proc.spark[0];
        a.spark[1] = CellAt(world, "mineflame", 5 * 128, 512, 128, 128) ?? proc.spark[1];
        a.spark[2] = CellAt(world, "mineflame", 0, 640, 128, 128) ?? proc.spark[2];
        a.haze[0] = CellAt(world, "mineflame", 128, 640, 128, 128) ?? proc.haze[0];
        a.haze[1] = CellAt(world, "mineflame", 256, 640, 128, 128) ?? proc.haze[1];
        return a;
    }

    // space_attack_laser (1024 x 384, 8 x 3 cells of 128), space_attack_laserbody (512 x 256: four 128 x 256 beam frames, root at the TOP)
    // and space_attack_lasertell (512 x 128: sight a,b then lock a,b). Null when any of the three files is missing (BossBeam then draws
    // the generic boss beam).
    //   laser row 0: windup 1-8 (energy gathering at the pod); row 1: muzzle a-d (loop) then fade 1-4; row 2: impact 1-4 then spark a-d
    public sealed class SpaceLaserArt
    {
        public readonly Sprite[] windup = new Sprite[8], muzzle = new Sprite[4], fade = new Sprite[4], impact = new Sprite[4],
                                 spark = new Sprite[4], body = new Sprite[4], sight = new Sprite[2], lockOn = new Sprite[2];
    }

    public const int SpaceLaserBodyOpaquePx = 72;   // the opaque columns of a 128 px wide beam frame (px 28..99): the part that stays in the hit shape

    static SpaceLaserArt spaceLaser;

    public static SpaceLaserArt SpaceLaser(int world = 0)
    {
        if (spaceLaser != null && spaceLaser.body[0] != null) return spaceLaser;
        if (!Has(world, "laser") || !Has(world, "laserbody") || !Has(world, "lasertell")) return null;
        var a = new SpaceLaserArt();
        for (int i = 0; i < 8; i++) if ((a.windup[i] = Cell(world, "laser", i, 0)) == null) return null;
        for (int i = 0; i < 4; i++)
        {
            a.muzzle[i] = Cell(world, "laser", i, 1);
            a.fade[i] = Cell(world, "laser", 4 + i, 1);
            a.impact[i] = Cell(world, "laser", i, 2);
            a.spark[i] = Cell(world, "laser", 4 + i, 2);
            a.body[i] = Cell(world, "laserbody", i, 0, 128, 256);
            if (a.muzzle[i] == null || a.fade[i] == null || a.impact[i] == null || a.spark[i] == null || a.body[i] == null) return null;
        }
        for (int i = 0; i < 2; i++)
        {
            a.sight[i] = Cell(world, "lasertell", i, 0);
            a.lockOn[i] = Cell(world, "lasertell", 2 + i, 0);
            if (a.sight[i] == null || a.lockOn[i] == null) return null;
        }
        return spaceLaser = a;
    }

    // ---- the cores' slots (plan phases 1c / 1d) --------------------------------------------------------
    // frost_attack_ring.png (8 x 128): bar x4, gapMarker a,b, glyph a,b
    public static Sprite RingBar(int world, int i) => Cell(world, "ring", Mathf.Clamp(i, 0, 3), 0);
    public static Sprite RingGapMarker(int world, int i) => Cell(world, "ring", 4 + Mathf.Clamp(i, 0, 1), 0);
    public static Sprite RingGlyph(int world, int i) => Cell(world, "ring", 6 + Mathf.Clamp(i, 0, 1), 0);
    // <w>_attack_strike.png (768 x 512): row 0 six 128 x 384 column frames (24 fps over the live window); row 1 (from y 384)
    // 128 x 128: glyph a,b then burst x3 then one reserved
    public static Sprite StrikeBody(int world, int i) => Cell(world, "strike", ((i % 6) + 6) % 6, 0, 128, 384);
    public static Sprite StrikeGlyph(int world, int i) => Cell(world, "strike", Mathf.Clamp(i, 0, 1), 3, 128, 128);
    public static Sprite StrikeBurst(int world, int i) => Cell(world, "strike", 2 + Mathf.Clamp(i, 0, 2), 3, 128, 128);

    // <w>_attack_lash.png (1024 x 128, 8 cells; Verdant vine, Tide tentacle; phase 1e): link a,b (one 128 px section of the whip pointing down,
    // tiled end to end), tip a,b (the thorn / barnacled tip), root a,b (the bud / hatch collar), dash a,b (a dotted arc-preview segment)
    public static Sprite LashLink(int world, int i) => Cell(world, "lash", Mathf.Clamp(i, 0, 1), 0);
    public static Sprite LashTip(int world, int i) => Cell(world, "lash", 2 + Mathf.Clamp(i, 0, 1), 0);
    public static Sprite LashRoot(int world, int i) => Cell(world, "lash", 4 + Mathf.Clamp(i, 0, 1), 0);
    public static Sprite LashDash(int world, int i) => Cell(world, "lash", 6 + Mathf.Clamp(i, 0, 1), 0);
    // <w>_attack_jet.png (768 x (body + 128)): row 0 six body frames 128 x body (Ember 256, Tide 320; apex at the TOP centre, 12 fps loop);
    // row 1 (from y = body) six 128 x 128 cells: nozzle x3 (the flare that grows through the tell) then tip x3 (sparks / splash at the far end)
    public static int JetBodyPx(int world)
    {
        var tex = Atlas(world, "jet");
        return tex == null ? 0 : tex.height - 128;
    }
    // The part of the body cell the jet itself fills, in pixels from the apex (Ember: 230 of 256; Tide: the whole 320)
    public static int JetContentPx(int world) { int h = JetBodyPx(world); return h == 256 ? 230 : h; }
    public static Sprite JetBody(int world, int i) { int h = JetBodyPx(world); return h < 128 ? null : CellAt(world, "jet", (((i % 6) + 6) % 6) * 128, 0, 128, h); }
    public static Sprite JetNozzle(int world, int i) { int h = JetBodyPx(world); return h < 128 ? null : CellAt(world, "jet", Mathf.Clamp(i, 0, 2) * 128, h, 128, 128); }
    public static Sprite JetTip(int world, int i) { int h = JetBodyPx(world); return h < 128 ? null : CellAt(world, "jet", (3 + Mathf.Clamp(i, 0, 2)) * 128, h, 128, 128); }

    // tide_attack_wave.png (512 x 480): four 512 x 96 body frames (seamless left-right, 12 fps) then row 4 (y 384) of 96 x 96 cells:
    // capL, capR, gapMarker a, b
    public static Sprite WaveBody(int world, int i) { return CellAt(world, "wave", 0, (((i % 4) + 4) % 4) * 96, 512, 96); }
    public static Sprite WaveCapL(int world) { return CellAt(world, "wave", 0, 384, 96, 96); }
    public static Sprite WaveCapR(int world) { return CellAt(world, "wave", 96, 384, 96, 96); }
    public static Sprite WaveGapMarker(int world, int i) { return CellAt(world, "wave", 192 + Mathf.Clamp(i, 0, 1) * 96, 384, 96, 96); }

    // ---- tests --------------------------------------------------------------------

    public static void Inject(int world, string file, Texture2D tex) { injected[PathOf(world, file)] = tex; }

    public static void Clear()
    {
        injected.Clear();
        spaceLaser = null;
        cells.Clear();
        atlases.Clear();
    }
}
