using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// THE THEMED BOSS INFRASTRUCTURE (docs/world-attacks-implementation-plan.md phase 1g).
//
//   * the tables: every default table has minPhase 1, 2, 3 and unlocks 1 / 2 / 3 attacks by phase (what the thirds of the
//     fight always did); a themed table has five attacks sorted by minPhase (1, 1, 2, 3, 3) and unlocks 2 / 3 / 5;
//     the flag decides which one `attacks` is; which bosses fight themed (Frost, Ember, Verdant);
//   * every new attack: kind has an executor, parts resolve, tell >= .7 s (FR1), a tell pose 0..2 whose frames the boss
//     shows, a muzzle on an opaque pixel of its tell drawing;
//   * each new attack through BossEncounter headless (BossAttackTest patterns), the flag on in a sandbox:
//     hazards armed at the START of the tell on the boss's own muzzle (FR1: preview from the first frame, no hazard
//     live before the tell is up), caps (FR3), a safe corridor >= 1.4 u at every sampled moment of every fight (FR4),
//     the boss and its hearts untouched by its own hazards (FR9), the attack ends, the cooldown and the next attack follow,
//     a frozen world freezes it (FR8), the boss dying in the tell takes its hazards with it;
//   * the scheduler: phase 1 uses 2 attacks, phase 2 three, phase 3 rotates all five, and never one before its minPhase;
//   * the executor registry is pluggable: a stub kind registered from here fights like any other.
// The dodge bot's `themed:boss_*` rows are AttackBudgetTest.Themed (budget: the attack of the same slot x 1.15 + 2 points).
public static class BossThemedTest
{
    static int fails;
    static void Check(string what, bool ok)
    {
        Debug.Log((ok ? "[BOSSTHM] PASS  " : "[BOSSTHM] FAIL  ") + what);
        if (!ok) fails++;
    }

    public static void Run() { TestHarness.Exit(Execute()); }

    const float Dt = 1f / 60f;
    static readonly FieldInfo PlayerField = typeof(BossEncounter).GetField("player", BindingFlags.NonPublic | BindingFlags.Instance);

    public static int Execute()
    {
        fails = 0;
        using (new TestHarness.Sandbox())
        {
            var was = new Dictionary<BossDef, bool>();
            foreach (var b in BossCatalog.All) was[b] = b.themedAttacks;
            try
            {
                Tables();
                NewAttacksAreWellFormed();
                Executors();
                NewAttacksFight();
                PhaseScheduling();
                DeathTakesHazardsAway();
                FrozenWorldFreezesIt();
                APluggableKind();
            }
            finally
            {
                foreach (var kv in was) kv.Key.themedAttacks = kv.Value;
                BossEncounter.ResetRun();
                BossRails.Reset();
                AttackPools.ClearAll();
                BossAttackTest.ReleaseAtlases();
            }
        }
        Debug.Log("[BOSSTHM] failures: " + fails);
        return fails;
    }

    // ---- fixtures ----

