using System.Collections.Generic;
using UnityEngine;

// The mid-run hitch removals (perf/warm-art): a world's lazily loaded art is
// warmed as the world begins and held until the next, the pieces that attach
// to the ship wait for its announcement instead of scanning the scene every
// frame, and the elite shot landings rely on the project's automatic physics
// transform sync.
//   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites PerfWarmupTest
public static class PerfWarmupTest
{
    static int fails;
    static void Check(string what, bool ok, string extra = "")
    {
        Debug.Log((ok ? "[WARM] PASS  " : "[WARM] FAIL  ") + what + (extra.Length > 0 ? "  [" + extra + "]" : ""));
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        try
        {
            Paths();
            Warm();
            Attachers();
            Physics();
        }
        finally
        {
            ArtWarm.SyncLoader = null;
            ArtWarm.Forget();
            ShipSpawn.Reset();
        }
        Debug.Log("[WARM] " + (fails == 0 ? "ALL PASS" : fails + " FAILED"));
        return fails;
    }

    // ---- what a world warms: exactly the paths its lazy loaders read ----
    static void Paths()
    {
        var into = new List<string>();
        int worlds = WorldManager.Worlds.Length;
        for (int w = 0; w < worlds; w++)
        {
            ArtWarm.Paths(w, into);
            var boss = BossCatalog.ForWorld(w);
            bool hasBoss = boss != null && !string.IsNullOrEmpty(boss.artKey);
            Check("world " + w + ": warms its boss's body, shots and card first (after the shared warning)",
                  !hasBoss || (into.Count >= 5 && into[0] == BossArt.Folder + "warning" && into[1] == BossArt.Folder + boss.artKey &&
                               into[2] == BossArt.Folder + boss.artKey + "_shots" && into[4] == BossArt.Folder + boss.artKey + "_card"));
            if (hasBoss && BossArt.HasDamageArt(boss))
                Check("world " + w + ": ... and its damage art and first-stage attack poses",
                      into.Contains(BossArt.Folder + boss.damageKey + "_damage") && into.Contains(BossArt.Folder + boss.damageKey + "_damage_fx") &&
                      into.Exists(p => p.StartsWith(BossArt.ImpactFolder + "attack/") && p.EndsWith("_stage1")) &&
                      !into.Exists(p => p.EndsWith("_stage2")));
            if (hasBoss && !string.IsNullOrEmpty(boss.deathKey))
                Check("world " + w + ": ... and its death strip", into.Contains(BossArt.Folder + boss.deathKey + "_death"));
            var elites = new List<EliteDef>();
            EliteCatalog.ForWorld(w, elites);
            bool allElites = true;
            foreach (var d in elites)
            {
                string strip = EliteArt.StripPath(d);
                allElites &= into.Contains(strip) && into.Contains(strip + "_damaged") && into.Contains(EliteArt.ExtraPath(d, EliteArt.Extra.Lights)) &&
                             into.Contains(EliteArt.ExtraPath(d, EliteArt.Extra.Death));
            }
            Check("world " + w + ": every elite's strip, companions and extras (" + elites.Count + " elites)", allElites);
            var e = CoverBoulderTable.For(w);
            if (e != null && e.enabled)
                Check("world " + w + ": its boulders' manifest, sheet, break strip and fx",
                      into.Contains(e.folder + "/manifest") && into.Contains(e.folder + "/boulder") && into.Contains(e.folder + "/break") && into.Contains(e.folder + "/fx"));
            var seen = new HashSet<string>();
            bool dup = false;
            foreach (var p in into) if (!seen.Add(p)) dup = true;
            Check("world " + w + ": no path twice, none empty", !dup && !into.Exists(string.IsNullOrEmpty));
        }
        // the lists name real files: the Space boss atlas and the first elite strip load
        ArtWarm.Paths(0, into);
        Check("Space: the boss atlas named is a real sheet", Resources.Load<Texture2D>(into[1]) != null, into[1]);
    }

