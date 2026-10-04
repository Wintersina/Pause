using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// The blue-atom shield's shape for one hull sprite: the hull's own silhouette
// grown outwards by a few pixels, traced into an angular outline, plus the
// mesh template ShipShield paints every frame.
//
//   alpha -> silhouette -> dilate (disk) -> fill holes -> trace outer edge
//         -> Douglas-Peucker (angular facets) -> resample + offset strips
//
// Roster ships: built at most once per ship (ForShip, keyed by ShipId) from
// the silhouette baked at edit time (ShieldSilhouettes), so no hull pixels are
// ever read back from the GPU in the game. Every skin, damage state and idle
// drawing of a ship shares that one contour (skins keep the stock alpha), and
// ShipShield builds it when the ship spawns, never on a blue-atom pickup.
//
// Any other sprite (For): built at most once per sprite (texture + rect) and
// cached, so a hull nobody baked still gets a fitted shield. Ship textures are
// imported non-readable, so its pixels are read back once through a
// RenderTexture (or straight from the PNG in the editor when no GPU is
// available).
//
// All sizes are in "hull units" U = the sprite's longest edge / 30 px. Every
// hull is normalised to the same world size (shopingShips.ReferenceHullSize),
// so U is the same on screen for every ship and the shield reads the same
// thickness on a 20 px Dove as on a 57 px Crimson Halo.
public sealed class ShieldContour
{
    public const float RefPixels = 30f;
    public const float DilateU = 4.2f;      // outline distance from the hull
    public const float SimplifyU = 1.3f;    // facet tolerance (angular, not bubbly)
    public const float InkU = 1.1f;         // ink stroke outside the outline
    public const float LineU = 1.0f;        // teal energy line just inside it
    public const float PlateU = 2.2f;       // hex plate band inside the line
    public const float PlateLenU = 3.4f;
    public const float PlateGapU = 0.6f;
    public const float SampleU = 0.6f;      // line/ink segment length
    public const byte AlphaCutoff = 40;

    // Quad kinds in the mesh, in draw order after the fill fan.
    public const byte KindInk = 0, KindPlate = 1, KindLine = 2;

    // Outline, CCW, sprite-local units, Polygon[0] is the nose (highest point).
    public Vector2[] Polygon;
    public float Unit;            // U in sprite-local units
    public float Perimeter;
    public Rect VisibleBounds;    // opaque hull pixels, sprite-local units
    public Rect OutlineBounds;

    // Uniform samples along the outline (for hit lookups / tests).
    public Vector2[] SamplePoint;
    public Vector2[] SampleNormal;
    public float[] SampleArc;     // 0..1 from the nose, CCW

    // Mesh template. Vertex layout: FillVertexCount fan vertices, then four
    // vertices per quad. ShipShield copies Vertices/Triangles once into its own
    // Mesh and repaints colours/uvs in place.
    public Vector3[] Vertices;
    public Vector2[] Uvs;
    public int[] Triangles;
    public int FillVertexCount;
    public int QuadCount;
    public byte[] QuadKind;
    public float[] QuadArc;       // 0..1 from the nose
    public bool[] QuadShadow;     // plates facing away from the upper-left light
    public int PlateCount;
    public int[] PlateQuad;       // quad index of each plate
    public Vector2[] PlateCenter; // sprite-local
    public Vector2[] PlateNormal;

    // Pixel-space mask kept for the collider union.
    bool[] solid;
    int gridW, gridH, pad;
    Vector2 pivotPx;
    float ppu;
    Rect cachedBox;
    Vector2[] cachedColliderPath;

    public static int BuildCount { get; private set; }
    // GPU -> CPU readbacks made (ReadPixels through a RenderTexture).
    public static int ReadbackCount { get; private set; }

    struct Key : IEquatable<Key>
    {
        public int tex, x, y, w, h;
        public bool Equals(Key o) { return tex == o.tex && x == o.x && y == o.y && w == o.w && h == o.h; }
        public override bool Equals(object o) { return o is Key && Equals((Key)o); }
        public override int GetHashCode() { return ((tex * 31 + x) * 31 + y) * 31 + w * 131 + h; }
    }

    static readonly Dictionary<Key, ShieldContour> cache = new Dictionary<Key, ShieldContour>();

    public static int CachedCount { get { return cache.Count; } }

    static readonly Dictionary<int, ShieldContour> byShip = new Dictionary<int, ShieldContour>();

    public static bool IsBuiltForShip(int id)
    {
        ShieldContour c;
        return byShip.TryGetValue(id, out c) && c != null;
    }

