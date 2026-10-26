using UnityEngine;

// The ship attack and secret power art. Every attack and power is a 4096 x 256 strip of 16 cells
// (cells 0-7 the loop, 8-15 the burst), drawn by Codex and kept in Resources/Weapons/Powers2/
// <ShipKey>_<name>.png (Strip / Extra below); one world unit across at scale 1.
//
// The secret-power badge still comes from one atlas rendered by Art/Weapons/src~/weapons.py:
//   Resources/Weapons/SecretMeter.png  64px cells, 16 x 15 (row = ship id - 1; 16 ships)
//     cols 0-9   the badge filling up (10 steps)
//     cols 10-13 ready loop (held key pose, then the pulse on 2s)
//     col 14     the fire flash
//     col 15     icon only
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
    public static readonly int[] MeterReadyTicks = { 4, 2, 2, 2 };

    const int MeterFillAt = 0, MeterReadyAt = 10, MeterFlashAt = 14, MeterIconAt = 15;

    static Sprite[] meterSheet;
    static readonly System.Collections.Generic.Dictionary<string, Sprite[]> strips = new System.Collections.Generic.Dictionary<string, Sprite[]>();

    // Resources/Weapons/Powers2/<ShipId.KeyOf>_attack.png: 16 cells of 256 px (0-7 loop, 8-15 burst or
    // impact), one world unit across at scale 1. Null when the strip is missing. (No ship has a generic
    // _power.png any more: every secret power draws its own named strip, PowerStripName.)
    public static Sprite[] Strip(int ship, bool power) => Strip(ship, power ? "power" : "attack");

    // The strip a secret power draws in flight (Powers2/<ShipKey>_<name>.png): the one table the game's
    // SecretPowers and the dock's power preview both read, so the card cannot show older art than the
    // flight.
    public static string PowerStripName(SecretPower power)
    {
        switch (power)
        {
            case SecretPower.ShieldPulse: return "pulse";
            case SecretPower.EmpStun: return "emp";
            case SecretPower.SolarNova: return "nova";
            case SecretPower.IonBattery: return "battery";
            case SecretPower.PhaseCloak: return "cloak";
            case SecretPower.GoldMagnet: return "magnet";
            case SecretPower.Thunderclap: return "clap";
            case SecretPower.FlareDecoy: return "pop";
            case SecretPower.TractorPull: return "tractor";
            case SecretPower.Mending: return "mending";
            case SecretPower.HardShell: return "shell";
            case SecretPower.AegisShield: return "aegis";
            case SecretPower.CapacitorDump: return "dump";
            case SecretPower.BlinkDash: return "dash";
            case SecretPower.BlackHole: return "hole";
            case SecretPower.TimeBubble: return "bubble";
        }
        return null;
    }

    // The same, for any named strip of the ship: "attack_mark" -> <ShipKey>_attack_mark.png.
    public static Sprite[] Strip(int ship, string suffix)
    {
        string key = (ShipId.KeyOf(ShipId.IsValid(ship) ? ship : ShipId.Starter) ?? "") + "_" + suffix;
        // Skin variant: a ship wearing its Special skin uses <key>_special when that strip exists (any ship can add one).
        if (ShipId.IsValid(ship) && ShipSkins.Has(ship, ShipSkins.Special) && ShipSkins.Shown(ship) == ShipSkins.Special)
        {
            var special = Load(key + "_special");
            if (special != null) return special;
        }
        return Load(key);
    }

    static Sprite[] Load(string key)
    {
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

    // A ship's own attack strip: cell i of the loop (wrapped) or of the burst (clamped); null when it is missing.
    static Sprite Fx(int ship, bool burst, int i)
    {
        var strip = Strip(ship, false);
        if (strip == null) return null;
        return strip[(burst ? LoopFrames : 0) + (burst ? Mathf.Clamp(i, 0, BurstFrames - 1) : Wrap(i, LoopFrames))];
    }

    // A cell of an extra strip (loop 0-7, burst 8-15); the ship's own attack strip when that one is missing.
    public static Sprite Extra(int ship, string suffix, bool burst, int i)
    {
        var strip = Strip(ship, "attack_" + suffix);
        if (strip == null) return Fx(ship, burst, i);
        return strip[(burst ? LoopFrames : 0) + (burst ? Mathf.Clamp(i, 0, BurstFrames - 1) : Wrap(i, LoopFrames))];
    }

    // Cell `cell` (0-15) of a ship's extra strip (Strip's key suffix: "attack_ice"); null when the strip is missing.
    public static Sprite Extra(int ship, string suffix, int cell)
    {
        var strip = Strip(ship, suffix);
        return strip == null ? null : strip[Mathf.Clamp(cell, 0, StripCells - 1)];
    }

    public static Sprite AttackLoop(int ship, int i)  => Fx(ship, false, i);
    public static Sprite AttackBurst(int ship, int i) => Fx(ship, true, i);

    public static Sprite MeterFill(int ship, int i)   => Get(Meter(), ship, MeterFillAt, MeterFillFrames, i);
    public static Sprite MeterReady(int ship, int i)  => Get(Meter(), ship, MeterReadyAt, MeterReadyFrames, Wrap(i, MeterReadyFrames));
    public static Sprite MeterFlash(int ship)         => Get(Meter(), ship, MeterFlashAt, 1, 0);
    public static Sprite MeterIcon(int ship)          => Get(Meter(), ship, MeterIconAt, 1, 0);

    // ---- the polish pass: thinner, fainter, smaller --------------------------
    // Global dials for every ship attack and secret power drawing (AttackSprite,
    // AttackProjectile, WeaponShot). Gameplay hit sizes never read these.
    public const float AttackAlpha = .6f;       // attack loops (beams, cones, arcs)
    public const float AttackBurstAlpha = .65f; // impact bursts
    public const float RimAlpha = .8f;          // the held Time Bubble dome
    public const float ShotScale = .72f;        // projectile body, of the size the attack asks for
    public const float TrailScale = .55f;       // light streak behind a shot, of the old width
    public const float BeamThin = .45f;         // arcs / chain / lance links: thickness in world units
    public const float BurstScale = .7f;        // burst size (blasts, fireball) on top of the per-ship scales
    public const float ImpactScale = .75f;      // weapon impact flipbook, of the old size

    static Sprite dome;
    // Paranoid's Time Bubble dome (hex facets, hollowed centre so the hull reads); one world unit across at scale 1.
    public static Sprite Dome => dome != null ? dome : (dome = Resources.Load<Sprite>("PowerFx/paranoid_time_dome"));

    static Sprite hand;
    // The Time Bubble's clock hand: a hairline 1 unit tall at scale 1, pivot at the middle of the sprite
    // (the dome centre), the visible stroke only in the upper half between ~.30 and ~.84 of the radius.
    public static Sprite ClockHand
    {
        get
        {
            if (hand != null) return hand;
            const int W = 16, H = 256;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[W * H];
            float cx = (W - 1) * .5f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float up = (y - (H - 1) * .5f) / (H * .5f);        // 0 at the pivot, 1 at the top
                    float dx = Mathf.Abs(x - cx);
                    float along = up < .30f ? Mathf.Clamp01((up - .26f) / .04f)
                                : up > .84f ? Mathf.Clamp01((.88f - up) / .04f) : 1f;
                    float across = Mathf.Clamp01(1.7f - dx);            // ~2.4 px wide, soft edge
                    byte a = (byte)(255f * along * across);
                    px[y * W + x] = new Color32(235, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            hand = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(.5f, .5f), H);
            return hand;
        }
    }

    static int Wrap(int i, int n) => ((i % n) + n) % n;

    // Fill fraction -> drawing, monotonic; the full drawing only when full.
    public static int MeterFrameFor(float fill01)
    {
        if (fill01 >= 1f) return MeterFillFrames - 1;
        return Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(fill01) * (MeterFillFrames - 1)), 0, MeterFillFrames - 2);
    }
}
