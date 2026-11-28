using System;
using System.Collections.Generic;
using UnityEngine;

// The four drone tiers of a ship: one gun-drone design per drone slot (DroneSquad slot 0 = the main floatie,
// the UltimateGun barrel, is T1; slot 1 = T2; slot 2 = T3) and the healing drone (HealDrone).
public enum DroneTier { T1 = 0, T2 = 1, T3 = 2, Heal = 3 }

// Each ship's own drone drawings (the per-ship drone sheet), per skin, with a small light-pulse idle flipbook.
//
// Files (all in the ship's folder, written by Art/Resources/ShipArt/src~/build_drones.py from
// ShipArt/<Key>/src~/drones/<tier>.png):
//   ShipArt/<Key>/<Key>_dronelayout.json     layout: cell, frames, fps, pivot, the tiers present (row + sprite rect), skins
//   ShipArt/<Key>/<Key>_drones.bytes         stock sheet (PNG bytes): one row per present tier, one column per frame
//   ShipArt/<Key>/<Key>_drones_<Skin>.bytes  the same sheet recoloured for skin <Skin> (ShipSkins names)
//
// Nothing here lists ships or tiers: a ship (or one tier of it) without art answers null, and the callers fall
// back to the old roster tile (UltimateGun.CompanionSprite -> ShipArt/Guns/ship_gun_roster; HealDrone tints it
// green). So art dropped in later (a new tier, a new ship) is picked up with no code change. A skin whose sheet is
// missing uses the stock sheet.
//
// More than three gun drones (in-run slots 3-5): the tiers cycle (slot 3 = T1, 4 = T2, 5 = T3), so neighbours in
// the outer wings never share a drawing with the slot beside them.
//
// Sizes: every caller sizes a drone by its sprite bounds, so the sprite rect is a square around the drawing that it
// fills the way the old roster tiles filled theirs (~93%); the on-screen size is unchanged. Pivot as the roster's.
public static class DroneArt
{
    public const int GunTiers = 3;
    public const float RosterCell = 313.5f;   // the old tile's size in px at 100 ppu: drone sprites keep its world size

    // Tests: behave as if no ship had drone art (the roster-tile fallback everywhere).
    public static bool ForceFallback;

    // Tests: behave as if ship `HiddenShip` had no drawing for `HiddenTier` (the per-tier fallback); 0 = off.
    public static int HiddenShip;
    public static DroneTier HiddenTier;

    public static DroneTier TierForSlot(int droneSlot) { return (DroneTier)(Mathf.Max(0, droneSlot) % GunTiers); }

    [Serializable] public class CellData { public string tier; public int row, x, y, w, h; }

    [Serializable]
    public class SheetData
    {
        public int cell = 104, frames = 1;
        public float fps = 6f, pivotX = .5f, pivotY = .12f;
        public CellData[] tiers;
        public string[] skins;
    }

    // (the layout must not share a name with a sheet: Resources paths drop the extension, so "<Key>_drones" would
    // be both the .json and the stock .bytes and Resources.Load returns either one)
    public static string LayoutPath(string key) { return ShipData.Folder(key) + key + "_dronelayout"; }
    public static string SheetPath(string key, string skin)
    {
        return ShipData.Folder(key) + key + "_drones" + (string.IsNullOrEmpty(skin) ? "" : "_" + skin);
    }

    // ------------------------------------------------------------ layout

    const int MaxIds = 64;
    static readonly SheetData[] layouts = new SheetData[MaxIds];
    static readonly bool[] triedLayout = new bool[MaxIds];

    // The ship's drone layout, or null when it has no drone art.
    public static SheetData Layout(int ship)
    {
        if (ForceFallback || ship <= 0 || ship >= MaxIds) return null;
        if (triedLayout[ship]) return layouts[ship];
        triedLayout[ship] = true;
        string key = ShipId.ArtKeyOf(ship);
        if (key == null) return null;
        var asset = Resources.Load<TextAsset>(LayoutPath(key));
        if (asset == null) return null;
        SheetData d = null;
        try { d = JsonUtility.FromJson<SheetData>(asset.text); }
        catch (Exception e) { Debug.LogWarning("DroneArt: bad layout for " + key + ": " + e.Message); }
        Resources.UnloadAsset(asset);
        if (d != null && (d.tiers == null || d.tiers.Length == 0 || d.cell <= 0)) d = null;
        if (d != null) d.frames = Mathf.Max(1, d.frames);
        layouts[ship] = d;
        return d;
    }

    static string TierName(DroneTier t)
    {
        switch (t)
        {
            case DroneTier.T1: return "t1";
            case DroneTier.T2: return "t2";
            case DroneTier.T3: return "t3";
            default: return "heal";
        }
    }

    static CellData CellOf(int ship, SheetData d, DroneTier tier)
    {
        if (d == null || (HiddenShip != 0 && ship == HiddenShip && tier == HiddenTier)) return null;
        string name = TierName(tier);
        for (int i = 0; i < d.tiers.Length; i++) if (d.tiers[i] != null && d.tiers[i].tier == name) return d.tiers[i];
        return null;
    }

    // Does the ship have its own drawing for this tier?
    public static bool Has(int ship, DroneTier tier) { return CellOf(ship, Layout(ship), tier) != null; }

    // How many of the four tiers it has drawings for.
    public static int TierCount(int ship)
    {
        int n = 0;
        for (int t = 0; t <= (int)DroneTier.Heal; t++) if (Has(ship, (DroneTier)t)) n++;
        return n;
    }

    public static int Frames(int ship) { var d = Layout(ship); return d != null ? d.frames : 0; }

    // ------------------------------------------------------------ sheets

    class Sheet
    {
        public Texture2D tex;
        public Sprite[] sprites;   // [tier * frames + frame]
    }

