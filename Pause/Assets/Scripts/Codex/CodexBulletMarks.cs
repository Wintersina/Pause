using UnityEngine;
using UnityEngine.UI;

// Bullet markers for a codex lore Text whose paragraphs (one per "\n") are a bullet list (the ships).
//
// The font has no bullet glyph worth relying on, and a legacy Text cannot hang an indent, so the text
// itself is plain: the lore rect is simply inset by Gutter on the left, and a small diamond Image sits in
// that gutter beside the FIRST line of every paragraph. A wrapped line therefore always aligns under the
// text, not under the bullet. The markers are placed from the Text's own generated lines whenever its mesh
// is rebuilt (a new ship, a resize, best-fit moving the size): nothing runs per frame, and nothing is
// allocated after the pool of Images exists. No new characters are drawn, so no glyph prewarm is needed.
[RequireComponent(typeof(Text))]
public sealed class CodexBulletMarks : BaseMeshEffect
{
    public const float Gutter = 30f;          // text inset (canvas units) the markers live in
    public const int MaxMarks = 14;
    const float UnitSize = 10f;
    public const float MarkSize = .34f;       // of the font size

    // A solid square (no sprite, so no Resources load) turned 45 degrees: a small diamond in the codex teal.
    Image[] marks;
    bool enabledMarks;
    int shown;
    Text label;

    public int Shown { get { return shown; } }
    public Image Mark(int i) { return marks != null && i >= 0 && i < marks.Length ? marks[i] : null; }

    // Turns the bullets on (a bullet list) or off (a normal paragraph) and repaints.
    public void SetBulleted(bool on)
    {
        enabledMarks = on;
        if (!on) Hide(0);
        if (graphic != null) graphic.SetVerticesDirty();
    }

    // Makes the pool of markers (invisible) so the first ship page does not build them.
    public void Prepare() { Build(); }

    void Build()
    {
        if (marks != null) return;
        marks = new Image[MaxMarks];
        for (int i = 0; i < MaxMarks; i++)
        {
            var img = CodexUi.NewImage("Bullet" + i, transform, null, CodexUi.Accent);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(.5f, .5f);
            rt.sizeDelta = new Vector2(UnitSize, UnitSize);   // fixed: only scaled afterwards, so no mesh rebuild is queued from inside ModifyMesh
            rt.localRotation = Quaternion.Euler(0f, 0f, 45f);
            img.canvasRenderer.SetAlpha(0f);   // always active, shown / hidden by alpha: no graphic rebuild is queued mid-rebuild
            marks[i] = img;
        }
    }

    void Hide(int from)
    {
        if (marks != null)
            for (int i = from; i < marks.Length; i++)
                marks[i].canvasRenderer.SetAlpha(0f);
        shown = from;
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive()) return;
        if (!enabledMarks) { if (marks != null) Hide(0); return; }
        Build();
        if (label == null) label = (Text)graphic;
        string text = label.text;
        var gen = label.cachedTextGenerator;
        var lines = gen.lines;
        var rt = label.rectTransform;
        Rect r = rt.rect;
        float ppu = Mathf.Max(label.pixelsPerUnit, .0001f);
        int n = 0, li = 0;
        for (int c = 0; c < text.Length && n < MaxMarks; c++)
        {
            if (c > 0 && text[c - 1] != '\n') continue;
            if (text[c] == '\n') continue;
            while (li < lines.Count && lines[li].startCharIdx < c) li++;
            if (li >= lines.Count || lines[li].startCharIdx != c) break;   // truncated away
            var line = lines[li];
            float fs = gen.fontSizeUsedForBestFit > 0 ? gen.fontSizeUsedForBestFit : label.fontSize * ppu;
            float y = (line.topY - line.height * .55f) / ppu;   // optical centre of the first line (caps)
            if (y < r.yMin) break;
            var m = marks[n];
            m.canvasRenderer.SetAlpha(1f);
            float size = fs / ppu * MarkSize;
            var mrt = m.rectTransform;
            mrt.localScale = new Vector3(size / UnitSize, size / UnitSize, 1f);
            mrt.anchoredPosition = new Vector2(Gutter * -.5f + 3f, y - r.yMax);
            n++;
        }
        Hide(n);
        shown = n;
    }
}
