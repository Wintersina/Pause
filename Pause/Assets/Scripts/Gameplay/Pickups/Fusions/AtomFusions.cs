using UnityEngine;

// ATOMIC FUSIONS in a run: catch two atoms close together and they fuse
// (AtomFusionTable has the recipes, AtomFusionState the rules, FusionTuning
// every number). This is the run-side glue:
//
//   collisionDetection reports every atom caught (OnAtom), the shield's kills
//   (OnShieldKill) and the shield letting go (OnShieldEnding / OnShieldEnded),
//   and ticks the clock on its running frames (Tick: the world's clock, so a
//   pause freezes the windows). MagnetPull's VACUUM reports itself
//   (OnExternal: MagnetCombo owns its effect and callout). Dove's Mending and
//   Turtle's Hard Shell report themselves as ship "atoms" (SecretPowers).
//
//   When a fusion fires: its effect (Apply), the record in the codex's COMBOS
//   page (FusionDiscoveryStore), and a big callout lifted above the ship
//   (ScoreHud.ShowCallout, one slot: replaced, never stacked) -- the two-line
//   "SHIELD + HEAL / BLOOM!", "CHAIN xN" on a re-trigger, or on the very
//   first time "NEW COMBO DISCOVERED / BLOOM!", bigger, plus the fusion's
//   own flipbook around the ship (FusionFx). No sounds.
//
//   Never in the tutorial, never in the kill cam's replay, never once the
//   pilot is dead. Bosses and elites are never insta-killed: the effects only
//   clear shots, push (ShieldShockwave, which treats them as the shield does)
//   or heal / refill the pilot.
//
// Strings are built once; nothing here allocates per frame.
public static class AtomFusions
{
    static readonly AtomFusionState state = new AtomFusionState { WindowFor = WindowOf };
    static float clock;
    static Transform ship;
    static bool tutorialScene;   // the ship flies the tutorial scene (read once: Scene.name allocates)

    // ---- effect state ----
    static float lifelineUntil, dustStormUntil, singularityUntil, bloomUntil, stasisUntil;
    static int bloomKills, bloomHearts, singularityHearts, stasisFree;
    static float bloomLastHeart = -99f, singularityLastHeart = -99f;
    static bool novaArmed, aftershockArmed;
    static float aftershockRestore = -1f;

    // ---- counters (tests, previews) ----
    public static int Fired { get; private set; }
    public static int Discoveries { get; private set; }
    public static FusionDef LastFusion { get; private set; }
    public static int LastChain { get; private set; }
    public static string LastCallout { get; private set; }
    public static int BloomHeals { get; private set; }
    public static int SingularityHeals { get; private set; }
    public static int FreePausesUsed { get; private set; }
    public static int ShotsCleared { get; private set; }
    public static int NovaKills { get; private set; }

    public static AtomFusionState State => state;
    public static float Clock => clock;

    // Tests: stand in for the run / tutorial / replay / death checks.
    public static bool? EnabledOverride;

    public static bool Enabled
    {
        get
        {
            if (EnabledOverride.HasValue) return EnabledOverride.Value;
            // (only a run whose ship began it: BeginRun)
            return ship != null && !tutorialScene && !startMenu.youAreInTutorial && !KillCam.Playing && !buttonClicks.playerDied;
        }
    }

    // ---- callout words, built once ----
    public const string DiscoveryLine = "NEW COMBO DISCOVERED";
    static readonly string[] comboWords = BuildWords(false);
    static readonly string[] discoveryWords = BuildWords(true);
    static string[] BuildWords(bool discovery)
    {
        var all = AtomFusionTable.All;
        var w = new string[all.Length];
        for (int i = 0; i < all.Length; i++)
            w[i] = (discovery ? DiscoveryLine : all[i].recipe) + "\n" + all[i].name + "!";
        return w;
    }
    public static string ComboWord(FusionId id) { return comboWords[(int)id]; }
    public static string DiscoveryWord(FusionId id) { return discoveryWords[(int)id]; }

