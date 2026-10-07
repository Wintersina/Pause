using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Bakes ShipHitbox's two polygons per roster ship into
// Resources/Shield/hull_hitboxes.bytes (ShieldSilhouetteBaker writes it with
// the silhouettes: on hull import and before every build).
//
// Hull (the damage hitbox), from the ship's baked rest silhouette S:
//
//   body  = S opened by a disk of ThinPx: anything narrower than ~2*ThinPx
//           (antennae, wingtip lights, needle tips) drops out
//   core  = body eroded by InsetPx, largest piece; its boundary pixel centres
//           seed the outline
//   fence = S eroded by InsetPx: no edge of the polygon may leave it
//
//   then vertices are removed one at a time, cheapest first (least area
//   lost; a reflex vertex whose chord stays inside the fence even gains
//   area), each removal kept only if its chord stays inside the fence and
//   crosses no other edge, until TargetHullPoints (24) remain. The result is a
//   simple polygon whose every point lies on painted pixels at least InsetPx
//   in from the art's edge.
//
// Shield zone: the visible shield line (ShieldContour.ForShip's polygon)
// rasterised, grown by ShipHitbox.ShieldMargin (world units, converted with
// the hull's NormalizedHullScale) plus the simplification tolerance, traced
// and simplified to at most MaxShieldPoints, so it clears the shield line by
// at least the margin everywhere.
public static class ShipHitboxBaker
{
    public const string AssetPath = "Assets/Art/Resources/Shield/hull_hitboxes.bytes";

    public const float InsetPx = 2f;      // graze allowance, sprite pixels
    public const float ThinPx = 6f;       // opening radius: thinner parts are not body
    public const int TargetHullPoints = 24;
    public const int MaxHullPoints = 32;    // only when 24 can't keep MinCover
    public const float MinCover = .82f;     // of the main body's area
    public const int MaxShieldPoints = 32;
    const int Pad = 4;

    public sealed class Report
    {
        public int id;
        public ShipHitbox.Shape shape;
        public float bodyAreaPx, hullAreaPx;   // sprite pixels^2
        public float scale;                    // sprite-local units -> world
        public float ppu;
    }

    // ------------------------------------------------------------------
    // Bake
    // ------------------------------------------------------------------

    public static byte[] Compute() { List<Report> r; return Compute(out r); }

    public static byte[] Compute(out List<Report> reports)
    {
        reports = new List<Report>();
        var list = new List<KeyValuePair<int, ShipHitbox.Shape>>();
        foreach (int id in ShipId.All)
        {
            var rep = Build(id);
            if (rep == null) { Debug.LogWarning("ShipHitboxBaker: no art for ship " + id); continue; }
            reports.Add(rep);
            list.Add(new KeyValuePair<int, ShipHitbox.Shape>(id, rep.shape));
        }
        return ShipHitbox.Encode(ShipHitbox.ShieldMargin, list);
    }

    public static bool IsCurrent()
    {
        if (!File.Exists(AssetPath)) return false;
        var have = File.ReadAllBytes(AssetPath);
        var want = Compute();
        return have.Length == want.Length && System.Linq.Enumerable.SequenceEqual(have, want);
    }

    // Writes the file only when it differs. Returns true if it wrote.
    public static bool BakeIfStale()
    {
        var bytes = Compute();
        if (File.Exists(AssetPath))
        {
            var have = File.ReadAllBytes(AssetPath);
            if (have.Length == bytes.Length && System.Linq.Enumerable.SequenceEqual(have, bytes)) return false;
        }
        File.WriteAllBytes(AssetPath, bytes);
        AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
        ShipHitbox.Invalidate();
        Debug.Log("ShipHitboxBaker: baked " + AssetPath + " (" + bytes.Length + " bytes)");
        return true;
    }

    public static Report Build(int id)
    {
        if (!ShipHullArt.Has(id)) return null;
        var sprite = ShipHullArt.StockRest(id);
        if (sprite == null) return null;
        int w, h;
        var S = ShieldSilhouetteBaker.Silhouette(id, out w, out h);
        if (S == null) return null;
        var rep = new Report { id = id, ppu = sprite.pixelsPerUnit, scale = shopingShips.NormalizedHullScale(sprite) };
        float bodyArea, hullArea;
        var hull = HullPolygon(S, w, h, sprite.pivot, sprite.pixelsPerUnit, out bodyArea, out hullArea);
        var contour = ShieldContour.BuildMask(S, w, h, sprite.pivot, sprite.pixelsPerUnit);
        float marginPx = ShipHitbox.ShieldMargin / rep.scale * sprite.pixelsPerUnit;
        var shield = Grow(contour.Polygon, marginPx, sprite.pivot, sprite.pixelsPerUnit, MaxShieldPoints);
        rep.shape = new ShipHitbox.Shape { hull = hull, shield = shield };
        rep.bodyAreaPx = bodyArea;
        rep.hullAreaPx = hullArea;
        return rep;
    }

