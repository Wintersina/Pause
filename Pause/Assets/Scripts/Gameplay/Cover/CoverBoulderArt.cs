using System;
using System.Collections.Generic;
using UnityEngine;

// One boulder variant's art and solid shape (loaded once per world, docs/cover-boulders.md section 6).
public struct CoverCircle
{
    public float x, y, r;   // sprite pixels, origin = the cell centre, y up
}

public sealed class CoverVariant
{
    public Sprite intact;
    public Sprite[] frames;          // the drawn destruction frames (null / empty: the procedural fracture)
    public Sprite[] pieces;          // the procedural fracture's 3 x 3 tiles
    public Vector2[] pieceCentre;    // each tile's centre from the sprite centre, sprite pixels / 100 (world u at scale 1)
    public CoverCircle[] circles;
    public float artWidth = 400f;    // the art's alpha bounding box width (px)
    public float cellPx = 512f;
    public float WorldScale(float widthU) { return widthU * 100f / Mathf.Max(8f, artWidth); }
}

// Loads a world's boulder art: Resources/Boulders/<World>/manifest.json (+ boulder.png and break.png, or in-place
// intact sprites for Space), cuts the cells into sprites ONCE, keeps them for the world in play.
public static class CoverBoulderArt
{
    [Serializable] class JCircle { public float x, y, r; }
    [Serializable] class JVariant { public int x0, y0, x1, y1; public JCircle[] circles; }
    [Serializable] class JManifest { public string world; public int cell = 512; public JVariant[] variants; public int breakFrames; public string[] intact; }

    public const int Cell = 512, FramesPerStrip = 5, Tiles = 3;
    public const float Ppu = 100f;

    static readonly Dictionary<string, CoverVariant[]> cache = new Dictionary<string, CoverVariant[]>();

    // The variants of `world`'s folder; null when it has no manifest.
    public static CoverVariant[] Load(string folder)
    {
        CoverVariant[] got;
        if (cache.TryGetValue(folder, out got) && got != null && got.Length > 0 && got[0].intact != null) return got;
        var made = Build(folder);
        if (made != null) cache[folder] = made;
        return made;
    }

    // The art a boulder of `world` is drawn with: its own, else (a world with no art: the system's tests) Space's.
    public static CoverVariant[] For(int world)
    {
        var e = CoverBoulderTable.For(world);
        var v = Load(e.folder);
        if (v == null && world != 0) v = Load(CoverBoulderTable.For(0).folder);
        return v;
    }

    public static bool Has(int world) { return Load(CoverBoulderTable.For(world).folder) != null; }

    public static void Forget()
    {
        cache.Clear();
    }

    static CoverVariant[] Build(string folder)
    {
        var text = Resources.Load<TextAsset>(folder + "/manifest");
        if (text == null) return null;
        JManifest m;
        try { m = JsonUtility.FromJson<JManifest>(text.text); }
        catch (Exception) { return null; }
        if (m == null || m.variants == null || m.variants.Length == 0) return null;

        Texture2D sheet = null, strip = null;
        bool inPlace = m.intact != null && m.intact.Length >= m.variants.Length;
        if (!inPlace)
        {
            sheet = Resources.Load<Texture2D>(folder + "/boulder");
            if (sheet == null) return null;
        }
        if (m.breakFrames > 0) strip = Resources.Load<Texture2D>(folder + "/break");

        float cell = m.cell > 0 ? m.cell : Cell;
        var outv = new CoverVariant[m.variants.Length];
        for (int v = 0; v < outv.Length; v++)
        {
            var jv = m.variants[v];
            var cv = new CoverVariant { cellPx = cell, artWidth = Mathf.Max(8, jv.x1 - jv.x0) };
            cv.intact = inPlace
                ? Resources.Load<Sprite>(m.intact[v])
                : Slice(sheet, v * (int)cell, 0, (int)cell);
            if (cv.intact == null) return null;
            int nc = jv.circles != null ? jv.circles.Length : 0;
            cv.circles = new CoverCircle[Mathf.Max(1, nc)];
            if (nc == 0) cv.circles[0] = new CoverCircle { x = 0, y = 0, r = cv.artWidth * .4f };
            for (int i = 0; i < nc; i++) cv.circles[i] = new CoverCircle { x = jv.circles[i].x, y = jv.circles[i].y, r = jv.circles[i].r };
            if (strip != null)
            {
                cv.frames = new Sprite[FramesPerStrip];
                // the strip's rows are top-down in the image, Unity's texture y is bottom-up
                int row = (int)cell * (m.variants.Length - 1 - v);
                for (int f = 0; f < FramesPerStrip; f++) cv.frames[f] = Slice(strip, f * (int)cell, row, (int)cell);
            }
            MakePieces(cv);
            outv[v] = cv;
        }
        return outv;
    }

    static Sprite Slice(Texture2D tex, int x, int y, int size)
    {
        return Sprite.Create(tex, new Rect(x, y, size, size), new Vector2(.5f, .5f), Ppu, 0, SpriteMeshType.FullRect);
    }

    // The procedural fracture: the intact sprite cut into Tiles x Tiles pieces (a world with no drawn strip, and
    // a boulder whose strip is missing, breaks apart along these).
    static void MakePieces(CoverVariant cv)
    {
        var sp = cv.intact;
        var tex = sp.texture;
        Rect r = sp.textureRect;
        int n = Tiles * Tiles;
        cv.pieces = new Sprite[n];
        cv.pieceCentre = new Vector2[n];
        float w = r.width / Tiles, h = r.height / Tiles;
        for (int j = 0; j < Tiles; j++)
            for (int i = 0; i < Tiles; i++)
            {
                int k = j * Tiles + i;
                cv.pieces[k] = Sprite.Create(tex, new Rect(r.x + i * w, r.y + j * h, w, h), new Vector2(.5f, .5f), Ppu, 0, SpriteMeshType.FullRect);
                cv.pieceCentre[k] = new Vector2(((i + .5f) / Tiles - .5f) * r.width, ((j + .5f) / Tiles - .5f) * r.height) / Ppu;
            }
    }
}