    // ---------------------------------------------------------------- run

    // A run's ship spawned (collisionDetection.Start): a clean slate, and the motes.
    public static void BeginRun(GameObject shipGo)
    {
        ship = shipGo != null ? shipGo.transform : null;
        tutorialScene = shipGo != null && shipGo.scene.name == score.TutorialScene;
        state.Reset();
        clock = 0f;
        lifelineUntil = dustStormUntil = singularityUntil = bloomUntil = stasisUntil = 0f;
        bloomKills = bloomHearts = singularityHearts = stasisFree = 0;
        bloomLastHeart = singularityLastHeart = -99f;
        novaArmed = aftershockArmed = false;
        RestoreAftershock();
        if (shipGo != null && Enabled) { FusionMotes.Attach(shipGo); FusionFx.Build(shipGo); FusionDiscoveryStore.Prewarm(); }
    }

    // The world's running clock (collisionDetection's running frames).
    public static void Tick(float dt)
    {
        if (dt <= 0f) return;
        clock += dt;
        bool shield = ShieldUp;
        state.KeepTimed(AtomKind.Shield, shield ? collisionDetection.invTimer : 0f, clock);
        state.KeepTimed(AtomKind.Magnet, MagnetLeft, clock);
        state.KeepTimed(AtomKind.Shell, ShellLeft, clock);
        if (!shield) EndShieldBound();
        if (!MagnetPull.MagnetRunning && dustStormUntil > clock) { dustStormUntil = 0f; state.EndCurrent(FusionId.DustStorm); }
        state.Expire(clock);
        FusionFx.Advance(dt);
    }

    static bool ShieldUp => collisionDetection.atomCheck && collisionDetection.invTimer > 0f;
    static float MagnetLeft { get { var p = MagnetPull.Active; return MagnetPull.MagnetRunning ? p.Remaining : 0f; } }
    static float ShellLeft { get { var c = SecretPowerController.Instance; return c != null ? c.ShellLeft : 0f; } }

    static float WindowOf(FusionDef f)
    {
        switch (f.window)
        {
            case FusionWindow.WhileShield: return ShieldUp ? Mathf.Min(FusionTuning.ShieldBoundCap, collisionDetection.invTimer) : 0f;
            case FusionWindow.WhileMagnet: return Mathf.Min(FusionTuning.MagnetBoundCap, MagnetLeft);
            default: return f.seconds;
        }
    }

    // ---------------------------------------------------------------- catches

    // An atom (or a ship power's "atom") caught at `at`. `gates`: FusionGate bits.
    public static AtomFusionState.Result OnAtom(AtomKind kind, Vector3 at, int gates = 0)
    {
        if (!Enabled) return default;
        float prime = kind == AtomKind.Shield ? (ShieldUp ? collisionDetection.invTimer : 0f)
                    : kind == AtomKind.Magnet ? MagnetLeft
                    : kind == AtomKind.Shell ? ShellLeft
                    : FusionTuning.MoteSeconds;
        var r = state.OnAtom(kind, clock, prime, gates);
        if (r.outcome == AtomFusionState.Outcome.Primed && AtomFusionTable.IsPickup(kind))
            AtomSpawnBias.OnCaught(kind, Random.value, BossEncounter.Running);   // sometimes a partner follows
        if (r.Fired) Fire(r);
        return r;
    }

    // A heal atom: `wasted` = the hearts were already full when it was caught (OVERHEAL).
    public static int HealGates(bool wasted) { return wasted ? (int)FusionGate.HealAtFullHearts : 0; }

    // MagnetPull / MagnetCombo started (chain 1) or re-chained (2+) the vacuum.
    public static AtomFusionState.Result OnExternal(FusionId id, int chain)
    {
        if (!Enabled) return default;
        var r = state.OnExternal(id, chain, clock);
        if (r.Fired) Fire(r);
        return r;
    }

