using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The UPGRADE OFFERS row of the Options screen (leaderboardS3): one tap flips
// whether the death screen may offer a star dust upgrade after two deaths in
// a row (UpgradeOfferStore.Enabled; default ON). Built at runtime from the
// screen's own "Tutorial" button like the sound and start-world rows, so it
// shares their font and look.
//
// It sits in the free slot above the EFFECTS / MUSIC row (y 57). Builds that
// offer developer mode fill that area with the developer rows, so the row is
// left out there (the setting still works; it is just not shown).
public class UpgradeOfferOptions : MonoBehaviour
{
    public const string RowName = "UpgradeOffersToggle";
    public const float RowY = 57f, RowH = 88f, RowW = 520f, HitPad = 6f;

    Text label;

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "leaderboardS3" && !DeveloperUnlocks.Available && FindFirstObjectByType<UpgradeOfferOptions>() == null)
            new GameObject("~UpgradeOfferOptions").AddComponent<UpgradeOfferOptions>();
    }

    public static string Caption(bool on) { return on ? "UPGRADE OFFERS  ON" : "UPGRADE OFFERS  OFF"; }

    void Start()
    {
        var template = SceneUtil.FindAny("Tutorial");
        if (template == null) return;
        var row = DeveloperOptions.Clone(template, template.transform.parent, RowName,
                        new Vector2(0f, RowY), new Vector2(RowW, RowH), Toggle);
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
        UpgradeOfferStore.Enabled = !UpgradeOfferStore.Enabled;
        PrefsSaver.SaveNow();
        Refresh();
    }

    void Refresh()
    {
        if (label == null) return;
        bool on = UpgradeOfferStore.Enabled;
        label.text = Caption(on);
        label.color = on ? AkiraPalette.Bone : AkiraPalette.Muted;
    }
}
