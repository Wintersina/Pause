using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The Options screen (leaderboardS3) built the way the player gets it -- every runtime row, PRIVACY OPTIONS
// included -- on a spread of screens (tall Flip7, its square cover, regular, tablet, wide foldable), with
// developer mode on and off: no two buttons may overlap. The PRIVACY OPTIONS row used to sit at -223 in dev
// builds, on top of the Credits button at -233.
public static class OptionsRowOverlapTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[OPTROWS] PASS  " : "[OPTROWS] FAIL  ") + what);
        if (!ok) fails++;
    }

    static readonly string[] Devices =
    {
        "flip7-1080x2520", "flip7-cover-948x1048", "and-1080x1920", "and-480x854", "and-1080x2640",
        "tab-1600x2560", "fold-1812x2176", "ipad-9", "iphone-se",
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetString("HasDoneTut", "true");
        foreach (var id in Devices)
            foreach (bool dev in new[] { true, false })
                Measure(id, dev);
        DeveloperUnlocks.SetEnabled(false);
        Debug.Log("[OPTROWS] failures: " + fails);
        return fails;
    }

    static T Add<T>(string name) where T : MonoBehaviour
    {
        var c = new GameObject(name).AddComponent<T>();
        c.SendMessage("Start");
        return c;
    }

    static void Measure(string deviceId, bool dev)
    {
        var device = FitDevice.Find(deviceId);
        var screen = ScreenFitScreens.Find("options");
        EditorSceneLoader.Open("leaderboardS3");
        Time.timeScale = 1f;
        DeveloperUnlocks.SetEnabled(dev);
        string tag = deviceId + (dev ? " (developer on)" : " (developer off)") + ": ";
        using (var rig = new ScreenFitRig(device, screen.MinHalfWidth))
        {
            MenuStyler.StyleScene();
            LeaderboardService.Instance = new LeaderboardService(new FakeLeaderboards(), () => 0f) { Ios = rig.device.ios };
            rig.Sync();
            if (dev) Add<DeveloperOptions>("~DeveloperOptions");
            else
            {
                Add<VibrationOptions>("~VibrationOptions");
                Add<PerchOptions>("~PerchOptions");
                Add<UpgradeOfferOptions>("~UpgradeOfferOptions");
            }
            Add<SoundOptions>("~SoundOptions");
            Add<LeaderboardSettingsOptions>("~LeaderboardSettingsOptions");
            var privacy = new GameObject("~PrivacyOptionsRow").AddComponent<PrivacyOptionsRow>();
            privacy.Build();
            SafeAreaClamp.AttachAll("leaderboardS3");
            rig.Sync();
            SafeAreaClamp.AttachAll("leaderboardS3");
            rig.Sync();

            var names = new List<string>();
            var rects = new List<Rect>();
            foreach (var b in Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var rt = b.transform as RectTransform;
                if (rt == null || !b.gameObject.activeInHierarchy) continue;
                var r = rig.PixelRect(rt);
                if (r.width < 1f || r.height < 1f) continue;
                names.Add(b.name); rects.Add(r);
            }
            Check(tag + "PRIVACY OPTIONS row exists", names.Contains(PrivacyOptionsRow.RowName));
            Check(tag + "Credits button exists", names.Contains("CreditsButton"));
            int overlaps = 0;
            for (int i = 0; i < rects.Count; i++)
                for (int j = i + 1; j < rects.Count; j++)
                {
                    var a = rects[i]; var c = rects[j];
                    float w = Mathf.Min(a.xMax, c.xMax) - Mathf.Max(a.xMin, c.xMin);
                    float h = Mathf.Min(a.yMax, c.yMax) - Mathf.Max(a.yMin, c.yMin);
                    if (w > 1f && h > 1f)
                    {
                        overlaps++;
                        Check(tag + names[i] + " and " + names[j] + " overlap by " + w + "x" + h + " px", false);
                    }
                }
            Check(tag + rects.Count + " buttons, no two overlap", overlaps == 0);
        }
    }
}