    // ------------------------------------------------------------------
    // Masks
    // ------------------------------------------------------------------

    // The sprite mask on a padded grid (a clear ring for the trace).
    static bool[] Padded(bool[] S, int w, int h, out int W, out int H)
    {
        W = w + 2 * Pad; H = h + 2 * Pad;
        var g = new bool[W * H];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                g[(y + Pad) * W + x + Pad] = S[y * w + x];
        return g;
    }

    static bool[] Not(bool[] m)
    {
        var r = new bool[m.Length];
        for (int i = 0; i < m.Length; i++) r[i] = !m[i];
        return r;
    }

    // Cells of m more than r from any cell outside m.
    public static bool[] Erode(bool[] m, int W, int H, float r)
    {
        var d = ShieldContour.SquaredDistance(Not(m), W, H);
        var e = new bool[m.Length];
        float r2 = r * r;
        for (int i = 0; i < m.Length; i++) e[i] = m[i] && d[i] > r2;
        return e;
    }

    // Cells within r of a cell of m.
    public static bool[] Dilate(bool[] m, int W, int H, float r)
    {
        var d = ShieldContour.SquaredDistance(m, W, H);
        var e = new bool[m.Length];
        float r2 = r * r;
        for (int i = 0; i < m.Length; i++) e[i] = d[i] <= r2;
        return e;
    }

    // The hull's main body on the padded grid: S opened by ThinPx.
    public static bool[] Body(bool[] Sg, int W, int H)
    {
        var opened = Dilate(Erode(Sg, W, H, ThinPx), W, H, ThinPx);
        for (int i = 0; i < opened.Length; i++) opened[i] &= Sg[i];
        return opened;
    }

    // The main-body area of ship `id` in sprite pixels (what the hull polygon
    // must mostly cover).
    public static int BodyArea(bool[] S, int w, int h)
    {
        int W, H;
        var body = Body(Padded(S, w, h, out W, out H), W, H);
        int n = 0;
        foreach (var b in body) if (b) n++;
        return n;
    }

