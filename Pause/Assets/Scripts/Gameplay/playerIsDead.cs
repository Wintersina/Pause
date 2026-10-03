using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

// Reveals flight results in a deliberate arcade sequence: best, this run,
// then the dust transfer into the persistent total.
public class playerIsDead : MonoBehaviour
{
    public Text deathHighScoreText;
    public Text deathHighestSpeedText;
    public Text deathSpeedReachedThisRoundText;
    bool waitingForDeath = true;
    float revealStartedAt, dustAtStart, dustWon;
    int runSpeed, previousBest, bestSpeed;
    bool newBest;
    RectTransform resultsRoot;
    readonly List<Image> dustBits = new List<Image>();
    Image celebrationCore;
    const float BestStart = .15f, RunStart = 1.20f, DustStart = 2.15f, DustFinish = 3.55f;

    void Start()
    {
        deathHighScoreText.gameObject.SetActive(false);
        deathHighestSpeedText.gameObject.SetActive(false);
        deathSpeedReachedThisRoundText.gameObject.SetActive(false);
    }

    void Update()
    {
        if (buttonClicks.playerDied && waitingForDeath) { BeginResults(); waitingForDeath = false; }
        if (!waitingForDeath && buttonClicks.playerDied) AnimateResults();
    }

    void BeginResults()
    {
        previousBest = Mathf.RoundToInt(PlayerPrefs.GetFloat("HighestSpeed"));
        runSpeed = Mathf.RoundToInt(moveBackGround.speed * 100f);
        bestSpeed = Mathf.Max(previousBest, runSpeed);
        newBest = runSpeed > previousBest;
        PlayerPrefs.SetFloat("HighestSpeed", bestSpeed);
        PlayerPrefs.SetFloat("PlayerCurrecny", score.totalCurrency);
        PlayerPrefs.Save();
        dustAtStart = score.runStartCurrency;
        dustWon = Mathf.Max(0f, score.totalCurrency - dustAtStart);
        revealStartedAt = Time.unscaledTime;
        BuildResultsStyle();
        LayoutCard(deathHighestSpeedText, new Vector2(0f, 88f), new Vector2(304f, 66f));
        LayoutCard(deathSpeedReachedThisRoundText, new Vector2(0f, 5f), new Vector2(304f, 66f));
        LayoutCard(deathHighScoreText, new Vector2(0f, -91f), new Vector2(304f, 96f));
        deathHighestSpeedText.gameObject.SetActive(true);
        deathSpeedReachedThisRoundText.gameObject.SetActive(true);
        deathHighScoreText.gameObject.SetActive(true);
        SetTextAlpha(deathHighestSpeedText, 0f); SetTextAlpha(deathSpeedReachedThisRoundText, 0f); SetTextAlpha(deathHighScoreText, 0f);
    }

