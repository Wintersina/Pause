using System.Collections.Generic;
using UnityEngine;

// THE TELEGRAPH (docs/world-attacks-design.md FR1 + FR2).
//
// An instant-hit hazard (jet, strike, wave, blast) must show its exact
// footprint at least MinLead seconds before it goes live. AttackPreview draws
// it: a dotted pink-white outline of the AttackShape the hitbox is made from,
// at half alpha, blinking in 8 fps steps (a danger tell, never an atom's
// smooth breath). It is pooled: Show() hands out a preview and its dots from
// fixed pools built on first use; nothing is made per call after that.
//
//   var pv = AttackPreview.Show(shape, secondsUntilLive, tint);   // at the start of the tell
//   pv.Step(dt);                                                 // every running frame (pause freezes it)
//   pv.Follow(delta);                                            // the footprint rides the board
//   pv.GoLive();                                                 // the hazard ignites: the preview ends
//   pv.End();                                                    // cancelled
//
// Shown with less than MinLead to go, or live before MinLead of it was
// shown, counts in TooShort (AttackFairnessTest asserts 0). MinShown is the
// shortest preview any hazard has actually given the player.
public sealed class AttackPreview
{
    public const float MinLead = .4f;        // FR1: the footprint shows at least this long before it goes live
    public const float DotSpacing = .16f;
    public const float DotSize = .09f;
    public const float Alpha = .5f;
    public const float BlinkFps = 8f;
    public const int MaxDots = 420, MaxPreviews = 16;
    public const int SortingOrder = 6;

    // counters (tests, previews)
    public static int Shown, TooShort, Dropped;
    public static float MinShown = float.PositiveInfinity;

    sealed class Dot { public SpriteRenderer sr; public bool used; }

    static readonly List<Dot> dots = new List<Dot>(MaxDots);
    static readonly AttackPreview[] pool = new AttackPreview[MaxPreviews];
    static Transform root;

    readonly Transform node;
    readonly int[] mine = new int[MaxDots / 2];
    int mineCount;
    float shownFor, untilLive, tintAlpha = 1f;
    Color tint = Color.white;

    public bool Active { get; private set; }
    public float ShownSeconds => shownFor;
    public float UntilLive => untilLive;
    public int DotCount => mineCount;
    public bool Live { get; private set; }

    AttackPreview()
    {
        node = new GameObject("Preview").transform;
        node.SetParent(root, false);
        node.gameObject.SetActive(false);
    }

    static void EnsureRoot()
    {
        if (root != null) return;
        // a scene change took the old ones
        dots.Clear();
        for (int i = 0; i < pool.Length; i++) pool[i] = null;
        root = new GameObject("~AttackPreviews").transform;
    }

    public static int ActiveCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < pool.Length; i++) if (pool[i] != null && pool[i].Active) n++;
            return n;
        }
    }

    public static int DotsInUse
    {
        get
        {
            int n = 0;
            for (int i = 0; i < dots.Count; i++) if (dots[i].used) n++;
            return n;
        }
    }

    public static void ResetCounters() { Shown = TooShort = Dropped = 0; MinShown = float.PositiveInfinity; }

    // Draws `shape`'s outline; the hazard goes live in `secondsUntilLive`. Null if all MaxPreviews are busy.
    public static AttackPreview Show(AttackShape shape, float secondsUntilLive, Color tint)
    {
        EnsureRoot();
        AttackPreview p = null;
        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i] == null) pool[i] = new AttackPreview();
            if (!pool[i].Active) { p = pool[i]; break; }
        }
        if (p == null) return null;
        p.Begin(shape, secondsUntilLive, tint);
        return p;
    }

    void Begin(AttackShape shape, float secondsUntilLive, Color pinkTint)
    {
        Active = true;
        Live = false;
        shownFor = 0f;
        untilLive = secondsUntilLive;
        tint = HostileShotPalette.Core(pinkTint);
        node.position = Vector3.zero;
        node.gameObject.SetActive(true);
        mineCount = 0;
        Shown++;
        if (secondsUntilLive < MinLead - 1e-4f) TooShort++;
        for (int l = 0; l < shape.LoopCount; l++)
        {
            int n = shape.LoopLength(l);
            for (int i = 0; i < n; i++)
            {
                Vector2 a = shape.LoopAt(l, i), b = shape.LoopAt(l, (i + 1) % n);
                float len = Vector2.Distance(a, b);
                int steps = Mathf.Max(1, Mathf.CeilToInt(len / DotSpacing));
                for (int s = 0; s < steps; s++) PlaceDot(Vector2.Lerp(a, b, (s + .5f) / steps));
            }
        }
        Paint();
    }

    void PlaceDot(Vector2 at)
    {
        if (mineCount >= mine.Length) { Dropped++; return; }
        int idx = -1;
        for (int i = 0; i < dots.Count; i++) if (!dots[i].used) { idx = i; break; }
        if (idx < 0)
        {
            if (dots.Count >= MaxDots) { Dropped++; return; }
            var go = new GameObject("Dot");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = EliteFxArt.Spark;
            sr.sortingOrder = SortingOrder;
            go.transform.localScale = Vector3.one * (DotSize / Mathf.Max(.01f, EliteFxArt.Spark.bounds.size.x));
            dots.Add(new Dot { sr = sr });
            idx = dots.Count - 1;
        }
        var d = dots[idx];
        d.used = true;
        d.sr.transform.SetParent(node, false);
        d.sr.transform.localPosition = new Vector3(at.x, at.y, 0f);
        d.sr.enabled = true;
        d.sr.gameObject.SetActive(true);
        mine[mineCount++] = idx;
    }

    void Paint()
    {
        bool on = (Mathf.FloorToInt(shownFor * BlinkFps) & 1) == 0;
        var c = tint;
        c.a = on ? Alpha : Alpha * .6f;
        for (int i = 0; i < mineCount; i++) dots[mine[i]].sr.color = c;
    }

    // One running frame (the hazard's Step: a frozen world freezes the preview).
    public void Step(float dt)
    {
        if (!Active || dt <= 0f) return;
        shownFor += dt;
        untilLive -= dt;
        Paint();
    }

    // The footprint rides the board (or its owner): shifts every dot.
    public void Follow(Vector2 delta)
    {
        if (!Active) return;
        node.position += new Vector3(delta.x, delta.y, 0f);
    }

    // The hazard ignites: records how long the player had to read it, then ends.
    public void GoLive()
    {
        if (!Active) return;
        Live = true;
        if (shownFor < MinLead - 1e-4f) TooShort++;
        if (shownFor < MinShown) MinShown = shownFor;
        End();
    }

    public void End()
    {
        if (!Active) return;
        Active = false;
        for (int i = 0; i < mineCount; i++)
        {
            var d = dots[mine[i]];
            d.used = false;
            d.sr.gameObject.SetActive(false);
            d.sr.transform.SetParent(root, false);
        }
        mineCount = 0;
        node.gameObject.SetActive(false);
    }

    // Everything off (world change, death domino, tests).
    public static void EndAll()
    {
        if (root == null)
        {
            // a scene change took the dots and nodes with it: nothing to give back
            dots.Clear();
            for (int i = 0; i < pool.Length; i++) pool[i] = null;
            return;
        }
        for (int i = 0; i < pool.Length; i++) if (pool[i] != null) pool[i].End();
    }
}
