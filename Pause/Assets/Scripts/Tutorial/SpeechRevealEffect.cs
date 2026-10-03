using UnityEngine;
using UnityEngine.UI;

// Reveals a UI Text glyph by glyph without ever changing its string, so the
// line is laid out (wrapped, best-fit) once and nothing reflows while the
// robot talks. Glyphs past `Visible` get zero alpha; the newest batch lands
// big (130%) for one held animation step and then snaps to size -- limited
// animation, like the robot itself, rather than an eased tween.
//
// Relies on UI Text emitting one quad per visible character and none for
// whitespace or rich-text tags (Unity 2019.1+). Add it *before* any Outline
// on the same object so the outline copies the hidden/popping alpha.
//
// Animation runs on unscaled time; the tutorial freezes timeScale whenever
// the player lifts their finger. ModifyMesh reads and writes vertices in
// place through VertexHelper, so it allocates nothing.
[RequireComponent(typeof(Text))]
public class SpeechRevealEffect : BaseMeshEffect
{
    public const float PopDuration = RobotSpeaker.Step;
    const float PopScale = 1.3f;

    int visible = int.MaxValue;
    int popFrom;
    float popStartedAt = -10f;

    public int Visible { get { return visible; } }

    // Shows glyphs [0, count); the ones from the previous count up pop in.
    public void Reveal(int count, float now)
    {
        if (count == visible) return;
        popFrom = Mathf.Min(visible, count);
        if (visible == int.MaxValue) popFrom = 0;
        visible = count;
        popStartedAt = now;
        Refresh();
    }

    public void HideAll()
    {
        visible = 0;
        popFrom = 0;
        Refresh();
    }

    // True while the newest glyphs are still popping (the mesh needs a rebuild
    // every frame until then).
    public bool Animating(float now)
    {
        return now - popStartedAt < PopDuration;
    }

    public void Tick(float now)
    {
        if (Animating(now) || now - popStartedAt < PopDuration + .05f) Refresh();
    }

    void Refresh()
    {
        if (graphic != null) graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive()) return;
        int quads = vh.currentVertCount / 4;
        float age = Mathf.Clamp01((Time.unscaledTime - popStartedAt) / PopDuration);
        float popScale = PopScale;

        var v = new UIVertex();
        for (int q = 0; q < quads; q++)
        {
            int baseIndex = q * 4;
            if (q >= visible)
            {
                for (int k = 0; k < 4; k++)
                {
                    vh.PopulateUIVertex(ref v, baseIndex + k);
                    v.color.a = 0;
                    vh.SetUIVertex(v, baseIndex + k);
                }
                continue;
            }
            if (q < popFrom || age >= 1f) continue;

            // Centre of the quad, for scaling about it.
            Vector3 centre = Vector3.zero;
            for (int k = 0; k < 4; k++)
            {
                vh.PopulateUIVertex(ref v, baseIndex + k);
                centre += v.position;
            }
            centre *= .25f;
            for (int k = 0; k < 4; k++)
            {
                vh.PopulateUIVertex(ref v, baseIndex + k);
                v.position = centre + (v.position - centre) * popScale;
                vh.SetUIVertex(v, baseIndex + k);
            }
        }
    }
}
