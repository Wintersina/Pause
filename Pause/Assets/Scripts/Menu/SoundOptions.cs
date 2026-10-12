using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The EFFECTS and MUSIC controls of the Options screen (leaderboardS3), side by
// side in one row: each is a toggle (two lines, "EFFECTS" over "ON" / "OFF")
// with a volume slider beside it (see SoundSettings). One row, not two, so the
// Account card above keeps its room on short screens. Built at runtime from the screen's own "Tutorial" button like
// START WORLD, so the toggles share its font, size and button look; the slider
// is a neon pixel-art one (Resources/Options, drawn by Assets/Art/UI~/Options/gen_options_icons.py):
// a speaker (effects) or a beamed note (music) above a chunky track + fill + handle. The icon shows
// no waves / 1 / 2 / 3 waves (bars for music) as the volume grows, and a slashed, grey one when
// the volume is 0 or the toggle is off. No outline boxes.
//
// The row sits in the slot above START WORLD (y -33), or, in builds that offer
// developer mode (whose rows fill -133 / -43 / 47), on top of the developer
// stack (147). It stays inside +-270 canvas units so a curved-edge safe area
// never clips it.
public class SoundOptions : MonoBehaviour
{
    const float RowH = 88f;
    const float HitPad = 6f;       // finger-sized: 88 + 2 x 6 clears 48 dp on a 1080-wide phone
    const float ToggleW = 110f, SliderW = 140f;
    // centres of the (effects, music) toggle and slider
    const float EffToggleX = -215f, EffSliderX = -80f, MusToggleX = 65f, MusSliderX = 200f;
    public const string RowName = "EffectsToggle";
    public const string MusicRowName = "MusicToggle";
    public const string EffectsSliderName = "EffectsSlider";
    public const string MusicSliderName = "MusicSlider";

    // ---- art -------------------------------------------------------------------
    public const string ResPath = "Options/";
    const float IconSize = 48f, IconY = 20f, TrackY = -28f, TrackH = 16f, HandleW = 24f, HandleH = 36f;
    static Sprite[] iconSprites;   // [kind * 4 + level]; kind 0 = speaker, 1 = note

    // 0 = muted / zero, 1..3 = the wave (bar) count: <= 1/3, <= 2/3, above.
    public static int IconLevel(bool on, float volume)
    {
        if (!on || volume <= 0f) return 0;
        return volume <= 1f / 3f ? 1 : (volume <= 2f / 3f ? 2 : 3);
    }

    public static Sprite IconSprite(bool music, int level)
    {
        if (iconSprites == null || iconSprites[0] == null)
        {
            var tex = Resources.Load<Texture2D>(ResPath + "options_icons");
            if (tex == null) return null;
            iconSprites = new Sprite[8];
            for (int k = 0; k < 2; k++)
                for (int l = 0; l < 4; l++)
                    iconSprites[k * 4 + l] = Sprite.Create(tex, new Rect(l * 48, (1 - k) * 48, 48, 48), new Vector2(.5f, .5f), 100f);
        }
        return iconSprites[(music ? 4 : 0) + Mathf.Clamp(level, 0, 3)];
    }

    public static float EffectsY { get { return DeveloperUnlocks.Available ? 147f : -33f; } }
    public static float MusicY { get { return EffectsY; } }

    class Row
    {
        public bool music;
        public GameObject toggle;
        public Text label;
        public Slider slider;
        public Image fill, handle, icon;
    }

