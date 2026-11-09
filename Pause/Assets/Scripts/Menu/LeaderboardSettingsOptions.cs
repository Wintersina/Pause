using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The LEADERBOARD SETTINGS row of the Options screen (leaderboardS3): opens the modal with SYNC NOW, the sharing
// switch and DELETE MY SHARED SCORES (LeaderboardSettingsPanel). Built at runtime from the screen's own "Tutorial"
// button like the other rows, so it shares their font and look.
//
// It sits in the free slot above VIBRATION (y 340), in every build (the developer stack ends at 147).
public class LeaderboardSettingsOptions : MonoBehaviour
{
    public const string RowName = "LeaderboardSettingsRow";
    public const string Caption = "LEADERBOARD SETTINGS";
    public const float RowY = 340f, RowH = 88f, RowW = 520f, HitPad = 6f;

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "leaderboardS3" && FindFirstObjectByType<LeaderboardSettingsOptions>() == null)
            new GameObject("~LeaderboardSettingsOptions").AddComponent<LeaderboardSettingsOptions>();
    }

    void Start()
    {
        var template = SceneUtil.FindAny("Tutorial");
        if (template == null) return;
        var row = DeveloperOptions.Clone(template, OptionsScroll.RowParent(template), RowName,
                        new Vector2(0f, RowY), new Vector2(RowW, RowH), () => LeaderboardSettingsPanel.Open());
        var button = row.GetComponent<Button>();
        if (button != null && button.targetGraphic != null)
            button.targetGraphic.raycastPadding = new Vector4(0f, -HitPad, 0f, -HitPad);
        var label = row.GetComponentInChildren<Text>(true);
        if (label == null) return;
        label.text = Caption;
        label.color = AkiraPalette.Bone;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 18;
        label.resizeTextMaxSize = 34;
    }
}
