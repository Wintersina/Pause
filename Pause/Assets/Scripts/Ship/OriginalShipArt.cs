using UnityEngine;
using System.Collections.Generic;

// Explicit single-hull rectangles: legacy strips contain animation/banking poses,
// not health states. Keep their silhouette stable as damage increases.
public static class OriginalShipArt
{
    static readonly Dictionary<int, Sprite> cache = new Dictionary<int, Sprite>();
    static readonly string[] names = { "Lightning", "Ligher", "Paranoid", "Ninja", "Saboteur", "UFO", "Dove", "Scout", "Interceptor", "Xenon", "Turtle" };
    static readonly Rect[] rects = {
        new Rect(2, 4, 28, 27), // Lightning
        new Rect(6, 2, 20, 29), // Ligher
        new Rect(1, 5, 30, 22), // Paranoid
        new Rect(1, 1, 30, 30), // Ninja
        new Rect(4, 2, 24, 30), // Saboteur
        new Rect(3, 3, 26, 26), // UFO
        new Rect(6, 2, 20, 24), // Dove
        new Rect(1, 3, 29, 26), // Scout
        new Rect(0, 0, 28, 21), // Interceptor
        new Rect(0, 0, 32, 27), // Xenon
        new Rect(5, 2, 22, 29), // Turtle
    };
    public static Sprite SpriteFor(int index)
    {
        int slot = index - 8;
        if (slot < 0 || slot >= names.Length) return null;
        Sprite sprite;
        if (cache.TryGetValue(index, out sprite)) return sprite;
        var texture = Resources.Load<Texture2D>("ShipArt/Originals/" + names[slot]);
        if (texture == null) return null;
        sprite = Sprite.Create(texture, rects[slot], new Vector2(.5f, .5f), 100f);
        cache[index] = sprite;
        return sprite;
    }
}
