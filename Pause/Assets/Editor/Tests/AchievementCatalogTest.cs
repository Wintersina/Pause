using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// The 60-achievement table: counts, uniqueness, limits, points, targets, the
// dormant Tide pair, store ids, badge art (painted or placeholder) and the CSV export.
public static class AchievementCatalogTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[ACH] PASS  " : "[ACH] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        bool tide = WorldManager.TideEnabled;
        try
        {
            WorldManager.TideEnabled = false;
            Catalogue();
            Dormant();
            StoreIds();
            Icons();
            Art();
            Export();
        }
        finally { WorldManager.TideEnabled = tide; }
        Debug.Log("[ACH] failures: " + fails);
        return fails;
    }

    static void Catalogue()
    {
        var all = AchievementCatalog.All;
        Check("60 achievements", all.Length == 60 && AchievementCatalog.Count == 60);
        Check("unique ids", all.Select(d => d.id).Distinct().Count() == 60);
        Check("ids are snake_case", all.All(d => d.id.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')));
        Check("titles <= 24 chars and unique", all.All(d => d.title.Length > 0 && d.title.Length <= 24) && all.Select(d => d.title).Distinct().Count() == 60);
        Check("descriptions <= 100 chars", all.All(d => d.description.Length > 0 && d.description.Length <= 100));
        Check("points are multiples of 5 within 5..40", all.All(d => d.points % 5 == 0 && d.points >= 5 && d.points <= 40));
        Check("points total exactly 1000 (" + all.Sum(d => d.points) + ")", all.Sum(d => d.points) == AchievementCatalog.TotalPoints);
        Check("every achievement pays 25 star dust", AchievementCatalog.RewardDust == 25);
        Check("3 hidden", all.Count(d => d.hidden) == 3 && all.Where(d => d.hidden).All(d => d.id == "mega_domino" || d.id == "deaths_100" || d.id == "pause_perfect_dodge"));
        Check("every achievement names its tracking hook", all.All(d => !string.IsNullOrEmpty(d.hook)));
        Check("counter achievements have a target, one-shots none",
              all.All(d => d.IsCounter ? d.Target >= 1 : d.Target == 0));
        Check("fixed targets match the design (kills 100/1000/5000, stars 150/1000/5000, rocks 500, mines 25, deaths 10/100)",
              Target("kills_100") == 100 && Target("kills_1000") == 1000 && Target("kills_5000") == 5000 &&
              Target("stars_150") == 150 && Target("stars_1000") == 1000 && Target("stars_5000") == 5000 &&
              Target("rocks_500") == 500 && Target("mines_25") == 25 && Target("deaths_10") == 10 && Target("deaths_100") == 100 &&
              Target("elite_10") == 10 && Target("elite_50") == 50 && Target("codex_10") == 10 && Target("codex_50") == 50 &&
              Target("ship_half") == 8 && Target("dust_spent_10000") == 10000 && Target("pause_blink_100") == 100 &&
              Target("loop_1") == 1 && Target("loop_2") == 2 && Target("loop_5") == 5 && Target("chain_10") == 10);
        Check("dynamic targets follow the game: bosses " + Target("boss_all") + ", ships " + Target("ship_all") + ", codex " + Target("codex_complete"),
              Target("boss_all") == WorldManager.LiveWorldCount && Target("ship_all") == ShipId.Count &&
              Target("codex_complete") == Codex.Entries.Length && Target("elite_space_all") == Elites(0) &&
              Target("elite_frost_all") == Elites(1) && Target("elite_verdant_all") == Elites(2) && Target("elite_ember_all") == Elites(3));
        Check("elites per world: Space 4, Frost 5, Verdant >= 1, Ember 6 (" + Elites(0) + "," + Elites(1) + "," + Elites(2) + "," + Elites(3) + ")",
              Elites(0) >= 4 && Elites(1) >= 5 && Elites(2) >= 1 && Elites(3) >= 6);
        Check("score thresholds ascend and sit inside what a run scores (ScoreRules: ~6,600 a first pass)",
              Target("score_10k") < Target("score_50k") && Target("score_50k") < Target("score_150k") && Target("score_10k") <= 3000);
        Check("the Codex is big enough for Cartographer (" + Codex.Entries.Length + " entries)", Codex.Entries.Length > 50);
        Check("Find works for every id and not for junk", all.All(d => AchievementCatalog.Find(d.id) == d) && AchievementCatalog.Find("nope") == null && AchievementCatalog.Find(null) == null);
        Check("counters are shared (kills feeds 3)", AchievementCatalog.ForCounter("kills").Length == 3 && AchievementCatalog.ForCounter("stars").Length == 3);
        Check("7 sections, each with achievements", System.Enum.GetValues(typeof(AchievementSection)).Cast<AchievementSection>().All(s => all.Any(d => d.Section == s)));
        Check("max 7 sections fits the codex (MaxSections)", CodexAchievementsView.BuildSections().Count <= CodexPanel.MaxSections);
        Check("every group is used", System.Enum.GetValues(typeof(AchievementGroup)).Cast<AchievementGroup>().All(g => all.Any(d => d.group == g)));
        Check("every tier is used", System.Enum.GetValues(typeof(AchievementTier)).Cast<AchievementTier>().All(t => all.Any(d => d.tier == t)));
    }

    static int Target(string id) { return AchievementCatalog.Find(id).Target; }

    static int Elites(int world) { return EliteCatalog.All.Count(e => e.WorldIndex == world); }

    static void Dormant()
    {
        var dormant = AchievementCatalog.All.Where(d => d.requiresTide).Select(d => d.id).OrderBy(x => x).ToArray();
        Check("exactly the two Tide achievements are dormant", dormant.Length == 2 && dormant[0] == "boss_tide" && dormant[1] == "world_tide_reached");
        Check("58 live while Tide is off", AchievementCatalog.ActiveCount == 58);
        AchievementStore.ResetAll();
        var tideWorld = AchievementCatalog.Find("world_tide_reached");
        Check("a dormant achievement cannot be unlocked", !AchievementStore.Unlock(tideWorld) && !AchievementStore.IsUnlocked(tideWorld));
        Check("... is not listed in the Codex tab", CodexAchievementsView.BuildSections().SelectMany(s => s.defs).All(d => !d.requiresTide) &&
              CodexAchievementsView.BuildSections().Sum(s => s.defs.Count) == 58);
        PlayerPrefs.SetInt(tideWorld.unlockedKey, 1);   // e.g. restored from the cloud
        Check("... and not claimable even if its flag arrives", !AchievementStore.IsClaimable(tideWorld) && AchievementStore.ClaimableCount == 0 &&
              AchievementStore.Claim(tideWorld) == 0);
        Check("... or counted in x/N", AchievementStore.UnlockedCount == 0);
        WorldManager.TideEnabled = true;
        Check("Tide on: all 60 live, listed, and the restored one is claimable",
              AchievementCatalog.ActiveCount == 60 && CodexAchievementsView.BuildSections().Sum(s => s.defs.Count) == 60 &&
              AchievementStore.IsClaimable(tideWorld) && AchievementStore.UnlockedCount == 1);
        Check("... boss_all follows the live world count", Target("boss_all") == WorldManager.LiveWorldCount);
        WorldManager.TideEnabled = false;
        AchievementStore.ResetAll();
    }

    // The 60 pixel-art badges: Art/Resources/Achievements/<id>.png, 128 x 128 RGBA with real transparency,
    // imported as point-filtered sprites, and nothing in the folder that is not an achievement.
    static void Icons()
    {
        const string folder = "Assets/Art/Resources/Achievements/";
        var ids = AchievementCatalog.All.Select(d => d.id).ToList();
        var missing = new List<string>(); var badSize = new List<string>(); var noAlpha = new List<string>(); var badImport = new List<string>();
        foreach (string id in ids)
        {
            string path = folder + id + ".png";
            if (!File.Exists(path)) { missing.Add(id); continue; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(path));
            if (tex.width != AchievementArt.Size || tex.height != AchievementArt.Size) badSize.Add(id);
            bool transparent = false, opaque = false;
            foreach (var c in tex.GetPixels32()) { if (c.a < 255) transparent = true; if (c.a > 0) opaque = true; }
            if (!transparent || !opaque) noAlpha.Add(id);
            Object.DestroyImmediate(tex);
            var imp = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
            if (imp == null || imp.textureType != UnityEditor.TextureImporterType.Sprite || imp.filterMode != FilterMode.Point ||
                imp.mipmapEnabled || !imp.alphaIsTransparency || imp.textureCompression != UnityEditor.TextureImporterCompression.Uncompressed)
                badImport.Add(id);
        }
        Check("every one of the 60 ids (dormant Tide included) has an icon (missing: " + string.Join(",", missing) + ")", missing.Count == 0 && ids.Count == 60);
        Check("all icons are 128 x 128 (bad: " + string.Join(",", badSize) + ")", badSize.Count == 0);
        Check("all icons have a transparent corner area and an opaque body (bad: " + string.Join(",", noAlpha) + ")", noAlpha.Count == 0);
        Check("all icons import as point-filtered, uncompressed, mip-less sprites (bad: " + string.Join(",", badImport) + ")", badImport.Count == 0);
        var orphans = Directory.GetFiles(folder, "*.png").Select(f => Path.GetFileNameWithoutExtension(f)).Where(n => !ids.Contains(n)).ToList();
        Check("no icon without an achievement (orphans: " + string.Join(",", orphans) + ")", orphans.Count == 0);
        AchievementArt.Reload();
        Check("AchievementArt.HasArt for all 60", ids.All(AchievementArt.HasArt));
        string setup = File.ReadAllText("../docs/achievements-store-setup.md");
        Check("docs/achievements-store-setup.md documents installing the icons", setup.Contains("Art/Resources/Achievements") && setup.Contains("play-icons-512"));
    }

    static void StoreIds()
    {
        var defs = AchievementCatalog.All;
        var android = defs.Select(AchievementIds.AndroidId).ToList();
        var ios = defs.Select(AchievementIds.IosId).ToList();
        Check("store ids: 60 unique Android ids", android.Distinct().Count() == 60 && android.All(s => !string.IsNullOrEmpty(s)));
        Check("store ids: 60 unique iOS ids", ios.Distinct().Count() == 60);
        Check("store ids: all 60 have a real Android id from AchievementStoreIds.csv", android.Count(s => s.StartsWith("CgkI")) == 60);
        Check("store ids: none is a placeholder", android.Count(AchievementIds.IsPlaceholder) == 0);
        Check("store ids: the override table fills in real ids and ignores junk",
              Override("loop_1,CgkIfake\nunknown_id,CgkIx\n#c\nloop_2,TODO_x\n").Equals("CgkIfake|" + AchievementIds.Placeholder("loop_2")));
        Check("store ids: no clash between iOS and Android namespaces", !android.Intersect(ios).Any());
    }

    static string Override(string csv)
    {
        var map = new Dictionary<string, string>();
        foreach (var d in AchievementCatalog.All) map[d.id] = AchievementIds.Placeholder(d.id);
        AchievementIds.ApplyOverrides(map, csv);
        return map["loop_1"] + "|" + map["loop_2"];
    }

    static void Art()
    {
        AchievementArt.Reload();
        int painted = 0;
        bool ok = true, size = true;
        foreach (var d in AchievementCatalog.All)
        {
            var s = AchievementArt.For(d);
            ok &= s != null;
            bool has = AchievementArt.HasArt(d.id);
            if (has) { painted++; size &= s.rect.width == 128 && s.rect.height == 128; }
            else ok &= AchievementArt.IsPlaceholder(s);
        }
        Check("every badge resolves to painted art or a placeholder medal (" + painted + " painted)", ok);
        Check("painted badges are 128x128", size);
        var a = AchievementArt.Placeholder(AchievementTier.Bronze);
        var b = AchievementArt.Placeholder(AchievementTier.Platinum);
        Check("placeholder medals are 128px, cached, and tier-coloured",
              a.rect.width == 128 && a == AchievementArt.Placeholder(AchievementTier.Bronze) && a != b &&
              a.texture.GetPixel(64, 6) != b.texture.GetPixel(64, 6));
        Check("placeholder corner is transparent, centre is the disc", a.texture.GetPixel(0, 0).a == 0f && a.texture.GetPixel(64, 64).a == 1f);
        Check("unknown id falls back to a medal, never null", AchievementArt.For("nope") != null);
    }

    static void Export()
    {
        string play = AchievementStoreExport.PlayConsoleCsv(), gc = AchievementStoreExport.GameCenterCsv();
        var playLines = play.Split('\n').Where(l => l.Length > 0).ToArray();
        var gcLines = gc.Split('\n').Where(l => l.Length > 0).ToArray();
        Check("export: Play CSV has a header and 60 rows", playLines.Length == 61 && playLines[0].StartsWith("Order,Name,Description,Points"));
        Check("export: Game Center CSV has a header and 60 rows", gcLines.Length == 61 && gcLines[0].StartsWith("Reference name,Achievement ID"));
        Check("export: quotes commas (Hundredfold)", play.Contains("\"Destroy 1,000 enemies.\""));
        Check("export: hidden rows (3 + 2 dormant)", playLines.Count(l => l.Contains(",Hidden,")) == 5 && gcLines.Count(l => l.Contains(",Yes,No,")) == 5);
        Check("export: incremental steps for kills_1000", playLines.Any(l => l.Contains("Hundredfold") && l.Contains(",10,1000,Revealed,kills_1000.png")));
        Check("export: Game Center ids use the ach_ scheme", gcLines.Skip(1).All(l => l.Contains("me.hapticgate.pause.ach_")));
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", AchievementStoreExport.Folder));
        Check("export: docs/achievements-export holds the generated files (run Pause > Achievements > Export store CSV)",
              File.Exists(Path.Combine(dir, "play-console.csv")) && File.Exists(Path.Combine(dir, "game-center.csv")) &&
              File.ReadAllText(Path.Combine(dir, "play-console.csv")) == play);
    }
}
