using System.Collections.Generic;
using UnityEngine;

// Loads the space dock's sprites, rasterised from the SVG sources in
// Assets/Art/UI/Dock/src~ (see render.sh there). Every PNG is rendered at
// 300 px per world unit, so the art keeps its authored world size and stays
// crisp on high-density phone screens.
public static class DockArt
{
    public const float PixelsPerUnit = 300f;
    public const float DeckOpacity = .85f; // 15% more transparent deck; ships and labels retain their opacity

    // SVG units per world unit; borders are given in SVG units.
    const float SvgUnitsPerWorld = 100f;

    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
    static Material shipMaterial;

    // border: 9-slice inset on all four sides, in SVG units.
    public static Sprite Get(string name, float border = 0f)
    {
        Sprite sprite;
        if (cache.TryGetValue(name, out sprite) && sprite != null) return sprite;
        var texture = Resources.Load<Texture2D>("Dock/" + name);
        if (texture == null) return null;
        float b = border * PixelsPerUnit / SvgUnitsPerWorld;
        sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                               new Vector2(.5f, .5f), PixelsPerUnit, 0, SpriteMeshType.FullRect,
                               new Vector4(b, b, b, b));
        sprite.name = "Dock_" + name;
        cache[name] = sprite;
        return sprite;
    }

    // Parking-lot kit pieces (Dock/Lot/*.png, point-filtered pixel art, authored 1 px = DockLayout.Px world units).
    static readonly Dictionary<string, Sprite> lotCache = new Dictionary<string, Sprite>();
    static Sprite[] digits;

    public static Sprite Lot(string name)
    {
        Sprite sprite;
        if (lotCache.TryGetValue(name, out sprite) && sprite != null) return sprite;
        var texture = Resources.Load<Texture2D>("Dock/Lot/" + name);
        if (texture == null) return null;
        sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f),
                               DockLayout.PixelsPerUnit, 0, SpriteMeshType.FullRect);
        sprite.name = "Lot_" + name;
        lotCache[name] = sprite;
        return sprite;
    }

    // One 12 x 16 digit cell of number_stencils.png (0..9).
    public static Sprite Digit(int d)
    {
        if (digits == null) digits = new Sprite[10];
        d = Mathf.Clamp(d, 0, 9);
        if (digits[d] != null) return digits[d];
        var texture = Resources.Load<Texture2D>("Dock/Lot/number_stencils");
        if (texture == null) return null;
        digits[d] = Sprite.Create(texture, new Rect(d * 12, 0, 12, 16), new Vector2(.5f, .5f), DockLayout.PixelsPerUnit, 0, SpriteMeshType.FullRect);
        digits[d].name = "LotDigit" + d;
        return digits[d];
    }

    // The "more below" chevron: an 11 x 6 pixel V (the same art as the shop's ScrollHint), point filtered.
    static Sprite chevron;
    public static Sprite HintChevron
    {
        get
        {
            if (chevron != null) return chevron;
            const int w = 11, h = 6;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, name = "DockHintChevron" };
            var clear = new Color(1f, 1f, 1f, 0f);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) tex.SetPixel(x, y, clear);
            for (int y = 0; y < 5; y++)
                foreach (int x in new[] { 5 - y, 6 - y, 5 + y - 1, 5 + y })
                    if (x >= 0 && x < w) tex.SetPixel(x, y, Color.white);
            tex.Apply(false, true);
            chevron = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
            return chevron;
        }
    }

    // A 1x1 white pixel sprite one world unit square: tinted and scaled for solid bands.
    static Sprite solid;
    public static Sprite Solid
    {
        get
        {
            if (solid != null) return solid;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "DockSolid" };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply(false, true);
            solid = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1f, 0, SpriteMeshType.FullRect);
            return solid;
        }
    }

    // Sprite material with a saturation control, so a powered-down hull can
    // sit greyed out. Null (default sprite material) if the shader is absent.
    public static Material ShipMaterial
    {
        get
        {
            if (shipMaterial != null) return shipMaterial;
            var shader = Resources.Load<Shader>("Dock/DockSprite");
            if (shader == null || !shader.isSupported) return null;
            shipMaterial = new Material(shader) { name = "DockShip" };
            return shipMaterial;
        }
    }

    // The game's own HUD typeface (Orbitron) is not in Resources; borrow it
    // from any Text the scene authored with it.
    public static Font FindSceneFont()
    {
        foreach (var text in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Text>())
        {
            if (text == null || text.font == null || !text.gameObject.scene.IsValid()) continue;
            if (text.font.name.Contains("Orbitron")) return text.font;
        }
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    // Akira palette (docs/art-style.md): CYAN neon, AMBER star dust, INK, RED_HI warnings.
    public static readonly Color Cyan = AkiraPalette.Cyan;
    public static readonly Color Gold = AkiraPalette.Amber;
    public static readonly Color Ink = AkiraPalette.Ink;
    public static readonly Color Warn = AkiraPalette.RedHi;
}