    // The contour of roster ship `id`, shared by all of its skins, damage
    // states and idle drawings. Cut from its baked silhouette; a ship with no
    // (or a stale) bake falls back to reading its stock rest drawing back once.
    public static ShieldContour ForShip(int id)
    {
        ShieldContour contour;
        if (byShip.TryGetValue(id, out contour) && contour != null) return contour;
        if (!ShipHullArt.Has(id)) return null;
        UnityEngine.Profiling.Profiler.BeginSample("ShieldContour.ForShip");
        var r = ShipHullArt.RectFor(id);
        bool[] mask;
        if (ShieldSilhouettes.TryGet(id, r.w, r.h, out mask))
            contour = BuildMask(mask, r.w, r.h, new Vector2(r.w * .5f, r.h * .5f), r.pixelsPerUnit);
        else
            contour = For(ShipHullArt.StockRest(id));
        if (contour != null) byShip[id] = contour;
        UnityEngine.Profiling.Profiler.EndSample();
        return contour;
    }

    // The alpha mask a sprite's contour is cut from.
    public static bool[] MaskOf(Color32[] px, int w, int h)
    {
        if (px == null) return null;
        var mask = new bool[w * h];
        for (int i = 0; i < mask.Length && i < px.Length; i++) mask[i] = px[i].a >= AlphaCutoff;
        return mask;
    }

    // Cached per hull sprite (texture + rect).
    public static ShieldContour For(Sprite sprite)
    {
        if (sprite == null || sprite.texture == null) return null;
        Rect r = sprite.textureRect;
        var key = new Key
        {
            tex = sprite.texture.GetInstanceID(),
            x = Mathf.RoundToInt(r.x), y = Mathf.RoundToInt(r.y),
            w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height),
        };
        ShieldContour contour;
        if (cache.TryGetValue(key, out contour) && contour != null) return contour;

        int w, h;
        Color32[] px = ReadPixels(sprite, out w, out h);
        contour = Build(px, w, h, sprite.pivot, sprite.pixelsPerUnit);
        cache[key] = contour;
        return contour;
    }

    // ------------------------------------------------------------------
    // Pixel readback
    // ------------------------------------------------------------------

    public static Color32[] ReadPixels(Sprite sprite, out int w, out int h)
    {
        var tex = sprite.texture;
        Rect r = sprite.textureRect;
        int x = Mathf.RoundToInt(r.x), y = Mathf.RoundToInt(r.y);
        w = Mathf.RoundToInt(r.width);
        h = Mathf.RoundToInt(r.height);
        if (w <= 0 || h <= 0) return null;

        if (tex.isReadable)
        {
            try { return ToColor32(tex.GetPixels(x, y, w, h)); }
            catch (Exception) { /* compressed formats: fall through to the GPU */ }
        }
#if UNITY_EDITOR
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            return ReadFromSourceFile(tex, x, y, w, h);
#endif
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;

        ReadbackCount++;
        var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active;
        Graphics.Blit(tex, rt);
        RenderTexture.active = rt;
        var copy = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
        copy.ReadPixels(new Rect(x, y, w, h), 0, 0, false);
        copy.Apply(false);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        var pixels = copy.GetPixels32();
        DestroyTexture(copy);
        return pixels;
    }

#if UNITY_EDITOR
    // Editor-only, no-GPU path (and a cross-check for the readback in tests).
    public static Color32[] ReadFromSourceFile(Texture2D tex, int x, int y, int w, int h)
    {
        string path = UnityEditor.AssetDatabase.GetAssetPath(tex);
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
        var file = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!file.LoadImage(System.IO.File.ReadAllBytes(path))) { DestroyTexture(file); return null; }
        var pixels = ToColor32(file.GetPixels(x, y, w, h));
        DestroyTexture(file);
        return pixels;
    }