    void AnimateResults()
    {
        float t = Time.unscaledTime - revealStartedAt;
        AnimateSpeedCard(deathHighestSpeedText, t, BestStart, bestSpeed, newBest ? "NEW BEST SPEED" : "BEST SPEED", new Color(.32f, .92f, 1f));
        AnimateSpeedCard(deathSpeedReachedThisRoundText, t, RunStart, runSpeed, "THIS RUN", new Color(1f, .43f, .33f));
        float p = Mathf.Clamp01((t - DustStart) / (DustFinish - DustStart));
        float eased = 1f - Mathf.Pow(1f - p, 3f);
        float transferred = dustWon * eased;
        deathHighScoreText.text = "STAR DUST  +" + transferred.ToString("F2") + "\nTOTAL  " + (dustAtStart + transferred).ToString("F2");
        SetTextAlpha(deathHighScoreText, Mathf.Clamp01((t - DustStart) * 4f));
        deathHighScoreText.transform.localScale = Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * 7f) * .018f + (p > .98f ? .08f : 0f));
        AnimateDustTransfer(p);
    }

    void AnimateSpeedCard(Text text, float time, float start, int target, string label, Color accent)
    {
        float p = Mathf.Clamp01((time - start) / .72f);
        float ease = 1f - Mathf.Pow(1f - p, 3f);
        text.text = label + "\n" + Mathf.RoundToInt(target * ease);
        SetTextAlpha(text, Mathf.Clamp01((time - start) * 5f));
        float pop = Mathf.Sin(Mathf.Clamp01((time - start) / .5f) * Mathf.PI) * .14f;
        text.transform.localScale = Vector3.one * (1f + pop + Mathf.Sin(Time.unscaledTime * 4f) * .012f);
        text.color = new Color(accent.r, accent.g, accent.b, text.color.a);
    }

    void BuildResultsStyle()
    {
        var old = GameObject.Find("DeathResultsBadge"); if (old != null) Destroy(old);
        var badge = new GameObject("DeathResultsBadge", typeof(RectTransform), typeof(Image), typeof(Outline));
        badge.transform.SetParent(deathHighScoreText.transform.parent, false);
        resultsRoot = badge.GetComponent<RectTransform>();
        resultsRoot.anchorMin = resultsRoot.anchorMax = new Vector2(.5f, .5f); resultsRoot.pivot = new Vector2(.5f, .5f);
        resultsRoot.anchoredPosition = new Vector2(0f, 24f); resultsRoot.sizeDelta = new Vector2(350f, 365f);
        badge.GetComponent<Image>().color = new Color(.018f, .028f, .09f, .96f);
        var edge = badge.GetComponent<Outline>(); edge.effectColor = new Color(.12f, .85f, 1f, .82f); edge.effectDistance = new Vector2(3f, -3f);
        CreateBanner("FLIGHT COMPLETE", new Vector2(0f, 157f), new Color(1f, .78f, .16f));
        CreateDecoration("ResultsRule", new Vector2(0f, 126f), new Vector2(292f, 3f), new Color(.18f, .86f, 1f, .8f)).rectTransform.SetAsFirstSibling();
        ReparentText(deathHighestSpeedText, new Color(.32f, .92f, 1f));
        ReparentText(deathSpeedReachedThisRoundText, new Color(1f, .43f, .33f));
        ReparentText(deathHighScoreText, new Color(1f, .78f, .18f));
        celebrationCore = CreateDecoration("DustCelebration", new Vector2(0f, -91f), new Vector2(145f, 145f), new Color(1f, .65f, .08f, 0f));
        celebrationCore.transform.SetAsFirstSibling();
        for (int i = 0; i < 14; i++) dustBits.Add(CreateDecoration("DustBit", new Vector2(0f, -18f), new Vector2(7f, 7f), new Color(1f, .76f, .2f, 0f)));
    }

    void ReparentText(Text text, Color color)
    {
        text.transform.SetParent(resultsRoot, false); text.fontSize = 22; text.fontStyle = FontStyle.Bold; text.alignment = TextAnchor.MiddleCenter; text.color = color;
        var outline = text.GetComponent<Outline>() ?? text.gameObject.AddComponent<Outline>(); outline.effectColor = new Color(0f, .03f, .12f, .98f); outline.effectDistance = new Vector2(2f, -2f);
        var card = CreateDecoration("ResultCard", Vector2.zero, Vector2.zero, new Color(color.r, color.g, color.b, .13f));
        card.transform.SetParent(text.transform, false); var rt = card.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(-7f, -4f); rt.offsetMax = new Vector2(7f, 4f);
        var edge = card.gameObject.AddComponent<Outline>(); edge.effectColor = new Color(color.r, color.g, color.b, .75f); edge.effectDistance = new Vector2(1f, -1f); card.transform.SetAsFirstSibling();
    }

    void LayoutCard(Text text, Vector2 position, Vector2 size)
    {
        var rt = text.transform as RectTransform; rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f); rt.pivot = new Vector2(.5f, .5f); rt.anchoredPosition = position; rt.sizeDelta = size;
    }

    void CreateBanner(string label, Vector2 position, Color color)
    {
        var go = new GameObject("ResultsTitle", typeof(RectTransform), typeof(Text), typeof(Outline)); go.transform.SetParent(resultsRoot, false);
        var rt = go.GetComponent<RectTransform>(); rt.anchoredPosition = position; rt.sizeDelta = new Vector2(315f, 34f);
        var text = go.GetComponent<Text>(); text.font = deathHighScoreText.font; text.text = "✦  " + label + "  ✦"; text.fontSize = 21; text.fontStyle = FontStyle.Bold; text.alignment = TextAnchor.MiddleCenter; text.color = color;
        var edge = go.GetComponent<Outline>(); edge.effectColor = new Color(.05f, .12f, .28f, 1f); edge.effectDistance = new Vector2(2f, -2f);
    }

    Image CreateDecoration(string name, Vector2 position, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(resultsRoot, false);
        var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
        var rt = image.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f); rt.anchoredPosition = position; rt.sizeDelta = size; return image;
    }

    void AnimateDustTransfer(float progress)
    {
        for (int i = 0; i < dustBits.Count; i++)
        {
            float phase = Mathf.Clamp01(progress * 1.4f - i * .055f);
            float x = Mathf.Sin(i * 2.31f) * (36f + (1f - phase) * 72f);
            float y = Mathf.Lerp(-10f, -91f, phase) + Mathf.Cos(i * 1.71f) * 22f * (1f - phase);
            dustBits[i].rectTransform.anchoredPosition = new Vector2(x, y); dustBits[i].color = new Color(1f, .76f, .2f, Mathf.Sin(phase * Mathf.PI) * .95f);
        }
        if (celebrationCore == null) return;
        float finished = Mathf.Clamp01((progress - .92f) / .08f);
        celebrationCore.color = new Color(1f, .68f, .12f, Mathf.Sin(finished * Mathf.PI) * .3f);
        celebrationCore.rectTransform.localScale = Vector3.one * (finished * 1.45f);
    }

    static void SetTextAlpha(Text text, float alpha) { var c = text.color; c.a = alpha; text.color = c; }
}
