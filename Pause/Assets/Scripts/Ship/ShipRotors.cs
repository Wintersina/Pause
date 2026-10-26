using System;
using System.Collections.Generic;
using UnityEngine;

// Spinning overlay rotors on a ship (Turtle's four helicopter blades). A ship opts in by having
// Resources/ShipArt/<Key>/<Key>_rotors.json; the loader finds it by art key, so adding rotors to
// another ship touches no shared file:
//
//   { "rotors": [ { "x": 51.5, "y": 43.5, "scale": 1.0, "speed_deg_per_s": 540, "direction": 1 }, ... ] }
//
//   x, y             hub centre in the 256 px hull cell, x right, y DOWN (image pixels, idle cell)
//   scale            1.0 = one rotor-sheet pixel per hull-cell pixel (Turtle's rotor has radius 48 px)
//   speed_deg_per_s  angular speed; direction +1 counter-clockwise, -1 clockwise
//
// Art: <Key>_rotor.png, a 2-cell sheet of square cells: left the blades (pivot = hub), right a static
// faint motion-blur disc. Both are drawn as children of the hull's transform (so they follow its
// position, scale and rotation), just above the hull and below the shield.
//
// Timing: scaled time by default (freezes with the pause, like the hull's flipbook); the dock passes
// unscaled so its parked ships keep spinning whatever timeScale is.
// Hull state is mirrored each LateUpdate: hidden while the hull is disabled (DeathCrash) and tinted
// with the hull's colour (invulnerability blink, dock dimming / silhouette ink).
public class ShipRotors : MonoBehaviour
{
    [Serializable] public class RotorData { public float x, y, scale = 1f, speed_deg_per_s = 540f, direction = 1f; }
    [Serializable] public class RotorFile { public RotorData[] rotors; }

    public const int CellSize = 256;

    public static string DataPath(string key) { return ShipData.Folder(key) + key + "_rotors"; }
    public static string SpritePath(string key) { return ShipData.Folder(key) + key + "_rotor"; }

    // ------------------------------------------------------------ data
    static readonly Dictionary<string, RotorFile> files = new Dictionary<string, RotorFile>();

    public static RotorFile Load(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        RotorFile file;
        if (files.TryGetValue(key, out file)) return file;
        var asset = Resources.Load<TextAsset>(DataPath(key));
        if (asset != null)
        {
            file = Parse(asset.text);
            Resources.UnloadAsset(asset);
        }
        files[key] = file;
        return file;
    }

    public static RotorFile Parse(string json)
    {
        RotorFile f = null;
        try { f = JsonUtility.FromJson<RotorFile>(json); } catch (ArgumentException) { }
        return f != null && f.rotors != null && f.rotors.Length > 0 ? f : null;
    }

    public static bool Has(int id)
    {
        return Load(ShipId.ArtKeyOf(id)) != null;
    }

    // Hub position in the hull sprite's local space (world units at scale 1), from the hull rect.
    public static Vector2 ToLocal(ShipHullArt.HullRect r, float x, float y)
    {
        return new Vector2((x - (r.x + r.w * .5f)) / r.pixelsPerUnit,
                           (CellSize - y - (r.y + r.h * .5f)) / r.pixelsPerUnit);
    }

    // ------------------------------------------------------------ sprites
    static readonly Dictionary<string, Sprite[]> sprites = new Dictionary<string, Sprite[]>();

    // [0] blades, [1] blur disc; null when the sheet is missing.
    public static Sprite[] SpritesFor(string key)
    {
        Sprite[] pair;
        if (sprites.TryGetValue(key, out pair) && pair != null && pair[0] != null) return pair;
        var tex = Resources.Load<Texture2D>(SpritePath(key));
        pair = null;
        if (tex != null && tex.width >= tex.height * 2)
        {
            int c = tex.height;
            pair = new[]
            {
                Sprite.Create(tex, new Rect(0, 0, c, c), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect),
                Sprite.Create(tex, new Rect(c, 0, c, c), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect),
            };
            pair[0].name = tex.name + "_blades";
            pair[1].name = tex.name + "_blur";
        }
        sprites[key] = pair;
        return pair;
    }

