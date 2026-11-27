using System.Collections.Generic;
using UnityEngine;

// Where a flipbook's frames sit in its sheet: row-major from the top-left, frame 1 top-left. The layout is
// DERIVED from the texture's size, so a re-rendered sheet at another resolution (or another packing of the same
// frames: a strip, a grid, a square atlas with unused cells) is a drop-in with no code change.
public static class FlipbookGrid
{
    public struct Layout
    {
        public int cols, rows, frames;
        public float frameW, frameH;
        public bool Valid { get { return cols > 0 && rows > 0 && frameW > 0f; } }
    }

    // `aspect`: a frame's height over its width (1 for a square frame, 1024 / 576 for the warp's portrait strip).
    // The smallest column count whose cells have that aspect and hold `frames` cells.
    public static Layout Find(int texW, int texH, int frames, float aspect = 1f)
    {
        for (int cols = 1; cols <= frames; cols++)
        {
            if (texW % cols != 0) continue;
            float fw = texW / (float)cols, fh = fw * aspect;
            float rows = texH / fh;
            int r = Mathf.RoundToInt(rows);
            if (r < 1 || Mathf.Abs(rows - r) > .02f || cols * r < frames) continue;
            return new Layout { cols = cols, rows = r, frames = frames, frameW = fw, frameH = texH / (float)r };
        }
        return new Layout();
    }

    // One whole-rect sprite per frame (FullRect: never a tight mesh), `ppuOfWidth` pixels per unit = the frame's
    // width, so a sprite is ONE world unit wide and a renderer's scale is its width in units.
    public static Sprite[] Cut(Texture2D tex, Layout l, string name)
    {
        var list = new Sprite[l.frames];
        for (int i = 0; i < l.frames; i++)
        {
            int col = i % l.cols, row = i / l.cols;
            var rect = new Rect(col * l.frameW, tex.height - (row + 1) * l.frameH, l.frameW, l.frameH);
            var s = Sprite.Create(tex, rect, new Vector2(.5f, .5f), l.frameW, 0, SpriteMeshType.FullRect);
            s.name = name + "_" + i.ToString("00");
            list[i] = s;
        }
        return list;
    }
}

// The destination planets' rotation sheets: per world, 16 frames of one full turn in a 4x4 atlas
// (Worlds/Dock/<world>_rot_bare, frame 1 top-left, row-major). The FINAL art is 4096x4096 (1024 px frames); the
// first drop is 2048x2048 (512 px frames, repacked from Codex's 8192x512 strips): the loader reads the frame size
// from the texture, so replacing a file with the hi-res one needs nothing else.
//
// MEMORY. A sheet is a 4096 ASTC texture (7.5 MB at 6x6 on phones, 16 MB DXT5 on the Mac), so at most MaxLoaded (2)
// are ever resident: the one on show and the one warping in. Users Acquire / Request a world and Release it; the
// last release cuts the sprites loose and unloads the texture. Nothing here allocates per frame.
//
// Users: the Space Dock's destination planet (DockPlanet), the Space backdrop's next-world planet after a boss
// (SpaceDirector) and the Frost planetfall (Planetfall).
public static class PlanetFlipbooks
{
    public const int FrameCount = 16, MaxLoaded = 2;
    public const string Folder = "Worlds/Dock/";
    // The drawn disc's share of a frame's width (the art leaves a clear border; measured on the first drop: .92 at
    // 512 px, .91 at 1024 px).
    public const float DiscShare = .91f;
    // Seconds for one whole turn: calm but clearly turning (the frames are cross-faded smoothly, never stepped).
    public static float TurnSeconds = 20f;
    // The worlds whose planetfall approach draws the rotation (instead of its own static planet art).
    public static readonly bool[] PlanetfallRotates = { false, true, true, true, true, true, true };

    // File stem by WorldManager index (0 = Space has no planet).
    static readonly string[] Names = { null, "frost", "verdant", "ember", "tide", "storm", "desert" };

    public static string PathOf(int world)
    {
        return world >= 1 && world < Names.Length ? Folder + Names[world] + "_rot_bare" : null;
    }

    sealed class Entry
    {
        public int world, refs;
        public Texture2D tex;
        public ResourceRequest req;
        public Sprite[] sprites;
    }

    static readonly Entry[] entries = new Entry[Names.Length];
    static readonly List<ResourceRequest> dropping = new List<ResourceRequest>();
    static readonly HashSet<int> missing = new HashSet<int>();

    // Resident or loading textures (a request being dropped still counts until it lands).
    public static int LoadedCount
    {
        get
        {
            int n = dropping.Count;
            for (int i = 0; i < entries.Length; i++) if (entries[i] != null) n++;
            return n;
        }
    }

