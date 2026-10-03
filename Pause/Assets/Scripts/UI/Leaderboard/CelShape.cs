using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Flat cel plate for UI, per docs/art-style.md: one flat fill, a thick INK
// contour, optionally ONE hard offset shadow (a cel drop shadow, never soft).
// Shapes are angular: 45-degree chamfered rectangles or slanted slabs
// (parallelograms, the Akira title-card stripe). No textures, no gradients.
//
// Drawn as a mesh, so it scales crisply at any size and needs no sprite.
[RequireComponent(typeof(CanvasRenderer))]
public class CelShape : MaskableGraphic
{
    public enum Kind { Chamfer, Slab }

    public Kind kind = Kind.Chamfer;
    // Chamfer: the 45-degree cut, in canvas units, and which corners get it.
    public float cut = 14f;
    public bool cutTopLeft = true, cutTopRight = true, cutBottomRight = true, cutBottomLeft = true;
    // Slab: how far the top edge is pushed right of the bottom edge.
    public float slant = 18f;

    public Color fill = AkiraPalette.Night1;
    public Color ink = AkiraPalette.Ink;
    public float inkWidth = 4f;
    // Contour only (a panel line): the ink ring is drawn, the inside is left empty.
    public bool hollow;

    public bool hasShadow;
    public Color shadow = AkiraPalette.Ink;
    public Vector2 shadowOffset = new Vector2(6f, -6f);

    public static CelShape Add(GameObject go, Kind kind, Color fill, float inkWidth = 4f)
    {
        var shape = go.AddComponent<CelShape>();
        shape.kind = kind;
        shape.fill = fill;
        shape.inkWidth = inkWidth;
        shape.raycastTarget = false;
        return shape;
    }

    public CelShape Cuts(bool tl, bool tr, bool br, bool bl)
    {
        cutTopLeft = tl; cutTopRight = tr; cutBottomRight = br; cutBottomLeft = bl;
        SetVerticesDirty();
        return this;
    }

    public CelShape Shadow(Color color, Vector2 offset)
    {
        hasShadow = true;
        shadow = color;
        shadowOffset = offset;
        SetVerticesDirty();
        return this;
    }

    public void SetFill(Color c)
    {
        fill = c;
        SetVerticesDirty();
    }

    // The outline, counter-clockwise, in local (rect) coordinates.
    public List<Vector2> Outline()
    {
        Rect r = rectTransform.rect;
        var pts = new List<Vector2>();
        if (kind == Kind.Slab)
        {
            float s = Mathf.Clamp(slant, -r.width * .4f, r.width * .4f);
            pts.Add(new Vector2(r.xMin, r.yMin));
            pts.Add(new Vector2(r.xMax - s, r.yMin));
            pts.Add(new Vector2(r.xMax, r.yMax));
            pts.Add(new Vector2(r.xMin + s, r.yMax));
            return pts;
        }
        float c = Mathf.Clamp(cut, 0f, Mathf.Min(r.width, r.height) * .45f);
        float tl = cutTopLeft ? c : 0f, tr = cutTopRight ? c : 0f;
        float br = cutBottomRight ? c : 0f, bl = cutBottomLeft ? c : 0f;
        // Counter-clockwise from the bottom-left corner.
        Add(pts, new Vector2(r.xMin, r.yMin + bl), new Vector2(r.xMin + bl, r.yMin), bl);
        Add(pts, new Vector2(r.xMax - br, r.yMin), new Vector2(r.xMax, r.yMin + br), br);
        Add(pts, new Vector2(r.xMax, r.yMax - tr), new Vector2(r.xMax - tr, r.yMax), tr);
        Add(pts, new Vector2(r.xMin + tl, r.yMax), new Vector2(r.xMin, r.yMax - tl), tl);
        return pts;
    }

    static void Add(List<Vector2> pts, Vector2 a, Vector2 b, float c)
    {
        pts.Add(a);
        if (c > 0f) pts.Add(b);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var outline = Outline();
        if (hasShadow)
        {
            var moved = new List<Vector2>(outline.Count);
            foreach (var p in outline) moved.Add(p + shadowOffset);
            Fan(vh, moved, shadow);
        }
        if (hollow)
        {
            Ring(vh, outline, Inset(outline, inkWidth), ink);
            return;
        }
        if (inkWidth > 0f)
        {
            Fan(vh, outline, ink);
            Fan(vh, Inset(outline, inkWidth), fill);
        }
        else Fan(vh, outline, fill);
    }

