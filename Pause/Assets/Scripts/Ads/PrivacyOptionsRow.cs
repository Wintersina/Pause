using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The "PRIVACY OPTIONS" row of the Options screen (leaderboardS3): reopens Google's UMP consent form,
// as UMP requires wherever a privacy-options entry is required (EEA / UK). Only built while ads are on
// and UMP says the entry is required. Built from the screen's own "Tutorial" button like the other rows.
public class PrivacyOptionsRow : MonoBehaviour
{
    public const string RowName = "PrivacyOptions";
    public const float RowY = -133f, DevRowY = -223f, RowH = 88f, RowW = 520f;
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
        Build();
    }

    System.Collections.IEnumerator Wait()
    {
        for (float t = 0f; t < 30f; t += Time.unscaledDeltaTime)
        {
            if (AdManager.PrivacyOptionsRequired) { Build(); yield break; }
            yield return null;
        }
    }

    void Build()
    {
        var template = SceneUtil.FindAny("Tutorial");
        if (template == null) return;
        float y = DeveloperUnlocks.Available ? DevRowY : RowY;
        var row = DeveloperOptions.Clone(template, template.transform.parent, RowName,
                        new Vector2(0f, y), new Vector2(RowW, RowH), () => AdManager.ShowPrivacyOptions(null));
        var label = row.GetComponentInChildren<Text>(true);
        if (label == null) return;
        label.text = Caption;
        label.color = AkiraPalette.Bone;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 18;
        label.resizeTextMaxSize = 34;
    }
}
