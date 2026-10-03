using UnityEngine;

// Sixteen authored projectile silhouettes, one per ship slot. The movement
// style is also per slot, so two ships never merely recolour the same shot.
public static class UltimateProjectileArt
{
    const int Columns = 4;
    const int Rows = 4;
    static Sprite[] sprites;

    public static Sprite ForShip(int shipIndex)
    {
        Ensure();
        return sprites[Mathf.Clamp(shipIndex, 0, sprites.Length - 1)];
    }

    // Four art frames are interleaved through the 4x4 sheet. A projectile
    // therefore changes its actual sprite as it travels instead of only
    // scaling the same picture.
    public static Sprite FrameForShip(int shipIndex, int frame)
    {
        Ensure();
        int slot = (Mathf.Abs(shipIndex) + (Mathf.Abs(frame) % 4) * 4) % sprites.Length;
        return sprites[slot];
    }

    static void Ensure()
    {
        if (sprites != null) return;
        sprites = new Sprite[Columns * Rows];
        var atlas = Resources.Load<Texture2D>("Vfx/ultimate_projectiles_atlas");
        if (atlas == null) return;
        float w = atlas.width / (float)Columns, h = atlas.height / (float)Rows;
        for (int row = 0; row < Rows; row++)
        for (int col = 0; col < Columns; col++)
        {
            int i = row * Columns + col;
            sprites[i] = Sprite.Create(atlas,
                new Rect(col * w, (Rows - 1 - row) * h, w, h),
                new Vector2(.5f, .5f), 180f);
        }
    }
}
