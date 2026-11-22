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
        try
        {
            foreach (var id in Devices)
                foreach (bool dev in new[] { true, false })
                foreach (bool privacy in new[] { true, false })
                {
                    // The editor always offers developer mode; "developer off" must lay out like a release build.
                    DeveloperUnlocks.TestAvailableOverride = dev ? (bool?)null : false;
                    Measure(id, dev, privacy);
                }
        }
        finally { DeveloperUnlocks.TestAvailableOverride = null; PrivacyOptionsRow.TestRequiredOverride = null; }
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

    static void Measure(string deviceId, bool dev, bool privacy)
    {
        var device = FitDevice.Find(deviceId);
        var screen = ScreenFitScreens.Find("options");
        EditorSceneLoader.Open("leaderboardS3");
        Time.timeScale = 1f;
        DeveloperUnlocks.SetEnabled(dev);
        string tag = deviceId + (dev ? " (developer on)" : " (developer off)") + (privacy ? " (privacy on)" : " (privacy off)") + ": ";
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
            PrivacyOptionsRow.TestRequiredOverride = privacy;
            if (PrivacyOptionsRow.Required) new GameObject("~PrivacyOptionsRow").AddComponent<PrivacyOptionsRow>().Build();
            SafeAreaClamp.AttachAll("leaderboardS3");
            rig.Sync();
            SafeAreaClamp.AttachAll("leaderboardS3");
            rig.Sync();

            var scroller = Object.FindFirstObjectByType<OptionsScroll>();
            var names = new List<string>();
            var present = new List<string>();   // every live button, even one scrolled fully out of view
            var rects = new List<Rect>();
            foreach (var b in Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var rt = b.transform as RectTransform;
                if (rt == null || !b.gameObject.activeInHierarchy) continue;
                var r = rig.PixelRect(rt);
                if (r.width >= 1f && r.height >= 1f) present.Add(b.name);
                // A row scrolled out of the viewport is cut off by its mask (no touch lands there): only what shows can be tapped.
                if (scroller != null && scroller.IsScrolling && rt.IsChildOf(scroller.Viewport))
                {
                    var vp = rig.PixelRect(scroller.Viewport);
                    r = Rect.MinMaxRect(Mathf.Max(r.xMin, vp.xMin), Mathf.Max(r.yMin, vp.yMin), Mathf.Min(r.xMax, vp.xMax), Mathf.Min(r.yMax, vp.yMax));
                    if (r.width < 1f || r.height < 1f) continue;
                }
                if (r.width < 1f || r.height < 1f) continue;
                names.Add(b.name); rects.Add(r);
            }
            Check(tag + "PRIVACY OPTIONS row " + (privacy ? "exists" : "absent"), present.Contains(PrivacyOptionsRow.RowName) == privacy);
            if (!dev)
            {
                foreach (var rowName in new[] { VibrationOptions.RowName, UpgradeOfferOptions.RowName })
                {
                    var row = GameObject.Find(rowName);
                    var sw = OptionsSwitch.Find(row);
                    Check(tag + rowName + " shows an ON/OFF switch", sw != null);
                    if (sw == null) continue;
                    Check(tag + rowName + " row dips on press", row.GetComponent<CelPress>() != null);
                    var swRect = rig.PixelRect((RectTransform)sw.transform);
                    var rowRect = rig.PixelRect((RectTransform)row.transform);
                    Check(tag + rowName + " switch sits inside the row", swRect.xMin >= rowRect.xMin - 1f && swRect.xMax <= rowRect.xMax + 1f);
                    var text = row.GetComponentInChildren<Text>(true);
                    if (text != null && text.gameObject != row)
                        Check(tag + rowName + " label clear of the switch", rig.PixelRect(text.rectTransform).xMax <= swRect.xMin + 1f);
                    sw.Set(true, false);
                    float onX = sw.KnobX;
                    sw.Set(false, false);
                    Check(tag + rowName + " knob left when OFF, right when ON", sw.KnobX < 0f && onX > 0f && !sw.On);
                }
            }
            if (!dev) GroupChecks(rig, tag);
            Check(tag + "Credits button exists", present.Contains("CreditsButton"));
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

    static Rect Px(ScreenFitRig rig, string name)
    {
        var go = GameObject.Find(name);
        return go == null ? default(Rect) : rig.PixelRect((RectTransform)go.transform);
    }

    static bool Apart(Rect a, Rect b) { return a.xMax <= b.xMin + .5f || b.xMax <= a.xMin + .5f || a.yMax <= b.yMin + .5f || b.yMax <= a.yMin + .5f; }

    // Switches (on/off buttons) and sliders each form one group, switches on top, each under its heading; and the
    // SHIP PERCH slider's tick numbers 1 2 3 clear of the track, the handle (ring included) and each other.
    static void GroupChecks(ScreenFitRig rig, string tag)
    {
        var sw = new List<Rect>();
        float swBottom = float.MaxValue, slTop = float.MinValue;
        foreach (var n in OptionsLayout.SwitchRows)
        {
            var r = Px(rig, n);
            Check(tag + n + " exists", r.width > 1f);
            sw.Add(r);
            swBottom = Mathf.Min(swBottom, r.yMin);
        }
        var sl = new List<Rect>();
        foreach (var n in OptionsLayout.SliderRows)
        {
            var r = Px(rig, n);
            Check(tag + n + " exists", r.width > 1f);
            sl.Add(r);
            slTop = Mathf.Max(slTop, r.yMax);
        }
        Check(tag + "every switch sits above every slider", swBottom >= slTop - 1f);
        // order inside the groups, top to bottom: VIBRATION, UPGRADE OFFERS, SHIP PERCH direction, EFFECTS / MUSIC switches
        Check(tag + "switch order VIBRATION > UPGRADE OFFERS > SHIP PERCH > sound switches",
              sw[0].yMin >= sw[1].yMax - 1f && sw[1].yMin >= sw[2].yMax - 1f && sw[2].yMin >= Mathf.Max(sw[3].yMax, sw[4].yMax) - 1f);
        Check(tag + "EFFECTS and MUSIC switches side by side", Mathf.Abs(sw[3].y - sw[4].y) < 1f && sw[3].xMax <= sw[4].xMin + 1f);
        Check(tag + "slider order EFFECTS + MUSIC volume > SHIP PERCH distance",
              Mathf.Abs(sl[0].y - sl[1].y) < 1f && sl[0].xMax <= sl[1].xMin + 1f && sl[0].yMin >= sl[2].yMax - 1f);
        var h1 = Px(rig, "SwitchesHeading"); var h2 = Px(rig, "SlidersHeading");
        Check(tag + "SWITCHES heading sits above the switches", h1.width > 1f && h1.yMin >= sw[0].yMax - 1f);
        Check(tag + "SLIDERS heading sits between the groups", h2.width > 1f && h2.yMax <= swBottom + 1f && h2.yMin >= slTop - 1f);

        // the SHIP PERCH slider and its ticks
        var root = GameObject.Find(PerchOptions.SliderName);
        var slider = root.GetComponent<UnityEngine.UI.Slider>();
        var perch = Object.FindFirstObjectByType<PerchOptions>();
        var texts = new UnityEngine.UI.Text[PerchSettings.StopCount];
        for (int i = 0; i < texts.Length; i++)
        {
            var tr = root.transform.Find(PerchOptions.TicksName + "/Tick" + (i + 1));
            texts[i] = tr == null ? null : tr.GetComponent<UnityEngine.UI.Text>();
        }
        bool labelled = true;
        for (int i = 0; i < texts.Length; i++) labelled &= texts[i] != null && texts[i].text == (i + 1).ToString();
        Check(tag + "perch ticks read 1 2 3", labelled);
        if (!labelled) return;
        var track = Px(rig, PerchOptions.SliderName + "/Track");
        var rootRect = Px(rig, PerchOptions.SliderName);
        var trackRect = rig.PixelRect((RectTransform)root.transform.Find("Track"));
        var ring = slider.handleRect.Find(OptionsSliderHandle.RingName);
        Check(tag + "perch handle wears a ring", ring != null && ring.GetComponent<UnityEngine.UI.Image>() != null);
        var tickRects = new Rect[texts.Length];
        for (int i = 0; i < texts.Length; i++) tickRects[i] = rig.PixelRect(texts[i].rectTransform);
        for (int i = 0; i < texts.Length; i++)
        {
            Check(tag + "tick " + (i + 1) + " inside the slider's touch area",
                  tickRects[i].xMin >= rootRect.xMin - 1f && tickRects[i].xMax <= rootRect.xMax + 1f && tickRects[i].yMin >= rootRect.yMin - 1f && tickRects[i].yMax <= rootRect.yMax + 1f);
            Check(tag + "tick " + (i + 1) + " clear of the track", Apart(tickRects[i], trackRect));
            for (int j = i + 1; j < texts.Length; j++) Check(tag + "ticks " + (i + 1) + " and " + (j + 1) + " do not overlap", Apart(tickRects[i], tickRects[j]));
        }
        var caption = rig.PixelRect((RectTransform)root.transform.Find(PerchOptions.CaptionName));
        Check(tag + "caption clear of the track and the ticks", Apart(caption, trackRect) && Apart(caption, tickRects[0]) && Apart(caption, tickRects[1]) && Apart(caption, tickRects[2]));
        for (int stop = 0; stop < PerchSettings.StopCount; stop++)
        {
            PerchSettings.SetStop(stop);
            perch.Refresh();
            Canvas.ForceUpdateCanvases();
            var handle = rig.PixelRect(slider.handleRect);
            var ringRect = rig.PixelRect((RectTransform)ring);
            bool clear = true;
            for (int i = 0; i < texts.Length; i++) clear &= Apart(ringRect, tickRects[i]) && Apart(handle, tickRects[i]);
            Check(tag + "stop " + (stop + 1) + ": handle and its ring clear of every tick and the caption [handle " + handle + " ring " + ringRect +
                  " caption " + caption + " ticks " + tickRects[0] + " " + tickRects[1] + " " + tickRects[2] + "]", clear && Apart(ringRect, caption));
            Check(tag + "stop " + (stop + 1) + ": handle centred over its number", Mathf.Abs(handle.center.x - tickRects[stop].center.x) < 2f);
            bool colours = true;
            for (int i = 0; i < texts.Length; i++) colours &= texts[i].color == PerchOptions.TickColor(i == stop);
            Check(tag + "stop " + (stop + 1) + ": only its number is lit", colours && Mathf.RoundToInt(slider.value) == stop);
        }
        // dragging / tapping lands on a stop, and the press shows
        slider.value = 2f;
        Check(tag + "slider value 2 stores the far stop", PerchSettings.Stop == 2 && Mathf.Approximately(PerchSettings.Scale, PerchSettings.MaxScale));
        slider.value = 1f;
        Check(tag + "slider value 1 is the stock perch", PerchSettings.Stop == 1 && Mathf.Approximately(PerchSettings.Scale, PerchSettings.DefaultScale));
        foreach (var n in new[] { PerchOptions.SliderName, SoundOptions.EffectsSliderName, SoundOptions.MusicSliderName })
        {
            var style = GameObject.Find(n).GetComponent<OptionsSliderHandle>();
            Check(tag + n + " handle has the grab ring", style != null && style.ring != null);
            if (style == null) continue;
            style.OnPointerDown(null);
            bool pressed = style.Pressed && style.ring.color == AkiraPalette.Bone && style.handle.rectTransform.localScale.x > 1.05f;
            style.OnPointerUp(null);
            Check(tag + n + " handle shows a pressed state and releases", pressed && !style.Pressed && style.handle.rectTransform.localScale.x == 1f);
        }
        PerchSettings.Reset();
    }
}
