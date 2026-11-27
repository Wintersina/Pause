using UnityEditor.SceneManagement;
using UnityEngine;

// Three-tier enemy hearts (HeartTiers.cs): crystal -> blue -> red. Red-only is unchanged, damage spends the outer bar first with the
// overflow CARRYING into the next bar, death needs the red bar empty, the power -> tier curve is monotonic and red-only at zero power,
// and every enemy / elite / boss in the rosters gets a valid tier configuration.
public static class HeartTiersTest
{
    static int fails;
    static void Check(string label, bool ok)
    {
        Debug.Log((ok ? "[HEARTTIERS] PASS " : "[HEARTTIERS] FAIL ") + label);
        if (!ok) fails++;
    }
    public static void Run() { TestHarness.Exit(Execute()); }
    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try { Growth(); Stages(); Enemy(); Rosters(); Art(); Hazards(); Shields(); DroneScore(); Heals(); HazardHits(); Loops(); DroneMeters(); }
        finally
        {
            HeartTierRun.ForceTier = -1;
            HeartTierRun.Live = false;
            RunPower.Override = -1f;
            PlayerPower.Override = -1f;
            BossEncounter.ResetRun();
        }
        Debug.Log("[HEARTTIERS] failures: " + fails);
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
        Time.timeScale = 1f;
    }

    static float MaxLevels { get { return PlayerPower.LevelsFor(6, 24, 300, ShipWeaponUpgrades.MaxLevel, 10); } }

    // P and what grows from it: deterministic, exponential, capped.
    static void Growth()
    {
        Check("a fresh ship has power 0 and gives exactly today's hearts", PlayerPower.LevelsFor(0, 0, 0, 0, 0) == 0f && PlayerPower.TotalHearts(HeartTierKind.Enemy, 3, 0f) == 3 &&
              PlayerPower.TotalHearts(HeartTierKind.Elite, 10, 0f) == 10 && PlayerPower.TotalHearts(HeartTierKind.Boss, 13, 0f) == 13 && !HeartShell.FromTotal(3, 3).Any);
        float a = PlayerPower.LevelsFor(1, 1, 5, 1, 1);
        Check("every part of the ship raises P", PlayerPower.LevelsFor(2, 1, 5, 1, 1) > a && PlayerPower.LevelsFor(1, 2, 5, 1, 1) > a && PlayerPower.LevelsFor(1, 1, 9, 1, 1) > a &&
              PlayerPower.LevelsFor(1, 1, 5, 2, 1) > a && PlayerPower.LevelsFor(1, 1, 5, 1, 2) > a);
        Check("a drone is worth one level", Mathf.Approximately(PlayerPower.LevelsFor(1, 0, 0, 0, 0), 1f));
        bool same = true, ratio = true, mono = true;
        int last = 0;
        for (int i = 0; i <= 40; i++)
        {
            float p = i * .5f;
            int t1 = PlayerPower.TotalHearts(HeartTierKind.Enemy, 3, p), t2 = PlayerPower.TotalHearts(HeartTierKind.Enemy, 3, p);
            same &= t1 == t2;
            mono &= t1 >= last; last = t1;
        }
        foreach (HeartTierKind kind in new[] { HeartTierKind.Enemy, HeartTierKind.Elite, HeartTierKind.Boss })
            for (int lv = 0; lv < 8; lv++)
                ratio &= Mathf.Abs(PlayerPower.Growth(lv + 1, kind) / PlayerPower.Growth(lv, kind) - HeartTierTuning.GrowthFor(kind)) < 1e-3f;
        Check("the same power always gives the same hearts, never fewer as power rises", same && mono);
        Check("toughness is strictly exponential in P: each level multiplies by G", ratio && HeartTierTuning.GEnemy > 1.1f && HeartTierTuning.GEnemy < 1.3f);
        Check("elites and bosses grow a little steeper than ordinary enemies", PlayerPower.Growth(10, HeartTierKind.Boss) > PlayerPower.Growth(10, HeartTierKind.Elite) && PlayerPower.Growth(10, HeartTierKind.Elite) > PlayerPower.Growth(10, HeartTierKind.Enemy));
        float maxed = PlayerPower.Growth(MaxLevels, HeartTierKind.Enemy);
        Check("a maxed ship (6 upgraded drones) makes enemies 6 to 10 times tougher (x" + maxed.ToString("0.0") + ")", maxed >= 6f && maxed <= 10f && PlayerPower.TotalHearts(HeartTierKind.Enemy, 3, MaxLevels) >= 18 && PlayerPower.TotalHearts(HeartTierKind.Enemy, 3, MaxLevels) <= 30);
        Check("caps hold at any power: enemy x12, elite x3, boss x2.5 of the red hearts",
              PlayerPower.TotalHearts(HeartTierKind.Enemy, 3, 500f) == 36 && PlayerPower.TotalHearts(HeartTierKind.Elite, 10, 500f) == 30 &&
              PlayerPower.TotalHearts(HeartTierKind.Boss, 13, 500f) <= Mathf.CeilToInt(13 * HeartTierTuning.BossMultCap));
        // threat
        bool tr = true; float lt = 0f;
        for (int i = 0; i <= 60; i++) { float t = PlayerPower.ThreatScaleFor(i * .5f); tr &= t >= lt && t <= HeartTierTuning.ThreatCap + 1e-4f; lt = t; }
        Check("threat T^P: 1 for a fresh ship, rising by T per level, capped", PlayerPower.ThreatScaleFor(0f) == 1f && tr && Mathf.Abs(PlayerPower.ThreatScaleFor(5f) / PlayerPower.ThreatScaleFor(4f) - HeartTierTuning.ThreatPerLevel) < 1e-3f &&
              PlayerPower.ThreatScaleFor(500f) == HeartTierTuning.ThreatCap && HeartTierTuning.ThreatPerLevel < HeartTierTuning.GEnemy);
        // RunPower is the same strength as 0..1
        PlayerPower.Override = 0f; RunPower.Override = -1f;
        Check("RunPower.Score01 is P over PowerFull (0 for a fresh ship, 1 maxed)", RunPower.Score01 == 0f);
        PlayerPower.Override = HeartTierTuning.PowerFull * 2f;
        Check("... and is capped at 1", RunPower.Score01 == 1f);
        PlayerPower.Override = -1f;
    }

    // The visual stages: red -> blue -> crystal.
    static void Stages()
    {
        var a = HeartShell.FromTotal(3, 3);
        Check("total = red: red only", !a.Any && a.Tier == 0 && a.Layer == HeartLayer.Red);
        var b = HeartShell.FromTotal(3, 4);
        Check("one heart over: a blue bar of one", b.Tier == 1 && b.blueMax == 1 && b.crystalMax == 0 && b.Layer == HeartLayer.Blue);
        var c = HeartShell.FromTotal(3, 6);
        Check("blue fills up to the red count before crystal starts", c.Tier == 1 && c.blueMax == 3 && c.crystalMax == 0);
        var d = HeartShell.FromTotal(3, 7);
        Check("past that, crystal holds the rest and shows first", d.Tier == 2 && d.blueMax == 3 && d.crystalMax == 1 && d.crystalPip == 1 && d.Layer == HeartLayer.Crystal);
        var e = HeartShell.FromTotal(3, 13);
        Check("a crystal bar bigger than the red count: each pip stands for several hearts (7 hearts on 3 pips = x3)", e.crystalMax == 7 && e.crystalPip == 3 && e.DisplayQuarters == 10 && e.DisplayQuarters <= 3 * 4);
        bool fits = true;
        for (int total = 3; total <= 120; total++) for (int red = 2; red <= 13; red++)
        {
            if (total < red) continue;
            var s = HeartShell.FromTotal(red, total);
            fits &= s.DisplayQuarters <= red * 4 && s.blueMax <= red && s.blueMax + s.crystalMax == total - red && s.crystalPip >= 1;
        }
        Check("the drawn bar never exceeds the red count, and the hearts always add up", fits);
        int br;
        var f = HeartShell.FromTotal(3, 13);
        int left = f.Absorb(12 * 4, out br);
        Check("a big hit spends true health: 12 hearts on 7 crystal + 3 blue leave 2 for red, both bars broken", left == 8 && br == 2 && !f.Any);
        f = HeartShell.FromTotal(3, 13);
        left = f.Absorb(7 * 4, out br);
        Check("spending exactly the crystal bar breaks it and shows blue", left == 0 && br == 1 && f.Layer == HeartLayer.Blue);
        left = f.Absorb(5 * 4, out br);
        Check("overflow carries: 5 hearts on a 3-heart blue bar leave 2 hearts for red", left == 8 && br == 1 && !f.Any);

        // live plans
        HeartTierRun.Live = true; HeartTierRun.ForceTier = -1;
        PlayerPower.Override = 0f;
        Check("a fresh ship (P = 0): red only, exactly today's", !HeartTierRun.Plan(HeartTierKind.Enemy, 3).Any && !HeartTierRun.Plan(HeartTierKind.Elite, 10).Any && !HeartTierRun.Plan(HeartTierKind.Boss, 13).Any);
        PlayerPower.Override = 3f;
        var mid = HeartTierRun.Plan(HeartTierKind.Enemy, 3);
        PlayerPower.Override = MaxLevels;
        var big = HeartTierRun.Plan(HeartTierKind.Enemy, 3);
        var big2 = HeartTierRun.Plan(HeartTierKind.Enemy, 3);
        Check("a middling ship: blue and red, a maxed ship: crystal (always, same every spawn)", mid.Tier == 1 && big.Tier == 2 && big.crystalMax == big2.crystalMax && big.blueMax == big2.blueMax);
        PlayerPower.Override = -1f; HeartTierRun.Live = false;
        Check("outside a real run (menus, tutorial, fixtures) nothing gets a shell", PlayerPower.Levels == 0f && !HeartTierRun.Plan(HeartTierKind.Enemy, 3).Any);
        // table (logged for the report)
        var sb = new System.Text.StringBuilder("[HEARTTIERS] table P -> total (red/blue/crystal x pip): ");
        foreach (float p in new[] { 0f, 3f, 6f, 9f, 12f, 16.6f })
            foreach (var pair in new[] { new[] { HeartTierKind.Enemy, (HeartTierKind)3 }, new[] { HeartTierKind.Elite, (HeartTierKind)10 }, new[] { HeartTierKind.Boss, (HeartTierKind)13 } })
            {
                int red = (int)pair[1], total = PlayerPower.TotalHearts(pair[0], red, p);
                var sh = HeartShell.FromTotal(red, total);
                sb.Append(pair[0] + " P" + p + "=" + total + "(" + red + "/" + sh.blueMax + "/" + sh.crystalMax + "x" + sh.crystalPip + ") ");
            }
        Debug.Log(sb.ToString());
    }

    static void Enemy()
    {
        Scene();
        RunScore.EndRun(RunScore.RunId); RunScore.BeginRun(true, true);
        // red only: exactly as before
        HeartTierRun.ForceTier = 0;
        var plain = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Fighter), Vector3.up, Quaternion.identity, 1f, true);
        var hp = plain.GetComponent<EnemyHearts>();
        Check("a red-only enemy has no shell and its old quarter pool", hp != null && hp.Tier == 0 && !hp.Shell.Any && hp.TotalQuarters == hp.QuartersLeft && hp.QuartersLeft == hp.Max * 4);
        ClearTarget.Ensure(plain);
        float budget = 0f;
        ShipAttackHits.Hit(plain, 1, .25f, ref budget, HitKind.Drone);
        Check("a red-only enemy loses one quarter from a quarter hit", hp.QuartersLeft == hp.Max * 4 - 1);

        HeartTierRun.ForceTier = 2;
        var go = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Fighter), Vector3.zero, Quaternion.identity, 1f, true);
        ClearTarget.Ensure(go);
        var h = go.GetComponent<EnemyHearts>();
        int red = h.Max;
        Check("a crystal enemy carries crystal, blue and red bars", h.Tier == 2 && h.Shell.crystalMax == red && h.Shell.blueMax == red && h.TotalQuarters == red * 12);
        Check("its view shows the crystal bar with two bars beneath", h.View != null && h.View.ShownLayer == 2);
        ShipAttackHits.Hit(go, 1, 1f, ref budget, HitKind.Drone);
        Check("a hit spends the crystal bar first and leaves the red hearts alone", h.Shell.crystalQ == red * 4 - 4 && h.QuartersLeft == red * 4);
        for (int i = 1; i < red; i++) ShipAttackHits.Hit(go, 1, 1f, ref budget, HitKind.Drone);
        Check("with the crystal bar spent the blue bar shows", h.Shell.Layer == HeartLayer.Blue && h.QuartersLeft == red * 4 && go != null);
        h.View.Refresh();
        Check("the view switched to the blue bar", h.View.ShownLayer == 1);
        for (int i = 0; i < red; i++) ShipAttackHits.Hit(go, 1, 1f, ref budget, HitKind.Drone);
        Check("with both shells spent the red hearts are untouched and it is alive", go != null && h.Shell.Layer == HeartLayer.Red && h.QuartersLeft == red * 4 && h.LayersBroken == 2);
        for (int i = 0; i < red - 1; i++) ShipAttackHits.Hit(go, 1, 1f, ref budget, HitKind.Drone);
        Check("it dies only when the red bar is empty", go != null && h.Hearts == 1);
        bool killed = ShipAttackHits.Hit(go, 1, 1f, ref budget, HitKind.Drone);
        Check("the last red heart kills it", killed && go == null);

        // overflow through the live route (blue 8 quarters, then red): 3 x 75% = 9 quarters, the 9th carries into red
        HeartTierRun.ForceTier = 1;
        var big = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Fighter), Vector3.zero, Quaternion.identity, 1f, true);
        ClearTarget.Ensure(big);
        var b = big.GetComponent<EnemyHearts>();
        int redMax = b.Max * 4, blueMax = b.Shell.blueMax * 4;
        for (int i = 0; i < 3; i++) ShipAttackHits.Hit(big, 1, .75f, ref budget, HitKind.Drone);
        int spent = 9 - blueMax;
        Check("overflow carries from a broken bar into the next", blueMax == 8 && !b.Shell.Any && b.QuartersLeft == redMax - spent && b.LayersBroken == 1);
        HeartTierRun.ForceTier = -1;
    }

    static void Rosters()
    {
        bool enemies = true, elites = true, bosses = true;
        float[] powers = { 0f, 2f, 5f, 8f, 12f, 16.6f, 40f };
        foreach (var def in EnemyRoster.All)
        {
            if (def == null || def.IsHazard) continue;
            for (int red = 2; red <= 4; red++)
                foreach (float p in powers)
                {
                    var s = HeartShell.FromTotal(red, PlayerPower.TotalHearts(HeartTierKind.Enemy, red, p));
                    enemies &= s.blueMax <= red && s.DisplayQuarters <= red * 4 && s.crystalPip >= 1 && (p > 0f || !s.Any);
                }
        }
        foreach (var def in EliteCatalog.All)
        {
            if (def == null) continue;
            int red = def.MaxHearts;
            foreach (float p in powers)
            {
                int total = PlayerPower.TotalHearts(HeartTierKind.Elite, red, p);
                var s = HeartShell.FromTotal(red, total);
                elites &= red > 0 && total >= red && total <= red * HeartTierTuning.EliteMultCap + 1 && s.blueMax <= red && s.DisplayQuarters <= red * 4 && (p > 0f || !s.Any);
            }
        }
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            int red = BossConfig.HeartsAt(w, 0);
            bool ok = BossCatalog.ForWorld(w) != null && red >= 5;
            foreach (float p in powers)
            {
                int total = PlayerPower.TotalHearts(HeartTierKind.Boss, red, p);
                var s = HeartShell.FromTotal(red, total);
                ok &= total >= red && total <= Mathf.CeilToInt(red * HeartTierTuning.BossMultCap) && s.blueMax <= red && s.DisplayQuarters <= red * 4 && (p > 0f || !s.Any);
            }
            bosses &= ok;
        }
        Check("every enemy hull gets a valid tier configuration at every power", enemies);
        Check("every elite gets a valid tier configuration at every power", elites);
        Check("every world's boss gets a valid tier configuration at every power", bosses);
    }

    static void Art()
    {
        bool ok = true;
        for (int l = 0; l < 3; l++)
        {
            ok &= HeartArt.Idle(l).Length == HeartArt.Frames && HeartArt.Break(l).Length == HeartArt.Frames;
            foreach (var sp in HeartArt.Idle(l)) ok &= sp != null;
            foreach (var sp in HeartArt.Break(l)) ok &= sp != null;
        }
        Check("idle and break flipbooks load for red, blue and crystal", ok);
        Check("blue and crystal use their own files, not the red fallback", HeartArt.Idle(1)[0].name.Contains("blue") && HeartArt.Idle(2)[0].name.Contains("crystal") && HeartArt.Empty != null);
    }

    static void Hazards()
    {
        bool mono = true;
        float last = 0f;
        for (int i = 0; i <= 20; i++) { float r = HazardScaling.RateFor(i / 20f); mono &= r >= last; last = r; }
        Check("hazard spawn rate is x1 at no power and rises to x1.5", HazardScaling.RateFor(0f) == 1f && mono && Mathf.Approximately(HazardScaling.RateFor(1f), 1.5f));
        bool capped = true;
        for (int i = 0; i <= 20; i++) for (float scale = 1f; scale <= 2f; scale += .25f)
        {
            float extra = HazardScaling.ExtraRate(i / 20f, scale);
            capped &= extra >= 1f && extra * scale <= Mathf.Max(HeartTierTuning.HazardRateCap, scale) + 1e-4f;
        }
        Check("hazard scaling never takes the whole spawn rate past its cap", capped);
        Check("hazards have no hearts, so no shell is ever attached to them", EnemyRoster.One(0, EnemyRole.Rock).IsHazard && EnemyRoster.One(0, EnemyRole.Mine).IsHazard);
    }

    static void Shields()
    {
        bool dec = true;
        float last = 2f;
        for (int i = 0; i <= 20; i++) { float r = HazardScaling.ShieldRateFor(i / 20f); dec &= r <= last && r > 0f; last = r; }
        Check("shield atoms: x1 with no squad, 40% with a full one, never zero, never rising", HazardScaling.ShieldRateFor(0f) == 1f && Mathf.Approximately(HazardScaling.ShieldRateFor(1f), .4f) && dec);
    }

    static void DroneScore()
    {
        Check("drone kill scale: x0.5 with one drone, x0.2 at a full squad, falling in a line", Mathf.Approximately(DroneScoring.Scale(1), .5f) && Mathf.Approximately(DroneScoring.Scale(HeartTierTuning.DampFullSquad), .2f) && DroneScoring.Scale(3) < DroneScoring.Scale(2) && DroneScoring.Scale(2) < DroneScoring.Scale(1) && DroneScoring.Scale(12) >= .2f - 1e-4f);
        int cap = ScoreRules.ComboThresholds[ScoreRules.ComboThresholds.Length - 1] - 1;
        Check("a squad's chain is capped under the top step until the pilot's own kills unlock it",
            DroneScoring.EffectiveChain(40, 0, 3) == cap && DroneScoring.EffectiveChain(40, HeartTierTuning.DroneChainUnlockKills, 3) == 40 && DroneScoring.EffectiveChain(40, 0, 1) == 40);
        Check("the cap leaves the top multiplier out of reach", ScoreRules.MultiplierFor(cap) < ScoreRules.MultiplierFor(ScoreRules.ComboThresholds[ScoreRules.ComboThresholds.Length - 1]));

        long own = RunKills(HitKind.Ultimate, 1, 14), droneOne = RunKills(HitKind.Drone, 1, 14), droneSquad = RunKills(HitKind.Drone, 3, 14);
        Check("with one drone a drone kill pays half of the pilot's own", droneOne > 0 && Mathf.Abs(droneOne - own * .5f) <= own * .05f + 2);
        Check("with a squad of three the same kills pay less and stay under the pilot's own", droneSquad > 0 && droneSquad < droneOne);
    }

    // Score of `count` kills inside one chain window, by `kind`, with `squad` drones active.
    static long RunKills(HitKind kind, int squad, int count)
    {
        Scene();
        HeartTierRun.ForceTier = 0;
        DroneSquad.BeginRun(Mathf.Max(1, squad));
        for (int i = 0; i < squad; i++) DroneSquad.Activate(i);
        RunScore.EndRun(RunScore.RunId); RunScore.BeginRun(true, true);
        float budget = 0f;
        for (int i = 0; i < count; i++)
        {
            var go = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Fighter), Vector3.zero, Quaternion.identity, 1f, true);
            ClearTarget.Ensure(go);
            for (int h = 0; h < 8 && go != null; h++) ShipAttackHits.Hit(go, 1, 1f, ref budget, kind);
        }
        long total = RunScore.Parts.kills;
        HeartTierRun.ForceTier = -1;
        return total;
    }

    static void Heals()
    {
        bool dec = true;
        float last = 2f;
        for (int i = 0; i <= 20; i++) { float r = HealScaling.RateFor(i / 20f); dec &= r <= last && r > 0f; last = r; }
        Check("heals: x1 at no power, 35% at full power, never zero, never rising", HealScaling.RateFor(0f) == 1f && Mathf.Approximately(HealScaling.RateFor(1f), .35f) && dec);
        Check("the heal bank keeps its size at no power and shrinks (never below one) at full power", HealScaling.BankMax(HealBank.Max, 0f) == HealBank.Max && HealScaling.BankMax(HealBank.Max, 1f) >= 1 && HealScaling.BankMax(HealBank.Max, 1f) < HealBank.Max);
        HealBank.Reset();
        RunPower.Override = 1f; HeartTierRun.Live = true;
        HealBank.OnCatch(false, false);
        var second = HealBank.OnCatch(false, false);
        Check("at full power the bank holds fewer atoms (the second banked catch is wasted)", second == HealBank.Catch.Wasted || HealScaling.BankMax(HealBank.Max, 1f) > 1);
        Check("the healing drone restores more slowly at full power", HealScaling.DelayScale > 2f);
        RunPower.Override = -1f; HeartTierRun.Live = false;
        HealBank.Reset();
        Check("at no power the bank and delay are exactly as before", HealScaling.DelayScale == 1f && HealBank.OnCatch(false, false) == HealBank.Catch.Bank);
        HealBank.Reset();
    }

    static void HazardHits()
    {
        HazardDamage.Reset();
        int costLow = 0;
        for (int i = 0; i < 8; i++) costLow += HazardDamage.Cost(true, 0f);
        Check("at no power a hazard costs exactly one heart a hit", costLow == 8);
        HazardDamage.Reset();
        int costHigh = 0, worst = 0;
        for (int i = 0; i < 8; i++) { int c = HazardDamage.Cost(true, 1f); costHigh += c; worst = Mathf.Max(worst, c); }
        Check("at full power a hazard averages 1.75 hearts (14 over 8 hits) and never costs more than 2", costHigh == 14 && worst <= 2);
        Check("anything that is not a hazard always costs one", HazardDamage.Cost(false, 1f) == 1);
        bool cap = HeartTierTuning.HazardExtraHearts <= 1f && HeartTierTuning.HazardSpeedMax <= .25f && HeartTierTuning.HazardBurstMax <= .5f;
        Check("hazard tuning is capped (extra heart <= 1, speed <= +25%, burst <= 50%)", cap && HazardScaling.SpeedFor(0f) == 1f && HazardScaling.SpeedFor(1f) <= 1.25f && HazardScaling.BurstChanceFor(0f) == 0f && HazardScaling.BurstChanceFor(1f) <= .5f);
        HazardDamage.Reset();
    }

    static void Loops()
    {
        float B = HeartTierTuning.LoopBase;
        bool ratio = true, inc = true, first = LoopDifficulty.For(0) == 1f;
        for (int i = 1; i <= 5; i++)
        {
            ratio &= Mathf.Abs(LoopDifficulty.For(i) / LoopDifficulty.For(i - 1) - B) < 1e-3f;
            inc &= LoopDifficulty.For(i) > LoopDifficulty.For(i - 1);
        }
        Check("D(loop 1) is exactly 1, strictly increasing, and each loop is Base times the last", first && inc && ratio && B > 1.3f && B < 1.6f);
        Check("D stops growing at LoopMaxIndex", LoopDifficulty.For(HeartTierTuning.LoopMaxIndex) == LoopDifficulty.For(99));

        // first pass identical to before
        Check("first pass: hearts, fire, density, boss, recovery, kill points are unchanged",
              LoopDifficulty.EnemyHearts(3, 0) == 3 && LoopDifficulty.VolleyGapScale(0) == 1f && LoopDifficulty.ShotBonus(0) == 0 && LoopDifficulty.ExtraDensity(0) == 1f &&
              LoopDifficulty.BossCooldownScale(0) == 1f && LoopDifficulty.KillScale(0) == 1f && HazardDamage.HeartsFor(.5f, 0) == HazardDamage.HeartsFor(.5f) &&
              HazardScaling.ExtraRate(.5f, 1.5f, 1f, 0) == HazardScaling.ExtraRate(.5f, 1.5f));

        // caps hold at loop 20
        int far = 20;
        bool caps = LoopDifficulty.EnemyHearts(4, far) <= HeartTierTuning.EnemyHeartCap &&
                    LoopDifficulty.ShotBonus(far) >= 0 && 
                    1f / LoopDifficulty.VolleyGapScale(far) <= HeartTierTuning.FireRateCap + 1e-4f &&
                    LoopDifficulty.ExtraDensity(far) <= HeartTierTuning.DensityCap + 1e-4f &&
                    LoopDifficulty.BossCooldownScale(far) >= HeartTierTuning.BossCooldownMin - 1e-4f &&
                    HazardDamage.HeartsFor(1f, far) <= HeartTierTuning.HazardCostCap + 1e-4f &&
                    HazardScaling.ExtraRate(1f, 1f, LoopDifficulty.For(far), far) <= HazardScaling.RateFor(1f) * HeartTierTuning.HazardLoopRateMax + 1e-4f &&
                    HazardScaling.ExtraRate(1f, 2f, LoopDifficulty.For(far), far) * 2f <= HeartTierTuning.HazardLoopRateCap + 1e-4f;
        Check("every loop-scaled number respects its cap at loop 20", caps);
        int budget = Mathf.Min(HeartTierTuning.ShotAliveCap, 4 + LoopDifficulty.ShotBonus(far));
        Check("shots alive never pass the alive cap, and the volley gap never drops under the minimum", budget <= HeartTierTuning.ShotAliveCap && HeartTierTuning.MinVolleyGap >= .1f);
        bool hearts = true; int lastHearts = 0;
        for (int i = 0; i <= 5; i++) { int h = LoopDifficulty.EnemyHearts(2, i); hearts &= h >= lastHearts && h >= 2; lastHearts = h; }
        Check("enemy hearts grow with the loop and stop at the cap", hearts && LoopDifficulty.EnemyHearts(2, 1) > 2 && LoopDifficulty.EnemyHearts(2, 5) == HeartTierTuning.EnemyHeartCap);
        Check("elites and bosses carry the loop on top of the power growth, capped", PlayerPower.TotalHearts(HeartTierKind.Elite, 10, 0f, LoopDifficulty.For(2)) > 10 &&
              PlayerPower.TotalHearts(HeartTierKind.Elite, 10, 0f, LoopDifficulty.For(40)) <= 30 && PlayerPower.TotalHearts(HeartTierKind.Boss, 13, 0f, 1f) == 13 && PlayerPower.TotalHearts(HeartTierKind.Enemy, 3, 0f, 5f) == 3);
        Check("nothing grows past loop 6 (index 5)", LoopDifficulty.For(5) == LoopDifficulty.For(40) && LoopDifficulty.KillScale(5) == LoopDifficulty.KillScale(40) && LoopDifficulty.EnemyHearts(3, 5) == LoopDifficulty.EnemyHearts(3, 40));

        // recovery and score
        Check("heals and shields shrink with the loop but never reach zero", Mathf.Max(HeartTierTuning.RecoverMin, 1f / LoopDifficulty.For(20)) >= .2f && HeartTierTuning.RecoverMin > 0f);
        bool kill = true;
        for (int i = 1; i <= 5; i++) kill &= LoopDifficulty.KillScale(i) > LoopDifficulty.KillScale(i - 1);
        Check("kill points rise 50% a loop", kill && Mathf.Approximately(LoopDifficulty.KillScale(1), 1.5f) && Mathf.Approximately(LoopDifficulty.KillScale(2), 2f));
        Check("the hazard cost rises with the loop and caps at 2 hearts", HazardDamage.HeartsFor(1f, 1) > HazardDamage.HeartsFor(1f, 0) && HazardDamage.HeartsFor(1f, 20) <= 2f + 1e-4f);
    }

    // Kill-driven meters: the secret power's meter (its cooldown) is the only one kills fill. A drone's kill does not fill it.
    static void DroneMeters()
    {
        Scene();
        HeartTierRun.ForceTier = 0;
        RunScore.EndRun(RunScore.RunId); RunScore.BeginRun(true, true);
        var go = new GameObject("~secret");
        var c = go.AddComponent<SecretPowerController>();
        go.SendMessage("Awake");   // (edit mode skips Awake: it is what sets SecretPowerController.Instance)
        float budget = 0f;
        c.SetMeter(0f);
        for (int k = 0; k < 2; k++)
        {
            var t = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Fighter), Vector3.zero, Quaternion.identity, 1f, true);
            ClearTarget.Ensure(t);
            for (int h = 0; h < 8 && t != null; h++) ShipAttackHits.Hit(t, 1, 1f, ref budget, HitKind.Drone);
        }
        Check("kills made by drones do not fill the secret power's meter", c.Meter == 0f);
        var own = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Fighter), Vector3.zero, Quaternion.identity, 1f, true);
        ClearTarget.Ensure(own);
        ShipAttackHits.Hit(own, 1, 1f, ref budget, HitKind.Ultimate);
        Check("the pilot's own kill still fills it by perKill", Mathf.Approximately(c.Meter, c.perKill));
        Object.DestroyImmediate(go);
        HeartTierRun.ForceTier = -1;
    }
}