    static void Fire(AtomFusionState.Result r)
    {
        var f = r.fusion;
        Fired++;
        LastFusion = f;
        LastChain = r.chain;
        Apply(f);
        FusionFx.Play(f.id);   // its own flipbook, on the ship
        bool first = !FusionTestMode.Active && FusionDiscoveryStore.RecordFiring(f.id, r.chain, FusionDiscoveryStore.Today());   // a dev fusion test records nothing
        if (first)
        {
            Discoveries++;
            Codex.OnFusionFound(f);   // the combo becomes a pending Codex reward (collected in the COMBOS tab)
        }
        string word = first ? discoveryWords[(int)f.id]
                    : f.external ? null   // VACUUM's own callout (MagnetCombo) is already up
                    : r.chain > 1 ? MagnetCombo.WordFor(MagnetCombo.Callout.Chain, r.chain)
                    : comboWords[(int)f.id];
        LastCallout = word;
        if (word == null) return;
        var hud = ScoreHud.Current;
        if (hud == null) return;
        Vector3 at = ship != null ? ship.position : Vector3.zero;
        Color text = Color.Lerp(f.glow, Color.white, .35f);
        Color glow = f.glow;
        glow.a = FusionTuning.CalloutGlowAlpha;
        if (first)
            hud.ShowCallout(word, at + Vector3.up * FusionTuning.CalloutLift, text, FusionTuning.DiscoverySize,
                            FusionTuning.DiscoverySeconds, FusionTuning.DiscoveryRise, glow);
        else
            hud.ShowCallout(word, at + Vector3.up * FusionTuning.CalloutLift, text, FusionTuning.CalloutSize,
                            FusionTuning.CalloutSeconds, FusionTuning.CalloutRise, glow);
    }

    // ---------------------------------------------------------------- effects

    // Each fusion's handler. A re-trigger (chain) runs it again: every timer
    // and allowance restarts (a reset, never an addition).
    static void Apply(FusionDef f)
    {
        float window = WindowOf(f);
        Vector3 at = ship != null ? ship.position : Vector3.zero;
        switch (f.id)
        {
            case FusionId.Vacuum:
                break;   // MagnetPull runs it (MagnetCombo)
            case FusionId.Lifeline:
                lifelineUntil = clock + window;
                break;
            case FusionId.Bloom:
                bloomUntil = clock + window;
                bloomKills = 0;
                bloomHearts = 0;
                break;
            case FusionId.Nova:
                novaArmed = ShieldUp;
                break;
            case FusionId.Overclock:
                {
                    var power = ShipPowerController.Instance;
                    if (power != null) power.ReduceTimer(power.SecondsLeft);   // fires on its next running frame
                    if (score.pauseCounter < FusionTuning.OverclockPausesTo)
                    {
                        score.pauseCounter = FusionTuning.OverclockPausesTo;
                        AchievementTracker.OnPauseCount(score.pauseCounter);
                    }
                }
                break;
            case FusionId.Stasis:
                stasisUntil = clock + window;
                stasisFree = FusionTuning.StasisFreePauses;
                break;
            case FusionId.DustStorm:
                dustStormUntil = clock + window;
                break;
            case FusionId.Singularity:
                singularityUntil = clock + window;
                singularityHearts = 0;
                break;
            case FusionId.FeatherStorm:
                ShotsCleared += HostileShots.ClearWithin(at, FusionTuning.FeatherStormClearRadius);
                collisionDetection.BeginCloak(FusionTuning.FeatherStormCloakSeconds);
                break;
            case FusionId.ShellBurst:
                {
                    var c = SecretPowerController.Instance;
                    if (c != null) c.CrackShell();
                    ShieldShockwave.TryRelease(at, .4f, 1f);
                    ShotsCleared += HostileShots.ClearWithin(at, FusionTuning.ShellBurstClearRadius);
                }
                break;
            case FusionId.Overheal:
                score.AwardStarDust(FusionTuning.OverhealDust);
                break;
            case FusionId.Aftershock:
                aftershockArmed = ShieldUp;
                break;
        }
    }

