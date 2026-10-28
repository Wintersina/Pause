using System.IO;
using UnityEngine;

// Menu screen changes (MenuNav): leaving a menu scene goes through LoadSceneAsync (the tap frame no
// longer blocks on the whole destination), leaving a run stays synchronous, a second tap while one is
// pending is ignored, and the menu backdrop no longer runs a full-heap sweep on every page change.
public static class MenuNavTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[MNAV] PASS  " : "[MNAV] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static int Execute()
    {
        fails = 0;
        try
        {
            Routing();
            Sources();
            Sweep();
        }
        finally { MenuNav.Reset(); MenuBackdrop.ResetSweepForTests(); }
        Debug.Log("[MNAV] failures: " + fails);
        return fails;
    }

    static void Routing()
    {
        MenuNav.Reset();
        MenuNav.OnComplete = (op, done) => { };
        var asyncNames = new System.Collections.Generic.List<string>();
        var syncNames = new System.Collections.Generic.List<string>();
        MenuNav.Playing = () => true;
        MenuNav.StartAsync = n => { asyncNames.Add(n); return new AsyncOperation(); };
        MenuNav.LoadSync = n => syncNames.Add(n);

        MenuNav.ActiveScene = () => "startS4";
        MenuNav.Load("shopS6");
        Check("home -> dock starts an async load", asyncNames.Count == 1 && asyncNames[0] == "shopS6" && syncNames.Count == 0 && MenuNav.Loading);
        MenuNav.Load("leaderboardS3");
        Check("a second tap while one load is pending is ignored", asyncNames.Count == 1 && syncNames.Count == 0);

        MenuNav.Reset();
        MenuNav.OnComplete = (op, done) => { };
        asyncNames.Clear();
        MenuNav.Playing = () => true;
        MenuNav.StartAsync = n => { asyncNames.Add(n); return new AsyncOperation(); };
        MenuNav.LoadSync = n => syncNames.Add(n);
        MenuNav.ActiveScene = () => "gameS1";
        MenuNav.Load("startS4");
        Check("leaving a run stays synchronous (the run would keep simulating during an async load)",
              asyncNames.Count == 0 && syncNames.Count == 1 && !MenuNav.Loading);

        MenuNav.Reset();
        syncNames.Clear();
        MenuNav.Playing = () => false;
        MenuNav.LoadSync = n => syncNames.Add(n);
        MenuNav.ActiveScene = () => "startS4";
        MenuNav.Load("shopS6");
        Check("out of play it is a plain LoadScene", syncNames.Count == 1);

        MenuNav.Reset();
        MenuNav.Playing = () => true;
        MenuNav.ActiveScene = () => "leaderboardS3";
        MenuNav.StartAsync = n => null;
        MenuNav.Load("nope");
        Check("a failed start does not wedge the guard", !MenuNav.Loading);

        // the real BackNavigator default goes through MenuNav
        MenuNav.Reset();
        MenuNav.OnComplete = (op, done) => { };
        MenuNav.Playing = () => true;
        MenuNav.ActiveScene = () => "creditsS7";
        int started = 0;
        MenuNav.StartAsync = n => { started++; return new AsyncOperation(); };
        BackNavigator.ResetHooks();
        BackNavigator.LoadScene(BackNavigator.OptionsScene);
        Check("BackNavigator's default load goes through MenuNav (async from a menu)", started == 1);
        BackNavigator.ResetHooks();
        MenuNav.Reset();
    }

    static string Src(string rel) { return File.ReadAllText("Assets/Scripts/" + rel); }

    static void Sources()
    {
        foreach (var f in new[] { "Menu/startMenu.cs", "Core/leaderboard.cs", "Core/menuButton.cs", "Shop/SpaceDock.cs" })
            Check(f + " has no direct SceneManager.LoadScene (menu changes go through MenuNav)", !Src(f).Contains("SceneManager.LoadScene("));
        Check("startMenu opens the dock and Options through MenuNav",
              Src("Menu/startMenu.cs").Contains("MenuNav.Load(\"shopS6\")") && Src("Menu/startMenu.cs").Contains("MenuNav.Load(\"leaderboardS3\")"));
        string bd = Src("Worlds/Backdrop/MenuBackdrop.cs");
        int sweeps = 0, at = 0;
        while ((at = bd.IndexOf("Resources.UnloadUnusedAssets()", at)) >= 0) { sweeps++; at++; }
        Check("MenuBackdrop has exactly one sweep, gated by SweepDue (no sweep in Awake's success path)",
              sweeps == 1 && bd.Contains("if (Build(pick)) { SweepIfProbed(); return; }"));
    }

    static void Sweep()
    {
        MenuBackdrop.ResetSweepForTests();
        MenuBackdropSelection.ResetForTests();
        MenuBackdropSelection.Roll();
        Check("a fresh roll (probed every sky) is due one sweep", MenuBackdrop.SweepDue());
        Check("the same pick kept page to page is not swept again", !MenuBackdrop.SweepDue() && !MenuBackdrop.SweepDue());
        MenuBackdropSelection.Roll();
        Check("the next roll is due one again", MenuBackdrop.SweepDue());
        MenuBackdropSelection.ResetForTests();
    }
}
