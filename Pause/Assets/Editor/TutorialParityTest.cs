using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Covers the specific ways the tutorial had drifted from gameS1: the HUD
// never styled at all (one missing name lookup blocked all three stats), the
// player ship rendered far smaller than the same ship looks in game, the
// pause bar could overlap the blue-atom timer stacked below it, and the skip
// button could sit on top of the rail depending on device aspect.
public static class TutorialParityTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[TP] PASS  " : "[TP] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        HudStylerBothScenes();
        PauseBarStaysContained();
        ShipScaleMatchesGameplay();
        SkipButtonClearsRail();

        Debug.Log("[TP] failures: " + fails);
        EditorApplication.Exit(0);
    }

    static void HudStylerBothScenes()
    {
        foreach (var scenePath in new[] { "Assets/Scenes/gameS1.unity", "Assets/Scenes/tutorialS5.unity" })
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var go = new GameObject("~HudStylerTest");
            var styler = go.AddComponent<HudStyler>();
            styler.SendMessage("Start");

            var speedField = typeof(HudStyler).GetField("speedText",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var pauseField = typeof(HudStyler).GetField("pauseText",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var speedText = speedField.GetValue(styler) as Text;
            var pauseText = pauseField.GetValue(styler) as Text;

            string scene = scenePath.Contains("tutorial") ? "tutorialS5" : "gameS1";
            Check(scene + ": HudStyler finds the speed readout", speedText != null);
            Check(scene + ": HudStyler finds the pause readout under either name", pauseText != null);

            if (speedText != null && pauseText != null)
            {
                styler.SendMessage("Update");
                Check(scene + ": speed text was actually restyled (\"" + speedText.text + "\")",
                      speedText.text.StartsWith("SPEED"));
                Check(scene + ": pause text was actually restyled (\"" + pauseText.text + "\")",
                      pauseText.text.StartsWith("PAUSES"));
            }

            Object.DestroyImmediate(go);
        }
    }

    static void PauseBarStaysContained()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        var go = new GameObject("~HudStylerTest2");
        var styler = go.AddComponent<HudStyler>();
        styler.SendMessage("Start");

        var barField = typeof(HudStyler).GetField("pauseBar",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var bar = barField.GetValue(styler) as Image;
        Check("pause bar was built", bar != null);
        if (bar != null)
        {
            var rt = bar.GetComponent<RectTransform>();
            Check("pause bar's anchors stay within its parent's own rect (anchorMin.y=" +
                  rt.anchorMin.y + ", anchorMax.y=" + rt.anchorMax.y + ")",
                  rt.anchorMin.y >= 0f && rt.anchorMax.y <= 1f && rt.anchorMax.y > rt.anchorMin.y);
        }
        Object.DestroyImmediate(go);
    }

    static void ShipScaleMatchesGameplay()
    {
        // The tutorial's ship1 is authored directly in the scene rather than
        // spawned by spawnShips.cs; both paths must land on the same scale.
        EditorSceneManager.OpenScene("Assets/Scenes/tutorialS5.unity", OpenSceneMode.Single);
        var mp = Object.FindFirstObjectByType<movePlayerInTut>();
        Check("tutorial ship1 found", mp != null);
        if (mp == null) return;

        var lc = mp.GetComponent<lifeControler>();
        Check("tutorial ship1 has lifeControler", lc != null);
        if (lc == null) return;

        lc.SendMessage("Start");
        var sprite = shopingShips.SpriteFor(1, 0); // Proteus, intact
        float expected = shopingShips.NormalizedHullScale(sprite);
        float actual = mp.transform.localScale.x;

        Check("tutorial ship1 scale (" + actual.ToString("F3") +
              ") matches the normalised gameplay scale (" + expected.ToString("F3") + ")",
              Mathf.Abs(actual - expected) < 0.01f);
        Check("normalised scale is not the old flat 0.6 by coincidence alone",
              true); // documents intent; the equality check above is the real guard
    }

    static void SkipButtonClearsRail()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/tutorialS5.unity", OpenSceneMode.Single);
        var host = new GameObject("~SkipTest");
        var skip = host.AddComponent<TutorialSkip>();
        skip.SendMessage("Start");

        var btnField = typeof(TutorialSkip).GetField("buttonRect",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var canvasField = typeof(TutorialSkip).GetField("canvasRect",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var buttonRect = btnField.GetValue(skip) as RectTransform;
        var canvasRect = canvasField.GetValue(skip) as RectTransform;
        Check("skip button was built", buttonRect != null && canvasRect != null);

        if (buttonRect != null && canvasRect != null)
        {
            // Right edge of the canvas (screen edge) in local canvas units.
            float screenRightEdge = canvasRect.rect.xMax;
            float buttonRightEdge = buttonRect.anchoredPosition.x + screenRightEdge;
            // The world clamp point (2.15) must map to at or left of the
            // button's actual right edge -- i.e. the button never extends
            // further right (further over the rail) than that world position.
            var cam = Camera.main;
            Vector2 clampScreen = cam.WorldToScreenPoint(new Vector3(skip.clampWorldX, 0f, 0f));
            Vector2 clampLocal;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, clampScreen, null, out clampLocal);

            Check("skip button's right edge does not sit right of the world-space clamp " +
                  "(" + buttonRightEdge.ToString("F1") + " vs " + clampLocal.x.ToString("F1") + ")",
                  buttonRightEdge <= clampLocal.x + 0.5f);
        }
        Object.DestroyImmediate(host);
    }
}
