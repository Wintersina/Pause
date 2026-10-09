using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The SOUND row of the Options screen (leaderboardS3): one toggle reading
// SOUND: ON / SOUND: OFF (see SoundSettings). Built at runtime from the
// screen's own "Tutorial" button like START WORLD, so it shares its font,
// size and button look.
//
// It sits in the slot above START WORLD (y -33), or, in builds that offer
// developer mode (whose rows fill -133 / -43 / 47), on top of the developer
// stack at 137.
public class SoundOptions : MonoBehaviour
{
    const float RowH = 88f;
    const float HitPad = 6f;
    public const string RowName = "SoundToggle";

    public static float RowY { get { return DeveloperUnlocks.Available ? 137f : -33f; } }

    GameObject row;
    Text label;

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
        row = DeveloperOptions.Clone(template, template.transform.parent, RowName,
                    new Vector2(0f, RowY), new Vector2(420f, RowH), Toggle);
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
        Refresh();
    }

    void Toggle()
    {
        bool wasMuted = SoundSettings.Muted;
        SoundSettings.SetMuted(!wasMuted);
        if (wasMuted) Click();   // a short confirmation, only when sound comes back
        Refresh();
    }

    static void Click()
    {
        var go = new GameObject("~SoundClick");
        var src = go.AddComponent<AudioSource>();
        var clip = ClickClip();
        src.PlayOneShot(clip, .6f);
        Destroy(go, clip.length + .1f);
    }

    static AudioClip click;
    static AudioClip ClickClip()
    {
        if (click != null) return click;
        const int rate = 22050;
        int n = rate / 14;
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            data[i] = Mathf.Sin(2f * Mathf.PI * 880f * t) * Mathf.Exp(-t * 55f) * .5f;
        }
        click = AudioClip.Create("SoundClick", n, 1, rate, false);
        click.SetData(data, 0);
        return click;
    }

    public void Refresh()
    {
        if (label != null) label.text = SoundSettings.Label(SoundSettings.Muted);
    }
}
