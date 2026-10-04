using UnityEngine;
using UnityEngine.UI;

// Leans the name's letters forward 8 degrees, like the rest of the game's
// display type (applied after the outline, so the contour leans with it).
public class BossSkewEffect : BaseMeshEffect
{
    public float slant = .1405f; // tan(8 deg)

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive()) return;
        var v = new UIVertex();
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref v, i);
            v.position.x += v.position.y * slant;
            vh.SetUIVertex(v, i);
        }
    }
}
