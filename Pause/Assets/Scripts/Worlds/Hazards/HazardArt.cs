using System.Collections.Generic;
using UnityEngine;

// The APPROVED per-world weather sheets (Storm and Tide; the other worlds stay staged in Art/Weather~ until approved):
//   wx_hazard.png   3072 x 1536, 512 px cells, 6 x 3: row 0 hazard A loop, row 1 hazard B loop, row 2 the two warnings (cells 0-1 A, 2-3 B)
//                   (+ optional wx_hazard.json {"gutter": g}: each cell's drawing lives in its inner (512 - 2g) px, the g px gutter around
//                   it is transparent (or, for a horizontally tiling strip, the strip's own wrap-around continuation), and the sprite is cut
//                   from the inner rect, so bilinear filtering never samples a neighbouring cell. Frost's soft sheet, g = 16. Without the
//                   file the whole 512 cell is the sprite, as before. Either way a cell is one unit across.)
//   wx_ambient.png  2048 x 512, 256 px cells, 8 x 2: 4 kinds x 4 variants (+ wx_ambient.json: each cell's tight bounds, so a particle's
//                   size is its drawn size, not its cell's)
// HazardArt is loaded when an event of the world BEGINS and released when it ends (reference counted); WeatherArt while the
// ambient layer shows that world. Both are high resolution, never clamped (WorldBackdropImport), ASTC 4x4 on phones.
public static class HazardArt
{
    public const string File = "wx_hazard";
    public const int CellPx = 512;
    [System.Serializable] class Layout { public int gutter; }
    static Texture2D tex;
    static Sprite[,] cells;
    static int refs, world = -1, gutter;

    // the loaded sheet's gutter (px, 0: whole cells) and its sprites' rect side (CellPx - 2 x gutter)
    public static int Gutter { get { return gutter; } }
    public static int SpritePx { get { return CellPx - 2 * gutter; } }

    public static bool Loaded { get { return cells != null; } }
    public static int Refs { get { return refs; } }
    public static int World { get { return world; } }
    public static Sprite Cell(int row, int col) { return cells[Mathf.Clamp(row, 0, 2), Mathf.Clamp(col, 0, 5)]; }

    public static void Acquire(int w)
    {
        refs++;
        if (cells != null && world == w) return;
        if (cells != null) Unload();
        world = w;
        var entry = WorldWeatherTable.ForWorld(w);
        if (entry == null) return;
        string folder = BackdropCatalog.Folder(entry.world);
        tex = Resources.Load<Texture2D>(folder + File);
        if (tex == null) return;                    // not installed (a staged world): the procedural body is drawn
        gutter = 0;
        var json = Resources.Load<TextAsset>(folder + File);
        if (json != null)
        {
            var l = JsonUtility.FromJson<Layout>(json.text);
            if (l != null) gutter = Mathf.Clamp(l.gutter, 0, CellPx / 4);
            Resources.UnloadAsset(json);
        }
        int side = SpritePx;
        cells = new Sprite[3, 6];
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 6; c++)
                cells[r, c] = Sprite.Create(tex, new Rect(c * CellPx + gutter, (2 - r) * CellPx + gutter, side, side), new Vector2(.5f, .5f), side, 0, SpriteMeshType.FullRect);
    }

    public static void Release()
    {
        if (refs > 0) refs--;
        if (refs == 0) Unload();
    }

    static void Unload()
    {
        if (cells != null) foreach (var s in cells) if (s != null) Kill(s);
        cells = null;
        if (tex != null) Resources.UnloadAsset(tex);
        tex = null; world = -1; gutter = 0;
    }

    internal static void Kill(Object o) { if (o == null) return; if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }
}

public static class WeatherArt
{
    public const string File = "wx_ambient";
    [System.Serializable] class Cell { public int x, y, w, h; }
    [System.Serializable] class Manifest { public Cell[] cells; }

    static Texture2D tex;
    static Sprite[] sprites;
    static int world = -1;

    public static bool LoadedFor(int w) { return sprites != null && world == w; }
    public static int World { get { return world; } }
    // kind 0-3, variant 0-3: a sprite whose long side is 1 unit (scale it to the particle's size)
    public static Sprite Get(int kind, int variant) { return sprites[Mathf.Clamp(kind, 0, 3) * 4 + (variant & 3)]; }

    public static void Load(int w)
    {
        if (world == w) return;
        Unload();
        world = w;
        var entry = WorldWeatherTable.ForWorld(w);
        if (entry == null) return;
        string folder = BackdropCatalog.Folder(entry.world);
        tex = Resources.Load<Texture2D>(folder + File);
        var json = Resources.Load<TextAsset>(folder + File);
        if (tex == null || json == null) { if (tex != null) Resources.UnloadAsset(tex); tex = null; return; }
        var m = JsonUtility.FromJson<Manifest>(json.text);
        if (m == null || m.cells == null || m.cells.Length < 16) { Resources.UnloadAsset(tex); tex = null; return; }
        sprites = new Sprite[16];
        for (int i = 0; i < 16; i++)
        {
            var c = m.cells[i];
            sprites[i] = Sprite.Create(tex, new Rect(c.x, c.y, c.w, c.h), new Vector2(.5f, .5f), Mathf.Max(c.w, c.h), 0, SpriteMeshType.FullRect);
        }
    }

    public static void Unload()
    {
        if (sprites != null) foreach (var s in sprites) if (s != null) HazardArt.Kill(s);
        sprites = null;
        if (tex != null) Resources.UnloadAsset(tex);
        tex = null; world = -1;
    }
}

// The painted gust sheet of a world (Backdrop3/wx_gust.png, 2048 x 1024, 512 px cells, 4 x 2): row 0 = four gust RIBBONS (horizontal streamline
// bundles flowing to +x, tiling horizontally), row 1 cells 0-1 = the gust's HEAD (the curling leading edge), cells 2-3 = the world's debris.
// Loaded while the world's wind is drawn; a world without the file simply keeps the procedural streaks.
public static class WindArt
{
    public const string File = "wx_gust";
    static Texture2D tex;
    static Sprite[,] cells;
    static int world = -1;

    public static bool LoadedFor(int w) { return cells != null && world == w; }
    public static Sprite Cell(int row, int col) { return cells[Mathf.Clamp(row, 0, 1), Mathf.Clamp(col, 0, 3)]; }

    public static void Load(int w)
    {
        if (world == w) return;
        Unload();
        world = w;
        var entry = WorldWeatherTable.ForWorld(w);
        if (entry == null || entry.wind == null) return;
        tex = Resources.Load<Texture2D>(BackdropCatalog.Folder(entry.world) + File);
        if (tex == null) return;
        cells = new Sprite[2, 4];
        for (int r = 0; r < 2; r++)
            for (int c = 0; c < 4; c++)
                cells[r, c] = Sprite.Create(tex, new Rect(c * 512, (1 - r) * 512, 512, 512), new Vector2(.5f, .5f), 512f, 0, SpriteMeshType.FullRect);
    }

    public static void Unload()
    {
        if (cells != null) foreach (var s in cells) if (s != null) HazardArt.Kill(s);
        cells = null;
        if (tex != null) Resources.UnloadAsset(tex);
        tex = null; world = -1;
    }
}
