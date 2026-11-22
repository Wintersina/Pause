using UnityEngine;

// The SPACE PARKING LOT: where the bays go, as pure arithmetic over the visible width -- testable without a scene.
// The art kit is authored in pixels (Art/UI/Dock/src~/lot); Px is one kit pixel in world units, so a bay is 96 x 128
// px.
//
// One unit, repeated: a VERTICAL RUNWAY with a bay column on each side. Ships are backed in with their noses pointing
// sideways into the runway; the runway has a dashed centre line. Narrow views (phones, a foldable's cover pane) get one
// runway; wider views get two side by side (never more). The unit is scaled to fit the width but never grown past
// MaxScale, so a big screen centres the lot with margins instead of blowing the bays up.
//
// Numbering runs runway by runway: a runway holds bays 1-8 (01/02 across the top, then 03/04 ...), the next one 9-16,
// so every runway reads exactly like the phone's single runway.
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
    public const int MaxLanes = 2;
    // Bays never grow past the size at which a parked hull is as big as the player's ship in actual play (the
    // reference hull x ShipScale.Main, 0.783 u longest edge, the same camera everywhere): a bigger screen gets
    // margins, not bigger bays.
    public const float MaxScale = ShipScale.Main * shopingShips.ReferenceHullSize / DockBay.HullSize;
    // Two runways are used once they fit at this scale or more; below it one bigger runway reads better.
    public const float TwoLaneMinScale = .75f;
    // Two runways shrink to fit the height down to this scale (so all 16 bays show); below it the lot scrolls.
    public const float FitHeightMinScale = .5f;
    public const float NumberW = 24f * Px, NumberH = 16f * Px;

    public int count;
    public int columns;    // bay columns (two per runway)
    public int rows;       // bay rows per runway
    public int lanes;      // vertical runways, 1 or 2
    public float scale;    // world units per unscaled unit, at most MaxScale

    // `usable` = the width the lot may take (world units); `space` = the height it may take (0 = unknown, never shrink
    // to fit it).
    public static DockLayout For(int count, float usable, float space = 0f)
    {
        var l = new DockLayout { count = Mathf.Max(1, count) };
        usable = Mathf.Max(1f, usable - SideMargin * 2f);
        l.lanes = count > 4 && usable >= WidthFor(2) * TwoLaneMinScale ? MaxLanes : 1;
        l.columns = l.lanes * 2;
        l.rows = Mathf.CeilToInt(l.count / (float)l.columns);
        l.scale = Mathf.Min(MaxScale, usable / l.Width);
        if (l.lanes == 2 && space > 0f && l.Height * l.scale > space)
            l.scale = Mathf.Min(l.scale, Mathf.Max(FitHeightMinScale, space / l.Height));
        return l;
    }

    public static float WidthFor(int lanes) { return lanes * GroupPx * Px + (lanes - 1) * GroupGap + 2f * Rail; }

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
    public float Width { get { return WidthFor(lanes); } }
    public float Height { get { return rows * BayW; } }

    // World size of one placed bay (turned on its side).
    public Vector2 BayExtent { get { return new Vector2(BayL, BayW); } }

    int PerLane { get { return rows * 2; } }
    // Runway by runway: slot 0..7 in the first runway (two per row), 8..15 in the second.
    public int LaneOf(int slot) { return slot / PerLane; }
    public int RowIndex(int slot) { return (slot % PerLane) / 2; }
    public int ColumnIndex(int slot) { return LaneOf(slot) * 2 + slot % 2; }

    // Left column: nose points right into the runway; right column: nose points left.
    public Facing FacingOf(int slot) { return slot % 2 == 0 ? Facing.Right : Facing.Left; }

    // Centre of a bay (0-based slot) in unscaled lot space, where the lot's own centre is the origin.
    public Vector2 BayCenter(int slot)
    {
        int lane = LaneOf(slot), side = slot % 2;
        float left = -Width * .5f + Rail + lane * (GroupPx * Px + GroupGap);
        float x = left + (side == 0 ? BayL * .5f : BayL + Lane + BayL * .5f);
        return new Vector2(x, Height * .5f - (RowIndex(slot) + .5f) * BayW);
    }

    // x of the centre of runway i.
    public float LaneCenter(int i)
    {
        return -Width * .5f + Rail + i * (GroupPx * Px + GroupGap) + BayL + Lane * .5f;
    }

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
