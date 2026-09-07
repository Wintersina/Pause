using UnityEngine;

// Shared frames for the world gateway and the ship's blink effect.  Keeping
// them in Resources means both effects use the same authored pixel art rather
// than drawing a generic circle at runtime.
public static class TeleportPortalSprites
{
    const int Columns = 4;
    const int Rows = 4;
    static Sprite[] frames;

    public static int FrameCount { get { return Columns * Rows; } }

    public static Sprite FrameAt(int index)
    {
        EnsureFrames();
        return frames[Mathf.Abs(index) % frames.Length];
    }

    static void EnsureFrames()
    {
        if (frames != null) return;

        var atlas = Resources.Load<Texture2D>("Vfx/teleport_portal_atlas");
        frames = new Sprite[FrameCount];

        // A procedural fallback keeps portals visible if an old build is
        // launched without the atlas, while current builds use every authored
        // animation frame below.
        if (atlas == null)
        {
            for (int i = 0; i < frames.Length; i++) frames[i] = PortalArt.Ring();
            return;
        }

        float width = atlas.width / (float)Columns;
        float height = atlas.height / (float)Rows;
        for (int row = 0; row < Rows; row++)
        for (int column = 0; column < Columns; column++)
        {
            // Sprite rect coordinates begin at the lower-left, while the
            // illustrated sheet is laid out from the upper-left.
            int index = row * Columns + column;
            frames[index] = Sprite.Create(atlas,
                new Rect(column * width, (Rows - 1 - row) * height, width, height),
                new Vector2(0.5f, 0.5f), 180f);
        }
    }
}
