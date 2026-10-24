using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The SHIP PERCH row of the Options screen (leaderboardS3): where the ship
// floats relative to the finger (PerchSettings). A button on the left cycles
// the direction (ABOVE / ABOVE LEFT / ABOVE RIGHT, shown with an arrow) and a
// slider on the right sets the distance (it clicks onto the stock gap, the
// default, when dragged near it). Built at runtime from the screen's own "Tutorial" button like
// the sound row, with the same neon slider art.
//
// It sits in the free slot above UPGRADE OFFERS (y 147). Builds that offer
// developer mode fill that area (the sound row moves there), so the row is
// left out there; the setting still works.
public class PerchOptions : MonoBehaviour
{
    public const string RowName = "PerchToggle", SliderName = "PerchSlider";
    public const float RowY = 147f, RowH = 88f, HitPad = 6f;
    const float ButtonW = 300f, ButtonX = -130f, SliderW = 220f, SliderX = 140f;
    const float TrackH = 16f, HandleW = 24f, HandleH = 36f;

    Text label;
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

    public static string Caption(int tilt)
    {
        string arrow = tilt == PerchSettings.AboveLeft ? "<  " : (tilt == PerchSettings.AboveRight ? ">  " : "");
        return "SHIP PERCH\n" + arrow + PerchSettings.TiltName(tilt);
    }

    void Start()
    {
        var template = SceneUtil.FindAny("Tutorial");
        if (template == null) return;
        var parent = template.transform.parent;
        var row = DeveloperOptions.Clone(template, parent, RowName,
                        new Vector2(ButtonX, RowY), new Vector2(ButtonW, RowH), Cycle);
        label = row.GetComponentInChildren<Text>(true);
        var button = row.GetComponent<Button>();
        if (button != null && button.targetGraphic != null)
            button.targetGraphic.raycastPadding = new Vector4(0f, -HitPad, 0f, -HitPad);
        if (label != null)
        {
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 16;
            label.resizeTextMaxSize = 26;
        }
        BuildSlider(parent);
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

    // Same chunky track + fill + handle as the volume sliders, centred in the row.
    void BuildSlider(Transform parent)
    {
        var root = Rect(SliderName, parent);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
        root.anchoredPosition = new Vector2(SliderX, RowY);
        root.sizeDelta = new Vector2(SliderW, RowH);
        var hit = root.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastPadding = new Vector4(0f, -HitPad, 0f, -HitPad);

        var track = Art(root, "Track", "options_track", true).rectTransform;
        track.anchorMin = new Vector2(0f, .5f); track.anchorMax = new Vector2(1f, .5f);
        track.offsetMin = new Vector2(4f, -TrackH * .5f); track.offsetMax = new Vector2(-4f, TrackH * .5f);

        var fillArea = Rect("Fill Area", root);
        fillArea.anchorMin = new Vector2(0f, .5f); fillArea.anchorMax = new Vector2(1f, .5f);
        fillArea.offsetMin = new Vector2(4f, -TrackH * .5f); fillArea.offsetMax = new Vector2(-4f, TrackH * .5f);
        var fill = Art(fillArea, "Fill", "options_fill", true).rectTransform;
        fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0f, 1f);
        fill.offsetMin = fill.offsetMax = Vector2.zero;

        var slide = Rect("Handle Slide Area", root);
        slide.anchorMin = new Vector2(0f, .5f); slide.anchorMax = new Vector2(1f, .5f);
        slide.offsetMin = new Vector2(4f + HandleW * .5f, -HandleH * .5f); slide.offsetMax = new Vector2(-4f - HandleW * .5f, HandleH * .5f);
        var handle = Art(slide, "Handle", "options_handle", false).rectTransform;
        handle.sizeDelta = new Vector2(HandleW, HandleH);

        slider = root.gameObject.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = hit;
        slider.transition = Selectable.Transition.None;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f; slider.maxValue = 1f; slider.wholeNumbers = false;
        slider.onValueChanged.AddListener(OnSlide);

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
        float scale = PerchSettings.FromSlider(v);
        if (Mathf.Abs(scale - PerchSettings.DefaultScale) < .04f) scale = PerchSettings.DefaultScale;
        PerchSettings.SetScale(scale);
    }

    public void Refresh()
    {
        refreshing = true;
        if (label != null) label.text = Caption(PerchSettings.Tilt);
        if (slider != null) slider.SetValueWithoutNotify(PerchSettings.ToSlider(PerchSettings.Scale));
        refreshing = false;
    }
}
