using UnityEngine;

// The obsidian shard's blinking glint: one child SpriteRenderer on the blade's upper-left facet, flashing for
// ObsidianShards.GlintOnSeconds every GlintPeriod (per-shard period and phase from the shard's seed). It is a child of the
// shard, so it spins and scales with the blade, and a plain SpriteRenderer, so the kill-cam recording sees it. Three small
// procedural sprites are built once (no per-frame allocation: Update only compares a level and swaps a cached sprite).
// Runs on scaled time like the rest of the board, so it freezes with the pause.
[DisallowMultipleComponent]
public class ObsidianGlint : MonoBehaviour
{
    static Sprite[] sprites;     // [1] edge (small), [2] peak (full cross)
    SpriteRenderer glint, body;
    uint seed;
    float clock;
    int level = -1;
    Color tint;

    public SpriteRenderer Renderer => glint;
    public int Level => level;

    public void Init(uint shardSeed, int variant)
    {
        seed = shardSeed;
        body = GetComponent<SpriteRenderer>();
        var child = transform.Find("Glint");
        if (child == null)
        {
            child = new GameObject("Glint").transform;
            child.SetParent(transform, false);
        }
        child.localPosition = new Vector3(ObsidianShards.GlintOffset.x, ObsidianShards.GlintOffset.y, 0f);
        glint = child.GetComponent<SpriteRenderer>();
        if (glint == null) glint = child.gameObject.AddComponent<SpriteRenderer>();
        glint.sortingOrder = body != null ? body.sortingOrder + 1 : 3;
        tint = ObsidianShards.GlintTints[Mathf.Clamp(variant, 0, ObsidianShards.GlintTints.Length - 1)];
        clock = 0f;
        level = -1;
        Apply(ObsidianShards.GlintLevel(seed, 0f));
    }

    void Update() { Advance(Time.deltaTime); }

    // Public so edit-mode tests can step it.
    public void Advance(float dt)
    {
        if (glint == null || dt <= 0f) return;
        clock += dt;
        Apply(ObsidianShards.GlintLevel(seed, clock));
    }

    void Apply(int next)
    {
        if (next == level) return;
        level = next;
        if (next == 0) { glint.enabled = false; return; }
        if (sprites == null || sprites[2] == null) sprites = Build();
        glint.sprite = sprites[next];
        var c = tint; c.a = next == 2 ? ObsidianShards.GlintAlpha : ObsidianShards.GlintAlpha * .5f;
        glint.color = c;
        glint.enabled = true;
    }

    // 9 px cell: a one-pixel bright core, a cross flare (arms 4 px, fading), a faint diagonal pair; the edge sprite is core + short cross.
    static Sprite[] Build()
    {
        var result = new Sprite[3];
        for (int level = 1; level <= 2; level++)
        {
            const int N = 9;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            var px = new Color[N * N];
            int reach = level == 2 ? 4 : 2;
            for (int i = -reach; i <= reach; i++)
            {
                float a = 1f - Mathf.Abs(i) / (reach + 1f);
                a = Mathf.Max(a, .25f);
                px[(4 + i) * N + 4] = new Color(1f, 1f, 1f, a);
                px[4 * N + 4 + i] = new Color(1f, 1f, 1f, a);
            }
            if (level == 2)
                foreach (int d in new[] { -1, 1 })
                    foreach (int e in new[] { -1, 1 })
                        px[(4 + d) * N + 4 + e] = new Color(1f, 1f, 1f, .35f);
            px[4 * N + 4] = Color.white;
            tex.SetPixels(px);
            tex.Apply(false, true);
            var s = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(.5f, .5f), N / ObsidianShards.GlintSize, 0, SpriteMeshType.FullRect);
            s.name = "obsidian_glint_" + level;
            s.hideFlags = HideFlags.HideAndDontSave;
            result[level] = s;
        }
        return result;
    }
}
