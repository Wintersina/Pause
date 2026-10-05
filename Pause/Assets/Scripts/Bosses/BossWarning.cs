using System;
using UnityEngine;

// BOSS INCOMING: the last half minute before a world's boss is a countdown.
//
// A level ends by DISTANCE (WorldManager), not by a timer, the player
// freezes the world all the time (pausing is the game) and speed moves
// (the ramp, a blue atom's +5, resume slow-mo). So "30 seconds before the
// boss" is an estimate: WorldManager.SecondsLeftInWorld, the seconds of
// running flight the distance left takes on the speed curve from the
// current speed. Everything here is measured in those FLIGHT seconds -- the
// same dt the level clock eats (Time.deltaTime on a flying frame) -- so:
//
//   paused / frozen     dt is 0: the countdown holds, and so does every
//                       animation of the warning (they run on the same clock)
//   resume slow-mo      timeScale 0.6: the count runs 0.6x in real time,
//                       exactly as the world (and the boss's arrival) does
//   speed changes       the estimate moves (it is taken on the natural
//                       speed; a blue atom's boost eats the distance faster,
//                       so the estimate falls faster while it lasts). The number
//                       on screen never follows it in a jump: it only ever
//                       counts DOWN, at between MinRate and MaxRate of a
//                       second per flight second, closing the gap to the
//                       estimate over ConvergeSeconds (or over what is left,
//                       when less is) -- so it lands on zero as the boss does.
//
// It fires once per level visit, the first time the estimate is at or under
// LeadSeconds, and cannot fire again until that boss has come (or the pilot
// died): every world's boss, and the bosses met again on a loop. Never while
// a portal is open and waiting (WorldManager.Stage == Portal): the level
// clock is stopped there and this visit's boss is done.
// A level entered with less than the lead left gets a shortened warning
// (a shorter banner, or none, and the count starts where it is). Not in the
// tutorial, which has no WorldManager and no boss.
//
// This file is the timing and the rules (BossCountdown, no Unity objects, so
// edit-mode tests step it); BossWarningHud draws it; BossWarningAudio is the
// sound. Every tunable is in BossWarningConfig, right below.
public static class BossWarningConfig
{
    // ---- when ----
    // The warning begins when the boss is this many flight seconds away ...
    public static float LeadSeconds = 30f;
    // ... and steps up twice on the way in.
    public static float CloseAt = 10f;
    public static float FinalAt = 3f;
    // Less than this left when the level is first seen: no warning at all.
    public static float MinWarnSeconds = 1f;

    // ---- the announcement banner ----
    public static float BannerSeconds = 2.6f;
    // A shortened warning: the banner takes at most this share of what is
    // left, and is skipped under MinBannerTotal (the compact count only).
    public static float BannerMaxShare = .4f;
    public static float MinBannerTotal = 4f;
    // The boss's name stays a secret until it has been met (the codex entry
    // unlocks on first sight): before that the banner says UnknownLine.
    public static bool RevealNameBeforeFirstSight = false;
    public const string Title = "BOSS INCOMING";
    public const string UnknownLine = "UNKNOWN SIGNAL  CLOSING IN";
    public const string KnownSuffix = "  APPROACHING";
    public const string ChipLabel = "BOSS";

    // ---- how the shown count follows the estimate ----
    public static float ConvergeSeconds = 2f;
    public static float MinConvergeSeconds = .25f;
    public static float MinRate = .5f;    // never slower than this (and never backwards)
    public static float MaxRate = 2.5f;

    // ---- look ----
    // Edge glow, as alpha: its resting level and the per-second throb on top.
    public static float CloseVignette = .10f, CloseVignettePulse = .10f;
    public static float FinalVignette = .16f, FinalVignettePulse = .20f;
    public static float AnnounceVignette = .26f;
    // The hand-off into the boss intro (real time: the world is frozen).
    public static float HandoffSeconds = .35f;
    public static float HandoffVignette = .5f;

    // ---- sound ----
    // Procedural stings in UltimateShotSound's idiom (no audio files). Off:
    // silent; BossWarning.Beat still fires for whoever wants to listen.
    public static bool AudioEnabled = true;
    public static float AudioVolume = .3f;

    // ---- gameplay (OFF) ----
    // Nothing reads the level's difficulty from here. If the last seconds
    // before a boss should thin out, a spawner can scale by
    // 1 - BossWarning.SpawnCalm01, which ramps 0 -> 1 over this many final
    // flight seconds. 0 = off (SpawnCalm01 is always 0): today's behaviour.
    public static float CalmFinalSeconds = 0f;

