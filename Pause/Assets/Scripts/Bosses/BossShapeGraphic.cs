using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Fan-fills convex polygons (plate px), optionally clipped to one side of a
// vertical cut, offset so `origin` (plate px) sits at the rect's centre.
public abstract class BossShapeGraphic : MaskableGraphic
{
    public Vector2 origin = new Vector2(BossNameShapes.PlateW * .5f, BossNameShapes.PlateH * .5f);

    static readonly List<Vector2> clipA = new List<Vector2>(16), clipB = new List<Vector2>(16);

    protected void Poly(VertexHelper vh, Color c, float side, float cutX, params Vector2[] pts)
    {
        clipA.Clear();
        clipA.AddRange(pts);
        if (side != 0f)
        {
            clipB.Clear();
            for (int i = 0; i < clipA.Count; i++)
            {
                Vector2 a = clipA[i], b = clipA[(i + 1) % clipA.Count];
                bool ina = (a.x - cutX) * side >= 0f, inb = (b.x - cutX) * side >= 0f;
                if (ina) clipB.Add(a);
                if (ina != inb)
                {
                    float t = (cutX - a.x) / (b.x - a.x);
                    clipB.Add(Vector2.Lerp(a, b, t));
                }
            }
            clipA.Clear();
            clipA.AddRange(clipB);
        }
        if (clipA.Count < 3) return;
        int start = vh.currentVertCount;
        Color32 col = c * color; // the graphic's colour tints every tone
        foreach (var p in clipA)
            vh.AddVert(new Vector3(p.x - origin.x, origin.y - p.y, 0f), col, Vector4.zero);
        for (int i = 1; i < clipA.Count - 1; i++) vh.AddTriangle(start, start + i, start + i + 1);
    }

    // A straight stroke from a to b, w wide, square ends.
    protected void Stroke(VertexHelper vh, Color c, Vector2 a, Vector2 b, float w)
    {
        Vector2 d = (b - a).normalized, n = new Vector2(-d.y, d.x) * (w * .5f);
        a -= d * (w * .5f);
        b += d * (w * .5f);
        Poly(vh, c, 0f, 0f, a + n, b + n, b - n, a - n);
    }
}
