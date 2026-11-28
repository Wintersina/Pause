using System.Collections.Generic;
using UnityEngine;

// Building blocks of WorldBackdrop. None of these read Time.* themselves:
// every update receives the frame's *scaled* delta from WorldBackdrop, so a
// paused game (timeScale 0) hands them dt = 0 and nothing moves, blinks or
// advances a flipbook. That is what makes the freeze a true still frame.

// Sprites cut from one world's atlas (a PNG plus the JSON rect manifest its
// art pipeline writes next to it). A sprite pivots on the middle of its rect.
public class BackdropAtlas
{
    // px / py: optional pivot inside the rect, 0..1 (0.5 when absent: py == 0 && px == 0 means centre).
    [System.Serializable] class Rect { public string n; public int x, y, w, h; public float px, py; }
    // Optional, for re-renders at more pixels per cell (anim_hires):
    //   pixelScale  sheet pixels per pixel of the original 1x art (2 for a
    //               2x re-render). Sprites keep the 1x art's bounds, so the
    //               re-render is drawn the same size, only sharper.
    //   sheetW/H    the sheet's authored size, which the rects are measured
    //               in. If the importer delivers a smaller texture (a
    //               platform's max size), the rects are scaled to it.
    [System.Serializable] class Manifest { public Rect[] sprites; public float pixelScale; public int sheetW, sheetH; }

    public const float PixelsPerUnit = 100f;

    // Texture pixels per pixel of the original 1x art (see Manifest).
    public float PixelScale { get; private set; }

    public readonly Texture2D texture;
    readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

    public BackdropAtlas(Texture2D texture, TextAsset manifest)
    {
        this.texture = texture;
        PixelScale = 1f;
        if (texture == null || manifest == null) return;
        var m = JsonUtility.FromJson<Manifest>(manifest.text);
        if (m == null || m.sprites == null) return;
        float fx = m.sheetW > 0 ? texture.width / (float)m.sheetW : 1f;
        float fy = m.sheetH > 0 ? texture.height / (float)m.sheetH : 1f;
        PixelScale = (m.pixelScale > 0f ? m.pixelScale : 1f) * fx;
        foreach (var r in m.sprites)
        {
            var s = Sprite.Create(texture, new UnityEngine.Rect(r.x * fx, r.y * fy, r.w * fx, r.h * fy),
                                  new Vector2(r.px > 0f ? r.px : 0.5f, r.py > 0f ? r.py : 0.5f), PixelsPerUnit * PixelScale, 0, SpriteMeshType.FullRect);
            s.name = r.n;
            sprites[r.n] = s;
        }
    }

    public bool Has(string name) { return sprites.ContainsKey(name); }

    public Sprite Get(string name)
    {
        Sprite s;
        return sprites.TryGetValue(name, out s) ? s : null;
    }

    // name_00, name_01, ... in order: a flipbook's frames, or a set of
    // variants to pick one from. Empty if missing.
    public Sprite[] Frames(string name)
    {
        var list = new List<Sprite>();
        for (int i = 0; ; i++)
        {
            var s = Get(name + "_" + i.ToString("00"));
            if (s == null) break;
            list.Add(s);
        }
        return list.ToArray();
    }

    public int Count { get { return sprites.Count; } }

    public void Destroy()
    {
        foreach (var s in sprites.Values) if (s != null) Kill(s);
        sprites.Clear();
    }

    // Destroy that also works from edit-mode tests.
    public static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o);
        else Object.DestroyImmediate(o);
    }
}

