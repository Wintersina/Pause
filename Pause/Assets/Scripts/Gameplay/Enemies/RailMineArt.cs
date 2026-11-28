using UnityEngine;

// The rail mines' neon pixel art: the four-world atlas
// (Resources/Enemies/Mines/rail_mines_neon.png), sliced at runtime the way
// the old RailBombSprites did, then played by RailBombAnimator.
//
// 2026-11: Space, Frost, Verdant, Ember, Tide and Storm are the Paragon-standard
// redraws: one user-approved dormant drawing per world (clamp and wheels on the
// rail side), scaled NEAREST to the old dormant frame's width, its four states
// built by script in the world's cue hue (Waking: core and lugs lit; Charging:
// brighter core, halo, arcs; Burst: a radial flare from the core). Nothing of
// the glow reaches past the clamp's outer face. Build script, approved
// drawings and the old atlases: Art/Enemies/src~/paragon_standard/mines/.
// Desert's Mirage Mine was redrawn the same way (desert_mine_codex.png, its
// codex still, is still the Codex one).
//
//   rows    = worlds (Space, Frost, Verdant, Ember), top to bottom
//   columns = Dormant, Waking, Charging, Burst, left to right
//
// Later worlds get a SECOND atlas file of their own (RailMineArtTest pins the
// four-world file's SHA-1): Tide's Limpet Mine is the
// one row of Resources/Enemies/Mines/rail_mines_neon_tide.png (1254x314,
// same four columns, same 384 PPU). AtlasPathFor(world)
// routes; PixelRect / PixelPivot are in the world's own atlas. Storm's Rod Mine
// is the third file, rail_mines_neon_storm.png (1254x314, one row, Tide's
// frame rects, its own pivots). Desert's Mirage Mine is the FOURTH file,
// rail_mines_neon_desert.png (1254x314, one row, Tide's cuts but a 305 px
// charging panel). Its lens centre stays DARK (the user's ask): waking lights
// the brass rim and a small sun-glint focal point the heat-lance leaves from.
//
// Each frame has its own rect (the rows keep the old bands; the columns are
// cut to each state's reach: dormant and waking tight, charging wider for the
// halo and arcs, the burst gets the rest) and its own pivot: the dormant
// body's centre carried over by the clamp's position, so the clamp stays put
// from frame to frame. A right-hand mine mirrors about that pivot
// (SpriteRenderer.flipX), so it sits on its rail the same way.
//
// PixelsPerUnit 384: the ~255 px drawing is ~0.66 u wide (the original mine
// was 0.65 u: 180 PPU at prefab scale 0.46), and on a 1080x1920 phone at
// orthographic size 5 (192 screen px per unit) every screen pixel covers
// exactly two atlas pixels. Point filtered, no mipmaps, uncompressed.
public static class RailMineArt
{
    public const string AtlasPath = "Enemies/Mines/rail_mines_neon";
    public const float PixelsPerUnit = 384f;
    public const string TideAtlasPath = "Enemies/Mines/rail_mines_neon_tide";
    // Verdant's LIT mine: the Verdant row again, repainted with the core and veins glowing, in its own file (built by the same
    // script). One row, 1254 x 298, the same four columns; its rects and pivots are the Verdant row's with the row's y (594)
    // taken off. The mine wears it once it ARMS: the flipbook's tell slots (Waking, Charging). Idle, hit and the burst frame stay original.
    public const string VerdantLitAtlasPath = "Enemies/Mines/rail_mines_neon_verdant_lit";
    public const int VerdantWorld = 2, VerdantLitAtlasHeight = 298, VerdantRowY = 594;
    public const int OriginalWorlds = 4;   // the rows of the original atlas
    public const string StormAtlasPath = "Enemies/Mines/rail_mines_neon_storm";
    public const string DesertAtlasPath = "Enemies/Mines/rail_mines_neon_desert";
    public const int Worlds = 7;           // Space, Frost, Verdant, Ember (original atlas), Tide (second atlas), Storm (third atlas), Desert (fourth atlas)
    public const int TideAtlasHeight = 314;
    public const int Dormant = 0, Waking = 1, Charging = 2, Burst = 3;
    public const int Columns = 4;

