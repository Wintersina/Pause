using System;
using UnityEngine;

// How far the board has rolled, as ONE number that the rail art and the rail
// mines both read, so they roll together and roll cleanly.
//
// WHY. The rails are point-filtered pixel art shown smaller than they were
// drawn: with the 3.72 u half-width camera one screen pixel covers about 2.0
// (Space, Ember) to 2.6 (Frost, Verdant) texels. Scrolled by an arbitrary
// fraction of a pixel each frame, point sampling picks a different texel of
// those two or three every frame: thin lines and lamp edges crawl and
// sparkle. The same is true of a mine's sprite. And the rail art and a mine
// used to integrate the scroll separately (a material offset here, a
// transform there), so nothing guaranteed they stayed registered.
//
// WHAT.
//   * Distance      the board's scroll since the scene loaded, in world
//                   units, a double, advanced once a frame (frame guard) by
//                   whoever runs first: the walls (moveBackGround) or a rail
//                   lane (RailLaneScroller). speed x BoardScroll x dt.
//   * FrameStep     this frame's share of it: what a rail lane moves by, so
//                   a mine and the rail art move by the SAME number.
//   * Snapped       Distance rounded to a whole screen pixel (PixelSnap).
//                   The rail art is drawn at Snapped, and a mine is lifted by
//                   SnapLift (= Distance - Snapped, under half a pixel) so it
//                   is drawn at Snapped too: both move in whole pixels, the
//                   same pixels, every frame. No crawl, no relative jitter.
//   * RailOffset    the wall material's v offset for that, as a fraction of
//                   a tile: computed from the total, never accumulated, so
//                   both walls are in phase by construction and it stays in
//                   [0, 1) however long the run.
//
// TUNABLES. RailRate (1 = the rail art rolls at the board's rate, the rate
// the mines ride; lower = a slower, parallax rail the mines then slide
// along), PixelSnap, and LegacyTileRate (the pre-vetting behaviour: one
// texture tile per unit of speed, whatever a tile measures).
public static class BoardRoll
{
    // World units a second per unit of moveBackGround.speed: the board's
    // scroll (SpawnSpace.ScrollSpeed).
    public const float BoardScroll = 30f;

    // ---- tunables ----
    public static float RailRate = 1f;
    public static bool PixelSnap = true;
    public static bool LegacyTileRate;

    // Test hooks (edit mode has no frame count or camera pixels).
    public static Func<int> FrameOverride;
    public static float PixelsPerUnitOverride;

    static double distance, legacyTiles;
    static float frameStep;
    static int lastFrame = int.MinValue;
    static Camera cam;

    public static double Distance => distance;
    public static float FrameStep => frameStep;

    static int Frame => FrameOverride != null ? FrameOverride() : Time.frameCount;

    // Screen pixels per world unit in the gameplay camera.
    public static float PixelsPerUnit
    {
        get
        {
            if (PixelsPerUnitOverride > 0f) return PixelsPerUnitOverride;
            if (cam == null) cam = Camera.main;
            if (cam == null || !cam.orthographic || cam.orthographicSize <= 0f) return 0f;
            return cam.pixelHeight / (2f * cam.orthographicSize);
        }
    }

    // One running frame of board scroll. The first caller in a frame
    // advances it; later callers that frame get the same FrameStep. A frame
    // nobody calls it in (paused, frozen, dead) rolls nothing: FrameStep 0.
    public static float Advance(float speed, float dt)
    {
        int frame = Frame;
        if (frame == lastFrame) return frameStep;
        lastFrame = frame;
        frameStep = dt > 0f && speed > 0f ? speed * BoardScroll * dt : 0f;
        distance += frameStep;
        if (dt > 0f && speed > 0f) legacyTiles = Frac(legacyTiles + speed * dt);
        return frameStep;
    }

    // What a caller that did not advance this frame should move by (0 when
    // the board did not roll this frame).
    public static float StepThisFrame => Frame == lastFrame ? frameStep : 0f;

    public static double Snapped
    {
        get
        {
            float ppu = PixelSnap ? PixelsPerUnit : 0f;
            return ppu > 0f ? Math.Round(distance * ppu) / ppu : distance;
        }
    }

    // How far up a board-riding sprite is drawn from where it truly is, so
    // it lands on the same whole pixel the rail art does (under half a pixel).
    public static float SnapLift => (float)(distance - Snapped);

    // The wall's texture v offset (a fraction of a tile) for art that
    // repeats `tiles` times over `worldHeight` units.
    public static float RailOffset(float tiles, float worldHeight)
    {
        if (LegacyTileRate || worldHeight <= 0f || tiles <= 0f) return (float)legacyTiles;
        return (float)Frac(Snapped * RailRate * tiles / worldHeight);
    }

    static double Frac(double v) { return v - Math.Floor(v); }

    public static void Reset()
    {
        distance = 0d;
        legacyTiles = 0d;
        frameStep = 0f;
        lastFrame = int.MinValue;
    }

    // Tests and the evidence renders: put the board at a distance (and the
    // legacy tile-rate scroll where `speed` would have taken it over the
    // same time: distance / BoardScroll tiles).
    public static void SetDistance(double d)
    {
        frameStep = (float)(d - distance);
        distance = d;
        legacyTiles = Frac(d / BoardScroll);
    }
}
