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

    static Sprite cached;

    public static GameObject Spawn(Vector3 position)
    {
        var go = new GameObject(ObjectName);
        go.tag = "pickUp";
        go.transform.position = position;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Art();
        sr.sortingOrder = 8;

        var col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.26f;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        // same scroller the asteroids and stars use
        go.AddComponent<moveItemEnmInStrightLine>();
        go.AddComponent<HealAtom>();

        return go;
    }

    void Update()
    {
        // gentle pulse so it reads as special against the red and blue atoms
        float k = 1f + Mathf.Sin(Time.time * 4f) * 0.08f;
        transform.localScale = new Vector3(k, k, 1f);

        if (transform.position.y < -12f) Destroy(gameObject);
    }

    // A green nucleus with three orbiting lobes, echoing the existing atoms.
    static Sprite Art()
    {
        if (cached != null) return cached;

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
