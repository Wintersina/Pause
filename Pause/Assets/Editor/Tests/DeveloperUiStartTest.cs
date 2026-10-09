using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// The developer start-world pick as a developer really uses it: the Options
// screen's own buttons (leaderboardS3: DEVELOPER ON/OFF, the START < NAME >
// row, BOSS RUSH) clicked through their onClick, then the PLAY that follows
// (SpaceDock.Destination: the game or the tutorial), then gameS1 loading in
// the order Unity runs it (sceneLoaded attaches the WorldManager, Awake,
// other scripts' Start reading the world, WorldManager.Start). After every
// step the world the run is on must be the one the label said.
public static class DeveloperUiStartTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[DEVUI] PASS  " : "[DEVUI] FAIL  ") + what);
        if (!ok) fails++;
    }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    public static void Run() { TestHarness.Exit(Execute()); }

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
        Debug.Log("[DEVUI] failures: " + fails);
        return fails;
    }

    static Button Find(string name)
    {
        var go = SceneUtil.FindAny(name);
        return go != null ? go.GetComponent<Button>() : null;
    }

    static string LabelOf(string name)
    {
        var go = SceneUtil.FindAny(name);
        var t = go != null ? go.GetComponentInChildren<Text>(true) : null;
        return t != null ? t.text : "(missing)";
    }

    static void Click(string name)
    {
        var b = Find(name);
        if (b == null) { Check("button " + name + " exists", false); return; }
        b.onClick.Invoke();
    }

    static void Body()
    {
        // A player who has played before: progress in the real save, a
        // previous session that ended in Frost, no developer state at all.
        PlayerPrefs.DeleteAll();
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, 1);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 1);
        WorldManager.ClearReplayWorld();

        // ---- the Options screen ----
        EditorSceneManager.OpenScene("Assets/Scenes/leaderboardS3.unity", OpenSceneMode.Single);
        var opts = new GameObject("~DeveloperOptions").AddComponent<DeveloperOptions>();
        opts.SendMessage("Start");
        Check("Options has the DEVELOPER switch", Find("DeveloperToggle") != null);
        Check("Options has the start-world row", SceneUtil.FindAny("DeveloperStartWorld") != null);
        Check("developer OFF label", LabelOf("DeveloperToggle") == "DEVELOPER  OFF");
        Click("DeveloperToggle");
        Check("DEVELOPER ON after one tap", DeveloperUnlocks.Enabled && LabelOf("DeveloperToggle") == "DEVELOPER  ON");
        Check("the start-world row appears", SceneUtil.FindAny("DeveloperStartWorld").activeSelf);
        Check("the tutorial gate is open", PlayerPrefs.GetString("HasDoneTut") == "true");
        Check("PLAY goes to the game, not the tutorial", SpaceDock.Destination == "gameS1");

        int count = WorldManager.Worlds.Length;
        // Walk the picker with the > button, then the < button, then the name.
        for (int step = 0; step < count * 2; step++)
        {
            string name = step < count ? "NextWorld" : (step < count + count / 2 ? "PrevWorld" : "WorldName");
            Click(name);
            int shown = DeveloperUnlocks.SelectedWorld;
            string label = LabelOf("WorldName");
            string expect = "START  " + WorldManager.Worlds[shown].displayName.ToUpperInvariant();
            Check("tap " + name + ": row reads '" + expect + "' (was '" + label + "')", label == expect);
            Check("tap " + name + ": the pick is saved", PlayerPrefs.GetInt(DeveloperUnlocks.SelectedWorldKey, -1) == shown);
            if (step < count) RunFromPick(shown, name);
        }

        // Every world, every rush setting.
        for (int pick = 0; pick < count; pick++)
        {
            DeveloperUnlocks.SelectWorld(pick);
            for (int rush = 0; rush < 3; rush++)
            {
                BossDev.SetRushMode(rush);
                RunFromPick(pick, "rush " + rush);
            }
        }
        BossDev.SetRushMode(0);

        // The pick survives turning the mode off and on (the label shows it).
        DeveloperUnlocks.SelectWorld(2);
        Click("DeveloperToggle");
        Check("OFF: the next run is the real progress (Frost)", DeveloperUnlocks.StartWorld(true) == 1);
        Click("DeveloperToggle");
        Check("ON again: the pick (Verdant) rules", DeveloperUnlocks.StartWorld(true) == 2);
        RunFromPick(2, "toggled");
        Object.DestroyImmediate(opts.gameObject);
    }

    // PLAY: the world the next run starts in, then gameS1 loading.
    static void RunFromPick(int pick, string how)
    {
        string tag = "[" + how + " -> " + WorldManager.Worlds[pick].displayName + "] ";
        Check(tag + "PLAY destination is gameS1", SpaceDock.Destination == "gameS1");
        // The previous session ended on another world (the saved current).
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, (pick + 1) % WorldManager.Worlds.Length);
        GameStateReset.Clear();

        EditorSceneManager.OpenScene("Assets/Scenes/gameS1.unity", OpenSceneMode.Single);
        BossEncounter.ResetRun();
        RunScore.EndRun(RunScore.RunId);
        RunScore.BeginRun(true, true);          // score.Awake

        // WorldBootstrap.OnSceneLoaded: the manager is attached after the
        // scene's Awakes and before the Starts.
        var wm = new GameObject("~WorldManager").AddComponent<WorldManager>();
        wm.SendMessage("Awake");
        int early = WorldManager.CurrentIndex;  // what a Start that runs first sees
        try { typeof(WorldManager).GetMethod("Start", Inst).Invoke(wm, null); }
        catch (System.Exception e) { Debug.Log("[DEVUI] (Start's presentation threw in edit mode: " + (e.InnerException ?? e).Message + ")"); }

        Check(tag + "the run starts in the pick (is " + WorldManager.Worlds[WorldManager.CurrentIndex].displayName + ")",
              WorldManager.CurrentIndex == pick);
        Check(tag + "the loop's return point is the pick", RunLoop.StartWorld == pick && RunLoop.Index == 0);
        Check(tag + "scripts that start before WorldManager.Start already see the pick (saw " + early + ")", early == pick);
        var bd = WorldBackdrop.Instance;
        if (bd != null && bd.Current != null)
            Check(tag + "backdrop is the pick's (" + bd.Current.Spec.world + ")",
                  bd.Current.Spec.world == WorldManager.Worlds[pick].displayName);

        // Replay keeps it.
        buttonClicks.playerDied = true;
        Time.timeScale = 0f;
        buttonClicks.PrepareReplay();
        Check(tag + "Replay pins the pick", WorldManager.PinnedReplayWorld == pick);
        Object.DestroyImmediate(wm.gameObject);
        Time.timeScale = 1f;
        buttonClicks.playerDied = false;
    }
}
