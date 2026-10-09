using System;
using System.Collections.Generic;

// The 60 achievements in one reviewable table (docs/achievements-redesign.md
// has the design; this is its code form, and the store CSV exporter and the
// tests read it). Ids are stable snake_case keys: they name the saved state,
// the badge art (Resources/Achievements/<id>) and the store rows.
//
// 58 are live; the two Tide ones are dormant (hidden, untracked, not
// claimable, not counted in "x/N") until WorldManager.TideEnabled.
//
// Counters (lifetime, shared between achievements):
//   kills rocks mines elites stars deaths blinks spent   Add()ed per event
//   ships codex bosses elite_w0..3                       recounted from owned state
//   best_chain best_loop best_score                      SetAtLeast()
public static class AchievementCatalog
{
    // The flat payout of every achievement (star dust).
    public const int RewardDust = 25;
    public const int Count = 60;
    public const int TotalPoints = 1000;

    // Single-run score thresholds. The design table had 10k / 50k / 150k, but a
    // whole first pass Space -> Ember scores ~6,600 and a second loop ~8,100
    // (ScoreRules), so they were recalibrated (ids keep their art names).
    public const int ScoreRookie = 2500, ScoreAce = 8000, ScoreLegend = 30000;

    public const string CKills = "kills", CRocks = "rocks", CMines = "mines", CElites = "elites", CStars = "stars",
        CDeaths = "deaths", CBlinks = "blinks", CSpent = "spent", CShips = "ships", CCodex = "codex",
        CBosses = "bosses", CChain = "best_chain", CLoop = "best_loop", CScore = "best_score";

    public static string EliteCounter(int world) { return "elite_w" + world; }
    public const int EliteWorlds = 4;

    static AchievementDef D(string id, string title, string desc, int pts, AchievementTier tier, AchievementGroup group,
                            string hook, string counter = null, int target = 0, Func<int> dyn = null, int steps = 0,
                            bool hidden = false, bool tide = false)
    {
        return new AchievementDef(id, title, desc, pts, tier, group, hook, counter, target, dyn, steps, hidden, tide);
    }

    const AchievementTier B = AchievementTier.Bronze, S = AchievementTier.Silver, G = AchievementTier.Gold, P = AchievementTier.Platinum;
    const AchievementGroup Meta = AchievementGroup.Meta, World = AchievementGroup.World, Boss = AchievementGroup.Boss,
        Elite = AchievementGroup.Elite, Enemy = AchievementGroup.Enemy, Cdx = AchievementGroup.Codex,
        Coll = AchievementGroup.Collection, Dust = AchievementGroup.Dust, Skill = AchievementGroup.Skill, Pause = AchievementGroup.Pause;

    // How many elites a world defines (grows as elites are added).
    static int EliteCount(int world)
    {
        int n = 0;
        var all = EliteCatalog.All;
        for (int i = 0; i < all.Length; i++) if (all[i].WorldIndex == world) n++;
        return n;
    }

