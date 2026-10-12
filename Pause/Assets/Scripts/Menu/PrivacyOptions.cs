using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The PRIVACY POLICY row of the Options screen (leaderboardS3): one button
// that opens the company policy in the browser (ExternalLinks.PrivacyPolicy).
// Built at runtime from the screen's own "Tutorial" button like SOUND, so it
// shares its font, size and button look. Sits directly above the SOUND row
// (and above the developer stack in developer builds), where the account
// card sits just over it. Menu buttons have no click sound, so neither does
// this one.
public class PrivacyOptions : MonoBehaviour
{
    const float RowH = 88f;
    const float HitPad = 6f;
    public const string RowName = "PrivacyPolicy";
    public const string Caption = "PRIVACY POLICY";

    public static float RowY { get { return SoundOptions.RowY + 100f; } }

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "leaderboardS3" && FindFirstObjectByType<PrivacyOptions>() == null)
            new GameObject("~PrivacyOptions").AddComponent<PrivacyOptions>();
    }

    void Start()
    {
        var template = SceneUtil.FindAny("Tutorial");
        if (template == null) return;
        var row = DeveloperOptions.Clone(template, template.transform.parent, RowName,
                    new Vector2(0f, RowY), new Vector2(420f, RowH), Open);
        var button = row.GetComponent<Button>();
        if (button != null && button.targetGraphic != null)
            button.targetGraphic.raycastPadding = new Vector4(0f, -HitPad, 0f, -HitPad);
        var label = row.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            label.text = Caption;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 18;
            label.resizeTextMaxSize = 34;
        }
    }

    public static void Open() { ExternalLinks.Open(ExternalLinks.PrivacyPolicy); }
}
