using UnityEngine;

// Puts a ship's skin onto its exhaust renderers without new art: the stock
// atlas is drawn through Pause/ExhaustRemap (Shaders/Resources/ShipArt/Exhaust),
// whose per-renderer palette shift (ExhaustRemap.cginc) moves each stock
// colour to ExhaustColors.For(ship). A stock skin gets the default sprite
// material and no property block, i.e. exactly what was drawn before.
// Renderers on another material that knows the remap (the title traffic's
// TitleTrafficHaze) keep that material and just get the shift.
//
// Skin changes: ShipSkins.Changed bumps Version; the exhaust components
// (ShipFlameFlipbook, ShipSpinDrift) compare it each frame, an int compare,
// and re-apply when it moved. Applying allocates nothing (one shared
// property block), and only happens on a change.
public static class ExhaustRemap
{
    public const string ShaderName = "Pause/ExhaustRemap";

    static readonly int OnId = Shader.PropertyToID("_ExOn");
    static readonly int[] SrcIds =
    {
        Shader.PropertyToID("_ExSrc0"), Shader.PropertyToID("_ExSrc1"), Shader.PropertyToID("_ExSrc2"),
        Shader.PropertyToID("_ExSrc3"), Shader.PropertyToID("_ExSrc4"),
    };
    static readonly int[] ShiftIds =
    {
        Shader.PropertyToID("_ExShift0"), Shader.PropertyToID("_ExShift1"), Shader.PropertyToID("_ExShift2"),
        Shader.PropertyToID("_ExShift3"), Shader.PropertyToID("_ExShift4"),
    };

    static int version;
    static Material material;
    static Shader spriteShader;
    static Material spriteMaterial;     // the default sprite material, to put back
    static MaterialPropertyBlock block;

    static ExhaustRemap()
    {
        ShipSkins.Changed += OnSkinChanged;
    }

    static void OnSkinChanged(int id) { version++; }

    // Moves whenever any ship's shown skin changes.
    public static int Version { get { return version; } }

    public static Material Material
    {
        get
        {
            if (material == null)
            {
                var shader = Shader.Find(ShaderName);
                if (shader != null) material = new Material(shader) { name = "ExhaustRemap" };
            }
            return material;
        }
    }

    static bool IsDefaultSprite(Material m)
    {
        if (m == null) return true;
        if (spriteShader == null) spriteShader = Shader.Find("Sprites/Default");
        return m.shader == spriteShader;
    }

    // True when `renderer` currently draws ship `shipId`'s exhaust remapped.
    public static bool IsRemapped(SpriteRenderer renderer)
    {
        if (renderer == null || !renderer.HasPropertyBlock()) return false;
        if (block == null) block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        return block.GetFloat(OnId) > .5f;
    }

    // Shows ship `shipId`'s exhaust on `renderer` in its shown skin.
    public static void Apply(SpriteRenderer renderer, int shipId)
    {
        if (!ShipId.IsValid(shipId)) shipId = ShipId.Starter;
        Apply(renderer, shipId, ShipSkins.Shown(shipId));
    }

    // ... in a given skin (the home-screen traffic flies every skin).
    public static void Apply(SpriteRenderer renderer, int shipId, int skin)
    {
        if (renderer == null) return;
        if (!ShipId.IsValid(shipId)) shipId = ShipId.Starter;
        var current = renderer.sharedMaterial;
        bool ours = current != null && current == material;

        if (ExhaustColors.IsStock(shipId, skin))
        {
            if (ours && spriteMaterial != null) renderer.sharedMaterial = spriteMaterial;
            if (renderer.HasPropertyBlock()) renderer.SetPropertyBlock(null);
            return;
        }

        if (!ours)
        {
            if (IsDefaultSprite(current))
            {
                var remap = Material;
                if (remap == null) return;
                if (current != null) spriteMaterial = current;
                renderer.sharedMaterial = remap;
            }
            else if (!current.HasProperty(OnId)) return;   // a material that can't remap
        }

        var src = ExhaustColors.Stock(shipId);
        var dst = ExhaustColors.For(shipId, skin);
        bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
        if (block == null) block = new MaterialPropertyBlock();
        block.Clear();
        block.SetFloat(OnId, 1f);
        for (int i = 0; i < ExhaustColors.Palette.Bands; i++)
        {
            Color s = src[i], d = dst[i];
            if (linear) { s = s.linear; d = d.linear; }
            block.SetVector(SrcIds[i], new Vector4(s.r, s.g, s.b, 0f));
            block.SetVector(ShiftIds[i], new Vector4(d.r - s.r, d.g - s.g, d.b - s.b, 0f));
        }
        renderer.SetPropertyBlock(block);
    }

    // ------------------------------------------------------------ CPU mirror
    //
    // ExhaustRemap.cginc's per-texel maths, for tests and previews: a texel
    // drawn in `src`'s colours as the remap shows it in `dst`'s.
    public const float Reach = .45f;

    public static Color RemapColor(Color c, ExhaustColors.Palette src, ExhaustColors.Palette dst)
    {
        float nearest = 4f, sum = 0f;
        Vector3 shift = Vector3.zero;
        for (int i = 0; i < ExhaustColors.Palette.Bands; i++)
        {
            Color s = src[i], d = dst[i];
            float dr = c.r - s.r, dg = c.g - s.g, db = c.b - s.b;
            float d2 = dr * dr + dg * dg + db * db;
            nearest = Mathf.Min(nearest, d2);
            float k = d2 + 1e-4f;
            float w = 1f / (k * k);
            sum += w;
            shift += w * new Vector3(d.r - s.r, d.g - s.g, d.b - s.b);
        }
        float reach = Mathf.Clamp01(1f - Mathf.Sqrt(nearest) / Reach);
        shift *= reach / sum;
        return new Color(Mathf.Clamp01(c.r + shift.x), Mathf.Clamp01(c.g + shift.y),
                         Mathf.Clamp01(c.b + shift.z), c.a);
    }
}
