using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The Options column (leaderboardS3) as a scroll list when the screen is too short to stack it.
//
// The rows are scene-authored / runtime-built centre-anchored buttons and sliders 100 units apart, from LEADERBOARD
// SETTINGS (340) down to Tutorial (-333), with BACK under them. On a tall phone the column fits and nothing here
// changes a pixel (the rows keep their authored heights; BACK's lift moves them with it, as before). On a canvas
// too short for it -- a Z Flip cover screen at 2.6 px/dp is only ~800 units tall -- the rows sit in a viewport between
// the top safe edge and BACK and scroll, so no row is ever cut off or out of reach.
public class OptionsScroll : MonoBehaviour
{
    public const string ViewportName = "OptionsViewport", ContentName = "OptionsContent";

    RectTransform parent, viewport, content;
    ScrollRect scroll;
    RectMask2D mask;
    Image catcher;
    readonly List<RectTransform> rows = new List<RectTransform>();
    readonly List<Vector2> authored = new List<Vector2>();
    bool scrolling, placed;

    public bool IsScrolling { get { return scrolling; } }
    public RectTransform Viewport { get { return viewport; } }

    public static OptionsScroll For(RectTransform canvasRect)
    {
        var s = canvasRect.GetComponent<OptionsScroll>();
        return s != null ? s : canvasRect.gameObject.AddComponent<OptionsScroll>();
    }

    // Where new rows are built: the canvas, never the scroll content (they are adopted into it on the next layout).
    public static Transform RowParent(GameObject template)
    {
        var p = template.transform.parent;
        return p != null && p.name == ContentName && p.parent != null && p.parent.parent != null ? p.parent.parent : p;
    }

    static bool IsRow(RectTransform rt, RectTransform back, float backAuthoredY)
    {
        if (rt == back || rt.name == ViewportName) return false;
        if (rt.anchorMin != new Vector2(.5f, .5f) || rt.anchorMax != new Vector2(.5f, .5f)) return false;
        if (rt.anchoredPosition.y <= backAuthoredY || rt.sizeDelta.x > 700f) return false;
        if (rt.name.StartsWith("Leaderboard") && rt.name != LeaderboardSettingsOptions.RowName) return false;
        return rt.GetComponentInChildren<Selectable>(true) != null;
    }

    void EnsureViewport(int siblingIndex)
    {
        if (viewport != null) return;
        var vgo = new GameObject(ViewportName, typeof(RectTransform));
        viewport = (RectTransform)vgo.transform;
        viewport.SetParent(parent, false);
        viewport.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, parent.childCount - 1));
        viewport.anchorMin = viewport.anchorMax = viewport.pivot = new Vector2(.5f, .5f);
        mask = vgo.AddComponent<RectMask2D>();
        catcher = vgo.AddComponent<Image>();
        catcher.color = new Color(0f, 0f, 0f, 0f);
        catcher.raycastTarget = false;
        var cgo = new GameObject(ContentName, typeof(RectTransform));
        content = (RectTransform)cgo.transform;
        content.SetParent(viewport, false);
        content.anchorMin = content.anchorMax = content.pivot = new Vector2(.5f, .5f);
        scroll = vgo.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 40f;
        scroll.enabled = false;
        mask.enabled = false;
    }

    // Picks up rows (also ones built later: developer rows, PRIVACY OPTIONS) and lays the column out.
    // safeTop: the highest y (canvas units, centre origin) a row may reach; lift: how far BACK was raised from where it was authored.
    public void Layout(RectTransform back, float backAuthoredY, float safeTop, float lift)
    {
        parent = transform as RectTransform;
        if (parent == null || back == null) return;
        var adopt = new List<RectTransform>();
        int firstIndex = int.MaxValue;
        for (int i = 0; i < parent.childCount; i++)
        {
            var rt = parent.GetChild(i) as RectTransform;
            if (rt != null && IsRow(rt, back, backAuthoredY)) { adopt.Add(rt); firstIndex = Mathf.Min(firstIndex, i); }
        }
        if (adopt.Count > 0)
        {
            EnsureViewport(firstIndex == int.MaxValue ? parent.childCount : firstIndex);
            foreach (var rt in adopt)
            {
                rows.Add(rt);
                authored.Add(rt.anchoredPosition);
                rt.SetParent(content, false);
            }
        }
        if (rows.Count == 0 || viewport == null) return;

        float top = float.MinValue, bottom = float.MaxValue;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] == null) continue;
            float h = rows[i].rect.height;
            top = Mathf.Max(top, authored[i].y + h * .5f);
            bottom = Mathf.Min(bottom, authored[i].y - h * .5f);
        }
        if (top < bottom) return;
        float cy = (top + bottom) * .5f, span = top - bottom;
        float backTop = back.anchoredPosition.y + (1f - back.pivot.y) * back.rect.height;
        float vpTop = safeTop, vpBottom = backTop;
        bool nowScrolling = top + lift > safeTop + .5f && vpTop - vpBottom > 60f;

        float width = parent.rect.width;
        float vpCentre;
        if (nowScrolling)
        {
            viewport.sizeDelta = new Vector2(width, vpTop - vpBottom);
            vpCentre = (vpTop + vpBottom) * .5f;
        }
        else
        {
            viewport.sizeDelta = new Vector2(width, parent.rect.height);
            vpCentre = 0f;
        }
        viewport.anchoredPosition = new Vector2(0f, vpCentre);
        content.sizeDelta = new Vector2(width, span);
        float shift = nowScrolling ? vpTop - top : lift;   // canvas units added to the authored heights
        content.anchoredPosition = new Vector2(0f, cy + shift - vpCentre);
        for (int i = 0; i < rows.Count; i++)
            if (rows[i] != null) rows[i].anchoredPosition = authored[i] - new Vector2(0f, cy);

        scrolling = nowScrolling;
        scroll.enabled = nowScrolling;
        mask.enabled = nowScrolling;
        catcher.raycastTarget = nowScrolling;
        if (nowScrolling) scroll.StopMovement();
        placed = true;
    }
}