    // ---- what the rest of the game asks ----

    // LIFELINE: the magnet's pull reaches the whole view and above it.
    public static bool LifelineActive => clock < lifelineUntil && Enabled;

    // The pull field while LIFELINE runs: the whole visible rect, grown upward.
    public static Rect LifelineField(Rect view)
    {
        return new Rect(view.xMin, view.yMin, view.width, view.height + FusionTuning.LifelineReachAbove);
    }

    // DUST STORM: what a star dust's cut to the weapon charge / the secret meter is multiplied by.
    public static float DustChargeMultiplier => DustStormOn ? FusionTuning.DustStormChargeMultiplier : 1f;
    public static float DustMeterMultiplier => DustStormOn ? FusionTuning.DustStormMeterMultiplier : 1f;
    // SINGULARITY is live: its window is open and the vacuum (magnet + shield) still runs. FusionFx loops its vortex on this.
    public static bool SingularityLive => clock < singularityUntil && MagnetPull.ComboOn && Enabled;
    // VACUUM is live and no SINGULARITY is drawing over it (FusionFx loops its vortex on this).
    public static bool VacuumLive => MagnetPull.ComboOn && Enabled && !SingularityLive;
    // The other loops' live states (FusionFx draws each only while its own rules hold).
    public static bool DustStormLive => clock < dustStormUntil && MagnetPull.MagnetRunning && Enabled;
    public static bool BloomLive => clock < bloomUntil && ShieldUp && Enabled;
    public static bool StasisLive => clock < stasisUntil && ShieldUp && Enabled;
    public static bool LifelineLive => LifelineActive && MagnetPull.MagnetRunning;
    public static bool NovaLive => novaArmed && ShieldUp && Enabled;
    public static bool AftershockLive => aftershockArmed && ShieldUp && Enabled;
    public static int BloomKills => bloomKills;
    static bool DustStormOn => clock < dustStormUntil && MagnetPull.MagnetRunning;

    // STASIS: a pause about to be spent is free (true: don't take it).
    public static bool ConsumeFreePause()
    {
        if (stasisFree <= 0 || clock >= stasisUntil || !ShieldUp || !Enabled) return false;
        stasisFree--;
        FreePausesUsed++;
        return true;
    }
    public static int StasisFreeLeft => clock < stasisUntil && ShieldUp ? stasisFree : 0;

    // The blue-atom shield destroyed something (collisionDetection): BLOOM
    // banks it; SINGULARITY heals for what the vacuum crushes. Both capped.
    public static void OnShieldKill()
    {
        if (!Enabled) return;
        if (clock < bloomUntil && ShieldUp)
        {
            bloomKills = Mathf.Min(bloomKills + 1, FusionTuning.BloomKillsPerHeart);
            if (bloomKills >= FusionTuning.BloomKillsPerHeart && bloomHearts < FusionTuning.BloomMaxHearts &&
                clock - bloomLastHeart >= FusionTuning.BloomMinHeartGap && Heal())
            {
                bloomKills = 0;
                bloomHearts++;
                bloomLastHeart = clock;
                BloomHeals++;
            }
        }
        if (clock < singularityUntil && MagnetPull.ComboOn && singularityHearts < FusionTuning.SingularityMaxHearts &&
            clock - singularityLastHeart >= FusionTuning.SingularityHealGap && Heal())
        {
            singularityHearts++;
            singularityLastHeart = clock;
            SingularityHeals++;
            FusionFx.Flash(FusionId.Singularity);
        }
    }

