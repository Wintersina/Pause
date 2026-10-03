using System;
using UnityEngine;

// Animated damage dressing shared by every playable hull. The hull sprite
// stays in its authored colours; this layer supplies fire, smoke, embers and
// small impact bursts while the ship is hurt.
public class ShipDamageFx : MonoBehaviour
{
    SpriteRenderer[] flames;
    SpriteRenderer smoke;
    SpriteRenderer[] explosions;
    Sprite[] frames;
    int shipOffset;
    float halfWidth;
    float halfHeight;

    const int AtlasColumns = 4;
    const int AtlasRows = 4;
    const float FramesPerSecond = 14f;

    void Start()
    {
        var hull = GetComponent<SpriteRenderer>();
        if (hull != null && hull.sprite != null)
        {
            halfWidth = hull.sprite.bounds.extents.x;
            halfHeight = hull.sprite.bounds.extents.y;
        }
        else
        {
            halfWidth = .2f;
            halfHeight = .2f;
        }

        shipOffset = ResolveShipIndex() * 3;
        var atlas = Resources.Load<Texture2D>("Vfx/ship_damage_fx_atlas");
        if (atlas == null) return;
        frames = Slice(atlas);

        int order = hull != null ? hull.sortingOrder + 2 : 2;
        flames = new SpriteRenderer[2];
        flames[0] = Create("~DamageFlame0", new Vector3(-halfWidth * .38f, -halfHeight * .30f, -.03f), order, .30f);
        flames[1] = Create("~DamageFlame1", new Vector3(halfWidth * .38f, -halfHeight * .30f, -.03f), order, .26f);
        smoke = Create("~DamageSmoke", new Vector3(0f, -halfHeight * .64f, -.02f), order - 1, .42f);
        explosions = new SpriteRenderer[2];
        explosions[0] = Create("~DamageExplosion0", new Vector3(-halfWidth * .60f, -halfHeight * .02f, -.04f), order + 1, .22f);
        explosions[1] = Create("~DamageExplosion1", new Vector3(halfWidth * .58f, halfHeight * .08f, -.04f), order + 1, .20f);
    }

    void Update()
    {
        if (frames == null || frames.Length == 0) return;
        int damage = Mathf.Clamp(collisionDetection.lifeCounter, 0, 2);
        bool hurt = damage > 0 && !buttonClicks.playerDied;
        float t = Time.unscaledTime * FramesPerSecond + shipOffset;
        int frame = Mathf.FloorToInt(t);
        float pulse = .82f + Mathf.Sin(Time.unscaledTime * 9f + shipOffset) * .12f;

        SetAnimated(flames[0], frames[(frame + 0) % 8], hurt ? pulse : 0f);
        SetAnimated(flames[1], frames[(frame + 2) % 8], hurt ? pulse * .82f : 0f);
        SetAnimated(smoke, frames[8 + ((frame / 2) % 8)], hurt ? .48f + damage * .12f : 0f);

        for (int i = 0; i < explosions.Length; i++)
        {
            float cadence = Mathf.Repeat(Time.unscaledTime * (2.0f + damage * .55f) + i * .47f, 1f);
            float alpha = hurt && damage >= 2 && cadence < .34f
                ? Mathf.Sin(cadence / .34f * Mathf.PI) * .92f
                : 0f;
            SetAnimated(explosions[i], frames[4 + ((frame + i * 3) % 8)], alpha);
        }
    }

    SpriteRenderer Create(string name, Vector3 position, int order, float scale)
    {
        var go = new GameObject(name, typeof(SpriteRenderer));
        go.transform.SetParent(transform, false);
        go.transform.localPosition = position;
        go.transform.localScale = Vector3.one * scale;
        var renderer = go.GetComponent<SpriteRenderer>();
        renderer.sortingOrder = order;
        renderer.color = new Color(1f, 1f, 1f, 0f);
        return renderer;
    }

    void SetAnimated(SpriteRenderer renderer, Sprite sprite, float alpha)
    {
        if (renderer == null) return;
        renderer.sprite = sprite;
        var c = renderer.color;
        c.a = Mathf.Clamp01(alpha);
        renderer.color = c;
    }

    static Sprite[] Slice(Texture2D atlas)
    {
        var result = new Sprite[AtlasColumns * AtlasRows];
        float width = atlas.width / (float)AtlasColumns;
        float height = atlas.height / (float)AtlasRows;
        int i = 0;
        for (int row = AtlasRows - 1; row >= 0; row--)
            for (int col = 0; col < AtlasColumns; col++)
            {
                var rect = new Rect(col * width, row * height, width, height);
                result[i] = Sprite.Create(atlas, rect, new Vector2(.5f, .5f), 700f);
                result[i].name = "shipDamageFrame" + i;
                i++;
            }
        return result;
    }

    int ResolveShipIndex()
    {
        string name = gameObject.name.Replace("(Clone)", "").Replace("ship", "");
        int index;
        return Int32.TryParse(name, out index) ? Mathf.Max(0, index) : 0;
    }
}
