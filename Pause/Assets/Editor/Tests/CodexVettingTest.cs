using System.Collections.Generic;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

// The Codex against everything modern in the game, with EVERY world live (Tide, Storm and Desert switched
// on, whatever the shipped flags say): each world's roster, elites, boss, hazards and mine have an entry
// with art, a name and text that still matches the game; meeting the whole cast discovers the whole
// codex and reaches Completionist's target; every world and boss has its MUSIC track; the cloud save
// carries the codexSeen set (entries, tracks and backdrops); and no text still talks about a game
// with fewer worlds.
public static class CodexVettingTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[CVT] PASS  " : "[CVT] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetString("HasDoneTut", "true");
        PlayerPrefs.SetString(Codex.PrefsKey, "");
        PlayerPrefs.DeleteKey(Codex.NewKey);
        PlayerPrefs.DeleteKey(Codex.AckKey);
        PlayerPrefs.DeleteKey(WorldManager.PrefsHighestWorld);
        for (int i = 1; i <= shopingShips.RosterTotal; i++) PlayerPrefs.DeleteKey(ShipId.OwnedKey(i));
        Codex.Reload();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        try
        {
            Cast();
            Entries();
            Text();
            Unlocks();
            Music();
            Cloud();
        }
        finally
        {
            DeveloperUnlocks.SetEnabled(false);
        }
        Debug.Log("[CVT] failures: " + fails);
        return fails;
    }

    // Every world fields the same cast shape; the gap table is logged.
    static void Cast()
    {
        int worlds = WorldManager.Worlds.Length;
        Check("the codex knows every world: " + worlds + " ids, " + EnemyRoster.WorldKeys.Length + " roster keys, " + BossCatalog.All.Length + " bosses",
              CodexCatalogue.WorldIds.Length == worlds && EnemyRoster.WorldKeys.Length == worlds && BossCatalog.All.Length == worlds);
        Debug.Log("[CVT] world      enemies hazards mines elites boss  world-entry  music  boss-music  backdrops");
        for (int w = 0; w < worlds; w++)
        {
            string name = WorldManager.Worlds[w].displayName;
            var defs = EnemyRoster.All.Where(d => d.world == w).ToList();
            int rocks = defs.Count(d => d.role == EnemyRole.Rock), mines = defs.Count(d => d.role == EnemyRole.Mine);
            int fighters = defs.Count(d => d.role == EnemyRole.Fighter && !EnemyRoster.IsHelicopter(d)), chasers = defs.Count(d => d.role == EnemyRole.Chaser);
            int aliens = defs.Count(d => d.role == EnemyRole.Alien), bigs = defs.Count(d => d.role == EnemyRole.Big);
            int elites = EliteCatalog.All.Count(e => e.WorldIndex == w);
            var boss = BossCatalog.All[w];
            var track = CodexMusic.Find(CodexMusic.WorldId(name));
            var bossTrack = CodexMusic.Find(CodexMusic.BossId(name));
            int bd = CodexBackdrops.All.Count(b => b.worldIndex == w);
            Debug.Log("[CVT] " + name.PadRight(10) + " " + (fighters + chasers + aliens + bigs).ToString().PadLeft(7) + " " + rocks.ToString().PadLeft(7) + " " + mines.ToString().PadLeft(5) +
                      " " + elites.ToString().PadLeft(6) + " " + (Codex.Find(boss.id) != null ? "yes" : "NO ").PadRight(5) + " " + (Codex.Find(CodexCatalogue.WorldIds[w]) != null ? "yes" : "NO").PadRight(12) +
                      (track != null ? "yes" : "NO").PadRight(7) + (bossTrack != null ? "yes" : "NO").PadRight(12) + bd);
            Check(name + ": seven fighters / chasers / aliens / heavies (4 + 1 + 1 + 1, helicopters are extras), rocks, one mine",
                  fighters == 4 && chasers == 1 && aliens == 1 && bigs == 1 && rocks >= 3 && mines == 1);
            Check(name + ": at least four elites", elites >= 4);
            Check(name + ": a boss, a world entry, a song, a boss song and its backdrops (four; Storm ships three)",
                  Codex.Find(boss.id) != null && Codex.Find(CodexCatalogue.WorldIds[w]) != null && track != null && bossTrack != null && bd == (name == "Storm" ? StormTuning.Variants : 4));
            Check(name + ": its boss entry names the boss and carries its attacks", Codex.Find(boss.id).name == CodexCatalogue.BossDisplayName(boss.name) &&
                  Codex.Find(boss.id).lore.Contains("PHASES"));
        }
    }

    // Art, names and text: nothing blank, nothing pointing at a missing asset.
    static void Entries()
    {
        var noArt = new List<string>(); var noText = new List<string>(); var noAnim = new List<string>(); var names = new HashSet<string>(); var dupes = new List<string>();
        foreach (var e in Codex.Entries)
        {
            if (e.Sprite == null) noArt.Add(e.id);
            if (string.IsNullOrEmpty(e.name) || string.IsNullOrWhiteSpace(e.lore) || e.lore.Length < 20) noText.Add(e.id);
            if (!names.Add(e.category + "/" + e.name)) dupes.Add(e.id);
            if ((e.category == CodexCategory.Enemies || e.category == CodexCategory.Hazards) && CodexAnimations.For(e) == null) noAnim.Add(e.id);
        }
        Check("every entry resolves its art" + (noArt.Count > 0 ? " (missing: " + string.Join(", ", noArt) + ")" : ""), noArt.Count == 0);
        Check("every entry has a name and text" + (noText.Count > 0 ? " (" + string.Join(", ", noText) + ")" : ""), noText.Count == 0);
        Check("no two entries of a tab share a name" + (dupes.Count > 0 ? " (" + string.Join(", ", dupes) + ")" : ""), dupes.Count == 0);
        Check("every enemy / hazard animates in the codex" + (noAnim.Count > 0 ? " (" + string.Join(", ", noAnim) + ")" : ""), noAnim.Count == 0);
        Check("every world is live: the reachable count is the whole catalogue", Codex.ReachableCount == Codex.Entries.Length);
    }

    // Stale text: wording from when the game ended sooner.
    static void Text()
    {
        string[] stale = { "last gate", "final world", "last world", "four worlds", "fourth world", "only world" };
        var bad = new List<string>();
        foreach (var e in Codex.Entries)
            foreach (string s in stale)
                if (e.lore.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0) bad.Add(e.id + ": " + s);
        Check("no entry still talks about the game ending sooner" + (bad.Count > 0 ? " (" + string.Join("; ", bad) + ")" : ""), bad.Count == 0);
        Check("Ember's boss no longer guards 'the last gate before home'", !Codex.Find("boss_ember").lore.Contains("last gate before home"));
    }

    // Playing finds everything: the whole cast discovered once, listed, counted, and Completionist's target reached.
    static void Unlocks()
    {
        Check("a fresh profile has found only the Pilot's Log and the starter ship (" + Codex.DiscoveredCount + ")", Codex.DiscoveredCount <= 3);
        foreach (var e in Codex.Entries.ToList())
        {
            if (e.category == CodexCategory.Log || e.category == CodexCategory.Ships) continue;
            if (e.category == CodexCategory.Worlds) { Codex.Discover(e.id); continue; }
            Codex.Discover(e.id);
        }
        foreach (int ship in ShipId.All) PlayerPrefs.SetString(ShipId.OwnedKey(ship), "True");
        Check("meeting everything once discovers all " + Codex.Total + " entries (" + Codex.DiscoveredCount + ")", Codex.DiscoveredCount == Codex.Total);
        Check("Completionist's target is every entry (" + Codex.ReachableCount + " of " + Codex.Total + ")", Codex.ReachableCount == Codex.Total);
        Check("every boss is listed once met", BossCatalog.All.All(b => Codex.IsListed(Codex.Find(b.id))));
        var field = AchievementTracker.AnyWorldCatalogued();
        Check("Field Guide sees a fully catalogued world", field);
        PlayerPrefs.SetString(Codex.PrefsKey, "");
        Codex.Reload();
    }

    // Each world song and each boss song is heard through the real hooks (WorldMusic.Apply / BeginBoss).
    static void Music()
    {
        foreach (var theme in WorldManager.Worlds)
        {
            CodexMusic.HeardWorld(theme);
            CodexMusic.HeardBoss(theme);
        }
        Codex.HearTrack(CodexMusic.CreditsId);   // MenuMusic does this when Credits starts its song
        Check("every world's song and boss song unlocks through the hooks (" + CodexMusic.UnlockedCount + " / " + CodexMusic.ListedCount + ")",
              CodexMusic.UnlockedCount == CodexMusic.ListedCount && CodexMusic.ListedCount == 3 + 2 * WorldManager.Worlds.Length);
        Check("every song has a real name, not just its world's", WorldManager.Worlds.All(w => CodexMusic.Find(CodexMusic.WorldId(w.displayName)).title != w.displayName));
        PlayerPrefs.SetString(Codex.PrefsKey, "");
        Codex.Reload();
    }

    // The cloud save carries entries, tracks and backdrops in the one codexSeen set and merges them by union.
    static void Cloud()
    {
        PlayerPrefs.SetString(Codex.PrefsKey, "enemy_chaser,music_world_ember,bd_frost_2");
        var local = ProgressSnapshot.Capture(1000);
        Check("the snapshot carries entries, tracks and backdrops", local.codexSeen.Contains("enemy_chaser") && local.codexSeen.Contains("music_world_ember") && local.codexSeen.Contains("bd_frost_2"));
        var cloud = ProgressSnapshot.Fresh(900);
        cloud.codexSeen = new[] { "bd_storm_2", "music_boss_storm", "hazard_storm_mine" };
        string[] seen, fresh, acked;
        ProgressMerge.MergeCodex(local, cloud, out seen, out fresh, out acked);
        Check("merging two devices unions them, nothing is lost", new[] { "enemy_chaser", "music_world_ember", "bd_frost_2", "bd_storm_2", "music_boss_storm", "hazard_storm_mine" }.All(seen.Contains));
        Check("... and backdrops / tracks never become NEW", !fresh.Any(id => id.StartsWith("bd_") || id.StartsWith("music_")));
        PlayerPrefs.SetString(Codex.PrefsKey, string.Join(",", seen));
        Codex.Reload();
        Check("restoring it unlocks them here", CodexBackdrops.IsUnlocked(CodexBackdrops.Find("Storm", 2)) && CodexMusic.IsUnlocked(CodexMusic.Find("music_boss_storm")));
    }
}