// A seamless vertical tile, leap-frogged: enough copies stacked to cover the
// view plus one, each wrapped back to the top as it leaves the bottom. A
// world drawn from higher up (Spec.groundZoom < 1: Frost) draws its tiles
// narrower than the view, so the stack is repeated side by side as well:
// columns centred on x = 0, k * TileWidth (the art is seamless across too:
// frost_redraw/hwrap_tiles.py), all on the one scroll.
public class BackdropTile
{
    public readonly BackdropCatalog.Layer layer;
    public float offset;                 // integrated scroll, world units (wrapped to one tile)
    // The same scroll, never wrapped (double: exact over a long run). Pieces
    // pinned to this tile (GroundPlanner) are placed from it, so they stay on
    // their spot of the ground for as long as they are in view.
    public double travel;
    readonly Transform root;
    readonly List<SpriteRenderer> copies = new List<SpriteRenderer>();
    readonly Sprite sprite;
    readonly bool halfTurn;
    float tileHeight, tileWidth, wobblePhase;
    int rows = 1, cols = 1;              // copies in use: rows stacked x cols side by side (copy i: row i / cols, column i % cols)

    // `lift` > 1 draws the tile brightened (BackdropGrade; not combined
    // with a wrap cross-fade).
    public BackdropTile(Transform parent, BackdropCatalog.Layer layer, Sprite sprite, int order, float z,
                        bool halfTurn = false, float lift = 1f, float sat = 1f)
    {
        this.layer = layer;
        this.sprite = sprite;
        this.halfTurn = halfTurn;
        root = new GameObject("Tile_" + layer.name).transform;
        root.SetParent(parent, false);
        root.localPosition = new Vector3(0f, 0f, z);
        if (layer.wrapBlend > 0f)
        {
            var shader = WorldArtPreload.Load<Shader>(WrapShader);
            if (shader != null)
            {
                wrapMat = new Material(shader) { name = "SkyWrap_" + layer.name };
                wrapMat.SetFloat("_Blend", layer.wrapBlend);
            }
        }
        else wrapMat = BackdropGrade.Create("tile_" + layer.name, lift, sat);
        gradeLift = wrapMat != null && layer.wrapBlend <= 0f ? lift : 1f;
        for (int i = 0; i < 3; i++) AddCopy(order);
    }

    public const string WrapShader = "BackdropShaders/BackdropSkyWrap";
    Material wrapMat;

    void AddCopy(int order)
    {
        var go = new GameObject("copy" + copies.Count);
        go.transform.SetParent(root, false);
        var sr = go.AddComponent<SpriteRenderer>();
        if (wrapMat != null) sr.sharedMaterial = wrapMat;
        sr.sprite = sprite;
        sr.flipX = halfTurn;
        sr.flipY = halfTurn;
        sr.sortingOrder = order;
        sr.color = layer.tint;
        copies.Add(sr);
    }

    // Tile width follows the view; a strip is a fixed fraction of the main
    // tile width (its pixels share the main tiles' scale). `zoom` (the world's
    // Spec.groundZoom, 1 = the tile spans the view) draws the tile that much
    // smaller -- seen from higher up -- with as many columns as cover the view.
    public void Layout(float viewWidth, float viewHeight, float mainTileWidth, float zoom = 1f)
    {
        if (sprite == null) return;
        zoom = zoom > 0f ? Mathf.Min(zoom, 1f) : 1f;
        Vector2 size = sprite.bounds.size;
        float scale = (layer.kind == BackdropCatalog.Kind.Strip
            ? mainTileWidth / 6f
            : viewWidth / size.x) * zoom;
        tileWidth = size.x * scale;
        // A wrap-blended tile shows only the first (1 - wrapBlend) of its art
        // per copy (BackdropSkyWrap.shader cross-fades the rest into its
        // start), so each copy is squashed to that height.
        float shown = layer.wrapBlend > 0f && wrapMat != null ? 1f - layer.wrapBlend : 1f;
        tileHeight = size.y * scale * shown;
        rows = Mathf.CeilToInt(viewHeight / tileHeight) + 1;
        // a centre column plus enough on each side to reach the view's edges
        cols = zoom < 1f ? 2 * Mathf.CeilToInt(Mathf.Max(0f, viewWidth * .5f - tileWidth * .5f) / tileWidth) + 1 : 1;
        int need = rows * cols;
        while (copies.Count < need) AddCopy(copies[0].sortingOrder);
        for (int i = 0; i < copies.Count; i++)
        {
            copies[i].transform.localScale = new Vector3(scale, scale * shown, 1f);
            copies[i].enabled = i < need;
        }
    }

