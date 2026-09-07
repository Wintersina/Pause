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
        if (hull == null) return new Vector3(0f, -.2f, .05f);
        float rear = hull.bounds.min.y;
        // These two hulls have swept wings extending below the central nozzle.
        if (index == 4) rear += .05f;
        if (index == 6) rear += .06f;
        if (index == 8) rear += .03f;
        if (index == 16) rear += .01f;
        return new Vector3(0f, rear + .01f, .05f);
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
        var animator = boost.GetComponent<Animator>();
        if (animator != null) animator.enabled = false;
        var hull = ship.GetComponent<SpriteRenderer>();
        var sr = boost.GetComponent<SpriteRenderer>();
        if (sr == null) sr = boost.AddComponent<SpriteRenderer>();
        sr.sprite = SpriteFor(index);
        sr.color = TintFor(index);
        sr.sortingOrder = hull != null ? hull.sortingOrder - 1 : 3;
        boost.transform.localPosition = MountFor(hull != null ? hull.sprite : null, index);
        boost.transform.localRotation = Quaternion.identity;
        boost.transform.localScale = ScaleFor(hull != null ? hull.sprite : null, index);
        var animation = boost.GetComponent<DockLaunchFlame>();
        if (animation == null) animation = boost.AddComponent<DockLaunchFlame>();
        animation.Refresh();
        return boost;
    }
}
