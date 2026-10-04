using System.Collections.Generic;
using UnityEngine;

// Building blocks of WorldBackdrop. None of these read Time.* themselves:
// every update receives the frame's *scaled* delta from WorldBackdrop, so a
// paused game (timeScale 0) hands them dt = 0 and nothing moves, blinks or
// advances a flipbook. That is what makes the freeze a true still frame.

// Sprites cut from one world's atlas (a PNG plus the JSON rect manifest its
// art pipeline writes next to it). A sprite pivots on the middle of its rect.
public class BackdropAtlas
{
    [System.Serializable] class Rect { public string n; public int x, y, w, h; }
    [System.Serializable] class Manifest { public Rect[] sprites; }

    public const float PixelsPerUnit = 100f;

    public readonly Texture2D texture;
    readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

    public BackdropAtlas(Texture2D texture, TextAsset manifest)
    {
        this.texture = texture;
        if (texture == null || manifest == null) return;
        var m = JsonUtility.FromJson<Manifest>(manifest.text);
        if (m == null || m.sprites == null) return;
        foreach (var r in m.sprites)
        {
            var s = Sprite.Create(texture, new UnityEngine.Rect(r.x, r.y, r.w, r.h), new Vector2(0.5f, 0.5f),
                                  PixelsPerUnit, 0, SpriteMeshType.FullRect);
            s.name = r.n;
            sprites[r.n] = s;
        }
    }

    public bool Has(string name) { return sprites.ContainsKey(name); }

    public Sprite Get(string name)
    {
        Sprite s;
        return sprites.TryGetValue(name, out s) ? s : null;
    }

    // name_00, name_01, ... in order: a flipbook's frames, or a set of
    // variants to pick one from. Empty if missing.
    public Sprite[] Frames(string name)
    {
        var list = new List<Sprite>();
        for (int i = 0; ; i++)
        {
            var s = Get(name + "_" + i.ToString("00"));
            if (s == null) break;
            list.Add(s);
        }
        return list.ToArray();
    }

    public int Count { get { return sprites.Count; } }

    public void Destroy()
    {
        foreach (var s in sprites.Values) if (s != null) Kill(s);
        sprites.Clear();
    }

    // Destroy that also works from edit-mode tests.
    public static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o);
        else Object.DestroyImmediate(o);
    }
}

// A seamless vertical tile, leap-frogged: enough copies stacked to cover the
// view plus one, each wrapped back to the top as it leaves the bottom.
public class BackdropTile
{
    public readonly BackdropCatalog.Layer layer;
    public float offset;                 // integrated scroll, world units
    readonly Transform root;
    readonly List<SpriteRenderer> copies = new List<SpriteRenderer>();
    readonly Sprite sprite;
    float tileHeight, tileWidth, wobblePhase;

    public BackdropTile(Transform parent, BackdropCatalog.Layer layer, Sprite sprite, int order, float z)
    {
        this.layer = layer;
        this.sprite = sprite;
        root = new GameObject("Tile_" + layer.name).transform;
        root.SetParent(parent, false);
        root.localPosition = new Vector3(0f, 0f, z);
        for (int i = 0; i < 3; i++) AddCopy(order);
    }

