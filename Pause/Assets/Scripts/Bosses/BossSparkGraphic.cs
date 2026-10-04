using UnityEngine;
using UnityEngine.UI;

// One chunky 4-point spark, 64 px, white in an ink contour (tinted per boss).
public class BossSparkGraphic : BossShapeGraphic
{
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        origin = new Vector2(32f, 32f);
        Star(vh, AkiraPalette.Ink, 1.25f);
        Star(vh, Color.white, 1f);
    }

    void Star(VertexHelper vh, Color c, float k)
    {
        // Star-shaped around its centre, so it fans as four kites.
        for (int q = 0; q < 4; q++)
        {
            float a = q * Mathf.PI * .5f;
            Vector2 tip = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (30f * k);
            Vector2 l = new Vector2(Mathf.Cos(a + Mathf.PI * .25f), Mathf.Sin(a + Mathf.PI * .25f)) * (11f * k);
            Vector2 r = new Vector2(Mathf.Cos(a - Mathf.PI * .25f), Mathf.Sin(a - Mathf.PI * .25f)) * (11f * k);
            Vector2 o = origin;
            Poly(vh, c, 0f, 0f, o, o + r, o + tip, o + l);
        }
    }
}
