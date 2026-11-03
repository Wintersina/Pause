// The rewarded "double your star dust" rules, pure.
//
// The reward doubles ONE thing: the dust the run itself earned (StarDustLedger.Earned,
// which includes flight dust, pickups and the score bonus; achievement and Codex
// claims go through StarDustLedger.Grant into the baseline, never into Earned). It pays
// exactly once per death: each death panel has a token (its UTC ticks); the token of the
// last paid death is persisted, and a claim for a token that is not newer than it pays
// nothing, so a repeated callback, a second tap or a restart around the callback cannot
// pay twice.
public static class DoubleDustRules
{
    public const string PaidTokenKey = "adsDoubleDustPaid";

    // Extra dust a doubling pays: a copy of what the run earned, in hundredths like the ledger.
    public static float Bonus(float earned)
    {
        if (!(earned > 0f) || float.IsInfinity(earned)) return 0f;
        return (float)System.Math.Round(earned, 2);
    }

    // Each death panel's token comes from a persisted counter that only ever goes up, never from the clock
    // (a clock set backwards would otherwise make every later death look "already paid").
    public const string DeathCounterKey = "adsDeathCounter";

    public static long NextToken(long counter, long lastPaidToken)
    {
        return System.Math.Max(counter, lastPaidToken) + 1L;
    }

    public static bool CanClaim(long token, long lastPaidToken)
    {
        return token > 0L && token > lastPaidToken;
    }
}
