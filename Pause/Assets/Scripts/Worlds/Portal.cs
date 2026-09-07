using System;
using UnityEngine;

// The gateway between planets.
//
// Built from primitives at runtime -- a glowing ring that drifts down the
// screen like everything else. Flying into it advances the world.
public class Portal : MonoBehaviour
{
    public float fallSpeed = 1.6f;
    Action onMissed;
    float life;
    SpriteRenderer ring, core;
    float spin;

    public static Portal Spawn(Color color, float lifetime, Action onMissed)
    {
        var go = new GameObject("~Portal");
        go.transform.position = new Vector3(UnityEngine.Random.Range(-1.6f, 1.6f), 7f, 0f);

        var p = go.AddComponent<Portal>();
        p.life = lifetime;
        p.onMissed = onMissed;
        p.Build(color);
        return p;
    }

    void Build(Color color)
    {
        ring = MakePart("Ring", PortalArt.Ring(), color, 0);
        core = MakePart("Core", PortalArt.Core(), new Color(1f, 1f, 1f, 0.85f), 1);
        core.transform.localScale = Vector3.one * 0.55f;

        var col = gameObject.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = 0.55f;

        var rb = gameObject.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;

        WorldBanner.Show("PORTAL OPEN");
    }

    SpriteRenderer MakePart(string name, Sprite sprite, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = 50 + order;
        return sr;
    }

    void Update()
    {
        // Drift down only while the world is actually moving, so a paused game
        // does not quietly lose the portal.
        bool running = !buttonClicks.playerDied &&
                       (TouchInput.IsPressed || score.pauseCounter <= 0);
        if (!running) return;

        transform.position += Vector3.down * fallSpeed * Time.deltaTime;

        spin += Time.deltaTime * 90f;
        if (ring != null) ring.transform.localRotation = Quaternion.Euler(0, 0, spin);
        if (core != null)
        {
            float pulse = 0.5f + Mathf.PingPong(Time.time * 0.6f, 0.25f);
            core.transform.localScale = Vector3.one * pulse;
        }

        life -= Time.deltaTime;
        if (life <= 0f || transform.position.y < -8f) Miss();
    }

    void Miss()
    {
        var cb = onMissed;
        onMissed = null;
        Destroy(gameObject);
        if (cb != null) cb();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<movePlayer>() == null &&
            other.GetComponentInParent<movePlayer>() == null) return;

        onMissed = null;
        if (WorldManager.Instance != null) WorldManager.Instance.Advance();
        Destroy(gameObject);
    }
}

// Portal sprites, drawn once into textures so no art assets are required.
public static class PortalArt
{
    static Sprite ring, core;

    public static Sprite Ring()
    {
        if (ring == null) ring = Build(128, 0.62f, 0.94f);
        return ring;
    }

    public static Sprite Core()
    {
        if (core == null) core = Build(128, 0f, 0.72f);
        return core;
    }

    static Sprite Build(int size, float innerR, float outerR)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;

        float c = (size - 1) / 2f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            float a;
            if (d > outerR) a = 0f;
            else if (d < innerR) a = 0f;
            else
            {
                // soft falloff at both edges so the ring glows rather than cuts
                float outerFade = Mathf.InverseLerp(outerR, outerR - 0.14f, d);
                float innerFade = innerR <= 0f ? 1f : Mathf.InverseLerp(innerR, innerR + 0.14f, d);
                a = Mathf.Clamp01(Mathf.Min(outerFade, innerFade));
            }
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