    public int Columns { get { return cols; } }

    public float TileHeight { get { return tileHeight; } }
    public float TileWidth { get { return tileWidth; } }
    // The material is the wrap cross-fade's, or the grade's (GradeLift > 1).
    public Material WrapMaterial { get { return layer.wrapBlend > 0f ? wrapMat : null; } }
    public Material GradeMaterial { get { return layer.wrapBlend > 0f ? null : wrapMat; } }
    public float GradeLift { get { return gradeLift; } }
    float gradeLift = 1f;
    public string SpriteName { get { return sprite != null ? sprite.name : null; } }
    public bool HalfTurn { get { return halfTurn; } }

    public void Destroy() { BackdropAtlas.Kill(wrapMat); wrapMat = null; }

    public void Tick(float dt, float velocity, float viewHeight, float alpha)
    {
        float step = (velocity * layer.rate + layer.flow) * dt;
        travel += step;
        if (tileHeight <= 0f) { offset += step; return; }
        // derived from the unwrapped travel, so the drawn tile and anything
        // pinned to it never drift apart
        double h = tileHeight;
        offset = (float)(travel - System.Math.Floor(travel / h) * h);
        wobblePhase += dt * 7.3f;
        float x = layer.wobble > 0f ? Mathf.Sin(wobblePhase) * layer.wobble : 0f;

        // Row r sits at bottom + r*h - offset, wrapping as offset grows; its
        // columns at x = (c - centre) * w, all on the same scroll.
        float bottom = -viewHeight * 0.5f - tileHeight * 0.5f;
        Color c = layer.tint;
        c.a *= alpha;
        int centre = cols / 2;
        for (int i = 0; i < copies.Count; i++)
        {
            if (!copies[i].enabled) continue;
            int r = i / cols, col = i % cols;
            float y = bottom + r * tileHeight - offset + tileHeight;
            copies[i].transform.localPosition = new Vector3(x + (col - centre) * tileWidth, y, 0f);
            copies[i].color = c;
        }
    }

    public int RendererCount { get { return copies.Count; } }
    public Transform CopyTransform(int i) { return copies[i].transform; }   // tests: where a copy is drawn
}

// One pooled set piece or particle. `body` is a child so a piece can spin or
// squash independently of its placement.
public class BackdropPiece
{
    public Transform root, body;
    public SpriteRenderer sr, blend;     // blend: next flipbook frame, faded in
    public SpriteRenderer smoke;         // optional Space asteroid effect flipbook
    public Sprite[] effectFrames;
    public bool active;
    public bool edge;                    // Space: a station peeking in across the screen edge
    public float x, y, vx, vy, age, life, size, phase, spin, rate;
    public Color color = Color.white;
    public Color dockTint = Color.white;   // an elite dock's own per-backdrop colour (DockGrade), multiplied into its draw colour
    public Sprite[] frames;
    public float fps;
    public bool smoothBlend;             // a rotating planet: the next frame fades in continuously, not in six steps
    public bool loop = true;
    public int kind, tier;               // tier: depth tier, where a director has them
    // Pinned to the ground (GroundPlanner): `gy` is the piece's ground
    // coordinate (its y plus the ground tile's travel), constant while it is
    // in view; its y is recomputed from it every frame.
    public double gy;
    public int footprint = -1;           // GroundPlanner footprint slot, -1 none
    // A pinned piece being destroyed (Desert's worms): seconds into its fall (-1 alive) and its fade.
    public float dyingT = -1f, fade = 1f;
    public float core = .3f;             // the footprint core (PieceRule.core) a director stamped on it
    public BackdropPiece[] children;     // e.g. a planet's moon
    public BackdropPiece parent;         // set on a child that is placed by its parent
    public readonly BackdropPiece[] slot = new BackdropPiece[1];   // reusable one-child `children` array
    // A turning sphere (Space's BackdropPlanet shader): `turn` is the
    // surface spin in radians of longitude, integrated from scaled dt only;
    // `disc` the drawn disc's centre and radii in atlas uv.
    public bool planet;
    public float turn, turnRate;
    public Vector4 disc;

