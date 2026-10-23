using UnityEngine;

// The power-up refund: spend power-ups and the game gives some back, so a
// combo of pauses and shields can keep going -- but never as many as were
// spent.
//
//   A hidden meter counts USES: a pause spent (score.pauseCounterFunction)
//   and a shield that absorbed its first hit (collisionDetection). Every
//   UsesPerRefund uses the next reward drop is a power-up (spawnGoodStuff),
//   guaranteed, not a chance. One refund per 3 uses is clearly under one
//   back per one spent (and a refunded red atom is +2 pauses, still only
//   ever paid for by 3 real uses).
//
//   Guardrails (RefundConfig): refunded power-ups never feed the meter (the
//   drop is marked, RefundMark; pauses it grants are spent first without
//   counting, and its shield's absorb is skipped); one refund drop on screen
//   at a time; MaxPerRun per run, MaxShieldsPerRun of them shields, never two
//   shields in a row and never while a shield is up; a pause refund is held
//   back while the player already holds MaxPausesHeld. The meter holds at one
//   refund (no banking). Bosses and elites change nothing: the same caps.
//   Off in the tutorial and practice runs (BeginRun(false)).
//
// Reward feedback is the pickup's own spawn visual: nothing added.
public static class PowerUpRefund
{
    public enum Kind { None, Pause, Shield, Capacitor }

    // Everything tunable lives here.
    public static class RefundConfig
    {
        public const int UsesPerRefund = 3;
        public const int MaxPerRun = 6;
        public const int MaxShieldsPerRun = 2;
        public const int MaxPausesHeld = 5;   // = score.RealRunPauses
        // Base weights, plus UsageWeightStep per use of that type this run
        // (up to UsageWeightCap), so the pick leans to what the player uses.
        public const int WeightPause = 4, WeightShield = 1, WeightCapacitor = 1;
        public const int UsageWeightStep = 1, UsageWeightCap = 4;
    }

    static bool enabled;
    static int meter, issued, shieldsIssued, outstanding;
    static int refundedPausesHeld;
    static bool refundedShieldUp, shieldCounted, lastWasShield;
    static int pauseUses, shieldUses;

    public static bool Enabled => enabled;
    public static int Meter => meter;
    public static int Issued => issued;
    public static int ShieldsIssued => shieldsIssued;
    public static int Outstanding => outstanding;

    // A new run: clears everything. paysRealDust false (tutorial, practice) = off.
    public static void BeginRun(bool on)
    {
        enabled = on;
        meter = issued = shieldsIssued = outstanding = 0;
        refundedPausesHeld = 0;
        refundedShieldUp = shieldCounted = lastWasShield = false;
        pauseUses = shieldUses = 0;
    }

    // A refund is due: the meter is full.
    public static bool Due => enabled && meter >= RefundConfig.UsesPerRefund;

    static void AddUse()
    {
        meter = Mathf.Min(meter + 1, RefundConfig.UsesPerRefund);
    }

    // A pause was spent. Pauses a refund granted go first and don't count.
    public static void OnPauseSpent()
    {
        if (!enabled) return;
        if (refundedPausesHeld > 0) { refundedPausesHeld--; return; }
        pauseUses++;
        AddUse();
    }

    // A fresh shield is up (blue atom, a refunded one, or a secret power's).
    public static void OnShieldRaised(bool fromRefund)
    {
        shieldCounted = false;
        refundedShieldUp = fromRefund;
    }

    // The shield absorbed something: its first absorb is the "use".
    public static void OnShieldAbsorb()
    {
        if (!enabled || shieldCounted) return;
        shieldCounted = true;
        if (refundedShieldUp) return;
        shieldUses++;
        AddUse();
    }

    // A refund drop was collected: remember what it granted.
    public static void OnRefundCollected(Kind kind, int pausesGranted)
    {
        if (!enabled) return;
        if (kind == Kind.Pause) refundedPausesHeld += pausesGranted;
    }

    // RefundMark's lifetime: at most one drop outstanding.
    public static void MarkSpawned() { outstanding++; }
    public static void MarkGone() { outstanding = Mathf.Max(0, outstanding - 1); }

    // Which refund to drop now, or None (not due, a cap says no, or nothing is
    // allowed). roll01 in [0,1) picks within the weights.
    public static Kind Pick(int pausesHeld, bool shieldActive, float roll01)
    {
        if (!Due || outstanding > 0 || issued >= RefundConfig.MaxPerRun) return Kind.None;
        int wp = pausesHeld >= RefundConfig.MaxPausesHeld ? 0 : Weight(RefundConfig.WeightPause, pauseUses);
        int ws = ShieldAllowed(shieldActive) ? Weight(RefundConfig.WeightShield, shieldUses) : 0;
        int wc = RefundConfig.WeightCapacitor;
        int total = wp + ws + wc;
        if (total <= 0) return Kind.None;
        float r = Mathf.Clamp01(roll01) * total;
        if (r < wp) return Kind.Pause;
        if (r < wp + ws) return Kind.Shield;
        return Kind.Capacitor;
    }

    static int Weight(int baseWeight, int uses)
    {
        return baseWeight + Mathf.Min(RefundConfig.UsageWeightCap, uses * RefundConfig.UsageWeightStep);
    }

    static bool ShieldAllowed(bool shieldActive)
    {
        return !shieldActive && !lastWasShield && shieldsIssued < RefundConfig.MaxShieldsPerRun;
    }

    // The drop went out: the meter pays for it.
    public static void OnIssued(Kind kind)
    {
        if (kind == Kind.None) return;
        issued++;
        meter = Mathf.Max(0, meter - RefundConfig.UsesPerRefund);
        lastWasShield = kind == Kind.Shield;
        if (lastWasShield) shieldsIssued++;
    }
}

// Marks a pickup as a refund drop (collisionDetection reads it on collection).
public class RefundMark : MonoBehaviour
{
    public PowerUpRefund.Kind kind;
    void OnEnable() { PowerUpRefund.MarkSpawned(); }
    void OnDisable() { PowerUpRefund.MarkGone(); }
}
