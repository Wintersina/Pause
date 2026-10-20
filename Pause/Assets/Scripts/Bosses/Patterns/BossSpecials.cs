using System;
using UnityEngine;

// THE SIGNATURE SPECIALS of the musical boss fight (docs/boss-music-director.md). While the boss song plays the boss
// fights with BossMusicDirector's gap-guaranteed waves -- and every few bars it performs one of its SPECIAL attacks: the
// non-Pattern slots of its rotation (BossPatternTables' Legacy slots: the Archon's pod lasers, the Leviathan's glare
// beams, the Bloom Queen's vine lash and trunk toss, the Drake's jaw flamethrower and eruption columns, the Kraken's
// beak jet and surf wave, the Tempest Engine's coil beams, the Dune-Eater's lens beam), fired by the executors BossActor
// already has (Tell -> Beams / Hazards), beat-synced:
//
//   * a special LANDS ON A BAR LINE: its tell starts tellSeconds before the bar line -- shortened by however late the
//     frame saw that moment (an audio-clock step, a long frame), so the hazard ignites / the laser burns on the
//     downbeat itself -- and its later volleys come a whole number of beats apart (volleyGap snapped to the beat);
//   * the waves STOP HoldBeats before it lands (no wave is in flight longer than that, so none reaches the ship's row
//     while the special is live) and resume on the next rhythm sixteenth once it is over (BossMusicDirector.HoldFromBeat);
//   * one every Bars[world] bars, counted from the director's start, in rotation over the specials the fight's
//     progress has unlocked (minPhase: BossCatalog.UnlockedAttacks). A boss whose specials are all still locked (the
//     Archon's lasers come in the last third) fires its first on the first bar line after the unlock;
//   * a tell start the game slept through (a pause, slow motion: the song runs on) is never launched late: the special
//     moves to the next bar line that leaves room for its whole tell. A special already in its tell plays out in game
//     time like everything else on the screen (its hazards froze with the ship), so it may land late after a pause.
//
// Plain C# (no engine objects): this decides WHEN and WHICH, BossActor fires it. Nothing is allocated after construction.
public sealed class BossSpecials
{
    // bars between two specials, per world (Space, Frost, Verdant, Ember, Tide, Storm, Desert): measured with the dodge
    // bot through the real encounter (BossSpecialsTest.Fairness prints the hits a minute at 4 and 8 bars)
    public static readonly int[] Bars = { 8, 8, 8, 8, 8, 8, 8 };
    // the waves stop this many beats before a special lands: a wave reaches the ship's row 2..6 beats after it leaves
    // the boss (BossWaveVoice: rows ceil((fan-out + fall) / beat), rings round(distance / speed / beat); the Dune-Eater's
    // slow tilted rows are the longest). A wave that would still take longer is not fired (BossWaveVoice.MaxTravelBeats,
    // from ArriveBeforeBeat), so none is in the air when the special lands.
    public const int HoldBeats = 6;
    // dev / test switch: false = the waves alone, as before the specials
    public static bool Enabled = true;

    readonly BossDef boss;
    readonly int world;
    readonly BossAttack[] pool;      // the table's specials, COPIED (tell and volley gap are re-timed to the song per firing)
    readonly int[] slot;             // each one's index in boss.attacks (the unlocked attacks are a prefix of it)
    readonly float[] tell0, gap0;    // the table's own tell and volley gap
    int next;                        // the rotation: the pool index tried first for the next special
    double landBeat = double.NaN;    // the bar line the next special lands on (NaN: no song)
    double activeLand = double.NaN;  // the landing of the special in progress (NaN: none)

    public int Count => pool.Length;
    public int Fired { get; private set; }
    public int Skipped { get; private set; }       // tell starts the game slept through, moved to a later bar
    public BossAttack Last { get; private set; }
    public double LastLanding { get; private set; } = double.NaN;
    public double LandBeat => landBeat;
    public bool Running => !double.IsNaN(activeLand);
    public int BarsBetween => Bars[Mathf.Clamp(world, 0, Bars.Length - 1)];

    public BossSpecials(BossDef boss, int world)
    {
        this.boss = boss;
        this.world = world;
        var table = boss != null ? boss.attacks : null;
        int n = 0;
        if (table != null) for (int i = 0; i < table.Length; i++) if (IsSpecial(table[i])) n++;
        pool = new BossAttack[n]; slot = new int[n]; tell0 = new float[n]; gap0 = new float[n];
        int k = 0;
        if (table != null)
            for (int i = 0; i < table.Length; i++)
            {
                if (!IsSpecial(table[i])) continue;
                pool[k] = table[i].Copy(); slot[k] = i; tell0[k] = table[i].tellSeconds; gap0[k] = table[i].volleyGap;
                k++;
            }
    }