    static bool[] LargestComponent(bool[] m, int W, int H)
    {
        var label = new int[m.Length];
        var stack = new Stack<int>();
        int best = 0, bestSize = 0, next = 0;
        for (int s = 0; s < m.Length; s++)
        {
            if (!m[s] || label[s] != 0) continue;
            next++;
            int size = 0;
            stack.Push(s);
            label[s] = next;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                size++;
                int x = i % W, y = i / W;
                if (x > 0 && m[i - 1] && label[i - 1] == 0) { label[i - 1] = next; stack.Push(i - 1); }
                if (x < W - 1 && m[i + 1] && label[i + 1] == 0) { label[i + 1] = next; stack.Push(i + 1); }
                if (y > 0 && m[i - W] && label[i - W] == 0) { label[i - W] = next; stack.Push(i - W); }
                if (y < H - 1 && m[i + W] && label[i + W] == 0) { label[i + W] = next; stack.Push(i + W); }
            }
            if (size > bestSize) { bestSize = size; best = next; }
        }
        var r = new bool[m.Length];
        for (int i = 0; i < m.Length; i++) r[i] = label[i] == best && best != 0;
        return r;
    }

    // ------------------------------------------------------------------
    // Hull polygon
    // ------------------------------------------------------------------

    public static Vector2[] HullPolygon(bool[] S, int w, int h, Vector2 pivotPx, float ppu,
                                        out float bodyArea, out float hullArea)
    {
        int W, H;
        var Sg = Padded(S, w, h, out W, out H);
        var body = Body(Sg, W, H);
        bodyArea = 0f;
        foreach (var b in body) if (b) bodyArea++;
        var fence = Erode(Sg, W, H, InsetPx);
        // The seed outline runs through the core's edge pixel centres, and a
        // chord between two of them may graze the neighbouring cells, so the
        // core sits a pixel and a half further in than the fence.
        var core = LargestComponent(Erode(body, W, H, InsetPx + 1.5f), W, H);
        CutHoles(core, fence, W, H);
        core = LargestComponent(core, W, H);

        // Boundary pixel centres of the core, CCW (grid coordinates).
        var crack = ShieldContour.Trace(core, W, H);
        var pts = new List<Vector2>();
        for (int i = 0; i < crack.Count; i++)
        {
            Vector2 a = crack[i], b = crack[(i + 1) % crack.Count];
            Vector2 d = b - a;
            Vector2 c = (a + b) * .5f + new Vector2(-d.y, d.x) * .5f;   // solid on the left
            if (pts.Count == 0 || pts[pts.Count - 1] != c) pts.Add(c);
        }
        while (pts.Count > 1 && pts[0] == pts[pts.Count - 1]) pts.RemoveAt(pts.Count - 1);
        // Drop straight-run interior points.
        for (int i = pts.Count - 1; i >= 0 && pts.Count > 3; i--)
        {
            Vector2 p = pts[(i + pts.Count - 1) % pts.Count], q = pts[i], r = pts[(i + 1) % pts.Count];
            if (Mathf.Abs(Cross(p, q, r)) < 1e-6f && Vector2.Dot(q - p, r - q) > 0f) pts.RemoveAt(i);
        }

        // 24 points; a hull that can't keep MinCover of its body in 24
        // (Crimson Halo: its outline has to dodge the gaps inside the halo)
        // gets a few more, never past MaxHullPoints.
        var seed = new List<Vector2>(pts);
        for (int budget = TargetHullPoints; budget <= MaxHullPoints; budget++)
        {
            pts = new List<Vector2>(seed);
            Reduce(pts, fence, W, H, budget);
            Relax(pts, fence, W, H);
            if (Mathf.Abs(ShieldContour.SignedArea(pts.ToArray())) >= MinCover * bodyArea) break;
        }

        hullArea = Mathf.Abs(ShieldContour.SignedArea(pts.ToArray()));
        var poly = new Vector2[pts.Count];
        for (int i = 0; i < poly.Length; i++)
            poly[i] = new Vector2((pts[i].x - Pad - pivotPx.x) / ppu, (pts[i].y - Pad - pivotPx.y) / ppu);
        return poly;
    }

    static float Cross(Vector2 o, Vector2 a, Vector2 b)
    {
        return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    }

    // A gap enclosed by the hull (Crimson Halo's ring) can't sit inside a
    // single outline, so each enclosed hole of the core is opened to the
    // outside along the shortest path through the core: a 3-px slit, taken
    // out of the core and the fence alike so no chord can close it again.
    static void CutHoles(bool[] core, bool[] fence, int W, int H)
    {
        for (int guard = 0; guard < 16; guard++)
        {
            // Outside = non-core cells reachable from the border.
            var outside = new bool[W * H];
            var queue = new Queue<int>();
            for (int x = 0; x < W; x++) { Seed(outside, core, queue, x); Seed(outside, core, queue, (H - 1) * W + x); }
            for (int y = 0; y < H; y++) { Seed(outside, core, queue, y * W); Seed(outside, core, queue, y * W + W - 1); }
            Flood(outside, queue, W, H, i => !core[i]);
            int hole = -1;
            for (int i = 0; i < core.Length && hole < 0; i++) if (!core[i] && !outside[i]) hole = i;
            if (hole < 0) return;

            // Shortest path from the hole through the core to the outside.
            var from = new int[W * H];
            for (int i = 0; i < from.Length; i++) from[i] = -2;
            for (int i = 0; i < core.Length; i++)
                if (!core[i] && !outside[i]) { from[i] = -1; queue.Enqueue(i); }
            int exit = -1;
            while (queue.Count > 0 && exit < 0)
            {
                int i = queue.Dequeue();
                int x = i % W, y = i / W;
                foreach (int j in new[] { x > 0 ? i - 1 : -1, x < W - 1 ? i + 1 : -1, y > 0 ? i - W : -1, y < H - 1 ? i + W : -1 })
                {
                    if (j < 0 || from[j] != -2) continue;
                    from[j] = i;
                    if (outside[j]) { exit = j; break; }
                    queue.Enqueue(j);
                }
            }
            queue.Clear();
            if (exit < 0) return;
            var path = new List<int>();
            for (int i = from[exit]; i >= 0 && core[i]; i = from[i]) path.Add(i);
            foreach (int i in path)
            {
                int x = i % W, y = i / W;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= W || yy >= H) continue;
                        core[yy * W + xx] = false;
                        fence[yy * W + xx] = false;
                    }
            }
        }
    }

    static void Seed(bool[] seen, bool[] core, Queue<int> q, int i)
    {
        if (seen[i] || core[i]) return;
        seen[i] = true;
        q.Enqueue(i);
    }

    static void Flood(bool[] seen, Queue<int> q, int W, int H, System.Func<int, bool> pass)
    {
        while (q.Count > 0)
        {
            int i = q.Dequeue();
            int x = i % W, y = i / W;
            if (x > 0) Visit(seen, q, i - 1, pass);
            if (x < W - 1) Visit(seen, q, i + 1, pass);
            if (y > 0) Visit(seen, q, i - W, pass);
            if (y < H - 1) Visit(seen, q, i + W, pass);
        }
    }

    static void Visit(bool[] seen, Queue<int> q, int j, System.Func<int, bool> pass)
    {
        if (seen[j] || !pass(j)) return;
        seen[j] = true;
        q.Enqueue(j);
    }

    // Removes vertices (cheapest area first) while chords stay in the fence
    // and the polygon stays simple, down to `max` points; keeps going past
    // that only while a removal gains area.
    static void Reduce(List<Vector2> pts, bool[] fence, int W, int H, int max)
    {
        var order = new List<int>();
        var cost = new List<float>();
        // Whether each vertex's chord (its neighbours joined) stays in the
        // fence: depends on the neighbours only, so it is cached and only
        // the two neighbours of a removed vertex are re-checked.
        var chordIn = new List<sbyte>();
        for (int i = 0; i < pts.Count; i++) chordIn.Add(-1);
        while (pts.Count > 3)
        {
            int n = pts.Count;
            order.Clear(); cost.Clear();
            for (int i = 0; i < n; i++)
            {
                order.Add(i);
                // CCW: a convex vertex's triangle is lost area (positive).
                cost.Add(Cross(pts[(i + n - 1) % n], pts[i], pts[(i + 1) % n]) * .5f);
            }
            order.Sort((a, b) => cost[a] != cost[b] ? cost[a].CompareTo(cost[b]) : a.CompareTo(b));
            int pick = -1;
            foreach (int i in order)
            {
                if (n <= max && cost[i] > 0f) break;
                if (chordIn[i] < 0)
                    chordIn[i] = (sbyte)(InFence(pts[(i + n - 1) % n], pts[(i + 1) % n], fence, W, H) ? 1 : 0);
                if (chordIn[i] == 0) continue;
                if (CanRemove(pts, i)) { pick = i; break; }
            }
            if (pick < 0) break;
            pts.RemoveAt(pick);
            chordIn.RemoveAt(pick);
            n--;
            chordIn[(pick + n - 1) % n] = -1;
            chordIn[pick % n] = -1;
            // Near the end, the two neighbours slide out to win back the
            // area the removal cut (their chords change with them).
            if (n <= 3 * max)
            {
                int left = (pick + n - 1) % n, right = pick % n;
                for (int round = 0; round < 3; round++)
                {
                    bool moved = RelaxVertex(pts, left, fence, W, H, 3f) | RelaxVertex(pts, right, fence, W, H, 3f);
                    if (!moved) break;
                }
                for (int d = -2; d <= 2; d++) chordIn[((pick + d) % n + n) % n] = -1;
            }
        }
    }

    // The chord replacing vertex k crosses no other edge.
    static bool CanRemove(List<Vector2> pts, int k)
    {
        int n = pts.Count;
        int ia = (k + n - 1) % n, ib = (k + 1) % n;
        Vector2 a = pts[ia], b = pts[ib];
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            // Edges ending at a, k or b meet the chord there by design (a
            // fold-back onto the chord is caught by the vertex check below).
            if (i == ia || i == k || i == ib || j == ia || j == k || j == ib) continue;
            if (SegmentsCross(a, b, pts[i], pts[j])) return false;
        }
        // The chord must not pass through another vertex either.
        for (int i = 0; i < n; i++)
        {
            if (i == ia || i == ib || i == k) continue;
            if (DistanceToSegment(pts[i], a, b) < .05f) return false;
        }
        return true;
    }

    // Every cell the segment passes through or touches is a fence cell
    // (exact: column by column, the segment's y span in that column).
    static bool InFence(Vector2 a, Vector2 b, bool[] fence, int W, int H)
    {
        const float e = 1e-3f;
        if (a.x > b.x) { var t = a; a = b; b = t; }
        int cx0 = Mathf.FloorToInt(a.x - e), cx1 = Mathf.FloorToInt(b.x + e);
        float dx = b.x - a.x;
        for (int cx = cx0; cx <= cx1; cx++)
        {
            float xa = Mathf.Max(a.x, cx), xb = Mathf.Min(b.x, cx + 1);
            float ya, yb;
            if (dx < 1e-6f) { ya = a.y; yb = b.y; }
            else
            {
                ya = a.y + (b.y - a.y) * (Mathf.Clamp(xa, a.x, b.x) - a.x) / dx;
                yb = a.y + (b.y - a.y) * (Mathf.Clamp(xb, a.x, b.x) - a.x) / dx;
            }
            int cy0 = Mathf.FloorToInt(Mathf.Min(ya, yb) - e), cy1 = Mathf.FloorToInt(Mathf.Max(ya, yb) + e);
            for (int cy = cy0; cy <= cy1; cy++)
                if (cx < 0 || cy < 0 || cx >= W || cy >= H || !fence[cy * W + cx]) return false;
        }
        return true;
    }

    // Slides each vertex (a few px, half-pixel steps) to wherever the
    // polygon gains the most area while both its edges stay in the fence
    // and cross nothing: the reduced outline pushed out to the inset line.
    static void Relax(List<Vector2> pts, bool[] fence, int W, int H)
    {
        for (int pass = 0; pass < 12; pass++)
        {
            bool moved = false;
            for (int k = 0; k < pts.Count; k++) moved |= RelaxVertex(pts, k, fence, W, H, 4f);
            if (!moved) break;
        }
    }

    static bool RelaxVertex(List<Vector2> pts, int k, bool[] fence, int W, int H, float reach)
    {
        const float Step = .5f;
        int n = pts.Count;
        Vector2 a = pts[(k + n - 1) % n], v = pts[k], b = pts[(k + 1) % n];
        Vector2 best = v;
        float bestGain = 1e-4f;
        for (float dy = -reach; dy <= reach; dy += Step)
            for (float dx = -reach; dx <= reach; dx += Step)
            {
                var c = new Vector2(v.x + dx, v.y + dy);
                // Area gained moving v to c (CCW): triangle (a, c, b) vs (a, v, b).
                float gain = (Cross(a, b, v) - Cross(a, b, c)) * .5f;
                if (gain <= bestGain) continue;
                if (!InFence(a, c, fence, W, H) || !InFence(c, b, fence, W, H)) continue;
                if (!EdgesClear(pts, k, c)) continue;
                best = c; bestGain = gain;
            }
        if (best == v) return false;
        pts[k] = best;
        return true;
    }

    // Vertex k moved to c: its two edges cross no other edge and pass
    // through no other vertex.
    static bool EdgesClear(List<Vector2> pts, int k, Vector2 c)
    {
        int n = pts.Count;
        int ia = (k + n - 1) % n, ib = (k + 1) % n;
        Vector2 a = pts[ia], b = pts[ib];
        if (Cross(a, c, b) == 0f) return false;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            if (i == ia || i == k || j == k) continue;          // the two moving edges
            // Edge (ib, ib+1) shares b with (c, b); (ia-1, ia) shares a with (a, c).
            if (i != ib && SegmentsCross(c, b, pts[i], pts[j])) return false;
            if (j != ia && SegmentsCross(a, c, pts[i], pts[j])) return false;
        }
        for (int i = 0; i < n; i++)
        {
            if (i == k) continue;
            if (i != ia && DistanceToSegment(pts[i], a, c) < .05f) return false;
            if (i != ib && DistanceToSegment(pts[i], c, b) < .05f) return false;
        }
        return true;
    }

    static bool SegmentsCross(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
        float d1 = Cross(q1, q2, p1), d2 = Cross(q1, q2, p2);
        float d3 = Cross(p1, p2, q1), d4 = Cross(p1, p2, q2);
        if (((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) &&
            ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f))) return true;
        // Touching / collinear overlap counts as crossing (keeps it simple).
        if (Mathf.Abs(d1) < 1e-4f && OnSegment(q1, q2, p1)) return true;
        if (Mathf.Abs(d2) < 1e-4f && OnSegment(q1, q2, p2)) return true;
        if (Mathf.Abs(d3) < 1e-4f && OnSegment(p1, p2, q1)) return true;
        if (Mathf.Abs(d4) < 1e-4f && OnSegment(p1, p2, q2)) return true;
        return false;
    }

    static bool OnSegment(Vector2 a, Vector2 b, Vector2 p)
    {
        return p.x >= Mathf.Min(a.x, b.x) - 1e-4f && p.x <= Mathf.Max(a.x, b.x) + 1e-4f &&
               p.y >= Mathf.Min(a.y, b.y) - 1e-4f && p.y <= Mathf.Max(a.y, b.y) + 1e-4f;
    }

    public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return (a + ab * t - p).magnitude;
    }

    // Distance from p to the nearest edge of a closed polygon.
    public static float DistanceToPolygon(Vector2 p, Vector2[] poly)
    {
        float best = float.MaxValue;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            best = Mathf.Min(best, DistanceToSegment(p, poly[j], poly[i]));
        return best;
    }

    // ------------------------------------------------------------------
    // Shield zone
    // ------------------------------------------------------------------

    // `poly` (sprite-local units) grown by marginPx sprite pixels, traced and
    // simplified to at most maxPoints; never inside the margin anywhere.
    public static Vector2[] Grow(Vector2[] poly, float marginPx, Vector2 pivotPx, float ppu, int maxPoints)
    {
        // The polygon in sprite pixels.
        var p = new Vector2[poly.Length];
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < p.Length; i++)
        {
            p[i] = poly[i] * ppu + pivotPx;
            minX = Mathf.Min(minX, p[i].x); maxX = Mathf.Max(maxX, p[i].x);
            minY = Mathf.Min(minY, p[i].y); maxY = Mathf.Max(maxY, p[i].y);
        }
        float eps = 1.5f;
        for (int attempt = 0; attempt < 12; attempt++, eps *= 1.4f)
        {
            // Grow by the margin, the simplification tolerance and a pixel of
            // rasterisation slack, so the simplified outline still clears it.
            float grow = marginPx + eps + 1.5f;
            int pad = Mathf.CeilToInt(grow) + 3;
            int ox = Mathf.FloorToInt(minX) - pad, oy = Mathf.FloorToInt(minY) - pad;
            int W = Mathf.CeilToInt(maxX) + pad - ox, H = Mathf.CeilToInt(maxY) + pad - oy;
            var inside = new bool[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    inside[y * W + x] = ShieldContour.PointInPolygon(p, new Vector2(ox + x + .5f, oy + y + .5f));
            var grown = Dilate(inside, W, H, grow);
            for (int x = 0; x < W; x++) { grown[x] = false; grown[(H - 1) * W + x] = false; }
            for (int y = 0; y < H; y++) { grown[y * W] = false; grown[y * W + W - 1] = false; }
            var loop = ShieldContour.Trace(grown, W, H);
            var simple = SimplifyClosed(loop, eps);
            if (simple.Count > maxPoints && attempt < 11) continue;
            var outPoly = new Vector2[simple.Count];
            for (int i = 0; i < outPoly.Length; i++)
                outPoly[i] = (new Vector2(simple[i].x + ox, simple[i].y + oy) - pivotPx) / ppu;
            return outPoly;
        }
        return poly;
    }

    // Douglas-Peucker on a closed loop, anchored at the first point and the
    // point farthest from it.
    static List<Vector2> SimplifyClosed(List<Vector2Int> loop, float eps)
    {
        int n = loop.Count;
        var p = new Vector2[n + 1];
        for (int i = 0; i < n; i++) p[i] = loop[i];
        p[n] = p[0];
        int far = 0; float best = -1f;
        for (int i = 1; i < n; i++)
        {
            float d = (p[i] - p[0]).sqrMagnitude;
            if (d > best) { best = d; far = i; }
        }
        var keep = new bool[n + 1];
        keep[0] = keep[far] = keep[n] = true;
        DouglasPeucker(p, 0, far, eps, keep);
        DouglasPeucker(p, far, n, eps, keep);
        var outList = new List<Vector2>();
        for (int i = 0; i < n; i++) if (keep[i]) outList.Add(p[i]);
        return outList;
    }

    static void DouglasPeucker(Vector2[] p, int a, int b, float eps, bool[] keep)
    {
        if (b <= a + 1) return;
        float best = -1f; int index = -1;
        for (int i = a + 1; i < b; i++)
        {
            float d = DistanceToSegment(p[i], p[a], p[b]);
            if (d > best) { best = d; index = i; }
        }
        if (best > eps)
        {
            keep[index] = true;
            DouglasPeucker(p, a, index, eps, keep);
            DouglasPeucker(p, index, b, eps, keep);
        }
    }
}