    // ---- colour ----
    // One neon accent per world (index = world). Never the player's red.
    public static readonly Color[] Accents =
    {
        new Color(1f, .18f, .53f),      // Space: magenta (the Archon's flash)
        new Color(.43f, .95f, .93f),    // Frost: cyan
        new Color(.78f, 1f, .23f),      // Verdant: lime
        new Color(1f, .58f, .16f),      // Ember: orange
    };

    public static Color Accent(int world)
    {
        return Accents[Mathf.Clamp(world, 0, Accents.Length - 1)];
    }
}

public enum BossWarningStage { Idle, Countdown, Close, Final }

// What the warning announces as it goes (BossWarning.Beat): the hook for
// sound, haptics, or anything else that wants to ride the build-up.
public enum BossWarningBeat
{
    Announce,   // the warning begins (T-30, or a shortened one)
    Close,      // T-10
    Final,      // T-3
    Second,     // each whole second from T-10 down
    Arrive,     // the boss intro has started
    Cancel,     // gone without a boss (the pilot died)
}

// Where the level stands, as the warning sees it.
public enum BossWarningInput
{
    None,       // no boss ahead: dead, tutorial, the fight itself, a portal open and waiting
    Ahead,      // flying (or paused) towards this world's boss
    Holding,    // the level is flown; the encounter waits out an ultimate's cinematic
    Arriving,   // the boss intro is running
}

// The countdown itself. Step it once a frame.
public sealed class BossCountdown
{
    public BossWarningStage Stage { get; private set; }
    // Seconds on the display (only ever falls while a warning runs) ...
    public float Shown { get; private set; }
    // ... what it started at (LeadSeconds, less for a shortened warning) ...
    public float Total { get; private set; }
    // ... and flight seconds since it began.
    public float Since { get; private set; }
    // The whole seconds read-out: ceil(Shown), 1 until the boss is here.
    public int Seconds { get; private set; }
    public float BannerFor { get; private set; }
    public bool Shortened { get; private set; }
    // How many warnings have begun (tests).
    public int Triggers { get; private set; }
    // Flight seconds (Since) at the last Second / stage beat: the pulse clock.
    public float BeatAt { get; private set; }

    public bool Active { get { return Stage != BossWarningStage.Idle; } }
    public bool BannerUp { get { return Active && Since < BannerFor; } }

    public Action<BossWarningBeat> OnBeat;

    bool fired;

    public void Reset()
    {
        Stage = BossWarningStage.Idle;
        Shown = Total = Since = BannerFor = BeatAt = 0f;
        Seconds = 0;
        Shortened = false;
        fired = false;
    }

    // One frame. `eta`: flight seconds to the boss (only read when Ahead);
    // `dt`: flight seconds this frame -- 0 while paused or frozen.
    public void Step(BossWarningInput input, float eta, float dt)
    {
        switch (input)
        {
            case BossWarningInput.None:
                if (Active) End(BossWarningBeat.Cancel);
                fired = false;   // the next level's boss may warn again
                return;
            case BossWarningInput.Arriving:
                if (Active) End(BossWarningBeat.Arrive);
                return;
            case BossWarningInput.Holding:
                return;
        }

        if (!Active)
        {
            // NaN / infinity (standing still at speed 0) never start it.
            if (fired || !(eta <= BossWarningConfig.LeadSeconds)) return;
            fired = true;
            if (eta < BossWarningConfig.MinWarnSeconds) return;
            Begin(eta);
            return;
        }
        if (dt <= 0f) return;

        // Close the gap to the estimate by counting faster or slower, never
        // by jumping, and never upwards.
        float rate = BossWarningConfig.MinRate;
        if (eta < 1e6f)
        {
            float window = Mathf.Max(BossWarningConfig.MinConvergeSeconds,
                                     Mathf.Min(BossWarningConfig.ConvergeSeconds, eta));
            rate = Mathf.Clamp(1f + (Shown - eta) / window, BossWarningConfig.MinRate, BossWarningConfig.MaxRate);
        }
        Shown = Mathf.Max(0f, Shown - dt * rate);
        Since += dt;

        var stage = StageFor(Shown);
        bool stepped = stage > Stage;
        if (stepped)
        {
            Stage = stage;
            BeatAt = Since;
        }
        int seconds = Mathf.Clamp(Mathf.CeilToInt(Shown), 1, 99);
        bool ticked = seconds != Seconds;
        Seconds = seconds;
        if (stepped) Raise(stage == BossWarningStage.Final ? BossWarningBeat.Final : BossWarningBeat.Close);
        if (ticked && Stage >= BossWarningStage.Close)
        {
            BeatAt = Since;
            Raise(BossWarningBeat.Second);
        }
    }

