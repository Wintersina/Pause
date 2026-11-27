using UnityEngine;

// SHIP SURVIVABILITY BALANCE: the one place the "how invincible can a ship get" numbers live (user, 2026-11-27: "some ships are
// practically invincible now"). Every protective secret power has a WINDOW (seconds it protects the hull per activation); after it
// fires the power is locked for a PERIOD (fire to ready again) of at least window x MinPeriodFactor, and no dust or kill refills the
// meter while the window runs. So no ship can chain its protection back to back, a kill -> charge -> invulnerable -> kill loop cannot
// close, and the protected share of the time stays under MaxProtectedUptime however fast the meter fills.
//
// The period grows with the run (Scale: RunPower and the loop count), the same way heals and shield atoms thin out (HeartTiers).
// The player's own kills and dust still fill the meter as before, outside the window.
//
// The pure functions take every input as a parameter so ShipBalanceTest can simulate a ship from its data.
public static class ShipBalance
{
    // ---- caps ----
    public const float MaxProtectedUptime = .35f;   // invulnerable / shielded share of the time a ship may ever reach
    public const float MinPeriodFactor = 3f;        // fire -> ready again is at least this x the window (uptime <= 1/3, i.e. a 2x window gap after it ends)
    public const float PowerSlowMax = .5f;          // at full RunPower every period is 1.5x longer
    public const float LoopSlowCap = 2f;            // ...and a later loop stretches it up to 2x (1 / LoopDifficulty.RecoverScale)

    // ---- the windows (what each power protects the hull for, per activation) ----
    public const float PulseSeconds = 1.2f;         // Shield Pulse
    public const float CloakSeconds = 4f;           // Phase Cloak
    public const float DashSeconds = .6f;           // Blink Dash
    public const float BubbleSeconds = 2.5f;        // Time Bubble (dome blocks shots, shoves hazards)
    public const float ShellWindowSeconds = PlayerInvuln.PostHitInvulnSeconds;   // Hard Shell eats one hit and the post-hit blink follows
    // Aegis Shield (Paragon): window and period come from PremiumShips tiers.

    // ---- heals ----
    public const float MendGapSeconds = 30f;        // Mending: at most one heart per this long (before Scale): 2 a minute
    public const float MaxHealsPerMinute = 2f;      // the cap the test holds every ship to (ship powers only; green atoms have their own spawn clocks)

    // How much longer every lockout is at this run power and loop (>= 1). `recoverScale` = LoopDifficulty.RecoverScale.
    public static float Scale(float power01, float recoverScale)
    {
        float loop = Mathf.Min(LoopSlowCap, 1f / Mathf.Max(.05f, recoverScale));
        return (1f + PowerSlowMax * Mathf.Clamp01(power01)) * Mathf.Max(1f, loop);
    }

    public static float ScaleNow { get { return Scale(RunPower.Score01, LoopDifficulty.RecoverScale); } }

    // Seconds one activation protects the hull (0: the power does not protect it).
    public static float Window(SecretPower power, int ship, int tier)
    {
        switch (power)
        {
            case SecretPower.ShieldPulse: return PulseSeconds;
            case SecretPower.PhaseCloak: return CloakSeconds;
            case SecretPower.BlinkDash: return DashSeconds;
            case SecretPower.TimeBubble: return BubbleSeconds;
            case SecretPower.HardShell: return ShellWindowSeconds;
            case SecretPower.AegisShield: return PremiumShips.Tier(ship, tier).shieldSeconds;
        }
        return 0f;
    }

    // Shortest seconds from one activation to the next (the meter cannot be ready before this). Aegis Shield is a timer: its tier's
    // shieldEvery, stretched by Scale (the premium table is held to MinPeriodFactor by the test).
    public static float Period(SecretPower power, int ship, int tier, float power01, float recoverScale)
    {
        float s = Scale(power01, recoverScale);
        if (power == SecretPower.AegisShield) return PremiumShips.Tier(ship, tier).shieldEvery * s;
        if (power == SecretPower.Mending) return MendGapSeconds * s;
        return Window(power, ship, tier) * MinPeriodFactor * s;
    }

    // Heart restored by the power per activation (ship powers only).
    public static int HeartsPerActivation(SecretPower power) { return power == SecretPower.Mending ? 1 : 0; }

    // ------------------------------------------------------------------ the audit model (tests, tools)

    public struct Profile
    {
        public int ship;
        public int hearts;
        public SecretPower power;
        public float window, period;          // seconds
        public float fillSeconds;             // meter fill time at the modelled rate
        public float cycle;                   // seconds between two activations (max of period, window + fill)
        public float protectedUptime;         // 0..1: share of the time the hull is protected by the power / shield
        public float absorbsPerMinute;        // activations that save a heart (hit-answering powers) or shield-window hits taken
        public float healsPerMinute;
        public bool chains;                   // can activate again before the window ends
        public bool flagged;
    }

    // One ship's profile. `metersPerSecond`: the secret meter's income from the pilot's own kills and dust. `threatsPerMinute`: hazards
    // that would hit (a hit-answering power fires on one). `tier`: premium skin tier (ignored by standard ships).
    public static Profile Model(int ship, int tier, float metersPerSecond, float threatsPerMinute, float power01, float recoverScale)
    {
        var l = ShipLoadoutTable.For(ship);
        var p = new Profile { ship = ship, power = l.power };
        p.hearts = ShipLives.Base(ship);
        p.window = Window(l.power, ship, tier);
        p.period = Period(l.power, ship, tier, power01, recoverScale);
        bool timer = l.power == SecretPower.AegisShield;
        // the meter only fills outside the window; a timer ignores the income
        p.fillSeconds = timer ? p.period : (metersPerSecond > 0f ? SecretPowerController.Full / metersPerSecond : 9999f);
        float afterWindow = timer ? p.period : p.window + p.fillSeconds;
        p.cycle = Mathf.Max(p.period, afterWindow);
        // hit-answering and lane-triggered powers also wait for a threat
        bool waitsForThreat = l.trigger == PowerTrigger.ImminentHit || l.trigger == PowerTrigger.ThreatClose || l.trigger == PowerTrigger.HazardInLane;
        if (waitsForThreat && threatsPerMinute > 0f) p.cycle = Mathf.Max(p.cycle, 60f / threatsPerMinute);
        if (l.power == SecretPower.Mending) p.cycle = Mathf.Max(p.cycle, 60f / Mathf.Max(.1f, threatsPerMinute));   // needs a damaged hull
        p.protectedUptime = p.window > 0f ? Mathf.Min(1f, p.window / p.cycle) : 0f;
        float perMinute = 60f / p.cycle;
        bool answers = l.trigger == PowerTrigger.ImminentHit || l.trigger == PowerTrigger.ThreatClose || l.power == SecretPower.HardShell;
        p.absorbsPerMinute = answers ? Mathf.Min(perMinute, threatsPerMinute) : (timer ? threatsPerMinute * p.protectedUptime : 0f);
        p.healsPerMinute = perMinute * HeartsPerActivation(l.power);
        p.chains = p.window > 0f && p.period < p.window * MinPeriodFactor - .001f;
        p.flagged = p.protectedUptime > MaxProtectedUptime || p.chains || p.healsPerMinute > MaxHealsPerMinute + .001f;
        return p;
    }
}
