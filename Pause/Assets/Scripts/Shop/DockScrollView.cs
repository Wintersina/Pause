using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// UI clipping and hit testing belong to ScrollRect; the real ship renderers are
// mirrored into each card so hull animation and engine mounting stay identical.
public class DockScrollView : MonoBehaviour
{
    public ScrollRect scroll;
    GridLayoutGroup grid;
    RectTransform content;
    float lastWidth;
    bool launching;

    public static void Build(GameObject canvas)
    {
        if (canvas == null || SceneUtil.FindAny("~DockScroll") != null) return;
        foreach (var c in new[] { canvas, SceneUtil.FindAny("StarDustCanvas"), SceneUtil.FindAny("PopUpCanvas") })
        {
            if (c == null) continue;
            var scaler = c.GetComponent<CanvasScaler>();
            if (scaler == null) continue;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(720, 960);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }
        var root = Rect("~DockScroll", canvas.transform);
        Stretch(root, new Vector2(22, 110), new Vector2(-22, -120));
        var background = root.gameObject.AddComponent<Image>();
        background.color = new Color(0.015f, 0.025f, 0.055f, 0.8f);
        var scroll = root.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 45;
        var viewport = Rect("Viewport", root);
        Stretch(viewport, new Vector2(8, 0), new Vector2(-20, 0));
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = Rect("Content", viewport);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1);
        content.sizeDelta = Vector2.zero;
        var grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.padding = new RectOffset(8, 8, 8, 8);
        grid.spacing = new Vector2(16, 16);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        scroll.viewport = viewport;
        scroll.content = content;