    Row effects, music;
    bool refreshing;

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "leaderboardS3" && FindFirstObjectByType<SoundOptions>() == null)
            new GameObject("~SoundOptions").AddComponent<SoundOptions>();
    }

    void Start()
    {
        var template = SceneUtil.FindAny("Tutorial");
        if (template == null) return;
        effects = Build(template, false, EffectsY);
        music = Build(template, true, MusicY);
        Refresh();
    }

    void OnDestroy() { SoundSettings.Save(); }

    Row Build(GameObject template, bool isMusic, float y)
    {
        var row = new Row { music = isMusic };
        var parent = template.transform.parent;
        row.toggle = DeveloperOptions.Clone(template, parent, isMusic ? MusicRowName : RowName,
                        new Vector2(isMusic ? MusToggleX : EffToggleX, y), new Vector2(ToggleW, RowH), () => Toggle(row));
        row.label = row.toggle.GetComponentInChildren<Text>(true);
        var button = row.toggle.GetComponent<Button>();
        if (button != null && button.targetGraphic != null)
            button.targetGraphic.raycastPadding = new Vector4(0f, -HitPad, 0f, -HitPad);
        if (row.label != null)
        {
            row.label.resizeTextForBestFit = true;
            row.label.resizeTextMinSize = 18;
            row.label.resizeTextMaxSize = 28;
        }
        BuildSlider(parent, row, isMusic ? MusicSliderName : EffectsSliderName, new Vector2(isMusic ? MusSliderX : EffSliderX, y));
        return row;
    }

    static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static void Art(Image img, string file, bool sliced)
    {
        img.sprite = Resources.Load<Sprite>(ResPath + file);
        img.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        img.color = Color.white;
    }

    void BuildSlider(Transform parent, Row row, string name, Vector2 pos)
    {
        var root = Rect(name, parent);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
        root.anchoredPosition = pos;
        root.sizeDelta = new Vector2(SliderW, RowH);
        var hit = root.gameObject.AddComponent<Image>();   // invisible, takes the touch
        hit.color = new Color(0f, 0f, 0f, 0f);
        hit.raycastPadding = new Vector4(0f, -HitPad, 0f, -HitPad);

        var track = Rect("Track", root);
        track.anchorMin = new Vector2(0f, .5f); track.anchorMax = new Vector2(1f, .5f);
        track.offsetMin = new Vector2(4f, TrackY - TrackH * .5f); track.offsetMax = new Vector2(-4f, TrackY + TrackH * .5f);
        var trackImg = track.gameObject.AddComponent<Image>();
        Art(trackImg, "options_track", true);
        trackImg.raycastTarget = false;

        var fillArea = Rect("Fill Area", root);
        fillArea.anchorMin = new Vector2(0f, .5f); fillArea.anchorMax = new Vector2(1f, .5f);
        fillArea.offsetMin = new Vector2(4f, TrackY - TrackH * .5f); fillArea.offsetMax = new Vector2(-4f, TrackY + TrackH * .5f);
        var fill = Rect("Fill", fillArea);
        fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0f, 1f);
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        row.fill = fill.gameObject.AddComponent<Image>();
        Art(row.fill, "options_fill", true);
        row.fill.raycastTarget = false;

        var slide = Rect("Handle Slide Area", root);
        slide.anchorMin = new Vector2(0f, .5f); slide.anchorMax = new Vector2(1f, .5f);
        slide.offsetMin = new Vector2(4f + HandleW * .5f, TrackY - HandleH * .5f); slide.offsetMax = new Vector2(-4f - HandleW * .5f, TrackY + HandleH * .5f);
        var handle = Rect("Handle", slide);
        handle.sizeDelta = new Vector2(HandleW, HandleH);
        row.handle = handle.gameObject.AddComponent<Image>();
        Art(row.handle, "options_handle", false);
        row.handle.raycastTarget = false;

        var icon = Rect("Icon", root);
        icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(.5f, .5f);
        icon.anchoredPosition = new Vector2(0f, IconY);
        icon.sizeDelta = new Vector2(IconSize, IconSize);
        row.icon = icon.gameObject.AddComponent<Image>();
        row.icon.raycastTarget = false;
        row.icon.preserveAspect = true;

        var slider = root.gameObject.AddComponent<Slider>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = hit;   // the whole row is the touch target, not just the handle
        slider.transition = Selectable.Transition.None;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f; slider.maxValue = 1f; slider.wholeNumbers = false;
        slider.onValueChanged.AddListener(v => OnSlide(row, v));
        row.slider = slider;

        // Persist when the finger lifts (the drag itself only touches the cached pref).
        var trigger = root.gameObject.AddComponent<EventTrigger>();
        var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        up.callback.AddListener(_ => OnRelease(row));
        trigger.triggers.Add(up);
    }

    void Toggle(Row row)
    {
        bool on = row.music ? SoundSettings.MusicOn : SoundSettings.SfxOn;
        Set(row, !on);
        SoundSettings.Save();
        if (!on && !row.music) Click();   // a short confirmation, only when effects come back
        Refresh();
    }

    static void Set(Row row, bool on)
    {
        if (row.music) SoundSettings.SetMusicOn(on); else SoundSettings.SetSfxOn(on);
    }

    void OnSlide(Row row, float v)
    {
        if (refreshing) return;
        if (row.music) SoundSettings.SetMusicVolume(v); else SoundSettings.SetSfxVolume(v);
        // dragging a slider while its bus is off turns it back on
        if (v > 0f && !(row.music ? SoundSettings.MusicOn : SoundSettings.SfxOn)) Set(row, true);
        Refresh();
    }

    void OnRelease(Row row)
    {
        SoundSettings.Save();
        if (!row.music && SoundSettings.SfxGain > 0f) Click();   // hear the new effects level
    }

    // A soft noise tick (no tone): short, filtered, decaying.
    static void Click()
    {
        var go = new GameObject("~SoundClick");
        var src = go.AddComponent<AudioSource>();
        var clip = ClickClip();
        src.PlayOneShot(clip, .7f);
        Destroy(go, clip.length + .1f);
    }

    static AudioClip click;
    static AudioClip ClickClip()
    {
        if (click != null) return click;
        const int rate = 22050;
        int n = rate / 12;
        var data = new float[n];
        var rng = new System.Random(7);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (noise - lp) * .22f;
            data[i] = lp * Mathf.Exp(-t * 60f) * .9f;
        }
        click = AudioClip.Create("SoundClick", n, 1, rate, false);
        click.SetData(data, 0);
        return click;
    }

    public void Refresh()
    {
        if (effects == null) return;
        refreshing = true;
        Paint(effects, "EFFECTS", SoundSettings.SfxOn, SoundSettings.SfxVolume);
        Paint(music, "MUSIC", SoundSettings.MusicOn, SoundSettings.MusicVolume);
        refreshing = false;
    }

    static void Paint(Row row, string name, bool on, float volume)
    {
        if (row.label != null) row.label.text = name + "\n" + SoundSettings.Label(on);
        row.slider.SetValueWithoutNotify(volume);
        // Dim the neon art (not a colour swap) while the bus is off.
        var tint = on ? Color.white : new Color(.55f, .58f, .72f, .85f);
        row.fill.color = tint;
        row.handle.color = tint;
        if (row.icon != null)
        {
            row.icon.sprite = IconSprite(row.music, IconLevel(on, volume));
            row.icon.color = Color.white;
        }
    }
}
