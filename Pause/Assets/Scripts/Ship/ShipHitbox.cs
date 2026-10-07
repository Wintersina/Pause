using System.Collections.Generic;
using System.IO;
using UnityEngine;

// The player ship's hit zones. Dodge-game convention: the damage hitbox is
// smaller than the art, never bigger, so a graze along the ink line is a
// near miss rather than a lost heart.
//
//   Hull    a PolygonCollider2D hugging the hull's main body: the rest
//           silhouette with its thin bits opened away (antennae, wingtip
//           lights, needle tips) and the edge eroded by a couple of pixels,
//           simplified to 24 points (a few more, never past 32, only where
//           24 would drop below 82% of the body: Lightning 25, Crimson Halo
//           32, whose outline must skirt the gaps inside its halo). Enabled
//           whenever no shield is up.
//
//   Shield  a PolygonCollider2D round the visible blue-atom shield line,
//           grown by ShieldMargin, so whatever comes near a shielded ship is
//           absorbed (and destroyed) where the shield visibly is and a bit
//           beyond. Enabled while any shield source is up: the blue atom
//           (ShipShield) or Hard Shell (SecretPowerController). Exactly one
//           of Hull / Shield is ever enabled, so a single contact can't
//           report twice; the swap happens the instant a source changes.
//
//   Pickups are forgiving on purpose, so a tight damage box doesn't make
//           dust and atoms fiddly: a pickup is collected when it comes within
//           PickupRadius of the hull centre (an overlap query each running
//           frame, no extra collider or body) or touches the active damage
//           collider, whichever is first. collisionDetection disables a
//           pickup's collider as it collects it, so it never pays twice.
//
// Both polygons are baked at edit time (ShieldSilhouetteBaker writes
// Resources/Shield/hull_hitboxes.bytes next to the silhouettes) in
// sprite-local units, keyed by ShipId: one shape per ship for every skin,
// damage state and idle drawing. Installing them at spawn (spawnShips.ApplyHull)
// only parses that file and sends two paths to physics -- no pixel is read,
// nothing is traced at runtime -- and a shield coming up flips `enabled`.
//
// Spinners (Ninja, UFO): the colliders live on the ship root, which is the
// transform ShipSpinDrift turns, so the polygons turn with the drawn hull.
// The pickup radius is a circle about the centre, so it doesn't care.
//
// Cloak (Phase Cloak, Shield Pulse's invulnerability) is not a shield here:
// it changes no collider, collisionDetection.Invulnerable decides.
public class ShipHitbox : MonoBehaviour
{
    // The single knob for the shielded hit zone: world units beyond the
    // visible shield line.
    public const float ShieldMargin = .15f;

    // Pickups are collected within this many world units of the hull centre
    // (the hull itself is ReferenceHullSize = 0.58 u across), x ShipScale on
    // a ship flown bigger (the main game: 0.3645 u), so the reach keeps its
    // ratio to the hull (Radius).
    public const float PickupRadius = .27f;

    public const string ResourcePath = "Shield/hull_hitboxes";
    public const int Magic = 0x31584248;   // "HBX1"

    [System.Flags]
    public enum Source { None = 0, Atom = 1, Shell = 2 }

    // ------------------------------------------------------------------
    // Baked shapes
    // ------------------------------------------------------------------

    public sealed class Shape
    {
        public Vector2[] hull;     // sprite-local units
        public Vector2[] shield;   // sprite-local units
    }

    static Dictionary<int, Shape> shapes;
    static float bakedMargin;

    public static int Count { get { Load(); return shapes.Count; } }
    public static float BakedMargin { get { Load(); return bakedMargin; } }

    public static Shape ShapeFor(int id)
    {
        Load();
        Shape s;
        return shapes.TryGetValue(id, out s) ? s : null;
    }

    // Forget the parsed file (the editor re-bakes it).
    public static void Invalidate() { shapes = null; }

