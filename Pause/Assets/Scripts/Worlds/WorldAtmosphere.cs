using System.Collections.Generic;
using UnityEngine;

// Lightweight, non-interactive atmosphere layered behind hazards. Each theme
// gets a readable kind of motion without adding collision objects or relying
// on a separate particle prefab in every scene.
public class WorldAtmosphere : MonoBehaviour
{
    class Mote
    {
        public Transform transform;
        public SpriteRenderer renderer;
        public float speed;
        public float drift;
        public float phase;
        public float size;
    }

    static WorldAtmosphere instance;
    static Sprite moteSprite;
    readonly List<Mote> motes = new List<Mote>();
    SpriteRenderer groundGlow;
    int world;

    public static void Apply(WorldTheme theme)
    {
        if (theme == null) return;
        if (instance == null)
            instance = new GameObject("~WorldAtmosphere").AddComponent<WorldAtmosphere>();
        instance.Configure(theme.displayName);
    }

    void Configure(string displayName)
    {
        world = displayName == "Frost" ? 1 : displayName == "Verdant" ? 2 : displayName == "Ember" ? 3 : 0;
        foreach (var mote in motes)
            if (mote.transform != null) Destroy(mote.transform.gameObject);
        motes.Clear();
        if (groundGlow != null) Destroy(groundGlow.gameObject);
        groundGlow = null;

        // Space has a modest particulate drift; the planets have denser,
        // distinct weather. These sprites never receive colliders.
        int count = world == 0 ? 10 : world == 3 ? 20 : 16;
        for (int i = 0; i < count; i++) CreateMote();
        if (world == 3) CreateLavaFloor();
    }

    void CreateMote()
    {
        var go = new GameObject("AtmosphereMote");
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(Random.Range(-2.35f, 2.35f), Random.Range(-5.2f, 5.2f), 0.4f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetMoteSprite();
        sr.sortingOrder = -2;

        Color color;
        if (world == 1) color = new Color(0.72f, 0.94f, 1f, 0.56f);       // falling ice
        else if (world == 2) color = new Color(0.55f, 1f, 0.60f, 0.52f);  // rising spores
        else if (world == 3) color = new Color(1f, 0.34f, 0.08f, 0.62f); // lava embers
        else color = new Color(0.40f, 0.94f, 1f, 0.34f);                  // space dust
        sr.color = color;

        float size = world == 3 ? Random.Range(0.045f, 0.12f) : Random.Range(0.025f, 0.075f);
        go.transform.localScale = Vector3.one * size;
        motes.Add(new Mote {
            transform = go.transform, renderer = sr, size = size,
            speed = world == 3 ? Random.Range(0.65f, 1.65f) : Random.Range(0.12f, 0.55f),
            drift = Random.Range(0.12f, 0.40f), phase = Random.value * 6.283185f,
        });
    }

    void CreateLavaFloor()
    {
        var go = new GameObject("LavaGroundGlow");
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(0f, -4.92f, 0.45f);
        groundGlow = go.AddComponent<SpriteRenderer>();
        groundGlow.sprite = GetMoteSprite();
        groundGlow.color = new Color(1f, 0.12f, 0.015f, 0.43f);
        groundGlow.sortingOrder = -2;
        go.transform.localScale = new Vector3(5.5f, 0.38f, 1f);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < motes.Count; i++)
        {
            var m = motes[i];
            if (m.transform == null) continue;
            Vector3 p = m.transform.position;
            float direction = (world == 1 || world == 0) ? -1f : 1f;
            p.y += direction * m.speed * dt;
            p.x += Mathf.Sin(Time.unscaledTime * 1.8f + m.phase) * m.drift * dt;
            if (p.y > 5.35f) p.y = -5.28f;
            if (p.y < -5.35f) p.y = 5.28f;
            m.transform.position = p;

            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 7f + m.phase) * 0.18f;
            m.transform.localScale = Vector3.one * (m.size * pulse);
        }
        if (groundGlow != null)
        {
            float breath = 0.38f + Mathf.Sin(Time.unscaledTime * 3.2f) * 0.10f;
            groundGlow.color = new Color(1f, 0.12f + breath * 0.18f, 0.015f, breath);
        }
    }

    static Sprite GetMoteSprite()
    {
        if (moteSprite != null) return moteSprite;
        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x - (size - 1) * 0.5f) / (size * 0.5f);
            float dy = (y - (size - 1) * 0.5f) / (size * 0.5f);
            float a = Mathf.Clamp01(1f - (dx * dx + dy * dy));
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
        }
        tex.Apply();
        moteSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return moteSprite;
    }
}
