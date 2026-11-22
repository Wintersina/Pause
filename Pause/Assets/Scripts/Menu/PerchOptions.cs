using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The SHIP PERCH row of the Options screen (leaderboardS3): where the ship
// floats relative to the finger (PerchSettings). A button on the left cycles
// the direction (ABOVE / ABOVE LEFT / ABOVE RIGHT, shown with an arrow); that
// button sits in the SWITCHES group. The distance is a 3-stop slider in the
// SLIDERS group (OptionsLayout), with the numbers 1 2 3 under the track, the
// chosen one lit, and a tap on a number (or anywhere on the track) selecting
// that stop. Built at runtime from the screen's own "Tutorial" button like
// the sound row, with the same neon slider art.
//
// Builds that offer developer mode fill that area (the sound row moves there), so the row is
// left out there; the setting still works.
public class PerchOptions : MonoBehaviour
{
    public const string RowName = "PerchToggle", SliderName = "PerchSlider";
    public const string TicksName = "Ticks", CaptionName = "Caption";
    public const float RowY = OptionsLayout.PerchDirectionY, RowH = 88f, RowW = 520f, HitPad = 6f;
    public const float SliderY = OptionsLayout.PerchSliderY, SliderW = 420f, SliderH = OptionsLayout.PerchSliderH;
    public const float TrackH = 16f, HandleW = 24f, HandleH = 36f;
    // Row-local layout of the slider (centre origin): caption on top, then the track and handle, tick numbers under them.
    public const float CaptionY = 44f, CaptionH = 22f, TrackY = 4f, TickY = -44f, TickH = 22f, TickW = 28f, SidePad = 4f;

    Text label;
    Slider slider;
    Text[] ticks;
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

    public static string Caption(int tilt)
    {
        string arrow = tilt == PerchSettings.AboveLeft ? "<  " : (tilt == PerchSettings.AboveRight ? ">  " : "");
        return "SHIP PERCH  " + arrow + PerchSettings.TiltName(tilt);
    }

    // Where stop 0..2 sits along the slider's own width (canvas units from its centre): the handle's centre.
    public static float StopX(int stop)
    {
        float travel = SliderW - 2f * SidePad - HandleW;
        return (stop / (float)(PerchSettings.StopCount - 1) - .5f) * travel;
    }

    void Start()
    {
        var template = SceneUtil.FindAny("Tutorial");
        if (template == null) return;
        var parent = OptionsScroll.RowParent(template);
        var row = DeveloperOptions.Clone(template, parent, RowName,
                        new Vector2(0f, RowY), new Vector2(RowW, RowH), Cycle);
        label = row.GetComponentInChildren<Text>(true);
        var button = row.GetComponent<Button>();
        if (button != null && button.targetGraphic != null)
            button.targetGraphic.raycastPadding = new Vector4(0f, -HitPad, 0f, -HitPad);
        if (label != null)
        {
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 18;
            label.resizeTextMaxSize = 34;
        }
        BuildSlider(parent, label);
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
        t.raycastTarget = false;   // a tap on a number falls through to the slider, which snaps to the nearest stop
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = min;
        t.resizeTextMaxSize = max;
        return t;
    }

    // The same chunky track + fill + handle as the volume sliders, with three stops: the handle snaps to 1, 2 or 3
    // and the numbers under the track say which one is chosen.
    void BuildSlider(Transform parent, Text like)
    {
        var root = Rect(SliderName, parent);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
        root.anchoredPosition = new Vector2(0f, SliderY);
        root.sizeDelta = new Vector2(SliderW, SliderH);
        var hit = root.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastPadding = new Vector4(0f, -HitPad, 0f, -HitPad);

        MakeText(root, CaptionName, like, "PERCH DISTANCE", CaptionY, CaptionH, SliderW, 14, 22, AkiraPalette.Muted);

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

        ticks = new Text[PerchSettings.StopCount];
        var tickRoot = Rect(TicksName, root);
        tickRoot.anchorMin = tickRoot.anchorMax = tickRoot.pivot = new Vector2(.5f, .5f);
        tickRoot.sizeDelta = new Vector2(SliderW, TickH);
        tickRoot.anchoredPosition = new Vector2(0f, TickY);
        for (int i = 0; i < ticks.Length; i++)
        {
            ticks[i] = MakeText(tickRoot, "Tick" + (i + 1), like, (i + 1).ToString(), 0f, TickH, TickW, 16, 26, AkiraPalette.Muted);
            ticks[i].rectTransform.anchoredPosition = new Vector2(StopX(i), 0f);
        }

        slider = root.gameObject.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handleImg.rectTransform;
        slider.targetGraphic = hit;
        slider.transition = Selectable.Transition.None;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f; slider.maxValue = PerchSettings.StopCount - 1; slider.wholeNumbers = true;
        slider.onValueChanged.AddListener(OnSlide);
        OptionsSliderHandle.Attach(slider, handleImg);

        var trigger = root.gameObject.AddComponent<EventTrigger>();
        var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        up.callback.AddListener(_ => PrefsSaver.SaveNow());
        trigger.triggers.Add(up);
    }

    void Cycle()
    {
        PerchSettings.SetTilt(PerchSettings.Tilt + 1);
        PrefsSaver.SaveNow();
        Refresh();
    }

    void OnSlide(float v)
    {
        if (refreshing) return;
        PerchSettings.SetStop(Mathf.RoundToInt(v));
        Refresh();
    }

    // Tick colours: the chosen stop's number bright amber, the others dim.
    public static Color TickColor(bool selected) { return selected ? AkiraPalette.Amber : AkiraPalette.WithAlpha(AkiraPalette.Muted, .6f); }

    public void Refresh()
    {
        refreshing = true;
        if (label != null) label.text = Caption(PerchSettings.Tilt);
        int stop = PerchSettings.Stop;
        if (slider != null) slider.SetValueWithoutNotify(stop);
        if (ticks != null)
            for (int i = 0; i < ticks.Length; i++) ticks[i].color = TickColor(i == stop);
        refreshing = false;
    }
}