    static void Load()
    {
        if (shapes != null) return;
        shapes = new Dictionary<int, Shape>();
        var asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset != null) bakedMargin = Decode(asset.bytes, shapes);
    }

    // File: "HBX1", float margin, int count, then per ship int id,
    // int n + n * (float x, float y) hull, int m + m * (x, y) shield.
    public static byte[] Encode(float margin, IList<KeyValuePair<int, Shape>> list)
    {
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(Magic);
            w.Write(margin);
            w.Write(list.Count);
            foreach (var kv in list)
            {
                w.Write(kv.Key);
                WritePath(w, kv.Value.hull);
                WritePath(w, kv.Value.shield);
            }
            w.Flush();
            return ms.ToArray();
        }
    }

    public static float Decode(byte[] data, Dictionary<int, Shape> into)
    {
        if (data == null || data.Length < 12) return 0f;
        using (var r = new BinaryReader(new MemoryStream(data)))
        {
            if (r.ReadInt32() != Magic) return 0f;
            float margin = r.ReadSingle();
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                int id = r.ReadInt32();
                var s = new Shape { hull = ReadPath(r), shield = ReadPath(r) };
                into[id] = s;
            }
            return margin;
        }
    }

    static void WritePath(BinaryWriter w, Vector2[] p)
    {
        w.Write(p.Length);
        foreach (var v in p) { w.Write(v.x); w.Write(v.y); }
    }

    static Vector2[] ReadPath(BinaryReader r)
    {
        var p = new Vector2[r.ReadInt32()];
        for (int i = 0; i < p.Length; i++) p[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
        return p;
    }

    // ------------------------------------------------------------------
    // On the ship
    // ------------------------------------------------------------------

    PolygonCollider2D hull, shield;
    Source sources;
    collisionDetection owner;
    readonly Collider2D[] nearby = new Collider2D[16];
    ContactFilter2D pickupFilter;

    public int ShipIndex { get; private set; }
    public PolygonCollider2D Hull { get { return hull; } }
    public PolygonCollider2D ShieldZone { get { return shield; } }
    public Collider2D Active { get { return sources != Source.None ? (Collider2D)shield : hull; } }
    public bool Shielded { get { return sources != Source.None; } }
    public Source Sources { get { return sources; } }

    // Paths sent to physics, ever (the pickup frame must send none).
    public static int PathWrites { get; private set; }

    // This ship's pickup reach (world units): PickupRadius x its scene's
    // ShipScale, read once (a scene's name allocates).
    public float Radius
    {
        get
        {
            if (radius < 0f) radius = PickupRadius * ShipScale.ForScene(gameObject.scene);
            return radius;
        }
    }
    float radius = -1f;

    public static ShipHitbox Of(GameObject ship)
    {
        return ship != null ? ship.GetComponent<ShipHitbox>() : null;
    }

    // Gives a gameplay ship roster ship `id`'s baked hit zones, retiring the
    // prefab's 2016 BoxCollider2D. Ships with no bake keep their box.
    public static ShipHitbox Install(GameObject ship, int id)
    {
        if (ship == null) return null;
        var shape = ShapeFor(id);
        if (shape == null || shape.hull == null || shape.hull.Length < 3) return null;
        var hb = ship.GetComponent<ShipHitbox>();
        if (hb == null) hb = ship.AddComponent<ShipHitbox>();
        hb.Setup(id, shape);
        return hb;
    }

    void Setup(int id, Shape shape)
    {
        ShipIndex = id;
        bool trigger = true;
        foreach (var box in GetComponents<BoxCollider2D>())
        {
            trigger = box.isTrigger;
            box.enabled = false;
            if (Application.isPlaying) Destroy(box); else DestroyImmediate(box);
        }
        if (hull == null) hull = NewZone(trigger);
        if (shield == null) shield = NewZone(trigger);
        SetPath(hull, shape.hull);
        SetPath(shield, shape.shield);
        Apply();
    }

    PolygonCollider2D NewZone(bool trigger)
    {
        var c = gameObject.AddComponent<PolygonCollider2D>();
        c.isTrigger = trigger;
        c.enabled = false;
        return c;
    }

    static void SetPath(PolygonCollider2D c, Vector2[] path)
    {
        c.pathCount = 1;
        c.SetPath(0, path);
        PathWrites++;
    }

    // A shield source coming up or going down; the zone follows at once.
    public void SetShield(Source source, bool on)
    {
        sources = on ? sources | source : sources & ~source;
        Apply();
    }

    public static void SetShield(GameObject ship, Source source, bool on)
    {
        var hb = Of(ship);
        if (hb != null) hb.SetShield(source, on);
    }

    void Apply()
    {
        bool up = sources != Source.None;
        // Disable first, so the two are never on together.
        if (up) { hull.enabled = false; shield.enabled = true; }
        else { shield.enabled = false; hull.enabled = true; }
    }

    // ------------------------------------------------------------------
    // Pickups
    // ------------------------------------------------------------------

    void Update()
    {
        if (Time.deltaTime <= 0f || Time.timeScale <= 0f || buttonClicks.playerDied) return;
        CatchPickups();
    }

    // Collects every pickup within PickupRadius of the hull centre. Returns
    // how many it handed to collisionDetection.
    public int CatchPickups()
    {
        if (owner == null) owner = GetComponent<collisionDetection>();
        if (owner == null || !owner.enabled) return 0;
        if (!pickupFilter.useTriggers)
        {
            pickupFilter = new ContactFilter2D();
            pickupFilter.useTriggers = true;
        }
        int n = Physics2D.OverlapCircle(transform.position, Radius, pickupFilter, nearby);
        int caught = 0;
        for (int i = 0; i < n; i++)
        {
            var c = nearby[i];
            nearby[i] = null;
            if (c == null || !c.enabled || !c.CompareTag("pickUp")) continue;
            if (owner.CollectPickup(c)) caught++;
        }
        return caught;
    }
}