    public static readonly AchievementDef[] All =
    {
        D("meta_first_flight", "First Flight", "Finish the tutorial and take your first real run.", 5, B, Meta, "Hints / TutorialSkip -> AchievementTracker.OnTutorialDone"),
        D("meta_logged_on", "Logged On", "Sign in to Google Play Games or Game Center.", 5, B, Meta, "CloudSync.OnSignedIn -> AchievementTracker.OnSignedIn"),
        D("world_frost_reached", "Cold Front", "Reach the Frost world.", 10, B, World, "WorldManager.Advance/Start -> OnWorldEntered(1)"),
        D("world_verdant_reached", "Into the Green", "Reach the Verdant world.", 10, S, World, "WorldManager.Advance/Start -> OnWorldEntered(2)"),
        D("world_ember_reached", "Trial by Fire", "Reach the Ember world.", 15, S, World, "WorldManager.Advance/Start -> OnWorldEntered(3)"),
        D("world_tide_reached", "Deep Water", "Reach the Tide world.", 20, S, World, "WorldManager.Advance/Start -> OnWorldEntered(4)", tide: true),
        D("loop_1", "Full Circle", "Complete a full loop of every world.", 25, G, World, "WorldManager.Advance loop -> OnLoop", CLoop, 1),
        D("loop_2", "Second Lap", "Complete two loops in a single run.", 25, G, World, "WorldManager.Advance loop -> OnLoop", CLoop, 2),
        D("loop_5", "Orbit Lifer", "Complete five loops in a single run.", 40, P, World, "WorldManager.Advance loop -> OnLoop", CLoop, 5),
        D("boss_space", "Archon Down", "Defeat the Void Archon.", 10, S, Boss, "BossEncounter.BeginOutro -> OnBossOutro(0)"),
        D("boss_frost", "Leviathan Slain", "Defeat the Hoarfrost Leviathan.", 15, S, Boss, "BossEncounter.BeginOutro -> OnBossOutro(1)"),
        D("boss_verdant", "Bloom Queen Felled", "Defeat the Bloom Queen.", 20, S, Boss, "BossEncounter.BeginOutro -> OnBossOutro(2)"),
        D("boss_ember", "Drake Slayer", "Defeat the Cinder Drake.", 25, G, Boss, "BossEncounter.BeginOutro -> OnBossOutro(3)"),
        D("boss_tide", "Tide Turner", "Defeat the Tide boss.", 25, G, Boss, "BossEncounter.BeginOutro -> OnBossOutro(4)", tide: true),
        D("boss_no_hit", "Untouchable", "Defeat a boss without losing a heart.", 25, G, Boss, "AchievementEvents.PlayerHurt + BossEncounter fight start/outro"),
        D("boss_all", "Boss Collector", "Defeat every live world's boss.", 35, P, Boss, "BossEncounter.BeginOutro (distinct worlds)", CBosses, 0, () => WorldManager.LiveWorldCount, steps: 4),
        D("elite_first", "Elite Hunter", "Destroy your first elite ship.", 10, B, Elite, "EliteShip.Died", CElites, 1),
        D("elite_10", "Veteran Hunter", "Destroy 10 elite ships.", 10, S, Elite, "EliteShip.Died", CElites, 10, steps: 10),
        D("elite_50", "Elite Exterminator", "Destroy 50 elite ships.", 25, G, Elite, "EliteShip.Died", CElites, 50, steps: 50),
        D("elite_space_all", "Space Elites", "Down every Space elite.", 15, S, Elite, "EliteShip.Died (EliteCatalog world 0)", "elite_w0", 0, () => EliteCount(0), steps: 4),
        D("elite_frost_all", "Frost Elites", "Down every Frost elite.", 20, S, Elite, "EliteShip.Died (EliteCatalog world 1)", "elite_w1", 0, () => EliteCount(1), steps: 5),
        D("elite_verdant_all", "Verdant Elites", "Down the Verdant elite.", 15, S, Elite, "EliteShip.Died (EliteCatalog world 2)", "elite_w2", 0, () => EliteCount(2)),
        D("elite_ember_all", "Ember Elites", "Down every Ember elite.", 25, G, Elite, "EliteShip.Died (EliteCatalog world 3)", "elite_w3", 0, () => EliteCount(3), steps: 6),
        D("elite_blink", "Blink Strike", "Destroy an elite with a pause blink.", 10, S, Elite, "EliteShip.Died (cause Teleport)"),
        D("kills_100", "Gunner", "Destroy 100 enemies.", 5, B, Enemy, "collisionDetection.AwardDestroyedTarget", CKills, 100, steps: 100),
        D("kills_1000", "Hundredfold", "Destroy 1,000 enemies.", 10, S, Enemy, "collisionDetection.AwardDestroyedTarget", CKills, 1000, steps: 1000),
        D("kills_5000", "Warlord", "Destroy 5,000 enemies.", 25, G, Enemy, "collisionDetection.AwardDestroyedTarget", CKills, 5000, steps: 5000),
        D("rocks_500", "Rock Breaker", "Destroy 500 asteroids and rocks.", 10, B, Enemy, "collisionDetection.AwardDestroyedTarget (tag Astr)", CRocks, 500, steps: 500),
        D("mines_25", "Minesweeper", "Destroy 25 rail mines.", 10, B, Enemy, "collisionDetection.AwardDestroyedTarget (EnemyRole.Mine)", CMines, 25, steps: 25),
        D("chain_10", "Chain Reaction", "Reach a 10-kill chain.", 10, S, Enemy, "RunScore.OnKill chain", CChain, 10),
        D("mega_domino", "MEGA DOMINO", "Trigger a MEGA DOMINO.", 15, S, Enemy, "DeathCrashDomino.MegaDominoStarted", hidden: true),
        D("codex_10", "Curious Pilot", "Discover 10 Codex entries.", 5, B, Cdx, "Codex.Discovered", CCodex, 10, steps: 10),
        D("codex_50", "Cartographer", "Discover 50 Codex entries.", 10, S, Cdx, "Codex.Discovered", CCodex, 50, steps: 50),
        D("codex_field_guide", "Field Guide", "Fully catalogue one world's enemies and hazards.", 15, S, Cdx, "Codex.Discovered (per-world sets)"),
        D("codex_complete", "Completionist", "Discover every Codex entry.", 35, P, Cdx, "Codex.Discovered", CCodex, 0, () => Codex.Entries.Length),
        D("ship_first", "Hangar Debut", "Buy your first ship.", 5, B, Coll, "shopingShips.TryPurchase"),
        D("ship_half", "Squadron", "Own half the hangar.", 15, S, Coll, "shopingShips.TryPurchase", CShips, 8, steps: 8),
        D("ship_all", "Fleet Admiral", "Own every ship.", 40, P, Coll, "shopingShips.TryPurchase", CShips, 0, () => ShipId.Count),
        D("skin_first", "Fresh Paint", "Buy your first ship skin.", 5, B, Coll, "ShipSkins.TryPurchase"),
        D("skin_special", "Special Edition", "Buy a Special skin.", 10, S, Coll, "ShipSkins.TryPurchase"),
        D("skin_full_set", "Full Wardrobe", "Own all skins for one ship.", 25, G, Coll, "ShipSkins.TryPurchase"),
        D("secret_power_first", "Secret Power", "Unleash a ship's secret power.", 10, B, Coll, "SecretPowerController.Fire"),
        D("stars_150", "Dust Gatherer", "Collect 150 star dust pickups.", 10, B, Dust, "collisionDetection pickup", CStars, 150, steps: 150),
        D("stars_1000", "Dust Hoarder", "Collect 1,000 star dust pickups.", 20, S, Dust, "collisionDetection pickup", CStars, 1000, steps: 1000),
        D("stars_5000", "Dust Baron", "Collect 5,000 star dust pickups.", 25, G, Dust, "collisionDetection pickup", CStars, 5000, steps: 5000),
        D("dust_spent_10000", "Big Spender", "Spend 10,000 star dust in the shop.", 15, S, Dust, "shopingShips/ShipSkins.TryPurchase", CSpent, 10000, steps: 10000),
        D("score_10k", "Rookie Score", "Score " + ScoreRookie.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " points in a run.", 10, B, Skill, "RunScore.Scored / Bank", CScore, ScoreRookie),
        D("score_50k", "Ace Score", "Score " + ScoreAce.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " points in a run.", 20, S, Skill, "RunScore.Scored / Bank", CScore, ScoreAce),
        D("score_150k", "Legend Score", "Score " + ScoreLegend.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " points in a run.", 35, P, Skill, "RunScore.Scored / Bank", CScore, ScoreLegend),
        D("speed_flash", "Flash", "Reach the speed cap.", 15, S, Skill, "SpeedMilestones.Step"),
        D("speed_speedster", "Speedster", "Break the speed limit.", 20, S, Skill, "SpeedMilestones.Step"),
        D("speed_super_sonic", "Super Sonic", "Max out the limit break.", 35, P, Skill, "SpeedMilestones.Step"),
        D("deaths_10", "Crash Course", "Crash 10 times.", 5, B, Skill, "collisionDetection death", CDeaths, 10, steps: 10),
        D("deaths_100", "Frequent Flyer", "Crash 100 times.", 10, S, Skill, "collisionDetection death", CDeaths, 100, steps: 100, hidden: true),
        D("pause_first", "Paused", "Spend your first pause.", 5, B, Pause, "score.pauseCounterFunction"),
        D("pause_blink_kill", "Correct Pause", "Destroy an enemy by blinking onto it.", 10, B, Pause, "AwardDestroyedTarget bonusPoints > 0"),
        D("pause_blink_100", "Blink Master", "Blink 100 times.", 10, S, Pause, "movePlayer teleport", CBlinks, 100, steps: 100),
        D("pause_no_pause_world", "Hold Your Breath", "Clear a world without spending a pause.", 20, S, Pause, "score.pauseCounterFunction + WorldManager.Advance"),
        D("pause_hoarder", "Time Banker", "Hold 15 pauses at once.", 10, S, Pause, "score.incromentPause"),
        D("pause_perfect_dodge", "Perfect Pause", "Blink out of a hostile shot's path.", 25, G, Pause, "movePlayer teleport probe + AchievementEvents.PlayerHurt", hidden: true),
    };