    // Where a mine sits on its rail. The frames' pivot is the body's centre;
    // the clamp's outer face is ClampReach further toward the wall (measured
    // on the redrawn atlas: 0.312 (Tide) - 0.336 u, 0.329 u on average). A mounted mine's centre is
    // put where that face sits ClampBite inside the DRAWN rail's inner edge
    // (WorldPainter.VisibleRailEdges), so the clamp visibly grips the rail
    // whatever the rail art or the camera is (enmiesOnBoard.WorldRailX).
    public const float ClampReach = .33f, ClampBite = .16f;
    // Without the reinforced rail art (headless tests, a plain wall): the
    // authored lane edge the mines always used.
    public const float FallbackRailX = 2.35f;

    // A mine's centre x (as a distance from the centre line) on a rail whose
    // drawn inner edge is at `railInnerEdge`.
    public static float MountX(float railInnerEdge) { return railInnerEdge + ClampBite - ClampReach; }

    // The Mirage Mine's clamp: the redrawn mine's sits 127.5 px (0.332 u) from the pivot, like the other rows' (the Codex drawing's
    // was a deeper 0.409 u), so it is ClampReach again; kept as its own constant for ClampReachFor.
    public const float DesertClampReach = ClampReach;
    public static float ClampReachFor(int world) { return world == DesertWorld ? DesertClampReach : ClampReach; }
    public static float MountX(float railInnerEdge, int world) { return railInnerEdge + ClampBite - ClampReachFor(world); }

    // Atlas size and per-frame rects in image pixels, top-left origin
    // (as an image viewer shows them): x, y, width, height.
    public const int AtlasSize = 1254;
    static readonly RectInt[] Rects =
    {
        // Space
        new RectInt(0, 0, 259, 308), new RectInt(259, 0, 259, 308), new RectInt(518, 0, 313, 308), new RectInt(831, 0, 423, 308),
        // Frost
        new RectInt(0, 308, 267, 286), new RectInt(267, 308, 270, 286), new RectInt(537, 308, 310, 286), new RectInt(847, 308, 407, 286),
        // Verdant
        new RectInt(0, 594, 267, 298), new RectInt(267, 594, 267, 298), new RectInt(534, 594, 320, 298), new RectInt(854, 594, 400, 298),
        // Ember
        new RectInt(0, 892, 268, 362), new RectInt(268, 892, 271, 362), new RectInt(539, 892, 311, 362), new RectInt(850, 892, 404, 362),
        // Tide (the second atlas, one row: y from its own top)
        new RectInt(0, 0, 265, 314), new RectInt(265, 0, 265, 314), new RectInt(530, 0, 304, 314), new RectInt(834, 0, 420, 314),
        // Storm (the third atlas, one row, the same cuts as Tide's)
        new RectInt(0, 0, 265, 314), new RectInt(265, 0, 265, 314), new RectInt(530, 0, 304, 314), new RectInt(834, 0, 420, 314),
        // Desert (the fourth atlas, one row)
        new RectInt(0, 0, 265, 314), new RectInt(265, 0, 265, 314), new RectInt(530, 0, 305, 314), new RectInt(835, 0, 419, 314),
    };