    static void FreshScene(int world)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EliteSystem.Clear();
        AttackPools.Forget();
        AttackHazard.ForgetAll();
        AttackHazardArt.Forget();
        EnemyThreat.Reset();
        FriendlyFire.ResetCounters();
        FriendlyFire.OnSceneLoaded("gameS1");
        BossEncounter.ResetRun();
        BossRails.Reset();
        var camGo = new GameObject("Main Camera", typeof(Camera));
        camGo.tag = "MainCamera";
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        buttonClicks.playerDied = false;
        score.pauseCounter = 0;
        moveBackGround.speed = .37f;
        Time.timeScale = 1f;
        PlayerInvuln.Reset();
        PlayerPrefs.SetInt(DeveloperUnlocks.EnabledKey, 0);
        PlayerPrefs.SetInt(WorldManager.PrefsCurrentWorld, world);
    }

    static Transform ship;

    static BossEncounter StartFight(int world, int forced, float shipX)
    {
        FreshScene(world);
        ship = new GameObject("~Ship").transform;
        ship.position = new Vector3(shipX, -3.2f, 0f);
        EliteSystem.PlayerOverride = ship;
        BossEncounter.Begin(world, null);
        var e = BossEncounter.Instance;
        PlayerField.SetValue(e, ship);
        e.Step(.1f, 1f);
        for (int i = 0; i < 400 && e.State == BossEncounter.Phase.Intro; i++) e.Step(.1f, 1f);
        e.Actor.ForcedAttack = forced;
        return e;
    }

    static readonly int[] ThemedWorlds = { 0, 1, 2, 3, 4 };   // Space, Frost, Verdant, Ember, Tide

    static bool IsNew(BossDef b, BossAttack a) => System.Array.IndexOf(b.DefaultAttacks, a) < 0;

    // ---- tables ----

    static void Tables()
    {
        foreach (var b in BossCatalog.All)
        {
            var d = b.DefaultAttacks;
            bool sorted = true, ones = true;
            for (int i = 0; i < d.Length; i++) { ones &= d[i].minPhase == i + 1; if (i > 0) sorted &= d[i].minPhase >= d[i - 1].minPhase; }
            Check(b.artKey + ": the default table has minPhase 1, 2, 3", d.Length == 3 && ones && sorted);
            bool was = b.themedAttacks;
            b.themedAttacks = false;
            Check(b.artKey + ": themedAttacks off, `attacks` is the default table", ReferenceEquals(b.attacks, d));
            Check(b.artKey + ": default table unlocks 1 / 2 / 3 attacks by phase (" + BossCatalog.UnlockedAttacks(b, 0f) + "/" + BossCatalog.UnlockedAttacks(b, .4f) + "/" + BossCatalog.UnlockedAttacks(b, .9f) + ")",
                  BossCatalog.UnlockedAttacks(b, 0f) == 1 && BossCatalog.UnlockedAttacks(b, .34f) == 2 && BossCatalog.UnlockedAttacks(b, .66f) == 2 && BossCatalog.UnlockedAttacks(b, .67f) == 3 && BossCatalog.UnlockedAttacks(b, 1f) == 3);
            if (b.themed == null)
            {
                b.themedAttacks = true;
                Check(b.artKey + ": no themed table yet (needs Lash + Roll), so the flag changes nothing", ReferenceEquals(b.attacks, d));
                b.themedAttacks = was;
                continue;
            }
            b.themedAttacks = true;
            var t = b.themed;
            bool tsorted = true;
            for (int i = 1; i < t.Length; i++) tsorted &= t[i].minPhase >= t[i - 1].minPhase;
            Check(b.artKey + ": themedAttacks on, `attacks` is the themed table of five", ReferenceEquals(b.attacks, t) && t.Length == 5);
            Check(b.artKey + ": its minPhase is 1, 1, 2, 3, 3 (sorted)", tsorted && t[0].minPhase == 1 && t[1].minPhase == 1 && t[2].minPhase == 2 && t[3].minPhase == 3 && t[4].minPhase == 3);
            Check(b.artKey + ": phase 1 uses 2 attacks, phase 2 three, phase 3 all five (" + BossCatalog.UnlockedAttacks(b, 0f) + "/" + BossCatalog.UnlockedAttacks(b, .4f) + "/" + BossCatalog.UnlockedAttacks(b, .9f) + ")",
                  BossCatalog.UnlockedAttacks(b, 0f) == 2 && BossCatalog.UnlockedAttacks(b, .34f) == 3 && BossCatalog.UnlockedAttacks(b, .66f) == 3 && BossCatalog.UnlockedAttacks(b, .67f) == 5);
            int news = 0;
            foreach (var a in t) if (IsNew(b, a)) news++;
            Check(b.artKey + ": two new attacks, today's three kept in order", news == 2 && t[0] == d[0] && t[2] == d[1] && t[3] == d[2]);
            b.themedAttacks = was;
        }
        Check("BossDef.themedAttacks defaults: Frost, Verdant and Ember fight themed; Space and Tide do not",
              BossCatalog.ForWorld(1).themedAttacks && BossCatalog.ForWorld(2).themedAttacks && BossCatalog.ForWorld(3).themedAttacks &&
              !BossCatalog.ForWorld(0).themedAttacks && !BossCatalog.ForWorld(4).themedAttacks);
        var verdant = BossCatalog.ForWorld(2);
        Check("Verdant's themed table: stinger thorns, vine lash, spore bloom, acid cannons, trunk toss (Lash + Roll)",
              verdant.themed != null && verdant.themed.Length == 5 && verdant.themed[1].name == "vine lash" && verdant.themed[1].kind == BossAttackKind.Lash &&
              verdant.themed[4].name == "trunk toss" && verdant.themed[4].kind == BossAttackKind.Roll);
    }

    // ---- the new attacks, as data ----

    static void NewAttacksAreWellFormed()
    {
        foreach (int w in ThemedWorlds)
        {
            var b = BossCatalog.ForWorld(w);
            foreach (var a in b.themed)
            {
                if (!IsNew(b, a)) continue;
                string tag = b.artKey + " " + a.name + ": ";
                Check(tag + "kind " + a.kind + " has an executor", BossExecutors.For(a.kind) != null);
                bool parts = a.parts != null && a.parts.Length > 0 && a.parts.Length <= BossActor.MaxParts;
                if (a.parts != null) foreach (int p in a.parts) parts &= p >= 0;
                Check(tag + "fires from " + string.Join("+", a.emitters) + " (all parts known)", parts);
                Check(tag + "tell " + a.tellSeconds + " s is at least " + AttackHazard.MinTellSeconds + " s (FR1)", a.tellSeconds >= AttackHazard.MinTellSeconds - 1e-4f);
                Check(tag + "tell pose " + a.tell + " is one of the boss's three", a.tell >= 0 && a.tell <= 2);
                Check(tag + "cooldown " + a.cooldown + " s, not under the old attacks' 1.1 s", a.cooldown >= 1.1f);
                bool on = true;
                int frame = BossArt.TellFrame(b, a.tell, 1f);
                foreach (int p in a.parts)
                {
                    var px = BossEmitters.Pixel(b, p, frame);
                    on &= BossAttackTest.Alpha(b, BossEmitters.TableFrame(frame) == frame ? frame : BossEmitters.TableFrame(frame), px.x, px.y) >= .5f;
                    on &= px.x > 8 && px.x < 376 && px.y > 8 && px.y < 376;
                }
                Check(tag + "its muzzle is an opaque pixel of the tell drawing it ends on", on);
                if (a.kind == BossAttackKind.Strike) Check(tag + "lanes " + a.count + " spaced " + a.spacing + " u (>= " + AttackStrike.MinLaneSpacing + ")", a.count >= 2 && a.count <= 4 && a.spacing >= AttackStrike.MinLaneSpacing - 1e-4f);
                if (a.kind == BossAttackKind.Blast) Check(tag + "ring speed " + a.blast.speed + " u/s <= " + AttackBlast.MaxSpeed + " (FR3)", a.blast.speed <= AttackBlast.MaxSpeed + 1e-4f);
                if (a.kind == BossAttackKind.Wave) Check(tag + "band speed " + a.wave.speed + " u/s <= " + AttackWave.MaxSpeed + ", gap " + a.wave.gapWidth + " u >= " + AttackWave.MinGap, a.wave.speed <= AttackWave.MaxSpeed + 1e-4f && a.wave.gapWidth >= AttackWave.MinGap - 1e-4f);
                if (a.kind == BossAttackKind.Jet) Check(tag + "live " + a.jet.liveSeconds + " s <= " + AttackJet.MaxLiveSeconds + " (FR3)", a.jet.liveSeconds <= AttackJet.MaxLiveSeconds + 1e-4f);
                if (a.kind == BossAttackKind.Lash) Check(tag + "sweep " + a.lash.sweepSeconds + " s in " + AttackLash.MinSweep + ".." + AttackLash.MaxSweep + " (FR3), arc " + a.lash.arcDeg + " deg, 2 volleys fit the pool of " + AttackLash.PoolSize,
                                                        a.lash.sweepSeconds >= AttackLash.MinSweep - 1e-4f && a.lash.sweepSeconds <= AttackLash.MaxSweep + 1e-4f && a.lash.arcDeg >= AttackLash.MinArcDeg && a.lash.arcDeg <= AttackLash.MaxArcDeg && a.volleys <= AttackLash.PoolSize);
                if (a.kind == BossAttackKind.Roll) Check(tag + "roll " + a.log.speed + " u/s in " + AttackLog.MinSpeed + ".." + AttackLog.MaxSpeed + " (FR3), " + a.log.rollSeconds + " s, 2 trunks fit the pool of " + AttackLog.PoolSize,
                                                        a.log.speed >= AttackLog.MinSpeed - 1e-4f && a.log.speed <= AttackLog.MaxSpeed + 1e-4f && a.log.rollSeconds >= AttackLog.MinRoll && a.log.rollSeconds <= AttackLog.MaxRoll && a.volleys <= AttackLog.PoolSize);
            }
        }
    }

    static void Executors()
    {
        Check("Jet / Wave / Blast / Strike / Lash / Roll have executors", BossExecutors.For(BossAttackKind.Jet) != null && BossExecutors.For(BossAttackKind.Wave) != null &&
                                                            BossExecutors.For(BossAttackKind.Blast) != null && BossExecutors.For(BossAttackKind.Strike) != null &&
                                                            BossExecutors.For(BossAttackKind.Lash) != null && BossExecutors.For(BossAttackKind.Roll) != null);
        Check("Lash and Roll are appended at the end of the enum (Strike < Lash < Roll)", (int)BossAttackKind.Lash == (int)BossAttackKind.Strike + 1 && (int)BossAttackKind.Roll == (int)BossAttackKind.Lash + 1);
        Check("Aimed / Fan / Lob / Beam stay with BossActor (no executor)", BossExecutors.For(BossAttackKind.Aimed) == null && BossExecutors.For(BossAttackKind.Fan) == null &&
                                                                            BossExecutors.For(BossAttackKind.Lob) == null && BossExecutors.For(BossAttackKind.Beam) == null);
        Check("an unknown kind has no executor", BossExecutors.For((BossAttackKind)200) == null);
    }

    // ---- the fights ----

    static float Corridor(float railEdge)
    {
        // the widest free width between hazards and rails, over the rows the ship flies in (centre-run of free x + two ship radii)
        float best = 0f, r = DodgeBot.ShipRadius;
        var all = AttackHazard.All;
        for (float y = DodgeBot.MinY; y <= DodgeBot.MaxY + .01f; y += .4f)
        {
            float run = 0f, longest = 0f;
            for (float x = -railEdge + r; x <= railEdge - r + 1e-3f; x += .05f)
            {
                bool free = true;
                for (int i = 0; i < all.Count && free; i++)
                    if (all[i] != null && all[i].ZoneLive && all[i].ZoneTouches(new Vector2(x, y), r)) free = false;
                if (free) { run += .05f; if (run > longest) longest = run; }
                else run = 0f;
            }
            best = Mathf.Max(best, longest > 0f ? longest + 2f * r : 0f);
        }
        return best;
    }

    static void NewAttacksFight()
    {
        foreach (int w in ThemedWorlds)
        {
            var def = BossCatalog.ForWorld(w);
            def.themedAttacks = true;
            for (int ai = 0; ai < def.attacks.Length; ai++)
            {
                var a = def.attacks[ai];
                if (!IsNew(def, a)) continue;
                foreach (float shipX in new[] { -1.8f, .2f, 1.9f })
                    FightOne(def, ai, a, shipX);
            }
            def.themedAttacks = false;
        }
    }

    static float AimLock(AttackHazard h)
    {
        var lash = h as AttackLash; var log = h as AttackLog;
        if (lash != null) return lash.StartRad * 1000f + lash.Dir * 10f + lash.Length;
        if (log != null) return log.Landing.x * 1000f + log.Landing.y * 100f + log.RollVelocity.x * 10f + log.RollVelocity.y;
        return 0f;
    }

    static void FightOne(BossDef def, int ai, BossAttack a, float shipX)
    {
        var e = StartFight(BossCatalog.All.Length > 0 ? System.Array.IndexOf(BossCatalog.All, def) : 0, ai, shipX);
        var actor = e.Actor;
        string tag = def.artKey + " " + a.name + " (ship x " + shipX + "): ";
        int hp0 = e.HitPointsLeft, hearts0 = e.HeartsLeft;
        float t = 0f;
        for (int i = 0; i < 900 && !actor.Telegraphing; i++) { e.Step(Dt, 1f); t += Dt; }
        Check(tag + "the tell starts with the attack", actor.Telegraphing && actor.CurrentAttack == a);
        int armed = actor.ArmedHazards;
        Check(tag + "hazards are armed at the very start of the tell (" + armed + ")", armed > 0);
        bool shooter = true, tellState = true, tellLong = true, preview = true, muzzle = true;
        for (int i = 0; i < armed; i++)
        {
            var h = actor.ArmedHazard(i);
            shooter &= h.Shooter == actor.gameObject;
            tellState &= h.State == AttackHazard.Phase.Tell;
            tellLong &= h.TellSeconds >= AttackHazard.MinTellSeconds - 1e-4f;
            preview &= h.Preview != null;
            var blast = h as AttackBlast; var jet = h as AttackJet; var lashH = h as AttackLash; var logH = h as AttackLog;
            Vector2 origin = Vector2.zero; bool has = false;
            if (blast != null) { origin = blast.Origin; has = true; }
            if (jet != null) { origin = jet.Origin; has = true; }
            if (lashH != null) { origin = lashH.Root; has = true; }
            if (logH != null) { origin = logH.Origin; has = true; }
            if (has)
            {
                float best = float.MaxValue;
                foreach (int p in a.parts) { Vector2 loc; best = Mathf.Min(best, Vector2.Distance(origin, actor.TellMuzzle(a, p, out loc))); }
                muzzle &= best < .02f;
            }
        }
        Check(tag + "they are the boss's own, in their tell (FR1: >= .7 s) and draw their footprint from the first frame (FR2)", shooter && tellState && tellLong && preview);
        Check(tag + "a ring / jet / whip / trunk starts at the real muzzle of its part", muzzle);

        // the aim of a whip / a trunk is locked at the start of the tell: its heading, spot and track never change after it
        var locks = new float[armed];
        for (int i = 0; i < armed; i++) locks[i] = AimLock(actor.ArmedHazard(i));
        bool aimLocked = true;
        // run the attack out
        float tellStart = t, firstLive = -1f, lastLive = 0f, leastCorridor = 99f;
        bool earlyLive = false, caps = true, frameOk = true, previewLate = true, damaged = false;
        var poseFrames = new HashSet<int>();
        for (float p = 0f; p <= 1.001f; p += .01f) poseFrames.Add(BossArt.TellFrame(def, a.tell, p));
        float rail = BossRails.DrawnInnerEdge;
        for (int i = 0; i < 1800 && (actor.Telegraphing || actor.HazardPhase); i++)
        {
            e.Step(Dt, 1f);
            t += Dt;
            bool anyLive = false;
            for (int k = 0; k < actor.ArmedHazards; k++)
            {
                var h = actor.ArmedHazard(k);
                if (h == null) continue;
                if (k < locks.Length && h.State != AttackHazard.Phase.Off && ReferenceEquals(h.Shooter, actor.gameObject)) aimLocked &= Mathf.Abs(AimLock(h) - locks[k]) < 1e-3f;
                if (h.Live)
                {
                    anyLive = true;
                    if (firstLive < 0f) firstLive = t - tellStart;
                    if (t - tellStart < a.tellSeconds - .03f) earlyLive = true;
                    var jet = h as AttackJet; var strike = h as AttackStrike; var blast = h as AttackBlast; var wave = h as AttackWave; var lashL = h as AttackLash; var logL = h as AttackLog;
                    if (lashL != null) caps &= lashL.SweepSeconds <= AttackLash.MaxSweep + 1e-3f && lashL.SweepSeconds >= AttackLash.MinSweep - 1e-3f;
                    if (logL != null) caps &= logL.RollVelocity.magnitude <= AttackLog.MaxSpeed + 1e-3f;
                    if (jet != null) caps &= jet.LiveSeconds <= AttackJet.MaxLiveSeconds + 1e-3f;
                    if (strike != null) caps &= strike.LiveSeconds <= AttackStrike.MaxLiveSeconds + 1e-3f;
                    if (blast != null) caps &= blast.RadialSpeed <= AttackBlast.MaxSpeed + 1e-3f;
                    if (wave != null) caps &= wave.FallSpeed <= AttackWave.MaxSpeed + 1e-3f;
                }
                else if (h.State == AttackHazard.Phase.Tell && h.TellLeft > .3f && h.TellLeft < .45f) previewLate &= h.Preview != null;
            }
            if (anyLive)
            {
                lastLive = t - tellStart;
                leastCorridor = Mathf.Min(leastCorridor, Corridor(rail));
            }
            if (actor.Telegraphing) frameOk &= poseFrames.Contains(actor.BodyFrame) || actor.Flashing;
            if (e.HitPointsLeft != hp0 || e.HeartsLeft != hearts0) damaged = true;
        }
        Check(tag + "no hazard goes live before the tell is up (first live " + firstLive.ToString("0.00") + " s, tell " + a.tellSeconds + " s)", !earlyLive && firstLive >= a.tellSeconds - .05f);
        Check(tag + "the footprint preview is still drawn in the last .4 s of the tell", previewLate);
        Check(tag + "FR3 caps hold (live time / speeds)", caps);
        if (a.kind == BossAttackKind.Lash || a.kind == BossAttackKind.Roll) Check(tag + "the aim is locked at the start of the tell (the whip's start line and turn, the trunk's spot and heading never move)", aimLocked);
        Check(tag + "FR4: a free corridor >= 1.4 u at every sampled moment of the live hazards (least " + leastCorridor.ToString("0.00") + " u)", leastCorridor >= 1.4f);
        Check(tag + "the boss shows its tell pose " + a.tell + " through the tell", frameOk);
        Check(tag + "its own hazards never hurt the boss or its hearts (FR9)", !damaged);
        Check(tag + "the attack ends (" + lastLive.ToString("0.0") + " s after the tell began), nothing left burning", !actor.HazardPhase && AttackHazard.ActiveCount == 0);
        int started = actor.AttacksStarted;
        for (int i = 0; i < 900 && actor.AttacksStarted == started; i++) e.Step(Dt, 1f);
        Check(tag + "the cooldown ends and the next attack begins", actor.AttacksStarted == started + 1);
        BossEncounter.ResetRun();
    }

    // ---- scheduling ----

    // Drives the actor at a fixed point of the fight (the clock never runs out) and lists what it starts.
    static List<string> Started(int world, float progress, int steps, out int distinct)
    {
        var e = StartFight(world, -1, .5f);
        var actor = e.Actor;
        var names = new List<string>();
        var seen = new HashSet<string>();
        BossAttack last = null;
        int starts = 0;
        Vector3 pos = new Vector3(.5f, -3.2f, 0f);
        for (int i = 0; i < steps; i++)
        {
            actor.StepFight(Dt, Dt, pos, progress, e.Pool);
            e.Pool.Step(Dt);
            AttackPools.StepAll(Dt);
            if (actor.AttacksStarted != starts)
            {
                starts = actor.AttacksStarted;
                last = actor.CurrentAttack;
                names.Add(last.name);
                seen.Add(last.name);
            }
        }
        distinct = seen.Count;
        BossEncounter.ResetRun();
        return names;
    }

    static void PhaseScheduling()
    {
        foreach (int w in ThemedWorlds)
        {
            var def = BossCatalog.ForWorld(w);
            def.themedAttacks = true;
            int n1, n2, n3;
            var p1 = Started(w, .1f, 60 * 60, out n1);
            var p2 = Started(w, .5f, 60 * 60, out n2);
            var p3 = Started(w, .9f, 60 * 80, out n3);
            Check(def.artKey + ": phase 1 rotates exactly its 2 attacks (" + string.Join(", ", new HashSet<string>(p1)) + ")", n1 == 2);
            Check(def.artKey + ": phase 2 rotates 3 (" + string.Join(", ", new HashSet<string>(p2)) + ")", n2 == 3);
            Check(def.artKey + ": phase 3 rotates all five (" + string.Join(", ", new HashSet<string>(p3)) + ")", n3 == 5);
            bool ordered = true;
            for (int i = 1; i < p3.Count; i++)
            {
                int ia = System.Array.FindIndex(def.attacks, x => x.name == p3[i - 1]), ib = System.Array.FindIndex(def.attacks, x => x.name == p3[i]);
                ordered &= ib == (ia + 1) % 5;
            }
            Check(def.artKey + ": the rotation is in table order", ordered);
            def.themedAttacks = false;
            int d1;
            var off = Started(w, .9f, 60 * 30, out d1);
            Check(def.artKey + ": with the flag off phase 3 is today's three attacks (" + d1 + ")", d1 == 3);
        }
    }

    // ---- the boss dies / the world freezes in a tell ----

    static void DeathTakesHazardsAway()
    {
        foreach (int w in new[] { 1, 2, 3 })
        {
            var def = BossCatalog.ForWorld(w);
            def.themedAttacks = true;
            int ai = System.Array.FindIndex(def.attacks, a => IsNew(def, a));
            var e = StartFight(w, ai, .5f);
            for (int i = 0; i < 900 && !e.Actor.Telegraphing; i++) e.Step(Dt, 1f);
            for (int i = 0; i < 20; i++) e.Step(Dt, 1f);
            bool armed = AttackHazard.ActiveCount > 0;
            e.OnShipAttackHit(100f);
            e.Step(Dt, 1f);
            e.Step(Dt, 1f);
            Check(def.artKey + ": the boss is destroyed in its tell (" + e.State + ") and takes its " + (armed ? "armed " : "") + "hazards with it", armed && e.State == BossEncounter.Phase.Outro && AttackHazard.ActiveCount == 0);
            def.themedAttacks = false;
            BossEncounter.ResetRun();
        }
    }

    static void FrozenWorldFreezesIt()
    {
        var def = BossCatalog.ForWorld(1);
        def.themedAttacks = true;
        int ai = System.Array.FindIndex(def.attacks, a => a.kind == BossAttackKind.Blast);
        var e = StartFight(1, ai, .5f);
        for (int i = 0; i < 900 && !e.Actor.Telegraphing; i++) e.Step(Dt, 1f);
        for (int i = 0; i < 30; i++) e.Step(Dt, 1f);
        var h = e.Actor.ArmedHazard(0);
        float before = h.PhaseTime; var pos = e.Actor.transform.position;
        for (int i = 0; i < 120; i++) e.Step(Dt, 0f);
        Check("a frozen world (timeScale 0) freezes the boss's hazard and its tell (" + before.ToString("0.000") + " -> " + h.PhaseTime.ToString("0.000") + ")",
              Mathf.Abs(h.PhaseTime - before) < 1e-4f && e.Actor.Telegraphing && e.Actor.transform.position == pos && h.State == AttackHazard.Phase.Tell);
        for (int i = 0; i < 400 && e.Actor.Telegraphing; i++) e.Step(Dt, 1f);
        Check("... and it goes on live when the world runs again", h.State != AttackHazard.Phase.Tell);
        def.themedAttacks = false;
        BossEncounter.ResetRun();
    }

    // ---- a kind registered from outside fights like any other ----

    sealed class StubExecutor : IBossAttackExecutor
    {
        public int arms;
        public int Arm(BossActor boss, BossAttack attack, Vector3 player, AttackHazard[] into)
        {
            arms++;
            var strike = AttackStrike.Arm(StrikeSpec.Standard(boss.World), player.x, player.y, attack.tellSeconds, boss.gameObject);
            if (strike == null) return 0;
            into[0] = strike;
            return 1;
        }
    }

    static void APluggableKind()
    {
        var kind = (BossAttackKind)90;
        var stub = new StubExecutor();
        BossExecutors.Register(kind, stub);
        var def = BossCatalog.ForWorld(1);
        var saved = def.themed;
        var dummy = new BossAttack { name = "stub", kind = kind, tell = 0, tellSeconds = .8f, emitters = new[] { "Jaw" }, cooldown = 1.1f, minPhase = 1 };
        dummy.parts = BossEmitters.Resolve(def, dummy.emitters);
        def.themed = new[] { dummy };
        def.themedAttacks = true;
        try
        {
            var e = StartFight(1, -1, .5f);
            for (int i = 0; i < 900 && stub.arms == 0; i++) e.Step(Dt, 1f);
            int started = e.Actor.AttacksStarted;
            for (int i = 0; i < 900 && !(e.Actor.AttacksStarted > started); i++) e.Step(Dt, 1f);
            Check("a kind registered with BossExecutors.Register fights (armed " + stub.arms + "x, the next attack " + e.Actor.AttacksStarted + ")", stub.arms >= 1 && e.Actor.AttacksStarted > started);
        }
        finally
        {
            def.themed = saved;
            def.themedAttacks = false;
            BossExecutors.Register(kind, null);
            BossEncounter.ResetRun();
        }
    }
}
