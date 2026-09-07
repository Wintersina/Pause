using UnityEngine;
using System.Collections.Generic;

// Explicit single-hull rectangles: legacy strips contain animation/banking poses,
// not health states. Keep their silhouette stable as damage increases.
public static class OriginalShipArt
{
    static readonly Dictionary<int, Sprite> cache = new Dictionary<int, Sprite>();
    // Scout, Interceptor and Xenon removed alongside shopingShips.Roster --
    // see the comment there. Positions here are index - 8 into that roster,
    // so they had to come out in step with it.
    static readonly string[] names = { "Lightning", "Ligher", "Paranoid", "Ninja", "Saboteur", "UFO", "Dove", "Turtle" };
    static readonly Rect[] rects = {
        new Rect(2, 4, 28, 27), // Lightning
        new Rect(6, 2, 20, 29), // Ligher
        new Rect(1, 5, 30, 22), // Paranoid
        new Rect(1, 1, 30, 30), // Ninja
        new Rect(4, 2, 24, 30), // Saboteur
        new Rect(3, 3, 26, 26), // UFO
        new Rect(6, 2, 20, 24), // Dove
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

    // idle0/1/2: a +-1px vertical bob, matching the Retro80s ships' own idle
    // cycle. Frames live under ShipArt/OriginalsIdle at the same 32x32 tile
    // size the static crop Rects were authored against, so the same rect
    // still lines up correctly on the shifted canvas.
    static readonly Dictionary<int, Sprite> idleCache = new Dictionary<int, Sprite>();

    public static Sprite OriginalIdleSpriteFor(int index, int idleFrame)
    {
        int slot = index - 8;
        if (slot < 0 || slot >= names.Length) return SpriteFor(index);

        int frame = Mathf.Clamp(idleFrame, 0, 2);
        int key = index * 10 + frame;
        Sprite sprite;
        if (idleCache.TryGetValue(key, out sprite)) return sprite;

        var texture = Resources.Load<Texture2D>("ShipArt/OriginalsIdle/" + names[slot] + "_idle" + frame);
        if (texture == null) return SpriteFor(index);

        sprite = Sprite.Create(texture, rects[slot], new Vector2(.5f, .5f), 100f);
        idleCache[key] = sprite;
        return sprite;
    }
}