    // Each frame's pivot in image pixels (top-left origin): where the rail is. Space .. Storm: the dormant drawing's centre (its
    // alpha > 50% bounds), carried into every frame by the clamp, which sits 6 px into every rect (the frames are built from the one
    // drawing, so the clamp is pixel-identical in all four).
    static readonly Vector2[] Pivots =
    {
        new Vector2(130.5f, 154f), new Vector2(389.5f, 154f), new Vector2(648.5f, 154f), new Vector2(961.5f, 154f),
        new Vector2(134.5f, 450.5f), new Vector2(401.5f, 450.5f), new Vector2(671.5f, 450.5f), new Vector2(981.5f, 450.5f),
        new Vector2(134.5f, 742.5f), new Vector2(401.5f, 742.5f), new Vector2(668.5f, 742.5f), new Vector2(988.5f, 742.5f),
        new Vector2(135f, 1072.5f), new Vector2(403f, 1072.5f), new Vector2(674f, 1072.5f), new Vector2(985f, 1072.5f),
        // Tide: the clamp's solid left edge sits 120 px left of the pivot (a 240 px drawing)
        new Vector2(126f, 156.5f), new Vector2(391f, 156.5f), new Vector2(656f, 156.5f), new Vector2(960f, 156.5f),
        // Storm: 127.5 px
        new Vector2(133.5f, 157f), new Vector2(398.5f, 157f), new Vector2(663.5f, 157f), new Vector2(967.5f, 157f),
        // Desert: 127.5 px
        new Vector2(133.5f, 157.5f), new Vector2(398.5f, 157.5f), new Vector2(663.5f, 157.5f), new Vector2(968.5f, 157.5f),
    };

    // The core: where the mine's laser leaves it (RailMineLaser.MuzzleOf).
    // Image px from a frame's pivot, x toward the lane (unflipped), y up:
    // the centre of the brightest pixels of the glowing core in the waking
    // and charging frames (shown through the windup and the burn), measured
    // on the atlas (RailMineLaserTest measures them again). Space .. Storm:
    // the redraws paint one white-hot point at the core's centre and cap
    // every other pixel below it, so the 25 brightest are that point.
    static readonly Vector2[] CorePx =
    {
        new Vector2(51.7f, 1.5f),    // Space: the eye
        new Vector2(43.5f, -.5f),    // Frost: the snowflake
        new Vector2(26.3f, -6.6f),   // Verdant: where the vines cross
        new Vector2(46.3f, 2.9f),    // Ember: the lens
        new Vector2(42f, 7.5f),      // Tide: the eye
        new Vector2(23.9f, -1.1f),   // Storm: the coil (the white-hot point at its centre, the same top-25 rule as the others)
        new Vector2(32.2f, 1.9f),    // Desert: the sun-glint focal point at the dark lens's centre
    };

    // The core's offset from the mine's centre in world units (unscaled,
    // for a left-hand mine: +x toward the lane).
    public static Vector2 CoreOffset(int world) => CorePx[Mathf.Clamp(world, 0, Worlds - 1)] / PixelsPerUnit;

    // The flipbook's seven slots (EnemyFlipbook: 0-3 idle, 4-5 tell, 6 hit)
    // as atlas columns. Idle is the dormant mine with one waking blink of the
    // core per loop (EnemyRoster.IdleTicks: 6, 4, 6, 6); the arming tell
    // loops waking -> charging while the ship is close; the hit slot (and the
    // detonation, RailBombAnimator.Burst) is the burst.
    public static readonly int[] FlipbookColumns = { Dormant, Waking, Dormant, Dormant, Waking, Charging, Burst };

    static Texture2D atlas, tideAtlas, stormAtlas, desertAtlas, litAtlas;
    static Sprite[] sprites;   // [world * Columns + column]
    static readonly Sprite[] litSprites = new Sprite[Columns];

    public static Texture2D LitAtlas
    {
        get
        {
            if (litAtlas == null) litAtlas = Resources.Load<Texture2D>(VerdantLitAtlasPath);
            return litAtlas;
        }
    }

    // True for the worlds that have a lit second drawing of their mine (Verdant).
    public static bool HasLit(int world) => world == VerdantWorld && LitAtlas != null;

    // Top-left-origin rect / pivot of a lit frame in the lit atlas: the Verdant row's, y offset removed.
    public static RectInt LitPixelRect(int column) { var r = PixelRect(VerdantWorld, column); return new RectInt(r.x, r.y - VerdantRowY, r.width, r.height); }
    public static Vector2 LitPixelPivot(int column) { var p = PixelPivot(VerdantWorld, column); return new Vector2(p.x, p.y - VerdantRowY); }

