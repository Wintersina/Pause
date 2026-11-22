using UnityEngine;

// The SPACE PARKING LOT: where the bays go, as pure arithmetic over the visible width -- testable without a scene.
// The art kit is authored in pixels (Art/UI/Dock/src~/lot); Px is one kit pixel in world units, so a bay is 96 x 128
// px. Bays are never stretched: the lot picks how many bays / lanes fit the width and centres the result, any extra
// width going to the edges.
//
//   Rows  (phones, narrow panes)  horizontal drive lanes. A lane runs between every two rows: rows alternate backed in
//         nose-DOWN / nose-UP, so facing rows share a lane and no wall butts a wall. Bays per row follow the width.
//   Lanes (iPad, Mac window, Android tablets, desktop) vertical drive lanes. Every lane has a bay column on each side,
//         ships backed in with their noses pointing sideways into the lane; lane arrows point up. The number of lanes
//         follows the width.
//
// The mode is chosen from the view's shape, never the platform: portrait views narrower than LaneAspect (every phone,
// 9:16..9:21) get Rows; wider views get Lanes whenever one lane group fits, else Rows again (a foldable's cover pane).
public enum LotMode { Rows, Lanes }

// The way a parked ship's nose points (its bay is backed in on the opposite side).
public enum Facing { Up, Down, Right, Left }

public struct DockLayout
{
    // One kit pixel in world units. The mockups drew a bay 96 px wide at about 1.5 world units; the owner found that
    // too big, so the lot is drawn at 75% of that (bays stay finger-sized, more of them show at once).
    public const float BayScale = .75f;
    public const float Px = BayScale / 64f;
    public const float PixelsPerUnit = 1f / Px;

    // kit sizes, in px
    public const int BayWpx = 96, BayLpx = 128, LanePx = 48, RailPx = 16, GroupGapPx = 16;
    public const int GroupPx = BayLpx + LanePx + BayLpx;   // a lane group of the vertical layout: bay, lane, bay (304)

    public const float BayW = BayWpx * Px, BayL = BayLpx * Px, Lane = LanePx * Px, Rail = RailPx * Px, GroupGap = GroupGapPx * Px;

    // The old name: a Rows-mode bay (nose-up frame).
    public static readonly Vector2 BaySize = new Vector2(BayW, BayL);

    // The saved grid the hearts (row) and start speed (column) rules read is three columns wide whatever is drawn.
    public const int MinColumns = 3;
    public const int MaxColumns = 3;

    public const float SideMargin = .06f;
    public const float WideAspect = ScreenLayout.WideAspect;   // view width / height from here up: the card docks beside the lot
    public const float CardShare = .62f;
    public const float PaneGap = .3f;
    public const float Gutter = .3f;
    public const float LaneAspect = .6f;       // portrait views at least this wide / tall lay the lot out in vertical lanes
    public const int MaxRowColumns = 6, MaxLanes = 3, MinRowColumns = 2;
    public const float NumberW = 24f * Px, NumberH = 16f * Px;

    public LotMode mode;
    public int count;
    public int columns;    // Rows: bays per row. Lanes: bay columns (two per lane)
    public int rows;       // bay rows
    public int lanes;      // Rows: drive lanes (one under each upper row). Lanes: vertical lanes
    public float scale;    // 1, unless not even the narrowest lot fits the width

    public static bool PrefersLanes(float viewHalfWidth, float viewHalfHeight)
    {
        return viewHalfHeight > 0f && viewHalfWidth / viewHalfHeight >= LaneAspect;
    }

    // `usable` = the width the lot may take (world units). `lanesPreferred` from PrefersLanes.
    public static DockLayout For(int count, float usable, bool lanesPreferred)
    {
        var l = new DockLayout { count = Mathf.Max(1, count), scale = 1f };
        usable = Mathf.Max(1f, usable - SideMargin * 2f);
        if (lanesPreferred)
        {
            int fit = Mathf.FloorToInt((usable - 2f * Rail + GroupGap) / (GroupPx * Px + GroupGap));
            if (fit >= 1)
            {
                l.mode = LotMode.Lanes;
                l.lanes = Mathf.Min(fit, MaxLanes);
                l.columns = l.lanes * 2;
                l.rows = Mathf.CeilToInt(l.count / (float)l.columns);
                return l;
            }
        }
        l.mode = LotMode.Rows;
        int cols = Mathf.FloorToInt((usable - 2f * Rail) / BayW);
        l.columns = Mathf.Min(Mathf.Clamp(cols, MinRowColumns, MaxRowColumns), l.count);
        l.rows = Mathf.CeilToInt(l.count / (float)l.columns);
        l.lanes = Mathf.Max(0, l.rows - 1);   // a drive lane between every two rows (the mockups' rhythm): no wall butts a wall
        l.scale = Mathf.Min(1f, usable / l.Width);
        return l;
    }