    // ---- the warm-up: loads a world's art ahead, bounded, held until the next world ----
    static void Warm()
    {
        ArtWarm.Forget();
        var asked = new List<string>();
        var fake = new Dictionary<string, Object>();
        ArtWarm.SyncLoader = path => { asked.Add(path); Object o; return fake.TryGetValue(path, out o) ? o : null; };
        var tex = new Texture2D(2, 2);
        try
        {
            var expected = new List<string>();
            ArtWarm.Paths(0, expected);
            fake[expected[1]] = tex;   // only the boss atlas "exists"
            ArtWarm.Begin(0);
            Check("Begin(0) loads every path of world 0 once, in order", asked.Count == expected.Count && asked[1] == expected[1] && ArtWarm.Done);
            Check("... and holds what loaded (the boss atlas), nothing for a missing optional sheet", ArtWarm.HeldCount == 1 && ArtWarm.Holds(tex) && ArtWarm.World == 0);
            asked.Clear();
            ArtWarm.Begin(1);
            Check("the next world drops the last one's hold and loads its own", ArtWarm.World == 1 && !ArtWarm.Holds(tex) && ArtWarm.HeldCount == 0 && asked.Count > 0);
            ArtWarm.Forget();
            Check("Forget empties it", ArtWarm.World == -1 && ArtWarm.Done && ArtWarm.HeldCount == 0);
            Check("in play at most " + ArtWarm.MaxInFlight + " loads are in flight at once", ArtWarm.MaxInFlight >= 1 && ArtWarm.MaxInFlight <= 4);

            // the lazy loaders are untouched: a warmed path is read through the same Resources.Load (a resident object comes back at once)
            string src = TestHarness.StripComments(System.IO.File.ReadAllText("Assets/Scripts/Core/ArtWarm.cs"));
            Check("ArtWarm never slices or caches on the loaders' behalf (no Sprite.Create, no UnloadAsset)", !src.Contains("Sprite.Create") && !src.Contains("UnloadAsset"));
            string wm = TestHarness.StripComments(System.IO.File.ReadAllText("Assets/Scripts/Worlds/WorldManager.cs"));
            int begins = 0, at = 0;
            while ((at = wm.IndexOf("ArtWarm.Begin(CurrentIndex)", at, System.StringComparison.Ordinal)) >= 0) { begins++; at++; }
            Check("WorldManager warms at the run's start, the planetfall entry, every advance and the developer jump (" + begins + " sites)", begins == 4);
        }
        finally
        {
            ArtWarm.SyncLoader = null;
            ArtWarm.Forget();
            Object.DestroyImmediate(tex);
        }
    }

