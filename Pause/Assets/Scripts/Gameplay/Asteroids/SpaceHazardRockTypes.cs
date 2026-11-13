using UnityEngine;

// The four rock types of Space's falling HAZARD field (SpaceAsteroidField): shard, ember, ore, crystal. They have
// their own art, apart from the decorative drift field's rocks (SpaceRockTypes / asteroid_00..02), so a pilot can
// never mistake a backdrop rock for something that costs a heart.
//
// Art: Resources/Worlds/Space/HazardRocks/
//   space_hazard_rock_<key>_blink.png        2048x512 = 4 frames of 512x512, the whole rock; only its accent pulses
//   space_hazard_rock_<key>_chunk_blink.png  the same for the chunk a split leaves (the chunk keeps its parent's type)
//   space_hazard_rock_<key>.png              frame 0 of the rock: the Codex icon
// The frames are cut once per type and shared by every rock. The measured numbers (Fill, Radius, ChunkFill,
// ChunkRadius) come from the strips' alpha (>= 128): run Tools/measure_space_hazard_rocks.py <folder>. SpaceHazardRocksTest
// re-measures the PNGs and fails when they drift, so a re-drawn strip must update this table.
public sealed class SpaceHazardRockType
{
    public readonly string key;
    public readonly string name;            // Codex name
    public readonly string lore;            // Codex text
    public readonly Color accent;           // the glow colour: hit chips take it
    public readonly float fill;             // widest side of the rock's art / frame width (max over the 4 frames)
    public readonly float radius;           // area-equivalent radius of the rock's art / frame width
    public readonly float chunkFill, chunkRadius;   // ... the same for the chunk strip

    public SpaceHazardRockType(string key, string name, string lore, Color accent,
                               float fill, float radius, float chunkFill, float chunkRadius)
    {
        this.key = key; this.name = name; this.lore = lore; this.accent = accent;
        this.fill = fill; this.radius = radius; this.chunkFill = chunkFill; this.chunkRadius = chunkRadius;
    }

    public string CodexId { get { return "hazard_space_rock_" + key; } }
    // The spawned object's name, which the Codex matches (Codex.IdForName).
    public string ObjectName { get { return "space_hazard_rock_" + key; } }

    // A chunk is drawn so its art is as wide as a whole rock's art of its size tier: its strip is padded more.
    public float ChunkBoost { get { return Mathf.Clamp(fill / Mathf.Max(.05f, chunkFill), 1f, 2.2f); } }

    Sprite[] frames, chunkFrames;
    Sprite still;
    public Sprite[] Frames
    {
        get
        {
            if (frames == null || frames.Length == 0 || frames[0] == null)
                frames = SpaceHazardRockTypes.Cut("space_hazard_rock_" + key + "_blink", "hazard_rock_" + key + "_");
            return frames;
        }
    }
    public Sprite[] ChunkFrames
    {
        get
        {
            if (chunkFrames == null || chunkFrames.Length == 0 || chunkFrames[0] == null)
                chunkFrames = SpaceHazardRockTypes.Cut("space_hazard_rock_" + key + "_chunk_blink", "hazard_rock_" + key + "_chunk_");
            return chunkFrames;
        }
    }
    public Sprite Still
    {
        get
        {
            if (still == null) still = Resources.Load<Sprite>(SpaceHazardRockTypes.Folder + "space_hazard_rock_" + key);
            return still;
        }
    }
    public void Forget() { frames = null; chunkFrames = null; still = null; }
}

public static class SpaceHazardRockTypes
{
    public const string Folder = "Worlds/Space/HazardRocks/";
    public const int BlinkFrames = 4;
    public const float BlinkFps = 7f;               // the loop's rate (6-8 fps)
    public const string CodexPrefix = "hazard_space_rock_";

    public static readonly SpaceHazardRockType[] All =
    {
        new SpaceHazardRockType("shard", "Glass Shard",
            "A jagged splinter of volcanic glass tumbling out of the dark, a pale cyan vein pulsing inside it. It breaks into sharp chunks, so give it room.",
            new Color(.55f, .93f, 1f), .859f, .354f, .586f, .255f),
        new SpaceHazardRockType("ember", "Ember Rock",
            "A heavy, scorched boulder with a sodium-orange core that never cools. Shoot it and it splits into glowing slag.",
            new Color(1f, .62f, .22f), .859f, .407f, .586f, .262f),
        new SpaceHazardRockType("ore", "Ion Ore",
            "A rough lump of ore veined with blue ion crystal. It hums as it falls and sheds hot grit when struck.",
            new Color(.35f, .62f, 1f), .859f, .317f, .586f, .265f),
        new SpaceHazardRockType("crystal", "Prism Crystal",
            "A cluster of violet crystal that flickers as it spins. Brittle, but it still takes a few good hits to shatter.",
            new Color(.78f, .45f, 1f), .859f, .358f, .586f, .257f),
    };
    public static int Count { get { return All.Length; } }

    // The collider is a circle of this share of the art's area-equivalent radius (a hair inside the drawing).
    public const float ColliderFit = .92f;

    public static SpaceHazardRockType Type(int index) { return All[((index % All.Length) + All.Length) % All.Length]; }
    public static SpaceHazardRockType FindByCodexId(string id)
    {
        for (int i = 0; i < All.Length; i++) if (All[i].CodexId == id) return All[i];
        return null;
    }

    // Every type has its rock and chunk frames (the field spawns nothing without them).
    public static bool Ready
    {
        get
        {
            for (int i = 0; i < All.Length; i++)
            {
                var f = All[i].Frames; var c = All[i].ChunkFrames;
                if (f == null || f.Length != BlinkFrames || c == null || c.Length != BlinkFrames) return false;
            }
            return true;
        }
    }

