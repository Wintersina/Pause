using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor.SceneManagement;

public static class PauseEnhancementsTest
{
    static int fails;
    static void Check(string label, bool ok) { Debug.Log((ok ? "[PAUSE-UP] PASS " : "[PAUSE-UP] FAIL ") + label); if (!ok) fails++; }
    public static void Run() { TestHarness.Exit(Execute()); }
    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try { Bonus(); PauseHook(); FusionPopup(); }
        finally { RunScore.EndRun(RunScore.RunId); }
        return fails;
    }
    static void Fresh()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        PortalPressure.Reset(); buttonClicks.playerDied = false; startMenu.youAreInTutorial = false;
        moveBackGround.speed = 0f; AtomFusions.BeginRun(null);
        RunScore.BeginRun(true, false, false);
    }
    static GameObject Target()
    {
        var target = new GameObject("testEnemy"); target.tag = "Enimey"; return target;
    }
    static void Bonus()
    {
        Fresh(); var target = Target();
        int ordinary = RunScore.OnKill(target, ScoreRules.TeleportKillBonus);
        RunScore.BeginRun(true, false, false); RunScore.OnPauseUsed();
        Check("pause arrival kill receives double its full score, including the blink bonus", RunScore.OnKill(target, ScoreRules.TeleportKillBonus) == ordinary * 2);
        int elite = RunScore.OnElite(Vector3.zero, 100);
        Check("elite kill score doubles during the same window", elite == 200 && RunScore.LastEliteMultiplier == 2);
        RunScore.Tick(0f, 0f);
        Check("a frozen world preserves all five seconds", RunScore.PauseBonusLeft == 5f);
        RunScore.Tick(4f, 0f);
        Check("bonus remains live before five flight seconds", RunScore.PauseKillMultiplier == 2);
        RunScore.OnPauseUsed();
        Check("another pause refreshes five seconds without stacking past double", RunScore.PauseBonusLeft == 5f && RunScore.PauseKillMultiplier == 2);
        RunScore.Tick(5f, 0f);
        Check("the bonus expires exactly at five flight seconds", RunScore.PauseKillMultiplier == 1 && RunScore.OnElite(Vector3.zero, 100) == 100);
        RunScore.OnPauseUsed();
        Check("pickups keep their ordinary point value", RunScore.OnDust(true) == ScoreRules.LargeDust);
        RunScore.BeginRun(true, false, false);
        Check("new runs clear the window", RunScore.PauseBonusLeft == 0f && !RunScore.PauseUsedThisFrame);
        RunScore.BeginRun(false, false, false); RunScore.OnPauseUsed();
        Check("practice runs never arm a score bonus", RunScore.PauseKillMultiplier == 1);
    }
    static void PauseHook()
    {
        Fresh(); var counter = new GameObject("score").AddComponent<score>(); score.pauseCounter = 2;
        typeof(score).GetMethod("pauseCounterFunction", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(counter, null);
        Check("the real pause-spend path starts the bonus", score.pauseCounter == 1 && RunScore.PauseBonusLeft == 5f && RunScore.PauseUsedThisFrame);
        RunScore.Tick(5f, 0f); score.pauseCounter = 0;
        typeof(score).GetMethod("pauseCounterFunction", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(counter, null);
        Check("an empty pause counter cannot grant another bonus", RunScore.PauseKillMultiplier == 1);
    }
    static void FusionPopup()
    {
        Fresh();
        var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var text = new GameObject("Speed", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        text.transform.SetParent(canvas.transform, false); text.font = CodexUi.FindFont();
        var hud = ScoreHud.AttachPopupsOnly(canvas.gameObject, text);
        foreach (var fusion in AtomFusionTable.All)
        {
            hud.ShowFusion(fusion, new Vector3(float.NaN, 0f, 0f));
            bool icons = hud.FusionPopupLabel.text == fusion.name && hud.FusionPopupLabel.fontSize == ScoreHud.FusionLabelSize;
            for (int i = 0; i < 3; i++)
            {
                var image = hud.FusionIcon(i);
                icons &= image.gameObject.activeSelf == (i < fusion.atoms.Length);
                if (i < fusion.atoms.Length) icons &= image.sprite != null && image.sprite == CodexCombosView.GlyphOf(fusion.atoms[i]) && !image.raycastTarget;
            }
            Check(fusion.name + " uses the actual atom icons with a small name underneath", icons && hud.FusionPopupLabel.rectTransform.anchoredPosition.y < hud.FusionIcon(0).rectTransform.anchoredPosition.y);
        }
        var root = hud.FusionPopupRoot;
        Vector2 before = root.anchoredPosition; hud.StepPopups(0f);
        Check("combo icons freeze with the world", root.anchoredPosition == before && root.gameObject.activeSelf);
        hud.ShowFusion(AtomFusionTable.Get(FusionId.Overclock), Vector3.zero, 2);
        Check("a retrigger reuses one compact popup and keeps the combo name", hud.FusionPopupRoot == root && hud.FusionPopupLabel.text == "OVERCLOCK x2");
        hud.StepPopups(-1f);
        Check("death clears the compact combo popup", !root.gameObject.activeSelf);
    }
}
