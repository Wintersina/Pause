using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The "PRIVACY OPTIONS" row of the Options screen (leaderboardS3): reopens Google's UMP consent form,
// as UMP requires wherever a privacy-options entry is required (EEA / UK). Only built while ads are on
// and UMP says the entry is required. Built from the screen's own "Tutorial" button like the other rows.
public class PrivacyOptionsRow : MonoBehaviour
{
    public const string RowName = "PrivacyOptions";
    // The row gets a slot of its own (never shared with another row), per layout of the Options column:
    //  - release builds: -133, the free slot between the sound row (-33) and Credits (-233);
    //  - developer builds: 243, the free slot between LEADERBOARD SETTINGS (340) and the sound row (147).
    // (-133 is the DEVELOPER switch in developer builds, -223 lands on Credits at -233.)
    public const float ReleaseRowY = -133f, DevRowY = 243f, RowH = 88f, RowW = 520f;
    public static float RowY { get { return DeveloperUnlocks.Available ? DevRowY : ReleaseRowY; } }

#if UNITY_EDITOR
    // Edit-mode tests only: pretend UMP does / does not require the privacy-options entry.
    public static bool? TestRequiredOverride;
#endif
    public static bool Required
    {
        get
        {
#if UNITY_EDITOR
            if (TestRequiredOverride.HasValue) return TestRequiredOverride.Value;
#endif
            return AdManager.PrivacyOptionsRequired;
        }
    }

    public const string Caption = "PRIVACY OPTIONS";

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "leaderboardS3" || !AdsConfig.Enabled || FindFirstObjectByType<PrivacyOptionsRow>() != null) return;
        new GameObject("~PrivacyOptionsRow").AddComponent<PrivacyOptionsRow>();
    }

    void Start()
    {
        // UMP may still be gathering consent when the screen opens; wait for it.
        if (!Required) { StartCoroutine(Wait()); return; }
        StartCoroutine(BuildNextFrame());
    }

    System.Collections.IEnumerator BuildNextFrame() { yield return null; Build(); }

    System.Collections.IEnumerator Wait()
    {
        for (float t = 0f; t < 30f; t += Time.unscaledDeltaTime)
        {
            if (Required) { yield return null; Build(); yield break; }
            yield return null;
        }
    }

    public void Build()
    {
        var template = SceneUtil.FindAny("Tutorial");
        if (template == null) return;
        var row = DeveloperOptions.Clone(template, OptionsScroll.RowParent(template), RowName,
                        new Vector2(0f, RowY), new Vector2(RowW, RowH),
                        () => AdManager.ShowPrivacyOptions(null));
        var label = row.GetComponentInChildren<Text>(true);
        if (label == null) return;
        label.text = Caption;
        label.color = AkiraPalette.Bone;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 18;
        label.resizeTextMaxSize = 34;
    }
}
