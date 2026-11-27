using System.Text;
using UnityEngine;

// The survivability audit (ShipBalance): every ship's protective windows, simulated from its data in a typical mid-run and a
// high-power run. No ship may reach the uptime cap, chain its protection back to back, out-heal a realistic damage intake, or lose
// its ultimate and secret power. Pure data: no scene.
public static class ShipBalanceTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[SB] PASS  " : "[SB] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    // mid-run: 2 drones, moderate kills (0.5 own kills/s x 5 + 0.8 dust/s x ~3.6) and ~5 threats a minute;
    // high power: run power 1, later loop, faster income (1 kill/s, 1.5 dust/s) and ~10 threats a minute.
    const float MidMeter = 5.4f, HighMeter = 10.4f, MidThreats = 5f, HighThreats = 10f;
    const float HighPower = 1f, HighRecover = .5f;   // loop index ~2 (D = 2)

    public static int Execute()
    {
        fails = 0;
        var table = new StringBuilder("\n[SB] ship | hearts | power | uptime mid/high % | absorbs/min mid/high | heals/min mid/high | flagged\n");
        for (int id = 1; id <= 16; id++)
        {
            int tiers = PremiumShips.IsPremium(id) ? PremiumShips.TierCount(id) : 1;
            for (int tier = 0; tier < tiers; tier++)
            {
                var mid = ShipBalance.Model(id, tier, MidMeter, MidThreats, .4f, 1f);
                var high = ShipBalance.Model(id, tier, HighMeter, HighThreats, HighPower, HighRecover);
                // the worst case for the caps: the fastest the game can ever refill (power 0, first loop, huge income, lots of threats)
                var fast = ShipBalance.Model(id, tier, 60f, 60f, 0f, 1f);
                string n = id + (tiers > 1 ? "/t" + tier : "") + " " + ShipId.NameOf(id);
                table.AppendLine("[SB] " + n + " | " + mid.hearts + " | " + mid.power + " | " + (mid.protectedUptime * 100f).ToString("0") + "/" +
                                 (high.protectedUptime * 100f).ToString("0") + " | " + mid.absorbsPerMinute.ToString("0.0") + "/" + high.absorbsPerMinute.ToString("0.0") +
                                 " | " + mid.healsPerMinute.ToString("0.0") + "/" + high.healsPerMinute.ToString("0.0") + " | " + (fast.flagged ? "Y" : "n"));
                Check(n + ": protected uptime under the cap even at the fastest refill (" + (fast.protectedUptime * 100f).ToString("0.0") + "%)",
                      fast.protectedUptime <= ShipBalance.MaxProtectedUptime + .0001f);
                Check(n + ": cannot chain protection back to back (period " + fast.period.ToString("0.0") + " s vs window " + fast.window.ToString("0.0") + " s)",
                      !fast.chains && (fast.window <= 0f || fast.period >= fast.window * ShipBalance.MinPeriodFactor - .001f));
                Check(n + ": heals per minute under the cap (" + fast.healsPerMinute.ToString("0.0") + ")", fast.healsPerMinute <= ShipBalance.MaxHealsPerMinute + .001f);
            }
        }
        Debug.Log(table.ToString());

        for (int id = 1; id <= 16; id++)
        {
            var l = ShipLoadoutTable.For(id);
            Check(id + " keeps its ultimate and its secret power (" + l.attack + " + " + l.power + ")",
                  l.shipId == id && l.attackName != null && l.attackName.Length > 0 && l.powerName != null && l.powerName.Length > 0 &&
                  System.Enum.IsDefined(typeof(ShipAttack), l.attack) && System.Enum.IsDefined(typeof(SecretPower), l.power));
        }

        // the scale only ever slows things down
        Check("Scale is 1 at no power, first loop", Mathf.Approximately(ShipBalance.Scale(0f, 1f), 1f));
        Check("Scale grows with power and with the loop, capped", ShipBalance.Scale(1f, 1f) > 1f && ShipBalance.Scale(0f, .5f) > 1f &&
              ShipBalance.Scale(1f, .01f) <= (1f + ShipBalance.PowerSlowMax) * ShipBalance.LoopSlowCap + .001f);

        // Paragon's tiers: the shield table obeys the same rule
        for (int t = 0; t < PremiumShips.TierCount(PremiumShips.Paragon); t++)
        {
            var tr = PremiumShips.Tier(PremiumShips.Paragon, t);
            Check("Paragon tier " + t + " shield " + tr.shieldSeconds + " s every " + tr.shieldEvery + " s stays under the cap",
                  tr.shieldSeconds / tr.shieldEvery <= ShipBalance.MaxProtectedUptime && tr.shieldEvery >= tr.shieldSeconds * ShipBalance.MinPeriodFactor);
        }
        return fails;
    }
}
