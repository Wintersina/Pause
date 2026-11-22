using System.Collections.Generic;
using UnityEngine;

// Thin pooled lines that tell the player where a player attack is about to land (Lightning Sky's javelins,
// Paragon's warheads). One pool of LineRenderers, capped at FxLinks.MaxLines; the oldest is recycled when
// it is full. Art rules: thin, low contrast against the ship, never a box / ring / bracket, the ship's own
// colours (WeaponStyleTable), motion (a flicker) instead of bloom.
public enum LinkKind { JavelinTelegraph, JavelinFlash, MissileTrail, ImpactSpark }

public class LinkLine : MonoBehaviour
{
    public const int Points = 9;

    LineRenderer lr;
    Vector3 a, b;
    float crackle, life, lifeMax, flickerHz, baseAlpha;
    Color color;
    bool jitterOn;
    static int seedCounter;
    int seed;

    public bool Active { get; private set; }
    public LinkKind Kind { get; private set; }
    public float StartedAt { get; private set; }
    public float Width { get { return lr != null ? lr.widthMultiplier : 0f; } }
    public Vector3 Point(int i) { return lr.GetPosition(i); }
    public Vector3 First { get { return lr.GetPosition(0); } }
    public Vector3 Last { get { return lr.GetPosition(Points - 1); } }
    public bool Visible { get { return lr != null && lr.enabled && color.a * baseAlpha > .01f; } }

    public void Build()
    {
        lr = gameObject.AddComponent<LineRenderer>();
        lr.positionCount = Points;
        lr.useWorldSpace = true;
        lr.numCapVertices = 0;
        lr.numCornerVertices = 0;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.sortingOrder = 71;
        lr.sharedMaterial = FxLinks.Material;
        gameObject.SetActive(false);
    }

    void Begin(LinkKind kind, Color c, float width, float life)
    {
        Kind = kind;
        color = c;
        baseAlpha = 1f;
        this.life = lifeMax = life;
        crackle = 0f;
        flickerHz = 0f;
        jitterOn = false;
        seed = ++seedCounter;
        StartedAt = Time.unscaledTime;
        lr.widthMultiplier = width;
        Paint(1f);
        Active = true;
        gameObject.SetActive(true);
    }

    void Paint(float alpha)
    {
        baseAlpha = alpha;
        var c = new Color(color.r, color.g, color.b, color.a * alpha);
        lr.startColor = c;
        lr.endColor = c;
    }

    // A crackling straight line from `from` to `to` (flickers every frame; `amp` is the sideways wobble).
    public void Crackle(LinkKind kind, Vector3 from, Vector3 to, Color c, float width, float amp, float hz)
    {
        Begin(kind, c, width, 0f);
        a = from; b = to; crackle = amp; flickerHz = hz; jitterOn = true;
        Layout(0f);
    }

    // A faint curved trail: a bow between `tail` and `head`, fading toward the tail.
    public void Trail(Vector3 tail, Vector3 head, float bow, Color c, float width)
    {
        if (!Active)
        {
            Begin(LinkKind.MissileTrail, c, width, 0f);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(c.a, 1f) });
            lr.colorGradient = g;
        }
        a = tail; b = head;
        Vector3 d = b - a;
        Vector3 side = new Vector3(-d.y, d.x, 0f).normalized * bow;
        for (int i = 0; i < Points; i++)
        {
            float t = i / (float)(Points - 1);
            lr.SetPosition(i, a + d * t + side * (4f * t * (1f - t)));
        }
    }

    public void MoveTrail(Vector3 tail, Vector3 head, float bow) { Trail(tail, head, bow, color, lr.widthMultiplier); }

    public void SetEnds(Vector3 from, Vector3 to) { a = from; b = to; Layout(0f); }

    // The strike: the crackle flashes once, wider and whiter, and fades out.
    public void Flash(float seconds, Color c, float width)
    {
        Kind = LinkKind.JavelinFlash;
        color = c;
        lr.widthMultiplier = width;
        crackle = 0f;
        jitterOn = false;
        flickerHz = 0f;
        life = lifeMax = seconds;
        Paint(1f);
        Layout(0f);
    }

    void Layout(float t)
    {
        for (int i = 0; i < Points; i++)
        {
            float k = i / (float)(Points - 1);
            Vector3 p = Vector3.Lerp(a, b, k);
            if (crackle > 0f && i > 0 && i < Points - 1)
            {
                float n = Mathf.Sin((seed * 12.9898f + i * 78.233f + t * 61f) * 43758.5453f);
                p.x += (n - Mathf.Floor(n) - .5f) * 2f * crackle;
            }
            lr.SetPosition(i, p);
        }
    }

    // Advances the flash / flicker by `dt` gameplay seconds (Update calls it; tests call it directly).
    public void Advance(float dt)
    {
        if (!Active) return;
        if (jitterOn) Layout(Time.unscaledTime);
        if (flickerHz > 0f)
        {
            float t = Time.unscaledTime * flickerHz;
            Paint(((int)t & 1) == 0 ? 1f : .35f);
        }
        if (lifeMax > 0f)
        {
            life -= dt;
            if (life <= 0f) { Stop(); return; }
            Paint(Mathf.Clamp01(life / lifeMax));
        }
    }

    void Update() { Advance(ShipAttackRunner.ScaledDelta()); }

    public void Stop()
    {
        if (!Active) return;
        Active = false;
        gameObject.SetActive(false);
    }
}

