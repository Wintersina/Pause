using UnityEngine;

// Extra wings fold into distinct inner columns at the board edges rather than stacking off screen.
public static class DroneFormation
{
    public static Vector3 KeepVisible(Transform hull, Vector3 desired, Bounds drawn, out bool folded)
    {
        folded = false;
        var view = ShipTargets.View();
        if (view.width > 1000f) return desired;
        var renderer = hull.GetComponent<SpriteRenderer>();
        Vector3 hullHalf = renderer != null && renderer.sprite != null ? renderer.bounds.extents : Vector3.Scale(new Vector3(.4f, .5f, 0f), hull.lossyScale);
        Vector3 relative = desired - hull.position, offset = drawn.center - desired;
        if (drawn.min.x < view.xMin + .04f || drawn.max.x > view.xMax - .04f)
        {
            int column = Mathf.Max(0, Mathf.RoundToInt(Mathf.Abs(relative.x) / Mathf.Max(.05f, hullHalf.x) * 2f) / 2 - 1);
            float depth = hullHalf.x * 3.2f + (column + 1) * (drawn.size.x + .12f);
            desired.x = hull.position.x - Mathf.Sign(relative.x) * depth;
            folded = true;
        }
        if (drawn.min.y < view.yMin + .06f || drawn.max.y > view.yMax - .06f)
        {
            desired.y = hull.position.y - Mathf.Sign(relative.y) * (hullHalf.y * 2f + Mathf.Abs(relative.y) + drawn.size.y + .1f);
            folded = true;
        }
        desired.x = Mathf.Clamp(desired.x, view.xMin + drawn.extents.x + .04f - offset.x, view.xMax - drawn.extents.x - .04f - offset.x);
        desired.y = Mathf.Clamp(desired.y, view.yMin + drawn.extents.y + .06f - offset.y, view.yMax - drawn.extents.y - .06f - offset.y);
        return desired;
    }
}
