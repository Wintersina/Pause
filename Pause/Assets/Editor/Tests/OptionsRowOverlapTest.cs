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
        // top cutouts: wide notch, waterfall + centre hole, corner punch-hole, centre hole + gesture bar, Dynamic Island
        "and-1080x2340-notch", "and-1080x2340-waterfall", "and-1080x2400-corner", "and-1080x2400-gesture", "iphone-15",
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
            TopChecks(rig, tag, scroller);
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

    // Everything in the Options column -- the first row, the headings, every button and slider -- starts below the
    // safe area and any camera cutout, with a margin (what is scrolled out of the viewport is not visible, so the
    // viewport's own top counts too).
    static void TopChecks(ScreenFitRig rig, string tag, OptionsScroll scroller)
    {
        float safeTopPx = rig.H - ScreenInfo.TopObstructionInset;
        float marginPx = 24f;
        Check(tag + "top obstruction covers the safe-area inset", ScreenInfo.TopObstructionInset >= ScreenInfo.SafeTopInset);
        float top = float.MinValue;
        string topName = "";
        var canvas = Object.FindFirstObjectByType<OptionsScroll>();
        if (canvas == null) { Check(tag + "options column exists", false); return; }
        var found = new List<RectTransform>();
        foreach (var b in canvas.GetComponentsInChildren<Selectable>(false)) found.Add((RectTransform)b.transform);
        foreach (var h in canvas.GetComponentsInChildren<OptionsHeading>(false)) found.Add((RectTransform)h.transform);
        foreach (var rt in found)
        {
            if (!rt.gameObject.activeInHierarchy || rt.name == "BackButton") continue;
            var r = rig.PixelRect(rt);
            if (r.width < 1f || r.height < 1f) continue;
            if (scroller != null && scroller.IsScrolling && rt.IsChildOf(scroller.Viewport))
            {
                var vp = rig.PixelRect(scroller.Viewport);
                r = Rect.MinMaxRect(Mathf.Max(r.xMin, vp.xMin), Mathf.Max(r.yMin, vp.yMin), Mathf.Min(r.xMax, vp.xMax), Mathf.Min(r.yMax, vp.yMax));
                if (r.width < 1f || r.height < 1f) continue;
            }
            if (r.yMax > top) { top = r.yMax; topName = rt.name; }
        }
        Check(tag + "top-most Options element (" + topName + ", " + top + " px) sits below the safe-area top (" + safeTopPx + ") by >= " + marginPx + " px",
              top > float.MinValue && top <= safeTopPx - marginPx);
        var lb = Px(rig, LeaderboardSettingsOptions.RowName);
        Check(tag + "LEADERBOARD SETTINGS row below the safe-area top with a margin", lb.width < 1f || lb.yMax <= safeTopPx - marginPx);
    }

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
        Check(tag + "switch order VIBRATION > UPGRADE OFFERS > sound switches",
              sw[0].yMin >= sw[1].yMax - 1f && sw[1].yMin >= Mathf.Max(sw[2].yMax, sw[3].yMax) - 1f);
        Check(tag + "EFFECTS and MUSIC switches side by side", Mathf.Abs(sw[2].y - sw[3].y) < 1f && sw[2].xMax <= sw[3].xMin + 1f);
        Check(tag + "EFFECTS and MUSIC sliders side by side", Mathf.Abs(sl[0].y - sl[1].y) < 1f && sl[0].xMax <= sl[1].xMin + 1f);
        // the SHIP PERCH block sits between the groups: switches > selector > distance slider > SLIDERS heading > sliders
        var perchSel = Px(rig, PerchOptions.RowName); var perchSl = Px(rig, PerchOptions.SliderName);
        Check(tag + "SHIP PERCH selector sits below the switches", perchSel.width > 1f && perchSel.yMax <= swBottom + 1f);
        Check(tag + "SHIP PERCH distance slider sits directly under the selector (gap under a third of the selector, centred with it)",
              perchSl.width > 1f && perchSl.yMax <= perchSel.yMin + 1f && perchSel.yMin - perchSl.yMax < perchSel.height * .33f && Mathf.Abs(perchSl.center.x - perchSel.center.x) < 1f);
        Check(tag + "SHIP PERCH block sits above the sound sliders", perchSl.yMin >= slTop - 1f);
        var h1 = Px(rig, "SwitchesHeading"); var h2 = Px(rig, "SlidersHeading");
        Check(tag + "SWITCHES heading sits above the switches", h1.width > 1f && h1.yMin >= sw[0].yMax - 1f);
        Check(tag + "SLIDERS heading sits under the SHIP PERCH block, over the sliders", h2.width > 1f && h2.yMax <= perchSl.yMin + 1f && h2.yMin >= slTop - 1f);

        // the SHIP PERCH direction selector: three buttons LEFT, TOP, RIGHT, clear of each other, the title and the row edge
        var rowRect = perchSel;
        var perch = Object.FindFirstObjectByType<PerchOptions>();
        var dirs = new Rect[PerchSettings.TiltCount];
        var dirButtons = new UnityEngine.UI.Button[PerchSettings.TiltCount];
        var perchRow = GameObject.Find(PerchOptions.RowName).transform;
        var titleT = perchRow.Find(PerchOptions.TitleName);
        var title = titleT == null ? default(Rect) : rig.PixelRect((RectTransform)titleT);
        Check(tag + "perch selector has a SHIP PERCH title", title.width > 1f);
        for (int i = 0; i < dirs.Length; i++)
        {
            var goT = perchRow.Find(PerchOptions.DirName + (i + 1));
            var go = goT == null ? null : goT.gameObject;
            Check(tag + "perch button " + (i + 1) + " exists", go != null);
            if (go == null) return;
            dirButtons[i] = go.GetComponent<UnityEngine.UI.Button>();
            dirs[i] = rig.PixelRect((RectTransform)go.transform);
            var label = go.GetComponentInChildren<UnityEngine.UI.Text>(true);
            Check(tag + "perch button " + (i + 1) + " reads \"" + PerchOptions.SlotLabel(i) + "\"", label != null && label.text == PerchOptions.SlotLabel(i) && label.text == new[] { "LEFT", "TOP", "RIGHT" }[i]);
            Check(tag + "perch button " + (i + 1) + " inside its row and clear of the title",
                  dirs[i].xMin >= rowRect.xMin - 1f && dirs[i].xMax <= rowRect.xMax + 1f && dirs[i].yMin >= rowRect.yMin - 1f && Apart(dirs[i], title));
        }
        Check(tag + "perch buttons run left to right LEFT, TOP, RIGHT without touching", dirs[0].xMax <= dirs[1].xMin && dirs[1].xMax <= dirs[2].xMin);
        // tapping maps the buttons to left/top/right and lights only the chosen one
        int[] expect = { PerchSettings.AboveLeft, PerchSettings.Above, PerchSettings.AboveRight };
        for (int i = 0; i < dirs.Length; i++)
        {
            dirButtons[i].onClick.Invoke();
            bool lit = true;
            for (int j = 0; j < dirs.Length; j++)
            {
                var t = dirButtons[j].GetComponentInChildren<UnityEngine.UI.Text>(true);
                lit &= t.color == PerchOptions.LabelColor(j == i) && ((UnityEngine.UI.Image)dirButtons[j].targetGraphic).color == PerchOptions.ArtColor(j == i);
            }
            Check(tag + "perch button " + (i + 1) + " stores " + PerchSettings.TiltName(expect[i]) + " and lights alone",
                  PerchSettings.Tilt == expect[i] && PerchSettings.SlotOfTilt(PerchSettings.Tilt) == i && lit);
        }
        // saved values map back: old ABOVE LEFT = 1 -> button 1, ABOVE = 0 -> button 2, ABOVE RIGHT = 2 -> button 3
        Check(tag + "saved tilts map to buttons (left=1, top=2, right=3)",
              PerchSettings.SlotOfTilt(PerchSettings.AboveLeft) == 0 && PerchSettings.SlotOfTilt(PerchSettings.Above) == 1 && PerchSettings.SlotOfTilt(PerchSettings.AboveRight) == 2);

        // the distance slider is continuous: any value is kept, no ticks, no snapping
        var root = GameObject.Find(PerchOptions.SliderName);
        var slider = root.GetComponent<UnityEngine.UI.Slider>();
        Check(tag + "perch slider has no tick numbers", root.transform.Find("Ticks") == null);
        Check(tag + "perch slider is continuous 0..1", !slider.wholeNumbers && slider.minValue == 0f && slider.maxValue == 1f);
        slider.value = .37f;
        Check(tag + "slider .37 is stored as is, not snapped", Mathf.Abs(PerchSettings.ToSlider(PerchSettings.Scale) - .37f) < .001f);
        PlayerPrefs.SetFloat(PerchSettings.ScaleKey, 1.23f);
        PerchSettings.ReloadForTests();
        perch.Refresh();
        Check(tag + "an old saved distance is shown unsnapped", Mathf.Abs(slider.value - PerchSettings.ToSlider(1.23f)) < .001f && Mathf.Approximately(PerchSettings.Scale, 1.23f));
        var ring = slider.handleRect.Find(OptionsSliderHandle.RingName);
        Check(tag + "perch handle wears a ring", ring != null && ring.GetComponent<UnityEngine.UI.Image>() != null);
        var rootRect = Px(rig, PerchOptions.SliderName);
        var trackRect = rig.PixelRect((RectTransform)root.transform.Find("Track"));
        var caption = rig.PixelRect((RectTransform)root.transform.Find(PerchOptions.CaptionName));
        foreach (float v in new[] { 0f, .5f, 1f })
        {
            slider.value = v;
            Canvas.ForceUpdateCanvases();
            var ringRect = rig.PixelRect((RectTransform)ring);
            Check(tag + "slider at " + v + ": handle ring inside the touch area and clear of the caption",
                  ringRect.xMin >= rootRect.xMin - 1f && ringRect.xMax <= rootRect.xMax + 1f && ringRect.yMin >= rootRect.yMin - 1f && Apart(ringRect, caption));
        }
        Check(tag + "caption clear of the track", Apart(caption, trackRect));
        var capText = root.transform.Find(PerchOptions.CaptionName).GetComponent<UnityEngine.UI.Text>();
        Check(tag + "distance caption reads DISTANCE", capText != null && capText.text == "DISTANCE");
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
