using UnityEngine;

// The boss attacks' small FX sheet (Art/BossAttacks/src~/build_boss_attack_fx.py
// -> Resources/BossAttackFx/boss_attack_fx.png): 6 x 4 cells, one row per boss
// in BossCatalog order (Space, Frost, Verdant, Ember), row 0 on top.
//
//   col 0..2  rail spark (ricochet / splash on a side rail)
//   col 3..4  muzzle flash (a part firing; the root of a live beam)
//   col 5     charge ring (a part gathering for its tell)
//
// Every cell is 1 world unit across at scale 1, like BossArt's cells.
public static class BossAttackFx
{
    public const string Path = "BossAttackFx/boss_attack_fx";
    public const int Columns = 6, Rows = 4;
    public const int Spark0 = 0, SparkFrames = 3, Flash0 = 3, FlashFrames = 2, Ring = 5;
    public static readonly int[] SparkTicks = { 1, 2, 3 };
    public const int FlashTicks = 2;
    public static readonly int[] FlashTickTable = { FlashTicks, FlashTicks };

    static Sprite[] sheet;

    static Sprite[] Sheet()
    {
        if (sheet != null && sheet.Length > 0 && sheet[0] != null) return sheet;
        sheet = new Sprite[Columns * Rows];
        var tex = Resources.Load<Texture2D>(Path);
        if (tex == null) return sheet;
        float w = tex.width / (float)Columns, h = tex.height / (float)Rows;
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Columns; c++)
                sheet[r * Columns + c] = Sprite.Create(tex, new Rect(c * w, (Rows - 1 - r) * h, w, h),
                                                       new Vector2(.5f, .5f), w);
        return sheet;
    }

    public static int Row(BossDef boss)
    {
        int w = BossEmitters.World(boss);
        return Mathf.Clamp(w < 0 ? 0 : w, 0, Rows - 1);
    }

    public static Sprite Get(BossDef boss, int col) =>
        Sheet()[Row(boss) * Columns + Mathf.Clamp(col, 0, Columns - 1)];
}
