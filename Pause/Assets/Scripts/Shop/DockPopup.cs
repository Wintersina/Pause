using UnityEngine;
using UnityEngine.UI;

// The small popup that floats just above the selected ship.
//
// It replaces the old full-screen yes/no dialog. It lives on its own
// world-space canvas, re-anchored to the ship every frame it is visible, and
// is clamped to the visible dock: if there is no room above the ship it
// flips below it, pointer and all.
//
//   owned ship  -> one action, LAUNCH
//   locked ship -> its price and BUY; can't afford -> shake + "NEED n MORE"
public class DockPopup : MonoBehaviour
{
    // World units. The canvas is scaled so 1 canvas unit = 0.01 world units.
    public const float Width = 1.50f;
    public const float Height = .62f;
    public const float TailLength = .085f;
    public const float Gap = .04f;
    const float CanvasScale = .01f;
    const float AppearTime = .2f;

    public enum Mode { Launch, Buy }

    public int ShipIndex { get; private set; }
    public Mode CurrentMode { get; private set; }
    public bool Visible { get { return ShipIndex > 0 && gameObject.activeSelf; } }
    public bool Flipped { get; private set; }
    public Rect safeView = new Rect(-2.8f, -4f, 5.6f, 8f);

    public System.Action<int> onLaunch;
    public System.Action<int> onBuy;

    Canvas canvas;
    CanvasGroup group;
    RectTransform panel, tail, dustIcon;
    Text title, status, message, buttonLabel;
    Image buttonImage;
    Transform target;
    float above, below;
    float shownAt;
    float shakeUntil, messageUntil;

    public static DockPopup Create(Transform parent, Font font, Camera eventCamera)
    {
        var go = new GameObject("~DockPopup", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var popup = go.AddComponent<DockPopup>();
        popup.Build(font, eventCamera);
        go.SetActive(false);
        return popup;
    }

    void Build(Font font, Camera eventCamera)
    {
        var root = (RectTransform)transform;
        root.sizeDelta = new Vector2(Width, Height) / CanvasScale;
        root.localScale = Vector3.one * CanvasScale;
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = eventCamera;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 60;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 5f;
        scaler.referencePixelsPerUnit = 100f;
        gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.AddComponent<CanvasGroup>();

        panel = Rect("Panel", root);
        Stretch(panel);
        var panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = DockArt.Get("popup", 12f);
        panelImage.type = Image.Type.Sliced;
        panelImage.raycastTarget = true;   // taps on the panel never fall through

        tail = Rect("Tail", panel);
        tail.sizeDelta = new Vector2(16f, 10f);
        var tailImage = tail.gameObject.AddComponent<Image>();
        tailImage.sprite = DockArt.Get("popup_tail");
        tailImage.raycastTarget = false;

        title = Label("Title", panel, font, 10, TextAnchor.MiddleLeft, AkiraPalette.Bone);
        title.horizontalOverflow = HorizontalWrapMode.Wrap;
        title.verticalOverflow = VerticalWrapMode.Truncate;
        title.resizeTextForBestFit = true;
        title.resizeTextMinSize = 6;
        title.resizeTextMaxSize = 10;
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(11f, -9f), new Vector2(84f, 16f), new Vector2(0f, 1f));
        status = Label("Status", panel, font, 11, TextAnchor.MiddleRight, DockArt.Gold);
        Place(status.rectTransform, new Vector2(1f, 1f), new Vector2(-11f, -9f), new Vector2(64f, 16f), new Vector2(1f, 1f));
        dustIcon = Rect("Dust", panel);
        var dust = dustIcon.gameObject.AddComponent<Image>();
        dust.sprite = DockArt.Get("icon_dust");
        dust.raycastTarget = false;
        Place(dustIcon, new Vector2(1f, 1f), new Vector2(-60f, -10f), new Vector2(12f, 12f), new Vector2(.5f, .5f));
        message = Label("Message", panel, font, 9, TextAnchor.MiddleCenter, DockArt.Warn);
        Place(message.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -9f), new Vector2(Width / CanvasScale - 16f, 16f), new Vector2(.5f, 1f));

