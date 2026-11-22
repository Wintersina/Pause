using System.Collections.Generic;
using UnityEngine;

// THE ELECTRIC ARC between two linked objects (Volt Viper's chain lightning: ship -> first target -> next target ..., or
// ship -> spark -> spark when no enemy is around). One pooled arc is a jagged polyline drawn twice, a bright thin core
// over a wider faint outer line, plus a tiny star-shaped spark at each end. It re-jags every JagEvery seconds (a flicker),
// lives about LinkLife seconds and fades out.
//
// Ship FX rule (ship-fx-restraint): thin and low glow, never covering the hull. Clearer through crisp contrast and motion
// (the re-jag, the flicker, the contact stars), not through bloom: the core is a pale sodium-white 0.06 units wide, the
// outer line teal at ~30 % alpha 0.16 wide, the contact stars sodium orange. No pink.
//
// Plain LineRenderers (so the kill cam records them), built once and reused: the pool is capped at MaxArcs and, over cap,
// steals the oldest arc. Stepped by ShipAttackRunner.Step on gameplay time, so a paused world freezes the arcs mid-flicker.
public sealed class VoltArc
{
    public bool Live;
    public Vector2 A, B;
    public float Age, Life;
    public int generation;
    internal float jagIn;
    public GameObject root;
    public LineRenderer core, outer, starA, starB;
    public int Jags;   // re-jags so far (tests)
    // This arc's colours (the Volt Viper's unless it was begun with its own: the shot atom ARC's pale lightning).
    public Color coreColor = VoltArcs.CoreColor, outerColor = VoltArcs.OuterColor, starColor = VoltArcs.StarColor;
}

public static class VoltArcs
{
    public const int MaxArcs = 10;               // chain 5 hops + the ship's link, or a 3-shot volley, with the fade tails
    public const int Segments = 8;               // points of the polyline
    public const float LinkLife = .22f;          // how long a chain link stays up, fading (0.15-0.25 s)
    public const float JagEvery = .045f;         // re-jag interval: the flicker
    public const float CoreWidth = .06f, OuterWidth = .16f, StarSize = .2f, StarWidth = .035f;
    public const float OuterAlpha = .3f;

    // sodium-white core, teal outer, sodium orange contact stars (the Volt Viper palette in WeaponStyleTable)
    public static readonly Color CoreColor = new Color(1f, .95f, .82f, 1f);
    public static readonly Color OuterColor = new Color(.12f, .71f, .73f, OuterAlpha);
    public static readonly Color StarColor = new Color(.95f, .53f, .17f, 1f);

    static readonly List<VoltArc> pool = new List<VoltArc>(MaxArcs);
    static Transform root;
    static Material mat;
    static int generationCounter;

    // tests: every arc drawn since load, and the endpoints of the newest ones
    public static int TotalDrawn { get; private set; }
    public const int LogSize = 32;
    static readonly Vector2[] logA = new Vector2[LogSize], logB = new Vector2[LogSize];
    public static int LogCount => Mathf.Min(TotalDrawn, LogSize);
    public static void LogAt(int newestFirst, out Vector2 a, out Vector2 b)
    {
        int i = ((TotalDrawn - 1 - newestFirst) % LogSize + LogSize) % LogSize;
        a = logA[i]; b = logB[i];
    }

    public static int PoolSize { get { Purge(); return pool.Count; } }

    public static int LiveCount
    {
        get
        {
            Purge();
            int n = 0;
            for (int i = 0; i < pool.Count; i++) if (pool[i].Live) n++;
            return n;
        }
    }

    public static VoltArc LiveAt(int index)
    {
        Purge();
        int n = 0;
        for (int i = 0; i < pool.Count; i++)
            if (pool[i].Live && n++ == index) return pool[i];
        return null;
    }

    static void Purge()
    {
        if (root == null) { pool.Clear(); return; }
    }

    // Starts an arc a -> b that lives `life` seconds. Returns its handle (valid for Hold while it is live).
    public static VoltArc Begin(Vector3 a, Vector3 b, float life = LinkLife)
    {
        return Begin(a, b, life, CoreColor, OuterColor, StarColor);
    }

    // The same in its own colours (the shot atom ARC).
    public static VoltArc Begin(Vector3 a, Vector3 b, float life, Color core, Color outer, Color star)
    {
        Purge();
        VoltArc arc = null;
        for (int i = 0; i < pool.Count; i++) if (!pool[i].Live) { arc = pool[i]; break; }
        if (arc == null && pool.Count < MaxArcs) { arc = Build(); pool.Add(arc); }
        if (arc == null)
        {
            arc = pool[0];
            for (int i = 1; i < pool.Count; i++) if (pool[i].Age > arc.Age) arc = pool[i];
        }
        arc.Live = true;
        arc.A = a; arc.B = b; arc.Age = 0f; arc.Life = life; arc.jagIn = 0f; arc.Jags = 0;
        arc.coreColor = core; arc.outerColor = outer; arc.starColor = star;
        arc.generation = ++generationCounter;
        arc.root.SetActive(true);
        int slot = TotalDrawn % LogSize;
        logA[slot] = a; logB[slot] = b;
        TotalDrawn++;
        Jag(arc);
        return arc;
    }