    public static void Forget() { for (int i = 0; i < All.Length; i++) All[i].Forget(); }

    // A 4-frame strip cut into sprites that share the texture (mipmapped: the rocks are drawn small).
    internal static Sprite[] Cut(string file, string namePrefix)
    {
        var strip = Resources.Load<Sprite>(Folder + file);
        if (strip == null) return null;
        var tex = strip.texture;
        int w = tex.width / BlinkFrames;
        var cut = new Sprite[BlinkFrames];
        for (int i = 0; i < BlinkFrames; i++)
        {
            cut[i] = Sprite.Create(tex, new Rect(i * w, 0, w, tex.height), new Vector2(.5f, .5f), strip.pixelsPerUnit);
            cut[i].name = namePrefix + i;
        }
        return cut;
    }
}

// Hit feedback for the hazard rocks, all of it pooled so a hit allocates nothing:
//   * CHIPS: a few tiny square flecks in the rock's accent colour fly off the hit side and fade (one fixed pool of
//     ChipPool sprites, recycled round-robin, moved by one driver; nothing is created after the first hit)
//   * FLASH: a white overlay on the rock (Pause/RockHitFlash, a flat white-where-opaque sprite shader): see FieldAsteroid
public static class RockHitFx
{
    public const int ChipPool = 24;
    public const float ChipLife = .32f;
    public const float ChipSpeedMin = .7f, ChipSpeedMax = 1.7f;     // u/s
    public const float ChipSizeMin = .035f, ChipSizeMax = .075f;    // u (a few device pixels: pixel-art flecks)
    public const float ChipSpread = .9f;                            // radians either side of the outward direction
    public const string FlashShader = "BackdropShaders/RockHitFlash";

    static Transform root;
    static SpriteRenderer[] sr;
    static Vector2[] vel;
    static float[] age;
    static Color[] tint;
    static float[] size;
    static int next, active;
    static Sprite chipSprite;
    static Material flashMaterial;
    static bool flashTried;

    public static int ActiveChips { get { return active; } }

    // The shared flash material (null when the shader is missing: the rock then dims for a blink instead).
    public static Material Flash
    {
        get
        {
            if (flashMaterial == null && !flashTried)
            {
                flashTried = true;
                var shader = Resources.Load<Shader>(FlashShader);
                if (shader != null) flashMaterial = new Material(shader) { name = "RockHitFlash" };
            }
            return flashMaterial;
        }
    }

    static void Ensure()
    {
        if (root != null && sr != null) return;
        var go = new GameObject("~RockChips");
        root = go.transform;
        go.AddComponent<Driver>();
        if (chipSprite == null)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "rock_chip" };
            tex.SetPixels32(new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) });
            tex.Apply(false, true);
            chipSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f), 2f);   // 1 u wide
        }
        sr = new SpriteRenderer[ChipPool];
        vel = new Vector2[ChipPool]; age = new float[ChipPool]; tint = new Color[ChipPool]; size = new float[ChipPool];
        for (int i = 0; i < ChipPool; i++)
        {
            var c = new GameObject("chip");
            c.transform.SetParent(root, false);
            var r = c.AddComponent<SpriteRenderer>();
            r.sprite = chipSprite;
            r.sortingOrder = SpaceAsteroidField.SortingOrder + 2;
            r.enabled = false;
            sr[i] = r;
            age[i] = -1f;
        }
        next = 0; active = 0;
    }

    // `count` chips from `at`, fanned about `dir` (outward from the rock), in `accent`.
    public static void Chips(Vector3 at, Vector2 dir, Color accent, int count)
    {
        Ensure();
        float baseAngle = Mathf.Atan2(dir.y, dir.x);
        for (int n = 0; n < count; n++)
        {
            int i = next; next = (next + 1) % ChipPool;
            if (age[i] < 0f) active++;
            float a = baseAngle + (Random.value * 2f - 1f) * ChipSpread;
            float speed = Mathf.Lerp(ChipSpeedMin, ChipSpeedMax, Random.value);
            vel[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed;
            age[i] = 0f;
            size[i] = Mathf.Lerp(ChipSizeMin, ChipSizeMax, Random.value);
            tint[i] = n == 0 ? Color.Lerp(accent, Color.white, .6f) : accent;   // (one bright fleck)
            var t = sr[i].transform;
            t.position = at;
            t.localScale = new Vector3(size[i], size[i], 1f);
            sr[i].color = tint[i];
            sr[i].enabled = true;
        }
    }

    // One frame of the pool (dt explicit for tests).
    public static void Step(float dt)
    {
        if (root == null) { active = 0; return; }       // (the scene went)
        if (active <= 0 || sr == null || dt <= 0f) return;
        for (int i = 0; i < ChipPool; i++)
        {
            if (age[i] < 0f) continue;
            age[i] += dt;
            if (age[i] >= ChipLife)
            {
                age[i] = -1f; sr[i].enabled = false; active--;
                continue;
            }
            float k = age[i] / ChipLife;
            var t = sr[i].transform;
            Vector3 p = t.position;
            float drag = 1f - k * .7f;
            p.x += vel[i].x * drag * dt; p.y += vel[i].y * drag * dt;
            t.position = p;
            Color c = tint[i];
            c.a = 1f - k * k;
            sr[i].color = c;
        }
    }

    public static void Reset()
    {
        if (root != null) { if (Application.isPlaying) Object.Destroy(root.gameObject); else Object.DestroyImmediate(root.gameObject); }
        root = null; sr = null; active = 0; next = 0;
    }

    sealed class Driver : MonoBehaviour
    {
        void Update() { Step(Time.deltaTime); }
    }
}
