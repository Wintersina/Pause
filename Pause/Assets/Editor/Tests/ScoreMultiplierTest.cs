using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The max-speed streak (ScoreMultiplier): hold the speed cap for 15 s without
// losing a heart and score doubles. A fake clock (the dt handed to
// RunScore.Tick) drives it: nothing here reads real time.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod ScoreMultiplierTest.Run
public static class ScoreMultiplierTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[X2] PASS  " : "[X2] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    static readonly float Cap = SpeedRamp.Cap;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            TunablesAreInOnePlace();
            NoMultiplierBefore15Seconds();
            ExactThreshold();
            BreaksOnHeartLost();
            BreaksBelowCap();
            PausedTimeIgnored();
            DoublesGainedScoreNotPrevious();
            EverySourceIsDoubled();
            ResetsOnNewRun();
            NeverNegativeOrOverflowing();
            TimeInX2Stat();
            LeaderboardsSeeTheDoubledTotal();
            CueStatesAndVisibility();
            CueLayout();
            CueAllocatesNothing();
            ShieldedAndHealedDoNotBreak();
        }
        finally
        {
            ScoreMultiplier.Reset();
            moveBackGround.speed = 0f;
            buttonClicks.playerDied = false;
        }
        Debug.Log("[X2] failures: " + fails);
        return fails;
    }

    // ---- fixtures ---------------------------------------------------------------

    static void NewRun()
    {
        RunScore.EndRun(RunScore.RunId);
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.DeleteKey(RunScore.BestScoreKey);
        startMenu.youAreInTutorial = false;
        buttonClicks.playerDied = false;
        moveBackGround.speed = Cap;
        RunScore.BeginRun(true, true);
    }

    // Seconds of flight at `speed` on the fake clock, in 60 fps frames.
    static void Fly(float seconds, float speed)
    {
        int frames = Mathf.RoundToInt(seconds / Dt);
        for (int i = 0; i < frames; i++)
        {
            moveBackGround.speed = speed;
            RunScore.Tick(Dt, speed);
        }
    }

    // What one small star dust pays right now.
    static long DustPays()
    {
        long before = RunScore.Total;
        RunScore.OnDust(false);
        return RunScore.Total - before;
    }

    // ---- rules ------------------------------------------------------------------

    static void TunablesAreInOnePlace()
    {
        Check("15 s and x2 are the named constants", ScoreMultiplier.StreakSeconds == 15f && ScoreMultiplier.Factor == 2);
        Check("the cap is HUD 35", SpeedRamp.CapHud == 35 && ScoreMultiplier.AtCap(.35f) && !ScoreMultiplier.AtCap(.349f));
        Check("a hair under the cap still counts (epsilon)", ScoreMultiplier.AtCap(.35f - ScoreMultiplier.CapEpsilon * .5f));
        string src = File.ReadAllText("Assets/Scripts/Core/Scoring/RunScore.cs");
        Check("RunScore holds no 15 or x2 of its own", !src.Contains("15f") && !src.Contains("* 2f") && src.Contains("ScoreMultiplier.Gain"));
    }

    static void NoMultiplierBefore15Seconds()
    {
        NewRun();
        Fly(14.9f, Cap);
        Check("14.9 s at the cap: not active", !ScoreMultiplier.Active && ScoreMultiplier.Current == 1);
        Check("... and its progress is under 1", ScoreMultiplier.Progress01 < 1f && ScoreMultiplier.Progress01 > .98f);
        long dust = DustPays();
        Check("... a pickup pays its plain " + ScoreRules.SmallDust + " (" + dust + ")", dust == ScoreRules.SmallDust);
    }

    static void ExactThreshold()
    {
        NewRun();
        Fly(14.98f, Cap);
        Check("just under 15 s: off", !ScoreMultiplier.Active);
        Fly(.04f, Cap);
        Check("past 15 s: on", ScoreMultiplier.Active && ScoreMultiplier.Current == 2);
        // the exact threshold in one step, and in many small float steps
        ScoreMultiplier.Reset();
        ScoreMultiplier.Tick(15f, Cap);
        Check("exactly 15.0 s in one tick: on", ScoreMultiplier.Active);
        ScoreMultiplier.Reset();
        for (int i = 0; i < 150; i++) ScoreMultiplier.Tick(.1f, Cap);   // 0.1f x 150 drifts in float
        Check("150 x 0.1 s (float drift): on at 15.0 s", ScoreMultiplier.Active);
        ScoreMultiplier.Reset();
        for (int i = 0; i < 149; i++) ScoreMultiplier.Tick(.1f, Cap);
        Check("149 x 0.1 s: still off", !ScoreMultiplier.Active);
        Check("the speed multiplier tiers are untouched (x2 at the cap on its own)",
              ScoreRules.SpeedMultiplierFor(Cap) == 2f);
    }

    static void BreaksOnHeartLost()
    {
        NewRun();
        Fly(10f, Cap);
        ScoreMultiplier.OnHeartLost();
        Check("a heart lost at 10 s resets the timer", ScoreMultiplier.Streak == 0f && !ScoreMultiplier.Active);
        Fly(14.9f, Cap);
        Check("a fresh 14.9 s after it is not enough", !ScoreMultiplier.Active);
        Fly(.2f, Cap);
        Check("15 s after the loss it comes on", ScoreMultiplier.Active);
        int lostBefore = ScoreMultiplier.LostCount;
        ScoreMultiplier.OnHeartLost();
        Check("a heart lost while x2 is on ends it", !ScoreMultiplier.Active && ScoreMultiplier.Current == 1);
        Check("... and counts one loss for the HUD", ScoreMultiplier.LostCount == lostBefore + 1);
        Check("... a pickup after it pays the plain amount", DustPays() == ScoreRules.SmallDust);
        string det = File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs");
        int hit = det.IndexOf("lifeCounter += 1;");
        Check("collisionDetection reports the lost heart right where it is taken",
              hit > 0 && det.IndexOf("ScoreMultiplier.OnHeartLost();", hit) - hit < 120);
    }

    static void BreaksBelowCap()
    {
        NewRun();
        Fly(20f, Cap);
        Check("on after 20 s", ScoreMultiplier.Active);
        Fly(Dt, Cap - .01f);
        Check("one frame at HUD 34 ends it", !ScoreMultiplier.Active && ScoreMultiplier.Streak == 0f);
        Fly(14f, Cap);
        Fly(Dt, .2f);
        Fly(14f, Cap);
        Check("a dip below the cap resets a streak in progress (14 + dip + 14 is not 15)", !ScoreMultiplier.Active);
        // a limit-break boost above the cap still counts as at the cap
        NewRun();
        Fly(15.2f, Cap + .1f);
        Check("above the cap (limit break) counts", ScoreMultiplier.Active);
    }

    static void PausedTimeIgnored()
    {
        NewRun();
        Fly(10f, Cap);
        // a paused / frozen world ticks with dt 0 however long the wall clock runs
        for (int i = 0; i < 6000; i++) RunScore.Tick(0f, Cap);
        for (int i = 0; i < 100; i++) ScoreMultiplier.Tick(-1f, 0f);   // even a nonsense speed: ignored while frozen
        Check("100 s of paused wall time neither counts nor breaks the streak",
              !ScoreMultiplier.Active && Mathf.Abs(ScoreMultiplier.Streak - 10f) < .05f);
        Fly(4.9f, Cap);
        Check("10 s + 4.9 s of flight (pause between): still off", !ScoreMultiplier.Active);
        Fly(.2f, Cap);
        Check("past 15 s of FLOWN time: on", ScoreMultiplier.Active);
        // the pause (touch released) is the only way the world freezes; score.cs
        // only calls RunScore.Tick on running frames and WorldEntry returns first
        string src = File.ReadAllText("Assets/Scripts/Core/score.cs");
        Check("the streak is fed only from score.StepRunning (running frames, after the entry)",
              src.Contains("RunScore.Tick(Time.deltaTime, moveBackGround.speed)") &&
              src.IndexOf("if (WorldEntry.Active) return;") < src.IndexOf("RunScore.Tick("));
    }

    static void DoublesGainedScoreNotPrevious()
    {
        NewRun();
        RunScore.OnDust(true);    // earned before the streak: stays 5
        long before = RunScore.Total;
        Check("pre-streak large star paid " + ScoreRules.LargeDust, before >= ScoreRules.LargeDust);
        Fly(15.05f, Cap);
        long atOn = RunScore.Total;
        Check("the score earned so far is not revalued when it comes on (no jump)", atOn >= before);
        long dust = DustPays();
        Check("a small star after: 2 x " + ScoreRules.SmallDust + " (" + dust + ")", dust == ScoreRules.SmallDust * 2);
        // flight (distance) points are doubled too
        long t0 = RunScore.Total;
        Fly(10f, Cap);
        long flown = RunScore.Total - t0;
        NewRun();
        Fly(10f, Cap);               // the first 10 s at the cap, no multiplier yet
        long plain = RunScore.Total;
        float expect = ScoreRules.DistancePoints(Cap, 10f) * ScoreRules.SpeedMultiplierFor(Cap);
        Check("10 s of plain flight earns ~" + expect.ToString("F0") + " (" + plain + ")", Mathf.Abs(plain - expect) <= 2f);
        Check("10 s of flight on x2 earns about double (" + flown + " vs " + plain + ")", Mathf.Abs(flown - 2f * plain) <= 3f);
    }

    static void EverySourceIsDoubled()
    {
        NewRun();
        Fly(15.1f, Cap);
        long b = RunScore.Total;
        RunScore.OnDust(true);
        Check("large dust x2", RunScore.Total - b == ScoreRules.LargeDust * 2);
        b = RunScore.Total;
        RunScore.OnAtom(RunScore.Atom.Heal);
        Check("an atom x2", RunScore.Total - b == ScoreRules.HealAtom * 2);
        b = RunScore.Total;
        RunScore.OnBoss(true, 0f, false, Vector3.zero);
        Check("a boss x2", RunScore.Total - b == ScoreRules.BossPoints(true, 0f, false) * 2);
        b = RunScore.Total;
        RunScore.OnWorldCleared(0);
        Check("a world clear x2", RunScore.Total - b == ScoreRules.WorldClearedPoints(0) * 2);
        b = RunScore.Total;
        RunScore.OnElite(Vector3.zero, ScoreRules.EliteDown);
        Check("an elite x2", RunScore.Total - b == ScoreRules.EliteDown * 2);
        b = RunScore.Total;
        RunScore.OnTeleport(Vector3.zero, new Vector3(5f, 0f, 0f));
        Check("a blink x2", RunScore.Total - b == ScoreRules.Teleport * 2);
        var rock = new GameObject("rock") { tag = "Astr" };
        b = RunScore.Total;
        RunScore.OnKill(rock);
        Object.DestroyImmediate(rock);
        float m = ScoreRules.Combined(1, ScoreRules.SpeedMultiplierFor(Cap));
        Check("a kill x2 on top of its own multipliers", RunScore.Total - b == Mathf.RoundToInt(ScoreRules.Rock * m) * 2);
        Check("RunScore.Total = the sum of its parts", RunScore.Total == RunScore.Parts.Total);
        // the popups show what was actually paid
        int shown = 0;
        System.Action<int, Vector3, RunScore.Source> h = (p, at, s) => shown = p;
        RunScore.Scored += h;
        RunScore.OnAtom(RunScore.Atom.Shield, Vector3.zero);
        RunScore.Scored -= h;
        Check("the +N popup shows the doubled amount", shown == ScoreRules.ShieldAtom * 2);
    }

    static void ResetsOnNewRun()
    {
        NewRun();
        Fly(20f, Cap);
        Check("on", ScoreMultiplier.Active);
        int lost = ScoreMultiplier.LostCount;
        RunScore.BeginRun(true, true);
        Check("a new run starts at 1x with an empty timer", !ScoreMultiplier.Active && ScoreMultiplier.Streak == 0f &&
              ScoreMultiplier.SecondsIn2x == 0f);
        Check("... silently (no loss feedback for a restart)", ScoreMultiplier.LostCount == lost);
        Check("... first pickup plain", DustPays() == ScoreRules.SmallDust);
        // death ends it (the killing hit loses a heart)
        NewRun();
        Fly(20f, Cap);
        ScoreMultiplier.OnHeartLost();
        Check("a death (the last heart) ends it", !ScoreMultiplier.Active);
        // world changes alone do not: nothing in a portal / world change touches the streak
        NewRun();
        Fly(10f, Cap);
        RunScore.OnWorldCleared(0);
        RunScore.OnLoop(1);
        Fly(5.1f, Cap);
        Check("flying through a portal at the cap keeps the streak (10 s + portal + 5.1 s = on)", ScoreMultiplier.Active);
        // the tutorial scores nothing: no streak either
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(false, false);
        Fly(20f, Cap);
        Check("the tutorial never starts it", !ScoreMultiplier.Active && ScoreMultiplier.SecondsIn2x == 0f);
    }

    static void NeverNegativeOrOverflowing()
    {
        NewRun();
        // a very long run on x2: two hours at the cap, in big steps
        for (int i = 0; i < 7200; i++) RunScore.Tick(1f, Cap);
        Check("two hours at the cap: still just x2", ScoreMultiplier.Current == 2 && ScoreMultiplier.Streak == ScoreMultiplier.StreakSeconds);
        Check("... the score is positive and finite", RunScore.Total > 0 && RunScore.Total < int.MaxValue);
        Check("... the time-in-2x is two hours less the 15 s", Mathf.Abs(ScoreMultiplier.SecondsIn2x - (7200f - 15f)) < 2f);
        Check("Gain never goes negative or past int", ScoreMultiplier.Gain(-5) == 0 && ScoreMultiplier.Gain(0) == 0 &&
              ScoreMultiplier.Gain(int.MaxValue) == int.MaxValue && ScoreMultiplier.Gain(int.MaxValue / 2 + 1) == int.MaxValue &&
              ScoreMultiplier.Gain(-1f) == 0f);
        Check("a huge dt cannot overflow the timer", Run(() => { ScoreMultiplier.Reset(); ScoreMultiplier.Tick(float.MaxValue, Cap); }) &&
              ScoreMultiplier.Active && ScoreMultiplier.Streak == ScoreMultiplier.StreakSeconds);
        Check("progress stays 0..1", ScoreMultiplier.Progress01 >= 0f && ScoreMultiplier.Progress01 <= 1f);
        ScoreMultiplier.Reset();
    }

    static bool Run(System.Action a) { try { a(); return true; } catch (System.Exception) { return false; } }

    static void TimeInX2Stat()
    {
        NewRun();
        Fly(15.05f, Cap);
        Check("no time in x2 until it is on", RunScore.Parts.secondsIn2x < .1f);
        Fly(10f, Cap);
        Check("10 s on x2 -> ~10 s recorded (" + RunScore.Parts.secondsIn2x.ToString("F2") + ")", Mathf.Abs(RunScore.Parts.secondsIn2x - 10f) < .1f);
        ScoreMultiplier.OnHeartLost();
        Fly(5f, Cap);
        Check("time off x2 is not counted", Mathf.Abs(RunScore.Parts.secondsIn2x - 10f) < .1f);
        Check("no achievement for it yet (the catalogue has no x2 entry)",
              !File.ReadAllText("Assets/Scripts/Achievements/AchievementCatalog.cs").Contains("ScoreMultiplier"));
    }

    static void LeaderboardsSeeTheDoubledTotal()
    {
        string src = File.ReadAllText("Assets/Scripts/Core/Leaderboards/LeaderboardRunTracker.cs");
        Check("the leaderboard tracker reads RunScore.Total (so it sees doubled score)", src.Contains("RunScore.Total"));
        string ach = File.ReadAllText("Assets/Scripts/Achievements/AchievementTracker.cs");
        Check("score achievements read RunScore.Total", ach.Contains("OnScore(RunScore.Total)"));
        NewRun();
        Fly(15.05f, Cap);
        for (int i = 0; i < 100; i++) RunScore.OnDust(true);
        Check("Total, Parts and the best all carry the doubled points",
              RunScore.Total == RunScore.Parts.Total && RunScore.Parts.dust == 100 * ScoreRules.LargeDust * 2);
    }

    static void ShieldedAndHealedDoNotBreak()
    {
        NewRun();
        Fly(10f, Cap);
        // a shielded / cloaked / invulnerable hit never reaches OnHeartLost; a heal is not a loss
        string det = File.ReadAllText("Assets/Scripts/Ship/collisionDetection.cs");
        int safeBranch = det.IndexOf("if (safe)");
        int elseBranch = det.IndexOf("lifeCounter += 1;");
        Check("only the unsafe branch (a heart actually lost) reports a loss",
              safeBranch > 0 && elseBranch > safeBranch && det.IndexOf("OnHeartLost", safeBranch) > elseBranch &&
              det.Split(new[] { "OnHeartLost" }, System.StringSplitOptions.None).Length == 2);
        Fly(5.1f, Cap);
        Check("10 s + 5.1 s with no loss: on", ScoreMultiplier.Active);
    }

    // ---- HUD cue ----------------------------------------------------------------

    static void CueStatesAndVisibility()
    {
        var S = ScoreX2Cue.CueState.Hidden;
        float far = 99f;
        Check("0 s: hidden", ScoreX2Cue.StateFor(0f, false, far) == ScoreX2Cue.CueState.Hidden);
        Check("1.9 s: still hidden (does not nag)", ScoreX2Cue.StateFor(1.9f, false, far) == ScoreX2Cue.CueState.Hidden);
        Check("2.0 s: the bar shows", ScoreX2Cue.StateFor(2f, false, far) == ScoreX2Cue.CueState.Charging);
        Check("14.9 s: still charging", ScoreX2Cue.StateFor(14.9f, false, far) == ScoreX2Cue.CueState.Charging);
        Check("active: the x2 badge", ScoreX2Cue.StateFor(15f, true, far) == ScoreX2Cue.CueState.Active);
        Check("just lost: the loss cue", ScoreX2Cue.StateFor(0f, false, .1f) == ScoreX2Cue.CueState.Lost);
        Check("... gone after " + ScoreMultiplier.LossCueSeconds + " s", ScoreX2Cue.StateFor(0f, false, ScoreMultiplier.LossCueSeconds + .01f) == ScoreX2Cue.CueState.Hidden);
        Check("fill is in half-second steps, 0..1", ScoreX2Cue.FillFor(0f) == 0f && ScoreX2Cue.FillFor(7.4f) == 7f / 15f &&
              ScoreX2Cue.FillFor(15f) == 1f && ScoreX2Cue.FillFor(99f) == 1f && ScoreX2Cue.FillFor(-3f) == 0f);

        // the live HUD (gameS1)
        var hud = BuildHud(out GameObject go, out ScoreX2Cue cue);
        if (cue == null) { Check("the HUD attaches the cue to the SCORE row", false); return; }
        Check("the cue sits on the SCORE row", cue.transform.name == ScoreHud.RowName && cue.Bar.transform.parent == cue.transform);
        Check("... and the SPEED row has none of it", SceneUtil.FindAny("SpeedText").GetComponent<ScoreX2Cue>() == null &&
              SceneUtil.FindAny("SpeedText").transform.Find(ScoreX2Cue.BarName) == null && SceneUtil.FindAny("SpeedText").transform.Find(ScoreX2Cue.PlateName) == null);
        float t = 100f;
        cue.Refresh(t);
        Check("not started: nothing visible", cue.State == ScoreX2Cue.CueState.Hidden && !cue.Bar.enabled && !cue.Plate.enabled);

        Fly(1.5f, Cap);
        cue.Refresh(t += Dt);
        Check("1.5 s in: still nothing", cue.State == ScoreX2Cue.CueState.Hidden && !cue.Bar.enabled);
        Fly(6.5f, Cap);
        cue.Refresh(t += Dt);
        Check("8 s in: bar visible, ~half full, no badge",
              cue.State == ScoreX2Cue.CueState.Charging && cue.Bar.enabled && !cue.Plate.enabled &&
              Mathf.Abs(cue.Bar.fillAmount - 8f / 15f) < .04f);
        Check("... in cyan, not red", NotRed(cue.Bar.color) && NotRed(cue.Back.color));

        Fly(7.2f, Cap);
        cue.Refresh(t += Dt);
        Check("active: the x2 plate shows, the bar is gone, the label reads 'x2'",
              cue.State == ScoreX2Cue.CueState.Active && cue.Plate.enabled && cue.LabelText.enabled && !cue.Bar.enabled &&
              cue.LabelText.text == "x2");
        Check("... it pops in (a stepped punch)", cue.Plate.rectTransform.localScale.x < .95f);
        cue.Refresh(t += .5f);
        Check("... and settles to its size", Mathf.Approximately(cue.Plate.rectTransform.localScale.x, 1f) && NotRed(cue.Plate.color));

        ScoreMultiplier.OnHeartLost();
        cue.Refresh(t += Dt);
        Check("just lost: the plate dims (muted, sinking)", cue.State == ScoreX2Cue.CueState.Lost && cue.Plate.enabled &&
              cue.Plate.color.a < .9f && NotRed(cue.Plate.color));
        cue.Refresh(t += ScoreMultiplier.LossCueSeconds + .05f);
        Check("... and gone shortly after", cue.State == ScoreX2Cue.CueState.Hidden && !cue.Plate.enabled && !cue.Bar.enabled);

        // losing before it came on is silent
        ScoreMultiplier.Reset();
        Fly(5f, Cap);
        cue.Refresh(t += Dt);
        ScoreMultiplier.OnHeartLost();
        cue.Refresh(t += Dt);
        Check("a streak lost before x2 had started makes no loss cue", cue.State == ScoreX2Cue.CueState.Hidden);

        // not during the tutorial / a finished run
        ScoreMultiplier.Reset();
        Fly(5f, Cap);
        RunScore.EndRun(RunScore.RunId);
        cue.Refresh(t += Dt);
        Check("after the run ended: hidden", cue.State == ScoreX2Cue.CueState.Hidden);
        Object.DestroyImmediate(go);
    }

    static bool NotRed(Color c)
    {
        // red means the player: no tint with red clearly above green and blue
        return !(c.r > .5f && c.r > c.g * 1.6f && c.r > c.b * 1.6f);
    }

    static ScoreHud BuildHud(out GameObject go, out ScoreX2Cue cue)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        NewRun();
        moveBackGround.speed = Cap;
        go = new GameObject("~HudX2Test");
        var styler = go.AddComponent<HudStyler>();
        styler.SendMessage("Start");
        var hud = go.GetComponent<ScoreHud>();
        cue = hud != null && hud.ScoreText != null ? hud.ScoreText.GetComponent<ScoreX2Cue>() : null;
        if (cue != null) { Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)hud.ScoreText.transform.parent); }
        return hud;
    }

    static void CueLayout()
    {
        var hud = BuildHud(out GameObject go, out ScoreX2Cue cue);
        if (cue == null) { Check("layout: cue exists", false); return; }
        var styler = go.GetComponent<HudStyler>();
        var scoreRow = hud.ScoreText;
        var rows = (RectTransform)scoreRow.transform.parent;
        LayoutRebuilder.ForceRebuildLayoutImmediate(rows);

        // the SPEED row is exactly as it was: its own height, no cue children
        var speedRow = SceneUtil.FindAny("SpeedText").GetComponent<Text>();
        Check("the SPEED row has no x2 bar or plate", speedRow.transform.Find(ScoreX2Cue.BarName) == null &&
              speedRow.transform.Find(ScoreX2Cue.BackName) == null && speedRow.transform.Find(ScoreX2Cue.PlateName) == null);
        Check("the read-out keeps its size (131)", Mathf.Approximately(styler.HudRoot.rect.size.y, 131f));

        var plate = cue.Plate.rectTransform;
        plate.localScale = Vector3.one;
        cue.Plate.enabled = true;
        var rowRect = scoreRow.rectTransform;
        float rowW = rowRect.rect.width;
        string[] scores = { "SCORE  0", "SCORE  12,345", "SCORE  123,456", "SCORE  9,999,999" };
        foreach (bool chain in new[] { false, true })
        {
            hud.ChainText.text = chain ? "x4" : "";
            float chainLeft = chain ? rowW - hud.ChainText.preferredWidth - 2f : rowW;
            foreach (var label in scores)
            {
                scoreRow.text = label;
                Measure(cue, label);
                float textRight = scoreRow.preferredWidth;
                float left = plate.anchoredPosition.x, right = left + ScoreX2Cue.PlateWidth;
                string what = "'" + label + "'" + (chain ? " + chain x4" : "");
                Debug.Log("[X2] " + what + ": text " + textRight.ToString("F1") + " plate " + left + ".." + right + " (shown " + cue.Plate.enabled + ") chain from " + chainLeft.ToString("F1") + " row " + rowW);
                if (cue.Plate.enabled)
                {
                    Check(what + ": the plate hugs the figure (gap " + (left - textRight).ToString("F1") + ")",
                          left >= textRight + 2f && left <= textRight + ScoreX2Cue.PlateGap + 1.5f);
                    Check(what + ": inside the row, clear of the chain badge", right <= chainLeft + .5f && InsideLocal(rowRect, plate));
                }
                else
                {
                    Check(what + ": no room, so the plate waits instead of covering a digit or the chain badge", chain && label.EndsWith("9,999,999"));
                }
            }
        }
        hud.ChainText.text = "";
        scoreRow.text = "SCORE  123,456,789,012";   // absurdly long: the plate waits, never spills
        Measure(cue, scoreRow.text);
        Check("an absurdly long score never puts the plate outside the row", !cue.Plate.enabled || InsideLocal(rowRect, plate));
        Check("the plate follows the figure (a longer score pushes it right)",
              ScoreX2Cue.PlateLeftFor(100f, 323f, 0f) < ScoreX2Cue.PlateLeftFor(150f, 323f, 0f) &&
              ScoreX2Cue.PlateLeftFor(900f, 323f, 0f) == ScoreX2Cue.NoRoom && ScoreX2Cue.PlateLeftFor(100f, 323f, 0f) == 106f);

        Check("the bar strip is inside the SCORE row", InsideLocal(rowRect, cue.Bar.rectTransform) && InsideLocal(rowRect, cue.Back.rectTransform));
        Check("the label fits its plate", cue.LabelText.preferredWidth <= ScoreX2Cue.PlateWidth - 8f && cue.LabelText.preferredHeight <= ScoreX2Cue.PlateHeight);
        Check("nothing of the cue is a raycast target", !cue.Plate.raycastTarget && !cue.Bar.raycastTarget && !cue.Back.raycastTarget && !cue.LabelText.raycastTarget);
        Check("the cue is children of the SCORE row only",
              cue.transform == scoreRow.transform && cue.Plate.transform.parent == cue.transform && cue.Bar.transform.parent == cue.transform);

        // every device shape: bar and plate (with a 7-digit and a very long score)
        // inside the safe area
        scoreRow.text = "SCORE  9,999,999";
        Measure(cue, scoreRow.text);
        var canvas = styler.HudRoot.parent.GetComponent<Canvas>();
        var scaler = canvas.GetComponent<CanvasScaler>();
        Vector2 hudSize = styler.HudRoot.rect.size;
        int checkedDevices = 0;
        foreach (var long_ in new[] { false, true })
        {
            scoreRow.text = long_ ? "SCORE  123,456,789,012" : "SCORE  9,999,999";
            Measure(cue, scoreRow.text);
            foreach (var d in FitDevice.All)
            {
                var screen = new Vector2(d.w, d.h);
                float scale = HudStyler.HudCanvasScale(canvas, scaler, screen);
                Rect r = HudStyler.HudScreenRect(d.Safe, screen, scale, hudSize);
                bool ok = d.Safe.Contains(r.min) && d.Safe.Contains(r.max);
                foreach (var item in new[] { plate, cue.Bar.rectTransform, cue.Back.rectTransform })
                {
                    if (item == plate && !cue.Plate.enabled) continue;
                    Rect pr = ScreenRectOf(styler.HudRoot, item, r, hudSize);
                    ok &= d.Safe.Contains(pr.min) && d.Safe.Contains(pr.max) && pr.height > 0f;
                }
                if (!ok) Check(d.id + ": HUD with the x2 cue (" + (long_ ? "long" : "7-digit") + " score) inside the safe area", false);
                if (!long_) checkedDevices++;
            }
        }
        Check("the read-out with the x2 plate and bar is inside the safe area on all " + checkedDevices + " devices (7-digit and very long scores)", checkedDevices == FitDevice.All.Length);
        Object.DestroyImmediate(go);
    }

    static void Measure(ScoreX2Cue cue, string label)
    {
        Canvas.ForceUpdateCanvases();
        cue.Plate.enabled = true;
        cue.Remeasure();
        cue.Plate.enabled = cue.PlateX >= 0f;   // as Refresh does in the Active state
        cue.Plate.rectTransform.anchoredPosition = new Vector2(Mathf.Max(0f, cue.PlateX), 0f);
    }

    // `item`'s rect in screen px, given where the panel (`panel`, size `hudSize`) lands (`r`)
    static Rect ScreenRectOf(RectTransform panel, RectTransform item, Rect r, Vector2 hudSize)
    {
        var corners = new Vector3[4];
        item.GetWorldCorners(corners);
        Vector2 lo = panel.InverseTransformPoint(corners[0]), hi = panel.InverseTransformPoint(corners[2]);
        float kx = r.width / hudSize.x, ky = r.height / hudSize.y;
        float x0 = r.xMin + (lo.x - panel.rect.xMin) * kx, x1 = r.xMin + (hi.x - panel.rect.xMin) * kx;
        float y0 = r.yMin + (lo.y - panel.rect.yMin) * ky, y1 = r.yMin + (hi.y - panel.rect.yMin) * ky;
        return Rect.MinMaxRect(Mathf.Min(x0, x1), Mathf.Min(y0, y1), Mathf.Max(x0, x1), Mathf.Max(y0, y1));
    }

    static bool InsideLocal(RectTransform outer, RectTransform inner)
    {
        var corners = new Vector3[4];
        inner.GetWorldCorners(corners);
        foreach (var c in corners)
        {
            Vector2 l = outer.InverseTransformPoint(c);
            if (l.x < outer.rect.xMin - .5f || l.x > outer.rect.xMax + .5f ||
                l.y < outer.rect.yMin - .5f || l.y > outer.rect.yMax + .5f) return false;
        }
        return true;
    }

    static void CueAllocatesNothing()
    {
        var hud = BuildHud(out GameObject go, out ScoreX2Cue cue);
        if (cue == null) { Check("alloc: cue exists", false); return; }
        long control;
        if (!TestHarness.AllocMeterWorks(out control)) { Check("the allocation meter sees its control (" + control + ")", false); Object.DestroyImmediate(go); return; }
        float t = 500f;
        // warm up through every state
        for (int pass = 0; pass < 2; pass++)
        {
            ScoreMultiplier.Reset();
            for (int i = 0; i < 1000; i++) { RunScore.Tick(Dt, Cap); cue.Refresh(t += Dt); }
            ScoreMultiplier.OnHeartLost();
            for (int i = 0; i < 60; i++) cue.Refresh(t += Dt);
        }
        ScoreMultiplier.Reset();
        long bytes = TestHarness.AllocatedBytes(() =>
        {
            for (int i = 0; i < 1200; i++) { RunScore.Tick(Dt, Cap); cue.Refresh(t += Dt); }
            ScoreMultiplier.OnHeartLost();
            for (int i = 0; i < 60; i++) { RunScore.Tick(Dt, Cap); cue.Refresh(t += Dt); }
        });
        Check("streak + cue allocate nothing through charging, active and lost (" + bytes + " bytes)", bytes == 0);
        Object.DestroyImmediate(go);
    }
}
