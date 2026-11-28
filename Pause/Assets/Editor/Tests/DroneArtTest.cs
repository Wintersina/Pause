using System.Collections.Generic;
using UnityEngine;

// The ships' own drone drawings (DroneArt, ShipArt/<Key>/<Key>_drones*):
//   * every ship whose four drawings are approved has all four cells (t1, t2, t3, heal), each a multi-frame pulse
//   * the art keeps the old on-screen size (sprite bounds == the roster tile's) and every cell sits inside its sheet
//   * every skin of a ship with drone art has its own recoloured sheet, the same size as stock and not a copy of it
//   * drone slot -> tier: 0 = T1 (the UltimateGun), 1 = T2, 2 = T3, then the tiers cycle
//   * the fallback: a ship (or one tier) without art, or ForceFallback, gets the roster tile; no heal art -> null
public static class DroneArtTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DRONEART] PASS  " : "[DRONEART] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    // Ships whose t1, t2, t3 and heal drawings are all approved and installed. Add a ship here once its last
    // drawing lands (UFO heal pending).
    static readonly string[] Complete =
    {
        "NeonComet", "VoltViper", "SolarFang", "CrimsonHalo", "IonLancer", "JadePhantom", "GoldWarden",
        "Lightning", "Ligher", "Paranoid", "Ninja", "Saboteur", "Dove", "Turtle", "Paragon",
    };

    // Ships with only some drawings so far: the tiers they must have (the rest fall back to the roster tile).
    static readonly Dictionary<string, DroneTier[]> Partial = new Dictionary<string, DroneTier[]>
    {
        { "UFO", new[] { DroneTier.T1, DroneTier.T2, DroneTier.T3 } },
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        DroneArt.ForceFallback = false;
        DroneArt.Invalidate();
        try
        {
            SlotsToTiers();
            ApprovedShipsHaveTheirCells();
            CellsAndSizes();
            SkinVariants();
            Fallback();
        }
        finally
        {
            DroneArt.ForceFallback = false;
            DroneArt.Invalidate();
        }
        Debug.Log("[DRONEART] failures: " + fails);
        return fails;
    }

    static void SlotsToTiers()
    {
        var want = new[] { DroneTier.T1, DroneTier.T2, DroneTier.T3, DroneTier.T1, DroneTier.T2, DroneTier.T3 };
        bool ok = true;
        for (int s = 0; s < want.Length; s++) ok &= DroneArt.TierForSlot(s) == want[s];
        Check("drone slot 0/1/2 -> T1/T2/T3, slots 3-5 cycle", ok && DroneArt.TierForSlot(-1) == DroneTier.T1);
    }

    static void ApprovedShipsHaveTheirCells()
    {
        foreach (var key in Complete)
        {
            int id = ShipId.FromKey(key);
            Check(key + " has all four drone cells (t1, t2, t3, heal)", id > 0 && DroneArt.TierCount(id) == 4);
        }
        foreach (var pair in Partial)
        {
            int id = ShipId.FromKey(pair.Key);
            bool ok = id > 0;
            foreach (var t in pair.Value) ok &= DroneArt.Has(id, t);
            Check(pair.Key + " has its approved drone cells so far", ok);
        }
    }

    static void CellsAndSizes()
    {
        int ships = 0;
        foreach (int id in ShipId.All)
        {
            var layout = DroneArt.Layout(id);
            if (layout == null) continue;
            ships++;
            string key = ShipId.KeyOf(id);
            Check(key + ": the idle pulse has 2-4 frames at a sane rate", layout.frames >= 2 && layout.frames <= 4 && layout.fps > 0f && layout.fps <= 15f);
            float roster = MaxSide(UltimateGun.RosterSprite(id));
            for (int t = 0; t <= (int)DroneTier.Heal; t++)
            {
                var tier = (DroneTier)t;
                if (!DroneArt.Has(id, tier)) continue;
                bool cut = true, same = true, sized = true, opaque = true;
                Rect first = default;
                for (int f = 0; f < layout.frames; f++)
                {
                    var sp = DroneArt.Frame(id, tier, ShipSkins.Stock, f);
                    cut &= sp != null && sp.texture != null;
                    if (sp == null) continue;
                    if (f == 0) first = sp.rect;
                    same &= sp.rect.size == first.size && sp.rect.y == first.y && Mathf.Abs(sp.bounds.size.x - sp.bounds.size.y) < 1e-3f;
                    sized &= Mathf.Abs(MaxSide(sp) - roster) < .02f;
                    opaque &= sp.texture.width >= sp.rect.xMax && sp.texture.height >= sp.rect.yMax;
                }
                Check(key + " " + tier + ": every frame is cut inside the sheet", cut && opaque);
                Check(key + " " + tier + ": frames share one square rect (no jitter)", same);
                Check(key + " " + tier + ": it keeps the old drone's on-screen size", sized);
            }
        }
        Check("some ships have drone art", ships >= Complete.Length);
    }

    static float MaxSide(Sprite s) { return s == null ? 0f : Mathf.Max(s.bounds.size.x, s.bounds.size.y); }

    static void SkinVariants()
    {
        foreach (int id in ShipId.All)
        {
            if (DroneArt.Layout(id) == null) continue;
            string key = ShipId.KeyOf(id);
            var stockPng = DroneArt.SheetPng(id, ShipSkins.Stock);
            var stock = Decode(stockPng);
            Check(key + ": the stock drone sheet loads", stock != null);
            if (stock == null) continue;
            for (int skin = 1; skin < ShipSkins.CountFor(id); skin++)
            {
                string name = ShipSkins.Get(id, skin).name;
                var png = DroneArt.SheetPng(id, skin);
                var tex = Decode(png);
                bool exists = tex != null && tex.width == stock.width && tex.height == stock.height;
                Check(key + " " + name + ": has its own drone sheet, the stock size", exists);
                if (exists)
                {
                    Check(key + " " + name + ": recoloured, not a copy of stock", !Same(png, stockPng));
                    Check(key + " " + name + ": same silhouette as stock", SameAlpha(tex, stock));
                    var sp = DroneArt.Frame(id, FirstTier(id), skin, 0);
                    Check(key + " " + name + ": its sprites come from the skin sheet", sp != null && sp.texture != null && sp.texture.name.EndsWith("_drones_" + name));
                }
                if (tex != null) Object.DestroyImmediate(tex);
            }
            Object.DestroyImmediate(stock);
        }
    }

    static DroneTier FirstTier(int id)
    {
        for (int t = 0; t <= (int)DroneTier.Heal; t++) if (DroneArt.Has(id, (DroneTier)t)) return (DroneTier)t;
        return DroneTier.T1;
    }

    static Texture2D Decode(byte[] png)
    {
        if (png == null) return null;
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (t.LoadImage(png, false)) return t;
        Object.DestroyImmediate(t);
        return null;
    }

    static bool Same(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static bool SameAlpha(Texture2D a, Texture2D b)
    {
        var pa = a.GetPixels32();
        var pb = b.GetPixels32();
        if (pa.Length != pb.Length) return false;
        for (int i = 0; i < pa.Length; i++) if ((pa[i].a > 127) != (pb[i].a > 127)) return false;
        return true;
    }

    static void Fallback()
    {
        // a ship (or one tier) without its own drawing: the roster tile, and no heal art
        bool sawMissing = false, tiered = true, heal = true;
        foreach (int id in ShipId.All)
        {
            for (int slot = 0; slot < DroneSquad.MaxSlots; slot++)
            {
                var tier = DroneArt.TierForSlot(slot);
                var sp = UltimateGun.CompanionSprite(id, slot);
                if (DroneArt.Has(id, tier))
                    tiered &= sp != null && sp == DroneArt.Frame(id, tier, ShipSkins.Shown(id), DroneArt.FrameAt(id, Time.time, slot + (int)tier));
                else { sawMissing = true; tiered &= sp != null && sp == UltimateGun.RosterSprite(id); }
            }
            heal &= DroneArt.Has(id, DroneTier.Heal) ? UltimateGun.HealSprite(id) != null : UltimateGun.HealSprite(id) == null;
        }
        Check("each drone slot shows its tier's own drawing, or the roster tile where there is none", tiered);
        Check("the heal drawing is there exactly where a ship has one", heal);
        if (!sawMissing) Debug.Log("[DRONEART] note: every ship has every tier; the per-tier fallback is covered by ForceFallback only");

        DroneArt.ForceFallback = true;
        bool all = true;
        foreach (int id in ShipId.All)
        {
            for (int slot = 0; slot < 3; slot++) all &= UltimateGun.CompanionSprite(id, slot) == UltimateGun.RosterSprite(id);
            all &= UltimateGun.HealSprite(id) == null && DroneArt.TierCount(id) == 0;
        }
        Check("with no drone art at all every drone falls back to the roster tile (and no heal art)", all);
        DroneArt.ForceFallback = false;
        DroneArt.Invalidate();
        int some = ShipId.FromKey(Complete[0]);
        Check("lifting the fallback brings the art back", DroneArt.Has(some, DroneTier.T1) && UltimateGun.CompanionSprite(some, 0) != UltimateGun.RosterSprite(some));
    }
}
