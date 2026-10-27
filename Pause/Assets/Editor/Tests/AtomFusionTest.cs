using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

// ATOMIC FUSIONS (AtomFusionTable, AtomFusionState, AtomFusions,
// FusionDiscoveryStore, AtomSpawnBias, the codex's COMBOS tab):
//   * the table: unique ids / save keys / recipes, VACUUM is fusion #1 and external;
//   * order never matters (every pair both ways);
//   * windows: an instant atom's mote lasts FusionTuning.MoteSeconds, a timed
//     atom is primed exactly as long as its effect runs;
//   * a third atom replaces the oldest primed one; the newest partner wins;
//   * chaining resets the window (never extends it) and counts CHAIN x2, x3;
//   * a triple beats its pairs (with the current fusion, or the vacuum);
//   * same-atom pairs (AFTERSHOCK, OVERHEAL only at full hearts);
//   * discoveries persist (counts, best chain, first day, unknown keys kept);
//   * spawn multipliers leave the green atom's rate alone; complements are never green;
//   * the run glue: effects (Stasis, Bloom, Dust Storm, Aftershock, Overclock),
//     nothing in the tutorial, callout words;
//   * no allocation in the per-frame paths;
//   * the dev FUSION TEST selector (Options, developer mode): cycles the combos + OFF, spawns only
//     that combo's atoms, does nothing with developer mode off, resets with it;
//   * every fusion has its OWN flipbook (FusionFx), pooled: nothing allocated per trigger;
//   * the COMBOS codex tab: locked rows are "???" silhouettes, found ones show stats.
//
//   Unity -batchmode -quit -projectPath Pause -executeMethod AtomFusionTest.Run
public static class AtomFusionTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[AF] PASS  " : "[AF] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        using var sandbox = new TestHarness.Sandbox();
        PlayerPrefs.DeleteKey(FusionDiscoveryStore.PrefsKey);
        PlayerPrefs.DeleteKey(FusionDiscoveryStore.UnseenKey);
        FusionDiscoveryStore.Reload();
        try
        {
            startMenu.youAreInTutorial = false;
            buttonClicks.playerDied = false;
            Table();
            OrderNeverMatters();
            Windows();
            SlotReplacement();
            Chains();
            Triples();
            SameAtom();
            Discovery();
            SpawnRates();
            Complements();
            RunEffects();
            NoAllocation();
            FusionFlipbooks();
            LoopingFusions();
            MoreLoopsAndNova();
            DevFusionTest();
            CodexTab();
        }
        finally
        {
            AtomFusions.EnabledOverride = null;
            collisionDetection.atomCheck = false;
            collisionDetection.invTimer = 0f;
            collisionDetection.lifeCounter = 0;
            startMenu.youAreInTutorial = false;
            AtomSpawnBias.Reset();
        }
        Debug.Log("[AF] failures: " + fails);
        return fails;
    }

    // A state with fixed windows (no game reads).
    static AtomFusionState NewState(float window = 6f)
    {
        return new AtomFusionState { WindowFor = f => window };
    }

    // ---- the table ------------------------------------------------------------

    static void Table()
    {
        var all = AtomFusionTable.All;
        var keys = new HashSet<string>();
        var recipes = new HashSet<int>();
        bool idsInOrder = true, filled = true;
        for (int i = 0; i < all.Length; i++)
        {
            var f = all[i];
            idsInOrder &= (int)f.id == i;
            keys.Add(f.key);
            recipes.Add(f.Key);
            filled &= !string.IsNullOrEmpty(f.name) && !string.IsNullOrEmpty(f.hint) && !string.IsNullOrEmpty(f.description) &&
                      f.atoms.Length >= 2 && f.atoms.Length <= 3 && !string.IsNullOrEmpty(f.recipe);
        }
        Check("one row per FusionId, in order (" + all.Length + " rows)", idsInOrder && all.Length == System.Enum.GetValues(typeof(FusionId)).Length);
        Check("save keys and recipes are unique", keys.Count == all.Length && recipes.Count == all.Length);
        Check("every row has a name, hint, description and 2-3 atoms", filled);
        var v = AtomFusionTable.Get(FusionId.Vacuum);
        Check("VACUUM is fusion #1: magnet + shield, external (MagnetCombo owns it), its synced length",
              (int)FusionId.Vacuum == 0 && v.external && v.Uses(AtomKind.Magnet) && v.Uses(AtomKind.Shield) &&
              Mathf.Approximately(v.seconds, MagnetCombo.SyncSeconds));
        Check("SINGULARITY is the magnet + shield + heal triple",
              AtomFusionTable.Find(AtomFusionTable.Recipe(AtomKind.Heal, AtomKind.Magnet, AtomKind.Shield)) == AtomFusionTable.Get(FusionId.Singularity));
        Check("recipes are unordered", AtomFusionTable.Recipe(AtomKind.Shield, AtomKind.Heal) == AtomFusionTable.Recipe(AtomKind.Heal, AtomKind.Shield) &&
              AtomFusionTable.Recipe(AtomKind.Shield, AtomKind.Shield) != AtomFusionTable.Recipe(AtomKind.Shield, AtomKind.Heal));
        Check("callout words: two lines, recipe over name; the discovery line over name",
              AtomFusions.ComboWord(FusionId.Bloom) == "SHIELD + HEAL\nBLOOM!" &&
              AtomFusions.DiscoveryWord(FusionId.Nova) == AtomFusions.DiscoveryLine + "\nNOVA!" &&
              ReferenceEquals(AtomFusions.ComboWord(FusionId.Bloom), AtomFusions.ComboWord(FusionId.Bloom)));
        Check("the discovery callout is bigger and longer than a fusion's, which is bigger than a pickup word",
              FusionTuning.DiscoverySize > FusionTuning.CalloutSize && FusionTuning.CalloutSize > 28 &&
              FusionTuning.DiscoverySeconds > FusionTuning.CalloutSeconds && FusionTuning.CalloutLift > collisionDetection.PickupWordLift);
    }

    // ---- order -----------------------------------------------------------------

    static void OrderNeverMatters()
    {
        int pairs = 0, bad = 0;
        foreach (var f in AtomFusionTable.All)
        {
            if (f.atoms.Length != 2 || f.external) continue;
            int gates = f.gate == FusionGate.None ? 0 : (int)f.gate;
            for (int order = 0; order < 2; order++)
            {
                var s = NewState();
                var a = order == 0 ? f.atoms[0] : f.atoms[1];
                var b = order == 0 ? f.atoms[1] : f.atoms[0];
                var r1 = s.OnAtom(a, 0f, 6f, gates);
                var r2 = s.OnAtom(b, 1f, 6f, gates);
                if (r1.outcome != AtomFusionState.Outcome.Primed || r2.outcome != AtomFusionState.Outcome.Fused || r2.fusion != f || r2.chain != 1) bad++;
                if (s.PrimedCount(1f) != 0) bad++;   // both consumed
            }
            pairs++;
        }
        Check("every pair fuses the same in both orders, consuming both (" + pairs + " pairs, " + bad + " wrong)", pairs >= 9 && bad == 0);

        // the external vacuum pair never fuses by itself
        var v = NewState();
        v.OnAtom(AtomKind.Magnet, 0f, 6f);
        var rv = v.OnAtom(AtomKind.Shield, .5f, 6f);
        Check("magnet + shield is left to MagnetCombo (no fusion from the slots alone)", rv.outcome == AtomFusionState.Outcome.Primed && v.PrimedCount(.5f) == 2);
    }

    // ---- windows -------------------------------------------------------------

    static void Windows()
    {
        float m = FusionTuning.MoteSeconds;
        Check("the mote window is about 6 s", Mathf.Approximately(m, 6f));
        var s = NewState();
        s.OnAtom(AtomKind.Pause, 0f, m);
        var inside = s.OnAtom(AtomKind.Capacitor, m - .05f, m);
        Check("instant: a partner just inside the mote's " + m + " s fuses", inside.Fired && inside.fusion.id == FusionId.Overclock);

        s = NewState();
        s.OnAtom(AtomKind.Pause, 0f, m);
        var late = s.OnAtom(AtomKind.Capacitor, m + .05f, m);
        Check("instant: just after it, no fusion (the new one is primed)", late.outcome == AtomFusionState.Outcome.Primed && !s.IsPrimed(AtomKind.Pause, m + .05f));

        // timed: the shield's own clock is the window
        s = NewState();
        s.OnAtom(AtomKind.Shield, 0f, 2f);
        s.KeepTimed(AtomKind.Shield, 4f, 1f);   // refreshed: now runs to 5
        var timedIn = s.OnAtom(AtomKind.Capacitor, 4.5f, m);
        Check("timed: primed as long as its effect runs (kept to 5 s by the live shield)", timedIn.Fired && timedIn.fusion.id == FusionId.Nova);

        s = NewState();
        s.OnAtom(AtomKind.Shield, 0f, 5f);
        s.KeepTimed(AtomKind.Shield, 0f, 1f);   // the shield ended at 1 s
        var timedOut = s.OnAtom(AtomKind.Capacitor, 1.5f, m);
        Check("timed: its effect ending ends the window at once", timedOut.outcome == AtomFusionState.Outcome.Primed);
    }

    // ---- slots ------------------------------------------------------------------

    static void SlotReplacement()
    {
        Check("two primed slots", FusionTuning.MaxSlots == 2);
        var s = NewState();
        s.OnAtom(AtomKind.Heal, 0f, 6f);
        s.OnAtom(AtomKind.Pause, 1f, 6f);      // heal + pause: no recipe
        Check("two non-fusing atoms fill both slots", s.PrimedCount(1f) == 2);
        s.OnAtom(AtomKind.Shell, 2f, 6f);      // shell fuses with neither
        Check("a third replaces the oldest (heal out; pause and shell primed)",
              !s.IsPrimed(AtomKind.Heal, 2f) && s.IsPrimed(AtomKind.Pause, 2f) && s.IsPrimed(AtomKind.Shell, 2f) && s.PrimedCount(2f) == 2);
        var r = s.OnAtom(AtomKind.Shield, 3f, 5f);   // fuses with pause (STASIS) or shell (SHELL BURST)
        Check("two partners primed: the newest one wins (SHELL BURST over STASIS), the other stays primed",
              r.Fired && r.fusion.id == FusionId.ShellBurst && s.IsPrimed(AtomKind.Pause, 3f) && !s.IsPrimed(AtomKind.Shell, 3f));
    }

    // ---- chains ---------------------------------------------------------------

    static void Chains()
    {
        var s = NewState(6f);
        s.OnAtom(AtomKind.Pause, 0f, 6f);
        var f = s.OnAtom(AtomKind.Capacitor, 1f, 6f);
        Check("OVERCLOCK fires at chain 1, window to 7 s", f.Fired && f.chain == 1 && Mathf.Approximately(s.CurrentUntil, 7f));
        var c2 = s.OnAtom(AtomKind.Pause, 5f, 6f);
        Check("a qualifying catch inside the window re-triggers it: CHAIN x2, window reset to 5 + 6 (not 7 + 6)",
              c2.outcome == AtomFusionState.Outcome.Chained && c2.chain == 2 && Mathf.Approximately(s.CurrentUntil, 11f));
        var c3 = s.OnAtom(AtomKind.Capacitor, 10.9f, 6f);
        var c4 = s.OnAtom(AtomKind.Capacitor, 16.8f, 6f);
        Check("no cap: x3, x4 ...", c3.chain == 3 && c4.chain == 4 && s.PrimedCount(16.8f) == 0);
        var after = s.OnAtom(AtomKind.Pause, 30f, 6f);
        Check("after the window it is a plain catch again", after.outcome == AtomFusionState.Outcome.Primed && s.Chain == 0);
        Check("the chain words come from the vacuum's cache (CHAIN xN)", MagnetCombo.WordFor(MagnetCombo.Callout.Chain, 3) == "CHAIN x3");

        // the vacuum's own chain is counted by its owner
        var v = NewState(6f);
        var e1 = v.OnExternal(FusionId.Vacuum, 1, 0f);
        var e2 = v.OnExternal(FusionId.Vacuum, 2, 2f);
        Check("VACUUM: reported start, then CHAIN x2 as MagnetPull counts it",
              e1.outcome == AtomFusionState.Outcome.Fused && e2.outcome == AtomFusionState.Outcome.Chained && e2.chain == 2);
        var absorbed = v.OnAtom(AtomKind.Magnet, 2f, 6f);
        Check("the catch that set the vacuum off (same frame) is absorbed, not chained or primed",
              absorbed.outcome == AtomFusionState.Outcome.Absorbed && v.PrimedCount(2f) == 0 && v.Chain == 2);
    }

    // ---- triples -----------------------------------------------------------------

    static void Triples()
    {
        // the vacuum running, then a heal: SINGULARITY, not LIFELINE / BLOOM
        var s = NewState(6f);
        s.OnAtom(AtomKind.Magnet, 0f, 6f);
        s.OnAtom(AtomKind.Shield, .5f, 5.8f);
        s.OnExternal(FusionId.Vacuum, 1, .5f);
        Check("the vacuum consumes the primed magnet and shield", s.PrimedCount(.5f) == 0);
        var r = s.OnAtom(AtomKind.Heal, 1f, 6f);
        Check("a heal while the vacuum runs: the triple SINGULARITY beats its pairs", r.Fired && r.fusion.id == FusionId.Singularity);

        // BLOOM running, then the vacuum starts: SINGULARITY
        s = NewState(6f);
        s.OnAtom(AtomKind.Heal, 0f, 6f);
        var bloom = s.OnAtom(AtomKind.Shield, 1f, 5.8f);
        var ext = s.OnExternal(FusionId.Vacuum, 1, 2f);
        Check("BLOOM, then the vacuum: SINGULARITY", bloom.fusion.id == FusionId.Bloom && ext.Fired && ext.fusion.id == FusionId.Singularity);
        var re = s.OnExternal(FusionId.Vacuum, 2, 3f);
        Check("... and a vacuum re-chain re-triggers the SINGULARITY", re.outcome == AtomFusionState.Outcome.Chained && re.fusion.id == FusionId.Singularity && re.chain == 2);

        // LIFELINE running, then a shield: the triple, not BLOOM
        s = NewState(6f);
        s.OnAtom(AtomKind.Heal, 0f, 6f);
        s.OnAtom(AtomKind.Magnet, .5f, 6f);
        var t = s.OnAtom(AtomKind.Shield, 1f, 5.8f);
        Check("LIFELINE, then a shield: SINGULARITY (not BLOOM)", t.Fired && t.fusion.id == FusionId.Singularity);
    }

    // ---- same atom --------------------------------------------------------------

    static void SameAtom()
    {
        var s = NewState();
        s.OnAtom(AtomKind.Heal, 0f, 6f);
        var plain = s.OnAtom(AtomKind.Heal, 1f, 6f, 0);
        Check("a second heal with hearts missing: no OVERHEAL", plain.outcome == AtomFusionState.Outcome.Primed);
        var full = s.OnAtom(AtomKind.Heal, 2f, 6f, AtomFusions.HealGates(true));
        Check("a second heal at full hearts: OVERHEAL", full.Fired && full.fusion.id == FusionId.Overheal);
        s = NewState();
        s.OnAtom(AtomKind.Shield, 0f, 5.8f);
        var sh = s.OnAtom(AtomKind.Shield, 2f, 5.8f);
        Check("shield + shield: AFTERSHOCK", sh.Fired && sh.fusion.id == FusionId.Aftershock);
    }

    // ---- discovery ------------------------------------------------------------------

    static void Discovery()
    {
        PlayerPrefs.DeleteKey(FusionDiscoveryStore.PrefsKey);
        PlayerPrefs.DeleteKey(FusionDiscoveryStore.UnseenKey);
        FusionDiscoveryStore.Reload();
        Check("a fresh save: nothing found", FusionDiscoveryStore.DiscoveredCount == 0 && !FusionDiscoveryStore.HasUnseen);
        bool first = FusionDiscoveryStore.RecordFiring(FusionId.Bloom, 1, 20261026);
        bool second = FusionDiscoveryStore.RecordFiring(FusionId.Bloom, 3, 20261101);
        FusionDiscoveryStore.RecordFiring(FusionId.Bloom, 2, 20261102);
        var rec = FusionDiscoveryStore.Get(FusionId.Bloom);
        Check("first discovery is reported once", first && !second);
        Check("counts every firing, keeps the best chain and the first day",
              rec.count == 3 && rec.bestChain == 3 && rec.firstDay == 20261026);
        Check("a NEW mark for the codex tab", FusionDiscoveryStore.HasUnseen);
        FusionDiscoveryStore.Reload();
        var back = FusionDiscoveryStore.Get(FusionId.Bloom);
        Check("persists through a reload (PlayerPrefs)", back.count == 3 && back.bestChain == 3 && back.firstDay == 20261026 &&
              FusionDiscoveryStore.DiscoveredCount == 1 && FusionDiscoveryStore.HasUnseen);
        FusionDiscoveryStore.MarkSeen();
        Check("seen clears the mark", !FusionDiscoveryStore.HasUnseen);

        var into = new FusionDiscoveryStore.Record[AtomFusionTable.Count];
        string extra = FusionDiscoveryStore.Parse("nova:4:2:20261020,futurecombo:9:9:20270101,bad", into);
        string round = FusionDiscoveryStore.Serialize(into, extra);
        Check("unknown entries survive a round trip", extra.Contains("futurecombo:9:9:20270101") && round.Contains("futurecombo") &&
              into[(int)FusionId.Nova].count == 4 && into[(int)FusionId.Nova].bestChain == 2);
        Check("days format as 26 OCT 2026", FusionDiscoveryStore.FormatDay(20261026) == "26 OCT 2026" && FusionDiscoveryStore.FormatDay(0) == "");
    }

    // ---- spawn rates ------------------------------------------------------------------

    static void SpawnRates()
    {
        Check("the green heal atom's multiplier is exactly 1", FusionTuning.HealSpawnMultiplier == 1f && AtomSpawnBias.RateOf(AtomKind.Heal) == 1f &&
              AtomSpawnBias.Budget(AtomKind.Heal, 2) == 2);
        bool others = true;
        foreach (var k in new[] { AtomKind.Shield, AtomKind.Pause, AtomKind.Capacitor, AtomKind.Magnet })
            others &= AtomSpawnBias.RateOf(k) >= 1.4f && AtomSpawnBias.RateOf(k) <= 1.6f;
        Check("shield / pause / capacitor / magnet run +40-60%", others);
        var heal = new GameObject("~heal").AddComponent<HealAtomSpawner>();
        Check("HealAtomSpawner unchanged (2 a world, 60 s first, 70-130 s gap, 60%) and reads no spawn multiplier",
              heal.maxPerWorld == 2 && heal.firstChanceAfter == 60f && heal.gapSeconds == new Vector2(70f, 130f) &&
              Mathf.Approximately(heal.chance, .6f) &&
              !System.IO.File.ReadAllText("Assets/Scripts/Gameplay/Pickups/HealAtomSpawner.cs").Contains("SpawnMultiplier"));
        Object.DestroyImmediate(heal.gameObject);
        Check("allowances: blue 3-5 -> 9-15, red 5 -> 8, capacitor 3, magnet 3",
              PickupRules.ShieldAtomBudget(3) == 9 && PickupRules.ShieldAtomBudget(5) == 15 && PickupRules.PauseAtomBudget(5) == 8 &&
              PickupRules.CooldownAtomsPerWorld == 3 && PickupRules.MagnetAtomsPerWorld == 3);
    }

    static void Complements()
    {
        AtomSpawnBias.Reset();
        bool neverGreen = true, partnersFuse = true;
        foreach (var k in new[] { AtomKind.Shield, AtomKind.Pause, AtomKind.Capacitor, AtomKind.Magnet })
        {
            foreach (var p in AtomSpawnBias.PartnersOf(k))
            {
                neverGreen &= p != AtomKind.Heal;
                var f = AtomFusionTable.Find(AtomFusionTable.Recipe(k, p));
                partnersFuse &= f != null;
            }
            for (float roll = 0f; roll < 1f; roll += .01f)
            {
                AtomSpawnBias.NewWorld();
                if (AtomSpawnBias.OnCaught(k, roll, false)) neverGreen &= AtomSpawnBias.PendingKind != AtomKind.Heal;
            }
        }
        Check("a complement is never the green atom", neverGreen);
        Check("every complement fuses with the atom caught", partnersFuse);
        Check("the heal atom never asks for one", AtomSpawnBias.PartnersOf(AtomKind.Heal).Length == 0);

        AtomSpawnBias.NewWorld();
        Check("a roll over the chance asks nothing", !AtomSpawnBias.OnCaught(AtomKind.Shield, FusionTuning.ComplementChance + .01f, false));
        Check("nothing during a boss fight", !AtomSpawnBias.OnCaught(AtomKind.Shield, 0f, true));
        Check("a low roll asks for a partner after the delay",
              AtomSpawnBias.OnCaught(AtomKind.Shield, 0f, false) && !AtomSpawnBias.Due);
        AtomSpawnBias.Tick(FusionTuning.ComplementDelay + .01f);
        Check("... which is then due", AtomSpawnBias.Due);
        AtomSpawnBias.Resolve(true);
        int n = 1;
        for (int i = 0; i < 20; i++) if (AtomSpawnBias.OnCaught(AtomKind.Pause, 0f, false)) { AtomSpawnBias.Resolve(true); n++; }
        Check("at most " + FusionTuning.ComplementsPerWorld + " a world (" + n + ")", n == FusionTuning.ComplementsPerWorld);

        AtomSpawnBias.Reset();
        AtomSpawnBias.Released(AtomKind.Shield);
        bool held = !AtomSpawnBias.Allows(AtomKind.Shield) && AtomSpawnBias.Allows(AtomKind.Pause);
        AtomSpawnBias.Tick(FusionTuning.SameKindGap);
        Check("the same kind waits SameKindGap after itself, another kind does not", held && AtomSpawnBias.Allows(AtomKind.Shield));
        AtomSpawnBias.Reset();
    }

    // ---- the run glue ------------------------------------------------------------------

    static GameObject NewShip()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        ClearTarget.Live.Clear();
        var ship = new GameObject("~fusionShip", typeof(SpriteRenderer));
        ship.transform.position = new Vector3(0f, -3f, 0f);
        return ship;
    }

    static void RunEffects()
    {
        // nothing outside a begun run, nothing in the tutorial
        AtomFusions.EnabledOverride = null;
        AtomFusions.BeginRun(null);
        Check("no run: a catch does nothing", AtomFusions.OnAtom(AtomKind.Pause, Vector3.zero).outcome == AtomFusionState.Outcome.None);
        var ship = NewShip();
        startMenu.youAreInTutorial = true;
        AtomFusions.BeginRun(ship);
        Check("tutorial: no fusions, nothing primed", AtomFusions.OnAtom(AtomKind.Pause, Vector3.zero).outcome == AtomFusionState.Outcome.None &&
              AtomFusions.State.PrimedCount(AtomFusions.Clock) == 0);
        startMenu.youAreInTutorial = false;
        AtomFusions.BeginRun(ship);
        Check("a real run: a catch primes", AtomFusions.Enabled && AtomFusions.OnAtom(AtomKind.Heal, Vector3.zero).outcome == AtomFusionState.Outcome.Primed);

        // STASIS: free pauses while shielded, capped
        AtomFusions.BeginRun(ship);
        collisionDetection.atomCheck = true;
        collisionDetection.invTimer = 5f;
        AtomFusions.OnAtom(AtomKind.Pause, Vector3.zero);
        var st = AtomFusions.OnAtom(AtomKind.Shield, Vector3.zero);
        int free = 0;
        for (int i = 0; i < 10; i++) if (AtomFusions.ConsumeFreePause()) free++;
        Check("STASIS: exactly " + FusionTuning.StasisFreePauses + " free pauses (" + free + ")",
              st.Fired && st.fusion.id == FusionId.Stasis && free == FusionTuning.StasisFreePauses);
        Check("... recorded in the codex", FusionDiscoveryStore.IsDiscovered(FusionId.Stasis));

        // BLOOM: shield kills bank a heart, capped
        AtomFusions.BeginRun(ship);
        collisionDetection.lifeCounter = 3;
        AtomFusions.OnAtom(AtomKind.Heal, Vector3.zero);
        var bl = AtomFusions.OnAtom(AtomKind.Shield, Vector3.zero);
        int healsBefore = AtomFusions.BloomHeals;
        for (int i = 0; i < FusionTuning.BloomKillsPerHeart; i++) AtomFusions.OnShieldKill();
        int afterFirst = collisionDetection.lifeCounter;
        int healsBefore = AtomFusions.BloomHeals;
        for (int i = 0; i < FusionTuning.BloomKillsPerHeart; i++) AtomFusions.OnShieldKill();   // inside the min gap: banked, not paid
        int sameMoment = collisionDetection.lifeCounter;
        AtomFusions.Tick(FusionTuning.BloomMinHeartGap + .01f);
        AtomFusions.OnShieldKill();
        int afterGap = collisionDetection.lifeCounter;
        for (int k = 0; k < 4; k++) { AtomFusions.Tick(FusionTuning.BloomMinHeartGap + .01f); for (int i = 0; i < FusionTuning.BloomKillsPerHeart; i++) AtomFusions.OnShieldKill(); }
        Check("BLOOM: " + FusionTuning.BloomKillsPerHeart + " shield kills restore a heart, rate-capped, at most " + FusionTuning.BloomMaxHearts +
              " (3 -> " + afterFirst + " -> " + sameMoment + " -> " + afterGap + " -> " + collisionDetection.lifeCounter + ")",
              bl.Fired && bl.fusion.id == FusionId.Bloom && afterFirst == 2 && sameMoment == 2 && afterGap == 1 &&
              collisionDetection.lifeCounter == 3 - FusionTuning.BloomMaxHearts);
        collisionDetection.lifeCounter = 0;

        // AFTERSHOCK: the parting shockwave reaches further, once
        AtomFusions.BeginRun(ship);
        collisionDetection.invTimer = 5f;
        float radius = ShieldShockwave.Radius;
        AtomFusions.OnAtom(AtomKind.Shield, Vector3.zero);
        var af = AtomFusions.OnAtom(AtomKind.Shield, Vector3.zero);
        AtomFusions.OnShieldEnding(Vector3.zero);
        float during = ShieldShockwave.Radius;
        AtomFusions.OnShieldEnded();
        Check("AFTERSHOCK: x" + FusionTuning.AftershockRadiusScale + " on the shield's parting wave, then back",
              af.Fired && af.fusion.id == FusionId.Aftershock && Mathf.Approximately(during, radius * FusionTuning.AftershockRadiusScale) &&
              ShieldShockwave.Radius == radius);

        // leaving mid-Aftershock (GameStateReset.Clear) must restore the radius too
        AtomFusions.BeginRun(ship);
        collisionDetection.invTimer = 5f;
        AtomFusions.OnAtom(AtomKind.Shield, Vector3.zero);
        AtomFusions.OnAtom(AtomKind.Shield, Vector3.zero);
        AtomFusions.OnShieldEnding(Vector3.zero);
        bool scaled = !Mathf.Approximately(ShieldShockwave.Radius, radius);
        GameStateReset.Clear();
        bool back = ShieldShockwave.Radius == radius;
        AtomFusions.RestoreAftershock();   // idempotent
        Check("AFTERSHOCK: GameStateReset.Clear restores the scaled radius (idempotent)",
              scaled && back && ShieldShockwave.Radius == radius);
        collisionDetection.invTimer = 0f;

        // DUST STORM: the dust multipliers while the magnet pulls
        AtomFusions.BeginRun(ship);
        collisionDetection.atomCheck = false;
        collisionDetection.invTimer = 0f;
        Check("no storm: x1", AtomFusions.DustChargeMultiplier == 1f && AtomFusions.DustMeterMultiplier == 1f);
        var pull = MagnetPull.Begin(ship);
        AtomFusions.OnAtom(AtomKind.Magnet, Vector3.zero);
        var ds = AtomFusions.OnAtom(AtomKind.Capacitor, Vector3.zero);
        Check("DUST STORM: dust charges x" + FusionTuning.DustStormChargeMultiplier + " while the magnet pulls",
              ds.Fired && ds.fusion.id == FusionId.DustStorm && AtomFusions.DustChargeMultiplier == FusionTuning.DustStormChargeMultiplier &&
              AtomFusions.DustMeterMultiplier == FusionTuning.DustStormMeterMultiplier);
        pull.End();
        AtomFusions.Tick(.1f);
        Check("... and ends with the magnet", AtomFusions.DustChargeMultiplier == 1f);

        // OVERCLOCK: the pauses refill
        AtomFusions.BeginRun(ship);
        score.pauseCounter = 1;
        AtomFusions.OnAtom(AtomKind.Pause, Vector3.zero);
        var oc = AtomFusions.OnAtom(AtomKind.Capacitor, Vector3.zero);
        Check("OVERCLOCK: pauses refill to " + FusionTuning.OverclockPausesTo, oc.Fired && score.pauseCounter == FusionTuning.OverclockPausesTo);
        score.pauseCounter = 0;

        // the vacuum, as MagnetCombo reports it, lands in the codex
        AtomFusions.BeginRun(ship);
        AtomFusions.OnExternal(FusionId.Vacuum, 1);
        Check("VACUUM reported by MagnetCombo is recorded as fusion #1", FusionDiscoveryStore.IsDiscovered(FusionId.Vacuum));

        // death stops it all
        buttonClicks.playerDied = true;
        Check("a dead pilot fuses nothing", AtomFusions.OnAtom(AtomKind.Pause, Vector3.zero).outcome == AtomFusionState.Outcome.None);
        buttonClicks.playerDied = false;
        collisionDetection.atomCheck = false;
        collisionDetection.invTimer = 0f;
        Object.DestroyImmediate(ship);
    }

    // ---- allocation ------------------------------------------------------------------

    static void NoAllocation()
    {
        var s = NewState();
        // warm up
        for (int i = 0; i < 50; i++)
        {
            float t = i * .5f;
            s.OnAtom((AtomKind)(i % 5), t, 6f, i % 3 == 0 ? 1 : 0);
            s.KeepTimed(AtomKind.Shield, 2f, t);
            s.Expire(t);
            s.OnExternal(FusionId.Vacuum, 1 + i % 3, t);
        }
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++)
        {
            float t = 100f + i * .3f;
            s.OnAtom((AtomKind)(i % 7), t, 6f, i % 3 == 0 ? 1 : 0);
            s.KeepTimed(AtomKind.Shield, 2f, t);
            s.KeepTimed(AtomKind.Magnet, 0f, t);
            s.Expire(t);
            if (i % 17 == 0) s.OnExternal(FusionId.Vacuum, 1 + i % 3, t);
            s.PrimedCount(t);
        }
        long state = System.GC.GetAllocatedBytesForCurrentThread() - before;

        var ship = NewShip();
        AtomFusions.EnabledOverride = true;
        AtomFusions.BeginRun(ship);
        var motes = FusionMotes.Attach(ship);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(FusionMotes).GetMethod("Awake", flags).Invoke(motes, null);
        var late = typeof(FusionMotes).GetMethod("LateUpdate", flags);
        AtomFusions.OnAtom(AtomKind.Pause, Vector3.zero);
        AtomFusions.OnAtom(AtomKind.Heal, Vector3.zero);
        for (int i = 0; i < 10; i++) { AtomFusions.Tick(.016f); late.Invoke(motes, null); }
        Check("two instant atoms primed: two motes circle the ship", motes.Shown == 2);
        // (the reflective Invoke itself allocates: measure the frame's own work)
        before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            AtomFusions.Tick(.016f);
            bool l = AtomFusions.LifelineActive;
            float d = AtomFusions.DustChargeMultiplier * AtomFusions.DustMeterMultiplier;
            AtomFusions.OnShieldKill();
            if (l && d < 0f) Debug.Log("");
        }
        long run = System.GC.GetAllocatedBytesForCurrentThread() - before;
        AtomFusions.EnabledOverride = null;
        Object.DestroyImmediate(ship);
        Check("the rules allocate nothing per call (" + state + " bytes over 2000 catches)", state == 0);
        Check("the per-frame tick and queries allocate nothing (" + run + " bytes over 1000 frames)", run == 0);
    }

    // ---- each fusion's own flipbook (FusionFx) ---------------------------------------------

    static void FusionFlipbooks()
    {
        var all = AtomFusionTable.All;
        // art: one strip per fusion, nine frames, every file different
        var seen = new HashSet<string>();
        bool allArt = true, distinct = true;
        for (int i = 0; i < all.Length; i++)
        {
            allArt &= FusionFx.HasArt(all[i].id);
            string path = "Assets/Art/Resources/Fusion/" + all[i].key + ".png";
            if (!System.IO.File.Exists(path)) { allArt = false; continue; }
            var bytes = System.IO.File.ReadAllBytes(path);
            distinct &= seen.Add(System.Convert.ToBase64String(System.Security.Cryptography.SHA1.Create().ComputeHash(bytes)));
        }
        Check("every fusion has its own " + FusionFx.FrameCount + "-frame flipbook strip (" + all.Length + " of them)", allArt);
        Check("no two fusions share an effect (all " + seen.Count + " strips differ)", distinct && seen.Count == all.Length);

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var ship = new GameObject("~fxShip", typeof(SpriteRenderer));
        ship.transform.position = new Vector3(1f, -3f, 0f);
        AtomFusions.EnabledOverride = true;
        AtomFusions.BeginRun(ship);
        var fx = FusionFx.Instance;
        Check("the run builds the effect layers once", fx != null);
        if (fx == null) { AtomFusions.EnabledOverride = null; Object.DestroyImmediate(ship); return; }

        // a real trigger plays that fusion's flipbook (and only that one)
        AtomFusions.OnAtom(AtomKind.Pause, Vector3.zero);
        var r = AtomFusions.OnAtom(AtomKind.Capacitor, Vector3.zero);
        int playing = 0;
        for (int i = 0; i < all.Length; i++) if (fx.IsPlaying(all[i].id)) playing++;
        Check("OVERCLOCK fires: its flipbook plays on the ship, no other",
              r.Fired && r.fusion.id == FusionId.Overclock && fx.IsPlaying(FusionId.Overclock) && playing == 1);
        var layer = fx.LayerOf(FusionId.Overclock);
        Check("... centred on the ship, behind the hull, restrained alpha",
              layer.enabled && Vector2.Distance(layer.transform.position, ship.transform.position) < .01f &&
              layer.sortingOrder < ship.GetComponent<SpriteRenderer>().sortingOrder && layer.color.a <= .85f);
        Check("... and small enough to leave the lane readable (<= 5.2 u wide)", layer.bounds.size.x <= 5.25f);

        // the world's clock: a frozen world holds the frame, a running one steps it and ends it
        int f0 = fx.FrameOf(FusionId.Overclock);
        AtomFusions.Tick(0f);
        Check("a paused world (dt 0) holds the frame", fx.FrameOf(FusionId.Overclock) == f0);
        AtomFusions.Tick(.3f);
        Check("a running world steps the flipbook", fx.FrameOf(FusionId.Overclock) > f0);
        AtomFusions.Tick(1f);
        Check("it ends after one play (" + (FusionFx.FrameCount / FusionFx.Fps).ToString("0.00") + " s) and hides", !fx.IsPlaying(FusionId.Overclock) && !layer.enabled);

        // every fusion plays its own layer
        bool own = true;
        for (int i = 0; i < all.Length; i++)
        {
            FusionFx.Play(all[i].id);
            own &= fx.IsPlaying(all[i].id) && fx.LayerOf(all[i].id).sprite != null && fx.LayerOf(all[i].id).sprite.texture.name == all[i].key;
        }
        Check("each of the " + all.Length + " fusions plays a layer of its own art", own);
        bool distinctLayers = true;
        for (int i = 0; i < all.Length; i++) for (int j = i + 1; j < all.Length; j++) distinctLayers &= fx.LayerOf(all[i].id) != fx.LayerOf(all[j].id);
        Check("... on separate pooled renderers", distinctLayers);

        // off (kill cam replay / dead pilot): dark at once
        AtomFusions.EnabledOverride = false;
        typeof(FusionFx).GetMethod("LateUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(fx, null);
        bool dark = true;
        for (int i = 0; i < all.Length; i++) dark &= !fx.LayerOf(all[i].id).enabled && !fx.IsPlaying(all[i].id);
        Check("fusions off (kill cam / death): every effect goes dark", dark);
        AtomFusions.EnabledOverride = true;

        // no allocation per trigger: the same layers are re-used, nothing is created
        int builds = FusionFx.Builds;
        int children = fx.transform.childCount;
        for (int w = 0; w < 20; w++) { for (int i = 0; i < all.Length; i++) FusionFx.Play(all[i].id); AtomFusions.Tick(.1f); }   // warm
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int n = 0; n < 500; n++)
        {
            for (int i = 0; i < all.Length; i++) FusionFx.Play(all[i].id);
            for (int k = 0; k < 12; k++) AtomFusions.Tick(.06f);
        }
        long fxBytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check("6000 triggers and their flipbooks allocate nothing (" + fxBytes + " bytes)", fxBytes == 0);
        Check("... and create no objects (renderers " + fx.transform.childCount + " = " + children + ", builds " + FusionFx.Builds + ")",
              fx.transform.childCount == children && FusionFx.Builds == builds && children == all.Length + FusionFx.OverlayRenderers);

        AtomFusions.EnabledOverride = null;
        Object.DestroyImmediate(ship);
    }

    // ---- VACUUM / SINGULARITY loop while live, fade out when not ---------------------------------

    static void LoopingFusions()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var ship = new GameObject("~loopShip", typeof(SpriteRenderer));
        AtomFusions.EnabledOverride = true;
        AtomFusions.BeginRun(ship);
        var fx = FusionFx.Instance;
        buttonClicks.playerDied = false;
        collisionDetection.atomCheck = true;
        collisionDetection.invTimer = 5.8f;
        var pull = MagnetPull.Begin(ship);
        var layer = fx.LayerOf(FusionId.Vacuum);

        AtomFusions.Tick(.01f);
        AtomFusions.OnExternal(FusionId.Vacuum, 1);
        Check("VACUUM: the vortex starts by itself when magnet + shield are both live", fx.IsPlaying(FusionId.Vacuum) && layer.enabled);
        AtomFusions.Tick(5f);
        Check("... and is still looping well past the old one-shot " + (FusionFx.FrameCount / FusionFx.Fps).ToString("0.00") + " s",
              fx.IsPlaying(FusionId.Vacuum) && layer.enabled && layer.color.a >= FusionFx.Alpha - .01f);
        AtomFusions.Tick(0f);
        bool held = fx.IsPlaying(FusionId.Vacuum);
        Check("... a paused world (dt 0) holds it", held);

        // SINGULARITY takes over the vortex, flashes on a heal
        collisionDetection.lifeCounter = 2;
        AtomFusions.OnAtom(AtomKind.Heal, Vector3.zero);
        AtomFusions.Tick(.05f);
        Check("SINGULARITY: its own vortex loops while its window and the vacuum run", AtomFusions.SingularityLive && fx.IsPlaying(FusionId.Singularity));
        AtomFusions.OnShieldKill();
        AtomFusions.Tick(.05f);
        Check("... a healed heart flashes it green", AtomFusions.SingularityHeals >= 1 && fx.LayerOf(FusionId.Singularity).color.g > fx.LayerOf(FusionId.Singularity).color.b + .05f);

        // the shield ends: the combo is off, so both fade out within LoopFadeSeconds and hide
        collisionDetection.invTimer = 0f;
        collisionDetection.atomCheck = false;
        AtomFusions.Tick(FusionTuning.LoopFadeSeconds * .5f);
        bool fading = fx.IsPlaying(FusionId.Singularity) && fx.LayerOf(FusionId.Singularity).color.a < FusionFx.Alpha;
        AtomFusions.Tick(FusionTuning.LoopFadeSeconds);
        Check("the effect ends: it fades over " + FusionTuning.LoopFadeSeconds + " s, then is gone (cannot outlive the rules)",
              fading && !fx.IsPlaying(FusionId.Vacuum) && !fx.IsPlaying(FusionId.Singularity) && !layer.enabled && !fx.LayerOf(FusionId.Singularity).enabled);

        // death / GameStateReset end it cleanly
        collisionDetection.atomCheck = true;
        collisionDetection.invTimer = 5f;
        MagnetPull.Begin(ship);
        AtomFusions.Tick(.05f);
        bool back = fx.IsPlaying(FusionId.Singularity);   // the singularity window is still open
        buttonClicks.playerDied = true;
        AtomFusions.EnabledOverride = false;   // a dead pilot turns fusions off (Enabled)
        AtomFusions.Tick(.05f);
        bool deadStops = !AtomFusions.VacuumLive;
        typeof(FusionFx).GetMethod("LateUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(fx, null);
        Check("death: the loop is dark at once", back && deadStops && !fx.IsPlaying(FusionId.Singularity) && !fx.IsPlaying(FusionId.Vacuum) && !fx.LayerOf(FusionId.Singularity).enabled);
        buttonClicks.playerDied = false;
        AtomFusions.EnabledOverride = true;

        // no allocation while looping
        MagnetPull.Begin(ship);
        for (int w = 0; w < 20; w++) AtomFusions.Tick(.05f);
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int n = 0; n < 2000; n++) { MagnetPull.Begin(ship); AtomFusions.Tick(.016f); }
        long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Check("2000 looping frames allocate nothing (" + bytes + " bytes)", bytes == 0);

        // NOVA's wipe is 60% wider
        Check("NOVA: the clear radius is 4.5 x 1.6 = 7.2 u", Mathf.Approximately(FusionTuning.NovaClearRadius, 7.2f));

        collisionDetection.atomCheck = false;
        collisionDetection.invTimer = 0f;
        collisionDetection.lifeCounter = 0;
        AtomFusions.EnabledOverride = null;
        Object.DestroyImmediate(ship);
    }

    // ---- DUST STORM / BLOOM / STASIS / LIFELINE loops, NOVA's kill and ring -----------------------

    static GameObject Plain(string tag, Vector2 at)
    {
        var go = new GameObject("~novaBody");
        go.tag = tag;
        go.transform.position = at;
        var col = go.AddComponent<CircleCollider2D>(); col.radius = .3f;
        go.AddComponent<ClearTarget>();
        return go;
    }

    static void MoreLoopsAndNova()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var ship = new GameObject("~moreShip", typeof(SpriteRenderer));
        AtomFusions.EnabledOverride = true;
        AtomFusions.BeginRun(ship);
        var fx = FusionFx.Instance;
        buttonClicks.playerDied = false;
        collisionDetection.atomCheck = false;
        collisionDetection.invTimer = 0f;

        // DUST STORM: dust streams in while the magnet pulls, fades out when it ends
        var pull = MagnetPull.Begin(ship);
        AtomFusions.Tick(.01f);
        AtomFusions.OnAtom(AtomKind.Capacitor, Vector3.zero);
        var ds = AtomFusions.OnAtom(AtomKind.Magnet, Vector3.zero);
        AtomFusions.Tick(.3f);
        Check("DUST STORM: violet dust streams while the magnet pulls (loop + dust)",
              ds.Fired && ds.fusion.id == FusionId.DustStorm && AtomFusions.DustStormLive && fx.IsPlaying(FusionId.DustStorm) && fx.DotsShowing(0) > 3);
        pull.End();
        AtomFusions.Tick(.1f);
        bool dusting = fx.DotsShowing(0) > 0;
        AtomFusions.Tick(.3f);
        Check("... and fades out within " + FusionTuning.LoopFadeSeconds + " s of the magnet ending", dusting && fx.DotsShowing(0) == 0 && !fx.IsPlaying(FusionId.DustStorm));

        // BLOOM: petals orbit the shield, pop on a heal
        AtomFusions.BeginRun(ship);
        collisionDetection.atomCheck = true;
        collisionDetection.invTimer = 5.5f;
        collisionDetection.lifeCounter = 2;
        AtomFusions.Tick(.01f);
        AtomFusions.OnAtom(AtomKind.Shield, Vector3.zero);
        var bl = AtomFusions.OnAtom(AtomKind.Heal, Vector3.zero);
        AtomFusions.Tick(.3f);
        Check("BLOOM: petals orbit the shield while it blooms", bl.Fired && bl.fusion.id == FusionId.Bloom && AtomFusions.BloomLive && fx.IsPlaying(FusionId.Bloom) && fx.DotsShowing(1) == 6);
        float dim = fx.DotAlpha(1, 0);
        int healsBefore = AtomFusions.BloomHeals;
        for (int i = 0; i < FusionTuning.BloomKillsPerHeart; i++) AtomFusions.OnShieldKill();
        AtomFusions.Tick(.02f);
        Check("... a heal pops them brighter", AtomFusions.BloomHeals == healsBefore + 1 && fx.DotAlpha(1, 0) > dim + .2f);
        collisionDetection.invTimer = 0f; collisionDetection.atomCheck = false;
        AtomFusions.Tick(.4f);
        Check("... the shield ending ends them", !AtomFusions.BloomLive && fx.DotsShowing(1) == 0 && !fx.IsPlaying(FusionId.Bloom));

        // STASIS: a pip per free pause, one goes dark per use
        AtomFusions.BeginRun(ship);
        collisionDetection.atomCheck = true;
        collisionDetection.invTimer = 5.5f;
        AtomFusions.Tick(.01f);
        AtomFusions.OnAtom(AtomKind.Pause, Vector3.zero);
        var st = AtomFusions.OnAtom(AtomKind.Shield, Vector3.zero);
        AtomFusions.Tick(.3f);
        int litBefore = 0, litAfter = 0;
        for (int i = 0; i < FusionTuning.StasisFreePauses; i++) if (fx.DotAlpha(2, i) > .4f) litBefore++;
        AtomFusions.ConsumeFreePause();
        AtomFusions.Tick(.02f);
        for (int i = 0; i < FusionTuning.StasisFreePauses; i++) if (fx.DotAlpha(2, i) > .4f) litAfter++;
        Check("STASIS: a pip per free pause (" + litBefore + "), one goes dark per use (" + litAfter + ")",
              st.Fired && st.fusion.id == FusionId.Stasis && AtomFusions.StasisLive && fx.IsPlaying(FusionId.Stasis) &&
              litBefore == FusionTuning.StasisFreePauses && litAfter == litBefore - 1);
        collisionDetection.invTimer = 0f; collisionDetection.atomCheck = false;
        AtomFusions.Tick(.4f);
        Check("... gone with the shield", fx.DotsShowing(2) == 0 && !fx.IsPlaying(FusionId.Stasis));

        // LIFELINE: band along the top edge while the extended reach is live
        AtomFusions.BeginRun(ship);
        pull = MagnetPull.Begin(ship);
        AtomFusions.Tick(.01f);
        AtomFusions.OnAtom(AtomKind.Magnet, Vector3.zero);
        var ll = AtomFusions.OnAtom(AtomKind.Heal, Vector3.zero);
        AtomFusions.Tick(.3f);
        bool llLive = ll.Fired && ll.fusion.id == FusionId.Lifeline && AtomFusions.LifelineLive && fx.IsPlaying(FusionId.Lifeline);
        bool noCam = Camera.main == null;   // with no camera there is no edge to draw on: the band stays off
        Check("LIFELINE: loops while its reach is live", llLive && (noCam || fx.BandShowing));
        pull.End();
        AtomFusions.Tick(.4f);
        Check("... ends with the magnet", !fx.IsPlaying(FusionId.Lifeline) && !fx.BandShowing && fx.DotsShowing(3) == 0);

        // NOVA: armed charge loops inside the shield; the clear kills plain enemies, ring reaches exactly the radius
        AtomFusions.BeginRun(ship);
        ship.transform.position = Vector3.zero;
        collisionDetection.atomCheck = true;
        collisionDetection.invTimer = 5f;
        AtomFusions.Tick(.01f);
        AtomFusions.OnAtom(AtomKind.Shield, Vector3.zero);
        var nv = AtomFusions.OnAtom(AtomKind.Capacitor, Vector3.zero);
        AtomFusions.Tick(1f);
        Check("NOVA: the armed charge loops while the shield holds", nv.Fired && nv.fusion.id == FusionId.Nova && AtomFusions.NovaLive && fx.IsPlaying(FusionId.Nova));
        var inner = Plain("Enimey", new Vector2(0f, 6.5f));      // past the old 4.5, inside the new 7.2
        var far = Plain("Enimey", new Vector2(0f, 8.5f));
        var rock = Plain("Astr", new Vector2(2f, 2f));
        int killsBefore = AtomFusions.NovaKills;
        collisionDetection.invTimer = 0f;
        AtomFusions.OnShieldEnding(Vector3.zero);
        Check("NOVA: a plain enemy inside 7.2 u dies by the ordinary kill; one outside, and a rock, do not",
              AtomFusions.NovaKills == killsBefore + 1 && (inner == null || !inner.activeInHierarchy || !inner.GetComponent<ClearTarget>().enabled) &&
              far != null && far.GetComponent<ClearTarget>().enabled && rock != null && rock.GetComponent<ClearTarget>().enabled);
        AtomFusions.OnShieldEnded();
        collisionDetection.atomCheck = false;
        AtomFusions.Tick(FusionFx.NovaRingSeconds * FusionFx.NovaGrowShare);
        Check("... a ring grows to exactly the clear radius (" + FusionTuning.NovaClearRadius + " u)",
              fx.NovaRingShowing && Mathf.Abs(fx.NovaRingRadius - FusionTuning.NovaClearRadius) < .05f && fx.NovaRingRenderer.color.a <= .7f);
        AtomFusions.Tick(FusionFx.NovaRingSeconds);
        Check("... then fades away", !fx.NovaRingShowing);
        if (far != null) Object.DestroyImmediate(far);
        if (rock != null) Object.DestroyImmediate(rock);
        if (inner != null) Object.DestroyImmediate(inner);

        // nothing in the tutorial: no kills
        AtomFusions.BeginRun(ship);
        collisionDetection.atomCheck = true; collisionDetection.invTimer = 5f;
        AtomFusions.Tick(.01f);
        AtomFusions.OnAtom(AtomKind.Shield, Vector3.zero);
        AtomFusions.OnAtom(AtomKind.Capacitor, Vector3.zero);
        var bystander = Plain("Enimey", new Vector2(0f, 3f));
        startMenu.youAreInTutorial = true;
        int before = AtomFusions.NovaKills;
        collisionDetection.invTimer = 0f;
        AtomFusions.OnShieldEnding(Vector3.zero);
        startMenu.youAreInTutorial = false;
        Check("NOVA kills nothing in the tutorial", AtomFusions.NovaKills == before && bystander.GetComponent<ClearTarget>().enabled);
        Object.DestroyImmediate(bystander);

        // every loop ends cleanly on death
        AtomFusions.BeginRun(ship);
        collisionDetection.atomCheck = true; collisionDetection.invTimer = 5f;
        AtomFusions.Tick(.01f);
        AtomFusions.OnAtom(AtomKind.Shield, Vector3.zero);
        AtomFusions.OnAtom(AtomKind.Heal, Vector3.zero);
        AtomFusions.Tick(.3f);
        bool bloomOn = fx.DotsShowing(1) > 0;
        buttonClicks.playerDied = true;
        AtomFusions.EnabledOverride = false;
        typeof(FusionFx).GetMethod("LateUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(fx, null);
        Check("death: the overlays go dark at once", bloomOn && fx.DotsShowing(1) == 0 && !fx.IsPlaying(FusionId.Bloom));
        buttonClicks.playerDied = false;
        AtomFusions.EnabledOverride = true;

        // no allocation while they run
        AtomFusions.BeginRun(ship);
        collisionDetection.atomCheck = true; collisionDetection.invTimer = 5.5f;
        AtomFusions.Tick(.01f);
        AtomFusions.OnAtom(AtomKind.Shield, Vector3.zero);
        AtomFusions.OnAtom(AtomKind.Heal, Vector3.zero);
        for (int w = 0; w < 20; w++) AtomFusions.Tick(.05f);
        long mem0 = System.GC.GetAllocatedBytesForCurrentThread();
        for (int n = 0; n < 1000; n++) { collisionDetection.invTimer = 5.5f; AtomFusions.Tick(.016f); }
        long bytes = System.GC.GetAllocatedBytesForCurrentThread() - mem0;
        Check("1000 frames of bloom petals allocate nothing (" + bytes + " bytes)", bytes == 0);

        pull.End();
        collisionDetection.atomCheck = false;
        collisionDetection.invTimer = 0f;
        collisionDetection.lifeCounter = 0;
        AtomFusions.EnabledOverride = null;
        Object.DestroyImmediate(ship);
    }

    // ---- the dev fusion test (FusionTestMode) -------------------------------------------------

    static GameObject Inert(string name)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        return go;
    }

    // Steps a real spawnGoodStuff for `seconds`; returns the clone names' counts (atoms by prefab name).
    static Dictionary<string, int> SimulateSpawner(int seed, float seconds)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Random.InitState(seed);
        var go = new GameObject("~spawnGoodStuffFusionTest");
        go.transform.position = new Vector3(0f, 6.5f, 0f);
        var sp = go.AddComponent<spawnGoodStuff>();
        sp.smStar = Inert("smStar1"); sp.midStar = Inert("LargeStar1"); sp.Atom = Inert("atom3a"); sp.redAtom = Inert("pauseAtom");
        var inst = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        typeof(spawnGoodStuff).GetMethod("Start", inst).Invoke(sp, null);
        var step = typeof(spawnGoodStuff).GetMethod("spawn", inst, null, new[] { typeof(float) }, null);
        var args = new object[] { .1f };
        for (int i = 0; i < Mathf.RoundToInt(seconds / .1f); i++) step.Invoke(sp, args);
        var counts = new Dictionary<string, int>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == null || !t.name.EndsWith("(Clone)")) continue;
            string n = t.name.Substring(0, t.name.Length - 7);
            counts[n] = (counts.ContainsKey(n) ? counts[n] : 0) + 1;
        }
        return counts;
    }

    static int Count(Dictionary<string, int> d, string k) { return d.ContainsKey(k) ? d[k] : 0; }

    static void DevFusionTest()
    {
        int n = AtomFusionTable.Count;
        // the selector: every combo in table order, then OFF, then round again
        int pos = FusionTestMode.Off;
        bool cycle = true;
        for (int i = 0; i < n; i++) { pos = FusionTestMode.Next(pos, n); cycle &= pos == i; }
        pos = FusionTestMode.Next(pos, n);
        cycle &= pos == FusionTestMode.Off && FusionTestMode.Next(pos, n) == 0;
        Check("the selector cycles through all " + n + " combos, then OFF, then round again", cycle && FusionTestMode.Positions == n + 1);
        bool labels = FusionTestMode.Label(FusionTestMode.Off).EndsWith("OFF");
        for (int i = 0; i < n; i++) labels &= FusionTestMode.Label(i).EndsWith(AtomFusionTable.All[i].name);
        Check("the button shows the current combo's name (OFF for normal spawning)", labels);

        // Step() walks the same ring and starts off
        FusionTestMode.Reset();
        Check("it starts OFF (never persisted: a restart is OFF too)", FusionTestMode.Selected == FusionTestMode.Off);
        FusionTestMode.Step();
        Check("Step moves to the first combo", FusionTestMode.Selected == 0);

        // developer mode OFF: selected but never applied
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        FusionTestMode.SetForTest((int)FusionId.Stasis);
        AtomKind k;
        Check("developer mode off: the selection is not active and releases nothing", !FusionTestMode.Active && !FusionTestMode.Due(10f, out k));
        var off = SimulateSpawner(7, 120f);
        FusionTestMode.Reset();
        var normal = SimulateSpawner(7, 120f);
        bool same = off.Count == normal.Count;
        foreach (var kv in normal) same &= Count(off, kv.Key) == kv.Value;
        Check("developer mode off: spawning is exactly the normal rules (same seed, same atoms)", same);

        // developer mode ON: only the combo's atoms, each of them
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        bool onlyRight = true, eachSeen = true;
        for (int i = 0; i < n; i++)
        {
            var f = AtomFusionTable.All[i];
            FusionTestMode.SetForTest(i);
            var seen = new HashSet<AtomKind>();
            for (int t = 0; t < 12; t++)
            {
                AtomKind kind;
                if (!FusionTestMode.Due(FusionTestMode.AtomGap + .01f, out kind)) { onlyRight = false; continue; }
                seen.Add(kind);
                onlyRight &= f.Uses(kind) && AtomFusionTable.IsPickup(kind);
            }
            for (int a = 0; a < f.atoms.Length; a++) if (AtomFusionTable.IsPickup(f.atoms[a])) eachSeen &= seen.Contains(f.atoms[a]);
        }
        Check("developer mode on: every combo releases only its own pickup atoms", onlyRight);
        Check("... and every pickup atom of the combo comes round", eachSeen);

        // the real spawner: STASIS (pause + shield) -> only blue and red atoms (stars still fall)
        FusionTestMode.SetForTest((int)FusionId.Stasis);
        var test = SimulateSpawner(7, 120f);
        int blue = Count(test, "atom3a"), red = Count(test, "pauseAtom");
        int others = 0;
        foreach (var kv in test) if (kv.Key.EndsWith("Atom") || kv.Key.StartsWith("atom")) if (kv.Key != "atom3a" && kv.Key != "pauseAtom") others += kv.Value;
        Check("the spawner releases STASIS's atoms often (blue " + blue + ", pause " + red + " in 120 s) and no other atom (" + others + ")",
              blue >= 10 && red >= 10 && others == 0);

        // developer mode going off puts it back to OFF
        FusionTestMode.SetForTest(3);
        DeveloperUnlocks.SetEnabled(false);
        Check("switching developer mode off resets the test to OFF", FusionTestMode.Selected == FusionTestMode.Off && !FusionTestMode.Active);

        // a test firing is not progress: the codex records nothing
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 1);
        PlayerPrefs.DeleteKey(FusionDiscoveryStore.PrefsKey);
        FusionDiscoveryStore.Reload();
        var ship = NewShip();
        AtomFusions.EnabledOverride = true;
        AtomFusions.BeginRun(ship);
        FusionTestMode.SetForTest((int)FusionId.Overclock);
        AtomFusions.OnAtom(AtomKind.Pause, Vector3.zero);
        var fired = AtomFusions.OnAtom(AtomKind.Capacitor, Vector3.zero);
        Check("a fusion fired in the dev test is not recorded in the codex", fired.Fired && !FusionDiscoveryStore.IsDiscovered(FusionId.Overclock));
        AtomFusions.EnabledOverride = null;
        FusionTestMode.Reset();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        Object.DestroyImmediate(ship);
    }

    // ---- the codex tab -----------------------------------------------------------------

    static void CodexTab()
    {
        PlayerPrefs.SetString(FusionDiscoveryStore.PrefsKey, "nova:5:3:20261026");
        PlayerPrefs.SetInt(FusionDiscoveryStore.UnseenKey, 1);
        FusionDiscoveryStore.Reload();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var es = new GameObject("TestEventSystem", typeof(EventSystem));
        CodexPanel panel = null;
        try
        {
            panel = CodexPanel.Open(CodexUi.FindFont());
            panel.SkipAnimations();
            Check("the COMBOS tab (after MUSIC, before LOG) carries a NEW dot for an unseen discovery",
                  panel.TabLabel(CodexPanel.CombosTab).text == CodexPanel.CombosLabel && CodexPanel.TabHasReward(CodexPanel.CombosTab));
            panel.ShowTab(CodexPanel.CombosTab);
            panel.SkipAnimations();
            var v = panel.Combos;
            Check("it opens its own view over the grid", panel.CombosOpen && v.Active && !panel.MusicOpen && !panel.AchievementsOpen);
            Check("one row per fusion", v.RowCount == AtomFusionTable.Count);
            int nova = (int)FusionId.Nova, bloom = (int)FusionId.Bloom;
            Check("a found fusion: its name, recipe + effect, FIRED / BEST CHAIN / FOUND, coloured glyphs",
                  v.RowTitle(nova).text == "NOVA" && v.RowLine(nova).text.Contains("SHIELD + CAPACITOR") &&
                  v.RowStats(nova).text.Contains("FIRED 5") && v.RowStats(nova).text.Contains("x3") && v.RowStats(nova).text.Contains("26 OCT 2026") &&
                  !CodexUi.IsSilhouette(v.RowGlyph(nova, 0)));
            Check("an unfound one: ???, its hint, no stats, blacked-out glyphs",
                  v.RowTitle(bloom).text == CodexCombosView.LockedName && v.RowLine(bloom).text == AtomFusionTable.Get(FusionId.Bloom).hint &&
                  v.RowStats(bloom).text == "" && (CodexUi.SilhouetteMaterial == null || CodexUi.IsSilhouette(v.RowGlyph(bloom, 0))));
            Check("the triple shows three glyphs", v.RowGlyph((int)FusionId.Singularity, 2).gameObject.activeSelf);
            Check("opening the tab clears its NEW dot", !FusionDiscoveryStore.HasUnseen && !CodexPanel.TabHasReward(CodexPanel.CombosTab));
            Check("the counter reads found / total", v.StripCount.text.StartsWith("1 / " + AtomFusionTable.Count));
            panel.ShowTab(CodexPanel.MusicTab);
            panel.SkipAnimations();
            Check("another tab hides it", !panel.CombosOpen && !v.Active && panel.MusicOpen);
        }
        finally
        {
            CodexMusicPlayer.Stop();
            if (panel != null) Object.DestroyImmediate(panel.gameObject);
            Object.DestroyImmediate(es);
        }
    }
}
