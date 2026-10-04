using UnityEngine;
using UnityEngine.UI;

// The boss intro's name plate, crack and dust sparks, drawn in code as flat
// cel polygons (AkiraPalette: one base, one shadow tone, one highlight kick,
// a thick INK contour) -- no texture, so the plate carries no type of its
// own; the boss's name is set on it as runtime text by BossIntroUI.
//
// Plate geometry is authored in plate pixels (1024 x 288, y down), the same
// layout as the old baked name card, and mapped to a centred rect.
public static class BossNameShapes
{
    public const float PlateW = 1024f, PlateH = 288f;
    // Where the plate splits when it cracks (plate px, from the left).
    public const float CrackX = 560f;
    // The free run of the slab right of the accent bar, where the name sits.
    public const float NameLeft = 172f, NameRight = 968f, NameMidY = 144f;

    public static Vector2 Local(float px, float py) => new Vector2(px - PlateW * .5f, PlateH * .5f - py);

    // The crack: a jagged split down the plate with two short forks.
    public static readonly Vector2[] CrackMain =
    {
        new Vector2(560, 14), new Vector2(538, 48), new Vector2(560, 80), new Vector2(534, 112),
        new Vector2(548, 146), new Vector2(522, 178), new Vector2(540, 212), new Vector2(516, 246), new Vector2(528, 276),
    };
    public static readonly Vector2[] CrackLeft = { new Vector2(560, 80), new Vector2(502, 98), new Vector2(458, 86) };
    public static readonly Vector2[] CrackRight = { new Vector2(548, 146), new Vector2(602, 130), new Vector2(646, 150) };
}
