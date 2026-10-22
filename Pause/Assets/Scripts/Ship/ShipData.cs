using System;
using UnityEngine;

// A player ship's own data file: Resources/ShipArt/<Key>/<Key>_data.json, next to its
// hull sheet, skin sheets, flame strips and baked shield / hitbox. Everything that used
// to be a row in a shared C# table lives here instead, so adding or restyling a ship
// touches only that ship's folder:
//
//   rect       the sprite rect inside a 256 px hull cell + pixels-per-unit   (ShipHullArt)
//   hue        the identity colours: base, shadow, highlight                 (ShipHullArt)
//   nozzles    engine nozzle points in sprite-rect pixels                    (ShipNozzles)
//   emitters   damage FX points on the 128 u design canvas                   (ShipDamageTable)
//   skins      the colourways, index 0 = Stock                               (ShipSkins)
//   exhaust    flame theme, colours, loop and the strips in <Key>_flame.png  (ShipExhaustStyle)
//   painted    drawn as hi-res neon pixel art (not flat vector cels)         (PaintedShips)
//
// The roster id -> art key list stays in ShipId (the ids are the save-game numbers); the
// folder name is that key. Each file is parsed once, on first use, with JsonUtility.
[Serializable] public class ShipRectData { public int x, y, w, h; public float ppu = 100f; }
[Serializable] public class ShipNozzleData { public float x, y, scale = 1f; }
[Serializable] public class ShipEmitterData { public int state; public string kind; public float u, v; }
[Serializable]
public class ShipSkinData
{
    public string name, kind, hue, shadow, highlight, pattern, accent, accentShadow, accentHighlight;
}
[Serializable] public class ShipStripData { public string layer; public int x, y, w, h, frames, stride; }
[Serializable]
public class ShipExhaustData
{
    public string kind, outer, mid, core, dark, accent;
    public int[] ticks;
    public ShipStripData[] strips;
}

[Serializable]
public class ShipData
{
    public int id;
    public string key;
    public bool painted;
    public ShipRectData rect;
    public string[] hue;
    public ShipNozzleData[] nozzles;
    public string damageNote;
    public ShipEmitterData[] emitters;
    public ShipSkinData[] skins;
    public ShipExhaustData exhaust;

    // Resolved on load.
    [NonSerialized] public ShipHullArt.HullRect hullRect;
    [NonSerialized] public ShipNozzles.Nozzle[] nozzleList;
    [NonSerialized] public DamageEmitter[] emitterList;
    [NonSerialized] public ShipSkin[] skinList;

    // ------------------------------------------------------------ paths
    public static string Folder(string key) { return "ShipArt/" + key + "/"; }
    public static string HullPath(string key) { return Folder(key) + key; }
    public static string SkinPath(string key, string skin) { return Folder(key) + key + "_" + skin; }
    public static string FlamePath(string key) { return Folder(key) + key + "_flame"; }
    public static string ShieldPath(string key) { return Folder(key) + key + "_shield"; }
    public static string HitboxPath(string key) { return Folder(key) + key + "_hitbox"; }
    public static string DataPath(string key) { return Folder(key) + key + "_data"; }

    // ------------------------------------------------------------ load
    const int MaxIds = 64;
    static readonly ShipData[] loaded = new ShipData[MaxIds];
    static readonly bool[] tried = new bool[MaxIds];

    // The ship's data, or null when its folder has none.
    public static ShipData For(int id)
    {
        if (id <= 0 || id >= MaxIds) return null;
        if (tried[id]) return loaded[id];
        tried[id] = true;
        string key = ShipId.ArtKeyOf(id);
        if (key == null) return null;
        var asset = Resources.Load<TextAsset>(DataPath(key));
        if (asset == null) return null;
        var data = Parse(asset.text, id);
        Resources.UnloadAsset(asset);
        loaded[id] = data;
        return data;
    }

    public static bool Has(int id) { return For(id) != null; }

    // Forget the parsed files (tests, the editor after a rewrite).
    public static void Invalidate()
    {
        for (int i = 0; i < MaxIds; i++) { loaded[i] = null; tried[i] = false; }
    }

    public static ShipData Parse(string json, int id)
    {
        var d = JsonUtility.FromJson<ShipData>(json);
        if (d == null) return null;
        d.id = id;
        d.key = ShipId.ArtKeyOf(id);
        var r = d.rect ?? new ShipRectData { w = ShipHullArt.Cell, h = ShipHullArt.Cell };
        string[] h = d.hue != null && d.hue.Length >= 3 ? d.hue : new[] { "#FFFFFF", "#FFFFFF", "#FFFFFF" };
        d.hullRect = new ShipHullArt.HullRect(r.x, r.y, r.w, r.h, r.ppu, h[0], h[1], h[2]);

        var n = d.nozzles ?? new ShipNozzleData[0];
        d.nozzleList = new ShipNozzles.Nozzle[n.Length];
        for (int i = 0; i < n.Length; i++) d.nozzleList[i] = new ShipNozzles.Nozzle(n[i].x, n[i].y, n[i].scale);

        var e = d.emitters ?? new ShipEmitterData[0];
        d.emitterList = new DamageEmitter[e.Length];
        for (int i = 0; i < e.Length; i++)
            d.emitterList[i] = new DamageEmitter(e[i].state, (DamageEmitterKind)Enum.Parse(typeof(DamageEmitterKind), e[i].kind), e[i].u, e[i].v);

        var s = d.skins ?? new ShipSkinData[0];
        d.skinList = new ShipSkin[s.Length];
        for (int i = 0; i < s.Length; i++)
            d.skinList[i] = new ShipSkin
            {
                name = s[i].name, kind = (ShipSkinKind)Enum.Parse(typeof(ShipSkinKind), s[i].kind),
                hue = s[i].hue, hueShadow = s[i].shadow, hueHighlight = s[i].highlight,
                pattern = NullIfEmpty(s[i].pattern), accent = NullIfEmpty(s[i].accent),
                accentShadow = NullIfEmpty(s[i].accentShadow), accentHighlight = NullIfEmpty(s[i].accentHighlight),
            };
        return d;
    }

    static string NullIfEmpty(string s) { return string.IsNullOrEmpty(s) ? null : s; }
}
