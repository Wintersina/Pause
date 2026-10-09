using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The player-facing START WORLD setting: Options row (StartWorldOptions), its
// persisted choice (PlayerStartWorld) and the run start that follows
// (WorldManager.RunStartWorld). The real Options buttons are clicked through
// their onClick, then a run is started in the order Unity runs it
// (score.Awake -> WorldManager Awake -> Start) with a stale saved current
// world, as a restarted game has.
public static class PlayerStartWorldTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[PSW] PASS  " : "[PSW] FAIL  ") + what);
        if (!ok) fails++;
    }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
    const BindingFlags Stat = BindingFlags.NonPublic | BindingFlags.Static;

    public static void Run() { TestHarness.Exit(Execute()); }

    static int Last { get { return WorldManager.Worlds.Length - 1; } }
    static string Name(int w) { return WorldManager.Worlds[w].displayName; }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try { Body(); }
        finally
        {
            WorldManager.ClearReplayWorld();
            RunLoop.Reset();
            BossEncounter.ResetRun();
            PortalPressure.Reset();
            buttonClicks.playerDied = false;
            moveBackGround.speed = 0f;
            Time.timeScale = 1f;
        }
        Debug.Log("[PSW] failures: " + fails);
        return fails;
    }

    // ---- fixtures ----------------------------------------------------------

    static void Progress(int highest, bool developer = false)
    {
        PlayerPrefs.DeleteAll();
        WorldManager.ClearReplayWorld();
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, developer ? 1 : 0);
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, highest);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, highest);
    }

    static StartWorldOptions options;

    // The Options screen as the player opens it (it loads fresh each visit).
    static StartWorldOptions OpenOptions()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/leaderboardS3.unity", OpenSceneMode.Single);
        if (DeveloperUnlocks.Available)
        {
            var dev = new GameObject("~DeveloperOptions").AddComponent<DeveloperOptions>();
            dev.SendMessage("Start");
        }
        options = new GameObject("~StartWorldOptions").AddComponent<StartWorldOptions>();
        options.SendMessage("Start");
        return options;
    }

    static GameObject Obj(string name) { return SceneUtil.FindAny(name); }
    static bool Shown(string name) { var g = Obj(name); return g != null && g.activeInHierarchy; }

    static string Label(string name)
    {
        var go = Obj(name);
        var t = go != null ? go.GetComponentInChildren<Text>(true) : null;
        return t != null ? t.text : "(missing)";
    }

    static void Click(string name)
    {
        var go = Obj(name);
        var b = go != null ? go.GetComponent<Button>() : null;
        if (b == null || !b.interactable) { Check("button " + name + " is clickable", false); return; }
        b.onClick.Invoke();
    }

    // PLAY, then the game scene loading: returns the manager. `early` is the
    // world a script whose Start runs before WorldManager.Start would see.
    static WorldManager StartRun(out int early, bool startAtHighest = true)
    {
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, (PlayerPrefs.GetInt(WorldManager.PrefsCurrentWorld, 0) + 2) % WorldManager.Worlds.Length);
        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        BossEncounter.ResetRun();
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true);
        var wm = new GameObject("~WorldManager").AddComponent<WorldManager>();
        wm.startAtHighestUnlocked = startAtHighest;
        wm.SendMessage("Awake");
        wm.HoldStartWorld();
        early = WorldManager.CurrentIndex;
        try { typeof(WorldManager).GetMethod("Start", Inst).Invoke(wm, null); }
        catch (System.Exception e) { Debug.Log("[PSW] (Start's presentation threw in edit mode: " + (e.InnerException ?? e).Message + ")"); }
        return wm;
    }

    static void Through(WorldManager wm)
    {
        typeof(BossEncounter).GetField("doneWorld", Stat).SetValue(null, WorldManager.CurrentIndex);
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(wm, 0f);
        wm.EndLevel();
        wm.Advance(false);
        if (Liftoff.Live != null) Object.DestroyImmediate(Liftoff.Live.gameObject);
        if (Planetfall.Live != null) Object.DestroyImmediate(Planetfall.Live.gameObject);
        foreach (var p in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
    }

    static void End(WorldManager wm)
    {
        if (wm != null) Object.DestroyImmediate(wm.gameObject);
        Time.timeScale = 1f;
        buttonClicks.playerDied = false;
    }

    static int Highest { get { return PlayerPrefs.GetInt(WorldManager.PrefsHighestWorld, 0); } }

    // A run from the menu's PLAY with the world `expect` and source `source`.
    static void ExpectRun(string tag, int expect, string source)
    {
        int highestBefore = Highest;
        Check(tag + "PLAY goes to the game", SpaceDock.Destination == "gameS1");
        string seen = WorldManager.RunStartSource(true);
        int early;
        var wm = StartRun(out early);
        Check(tag + "run starts in " + Name(expect) + " (is " + Name(WorldManager.CurrentIndex) + ")", WorldManager.CurrentIndex == expect);
        Check(tag + "scripts starting before WorldManager.Start see it (saw " + early + ")", early == expect);
        Check(tag + "loop return point is " + Name(expect), RunLoop.StartWorld == expect && RunLoop.Index == 0);
        Check(tag + "source '" + seen + "' is '" + source + "'", seen == source);
        Check(tag + "highestWorld not lowered (" + Highest + " >= " + highestBefore + ")", Highest == highestBefore);
        var bd = WorldBackdrop.Instance;
        if (bd != null && bd.Current != null)
            Check(tag + "backdrop is " + Name(expect), bd.Current.Spec.world == Name(expect));
        End(wm);
    }

    // ---- the checks --------------------------------------------------------

    static void Body()
    {
        FreshPlayer();
        Unlocking();
        EveryUnlockedWorld();
        Clamping();
        DefaultUnchanged();
        Priority();
        ReplayKeepsTheChoice();
        Persistence();
        ProgressionNeverLowers();
        CloudSnapshot();
        DeveloperHidesTheRow();
    }

    // Before any level is finished: the row is locked, not a picker.
    static void FreshPlayer()
    {
        Progress(0);
        OpenOptions();
        Check("fresh: no picker row", !Shown(StartWorldOptions.RowName));
        Check("fresh: locked row shown", Shown(StartWorldOptions.LockedName));
        Check("fresh: locked label", Label(StartWorldOptions.LockedName) == PlayerStartWorld.LockedLabel);
        var locked = Obj(StartWorldOptions.LockedName).GetComponent<Button>();
        Check("fresh: locked row is not tappable", locked != null && !locked.interactable);
        Check("fresh: condition says locked", !PlayerStartWorld.Unlocked && PlayerStartWorld.Chosen() == -1);
        ExpectRun("fresh: ", 0, "furthest planet");

        // A tutorial player is still sent to the tutorial first.
        PlayerPrefs.DeleteKey("HasDoneTut");
        Check("new player: PLAY goes to the tutorial", SpaceDock.Destination == "tutorialS5");
        PlayerPrefs.SetString("HasDoneTut", "true");
    }

    // Finishing the first world (reaching the next) unlocks the row.
    static void Unlocking()
    {
        Progress(0);
        int early;
        var wm = StartRun(out early);
        Through(wm);                                    // boss beaten, portal taken
        Check("beating the first world raises highestWorld", Highest == 1);
        End(wm);
        OpenOptions();
        Check("unlocked: picker row shown", Shown(StartWorldOptions.RowName));
        Check("unlocked: locked row gone", !Shown(StartWorldOptions.LockedName));
        Check("unlocked: row reads '" + PlayerStartWorld.Label(1) + "' (" + Label(StartWorldOptions.NameName) + ")",
              Label(StartWorldOptions.NameName) == "START  " + Name(1).ToUpperInvariant());
        Check("unlocked: arrows read < and >", Label(StartWorldOptions.PrevName) == "<" && Label(StartWorldOptions.NextName) == ">");
        var row = (RectTransform)Obj(StartWorldOptions.RowName).transform;
        var tut = (RectTransform)Obj("Tutorial").transform;
        var lb = (RectTransform)Obj("pullUpLeaderBoard").transform;
        Check("row sits above LeaderBoard and clear of it",
              row.anchoredPosition.y - RowHalf() >= lb.anchoredPosition.y + lb.sizeDelta.y * .5f - .01f &&
              row.parent == tut.parent);
        Check("row is centred and inside the 800-unit canvas", Mathf.Abs(row.anchoredPosition.x) < .01f && row.sizeDelta.x <= 800f);
    }

    static float RowHalf() { return 44f; }

    // Every reached world is selectable with the real buttons and starts the run there.
    static void EveryUnlockedWorld()
    {
        for (int highest = 1; highest <= Last; highest++)
        {
            Progress(highest);
            OpenOptions();
            int count = highest + 1;
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int step = 0; step < count * 2 + 1; step++)
            {
                Click(step < count ? StartWorldOptions.NextName : (step < count * 2 ? StartWorldOptions.PrevName : StartWorldOptions.NameName));
                int shown = PlayerStartWorld.Selected;
                seen.Add(shown);
                string tag = "highest " + Name(highest) + " step " + step + ": ";
                Check(tag + "selection <= highest (" + shown + ")", shown >= 0 && shown <= highest);
                Check(tag + "row reads " + PlayerStartWorld.Label(shown), Label(StartWorldOptions.NameName) == PlayerStartWorld.Label(shown));
                Check(tag + "pref " + (shown == highest ? "cleared (follows furthest)" : "saved"),
                      shown == highest ? !PlayerPrefs.HasKey(PlayerStartWorld.Key) : PlayerPrefs.GetInt(PlayerStartWorld.Key, -1) == shown);
            }
            Check("highest " + Name(highest) + ": every reached world was offered (" + seen.Count + "/" + count + ")", seen.Count == count);
            Check("highest " + Name(highest) + ": no locked world was offered", !seen.Contains(highest + 1));

            for (int pick = 0; pick <= highest; pick++)
            {
                // Select through the real buttons: walk to it.
                OpenOptions();
                for (int guard = 0; guard < count && PlayerStartWorld.Selected != pick; guard++) Click(StartWorldOptions.NextName);
                Check("highest " + Name(highest) + " pick " + Name(pick) + ": row shows it", Label(StartWorldOptions.NameName) == PlayerStartWorld.Label(pick));
                string tag = "highest " + Name(highest) + " pick " + Name(pick) + ": ";
                ExpectRun(tag, pick, pick == highest ? "furthest planet" : "player start world");
            }
        }
    }

    // A saved choice above what is reached (progress restore / cloud sync) is clamped.
    static void Clamping()
    {
        Progress(1);
        PlayerPrefs.SetInt(PlayerStartWorld.Key, 3);
        Check("clamp: choice 3 with highest 1 reads as 1", PlayerStartWorld.Selected == 1);
        OpenOptions();
        Check("clamp: row shows the furthest reached", Label(StartWorldOptions.NameName) == PlayerStartWorld.Label(1));
        ExpectRun("clamp: ", 1, "player start world");

        Progress(3);
        PlayerPrefs.SetInt(PlayerStartWorld.Key, 2);
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 1);   // progress restored to less
        ExpectRun("clamp after restore: ", 1, "player start world");
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 0);   // restored to before any level
        Check("restored to nothing: setting has no say", PlayerStartWorld.Chosen() == -1);
        ExpectRun("restored to nothing: ", 0, "furthest planet");
        PlayerPrefs.SetInt(PlayerStartWorld.Key, -4);
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 2);
        Check("negative choice reads as Space", PlayerStartWorld.Selected == 0);
    }

    static void DefaultUnchanged()
    {
        for (int highest = 0; highest <= Last; highest++)
        {
            Progress(highest);
            Check("default " + Name(highest) + ": no choice saved", !PlayerPrefs.HasKey(PlayerStartWorld.Key));
            ExpectRun("default furthest " + Name(highest) + ": ", highest, "furthest planet");
        }
        // The journey rule (every run from Space) is not overridden.
        Progress(2);
        PlayerPrefs.SetInt(PlayerStartWorld.Key, 1);
        Check("journey mode ignores the choice", WorldManager.RunStartWorld(false) == 0);
    }

    static void Priority()
    {
        // developer pick (mode on) beats the player's choice.
        Progress(2);
        PlayerPrefs.SetInt(PlayerStartWorld.Key, 1);
        Check("player choice alone: Frost", WorldManager.RunStartWorld(true) == 1 && WorldManager.RunStartSource(true) == "player start world");
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        PlayerPrefs.SetInt(DeveloperUnlocks.SelectedWorldKey, 0);
        Check("developer pick beats the player's choice", WorldManager.RunStartWorld(true) == 0 &&
              WorldManager.RunStartSource(true) == "developer pick");
        PlayerPrefs.DeleteKey(DeveloperUnlocks.SelectedWorldKey);
        Check("developer mode without a pick starts as before (not the player's choice)",
              WorldManager.RunStartSource(true) != "player start world");
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(DeveloperUnlocks.SelectedWorldKey, 0);
        Check("developer pick ignored while the mode is off: the player's choice rules",
              WorldManager.RunStartWorld(true) == 1);
        // replay pin beats both.
        Progress(2);
        PlayerPrefs.SetInt(PlayerStartWorld.Key, 1);
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        PlayerPrefs.SetInt(DeveloperUnlocks.SelectedWorldKey, 0);
        int early;
        var wm = StartRun(out early);
        typeof(WorldManager).GetField("replayWorld", Stat).SetValue(null, 2);
        Check("replay pin beats developer pick and player choice",
              WorldManager.RunStartWorld(true) == 2 && WorldManager.RunStartSource(true) == "replay pin");
        End(wm);
        WorldManager.ClearReplayWorld();
    }

    // Replay restarts the world the run began in: the chosen start world, not
    // the furthest planet the run reached on the way.
    static void ReplayKeepsTheChoice()
    {
        for (int highest = 1; highest <= Last; highest++)
        {
            for (int pick = 0; pick < highest; pick++)
            {
                Progress(highest);
                PlayerPrefs.SetInt(PlayerStartWorld.Key, pick);
                int early;
                var wm = StartRun(out early);
                string tag = "replay highest " + Name(highest) + " pick " + Name(pick) + ": ";
                Check(tag + "first run in the pick", WorldManager.CurrentIndex == pick);
                for (int n = 1; n <= 2; n++)
                {
                    if (n == 1 && WorldManager.CurrentIndex < Last) Through(wm);
                    buttonClicks.playerDied = true;
                    Time.timeScale = 0f;
                    buttonClicks.PrepareReplay();
                    Check(tag + "replay " + n + " pins the pick", WorldManager.PinnedReplayWorld == pick);
                    End(wm);
                    wm = StartRun(out early);
                    Check(tag + "replay " + n + " restarts in the pick (is " + Name(WorldManager.CurrentIndex) + ")",
                          WorldManager.CurrentIndex == pick && early == pick && RunLoop.StartWorld == pick);
                    Check(tag + "replay " + n + " source is replay pin", WorldManager.RunStartSource(true) == "replay pin");
                }
                End(wm);
                // Out to the menu and PLAY: the choice again.
                GameStateReset.Clear();
                ExpectRun(tag + "menu PLAY: ", pick, "player start world");
            }
        }
    }

    // The choice survives a restart (prefs persist; the pin and options UI do not).
    static void Persistence()
    {
        Progress(3);
        OpenOptions();
        Click(StartWorldOptions.PrevName);
        Click(StartWorldOptions.PrevName);
        int chosen = PlayerStartWorld.Selected;
        Check("persist: stepped back two worlds", chosen == 1);
        PlayerPrefs.Save();
        WorldManager.ClearReplayWorld();
        RunLoop.Reset();
        options = null;
        OpenOptions();   // a restarted game: Options built fresh
        Check("persist: reopened row still reads " + Name(chosen), Label(StartWorldOptions.NameName) == PlayerStartWorld.Label(chosen));
        ExpectRun("persist: ", chosen, "player start world");
        // Choosing the furthest again follows future progress.
        OpenOptions();
        Click(StartWorldOptions.PrevName);   // 1 -> 0 -> wraps? step back from 1 to 0
        Click(StartWorldOptions.PrevName);   // wraps to the furthest (3)
        Check("persist: wrapped to the furthest world", PlayerStartWorld.Selected == 3 && !PlayerPrefs.HasKey(PlayerStartWorld.Key));
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, Last);
        Check("follows the furthest world once more is unlocked", PlayerStartWorld.Selected == Last);
    }

    // Starting early and flying on raises highestWorld only for worlds newly reached.
    static void ProgressionNeverLowers()
    {
        Progress(2);
        PlayerPrefs.SetInt(PlayerStartWorld.Key, 0);
        int early;
        var wm = StartRun(out early);
        Check("early start: in Space", WorldManager.CurrentIndex == 0 && Highest == 2);
        Through(wm);
        Check("early start: Frost reached, highest still 2", WorldManager.CurrentIndex == 1 && Highest == 2);
        Through(wm);
        Check("early start: Verdant reached, highest still 2", WorldManager.CurrentIndex == 2 && Highest == 2);
        if (Last > 2)
        {
            Through(wm);
            Check("early start: first new world raises highest to " + (Highest), WorldManager.CurrentIndex == 3 && Highest == 3);
        }
        Check("choice kept while progressing", PlayerPrefs.GetInt(PlayerStartWorld.Key, -1) == 0);
        End(wm);
    }

    static void CloudSnapshot()
    {
        Progress(3);
        PlayerPrefs.SetInt(PlayerStartWorld.Key, 1);
        var snap = ProgressSnapshot.Capture(100);
        Check("snapshot carries the choice (world + 1)", snap.startWorld == 2);
        var parsed = ProgressSnapshot.TryParse(snap.ToJson(), out var back);
        Check("snapshot json round-trips", parsed == ProgressSnapshot.ParseResult.Ok && back.startWorld == 2);
        PlayerPrefs.DeleteKey(PlayerStartWorld.Key);
        back.Apply();
        Check("apply restores the choice", PlayerPrefs.GetInt(PlayerStartWorld.Key, -1) == 1);
        PlayerPrefs.DeleteKey(PlayerStartWorld.Key);
        Check("snapshot without a choice is 0", ProgressSnapshot.Capture(1).startWorld == 0);
        var none = new ProgressSnapshot { savedAtUtc = 5 };
        PlayerPrefs.SetInt(PlayerStartWorld.Key, 2);
        none.Apply();
        Check("applying a snapshot with no choice clears it", !PlayerPrefs.HasKey(PlayerStartWorld.Key));
        var local = new ProgressSnapshot { savedAtUtc = 1, startWorld = 1, highestWorld = 3 };
        var cloud = new ProgressSnapshot { savedAtUtc = 2, startWorld = 3, highestWorld = 2 };
        Check("merge takes the newer side's choice", ProgressMerge.Merge(local, cloud).startWorld == 3);
    }

    // With developer mode on the player row steps aside for the dev picker.
    static void DeveloperHidesTheRow()
    {
        if (!DeveloperUnlocks.Available) return;
        Progress(2);
        PlayerPrefs.SetInt(PlayerStartWorld.Key, 0);
        OpenOptions();
        Check("dev off: player row shown", Shown(StartWorldOptions.RowName));
        float y = ((RectTransform)Obj(StartWorldOptions.RowName).transform).anchoredPosition.y;
        var toggle = Obj("DeveloperToggle");
        Check("player row does not sit on the developer switch",
              toggle != null && Mathf.Abs(((RectTransform)toggle.transform).anchoredPosition.y - y) >= 88f);
        Click("DeveloperToggle");
        options.SendMessage("Update");
        Check("dev on: player row hidden, dev picker shown", !Shown(StartWorldOptions.RowName) && Shown("DeveloperStartWorld"));
        Check("dev on: no player choice applies", WorldManager.RunStartSource(true) != "player start world");
        Click("DeveloperToggle");
        options.SendMessage("Update");
        Check("dev off again: player row back, choice kept", Shown(StartWorldOptions.RowName) && PlayerStartWorld.Selected == 0 &&
              Highest == 2);
        options = null;
    }
}