    public void Show(bool on)
    {
        active = on;
        if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
    }

    // Advance the flipbook. With `blend` set, the next frame cross-fades in
    // over the current one so slow rotations read smoothly.
    public void Animate()
    {
        if (frames == null || frames.Length == 0) return;
        float f = age * fps;
        int n = frames.Length;
        int i = loop ? (int)f % n : Mathf.Min((int)f, n - 1);
        sr.sprite = frames[i];
        if (blend != null)
        {
            blend.sprite = frames[(i + 1) % n];
            Color c = sr.color;
            // Five visible in-between states between each pair of authored
            // frames, followed by the next frame. Quantizing the dissolve
            // avoids a sharp jump without allocating extra sprite textures.
            float fr = f - Mathf.Floor(f);
            c.a *= loop ? (smoothBlend ? fr : Mathf.Floor(fr * 6f) / 6f) : 0f;
            blend.color = c;
        }
    }

    public bool Finished { get { return !loop && frames != null && age * fps >= frames.Length; } }
}

// Fixed-capacity pool: Spawn() returns null when every piece is busy, so a
// long run can never grow it.
public class BackdropPool
{
    public readonly List<BackdropPiece> items = new List<BackdropPiece>();
    public readonly string name;

    public BackdropPool(Transform parent, string name, int capacity, int order, float z, bool blend = false)
    {
        this.name = name;
        var holder = new GameObject("Pool_" + name).transform;
        holder.SetParent(parent, false);
        holder.localPosition = new Vector3(0f, 0f, z);
        for (int i = 0; i < capacity; i++)
        {
            // A piece the next-world preload already built (BackdropPieceStock, a few a frame) is only re-parented:
            // no GameObject / SpriteRenderer is created inside the swap frame.
            var p = BackdropPieceStock.Take(blend);
            if (p == null) p = BackdropPieceStock.Make(blend);
            p.root.name = name + i;
            p.root.SetParent(holder, false);
            p.sr.sortingOrder = order;
            if (p.blend != null) p.blend.sortingOrder = order + 1;
            p.Show(false);
            items.Add(p);
        }
    }

    public int Capacity { get { return items.Count; } }

    public int ActiveCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < items.Count; i++) if (items[i].active) n++;
            return n;
        }
    }

    public BackdropPiece Spawn()
    {
        for (int i = 0; i < items.Count; i++)
        {
            var p = items[i];
            if (p.active) continue;
            p.age = 0f;
            p.spin = 0f;
            p.vx = p.vy = 0f;
            p.frames = null;
            p.effectFrames = null;
            p.edge = false;
            if (p.smoke != null) p.smoke.enabled = false;
            p.loop = true;
            p.smoothBlend = false;
            p.children = null;
            p.parent = null;
            p.planet = false;
            p.dyingT = -1f;
            p.fade = 1f;
            p.turn = p.turnRate = 0f;
            p.body.localRotation = Quaternion.identity;
            p.body.localScale = Vector3.one;
            p.root.localRotation = Quaternion.identity;
            p.root.localScale = Vector3.one;
            p.Show(true);
            return p;
        }
        return null;
    }
}

// Backdrop set pieces built AHEAD of a world swap. A BackdropSet's directors create a few hundred pooled pieces (three
// GameObjects each) in the frame of the swap; on a plain portal crossfade nothing covers that burst. While the boss
// fight runs, WorldArtPreload.Next asks for the destination's pieces (Want) and Pump builds a small budget of them a frame,
// inactive and parked under one staging object; BackdropPool takes them (Take) instead of creating. The count wanted is
// what the world's last set actually used (Learn), else a default; leftovers are destroyed a few a frame after the swap
// (Trim / Drain). A swap that finds the stock short just creates the rest, as it always did.
public static class BackdropPieceStock
{
    public const int PerFrame = 10;           // pieces built per Pump (30 GameObjects)
    public const int DefaultPlain = 150, DefaultBlend = 3, MaxTotal = 320;
    const int DrainPerFrame = 6;

