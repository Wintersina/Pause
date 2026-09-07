using UnityEngine;

// Art-only helper for themed rail bombs. Spawn/mount ownership remains with
// RailMineMount; it can request the current world's four-frame sequence here.
public static class RailBombSprites
{
    const int Columns = 4;
    const int Rows = 4;
    static Sprite[] frames;

    public static Sprite FrameForWorld(int worldIndex, int animationFrame)
    {
        Ensure();
        int row = Mathf.Clamp(worldIndex, 0, Rows - 1);
        int column = Mathf.Abs(animationFrame) % Columns;
        return frames[row * Columns + column];
    }

    static void Ensure()
    {
        if (frames != null) return;
        var atlas = Resources.Load<Texture2D>("Vfx/rail_bomb_themes_atlas");
        frames = new Sprite[Columns * Rows];
        if (atlas == null) return;

        float width = atlas.width / (float)Columns;
        float height = atlas.height / (float)Rows;
        for (int row = 0; row < Rows; row++)
        for (int column = 0; column < Columns; column++)
        {
            int i = row * Columns + column;
            frames[i] = Sprite.Create(atlas,
                new Rect(column * width, (Rows - 1 - row) * height, width, height),
                new Vector2(0.5f, 0.5f), 180f);
        }
    }
}
