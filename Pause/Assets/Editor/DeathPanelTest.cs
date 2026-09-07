using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Forces a real layout pass (edit mode, no Play mode needed) so the checks
// see the same rects the VerticalLayoutGroups actually compute at runtime,
// not the (0,0) placeholders Unity leaves in the serialized scene for
// layout-driven children.
public static class DeathPanelTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DPT] PASS  " : "[DPT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);

        var modelPanel = SceneUtil.FindAny("Model Panel").GetComponent<RectTransform>();
        var dialauge = SceneUtil.FindAny("Model Dialauge").GetComponent<RectTransform>();
        var results = SceneUtil.FindAny("ResultsPanel").GetComponent<RectTransform>();
        var panelBg = SceneUtil.FindAny("PanelBackground").GetComponent<RectTransform>();
        var buttonPanel = SceneUtil.FindAny("Button Panel").GetComponent<RectTransform>();

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelBg);
        LayoutRebuilder.ForceRebuildLayoutImmediate(buttonPanel);
        // Nested layout groups sometimes need a second pass to fully settle;
        // also rebuild each button's own rect directly.
        LayoutRebuilder.ForceRebuildLayoutImmediate(buttonPanel);
        foreach (var n in new[] { "Replay", "MainMenu" })
            LayoutRebuilder.ForceRebuildLayoutImmediate(SceneUtil.FindAny(n).GetComponent<RectTransform>());

        Check("panel is bigger than the old 300x412", modelPanel.sizeDelta.x > 300 && modelPanel.sizeDelta.y > 412);
        Check("inner frame fits inside the panel",
              dialauge.sizeDelta.x < modelPanel.sizeDelta.x && dialauge.sizeDelta.y < modelPanel.sizeDelta.y);
        Check("results box fits inside the frame",
              results.sizeDelta.x < dialauge.sizeDelta.x && results.sizeDelta.y < dialauge.sizeDelta.y);

        var texts = new[] { "playerDeadHighestSpeed", "deathSpeedReachedThisRoundText", "playerDeadHighScore" };
        var rects = new System.Collections.Generic.List<Rect>();
        foreach (var n in texts)
        {
            var go = SceneUtil.FindAny(n);
            var rt = go.GetComponent<RectTransform>();
            var txt = go.GetComponent<Text>();

            Check(n + " has a non-zero laid-out height", rt.rect.height > 5f);
            Check(n + " font is readable (>=20)", txt.fontSize >= 20);
            rects.Add(new Rect(rt.anchoredPosition - rt.rect.size * rt.pivot, rt.rect.size));
        }

        // The whole point of the fix: three lines that used to sit on the
        // exact same point must now occupy distinct vertical bands.
        var centersY = new float[3];
        for (int i = 0; i < 3; i++) centersY[i] = rects[i].center.y;
        bool allDistinct = Mathf.Abs(centersY[0] - centersY[1]) > 5f
                         && Mathf.Abs(centersY[1] - centersY[2]) > 5f
                         && Mathf.Abs(centersY[0] - centersY[2]) > 5f;
        Check("the three stat lines no longer share one point (y = " +
              string.Join(", ", System.Array.ConvertAll(centersY, v => v.ToString("F1"))) + ")", allDistinct);

        // Button panel: taller than the old 176, and its buttons must have
        // real (nonzero) laid-out height once the layout group resolves.
        Check("button panel is at least as tall as before", buttonPanel.sizeDelta.y >= 150);
        foreach (var n in new[] { "Replay", "MainMenu" })
        {
            var go = SceneUtil.FindAny(n);
            var rt = go.GetComponent<RectTransform>();
            Check(n + " button has a non-zero laid-out height", rt.rect.height > 5f);
        }

        Debug.Log("[DPT] failures: " + fails);
        EditorApplication.Exit(0);
    }
}