    // A special is a laser or an area hazard (the kinds BossExecutors fires): the bullet-hell patterns are the waves' own
    // voice, and a plain volley is no signature.
    public static bool IsSpecial(BossAttack a) => a != null && (a.kind == BossAttackKind.Beam || BossExecutors.IsHazardKind(a.kind));

    // The song started (the director's first step): the first special lands on the first bar line a full cadence after
    // the director's own start, so the waves establish the fight before the boss shows its first special.
    public void Begin(IBeatSource music, double directorStart)
    {
        activeLand = double.NaN;
        landBeat = BarLineAtOrAfter(music, directorStart + BarsBetween * Math.Max(1, music.BeatsPerBar));
    }

    // The song stopped: nothing is scheduled until it starts again.
    public void Suspend() { landBeat = double.NaN; activeLand = double.NaN; }

    // The special in progress is over: the waves may resume.
    public void Done() { activeLand = double.NaN; }

    // Where the director must stop its waves: HoldBeats before the next landing (or the one in progress); NaN when no
    // special is coming (none scheduled, none unlocked yet, switched off).
    public double HoldFromBeat
    {
        get
        {
            if (!double.IsNaN(activeLand)) return activeLand - HoldBeats;
            return pending ? landBeat - HoldBeats : double.NaN;
        }
    }
    bool pending;   // a special is unlocked and scheduled (set by Due)

    // The beat the next (or running) special lands on: every wave must reach the ship's row before it. NaN: none coming.
    public double ArriveBeforeBeat
    {
        get
        {
            if (!double.IsNaN(activeLand)) return activeLand;
            return pending ? landBeat : double.NaN;
        }
    }

    // The special whose tell starts THIS frame (its tellSeconds and volleyGap re-timed to land on the bar line), or
    // null. `progress01` is the fight's progress for the unlocks; `realDt` the frame's unscaled length (how late a tell
    // start may legitimately be seen).
    public BossAttack Due(IBeatSource music, float realDt, float progress01)
    {
        if (!Enabled || pool.Length == 0 || music == null || !music.Active || double.IsNaN(landBeat) || Running) { pending = false; return null; }
        int pick = Pick(progress01);
        double now = music.BeatNow, spb = music.SecondsPerBeat;
        int bar = Math.Max(1, music.BeatsPerBar);
        if (pick < 0)
        {
            // nothing unlocked yet: the slot slides along the bar grid ahead of the clock (always HoldBeats or more ahead
            // of it), so the first special lands on the first bar line after the unlock with room for the waves' hold
            // and its tell
            pending = false;
            if (now >= landBeat - HoldBeats) landBeat = BarLineAtOrAfter(music, Math.Max(landBeat + bar, now + HoldBeats));
            return null;
        }
        pending = true;
        double tellBeats = tell0[pick] / spb;
        double startAt = landBeat - tellBeats;
        if (now < startAt) return null;
        // seen late: a long frame or an audio-buffer step still lands it on the bar line (the tell is that much shorter);
        // anything later (the game slept through it) moves the special to the next bar line with room for its whole tell
        double late = (now - startAt) * spb;
        double allowed = Math.Max(Math.Max(0, realDt), BossMusicDirector.AudioStepSeconds);
        if (late > allowed + 1e-6)
        {
            Skipped++;
            landBeat = BarLineAtOrAfter(music, now + tellBeats + 1e-6);
            return null;
        }
        var a = pool[pick];
        a.tellSeconds = (float)Math.Max(AttackHazard.MinTellSeconds, tell0[pick] - late);
        // later volleys on the beat: the table's gap rounded to whole beats (never under one)
        a.volleyGap = (float)(Math.Max(1.0, Math.Round(gap0[pick] / spb)) * spb);
        next = pick + 1;
        Fired++;
        Last = a;
        LastLanding = activeLand = landBeat;
        landBeat = BarLineAtOrAfter(music, landBeat + BarsBetween * bar);
        return a;
    }

    // The next unlocked special in rotation (a pool index), or -1 while every special is still locked.
    int Pick(float progress01)
    {
        int unlocked = BossCatalog.UnlockedAttacks(boss, progress01);
        for (int k = 0; k < pool.Length; k++)
        {
            int i = (next + k) % pool.Length;
            if (slot[i] < unlocked) return i;
        }
        return -1;
    }

    // The first bar line (a beat index that is DownbeatBeat mod BeatsPerBar) at or after `beat`.
    public static double BarLineAtOrAfter(IBeatSource music, double beat)
    {
        int bar = Math.Max(1, music.BeatsPerBar);
        double rel = beat - music.DownbeatBeat;
        return music.DownbeatBeat + Math.Ceiling(rel / bar - 1e-9) * bar;
    }
}
