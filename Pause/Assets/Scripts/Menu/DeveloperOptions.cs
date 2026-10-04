using UnityEngine;
using UnityEngine.UI;

// The developer section of the Options screen (leaderboardS3): a developer
// mode switch and, while it is on, a start-world picker and the BOSS RUSH
// switch (every run's end-of-level boss arrives a few seconds in).
//
// Built at runtime from the screen's own "Tutorial" button, so it shares the
// screen's font, size and transparent-button look; the labels are tinted
// star-dust amber to read as a developer extra. Added only in builds where
// DeveloperUnlocks.Available (editor, development and PAUSE_DEV builds).
public class DeveloperOptions : MonoBehaviour
{
    static readonly Color DevTint = AkiraPalette.Amber;

    Text toggleLabel, worldLabel, bossLabel;
    GameObject worldRow, bossRow;

    void Start()
    {
        var template = SceneUtil.FindAny("Tutorial");
        if (template == null) return;
        var parent = template.transform.parent;

        var toggle = Clone(template, parent, "DeveloperToggle",
                           new Vector2(0f, -133f), new Vector2(331f, 99f), ToggleMode);
        toggleLabel = Label(toggle);

        worldRow = new GameObject("DeveloperStartWorld", typeof(RectTransform));
        var rowRect = worldRow.GetComponent<RectTransform>();
        rowRect.SetParent(parent, false);
        rowRect.anchorMin = rowRect.anchorMax = rowRect.pivot = new Vector2(0.5f, 0.5f);
        rowRect.anchoredPosition = new Vector2(0f, -43f);
        rowRect.sizeDelta = new Vector2(520f, 99f);

        Label(Clone(template, worldRow.transform, "PrevWorld",
                    new Vector2(-215f, 0f), new Vector2(80f, 99f), () => Step(-1))).text = "<";
        Label(Clone(template, worldRow.transform, "NextWorld",
                    new Vector2(215f, 0f), new Vector2(80f, 99f), () => Step(1))).text = ">";

        // The name itself is also a button: tapping it steps forward too.
        worldLabel = Label(Clone(template, worldRow.transform, "WorldName",
                                 Vector2.zero, new Vector2(350f, 99f), () => Step(1)));

        // Boss rush: every run's end-of-level boss arrives a few seconds in
        // (BossDev), for testing the encounters without flying a level.
        // Stacked above the start-world row (it used to sit at -223, on top
        // of the LeaderBoard button at -233).
        bossRow = Clone(template, parent, "DeveloperBossRush",
                        new Vector2(0f, 47f), new Vector2(420f, 99f), ToggleBossRush);
        bossLabel = Label(bossRow);

        Refresh();
    }

    void ToggleBossRush()
    {
        BossDev.SetRush(PlayerPrefs.GetInt(BossDev.RushKey, 0) != 1);
        Refresh();
    }

    static GameObject Clone(GameObject template, Transform parent, string name,
                            Vector2 position, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        var go = Instantiate(template, parent, false);
        go.name = name;
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        var button = go.GetComponent<Button>();
        if (button != null)
        {
            // Drop the template's persistent playTut call.
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(onClick);
        }
        return go;
    }

    static Text Label(GameObject button)
    {
        var text = button.GetComponentInChildren<Text>(true);
        if (text == null) return null;
        text.color = DevTint;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 18;
        text.resizeTextMaxSize = 34;
        return text;
    }

    void ToggleMode()
    {
        DeveloperUnlocks.SetEnabledByUser(!DeveloperUnlocks.Enabled);
        Refresh();
    }

    void Step(int direction)
    {
        int count = WorldManager.Worlds.Length;
        DeveloperUnlocks.SelectWorld((DeveloperUnlocks.SelectedWorld + direction + count) % count);
        Refresh();
    }

    void Refresh()
    {
        bool on = DeveloperUnlocks.Enabled;
        if (toggleLabel != null) toggleLabel.text = "DEVELOPER  " + (on ? "ON" : "OFF");
        if (worldRow != null) worldRow.SetActive(on);
        if (bossRow != null) bossRow.SetActive(on);
        if (bossLabel != null)
            bossLabel.text = "BOSS RUSH  " + (PlayerPrefs.GetInt(BossDev.RushKey, 0) == 1 ? "ON" : "OFF");
        if (worldLabel != null)
            worldLabel.text = "START  " +
                WorldManager.Worlds[DeveloperUnlocks.SelectedWorld].displayName.ToUpperInvariant();
    }
}
