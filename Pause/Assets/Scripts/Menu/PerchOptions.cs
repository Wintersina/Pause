using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The SHIP PERCH rows of the Options screen (leaderboardS3): where the ship floats relative to the finger
// (PerchSettings). The direction is a 3-button selector labelled LEFT, TOP, RIGHT
// under a SHIP PERCH title, the chosen one lit amber, the others dim. The distance is a continuous slider with a
// DISTANCE caption directly under the buttons, so title, buttons and slider read as one block (OptionsLayout). Built at runtime from the screen's own "Tutorial" button
// like the sound row, with the same neon slider art.
//
// Builds that offer developer mode fill that area (the sound row moves there), so the rows are
// left out there; the setting still works.
public class PerchOptions : MonoBehaviour
{
    public const string RowName = "PerchToggle", SliderName = "PerchSlider", DirName = "PerchDir", TitleName = "PerchTitle";
    public const string CaptionName = "Caption";
    public const float RowY = OptionsLayout.PerchDirectionY, RowH = OptionsLayout.PerchDirectionH, RowW = 520f, HitPad = 6f;
    public const float TitleH = 24f, ButtonH = 88f, ButtonW = 160f, ButtonGap = 20f;
    public const float SliderY = OptionsLayout.PerchSliderY, SliderW = 420f, SliderH = OptionsLayout.PerchSliderH;
    public const float TrackH = 16f, HandleW = 24f, HandleH = 36f;
    // Row-local layout of the slider (centre origin): caption on top, then the track and handle.
    public const float CaptionY = 30f, CaptionH = 22f, TrackY = -10f, SidePad = 4f;
    public static readonly string[] SlotWords = { "LEFT", "TOP", "RIGHT" };

