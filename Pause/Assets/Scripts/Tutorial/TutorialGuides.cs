using UnityEngine;
using UnityEngine.UI;

// The tutorial's two pointers, on the robot's canvas so they share its style:
//
//  - a pulsing "touch here" ring in the lower middle of the screen, shown only
//    while the world is frozen and the step wants a finger down;
//  - a sodium-orange double-chevron hint arrow that points at a HUD readout (another
//    canvas's Text) or at something in the world (the red atom), bobbing
//    toward it.
//
// Replaces nothing authored in the scene -- the old tutorial had no pointers,
// only text. Flat cel art and limited animation like the robot: held poses
// that snap on whole steps, no fades. Unscaled time throughout, nothing
// allocated per frame.
public class TutorialGuides : MonoBehaviour
{

    public const float ArrowSize = 46f;
    const float ArrowGap = 18f;          // from the target's edge to the arrow tip

    RectTransform root;
    Canvas canvas;
    RectTransform touch;
    Image[] touchRings = new Image[2];
    Image touchDot;
    RectTransform arrow;
    Image arrowImage;

    bool touchWanted;
    float touchAlpha, arrowAlpha;

    Text uiTarget;
    float uiTargetWidth;
    Transform worldTarget;
    readonly Vector3[] corners = new Vector3[4];

    public static TutorialGuides Create(RectTransform parent)
    {
        var go = new GameObject("Guides", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        rt.SetAsFirstSibling();   // behind the robot and its bubble
        var guides = go.AddComponent<TutorialGuides>();
        guides.Build(rt);
        return guides;
    }

    void Build(RectTransform r)
    {
        root = r;
        canvas = r.GetComponentInParent<Canvas>();
        touch = NewRect("TouchHint", root);
        touch.anchorMin = touch.anchorMax = new Vector2(.5f, .27f);
        touch.sizeDelta = new Vector2(150f, 150f);
        var ring = Resources.Load<Sprite>("Tutorial/tut_ring");
        for (int i = 0; i < touchRings.Length; i++)
        {
            touchRings[i] = NewImage("Ring", touch, ring, TutorialPalette.Teal);
            Stretch(touchRings[i].rectTransform);
        }
        touchDot = NewImage("Dot", touch, Resources.Load<Sprite>("Tutorial/tut_glow"), TutorialPalette.Teal);
        touchDot.rectTransform.sizeDelta = new Vector2(64f, 64f);

        arrowImage = NewImage("HintArrow", root, Resources.Load<Sprite>("Tutorial/tut_arrow"), Color.white);   // colours baked in
        arrow = arrowImage.rectTransform;
        arrow.sizeDelta = new Vector2(ArrowSize, ArrowSize);
        Apply(0f, 0f);
    }

    // ---- What to point at ----

    public void ShowTouch(bool on) { touchWanted = on; }

    public void PointAt(Text hudReadout)
    {
        uiTarget = hudReadout;
        worldTarget = null;
        // Measured once: the readout's string changes every frame, its width
        // barely does, and measuring per frame would allocate.
        uiTargetWidth = hudReadout != null ? hudReadout.preferredWidth : 0f;
    }

    public void PointAt(Transform worldObject)
    {
        worldTarget = worldObject;
        uiTarget = null;
    }

    public void ClearArrow()
    {
        uiTarget = null;
        worldTarget = null;
    }

    public void Clear()
    {
        ShowTouch(false);
        ClearArrow();
    }

    void Update()
    {
        float now = Time.unscaledTime;
        bool frozen = !TouchInput.IsPressed && score.pauseCounter > 0;
        touchAlpha = touchWanted && frozen ? 1f : 0f;

        Vector2 tip, dir;
        bool hasArrow = ArrowTarget(out tip, out dir);
        arrowAlpha = hasArrow ? 1f : 0f;
        if (hasArrow)
        {
            // Two held positions on 3s: a cartoon "nudge" toward the target.
            float bob = (Mathf.FloorToInt(now / RobotSpeaker.SlowStep) & 1) * 8f;
            // The arrow's tip is its top edge (the art points up); sit the tip
            // ArrowGap + bob away from the target, pointing at it.
            Vector2 centre = tip - dir * (ArrowGap + bob + ArrowSize * .5f);
            arrow.anchoredPosition = centre;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            arrow.localRotation = Quaternion.Euler(0f, 0f, angle);
        }
        Apply(now, touchAlpha);
    }

    void Apply(float now, float tAlpha)
    {
        for (int i = 0; i < touchRings.Length; i++)
        {
            // Four held sizes per cycle, stepping outward.
            float p = Mathf.Floor(Mathf.Repeat(now * .9f + i * .5f, 1f) * 4f) / 4f;
            touchRings[i].rectTransform.localScale = Vector3.one * Mathf.Lerp(.4f, 1.15f, p);
            SetAlpha(touchRings[i], tAlpha * (1f - p * .8f));
        }
        bool dotBig = (Mathf.FloorToInt(now / RobotSpeaker.SlowStep) & 3) == 0;
        touchDot.rectTransform.localScale = Vector3.one * (dotBig ? 1.15f : 1f);
        SetAlpha(touchDot, tAlpha);
        SetAlpha(arrowImage, arrowAlpha);
    }

    // Where the arrow should point, in root (canvas-centre) units, and the
    // unit direction it travels toward that point.
    bool ArrowTarget(out Vector2 tip, out Vector2 dir)
    {
        tip = Vector2.zero;
        dir = Vector2.down;
        Vector2 screen;
        if (uiTarget != null && uiTarget.isActiveAndEnabled)
        {
            // Overlay canvas: world corners are screen pixels. Point at the
            // right end of the readout's text, from the right.
            uiTarget.rectTransform.GetWorldCorners(corners);
            float scaleX = uiTarget.rectTransform.lossyScale.x;
            float left = corners[0].x;
            float textRight = left + uiTargetWidth * scaleX;
            if (uiTarget.alignment == TextAnchor.MiddleCenter || uiTarget.alignment == TextAnchor.UpperCenter || uiTarget.alignment == TextAnchor.LowerCenter)
                textRight = (corners[0].x + corners[2].x) * .5f + uiTargetWidth * scaleX * .5f;
            screen = new Vector2(textRight, (corners[0].y + corners[1].y) * .5f);
            dir = Vector2.left;
        }
        else if (worldTarget != null)
        {
            var cam = Camera.main;
            if (cam == null) return false;
            Vector3 sp = cam.WorldToScreenPoint(worldTarget.position);
            if (sp.z < 0f || sp.y > Screen.height || sp.y < 0f) return false;
            screen = new Vector2(sp.x, sp.y + 28f * Mathf.Max(.0001f, ScaleFactor()));
            dir = Vector2.down;
        }
        else return false;

        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out local)) return false;
        tip = local;
        return true;
    }

    float ScaleFactor()
    {
        return canvas != null ? canvas.scaleFactor : 1f;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
        return rt;
    }

    static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void SetAlpha(Graphic g, float a)
    {
        var c = g.color;
        if (Mathf.Approximately(c.a, a)) return;
        c.a = a;
        g.color = c;
    }
}