public static class FxLinks
{
    public const int MaxLines = 24;

    // Javelin telegraph: how long before the javelin manifests the line starts to flicker, its look.
    public const float JavelinLead = .25f, JavelinWidth = .035f, JavelinCrackle = .045f, JavelinFlashSeconds = .18f, JavelinFlashWidth = .09f;
    // Missile trail and impact spark.
    public const float TrailWidth = .03f, TrailAlpha = .5f, TrailLength = 1.1f, TrailBow = .18f;
    public const float SparkLead = .2f, SparkWidth = .035f, SparkLength = .5f;

    static readonly List<LinkLine> pool = new List<LinkLine>();
    static Transform root;
    static Material mat;

    public static Material Material
    {
        get
        {
            if (mat == null)
            {
                var sh = Shader.Find("Sprites/Default");
                mat = sh != null ? new Material(sh) : null;
                if (mat != null) mat.hideFlags = HideFlags.HideAndDontSave;
            }
            return mat;
        }
    }

    public static IReadOnlyList<LinkLine> Pool { get { Purge(); return pool; } }

    static void Purge()
    {
        if (root == null) { pool.Clear(); return; }
        for (int i = pool.Count - 1; i >= 0; i--) if (pool[i] == null) pool.RemoveAt(i);
    }

    public static int ActiveCount
    {
        get { Purge(); int n = 0; for (int i = 0; i < pool.Count; i++) if (pool[i].Active) n++; return n; }
    }

    public static int ActiveOf(LinkKind kind)
    {
        Purge();
        int n = 0;
        for (int i = 0; i < pool.Count; i++) if (pool[i].Active && pool[i].Kind == kind) n++;
        return n;
    }

    public static LinkLine Acquire()
    {
        if (root == null) { root = new GameObject("~FxLinks").transform; pool.Clear(); }
        Purge();
        LinkLine oldest = null;
        for (int i = 0; i < pool.Count; i++)
        {
            var l = pool[i];
            if (!l.Active) return l;
            if (oldest == null || l.StartedAt < oldest.StartedAt) oldest = l;
        }
        if (pool.Count >= MaxLines && oldest != null) { oldest.Stop(); return oldest; }
        var go = new GameObject("~link");
        go.transform.SetParent(root, false);
        var line = go.AddComponent<LinkLine>();
        line.Build();
        pool.Add(line);
        return line;
    }

    public static void StopAll()
    {
        Purge();
        for (int i = 0; i < pool.Count; i++) pool[i].Stop();
    }

    // The ship's own light: its weapon style's energy colour (Lightning cyan, Paragon cyan), a touch whiter.
    public static Color ShipColor(int ship, float alpha)
    {
        var e = WeaponStyleTable.For(ship).energy;
        return new Color(Mathf.Lerp(e.r, 1f, .25f), Mathf.Lerp(e.g, 1f, .25f), Mathf.Lerp(e.b, 1f, .25f), alpha);
    }

    // ---- javelins --------------------------------------------------------

    // The thin crackling line down a javelin's lane, from its top to a little above where it stops (so it
    // never sits on the hull).
    public static LinkLine JavelinTelegraph(int ship, float x, float topY, float stopY)
    {
        var l = Acquire();
        l.Crackle(LinkKind.JavelinTelegraph, new Vector3(x, topY, 0f), new Vector3(x, stopY + JavelinHullClear, 0f),
                  ShipColor(ship, .9f), JavelinWidth, JavelinCrackle, 40f);
        return l;
    }
    public const float JavelinHullClear = .5f;

    // ---- warheads ----------------------------------------------------------

    // Faint curved trail behind a falling warhead.
    public static LinkLine MissileTrail(int ship, Vector3 tail, Vector3 head, float bow)
    {
        var l = Acquire();
        l.Trail(tail, head, bow, ShipColor(ship, TrailAlpha), TrailWidth);
        return l;
    }

    // A short flickering spark at the impact point: no crosshair, no ring.
    public static LinkLine ImpactSpark(int ship, Vector3 at)
    {
        var l = Acquire();
        l.Crackle(LinkKind.ImpactSpark, at + Vector3.up * SparkLength * .5f, at + Vector3.down * SparkLength * .5f,
                  ShipColor(ship, .95f), SparkWidth, .05f, 45f);
        return l;
    }
}
