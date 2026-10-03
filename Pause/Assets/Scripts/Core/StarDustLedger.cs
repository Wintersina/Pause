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

    public static bool IsActive { get { return active; } }
    public static float Earned { get { return earned; } }
    public static float Balance { get { return baseline + earned; } }
    public static float Saved { get { return PlayerPrefs.GetFloat(CurrencyKey); } }

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
        return runId;
    }

    public static void Earn(float amount)
    {
        if (!active || amount <= 0f) return;
        earned += amount;
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