    void Ring(VertexHelper vh, List<Vector2> outer, List<Vector2> inner, Color c)
    {
        Color32 col = c * color;
        int n = outer.Count;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            int start = vh.currentVertCount;
            vh.AddVert(new Vector3(outer[i].x, outer[i].y), col, Vector2.zero);
            vh.AddVert(new Vector3(outer[j].x, outer[j].y), col, Vector2.zero);
            vh.AddVert(new Vector3(inner[j].x, inner[j].y), col, Vector2.zero);
            vh.AddVert(new Vector3(inner[i].x, inner[i].y), col, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }

    void Fan(VertexHelper vh, List<Vector2> pts, Color c)
    {
        if (pts.Count < 3) return;
        Color32 col = c * color;
        int start = vh.currentVertCount;
        foreach (var p in pts) vh.AddVert(new Vector3(p.x, p.y), col, Vector2.zero);
        for (int i = 1; i < pts.Count - 1; i++) vh.AddTriangle(start, start + i, start + i + 1);
    }

    // Moves every edge of a convex counter-clockwise polygon inward by d and
    // re-intersects neighbours: the fill inside an ink contour of width d.
    public static List<Vector2> Inset(List<Vector2> pts, float d)
    {
        int n = pts.Count;
        var lines = new List<KeyValuePair<Vector2, Vector2>>(n);   // point, direction
        for (int i = 0; i < n; i++)
        {
            Vector2 a = pts[i], b = pts[(i + 1) % n];
            Vector2 dir = (b - a).normalized;
            Vector2 inward = new Vector2(-dir.y, dir.x);   // left of a CCW edge is inside
            lines.Add(new KeyValuePair<Vector2, Vector2>(a + inward * d, dir));
        }
        var result = new List<Vector2>(n);
        for (int i = 0; i < n; i++)
        {
            var l0 = lines[(i + n - 1) % n];
            var l1 = lines[i];
            float cross = l0.Value.x * l1.Value.y - l0.Value.y * l1.Value.x;
            if (Mathf.Abs(cross) < 1e-5f) { result.Add(l1.Key); continue; }
            Vector2 delta = l1.Key - l0.Key;
            float t = (delta.x * l1.Value.y - delta.y * l1.Value.x) / cross;
            result.Add(l0.Key + l0.Value * t);
        }
        return result;
    }
}

// docs/art-style.md section 1.1 (names match art-samples/src/akira.py).
public static class AkiraPalette
{
    public static readonly Color Night0 = Hex(0x070A16);
    public static readonly Color Night1 = Hex(0x0E1424);
    public static readonly Color Card = Hex(0x151B30);      // ui.py CARD
    public static readonly Color Indigo0 = Hex(0x1A1F45);
    public static readonly Color Indigo1 = Hex(0x2A2E6B);
    public static readonly Color Hairline = Hex(0x2E3560);  // ui.py inner panel line
    public static readonly Color Muted = Hex(0x8C93B8);     // ui.py MUTED
    public static readonly Color Red = Hex(0xD8232C);
    public static readonly Color RedShadow = Hex(0x86121F);
    public static readonly Color RedHi = Hex(0xFF5B45);
    public static readonly Color Sodium = Hex(0xF2862B);
    public static readonly Color Amber = Hex(0xFFB43C);
    public static readonly Color Teal = Hex(0x1FB5B9);
    public static readonly Color Cyan = Hex(0x6EF2EE);
    public static readonly Color TealShadow = Hex(0x0F5E6A);
    public static readonly Color Magenta = Hex(0xFF2E88);
    public static readonly Color Ink = Hex(0x140C14);
    public static readonly Color Bone = Hex(0xF4EAD4);

    public static Color Hex(int rgb, float a = 1f)
    {
        return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);
    }

    public static Color WithAlpha(Color c, float a) { c.a = a; return c; }
}
