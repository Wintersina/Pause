using UnityEngine;

// The kill cam's footage: what the board drew over the last couple of seconds
// before the fatal hit, frame by frame, as plain draw state -- never a
// re-simulation. KillCamRecorder fills one live from the running board;
// KillCamArchive rebuilds one from the saved high-score death. KillCamStage
// draws either at any moment of its clock (interpolated between frames, so
// the very slow motion stays smooth); KillCam / CodexReplayViewer drive it.
//
// A frame is the camera's position and a run of entries, one per sprite on
// screen: its sprite, material, world position, the 2x2 linear part of its
// world matrix (rotation, scale and mirroring in one), colour, sorting and
// flips. `track` names the same drawn thing from frame to frame (the live
// renderer's instance id), which is what lets two frames be blended. The
// rails and the other scrolling quads (MeshRenderers whose look is a material
// offset) are mesh entries: just their offset.
//
// Entries live in one flat array; a frame's run may wrap round its end (the
// recorder's ring), so read them with Entry(frame, i). Nothing here
// allocates after construction.
public struct KillCamEntry
{
    public const byte FlipX = 1, FlipY = 2;
    public const int DrawModeShift = 2;   // two bits: SpriteDrawMode

    public int track;
    public Sprite sprite;
    public Material material;        // what the renderer drew with (null: the default sprite material)
    public SpriteRenderer source;    // live footage only: its property block is copied when replayed
    public Vector3 pos;
    public float m00, m01, m10, m11; // world matrix, 2x2 linear part (columns: the sprite's x and y axes)
    public Color32 color;
    public int layer;                // sortingLayerID
    public short order;
    public byte flags;
    public Vector2 size;             // tiled / sliced sprites only

    public SpriteDrawMode DrawMode { get { return (SpriteDrawMode)((flags >> DrawModeShift) & 3); } }
}

public struct KillCamMeshEntry
{
    public int track;                // live: the renderer's instance id; archived: index into KillCamClip.meshTracks
    public Renderer renderer;        // live footage: the scene's renderer; archived: null
    public Vector2 offset;           // its material's main texture offset this frame
}

public sealed class KillCamClip
{
    // ---- playback timing ----
    // The live kill cam shows this much of the run before the fatal hit (game seconds).
    public const float LiveWindow = 1.3f;
    // ... and this much of the death after it (the crash's clock: the hit flash, the
    // hull breaking up, the blasts, the killer's tumble). The crash holds for the
    // replay once this much of it has played (DeathCrash), so the tape ends there.
    public const float DeathTail = 1f;
    // Clock rate (game seconds per real second): LeadRate early on, easing down to
    // SlowRate over the last RampSeconds before the hit -- very slow motion at the
    // collision -- then, after it, back up to TailRate over TailRampSeconds while
    // the ship breaks up.
    public const float LeadRate = .7f, SlowRate = .18f, RampSeconds = .7f;
    public const float TailRate = .6f, TailRampSeconds = .45f;
    // The last frame holds this long (real seconds) before the replay ends.
    public const float HoldEnd = .3f;

