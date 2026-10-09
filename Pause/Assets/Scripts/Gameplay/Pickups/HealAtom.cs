using UnityEngine;
using UnityEngine.SceneManagement;

// The green atom: a rare pickup that repairs one point of hull damage.
//
// Built entirely in code -- sprite included -- so it needs no prefab, no scene
// wiring and no art drop. It carries the same "pickUp" tag and the same world
// scroller as every other collectable, so collisionDetection and the pause
// logic treat it like anything else.
public class HealAtom : MonoBehaviour
{
    public const string ObjectName = "healAtom";
    public const float DespawnBelowView = 3f;

    static Sprite cached;

    // World-space diameter of the authored red/blue atoms: 28 px at 100 PPU.
    public const float TargetDiameter = 28f / 100f;

    public static float VisualScaleFor(Sprite sprite)
    {
        float source = sprite != null ? Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y) : TargetDiameter;
        return TargetDiameter / Mathf.Max(0.0001f, source);
    }

    public static GameObject Spawn(Vector3 position)
    {
        var go = new GameObject(ObjectName);
        go.tag = "pickUp";
        go.transform.position = position;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Art();
        sr.sortingOrder = 8;

        // The generated art is high resolution, while the authored red/blue
        // atoms are 28 px sprites at 100 PPU. Match their world-space size.
        float visualScale = VisualScaleFor(sr.sprite);
        go.transform.localScale = new Vector3(visualScale, visualScale, 1f);

        // 0.14 world units of radius, the same footprint as the red/blue
        // atoms' 0.28 box -- expressed in local space so the scale cancels.
        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = (TargetDiameter * 0.5f) / visualScale;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        // same scroller the asteroids and stars use
        go.AddComponent<moveItemEnmInStrightLine>();
        go.AddComponent<HealAtom>();
        go.AddComponent<AtomSpin>();
        // Its idle animation: light overlays flipped on top of the untouched
        // original art (electron glints in sequence, then a nucleus pulse).
        PickupFlipbook.AddTo(go, PickupKind.Heal);
        // the friendly look: bigger, a soft halo and an orbit ring
        PickupGlow.Dress(go);

        return go;
    }

    void Update()
    {
        // The animation lives in PickupFlipbook (sprite overlays). Nothing here
        // may touch transform.localScale: an old pulse around 1.0 threw away
        // the ~0.04 fit scale set in Spawn and drew the atom ~25x too large.
        // A backstop (AtomWander removes it one unit under the view): well
        // under the visible bottom on every screen (it was a fixed -12).
        if (transform.position.y < CameraFit.ViewBottom - DespawnBelowView) Destroy(gameObject);
    }

    // Square crop around the opaque art of heal_atom_green.png (1254 x 1254).
    // Falls back to the full texture if the art is ever replaced at another size.
    public static Rect ArtRect(int width, int height)
    {
        if (width != 1254 || height != 1254) return new Rect(0, 0, width, height);
        const float side = 944f;                 // tallest extent with alpha > 1
        return new Rect(627f - side * 0.5f, 634.5f - side * 0.5f, side, side);
    }

    // A green nucleus with three orbiting lobes, echoing the existing atoms.
    static Sprite Art()
    {
        if (cached != null) return cached;

        // Authored to match the red and blue pickup family: compact nucleus,
        // three orbiting lobes and the same hard pixel outline.
        var authored = Resources.Load<Texture2D>("Pickups/heal_atom_green");
        if (authored != null)
        {
            // The 1254 px canvas has wide transparent margins (the opaque art
            // spans only x 210-1044, y 163-1106 measured from the bottom),
            // while the red/blue sprites fill their 28 px canvas edge to edge.
            // Crop to a square around the art so the sprite bounds are the
            // visible atom and the fit scale matches what players see.
            cached = Sprite.Create(authored, ArtRect(authored.width, authored.height),
                new Vector2(.5f, .5f), 180f);
            return cached;
        }

        const int S = 96;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;

        var core = new Color(0.44f, 1f, 0.52f);
        var lobe = new Color(0.16f, 0.78f, 0.36f);
        var ink = new Color(0.05f, 0.16f, 0.08f);

        var pixels = new Color[S * S];
        float c = (S - 1) / 2f;

        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float dx = (x - c) / c, dy = (y - c) / c;
            Color col = new Color(0, 0, 0, 0);

            // three lobes at 90, 210, 330 degrees
            for (int i = 0; i < 3; i++)
            {
                float a = Mathf.Deg2Rad * (90f + i * 120f);
                float lx = dx - Mathf.Cos(a) * 0.42f;
                float ly = dy - Mathf.Sin(a) * 0.42f;
                float d = Mathf.Sqrt(lx * lx + ly * ly);
                if (d < 0.30f) col = d > 0.24f ? ink : lobe;
            }

            float dc = Mathf.Sqrt(dx * dx + dy * dy);
            if (dc < 0.34f) col = dc > 0.28f ? ink : core;

            // a small cross in the nucleus reads as "heal"
            if (dc < 0.22f && (Mathf.Abs(dx) < 0.06f || Mathf.Abs(dy) < 0.06f))
                col = new Color(0.92f, 1f, 0.94f);

            pixels[y * S + x] = col;
        }

        tex.SetPixels(pixels);
        tex.Apply();
        cached = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        return cached;
    }
}
