using UnityEngine;

// Damage is readable on every roster entry, including the legacy hulls whose
// source sheets do not include authored damaged frames: scorched pin flames
// cling to the ship and tiny bursts peel away while it is hurt.
public class ShipDamageFx : MonoBehaviour
{
    SpriteRenderer[] flames;
    float nextBurst;

    void Start()
    {
        var hull = GetComponent<SpriteRenderer>();
        var texture = Resources.Load<Texture2D>("Prefabs/Vfx/vfx_flare_01");
        if (texture == null) return;
        var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                                   new Vector2(.5f, .5f), 100f);
        float x = hull != null && hull.sprite != null ? hull.sprite.bounds.extents.x * .45f : .2f;
        float y = hull != null && hull.sprite != null ? hull.sprite.bounds.extents.y * .1f : 0f;
        flames = new SpriteRenderer[2];
        for (int i = 0; i < flames.Length; i++)
        {
            var go = new GameObject("~DamageFlame" + i, typeof(SpriteRenderer));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(i == 0 ? -x : x, y, -.03f);
            go.transform.localScale = Vector3.one * .12f;
            flames[i] = go.GetComponent<SpriteRenderer>();
            flames[i].sprite = sprite;
            flames[i].sortingOrder = (hull != null ? hull.sortingOrder : 0) + 2;
            flames[i].color = new Color(1f, .38f, .08f, 0f);
        }
    }

    void Update()
    {
        int damage = collisionDetection.lifeCounter;
        bool hurt = damage > 0 && !buttonClicks.playerDied;
        if (flames != null)
        {
            for (int i = 0; i < flames.Length; i++)
            {
                if (flames[i] == null) continue;
                float flicker = .6f + .4f * Mathf.Sin(Time.unscaledTime * (12f + i * 3f));
                flames[i].color = new Color(1f, .35f + .2f * flicker, .06f, hurt ? .82f * flicker : 0f);
                flames[i].transform.localScale = Vector3.one * (.09f + damage * .035f + flicker * .035f);
            }
        }
        if (hurt && Time.unscaledTime >= nextBurst)
        {
            nextBurst = Time.unscaledTime + Mathf.Max(.3f, .75f - damage * .12f);
            PowerFx.Burst(transform.position + Random.insideUnitSphere * .18f, new Color(1f, .3f, .08f), 3);
        }
    }
}
