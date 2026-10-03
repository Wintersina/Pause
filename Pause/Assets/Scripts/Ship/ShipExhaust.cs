using UnityEngine;

// The plume's top pivot stays on the engine when its length changes.
public static class ShipExhaust
{
    static readonly Sprite[] sprites = new Sprite[4];
    static readonly string[] paths = { "Engine_exhaust1_frames", "Engine_exhaust2_frames", "Engine_exhaust3_frames", "Engine_exhaust3_frames_1" };
    static readonly Rect[] rects = { new Rect(0, 12, 28, 79), new Rect(11, 11, 65, 106), new Rect(1, 0, 32, 60), new Rect(1, 0, 32, 60) };
    // The plume art does not start at the top of its rect: exhaust2 carries
    // ~19% of transparent halo above the hot core, exhaust1 ~6%, exhaust3
    // ~12%. A (0.5, 1) pivot therefore hung the visible flame well below the
    // nozzle it was mounted on. Pivot each sheet at its own flame head.
    static readonly float[] pivotY = { .93f, .80f, .875f, .875f };

    public static int IndexFor(GameObject ship)
    {
        return ShipId.Of(ship, ShipId.Starter);
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
            if (tex != null) sprites[slot] = Sprite.Create(tex, rects[slot], new Vector2(.5f, pivotY[slot]), 100f);
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

    // Where each plume leaves the hull, in the hull's local space. Read from
    // ShipNozzles' per-ship pixel table, so the flight flame, the dock flame
    // and the launch flare all leave the same painted nozzles.
    public static Vector3[] MountsFor(Sprite hull, int index)
    {
        if (hull == null) return new[] { new Vector3(0f, -.2f, .05f) };
        var nozzles = ShipNozzles.For(index);
        var mounts = new Vector3[nozzles.Length];
        for (int i = 0; i < nozzles.Length; i++)
        {
            Vector2 local = ShipNozzles.ToLocal(hull, nozzles[i]);
            mounts[i] = new Vector3(local.x, local.y, .05f);
        }
        return mounts;
    }

    // Relative plume size per nozzle: a twin-engine ship splits its thrust,
    // and small auxiliary nozzles get a smaller flame than the main one.
    public static float NozzleScale(int index, int nozzle)
    {
        var nozzles = ShipNozzles.For(index);
        if (nozzle < 0 || nozzle >= nozzles.Length) return 1f;
        return nozzles[nozzle].scale;
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
            float k = NozzleScale(index, i);
            plume.transform.localScale = new Vector3(scale.x * k, scale.y * Mathf.Sqrt(k), scale.z);
            var animation = plume.GetComponent<DockLaunchFlame>();
            if (animation == null) animation = plume.AddComponent<DockLaunchFlame>();
            animation.Refresh();
        }
        return boost;
    }
}