    static readonly Dictionary<int, Sheet> sheets = new Dictionary<int, Sheet>();
    static int SheetKey(int ship, int skin) { return ship * ShipSkins.PerShip + Mathf.Clamp(skin, 0, ShipSkins.PerShip - 1); }

    // The skin's sheet name ("" for stock), or null when the ship has no such skin.
    static string SkinName(int ship, int skin)
    {
        if (skin == ShipSkins.Stock || !ShipSkins.Has(ship, skin)) return "";
        return ShipSkins.Get(ship, skin).name;
    }

    // Raw PNG bytes of a drone sheet (tests); null when the file is missing. skin 0 = stock.
    public static byte[] SheetPng(int ship, int skin)
    {
        string key = ShipId.ArtKeyOf(ship);
        if (key == null) return null;
        var asset = Resources.Load<TextAsset>(SheetPath(key, SkinName(ship, skin)));
        if (asset == null) return null;
        var bytes = asset.bytes;
        Resources.UnloadAsset(asset);
        return bytes;
    }

    static Sheet SheetFor(int ship, int skin)
    {
        var d = Layout(ship);
        if (d == null) return null;
        if (skin != ShipSkins.Stock && !ShipSkins.Has(ship, skin)) skin = ShipSkins.Stock;
        int k = SheetKey(ship, skin);
        Sheet s;
        if (sheets.TryGetValue(k, out s)) return s;
        var bytes = SheetPng(ship, skin);
        if (bytes == null)
        {
            s = skin != ShipSkins.Stock ? SheetFor(ship, ShipSkins.Stock) : null;   // a missing skin sheet: the stock drawing
            sheets[k] = s;
            return s;
        }
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(bytes, true))
        {
            UnityEngine.Object.DestroyImmediate(tex);
            sheets[k] = null;
            return null;
        }
        tex.name = ShipId.ArtKeyOf(ship) + "_drones" + (skin == ShipSkins.Stock ? "" : "_" + SkinName(ship, skin));
        tex.filterMode = FilterMode.Bilinear;   // drawn well below its pixel size and turned as it aims: bilinear, as the hulls
        tex.wrapMode = TextureWrapMode.Clamp;
        s = new Sheet { tex = tex, sprites = new Sprite[(int)DroneTier.Heal * d.frames + d.frames] };
        sheets[k] = s;
        return s;
    }

    // Tier `tier`, frame `frame` of the ship's drone in skin `skin`; null when the ship has no drawing for that tier.
    public static Sprite Frame(int ship, DroneTier tier, int skin, int frame)
    {
        var d = Layout(ship);
        var c = CellOf(ship, d, tier);
        if (c == null) return null;
        var s = SheetFor(ship, skin);
        if (s == null || s.tex == null) return null;
        frame = ((frame % d.frames) + d.frames) % d.frames;
        int i = (int)tier * d.frames + frame;
        if (s.sprites[i] != null) return s.sprites[i];
        // cut the tier's whole flipbook at once: the pulse never allocates mid-flight
        float ppu = c.w * 100f / RosterCell;
        for (int f = 0; f < d.frames; f++)
        {
            var rect = new Rect(c.x + f * d.cell, c.y, c.w, c.h);
            if (rect.xMax > s.tex.width || rect.yMax > s.tex.height || rect.xMin < 0 || rect.yMin < 0) return null;
            int j = (int)tier * d.frames + f;
            if (s.sprites[j] != null) continue;
            s.sprites[j] = UnityEngine.Sprite.Create(s.tex, rect, new Vector2(d.pivotX, d.pivotY), ppu, 0, SpriteMeshType.FullRect);
            s.sprites[j].name = s.tex.name + "_" + TierName(tier) + "_" + f;
        }
        return s.sprites[i];
    }

    // The idle flipbook frame at `time` (a slot's own phase keeps neighbours out of step).
    public static int FrameAt(int ship, float time, int phase = 0)
    {
        var d = Layout(ship);
        if (d == null || d.frames <= 1) return 0;
        int f = Mathf.FloorToInt(time * d.fps) + phase;
        return ((f % d.frames) + d.frames) % d.frames;
    }

    // The drawing to show now: the skin the ship is drawn in (ShipSkins.Shown), the pulse frame at `time`.
    public static Sprite Current(int ship, DroneTier tier, float time, int phase = 0)
    {
        if (!Has(ship, tier)) return null;
        return Frame(ship, tier, ShipSkins.Shown(ship), FrameAt(ship, time, phase + (int)tier));
    }

    // Decode the shown skin's sheet now (FirstUseWarm), not on the first drone of a run.
    public static void Warm(int ship)
    {
        if (Layout(ship) == null) return;
        for (int t = 0; t <= (int)DroneTier.Heal; t++) Current(ship, (DroneTier)t, 0f);
    }

    public static bool IsLoaded(int ship, int skin)
    {
        Sheet s;
        return sheets.TryGetValue(SheetKey(ship, skin), out s) && s != null && s.tex != null;
    }

    // Forget everything parsed and decoded (tests, the editor after a rebuild).
    public static void Invalidate()
    {
        var seen = new HashSet<Texture2D>();
        foreach (var pair in sheets)
        {
            var s = pair.Value;
            if (s == null || s.tex == null || !seen.Add(s.tex)) continue;
            for (int i = 0; i < s.sprites.Length; i++) if (s.sprites[i] != null) UnityEngine.Object.DestroyImmediate(s.sprites[i]);
            UnityEngine.Object.DestroyImmediate(s.tex);
        }
        sheets.Clear();
        HiddenShip = 0;
        for (int i = 0; i < MaxIds; i++) { layouts[i] = null; triedLayout[i] = false; }
    }
}
