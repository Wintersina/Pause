using UnityEngine;

// Draw-time brightening of backdrop art (BackdropGrade.shader), so a world
// can be made brighter without regrading its PNGs and tuned live.
//
// A world opts in with Spec.brightness (Frost: FrostTuning.Brightness) and
// each layer takes a share of it (Layer.grade, 0 = drawn as painted, 1 = the
// full lift): the ground lifts most, far layers less so the depth haze holds.
// The lift k of a layer is 1 + (brightness - 1) x grade; a texel of HSV value
// v is drawn at 1 - (1 - v)^k (about k x v in the shadows, rolling off to 1
// at white), hue and saturation kept. Apply() is the same maths on the CPU,
// for the tests that hold the art to its brightness rules as drawn.
public static class BackdropGrade
{
    public const string ShaderName = "BackdropShaders/BackdropGrade";

    // The value lift of a layer of `spec` (1 = as painted).
    public static float Lift(BackdropCatalog.Spec spec, BackdropCatalog.Layer layer)
    {
        return Lift(spec, layer.grade);
    }

    public static float Lift(BackdropCatalog.Spec spec, float share)
    {
        float b = spec != null && spec.brightness != null ? spec.brightness() : 1f;
        return Mathf.Max(1f, 1f + (b - 1f) * share);
    }

    // Saturation for a layer lifted by `lift`: the world's saturation nudge,
    // in proportion to how much the layer is lifted.
    public static float Saturation(BackdropCatalog.Spec spec, float lift)
    {
        float b = spec != null && spec.brightness != null ? spec.brightness() : 1f;
        float s = spec != null && spec.saturation != null ? spec.saturation() : 1f;
        if (b <= 1.0001f) return 1f;
        return 1f + (s - 1f) * Mathf.Clamp01((lift - 1f) / (b - 1f));
    }

    public static float Curve(float v, float k) { return 1f - Mathf.Pow(Mathf.Clamp01(1f - v), k); }

    // One texel as the shader draws it (straight alpha, before the tint).
    public static Color Apply(Color c, float lift, float sat = 1f, float alphaLift = 1f)
    {
        float v = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        float k = v > 1e-4f ? Curve(v, lift) / v : 0f;
        float r = c.r * k, g = c.g * k, b = c.b * k;
        float l = (r + g + b) / 3f;
        r = Mathf.Clamp01(l + (r - l) * sat);
        g = Mathf.Clamp01(l + (g - l) * sat);
        b = Mathf.Clamp01(l + (b - l) * sat);
        return new Color(r, g, b, Curve(c.a, alphaLift));
    }

    public static Color[] Apply(Color[] px, float lift, float sat = 1f, float alphaLift = 1f)
    {
        if (lift <= 1f && alphaLift <= 1f && Mathf.Approximately(sat, 1f)) return px;
        var o = new Color[px.Length];
        for (int i = 0; i < px.Length; i++) o[i] = Apply(px[i], lift, sat, alphaLift);
        return o;
    }

    // A material drawing with the lift, or null when there is nothing to
    // lift (keep the default sprite material) or the shader is missing.
    public static Material Create(string name, float lift, float sat = 1f, float alphaLift = 1f)
    {
        if (lift <= 1f && alphaLift <= 1f && Mathf.Approximately(sat, 1f)) return null;
        var shader = Resources.Load<Shader>(ShaderName);
        if (shader == null) return null;
        var m = new Material(shader) { name = "Grade_" + name };
        Set(m, lift, sat, alphaLift);
        return m;
    }

    public static void Set(Material m, float lift, float sat, float alphaLift)
    {
        if (m == null) return;
        m.SetFloat("_Lift", lift);
        m.SetFloat("_Sat", sat);
        m.SetFloat("_AlphaLift", alphaLift);
    }
}
