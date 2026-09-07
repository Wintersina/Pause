using System.Collections;
using UnityEngine;

// The pause-teleport: lift your finger, touch somewhere else, and the ship
// blinks across the screen.
//
// Until now that was a silent position snap. This gives it a departure and
// arrival flash, and lets the ship clear whatever it materialises on top of --
// so teleporting into a cluster is a real offensive option rather than a way to
// die instantly.
public class TeleportFx : MonoBehaviour
{
    [Tooltip("Anything tagged Enimey or Astr this close to the landing point is " +
             "destroyed on arrival.")]
    public static float BlastRadius = 0.95f;

    [Tooltip("Ignore tiny nudges -- only a real jump counts as a teleport.")]
    public static float MinimumJump = 0.85f;

    static TeleportFx runner;
    static Sprite ring;

    public static void Play(Vector3 from, Vector3 to)
    {
        if (Vector3.Distance(from, to) < MinimumJump) return;

        Ensure();
        runner.StartCoroutine(Flash(from, 0.9f, 1.6f, new Color(0.55f, 0.85f, 1f, 0.85f)));
        runner.StartCoroutine(Flash(to, 1.7f, 0.5f, new Color(1f, 0.95f, 0.7f, 0.95f)));
        Strike(to);
    }

    static void Ensure()
    {
        if (runner == null) runner = new GameObject("~TeleportFx").AddComponent<TeleportFx>();
    }

    // Destroy what we landed on, reusing the game's own explosion art.
    static void Strike(Vector3 at)
    {
        var explosion = Resources.Load<GameObject>("Prefabs/explosion_0");

        foreach (var col in Physics2D.OverlapCircleAll(at, BlastRadius))
        {
            var go = col.gameObject;
            if (!go.CompareTag("Enimey") && !go.CompareTag("Astr")) continue;

            if (explosion != null)
            {
                var fx = Instantiate(explosion, go.transform.position, Quaternion.identity);
                fx.AddComponent<moveItemEnmInStrightLine>();
                Destroy(fx, 2f);
            }
            Destroy(go);
        }
    }

    // Unscaled time: a teleport begins while the world is still frozen.
    static IEnumerator Flash(Vector3 at, float startScale, float endScale, Color tint)
    {
        var go = new GameObject("~TeleportRing");
        go.transform.position = at;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Ring();
        sr.color = tint;
        sr.sortingOrder = 60;

        const float life = 0.32f;
        for (float t = 0f; t < life; t += Time.unscaledDeltaTime)
        {
            float k = t / life;
            go.transform.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, k);
            var c = sr.color; c.a = tint.a * (1f - k); sr.color = c;
            yield return null;
        }
        Destroy(go);
    }

    static Sprite Ring()
    {
        if (ring != null) return ring;

        const int S = 128;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float c = (S - 1) / 2f;

        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            // a soft annulus: bright at the rim, hollow in the middle
            float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.78f) / 0.20f);
            if (d > 1f) a = 0f;
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        ring = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        return ring;
    }
}
