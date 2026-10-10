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

    // ---- tests --------------------------------------------------------------------

    public static void Inject(int world, string file, Texture2D tex) { injected[PathOf(world, file)] = tex; }

    public static void Clear()
    {
        injected.Clear();
        cells.Clear();
        atlases.Clear();
    }
}
