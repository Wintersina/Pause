using UnityEngine;

// Later-wave fighters use their authored tier hull plus visible shoulder plates, never hidden coin-scaled HP.
public static class EnemyArmour
{
    static Sprite veteran, armoured;
    public static void Prewarm() { Art(false); Art(true); }
    static Sprite Art(bool heavy)
    {
        if (heavy ? armoured != null : veteran != null) return heavy ? armoured : veteran;
        const int width = 24, height = 16;
        var pixels = new Color32[width * height];
        Color plate = heavy ? new Color(.36f, .65f, .88f) : new Color(1f, .69f, .23f);
        for (int y = 3; y < height - 3; y++)
            for (int x = 0; x < width; x++)
            {
                int edge = Mathf.Min(x, width - 1 - x);
                if (edge > (heavy ? 1 : 0)) continue;
                bool rim = edge == 0 || y == 3 || y == height - 4;
                pixels[y * width + x] = rim ? Color.Lerp(plate, Color.white, .65f) : plate;
            }
        int pips = heavy ? 4 : 3;
        for (int i = 0; i < pips; i++)
            for (int y = height - 2; y < height; y++)
                for (int x = 0; x < 2; x++) pixels[y * width + 7 + i * 3 + x] = plate;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.name = heavy ? "ArmouredHullPlates" : "VeteranHullPlates";
        texture.filterMode = FilterMode.Point; texture.hideFlags = HideFlags.HideAndDontSave;
        texture.SetPixels32(pixels); texture.Apply();
        var sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f), 24f, 0, SpriteMeshType.FullRect);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        if (heavy) armoured = sprite; else veteran = sprite;
        return sprite;
    }
    public static void Mark(GameObject enemy, int hearts)
    {
        if (hearts <= 2 || !enemy.TryGetComponent(out SpriteRenderer hull) || hull.sprite == null) return;
        var go = new GameObject(hearts >= 4 ? "ArmouredPlates" : "VeteranPlates", typeof(SpriteRenderer));
        go.transform.SetParent(enemy.transform, false);
        go.transform.localPosition = hull.sprite.bounds.center;
        go.transform.localScale = Vector3.one * hull.sprite.bounds.size.x * .6f;
        var renderer = go.GetComponent<SpriteRenderer>(); renderer.sprite = Art(hearts >= 4); renderer.sortingOrder = hull.sortingOrder + 1;
    }
}
