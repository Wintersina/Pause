using System.Collections.Generic;
using UnityEngine;

// Thin lines BETWEEN things for three player attacks (Paranoid's Arcane Eye tethers, Saboteur's mine wires, Jade
// Phantom's ice cracks). Immediate mode: the attack code calls Draw / Link each frame for what is linked now, and
// EndFrame (called by the runner at the end of its Step) hides the pooled renderers nobody drew. Timed one-offs
// (a wire burning, a tether snapping, a crack running) live in a small fixed array. Never a loop, a ring or a box:
// every line has two distinct ends, and its ends sit on the objects it joins, never around them.
public sealed class ShipLinkLines
{
    public const int MaxLines = 24;      // pooled LineRenderers: the hard cap on what is on screen at once
    const int MaxTimed = 16;
    const int SortOrder = 70;            // above the enemies, under the hit bursts (72)
    const float FlickerHz = 14f;

    public enum Kind { Tether, Snap, Wire, Burn, Ice, Crack }

    public struct LinkInfo
    {
        public Vector3 a, b;
        public Kind kind;
    }

    struct Timed
    {
        public bool on;
        public Kind kind;
        public Vector3 a, b;
        public float age, dur;
        public int seed;
    }

    readonly LineRenderer[] pool = new LineRenderer[MaxLines];
    readonly Timed[] timed = new Timed[MaxTimed];
    List<LinkInfo> drawing = new List<LinkInfo>(MaxLines);
    List<LinkInfo> last = new List<LinkInfo>(MaxLines);
    readonly Vector3[] pts = new Vector3[12];
    GameObject root;
    Material mat;
    Transform parent;
    int used;
    float clock;

    // The links drawn during the last finished frame (tests read these).
    public IList<LinkInfo> Last { get { return last; } }
    public int LastCount { get { return last.Count; } }
    public int CountOf(Kind k) { int n = 0; for (int i = 0; i < last.Count; i++) if (last[i].kind == k) n++; return n; }
    public int TimedLive { get { int n = 0; for (int i = 0; i < MaxTimed; i++) if (timed[i].on) n++; return n; } }

    public ShipLinkLines(Transform under) { parent = under; }

    // ---- what the three attacks colour their lines with: the ship's own palette -----------------------------------
    // Paranoid: its hull red (WeaponStyleTable Kaneda). Saboteur: red hull, sodium orange wire. Jade Phantom: ice.
    public static readonly Color TetherColor = new Color(.85f, .14f, .17f, .8f);
    public static readonly Color WireColor = new Color(.95f, .53f, .17f, .38f);
    public static readonly Color FuseColor = new Color(1f, .9f, .55f, 1f);
    public static readonly Color IceColor = new Color(.7f, .97f, 1f, .8f);
    public static readonly Color CrackColor = new Color(.92f, 1f, 1f, 1f);

    public const float TetherWidth = .02f, WireWidth = .014f, FuseWidth = .03f, IceWidth = .022f, CrackWidth = .034f;

