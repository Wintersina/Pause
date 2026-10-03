using System.Collections.Generic;
using UnityEngine;

// Loads the space dock's sprites, rasterised from the SVG sources in
// Assets/Art/UI/Dock/src~ (see render.sh there). Every PNG is rendered at
// 300 px per world unit, so the art keeps its authored world size and stays
// crisp on high-density phone screens.
public static class DockArt
{
    public const float PixelsPerUnit = 300f;

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

    public static readonly Color Cyan = new Color(.18f, .9f, 1f);
    public static readonly Color Gold = new Color(1f, .79f, .26f);
    public static readonly Color Ink = new Color(.024f, .133f, .227f);
    public static readonly Color Warn = new Color(1f, .38f, .52f);
}