    // How fast the replay clock runs `toImpact` game seconds before the hit
    // (negative: that long after it, the death).
    public static float RateAt(float toImpact)
    {
        if (toImpact < 0f)
            return Mathf.Lerp(SlowRate, TailRate, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(-toImpact / TailRampSeconds)));
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(toImpact / RampSeconds));
        return Mathf.Lerp(SlowRate, LeadRate, k);
    }

    // Real seconds a replay of `window` game seconds before the hit and `tail`
    // after it takes (with the hold), at `step` real seconds a frame.
    public static float RealSeconds(float window, float tail = 0f, float step = 1f / 60f)
    {
        float t = 0f, real = 0f, end = window + Mathf.Max(0f, tail);
        int guard = 0;
        while (t < end && guard++ < 100000)
        {
            t += step * RateAt(window - t);
            real += step;
        }
        return real + HoldEnd;
    }

    // The fatal hit on this clip's clock (-1: not marked -- the clip ends on it).
    public float impact = -1f;
    public float ImpactTime { get { return impact >= 0f ? Mathf.Min(impact, EndTime) : EndTime; } }
    public bool HasTail { get { return impact >= 0f && impact < EndTime - 1e-4f; } }
    // The replay clock's rate at clip time `t`.
    public float Rate(float t) { return RateAt(ImpactTime - t); }

    // ---- frames ----
    public int frameCount;
    public float[] time;             // clip clock (game seconds), ascending
    public Vector2[] cam;            // camera position (x, y)
    public int[] first, count;       // each frame's run in entries
    public int[] meshFirst, meshCount;
    public KillCamEntry[] entries;
    public KillCamMeshEntry[] meshEntries;

    // ---- the view ----
    public float camHalfHeight = 6.6f;
    public float camAspect = 9f / 16f;
    public float camZ = -10f;
    public Color background = Color.black;

    // ---- archived clips only ----
    public KillCamMeshTrack[] meshTracks;

    public KillCamClip(int frames, int entryCapacity, int meshCapacity)
    {
        time = new float[frames];
        cam = new Vector2[frames];
        first = new int[frames];
        count = new int[frames];
        meshFirst = new int[frames];
        meshCount = new int[frames];
        entries = new KillCamEntry[Mathf.Max(1, entryCapacity)];
        meshEntries = new KillCamMeshEntry[Mathf.Max(1, meshCapacity)];
    }

    public int Capacity { get { return time.Length; } }
    public bool Playable { get { return frameCount >= 2 && Duration > 0f; } }
    public float StartTime { get { return frameCount > 0 ? time[0] : 0f; } }
    public float EndTime { get { return frameCount > 0 ? time[frameCount - 1] : 0f; } }
    public float Duration { get { return EndTime - StartTime; } }

    public KillCamEntry Entry(int frame, int i) { return entries[(first[frame] + i) % entries.Length]; }
    public KillCamMeshEntry MeshEntry(int frame, int i) { return meshEntries[(meshFirst[frame] + i) % meshEntries.Length]; }

    // The frame at clip time `t` and how far (0..1) toward the next one it is.
    public int FrameAt(float t, out float alpha)
    {
        alpha = 0f;
        if (frameCount <= 0) return -1;
        if (t <= time[0]) return 0;
        if (t >= time[frameCount - 1]) return frameCount - 1;
        int lo = 0, hi = frameCount - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (time[mid] <= t) lo = mid; else hi = mid;
        }
        float span = time[hi] - time[lo];
        alpha = span > 1e-6f ? Mathf.Clamp01((t - time[lo]) / span) : 0f;
        return lo;
    }

    // The entry of frame `next` drawing `track`, searched from `cursor` (entries keep
    // their order between frames, so this is nearly always the first look). -1 when
    // that track is not drawn in `next`.
    public int Match(int next, int track, ref int cursor)
    {
        int n = count[next];
        if (n == 0) return -1;
        int look = Mathf.Min(n, 12);
        for (int k = 0; k < look; k++)
        {
            int j = (cursor + k) % n;
            if (Entry(next, j).track == track) { cursor = j + 1; return j; }
        }
        // Not near: a burst of new sprites (a barrage, a blast) pushed the rest
        // further along. Look on (FarLook), so everything after the burst still
        // blends instead of stepping. (Bounded: a sprite that is gone costs at
        // most FarLook compares, not a scan of the whole frame.)
        int far = Mathf.Min(n, FarLook);
        for (int k = look; k < far; k++)
        {
            int j = (cursor + k) % n;
            if (Entry(next, j).track == track) { cursor = j + 1; return j; }
        }
        return -1;
    }

    // A blended pair is the same thing moving, not a pooled renderer that went
    // out and was handed back elsewhere between two frames (a shot, a blast):
    // that would sweep across the screen. Further than this (world units) in
    // one recorded frame is a respawn, never motion on this board.
    public const float MaxBlendJump = 2.5f;
    public const int FarLook = 320;
    public static bool Continuous(in KillCamEntry a, in KillCamEntry b)
    {
        float dx = b.pos.x - a.pos.x, dy = b.pos.y - a.pos.y;
        return dx * dx + dy * dy <= MaxBlendJump * MaxBlendJump;
    }

    // Same for a mesh entry (there are only a handful: a straight scan).
    public int MatchMesh(int next, int track)
    {
        for (int j = 0; j < meshCount[next]; j++) if (MeshEntry(next, j).track == track) return j;
        return -1;
    }
}

// An archived scrolling quad (a rail): drawn by the replay as a quad with its
// own material (the rails' WorldRailRepeat, its settings copied) and texture.
public sealed class KillCamMeshTrack
{
    public Vector3 center;
    public Vector2 size;
    public int layer;
    public short order;
    public Texture2D texture;
    public Material material;
    public Vector2 textureScale = Vector2.one;
}
