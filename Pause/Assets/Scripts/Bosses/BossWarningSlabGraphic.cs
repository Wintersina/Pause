using UnityEngine;
using UnityEngine.UI;

// The boss warning's code-drawn pieces, in the language of the boss intro's
// WARNING slab (Art/Resources/Bosses/warning.png): a leaning indigo plate
// with an ink contour and hard drop shadow, and bands of hazard stripes.
//
// Plate and Stripes are separate graphics so the stripes can be recoloured
// (CanvasRenderer.SetColor: no mesh rebuild, no allocation) while the plate
// keeps its own tones. The stripes' vertices are white; the tint is the
// warning's accent.
public class BossWarningSlabGraphic : MaskableGraphic
{
    public enum Part { Plate, Stripes }

    public Part part = Part.Plate;
    // How far the top edge leans right of the bottom edge, in rect units.
    public float lean = 8f;
    // Stripe band height (top; and bottom too when `bottomBand`).
    public float band = 12f;
    public bool bottomBand = true;
    public float stripeWidth = 12f;
    public float contour = 3f;
    public Vector2 shadow = new Vector2(4f, -4f);
    public float plateAlpha = .94f;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (part == Part.Plate) Plate(vh, r);
        else Stripes(vh, r);
    }

    void Plate(VertexHelper vh, Rect r)
    {
        Color ink = AkiraPalette.Ink;
        Color slab = AkiraPalette.WithAlpha(AkiraPalette.Indigo0, plateAlpha);
        Color foot = AkiraPalette.WithAlpha(AkiraPalette.Night1, plateAlpha);
        Slab(vh, ink, r.xMin + shadow.x, r.yMin + shadow.y, r.xMax + shadow.x, r.yMax + shadow.y);
        Slab(vh, ink, r.xMin, r.yMin, r.xMax, r.yMax);
        float c = contour;
        Slab(vh, slab, r.xMin + c * 1.5f, r.yMin + c, r.xMax - c * 1.5f, r.yMax - c);
        // a darker foot, like the name plate's
        float footTop = r.yMin + c + (r.height - 2f * c) * .16f;
        float k = lean * .16f;
        Quad(vh, foot,
             new Vector2(r.xMin + c * 1.5f, r.yMin + c), new Vector2(r.xMin + c * 1.5f + k, footTop),
             new Vector2(r.xMax - c * 1.5f - lean + k, footTop), new Vector2(r.xMax - c * 1.5f - lean, r.yMin + c));
    }

    // A parallelogram filling (x0,y0)-(x1,y1), its top edge `lean` to the right.
    void Slab(VertexHelper vh, Color c, float x0, float y0, float x1, float y1)
    {
        Quad(vh, c, new Vector2(x0, y0), new Vector2(x0 + lean, y1), new Vector2(x1, y1), new Vector2(x1 - lean, y0));
    }

    void Stripes(VertexHelper vh, Rect r)
    {
        float c = contour;
        float inset = c * 1.5f + lean + 2f;
        Band(vh, r.xMin + inset, r.xMax - inset + lean * .5f, r.yMax - c - 2f - band, r.yMax - c - 2f);
        if (bottomBand) Band(vh, r.xMin + inset - lean * .5f, r.xMax - inset, r.yMin + c + 2f, r.yMin + c + 2f + band);
    }

    // Stripes leaning 45 degrees across the band, whole ones only.
    void Band(VertexHelper vh, float x0, float x1, float y0, float y1)
    {
        float h = y1 - y0, w = stripeWidth;
        if (h <= 0f || w <= 0f) return;
        for (float x = x0; x + w + h <= x1 + .01f; x += 2f * w)
            Quad(vh, Color.white, new Vector2(x, y0), new Vector2(x + h, y1), new Vector2(x + h + w, y1), new Vector2(x + w, y0));
    }

    void Quad(VertexHelper vh, Color c, Vector2 a, Vector2 b, Vector2 d, Vector2 e)
    {
        int start = vh.currentVertCount;
        Color32 col = c * color;
        vh.AddVert(a, col, Vector4.zero);
        vh.AddVert(b, col, Vector4.zero);
        vh.AddVert(d, col, Vector4.zero);
        vh.AddVert(e, col, Vector4.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }
}