    // ------------------------------------------------------------ component
    public int shipId;
    public bool unscaledTime;
    // Set by an owner that wants them off (the dock hides them on an unbought ink silhouette).
    bool hiddenByOwner;
    public bool hidden { get { return hiddenByOwner; } set { if (hiddenByOwner == value) return; hiddenByOwner = value; Sync(); } }

    SpriteRenderer hull;
    Transform[] blades;
    SpriteRenderer[] bladeRenderers, discRenderers;
    float[] speed;      // deg / s, signed
    float[] angle;

    public int Count { get { return blades == null ? 0 : blades.Length; } }
    public float AngleOf(int i) { return angle[i]; }

    // Adds the rotors to `ship` if its folder has a rotors file. Returns null otherwise.
    public static ShipRotors Attach(GameObject ship, int id, SpriteRenderer hull, bool unscaled)
    {
        if (ship == null || hull == null) return null;
        string key = ShipId.ArtKeyOf(id);
        var file = Load(key);
        if (file == null || !ShipHullArt.Has(id)) return null;
        var art = SpritesFor(key);
        if (art == null) return null;
        var existing = ship.GetComponent<ShipRotors>();
        if (existing != null) return existing;
        var r = ShipHullArt.RectFor(id);
        var self = ship.AddComponent<ShipRotors>();
        self.Build(id, hull, file, art, r, unscaled);
        return self;
    }

    void Build(int id, SpriteRenderer hullRenderer, RotorFile file, Sprite[] art, ShipHullArt.HullRect r, bool unscaled)
    {
        shipId = id;
        unscaledTime = unscaled;
        hull = hullRenderer;
        int n = file.rotors.Length;
        blades = new Transform[n];
        bladeRenderers = new SpriteRenderer[n];
        discRenderers = new SpriteRenderer[n];
        speed = new float[n];
        angle = new float[n];
        for (int i = 0; i < n; i++)
        {
            var d = file.rotors[i];
            Vector2 p = ToLocal(r, d.x, d.y);
            float s = (100f / r.pixelsPerUnit) * (d.scale <= 0f ? 1f : d.scale);   // sheet px -> hull px
            var root = new GameObject("Rotor" + i).transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(p.x, p.y, 0f);
            root.localScale = new Vector3(s, s, 1f);
            discRenderers[i] = MakeRenderer("Blur", root, art[1], .01f);
            bladeRenderers[i] = MakeRenderer("Blades", root, art[0], 0f);
            blades[i] = bladeRenderers[i].transform;
            speed[i] = d.speed_deg_per_s * (d.direction < 0f ? -1f : 1f);
            angle[i] = i * 37f;   // out of step
            blades[i].localRotation = Quaternion.Euler(0f, 0f, angle[i]);
        }
        Sync();
    }

    // Draws the blades and their blur disc with `material` (the title traffic's haze layers).
    public void SetMaterial(Material material)
    {
        if (blades == null || material == null) return;
        for (int i = 0; i < blades.Length; i++)
            bladeRenderers[i].sharedMaterial = discRenderers[i].sharedMaterial = material;
    }

    SpriteRenderer MakeRenderer(string name, Transform parent, Sprite sprite, float z)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, 0f, z);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        return sr;
    }

    void LateUpdate()
    {
        float dt = unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        if (dt > 0f && blades != null)
            for (int i = 0; i < blades.Length; i++)
            {
                angle[i] = Mathf.Repeat(angle[i] + speed[i] * dt, 360f);
                blades[i].localRotation = Quaternion.Euler(0f, 0f, angle[i]);
            }
        Sync();
    }

    // Mirror the hull: visibility, sorting and colour.
    void Sync()
    {
        if (hull == null || blades == null) return;
        bool show = !hiddenByOwner && hull.enabled && hull.sprite != null;
        Color tint = hull.color;
        int layer = hull.sortingLayerID, order = hull.sortingOrder + 1;
        for (int i = 0; i < blades.Length; i++)
        {
            var b = bladeRenderers[i];
            var d = discRenderers[i];
            b.enabled = show;
            d.enabled = show;
            b.color = tint;
            d.color = tint;
            b.sortingLayerID = d.sortingLayerID = layer;
            b.sortingOrder = d.sortingOrder = order;
        }
    }
}