    // One heart back, if one is missing (the heal atom's own rule).
    static bool Heal()
    {
        if (collisionDetection.lifeCounter <= 0) return false;
        collisionDetection.lifeCounter--;
        var hud = ScoreHud.Current;
        if (hud != null && ship != null)
            hud.ShowWord(collisionDetection.HealWord, ship.position + Vector3.up * collisionDetection.PickupWordLift, PickupGlow.HealGreen, 28);
        return true;
    }

    // The blue-atom shield is about to let go at `at` (collisionDetection,
    // just before ShipShield.Hide fires its shockwave): NOVA clears the shots
    // around the ship; AFTERSHOCK widens the shockwave for this one release.
    public static void OnShieldEnding(Vector3 at)
    {
        if (novaArmed && Enabled)
        {
            ShotsCleared += HostileShots.ClearWithin(at, FusionTuning.NovaClearRadius);
            NovaKills += NovaKill(at);
            FusionFx.NovaRing(at);
        }
        novaArmed = false;
        if (aftershockArmed && Enabled && aftershockRestore < 0f)
        {
            aftershockRestore = ShieldShockwave.Radius;
            ShieldShockwave.Radius = aftershockRestore * FusionTuning.AftershockRadiusScale;
        }
        aftershockArmed = false;
    }

    // NOVA also destroys the hazards in the clear circle (plain enemies, rocks, mines), by the ordinary kill
    // (ShipAttackHits: score, dust, combo, achievements, death effects), only on screen: bosses, elites, what
    // elites fly, scenery and shots are left alone. Bloom / Singularity kill
    // bookkeeping is not called: the shield is already down.
    static readonly System.Collections.Generic.List<ClearTarget> novaScratch = new System.Collections.Generic.List<ClearTarget>(64);
    static int NovaKill(Vector3 at)
    {
        if (ship == null || startMenu.youAreInTutorial) return 0;
        int shipId = ShipId.Of(ship.gameObject, ShipId.Equipped());
        ShipTargets.Collect(novaScratch, 0f);
        int kills = 0;
        for (int i = 0; i < novaScratch.Count; i++)
        {
            var t = novaScratch[i];
            if (t == null || !t.isActiveAndEnabled) continue;
            Vector2 d = t.transform.position - at;
            float reach = FusionTuning.NovaClearRadius + t.Radius * .5f;
            if (d.sqrMagnitude > reach * reach) continue;
            var kind = MagnetPull.KindOf(t);
            // hazards: plain enemies, free mines, rocks and rail mines (the shield's own contact destroys any of them)
            if (kind != MagnetCombo.Kind.Enemy && kind != MagnetCombo.Kind.Rock && kind != MagnetCombo.Kind.RailMine) continue;
            var go = t.gameObject;
            if (go.TryGetComponent(out FieldAsteroid field))
            {
                if (field.Broken) continue;
                field.Break(shipId);   // a Space field rock breaks whole, like any power's kill (SecretPowerController.PowerKill)
                kills++;
            }
            else if (ShipAttackHits.Hit(go, shipId)) kills++;
        }
        novaScratch.Clear();
        return kills;
    }

    // ... and has let go.
    public static void OnShieldEnded()
    {
        RestoreAftershock();
        EndShieldBound();
    }

    // Idempotent; also called by GameStateReset.Clear so leaving mid-Aftershock
    // does not leave the global radius scaled.
    public static void RestoreAftershock()
    {
        if (aftershockRestore >= 0f) ShieldShockwave.Radius = aftershockRestore;
        aftershockRestore = -1f;
    }

    // The shield is down: everything that lived while it was up ends.
    static void EndShieldBound()
    {
        if (bloomUntil > clock) bloomUntil = clock;
        if (stasisUntil > clock) stasisUntil = clock;
        stasisFree = 0;
        novaArmed = false;
        aftershockArmed = false;
        var cur = state.Current;
        if (cur != null && cur.window == FusionWindow.WhileShield) state.EndCurrent(cur.id);
    }
}