    readonly Button[] dirButtons = new Button[PerchSettings.TiltCount];
    readonly Text[] dirLabels = new Text[PerchSettings.TiltCount];
    readonly Image[] dirImages = new Image[PerchSettings.TiltCount];
    Slider slider;
    bool refreshing;

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "leaderboardS3" && !DeveloperUnlocks.Available && FindFirstObjectByType<PerchOptions>() == null)
            new GameObject("~PerchOptions").AddComponent<PerchOptions>();
    }

    public static string SlotLabel(int slot) { return SlotWords[slot]; }

    // Selected button: amber text on the lit art; the others dim.
    public static Color LabelColor(bool selected) { return selected ? AkiraPalette.Amber : AkiraPalette.WithAlpha(AkiraPalette.Muted, .75f); }
    public static Color ArtColor(bool selected) { return selected ? Color.white : new Color(.55f, .58f, .72f, .7f); }

    void Start()
    {
        var template = SceneUtil.FindAny("Tutorial");
        if (template == null) return;
        var parent = OptionsScroll.RowParent(template);
        var row = Rect(RowName, parent);
        row.anchorMin = row.anchorMax = row.pivot = new Vector2(.5f, .5f);
        row.anchoredPosition = new Vector2(0f, RowY);
        row.sizeDelta = new Vector2(RowW, RowH);
        var like = template.GetComponentInChildren<Text>(true);
        MakeText(row, TitleName, like, "SHIP PERCH", (RowH - TitleH) * .5f, TitleH, RowW, 14, 24, AkiraPalette.Muted);
        float by = -TitleH * .5f;
        for (int slot = 0; slot < PerchSettings.TiltCount; slot++)
        {
            int s = slot;
            var b = DeveloperOptions.Clone(template, row, DirName + (slot + 1),
                        new Vector2((slot - 1) * (ButtonW + ButtonGap), by), new Vector2(ButtonW, ButtonH), () => Pick(s));
            var button = b.GetComponent<Button>();
            if (button != null && button.targetGraphic != null)
                button.targetGraphic.raycastPadding = new Vector4(0f, -HitPad, 0f, -HitPad);
            dirButtons[slot] = button;
            dirImages[slot] = button != null ? button.targetGraphic as Image : null;
            var t = b.GetComponentInChildren<Text>(true);
            if (t != null)
            {
                t.resizeTextForBestFit = true;
                t.resizeTextMinSize = 16;
                t.resizeTextMaxSize = 30;
                t.text = SlotLabel(slot);
            }
            dirLabels[slot] = t;
        }
        BuildSlider(parent, like);
        Refresh();
    }

    void OnDestroy() { PerchSettings.Save(); }

    static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static Image Art(Transform parent, string name, string file, bool sliced)
    {
        var img = Rect(name, parent).gameObject.AddComponent<Image>();
        img.sprite = Resources.Load<Sprite>(SoundOptions.ResPath + file);
        img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        img.raycastTarget = false;
        return img;
    }

    static Text MakeText(Transform parent, string name, Text like, string text, float y, float h, float w, int min, int max, Color color)
    {
        var rt = Rect(name, parent);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(w, h);
        var t = rt.gameObject.AddComponent<Text>();
        if (like != null) { t.font = like.font; t.fontStyle = like.fontStyle; }
        t.text = text;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.raycastTarget = false;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = min;
        t.resizeTextMaxSize = max;
        return t;
    }

    // The same chunky track + fill + handle as the volume sliders, continuous from near to far.
    void BuildSlider(Transform parent, Text like)
    {
        var root = Rect(SliderName, parent);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
        root.anchoredPosition = new Vector2(0f, SliderY);
        root.sizeDelta = new Vector2(SliderW, SliderH);
        var hit = root.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastPadding = new Vector4(0f, -HitPad, 0f, -HitPad);

        MakeText(root, CaptionName, like, "DISTANCE", CaptionY, CaptionH, SliderW, 14, 22, AkiraPalette.Muted);

        var track = Art(root, "Track", "options_track", true).rectTransform;
        track.anchorMin = new Vector2(0f, .5f); track.anchorMax = new Vector2(1f, .5f);
        track.offsetMin = new Vector2(SidePad, TrackY - TrackH * .5f); track.offsetMax = new Vector2(-SidePad, TrackY + TrackH * .5f);

        var fillArea = Rect("Fill Area", root);
        fillArea.anchorMin = new Vector2(0f, .5f); fillArea.anchorMax = new Vector2(1f, .5f);
        fillArea.offsetMin = new Vector2(SidePad, TrackY - TrackH * .5f); fillArea.offsetMax = new Vector2(-SidePad, TrackY + TrackH * .5f);
        var fill = Art(fillArea, "Fill", "options_fill", true).rectTransform;
        fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0f, 1f);
        fill.offsetMin = fill.offsetMax = Vector2.zero;

        var slide = Rect("Handle Slide Area", root);
        slide.anchorMin = new Vector2(0f, .5f); slide.anchorMax = new Vector2(1f, .5f);
        // zero height: Slider stretches the handle over this area's height, so sizeDelta.y alone (HandleH) is its height
        slide.offsetMin = new Vector2(SidePad + HandleW * .5f, TrackY); slide.offsetMax = new Vector2(-SidePad - HandleW * .5f, TrackY);
        var handleImg = Art(slide, "Handle", "options_handle", false);
        handleImg.rectTransform.sizeDelta = new Vector2(HandleW, HandleH);

        slider = root.gameObject.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handleImg.rectTransform;
        slider.targetGraphic = hit;
        slider.transition = Selectable.Transition.None;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f; slider.maxValue = 1f; slider.wholeNumbers = false;
        slider.onValueChanged.AddListener(OnSlide);
        OptionsSliderHandle.Attach(slider, handleImg);

        var trigger = root.gameObject.AddComponent<EventTrigger>();
        var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        up.callback.AddListener(_ => PrefsSaver.SaveNow());
        trigger.triggers.Add(up);
    }

    void Pick(int slot)
    {
        PerchSettings.SetTilt(PerchSettings.TiltOfSlot(slot));
        PrefsSaver.SaveNow();
        Refresh();
    }

    void OnSlide(float v)
    {
        if (refreshing) return;
        PerchSettings.SetScale(PerchSettings.FromSlider(v));
    }

    public void Refresh()
    {
        refreshing = true;
        int chosen = PerchSettings.SlotOfTilt(PerchSettings.Tilt);
        for (int i = 0; i < dirButtons.Length; i++)
        {
            if (dirLabels[i] != null) dirLabels[i].color = LabelColor(i == chosen);
            if (dirImages[i] != null) dirImages[i].color = ArtColor(i == chosen);
        }
        if (slider != null) slider.SetValueWithoutNotify(PerchSettings.ToSlider(PerchSettings.Scale));
        refreshing = false;
    }
}
