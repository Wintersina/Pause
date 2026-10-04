using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Vertical two-colour fill for PausedLabel's lettering: the PAUSE logo's red
// is brighter along the top of each stroke and deeper at the foot. Applied
// before the Outline/Shadow effects so only the fill is tinted.
public class PausedLabelGradient : BaseMeshEffect
{
    public Color top = Color.white;
    public Color bottom = Color.white;

    public void Set(Color topColor, Color bottomColor)
    {
        top = topColor;
        bottom = bottomColor;
        if (graphic != null) graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount == 0) return;
        var verts = new List<UIVertex>();
        vh.GetUIVertexStream(verts);
        float min = float.MaxValue, max = float.MinValue;
        foreach (var v in verts)
        {
            min = Mathf.Min(min, v.position.y);
            max = Mathf.Max(max, v.position.y);
        }
        float span = Mathf.Max(max - min, 1e-4f);
        for (int i = 0; i < verts.Count; i++)
        {
            var v = verts[i];
            Color c = Color.Lerp(bottom, top, (v.position.y - min) / span);
            c.a *= v.color.a / 255f;
            v.color = c;
            verts[i] = v;
        }
        vh.Clear();
        vh.AddUIVertexTriangleStream(verts);
    }
}
