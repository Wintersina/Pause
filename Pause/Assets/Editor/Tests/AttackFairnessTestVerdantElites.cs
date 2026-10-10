using UnityEngine;

// THE FAIRNESS CONTRACT FOR THE FOUR NEW VERDANT ELITES (VerdantElites.cs; the contract of AttackFairnessTest).
//
//   * FR1: the real EliteShip tells >= .7 s, its footprint is drawn from the first frame of the tell and shown >= .4 s before anything
//     can hurt (AttackPreview counters: none too short), whatever the attack draws (rings, lanes, the whip's swept area)
//   * aim locked at the tell: nothing drawn moves with the pilot afterwards
//   * FR6: the dodge bot's hit rate against each attack, in a short run (AttackBudgetTest rolls the 2000), within the pinned Verdant elite
//     baseline's x1.15 + .02 rule (AttackBudgetTest.Themed), and never harder to dodge than standing still
//   * the pink cue: a hostile shot of these elites is drawn pink (ShotMotionArt bodies carry the pink-edge contract; ShotMotionTest checks pixels)
public static partial class AttackFairnessTest
{
    static readonly string[] VerdantElites =
    {
        "verdant_elite_timber_hauler", "verdant_elite_thornlash", "verdant_elite_sporebloom", "verdant_elite_leafblade",
    };

    static void VerdantEliteSuite()
    {
        foreach (string key in VerdantElites)
        {
            AttackBudgetScenarios.Reset();
            AttackPreview.ResetCounters();
            var def = EliteCatalog.Find(key);
            var sc = AttackBudgetScenarios.Make("elite:" + key);
            var pilot = sc.Begin(new System.Random(5), new Vector2(0f, -2.5f));
            float tell = 0f, firstFrame = -1f;
            bool drawnAtOnce = false;
            for (int f = 0; f < 600 && !sc.Done; f++)
            {
                sc.Step(1f / 60f);
                if (sc.Telling || EliteSystem.Player != null && EliteShip.Live.Count > 0 && EliteShip.Live[0].Telling)
                {
                    tell += 1f / 60f;
                    if (firstFrame < 0f) { firstFrame = f; drawnAtOnce = AttackPreview.ActiveCount > 0 || AttackHazard.ActiveCount > 0; }
                }
            }
            Check(key + ": a real wind-up of at least .7 s (" + tell.ToString("0.00") + " s) with the footprint drawn from its first frame (" + drawnAtOnce + ")",
                  tell >= .69f && drawnAtOnce);
            Check(key + ": nothing is shown too short (AttackPreview too-short count " + AttackPreview.TooShort + ", shortest " + AttackPreview.MinShown.ToString("0.00") + " s)",
                  AttackPreview.TooShort == 0 && AttackPreview.MinShown >= AttackPreview.MinLead);
            sc.End();
            AttackBudgetScenarios.Cleanup();
            var r = AttackBudgetTest.Measure("elite:" + key, 300);
            string why;
            bool ok = AttackBudgetTest.WithinBudget("elite:" + key, r, out why);
            Check("the dodge bot against " + key + " (300 rolls; standing still: " + r.GhostRate.ToString("P0") + "): " + why, ok && r.hits <= r.ghostHits);
        }
    }
}
