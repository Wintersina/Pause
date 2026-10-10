using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// Bug report: "other custom enemies appear in all worlds, not just their own
// world". The rule (EnemyRoster, EliteCatalog, BossCatalog): each world
// (WorldManager.Worlds: Space, Frost, Verdant, Ember) fields its OWN cast --
// roster enemies of its world index, elites whose def names its world key,
// its own boss -- and nothing is shared between worlds except the shared
// systems themselves (no enemy, elite or boss belongs to two worlds).
//
// Per world, with a real WorldManager on that world (PlayerPrefs, as a portal
// leaves it), this samples every entry point that picks what to field:
//   * the roster slots (EnemyRoster.Pick / One / Fighter for every role, and
//     the spawner's extras, ChooseExtraDef, in every phase)
//   * the elite director's own clock (EliteDirector.Tick over a long run on
//     landing pads), which is what the game calls every running frame
//   * the boss (BossCatalog.ForWorld)
// and checks that every elite def's world key matches its own key's prefix
// (a Codex def dropped in with the wrong "world" would leak it elsewhere).
public static class WorldGatingTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[WORLDGATE] PASS  " : "[WORLDGATE] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags Stat = BindingFlags.NonPublic | BindingFlags.Static;
    static int Worlds => WorldManager.LiveWorldCount;
    static string W(int w) => WorldManager.Worlds[w].displayName;

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            EliteCatalog.Reload();
            Keys();
            RosterPerWorld();
            ElitesPerWorld();
            BossesPerWorld();
        }
        finally
        {
            EliteSystem.Clear();
            EliteSystem.PlayerOverride = null;
            LandingSites.Override = null;
            SetWorldManager(null);
            BossEncounter.ResetRun();
            startMenu.youAreInTutorial = false;
            buttonClicks.playerDied = false;
        }
        Debug.Log("[WORLDGATE] failures: " + fails);
        return fails;
    }

    static void SetWorldManager(WorldManager wm)
    {
        typeof(WorldManager).GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, wm);
    }

    // A WorldManager on world `w`, far from its boss.
    static WorldManager OnWorld(int w)
    {
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, w);
        var wm = new GameObject("~WgWorlds").AddComponent<WorldManager>();
        SetWorldManager(wm);
        moveBackGround.speed = .25f;
        typeof(WorldManager).GetField("appliedRate", Stat).SetValue(null, 0f);
        typeof(WorldManager).GetField("distanceLeft", Inst).SetValue(wm, 1000f);
        return wm;
    }

    static void Keys()
    {
        Check("the roster has a key per world", EnemyRoster.WorldKeys.Length == WorldManager.Worlds.Length);
        int bad = 0;
        string example = null;
        foreach (var d in EnemyRoster.All)
            if (d.world < 0 || d.world >= WorldManager.Worlds.Length || !d.key.StartsWith(EnemyRoster.WorldKeys[d.world] + "_", StringComparison.Ordinal))
            { bad++; example = example ?? d.key; }
        Check("every roster enemy belongs to exactly one world, named by its key (" + bad + " off" +
              (example != null ? ", e.g. " + example : "") + ")", bad == 0);

        bad = 0; example = null;
        foreach (var d in EliteCatalog.All)
            if (d.WorldIndex < 0 || !d.key.StartsWith(EnemyRoster.WorldKeys[d.WorldIndex] + "_", StringComparison.Ordinal))
            { bad++; example = example ?? (d.key + " says '" + d.world + "'"); }
        Check("every elite def's world is its own key's world (" + EliteCatalog.All.Length + " defs, " + bad + " off" +
              (example != null ? ", e.g. " + example : "") + ")", bad == 0);
    }

    static void RosterPerWorld()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var roles = (EnemyRole[])Enum.GetValues(typeof(EnemyRole));
        for (int w = 0; w < Worlds; w++)
        {
            var wm = OnWorld(w);
            Check(W(w) + ": the spawner's world is " + W(w), EnemyRoster.CurrentWorld == w);
            int n = 0, foreign = 0;
            string example = null;
            Action<EnemyDef> see = d =>
            {
                if (d == null) return;
                n++;
                if (d.world != w) { foreign++; example = example ?? d.key; }
            };
            UnityEngine.Random.InitState(77 + w);
            for (int k = 0; k < 200; k++)
            {
                foreach (var role in roles)
                {
                    see(EnemyRoster.Pick(EnemyRoster.CurrentWorld, role));
                    see(EnemyRoster.One(EnemyRoster.CurrentWorld, role));
                }
                for (int tier = 1; tier <= 4; tier++) see(EnemyRoster.Fighter(EnemyRoster.CurrentWorld, tier));
                for (int phase = 0; phase <= 4; phase++) see(enmiesOnBoard.ChooseExtraDef(EnemyRoster.CurrentWorld, phase));
            }
            Check(W(w) + ": every roster pick is " + W(w) + "'s own (" + n + " picks, " + foreign + " foreign" +
                  (example != null ? ", e.g. " + example : "") + ")", foreign == 0 && n > 0);
            SetWorldManager(null);
            UnityEngine.Object.DestroyImmediate(wm.gameObject);
        }
    }

    static void ElitesPerWorld()
    {
        var own = new List<EliteDef>();
        for (int w = 0; w < Worlds; w++)
        {
            EliteSystem.Clear();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            startMenu.youAreInTutorial = false;
            buttonClicks.playerDied = false;
            score.pauseCounter = 0;
            BossEncounter.ResetRun();
            var pilot = new GameObject("~WgPilot").transform;
            pilot.position = new Vector3(0f, -2.5f, 0f);
            EliteSystem.PlayerOverride = pilot;
            var pads = new List<LandingSite>();
            for (int i = 0; i < 6; i++)
            {
                var anchor = new GameObject("~WgPad" + i).transform;
                anchor.position = new Vector3(-2f + i * .8f, 3f, 0f);
                pads.Add(new LandingSite { anchor = anchor, local = Vector3.zero, scale = .3f, order = -420, id = 10 + i });
            }
            LandingSites.Override = list => list.AddRange(pads);
            var wm = OnWorld(w);
            UnityEngine.Random.InitState(4242 + w);

            var dir = new GameObject("~WgDir").AddComponent<EliteDirector>();
            var seen = new HashSet<string>();
            int made = 0, foreign = 0;
            string example = null;
            const float dt = .25f;
            for (int i = 0; i < 2400; i++)   // ten minutes of flight
            {
                dir.Tick(dt);
                var live = EliteShip.Live;
                if (live.Count == 0) continue;
                for (int k = 0; k < live.Count; k++)
                {
                    var e = live[k];
                    if (e == null) continue;
                    made++;
                    seen.Add(e.Def.key);
                    if (e.Def.WorldIndex != w) { foreign++; example = example ?? e.Def.key; }
                }
                EliteSystem.Clear();   // gone again: the next group may come
            }
            int defs = EliteCatalog.ForWorld(w, own);
            Check(W(w) + ": the elite director fields only " + W(w) + "'s elites (" + made + " made, " + seen.Count + " of its " +
                  defs + " kinds, " + foreign + " foreign" + (example != null ? ", e.g. " + example : "") + ")",
                  foreign == 0 && (defs == 0 ? made == 0 : made > 0));

            UnityEngine.Object.DestroyImmediate(dir.gameObject);
            SetWorldManager(null);
            UnityEngine.Object.DestroyImmediate(wm.gameObject);
            EliteSystem.PlayerOverride = null;
            LandingSites.Override = null;
        }
    }

    static void BossesPerWorld()
    {
        var seen = new HashSet<BossDef>();
        bool distinct = true;
        for (int w = 0; w < Worlds; w++)
        {
            var b = BossCatalog.ForWorld(w);
            distinct &= b != null && seen.Add(b);
        }
        Check("each world has its own boss", distinct);
    }
}
