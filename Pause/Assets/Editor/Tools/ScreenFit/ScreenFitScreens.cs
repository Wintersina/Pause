using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

// Every screen and overlay of the game, as a recipe that puts it on screen in
// edit mode (what the runtime bootstraps and a few frames of Update would do)
// on the device a ScreenFitRig is faking. ScreenFitTest checks each one on
// every device of the matrix; ScreenFitSheets renders them.
//
// The gameplay HUD's top band (score read-out, quick actions, boss chip and
// banner) is laid out here only so the screens under it are staged the way
// they appear in a run; its own checks live in NextFeatures0907Test and
// BossWarningTest, so its subtree is ignored by this suite's generic checks.
public class FitScreen
{
    public string id;
    public string scene;          // null = an empty scene
    public string title;
    public bool gameplayView;     // camera fits CameraFit.GameplayHalfWidth
    public bool fullBleed;        // the frame must leave no bare (undrawn) pixel
    public Action<ScreenFitRig> stage;

    public float MinHalfWidth { get { return gameplayView ? CameraFit.GameplayHalfWidth : 2.85f; } }
}

// A finding accepted on purpose: it stays in the report (WAIVED) but does
// not fail ScreenFitTest. Each one is an open design decision, listed with
// the reason in the screen-fit report.
public class FitWaiver
{
    public string screen;          // id prefix; null = every screen
    public string kind, element;   // finding kind; substring of the element
    public string devices;         // comma-separated device ids; null = every device
    public string reason;

    public bool Applies(string screenId, FitDevice d)
    {
        if (screen != null && !screenId.StartsWith(screen)) return false;
        if (devices == null) return true;
        foreach (var id in devices.Split(',')) if (id.Trim() == d.id) return true;
        return false;
    }
}

public static class ScreenFitScreens
{
    public static readonly FitWaiver[] Waivers =
    {
        // Developer-only rows: left as they are on purpose (user decision).
        new FitWaiver { screen = "options", kind = "TAPSIZE", element = "Canvas/Developer",
                        reason = "DEVELOPER-ONLY rows (DeveloperUnlocks.Available builds; not shipped to players): left as is by decision" },
        new FitWaiver { screen = "options", kind = "SMALLTEXT", element = "AccountRow/Details",
                        reason = "DEVELOPER-ONLY sign-in details line (not shipped to players): left as is by decision" },
    };

    const float Dt = 1f / 60f;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    public static readonly FitScreen[] All =
    {
        new FitScreen { id = "splash", scene = "spashS7", title = "Splash (HapticGate)", fullBleed = true, stage = Splash },
        new FitScreen { id = "home", fullBleed = true, scene = "startS4", title = "Title / main menu", stage = r => Home(r, false) },
        new FitScreen { id = "home-toasts", fullBleed = true, scene = "startS4", title = "Home + quit / account toasts", stage = r => Home(r, true) },
        new FitScreen { id = "codex-grid", fullBleed = true, scene = "startS4", title = "Codex / pilot's log: list", stage = r => CodexScreen(r, false) },
        new FitScreen { id = "codex-detail", fullBleed = true, scene = "startS4", title = "Codex / pilot's log: entry", stage = r => CodexScreen(r, true) },
        new FitScreen { id = "options", fullBleed = true, scene = "leaderboardS3", title = "Options: account + developer rows", stage = r => Options(r, 0) },
        new FitScreen { id = "options-signout", fullBleed = true, scene = "leaderboardS3", title = "Options: sign-out dialog", stage = r => Options(r, 1) },
        new FitScreen { id = "leaderboard", fullBleed = true, scene = "leaderboardS3", title = "Leaderboard panel", stage = r => Options(r, 2) },
        new FitScreen { id = "credits", fullBleed = true, scene = "creditsS7", title = "Credits", stage = Credits },
        new FitScreen { id = "dock", fullBleed = true, scene = "shopS6", title = "Space dock", stage = r => Dock(r, 0, false) },
        new FitScreen { id = "dock-popup-owned", fullBleed = true, scene = "shopS6", title = "Dock popup: owned ship, colours + weapon", stage = r => Dock(r, 8, false) },
        new FitScreen { id = "dock-popup-buy", fullBleed = true, scene = "shopS6", title = "Dock popup: unbought ship", stage = r => Dock(r, 4, false) },
        new FitScreen { id = "dock-popup-top-row", fullBleed = true, scene = "shopS6", title = "Dock popup: top-row ship (flips below)", stage = r => Dock(r, -1, false) },
        new FitScreen { id = "dock-popup-edge", fullBleed = true, scene = "shopS6", title = "Dock popup: edge-column ship", stage = r => Dock(r, -2, false) },
        new FitScreen { id = "tutorial", scene = "tutorialS5", title = "Tutorial: robot, bubble, SKIP", gameplayView = true, fullBleed = true, stage = r => Tutorial(r, false) },
        new FitScreen { id = "tutorial-complete", scene = "tutorialS5", title = "Tutorial complete panel", gameplayView = true, fullBleed = true, stage = r => Tutorial(r, true) },
        new FitScreen { id = "game-launch", scene = "gameS1", title = "Run start: READY countdown", gameplayView = true, fullBleed = true, stage = r => Game(r, GameShot.Launch) },
        new FitScreen { id = "game-paused", scene = "gameS1", title = "Paused: PAUSED label, quick actions", gameplayView = true, fullBleed = true, stage = r => Game(r, GameShot.Paused) },
        new FitScreen { id = "game-popups", scene = "gameS1", title = "Score popups, banner, codex toast", gameplayView = true, fullBleed = true, stage = r => Game(r, GameShot.Popups) },
        new FitScreen { id = "game-world-banner", scene = "gameS1", title = "World banner + portal", gameplayView = true, fullBleed = true, stage = r => Game(r, GameShot.Banner) },
        new FitScreen { id = "game-boss-intro", scene = "gameS1", title = "Boss intro: name plate", gameplayView = true, fullBleed = true, stage = r => Game(r, GameShot.BossIntro) },
        new FitScreen { id = "game-boss-fight", scene = "gameS1", title = "Boss at rest (BossY)", gameplayView = true, fullBleed = true, stage = r => Game(r, GameShot.BossFight) },
        new FitScreen { id = "game-portal-pressure", scene = "gameS1", title = "Open portal: ENTER THE PORTAL, DANGER chip, lane glows", gameplayView = true, fullBleed = true, stage = r => Game(r, GameShot.PortalPressure) },
        new FitScreen { id = "game-death", scene = "gameS1", title = "Flight complete panel", gameplayView = true, fullBleed = true, stage = r => Game(r, GameShot.Death) },
    };

    public static FitScreen Find(string id)
    {
        foreach (var s in All) if (s.id == id) return s;
        return null;
    }

    // ---- helpers ------------------------------------------------------------

    static T Call<T>(object target, string method, params object[] args)
    {
        var m = target.GetType().GetMethod(method, Private);
        if (m == null) throw new MissingMethodException(target.GetType().Name, method);
        return (T)m.Invoke(target, args);
    }

    static void Call(object target, string method, params object[] args)
    {
        var m = target.GetType().GetMethod(method, Private);
        if (m == null) throw new MissingMethodException(target.GetType().Name, method);
        m.Invoke(target, args);
    }

    static object Field(object target, string name)
    {
        var f = target.GetType().GetField(name, Private);
        if (f == null) throw new MissingFieldException(target.GetType().Name, name);
        return f.GetValue(target);
    }

