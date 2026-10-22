using UnityEngine;
using Kind = PowerUpRefund.Kind;
using Cfg = PowerUpRefund.RefundConfig;

// The power-up refund (PowerUpRefund): a hidden meter of power-ups USED
// (pauses spent, a shield's first absorb) earns a guaranteed power-up drop
// every UsesPerRefund uses, under hard caps. Pure logic, no scene.
public static class PowerUpRefundTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[REFUND] PASS  " : "[REFUND] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        try
        {
            MeterFills();
            DisabledInTutorial();
            RefundsDontFeedTheMeter();
            CapsHold();
            ShieldRules();
            AverageRatio();
        }
        finally { PowerUpRefund.BeginRun(false); }
        Debug.Log("[REFUND] failures: " + fails);
        return fails;
    }

    static void MeterFills()
    {
        PowerUpRefund.BeginRun(true);
        for (int i = 0; i < Cfg.UsesPerRefund - 1; i++) PowerUpRefund.OnPauseSpent();
        Check("not due one use short", !PowerUpRefund.Due);
        PowerUpRefund.OnPauseSpent();
        Check("due after N uses", PowerUpRefund.Due);
        PowerUpRefund.OnPauseSpent();
        Check("meter holds at one refund (no banking)", PowerUpRefund.Meter == Cfg.UsesPerRefund);
        var k = PowerUpRefund.Pick(0, false, .1f);
        Check("a type is picked when due", k != Kind.None);
        PowerUpRefund.OnIssued(k);
        Check("issuing empties the meter", PowerUpRefund.Meter == 0 && !PowerUpRefund.Due);
        Check("N is under 1 refund per 1 use (about 2-3 uses)", Cfg.UsesPerRefund >= 2 && Cfg.UsesPerRefund <= 3);
    }

    static void DisabledInTutorial()
    {
        PowerUpRefund.BeginRun(false);
        for (int i = 0; i < 10; i++) { PowerUpRefund.OnPauseSpent(); PowerUpRefund.OnShieldAbsorb(); }
        Check("off: the meter never fills", PowerUpRefund.Meter == 0 && !PowerUpRefund.Due);
        Check("off: nothing is picked", PowerUpRefund.Pick(0, false, 0f) == Kind.None);
    }

    static void RefundsDontFeedTheMeter()
    {
        PowerUpRefund.BeginRun(true);
        PowerUpRefund.OnRefundCollected(Kind.Pause, 2);   // a refunded red atom: +2 pauses
        PowerUpRefund.OnPauseSpent();
        PowerUpRefund.OnPauseSpent();
        Check("the two refunded pauses don't count", PowerUpRefund.Meter == 0);
        PowerUpRefund.OnPauseSpent();
        Check("the next real pause does", PowerUpRefund.Meter == 1);

        PowerUpRefund.BeginRun(true);
        PowerUpRefund.OnShieldRaised(true);   // a refunded shield
        PowerUpRefund.OnShieldAbsorb();
        Check("a refunded shield's absorb doesn't count", PowerUpRefund.Meter == 0);
        PowerUpRefund.OnShieldRaised(false);
        PowerUpRefund.OnShieldAbsorb();
        PowerUpRefund.OnShieldAbsorb();
        Check("a real shield counts once, however many hits", PowerUpRefund.Meter == 1);
    }

    static void CapsHold()
    {
        PowerUpRefund.BeginRun(true);
        for (int i = 0; i < Cfg.UsesPerRefund; i++) PowerUpRefund.OnPauseSpent();
        var go = new GameObject("refund");
        var mark = go.AddComponent<RefundMark>();
        Check("a drop outstanding blocks another", PowerUpRefund.Pick(0, false, 0f) == Kind.None);
        Object.DestroyImmediate(go);
        Check("gone, the next may go", PowerUpRefund.Pick(0, false, 0f) != Kind.None);

        PowerUpRefund.BeginRun(true);
        int given = 0;
        for (int round = 0; round < 50; round++)
        {
            for (int i = 0; i < Cfg.UsesPerRefund; i++) PowerUpRefund.OnPauseSpent();
            var k = PowerUpRefund.Pick(0, false, round * .137f % 1f);
            if (k == Kind.None) continue;
            PowerUpRefund.OnIssued(k);
            given++;
        }
        Check("per-run cap holds (" + Cfg.MaxPerRun + ")", given == Cfg.MaxPerRun && PowerUpRefund.Issued == Cfg.MaxPerRun);

        PowerUpRefund.BeginRun(true);
        for (int i = 0; i < Cfg.UsesPerRefund; i++) PowerUpRefund.OnPauseSpent();
        bool pauseAtFull = false;
        for (float r = 0f; r < 1f; r += .05f)
            if (PowerUpRefund.Pick(Cfg.MaxPausesHeld, false, r) == Kind.Pause) pauseAtFull = true;
        Check("no pause refund while the max is held", !pauseAtFull);
    }

    static void ShieldRules()
    {
        PowerUpRefund.BeginRun(true);
        for (int i = 0; i < Cfg.UsesPerRefund; i++) PowerUpRefund.OnPauseSpent();
        bool shieldWhileUp = false;
        for (float r = 0f; r < 1f; r += .01f)
            if (PowerUpRefund.Pick(0, true, r) == Kind.Shield) shieldWhileUp = true;
        Check("never a shield while one is active", !shieldWhileUp);

        // a shield per run: at most MaxShieldsPerRun, never back to back
        PowerUpRefund.BeginRun(true);
        int shields = 0; Kind last = Kind.None; bool backToBack = false;
        for (int round = 0; round < 50; round++)
        {
            for (int i = 0; i < Cfg.UsesPerRefund; i++) PowerUpRefund.OnPauseSpent();
            var k = PowerUpRefund.Pick(0, false, .999f - (round % 3) * .0f);   // top of the roll: favours a late type
            // force shield-leaning rolls too
            var s = PowerUpRefund.Pick(0, false, (float)Cfg.WeightPause / (Cfg.WeightPause + Cfg.WeightShield + Cfg.WeightCapacitor) + .001f);
            if (s == Kind.Shield) k = s;
            if (k == Kind.None) continue;
            if (k == Kind.Shield) { shields++; if (last == Kind.Shield) backToBack = true; }
            PowerUpRefund.OnIssued(k);
            last = k;
        }
        Check("shields are capped per run", shields <= Cfg.MaxShieldsPerRun && PowerUpRefund.ShieldsIssued == shields);
        Check("never two shield refunds in a row", !backToBack);
        Check("shields are a small share of the refund budget", Cfg.MaxShieldsPerRun * 3 <= Cfg.MaxPerRun);
    }

    // Simulate many players: spend everything they hold, always collect.
    static void AverageRatio()
    {
        var rng = new System.Random(7);
        int spent = 0, back = 0;
        for (int run = 0; run < 200; run++)
        {
            PowerUpRefund.BeginRun(true);
            for (int use = 0; use < 60; use++)
            {
                if (rng.NextDouble() < .8) PowerUpRefund.OnPauseSpent();
                else { PowerUpRefund.OnShieldRaised(false); PowerUpRefund.OnShieldAbsorb(); }
                spent++;
                var k = PowerUpRefund.Pick(0, false, (float)rng.NextDouble());
                if (k == Kind.None) continue;
                PowerUpRefund.OnIssued(k);
                back++;
                if (k == Kind.Pause) PowerUpRefund.OnRefundCollected(k, 2);
            }
        }
        float ratio = back / (float)spent;
        Debug.Log("[REFUND] refunds per use " + ratio.ToString("0.000"));
        Check("average refund ratio is well under 1 per use", ratio < .5f);
    }
}
