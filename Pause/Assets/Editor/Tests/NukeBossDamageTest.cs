using UnityEditor.SceneManagement;
using UnityEngine;

// Feature: one Paragon nuke takes ShipSignaturesG3.NukeBossDamage (3) boss health, as ONE hit.
// The one exception to "a firing is worth about one boss hit" (ShipWeaponUpgrades); every other ship, the
// mini-warheads, elites and ordinary targets are unchanged. Drives BossEncounter frame by frame in edit mode.
public static class NukeBossDamageTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[NUKEBOSS] PASS  " : "[NUKEBOSS] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        try
        {
            TheNumbers();
            NukeTakesThreeInOneHit();
            OtherShipsStayOneHit();
            LowHealthKillsCleanlyOnce();
            WindowBlocksThenLetsItThrough();
        }
        finally
        {
            BossEncounter.ResetRun();
            BossRails.Reset();
            PlayField.Reset();
            Time.timeScale = 1f;
        }
        Debug.Log("[NUKEBOSS] failures: " + fails);
        return fails;
    }

    const float Dt = 1f / 60f;

    static BossEncounter StartFight()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BossEncounter.ResetRun();
        BossRails.Reset();
        PlayField.Reset();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .37f;
        Time.timeScale = 1f;
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, 0);
        BossEncounter.Begin(0, null);
        var e = BossEncounter.Instance;
        e.Step(.1f, 1f);
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);
        return e;
    }

    static BossTarget Target(BossEncounter e) => e.Actor.BodyHitbox.GetComponent<BossTarget>();

    static void Settle(BossEncounter e)
    {
        for (int i = 0; i < 60; i++) e.Step(Dt, 1f);
    }

    static void TheNumbers()
    {
        Check("the nuke's boss damage is 3", Mathf.Approximately(ShipSignaturesG3.NukeBossDamage, 3f));
        for (int level = 0; level <= ShipWeaponUpgrades.MaxLevel; level++)
            Check("level " + level + ": the warheads' boss shares add up to the nuke's damage",
                  Mathf.Abs(ShipSignaturesG3.NukeBossWeight(level) * ShipSignaturesG3.NukeWarheads(level) - 3f) < 1e-4f);
        Check("ordinary targets still see one share per warhead (unchanged)",
              Mathf.Approximately(ShipSignaturesG3.NukeWarheadWeight(0), 1f) && Mathf.Approximately(ShipSignaturesG3.NukeWarheadWeight(4), .5f));
        Check("the boss health pool is bigger than one nuke", BossConfig.HitPoints > 3);
    }

    static void NukeTakesThreeInOneHit()
    {
        var e = StartFight();
        var t = Target(e);
        int hp = e.HitPointsLeft, hearts = e.HeartsLeft;
        Check("one nuke lands", ShipAttackHits.HitBoss(t, 16, ShipSignaturesG3.NukeBossWeight(0), t.transform.position));
        Check("... and takes exactly 3 boss health (" + e.HitPointsLeft + " left of " + hp + ")", e.HitPointsLeft == hp - 3);
        Check("... as a single hit that opens the hit window once", e.Hits == 1 && e.Invulnerable);
        Check("... and the hearts follow (3 of " + hearts + " gone)", e.HeartsLeft == hearts - 3);
        Check("the fight goes on", e.State == BossEncounter.Phase.Fight && !e.Destroyed);
    }

    static void OtherShipsStayOneHit()
    {
        int nukes = 0;
        for (int id = 0; id < ShipLoadoutTable.Count; id++)
            if (ShipLoadoutTable.AttackFor(id) == ShipAttack.Nuke) nukes++;
        Check("only one ship (Paragon) flies the nuke", nukes == 1 && ShipLoadoutTable.AttackFor(16) == ShipAttack.Nuke);

        var e = StartFight();
        int hp = e.HitPointsLeft;
        float budget = 1f;
        ShipAttackHits.Hit(e.Actor.BodyHitbox, 1, 1f, ref budget);   // an ordinary full attack hit, as every other ship deals
        Check("a normal full ship hit still costs one boss health", e.HitPointsLeft == hp - 1);
    }

    static void LowHealthKillsCleanlyOnce()
    {
        foreach (int left in new[] { 1, 2, 3 })
        {
            var e = StartFight();
            var t = Target(e);
            // spend health down to `left` (whole hits, stepping past each hit window)
            while (e.HitPointsLeft > left)
            {
                e.OnShipAttackHit(1f);
                Settle(e);
            }
            Settle(e);
            bool landed = ShipAttackHits.HitBoss(t, 16, ShipSignaturesG3.NukeBossWeight(0), t.transform.position);
            Check("nuke on a boss with " + left + " health left lands", landed);
            Check("... health is 0, never negative", e.HitPointsLeft == 0 && e.Destroyed);
            Check("... hearts are 0, never negative", e.HeartsLeft == 0);
            // a second nuke on the dead boss (before its body is torn down in the outro): reports done, changes nothing
            int hits = e.Hits;
            Check("a second nuke on the dead boss reports done and changes nothing",
                  ShipAttackHits.HitBoss(t, 16, ShipSignaturesG3.NukeBossWeight(0), t.transform.position) && e.Hits == hits && e.HitPointsLeft == 0 && e.HeartsLeft == 0);
            Settle(e);
            Check("... the boss goes to its outro (one death)", e.State == BossEncounter.Phase.Outro || e.State == BossEncounter.Phase.Done);
        }
    }

    static void WindowBlocksThenLetsItThrough()
    {
        var e = StartFight();
        var t = Target(e);
        e.OnShipAttackHit(1f);   // raw bookkeeping, no window
        t.Strike(1, 1f, t.transform.position);   // a landed hit opens the 0.25 s window
        Check("a hit opened the window", e.Invulnerable);
        int hp = e.HitPointsLeft;
        Check("a nuke inside the window is blocked and asks to retry",
              !ShipAttackHits.HitBoss(t, 16, 3f, t.transform.position) && e.HitPointsLeft == hp);
        e.Step(BossConfig.HitInvulnerableSeconds + .02f, 1f);
        Check("the window closes", !e.Invulnerable);
        Check("the retried nuke lands in full", ShipAttackHits.HitBoss(t, 16, 3f, t.transform.position) && e.HitPointsLeft == hp - 3);
    }
}
