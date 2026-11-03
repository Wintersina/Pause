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
    }

    static readonly Pin[] Pins =
    {
        new Pin { device = "and-1080x1920", logoY0 = 1293.7f, logoY1 = 1676.2f, x = 118.8f, w = 842.4f, h = 108.8f,
                  play = 1014.4f, dock = 882.6f, shop = 750.7f, options = 618.9f, credits = 487.0f, codex = 355.2f },
        new Pin { device = "and-1080x2340-notch", logoY0 = 1503.7f, logoY1 = 1886.2f, x = 118.8f, w = 842.4f, h = 132.6f,
                  play = 1236.3f, dock = 1075.6f, shop = 914.9f, options = 754.3f, credits = 593.6f, codex = 432.9f },
        new Pin { device = "iphone-13", logoY0 = 1627.5f, logoY1 = 2041.9f, x = 128.7f, w = 912.6f, h = 143.5f,
                  play = 1337.7f, dock = 1163.9f, shop = 990.0f, options = 816.1f, credits = 642.3f, codex = 468.4f },
    };

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, WorldManager.Worlds.Length - 1);
        foreach (var id in new[] { "and-1080x1920", "and-1080x2340-notch", "iphone-13" })
        {
            bool found = false;
            foreach (var pin in Pins)
                if (pin.device == id) { found = true; Measure(pin, false); Measure(pin, true); }
            if (!found) { bool ok; Debug.Log("[HOMELAY] measured " + JsonUtility.ToJson(Stage(id, false, out ok))); }
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
        // the spacing the player sees, independent of the absolute pins
        float gap = got.logoY0 - (got.play + got.h);
        Check(d + "the logo clears Play by " + gap + " px (" + (gap / got.h).ToString("F2") + " button heights)", gap >= got.h * .8f);
        float pitch = got.play - got.dock;
        Check(d + "buttons are evenly spaced (pitch " + pitch + ")",
              Near(got.dock - got.shop, pitch) && Near(got.shop - got.options, pitch) && Near(got.options - got.credits, pitch) && Near(got.credits - got.codex, pitch));
    }
}
