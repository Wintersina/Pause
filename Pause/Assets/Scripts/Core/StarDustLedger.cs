using UnityEngine;

// Credits the star dust earned in a run to the saved total.
//
// Dust used to reach PlayerPrefs only from the death screen, so leaving a run
// any other way -- Menu, Replay, Back, backgrounding or killing the app --
// threw away everything collected in it. Worse, Replay and Menu zero
// score.totalCurrency before the scene changes, so that field can't be
// trusted at the moment a run is torn down.
//
// The ledger keeps its own record: the saved balance when the run began plus
// what the run has earned since. Every commit writes that *absolute* value,
// so committing again -- or the death screen also writing
// score.totalCurrency, which is the same sum -- can never add the run's dust
// a second time.
public static class StarDustLedger
{
    // Misspelt, but it's the key every existing install has its dust under.
    public const string CurrencyKey = "PlayerCurrecny";

    static int runId;
    static bool active;
    static float baseline;
    static float earned;
    // The end-of-run score bonus (ScoreRules.ScoreDustBonus): paid at most
    // once per run, into `earned`, so every later commit already includes it.
    static bool bonusPaid;
    static float bonus;

    public static bool IsActive { get { return active; } }
    public static float Earned { get { return earned; } }
    public static float Balance { get { return baseline + earned; } }
    public static float Saved { get { return PlayerPrefs.GetFloat(CurrencyKey); } }
    public static int RunId { get { return runId; } }
    public static bool BonusPaid { get { return bonusPaid; } }
    public static float Bonus { get { return bonus; } }

    // Starts a run and returns its token. A run that doesn't pay real dust
    // (the tutorial) never touches the saved balance. Any run still open is
    // committed first, in case the next scene's Awake beats the old scene's
    // OnDestroy.
    public static int BeginRun(bool paysRealDust)
    {
        CommitCurrent();
        runId++;
        active = paysRealDust;
        baseline = Saved;
        earned = 0f;
        bonusPaid = false;
        bonus = 0f;
        return runId;
    }

    public static void Earn(float amount)
    {
        if (!active || amount <= 0f) return;
        earned += amount;
    }

    // Adds the run's score bonus to what it earned -- once per run, and only
    // for a run that pays real dust. Returns what was added (0 on a repeat,
    // a stale token, or a practice run). Call before the commit that banks it.
    public static float PayScoreBonus(int token, float amount)
    {
        if (token != runId || !active || bonusPaid) return 0f;
        bonusPaid = true;
        if (amount <= 0f) return 0f;
        bonus = amount;
        earned += amount;
        return amount;
    }

    // Adds star dust that is not a run's earnings (an achievement reward). It
    // is claimed from menus, where no run is active, but if one is (a test, a
    // future caller) the grant also goes into the run's baseline so the next
    // Commit's absolute write keeps it. Does not save: the caller does.
    public static void Grant(float amount)
    {
        if (amount <= 0f) return;
        if (active)
        {
            baseline += amount;
            PlayerPrefs.SetFloat(CurrencyKey, Balance);
        }
        else PlayerPrefs.SetFloat(CurrencyKey, Saved + amount);
        PrefsSaver.MarkDirty();
    }

    // Writes and saves the run's balance. Safe to call any number of times.
    public static void Commit(int token)
    {
        if (token != runId) return;
        CommitCurrent();
    }

    // Final commit when the run's scene goes away. A stale token (an old
    // scene torn down after the next run already began) does nothing.
    public static void EndRun(int token)
    {
        if (token != runId) return;
        CommitCurrent();
        active = false;
    }

    public static void CommitCurrent()
    {
        if (!Stage()) return;
        PrefsSaver.SaveNow();
    }

    // Writes the balance into PlayerPrefs without flushing to disk. Returns
    // false when there is no paying run.
    public static bool Stage()
    {
        if (!active) return false;
        PlayerPrefs.SetFloat(CurrencyKey, Balance);
        PrefsSaver.MarkDirty();
        return true;
    }
}
