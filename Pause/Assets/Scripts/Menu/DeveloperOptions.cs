using UnityEngine;
using UnityEngine.UI;

// The developer section of the Options screen (leaderboardS3): a developer
// mode switch and, while it is on, a start-world picker and the BOSS RUSH
// switch: ON (every run's end-of-level boss arrives a few seconds in) or
// FINAL (straight to the end of the final world's boss and the portal back
// round; see BossDev), and a FUSION TEST button (cycles the combos; only that
// combo's atoms spawn: FusionTestMode), +1000 STARDUST and a two-tap WIPE DUST &
// COLLECTION (DeveloperDustTools; they sit above LEADERBOARD SETTINGS).
//
// Built at runtime from the screen's own "Tutorial" button, so it shares the
// screen's font, size and transparent-button look; the labels are tinted
// star-dust amber to read as a developer extra. Added only in builds where
// DeveloperUnlocks.Available (editor, development and PAUSE_DEV builds).
public class DeveloperOptions : MonoBehaviour
{
    static readonly Color DevTint = AkiraPalette.Amber;

    const float RowH = 88f;   // rows are 90 apart (47, -43, -133)

    Text toggleLabel, worldLabel, bossLabel, fusionLabel, dustLabel, wipeLabel;
    GameObject worldRow, bossRow, fusionRow, dustRow, wipeRow;

    // Row y for the two dust tools: one pitch above LEADERBOARD SETTINGS (340), so they join the
    // Options column (and its scroll on short screens) without touching any existing row.
    const float DustToolsY = 437f;
    const float FeedbackSeconds = 1.6f;
    float wipeArmedAt = -1f, feedbackUntil = -1f, wipedUntil = -1f;
    string dustFeedback;

    void Start()
    {
        var template = SceneUtil.FindAny("Tutorial");
        if (template == null) return;
        var parent = OptionsScroll.RowParent(template);

        // Rows sit RowPitch apart; their hit areas are RowH tall so they no
        // longer overlap (they were 99 tall, 9 units into each other).
        var toggle = Clone(template, parent, "DeveloperToggle",
                           new Vector2(0f, -133f), new Vector2(331f, RowH), ToggleMode);
        toggleLabel = Label(toggle);

        worldRow = new GameObject("DeveloperStartWorld", typeof(RectTransform));
        var rowRect = worldRow.GetComponent<RectTransform>();
        rowRect.SetParent(parent, false);
        rowRect.anchorMin = rowRect.anchorMax = rowRect.pivot = new Vector2(0.5f, 0.5f);
        rowRect.anchoredPosition = new Vector2(0f, -43f);
        rowRect.sizeDelta = new Vector2(520f, RowH);

        Label(Clone(template, worldRow.transform, "PrevWorld",
                    new Vector2(-215f, 0f), new Vector2(80f, RowH), () => Step(-1))).text = "<";
        Label(Clone(template, worldRow.transform, "NextWorld",
                    new Vector2(215f, 0f), new Vector2(80f, RowH), () => Step(1))).text = ">";

        // The name itself is also a button: tapping it steps forward too.
        worldLabel = Label(Clone(template, worldRow.transform, "WorldName",
                                 Vector2.zero, new Vector2(330f, RowH), () => Step(1)));

        // Boss rush: every run's end-of-level boss arrives a few seconds in
        // (BossDev), for testing the encounters without flying a level.
        // Stacked above the start-world row (it used to sit at -223, on top
        // of the Credits (then LeaderBoard) button at -233).
        bossRow = Clone(template, parent, "DeveloperBossRush",
                        new Vector2(-140f, 47f), new Vector2(270f, RowH), ToggleBossRush);
        bossLabel = Label(bossRow);

        // Fusion test: cycles the combos; the spawner then releases only that
        // combo's atoms (FusionTestMode). Shares the boss rush row (half each),
        // so the Options stack gets no taller.
        fusionRow = Clone(template, parent, "DeveloperFusionTest",
                          new Vector2(140f, 47f), new Vector2(270f, RowH), StepFusion);
        fusionLabel = Label(fusionRow);

        // Dust tools (developer mode only): +1000 stardust per tap, and a two-tap wipe of dust and
        // collection for testing a fresh player (DeveloperDustTools).
        dustRow = Clone(template, parent, "DeveloperAddDust",
                        new Vector2(-140f, DustToolsY), new Vector2(270f, RowH), AddDust);
        dustLabel = Label(dustRow);
        wipeRow = Clone(template, parent, "DeveloperWipeDust",
                        new Vector2(140f, DustToolsY), new Vector2(270f, RowH), WipeDust);
        wipeLabel = Label(wipeRow);

        Refresh();
    }

    void Update()
    {
        float now = Time.unscaledTime;
        bool changed = false;
        if (feedbackUntil >= 0f && now >= feedbackUntil) { feedbackUntil = -1f; dustFeedback = null; changed = true; }
        if (wipedUntil >= 0f && now >= wipedUntil) { wipedUntil = -1f; changed = true; }
        if (wipeArmedAt >= 0f && now - wipeArmedAt > DeveloperDustTools.ConfirmSeconds) { wipeArmedAt = -1f; changed = true; }
        if (changed) Refresh();
    }

    void AddDust()
    {
        float balance = DeveloperDustTools.AddDust();
        if (balance < 0f) { Refresh(); return; }
        dustFeedback = "+1000  =  " + Mathf.FloorToInt(balance).ToString("N0");
        feedbackUntil = Time.unscaledTime + FeedbackSeconds;
        Refresh();
    }

    void WipeDust()
    {
        float armed = wipeArmedAt;
        if (DeveloperDustTools.Confirm(ref armed, Time.unscaledTime))
        {
            wipeArmedAt = -1f;
            if (DeveloperDustTools.Wipe()) wipedUntil = Time.unscaledTime + FeedbackSeconds;
        }
        else wipeArmedAt = armed;
        Refresh();
    }

    void StepFusion()
    {
        FusionTestMode.Step();
        Refresh();
    }

    void ToggleBossRush()
    {
        // OFF -> ON -> FINAL (straight to the final boss and its choice) -> OFF.
        BossDev.SetRushMode(BossDev.NextRushMode(BossDev.RushMode));
        Refresh();
    }

    internal static GameObject Clone(GameObject template, Transform parent, string name,
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
        CelPress.AddTo(go);   // every Options row dips when pressed, so it reads as tappable
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
        wipeArmedAt = -1f;
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
        if (fusionRow != null) fusionRow.SetActive(on);
        if (dustRow != null) dustRow.SetActive(on);
        if (wipeRow != null) wipeRow.SetActive(on);
        if (dustLabel != null) dustLabel.text = dustFeedback ?? "+1000 STARDUST";
        if (wipeLabel != null)
            wipeLabel.text = wipedUntil >= 0f ? "WIPED" : wipeArmedAt >= 0f ? "TAP AGAIN TO WIPE" : "WIPE DUST & COLLECTION";
        if (fusionLabel != null) fusionLabel.text = FusionTestMode.Label(FusionTestMode.Selected);
        if (bossLabel != null)
            bossLabel.text = BossDev.RushLabel(BossDev.RushMode);
        if (worldLabel != null)
            worldLabel.text = "START  " +
                WorldManager.Worlds[DeveloperUnlocks.SelectedWorld].displayName.ToUpperInvariant();
    }
}
