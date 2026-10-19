using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

// THE SIGNATURE SPECIALS of the musical boss fight (BossSpecials; docs/boss-music-director.md):
//   1 tables     every boss's rotation has at least one special (a non-Pattern slot: a laser or an executor's hazard), all of
//                them unlocked by the last third; the Bloom Queen's and the Iron Kraken's first one from phase 1
//   2 scheduler  on a manual clock: the first landing is the first bar line a cadence after the director's start, the tell
//                starts tellSeconds before it (a late frame shortens it, never past an audio-clock step), the volley gap snaps
//                to whole beats, a tell start the game slept through moves to a later bar line, nothing lands before its
//                unlock (then the first bar line with room), the rotation cycles the unlocked specials, the hold runs from
//                HoldBeats before the landing until the special is over
//   3 director   held: no wave leaves at or after the hold beat and none is told, no accent is planned into it, and the
//                waves resume on the next rhythm sixteenth once the hold lifts
//   4 live       the real BossActor, projectile pool and hazard pools on a manual song, every boss: the special's hazards /
//                lasers go live within a frame of a bar line, no wave reaches the ship's row after it lands and none leaves
//                while it is live, the waves resume after it, the boss holds the special's tell drawings with its parts
//                charging, the next special lands a cadence later
//   5 pause      the song runs on through a freeze: in the hold, the special moves to a later bar line (never the beat the
//                game slept through); in the tell, the armed special plays out in game time and the waves stay held
//   6 fairness   the dodge bot through the real BossEncounter on the real song clock: hits a minute with the waves alone,
//                with specials every 8 bars and every 4 (the measurements behind BossSpecials.Bars)
public static class BossSpecialsTest
{
    static int fails;
    static readonly string[] Names = { "Space", "Frost", "Verdant", "Ember", "Tide", "Storm", "Desert" };
    static void Check(string what, bool ok) { Debug.Log("[SPECIALS] " + (ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
    public static void Run() { TestHarness.Exit(Execute()); }

    public static int Execute()
    {
        fails = 0;
        var oldNow = BeatClock.Now;
        bool wasEnabled = BossSpecials.Enabled;
        var savedBars = (int[])BossSpecials.Bars.Clone();
        try
        {
            BossSpecials.Enabled = true;
            Tables();
            Scheduler();
            Director();
            foreach (int w in new[] { 0, 1, 2, 3, 4, 5, 6 }) Live(w);
            Pause();
            Fairness();
        }
        finally
        {
            BeatClock.Now = oldNow;
            BossSpecials.Enabled = wasEnabled;
            for (int i = 0; i < savedBars.Length; i++) BossSpecials.Bars[i] = savedBars[i];
        }
        Debug.Log("[SPECIALS] failures: " + fails);
        return fails;
    }

    sealed class Manual : IMusicBeatSource
    {
        public bool Active { get; set; } = true;
        public double BeatNow { get; set; }
        public double SecondsPerBeat => .4;
        public int BeatsPerBar => 4;
        public int DownbeatBeat => 0;
        public MusicMoment Moment = new MusicMoment { energy = .7f, bass = .7f, accent = .7f, kind = "drive" };
        public MusicMoment MusicNow => Moment;
        public MusicMoment MusicAhead(double beats) => Moment;
    }

    // ---- 1 ----
    static void Tables()
    {
        for (int w = 0; w < 7; w++)
        {
            var boss = BossCatalog.ForWorld(w);
            var sp = new BossSpecials(boss, w);
            var names = new List<string>();
            bool kinds = true;
            foreach (var a in boss.attacks)
                if (BossSpecials.IsSpecial(a)) { names.Add(a.name + " (phase " + a.minPhase + ")"); kinds &= a.kind == BossAttackKind.Beam || BossExecutors.IsHazardKind(a.kind); }
            Check(Names[w] + ": " + sp.Count + " special(s) in its rotation, each a laser or an executor's hazard: " + string.Join(", ", names), sp.Count >= 1 && kinds);
            int all = BossCatalog.UnlockedAttacks(boss, 1f);
            bool unlocked = true;
            for (int i = 0; i < boss.attacks.Length; i++) if (BossSpecials.IsSpecial(boss.attacks[i]) && i >= all) unlocked = false;
            Check(Names[w] + ": every special is in rotation by the last third", unlocked);
        }
        bool early = true;
        foreach (int w in new[] { 2, 4 })
        {
            var boss = BossCatalog.ForWorld(w);
            int n = BossCatalog.UnlockedAttacks(boss, 0f);
            bool one = false;
            for (int i = 0; i < n; i++) one |= BossSpecials.IsSpecial(boss.attacks[i]);
            early &= one;
        }
        Check("the Bloom Queen (vine lash) and the Iron Kraken (beak jet) have a special from phase 1", early);
    }

    // ---- 2 ----
    static void Scheduler()
    {
        const float dt = 1f / 60f;
        var boss = BossCatalog.ForWorld(2);   // Bloom Queen: vine lash (phase 1), trunk toss (phase 3)
        var m = new Manual();
        var s = new BossSpecials(boss, 2);
        s.Begin(m, 2.0);
        int bars = BossSpecials.Bars[2];
        Check("first landing: the first bar line " + bars + " bars after the director's start (beat " + s.LandBeat + ")", s.LandBeat == BossSpecials.BarLineAtOrAfter(m, 2.0 + bars * 4) && s.LandBeat % 4 == 0 && s.LandBeat >= 2 + bars * 4);
        Check("nothing is held while nothing is scheduled yet (no Due call)", double.IsNaN(s.HoldFromBeat));
        double land = s.LandBeat;
        BossAttack first = null; double at = -1;
        bool holdOk = true;
        for (int f = 0; f < 60 * 30 && first == null; f++)
        {
            m.BeatNow = f * dt / .4;
            first = s.Due(m, dt, .8f);
            if (first != null) at = m.BeatNow;
            else holdOk &= Math.Abs(s.HoldFromBeat - (land - BossSpecials.HoldBeats)) < 1e-9;
        }
        Check("the hold runs from HoldBeats before the landing from the first Due on", holdOk);
        Check("the first special is the vine lash", first != null && first.name == "vine lash" && first.kind == BossAttackKind.Lash);
        if (first == null) return;
        double tellBeats = 1.0 / .4;
        Check("its tell starts the first frame at or after landing - tell (" + at.ToString("F3") + " vs " + (land - tellBeats).ToString("F3") + ")", at >= land - tellBeats - 1e-9 && at < land - tellBeats + dt / .4 + 1e-9);
        Check("its tell is timed to land on the bar line (" + first.tellSeconds.ToString("F4") + " s for " + ((land - at) * .4).ToString("F4") + ")", Math.Abs(first.tellSeconds - (land - at) * .4) < 1e-5);
        Check("the landing is remembered and the next slot is a cadence on", s.LastLanding == land && s.LandBeat == land + bars * 4 && s.Running);
        Check("held through the special (from " + s.HoldFromBeat + ")", Math.Abs(s.HoldFromBeat - (land - BossSpecials.HoldBeats)) < 1e-9);
        Check("no second special while one runs", s.Due(m, dt, .8f) == null);
        s.Done();
        s.Due(m, dt, .8f);
        Check("after it: the hold moves to the next landing", !s.Running && Math.Abs(s.HoldFromBeat - (land + bars * 4 - BossSpecials.HoldBeats)) < 1e-9);
        // the rotation: trunk toss next (phase 3 unlocked), its two trunks a whole number of beats apart
        BossAttack second = null;
        for (int f = 0; f < 60 * 40 && second == null; f++) { m.BeatNow += dt / .4; second = s.Due(m, dt, .8f); }
        Check("the rotation gives the trunk toss next, its volleys snapped to whole beats (gap " + (second != null ? second.volleyGap.ToString("F3") : "-") + ")",
              second != null && second.name == "trunk toss" && Math.Abs(second.volleyGap / .4 - Math.Round(second.volleyGap / .4)) < 1e-6 && second.volleyGap >= .4f - 1e-6f);
        Check("it lands on the next cadence bar line", second != null && s.LastLanding == land + bars * 4);
        s.Done();
        BossAttack third = null;
        for (int f = 0; f < 60 * 40 && third == null; f++) { m.BeatNow += dt / .4; third = s.Due(m, dt, .8f); }
        Check("then the vine lash again", third != null && third.name == "vine lash");
        s.Done();

        // phase 1: only the lash, every time
        var p1 = new BossSpecials(boss, 2);
        var m1 = new Manual();
        p1.Begin(m1, 2.0);
        bool onlyLash = true; int fired = 0;
        for (int f = 0; f < 60 * 80; f++)
        {
            m1.BeatNow = f * dt / .4;
            var a = p1.Due(m1, dt, 0f);
            if (a != null) { fired++; onlyLash &= a.name == "vine lash"; p1.Done(); }
        }
        Check("phase 1 fights the vine lash alone (" + fired + " specials in 80 s)", fired >= 4 && onlyLash);

        // a late frame (an audio-clock step): the tell is shortened, the landing kept; a sleep: moved to a later bar
        var l = new BossSpecials(boss, 2);
        var ml = new Manual();
        l.Begin(ml, 2.0);
        double land2 = l.LandBeat;
        ml.BeatNow = land2 - tellBeats - .1;
        Check("just before the tell start: nothing", l.Due(ml, dt, .8f) == null);
        ml.BeatNow = land2 - tellBeats + .08 / .4;   // seen 80 ms late (an audio buffer)
        var lateA = l.Due(ml, .08f, .8f);
        Check("a tell start seen 80 ms late still lands on its bar line with a tell 80 ms shorter (" + (lateA != null ? lateA.tellSeconds.ToString("F3") : "-") + ")", lateA != null && Math.Abs(lateA.tellSeconds - .92f) < 1e-4 && l.LastLanding == land2 && l.Skipped == 0);
        l.Done();
        double land3 = l.LandBeat;
        ml.BeatNow = land3 - tellBeats - .1;
        l.Due(ml, dt, .8f);
        ml.BeatNow = land3 - tellBeats + 1.2;   // the game slept half a second
        var slept = l.Due(ml, dt, .8f);
        Check("a tell start the game slept through is not launched; the special moves to the next bar line with room (" + land3 + " -> " + l.LandBeat + ")",
              slept == null && l.Skipped == 1 && l.LandBeat > land3 && l.LandBeat % 4 == 0 && l.LandBeat - tellBeats >= ml.BeatNow);
        BossAttack moved = null;
        for (int f = 0; f < 60 * 10 && moved == null; f++) { ml.BeatNow += dt / .4; moved = l.Due(ml, dt, .8f); }
        Check("... and lands there", moved != null && l.LastLanding == l.LandBeat - bars * 4 && Math.Abs(moved.tellSeconds - (l.LastLanding - ml.BeatNow) * .4) < 1e-5);

        // the Void Archon's pod lasers come in the last third: nothing before, the first bar line with room after
        var sp = new BossSpecials(BossCatalog.ForWorld(0), 0);
        var ms = new Manual();
        sp.Begin(ms, 2.0);
        bool none = true, noHold = true;
        for (int f = 0; f < 60 * 40; f++)
        {
            ms.BeatNow = f * dt / .4;
            none &= sp.Due(ms, dt, .2f) == null;
            noHold &= double.IsNaN(sp.HoldFromBeat);
        }
        Check("Space: no special and no hold while its lasers are locked (40 s at phase 1)", none && noHold && sp.LandBeat >= ms.BeatNow + BossSpecials.HoldBeats);
        double unlockAt = ms.BeatNow;
        BossAttack laser = null;
        for (int f = 0; f < 60 * 20 && laser == null; f++) { ms.BeatNow += dt / .4; laser = sp.Due(ms, dt, .8f); }
        Check("Space: after the unlock the pod lasers land on the first bar line with room for the hold and the tell (beat " + sp.LastLanding + ", unlocked at " + unlockAt.ToString("F1") + ")",
              laser != null && laser.name == "pod lasers" && sp.LastLanding % 4 == 0 && sp.LastLanding >= unlockAt + BossSpecials.HoldBeats && sp.LastLanding < unlockAt + BossSpecials.HoldBeats + 4);

        // switched off: nothing
        BossSpecials.Enabled = false;
        var off = new BossSpecials(boss, 2);
        off.Begin(m, 2.0);
        bool quiet = true;
        for (int f = 0; f < 60 * 40; f++) { m.BeatNow = f * dt / .4; quiet &= off.Due(m, dt, .8f) == null && double.IsNaN(off.HoldFromBeat); }
        BossSpecials.Enabled = true;
        Check("switched off: no special, no hold", quiet);
    }

    // ---- 3 ----
    static void Director()
    {
        foreach (int world in new[] { 4, 3 })   // the Kraken's curtains; the Drake with its bomb accents
        {
            var sim = new PatternSim { cap = BossConfig.ProjectilePoolMax };
            var music = new Manual();
            var d = new BossMusicDirector(world, BossCatalog.ForWorld(world), sim);
            const float dt = 1f / 60f;
            bool offGrid = false, toldIntoHold = false, firedInHold = false;
            int firedBefore = 0, resumedAt = -1, wavesBefore = 0;
            for (int f = 0; f < 60 * 20; f++)
            {
                double t = f * (double)dt;
                music.BeatNow = t / .4;
                d.HoldFromBeat = music.BeatNow < 30 ? 20 : double.NaN;   // held from beat 20, lifted at 30
                sim.Advance(dt);
                int was = sim.fired;
                d.Step(music, dt, dt, .8f);
                if (music.BeatNow < 20) { firedBefore = sim.fired; wavesBefore = d.Waves.Waves; }
                if (sim.fired != was && d.LastScheduledBeat >= 20 - 1e-9 && music.BeatNow < 30) firedInHold = true;
                if (d.Telling && !double.IsNaN(d.NextWaveBeat) && d.NextWaveBeat >= 20 - 1e-9 && music.BeatNow < 30) toldIntoHold = true;
                if (music.BeatNow >= 30 && sim.fired != was && resumedAt < 0) resumedAt = f;
                if (sim.fired != was && Math.Abs(d.LastScheduledBeat * 4 - Math.Round(d.LastScheduledBeat * 4)) > 1e-6) offGrid = true;
            }
            string n = Names[world];
            Check(n + ": waves before the hold (" + wavesBefore + "), none from the hold beat on (" + d.HeldWaves + " kept in), none told into it", wavesBefore > 10 && !firedInHold && d.HeldWaves > 0 && !toldIntoHold);
            Check(n + ": the waves resume within a bar of the hold lifting (" + (resumedAt >= 0 ? (resumedAt * dt - 12).ToString("F2") : "never") + " s after)", resumedAt >= 0 && resumedAt * dt - 12 <= 1.6 + dt && !offGrid);
        }
    }

    // ---- 4 ----
    static readonly FieldInfo ChargesField = typeof(BossActor).GetField("charges", BindingFlags.NonPublic | BindingFlags.Instance);

    static void FreshScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EliteSystem.Clear();
        AttackPools.Forget();
        AttackHazard.ForgetAll();
        AttackHazardArt.Forget();
        BossRails.Reset();
    }

    static bool IsLive(AttackHazard h, GameObject by) => h != null && h.Live && ReferenceEquals(h.Shooter, by);

    static void Live(int world)
    {
        using var sandbox = new TestHarness.Sandbox();
        FreshScene();
        var boss = BossCatalog.ForWorld(world);
        var actor = BossActor.Spawn(boss);
        var pool = new BossProjectilePool(BossConfig.ProjectilePoolMax, BossConfig.BeamPoolMax);
        var music = new Manual();
        string n = Names[world];
        int fps = 60;
        float dt = 1f / fps;
        var player = new Vector3(0f, -3.2f, 0f);
        int launches = 0, wrongKind = 0, offBar = 0, tellOff = 0, armedNone = 0, liveOff = 0, neverLive = 0, poseWrong = 0, chargeOff = 0, wavesDuring = 0, resumeLate = 0, offCadence = 0;
        float worstArrival = float.NegativeInfinity;
        double lastLanding = double.NaN, cadenceBeats = 0;
        string detail = null;
        try
        {
            BossExecutors.WarmPools(boss);
            actor.BeginFight();
            actor.BeatOverride = music;
            bool running = false, wentLive = false;
            double landing = double.NaN;
            float gameT = 0f, landingGame = 0f, lastWaveArrival = float.NegativeInfinity, endedAt = -1f;
            int waves = 0, wavesAtLaunch = 0;
            BossAttack special = null;
            var charges = (SpriteRenderer[])ChargesField.GetValue(actor);
            for (int f = 0; f < fps * 44; f++)
            {
                music.BeatNow = f * (double)dt / .4;
                actor.StepFight(dt, dt, player, .8f, pool);
                gameT += dt;
                var d = actor.MusicDirector;
                if (d.Waves.Waves != waves && d.Waves.LastBullets > 0) { waves = d.Waves.Waves; lastWaveArrival = d.Waves.LastArrival; if (running) wavesDuring++; }
                if (actor.SpecialActive && !running)
                {
                    // the launch frame
                    running = true; wentLive = false; launches++;
                    special = actor.CurrentAttack;
                    landing = actor.Specials.LastLanding;
                    landingGame = gameT - dt + special.tellSeconds;   // (the tell began on this frame's step)
                    wavesAtLaunch = waves;
                    if (special == null || special.kind == BossAttackKind.Pattern) wrongKind++;
                    if (Math.Abs(((landing - music.DownbeatBeat) % 4 + 4) % 4) > 1e-9) offBar++;
                    double want = (landing - music.BeatNow) * .4;
                    if (special != null && Math.Abs(special.tellSeconds - want) > dt + 1e-4) { tellOff++; detail ??= n + " " + special.name + ": tell " + special.tellSeconds.ToString("F3") + " s for a landing " + want.ToString("F3") + " s away"; }
                    bool armed = special != null && (special.kind == BossAttackKind.Beam ? pool.ActiveBeams > 0 : actor.ArmedHazards > 0);
                    if (!armed) armedNone++;
                    // the last wave before it reaches the ship's row no later than the landing
                    worstArrival = Mathf.Max(worstArrival, lastWaveArrival - landingGame);
                    if (!double.IsNaN(lastLanding))
                    {
                        cadenceBeats = landing - lastLanding;
                        if (Math.Abs(cadenceBeats - BossSpecials.Bars[world] * 4) > 1e-9) offCadence++;
                    }
                    lastLanding = landing;
                }
                if (running && !actor.SpecialActive)
                {
                    // the frame it ended on: the boss is the song's again (its drawing too)
                    running = false;
                    if (!wentLive) neverLive++;
                    endedAt = gameT;
                }
                else if (running)
                {
                    // the drawing: the special's tell drawings while it is told / fired (or the hit flash), its parts charging in the tell
                    int frame = actor.BodyFrame;
                    bool pose = frame == BossArt.Hit || frame == BossArt.FireFrame(boss, special.tell);
                    for (int k = 0; k < 3; k++) pose |= frame == BossArt.TellFrame(boss, special.tell, k * .45f);
                    if (!pose) poseWrong++;
                    if (actor.Telegraphing && special.parts != null && f > 0)
                    {
                        int np = Mathf.Min(special.parts.Length, BossActor.MaxParts);
                        for (int i = 0; i < np; i++) if (special.parts[i] >= 0 && !charges[i].enabled) chargeOff++;
                    }
                    // the moment it goes live: a bar line, within a frame
                    bool live = false;
                    if (special.kind == BossAttackKind.Beam) { foreach (var b in pool.Beams) if (b != null && b.Live) live = true; }
                    else foreach (var h in AttackHazard.All) if (IsLive(h, actor.gameObject)) live = true;
                    if (live && !wentLive)
                    {
                        wentLive = true;
                        double late = music.BeatNow - landing;
                        if (late < -1e-9 || late > dt / .4 + 1e-6) { liveOff++; detail ??= n + " " + special.name + ": live " + (late * .4 * 1000).ToString("F0") + " ms from the bar line"; }
                    }
                }
                else if (endedAt >= 0f && waves > wavesAtLaunch) { if (gameT - endedAt > 3.2f + dt) resumeLate++; endedAt = -1f; }
                pool.PlayerPos = player;
                pool.Step(dt);
                AttackPools.StepAll(dt);
            }
            var sp = actor.Specials;
            Check(n + ": " + launches + " specials in 44 s (" + sp.Fired + " fired, " + sp.Skipped + " moved), " + cadenceBeats / 4 + " bars apart, every one a laser or a hazard (" + wrongKind + " wrong)",
                  launches >= 2 && sp.Fired == launches && sp.Skipped == 0 && offCadence == 0 && wrongKind == 0);
            Check(n + ": each lands on a bar line, its tell timed to it (" + offBar + " off a bar, " + tellOff + " mistimed), its lasers / hazards armed as the tell starts (" + armedNone + " unarmed)", offBar == 0 && tellOff == 0 && armedNone == 0);
            Check(n + ": each goes live within a frame of its bar line (" + liveOff + " off, " + neverLive + " never)" + (detail != null ? " -- " + detail : ""), liveOff == 0 && neverLive == 0);
            Check(n + ": no wave leaves while a special runs (" + wavesDuring + "), the last one before it reached the ship's row " + (-worstArrival).ToString("F2") + " s before the landing, the waves resume within two bars after (" + resumeLate + " late)",
                  wavesDuring == 0 && worstArrival <= 1e-3f && resumeLate == 0 && actor.MusicDirector.HeldWaves > 0);
            Check(n + ": the boss holds the special's tell drawings (" + poseWrong + " frames wrong) with its parts charging (" + chargeOff + " dark)", poseWrong == 0 && chargeOff == 0);
        }
        finally
        {
            AttackPools.ClearAll();
            pool.Dispose();
            UnityEngine.Object.DestroyImmediate(actor.gameObject);
            FreshScene();
        }
    }

    // ---- 5 ----
    static void Pause()
    {
        using var sandbox = new TestHarness.Sandbox();
        FreshScene();
        int world = 2;   // the Bloom Queen's vine lash: a hazard with its own game-time tell
        var boss = BossCatalog.ForWorld(world);
        var actor = BossActor.Spawn(boss);
        var pool = new BossProjectilePool(BossConfig.ProjectilePoolMax, BossConfig.BeamPoolMax);
        var music = new Manual();
        const float dt = 1f / 60f;
        var player = new Vector3(0f, -3.2f, 0f);
        try
        {
            BossExecutors.WarmPools(boss);
            actor.BeginFight();
            actor.BeatOverride = music;
            double t = 0;
            Action step = () => { music.BeatNow = t / .4; actor.StepFight(dt, dt, player, .8f, pool); pool.PlayerPos = player; pool.Step(dt); AttackPools.StepAll(dt); t += dt; };
            step();
            double land = actor.Specials.LandBeat;
            // into the hold, before the tell starts
            while (music.BeatNow < land - BossSpecials.HoldBeats + .5) step();
            Check("in the hold before the tell: no special yet, waves held", !actor.SpecialActive && actor.Specials.Fired == 0 && actor.MusicDirector.HeldAt(music.BeatNow + 1));
            // the player pauses: the song runs on 9.37 beats (past the old landing), the game sees none of it
            t += 9.37 * .4;
            double resumeBeat = t / .4;
            int fired = actor.Specials.Fired;
            for (int f = 0; f < 60 * 12 && actor.Specials.Fired == fired; f++) step();
            var sp = actor.Specials;
            Check("after the freeze the special is not launched on the slept-through beat: it moved to a later bar line (" + land + " -> " + sp.LastLanding + ", " + sp.Skipped + " moved) and launched there",
                  sp.Fired == fired + 1 && sp.Skipped == 1 && sp.LastLanding > land && sp.LastLanding - 1.0 / .4 >= resumeBeat && sp.LastLanding % 4 == 0 && actor.SpecialActive);
            Check("its tell is timed to the new bar line", Math.Abs(actor.CurrentAttack.tellSeconds - (sp.LastLanding - music.BeatNow) * .4) <= 1e-4);
            // a pause in the tell itself: the armed whip froze with the ship; it ignites on its own (game-time) tell, late for the song, and the waves stay held
            while (actor.Telegraphing && actor.CurrentAttack.tellSeconds > .4f && music.BeatNow < sp.LastLanding - 1.0) step();
            t += 5 * .4;   // two seconds of song with no frames
            int waves = actor.MusicDirector.Waves.Waves;
            bool live = false;
            for (int f = 0; f < 60 * 4 && !live; f++) { step(); foreach (var h in AttackHazard.All) live |= IsLive(h, actor.gameObject); }
            Check("a special in its tell when the game freezes still ignites afterwards (in game time)", live && actor.SpecialActive);
            Check("the waves stayed held through it", actor.MusicDirector.Waves.Waves == waves);
            for (int f = 0; f < 60 * 12 && actor.SpecialActive; f++) step();
            int after = actor.MusicDirector.Waves.Waves;
            for (int f = 0; f < 60 * 4 && actor.MusicDirector.Waves.Waves == after; f++) step();
            Check("... and resume once it is over", !actor.SpecialActive && actor.MusicDirector.Waves.Waves > after);
        }
        finally
        {
            AttackPools.ClearAll();
            pool.Dispose();
            UnityEngine.Object.DestroyImmediate(actor.gameObject);
            FreshScene();
        }
    }

    // ---- 6 ----
    // The dodge bot (DodgeBot) flies the real BossEncounter on the real song clock for `seconds`, `rolls` times, with the
    // waves alone and with the specials at 8 and 4 bars: the hits a minute it takes, and how many specials it met.
    static void Fairness()
    {
        bool counted = DodgeBot.CountHits;
        DodgeBot.CountHits = true;
        int rolls = TestHarness.FullLane ? 12 : 4;
        float seconds = TestHarness.FullLane ? 36f : 30f;
        var saved = (int[])BossSpecials.Bars.Clone();
        try
        {
            using var sandbox = new TestHarness.Sandbox();
            for (int w = 0; w < 7; w++)
            {
                float[] rate = new float[3]; int[] specials = new int[3]; float[] waves = new float[3];
                for (int c = 0; c < 3; c++)
                {
                    BossSpecials.Enabled = c > 0;
                    BossSpecials.Bars[w] = c == 2 ? 4 : 8;
                    var sc = new AttackBudgetScenarios.Rotation(w, seconds) { Music = BeatMaps.For(Names[w]) };
                    var r = DodgeBot.Run(sc, rolls, 4242u + (uint)w);
                    rate[c] = r.hitEvents / Mathf.Max(1f, r.seconds) * 60f;
                    specials[c] = sc.SpecialsFired;
                    waves[c] = sc.WavesFired / Mathf.Max(1f, r.seconds) * 60f;
                }
                BossSpecials.Enabled = true;
                BossSpecials.Bars[w] = saved[w];
                int chosen = saved[w] == 4 ? 2 : 1;
                Debug.Log("[SPECIALS] " + Names[w] + " dodge bot (" + rolls + " x " + seconds + " s): waves alone " + rate[0].ToString("F1") + " hits/min (" + waves[0].ToString("F0") + " waves/min); specials every 8 bars " + rate[1].ToString("F1") + " hits/min (" + specials[1] + " specials, " + waves[1].ToString("F0") + " waves/min); every 4 bars " + rate[2].ToString("F1") + " hits/min (" + specials[2] + " specials, " + waves[2].ToString("F0") + " waves/min)");
                Check(Names[w] + ": the specials fired in the bot's fights (" + specials[chosen] + " at " + saved[w] + " bars)", specials[chosen] >= rolls);
                Check(Names[w] + ": with its specials the bot takes " + rate[chosen].ToString("F1") + " hits a minute: beatable (under 14) and no more than 1.6x the waves alone + 2 (" + rate[0].ToString("F1") + ")", rate[chosen] < 14f && rate[chosen] <= rate[0] * 1.6f + 2f);
            }
        }
        finally
        {
            DodgeBot.CountHits = counted;
            BossSpecials.Enabled = true;
            for (int i = 0; i < saved.Length; i++) BossSpecials.Bars[i] = saved[i];
            BossCatalog.UseOverhaul = true;
            BossEncounter.ResetRun();
            BossRails.Reset();
            BossAttackTest.ReleaseAtlases();
        }
    }
}
