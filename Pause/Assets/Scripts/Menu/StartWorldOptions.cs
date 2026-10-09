using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The START WORLD row of the Options screen (leaderboardS3), built at runtime
// from the screen's own "Tutorial" button like DeveloperOptions' rows, so it
// shares their font, size and button look (in the player's colour, not the
// developer amber).
//
//   nothing finished yet    one dimmed, untappable row: START WORLD  LOCKED
//                           with the hint "BEAT A WORLD'S BOSS TO UNLOCK"
//   a level finished        [ < ]  START  <WORLD>  [ > ]   -- every world
//                           reached, wrapping; the name steps forward too
//   developer mode on       hidden: the developer's own picker takes this slot
//                           (and wins over this choice anyway)
//
// It sits in the slot just above LeaderBoard (y -133), or one row higher
// (-33, just under the developer picker's slot) in builds that offer developer mode, whose
// switch lives at -133.
public class StartWorldOptions : MonoBehaviour
{
    const float RowH = 88f;
    const float ArrowW = 100f;   // finger-sized: >= 96 units wide and (with the hit padding) tall
    const float HitPad = 6f;

    public const string RowName = "PlayerStartWorld";
    public const string PrevName = "PlayerPrevWorld", NextName = "PlayerNextWorld", NameName = "PlayerWorldName";
    public const string LockedName = "PlayerStartWorldLocked";

    GameObject row, lockedRow;
    Text nameLabel, lockedLabel, lockedHint;
    bool shownUnlocked, shownDev;
    int shownWorld = -2, shownHighest = -2;

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "leaderboardS3" && FindFirstObjectByType<StartWorldOptions>() == null)
            new GameObject("~StartWorldOptions").AddComponent<StartWorldOptions>();
    }

    public static float RowY { get { return DeveloperUnlocks.Available ? -33f : -133f; } }

    void Start()
    {
        var template = SceneUtil.FindAny("Tutorial");
        if (template == null) return;
        var parent = template.transform.parent;

        row = new GameObject(RowName, typeof(RectTransform));
        var rowRect = row.GetComponent<RectTransform>();
        rowRect.SetParent(parent, false);
        rowRect.anchorMin = rowRect.anchorMax = rowRect.pivot = new Vector2(0.5f, 0.5f);
        rowRect.anchoredPosition = new Vector2(0f, RowY);
        rowRect.sizeDelta = new Vector2(550f, RowH);

        Style(DeveloperOptions.Clone(template, row.transform, PrevName,
                    new Vector2(-225f, 0f), new Vector2(ArrowW, RowH), () => Step(-1))).text = "<";
        Style(DeveloperOptions.Clone(template, row.transform, NextName,
                    new Vector2(225f, 0f), new Vector2(ArrowW, RowH), () => Step(1))).text = ">";
        nameLabel = Style(DeveloperOptions.Clone(template, row.transform, NameName,
                    Vector2.zero, new Vector2(330f, RowH), () => Step(1)));

        var locked = DeveloperOptions.Clone(template, parent, LockedName,
                    new Vector2(0f, RowY), new Vector2(520f, RowH), () => { });
        lockedRow = locked;
        lockedLabel = Style(locked);
        lockedLabel.text = PlayerStartWorld.LockedLabel;
        lockedLabel.color = AkiraPalette.Muted;
        var button = locked.GetComponent<Button>();
        if (button != null) button.interactable = false;

        // The hint under the dimmed row's name.
        var hintGo = new GameObject("Hint", typeof(RectTransform));
        hintGo.transform.SetParent(locked.transform, false);
        var hr = hintGo.GetComponent<RectTransform>();
        hr.anchorMin = hr.anchorMax = new Vector2(.5f, .5f);
        hr.pivot = new Vector2(.5f, 1f);
        hr.anchoredPosition = new Vector2(0f, -RowH * .32f);
        hr.sizeDelta = new Vector2(520f, 28f);
        lockedHint = hintGo.AddComponent<Text>();
        lockedHint.font = lockedLabel.font;
        lockedHint.fontSize = 17;
        lockedHint.alignment = TextAnchor.MiddleCenter;
        lockedHint.color = AkiraPalette.Amber;
        lockedHint.raycastTarget = false;
        lockedHint.text = PlayerStartWorld.LockedHint;

        Refresh();
    }

    static Text Style(GameObject button)
    {
        var text = button.GetComponentInChildren<Text>(true);
        if (text == null) return null;
        // 88 units tall is under a 48 dp finger on a small phone: grow the hit area.
        var button0 = button.GetComponent<Button>();
        if (button0 != null && button0.targetGraphic != null)
            button0.targetGraphic.raycastPadding = new Vector4(0f, -HitPad, 0f, -HitPad);
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 18;
        text.resizeTextMaxSize = 34;
        return text;
    }

    void Step(int direction)
    {
        if (!PlayerStartWorld.Unlocked) return;
        PlayerStartWorld.Step(direction);
        Refresh();
    }

    public void Refresh()
    {
        bool dev = DeveloperUnlocks.Available && DeveloperUnlocks.Enabled;
        bool unlocked = PlayerStartWorld.Unlocked;
        shownDev = dev;
        shownUnlocked = unlocked;
        shownWorld = PlayerStartWorld.Selected;
        shownHighest = PlayerStartWorld.Highest;
        if (row != null) row.SetActive(unlocked && !dev);
        if (lockedRow != null) lockedRow.SetActive(!unlocked && !dev);
        if (nameLabel != null) nameLabel.text = PlayerStartWorld.Label(shownWorld);
    }

    void Update()
    {
        // The developer switch, or progress, changed under the open screen.
        bool dev = DeveloperUnlocks.Available && DeveloperUnlocks.Enabled;
        if (dev != shownDev || PlayerStartWorld.Unlocked != shownUnlocked ||
            PlayerStartWorld.Selected != shownWorld || PlayerStartWorld.Highest != shownHighest)
            Refresh();
    }
}