    void AddCopy(int order)
    {
        var go = new GameObject("copy" + copies.Count);
        go.transform.SetParent(root, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        sr.color = layer.tint;
        copies.Add(sr);
    }

    // Tile width follows the view; a strip is a fixed fraction of the main
    // tile width (its pixels share the main tiles' scale).
    public void Layout(float viewWidth, float viewHeight, float mainTileWidth)
    {
        if (sprite == null) return;
        Vector2 size = sprite.bounds.size;
        float scale = layer.kind == BackdropCatalog.Kind.Strip
            ? mainTileWidth / 6f
            : viewWidth / size.x;
        tileWidth = size.x * scale;
        tileHeight = size.y * scale;
        int need = Mathf.CeilToInt(viewHeight / tileHeight) + 1;
        while (copies.Count < need) AddCopy(copies[0].sortingOrder);
        for (int i = 0; i < copies.Count; i++)
        {
            copies[i].transform.localScale = new Vector3(scale, scale, 1f);
            copies[i].enabled = i < need;
        }
    }

    public float TileHeight { get { return tileHeight; } }
    public float TileWidth { get { return tileWidth; } }

    public void Tick(float dt, float velocity, float viewHeight, float alpha)
    {
        offset += (velocity * layer.rate + layer.flow) * dt;
        if (tileHeight <= 0f) return;
        offset = Mathf.Repeat(offset, tileHeight);
        wobblePhase += dt * 7.3f;
        float x = layer.wobble > 0f ? Mathf.Sin(wobblePhase) * layer.wobble : 0f;

        // Copy i sits at bottom + i*h - offset, wrapping as offset grows.
        float bottom = -viewHeight * 0.5f - tileHeight * 0.5f;
        Color c = layer.tint;
        c.a *= alpha;
        for (int i = 0; i < copies.Count; i++)
        {
            if (!copies[i].enabled) continue;
            float y = bottom + i * tileHeight - offset + tileHeight;
            copies[i].transform.localPosition = new Vector3(x, y, 0f);
            copies[i].color = c;
        }
    }

    public int RendererCount { get { return copies.Count; } }
}

// One pooled set piece or particle. `body` is a child so a piece can spin or
// squash independently of its placement.
public class BackdropPiece
{
    public Transform root, body;
    public SpriteRenderer sr, blend;     // blend: next flipbook frame, faded in
    public bool active;
    public float x, y, vx, vy, age, life, size, phase, spin, rate;
    public Color color = Color.white;
    public Sprite[] frames;
    public float fps;
    public bool loop = true;
    public int kind, tier;               // tier: depth tier, where a director has them
    public BackdropPiece[] children;     // e.g. a planet's moon
    public BackdropPiece parent;         // set on a child that is placed by its parent

    public void Show(bool on)
    {
        active = on;
        if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
    }

    // Advance the flipbook. With `blend` set, the next frame cross-fades in
    // over the current one so slow rotations read smoothly.
    public void Animate()
    {
        if (frames == null || frames.Length == 0) return;
        float f = age * fps;
        int n = frames.Length;
        int i = loop ? (int)f % n : Mathf.Min((int)f, n - 1);
        sr.sprite = frames[i];
        if (blend != null)
        {
            blend.sprite = frames[(i + 1) % n];
            Color c = sr.color;
            c.a *= loop ? f - Mathf.Floor(f) : 0f;
            blend.color = c;
        }
    }

    public bool Finished { get { return !loop && frames != null && age * fps >= frames.Length; } }
}

// Fixed-capacity pool: Spawn() returns null when every piece is busy, so a
// long run can never grow it.
public class BackdropPool
{
    public readonly List<BackdropPiece> items = new List<BackdropPiece>();
    public readonly string name;

    public BackdropPool(Transform parent, string name, int capacity, int order, float z, bool blend = false)
    {
        this.name = name;
        var holder = new GameObject("Pool_" + name).transform;
        holder.SetParent(parent, false);
        holder.localPosition = new Vector3(0f, 0f, z);
        for (int i = 0; i < capacity; i++)
        {
            var p = new BackdropPiece();
            p.root = new GameObject(name + i).transform;
            p.root.SetParent(holder, false);
            p.body = new GameObject("body").transform;
            p.body.SetParent(p.root, false);
            p.sr = p.body.gameObject.AddComponent<SpriteRenderer>();
            p.sr.sortingOrder = order;
            if (blend)
            {
                var b = new GameObject("blend");
                b.transform.SetParent(p.body, false);
                p.blend = b.AddComponent<SpriteRenderer>();
                p.blend.sortingOrder = order + 1;
            }
            p.Show(false);
            items.Add(p);
        }
    }

    public int Capacity { get { return items.Count; } }

    public int ActiveCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < items.Count; i++) if (items[i].active) n++;
            return n;
        }
    }

    public BackdropPiece Spawn()
    {
        for (int i = 0; i < items.Count; i++)
        {
            var p = items[i];
            if (p.active) continue;
            p.age = 0f;
            p.spin = 0f;
            p.vx = p.vy = 0f;
            p.frames = null;
            p.loop = true;
            p.children = null;
            p.parent = null;
            p.body.localRotation = Quaternion.identity;
            p.body.localScale = Vector3.one;
            p.root.localRotation = Quaternion.identity;
            p.root.localScale = Vector3.one;
            p.Show(true);
            return p;
        }
        return null;
    }
}