    static readonly Stack<BackdropPiece> plain = new Stack<BackdropPiece>();
    static readonly Stack<BackdropPiece> blend = new Stack<BackdropPiece>();
    static readonly Dictionary<string, int[]> learned = new Dictionary<string, int[]>();
    static Transform staging;
    static int wantPlain, wantBlend;
    static string world;
    static bool draining;

    public static int Built { get; private set; }
    public static int Taken { get; private set; }
    public static string World { get { return world; } }
    public static int Count { get { return plain.Count + blend.Count; } }
    public static bool Filling { get { return Application.isPlaying && (plain.Count < wantPlain || blend.Count < wantBlend); } }

    // Asks for `worldName`'s pieces (WorldArtPreload.Next). Out of play nothing is built.
    public static void Want(string worldName)
    {
        if (!Application.isPlaying || PerfTier.LowEnd) return;   // low-end phones hold nothing ahead of time
        int[] n;
        if (!learned.TryGetValue(worldName, out n)) n = new[] { DefaultPlain, DefaultBlend };
        world = worldName;
        draining = false;
        wantPlain = Mathf.Min(n[0] + 2, MaxTotal);
        wantBlend = n[1];
    }

    // One frame: build up to PerFrame pieces towards the wanted counts (and, after the swap, destroy leftovers).
    public static void Pump()
    {
        if (draining) { Drain(); return; }
        if (!Filling) return;
        EnsureStaging();
        for (int i = 0; i < PerFrame && Filling; i++)
        {
            if (blend.Count < wantBlend) blend.Push(Make(true, staging));
            else plain.Push(Make(false, staging));
            Built++;
        }
    }

    // A piece for a pool of this kind from the stock, or null.
    public static BackdropPiece Take(bool wantBlendKind)
    {
        var from = wantBlendKind ? blend : plain;
        while (from.Count > 0)
        {
            var p = from.Pop();
            if (p != null && p.root != null) { Taken++; return p; }
        }
        return null;
    }

    public static BackdropPiece Make(bool withBlend, Transform parent = null)
    {
        var p = new BackdropPiece();
        p.root = new GameObject("piece").transform;
        if (parent != null) p.root.SetParent(parent, false);
        p.body = new GameObject("body").transform;
        p.body.SetParent(p.root, false);
        p.sr = p.body.gameObject.AddComponent<SpriteRenderer>();
        if (withBlend)
        {
            var b = new GameObject("blend");
            b.transform.SetParent(p.body, false);
            p.blend = b.AddComponent<SpriteRenderer>();
        }
        p.Show(false);
        return p;
    }

    // The set for `worldName` was built: remember how many pieces it used, and let the leftovers go.
    public static void Learn(string worldName, BackdropSet set)
    {
        if (!Application.isPlaying || set == null || set.Director == null) return;
        int a = 0, b = 0;
        foreach (var pool in set.Director.Pools)
        {
            if (pool.items.Count == 0) continue;
            if (pool.items[0].blend != null) b += pool.Capacity; else a += pool.Capacity;
        }
        learned[worldName] = new[] { a, b };
        if (worldName == world) { draining = true; wantPlain = wantBlend = 0; }
    }

    static void Drain()
    {
        for (int i = 0; i < DrainPerFrame && Count > 0; i++)
        {
            var p = plain.Count > 0 ? plain.Pop() : blend.Pop();
            if (p != null && p.root != null) Object.Destroy(p.root.gameObject);
        }
        if (Count == 0)
        {
            draining = false;
            world = null;
            if (staging != null) { Object.Destroy(staging.gameObject); staging = null; }
        }
    }

    // Called each frame by WorldBackdrop.Step: leftovers go a few at a time, never in one frame.
    public static void Tick() { if (draining) Drain(); }

    static void EnsureStaging()
    {
        if (staging != null) return;
        staging = new GameObject("~BackdropStock").transform;
    }

    // Tests.
    public static void Reset()
    {
        plain.Clear(); blend.Clear(); learned.Clear();
        wantPlain = wantBlend = 0; world = null; draining = false; Built = Taken = 0;
    }
}
