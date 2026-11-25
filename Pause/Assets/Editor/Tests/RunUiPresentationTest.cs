using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// An asynchronous results wait must never expose the scene's empty authored frame.
public static class RunUiPresentationTest
{
    static int fails;
    static void Check(string name, bool ok)
    {
        Debug.Log((ok ? "[RUNUI] PASS  " : "[RUNUI] FAIL  ") + name);
        if (!ok) fails++;
    }
    public static void Run() { TestHarness.Exit(Execute()); }
    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        CheckCoinGap();
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        var canvas = SceneUtil.FindAny("PopUpCanvas");
        var dust = SceneUtil.FindAny("playerDeadHighScore").GetComponent<Text>();
        var best = SceneUtil.FindAny("playerDeadHighestSpeed").GetComponent<Text>();
        var spare = SceneUtil.FindAny("deathSpeedReachedThisRoundText").GetComponent<Text>();
        var replay = SceneUtil.FindAny("Replay").GetComponent<Button>();
        var menu = SceneUtil.FindAny("MainMenu").GetComponent<Button>();
        var controls = Object.FindObjectOfType<buttonClicks>();
        controls.popUpCanvas = canvas.GetComponent<Canvas>();
        bool wasDead = buttonClicks.playerDied;
        try
        {
            playerIsDead.PrepareResultsCanvas(canvas, dust);
            var legacy = canvas.transform.Find("Model Panel");
            Check("legacy frame is retired before the pending callback", !legacy.gameObject.activeSelf);
            Check("serialized controls remain accessible during the wait", dust != null && best != null && replay != null && menu != null);
            buttonClicks.playerDied = true;
            var update = typeof(buttonClicks).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            // A withheld callback represents an arbitrarily long ad wait, without loading an ad SDK.
            bool hidden = true;
            for (int i = 0; i < 180; i++)
            {
                update.Invoke(controls, null);
                hidden &= !canvas.activeSelf && !playerIsDead.PresentationReady(canvas.transform);
            }
            Check("pending results never reveal an empty template", hidden);
            var unfinished = new GameObject("FlightComplete", typeof(RectTransform), typeof(DeathPanelView));
            unfinished.transform.SetParent(canvas.transform, false);
            playerIsDead.RevealResults(canvas);
            Check("a named but unfinished result is not revealable", !canvas.activeSelf);
            Object.DestroyImmediate(unfinished);
            var view = DeathPanelView.Build(canvas.transform, best, spare, dust, replay, menu,
                new DeathPanelView.Results { score = 100, bestScore = 100, ranked = true, dustWon = 1 });
            playerIsDead.RevealResults(canvas);
            Check("the callback reveals the complete panel in the same frame", canvas.activeSelf && view.PresentationReady && playerIsDead.PresentationReady(canvas.transform));
            Check("the blank legacy template stays hidden after handoff", !legacy.gameObject.activeSelf);
        }
        finally { buttonClicks.playerDied = wasDead; }
        return fails;
    }

    static void CheckCoinGap()
    {
        bool clear = true, closer = true, half = false, controlsClear = true;
        foreach (var screen in new[] { new Vector2(480, 854), new Vector2(1080, 1920), new Vector2(800, 600) })
        {
            Rect safe = new Rect(Vector2.zero, screen);
            var band = TopBand.FrameFor(safe, screen, 0f, new Rect[0]);
            float scale = HudStyler.ScaleWithScreenSize(screen, new Vector2(800, 600), CanvasScaler.ScreenMatchMode.MatchWidthOrHeight, 0f);
            Rect hud = HudStyler.HudScreenRect(band, screen, scale, new Vector2(351, 131));
            for (int world = 0; world < 7; world++)
            {
                var g = ShotAtomHud.Compute(band, safe, screen, hud, world, 6);
                float visibleGap = hud.yMin - HudStyler.BoardVisibleBottomPx(hud, world) - ShotAtomHud.VisibleIconTop(g);
                clear &= visibleGap >= 0f;
                closer &= visibleGap <= g.originalVisibleGap + .01f;
                half |= Mathf.Abs(visibleGap - g.originalVisibleGap * .5f) < .01f;
                controlsClear &= !ShotAtomHud.BlockRect(g).Overlaps(PauseQuickActions.ScreenRectFor(band, screen));
            }
        }
        Check("visible coin art stays below the visible scoreboard frame", clear);
        Check("coin art moves closer and halves the original visible gap where space permits", closer && half);
        Check("reserved coin rows stay clear of Replay and Home", controlsClear);
    }
}