    // Keeps a moving link alive: moves `held` (when it is still the arc it was) to a -> b and resets its fade; otherwise
    // starts a new one. `life` is how long it lingers once it is no longer held.
    public static VoltArc Hold(VoltArc held, int generationOfHeld, Vector3 a, Vector3 b, float life = .1f)
    {
        if (held != null && held.Live && held.generation == generationOfHeld)
        {
            held.A = a; held.B = b; held.Age = 0f; held.Life = life;
            return held;
        }
        return Begin(a, b, life);
    }

    public static int GenerationOf(VoltArc arc) { return arc != null ? arc.generation : -1; }

    public static void Tick(float dt)
    {
        if (root == null) return;
        for (int i = 0; i < pool.Count; i++)
        {
            var arc = pool[i];
            if (!arc.Live) continue;
            arc.Age += dt;
            if (arc.Age >= arc.Life) { arc.Live = false; arc.root.SetActive(false); continue; }
            arc.jagIn -= dt;
            if (arc.jagIn <= 0f) { arc.jagIn = JagEvery; Jag(arc); arc.Jags++; }
            Fade(arc);
        }
    }

    public static void StopAll()
    {
        for (int i = 0; i < pool.Count; i++)
            if (pool[i] != null && pool[i].root != null) { pool[i].Live = false; pool[i].root.SetActive(false); }
    }

    static VoltArc Build()
    {
        if (root == null)
        {
            root = new GameObject("~VoltArcs").transform;
            pool.Clear();
        }
        if (mat == null) mat = new Material(Shader.Find("Sprites/Default"));
        var arc = new VoltArc();
        arc.root = new GameObject("arc");
        arc.root.transform.SetParent(root, false);
        arc.outer = Line(arc.root, "outer", OuterWidth, 70);
        arc.core = Line(arc.root, "core", CoreWidth, 71);
        arc.starA = Line(arc.root, "starA", StarWidth, 72);
        arc.starB = Line(arc.root, "starB", StarWidth, 72);
        arc.outer.positionCount = arc.core.positionCount = Segments;
        arc.starA.positionCount = arc.starB.positionCount = 5;
        arc.root.SetActive(false);
        return arc;
    }

    static LineRenderer Line(GameObject parent, string name, float width, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = mat;
        lr.useWorldSpace = true;
        lr.startWidth = lr.endWidth = width;
        lr.numCapVertices = 0;
        lr.numCornerVertices = 0;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.sortingOrder = order;
        return lr;
    }

    // a fresh jagged path a -> b: sideways offsets that are largest mid-way and zero at the ends
    static void Jag(VoltArc arc)
    {
        Vector2 d = arc.B - arc.A;
        float len = d.magnitude;
        Vector2 dir = len > 1e-4f ? d / len : Vector2.up;
        Vector2 side = new Vector2(-dir.y, dir.x);
        float amp = Mathf.Clamp(len * .12f, .06f, .3f);
        for (int i = 0; i < Segments; i++)
        {
            float f = i / (float)(Segments - 1);
            float taper = Mathf.Sin(f * Mathf.PI);
            Vector2 p = arc.A + d * f + side * (Random.Range(-1f, 1f) * amp * taper);
            var v = new Vector3(p.x, p.y, 0f);
            arc.core.SetPosition(i, v);
            arc.outer.SetPosition(i, v);
        }
        Star(arc.starA, arc.A);
        Star(arc.starB, arc.B);
        Fade(arc);
    }

    // a small asterisk drawn in one stroke, turned a random way each jag
    static void Star(LineRenderer lr, Vector2 at)
    {
        float r = StarSize * .5f * Random.Range(.8f, 1.2f);
        float rot = Random.Range(0f, Mathf.PI);
        Vector2 u = new Vector2(Mathf.Cos(rot), Mathf.Sin(rot)) * r, v = new Vector2(-u.y, u.x);
        Put(lr, 0, at - u); Put(lr, 1, at + u); Put(lr, 2, at); Put(lr, 3, at - v); Put(lr, 4, at + v);
    }

    static void Put(LineRenderer lr, int i, Vector2 p) { lr.SetPosition(i, new Vector3(p.x, p.y, 0f)); }

    // fades with age; a quick flicker between re-jags keeps it alive on screen
    static void Fade(VoltArc arc)
    {
        float life = Mathf.Clamp01(1f - arc.Age / Mathf.Max(.01f, arc.Life));
        float flicker = (Mathf.FloorToInt(arc.Age / (JagEvery * .5f)) & 1) == 0 ? 1f : .78f;
        float k = Mathf.Sqrt(life) * flicker;
        Tint(arc.core, arc.coreColor, k);
        Tint(arc.outer, arc.outerColor, k);
        Tint(arc.starA, arc.starColor, k);
        Tint(arc.starB, arc.starColor, k);
    }

    static void Tint(LineRenderer lr, Color c, float k)
    {
        c.a *= k;
        lr.startColor = lr.endColor = c;
    }
}
