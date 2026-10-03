using UnityEngine;

// Gives every spawned hull the same visible blue shield, including ships that
// were never authored with the old Amadeus-only shield child.
public class ShipShieldBubble : MonoBehaviour
{
    GameObject bubble;

    public static ShipShieldBubble For(GameObject ship)
    {
        var shield = ship.GetComponent<ShipShieldBubble>();
        if (shield == null) shield = ship.AddComponent<ShipShieldBubble>();
        shield.Ensure();
        return shield;
    }

    public void Show(float padding)
    {
        Ensure();
        if (bubble == null) return;
        bubble.SetActive(true);
        Fit(padding);
    }

    public void Hide()
    {
        if (bubble != null) bubble.SetActive(false);
    }

    public GameObject Visual { get { Ensure(); return bubble; } }

    void Ensure()
    {
        if (bubble != null) return;
        var existing = transform.Find("Shield") ?? transform.Find("shield");
        if (existing != null) bubble = existing.gameObject;
        else
        {
            var prefab = Resources.Load<GameObject>("Prefabs/Amadeus-Shild");
            bubble = prefab != null ? Instantiate(prefab) : BuildFallback();
            bubble.name = "Shield";
            bubble.transform.SetParent(transform, false);
        }
        bubble.transform.localPosition = Vector3.zero;
        var sr = bubble.GetComponent<SpriteRenderer>();
        var hull = GetComponent<SpriteRenderer>();
        if (sr != null) sr.sortingOrder = (hull != null ? hull.sortingOrder : 0) + 3;
        bubble.SetActive(false);
    }

    void Fit(float padding)
    {
        var hull = GetComponent<SpriteRenderer>();
        var sr = bubble != null ? bubble.GetComponent<SpriteRenderer>() : null;
        if (hull == null || hull.sprite == null || sr == null || sr.sprite == null) return;
        float parentScale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y);
        float scale = collisionDetection.ComputeShieldLocalScale(hull.sprite.bounds.extents,
            parentScale, sr.sprite.bounds.extents, padding);
        if (scale > 0f) bubble.transform.localScale = new Vector3(scale, scale, 1f);
    }

    static GameObject BuildFallback()
    {
        var go = new GameObject("Shield", typeof(SpriteRenderer));
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        float c = (S - 1) * .5f;
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) / c;
            float a = Mathf.Clamp01(1f - Mathf.Abs(d - .82f) / .13f) * .62f;
            tex.SetPixel(x, y, new Color(.3f, .82f, 1f, a));
        }
        tex.Apply();
        go.GetComponent<SpriteRenderer>().sprite = Sprite.Create(tex, new Rect(0, 0, S, S),
            new Vector2(.5f, .5f), 100f);
        return go;
    }
}
