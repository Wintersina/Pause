using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The end-of-run star dust score bonus (ScoreRules.ScoreDustBonus, paid by
// score.Settle through StarDustLedger.PayScoreBonus):
//   1  diminishing, monotonic and capped
//   2  banked exactly once on death, Menu, Replay and backgrounding; none in
//      the tutorial (practice) or developer mode
//   3  shown on the Flight Complete panel (count-up, sparkle, popup) and the
//      panel still fits
public static class ScoreBonusTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BONUS] PASS  " : "[BONUS] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    const string Dust = StarDustLedger.CurrencyKey;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Curve();
            BankedOnce();
            OnThePanel();
        }
        finally
        {
            buttonClicks.playerDied = false;
            RunScore.EndRun(RunScore.RunId);
            StarDustLedger.EndRun(StarDustLedger.RunId);
        }
        Debug.Log("[BONUS] failures: " + fails);
        return fails;
    }

    // ---- 1. the curve ----------------------------------------------------------

    static void Curve()
    {
        Check("no score, no bonus", ScoreRules.ScoreDustBonus(0) == 0f && ScoreRules.ScoreDustBonus(-5) == 0f);
        bool monotonic = true, diminishing = true;
        float prev = 0f, prevStep = float.MaxValue;
        for (long sc = 250; sc <= 6000; sc += 250)
        {
            float b = ScoreRules.ScoreDustBonus(sc);
            monotonic &= b >= prev;
            float step = b - prev;
            if (sc > 250 && b < ScoreRules.ScoreDustCap) diminishing &= step <= prevStep + .011f;
            prevStep = step;
            prev = b;
        }
        Check("the bonus never falls as the score rises", monotonic);
        Check("each extra 250 points adds less (diminishing)", diminishing);
        float paidCap = Mathf.Round(ScoreRules.ScoreDustCap * ScoreRules.DustRewardScale * 100f) / 100f;
        Check("capped at " + paidCap + " (" + ScoreRules.ScoreDustCap + " x DustRewardScale) however big the loop run",
              ScoreRules.ScoreDustBonus(1000000) == paidCap && ScoreRules.ScoreDustBonus(14757) == paidCap);
        foreach (long sc in new long[] { 200, 400, 600, 1466, 3000, 6645, 14757 })
            Debug.Log("[BONUS] score " + sc + " -> +" + ScoreRules.ScoreDustBonus(sc).ToString("F2") + " star dust");
        Check("small: 400 points -> 0.24 (0.40 x 0.60), a whole first pass (6,645) -> the 0.90 cap",
              Mathf.Approximately(ScoreRules.ScoreDustBonus(400), .24f) && Mathf.Approximately(ScoreRules.ScoreDustBonus(6645), .9f));
        Check("a top-up, not an income: the cap is under 1% of the cheapest ship (600)",
              ScoreRules.ScoreDustCap <= shopingShips.Prices[2] * .01f);
    }

    // ---- 2. banking ----------------------------------------------------------------

    static score MakeScore()
    {
        var go = new GameObject("~ScoreBonus");
        var s = go.AddComponent<score>();
        s.speedValue = new GameObject("s").AddComponent<Text>();
        s.pauseCounterText = new GameObject("p").AddComponent<Text>();
        return s;
    }

    static void Call(Object o, string method)
    {
        o.GetType().GetMethod(method, Inst).Invoke(o, null);
    }

    static void RealRun()
    {
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        moveBackGround.speed = 0f;
    }

    static bool Saved(float v) { return Mathf.Abs(PlayerPrefs.GetFloat(Dust) - v) < .001f; }

    static void BankedOnce()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RealRun();
        RunScore.EndRun(RunScore.RunId);
        StarDustLedger.EndRun(StarDustLedger.RunId);
        float bonus = ScoreRules.ScoreDustBonus(300);
        Check("a 300-point run earns " + bonus, bonus > 0f);

        // Death, the death panel, then the teardown.
        PlayerPrefs.SetFloat(Dust, 100f);
        var s = MakeScore();
        Call(s, "Awake");
        Call(s, "Start");
        RunScore.OnBoss(true, 0f, false, Vector3.zero);   // 300 points
        buttonClicks.playerDied = true;
        Call(s, "Update");
        Check("death: the bonus is banked (" + PlayerPrefs.GetFloat(Dust) + ")", Saved(100f + bonus));
        Check("the death panel's settle pays nothing more", score.SettleCurrentRun() == bonus && Saved(100f + bonus));
        Check("score.totalCurrency includes it (what the death screen writes)",
              Mathf.Abs(score.totalCurrency - (100f + bonus)) < .001f);
        Call(s, "Update");
        Call(s, "OnDestroy");
        Check("death + teardown: banked once", Saved(100f + bonus));
        Object.DestroyImmediate(s.gameObject);
        buttonClicks.playerDied = false;

        // Menu / Back: the teardown pays it.
        PlayerPrefs.SetFloat(Dust, 100f);
        s = MakeScore();
        Call(s, "Awake");
        RunScore.OnBoss(true, 0f, false, Vector3.zero);
        Call(s, "OnDestroy");
        Call(s, "OnDestroy");
        Check("Menu: banked once", Saved(100f + bonus));
        Object.DestroyImmediate(s.gameObject);

        // Replay: the next run's Awake settles the old one first.
        PlayerPrefs.SetFloat(Dust, 100f);
        var old = MakeScore();
        Call(old, "Awake");
        RunScore.OnBoss(true, 0f, false, Vector3.zero);
        var next = MakeScore();
        Call(next, "Awake");
        Check("Replay: the old run's bonus is banked before the new run starts",
              Saved(100f + bonus) && StarDustLedger.Earned == 0f && !StarDustLedger.BonusPaid);
        Call(old, "OnDestroy");
        Check("Replay: the old scene's late teardown pays nothing again",
              Mathf.Abs(StarDustLedger.Balance - (100f + bonus)) < .001f && !StarDustLedger.BonusPaid);
        Call(next, "OnDestroy");
        Check("Replay: a scoreless new run adds no bonus", Saved(100f + bonus));
        Object.DestroyImmediate(old.gameObject);
        Object.DestroyImmediate(next.gameObject);

        // Backgrounding stages the dust but never pays the bonus early.
        PlayerPrefs.SetFloat(Dust, 100f);
        s = MakeScore();
        Call(s, "Awake");
        RunScore.OnBoss(true, 0f, false, Vector3.zero);
        StarDustLedger.Stage();
        RunScore.Stage();
        Check("backgrounding: no bonus while the run is open", Saved(100f) && !StarDustLedger.BonusPaid);
        Call(s, "OnDestroy");
        Check("... then paid once at the end", Saved(100f + bonus));
        Object.DestroyImmediate(s.gameObject);

        // Tutorial / practice and developer mode: never.
        PlayerPrefs.SetFloat(Dust, 100f);
        PlayerPrefs.DeleteKey("HasDoneTut");
        s = MakeScore();
        Call(s, "Awake");
        RunScore.OnBoss(true, 0f, false, Vector3.zero);
        Call(s, "OnDestroy");
        Check("practice / tutorial run: no bonus, saved dust untouched", Saved(100f) && StarDustLedger.Bonus == 0f);
        Object.DestroyImmediate(s.gameObject);

        RealRun();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        s = MakeScore();
        Call(s, "Awake");
        for (int i = 0; i < 20; i++) RunScore.OnBoss(true, 0f, false, Vector3.zero);
        Call(s, "OnDestroy");
        Check("developer run: no bonus however big the score", Saved(100f) && StarDustLedger.Bonus == 0f);
        Object.DestroyImmediate(s.gameObject);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);

        Check("playerIsDead settles the bonus before reading the totals",
              File.ReadAllText("Assets/Scripts/Gameplay/playerIsDead.cs").Contains("float scoreBonus = score.SettleCurrentRun();"));
    }

    // ---- 3. the panel ------------------------------------------------------------------

    static void OnThePanel()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        var canvas = SceneUtil.FindAny("PopUpCanvas");
        var best = SceneUtil.FindAny("playerDeadHighestSpeed").GetComponent<Text>();
        var run = SceneUtil.FindAny("deathSpeedReachedThisRoundText").GetComponent<Text>();
        var dust = SceneUtil.FindAny("playerDeadHighScore").GetComponent<Text>();
        var parts = new RunScore.Breakdown { distance = 900, kills = 1200, bosses = 300, bossCount = 1 };
        var view = DeathPanelView.Build(canvas.transform, best, run, dust,
            SceneUtil.FindAny("Replay").GetComponent<Button>(), SceneUtil.FindAny("MainMenu").GetComponent<Button>(),
            new DeathPanelView.Results { score = parts.Total, bestScore = 9000, ranked = true, parts = parts,
                                         dustAtStart = 99987.65f, dustWon = 3.25f, dustBonus = 1.5f });
        var line = view.Panel.Find("Card2/" + DeathPanelView.ScoreBonusName);
        Check("the star dust card has a SCORE BONUS line", line != null);
        if (line == null) { Object.DestroyImmediate(view.gameObject); return; }
        var text = line.GetComponent<Text>();
        view.ApplyAt(.85f);
        Check("mid-intro: the run's own dust first, the bonus still +0.00 ('" + text.text + "')", text.text == "SCORE BONUS  +0.00");
        view.ApplyAt(.96f);
        float mid = float.Parse(text.text.Substring(text.text.IndexOf('+') + 1), System.Globalization.CultureInfo.InvariantCulture);
        Check("... then it counts up (" + text.text + ")", mid > 0f && mid < 1.5f);
        view.ApplyAt(1.1f);
        var popup = view.Panel.Find(DeathPanelView.BonusPopupName);
        bool sparkle = false;
        foreach (Transform t in view.Panel) if (t.name == "BonusBurst" && t.gameObject.activeSelf) sparkle = true;
        Check("it lands with a popup and a sparkle", popup != null && popup.gameObject.activeSelf && sparkle);
        view.Skip();
        Canvas.ForceUpdateCanvases();
        Check("settled: SCORE BONUS +1.50 ('" + text.text + "'), EARNED +3.25 includes it ('" + dust.text + "')",
              text.text == "SCORE BONUS  +1.50" && dust.text == "+3.25");
        var total = dust.transform.parent.Find("Total").GetComponent<Text>();
        Check("the total includes it ('" + total.text + "')", total.text == "TOTAL  99990.90");
        Check("the popup is gone once settled", !popup.gameObject.activeSelf);
        var sub = view.Panel.Find("Card2/Sub");
        Check("it takes the sub-label's place", sub == null || !sub.gameObject.activeSelf);

        // Fits: inside its card, clear of the earned figure, inside the panel.
        var card = (RectTransform)view.Panel.Find("Card2");
        Rect lineRect = PanelSpace(view.Panel, text.rectTransform);
        float lineRight = lineRect.xMin + text.preferredWidth;
        Rect valueRect = PanelSpace(view.Panel, dust.rectTransform);
        Check("the bonus line fits its rect and its card",
              text.preferredWidth <= lineRect.width && PanelSpace(view.Panel, card).Contains(lineRect.min) &&
              PanelSpace(view.Panel, card).Contains(lineRect.max));
        Check("the bonus line clears the earned figure", lineRight + 12f <= valueRect.xMax - dust.preferredWidth);
        var panelRect = DeathPanelView.PanelRect;
        bool inside = true;
        foreach (var g in view.Panel.GetComponentsInChildren<Graphic>(false))
        {
            if (g.name == "Frame" || g.name == "Glow" || g.name == "NewBestGlow") continue;
            var r = PanelSpace(view.Panel, g.rectTransform);
            if (r.xMin < panelRect.xMin - .5f || r.xMax > panelRect.xMax + .5f || r.yMin < panelRect.yMin - .5f || r.yMax > panelRect.yMax + .5f)
            {
                inside = false;
                Debug.Log("[BONUS] outside: " + g.name + " " + r);
            }
        }
        Check("everything still sits inside the panel (its size and fit are unchanged at every aspect)",
              inside && DeathPanelView.Height == 760f && DeathPanelView.Width == 680f);
        Object.DestroyImmediate(view.gameObject);
    }

    static Rect PanelSpace(RectTransform panel, RectTransform rt)
    {
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        Vector2 a = panel.InverseTransformPoint(c[0]);
        Vector2 b = panel.InverseTransformPoint(c[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }
}
