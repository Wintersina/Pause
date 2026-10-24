using UnityEngine;

// DAZED SPARKS: two small electric sparkles circling above the head of an enemy or elite the Space flare has stunned. Pooled
// placeholder pixel sprites (WeatherSprites.Flake, no outline, no border): a fixed set of Slots x 2 renderers built once, so Draw
// allocates nothing. A slot follows its target while the target is still stunned (FrozenState.IsFrozen / EliteShip.Stunned), so the
// sparkles end with the stun and a target that dies or is despawned just frees its slot.
public static class DazedSparks
{
    public const int Slots = 14;
    public const int SortingOrder = 46;
    const float Above = .14f, Orbit = .3f, Spin = 5.5f;

    struct Slot { public Transform tr; public FrozenState frozen; public EliteShip elite; public float head; public float phase; }

    static readonly Slot[] slots = new Slot[Slots];
    static readonly SpriteRenderer[] sr = new SpriteRenderer[Slots * 2];
    static Transform root;
    static int phaseSeed;

    public static int Shown { get; private set; }

    // Marks a stunned target (call it each frame it is held: it only registers a new target once). Exactly one of frozen / elite is set.
    public static void Mark(Transform tr, FrozenState frozen, EliteShip elite)
    {
        if (tr == null) return;
        int free = -1;
        for (int i = 0; i < Slots; i++)
        {
            if (slots[i].tr == tr) return;
            if (free < 0 && (slots[i].tr == null || !Holds(slots[i]))) free = i;
        }
        if (free < 0) return;
        float top = .5f;
        if (tr.TryGetComponent(out SpriteRenderer body)) top = body.bounds.max.y - tr.position.y;
        slots[free] = new Slot { tr = tr, frozen = frozen, elite = elite, head = Mathf.Max(.2f, top) + Above, phase = (phaseSeed++ % 7) * .9f };
    }

    // Is this target currently wearing sparks? (tests)
    public static bool Has(Transform tr)
    {
        if (tr == null) return false;
        for (int i = 0; i < Slots; i++) if (slots[i].tr == tr && Holds(slots[i])) return true;
        return false;
    }

    static bool Holds(in Slot s)
    {
        if (s.tr == null) return false;
        return (s.frozen != null && s.frozen.IsFrozen) || (s.elite != null && s.elite.Stunned);
    }

    static void Build()
    {
        root = new GameObject("~DazedSparks").transform;
        Sprite spark = WeatherSprites.Get(AmbientShape.Flake);
        for (int i = 0; i < sr.Length; i++)
        {
            var go = new GameObject("s" + i, typeof(SpriteRenderer));
            go.transform.SetParent(root, false);
            var r = go.GetComponent<SpriteRenderer>();
            r.sprite = spark;
            r.sortingOrder = SortingOrder;
            r.enabled = false;
            sr[i] = r;
        }
    }

    // One frame (time: Time.time). Two sparkles per target circle its head, one gold and one pale cyan, twinkling out of step.
    public static void Draw(float time)
    {
        if (root == null)
        {
            bool any = false;
            for (int i = 0; i < Slots && !any; i++) any = slots[i].tr != null;
            if (!any) return;
            Build();
        }
        int n = 0;
        for (int i = 0; i < Slots; i++)
        {
            if (!Holds(slots[i])) { slots[i] = default; continue; }
            Vector3 p = slots[i].tr.position;
            float a = time * Spin + slots[i].phase;
            for (int k = 0; k < 2; k++)
            {
                float ang = a + k * Mathf.PI;
                float tw = .5f + .5f * Mathf.Sin(time * 14f + slots[i].phase * 3f + k * 2.1f);
                var r = sr[n++];
                r.transform.position = new Vector3(p.x + Mathf.Cos(ang) * Orbit, p.y + slots[i].head + Mathf.Sin(ang) * .07f, p.z);
                float s = .16f + .12f * tw;
                r.transform.localScale = new Vector3(s, s, 1f);
                r.color = k == 0 ? new Color(1f, .9f, .5f, .5f + .5f * tw) : new Color(.7f, .95f, 1f, .5f + .5f * (1f - tw));
                r.enabled = true;
            }
        }
        Shown = n / 2;
        if (root == null) return;
        for (int i = n; i < sr.Length; i++) if (sr[i].enabled) sr[i].enabled = false;
    }

    public static void Reset()
    {
        for (int i = 0; i < Slots; i++) slots[i] = default;
        Shown = 0;
        if (root != null) Object.DestroyImmediate(root.gameObject);
        root = null;
    }
}
