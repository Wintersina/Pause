using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

// The home screen (startS4) laid out like a phone (ScreenFitRig: the device's
// pixel size, safe area and density; CanvasScaler + UiScaleFloor; LayoutHome)
// at three shapes, with the PAUSE logo and the menu buttons' rects pinned:
// the gap between the logo and Play, the pitch between the buttons, their
// size. The menu backdrop (MenuBackdrop) must never move any of it; this
// failing means the menu's spacing changed (on purpose: re-pin).
//
// Why a rig and not the batch screen: the editor's batch Game view has no
// phone-sized screen, so a canvas read from it is laid out for the wrong
// shape (the old MenuBackdropPreview did, and showed Play under the logo).
// Values in pixels, Unity bottom-left space; measured on the layout before
// the menu backdrop existed and unchanged since.
public static class HomeLayoutTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[HOMELAY] PASS  " : "[HOMELAY] FAIL  ") + what);
        if (!ok) fails++;
    }

    public struct Pin
    {
        public string device;
        public float logoY0, logoY1;      // PAUSE logo's drawn outline
        public float x, w, h;             // every button
        public float play, dock, shop, options, credits, codex;   // bottom edges
        public float screenW, screenH, safeTop;   // the screen, and the unsafe strip at its top (px)
    }

    static readonly Pin[] Pins =
    {
        new Pin { device = "and-1080x1920", logoY0 = 1293.7f, logoY1 = 1676.2f, x = 118.8f, w = 842.4f, h = 108.8f,
                  play = 1014.4f, dock = 882.6f, shop = 750.7f, options = 618.9f, credits = 487.0f, codex = 355.2f },
        // Tall screens (>= ~21:9) lift the logo and the column (startMenu.LogoLiftFraction / ColumnExtraFraction): re-pinned 2026-11-08.
        new Pin { device = "and-1080x2340-notch", logoY0 = 1704.4f, logoY1 = 2086.9f, x = 118.8f, w = 842.4f, h = 132.6f,
                  play = 1455.5f, dock = 1294.8f, shop = 1134.2f, options = 973.5f, credits = 812.8f, codex = 652.1f },
        new Pin { device = "iphone-13", logoY0 = 1842.9f, logoY1 = 2257.3f, x = 128.7f, w = 912.6f, h = 143.5f,
                  play = 1573.0f, dock = 1399.2f, shop = 1225.3f, options = 1051.4f, credits = 877.6f, codex = 703.7f },
        new Pin { device = "flip7-1080x2520", logoY0 = 1921.3f, logoY1 = 2303.8f, x = 118.8f, w = 842.4f, h = 142.8f,
                  play = 1689.2f, dock = 1516.2f, shop = 1343.2f, options = 1170.1f, credits = 997.1f, codex = 824.0f },
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, WorldManager.Worlds.Length - 1);
        foreach (var id in new[] { "and-1080x1920", "and-1080x2340-notch", "iphone-13", "flip7-1080x2520", "and-1080x2640", "flip7-cover-948x1048" })
        {
            bool found = false;
            foreach (var pin in Pins)
                if (pin.device == id) { found = true; Measure(pin, false); Measure(pin, true); }
            if (!found) { bool ok; var got = Stage(id, false, out ok); Debug.Log("[HOMELAY] measured " + JsonUtility.ToJson(got)); Shape(got, id); }
        }
        Debug.Log("[HOMELAY] failures: " + fails);
        return fails;
    }

    static readonly string[] Buttons =
    {
        "MainMenuCanvas/UIPanel/PlayButton", "MainMenuCanvas/UIPanel/shopButton", "MainMenuCanvas/UIPanel/ShopEntryButton", "MainMenuCanvas/UIPanel/achivButton",
        "MainMenuCanvas/UIPanel/LeaderboardButton", "MainMenuCanvas/UIPanel/CodexButton",
    };

    static Rect Rect_(ScreenFitRig rig, string path)
    {
        var go = GameObject.Find(path);
        return go == null ? default(Rect) : rig.PixelRect((RectTransform)go.transform);
    }

    // The home screen as the rig stages it, reduced to numbers.
    public static Pin Stage(string deviceId, bool withBackdrop, out bool ok)
    {
        var pin = new Pin { device = deviceId };
        ok = false;
        var device = FitDevice.Find(deviceId);
        var screen = ScreenFitScreens.Find("home");
        EditorSceneLoader.Open(screen.scene);
        Time.timeScale = 1f;
        using (var rig = new ScreenFitRig(device, screen.MinHalfWidth))
        {
            UiScaleFloor.AttachScene();
            rig.Sync();
            screen.stage(rig);
            rig.Sync();
            if (withBackdrop)
            {
                // the menu's backdrop, built and ticked: it draws behind the UI and must not move it
                var picks = new List<MenuBackdropSelection.Pick>();
                MenuBackdropSelection.Candidates(WorldManager.Worlds.Length - 1, picks);
                var mb = new GameObject("~MenuBackdrop").AddComponent<MenuBackdrop>();
                foreach (var p in picks) if (mb.Build(p)) break;
                for (int i = 0; i < 60; i++) mb.Step(1f / 30f);
                rig.Sync();
            }
            var title = GameObject.Find("menuTitle");
            var logo = title != null ? rig.TightPixelRect(title.GetComponent<SpriteRenderer>()) : default(Rect);
            var r = new Rect[Buttons.Length];
            for (int i = 0; i < r.Length; i++) r[i] = Rect_(rig, Buttons[i]);
            pin.screenW = rig.W; pin.screenH = rig.H; pin.safeTop = rig.device.top;
            pin.logoY0 = logo.yMin; pin.logoY1 = logo.yMax;
            pin.x = r[0].x; pin.w = r[0].width; pin.h = r[0].height;
            pin.play = r[0].y; pin.dock = r[1].y; pin.shop = r[2].y; pin.options = r[3].y; pin.credits = r[4].y; pin.codex = r[5].y;
            ok = logo.height > 0f && r[0].height > 0f;
            var t = Object.FindFirstObjectByType<TitleScreenTraffic>();
            if (t != null) t.Shutdown();
            foreach (var m in Object.FindObjectsByType<MenuBackdrop>(FindObjectsSortMode.None)) Object.DestroyImmediate(m.gameObject);
        }
        return pin;
    }

    // Where the group sits on the screen: tall phones must not hang the column from the bottom half.
    // Fractions are of the screen height, measured down from the top.
    public const float TallScreenColumnCentreMax = .53f;   // Play's top .. Codex's bottom, on screens >= 2.0 tall
    static void Shape(Pin p, string d)
    {
        if (p.screenW <= 0f) return;
        float top = p.screenH - (p.play + p.h), bottom = p.screenH - p.codex;   // px from the top
        float centre = (top + bottom) * .5f / p.screenH;
        float tall = startMenu.Tallness(p.screenW, p.screenH);
        float aspect = p.screenH / p.screenW;
        Debug.Log("[HOMELAY] " + d + " column centre " + centre.ToString("F3") + " of the height from the top, logo top " + (p.screenH - p.logoY1) + " px from the top");
        if (aspect >= 2f) Check(d + ": column centre " + centre.ToString("F3") + " <= " + TallScreenColumnCentreMax + " (not bottom-heavy)", centre <= TallScreenColumnCentreMax);
        float logoTop = p.screenH - p.logoY1;
        Check(d + ": logo top " + logoTop + " px clears the top safe strip " + p.safeTop, logoTop >= p.safeTop + 24f);
        if (aspect < 1.85f) Check(d + ": a short / wide screen is not lifted (tallness " + tall + ")", tall == 0f);
    }

    static bool Near(float a, float b) { return Mathf.Abs(a - b) <= 1.5f; }

    static void Measure(Pin want, bool withBackdrop)
    {
        bool ok;
        var got = Stage(want.device, withBackdrop, out ok);
        Debug.Log("[HOMELAY] measured " + JsonUtility.ToJson(got));
        string d = want.device + (withBackdrop ? " + menu backdrop: " : ": ");
        Check(d + "logo and buttons found", ok);
        Check(d + "logo " + got.logoY0 + ".." + got.logoY1, Near(got.logoY0, want.logoY0) && Near(got.logoY1, want.logoY1));
        Check(d + "buttons " + got.w + "x" + got.h + " at x " + got.x, Near(got.x, want.x) && Near(got.w, want.w) && Near(got.h, want.h));
        Check(d + "Play / Space dock / Shop / Options / Leaderboard / Codex at " + got.play + " " + got.dock + " " + got.shop + " " + got.options + " " + got.credits + " " + got.codex,
              Near(got.play, want.play) && Near(got.dock, want.dock) && Near(got.shop, want.shop) && Near(got.options, want.options) &&
              Near(got.credits, want.credits) && Near(got.codex, want.codex));
        Shape(got, want.device);
        // the spacing the player sees, independent of the absolute pins
        float gap = got.logoY0 - (got.play + got.h);
        Check(d + "the logo clears Play by " + gap + " px (" + (gap / got.h).ToString("F2") + " button heights)", gap >= got.h * .6f);
        float pitch = got.play - got.dock;
        Check(d + "buttons are evenly spaced (pitch " + pitch + ")",
              Near(got.dock - got.shop, pitch) && Near(got.shop - got.options, pitch) && Near(got.options - got.credits, pitch) && Near(got.credits - got.codex, pitch));
    }
}
