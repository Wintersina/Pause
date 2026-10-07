using UnityEngine;

// Slices the two shared atlases the ship attacks and secret powers draw from,
// both rendered by Art/Weapons/src~/weapons.py (fx_attack / fx_power /
// meter functions, colours from the same PALETTES row as each ship's
// weapon):
//
//   Resources/Weapons/AttackFx.png   128px cells, 16 x 15 (row = ship id - 1)
//     cols 0-3   attack loop   (beam, flame cone, chain arc, javelin strike,
//                               rail flash, orbit ring, ...)
//     cols 4-7   attack burst  (fireball blast, impact sparks, ...)
//     cols 8-11  power loop    (cloak shimmer, magnet field, black hole, ...)
//     cols 12-15 power burst   (nova, EMP ring, shield pop, ...)
//
//   Resources/Weapons/SecretMeter.png  64px cells, 16 x 15 (row = ship id - 1)
//     cols 0-9   the badge filling up (10 steps)
//     cols 10-13 ready loop (held key pose, then the pulse on 2s)
//     col 14     the fire flash
//     col 15     icon only
//
// Every sprite is one world unit across at scale 1, like WeaponArt.
public static class ShipFxArt
{
    public const int Columns = 16, Rows = 15;
    public const int LoopFrames = 4, BurstFrames = 4;
    public const int MeterFillFrames = 10, MeterReadyFrames = 4;

    public static readonly int[] LoopTicks = { 2, 2, 2, 2 };
    public static readonly int[] BurstTicks = { 1, 1, 2, 3 };
    public static readonly int[] PowerLoopTicks = { 3, 2, 3, 2 };
    public static readonly int[] PowerBurstTicks = { 1, 2, 2, 3 };
    public static readonly int[] MeterReadyTicks = { 4, 2, 2, 2 };

    const int AttackLoopAt = 0, AttackBurstAt = 4, PowerLoopAt = 8, PowerBurstAt = 12;
    const int MeterFillAt = 0, MeterReadyAt = 10, MeterFlashAt = 14, MeterIconAt = 15;

    static Sprite[] attackSheet, meterSheet;

    static Sprite[] Slice(string file)
    {
        var tex = Resources.Load<Texture2D>("Weapons/" + file);
        var sheet = new Sprite[Columns * Rows];
        if (tex == null) return sheet;
        float w = tex.width / (float)Columns, h = tex.height / (float)Rows;
        for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Columns; col++)
                sheet[row * Columns + col] = Sprite.Create(tex, new Rect(col * w, (Rows - 1 - row) * h, w, h),
                                                           new Vector2(.5f, .5f), w);
        return sheet;
    }

    // Rebuilt if the editor destroyed the Sprite.Create()d cells (tests open
    // new scenes a lot).
    static Sprite[] Attack()
    {
        if (attackSheet == null || attackSheet[0] == null) attackSheet = Slice("AttackFx");
        return attackSheet;
    }

    static Sprite[] Meter()
    {
        if (meterSheet == null || meterSheet[0] == null) meterSheet = Slice("SecretMeter");
        return meterSheet;
    }

    static int Row(int ship) => (ShipId.IsValid(ship) && ship <= Rows ? ship : ShipId.Starter) - 1;

    static Sprite Get(Sprite[] sheet, int ship, int at, int count, int i)
    {
        return sheet[Row(ship) * Columns + at + Mathf.Clamp(i, 0, count - 1)];
    }

    public static Sprite AttackLoop(int ship, int i)  => Get(Attack(), ship, AttackLoopAt, LoopFrames, Wrap(i, LoopFrames));
    public static Sprite AttackBurst(int ship, int i) => Get(Attack(), ship, AttackBurstAt, BurstFrames, i);
    public static Sprite PowerLoop(int ship, int i)   => Get(Attack(), ship, PowerLoopAt, LoopFrames, Wrap(i, LoopFrames));
    public static Sprite PowerBurst(int ship, int i)  => Get(Attack(), ship, PowerBurstAt, BurstFrames, i);

    public static Sprite MeterFill(int ship, int i)   => Get(Meter(), ship, MeterFillAt, MeterFillFrames, i);
    public static Sprite MeterReady(int ship, int i)  => Get(Meter(), ship, MeterReadyAt, MeterReadyFrames, Wrap(i, MeterReadyFrames));
    public static Sprite MeterFlash(int ship)         => Get(Meter(), ship, MeterFlashAt, 1, 0);
    public static Sprite MeterIcon(int ship)          => Get(Meter(), ship, MeterIconAt, 1, 0);

    static int Wrap(int i, int n) => ((i % n) + n) % n;

    // Fill fraction -> drawing, monotonic; the full drawing only when full.
    public static int MeterFrameFor(float fill01)
    {
        if (fill01 >= 1f) return MeterFillFrames - 1;
        return Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(fill01) * (MeterFillFrames - 1)), 0, MeterFillFrames - 2);
    }
}