    static BossWarningStage StageFor(float shown)
    {
        return shown <= BossWarningConfig.FinalAt ? BossWarningStage.Final
             : shown <= BossWarningConfig.CloseAt ? BossWarningStage.Close
             : BossWarningStage.Countdown;
    }

    void Begin(float eta)
    {
        Total = Shown = Mathf.Max(0f, eta);
        Since = BeatAt = 0f;
        Shortened = eta < BossWarningConfig.LeadSeconds - 1.5f;
        BannerFor = Total < BossWarningConfig.MinBannerTotal ? 0f
            : Mathf.Min(BossWarningConfig.BannerSeconds, Total * BossWarningConfig.BannerMaxShare);
        Stage = StageFor(Shown);
        Seconds = Mathf.Clamp(Mathf.CeilToInt(Shown), 1, 99);
        Triggers++;
        Raise(BossWarningBeat.Announce);
    }

    void End(BossWarningBeat beat)
    {
        Stage = BossWarningStage.Idle;
        Shown = 0f;
        Seconds = 0;
        Raise(beat);
    }

    void Raise(BossWarningBeat beat)
    {
        var handler = OnBeat;
        if (handler != null) handler(beat);
    }
}

// What the rest of the game may read (all read-only), and the beat hook.
public static class BossWarning
{
    // Sound, haptics, a spawner: subscribe once, at load.
    public static event Action<BossWarningBeat> Beat;

    // The live countdown (BossWarningHud's), null outside a run.
    public static BossCountdown Countdown;

    public static bool Active { get { return Countdown != null && Countdown.Active; } }
    public static BossWarningStage Stage
    {
        get { return Countdown != null ? Countdown.Stage : BossWarningStage.Idle; }
    }
    // The count on screen; +infinity when no warning is up.
    public static float ShownSeconds
    {
        get { return Active ? Countdown.Shown : float.PositiveInfinity; }
    }

    // Flight seconds until this world's boss, whether or not the warning is
    // up yet; +infinity when there is no boss ahead (see Read). The raw
    // estimate: it moves with speed. For pacing, not for display.
    public static float SecondsToBoss
    {
        get
        {
            var wm = WorldManager.Instance;
            return Read(wm) == BossWarningInput.Ahead ? wm.SecondsLeftInWorld : float.PositiveInfinity;
        }
    }

    // 0 -> 1 over the last CalmFinalSeconds before the boss; always 0 while
    // that tunable is 0 (the default). Nothing consumes it yet.
    public static float SpawnCalm01
    {
        get
        {
            float span = BossWarningConfig.CalmFinalSeconds;
            if (span <= 0f) return 0f;
            float left = SecondsToBoss;
            return left >= span ? 0f : Mathf.Clamp01(1f - left / span);
        }
    }

    // Is a boss ahead of this flight? Allocation-free: called every frame.
    public static BossWarningInput Read(WorldManager wm)
    {
        if (wm == null || buttonClicks.playerDied || startMenu.youAreInTutorial || !wm.HasLevelClock)
            return BossWarningInput.None;
        var encounter = BossEncounter.Instance;
        if (encounter != null && encounter.IsRunning)
        {
            switch (encounter.State)
            {
                case BossEncounter.Phase.Pending: return BossWarningInput.Holding;
                case BossEncounter.Phase.Intro: return BossWarningInput.Arriving;
                default: return BossWarningInput.None;
            }
        }
        // A portal is open and waiting (any world, the loop portal too): the
        // level clock is stopped and this visit's boss is done.
        if (wm.Stage == WorldManager.LevelStage.Portal || wm.DistanceLeft <= 0f) return BossWarningInput.None;
        if (BossEncounter.DoneInWorld(WorldManager.CurrentIndex)) return BossWarningInput.None;
        return BossWarningInput.Ahead;
    }

    internal static void Raise(BossWarningBeat beat)
    {
        var handler = Beat;
        if (handler != null) handler(beat);
    }
}