    // The lit sprite of a column, or the ordinary frame when the world has none (or its file is missing).
    public static Sprite LitFrame(int world, int column)
    {
        if (!HasLit(world)) return Frame(world, column);
        column = Mathf.Clamp(column, 0, Columns - 1);
        if (litSprites[column] == null)
        {
            var tex = LitAtlas;
            var r = LitPixelRect(column);
            var p = LitPixelPivot(column);
            var rect = new Rect(r.x, tex.height - r.y - r.height, r.width, r.height);
            var pivot = new Vector2((p.x - r.x) / r.width, 1f - (p.y - r.y) / r.height);
            litSprites[column] = Sprite.Create(tex, rect, pivot, PixelsPerUnit, 0, SpriteMeshType.FullRect);
            litSprites[column].name = "verdant_mine_" + ColumnName(column) + "_lit";
        }
        return litSprites[column];
    }

    // The atlas file a world's mine is drawn from.
    public static string AtlasPathFor(int world) =>
        world < OriginalWorlds ? AtlasPath : world == DesertWorld ? DesertAtlasPath : world == StormWorld ? StormAtlasPath : TideAtlasPath;
    public const int StormWorld = 5, DesertWorld = 6;

    public static Texture2D AtlasFor(int world)
    {
        if (world < OriginalWorlds) return Atlas;
        if (world == DesertWorld)
        {
            if (desertAtlas == null) desertAtlas = Resources.Load<Texture2D>(DesertAtlasPath);
            return desertAtlas;
        }
        if (world == StormWorld)
        {
            if (stormAtlas == null) stormAtlas = Resources.Load<Texture2D>(StormAtlasPath);
            return stormAtlas;
        }
        if (tideAtlas == null) tideAtlas = Resources.Load<Texture2D>(TideAtlasPath);
        return tideAtlas;
    }

    public static Texture2D Atlas
    {
        get
        {
            if (atlas == null) atlas = Resources.Load<Texture2D>(AtlasPath);
            return atlas;
        }
    }

    // Top-left-origin pixel rect of one frame (tests sample it from the PNG).
    public static RectInt PixelRect(int world, int column) => Rects[Index(world, column)];
    public static Vector2 PixelPivot(int world, int column) => Pivots[Index(world, column)];

    static int Index(int world, int column)
    {
        return Mathf.Clamp(world, 0, Worlds - 1) * Columns + Mathf.Clamp(column, 0, Columns - 1);
    }

    public static Sprite Frame(int world, int column)
    {
        int i = Index(world, column);
        // Sprite.Create()d sprites die with an editor scene swap; Unity's
        // null check catches that and the atlas is sliced again.
        if (sprites == null || sprites[i] == null)
        {
            var tex = AtlasFor(world);
            if (tex == null) return null;
            if (sprites == null) sprites = new Sprite[Worlds * Columns];
            var r = Rects[i];
            var p = Pivots[i];
            // Unity rects start bottom-left.
            var rect = new Rect(r.x, tex.height - r.y - r.height, r.width, r.height);
            var pivot = new Vector2((p.x - r.x) / r.width, 1f - (p.y - r.y) / r.height);
            sprites[i] = Sprite.Create(tex, rect, pivot, PixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprites[i].name = EnemyRoster.WorldKeys[i / Columns] + "_mine_" + ColumnName(i % Columns);
        }
        return sprites[i];
    }

    // The seven flipbook frames for one world's mine.
    public static Sprite[] Flipbook(int world)
    {
        if (AtlasFor(world) == null) return null;
        var frames = new Sprite[FlipbookColumns.Length];
        for (int i = 0; i < frames.Length; i++)
        {
            int c = FlipbookColumns[i];
            // the arming tell (slots 4-5: waking, charging) is the lit drawing where the world has one
            bool arming = i == 4 || i == 5;
            frames[i] = arming && HasLit(world) ? LitFrame(world, c) : Frame(world, c);
        }
        return frames;
    }

    public static string ColumnName(int column)
    {
        switch (column)
        {
            case Dormant: return "dormant";
            case Waking: return "waking";
            case Charging: return "charging";
            default: return "burst";
        }
    }
}
