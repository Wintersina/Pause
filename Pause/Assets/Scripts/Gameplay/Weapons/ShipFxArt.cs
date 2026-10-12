using UnityEngine;

// Slices the two shared atlases the ship attacks and secret powers draw from,
// both rendered by Art/Weapons/src~/weapons.py (fx_attack / fx_power /
// meter functions, colours from the same PALETTES row as each ship's
// weapon):
//
//   Resources/Weapons/AttackFx.png   128px cells, 16 x 15 (row = ship id - 1; 16 ships)
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
    public const int Columns = 16, Rows = 16;
    public const int LoopFrames = 8, BurstFrames = 8;
    public const int StripCell = 256, StripCells = 16;   // powers-v2 strips: 4096 x 256
    public const int MeterFillFrames = 10, MeterReadyFrames = 4;

    // 8-frame strips at 24 fps (one tick a drawing, the last burst drawing held longer).
    public static readonly int[] LoopTicks = { 1, 1, 1, 1, 1, 1, 1, 1 };
    public static readonly int[] BurstTicks = { 1, 1, 1, 1, 1, 1, 2, 2 };
    public static readonly int[] PowerLoopTicks = { 1, 1, 1, 1, 1, 1, 1, 1 };
    public static readonly int[] PowerBurstTicks = { 1, 1, 1, 1, 1, 2, 2, 2 };
    const int OldFrames = 4;   // the legacy AttackFx atlas, the fallback when a strip is missing
    public static readonly int[] MeterReadyTicks = { 4, 2, 2, 2 };

    const int AttackLoopAt = 0, AttackBurstAt = 4, PowerLoopAt = 8, PowerBurstAt = 12;
    const int MeterFillAt = 0, MeterReadyAt = 10, MeterFlashAt = 14, MeterIconAt = 15;

    static Sprite[] attackSheet, meterSheet;
    static readonly System.Collections.Generic.Dictionary<string, Sprite[]> strips = new System.Collections.Generic.Dictionary<string, Sprite[]>();

    // Resources/Weapons/Powers2/<ShipId.KeyOf>_power.png / _attack.png: 16
    // cells of 256 px (0-7 loop, 8-15 burst or impact), one world unit across
    // at scale 1 like the old atlas. Null when the strip is missing.
    public static Sprite[] Strip(int ship, bool power)
    {
        string key = (ShipId.KeyOf(ShipId.IsValid(ship) ? ship : ShipId.Starter) ?? "") + (power ? "_power" : "_attack");
        Sprite[] cells;
        if (strips.TryGetValue(key, out cells) && (cells == null || cells[0] != null)) return cells;
        var tex = Resources.Load<Texture2D>("Weapons/Powers2/" + key);
        cells = null;
        if (tex != null && tex.height > 0 && tex.width >= tex.height * StripCells)
        {
            cells = new Sprite[StripCells];
            float c = tex.width / (float)StripCells;
            for (int i = 0; i < StripCells; i++)
                cells[i] = Sprite.Create(tex, new Rect(i * c, 0, c, tex.height), new Vector2(.5f, .5f), c);
        }
        strips[key] = cells;
        return cells;
    }

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

    static Sprite Fx(int ship, bool power, bool burst, int i)
    {
        var strip = Strip(ship, power);
        if (strip != null) return strip[(burst ? LoopFrames : 0) + (burst ? Mathf.Clamp(i, 0, BurstFrames - 1) : Wrap(i, LoopFrames))];
        // fallback: the old 4-frame atlas, each of its drawings held for two of ours
        int at = power ? (burst ? PowerBurstAt : PowerLoopAt) : (burst ? AttackBurstAt : AttackLoopAt);
        int j = Mathf.Clamp(i, 0, burst ? BurstFrames - 1 : int.MaxValue) / 2;
        return Get(Attack(), ship, at, OldFrames, burst ? j : Wrap(j, OldFrames));
    }

    public static Sprite AttackLoop(int ship, int i)  => Fx(ship, false, false, i);
    public static Sprite AttackBurst(int ship, int i) => Fx(ship, false, true, i);
    public static Sprite PowerLoop(int ship, int i)   => Fx(ship, true, false, i);
    public static Sprite PowerBurst(int ship, int i)  => Fx(ship, true, true, i);

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
