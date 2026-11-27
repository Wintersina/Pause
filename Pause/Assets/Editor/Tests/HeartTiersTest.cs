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
        try { Curve(); Power(); Shell(); Enemy(); Rosters(); Art(); }
        finally
        {
            HeartTierRoll.ForceTier = -1;
            HeartTierRoll.Live = false;
            RunPower.Override = -1f;
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

    static void Curve()
    {
        bool zero = true, mono = true, nested = true, order = true;
        foreach (HeartTierKind kind in new[] { HeartTierKind.Enemy, HeartTierKind.Elite, HeartTierKind.Boss })
        {
            for (int r = 0; r <= 10; r++) zero &= HeartTierCurve.Tier(0f, kind, r / 10f) == 0 && HeartTierCurve.Tier(0f, kind, .999f) == 0;
            float lastBlue = 0f, lastCrystal = 0f;
            for (int i = 0; i <= 100; i++)
            {
                float p = i / 100f;
                float b = HeartTierCurve.BlueOrBetter(p, kind), c = HeartTierCurve.Crystal(p, kind);
                mono &= b >= lastBlue - 1e-6f && c >= lastCrystal - 1e-6f;
                nested &= c <= b + 1e-6f && b <= 1f && c >= 0f;
                lastBlue = b; lastCrystal = c;
            }
        }
        for (int i = 0; i <= 100; i++)
        {
            float p = i / 100f;
            order &= HeartTierCurve.BlueOrBetter(p, HeartTierKind.Boss) >= HeartTierCurve.BlueOrBetter(p, HeartTierKind.Elite) - 1e-6f &&
                     HeartTierCurve.BlueOrBetter(p, HeartTierKind.Elite) >= HeartTierCurve.BlueOrBetter(p, HeartTierKind.Enemy) - 1e-6f;
        }
        Check("zero power gives red-only for every kind and every roll", zero);
        Check("the blue-or-better and crystal shares never fall as power rises", mono);
        Check("crystal is always a subset of blue-or-better", nested);
        Check("bosses are biased up from elites, elites from enemies", order);
        Check("full power makes blue and crystal common", HeartTierCurve.Crystal(1f, HeartTierKind.Enemy) >= .3f && HeartTierCurve.BlueOrBetter(1f, HeartTierKind.Enemy) >= .6f);
        Check("the tier roll is deterministic per seed", Rolls(7) == Rolls(7) && Rolls(7) != Rolls(8));
    }

    static string Rolls(int seed)
    {
        HeartTierRoll.BeginRun(seed);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 8; i++) sb.Append(HeartTierRoll.NextRoll().ToString("0.0000")).Append(',');
        return sb.ToString();
    }

    static void Power()
    {
        Check("RunPower is 0 with nothing and 1 with everything", RunPower.Compute(0, 0, 0, 0) == 0f && Mathf.Approximately(RunPower.Compute(ShotAtoms.MaxPower, 12, ShipWeaponUpgrades.MaxLevel, 999), 1f));
        float a = RunPower.Compute(10, 0, 0, 0);
        Check("each part raises RunPower", RunPower.Compute(20, 0, 0, 0) > a && RunPower.Compute(10, 4, 0, 0) > a && RunPower.Compute(10, 0, 2, 0) > a && RunPower.Compute(10, 0, 0, 30) > a);
        RunPower.Override = 0f; HeartTierRoll.Live = true; HeartTierRoll.ForceTier = -1; HeartTierRoll.BeginRun(3);
        bool red = true;
        for (int i = 0; i < 50; i++) red &= HeartTierRoll.Pick(HeartTierKind.Enemy) == 0;
        RunPower.Override = 1f; HeartTierRoll.BeginRun(3);
        int crystal = 0;
        for (int i = 0; i < 200; i++) if (HeartTierRoll.Pick(HeartTierKind.Enemy) == 2) crystal++;
        Check("Pick gives red-only at power 0 and mostly upgraded hulls at power 1", red && crystal > 40);
        HeartTierRoll.Live = false; RunPower.Override = -1f;
    }

    static void Shell()
    {
        var s = HeartShell.Make(2, HeartTierKind.Enemy, 2);   // crystal 2, blue 2 hearts
        Check("a crystal shell shows crystal first, then blue, then red", s.Layer == HeartLayer.Crystal && s.crystalMax == 2 && s.blueMax == 2);
        int broken;
        int left = s.Absorb(8, out broken);
        Check("spending the crystal bar exactly leaves nothing over and shows blue", left == 0 && broken == 1 && s.Layer == HeartLayer.Blue);
        left = s.Absorb(12, out broken);
        Check("overflow carries: 12 on a 8-quarter blue bar leaves 4 for the red hearts", left == 4 && broken == 1 && !s.Any && s.Layer == HeartLayer.Red);
        var t = HeartShell.Make(2, HeartTierKind.Enemy, 2);
        left = t.Absorb(20, out broken);
        Check("one big hit can break both bars and carry the rest", broken == 2 && left == 4);
        Check("red-only makes an empty shell", !HeartShell.Make(0, HeartTierKind.Enemy, 3).Any && HeartShell.Make(0, HeartTierKind.Boss, 13).Tier == 0);
        Check("bar sizes: enemy = red, elite = half, boss = quarter", HeartTierTuning.BarHearts(HeartTierKind.Enemy, 3) == 3 && HeartTierTuning.BarHearts(HeartTierKind.Elite, 6) == 3 && HeartTierTuning.BarHearts(HeartTierKind.Boss, 13) == 4);
    }

    static void Enemy()
    {
        Scene();
        RunScore.EndRun(RunScore.RunId); RunScore.BeginRun(true, true);
        // red only: exactly as before
        HeartTierRoll.ForceTier = 0;
        var plain = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Fighter), Vector3.up, Quaternion.identity, 1f, true);
        var hp = plain.GetComponent<EnemyHearts>();
        Check("a red-only enemy has no shell and its old quarter pool", hp != null && hp.Tier == 0 && !hp.Shell.Any && hp.TotalQuarters == hp.QuartersLeft && hp.QuartersLeft == hp.Max * 4);
        ClearTarget.Ensure(plain);
        float budget = 0f;
        ShipAttackHits.Hit(plain, 1, .25f, ref budget, HitKind.Drone);
        Check("a red-only enemy loses one quarter from a quarter hit", hp.QuartersLeft == hp.Max * 4 - 1);

        HeartTierRoll.ForceTier = 2;
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
        HeartTierRoll.ForceTier = 1;
        var big = EnemyFactory.Create(EnemyRoster.One(0, EnemyRole.Fighter), Vector3.zero, Quaternion.identity, 1f, true);
        ClearTarget.Ensure(big);
        var b = big.GetComponent<EnemyHearts>();
        int redMax = b.Max * 4, blueMax = b.Shell.blueMax * 4;
        for (int i = 0; i < 3; i++) ShipAttackHits.Hit(big, 1, .75f, ref budget, HitKind.Drone);
        int spent = 9 - blueMax;
        Check("overflow carries from a broken bar into the next", blueMax == 8 && !b.Shell.Any && b.QuartersLeft == redMax - spent && b.LayersBroken == 1);
        HeartTierRoll.ForceTier = -1;
    }

    static void Rosters()
    {
        bool enemies = true, elites = true, bosses = true;
        foreach (var def in EnemyRoster.All)
        {
            if (def == null || def.IsHazard) continue;
            for (int red = 2; red <= 4; red++)
                for (int tier = 0; tier <= 2; tier++)
                {
                    var s = HeartShell.Make(tier, HeartTierKind.Enemy, red);
                    int bar = HeartTierTuning.BarHearts(HeartTierKind.Enemy, red);
                    enemies &= s.Tier == tier && bar >= 1 && bar <= red && s.QuartersLeft == tier * bar * 4;
                }
        }
        foreach (var def in EliteDef.All)
        {
            if (def == null) continue;
            int red = def.MaxHearts, bar = HeartTierTuning.BarHearts(HeartTierKind.Elite, red);
            for (int tier = 0; tier <= 2; tier++)
            {
                var s = HeartShell.Make(tier, HeartTierKind.Elite, red);
                elites &= red > 0 && bar >= 1 && bar <= red && s.Tier == tier && s.QuartersLeft == tier * bar * 4;
            }
        }
        for (int w = 0; w < WorldManager.Worlds.Length; w++)
        {
            int red = BossConfig.HeartsAt(w, 0), bar = HeartTierTuning.BarHearts(HeartTierKind.Boss, red);
            bosses &= BossCatalog.ForWorld(w) != null && red >= 5 && bar >= 1 && bar <= red && HeartShell.Make(2, HeartTierKind.Boss, red).Tier == 2;
        }
        Check("every enemy hull gets a valid tier configuration", enemies);
        Check("every elite gets a valid tier configuration", elites);
        Check("every world's boss gets a valid tier configuration", bosses);
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
}