#endif

    static Color32[] ToColor32(Color[] c)
    {
        var o = new Color32[c.Length];
        for (int i = 0; i < c.Length; i++) o[i] = c[i];
        return o;
    }

    static void DestroyTexture(Texture2D t)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(t);
        else UnityEngine.Object.DestroyImmediate(t);
    }

    // ------------------------------------------------------------------
    // Build
    // ------------------------------------------------------------------

    public static ShieldContour Build(Color32[] px, int w, int h, Vector2 pivotPx, float ppu)
    {
        return BuildMask(MaskOf(px, w, h), w, h, pivotPx, ppu);
    }

    // `on` = opaque hull pixels (w * h, bottom row first); null = all solid.
    public static ShieldContour BuildMask(bool[] on, int w, int h, Vector2 pivotPx, float ppu)
    {
        BuildCount++;
        if (w <= 0 || h <= 0) return null;
        if (ppu <= 0f) ppu = 100f;

        float U = Mathf.Max(w, h) / RefPixels;
        float R = DilateU * U;
        const int Attempts = 5;
        float step = .75f * U;
        int pad = Mathf.CeilToInt(R + step * Attempts + 3f);
        int W = w + 2 * pad, H = h + 2 * pad;

        var hull = new bool[W * H];
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // No pixels at all (readback impossible): treat the rect as solid.
                if (on != null && !on[y * w + x]) continue;
                hull[(y + pad) * W + x + pad] = true;
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
            }
        if (maxX < 0)
        {
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) hull[(y + pad) * W + x + pad] = true;
            minX = 0; minY = 0; maxX = w - 1; maxY = h - 1;
        }

        // One distance transform serves every dilation radius tried below.
        var dist = SquaredDistance(hull, W, H);
        bool[] grown = null;
        for (int attempt = 0; attempt <= Attempts; attempt++, R += step)
        {
            grown = Dilate(dist, W, H, R);
            FillHoles(grown, W, H);
            if (CountComponents(grown, W, H) <= 1) break;
            if (attempt == Attempts) FillConvexHull(grown, W, H);
        }

        var c = new ShieldContour
        {
            solid = grown, gridW = W, gridH = H, pad = pad, pivotPx = pivotPx, ppu = ppu,
            Unit = U / ppu,
        };
        c.VisibleBounds = Rect.MinMaxRect(
            (minX - pivotPx.x) / ppu, (minY - pivotPx.y) / ppu,
            (maxX + 1 - pivotPx.x) / ppu, (maxY + 1 - pivotPx.y) / ppu);
        c.Polygon = c.Outline(grown, W, H, pad, pad, SimplifyU * U);
        c.BuildGeometry();
        return c;
    }

    // Outline of a grid mask as an angular CCW polygon in sprite-local units,
    // nose first. Grid cell (gx, gy) is sprite pixel (gx - ox, gy - oy).
    Vector2[] Outline(bool[] mask, int W, int H, int ox, int oy, float epsilonPx)
    {
        var loop = Trace(mask, W, H);
        if (loop.Count < 3) return new[] { Vector2.zero, Vector2.right, Vector2.up };
        int nose = 0;
        for (int i = 1; i < loop.Count; i++)
        {
            if (loop[i].y > loop[nose].y ||
                (loop[i].y == loop[nose].y &&
                 Mathf.Abs(loop[i].x - ox - pivotPx.x) < Mathf.Abs(loop[nose].x - ox - pivotPx.x)))
                nose = i;
        }
        var simplified = SimplifyClosed(loop, nose, epsilonPx);
        var poly = new Vector2[simplified.Count];
        for (int i = 0; i < poly.Length; i++)
            poly[i] = new Vector2((simplified[i].x - ox - pivotPx.x) / ppu,
                                  (simplified[i].y - oy - pivotPx.y) / ppu);
        if (SignedArea(poly) < 0f) Array.Reverse(poly, 1, poly.Length - 1);
        return poly;
    }

    // The trigger the shield owns while it is up: the visible outline unioned
    // with the hull's normal hitbox, so the shielded hit zone is never smaller
    // than the unshielded one anywhere (no gap when the collider swaps back).
    public Vector2[] ColliderPath(Rect boxLocal)
    {
        if (cachedColliderPath != null && cachedBox == boxLocal) return cachedColliderPath;
        // Box in sprite pixels.
        int bx0 = Mathf.FloorToInt(boxLocal.xMin * ppu + pivotPx.x);
        int bx1 = Mathf.CeilToInt(boxLocal.xMax * ppu + pivotPx.x);
        int by0 = Mathf.FloorToInt(boxLocal.yMin * ppu + pivotPx.y);
        int by1 = Mathf.CeilToInt(boxLocal.yMax * ppu + pivotPx.y);
        // A grid big enough for both the outline mask and the box.
        int ox = Mathf.Max(pad, -bx0 + 2), oy = Mathf.Max(pad, -by0 + 2);
        int W = Mathf.Max(gridW - pad + ox, bx1 + ox + 2);
        int H = Mathf.Max(gridH - pad + oy, by1 + oy + 2);
        var mask = new bool[W * H];
        // Rasterise the faceted outline itself (any corner of a cell inside
        // counts), so the trigger follows exactly what is drawn. Only cells
        // near the grown mask are candidates (the faceted outline can stray
        // slightly outside it); cell corners are classified a scanline at a
        // time with the same crossing rule as PointInPolygon.
        var near = NearMask();
        var corner = CornersInside();
        int LW = gridW + 1;
        for (int y = 0; y < gridH; y++)
            for (int x = 0; x < gridW; x++)
            {
                if (!near[y * gridW + x]) continue;
                int l = y * LW + x;
                if (!(corner[l] || corner[l + 1] || corner[l + LW] || corner[l + LW + 1])) continue;
                int sx = x - pad, sy = y - pad;
                mask[(sy + oy) * W + sx + ox] = true;
            }
        for (int y = by0; y < by1; y++)
            for (int x = bx0; x < bx1; x++)
                mask[(y + oy) * W + x + ox] = true;
        cachedBox = boxLocal;
        cachedColliderPath = Outline(mask, W, H, ox, oy, .3f * Unit * ppu);
        return cachedColliderPath;
    }

    // Cells within a few cells (a square of radius r) of the grown mask.
    // Separable: a row pass then a column pass over running counts.
    bool[] NearMask()
    {
        int r = Mathf.CeilToInt(SimplifyU * Unit * ppu) + 1;
        int W = gridW, H = gridH;
        var rows = new bool[W * H];
        var prefix = new int[Mathf.Max(W, H) + 1];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++) prefix[x + 1] = prefix[x] + (solid[y * W + x] ? 1 : 0);
            for (int x = 0; x < W; x++)
                rows[y * W + x] = prefix[Mathf.Min(W, x + r + 1)] - prefix[Mathf.Max(0, x - r)] > 0;
        }
        var near = new bool[W * H];
        for (int x = 0; x < W; x++)
        {
            for (int y = 0; y < H; y++) prefix[y + 1] = prefix[y] + (rows[y * W + x] ? 1 : 0);
            for (int y = 0; y < H; y++)
                near[y * W + x] = prefix[Mathf.Min(H, y + r + 1)] - prefix[Mathf.Max(0, y - r)] > 0;
        }
        return near;
    }

    // Whether each cell corner of the grid ((gridW + 1) x (gridH + 1)
    // lattice) lies inside Polygon -- PointInPolygon's even-odd crossing
    // rule, evaluated a scanline at a time instead of once per point.
    bool[] CornersInside()
    {
        int LW = gridW + 1, LH = gridH + 1;
        var inside = new bool[LW * LH];
        var poly = Polygon;
        var xs = new float[poly.Length];
        for (int ly = 0; ly < LH; ly++)
        {
            float qy = (ly - pad - pivotPx.y) / ppu;
            int count = 0;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > qy) != (poly[j].y > qy))
                    xs[count++] = (poly[j].x - poly[i].x) * (qy - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x;
            if (count == 0) continue;
            Array.Sort(xs, 0, count);
            int passed = 0;   // crossings at or left of qx
            for (int lx = 0; lx < LW; lx++)
            {
                float qx = (lx - pad - pivotPx.x) / ppu;
                while (passed < count && xs[passed] <= qx) passed++;
                inside[ly * LW + lx] = ((count - passed) & 1) == 1;
            }
        }
        return inside;
    }

    // Disk dilation by R: every interior cell within R of a solid cell (the
    // outer ring of the grid stays clear for the trace). `dist` holds each
    // cell's squared distance to the nearest solid cell.
    static bool[] Dilate(int[] dist, int W, int H, float R)
    {
        var dst = new bool[W * H];
        float r2 = R * R;
        for (int y = 1; y < H - 1; y++)
            for (int x = 1; x < W - 1; x++)
                if (dist[y * W + x] <= r2) dst[y * W + x] = true;
        return dst;
    }

    // Exact squared Euclidean distance transform (Felzenszwalb-Huttenlocher):
    // two separable 1-D lower-envelope passes, O(W * H). It replaced a brute
    // disk stamp costing ~R^2 per solid pixel (R ~ 34 px on a 243 px hull:
    // a quarter of a second on the main thread).
    public static int[] SquaredDistance(bool[] src, int W, int H)
    {
        const int Inf = 1 << 28;
        int n = Mathf.Max(W, H);
        var f = new int[n];
        var d = new int[n];
        var v = new int[n];
        var z = new double[n + 1];
        var dist = new int[W * H];
        for (int x = 0; x < W; x++)
        {
            for (int y = 0; y < H; y++) f[y] = src[y * W + x] ? 0 : Inf;
            Envelope(f, H, d, v, z);
            for (int y = 0; y < H; y++) dist[y * W + x] = d[y];
        }
        for (int y = 0; y < H; y++)
        {
            int row = y * W;
            for (int x = 0; x < W; x++) f[x] = dist[row + x];
            Envelope(f, W, d, v, z);
            for (int x = 0; x < W; x++) dist[row + x] = d[x];
        }
        return dist;
    }

    // Lower envelope of the parabolas (q - p)^2 + f[p], sampled at 0..n-1.
    static void Envelope(int[] f, int n, int[] d, int[] v, double[] z)
    {
        int k = 0;
        v[0] = 0;
        z[0] = double.NegativeInfinity;
        z[1] = double.PositiveInfinity;
        for (int q = 1; q < n; q++)
        {
            double s;
            while (true)
            {
                int p = v[k];
                s = ((f[q] + (double)q * q) - (f[p] + (double)p * p)) / (2.0 * (q - p));
                if (s > z[k]) break;
                k--;
            }
            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = double.PositiveInfinity;
        }
        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q) k++;
            long dq = q - v[k];
            long val = dq * dq + f[v[k]];
            d[q] = val > int.MaxValue ? int.MaxValue : (int)val;
        }
    }

    static void FillHoles(bool[] m, int W, int H)
    {
        var outside = new bool[W * H];
        var stack = new Stack<int>();
        for (int x = 0; x < W; x++) { stack.Push(x); stack.Push((H - 1) * W + x); }
        for (int y = 0; y < H; y++) { stack.Push(y * W); stack.Push(y * W + W - 1); }
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            if (outside[i] || m[i]) continue;
            outside[i] = true;
            int x = i % W, y = i / W;
            if (x > 0) stack.Push(i - 1);
            if (x < W - 1) stack.Push(i + 1);
            if (y > 0) stack.Push(i - W);
            if (y < H - 1) stack.Push(i + W);
        }
        for (int i = 0; i < m.Length; i++) if (!outside[i]) m[i] = true;
    }

    static int CountComponents(bool[] m, int W, int H)
    {
        var seen = new bool[W * H];
        var stack = new Stack<int>();
        int count = 0;
        for (int s = 0; s < m.Length; s++)
        {
            if (!m[s] || seen[s]) continue;
            count++;
            stack.Push(s);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                if (seen[i] || !m[i]) continue;
                seen[i] = true;
                int x = i % W, y = i / W;
                if (x > 0) stack.Push(i - 1);
                if (x < W - 1) stack.Push(i + 1);
                if (y > 0) stack.Push(i - W);
                if (y < H - 1) stack.Push(i + W);
            }
        }
        return count;
    }

    static void FillConvexHull(bool[] m, int W, int H)
    {
        var pts = new List<Vector2>();
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (m[y * W + x]) pts.Add(new Vector2(x + .5f, y + .5f));
        var hull = ConvexHull(pts);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (PointInPolygon(hull, new Vector2(x + .5f, y + .5f))) m[y * W + x] = true;
    }

    static Vector2[] ConvexHull(List<Vector2> p)
    {
        p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        var h = new Vector2[p.Count * 2];
        int k = 0;
        for (int i = 0; i < p.Count; i++)
        {
            while (k >= 2 && Cross(h[k - 2], h[k - 1], p[i]) <= 0) k--;
            h[k++] = p[i];
        }
        for (int i = p.Count - 2, t = k + 1; i >= 0; i--)
        {
            while (k >= t && Cross(h[k - 2], h[k - 1], p[i]) <= 0) k--;
            h[k++] = p[i];
        }
        Array.Resize(ref h, Mathf.Max(k - 1, 0));
        return h;
    }

    static float Cross(Vector2 o, Vector2 a, Vector2 b)
    {
        return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    }

    // Crack-following trace of the outer boundary along pixel edges, solid on
    // the left, so the loop comes out counter-clockwise.
    static List<Vector2Int> Trace(bool[] m, int W, int H)
    {
        int VW = W + 1;
        var nextA = new int[VW * (H + 1)];
        var nextB = new int[VW * (H + 1)];
        for (int i = 0; i < nextA.Length; i++) { nextA[i] = -1; nextB[i] = -1; }
        int start = -1;
        Action<int, int> add = (from, to) =>
        {
            if (nextA[from] < 0) nextA[from] = to; else nextB[from] = to;
            if (start < 0) start = from;
        };
        for (int y = 1; y < H - 1; y++)
            for (int x = 1; x < W - 1; x++)
            {
                if (!m[y * W + x]) continue;
                if (!m[(y - 1) * W + x]) add(y * VW + x, y * VW + x + 1);
                if (!m[y * W + x + 1]) add(y * VW + x + 1, (y + 1) * VW + x + 1);
                if (!m[(y + 1) * W + x]) add((y + 1) * VW + x + 1, (y + 1) * VW + x);
                if (!m[y * W + x - 1]) add((y + 1) * VW + x, y * VW + x);
            }
        var loop = new List<Vector2Int>();
        if (start < 0) return loop;
        int v = start, prevDx = 1, prevDy = 0;
        int guard = nextA.Length * 2;
        do
        {
            loop.Add(new Vector2Int(v % VW, v / VW));
            int a = nextA[v], b = nextB[v], n;
            if (b < 0) { n = a; nextA[v] = -1; }
            else
            {
                // Pinch vertex: take the left turn so the walk hugs the solid.
                int ax = a % VW - v % VW, ay = a / VW - v / VW;
                bool aLeft = prevDx * ay - prevDy * ax > 0;
                if (aLeft) { n = a; nextA[v] = b; nextB[v] = -1; }
                else { n = b; nextB[v] = -1; }
            }
            if (n < 0) break;
            prevDx = n % VW - v % VW; prevDy = n / VW - v / VW;
            v = n;
        } while (v != start && --guard > 0);
        return loop;
    }

    // Douglas-Peucker on a closed loop, anchored at the nose and at the point
    // farthest from it so both always survive.
    static List<Vector2> SimplifyClosed(List<Vector2Int> loop, int nose, float eps)
    {
        int n = loop.Count;
        var p = new Vector2[n];
        for (int i = 0; i < n; i++) p[i] = loop[(nose + i) % n];
        int far = 0; float best = -1f;
        for (int i = 1; i < n; i++)
        {
            float d = (p[i] - p[0]).sqrMagnitude;
            if (d > best) { best = d; far = i; }
        }
        var keep = new bool[n + 1];
        keep[0] = keep[far] = keep[n] = true;
        var ext = new Vector2[n + 1];
        Array.Copy(p, ext, n);
        ext[n] = p[0];
        DouglasPeucker(ext, 0, far, eps, keep);
        DouglasPeucker(ext, far, n, eps, keep);
        var outList = new List<Vector2>();
        for (int i = 0; i < n; i++) if (keep[i]) outList.Add(p[i]);
        return outList;
    }

    static void DouglasPeucker(Vector2[] p, int a, int b, float eps, bool[] keep)
    {
        if (b <= a + 1) return;
        float best = -1f; int index = -1;
        Vector2 A = p[a], B = p[b], AB = B - A;
        float len = AB.magnitude;
        for (int i = a + 1; i < b; i++)
        {
            float d = len > 1e-5f ? Mathf.Abs(AB.x * (p[i].y - A.y) - AB.y * (p[i].x - A.x)) / len
                                  : (p[i] - A).magnitude;
            if (d > best) { best = d; index = i; }
        }
        if (best > eps)
        {
            keep[index] = true;
            DouglasPeucker(p, a, index, eps, keep);
            DouglasPeucker(p, index, b, eps, keep);
        }
    }

    public static float SignedArea(Vector2[] p)
    {
        float a = 0f;
        for (int i = 0, j = p.Length - 1; i < p.Length; j = i++) a += p[j].x * p[i].y - p[i].x * p[j].y;
        return a * .5f;
    }

    public static bool PointInPolygon(Vector2[] poly, Vector2 q)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].y > q.y) != (poly[j].y > q.y) &&
                q.x < (poly[j].x - poly[i].x) * (q.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        }
        return inside;
    }

    // ------------------------------------------------------------------
    // Mesh template
    // ------------------------------------------------------------------

    // Atlas cells (Resources/Shield/shield_atlas.png, 4 x 64 px).
    public const int CellPlate = 0, CellCracked = 1, CellWhite = 2, CellHot = 3;

    public static Vector2 CellUv(int cell, float u, float v)
    {
        const float texel = 1f / 256f;
        float u0 = cell * .25f + texel, u1 = (cell + 1) * .25f - texel;
        float v0 = 1f / 64f, v1 = 1f - 1f / 64f;
        return new Vector2(Mathf.Lerp(u0, u1, u), Mathf.Lerp(v0, v1, v));
    }

    public static readonly Vector2 WhiteUv = CellUv(CellWhite, .5f, .5f);

    void BuildGeometry()
    {
        var poly = Polygon;
        int n = poly.Length;
        float u = Unit;

        var edgeNormal = new Vector2[n];
        var edgeLen = new float[n];
        float perimeter = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector2 d = poly[(i + 1) % n] - poly[i];
            edgeLen[i] = d.magnitude;
            perimeter += edgeLen[i];
            edgeNormal[i] = edgeLen[i] > 1e-6f ? new Vector2(d.y, -d.x) / edgeLen[i] : Vector2.zero;
        }
        Perimeter = perimeter;

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in poly)
        {
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
        }
        OutlineBounds = Rect.MinMaxRect(minX, minY, maxX, maxY);

        // Uniform samples; polygon corners are always samples and carry a
        // mitred normal so the ink keeps its sharp corners.
        var sp = new List<Vector2>();
        var sn = new List<Vector2>();
        var sa = new List<float>();
        float run = 0f;
        for (int i = 0; i < n; i++)
        {
            int k = Mathf.Max(1, Mathf.CeilToInt(edgeLen[i] / (SampleU * u)));
            Vector2 a = poly[i], b = poly[(i + 1) % n];
            for (int j = 0; j < k; j++)
            {
                float t = j / (float)k;
                sp.Add(Vector2.Lerp(a, b, t));
                sa.Add((run + edgeLen[i] * t) / Mathf.Max(perimeter, 1e-6f));
                if (j == 0) sn.Add(Miter(edgeNormal[(i - 1 + n) % n], edgeNormal[i]));
                else sn.Add(edgeNormal[i]);
            }
            run += edgeLen[i];
        }
        SamplePoint = sp.ToArray();
        SampleNormal = sn.ToArray();
        SampleArc = sa.ToArray();
        int m = SamplePoint.Length;

        // Plates: per polygon edge, centred, hex cell stretched over a quad.
        var plateA = new List<Vector2>();
        var plateB = new List<Vector2>();
        var plateN = new List<Vector2>();
        var plateArc = new List<float>();
        run = 0f;
        float plateLen = PlateLenU * u, gap = PlateGapU * u;
        for (int i = 0; i < n; i++)
        {
            float L = edgeLen[i];
            int count = Mathf.FloorToInt((L + gap) / (plateLen + gap));
            float len = plateLen;
            if (count == 0 && L > 1.1f * u) { count = 1; len = L - gap; }
            if (count > 0)
            {
                Vector2 a = poly[i], dir = (poly[(i + 1) % n] - a) / L;
                float used = count * len + (count - 1) * gap;
                float s0 = (L - used) * .5f;
                for (int j = 0; j < count; j++)
                {
                    float s = s0 + j * (len + gap);
                    plateA.Add(a + dir * s);
                    plateB.Add(a + dir * (s + len));
                    plateN.Add(edgeNormal[i]);
                    plateArc.Add((run + s + len * .5f) / Mathf.Max(perimeter, 1e-6f));
                }
            }
            run += L;
        }
        PlateCount = plateA.Count;

        // Fill fan (triangulated outline).
        var fillTris = Triangulate(poly);
        FillVertexCount = n;
        QuadCount = m + PlateCount + m;
        int vCount = n + QuadCount * 4;
        Vertices = new Vector3[vCount];
        Uvs = new Vector2[vCount];
        QuadKind = new byte[QuadCount];
        QuadArc = new float[QuadCount];
        QuadShadow = new bool[QuadCount];
        PlateQuad = new int[PlateCount];
        PlateCenter = new Vector2[PlateCount];
        PlateNormal = new Vector2[PlateCount];
        Triangles = new int[fillTris.Count + QuadCount * 6];

        for (int i = 0; i < n; i++) { Vertices[i] = poly[i]; Uvs[i] = WhiteUv; }
        for (int i = 0; i < fillTris.Count; i++) Triangles[i] = fillTris[i];
        int tri = fillTris.Count;
        int q = 0;

        // Ink stroke, outside the outline.
        for (int k = 0; k < m; k++, q++)
        {
            int k1 = (k + 1) % m;
            SetQuad(q, ref tri,
                SamplePoint[k] - SampleNormal[k] * (.1f * u), SamplePoint[k] + SampleNormal[k] * (InkU * u),
                SamplePoint[k1] + SampleNormal[k1] * (InkU * u), SamplePoint[k1] - SampleNormal[k1] * (.1f * u),
                CellWhite, KindInk, ArcMid(SampleArc[k], SampleArc[k1]));
        }
        // Hex plates, inside the line.
        for (int p = 0; p < PlateCount; p++, q++)
        {
            Vector2 nrm = plateN[p];
            float inner = -(LineU + PlateU) * u, outer = -(LineU - .25f) * u;
            SetQuad(q, ref tri,
                plateA[p] + nrm * inner, plateA[p] + nrm * outer,
                plateB[p] + nrm * outer, plateB[p] + nrm * inner,
                CellPlate, KindPlate, plateArc[p]);
            QuadShadow[q] = Vector2.Dot(nrm, new Vector2(-.6f, .8f)) < -.25f;   // lit from the upper left
            PlateQuad[p] = q;
            PlateCenter[p] = (plateA[p] + plateB[p]) * .5f + nrm * ((inner + outer) * .5f);
            PlateNormal[p] = nrm;
        }
        // Teal energy line, on top of the plates' outer edge.
        for (int k = 0; k < m; k++, q++)
        {
            int k1 = (k + 1) % m;
            SetQuad(q, ref tri,
                SamplePoint[k] - SampleNormal[k] * (LineU * u), SamplePoint[k] + SampleNormal[k] * (.05f * u),
                SamplePoint[k1] + SampleNormal[k1] * (.05f * u), SamplePoint[k1] - SampleNormal[k1] * (LineU * u),
                CellWhite, KindLine, ArcMid(SampleArc[k], SampleArc[k1]));
        }
    }

    static float ArcMid(float a, float b)
    {
        if (b < a) b += 1f;
        float mid = (a + b) * .5f;
        return mid >= 1f ? mid - 1f : mid;
    }

    void SetQuad(int q, ref int tri, Vector2 a, Vector2 b, Vector2 c, Vector2 d, int cell, byte kind, float arc)
    {
        int v = FillVertexCount + q * 4;
        Vertices[v] = a; Vertices[v + 1] = b; Vertices[v + 2] = c; Vertices[v + 3] = d;
        if (cell == CellWhite)
        {
            Uvs[v] = Uvs[v + 1] = Uvs[v + 2] = Uvs[v + 3] = WhiteUv;
        }
        else
        {
            Uvs[v] = CellUv(cell, 0f, 0f); Uvs[v + 1] = CellUv(cell, 0f, 1f);
            Uvs[v + 2] = CellUv(cell, 1f, 1f); Uvs[v + 3] = CellUv(cell, 1f, 0f);
        }
        Triangles[tri++] = v; Triangles[tri++] = v + 1; Triangles[tri++] = v + 2;
        Triangles[tri++] = v; Triangles[tri++] = v + 2; Triangles[tri++] = v + 3;
        QuadKind[q] = kind;
        QuadArc[q] = arc;
    }

    static Vector2 Miter(Vector2 n0, Vector2 n1)
    {
        Vector2 sum = n0 + n1;
        if (sum.sqrMagnitude < 1e-6f) return n1;
        Vector2 dir = sum.normalized;
        float cos = Vector2.Dot(dir, n1);
        return dir / Mathf.Max(cos, .5f);   // miter length capped at 2x
    }

    // Ear clipping; falls back to a fan for a degenerate outline.
    static List<int> Triangulate(Vector2[] p)
    {
        var tris = new List<int>();
        var idx = new List<int>();
        for (int i = 0; i < p.Length; i++) idx.Add(i);
        int guard = p.Length * p.Length + 8;
        while (idx.Count > 3 && guard-- > 0)
        {
            bool clipped = false;
            for (int i = 0; i < idx.Count; i++)
            {
                int a = idx[(i - 1 + idx.Count) % idx.Count], b = idx[i], c = idx[(i + 1) % idx.Count];
                if (Cross(p[a], p[b], p[c]) <= 0f) continue;
                bool empty = true;
                for (int j = 0; j < idx.Count && empty; j++)
                {
                    int t = idx[j];
                    if (t == a || t == b || t == c) continue;
                    if (InTriangle(p[t], p[a], p[b], p[c])) empty = false;
                }
                if (!empty) continue;
                tris.Add(a); tris.Add(b); tris.Add(c);
                idx.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped) break;
        }
        if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
        else if (idx.Count > 3)
            for (int i = 1; i < idx.Count - 1; i++) { tris.Add(idx[0]); tris.Add(idx[i]); tris.Add(idx[i + 1]); }
        return tris;
    }

    static bool InTriangle(Vector2 q, Vector2 a, Vector2 b, Vector2 c)
    {
        return Cross(a, b, q) >= 0f && Cross(b, c, q) >= 0f && Cross(c, a, q) >= 0f;
    }

    // Nearest outline sample to a sprite-local point.
    public int NearestSample(Vector2 local)
    {
        int best = 0; float bestD = float.MaxValue;
        for (int i = 0; i < SamplePoint.Length; i++)
        {
            float d = (SamplePoint[i] - local).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    // Distance along the outline between two arc positions (0..0.5).
    public static float ArcDistance(float a, float b)
    {
        float d = Mathf.Abs(a - b);
        return d > .5f ? 1f - d : d;
    }
}