        var track = Rect("Scrollbar", root);
        track.anchorMin = new Vector2(1, 0);
        track.anchorMax = Vector2.one;
        track.pivot = new Vector2(1, 0.5f);
        track.sizeDelta = new Vector2(10, -12);
        track.anchoredPosition = new Vector2(-4, 0);
        track.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.2f, 0.3f, 0.8f);
        var handle = Rect("Handle", track);
        Stretch(handle, Vector2.zero, Vector2.zero);
        var handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = new Color(0.2f, 0.85f, 1f, 0.9f);
        var bar = track.gameObject.AddComponent<Scrollbar>();
        bar.handleRect = handle;
        bar.targetGraphic = handleImage;
        bar.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = bar;

        for (int i = 1; i < shopingShips.shipTotal; i++)
        {
            var button = SceneUtil.FindAny("Button" + i);
            var ship = SceneUtil.FindAny("ship" + i);
            if (button == null || ship == null) continue;
            button.transform.SetParent(content, false);
            button.GetComponent<ShopButtonAligner>().followShip = false;
            button.GetComponent<Image>().color = new Color(0.06f, 0.10f, 0.17f, 0.96f);
            var label = button.GetComponentInChildren<Text>(true);
            label.fontSize = 25;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = new Vector2(1, 0);
            label.rectTransform.pivot = new Vector2(0.5f, 0);
            label.rectTransform.anchoredPosition = new Vector2(0, 12);
            label.rectTransform.sizeDelta = new Vector2(-12, 64);
            var preview = Rect("ShipPreview", button.transform);
            preview.anchorMin = preview.anchorMax = new Vector2(0.5f, 0.65f);
            preview.sizeDelta = new Vector2(180, 130);
            var mirror = preview.gameObject.AddComponent<DockCardArt>();
            mirror.ship = ship.transform;
            mirror.shipIndex = i;
            // The world bays are replaced by clipped UI cards.
            var bay = SceneUtil.FindAny("~DockBay" + i);
            if (bay != null) bay.SetActive(false);
            foreach (var sr in ship.GetComponentsInChildren<SpriteRenderer>(true))
                sr.forceRenderingOff = true;
        }
        var controller = new GameObject("~DockScrollController").AddComponent<DockScrollView>();
        controller.scroll = scroll;
        controller.content = content;
        controller.grid = grid;
        PlaceFooter("BackButton", canvas.transform, -150);
        PlaceFooter("PlayButton", canvas.transform, 150);
        var instruction = SceneUtil.FindAny("~DockInstruction").GetComponent<Text>();
        instruction.text = "SPACE DOCK  ·  SCROLL TO EXPLORE\nTOUCH A SHIP TO SELECT OR BUY";
        instruction.fontSize = 23;
        instruction.rectTransform.anchoredPosition = new Vector2(0, -56);
        instruction.rectTransform.sizeDelta = new Vector2(650, 58);
        var dust = SceneUtil.FindAny("starDustText");
        if (dust != null)
        {
            var t = dust.GetComponent<Text>();
            t.fontSize = 25;
            t.resizeTextForBestFit = false;
            t.alignment = TextAnchor.MiddleCenter;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1);
            rt.localScale = Vector3.one;
            rt.anchoredPosition = new Vector2(0, -27);
            rt.sizeDelta = new Vector2(640, 44);
        }
        StyleDialog();
        Canvas.ForceUpdateCanvases();
        controller.RefreshLayout();
        scroll.verticalNormalizedPosition = 1;
    }

    void LateUpdate()
    {
        RefreshLayout();
        if (!rotateRight.flyOffChecker || rotateRight.shipSelected <= 0 || launching) return;
        launching = true;
        // Launch from a visible lane even if the purchased card was scrolled
        // offscreen. Existing selection, payment and departure logic is kept.
        int index = rotateRight.shipSelected;
        var ship = shopingShips.ships[index];
        if (ship == null) return;
        var cam = Camera.main;
        Vector3 start = cam.ViewportToWorldPoint(new Vector3(index % 2 == 0 ? 0.72f : 0.28f, 0.4f, -cam.transform.position.z));
        start.z = 0;
        ship.transform.position = start;
        SceneUtil.FindAny("face" + index).transform.position = start;
        string pad = index % 2 == 0 ? "LiftOffRight" : "LiftOffLeft";
        SceneUtil.FindAny(pad).transform.position = new Vector3(start.x, cam.transform.position.y + cam.orthographicSize + 2, 0);
        foreach (var sr in ship.GetComponentsInChildren<SpriteRenderer>(true)) sr.forceRenderingOff = false;
        scroll.gameObject.SetActive(false);
    }

    // Cards never render narrower than this (the Mathf.Max floor below), and
    // the grid's own padding/spacing (RectOffset(8,8,8,8) + 16px spacing) eat
    // a fixed 32px regardless of column count.
    const float MinCellWidth = 200f;
    const float GridSpacing = 16f;
    const float GridHorizontalPadding = 16f;

    public void RefreshLayout()
    {
        if (scroll == null) return;
        float width = scroll.viewport.rect.width;
        if (Mathf.Approximately(width, lastWidth)) return;
        lastWidth = width;

        // Two columns only when they still fit at the minimum card width --
        // otherwise the Mathf.Max floor below would force cards wider than
        // the viewport actually has room for, overflowing past the clipped
        // scroll area instead of just dropping to one column cleanly.
        //
        // The old flat 540px threshold was tuned against Editor/Mac test
        // windows, which run close to the 720x960 reference aspect this
        // canvas scales against. Real phones -- especially the tall, narrow
        // ones Android actually ships -- are a good deal narrower than that
        // reference once CanvasScaler's width/height blend is applied, so
        // the viewport routinely landed under 540px and silently fell back
        // to one column exactly on the devices most in need of two.
        float twoColumnWidth = MinCellWidth * 2f + GridSpacing + GridHorizontalPadding;
        int columns = width >= twoColumnWidth ? 2 : 1;
        grid.constraintCount = columns;
        grid.cellSize = new Vector2(Mathf.Max(MinCellWidth, (width - GridHorizontalPadding - (columns - 1) * GridSpacing) / columns), 230);
        int rows = Mathf.CeilToInt((shopingShips.shipTotal - 1f) / columns);
        content.sizeDelta = new Vector2(0, 16 + rows * 230 + (rows - 1) * 16);
    }

    static void StyleDialog()
    {
        var shop = Object.FindFirstObjectByType<shopingShips>();
        var dialog = SceneUtil.FindAny("Model Dialauge");
        if (shop == null || dialog == null) return;
        var rt = dialog.GetComponent<RectTransform>();
        rt.localScale = Vector3.one;
        rt.sizeDelta = new Vector2(580, 520);
        var popup = SceneUtil.FindAny("PopUpCanvas");
        rt.SetParent(popup.transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        foreach (var group in dialog.GetComponentsInChildren<LayoutGroup>(true)) group.enabled = false;
        if (shop.shipImg != null) shop.shipImg.transform.SetParent(rt, false);
        if (shop.question != null)
        {
            shop.question.transform.SetParent(rt, false);
            var q = shop.question.rectTransform;
            q.anchorMin = q.anchorMax = new Vector2(0.5f, 0.5f);
            q.localScale = Vector3.one;
            q.anchoredPosition = new Vector2(0, -30);
            q.sizeDelta = new Vector2(520, 180);
            shop.question.fontSize = 28;
            shop.question.resizeTextForBestFit = false;
            shop.question.alignment = TextAnchor.MiddleCenter;
        }
        int side = -1;
        foreach (var button in new[] { shop.noButton, shop.yesButton })
        {
            if (button == null) continue;
            button.transform.SetParent(rt, false);
            var b = button.GetComponent<RectTransform>();
            b.anchorMin = b.anchorMax = new Vector2(0.5f, 0);
            b.localScale = Vector3.one;
            b.anchoredPosition = new Vector2(side * 130, 55);
            b.sizeDelta = new Vector2(230, 64);
            var text = button.GetComponentInChildren<Text>();
            if (text != null) { text.fontSize = 28; text.resizeTextForBestFit = false; }
            side = 1;
        }
        foreach (string name in new[] { "Question Panel", "Button Panel" })
        {
            var old = SceneUtil.FindAny(name);
            if (old != null) old.SetActive(false);
        }
    }

    static void PlaceFooter(string name, Transform parent, float x)
    {
        var go = SceneUtil.FindAny(name);
        if (go == null) return;
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.anchoredPosition = new Vector2(x, 24);
        rt.sizeDelta = new Vector2(260, 64);
        rt.localScale = Vector3.one;
        var t = go.GetComponentInChildren<Text>();
        if (t != null) { t.fontSize = 30; t.resizeTextForBestFit = false; }
    }

    static RectTransform Rect(string name, Transform parent)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }
    static void Stretch(RectTransform rt, Vector2 min, Vector2 max)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = min; rt.offsetMax = max;
    }
}