    public static bool IsWide(float viewHalfWidth, float viewHalfHeight)
    {
        return ScreenLayout.IsWide(viewHalfWidth, viewHalfHeight);
    }

    // The two panes of a wide view: the card pane on the right, as wide as the card wants (cardWidth, world units) up
    // to CardShare of the view; the lot pane takes the rest.
    public static void Panes(Rect view, float cardWidth, out Rect rackPane, out Rect cardPane)
    {
        float cw = Mathf.Min(cardWidth + PaneGap, view.width * CardShare);
        cardPane = new Rect(view.xMax - cw, view.yMin, cw, view.height);
        rackPane = new Rect(view.xMin, view.yMin, view.width - cw, view.height);
    }

    // Unscaled lot size, rails included.
    public float Width
    {
        get
        {
            if (mode == LotMode.Lanes) return lanes * GroupPx * Px + (lanes - 1) * GroupGap + 2f * Rail;
            return columns * BayW + 2f * Rail;
        }
    }

    public float Height
    {
        get { return mode == LotMode.Lanes ? rows * BayW : rows * BayL + lanes * Lane; }   // (Rows: lanes = rows - 1)
    }

    // World size of one placed bay (Lanes: turned on its side).
    public Vector2 BayExtent { get { return mode == LotMode.Lanes ? new Vector2(BayL, BayW) : new Vector2(BayW, BayL); } }

    public int RowIndex(int slot) { return slot / columns; }
    public int ColumnIndex(int slot) { return slot % columns; }

    public Facing FacingOf(int slot)
    {
        if (mode == LotMode.Lanes) return ColumnIndex(slot) % 2 == 0 ? Facing.Right : Facing.Left;
        return RowIndex(slot) % 2 == 0 ? Facing.Down : Facing.Up;
    }

    // The lane group (Lanes) a slot sits in; for Rows, the pair of rows.
    public int LaneOf(int slot) { return mode == LotMode.Lanes ? ColumnIndex(slot) / 2 : RowIndex(slot) / 2; }

    // Centre of a bay (0-based slot, row-major from the top-left) in unscaled lot space, where the lot's own centre is
    // the origin.
    public Vector2 BayCenter(int slot)
    {
        int col = ColumnIndex(slot), row = RowIndex(slot);
        float h = Height;
        if (mode == LotMode.Lanes)
        {
            int lane = col / 2, side = col % 2;
            float left = -Width * .5f + Rail + lane * (GroupPx * Px + GroupGap);
            float x = left + (side == 0 ? BayL * .5f : BayL + Lane + BayL * .5f);
            return new Vector2(x, h * .5f - (row + .5f) * BayW);
        }
        // A short last row (the lone Paragon bay) is centred rather than left with a gap beside it.
        int inRow = Mathf.Min(columns, count - row * columns);
        float cx = (col - (Mathf.Max(1, inRow) - 1) * .5f) * BayW;
        float top = h * .5f - row * (BayL + Lane);
        return new Vector2(cx, top - BayL * .5f);
    }

    // Rows: y of the centre of drive lane i (under upper row 2i). Lanes: x of the centre of vertical lane i.
    public float LaneCenter(int i)
    {
        if (mode == LotMode.Lanes) return -Width * .5f + Rail + i * (GroupPx * Px + GroupGap) + BayL + Lane * .5f;
        return Height * .5f - (i + 1) * BayL - i * Lane - Lane * .5f;
    }

    // Width of the strip a lane tile covers (the bay span).
    public float SpanWidth { get { return Width - 2f * Rail; } }

    // Vertical scroll range for a lot whose scaled height may exceed the space between the header band and the footer
    // band. Returns the lot centre's y when scrolled to the top (min) and to the bottom (max).
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