    // ------------------------------------------------------------------------------------------------ immediate
    // Draws a jagged line a to b this frame, its ends pulled in by ra / rb (the objects' own sizes) so the line
    // joins them without crossing their bodies. False when the pool is spent (the cap).
    public bool Link(Kind kind, Vector3 a, Vector3 b, float ra, float rb, float width, Color c, float jag, int seed)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len < .05f) return false;
        Vector3 dir = d / len;
        float cut = Mathf.Min(ra + rb, len * .5f - .01f);
        float ta = Mathf.Min(ra, cut), tb = Mathf.Min(rb, cut);
        if (len - ta - tb < .04f) return false;
        return Draw(kind, a + dir * ta, b - dir * tb, width, c, jag, seed);
    }

    public bool Draw(Kind kind, Vector3 a, Vector3 b, float width, Color c, float jag, int seed)
    {
        if (used >= MaxLines) return false;
        float len = Vector3.Distance(a, b);
        if (len < .02f) return false;
        var lr = Get(used);
        used++;
        int n = Mathf.Clamp(Mathf.CeilToInt(len / .3f), 2, pts.Length - 1) + 1;
        lr.positionCount = n;
        Vector3 dir = (b - a) / len;
        Vector3 side = new Vector3(-dir.y, dir.x, 0f);
        int step = (int)(clock * FlickerHz);
        for (int k = 0; k < n; k++)
        {
            float f = k / (float)(n - 1);
            Vector3 p = Vector3.Lerp(a, b, f);
            if (k > 0 && k < n - 1) p += side * (H(seed + k * 31, step) * jag);
            pts[k] = p;
        }
        lr.SetPositions(pts);
        lr.startWidth = lr.endWidth = width;
        Color col = c;
        col.a *= .6f + .4f * (H(seed, step + 7) * .5f + .5f);   // the flicker: crisp on/off in alpha, no glow
        lr.startColor = lr.endColor = col;
        lr.enabled = true;
        drawing.Add(new LinkInfo { a = a, b = b, kind = kind });
        return true;
    }

    // ---------------------------------------------------------------------------------------------- timed one-offs
    public void Burn(Vector3 a, Vector3 b, float seconds) { Add(Kind.Burn, a, b, seconds); }
    public void Snap(Vector3 a, Vector3 b, float seconds) { Add(Kind.Snap, a, b, seconds); }
    public void Crack(Vector3 a, Vector3 b, float seconds) { Add(Kind.Crack, a, b, seconds); }

    void Add(Kind kind, Vector3 a, Vector3 b, float seconds)
    {
        for (int i = 0; i < MaxTimed; i++)
        {
            if (timed[i].on) continue;
            timed[i] = new Timed { on = true, kind = kind, a = a, b = b, age = 0f, dur = Mathf.Max(.02f, seconds), seed = i * 97 + 13 };
            return;
        }
    }

    // End of the runner's frame: advance and draw the timed lines, hide what nobody drew.
    public void EndFrame(float dt)
    {
        clock += dt;
        for (int i = 0; i < MaxTimed; i++)
        {
            if (!timed[i].on) continue;
            timed[i].age += dt;
            float p = timed[i].age / timed[i].dur;
            if (p >= 1f) { timed[i].on = false; continue; }
            DrawTimed(timed[i], p);
        }
        for (int i = used; i < MaxLines; i++) if (pool[i] != null && pool[i].enabled) pool[i].enabled = false;
        var swap = last; last = drawing; drawing = swap;
        drawing.Clear();
        used = 0;
    }

    void DrawTimed(Timed t, float p)
    {
        switch (t.kind)
        {
            case Kind.Burn:
                {
                    // the fuse front runs a to b; ahead of it the wire is still there, faint; behind it, gone
                    Vector3 front = Vector3.Lerp(t.a, t.b, p);
                    Vector3 tail = Vector3.Lerp(t.a, t.b, Mathf.Max(0f, p - .35f));
                    Draw(Kind.Wire, front, t.b, WireWidth, WireColor, .05f, t.seed);
                    Draw(Kind.Burn, tail, front, FuseWidth, FuseColor, .05f, t.seed + 1);
                    break;
                }
            case Kind.Snap:
                {
                    // the tether parts in the middle and both halves recoil, fading
                    Color c = TetherColor; c.a *= 1f - p;
                    Draw(Kind.Snap, t.a, Vector3.Lerp(t.a, t.b, .5f * (1f - p)), TetherWidth, c, .12f, t.seed);
                    Draw(Kind.Snap, t.b, Vector3.Lerp(t.b, t.a, .5f * (1f - p)), TetherWidth, c, .12f, t.seed + 1);
                    break;
                }
            case Kind.Crack:
                {
                    // the ice crack runs a to b, then fades
                    Color c = CrackColor; c.a *= 1f - p * p;
                    Draw(Kind.Crack, t.a, Vector3.Lerp(t.a, t.b, Mathf.Min(1f, p * 2f + .1f)), CrackWidth, c, .16f, t.seed);
                    break;
                }
        }
    }

    public void Clear()
    {
        for (int i = 0; i < MaxTimed; i++) timed[i].on = false;
        for (int i = 0; i < MaxLines; i++) if (pool[i] != null) pool[i].enabled = false;
        drawing.Clear();
        last.Clear();
        used = 0;
    }

    public void Dispose()
    {
        Clear();
        if (root != null) { if (Application.isPlaying) Object.Destroy(root); else Object.DestroyImmediate(root); }
        if (mat != null) { if (Application.isPlaying) Object.Destroy(mat); else Object.DestroyImmediate(mat); }
        root = null; mat = null;
        for (int i = 0; i < MaxLines; i++) pool[i] = null;
    }

    LineRenderer Get(int i)
    {
        if (pool[i] != null) return pool[i];
        if (root == null)
        {
            root = new GameObject("ShipLinkLines");
            if (parent != null) root.transform.SetParent(parent, false);
            var sh = Shader.Find("Sprites/Default");
            mat = new Material(sh);
        }
        var go = new GameObject("link" + i);
        go.transform.SetParent(root.transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = mat;
        lr.useWorldSpace = true;
        lr.loop = false;
        lr.numCapVertices = 0;
        lr.numCornerVertices = 0;
        lr.alignment = LineAlignment.View;
        lr.textureMode = LineTextureMode.Stretch;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.sortingOrder = SortOrder;
        lr.enabled = false;
        pool[i] = lr;
        return lr;
    }

    // a cheap hash to -1..1
    static float H(int a, int b)
    {
        unchecked
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
            h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
            return (h & 0xFFFF) / 32767.5f - 1f;
        }
    }
}
