using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Restyles the in-game read-out.
//
// It was three lines of flat grey label text on a plain panel -- readable, but
// it looked like a debug overlay rather than part of the game. This gives each
// stat a colour and an icon-ish prefix, adds outlines so they hold up over a
// bright starfield, and puts a subtle bar behind the pause counter so the
// resource you actually spend is the thing your eye lands on.
//
// Purely presentational: it never changes what the numbers say.
public class HudStyler : MonoBehaviour
{
    static readonly Color Speed = new Color(0.53f, 0.85f, 1f);
    static readonly Color Dust = new Color(1f, 0.79f, 0.26f);
    static readonly Color Pause = new Color(0.62f, 1f, 0.70f);
    static readonly Color PauseLow = new Color(1f, 0.42f, 0.38f);

    Text speedText, dustText, pauseText;
    Image pauseBar;

    void Start()
    {
        speedText = Find("SpeedText");
        dustText = Find("CurrecnyGatheredText");
        // gameS1 names this PauseCounter; tutorialS5 names the same readout
        // PausesRemainingText. Find() returning null for the first name used
        // to short-circuit Update() entirely (see below), which is why none
        // of the three stats styled in the tutorial, not just the pause one.
        pauseText = Find("PauseCounter") ?? Find("PausesRemainingText");

        Style(speedText, Speed, 26);
        Style(dustText, Dust, 26);
        Style(pauseText, Pause, 30);

        if (pauseText != null) pauseBar = BuildPauseBar(pauseText);
    }

    static Text Find(string name)
    {
        var go = SceneUtil.FindAny(name);
        return go != null ? go.GetComponent<Text>() : null;
    }

    static void Style(Text t, Color colour, int size)
    {
        if (t == null) return;
        t.color = colour;
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
        t.resizeTextForBestFit = false;

        var outline = t.GetComponent<Outline>();
        if (outline == null) outline = t.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);
    }

    // A thin depleting bar under the pause counter, so the most important
    // number is readable at a glance instead of being parsed as text.
    //
    // This used to hang 4-10px *below* the pause counter's own rect. The
    // panel's VerticalLayoutGroup has no idea that extra height exists --
    // PauseBar is a plain child of PauseCounter, invisible to the group's own
    // spacing math -- so the fixed gap it left before the next stacked
    // element (the blue-atom timer, gotAtomText) was not enough to clear it,
    // and the two overlapped. Contained within the bottom of PauseCounter's
    // own allocated rect instead, it cannot spill into whatever the layout
    // group stacks next, on any screen size.
    static Image BuildPauseBar(Text anchor)
    {
        var holder = new GameObject("PauseBar", typeof(Image));
        holder.transform.SetParent(anchor.transform, false);

        var rt = holder.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0.16f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = holder.GetComponent<Image>();
        img.color = Pause;
        img.raycastTarget = false;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        return img;
    }

    void Update()
    {
        // Each stat is independently guarded rather than one shared early
        // return -- a single missing element (the pause counter's name
        // mismatch in the tutorial) used to silently skip every stat here,
        // not just the one that could not be found.
        if (pauseText != null)
        {
            int left = Mathf.Max(0, score.pauseCounter);
            bool low = left <= 1;

            pauseText.color = low ? PauseLow : Pause;
            pauseText.text = "PAUSES  " + left;

            if (pauseBar != null)
            {
                // five is a full run's allotment; anything above that just fills it
                pauseBar.fillAmount = Mathf.Clamp01(left / 5f);
                pauseBar.color = low ? PauseLow : Pause;
            }
        }

        if (speedText != null)
            speedText.text = "SPEED  " + Mathf.RoundToInt(moveBackGround.speed * 100f);

        if (dustText != null)
            dustText.text = "★ " + score.totalCurrency.ToString("F1");
    }
}

public static class HudStylerBootstrap
{
    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "gameS1" && scene.name != "tutorialS5") return;
        if (Object.FindFirstObjectByType<HudStyler>() != null) return;
        new GameObject("~HudStyler").AddComponent<HudStyler>();
    }
}
