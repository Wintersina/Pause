using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Every roster hull's silhouette (its rest drawing's alpha >= the shield's
// cutoff), baked at edit time by ShieldSilhouetteBaker into the ship's own folder,
// Resources/ShipArt/<Key>/<Key>_shield.bytes. The blue-atom shield's contour is
// cut from these, so the game never reads hull pixels back from the GPU: the
// hull sheets are non-readable, and a RenderTexture readback stalls the frame
// on a phone.
//
// One silhouette per ship serves every skin, damage state and idle drawing:
// skins are recolours with the stock alpha (ShipSkinsTest), and the shield is
// cut from the intact rest pose whatever is showing (ShipShield).
//
// File: "SIL1", int count (1 in a per-ship file), then per ship int id, int w, int h and
// w*h bits, row-major from the bottom row (Texture2D.GetPixels order), LSB first.
public static class ShieldSilhouettes
{
    public const string LegacyResourcePath = "Shield/hull_silhouettes";   // LEGACY-COMPAT
    public const int Magic = 0x314C4953;   // "SIL1"

    struct Entry { public int w, h; public byte[] bits; }

    static readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
    static readonly Dictionary<int, bool> tried = new Dictionary<int, bool>();
    static bool legacyTried;

    // How many roster ships have a baked silhouette.
    public static int Count
    {
        get
        {
            for (int id = 1; ShipId.ArtKeyOf(id) != null; id++) Load(id);
            return entries.Count;
        }
    }

    // The baked mask of ship `id`, if one was baked at exactly w x h.
    public static bool TryGet(int id, int w, int h, out bool[] mask)
    {
        mask = null;
        Load(id);
        Entry e;
        if (!entries.TryGetValue(id, out e) || e.w != w || e.h != h) return false;
        mask = Unpack(e.bits, w * h);
        return true;
    }

    // Forget the parsed files (the editor re-bakes them).
    public static void Invalidate() { entries.Clear(); tried.Clear(); legacyTried = false; }

    static void Load(int id)
    {
        if (tried.ContainsKey(id)) return;
        tried[id] = true;
        string key = ShipId.ArtKeyOf(id);
        if (key == null) return;
        var asset = Resources.Load<TextAsset>(ShipData.ShieldPath(key));
        if (asset != null) { Parse(asset.bytes, entries); return; }
        if (legacyTried) return;   // LEGACY-COMPAT: the one shared file
        legacyTried = true;
        asset = Resources.Load<TextAsset>(LegacyResourcePath);
        if (asset != null) Parse(asset.bytes, entries);
    }

    static void Parse(byte[] data, Dictionary<int, Entry> into)
    {
        if (data == null || data.Length < 8) return;
        using (var r = new BinaryReader(new MemoryStream(data)))
        {
            if (r.ReadInt32() != Magic) return;
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                var e = new Entry();
                int id = r.ReadInt32();
                e.w = r.ReadInt32();
                e.h = r.ReadInt32();
                e.bits = r.ReadBytes((e.w * e.h + 7) / 8);
                into[id] = e;
            }
        }
    }

    public static bool[] Unpack(byte[] bits, int n)
    {
        var mask = new bool[n];
        for (int i = 0; i < n; i++) mask[i] = (bits[i >> 3] & (1 << (i & 7))) != 0;
        return mask;
    }

    public static byte[] Pack(bool[] mask)
    {
        var bits = new byte[(mask.Length + 7) / 8];
        for (int i = 0; i < mask.Length; i++) if (mask[i]) bits[i >> 3] |= (byte)(1 << (i & 7));
        return bits;
    }

    // Serialises (id, w, h, mask) tuples in the file format above.
    public static byte[] Encode(IList<KeyValuePair<int, bool[]>> masks, IList<Vector2Int> sizes)
    {
        using (var ms = new MemoryStream())
        using (var wtr = new BinaryWriter(ms))
        {
            wtr.Write(Magic);
            wtr.Write(masks.Count);
            for (int i = 0; i < masks.Count; i++)
            {
                wtr.Write(masks[i].Key);
                wtr.Write(sizes[i].x);
                wtr.Write(sizes[i].y);
                wtr.Write(Pack(masks[i].Value));
            }
            wtr.Flush();
            return ms.ToArray();
        }
    }
}
