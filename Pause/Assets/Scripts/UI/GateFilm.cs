using UnityEngine;

// The approved HapticGate gate-opening film (brand job anim_v2), played back
// frame by frame on one SpriteRenderer. Used by the splash (splashScene); any
// other screen can play it the same way (see the API below).
//
// The film (30 fps): 0 - 1.13 s the closed steel H-gate in charcoal smoke
// (static); 1.133 s the leaves are forced apart, smoke and steam spill out,
// warm haze in the gap; from 2.2 s the "HapticGate" wordmark appears small,
// deep in the gap, rushes out of the door (2.57 s) and lands at the bottom
// (3.30 s); frame 100 (3.333 s) is the final frame, held.
//
// Art: Resources/HapticGate/Film/gate_film_NNN.jpg, NNN = film frame, one file
// per KEPT frame (frames 1..33 equal 0, and in the near-still 58..73 only every
// second one is stored). 810x828, the 1080x1104 band of the 1080x1920 portrait
// film that holds every lit pixel, scaled 0.75. Built by
// Art/HapticGate~/build_gate_film.py; importer: GateFilmImporter (no mips,
// clamp, bilinear, ASTC 6x6 on Android / iOS via AndroidTextureDiet).
//
// Loading never blocks: Begin() loads frame 0 (one ~300 KB texture), then the
// final frame and the rest stream in with Resources.LoadAsync, a few at a time,
// in film order, during the 1.1 s the closed gate stands still. If a frame is
// not in yet, the newest loaded frame at or before it stays on screen. Frames
// behind the playhead are released as it passes them (the final frame stays).
// In edit mode (tests, previews) everything loads synchronously.
//
// API (all film-local; the film is 1 world unit wide on its own transform,
// Aspect tall, centred on it; scale the transform to size it):
//   var film = new GateFilm(parent, sortingOrder);   // creates the "Gate film" child
//   film.Begin();             // false if the art is missing (show a fallback)
//   film.Show(seconds);       // call every frame with the film clock (pumps loading)
//   film.ShowEnd();           // jump to the final frame (it is loaded first)
//   film.Release();           // in OnDestroy: destroys sprites, unloads textures
//   GateFilm.FrameAt(t), KeyFor(frame), OpenAt(t), the geometry constants.
public sealed class GateFilm
{
    public const string Folder = "HapticGate/Film/";
    public const string Prefix = "gate_film_";
    public const float Fps = 30f;
    public const int LastFrame = 100;                    // the final frame, held after Length
    public const float Length = LastFrame / Fps;         // 3.333 s
    public const int Width = 810, Height = 828;          // imported frame size
    public const float Aspect = (float)Height / Width;   // film height in film widths

    // Film moments (seconds), from the brand manifest.
    public const float FirstSplit = 34 / Fps;            // 1.133: the leaves start to part
    public const float OpenSettled = 57 / Fps;           // 1.90: leaves fully open
    public const float WordmarkAppears = 66 / Fps;       // 2.20: tiny, deep in the gap
    public const float WordmarkExits = 77 / Fps;         // 2.567: out of the door
    public const float WordmarkLands = 99 / Fps;         // 3.30: landed (after its bounce)

    // Geometry in film widths, origin at the film's centre, y up (measured on
    // the frames: 1 film width = 1080 source pixels).
    public const float GateCentreY = 0.141f;             // gate centre above the film centre
    public const float GateHeight = 0.612f;
    public const float LeafWidth = 0.306f;
    public const float LeafClosedX = 0.154f;             // leaf centre |x|, shut
    public const float LeafOpenX = 0.278f;               // leaf centre |x|, open
    public const float WordmarkCentreY = -0.397f;        // the landed wordmark's centre
    public const float WordmarkWidth = 0.70f;            // its letters, glow excluded

    // Film frames stored as files (ascending; must match build_gate_film.py).
    public static readonly int[] Kept = BuildKept();

    static int[] BuildKept()
    {
        var list = new System.Collections.Generic.List<int> { 0 };
        for (int f = 34; f < 58; f++) list.Add(f);
        for (int f = 58; f < 74; f += 2) list.Add(f);
        for (int f = 74; f <= LastFrame; f++) list.Add(f);
        return list.ToArray();
    }

    public static string PathFor(int frame) { return Folder + Prefix + frame.ToString("000"); }

    // Film frame shown at t seconds (clamped to the held final frame).
    public static int FrameAt(float t)
    {
        if (t <= 0f) return 0;
        return Mathf.Clamp(Mathf.FloorToInt(t * Fps + 0.01f), 0, LastFrame);   // 0.01 frame: float drift of a summed clock
    }