    public static bool Has(int world) { return world >= 1 && world < Names.Length && entries[world] != null && entries[world].refs > 0; }
    public static int Refs(int world) { return Has(world) ? entries[world].refs : 0; }
    public static bool Missing(int world) { return missing.Contains(world); }

    // The art can be used without a stall: resident, or already preloaded (WorldArtPreload).
    public static bool Ready(int world)
    {
        if (world < 1 || world >= Names.Length || missing.Contains(world)) return false;
        var e = entries[world];
        if (e != null) { Finish(e); return e.sprites != null; }
        return WorldArtPreload.Holds(PathOf(world), typeof(Texture2D));
    }

    // Takes a reference and starts loading (asynchronously while playing, at once in edit mode / tests, or a cache
    // hit of the preload). True when the world has art.
    public static bool Request(int world)
    {
        if (world < 1 || world >= Names.Length || missing.Contains(world)) return false;
        var e = entries[world];
        if (e == null)
        {
            e = entries[world] = new Entry { world = world };
            string path = PathOf(world);
            var held = WorldArtPreload.Holds(path, typeof(Texture2D)) ? WorldArtPreload.Load<Texture2D>(path) : null;
            if (held != null) e.tex = held;
            else if (Application.isPlaying) e.req = Resources.LoadAsync<Texture2D>(path);
            else e.tex = Resources.Load<Texture2D>(path);
            Finish(e);
            if (e.req == null && e.tex == null) { entries[world] = null; missing.Add(world); return false; }
        }
        e.refs++;
        return true;
    }

    // The frames when the texture has arrived, else null (call it each frame while waiting).
    public static Sprite[] Frames(int world)
    {
        if (world < 1 || world >= Names.Length) return null;
        var e = entries[world];
        if (e == null || e.refs <= 0) return null;
        Finish(e);
        return e.sprites;
    }

    // Request + a synchronous wait (Resources.Load on a request in flight).
    public static Sprite[] Acquire(int world)
    {
        if (!Request(world)) return null;
        var e = entries[world];
        if (e.sprites == null && e.tex == null)
        {
            // an async load in flight: the synchronous load returns the same asset
            e.tex = WorldArtPreload.Load<Texture2D>(PathOf(world));   // the held preload when there is one
            e.req = null;
        }
        Finish(e);
        if (e.sprites == null) { Release(world); missing.Add(world); return null; }
        return e.sprites;
    }

    static void Finish(Entry e)
    {
        if (e.sprites != null) return;
        if (e.tex == null && e.req != null && e.req.isDone)
        {
            e.tex = e.req.asset as Texture2D;
            e.req = null;
            if (e.tex == null) { missing.Add(e.world); return; }
        }
        if (e.tex == null) return;
        var l = FlipbookGrid.Find(e.tex.width, e.tex.height, FrameCount);
        if (!l.Valid) { Debug.LogWarning("[PlanetFlipbooks] " + e.tex.name + " is not a 4x4 square-frame atlas"); return; }
        e.sprites = FlipbookGrid.Cut(e.tex, l, Names[e.world] + "_rot");
    }

    // Gives a reference back; the last one frees the sprites and unloads the texture.
    public static void Release(int world)
    {
        if (world < 1 || world >= Names.Length) return;
        var e = entries[world];
        if (e == null) return;
        if (--e.refs > 0) return;
        entries[world] = null;
        Free(e);
    }

    static void Free(Entry e)
    {
        if (e.sprites != null) foreach (var s in e.sprites) if (s != null) BossUtil.Kill(s);
        if (e.tex != null) Resources.UnloadAsset(e.tex);
        else if (e.req != null)
        {
            if (e.req.isDone) { if (e.req.asset != null) Resources.UnloadAsset(e.req.asset); }
            else dropping.Add(e.req);   // cannot be cancelled: unloaded when it lands
        }
    }

    // Lets landed, dropped requests go (called each frame by whoever loads: DockPlanet).
    public static void Pump()
    {
        for (int i = dropping.Count - 1; i >= 0; i--)
        {
            var r = dropping[i];
            if (r != null && !r.isDone) continue;
            if (r != null && r.asset != null) Resources.UnloadAsset(r.asset);
            dropping.RemoveAt(i);
        }
    }

    // Tests: forget everything (and free it).
    public static void ResetForTests()
    {
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i] == null) continue;
            var e = entries[i];
            entries[i] = null;
            Free(e);
        }
        dropping.Clear();
        missing.Clear();
    }

    // The frame (and the next one, and how far between them) a turn clock `seconds` shows.
    public static int FrameAt(float seconds, out int next, out float blend)
    {
        float f = Mathf.Repeat(seconds / Mathf.Max(1f, TurnSeconds), 1f) * FrameCount;
        int i = Mathf.Min(FrameCount - 1, (int)f);
        next = (i + 1) % FrameCount;
        blend = f - i;
        return i;
    }
}