    // ---- the ship attachers: no scene scan, they wait for the ship's announcement ----
    static void Attachers()
    {
        // Out of play no OnEnable / Update runs on its own: the announcement is made by hand and each attacher is
        // stepped once (as TutorialPolishTest steps the hearts' attacher); in play the Spawned event does the same.
        ShipSpawn.Reset();
        int announced = 0; GameObject last = null;
        System.Action<GameObject> listener = go => { announced++; last = go; };
        ShipSpawn.Spawned += listener;
        var made = new List<GameObject>();
        GameObject Attacher<T>(string name) where T : MonoBehaviour { var go = new GameObject(name); go.AddComponent<T>(); made.Add(go); return go; }
        var ship = new GameObject("~ship"); made.Add(ship);
        try
        {
            var hearts = Attacher<ShipLivesIndicatorAttach>("~hearts");
            var thruster = Attacher<ShipThrusterAttach>("~thruster");
            var powers = Attacher<ShipPowerAttach>("~powers");
            ship.AddComponent<movePlayer>();   // (its Start does not run out of play)
            Check("before the ship: nothing announced", ShipSpawn.Live == null && announced == 0);
            ShipSpawn.Announce(ship);
            Check("the ship announces itself once, to every listener", announced == 1 && last == ship && ShipSpawn.Live == ship);
            hearts.SendMessage("Update"); thruster.SendMessage("Update"); powers.SendMessage("Update");
            Check("the hearts attach from the announced ship (no scene scan) and the attacher is gone", ship.GetComponent<ShipLivesIndicator>() != null && hearts == null);
            Check("the thruster attaches from the announced ship", (ship.GetComponent<ShipThruster>() != null || ship.GetComponent<ShipSpinDrift>() != null) && thruster == null);
            Check("the powers attach from the announced ship", ship.GetComponent<ShipPowerController>() != null && powers == null);
            int hearts0 = ship.GetComponents<ShipLivesIndicator>().Length, powers0 = ship.GetComponents<ShipPowerController>().Length;

            // a late attacher (made after the ship) attaches from Live, and attaches nothing twice
            var late = Attacher<ShipPowerAttach>("~late");
            var lateHearts = Attacher<ShipLivesIndicatorAttach>("~lateHearts");
            ShipSpawn.Announce(ship);
            late.SendMessage("Update"); lateHearts.SendMessage("Update");
            Check("an attacher made after the ship attaches from Live, nothing twice",
                  late == null && lateHearts == null && ship.GetComponents<ShipLivesIndicator>().Length == hearts0 && ship.GetComponents<ShipPowerController>().Length == powers0);

            // the tutorial ship gets hearts and a thruster but no powers (the game ship goes first: out of play
            // an attacher's fallback scan would find it)
            ShipSpawn.Reset();
            Object.DestroyImmediate(ship);
            var tut = new GameObject("~tutShip"); made.Add(tut);
            tut.AddComponent<movePlayerInTut>();
            var tutHearts = Attacher<ShipLivesIndicatorAttach>("~tutHearts");
            var tutPowers = Attacher<ShipPowerAttach>("~tutPowers");
            ShipSpawn.Announce(tut);
            tutHearts.SendMessage("Update"); tutPowers.SendMessage("Update");
            Check("the tutorial ship gets its hearts", tut.GetComponent<ShipLivesIndicator>() != null && tutHearts == null);
            Check("... but no powers (gameS1's ship only), and that attacher keeps waiting", tut.GetComponent<ShipPowerController>() == null && tutPowers != null);
            ShipSpawn.Forget(tut);
            Check("a destroyed ship is forgotten", ShipSpawn.Live == null);

            // the ship announces itself from its Start and forgets itself when destroyed
            string mp = TestHarness.StripComments(System.IO.File.ReadAllText("Assets/Scripts/Ship/movePlayer.cs"));
            string mt = TestHarness.StripComments(System.IO.File.ReadAllText("Assets/Scripts/Gameplay/movePlayerInTut.cs"));
            Check("movePlayer announces in Start and forgets in OnDestroy", mp.Contains("ShipSpawn.Announce(gameObject)") && mp.Contains("ShipSpawn.Forget(gameObject)"));
            Check("movePlayerInTut announces in Start and forgets in OnDestroy", mt.Contains("ShipSpawn.Announce(gameObject)") && mt.Contains("ShipSpawn.Forget(gameObject)"));
            foreach (var f in new[] { "Assets/Scripts/Ship/ShipLivesIndicator.cs", "Assets/Scripts/Ship/ShipThruster.cs", "Assets/Scripts/Gameplay/Weapons/ShipPowerController.cs" })
            {
                string s = TestHarness.StripComments(System.IO.File.ReadAllText(f));
                int i = s.IndexOf("FindFirstObjectByType", System.StringComparison.Ordinal);
                bool guarded = true;
                while (i >= 0)
                {
                    // every scan left is behind an out-of-play guard (the attachers' Update / FindPlayer)
                    int from = System.Math.Max(0, i - 400);
                    guarded &= s.Substring(from, i - from).Contains("!Application.isPlaying") || s.Substring(from, i - from).Contains("static GameObject FindPlayer");
                    i = s.IndexOf("FindFirstObjectByType", i + 1, System.StringComparison.Ordinal);
                }
                Check(System.IO.Path.GetFileName(f) + ": no scene scan in play", guarded);
            }
        }
        finally
        {
            ShipSpawn.Spawned -= listener;
            ShipSpawn.Reset();
            foreach (var go in made) if (go != null) Object.DestroyImmediate(go);
        }
    }

    // ---- elite shot landings: the explicit physics sync was redundant ----
    static void Physics()
    {
        // The project auto-syncs transforms before every physics query, and the simulation step syncs them
        // itself: a landed shot's collider is seen without a manual Physics2D.SyncTransforms. The removal
        // stands only while that setting holds.
        Check("Physics2D.autoSyncTransforms is on (Physics2DSettings)", Physics2D.autoSyncTransforms);
        string src = TestHarness.StripComments(System.IO.File.ReadAllText("Assets/Scripts/Gameplay/Elites/EliteShots.cs"));
        Check("EliteShots makes no manual Physics2D.SyncTransforms call", !src.Contains("Physics2D.SyncTransforms"));
        string proj = System.IO.File.ReadAllText("ProjectSettings/Physics2DSettings.asset");
        Check("the project file holds the setting (m_AutoSyncTransforms: 1)", proj.Contains("m_AutoSyncTransforms: 1"));
    }
}