    // Index into Kept of the stored frame that stands for film frame f.
    public static int KeyFor(int frame)
    {
        int lo = 0, hi = Kept.Length - 1;
        if (frame >= Kept[hi]) return hi;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (Kept[mid] <= frame) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    // How far the leaves are open at t (0 shut .. 1 open), for effects placed on them.
    public static float OpenAt(float t)
    {
        float k = Mathf.Clamp01((t - FirstSplit) / (OpenSettled - FirstSplit));
        return 1f - (1f - k) * (1f - k);
    }

    // Leaf centre x (film widths) for side -1 (left) / +1 (right) at open fraction o.
    public static float LeafCentreX(float side, float open)
    {
        return side * Mathf.Lerp(LeafClosedX, LeafOpenX, Mathf.Clamp01(open));
    }

    // ---- instance ---------------------------------------------------------

    // How many async loads may be in flight, and how far ahead (in kept frames) to stream.
    public const int MaxInFlight = 3;
    public const int Lookahead = 24;

    public readonly Transform transform;
    public readonly SpriteRenderer renderer;
    public int ShownFrame { get; private set; }          // film frame on screen (-1 none)
    public int ShownKey { get; private set; }
    public bool HasArt { get; private set; }

    readonly Texture2D[] tex = new Texture2D[Kept.Length];
    readonly Sprite[] sprites = new Sprite[Kept.Length];
    readonly ResourceRequest[] pending = new ResourceRequest[Kept.Length];
    readonly bool[] missing = new bool[Kept.Length];
    int inFlight;
    int nextToQueue = 1;
    int releasedBelow;

    public GateFilm(Transform parent, int sortingOrder)
    {
        var go = new GameObject("Gate film");
        transform = go.transform;
        transform.SetParent(parent, false);
        renderer = go.AddComponent<SpriteRenderer>();
        renderer.sortingOrder = sortingOrder;
        renderer.enabled = false;
        ShownFrame = -1; ShownKey = -1;
    }

    public int LoadedCount
    {
        get { int n = 0; for (int i = 0; i < sprites.Length; i++) if (sprites[i] != null) n++; return n; }
    }

    public bool IsLoaded(int key) { return key >= 0 && key < sprites.Length && sprites[key] != null; }

    // Loads frame 0 now (and, outside play mode, everything). False if the art is missing.
    public bool Begin()
    {
        HasArt = LoadNow(0);
        if (!HasArt) return false;
        if (!Application.isPlaying) { for (int i = 1; i < Kept.Length; i++) LoadNow(i); }
        else Queue(Kept.Length - 1);   // the final frame first: a smash jumps straight to it
        Show(0f);
        return true;
    }

    public void ShowEnd() { Show(Length); }

    public void Show(float t)
    {
        if (!HasArt) return;
        Pump();
        int frame = FrameAt(t);
        int key = KeyFor(frame);
        // the newest loaded frame at or before the wanted one
        int k = key;
        while (k > 0 && sprites[k] == null) k--;
        if (sprites[k] == null) return;
        if (k != ShownKey)
        {
            renderer.sprite = sprites[k];
            ShownKey = k;
        }
        ShownFrame = Kept[k];
        if (!renderer.enabled) renderer.enabled = true;
        ReleaseBehind(k);
    }

    bool LoadNow(int key)
    {
        if (sprites[key] != null) return true;
        if (missing[key]) return false;
        var t = Resources.Load<Texture2D>(PathFor(Kept[key]));
        return Adopt(key, t);
    }

    bool Adopt(int key, Texture2D t)
    {
        if (t == null) { missing[key] = true; return false; }
        tex[key] = t;
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        sprites[key] = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), t.width,
                                     0, SpriteMeshType.FullRect);
        sprites[key].name = "gate_film_" + Kept[key];
        return true;
    }

    void Queue(int key)
    {
        if (key <= 0 || key >= Kept.Length || sprites[key] != null || pending[key] != null || missing[key]) return;
        pending[key] = Resources.LoadAsync<Texture2D>(PathFor(Kept[key]));
        inFlight++;
    }

    // Collects finished loads and keeps up to MaxInFlight going, in film order.
    void Pump()
    {
        if (!Application.isPlaying) return;
        for (int i = 0; i < pending.Length; i++)
        {
            var r = pending[i];
            if (r == null || !r.isDone) continue;
            pending[i] = null; inFlight--;
            if (i < releasedBelow && i != Kept.Length - 1) { if (r.asset != null) Resources.UnloadAsset(r.asset); continue; }
            Adopt(i, r.asset as Texture2D);
        }
        int limit = Mathf.Min(Kept.Length - 1, Mathf.Max(ShownKey, 0) + Lookahead);
        while (inFlight < MaxInFlight && nextToQueue <= limit)
        {
            if (nextToQueue >= releasedBelow) Queue(nextToQueue);
            nextToQueue++;
        }
    }

    // Frees frames the playhead has passed (never the final frame, never the shown one).
    void ReleaseBehind(int shown)
    {
        if (!Application.isPlaying) return;
        while (releasedBelow < shown)
        {
            int i = releasedBelow++;
            if (i == Kept.Length - 1) continue;
            Free(i);
        }
    }

    void Free(int i)
    {
        if (sprites[i] != null) { Object.Destroy(sprites[i]); sprites[i] = null; }
        if (tex[i] != null) { Resources.UnloadAsset(tex[i]); tex[i] = null; }
    }

    public void Release()
    {
        for (int i = 0; i < sprites.Length; i++)
        {
            if (sprites[i] != null)
            {
                if (Application.isPlaying) Object.Destroy(sprites[i]); else Object.DestroyImmediate(sprites[i]);
                sprites[i] = null;
            }
            // Edit mode: the texture is the project asset itself; never unload/destroy it there.
            if (tex[i] != null && Application.isPlaying) Resources.UnloadAsset(tex[i]);
            tex[i] = null;
            pending[i] = null;
        }
        inFlight = 0;
        if (renderer != null) renderer.sprite = null;
    }
}
