using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The dock's news ticker: one thin low-contrast line above the footer buttons
// (SpaceDock reserves its space, so it never covers a ship or a control). A
// long line scrolls right to left and the next item follows; a short line
// sways left and right. Tapping it opens the item's purchase (DockNews.Tap).
// Shown only while at least one item is showable, which keeps it off iOS and
// the editor. Update touches only the text's position: no per-frame garbage.
public class DockNewsTicker : MonoBehaviour
{
    public const string Name = "~DockNews";
    public const float Height = 30f, SideMargin = 14f;

    RectTransform view, line;
    Text label;
    Image hit;
    readonly List<int> showing = new List<int>();
    int slot;        // position in `showing`
    string itemId;   // the item the line shows, to keep scrolling across rebuilds
    float t, textW, duration;

    public static DockNewsTicker Build(Transform canvas)
    {
        var existing = SceneUtil.FindAny(Name);
        if (existing != null) return existing.GetComponent<DockNewsTicker>();
        var go = new GameObject(Name, typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        go.transform.SetParent(canvas, false);
        var ticker = go.AddComponent<DockNewsTicker>();
        ticker.Construct();
        ticker.Rebuild();
        return ticker;
    }

    void Construct()
    {
        view = (RectTransform)transform;
        view.anchorMin = new Vector2(0f, 0f);
        view.anchorMax = new Vector2(1f, 0f);
        view.pivot = new Vector2(.5f, 0f);
        view.sizeDelta = new Vector2(0f, Height);
        hit = GetComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);   // invisible tap target, no plate
        var button = gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        button.onClick.AddListener(OnTap);

        var go = new GameObject("Line", typeof(RectTransform), typeof(Text));
        line = (RectTransform)go.transform;
        line.SetParent(transform, false);
        line.anchorMin = line.anchorMax = new Vector2(0f, .5f);
        line.pivot = new Vector2(0f, .5f);
        label = go.GetComponent<Text>();
        label.font = DockArt.FindSceneFont();
        label.fontSize = 14;
        label.alignment = TextAnchor.MiddleLeft;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.color = AkiraPalette.WithAlpha(AkiraPalette.Cyan, .55f);
        label.raycastTarget = false;
    }

    // Re-reads which items are showable (ownership, store price). Keeps the
    // current line scrolling when it is still showable.
    public void Rebuild()
    {
        DockNews.Fill(showing);
        bool any = showing.Count > 0;
        if (gameObject.activeSelf != any)
        {
            gameObject.SetActive(any);
            if (SpaceDock.Instance != null) SpaceDock.Instance.Relayout();   // the rack refits above the line
        }
        if (!any) { itemId = null; return; }
        int keep = itemId == null ? -1 : showing.IndexOf(DockNews.IndexOf(itemId));
        if (keep >= 0) { slot = keep; SetText(false); }
        else { slot = Mathf.Clamp(slot, 0, showing.Count - 1); SetText(true); }
    }

    void SetText(bool restart)
    {
        var item = DockNews.Items[showing[slot]];
        itemId = item.id;
        label.text = item.text();
        textW = label.preferredWidth;
        line.sizeDelta = new Vector2(textW, Height);
        if (restart) t = 0f;
        duration = DockNews.Duration(textW, ViewWidth, DockNews.Speed);
    }

    float ViewWidth { get { return Mathf.Max(0f, view.rect.width - 2f * SideMargin); } }

    void OnTap()
    {
        if (showing.Count == 0) return;
        DockNews.Tap(showing[Mathf.Clamp(slot, 0, showing.Count - 1)]);
    }

    void Update()
    {
        if (showing.Count == 0) return;
        t += Time.unscaledDeltaTime;
        float viewW = ViewWidth;
        if (t >= duration)
        {
            t = 0f;
            slot = (slot + 1) % showing.Count;
            SetText(true);
        }
        float x = SideMargin + DockNews.Position(t, textW, viewW, DockNews.Speed);
        line.anchoredPosition = new Vector2(x, 0f);
    }

    // Safe area: the line sits above the footer buttons and inside the side insets.
    public void Place(float y, float safeL, float safeR, float safeBottom)
    {
        view.offsetMin = new Vector2(safeL, y + safeBottom);
        view.offsetMax = new Vector2(-safeR, y + safeBottom + Height);
        if (showing.Count > 0) duration = DockNews.Duration(textW, ViewWidth, DockNews.Speed);
    }
}
