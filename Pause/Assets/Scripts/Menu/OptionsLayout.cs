using UnityEngine;
using UnityEngine.UI;

// Where the player's Options rows sit (leaderboardS3), top to bottom, grouped by control type:
//
//   LEADERBOARD SETTINGS                       (opens a modal)
//   SWITCHES   VIBRATION, UPGRADE OFFERS, EFFECTS + MUSIC on/off
//   SHIP PERCH its own block between the groups: title, LEFT / TOP / RIGHT buttons, then the DISTANCE slider
//   SLIDERS    EFFECTS + MUSIC volume
//
// Builds that offer developer mode keep their older stack (the developer rows fill the middle), so every Y here has
// a developer-mode twin where the row exists there at all. Rows are 88 units tall (finger-sized with the hit pad),
// the SHIP PERCH direction selector 112 (title + 88 buttons) with its 88 slider directly under it, the two group headings 34.
public static class OptionsLayout
{
    public const float HeadingH = 34f, PerchDirectionH = 112f, PerchSliderH = 88f;

    // Switches and sliders are separate groups only in the player's build.
    public static bool Grouped { get { return !DeveloperUnlocks.Available; } }

    // Rows are 88 tall plus 6 units of touch padding top and bottom, so they sit 102 apart (tap rects never touch).
    public const float LeaderboardY = 667f;
    public const float SwitchesHeadingY = 606f;
    // The SHIP PERCH block (title + buttons, then the distance slider right under them) sits between the two groups.
    public const float VibrationY = 539f, UpgradeY = 437f, SoundSwitchY = 335f;
    public const float PerchDirectionY = 215f, PerchSliderY = 104f;
    public const float SlidersHeadingY = 22f;
    public const float SoundSliderY = -47f;

    // The order a player meets the rows in, for the layout tests.
    public static readonly string[] SwitchRows =
    {
        VibrationOptions.RowName, UpgradeOfferOptions.RowName, SoundOptions.RowName, SoundOptions.MusicRowName,
    };
    public static readonly string[] SliderRows = { SoundOptions.EffectsSliderName, SoundOptions.MusicSliderName };

    public const string SwitchesHeading = "SWITCHES", SlidersHeading = "SLIDERS";

    // A small caption over a group. Not a button, but OptionsScroll carries it with the rows.
    public static GameObject Heading(Transform parent, GameObject template, string name, string text, float y)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(OptionsHeading));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(520f, HeadingH);
        var t = go.AddComponent<Text>();
        var src = template != null ? template.GetComponentInChildren<Text>(true) : null;
        if (src != null) { t.font = src.font; t.fontStyle = src.fontStyle; }
        t.text = text;
        t.color = AkiraPalette.Muted;
        t.alignment = TextAnchor.MiddleCenter;
        t.raycastTarget = false;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = 14;
        t.resizeTextMaxSize = 22;
        return go;
    }
}

// Marks a non-interactive caption that moves with the Options column (OptionsScroll).
public class OptionsHeading : MonoBehaviour { }
