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
        pauseText = Find("PauseCounter");

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
    static Image BuildPauseBar(Text anchor)
    {
        var holder = new GameObject("PauseBar", typeof(Image));
        holder.transform.SetParent(anchor.transform, false);

        var rt = holder.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0f, 1f);
        rt.offsetMin = new Vector2(0f, -10f);
        rt.offsetMax = new Vector2(0f, -4f);

        var img = holder.GetComponent<Image>();
        img.color = Pause;
        img.raycastTarget = false;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        return img;
    }

    void Update()
    {
        if (pauseText == null) return;

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