public class DockCardArt : MonoBehaviour
{
    public Transform ship;
    public int shipIndex;
    readonly Dictionary<SpriteRenderer, Image> images = new Dictionary<SpriteRenderer, Image>();
    SpriteRenderer hull;

    void LateUpdate()
    {
        if (ship == null) return;
        if (hull == null) hull = ship.GetComponent<SpriteRenderer>();
        if (hull == null || hull.sprite == null) return;
        var rotation = Quaternion.Euler(0, 0, ShopSceneExtender.DockAngle(shipIndex));
        float pixelsPerUnit = 138 / Mathf.Max(hull.sprite.bounds.size.x, hull.sprite.bounds.size.y);
        foreach (var sr in ship.GetComponentsInChildren<SpriteRenderer>(true))
        {
            sr.forceRenderingOff = !(rotateRight.flyOffChecker && rotateRight.shipSelected == shipIndex);
            Image img;
            if (!images.TryGetValue(sr, out img))
            {
                img = new GameObject(sr.name, typeof(Image)).GetComponent<Image>();
                img.transform.SetParent(transform, false);
                img.raycastTarget = false;
                images.Add(sr, img);
                if (sr != hull) img.transform.SetAsFirstSibling();
            }
            img.enabled = sr.enabled && sr.gameObject.activeInHierarchy && sr.sprite != null;
            if (!img.enabled) continue;
            img.sprite = sr.sprite;
            img.color = sr.color;
            Vector3 center = ship.InverseTransformPoint(sr.transform.TransformPoint(sr.sprite.bounds.center));
            img.rectTransform.anchoredPosition = rotation * center * pixelsPerUnit;
            img.rectTransform.localRotation = rotation * Quaternion.Inverse(ship.rotation) * sr.transform.rotation;
            Vector3 scale = sr.transform.lossyScale;
            img.rectTransform.sizeDelta = new Vector2(sr.sprite.bounds.size.x * scale.x / ship.lossyScale.x,
                                                       sr.sprite.bounds.size.y * scale.y / ship.lossyScale.y) * pixelsPerUnit;
        }
    }
}