    static T Add<T>(string name, bool awake = true, bool start = true) where T : MonoBehaviour
    {
        var c = new GameObject(name).AddComponent<T>();
        if (awake && typeof(T).GetMethod("Awake", Private) != null) c.SendMessage("Awake");
        if (start && typeof(T).GetMethod("Start", Private) != null) c.SendMessage("Start");
        return c;
    }

    static void DevBadge(ScreenFitRig rig)
    {
        var badge = Add<DevBuildBadge>("~DevBuildBadge", true, false);
        rig.Sync();
        badge.SendMessage("Place");
    }

    // The camera view must be covered by `r` (art under the cutouts too).
    static void Bleed(ScreenFitRig rig, string name, Renderer r)
    {
        if (r != null && r.enabled && r.gameObject.activeInHierarchy) rig.AddBleed(name, rig.PixelRect(r));
    }

    // ---- splash ---------------------------------------------------------------

    static void Splash(ScreenFitRig rig)
    {
        var splash = UnityEngine.Object.FindFirstObjectByType<splashScene>();
        splash.ApplyLayout(rig.W, rig.H, rig.device.Safe);
        rig.Sync();   // the words' canvas keeps the constant scale ApplyLayout chose
        Bleed(rig, "splash backdrop", splash.GetComponent<Renderer>());
        if (splash.logo != null) rig.AddImportant("HapticGate mark", rig.TightPixelRect(splash.logo));
        DevBadge(rig);
    }

    // ---- home -------------------------------------------------------------------

