using UnityEngine;

// ONE FOOTPRINT, TWO JOBS (docs/world-attacks-design.md FR2).
//
// The hit geometry of a jet, a wave band, a blast ring, a strike column or a
// lash and the dotted outline the telegraph draws are the SAME object, so the
// preview cannot disagree with the hitbox. It is a bag of convex polygons in
// world space (a column is a rectangle, a cone a triangle, a band with a gap
// two rectangles, a ring one quad per bar, a lash a chain of quads):
//
//   Touches(p, r)   does a circle of radius r at p overlap any polygon: the
//                   hit test (AttackHazardTest samples it; IHostileZone uses it)
//   loops           the outlines AttackPreview draws; by default each polygon's
//                   own edge loop, or explicit ones (a ring draws its two arcs
//                   and the gap edges, not 18 separate bars)
//
// Fixed capacity, built once, refilled in place: nothing allocates after the
// constructor (FR8). A pooled hazard owns one and rebuilds it as it moves.
public sealed class AttackShape
{
    public const int MaxPolys = 48, MaxPoints = 8, MaxLoops = 12, MaxLoopPoints = 64;

    readonly Vector2[] hit = new Vector2[MaxPolys * MaxPoints];
    readonly int[] hitStart = new int[MaxPolys], hitCount = new int[MaxPolys];
    int polys, hitUsed;
    readonly Vector2[] loop = new Vector2[MaxLoops * MaxLoopPoints];
    readonly int[] loopCount = new int[MaxLoops];
    int loops;
    bool building, buildForPreview;
    int buildStart, buildCount;

    public int PolyCount => polys;
    public int LoopCount => loops;
    public bool Empty => polys == 0;

    public void Clear() { polys = 0; hitUsed = 0; loops = 0; building = false; }

    // ---- building ---------------------------------------------------------------

    // A convex polygon, added point by point: BeginPoly(), Point() x3..8, EndPoly().
    // `preview` false: part of the hit geometry only (its outline is drawn by an explicit loop).
    public void BeginPoly(bool preview = true)
    {
        building = true;
        buildForPreview = preview;
        buildStart = hitUsed;
        buildCount = 0;
    }

    public void Point(Vector2 p)
    {
        if (!building || buildCount >= MaxPoints || hitUsed >= hit.Length) return;
        hit[hitUsed++] = p;
        buildCount++;
    }

    public void EndPoly()
    {
        if (!building) return;
        building = false;
        if (buildCount < 3 || polys >= MaxPolys) { hitUsed = buildStart; return; }
        hitStart[polys] = buildStart;
        hitCount[polys] = buildCount;
        polys++;
        if (buildForPreview) AddLoop(buildStart, buildCount, true);
    }

    // A thick segment a -> b of half-width `half`: a rectangle (a column, a bar, a lash link).
    public void AddQuad(Vector2 a, Vector2 b, float half, bool preview = true)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        Vector2 n = len > 1e-5f ? new Vector2(-d.y, d.x) / len * half : new Vector2(0f, half);
        BeginPoly(preview);
        Point(a - n); Point(b - n); Point(b + n); Point(a + n);
        EndPoly();
    }

    public void AddTriangle(Vector2 a, Vector2 b, Vector2 c, bool preview = true)
    {
        BeginPoly(preview);
        Point(a); Point(b); Point(c);
        EndPoly();
    }

    // An explicit outline to draw (closed, in order). Not part of the hit test.
    public void BeginLoop() { loopBuilding = true; loopStart = loops * MaxLoopPoints; loopLen = 0; }
    public void LoopPoint(Vector2 p)
    {
        if (!loopBuilding || loopLen >= MaxLoopPoints) return;
        loop[loopStart + loopLen++] = p;
    }
    public void EndLoop()
    {
        if (!loopBuilding) return;
        loopBuilding = false;
        if (loopLen < 2 || loops >= MaxLoops) return;
        loopCount[loops++] = loopLen;
    }
    bool loopBuilding;
    int loopStart, loopLen;

    void AddLoop(int start, int count, bool fromHit)
    {
        if (loops >= MaxLoops) return;
        int n = Mathf.Min(count, MaxLoopPoints);
        for (int i = 0; i < n; i++) loop[loops * MaxLoopPoints + i] = hit[start + i];
        loopCount[loops++] = n;
    }

    // ---- reading ----------------------------------------------------------------

    // The hit polygons (a pooled hazard copies them into its trigger collider).
    public int PolyLength(int poly) => hitCount[poly];
    public Vector2 PolyAt(int poly, int i) => hit[hitStart[poly] + i];

    public Vector2 LoopAt(int loopIndex, int i) => loop[loopIndex * MaxLoopPoints + i];
    public int LoopLength(int loopIndex) => loopCount[loopIndex];

    // Shifts the whole footprint (it rides the board).
    public void Offset(Vector2 d)
    {
        for (int i = 0; i < hitUsed; i++) hit[i] += d;
        for (int l = 0; l < loops; l++)
            for (int i = 0; i < loopCount[l]; i++) loop[l * MaxLoopPoints + i] += d;
    }

    // THE HIT TEST: a circle of radius r at p overlaps the footprint.
    public bool Touches(Vector2 p, float r)
    {
        float rr = r * r;
        for (int k = 0; k < polys; k++)
        {
            int s = hitStart[k], n = hitCount[k];
            bool inside = true;
            float sign = 0f;
            float best = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = hit[s + i], b = hit[s + (i + 1) % n];
                float cross = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
                if (sign == 0f) sign = cross;
                else if (cross * sign < 0f) inside = false;
                float d = HostileShots.SegmentDistanceSq(a, b, p);
                if (d < best) best = d;
            }
            if (inside || best <= rr) return true;
        }
        return false;
    }

    // Tight bounds of the hit polygons (broad-phase for a hazard's own tests, previews).
    public Rect Bounds()
    {
        if (hitUsed == 0) return new Rect();
        float x0 = float.PositiveInfinity, y0 = float.PositiveInfinity, x1 = float.NegativeInfinity, y1 = float.NegativeInfinity;
        for (int i = 0; i < hitUsed; i++)
        {
            var p = hit[i];
            if (p.x < x0) x0 = p.x; if (p.x > x1) x1 = p.x;
            if (p.y < y0) y0 = p.y; if (p.y > y1) y1 = p.y;
        }
        return Rect.MinMaxRect(x0, y0, x1, y1);
    }

    // Total edge length of the preview loops (the dot budget).
    public float LoopPerimeter()
    {
        float t = 0f;
        for (int l = 0; l < loops; l++)
        {
            int n = loopCount[l];
            for (int i = 0; i < n; i++) t += Vector2.Distance(loop[l * MaxLoopPoints + i], loop[l * MaxLoopPoints + (i + 1) % n]);
        }
        return t;
    }
}
