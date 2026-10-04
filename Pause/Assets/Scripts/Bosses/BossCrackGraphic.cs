using UnityEngine;
using UnityEngine.UI;

public class BossCrackGraphic : BossShapeGraphic
{
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Line(vh, AkiraPalette.Ink, BossNameShapes.CrackLeft, 15f);
        Line(vh, AkiraPalette.Ink, BossNameShapes.CrackRight, 15f);
        Line(vh, AkiraPalette.Ink, BossNameShapes.CrackMain, 19f);
        // White: the renderer's colour tints it to the boss's colour.
        Line(vh, Color.white, BossNameShapes.CrackLeft, 5f);
        Line(vh, Color.white, BossNameShapes.CrackRight, 5f);
        Line(vh, Color.white, BossNameShapes.CrackMain, 9f);
    }

    void Line(VertexHelper vh, Color c, Vector2[] pts, float w)
    {
        for (int i = 0; i + 1 < pts.Length; i++) Stroke(vh, c, pts[i], pts[i + 1], w);
    }
}