    static startMenu HomeBase(ScreenFitRig rig)
    {
        PlayerPrefs.SetString("HasDoneTut", "true");
        MenuStyler.StyleScene();
        foreach (var cb in UnityEngine.Object.FindObjectsByType<CodexHomeButton>(FindObjectsInactive.Include, FindObjectsSortMode.None)) cb.Build();
        var menu = UnityEngine.Object.FindFirstObjectByType<startMenu>();
        menu.quitB.gameObject.SetActive(!rig.device.ios);   // BackNavigator.QuitAllowed: never on iOS
        rig.Sync();
        menu.LayoutHome();
        rig.Sync();
        menu.LayoutHome();
        var panel = GameObject.Find("UIPanel");
        if (panel != null) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)panel.transform);
        Canvas.ForceUpdateCanvases();

        var traffic = new GameObject("~TitleScreenTraffic").AddComponent<TitleScreenTraffic>();
        traffic.plungeInterval = new Vector2(1e6f, 1e6f);
        UnityEngine.Random.InitState(2026);
        traffic.Init();
        for (int i = 0; i < 30 * 10; i++) traffic.Step(1f / 30f);

        var title = GameObject.Find("menuTitle");
        var logo = title != null ? title.GetComponent<SpriteRenderer>() : null;
        if (logo != null) rig.AddImportant("PAUSE title logo", rig.TightPixelRect(logo));
        DevBadge(rig);
        return menu;
    }

    static void Home(ScreenFitRig rig, bool toasts)
    {
        HomeBase(rig);
        if (!toasts) return;
        var quit = BackQuitToast.Show();
        rig.Sync();
        Call(quit, "ApplyAt", .5f);
        var hint = AccountHintToast.Show("Signed in as KANEDA_THE_LONG_NAMED. Progress syncs to the cloud.");
        rig.Sync();
        Call(hint, "ApplyAt", .5f);
    }

    static void CodexScreen(ScreenFitRig rig, bool detail)
    {
        HomeBase(rig);
        DeveloperUnlocks.SetEnabled(true);   // every entry discovered: the fullest lists and details
        Codex.Reload();
        var panel = CodexPanel.Open(null);
        rig.Sync();
        panel.Refresh();
        panel.ApplyLayout(CodexUi.SafeAreaUnits(panel.GetComponent<Canvas>()));
        panel.ShowGrid();
        panel.ShowCategory(CodexPanel.Tabs[0]);
        panel.SkipAnimations();
        if (detail)
        {
            // the entry with the longest name and text
            CodexEntry longest = null;
            foreach (var tab in CodexPanel.Tabs)
            {
                panel.ShowCategory(tab);
                panel.SkipAnimations();
                for (int i = 0; i < panel.VisibleCards; i++)
                {
                    var e = panel.CardEntry(i);
                    if (e == null) continue;
                    if (longest == null || Weight(e) > Weight(longest)) longest = e;
                }
            }
            if (longest != null) panel.ShowDetail(longest);
            panel.SkipAnimations();
        }
        panel.ApplyLayout(CodexUi.SafeAreaUnits(panel.GetComponent<Canvas>()));
        panel.SkipAnimations();
        Canvas.ForceUpdateCanvases();
        // the home menu is behind the panel's scrim: covered, not part of this screen
        rig.Ignore("MainMenuCanvas");
    }

    static int Weight(CodexEntry e)
    {
        return (e.name != null ? e.name.Length * 8 : 0) + (e.lore != null ? e.lore.Length : 0);
    }

    // ---- options / leaderboard -----------------------------------------------------

    static void Options(ScreenFitRig rig, int shot)
    {
        PlayerPrefs.SetString("HasDoneTut", "true");
        MenuStyler.StyleScene();
        DeveloperUnlocks.SetEnabled(true);
        var fake = new FakeLeaderboards { LocalPlayerName = "KANEDA_THE_LONG_NAMED_PILOT" };
        var service = new LeaderboardService(fake, () => 0f) { Ios = rig.device.ios };
        LeaderboardService.Instance = service;
        rig.Sync();
        Add<DeveloperOptions>("~DeveloperOptions");
        var account = new GameObject("~AccountOptions").AddComponent<AccountOptions>();
        account.Build(new Vector2(rig.W, rig.H), rig.device.Safe, true);
        rig.Sync();
        account.Relayout();
        SafeAreaClamp.AttachAll("leaderboardS3");
        rig.Sync();   // it may change the canvas scaler
        SafeAreaClamp.AttachAll("leaderboardS3");
        DevBadge(rig);
        if (shot == 1)
        {
            AccountDialog.ConfirmSignOut();
            rig.Sync();
            rig.Ignore("Canvas");
        }
        else if (shot == 2)
        {
            // The shipped table has no live store ids yet (the panel would say
            // NO LEADERBOARDS YET): stage the one board it has, Top Score,
            // with stand-in ids so its tab and rows are on screen.
            var real = LeaderboardBoards.Get(LeaderboardBoards.TopScore);
            LeaderboardBoards.OverrideForTests(new[]
            {
                new LeaderboardBoard(real.id, "fit_android_top_score", "fit_ios_top_score", real.displayName, real.description,
                                     real.sort, LeaderboardBoards.FormatScore, run => run.score),
            });
            foreach (var b in LeaderboardBoards.All)
            {
                var rows = new List<LeaderboardEntry>();
                for (int i = 0; i < 25; i++)
                    rows.Add(new LeaderboardEntry
                    {
                        rank = i + 1,
                        playerId = i == 3 ? fake.LocalPlayerId : "p" + i,
                        playerName = i < LongNames.Length ? LongNames[i] : i % 2 == 0 ? "TETSUO" + i : "A_RATHER_LONG_PLAYER_NAME_" + i,
                        value = 99999999 - i * 137,
                        isLocalPlayer = i == 3,
                    });
                string boardId = b.PlatformId(rig.device.ios);
                if (!string.IsNullOrEmpty(boardId)) fake.SetBoard(boardId, rows.ToArray());
            }
            var panel = LeaderboardPanel.Open(service, null, new Vector2(rig.W, rig.H), rig.device.Safe, true);
            rig.Sync();
            panel.SendMessage("Update");
            rig.Ignore("Canvas");
            LeaderboardChecks(rig, panel);
        }
    }

    // Row 3 is the player's own; the rest: a 30+ character name, wide glyphs
    // (W, CJK, a surrogate-pair emoji), a short one.
    static readonly string[] LongNames =
    {
        "MAXIMILIAN_VON_STARDUST_THE_THIRD_OF_NEO_TOKYO",
        "WWWWWWWWWWWWWWWWWWWWWWWW",
        "\u5B87\u5B99\u306E\u30D1\u30A4\u30ED\u30C3\u30C8\u91D1\u7530\u6B63\u592A\u90CE\u3068\u5C71\u5F62\u3055\u3093",
        "KANEDA_THE_LONG_NAMED_PILOT",
        "ACE\uD83D\uDE80\uD83D\uDE80\uD83D\uDE80\uD83D\uDE80\uD83D\uDE80\uD83D\uDE80\uD83D\uDE80ROCKETEER",
        "KEI",
    };

    // Names: one line, at least LeaderboardPanel.NameMinSize, inside their
    // box (cut with an ellipsis when too long); rank and score columns lined
    // up row to row and never cut.
    static void LeaderboardChecks(ScreenFitRig rig, LeaderboardPanel panel)
    {
        float rankX = float.NaN, valueX = float.NaN;
        int names = 0, cut = 0;
        foreach (var t in panel.GetComponentsInChildren<Text>())
        {
            var row = t.transform.parent;
            if (row == null) continue;
            Rect glyphs; float fontPx; bool truncated;
            if (t.name == "Name")
            {
                names++;
                if (!rig.MeasureText(t, out glyphs, out fontPx, out truncated)) continue;
                Rect box = rig.PixelRect(t.rectTransform);
                if (t.fontSize < LeaderboardPanel.NameMinSize || t.resizeTextForBestFit)
                    rig.Fail("LEADERBOARD", "name " + row.name, "drawn at " + t.fontSize + " units, under the " + LeaderboardPanel.NameMinSize + " floor", glyphs);
                if (glyphs.xMax > box.xMax + 1f || glyphs.xMin < box.xMin - 1f)
                    rig.Fail("LEADERBOARD", "name " + row.name + " \"" + t.text + "\"", "runs out of its column", glyphs);
                if (glyphs.height > fontPx * 1.9f)
                    rig.Fail("LEADERBOARD", "name " + row.name, "wraps onto a second line", glyphs);
                if (t.text.EndsWith(LeaderboardPanel.Ellipsis)) cut++;
            }
            else if (t.name == "Rank" || t.name == "Value")
            {
                Rect box = rig.PixelRect(t.rectTransform);
                float x = t.name == "Rank" ? box.xMin : box.xMax;
                if (row.parent != null && row.parent.name == "PlayerRow") continue;   // the own row is inset differently
                ref float col = ref (t.name == "Rank" ? ref rankX : ref valueX);
                if (float.IsNaN(col)) col = x;
                else if (Mathf.Abs(col - x) > 1f)
                    rig.Fail("LEADERBOARD", t.name + " column", row.name + " is " + (x - col).ToString("F0") + "px out of line", box);
            }
        }
        if (names < 10) rig.Fail("STAGE", "leaderboard", "only " + names + " name cells");
        if (cut < 3) rig.Fail("LEADERBOARD", "names", "the long names were not cut with an ellipsis (" + cut + ")");
    }

    static void Credits(ScreenFitRig rig)
    {
        MenuStyler.StyleScene();
        rig.Sync();
        SafeAreaClamp.AttachAll("creditsS7");
        rig.Sync();   // it may change the canvas scaler
        SafeAreaClamp.AttachAll("creditsS7");
        DevBadge(rig);
    }

    // ---- space dock -------------------------------------------------------------------

    static void Dock(ScreenFitRig rig, int selected, bool unused)
    {
        DeveloperUnlocks.SetEnabled(false);
        for (int i = 0; i <= shopingShips.shipTotal; i++) PlayerPrefs.DeleteKey(ShipId.OwnedKey(i));
        foreach (int id in new[] { 8, 10 }) PlayerPrefs.SetString(ShipId.OwnedKey(id), "True");
        PlayerPrefs.SetInt(ShipId.SelectedKey, 8);
        PlayerPrefs.SetFloat(StarDustLedger.CurrencyKey, 2600f);
        for (int n = 1; n < ShipSkins.PerShip; n++) PlayerPrefs.DeleteKey(ShipSkins.OwnedKey(8, n));
        PlayerPrefs.SetInt(ShipSkins.OwnedKey(8, 1), 1);

        ShopSceneExtender.Build();
        MenuStyler.StyleScene();
        var shop = UnityEngine.Object.FindFirstObjectByType<shopingShips>();
        shop.SendMessage("Start");
        rig.Sync();
        var dock = SpaceDock.Instance;
        dock.Relayout();
        rig.Sync();
        dock.Relayout();
        foreach (var thruster in UnityEngine.Object.FindObjectsByType<ShipThruster>(FindObjectsSortMode.None)) thruster.SendMessage("Start");

        // every bay is a tap target (SpaceDock.Tap hit-tests the hull)
        for (int id = 1; id < dock.bays.Length; id++)
        {
            var bay = dock.bays[id];
            if (bay == null) continue;
            var sr = bay.hull;
            if (sr != null) rig.AddImportant("bay " + id + " " + ShipId.NameOf(id), rig.PixelRect(sr));
        }

        if (selected == -1 || selected == -2)
        {
            // the bay highest on screen / furthest to a side
            int best = 1;
            for (int id = 1; id < dock.bays.Length; id++)
            {
                if (dock.bays[id] == null) continue;
                Vector3 p = dock.bays[id].transform.position, q = dock.bays[best].transform.position;
                if (selected == -1 ? p.y > q.y + .01f : Mathf.Abs(p.x) > Mathf.Abs(q.x) + .01f || (Mathf.Abs(Mathf.Abs(p.x) - Mathf.Abs(q.x)) < .01f && p.y < q.y)) best = id;
            }
            selected = best;
            PlayerPrefs.SetString(ShipId.OwnedKey(selected), "True");
            PlayerPrefs.SetInt(ShipSkins.OwnedKey(selected, 1), 1);
            dock.RefreshStatuses();
        }
        if (selected > 0)
        {
            dock.Select(selected);
            dock.bays[selected].SnapPower();
            dock.popup.SkipAppear();
            dock.popup.SendMessage("LateUpdate");
            Canvas.ForceUpdateCanvases();
            PopupChecks(rig, dock, selected);
        }
        for (int k = 0; k < 20; k++)
            foreach (var thruster in UnityEngine.Object.FindObjectsByType<ShipThruster>(FindObjectsSortMode.None)) thruster.SendMessage("LateUpdate");
        DevBadge(rig);
    }

    // The ship card's own floors, on top of the generic ones (7 pt type,
    // 44 pt / 48 dp targets): its stats, prices and weapon line at least
    // PopupSmallPt, the name PopupNamePt, the action's label PopupActionPt;
    // the action, each swatch and the close button finger-sized; the drawn
    // swatch chips PopupChipGapPt apart; and the card itself beside its ship
    // (not over it), its tail pointing at it.
    public const float PopupSmallPt = 11f, PopupNamePt = 13f, PopupActionPt = 16f, PopupChipGapPt = 8f;

    static void PopupChecks(ScreenFitRig rig, SpaceDock dock, int selected)
    {
        var popup = dock.popup;
        float minTap = rig.device.MinTapPx;
        foreach (var t in popup.GetComponentsInChildren<Text>())
        {
            if (!t.enabled || string.IsNullOrWhiteSpace(t.text)) continue;
            Rect glyphs; float fontPx; bool truncated;
            if (!rig.MeasureText(t, out glyphs, out fontPx, out truncated)) continue;
            float pt = fontPx / rig.device.pxPerPt;
            float floor = t.name == "Title" && t.transform.parent.name == "Panel" ? PopupNamePt
                        : t.transform.parent.name == "Action" ? PopupActionPt : PopupSmallPt;
            if (pt < floor - .05f)
                rig.Fail("POPUP", "~DockPopup/" + t.transform.parent.name + "/" + t.name + " \"" + t.text + "\"",
                         "type " + pt.ToString("F1") + "pt is under the card's " + floor + "pt floor", glyphs);
        }
        var hits = new List<KeyValuePair<string, Rect>>();
        hits.Add(new KeyValuePair<string, Rect>("action", rig.HitRect(popup.ActionButton)));
        hits.Add(new KeyValuePair<string, Rect>("close", rig.HitRect(popup.CloseButton)));
        var chips = new List<Rect>();
        if (popup.SkinRowVisible)
            foreach (var w in popup.swatches)
                if (w.root.gameObject.activeInHierarchy)
                {
                    hits.Add(new KeyValuePair<string, Rect>(w.root.name, rig.HitRect(w.button)));
                    chips.Add(rig.PixelRect(w.body.rectTransform));
                }
        foreach (var kv in hits)
            if (kv.Value.width < minTap - .5f || kv.Value.height < minTap - .5f)
                rig.Fail("POPUP", "~DockPopup/" + kv.Key, "touch target " + (kv.Value.width / rig.device.pxPerPt).ToString("F0") + "x" +
                         (kv.Value.height / rig.device.pxPerPt).ToString("F0") + "pt is under " + (minTap / rig.device.pxPerPt).ToString("F0"), kv.Value);
        for (int i = 1; i < chips.Count; i++)
        {
            float gap = (chips[i].xMin - chips[i - 1].xMax) / rig.device.pxPerPt;
            if (gap < PopupChipGapPt)
                rig.Fail("POPUP", "~DockPopup/swatch chips " + (i - 1) + "-" + i, "drawn chips only " + gap.ToString("F1") + "pt apart", chips[i]);
        }
        // beside its ship: the card does not cover the hull, and the tail
        // points at it (the ship's x is within the card's width)
        Rect card = rig.PixelRect(popup.WorldRect);
        Rect hull = rig.TightPixelRect(dock.bays[selected].hull);
        float overlapY = Mathf.Min(card.yMax, hull.yMax) - Mathf.Max(card.yMin, hull.yMin);
        float overlapX = Mathf.Min(card.xMax, hull.xMax) - Mathf.Max(card.xMin, hull.xMin);
        if (overlapX > 1f && overlapY > hull.height * .15f)
            rig.Fail("POPUP", "~DockPopup/card", "covers its own ship (" + overlapY.ToString("F0") + "px of the hull's " + hull.height.ToString("F0") + ")", card);
        float shipX = rig.Pixel(dock.bays[selected].ship.position).x;
        if (shipX < card.xMin || shipX > card.xMax)
            rig.Fail("POPUP", "~DockPopup/card", "is not over its ship's column", card);
        rig.AddImportant("ship card", card);
    }

    // ---- gameplay scenes: the world -------------------------------------------------------

    // What the runtime bootstraps do for gameS1 / tutorialS5 before the first
    // frame: rails to the view's height, spawn line above it, backdrop, HUD.
    static HudStyler WorldBase(ScreenFitRig rig, int world, bool paused, bool tutorial)
    {
        buttonClicks.playerDied = false;
        score.pauseCounter = paused ? 3 : 0;
        moveBackGround.speed = .31f;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
        PlayerPrefs.SetString("HasDoneTut", "true");
        startMenu.youAreInTutorial = tutorial;
        var theme = WorldManager.Worlds[tutorial ? 0 : world];

        // buttonClicks / tutButtonClicks.Start and moveStarsBackground: the end
        // dialog, the pause icon and the quick actions' templates start hidden
        foreach (var name in new[] { "PopUpCanvas", "paused", "replayWhenPausedButton", "mainMenuWhenPausedButton" })
        {
            var hidden = SceneUtil.FindAny(name);
            if (hidden != null) hidden.SetActive(false);
        }

        foreach (var name in new[] { "leftPipe", "rightPipe" })
        {
            var go = SceneUtil.FindAny(name);
            if (go == null) continue;
            var rail = go.GetComponent<RailFit>() ?? go.AddComponent<RailFit>();
            rail.SendMessage("Start");
        }
        var spawn = SceneUtil.FindAny("Enemey_Item_Position");
        if (spawn != null) (spawn.GetComponent<SpawnAboveCamera>() ?? spawn.AddComponent<SpawnAboveCamera>()).SendMessage("Start");
        var destroyer = SceneUtil.FindAny("Destroyer");
        if (destroyer != null && !tutorial) (destroyer.GetComponent<BelowCameraDestroyer>() ?? destroyer.AddComponent<BelowCameraDestroyer>()).Reposition();

        if (!tutorial) WorldPainter.Apply(theme);
        var wb = new GameObject("~WorldBackdrop").AddComponent<WorldBackdrop>();
        wb.Show(theme.displayName, false);
        for (int i = 0; i < 30; i++) wb.Step(Dt);

        var styler = new GameObject("~HudStyler").AddComponent<HudStyler>();
        styler.SendMessage("Start");
        styler.SendMessage("Update");
        var actions = new GameObject("~PauseQuickActions").AddComponent<PauseQuickActions>();
        actions.SendMessage("Start");
        foreach (var name in new[] { "replayQuickAction", "leaveQuickAction" })
        {
            var go = SceneUtil.FindAny(name);
            if (go != null) go.SetActive(paused);
        }
        rig.Sync();
        SafeAreaClamp.AttachAll(tutorial ? "tutorialS5" : "gameS1");
        PlaceHudBand(rig, styler);
        return styler;
    }

    // HudStyler / PauseQuickActions read Screen.* themselves (another branch
    // owns them), so their published pure layout functions are applied here.
    static void PlaceHudBand(ScreenFitRig rig, HudStyler styler, bool check = true)
    {
        var screen = new Vector2(rig.W, rig.H);
        Rect safe = rig.device.Safe;
        var actions = UnityEngine.Object.FindFirstObjectByType<PauseQuickActions>();
        if (actions != null) actions.PlaceFor(safe, screen, Band(rig));
        var actionSafe = SceneUtil.FindAny("SafeArea");
        if (actionSafe != null) rig.Ignore(actionSafe.transform.root);
        if (styler != null && styler.HudRoot != null)
        {
            var hudCanvas = styler.HudRoot.GetComponentInParent<Canvas>().rootCanvas;
            float hudScale = hudCanvas.scaleFactor;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(styler.HudRoot);
            Vector2 hudSize = styler.HudRoot.rect.size;
            HudStyler.ComputeHudLayout(Band(rig), screen, hudScale, hudSize, out Vector2 at, out float fit, out bool stacked);
            styler.HudRoot.anchoredPosition = at;
            styler.HudRoot.localScale = new Vector3(fit, fit, 1f);
            HudStyler.StackedReadout = stacked ? HudStyler.HudScreenRect(Band(rig), screen, hudScale, hudSize) : default(Rect);
            rig.Ignore(styler.HudRoot);
            Canvas.ForceUpdateCanvases();
            if (check) BandChecks(rig, styler, fit);
        }
        Canvas.ForceUpdateCanvases();
    }

    static bool Overlap(Rect a, Rect b)
    {
        return Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > 1f && Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > 1f;
    }

    // The band itself (its own suites check its arithmetic): the quick
    // actions finger-sized, inside the band's right end; the read-out inside
    // its left end, at no less than TopBand.ReadoutMinScale, clear of them.
    static void BandChecks(ScreenFitRig rig, HudStyler styler, float fit)
    {
        var screen = new Vector2(rig.W, rig.H);
        var band = Band(rig);
        float minTap = rig.device.MinTapPx;
        Rect actions = PauseQuickActions.ScreenRectFor(band, screen);
        for (int slot = 0; slot < 2; slot++)
        {
            Rect b = PauseQuickActions.ButtonScreenRect(band, screen, slot);
            if (b.width < minTap - .5f || b.height < minTap - .5f)
                rig.Fail("BAND", "quick action " + (slot == 0 ? "replay" : "home"), "button " + (b.width / rig.device.pxPerPt).ToString("F1") +
                         (rig.device.ios ? "pt" : "dp") + " is under " + (rig.device.ios ? "44pt" : "48dp"), b);
            rig.AddImportant("quick action " + slot, b);
        }
        if (actions.xMax > band.right + 1f || actions.yMax > band.top + 1f)
            rig.Fail("BAND", "quick actions", "outside the band: " + actions, actions);
        Rect hud = rig.PixelRect(styler.HudRoot);
        rig.AddImportant("HUD read-out", hud);
        if (fit < TopBand.ReadoutMinScale - .001f)
            rig.Fail("BAND", "HUD read-out", "shrunk to " + fit.ToString("F2") + ", under TopBand.ReadoutMinScale " + TopBand.ReadoutMinScale, hud);
        if (hud.xMin < band.left - 1f || hud.yMax > band.top + 1f)
            rig.Fail("BAND", "HUD read-out", "outside the band: " + hud + " band " + band.left.ToString("F0") + " top " + band.top.ToString("F0"), hud);
        if (Overlap(hud, actions))
            rig.Fail("BAND", "HUD read-out", "runs into the quick actions: " + hud + " vs " + actions, hud);
        if (hud.xMax > band.right + 1f)
            rig.Fail("BAND", "HUD read-out", "runs past the band's right end", hud);
        // BOSS INCOMING's chip: inside the band, clear of the read-out and the actions
        var warn = BossWarningHud.ComputeLayout(rig.device.Safe, screen, hud, band);
        if (Overlap(warn.chip, hud) || Overlap(warn.chip, actions))
            rig.Fail("BAND", "boss chip", "overlaps the " + (Overlap(warn.chip, hud) ? "read-out" : "quick actions") + ": " + warn.chip, warn.chip);
        if (warn.chip.xMin < band.left - 1f || warn.chip.xMax > band.right + 1f)
            rig.Fail("BAND", "boss chip", "outside the band: " + warn.chip, warn.chip);
        // the read-out's type, at the size the band leaves it
        foreach (var t in styler.HudRoot.GetComponentsInChildren<Text>())
        {
            if (!t.IsActive() || string.IsNullOrWhiteSpace(t.text) || t.color.a < .05f) continue;
            Rect glyphs; float fontPx; bool truncated;
            if (!rig.MeasureText(t, out glyphs, out fontPx, out truncated)) continue;
            float pt = fontPx / rig.device.pxPerPt;
            if (pt < ScreenFitRig.MinTextPt)
                rig.Fail("BAND", "HUD read-out " + t.name + " \"" + t.text + "\"", "type " + pt.ToString("F1") + "pt under the " + ScreenFitRig.MinTextPt + " floor", glyphs);
        }
    }

    // The top band as the device lays it out: inside the rails, under its cutouts.
    static TopBand.Frame Band(ScreenFitRig rig)
    {
        return TopBand.FrameFor(rig.device.Safe, new Vector2(rig.W, rig.H), BossRails.InnerEdge, rig.device.Cutouts);
    }

    // Everything the top band can put on screen (read-out, quick actions,
    // boss chip, boss banner), screen px: what an overlay up there must clear.
    static List<KeyValuePair<string, Rect>> BandRects(ScreenFitRig rig, HudStyler styler, bool banner, bool chip = true)
    {
        var list = new List<KeyValuePair<string, Rect>>();
        var screen = new Vector2(rig.W, rig.H);
        var band = Band(rig);
        Rect hud = default(Rect);
        if (styler != null && styler.HudRoot != null)
        {
            hud = rig.PixelRect(styler.HudRoot);
            list.Add(new KeyValuePair<string, Rect>("HUD read-out", hud));
        }
        list.Add(new KeyValuePair<string, Rect>("quick actions", PauseQuickActions.ScreenRectFor(band, screen)));
        var warn = BossWarningHud.ComputeLayout(rig.device.Safe, screen, hud, band);
        if (chip) list.Add(new KeyValuePair<string, Rect>("boss chip", warn.chip));
        if (banner) list.Add(new KeyValuePair<string, Rect>("boss banner", warn.banner));
        return list;
    }

    // `what` (an overlay near the top) must not cover any part of the band.
    // `banner`: also BOSS INCOMING's banner (it is up for the warning's first
    // seconds; the codex toast dodges it at run time, and it is gone by the
    // time the boss's name plate comes up).
    static void ClearOfBand(ScreenFitRig rig, HudStyler styler, string what, Rect r, bool banner = true, bool chip = true)
    {
        foreach (var kv in BandRects(rig, styler, banner, chip))
        {
            Rect b = kv.Value;
            float x0 = Mathf.Max(b.xMin, r.xMin), x1 = Mathf.Min(b.xMax, r.xMax);
            float y0 = Mathf.Max(b.yMin, r.yMin), y1 = Mathf.Min(b.yMax, r.yMax);
            if (x1 > x0 + 1f && y1 > y0 + 1f)
                rig.Fail("BAND", what, "overlaps the top band's " + kv.Key + " by " + (x1 - x0).ToString("F0") + "x" + (y1 - y0).ToString("F0") + "px", Rect.MinMaxRect(x0, y0, x1, y1));
        }
    }

    // The world framing every gameplay shot is checked for.
    static void WorldChecks(ScreenFitRig rig, bool tutorial)
    {
        Rect view = rig.WorldView;
        foreach (var name in new[] { "leftPipe", "rightPipe" })
        {
            var go = SceneUtil.FindAny(name);
            var r = go != null ? go.GetComponent<Renderer>() : null;
            if (r == null) { rig.Fail("WORLD", name, "rail missing"); continue; }
            if (r.bounds.min.y > view.yMin || r.bounds.max.y < view.yMax)
                rig.Fail("WORLD", name, "rail does not span the view's height: " + r.bounds.min.y.ToString("F2") + ".." + r.bounds.max.y.ToString("F2") +
                         " of " + view.yMin.ToString("F2") + ".." + view.yMax.ToString("F2"), rig.PixelRect(r));
        }
        var spawn = SceneUtil.FindAny("Enemey_Item_Position");
        if (spawn != null && spawn.transform.position.y <= view.yMax)
            rig.Fail("WORLD", "spawn line", "enemies / pickups spawn inside the view: y " + spawn.transform.position.y.ToString("F2") + " <= top " + view.yMax.ToString("F2"));
        var destroyer = SceneUtil.FindAny("Destroyer");
        if (destroyer != null && !tutorial && destroyer.transform.position.y >= view.yMin - 1f)
            rig.Fail("WORLD", "destroyer", "the despawn strip is not clear of the view's bottom: y " + destroyer.transform.position.y.ToString("F2") + " vs bottom " + view.yMin.ToString("F2"));
        if (Portal.SpawnY - .9f <= view.yMax)
            rig.Fail("WORLD", "portal spawn", "a portal would appear inside the view: SpawnY " + Portal.SpawnY.ToString("F2") + " top " + view.yMax.ToString("F2"));
        if (BossActor.ArrivalY - BossConfig.BossWorldSize * .5f <= view.yMax)
            rig.Fail("WORLD", "boss arrival", "the boss would pop in inside the view");
        if (rig.HalfWidth < CameraFit.GameplayHalfWidth - .001f)
            rig.Fail("WORLD", "camera", "the play field's half-width " + rig.HalfWidth.ToString("F3") + " is under " + CameraFit.GameplayHalfWidth);

        var wb = UnityEngine.Object.FindFirstObjectByType<WorldBackdrop>();
        if (wb == null || wb.Current == null) rig.Fail("WORLD", "backdrop", "no backdrop set");
        else
        {
            var set = wb.Current;
            if (set.HalfWidth < rig.HalfWidth - .001f || set.HalfHeight < rig.HalfHeight - .001f)
                rig.Fail("WORLD", "backdrop", "laid out for " + set.HalfWidth.ToString("F2") + "x" + set.HalfHeight.ToString("F2") +
                         " but the view is " + rig.HalfWidth.ToString("F2") + "x" + rig.HalfHeight.ToString("F2"));
        }
    }

    // ---- tutorial ----------------------------------------------------------------------------

    static void Tutorial(ScreenFitRig rig, bool complete)
    {
        var styler = WorldBase(rig, 0, false, true);
        var skip = Add<TutorialSkip>("~TutorialSkip", false, true);
        rig.Sync();
        skip.SendMessage("Reposition");

        var font = (Font)typeof(TutorialSkip).GetMethod("SceneFont", PrivateStatic).Invoke(null, null);
        var speaker = RobotSpeaker.Create(font);
        rig.Sync();
        // the longest line of the script, fully spoken
        TutorialStep longest = TutorialScript.Steps[0];
        foreach (var s in TutorialScript.Steps)
            if (TutorialScript.ToPlainText(s.line).Length > TutorialScript.ToPlainText(longest.line).Length) longest = s;
        var hudPanel = styler != null ? styler.HudRoot : null;
        float lowest = rig.device.Safe.yMax;
        if (hudPanel != null) lowest = Mathf.Min(lowest, rig.PixelRect(hudPanel).yMin);
        if (skip.ButtonRect != null) lowest = Mathf.Min(lowest, rig.PixelRect(skip.ButtonRect).yMin);
        float sf = Mathf.Max(speaker.GetComponent<Canvas>().scaleFactor, .0001f);
        speaker.SetTopBlocked(Mathf.Round((rig.device.Safe.yMax - lowest) / sf));
        speaker.Say(TutorialScript.Speak(longest.line));
        speaker.CompleteLine();
        Call(speaker, "Fit", true);
        SnapRobot(speaker);

        if (complete)
        {
            skip.SetVisible(false);
            skip.SendMessage("Update");
            speaker.HideAll();
            speaker.gameObject.SetActive(false);
            var panel = TutorialCompletePanel.Show(1234.56f, 7);
            rig.Sync();
            if (panel != null)
            {
                Call(panel, "Fit");
                panel.Skip();
                Call(panel, "Fit");
            }
        }
        WorldChecks(rig, true);
        DevBadge(rig);
    }

    // RobotSpeaker animates in on unscaled time in Update; land on the end pose.
    static void SnapRobot(RobotSpeaker speaker)
    {
        foreach (var name in new[] { "robotShownAt", "bubbleShownAt" })
        {
            var f = typeof(RobotSpeaker).GetField(name, Private);
            if (f != null) f.SetValue(speaker, Time.unscaledTime - 10f);
        }
        foreach (var name in new[] { "lineSwapAt" })
        {
            var f = typeof(RobotSpeaker).GetField(name, Private);
            if (f != null) f.SetValue(speaker, -100f);
        }
        speaker.SendMessage("Update");
        var reveal = speaker.GetComponentInChildren<SpeechRevealEffect>(true);
        if (reveal != null) reveal.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
        Canvas.ForceUpdateCanvases();
    }

    // ---- the run ---------------------------------------------------------------------------------

    enum GameShot { Launch, Paused, Popups, Banner, BossIntro, BossFight, PortalPressure, Death }

    static void Game(ScreenFitRig rig, GameShot shot)
    {
        int world = shot == GameShot.BossIntro ? 1 : shot == GameShot.PortalPressure ? 3 : shot == GameShot.BossFight ? 2 : 0;
        var styler = WorldBase(rig, world, shot == GameShot.Paused, false);
        Rect view = rig.WorldView;

        // a stand-in for the player's ship, at the bottom of its reach
        var ship = new GameObject("~FitShip", typeof(SpriteRenderer));
        var shipArt = Resources.Load<Sprite>("Ships/ship8") ?? FirstSprite();
        ship.GetComponent<SpriteRenderer>().sprite = shipArt;
        ship.GetComponent<SpriteRenderer>().sortingOrder = 10;
        ship.transform.position = new Vector3(0f, movePlayer.ClampPlayerY(-100f), 0f);

        var go = SceneUtil.FindAny("goText");
        var goText = go != null ? go.GetComponent<Text>() : null;
        foreach (var legacy in new[] { "hypeText", "boostText" })
        {
            var l = SceneUtil.FindAny(legacy);
            if (l != null && shot != GameShot.Launch) l.SetActive(false);
        }
        if (goText != null) goText.gameObject.SetActive(shot == GameShot.Launch);

        switch (shot)
        {
            case GameShot.Launch:
                if (goText != null)
                {
                    goText.text = movePlayer.GoLabel;
                    goText.fontSize = movePlayer.CountdownFontSize(goText.fontSize, 0f);   // its largest
                }
                break;

            case GameShot.Paused:
                {
                    var icon = SceneUtil.FindAny("paused");
                    if (icon != null)
                    {
                        icon.SetActive(true);
                        var label = PausedLabel.AttachTo(icon);
                        if (label != null) label.Advance(1f);
                        Canvas.ForceUpdateCanvases();
                        foreach (var t in icon.GetComponentsInChildren<Text>())
                            ClearOfBand(rig, styler, "PAUSED label", rig.PixelRect(t.rectTransform));
                    }
                    break;
                }

            case GameShot.Popups:
                {
                    var hud = ScoreHud.Current ?? UnityEngine.Object.FindFirstObjectByType<ScoreHud>();
                    if (hud != null)
                    {
                        // popups at the four corners of the view and the middle: the clamps
                        hud.ShowPopup(5, new Vector3(view.xMin, view.yMax, 0f), RunScore.Source.Atom);
                        hud.ShowPopup(1250, new Vector3(view.xMax, view.yMax, 0f), RunScore.Source.Kill);
                        hud.ShowPopup(99999, new Vector3(view.xMin, view.yMin, 0f), RunScore.Source.Boss);
                        hud.ShowPopup(5, new Vector3(view.xMax, view.yMin, 0f), RunScore.Source.Dust);
                        hud.ShowPopup(250, new Vector3(0f, 0f, 0f), RunScore.Source.Teleport);
                        hud.ShowBanner(ScoreHud.DeathComboLabel(9999999), AkiraPalette.Magenta, 54, 1.4f, .05f);
                        hud.ShowBanner(ScoreHud.MegaDominoLabel, AkiraPalette.Amber, 54, 1.4f, .16f);
                        for (int i = 0; i < 12; i++) hud.StepPopups(Dt);
                    }
                    var toast = CodexToast.Build();
                    rig.Sync();
                    var entry = LongestCodexEntry();
                    if (entry != null)
                    {
                        toast.Enqueue(entry);
                        toast.ApplyAt(.6f);
                        Canvas.ForceUpdateCanvases();
                        var box = toast.transform.Find("Toast") as RectTransform;
                        if (box != null)
                        {
                            // at rest, with no boss warning up: clear of the read-out and the quick actions
                            ClearOfBand(rig, styler, "codex toast", rig.PixelRect(box), false, false);
                            // and with BOSS INCOMING's banner, then its chip, up: dropped in under them
                            var warn = BossWarningHud.ComputeLayout(rig.device.Safe, new Vector2(rig.W, rig.H),
                                styler != null && styler.HudRoot != null ? rig.PixelRect(styler.HudRoot) : default(Rect), Band(rig));
                            var canvas = toast.GetComponent<Canvas>();
                            float sf = Mathf.Max(canvas.scaleFactor, .0001f);
                            float safeTop = (rig.H - rig.device.Safe.yMax) / sf;
                            Vector2 rest = box.anchoredPosition;
                            foreach (var up in new[] { "banner", "chip" })
                            {
                                float top = CodexToast.TopOffset(safeTop, rig.H, sf, up == "banner" ? warn.banner : default(Rect),
                                                                 up == "chip" ? warn.chip : default(Rect));
                                box.anchoredPosition = new Vector2(0f, -top);
                                Canvas.ForceUpdateCanvases();
                                Rect r = rig.PixelRect(box);
                                ClearOfBand(rig, styler, "codex toast (boss " + up + " up)", r, up == "banner", up == "chip");
                                rig.AddImportant("codex toast (boss " + up + " up)", r);
                            }
                            box.anchoredPosition = rest;
                        }
                    }
                    break;
                }

            case GameShot.Banner:
                {
                    ShowBanner(rig, "PORTAL OPEN");
                    var portal = Portal.Spawn(new Color(.4f, .9f, 1f));
                    portal.transform.position = new Vector3(1.6f, view.yMax - 2.2f, 0f);
                    // the longest banner the game shows
                    string longest = PortalPressure.UrgeBanner;
                    foreach (var w in WorldManager.Worlds)
                    {
                        string s = w.displayName.ToUpperInvariant() + "  ONE MORE";
                        if (s.Length > longest.Length) longest = s;
                    }
                    ShowBanner(rig, longest);
                    break;
                }

            case GameShot.BossIntro:
            case GameShot.BossFight:
                {
                    // the boss with the longest name
                    BossDef boss = null;
                    foreach (var b in BossCatalog.All)
                        if (b != null && (boss == null || b.name.Length > boss.name.Length)) boss = b;
                    if (shot == GameShot.BossFight) boss = BossCatalog.ForWorld(world) ?? boss;
                    var actor = new GameObject("~FitBoss", typeof(SpriteRenderer));
                    var sr = actor.GetComponent<SpriteRenderer>();
                    sr.sprite = boss != null ? BossArt.Body(boss, 0) : null;
                    sr.sortingOrder = 6;
                    actor.transform.position = new Vector3(0f, BossConfig.BossY, 0f);
                    if (sr.sprite != null)
                    {
                        float k = BossConfig.BossWorldSize / Mathf.Max(sr.sprite.bounds.size.x, sr.sprite.bounds.size.y);
                        actor.transform.localScale = Vector3.one * k;
                    }
                    if (shot == GameShot.BossIntro && boss != null)
                    {
                        var ui = BossIntroUI.Play(boss);
                        rig.Sync();
                        float hold = Mathf.Max(.1f, BossConfig.NameBreakAt - .15f);
                        for (int i = 0; i < 100000 && ui.Clock < hold; i++) ui.Step(Dt);
                        Canvas.ForceUpdateCanvases();
                        for (int i = 0; i < ui.LetterCount; i++)
                        {
                            rig.AddImportant("boss name letter " + i, rig.PixelRect(ui.PieceAt(i)));
                            ClearOfBand(rig, styler, "boss name letter " + i, rig.PixelRect(ui.PieceAt(i)), false, false);
                        }
                    }
                    break;
                }

            case GameShot.PortalPressure:
                {
                    PortalPressureShot(rig, styler, world);
                    break;
                }

            case GameShot.Death:
                {
                    buttonClicks.playerDied = true;
                    foreach (var name in new[] { "replayQuickAction", "leaveQuickAction" })
                    {
                        var q = SceneUtil.FindAny(name);
                        if (q != null) q.SetActive(true);
                    }
                    var canvas = SceneUtil.FindAny("PopUpCanvas");
                    canvas.SetActive(true);
                    // the scene's old speed Texts: the score figure, and a spare the panel switches off
                    var best = SceneUtil.FindAny("playerDeadHighestSpeed").GetComponent<Text>();
                    var run = SceneUtil.FindAny("deathSpeedReachedThisRoundText").GetComponent<Text>();
                    var dust = SceneUtil.FindAny("playerDeadHighScore").GetComponent<Text>();
                    var replay = SceneUtil.FindAny("Replay").GetComponent<Button>();
                    var menu = SceneUtil.FindAny("MainMenu").GetComponent<Button>();
                    var parts = new RunScore.Breakdown
                    {
                        distance = 999999, kills = 999999, dust = 99999, atoms = 9999, teleports = 9999,
                        bosses = 99999, worlds = 99999, deathCombo = 99999, deathComboKills = 99,
                        killCount = 999, dustCount = 999, atomCount = 99, teleportCount = 99, bossCount = 9, worldCount = 9,
                    };
                    var viewPanel = DeathPanelView.Build(canvas.transform, best, run, dust, replay, menu, new DeathPanelView.Results
                    {
                        score = 9999999, bestScore = 9999999, newBest = true, ranked = true, parts = parts,
                        dustAtStart = 99987.65f, dustWon = 12.34f,
                    });
                    rig.Sync();
                    PlaceHudBand(rig, styler, false);
                    Call(viewPanel, "Fit");
                    viewPanel.Skip();
                    Call(viewPanel, "Fit");
                    break;
                }
        }
        Canvas.ForceUpdateCanvases();
        WorldChecks(rig, false);
        DevBadge(rig);
    }

    // An open portal kept waiting past its grace (PortalPressure): the portal
    // at its station, the ENTER THE PORTAL banner, PORTAL DANGER's chip at a
    // two-digit Level and the glows down the lane's edges (PortalPressureHud),
    // with a codex toast up as well. The HUD reads Screen.* itself (as
    // BossWarningHud does), so its pure placement is applied for the device.
    static void PortalPressureShot(ScreenFitRig rig, HudStyler styler, int world)
    {
        var screen = new Vector2(rig.W, rig.H);
        var band = Band(rig);
        int destination = (world + 1) % WorldManager.Worlds.Length;

        var portal = Portal.Spawn(WorldManager.Worlds[destination].portalColor);
        // at the far end of its drift, at its station
        portal.transform.position = new Vector3(Portal.HomeMaxX + Portal.DriftHalf, Portal.StationY, 0f);
        float reach = Portal.HomeMaxX + Portal.DriftHalf + Portal.Radius;
        if (reach > BossRails.InnerEdge)
            rig.Fail("WORLD", "portal", "at the far end of its drift its edge reaches x " + reach.ToString("F2") + ", over the rails' inner edge " + BossRails.InnerEdge.ToString("F2"));
        if (Portal.StationY + Portal.Radius > rig.WorldView.yMax || Portal.StationY - Portal.Radius < rig.WorldView.yMin)
            rig.Fail("WORLD", "portal", "its station y " + Portal.StationY.ToString("F2") + " is not inside the view");

        // a Level that puts two digits on the chip (its widest)
        PortalPressure.Open(destination);
        PortalPressure.Tick(PortalPressure.GraceSeconds + PortalPressure.LevelSeconds * 11.5f);
        ShowBanner(rig, PortalPressure.UrgeBanner);

        var hud = PortalPressureHud.Ensure();
        rig.Sync();
        hud.Refresh();   // text, colours, beat (and a layout for the editor's own screen ...)
        var canvas = hud.GetComponent<Canvas>();
        hud.Layout(band, screen, Mathf.Max(canvas.scaleFactor, .0001f));   // ... replaced by the device's
        Canvas.ForceUpdateCanvases();

        if (!hud.Showing || hud.Chip.text != PortalPressureHud.ChipLabel(PortalPressure.DangerNumber))
            rig.Fail("STAGE", "portal danger chip", "not showing (" + (hud.Chip != null ? hud.Chip.text : "null") + ")");
        Rect chip = rig.PixelRect(hud.Chip.rectTransform);
        rig.AddImportant("portal danger chip", chip);
        if (chip.xMin < band.left - 1f || chip.xMax > band.right + 1f || chip.yMax > band.top + 1f)
            rig.Fail("BAND", "portal danger chip", "outside the band between the rails: " + chip + " band " + band.left.ToString("F0") + ".." +
                     band.right.ToString("F0") + " top " + band.top.ToString("F0"), chip);
        // (safe area, cutouts, corners: the generic checks, through AddImportant;
        // its text's fit and size: the generic text checks)
        // no boss warning is up while a portal is open: the read-out and the quick actions
        ClearOfBand(rig, styler, "portal danger chip", chip, false, false);

        foreach (var glow in new[] { hud.LeftGlow, hud.RightGlow })
        {
            Rect g = rig.PixelRect(glow.rectTransform);
            if (g.width < 1f || g.xMin < band.left - 1f || g.xMax > band.right + 1f)
                rig.Fail("BAND", "portal " + glow.name, "not inside the rails: " + g + " band " + band.left.ToString("F0") + ".." + band.right.ToString("F0"), g);
            if (g.yMin > 1f || g.yMax < rig.H - 1f)
                rig.Fail("BAND", "portal " + glow.name, "does not run the screen's height: " + g, g);
        }

        // NEW CODEX ENTRY while the chip is up: dropped in under it
        var toast = CodexToast.Build();
        rig.Sync();
        var entry = LongestCodexEntry();
        if (entry != null)
        {
            toast.Enqueue(entry);
            toast.ApplyAt(.6f);
            Canvas.ForceUpdateCanvases();
            var box = toast.transform.Find("Toast") as RectTransform;
            if (box != null)
            {
                Rect r = rig.PixelRect(box);
                rig.AddImportant("codex toast (portal chip up)", r);
                ClearOfBand(rig, styler, "codex toast (portal chip up)", r, false, false);
                if (r.Overlaps(chip) && Rect.MinMaxRect(Mathf.Max(r.xMin, chip.xMin), Mathf.Max(r.yMin, chip.yMin),
                                                        Mathf.Min(r.xMax, chip.xMax), Mathf.Min(r.yMax, chip.yMax)).height > 1f)
                    rig.Fail("OVERLAP", "codex toast (portal chip up)", "covers the PORTAL DANGER chip", r);
                // nor the ENTER THE PORTAL card (WorldBanner): on a short phone
                // the toast, dropped under a stacked read-out, reaches it
                var bannerObj = (WorldBanner)typeof(WorldBanner).GetField("instance", PrivateStatic).GetValue(null);
                var card = bannerObj != null ? (RectTransform)Field(bannerObj, "card") : null;
                if (card == null || !card.gameObject.activeInHierarchy)
                    rig.Fail("STAGE", "ENTER THE PORTAL", "the banner card is not up");
                else
                {
                    Rect cardPx = rig.PixelRect(card);
                    if (Rect.MinMaxRect(Mathf.Max(r.xMin, cardPx.xMin), Mathf.Max(r.yMin, cardPx.yMin),
                                        Mathf.Min(r.xMax, cardPx.xMax), Mathf.Min(r.yMax, cardPx.yMax)) is Rect o && o.width > 1f && o.height > 1f)
                        rig.Fail("OVERLAP", "codex toast (portal chip up)", "runs into the ENTER THE PORTAL card " + cardPx, r);
                }
            }
        }
    }

    static Sprite FirstSprite()
    {
        foreach (var s in Resources.LoadAll<Sprite>("Ships")) return s;
        return null;
    }

    static CodexEntry LongestCodexEntry()
    {
        CodexEntry longest = null;
        foreach (var e in CodexCatalogue.All)
            if (e != null && e.name != null && (longest == null || e.name.Length > longest.name.Length)) longest = e;
        return longest;
    }

    // WorldBanner runs on a coroutine; put its card up on the rest pose.
    static void ShowBanner(ScreenFitRig rig, string message)
    {
        var f = typeof(WorldBanner).GetField("instance", PrivateStatic);
        var banner = (WorldBanner)f.GetValue(null);
        if (banner == null)
        {
            banner = (WorldBanner)typeof(WorldBanner).GetMethod("Build", PrivateStatic).Invoke(null, null);
            f.SetValue(null, banner);
        }
        rig.Sync();
        var label = (Text)Field(banner, "label");
        var slab = (Image)Field(banner, "slab");
        var card = (RectTransform)Field(banner, "card");
        label.text = message;
        card.gameObject.SetActive(true);
        slab.rectTransform.sizeDelta = new Vector2(Mathf.Max(420f, label.preferredWidth + 140f), 88f);
        Call(banner, "Apply", new Vector4(1f, 0f, 1f, 0f));
        Canvas.ForceUpdateCanvases();
    }
}
