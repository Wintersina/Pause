using System.Collections.Generic;
using UnityEngine;

// Slices the per-ship weapon atlases rendered by Art/Weapons/src~/weapons.py
// (Resources/Weapons/<artKey>.png) and the shared Explosions.png.
//
// Atlas layout, 128px cells, 16 columns x 3 rows, row 0 at the top:
//   row 0  charge 0..15
//   row 1  ready 0..3 | shot 0..5 (0..3 flight loop, 4..5 smear) | impact 0..5
//   row 2  muzzle 0..3 | release 0..3 | trail 0..1
// Every sprite is 1 world unit across at scale 1 (pixelsPerUnit = cell size),
// so a renderer's localScale is directly its world size.
public static class WeaponArt
{
    public const int Columns = 16, Rows = 3;
    public const int ChargeFrames = 16, ReadyFrames = 4, ShotFrames = 6, ShotLoopFrames = 4;
    public const int ImpactFrames = 6, MuzzleFrames = 4, ReleaseFrames = 4, TrailFrames = 2;
    public const int ExplosionFrames = 10;

    // Flat index of each section inside a ship's sprite array.
    const int ChargeAt = 0, ReadyAt = 16, ShotAt = 20, ImpactAt = 26, MuzzleAt = 32, ReleaseAt = 36, TrailAt = 40;
    const int SpriteCount = 42;

    static readonly Dictionary<string, Sprite[]> sheets = new Dictionary<string, Sprite[]>();
    static Sprite[] explosionSheet;

    static Sprite Cell(Texture2D tex, int col, int row)
    {
        float w = tex.width / (float)Columns, h = tex.height / (float)Rows;
        return Sprite.Create(tex, new Rect(col * w, (Rows - 1 - row) * h, w, h), new Vector2(.5f, .5f), w);
    }

    static void Put(Sprite[] dest, int at, Texture2D tex, int row, int col, int count)
    {
        for (int i = 0; i < count; i++) dest[at + i] = Cell(tex, col + i, row);
    }

    // Sprite.Create()d sprites can be destroyed under a cache when the editor
    // opens a new scene (tests do this a lot), so a stale entry is rebuilt.
    static Sprite[] Sheet(int ship)
    {
        string key = WeaponStyleTable.For(ship).artKey;
        Sprite[] sheet;
        if (sheets.TryGetValue(key, out sheet) && sheet != null && sheet[0] != null) return sheet;
        var tex = Resources.Load<Texture2D>("Weapons/" + key);
        sheet = new Sprite[SpriteCount];
        if (tex != null)
        {
            Put(sheet, ChargeAt, tex, 0, 0, ChargeFrames);
            Put(sheet, ReadyAt, tex, 1, 0, ReadyFrames);
            Put(sheet, ShotAt, tex, 1, 4, ShotFrames);
            Put(sheet, ImpactAt, tex, 1, 10, ImpactFrames);
            Put(sheet, MuzzleAt, tex, 2, 0, MuzzleFrames);
            Put(sheet, ReleaseAt, tex, 2, 4, ReleaseFrames);
            Put(sheet, TrailAt, tex, 2, 8, TrailFrames);
        }
        sheets[key] = sheet;
        return sheet;
    }

    static Sprite Get(int ship, int at, int count, int i)
    {
        return Sheet(ship)[at + Mathf.Clamp(i, 0, count - 1)];
    }

    public static Sprite Charge(int ship, int i)  => Get(ship, ChargeAt, ChargeFrames, i);
    public static Sprite Ready(int ship, int i)   => Get(ship, ReadyAt, ReadyFrames, ((i % ReadyFrames) + ReadyFrames) % ReadyFrames);
    public static Sprite Shot(int ship, int i)    => Get(ship, ShotAt, ShotFrames, i);
    public static Sprite Impact(int ship, int i)  => Get(ship, ImpactAt, ImpactFrames, i);
    public static Sprite Muzzle(int ship, int i)  => Get(ship, MuzzleAt, MuzzleFrames, i);
    public static Sprite Release(int ship, int i) => Get(ship, ReleaseAt, ReleaseFrames, i);
    public static Sprite Trail(int ship, int i)   => Get(ship, TrailAt, TrailFrames, i);

    // ---- target explosions --------------------------------------------------

    // Explosions.png: rows metal / rock / mine, ten frames each; row 0
    // columns 10-12 are white overlays (flash star x2, shockwave ring) that
    // are tinted with the firing weapon's colour at runtime.
    const int FlashAt = 30, RingAt = 32, ExplosionSpriteCount = 33;

    static Sprite[] Explosions()
    {
        if (explosionSheet != null && explosionSheet[0] != null) return explosionSheet;
        explosionSheet = new Sprite[ExplosionSpriteCount];
        var tex = Resources.Load<Texture2D>("Weapons/Explosions");
        if (tex != null)
        {
            for (int row = 0; row < 3; row++) Put(explosionSheet, row * ExplosionFrames, tex, row, 0, ExplosionFrames);
            Put(explosionSheet, FlashAt, tex, 0, 10, 2);
            explosionSheet[RingAt] = Cell(tex, 12, 0);
        }
        return explosionSheet;
    }

    public static Sprite Explosion(TargetExplosion.Kind kind, int i)
    {
        return Explosions()[(int)kind * ExplosionFrames + Mathf.Clamp(i, 0, ExplosionFrames - 1)];
    }

    public static Sprite ExplosionFlash(int i) => Explosions()[FlashAt + Mathf.Clamp(i, 0, 1)];
    public static Sprite ExplosionRing() => Explosions()[RingAt];
}