    // Points that the stores see (Play Games / Game Center: tier-independent).
    static Dictionary<string, AchievementDef> byId;
    static Dictionary<string, AchievementDef[]> byCounter;

    public static AchievementDef Find(string id)
    {
        if (byId == null)
        {
            var map = new Dictionary<string, AchievementDef>(All.Length);
            for (int i = 0; i < All.Length; i++) map[All[i].id] = All[i];
            byId = map;
        }
        AchievementDef def;
        return id != null && byId.TryGetValue(id, out def) ? def : null;
    }

    // The achievements that read a counter (empty array: none).
    public static AchievementDef[] ForCounter(string counter)
    {
        if (byCounter == null)
        {
            var lists = new Dictionary<string, List<AchievementDef>>();
            foreach (var d in All)
            {
                if (d.counter == null) continue;
                List<AchievementDef> l;
                if (!lists.TryGetValue(d.counter, out l)) lists[d.counter] = l = new List<AchievementDef>();
                l.Add(d);
            }
            var map = new Dictionary<string, AchievementDef[]>(lists.Count);
            foreach (var pair in lists) map[pair.Key] = pair.Value.ToArray();
            byCounter = map;
        }
        AchievementDef[] defs;
        return counter != null && byCounter.TryGetValue(counter, out defs) ? defs : NoDefs;
    }

    static readonly AchievementDef[] NoDefs = new AchievementDef[0];

    // Live now (a dormant Tide achievement is not).
    public static bool IsActive(AchievementDef def)
    {
        return def != null && (!def.requiresTide || WorldManager.TideEnabled);
    }

    // The achievements the player can see and earn right now.
    public static int ActiveCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < All.Length; i++) if (IsActive(All[i])) n++;
            return n;
        }
    }

    public static string SectionLabel(AchievementSection s)
    {
        switch (s)
        {
            case AchievementSection.Journey: return "JOURNEY";
            case AchievementSection.Bosses: return "BOSSES";
            case AchievementSection.Elites: return "ELITES";
            case AchievementSection.Combat: return "COMBAT";
            case AchievementSection.Pause: return "PAUSE";
            case AchievementSection.Codex: return "CODEX";
            default: return "COLLECTION";
        }
    }
}
