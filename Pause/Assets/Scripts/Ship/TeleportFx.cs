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
    static AudioClip warpSound;

    public static void Play(Vector3 from, Vector3 to)
    {
        if (Vector3.Distance(from, to) < MinimumJump) return;

        Ensure();
        PlaySound();
        runner.StartCoroutine(Flash(from, 0.9f, 1.6f, new Color(0.55f, 0.85f, 1f, 0.85f)));
        runner.StartCoroutine(Flash(to, 1.7f, 0.5f, new Color(1f, 0.95f, 0.7f, 0.95f)));
        Strike(to);
    }

    // Feedback for a teleport refused on cooldown -- a dull, collapsing ring
    // rather than the bright flare of a successful jump, so the two never read
    // as the same thing.
    public static void Denied(Vector3 at)
    {
        Ensure();
        runner.StartCoroutine(Flash(at, 1.1f, 0.6f, new Color(0.75f, 0.78f, 0.85f, 0.45f)));
    }

    static void Ensure()
    {
        if (runner == null) runner = new GameObject("~TeleportFx").AddComponent<TeleportFx>();
    }

    // A compact 80s-style rising warp followed by a bright arrival chime.
    // The clip lives in Resources so this effect remains self-contained like
    // the portal visuals and needs no AudioSource wired in a scene.
    static void PlaySound()
    {
        if (warpSound == null) warpSound = Resources.Load<AudioClip>("Audio/teleport_warp");
        if (warpSound == null || runner == null) return;

        var source = runner.GetComponent<AudioSource>();
        if (source == null) source = runner.gameObject.AddComponent<AudioSource>();
        source.spatialBlend = 0f;
        source.PlayOneShot(warpSound, 0.78f);
    }

    // Destroy what we landed on, reusing the game's own explosion art.
    static void Strike(Vector3 at)
    {
        var explosion = Resources.Load<GameObject>("Prefabs/explosion_0");

        foreach (var col in Physics2D.OverlapCircleAll(at, BlastRadius))
        {
            var go = col.gameObject;
            if (!go.CompareTag("Enimey") && !go.CompareTag("Astr")) continue;

            collisionDetection.PlayExplosion();

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
        var go = new GameObject("~TeleportVortex");
        go.transform.position = at;

        var outer = MakeLayer(go.transform, "Vortex", tint, 60, 0);
        var inner = MakeLayer(go.transform, "Core", new Color(0.65f, 0.92f, 1f, tint.a * 0.72f), 61, 5);
        var sparks = MakeLayer(go.transform, "Sparks", new Color(1f, 0.46f, 1f, tint.a * 0.38f), 62, 10);
        inner.transform.localScale = Vector3.one * 0.72f;
        sparks.transform.localScale = Vector3.one * 1.15f;

        const float life = 0.32f;
        for (float t = 0f; t < life; t += Time.unscaledDeltaTime)
        {
            float k = t / life;
            go.transform.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, k);
            int frame = Mathf.FloorToInt(t * 35f);
            outer.sprite = TeleportPortalSprites.FrameAt(frame);
            inner.sprite = TeleportPortalSprites.FrameAt(frame + 5);
            sparks.sprite = TeleportPortalSprites.FrameAt(frame + 10);
            outer.transform.localRotation = Quaternion.Euler(0, 0, frame * 16f);
            inner.transform.localRotation = Quaternion.Euler(0, 0, -frame * 10f);
            sparks.transform.localRotation = Quaternion.Euler(0, 0, frame * 24f);
            Fade(outer, tint.a * (1f - k));
            Fade(inner, tint.a * 0.72f * (1f - k));
            Fade(sparks, tint.a * 0.38f * (1f - k));
            yield return null;
        }
        Destroy(go);
    }

    static SpriteRenderer MakeLayer(Transform parent, string name, Color tint, int order, int frame)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = TeleportPortalSprites.FrameAt(frame);
        sr.color = tint;
        sr.sortingOrder = order;
        return sr;
    }

    static void Fade(SpriteRenderer renderer, float alpha)
    {
        var color = renderer.color;
        color.a = alpha;
        renderer.color = color;
    }
}