        var buttonRect = Rect("Action", panel);
        Place(buttonRect, new Vector2(.5f, 0f), new Vector2(0f, 8f), new Vector2(Width / CanvasScale - 18f, 26f), new Vector2(.5f, 0f));
        buttonImage = buttonRect.gameObject.AddComponent<Image>();
        buttonImage.sprite = DockArt.Get("button", 8f);
        buttonImage.type = Image.Type.Sliced;
        var button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        var colors = button.colors;
        colors.pressedColor = new Color(.8f, .8f, .8f, 1f);
        colors.highlightedColor = Color.white;
        colors.selectedColor = Color.white;
        button.colors = colors;
        button.onClick.AddListener(Clicked);
        CelPress.AddTo(buttonRect.gameObject);   // cartoon squash-and-pop on press
        buttonLabel = Label("Label", buttonRect, font, 12, TextAnchor.MiddleCenter, DockArt.Ink);
        Stretch(buttonLabel.rectTransform);
        buttonLabel.fontStyle = FontStyle.Bold;
    }

    public void Show(int index, Transform ship, float halfHeight, bool owned, bool equipped,
                     float price, float balance)
    {
        bool wasVisible = Visible && ShipIndex == index;
        ShipIndex = index;
        target = ship;
        above = halfHeight + Gap;
        below = halfHeight + .34f;        // clear the engine plume
        gameObject.SetActive(true);
        title.text = (shopingShips.NameFor(index) ?? "").ToUpperInvariant();
        messageUntil = 0f;
        if (owned)
        {
            CurrentMode = Mode.Launch;
            // LAUNCH already says it's yours; only call out the equipped one.
            status.text = equipped ? "EQUIPPED" : "";
            status.color = AkiraPalette.WithAlpha(AkiraPalette.Cyan, .9f);
            status.fontSize = 8;
            dustIcon.gameObject.SetActive(false);
            buttonLabel.text = "LAUNCH";
            buttonImage.color = DockArt.Cyan;
        }
        else
        {
            CurrentMode = Mode.Buy;
            bool affordable = balance >= price;
            status.text = Mathf.RoundToInt(price).ToString("N0");
            status.color = affordable ? DockArt.Gold : DockArt.Warn;
            status.fontSize = 11;
            dustIcon.gameObject.SetActive(true);
            dustIcon.anchoredPosition = new Vector2(-11f - status.preferredWidth - 8f, -17f);
            buttonLabel.text = "BUY";
            buttonImage.color = affordable ? DockArt.Gold : AkiraPalette.GunHi;
        }
        // The name gets whatever width the status leaves, shrinking to fit
        // long names rather than running into the price.
        float statusWidth = status.text.Length > 0 ? status.preferredWidth + (CurrentMode == Mode.Buy ? 16f : 0f) + 6f : 0f;
        title.rectTransform.sizeDelta = new Vector2(Width / CanvasScale - 22f - statusWidth, 16f);
        SetRowVisible(true);
        if (!wasVisible) shownAt = Time.unscaledTime;
        Follow();
    }

    public void ShowCantAfford(float shortBy)
    {
        message.text = "NEED " + Mathf.CeilToInt(Mathf.Max(0f, shortBy)).ToString("N0") + " MORE";
        message.color = DockArt.Warn;
        messageUntil = Time.unscaledTime + 1.8f;
        shakeUntil = Time.unscaledTime + .35f;
        SetRowVisible(false);
    }

    public void ShowMessage(string text, Color color, float seconds)
    {
        message.text = text;
        message.color = color;
        messageUntil = Time.unscaledTime + seconds;
        SetRowVisible(false);
    }

    public void Hide()
    {
        ShipIndex = 0;
        target = null;
        gameObject.SetActive(false);
    }

    void Clicked()
    {
        if (!Visible) return;
        if (CurrentMode == Mode.Launch) { if (onLaunch != null) onLaunch(ShipIndex); }
        else if (onBuy != null) onBuy(ShipIndex);
    }

    // Shows the popup fully grown at once (previews, tests).
    public void SkipAppear() { shownAt = -100f; }

    // Lets the legacy shopingShips.yes_no() entry point press the action.
    public void Press() { Clicked(); }

    void SetRowVisible(bool row)
    {
        title.enabled = row;
        status.enabled = row;
        dustIcon.gameObject.SetActive(row && CurrentMode == Mode.Buy);
        message.enabled = !row;
    }

    void LateUpdate()
    {
        if (target == null) { Hide(); return; }
        float now = Time.unscaledTime;
        if (messageUntil > 0f && now >= messageUntil)
        {
            messageUntil = 0f;
            SetRowVisible(true);
        }
        float e = Mathf.Clamp01((now - shownAt) / AppearTime);
        group.alpha = DockTween.OutCubic(e);
        float s = Mathf.LerpUnclamped(.82f, 1f, DockTween.OutBack(e)) * CanvasScale;
        transform.localScale = new Vector3(s, s, 1f);
        float shake = now < shakeUntil ? Mathf.Sin(now * 70f) * 5f * ((shakeUntil - now) / .35f) : 0f;
        panel.anchoredPosition = new Vector2(shake, 0f);
        Follow();
    }

    void Follow()
    {
        if (target == null) return;
        Vector2 ship = target.position;
        bool flipped;
        Vector2 center = Place(ship, above, below, new Vector2(Width, Height), safeView, out flipped);
        Flipped = flipped;
        transform.position = new Vector3(center.x, center.y, -1f);
        float tailX = Mathf.Clamp(ship.x - center.x, -Width * .5f + .14f, Width * .5f - .14f) / CanvasScale;
        float edge = Height * .5f / CanvasScale;
        tail.anchoredPosition = new Vector2(tailX, flipped ? edge + 3.5f : -edge - 3.5f);
        tail.localRotation = flipped ? Quaternion.Euler(0f, 0f, 180f) : Quaternion.identity;
    }

    // Pure placement: centre of a popup of `size` floating above a ship at
    // `ship`, clamped inside `view`. Falls back below the ship when there is
    // no room above.
    public static Vector2 Place(Vector2 ship, float above, float below, Vector2 size, Rect view, out bool flipped)
    {
        float halfW = size.x * .5f, halfH = size.y * .5f;
        float x = view.width >= size.x
            ? Mathf.Clamp(ship.x, view.xMin + halfW, view.xMax - halfW)
            : view.center.x;
        float y = ship.y + above + TailLength + halfH;
        flipped = false;
        if (y + halfH > view.yMax)
        {
            float belowY = ship.y - below - TailLength - halfH;
            if (belowY - halfH >= view.yMin)
            {
                y = belowY;
                flipped = true;
            }
        }
        if (view.height >= size.y) y = Mathf.Clamp(y, view.yMin + halfH, view.yMax - halfH);
        return new Vector2(x, y);
    }

    // World-space corners of the popup panel, for tests.
    public Rect WorldRect
    {
        get
        {
            Vector3 c = transform.position;
            return new Rect(c.x - Width * .5f, c.y - Height * .5f, Width, Height);
        }
    }

    static RectTransform Rect(string name, Transform parent)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static void Place(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size, Vector2 pivot)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
    }

    static Text Label(string name, Transform parent, Font font, int size, TextAnchor align, Color color)
    {
        var rt = Rect(name, parent);
        var text = rt.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = align;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }
}
