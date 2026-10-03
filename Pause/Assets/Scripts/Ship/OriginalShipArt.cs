using UnityEngine;

// The original hulls (8-15: Lightning .. Turtle). They used to be 128x32
// pixel strips and 32x32 idle tiles cropped by hard-coded rects; they are
// Akira flipbook sheets like every other hull now, so this only forwards to
// ShipHullArt and is kept for callers that ask for the originals by name.
public static class OriginalShipArt
{
    public static Sprite SpriteFor(int index)
    {
        return index >= 8 ? ShipHullArt.Rest(index) : null;
    }

    public static Sprite OriginalIdleSpriteFor(int index, int idleFrame)
    {
        return index >= 8 ? ShipHullArt.Idle(index, 0, idleFrame) : null;
    }
}
