using UnityEngine;
using UnityEngine.UI;

public class BossPlateGraphic : BossShapeGraphic
{
    public Color light = AkiraPalette.Magenta;
    // 0 the whole plate; -1 / +1 only the part left / right of the crack.
    public float side;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        float s = side, cut = BossNameShapes.CrackX;
        Color shade = new Color(light.r * .55f, light.g * .55f, light.b * .55f, 1f);
        Color ink = AkiraPalette.Ink;
        V(vh, ink, s, cut, 57, 33, 1014, 33, 982, 270, 25, 270);                 // drop shadow
        V(vh, ink, s, cut, 47, 23, 1010, 23, 976, 265, 14, 265);                 // contour
        V(vh, AkiraPalette.Indigo0, s, cut, 52, 28, 1004, 28, 972, 260, 20, 260); // slab
        V(vh, AkiraPalette.Night1, s, cut, 146, 238, 975, 238, 972, 260, 118, 260);
        V(vh, light, s, cut, 52, 28, 150, 28, 118, 260, 20, 260);                // accent bar
        V(vh, shade, s, cut, 96, 150, 140, 150, 118, 260, 82, 260);
        V(vh, light, s, cut, 158, 40, 996, 40, 995, 46, 157, 46);                // highlight kick
        V(vh, AkiraPalette.Bone, s, cut, 60, 36, 142, 36, 141, 42, 59, 42);
        V(vh, ink, s, cut, 147, 28, 153, 28, 121, 260, 115, 260);                // bar divider
        V(vh, ink, s, cut, 62, 120, 100, 92, 128, 120, 100, 148);                // diamond
        V(vh, AkiraPalette.Bone, s, cut, 74, 120, 100, 102, 116, 120, 100, 138);
    }

    void V(VertexHelper vh, Color c, float side, float cut, params float[] xy)
    {
        var p = new Vector2[xy.Length / 2];
        for (int i = 0; i < p.Length; i++) p[i] = new Vector2(xy[2 * i], xy[2 * i + 1]);
        Poly(vh, c, side, cut, p);
    }
}
