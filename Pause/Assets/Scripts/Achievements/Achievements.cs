using UnityEngine;

// Events that exist only for achievements.
public static class AchievementEvents
{
    // The ship lost a heart (collisionDetection). boss_no_hit and the perfect-dodge probe latch it.
    public static event System.Action PlayerHurt;
    // World time (Time.time) of the last hurt, for the dodge probe's "and survived a second".
    public static float LastHurtAt { get; private set; } = -1f;

    // The shield let go and its shockwave fired (ShieldShockwave.TryRelease); the argument is how
    // many bodies it moved. Informational: no achievement counts it, and it is not a kill or a hit.
    public static event System.Action<int> ShieldReleased;
    public static void RaiseShieldReleased(int moved)
    {
        var handler = ShieldReleased;
        if (handler != null) handler(moved);
    }

    public static void RaisePlayerHurt()
    {
        LastHurtAt = AchievementTracker.Clock();
        var handler = PlayerHurt;
        if (handler != null) handler();
    }
}

// The facade every tracking hook goes through: a guard, then the store.
//
// A run's progress only counts in a real run: not the tutorial, not developer
// mode (the same rules as Codex.Discover) -- `Real`. Things the pilot does in
// the menus (the shop, signing in) only need developer mode off -- `Menu`.
// Every call is O(1) with cached keys and no allocation.
public static class Achievements
{
    // Test seam: forces the guards open/closed (null = the real rules).
    public static bool? ForceReal;

    public static bool Real
    {
        get
        {
            if (ForceReal.HasValue) return ForceReal.Value;
            return score.paysRealDust && !DeveloperUnlocks.Enabled;
        }
    }

    public static bool Menu
    {
        get
        {
            if (ForceReal.HasValue) return ForceReal.Value;
            return !DeveloperUnlocks.Enabled;
        }
    }

    // ---- primitives (guarded in-run) ----

    public static bool Report(string id)
    {
        return Real && AchievementStore.Unlock(AchievementCatalog.Find(id));
    }

    public static void Add(string counter, int n = 1)
    {
        if (Real) AchievementStore.AddCounter(counter, n);
    }

    public static void SetAtLeast(string counter, int value)
    {
        if (Real) AchievementStore.SetCounterAtLeast(counter, value);
    }

    // ---- the same, for menu-side events ----

    public static bool ReportMenu(string id)
    {
        return Menu && AchievementStore.Unlock(AchievementCatalog.Find(id));
    }

    public static void AddMenu(string counter, int n)
    {
        if (Menu) AchievementStore.AddCounter(counter, n);
    }

    public static void SetAtLeastMenu(string counter, int value)
    {
        if (Menu) AchievementStore.SetCounterAtLeast(counter, value);
    }

    // ---- claim (menu-only) ----

    public static int Claim(string id) { return AchievementStore.Claim(AchievementCatalog.Find(id)); }
    public static int ClaimAll() { return AchievementStore.ClaimAll(); }
}

// Ticks the dodge probe while a blink is waiting to prove itself (otherwise idle).
public class AchievementRunner : MonoBehaviour
{
    static AchievementRunner instance;

    public static void Wake()
    {
        if (!Application.isPlaying) return;
        if (instance == null)
        {
            var go = new GameObject("~AchievementRunner");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            instance = go.AddComponent<AchievementRunner>();
        }
        instance.enabled = true;
    }

    void Update()
    {
        if (!AchievementTracker.DodgePending) { enabled = false; return; }
        AchievementTracker.TickDodge();
    }
}
