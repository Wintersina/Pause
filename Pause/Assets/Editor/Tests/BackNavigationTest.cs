using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// "Back undoes one level", everywhere (BackNavigator):
//   dock: a tapped ship is deselected (nothing saved), then home
//   home: codex detail -> grid -> closed -> quit armed -> quit
//   Options: the leaderboard panel closes first, then home
//   credits / gameS1 / tutorial: home
// plus: one Escape press is consumed by exactly one handler, only one script
// reads Escape, and iOS never quits or shows a quit affordance.
//
// Scene loads and Application.Quit go through BackNavigator's hooks, stubbed
// here to record what would have happened.
public static class BackNavigationTest
{
    static int fails;
    static readonly List<string> loads = new List<string>();
    static int quits;
    static float now;

    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BACK] PASS  " : "[BACK] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run()
    {
        TestHarness.Exit(Execute());
    }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            Stub(RuntimePlatform.Android);
            EscapeIsConsumedOnce();
            OnlyTheNavigatorReadsEscape();
            Dock();
            HomeCodexThenQuit();
            HomeDoubleBack();
            IosHasNoQuit();
            OptionsLeaderboard();
            Credits();
            InRun();
        }
        finally
        {
            BackNavigator.ResetHooks();
            BackNavigator.Disarm();
            LeaderboardPanel.Close();
            if (BackQuitToast.Current != null) Object.DestroyImmediate(BackQuitToast.Current.gameObject);
        }
        Debug.Log("[BACK] failures: " + fails);
        return fails;
    }

    // ------------------------------------------------------------- helpers

    static void Stub(RuntimePlatform platform)
    {
        loads.Clear();
        quits = 0;
        BackNavigator.LoadScene = s => loads.Add(s);
        BackNavigator.QuitApplication = () => quits++;
        BackNavigator.Platform = () => platform;
        BackNavigator.Clock = () => now;
        BackNavigator.Disarm();
    }

    static string LastLoad { get { return loads.Count > 0 ? loads[loads.Count - 1] : null; } }

    static void Call(Component c, string method)
    {
        var m = c.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (m == null) return;
        try { m.Invoke(c, null); }
        catch (TargetInvocationException e) { throw e.InnerException; }
    }

    // ------------------------------------------------------------- stack

    class Owner { }

    static void EscapeIsConsumedOnce()
    {
        EditorSceneLoader.Open("creditsS7", OpenSceneMode.Single);
        Stub(RuntimePlatform.Android);
        var lower = new Owner();
        var upper = new Owner();
        int lowerHits = 0, upperHits = 0;
        bool upperWants = true;
        BackNavigator.Register(lower, () => { lowerHits++; return true; });
        BackNavigator.Register(upper, () => { if (!upperWants) return false; upperHits++; return true; });

        Check("the newest layer is on top", BackNavigator.Top == upper);
        Check("Escape in frame 100 is handled", BackNavigator.HandleEscape(100));
        Check("...by exactly one handler, the top one", upperHits == 1 && lowerHits == 0 && loads.Count == 0);
        Check("a second Escape read in the same frame is ignored", !BackNavigator.HandleEscape(100));
        Check("...and nothing else reacted to it", upperHits == 1 && lowerHits == 0 && loads.Count == 0);
        BackNavigator.HandleEscape(101);
        Check("the next frame's press is handled once more", upperHits == 2 && lowerHits == 0);

        upperWants = false;
        BackNavigator.HandleEscape(102);
        Check("a layer that passes hands the press to the one below", upperHits == 2 && lowerHits == 1 && loads.Count == 0);

        BackNavigator.Unregister(lower);
        BackNavigator.Unregister(upper);
        BackNavigator.HandleEscape(103);
        Check("with no layers the scene root runs (credits -> home)", LastLoad == BackNavigator.HomeScene && loads.Count == 1);

        var dead = new GameObject("deadLayer");
        BackNavigator.Register(dead, () => { lowerHits += 100; return true; });
        Object.DestroyImmediate(dead);
        BackNavigator.Back();
        Check("a destroyed layer drops out", lowerHits == 1 && loads.Count == 2);
    }

    static void OnlyTheNavigatorReadsEscape()
    {
        var readers = new List<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(Application.dataPath, "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            foreach (var line in File.ReadAllLines(file))
            {
                string code = line.Trim();
                if (code.StartsWith("//")) continue;
                if (code.Contains("KeyCode.Escape")) readers.Add(Path.GetFileName(file));
            }
        }
        Check("only BackNavigatorRunner reads Escape (" + string.Join(", ", readers) + ")",
              readers.Count == 1 && readers[0] == "BackNavigatorRunner.cs");
    }

    // ------------------------------------------------------------- dock

    static SpaceDock FreshDock()
    {
        foreach (var old in Object.FindObjectsByType<SpaceDock>(FindObjectsSortMode.None))
            Object.DestroyImmediate(old.gameObject);
        ShopSceneExtender.Build();
        var dock = SpaceDock.Instance;
        if (dock != null) dock.Relayout();
        return dock;
    }

    static void Dock()
    {
        for (int i = 0; i <= shopingShips.shipTotal; i++) PlayerPrefs.DeleteKey(ShipId.OwnedKey(i));
        PlayerPrefs.SetString(ShipId.OwnedKey(6), "True");
        PlayerPrefs.SetString(ShipId.OwnedKey(11), "True");
        PlayerPrefs.SetInt(ShipId.SelectedKey, 11);
        DeveloperUnlocks.SetEnabled(false);

        EditorSceneLoader.Open("shopS6", OpenSceneMode.Single);
        Stub(RuntimePlatform.Android);
        var dock = FreshDock();
        Check("dock: builds", dock != null);
        if (dock == null) return;
        Check("dock: is a Back layer", BackNavigator.IsRegistered(dock));
        Check("dock: the equipped ship (11) is marked", dock.bays[11].Equipped && !dock.bays[6].Equipped);
        shopingShips.shipNumber = 3;
        shopingShips.LastShipSelected = 2;

        dock.Tap(dock.bays[6].transform.position);
        Check("dock: tapping ship 6 selects it and opens its popup",
              dock.Selected == 6 && dock.popup.Visible && dock.bays[6].Powered);

        BackNavigator.Back();
        Check("dock: Back closes the popup", !dock.popup.Visible);
        Check("dock: Back powers ship 6 down and clears the selection", dock.Selected == 0 && !dock.bays[6].Powered);
        Check("dock: the equipped ship is unchanged", ShipId.Equipped() == 11);
        Check("dock: spawnShip is unchanged (" + PlayerPrefs.GetInt(ShipId.SelectedKey) + ")",
              PlayerPrefs.GetInt(ShipId.SelectedKey) == 11);
        Check("dock: the equipped ship is shown as the selection again",
              dock.bays[11].Equipped && !dock.bays[6].Equipped);
        Check("dock: the selection statics are restored",
              shopingShips.shipNumber == 3 && shopingShips.LastShipSelected == 2 && rotateRight.shipSelected == 0);
        Check("dock: the first Back stays in the dock", loads.Count == 0);
        Check("dock: LIFT-OFF after Back flies the equipped ship", dock.LiftOffIndex() == 11);

        BackNavigator.Back();
        Check("dock: the next Back goes home", LastLoad == BackNavigator.HomeScene && loads.Count == 1);
        Check("dock: and still saved nothing", PlayerPrefs.GetInt(ShipId.SelectedKey) == 11);

        // The on-screen BACK (menuButton.back, UnityEvent-wired) is the same press.
        var menu = Object.FindFirstObjectByType<menuButton>();
        Check("dock: scene has its BACK button (menuButton)", menu != null);
        if (menu != null)
        {
            loads.Clear();
            dock.Tap(dock.bays[6].transform.position);
            menu.back();
            Check("dock: on-screen BACK also only deselects", dock.Selected == 0 && !dock.popup.Visible && loads.Count == 0);
            menu.back();
            Check("dock: on-screen BACK again goes home", LastLoad == BackNavigator.HomeScene);
        }

        // During the launch animation Back is swallowed: no skip, no leaving.
        loads.Clear();
        dock.Tap(dock.bays[6].transform.position);
        dock.LiftOff();
        Check("dock: LIFT-OFF launches the selection", dock.Launching && PlayerPrefs.GetInt(ShipId.SelectedKey) == 6);
        BackNavigator.Back();
        Check("dock: Back during the launch does nothing", dock.Launching && loads.Count == 0 &&
              PlayerPrefs.GetInt(ShipId.SelectedKey) == 6);

        Object.DestroyImmediate(dock.gameObject);
        Check("dock: a destroyed dock is no longer a layer", !BackNavigator.IsRegistered(dock));
    }

    // ------------------------------------------------------------- home

    static void HomeCodexThenQuit()
    {
        EditorSceneLoader.Open("startS4", OpenSceneMode.Single);
        Stub(RuntimePlatform.Android);
        now = 50f;
        var entry = Object.FindFirstObjectByType<CodexHomeButton>();
        Check("home: has the CODEX button", entry != null);
        if (entry == null) return;
        entry.Build();
        entry.OpenCodex();
        var panel = CodexPanel.Current;
        Check("codex: opens", panel != null && panel.IsOpen);
        if (panel == null) return;
        panel.SkipAnimations();
        panel.ShowDetail(Codex.Entries[0]);
        panel.SkipAnimations();
        Check("codex: detail shown", panel.InDetail);

        BackNavigator.Back();
        panel.SkipAnimations();
        Check("codex: Back goes detail -> grid", !panel.InDetail && panel.IsOpen);
        BackNavigator.Back();
        panel.SkipAnimations();
        Check("codex: Back again closes it", !panel.IsOpen && !panel.gameObject.activeSelf);
        Check("codex: ...without arming quit", !BackNavigator.QuitIsArmed && quits == 0);

        BackNavigator.Back();
        Check("codex: Back with it closed arms quitting", BackNavigator.QuitIsArmed && quits == 0 && loads.Count == 0);
        BackNavigator.Disarm();
    }

    static void HomeDoubleBack()
    {
        EditorSceneLoader.Open("startS4", OpenSceneMode.Single);
        Stub(RuntimePlatform.Android);
        now = 10f;
        BackNavigator.Back();
        Check("home: first Back only arms quitting", BackNavigator.QuitIsArmed && quits == 0 && loads.Count == 0);
        var toast = BackQuitToast.Current;
        Check("home: first Back shows the 'press back again' toast",
              toast != null && toast.Showing && toast.ShownText == BackQuitToast.Message);
        if (toast != null)
        {
            var label = toast.GetComponentInChildren<UnityEngine.UI.Text>(true);
            Check("home: the toast never blocks input",
                  toast.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null && label != null && !label.raycastTarget);
        }

        now = 11.5f;
        BackNavigator.Back();
        Check("home: a second Back within 2s quits", quits == 1 && !BackNavigator.QuitIsArmed);

        now = 20f;
        BackNavigator.Back();
        now = 22.5f;
        Check("home: arming expires after the window", !BackNavigator.QuitIsArmed);
        BackNavigator.Back();
        Check("home: a late second Back re-arms instead of quitting", quits == 1 && BackNavigator.QuitIsArmed);
        now = 23f;
        BackNavigator.Back();
        Check("home: ...and the next one quits", quits == 2);

        // The deliberate Quit button quits straight away on Android.
        var menu = Object.FindFirstObjectByType<startMenu>();
        if (menu != null)
        {
            menu.quit();
            Check("home: the Quit button quits at once", quits == 3);
            Call(menu, "Start");
            var quitButton = SceneUtil.FindAny("QuitButton");
            Check("home (Android): the Quit button is shown", quitButton != null && quitButton.activeSelf);
        }
        BackNavigator.Disarm();
    }

    static void IosHasNoQuit()
    {
        EditorSceneLoader.Open("startS4", OpenSceneMode.Single);
        Stub(RuntimePlatform.IPhonePlayer);
        if (BackQuitToast.Current != null) Object.DestroyImmediate(BackQuitToast.Current.gameObject);
        now = 5f;
        Check("iOS: quitting is not allowed", !BackNavigator.QuitAllowed);
        BackNavigator.Back();
        now = 5.5f;
        BackNavigator.Back();
        Check("iOS: Back on home never quits or arms", quits == 0 && !BackNavigator.QuitIsArmed && loads.Count == 0);
        Check("iOS: no quit toast", BackQuitToast.Current == null || !BackQuitToast.Current.Showing);
        var menu = Object.FindFirstObjectByType<startMenu>();
        Check("iOS: home has its startMenu", menu != null);
        if (menu == null) return;
        Call(menu, "Start");
        var quitButton = SceneUtil.FindAny("QuitButton");
        Check("iOS: the home Quit button is hidden", quitButton != null && !quitButton.activeSelf);
        menu.quit();
        Check("iOS: the Quit action does nothing", quits == 0);
    }

    // ------------------------------------------------------------- other screens

    static void OptionsLeaderboard()
    {
        EditorSceneLoader.Open("leaderboardS3", OpenSceneMode.Single);
        Stub(RuntimePlatform.Android);
        var service = new LeaderboardService(FakeLeaderboards.Demo(), () => 0f) { Ios = false };
        LeaderboardService.Instance = service;
        var panel = LeaderboardPanel.Open(service, null, new Vector2(1080f, 2340f), new Rect(0f, 63f, 1080f, 2202f));
        Check("options: leaderboard panel opens as the top Back layer", LeaderboardPanel.IsOpen && BackNavigator.Top == (object)panel);
        BackNavigator.Back();
        Check("options: Back closes the leaderboard panel first", !LeaderboardPanel.IsOpen && loads.Count == 0);
        BackNavigator.Back();
        Check("options: the next Back goes home", LastLoad == BackNavigator.HomeScene && loads.Count == 1);

        var menu = Object.FindFirstObjectByType<menuButton>();
        if (menu != null)
        {
            LeaderboardPanel.Open(service, null, new Vector2(1080f, 2340f), new Rect(0f, 63f, 1080f, 2202f));
            loads.Clear();
            menu.back();
            Check("options: on-screen BACK also closes the panel first", !LeaderboardPanel.IsOpen && loads.Count == 0);
            menu.back();
            Check("options: on-screen BACK again goes home", LastLoad == BackNavigator.HomeScene);
        }
        LeaderboardService.Instance = null;
    }

    static void Credits()
    {
        EditorSceneLoader.Open("creditsS7", OpenSceneMode.Single);
        Stub(RuntimePlatform.Android);
        BackNavigator.Back();
        Check("credits: Back goes home", LastLoad == BackNavigator.HomeScene);
        var credits = Object.FindFirstObjectByType<everythingCredit>();
        Check("credits: scene has its BACK target", credits != null);
        if (credits != null)
        {
            loads.Clear();
            credits.back();
            Check("credits: on-screen BACK goes home", LastLoad == BackNavigator.HomeScene && loads.Count == 1);
        }
    }

    static void InRun()
    {
        EditorSceneLoader.Open("gameS1", OpenSceneMode.Single);
        Stub(RuntimePlatform.Android);
        // Flying: Back does what the Home quick action (mainMenuButton) does.
        buttonClicks.playerDied = false;
        Time.timeScale = 0f;
        BackNavigator.Back();
        Check("run: Back while flying goes home", LastLoad == BackNavigator.HomeScene);
        Check("run: ...clearing the frozen clock like Home does", Time.timeScale == 1f);

        // Dead: same as MENU.
        loads.Clear();
        buttonClicks.playerDied = true;
        Time.timeScale = 0f;
        BackNavigator.Back();
        Check("run: Back on the death panel goes home", LastLoad == BackNavigator.HomeScene);
        Check("run: ...like MENU (run state cleared)",
              !buttonClicks.playerDied && Time.timeScale == 1f);

        var clicks = Object.FindFirstObjectByType<buttonClicks>();
        if (clicks != null)
        {
            loads.Clear();
            buttonClicks.playerDied = true;
            clicks.mainMenuButton();
            Check("run: MENU / Home takes the same path", LastLoad == BackNavigator.HomeScene && !buttonClicks.playerDied);
        }

        EditorSceneLoader.Open("tutorialS5", OpenSceneMode.Single);
        Stub(RuntimePlatform.Android);
        PlayerPrefs.SetString("HasDoneTut", "false");
        startMenu.youAreInTutorial = true;
        BackNavigator.Back();
        Check("tutorial: Back goes home (like its Home action)", LastLoad == BackNavigator.HomeScene);
        Check("tutorial: ...and leaves it unfinished", PlayerPrefs.GetString("HasDoneTut") == "false" &&
              !startMenu.youAreInTutorial);
    }
}
