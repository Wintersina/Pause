// ATOMIC FUSIONS: every number in one place (AtomFusionTable, AtomFusionState,
// AtomFusions, FusionMotes, AtomSpawnBias and PickupRules read these; the
// tests pin them). Durations are seconds on the world's clock (a pause
// freezes them), strengths are conservative on purpose.
public static class FusionTuning
{
    // ---- the primed slots ----
    public const int MaxSlots = 2;                 // a third pickup replaces the oldest primed atom
    public const float MoteSeconds = 6f;           // an instant atom (heal, pause, capacitor, Mending) stays primed this long
    // (a timed atom -- shield, magnet, Hard Shell -- is primed for as long as its own effect runs)

    // ---- each fusion's chain window: catching one of its atoms again inside
    // it re-triggers the fusion (a reset, never an extension; no chain cap) ----
    public const float ChainWindowSeconds = 6f;    // the instant fusions (Overclock, Feather Storm, Shell Burst, Overheal)
    public const float LifelineSeconds = 5f;       // magnet + heal: the magnet's reach grows to the whole screen and above it
    public const float SingularitySeconds = 6f;    // magnet + shield + heal (the vacuum's synced length: MagnetCombo.SyncSeconds)
    public const float ShieldBoundCap = 6f;        // Bloom / Nova / Stasis / Aftershock: while the shield is up, at most this long
    public const float MagnetBoundCap = 6f;        // Dust Storm: while the magnet is on, at most this long

    // ---- BLOOM (shield + heal): shield kills bank toward a heart ----
    public const int BloomKillsPerHeart = 3;
    public const int BloomMaxHearts = 2;           // per bloom (a chain re-triggers it: a fresh allowance)
    public const float BloomMinHeartGap = 1f;      // s between two restored hearts at most

    // ---- NOVA (shield + capacitor): the shield's parting shockwave also clears enemy shots ----
    public const float NovaClearRadius = 4.5f;     // u from the ship (the plain shockwave reaches ShieldShockwave.Radius 3.2)

    // ---- OVERCLOCK (pause + capacitor) ----
    public const int OverclockPausesTo = 5;        // the pause counter is topped up to at least this (a run starts with 5)

    // ---- STASIS (pause + shield): free freezes while shielded ----
    public const int StasisFreePauses = 3;

    // ---- DUST STORM (capacitor + magnet): star dust charges the ship's powers faster ----
    public const float DustStormChargeMultiplier = 3f;   // the weapon charge cut per star dust
    public const float DustStormMeterMultiplier = 2f;    // the secret power meter per star dust

    // ---- LIFELINE (magnet + heal) ----
    public const float LifelineReachAbove = 2f;    // u above the top of the view the pull also reaches (the whole view below it)

    // ---- SINGULARITY (magnet + shield + heal): the vacuum heals ----
    public const float SingularityHealGap = 1.5f;  // s between two healed hearts at most
    public const int SingularityMaxHearts = 2;     // per singularity

    // ---- the ship-flavoured ones ----
    public const float FeatherStormClearRadius = 2.4f;   // Dove's Mending + heal atom: clears enemy shots this close...
    public const float FeatherStormCloakSeconds = 1f;    // ...and a moment of invulnerability
    public const float ShellBurstClearRadius = 3.2f;     // Turtle's Hard Shell + shield atom: the shell cracks into a shockwave

    // ---- same-atom pairs ----
    public const float OverhealDust = 3f;          // a second heal caught at full hearts: star dust instead
    public const float AftershockRadiusScale = 1.35f;   // a second shield: the first one's parting shockwave reaches further

    // ---- callouts (ScoreHud.ShowCallout: one slot, replaced, never stacked) ----
    public const int CalloutSize = 40;             // the plain pickup words are 28
    public const float CalloutSeconds = 1.4f;
    public const float CalloutRise = 70f;
    public const float CalloutLift = 1f;           // u above the ship
    public const int DiscoverySize = 46;           // "NEW COMBO DISCOVERED": bigger and longer
    public const float DiscoverySeconds = 2f;
    public const float DiscoveryRise = 80f;
    public const float CalloutGlowAlpha = .55f;    // the fusion's own glow behind the words

    // ---- first discovery ----
    public const float FirstDiscoveryDust = 0f;    // a one-time star dust reward; 0 = off (economy)

    // ---- the motes (an instant atom primed on the ship) ----
    public const float MoteSize = .14f;            // u, the dot's diameter
    public const float MoteOrbit = .62f;           // u from the ship's centre
    public const float MoteSpeed = 2.4f;           // radians a second
    public const float MoteAlpha = .7f;            // restrained: thin and low glow

    // ---- atom spawn rates (spawnGoodStuff via PickupRules) ----
    // More atoms so fusions come up; the green heal atom stays exactly as rare
    // as it was (HealAtomSpawner reads none of this). A multiplier speeds that
    // atom's release clock and grows its per-world allowance by the same share.
    public const float ShieldSpawnMultiplier = 1.5f;
    public const float PauseSpawnMultiplier = 1.5f;
    public const float CapacitorSpawnMultiplier = 1.5f;
    public const float MagnetSpawnMultiplier = 1.5f;
    public const float HealSpawnMultiplier = 1f;   // unchanged, by design

    // A complementary atom shortly after one is caught (AtomSpawnBias): a
    // partner that fuses with it, pulled forward out of that partner's own
    // per-world allowance (never extra atoms), never green, never in a boss fight.
    public const float ComplementChance = .35f;
    public const float ComplementDelay = .5f;      // s after the catch (it still has to drift down inside the 6 s window)
    public const int ComplementsPerWorld = 3;
    // The same atom is not released twice within this many seconds (another kind goes first).
    public const float SameKindGap = 8f;
}
