using UnityEngine;
using UnityEngine.UI;

// The game's cut-corner rectangle (45 degree chamfer at the TOP-LEFT and
// BOTTOM-RIGHT corners only), as a procedural 9-sliced sprite: the shape of
// the death panel's dp_button plates, for UI that has no authored plate.
// Built once and cached (no per-frame allocation); point filtered, so the
// edge is crisp at every phone size. Proportions follow dp_button (192x160 px
// at 200 ppu, border 48, cut 30): cut = 0.625 of the border.
// Easy to unify with DeathPanelShapes later: the numbers are the same.
public static class CutCornerShape
{
    public const int Border = 32, Cut = 20, Size = 2 * Border + 2;   // px (200 ppu: 1 px = 0.5 canvas unit)
    public const float PixelsPerUnit = 200f;

    static Sprite sprite;

    public static Sprite Sprite
    {
        get
        {
            if (sprite == null) sprite = Build();
            return sprite;
        }
    }

    // true where the shape is filled; (0,0) is the bottom-left texel
    public static bool Filled(int x, int y)
    {
        // top-left corner: cut where (distance from left) + (distance from top) < Cut
        if (x + (Size - 1 - y) < Cut) return false;
        // bottom-right corner
        if ((Size - 1 - x) + y < Cut) return false;
        return true;
    }

    static Sprite Build()
    {
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            name = "CutCornerShape",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        var px = new Color32[Size * Size];
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                px[y * Size + x] = Filled(x, y) ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
        tex.SetPixels32(px);
        tex.Apply(false, false);   // stays readable (tests read the corners)
        var s = Sprite_Create(tex);
        s.name = "CutCornerShape";
        s.hideFlags = HideFlags.HideAndDontSave;
        return s;
    }

    static Sprite Sprite_Create(Texture2D tex)
    {
        return UnityEngine.Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(.5f, .5f), PixelsPerUnit, 0,
                                         SpriteMeshType.FullRect, new Vector4(Border, Border, Border, Border));
    }

    // Swap an Image's plate to the cut-corner shape (colour, raycast and
    // layout untouched).
    public static void Apply(Image image)
    {
        if (image == null) return;
        image.sprite = Sprite;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 1f;
    }
}
