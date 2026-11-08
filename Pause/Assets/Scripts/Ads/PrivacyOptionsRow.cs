using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The "PRIVACY OPTIONS" row of the Options screen (leaderboardS3): reopens Google's UMP consent form,
// as UMP requires wherever a privacy-options entry is required (EEA / UK). Only built while ads are on
// and UMP says the entry is required. Built from the screen's own "Tutorial" button like the other rows.
public class PrivacyOptionsRow : MonoBehaviour
{
    public const string RowName = "PrivacyOptions";
    // The row sits where the DEVELOPER switch sits in dev builds (-133), sharing it half and half like the boss rush /
    // fusion pair: -223 (its old dev slot) landed on the Credits button at -233, and the Options stack has no free slot.
    public const float RowY = -133f, RowH = 88f, RowW = 520f, HalfW = 270f, HalfX = 140f;
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
        if (!AdManager.PrivacyOptionsRequired) { StartCoroutine(Wait()); return; }
        StartCoroutine(BuildNextFrame());
    }

    // One frame late so the developer rows (built in their own Start) exist and can be shared with.
    System.Collections.IEnumerator BuildNextFrame() { yield return null; Build(); }

    System.Collections.IEnumerator Wait()
    {
        for (float t = 0f; t < 30f; t += Time.unscaledDeltaTime)
        {
            if (AdManager.PrivacyOptionsRequired) { yield return null; Build(); yield break; }
            yield return null;
        }
    }

    public void Build()
    {
        var template = SceneUtil.FindAny("Tutorial");
        if (template == null) return;
        bool shared = DeveloperUnlocks.Available;
        if (shared)
        {
            // Developer builds: the DEVELOPER switch takes the left half, this row the right half.
            var toggle = SceneUtil.FindAny("DeveloperToggle");
            var rect = toggle != null ? toggle.GetComponent<RectTransform>() : null;
            if (rect != null) { rect.anchoredPosition = new Vector2(-HalfX, RowY); rect.sizeDelta = new Vector2(HalfW, RowH); }
        }
        var row = DeveloperOptions.Clone(template, template.transform.parent, RowName,
                        new Vector2(shared ? HalfX : 0f, RowY), new Vector2(shared ? HalfW : RowW, RowH),
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
