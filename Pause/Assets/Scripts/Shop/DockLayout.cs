using UnityEngine;

// Where the berths go, as pure arithmetic over the visible width -- testable
// without a scene. Berths are packed edge to edge, separated only by a thin
// support spine (columns) and a gantry (rows).
//
// Portrait phones get three columns (CameraFit guarantees at least 2.85
// world units of half-width), so the full 15-ship roster fits in five rows.
// Wider screens keep three columns, centred over the starfield. If the rack
// is a hair too wide for the screen it is scaled down to fit rather than
// dropping a column.
//
// Near-square screens (the Z Flip7 cover screen, a foldable's inner screen:
// view width / height from WideAspect up) split the view into two panes: the
// rack, scaled to fit its pane's width AND height so the whole fleet shows
// without scrolling, on the left, and the selected ship's card docked in the
// pane on the right (Panes, ForWide) instead of floating over the rack.
public struct DockLayout
{
    public static readonly Vector2 BaySize = new Vector2(1.62f, 1.36f);
    public const float Spine = .12f;
    public const float Gantry = .10f;
    public const float Wall = .22f;
    public const float Gate = .20f;
    public const float Foot = .08f;
    public const float SideMargin = .06f;
    public const int MinColumns = 3;
    public const int MaxColumns = 3;

    public const float WideAspect = .8f;       // view width / height from here up
    public const float CardShare = .62f;       // the card pane never takes more than this share of the width
    public const float PaneGap = .3f;          // between the panes
    public const float Gutter = .3f;           // clear of the view's left and right edges (both panes)
    public const float LabelFloor = 1.2f;      // berth labels grow when the rack is drawn below this scale
    public const float MinWideScale = .75f;    // the rack scrolls rather than shrink below this

    public int count;
    public int columns;
    public int rows;
    public float scale;    // applied to the whole rack

    public float PitchX { get { return BaySize.x + Spine; } }
    public float PitchY { get { return BaySize.y + Gantry; } }

    // Unscaled rack size, walls and gate included.
    public float Width { get { return columns * BaySize.x + (columns - 1) * Spine + 2f * Wall; } }
    public float Height { get { return Gate + rows * BaySize.y + (rows - 1) * Gantry + Foot; } }

    public static DockLayout For(int count, float viewHalfWidth)
    {
        var layout = new DockLayout { count = Mathf.Max(1, count) };
        float usable = Mathf.Max(1f, viewHalfWidth * 2f - SideMargin * 2f);
        int fit = Mathf.FloorToInt((usable - 2f * Wall + Spine) / (BaySize.x + Spine));
        layout.columns = Mathf.Clamp(fit, MinColumns, MaxColumns);
        layout.columns = Mathf.Min(layout.columns, layout.count);
        layout.rows = Mathf.CeilToInt(layout.count / (float)layout.columns);
        layout.scale = Mathf.Min(1f, usable / layout.Width);
        return layout;
    }

    public static bool IsWide(float viewHalfWidth, float viewHalfHeight)
    {
        return viewHalfHeight > 0f && viewHalfWidth / viewHalfHeight >= WideAspect;
    }

    // The two panes of a wide view: the card pane on the right, as wide as the card wants (cardWidth, world
    // units) up to CardShare of the view; the rack pane takes the rest.
    public static void Panes(Rect view, float cardWidth, out Rect rackPane, out Rect cardPane)
    {
        float cw = Mathf.Min(cardWidth + PaneGap, view.width * CardShare);
        cardPane = new Rect(view.xMax - cw, view.yMin, cw, view.height);
        rackPane = new Rect(view.xMin, view.yMin, view.width - cw, view.height);
    }

    // The rack for a wide view: three columns scaled to fit the rack pane's width and height (down to
    // MinWideScale, below which it scrolls).
    public static DockLayout ForWide(int count, Rect rackPane)
    {
        var layout = For(count, rackPane.width * .5f + SideMargin);
        float byHeight = rackPane.height / layout.Height;
        layout.scale = Mathf.Min(layout.scale, Mathf.Max(MinWideScale, byHeight));
        return layout;
    }

    // Centre of a berth (0-based slot, row-major from the top-left) in
    // unscaled rack space, where the rack's own centre is the origin.
    public Vector2 BayCenter(int slot)
    {
        int col = slot % columns, row = slot / columns;
        float x = (col - (columns - 1) * .5f) * PitchX;
        float top = Height * .5f - Gate;
        float y = top - BaySize.y * .5f - row * PitchY;
        return new Vector2(x, y);
    }

    // Vertical scroll range for a rack whose scaled height may exceed the
    // space between the header and the footer. Returns the rack centre's y
    // when scrolled to the top (min) and to the bottom (max).
    public void ScrollRange(float viewTop, float viewBottom, out float atTop, out float atBottom)
    {
        float h = Height * scale;
        float space = viewTop - viewBottom;
        if (h <= space)
        {
            atTop = atBottom = (viewTop + viewBottom) * .5f;
            return;
        }
        atTop = viewTop - h * .5f;
        atBottom = viewBottom + h * .5f;
    }
}
