using UnityEngine;

// The plume's top pivot stays on the engine when its length changes.
public static class ShipExhaust
{
    static readonly Sprite[] sprites = new Sprite[4];
    static readonly string[] paths = { "Engine_exhaust1_frames", "Engine_exhaust2_frames", "Engine_exhaust3_frames", "Engine_exhaust3_frames_1" };
    static readonly Rect[] rects = { new Rect(0, 12, 28, 79), new Rect(11, 11, 65, 106), new Rect(1, 0, 32, 60), new Rect(1, 0, 32, 60) };

    public static int IndexFor(GameObject ship)
    {
        string name = ship.name.Replace("(Clone)", "").Replace("ship", "");
        int index;
        return int.TryParse(name, out index) ? index : shopingShips.StarterShip;
    }

    // Ninja and UFO are spinning craft, not nozzle-driven ships. A fixed
    // exhaust would rotate around with the hull and read as a broken flame.
    public static bool UsesWind(int index)
    {
        return index == 11 || index == 13;
    }

    public static Sprite SpriteFor(int index)
    {
        int slot = Mathf.Abs(index - 1) % paths.Length;
        if (sprites[slot] == null)
        {
            var tex = Resources.Load<Texture2D>("ShipArt/Exhaust/" + paths[slot]);
            if (tex != null) sprites[slot] = Sprite.Create(tex, rects[slot], new Vector2(.5f, 1f), 100f);
        }
        return sprites[slot];
    }

    public static Color TintFor(int index)
    {
        // Shape, hue and length together give every roster entry its own engine.
        Color color = Color.HSVToRGB(Mathf.Repeat(index * .137f, 1f), .28f, 1f);
        color.a = .96f;
        return color;
    }

    public static Vector3 MountFor(Sprite hull, int index)
    {
        var mounts = MountsFor(hull, index);
        return mounts.Length > 0 ? mounts[0] : new Vector3(0f, -.2f, .05f);
    }

    // A hull's nozzle count is part of its silhouette. Keeping these mounts
    // here makes the normal flight flame and the dock launch flame agree.
    public static Vector3[] MountsFor(Sprite hull, int index)
    {
        if (hull == null) return new[] { new Vector3(0f, -.2f, .05f) };
        float rear = hull.bounds.min.y;
        // These two hulls have swept wings extending below the central nozzle.
        if (index == 4) rear += .05f;
        if (index == 6) rear += .06f;
        if (index == 8) rear += .03f;
        if (index == 16) rear += .01f;
        Vector3 center = new Vector3(0f, rear + .01f, .05f);
        float halfSpan;
        switch (index)
        {
            // Volt Viper, Lightning and Paranoid visibly have two engines.
            // Their plumes must leave the two nozzles, never the fuselage.
            case 2:  halfSpan = hull.bounds.size.x * .27f; break;
            case 8:  halfSpan = hull.bounds.size.x * .25f; break;
            case 10: halfSpan = hull.bounds.size.x * .23f; break;
            default: return new[] { center };
        }
        return new[]
        {
            center + Vector3.left * halfSpan,
            center + Vector3.right * halfSpan,
        };
    }

    public static Vector3 ScaleFor(Sprite hull, int index)
    {
        var sprite = SpriteFor(index);
        if (hull == null || sprite == null) return Vector3.one * .2f;
        float height = hull.bounds.size.y;
        return new Vector3(height * (.22f + (index % 3) * .025f) / sprite.bounds.size.x,
                           height * (.65f + (index % 5) * .045f) / sprite.bounds.size.y, 1f);
    }

    public static GameObject ConfigureBoost(GameObject ship, int index)
    {
        GameObject boost = null;
        foreach (var child in ship.GetComponentsInChildren<Transform>(true))
            if (child != ship.transform && (child.CompareTag("boost") || child.name.StartsWith("Boost")))
            { boost = child.gameObject; break; }
        if (boost == null)
        {
            boost = new GameObject("Boost" + index);
            boost.transform.SetParent(ship.transform, false);
            boost.SetActive(false);
        }
        boost.name = "Boost" + index;
        boost.tag = "boost";
        if (UsesWind(index))
        {
            // collisionDetection still uses this tagged holder for the old
            // blue-atom state. Leave the object discoverable, but disable all
            // visual renderers so a spinning craft never gains a flame.
            foreach (var renderer in boost.GetComponentsInChildren<SpriteRenderer>(true))
                renderer.enabled = false;
            return boost;
        }
        var animator = boost.GetComponent<Animator>();
        if (animator != null) animator.enabled = false;
        var hull = ship.GetComponent<SpriteRenderer>();
        // The root is what the legacy boost scripts activate. Actual plume
        // renderers are its children so a twin-engine ship gets twin flames.
        var rootRenderer = boost.GetComponent<SpriteRenderer>();
        if (rootRenderer != null) rootRenderer.enabled = false;
        var rootAnimation = boost.GetComponent<DockLaunchFlame>();
        if (rootAnimation != null) rootAnimation.enabled = false;
        boost.transform.localPosition = Vector3.zero;
        boost.transform.localRotation = Quaternion.identity;
        boost.transform.localScale = Vector3.one;
        var mounts = MountsFor(hull != null ? hull.sprite : null, index);
        var scale = ScaleFor(hull != null ? hull.sprite : null, index);
        for (int i = 0; i < mounts.Length; i++)
        {
            var child = boost.transform.Find("Nozzle" + i);
            var plume = child != null ? child.gameObject : new GameObject("Nozzle" + i);
            if (child == null) plume.transform.SetParent(boost.transform, false);
            var sr = plume.GetComponent<SpriteRenderer>();
            if (sr == null) sr = plume.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFor(index);
            sr.color = TintFor(index);
            sr.sortingOrder = hull != null ? hull.sortingOrder - 1 : 3;
            plume.transform.localPosition = mounts[i];
            plume.transform.localRotation = Quaternion.identity;
            plume.transform.localScale = mounts.Length > 1
                ? new Vector3(scale.x * .72f, scale.y, scale.z)
                : scale;
            var animation = plume.GetComponent<DockLaunchFlame>();
            if (animation == null) animation = plume.AddComponent<DockLaunchFlame>();
            animation.Refresh();
        }
        return boost;
    }
}
