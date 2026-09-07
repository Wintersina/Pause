using System.Collections;
using UnityEngine;

// Visuals for the ship powers.
//
// Firing a power used to change the world silently -- enemies simply vanished,
// with nothing to connect the second touch to the result. Each power now draws
// its own effect from the CC0 Kenney particle set, so the player can see what
// their ship just did.
public static class PowerFx
{
    static PowerFxRunner runner;

    static PowerFxRunner Runner
    {
        get
        {
            if (runner == null)
                runner = new GameObject("~PowerFx").AddComponent<PowerFxRunner>();
            return runner;
        }
    }

    static Sprite Load(string file)
    {
        var tex = Resources.Load<Texture2D>("Prefabs/Vfx/" + file);
        if (tex == null) return null;
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
    }

    static GameObject Piece(string file, Vector3 at, Color tint, float scale, int order = 70)
    {
        var go = new GameObject("~fx");
        go.transform.position = at;
        go.transform.localScale = Vector3.one * scale;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Load(file);
        sr.color = tint;
        sr.sortingOrder = order;
        return go;
    }

    // A lance of light down the firing lane.
    public static void Laser(Vector3 from, float width, float length, Color tint)
    {
        var go = Piece("vfx_trace_01", from + Vector3.up * (length * 0.5f), tint, 1f);
        var sr = go.GetComponent<SpriteRenderer>();
        if (sr.sprite != null)
        {
            float sx = width / sr.sprite.bounds.size.x;
            float sy = length / sr.sprite.bounds.size.y;
            go.transform.localScale = new Vector3(sx, sy, 1f);
        }
        Runner.StartCoroutine(FadeOut(go, 0.35f));

        var flash = Piece("vfx_muzzle_02", from, tint, 1.2f);
        Runner.StartCoroutine(FadeOut(flash, 0.2f));
    }

    // An expanding ring, used for the shockwave and the cloak.
    public static void Ring(Vector3 at, float radius, Color tint, float seconds = 0.45f)
    {
        var go = Piece("vfx_circle_05", at, tint, 0.2f);
        Runner.StartCoroutine(Expand(go, radius, seconds));
    }

    // Little darts flying out toward what the missiles hit.
    public static void Missiles(Vector3 from, Vector3[] targets, Color tint)
    {
        foreach (var t in targets)
        {
            var go = Piece("vfx_spark_05", from, tint, 0.5f);
            Runner.StartCoroutine(FlyTo(go, t, 0.28f));
        }
    }

    public static void Burst(Vector3 at, Color tint, int count = 10)
    {
        for (int i = 0; i < count; i++)
        {
            var go = Piece("vfx_star_08", at, tint, Random.Range(0.25f, 0.5f));
            float a = Random.Range(0f, Mathf.PI * 2f);
            Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * Random.Range(0.8f, 2.2f);
            Runner.StartCoroutine(FlyTo(go, at + dir, Random.Range(0.3f, 0.6f)));
        }
    }

    public static void Aura(Vector3 at, Color tint, float seconds)
    {
        var go = Piece("vfx_light_02", at, tint, 1.6f, 40);
        Runner.StartCoroutine(FollowAndFade(go, seconds));
    }

    // ---- coroutines (unscaled: powers can fire while the world is frozen) ----

    static IEnumerator FadeOut(GameObject go, float seconds)
    {
        var sr = go.GetComponent<SpriteRenderer>();
        Color start = sr.color;
        for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
        {
            if (sr == null) yield break;
            var c = start; c.a = start.a * (1f - t / seconds); sr.color = c;
            yield return null;
        }
        Object.Destroy(go);
    }

    static IEnumerator Expand(GameObject go, float radius, float seconds)
    {
        var sr = go.GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null) { Object.Destroy(go); yield break; }

        float target = radius * 2f / sr.sprite.bounds.size.x;
        Color start = sr.color;
        for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
        {
            float k = t / seconds;
            go.transform.localScale = Vector3.one * Mathf.Lerp(0.2f, target, k);
            var c = start; c.a = start.a * (1f - k); sr.color = c;
            yield return null;
        }
        Object.Destroy(go);
    }

    static IEnumerator FlyTo(GameObject go, Vector3 to, float seconds)
    {
        Vector3 from = go.transform.position;
        var sr = go.GetComponent<SpriteRenderer>();
        Color start = sr != null ? sr.color : Color.white;
        for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
        {
            float k = t / seconds;
            go.transform.position = Vector3.Lerp(from, to, k);
            if (sr != null) { var c = start; c.a = start.a * (1f - k); sr.color = c; }
            yield return null;
        }
        Object.Destroy(go);
    }

    static IEnumerator FollowAndFade(GameObject go, float seconds)
    {
        var player = Object.FindFirstObjectByType<movePlayer>();
        var sr = go.GetComponent<SpriteRenderer>();
        Color start = sr != null ? sr.color : Color.white;
        for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
        {
            if (player != null) go.transform.position = player.transform.position;
            if (sr != null)
            {
                var c = start;
                c.a = start.a * (0.55f + 0.45f * Mathf.Sin(t * 9f)) * (1f - t / seconds);
                sr.color = c;
            }
            yield return null;
        }
        Object.Destroy(go);
    }
}

public class PowerFxRunner : MonoBehaviour { }
