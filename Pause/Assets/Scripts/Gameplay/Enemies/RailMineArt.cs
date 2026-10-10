using UnityEngine;

// The rail mines' neon pixel art: the original four-world atlas
// (Resources/Enemies/Mines/rail_mines_neon.png, byte-identical to the old
// Vfx/rail_bomb_themes_atlas.png the user approved), sliced at runtime the way
// the old RailBombSprites did, then played by RailBombAnimator.
//
//   rows    = worlds (Space, Frost, Verdant, Ember), top to bottom
//   columns = Dormant, Waking, Charging, Burst, left to right
//
// Later worlds get a SECOND atlas file of their own (the original stays
// byte-identical: RailMineArtTest pins its SHA-1): Tide's Limpet Mine is the
// one row of Resources/Enemies/Mines/rail_mines_neon_tide.png (1254x314,
// same four columns, same 384 PPU, same clamp geometry). AtlasPathFor(world)
// routes; PixelRect / PixelPivot are in the world's own atlas.
//
// The drawings don't sit on the even 313.5 px grid the old slicer cut (the
// rows drift up and the columns left, so Verdant's and Ember's top lugs were
// clipped and frames jumped about), and the charging/burst glow spills past
// it. So each frame has its own rect, cut along the empty gaps between rows
// and the faintest column between neighbours, and its own pivot: the dormant
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
    public const int OriginalWorlds = 4;   // the rows of the original atlas
    public const int Worlds = 5;           // Space, Frost, Verdant, Ember (original atlas), Tide (second atlas)
    public const int TideAtlasHeight = 314;
    public const int Dormant = 0, Waking = 1, Charging = 2, Burst = 3;
    public const int Columns = 4;

    // Where a mine sits on its rail. The frames' pivot is the body's centre;
    // the clamp's outer face is ClampReach further toward the wall (measured
    // on the atlas: 0.326-0.336 u in every row). A mounted mine's centre is
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

    // Atlas size and per-frame rects in image pixels, top-left origin
    // (as an image viewer shows them): x, y, width, height.
    public const int AtlasSize = 1254;
    static readonly RectInt[] Rects =
    {
        // Space
        new RectInt(0, 0, 310, 308), new RectInt(310, 0, 283, 308), new RectInt(593, 0, 327, 308), new RectInt(920, 0, 334, 308),
        // Frost
        new RectInt(0, 308, 328, 286), new RectInt(328, 308, 289, 286), new RectInt(617, 308, 305, 286), new RectInt(922, 308, 332, 286),
        // Verdant
        new RectInt(0, 594, 320, 298), new RectInt(320, 594, 293, 298), new RectInt(613, 594, 309, 298), new RectInt(922, 594, 332, 298),
        // Ember
        new RectInt(0, 892, 309, 362), new RectInt(309, 892, 294, 362), new RectInt(603, 892, 319, 362), new RectInt(922, 892, 332, 362),
        // Tide (the second atlas, one row: y from its own top). Cut along the empty gaps between the panels.
        new RectInt(0, 0, 330, 314), new RectInt(330, 0, 308, 314), new RectInt(638, 0, 309, 314), new RectInt(947, 0, 307, 314),
    };

    // Each frame's pivot in image pixels (top-left origin): where the rail is.
    static readonly Vector2[] Pivots =
    {
        new Vector2(174f, 160f), new Vector2(472f, 160f), new Vector2(767f, 164f), new Vector2(1067f, 166f),
        new Vector2(177f, 443f), new Vector2(477f, 445f), new Vector2(772f, 443f), new Vector2(1070f, 455f),
        new Vector2(177f, 738f), new Vector2(473f, 744f), new Vector2(766f, 744f), new Vector2(1071f, 747f),
        new Vector2(175f, 1042.5f), new Vector2(476f, 1049.5f), new Vector2(770f, 1046.5f), new Vector2(1074f, 1056.5f),
        // Tide: the clamp's solid left edge sits 128 px left of the pivot in every frame (the others 124-134)
        new Vector2(186f, 161f), new Vector2(490f, 161f), new Vector2(788f, 161f), new Vector2(1093f, 161f),
    };

    // The core: where the mine's laser leaves it (RailMineLaser.MuzzleOf).
    // Image px from a frame's pivot, x toward the lane (unflipped), y up:
    // the centre of the brightest pixels of the glowing core in the waking
    // and charging frames (shown through the windup and the burn), measured
    // on the atlas (RailMineLaserTest measures them again).
    static readonly Vector2[] CorePx =
    {
        new Vector2(34.4f, -1.4f),   // Space
        new Vector2(38f, -3f),       // Frost
        new Vector2(39.4f, 1f),      // Verdant
        new Vector2(38.1f, -2.2f),   // Ember
        new Vector2(27.5f, .7f),     // Tide (same method; the brightest pixels sit on the eye's glare)
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

    static Texture2D atlas, tideAtlas;
    static Sprite[] sprites;   // [world * Columns + column]

    // The atlas file a world's mine is drawn from.
    public static string AtlasPathFor(int world) => world >= OriginalWorlds ? TideAtlasPath : AtlasPath;
    public static Texture2D AtlasFor(int world)
    {
        if (world < OriginalWorlds) return Atlas;
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
        for (int i = 0; i < frames.Length; i++) frames[i] = Frame(world, FlipbookColumns[i]);
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
