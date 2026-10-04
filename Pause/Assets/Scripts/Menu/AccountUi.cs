using UnityEngine;
using UnityEngine.UI;

// Building blocks shared by the Account row (AccountOptions), its dialogs
// (AccountDialog) and the home-screen hint (AccountHintToast). Same look as
// the leaderboard panel (docs/art-style.md): flat CelShape plates with a
// thick INK contour and one hard offset shadow, Orbitron with an ink stroke.
public static class AccountUi
{
    public static RectTransform Child(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    public static void Stretch(RectTransform rt, float inset = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(.5f, .5f);
        rt.offsetMin = new Vector2(inset, 0f);
        rt.offsetMax = new Vector2(-inset, 0f);
    }

    // Left x / top y (downwards from the parent's top-left corner), size.
    public static RectTransform Place(Transform parent, string name, float left, float top, float w, float h)
    {
        var rt = Child(parent, name);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(left, -top);
        return rt;
    }

    public static Text Label(RectTransform rt, Font font, string s, int size, Color color, TextAnchor align,
                             float ink, bool italic = true)
    {
        var text = rt.gameObject.AddComponent<Text>();
        text.font = font;
        text.text = s;
        text.fontSize = size;
        text.fontStyle = italic ? FontStyle.BoldAndItalic : FontStyle.Bold;
        text.color = color;
        text.alignment = align;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Mathf.Max(10, Mathf.RoundToInt(size * .55f));
        text.resizeTextMaxSize = size;
        if (ink > 0f)
        {
            var outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = AkiraPalette.Ink;
            outline.effectDistance = new Vector2(ink * .6f, -ink * .6f);
        }
        return text;
    }

    // A chamfered cel button: filled with the accent (primary) or a card
    // plate whose hard shadow carries the accent (secondary).
    public static Button MakeButton(Transform parent, string name, Font font, float left, float top, float w,
                                    float h, string caption, Color accent, bool filled,
                                    UnityEngine.Events.UnityAction onClick, int fontSize = 28)
    {
        var rt = Place(parent, name, left, top, w, h);
        var shape = CelShape.Add(rt.gameObject, CelShape.Kind.Chamfer, filled ? accent : AkiraPalette.Card, 4f)
                            .Shadow(filled ? AkiraPalette.Ink : accent, new Vector2(6f, -6f));
        shape.cut = 16f;
        shape.raycastTarget = true;
        var label = Child(rt, "Label");
        Stretch(label, 14f);
        Label(label, font, caption, fontSize, AkiraPalette.Bone, TextAnchor.MiddleCenter, 3f);
        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = shape;
        button.transition = Selectable.Transition.None;
        if (onClick != null) button.onClick.AddListener(onClick);
        CelPress.AddTo(rt.gameObject);
        return button;
    }

    public static void SetButton(Button button, string caption, Color accent, bool filled, bool interactable)
    {
        if (button == null) return;
        var shape = button.GetComponent<CelShape>();
        if (shape != null)
        {
            shape.SetFill(filled ? accent : AkiraPalette.Card);
            shape.Shadow(filled ? AkiraPalette.Ink : accent, new Vector2(6f, -6f));
        }
        var text = button.GetComponentInChildren<Text>(true);
        if (text != null)
        {
            text.text = caption;
            text.color = interactable ? AkiraPalette.Bone : AkiraPalette.Muted;
        }
        button.interactable = interactable;
    }

    public static string Caption(Button button)
    {
        var text = button != null ? button.GetComponentInChildren<Text>(true) : null;
        return text != null ? text.text : null;
    }
}
