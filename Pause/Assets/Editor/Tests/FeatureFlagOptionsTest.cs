using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The Options screen's developer feature-flag list, and the Options screen without
// the account row (sign-in lives on the leaderboard screen).
//   1  registry: every [Flag] const is discovered, defaults read, Set clears a default
//   2  the Options list shows one row per registered flag, only in developer mode,
//      rows flip the flag, the list scrolls, nothing overlaps the button stack
//   3  no account / sign-in controls in Options
public static class FeatureFlagOptionsTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[FFO] PASS  " : "[FFO] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        Registry();
        OptionsList();
        NoAccountControls();
        Debug.Log("[FFO] failures: " + fails);
        return fails;
    }

    static void Registry()
    {
        var all = FeatureFlags.All;
        Check("registry: Paragon is registered with its label and default ON",
              all.Any(f => f.name == FeatureFlags.ParagonShip && f.defaultOn && f.label == "PARAGON SHIP"));
        var consts = typeof(FeatureFlags).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name != "Prefix").ToArray();
        Check("registry: every flag const is registered (no hand-listed flags)", consts.Length == all.Length && all.Length >= 1);
        Check("registry: names are unique", all.Select(f => f.name).Distinct().Count() == all.Length);
        const string f1 = FeatureFlags.ParagonShip;
        PlayerPrefs.DeleteKey(FeatureFlags.KeyOf(f1));
        FeatureFlags.Set(f1, false);
        Check("registry: off is stored explicitly against a default-on flag",
              PlayerPrefs.GetInt(FeatureFlags.KeyOf(f1), -1) == 0 && !FeatureFlags.IsOn(f1));
        FeatureFlags.Set(f1, true);
        Check("registry: back to the default clears the key", !PlayerPrefs.HasKey(FeatureFlags.KeyOf(f1)) && FeatureFlags.IsOn(f1));
        Check("registry: an unregistered flag defaults off", !FeatureFlags.IsOn("no_such_flag_xyz"));
    }

    static FeatureFlagOptions Open(bool dev, Vector2? screen = null, Rect? safe = null)
    {
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, dev ? 1 : 0);
        EditorSceneManager.OpenScene("Assets/Scenes/leaderboardS3.unity", OpenSceneMode.Single);
        var go = new GameObject("~FeatureFlagOptions");
        var panel = go.AddComponent<FeatureFlagOptions>();
        var s = screen ?? new Vector2(1080f, 2340f);
        panel.Build(s, safe ?? new Rect(0f, 0f, s.x, s.y));
        return panel;
    }

    static void OptionsList()
    {
        var off = Open(false);
        Check("player: the flag list is not shown", off.Card != null && !off.Shown);
        Object.DestroyImmediate(off.gameObject);

        var panel = Open(true);
        var all = FeatureFlags.All;
        Check("developer: the flag list is shown", panel.Shown);
        Check("developer: one row per registered flag", panel.RowCount == all.Length);
        for (int i = 0; i < all.Length; i++)
        {
            Check("row " + all[i].name + ": label", panel.RowLabel(i) == all[i].label);
            bool on = FeatureFlags.IsOn(all[i].name);
            Check("row " + all[i].name + ": shows its state", panel.RowState(i) == (on ? "ON" : "OFF"));
            panel.RowButton(i).onClick.Invoke();
            Check("row " + all[i].name + ": tap flips it", FeatureFlags.IsOn(all[i].name) == !on
                  && panel.RowState(i) == (on ? "OFF" : "ON"));
            panel.RowButton(i).onClick.Invoke();
            Check("row " + all[i].name + ": tap again restores it", FeatureFlags.IsOn(all[i].name) == on);
        }
        Check("developer: the list scrolls vertically", panel.List != null && panel.List.vertical && !panel.List.horizontal);

        // the card clears the button stack on a range of screens
        var screens = new[]
        {
            (new Vector2(1080f, 2340f), new Rect(0f, 0f, 1080f, 2250f)),
            (new Vector2(1080f, 1920f), new Rect(0f, 0f, 1080f, 1920f)),
            (new Vector2(720f, 1280f), new Rect(0f, 0f, 720f, 1280f)),
            (new Vector2(1536f, 2048f), new Rect(0f, 0f, 1536f, 2048f)),
        };
        foreach (var (screen, safe) in screens)
        {
            Object.DestroyImmediate(panel.gameObject);
            panel = Open(true, screen, safe);
            var dev = new GameObject("~DeveloperOptions").AddComponent<DeveloperOptions>();
            typeof(DeveloperOptions).GetMethod("Start", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(dev, null);
            panel.Refresh();
            var layout = panel.AppliedLayout;
            string label = screen.x + "x" + screen.y;
            float h = FeatureFlagOptions.CardHeight * layout.scale;
            float bottom = layout.centerY - h * .5f;
            float top = layout.centerY + h * .5f + 14f * layout.scale;
            Check(label + ": list inside the safe area (top)", top <= layout.safeTop + .01f);
            bool overlaps = false;
            foreach (RectTransform rt in (RectTransform)panel.Card.parent)
            {
                if (rt == panel.Card || rt.GetComponentInChildren<Button>(true) == null || !rt.gameObject.activeInHierarchy) continue;
                if (rt.anchoredPosition.y + rt.sizeDelta.y * (1f - rt.pivot.y) > bottom) overlaps = true;
            }
            Check(label + ": list clear of the Options buttons", !overlaps);
            Check(label + ": readable (scale " + layout.scale.ToString("0.00") + ")", layout.scale >= .7f);
            Object.DestroyImmediate(dev.gameObject);
        }
        Object.DestroyImmediate(panel.gameObject);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
    }

    static void NoAccountControls()
    {
        var panel = Open(false);
        var offenders = new System.Collections.Generic.List<string>();
        foreach (var b in Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var t = b.GetComponentInChildren<Text>(true);
            string caption = t != null ? t.text.ToLowerInvariant() : "";
            string n = b.name.ToLowerInvariant();
            if (n.Contains("account") || n.Contains("signin") || n.Contains("signout") ||
                caption.Contains("sign in") || caption.Contains("sign out") || caption.Contains("play games") ||
                caption.Contains("game center"))
                offenders.Add(b.name);
        }
        Check("options: no account / sign-in / sign-out buttons (" + string.Join(",", offenders) + ")", offenders.Count == 0);
        Check("options: no AccountRow object", SceneUtil.FindAny("AccountRow") == null);
        Object.DestroyImmediate(panel.gameObject);
    }
}
