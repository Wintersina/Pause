using UnityEditor.SceneManagement;
using UnityEngine;

// The authored coin percentages must be actual health removed, through the live damage route.
public static class QuarterHeartsTest
{
    static int fails;
    static void Check(string label, bool ok)
    {
        Debug.Log((ok ? "[QUARTER] PASS " : "[QUARTER] FAIL ") + label);
        if (!ok) fails++;
    }
    public static void Run() { TestHarness.Exit(Execute()); }
    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try { Tables(); Ordinary(); Boss(); Drops(); }
        finally
        {
            BossEncounter.ResetRun();
            Boosters.SetRunRushForTest(false);
            WorldManager.ClearReplayWorld();
            ShipAttackHits.HitFrom = null;
        }
        Debug.Log("[QUARTER] failures: " + fails);
        return fails;
    }
    static void Scene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
        cam.tag = "MainCamera";
        cam.orthographic = true; cam.orthographicSize = 5f; cam.aspect = .5625f;
        cam.transform.position = new Vector3(0, 0, -10);
        buttonClicks.playerDied = false;
        startMenu.youAreInTutorial = false;
        PlayerPrefs.SetString("HasDoneTut", "true");
        Time.timeScale = 1f;
    }
    static void Tables()
    {
        bool quarters = true, upgrades = true, lore = true;
        for (int level = 1; level <= 10; level++)
        {
            foreach (var table in new[] { ShotAtomTuning.TwinDamage, ShotAtomTuning.SplitDamage, ShotAtomTuning.ArcDamage, ShotAtomTuning.RapidBonus })
                quarters &= Mathf.Abs(table[level] * 4f - Mathf.Round(table[level] * 4f)) < .0001f;
            if (level > 1)
                upgrades &= ShotAtomTuning.TwinDamage[level] * ShotAtomTuning.TwinShots[level] >= ShotAtomTuning.TwinDamage[level - 1] * ShotAtomTuning.TwinShots[level - 1] &&
                    ShotAtomTuning.SplitDamage[level] * ShotAtomTuning.SplitShards[level] >= ShotAtomTuning.SplitDamage[level - 1] * ShotAtomTuning.SplitShards[level - 1];
            lore &= CodexCatalogue.ShotAtomLevelText(ShotAtom.Arc, level).Contains(ShotAtomTuning.ArcRange[level].ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " range");
            foreach (var atom in new[] { ShotAtom.Twin, ShotAtom.Split, ShotAtom.Arc })
            {
                var table = atom == ShotAtom.Twin ? ShotAtomTuning.TwinDamage : atom == ShotAtom.Split ? ShotAtomTuning.SplitDamage : ShotAtomTuning.ArcDamage;
                lore &= CodexCatalogue.ShotAtomLevelText(atom, level).Contains(Mathf.RoundToInt(table[level] * 100f) + "% of a heart");
            }
        }
        Check("all ten coin levels use quarter percentages and preserve volley damage on upgrade", quarters && upgrades);
        Check("every damage coin's Codex text reports its actual health value at every level", lore);
        Check("requested examples: SPLIT 50% at L1 and 75% at L10; TWIN 75% at L10", ShotAtomTuning.SplitDamage[1] == .5f && ShotAtomTuning.SplitDamage[10] == .75f && ShotAtomTuning.TwinDamage[10] == .75f);
    }
    static void Ordinary()
    {
        Scene();
        RunScore.EndRun(RunScore.RunId); RunScore.BeginRun(true, true);
        var go = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Fighter), Vector3.up, Quaternion.identity, 1f, true);
        ClearTarget.Ensure(go);
        var h = go.GetComponent<EnemyHearts>();
        int kills = ShipAttackHits.Kills;
        float budget = 0f;
        ShipAttackHits.Hit(go, 1, .25f, ref budget, HitKind.Drone);
        Check("ordinary enemy loses exactly one quarter and displays three quarters on the last heart", h.QuartersLeft == 7 && h.Health == 1.75f && h.View.VisibleQuarters(1) == 3 && h.View.VisibleQuarters(0) == 4);
        ShipAttackHits.Hit(go, 1, .5f, ref budget, HitKind.Drone);
        Check("a same-frame 50% hit removes two more quarters; detached pieces coexist", h.QuartersLeft == 5 && h.View.VisibleQuarters(1) == 1 && h.View.ActiveQuarterBreaks == 3);
        ShipAttackHits.Hit(go, 1, .75f, ref budget, HitKind.Drone);
        Check("a same-frame 75% hit removes three quarters without restoring the broken heart", h.QuartersLeft == 2 && h.View.VisibleQuarters(0) == 2 && h.View.VisibleQuarters(1) == 0);
        int remnantsBefore = Object.FindObjectsByType<HeartQuarterRemnant>(FindObjectsSortMode.None).Length;
        bool killed = ShipAttackHits.Hit(go, 1, .5f, ref budget, HitKind.Drone);
        Check("the final half heart kills and pays once", killed && go == null && ShipAttackHits.Kills == kills + 1);
        Check("quarter fragments survive the enemy's destruction", Object.FindObjectsByType<HeartQuarterRemnant>(FindObjectsSortMode.None).Length == remnantsBefore + 1);
        var remnant = Object.FindFirstObjectByType<HeartQuarterRemnant>();
        bool visible = remnant != null;
        if (remnant != null)
        {
            foreach (var part in remnant.GetComponentsInChildren<SpriteRenderer>())
                visible &= part.enabled && part.color.a >= .9f && part.sprite != null;
            var partRenderer = remnant.GetComponentInChildren<SpriteRenderer>();
            var before = partRenderer.transform.position;
            remnant.Step(.2f);
            visible &= remnant != null && (partRenderer.transform.position - before).sqrMagnitude > .0001f;
            remnant.Step(HeartQuarterFrames.BreakSeconds);
        }
        Check("the detached animation is bright, survives at least .2 seconds, then cleans itself up", visible && Object.FindObjectsByType<HeartQuarterRemnant>(FindObjectsSortMode.None).Length == remnantsBefore);
    }
    static void Boss()
    {
        Scene();
        WorldManager.CurrentIndex = 0;
        BossEncounter.Begin(0, null);
        var fight = BossEncounter.Instance;
        for (int i = 0; i < 401 && fight.State != BossEncounter.Phase.Fight; i++) fight.Step(.1f, 1f);
        var target = Object.FindFirstObjectByType<BossTarget>();
        Check("boss fixture reaches its fight", target != null && fight.State == BossEncounter.Phase.Fight);
        if (target == null) return;
        float start = fight.Damage, budget = 0f;
        int before = fight.QuartersLeft;
        bool exact = true;
        foreach (float amount in new[] { .25f, .5f, .75f, 1f })
        {
            ShipAttackHits.Hit(target.gameObject, 1, amount, ref budget, HitKind.Drone);
            start += amount;
            exact &= Mathf.Abs(fight.Damage - start) < .0001f && !fight.Invulnerable;
        }
        Check("all four coin damage values land immediately on a boss, with no second multiplier or hit window", exact && fight.QuartersLeft == before - 10 && fight.Actor.Hearts.VisibleQuarters(BossConfig.Hearts - 3) == 2);
        BossEncounter.ResetRun();
    }
    static void Drops()
    {
        bool all = true, actual = true;
        for (int start = 0; start < WorldManager.Worlds.Length; start++)
        {
            Scene();
            WorldManager.ClearReplayWorld();
            PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
            PlayerPrefs.SetInt(WorldManager.PrefsHighestWorld, WorldManager.Worlds.Length - 1);
            PlayerStartWorld.Select(start);
            var manager = new GameObject("~WorldManager").AddComponent<WorldManager>();
            manager.SendMessage("Awake"); manager.HoldStartWorld();
            actual &= WorldManager.RunStart == start;
            for (int current = 0; current < WorldManager.Worlds.Length; current++)
            {
                WorldManager.CurrentIndex = current;
                float chance = start == current ? .18f : .08f;
                Boosters.SetRunRushForTest(false);
                all &= Mathf.Approximately(ShotAtomDrops.EnemyChanceFor(current, start), chance);
                actual &= Mathf.Approximately(ShotAtomDrops.EnemyChance, chance) && WorldManager.RunStart == start;
                Boosters.SetRunRushForTest(true);
                actual &= Mathf.Approximately(ShotAtomDrops.EnemyChance, chance * 3f);
            }
        }
        Check("every starting-world/current-world pair gets 18% on the starting level and 8% elsewhere", all && actual);
    }
}
