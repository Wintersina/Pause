using UnityEngine;

// BEAT SYNC KNOBS, one row per boss (index-aligned with WorldManager.Worlds / BossPatternTables). While the boss's
// track is playing (BossBeat.Source), the pattern runner fires each stream's volleys on the beat grid and a pattern
// starts on a bar line; with no beat map or no music every boss keeps its old cooldown timers and these numbers do
// nothing. The shapes, colours and per-stream speeds stay in BossPatternTables.
//
//   beatsScale   every stream's beats-between-volleys times this (1 = about the old pace: the old interval rounded to a
//                sixteenth / half beat; 2 = half as many waves). Per-stream override: PatternStream.beats.
//   speedScale   bullet speed times this while beat-synced (waves arrive on the beat; 1 = the old speed)
//   barWaitMax   a pattern waits for the next BAR line if that is at most this many seconds away, else for the next
//                beat (the boss holds its tell pose a little longer; it never fires early)
//   enabled      false keeps this boss on its old timers
public sealed class BeatTuning
{
    public float beatsScale = 1f;
    public float speedScale = 1f;
    public float barWaitMax = 1.2f;
    public bool enabled = true;

    public static readonly BeatTuning Default = new BeatTuning();
}

public static class BossBeatTuning
{
    static readonly BeatTuning[] rows =
    {
        new BeatTuning(),   // 0 Space   Void Archon
        new BeatTuning(),   // 1 Frost   Hoarfrost Leviathan
        new BeatTuning(),   // 2 Verdant Bloom Queen
        new BeatTuning(),   // 3 Ember   Cinder Drake
        new BeatTuning(),   // 4 Tide    Iron Kraken
        new BeatTuning(),   // 5 Storm   Tempest Engine
        new BeatTuning(),   // 6 Desert  Dune-Eater
    };

    public static BeatTuning For(int world)
    {
        return rows[Mathf.Clamp(world, 0, rows.Length - 1)];
    }
}

// Where a pattern's first volley lands: the next bar line if it is close enough, else the next beat.
public static class BeatAlign
{
    const double Slack = .08;   // beats: a moment already just past a line still counts as on it

    // The beat position to start at, for a pattern whose telegraph ends `afterSeconds` from now. Never earlier than that.
    public static double Target(IBeatSource b, double afterSeconds, float barWaitMax)
    {
        double spb = b.SecondsPerBeat;
        double earliest = b.BeatNow + afterSeconds / spb;
        double nextBeat = System.Math.Ceiling(earliest - Slack);
        int bar = System.Math.Max(1, b.BeatsPerBar);
        double rel = nextBeat - b.DownbeatBeat;
        double nextBar = nextBeat + ((bar - (rel % bar)) % bar + bar) % bar;
        return (nextBar - earliest) * spb <= barWaitMax ? nextBar : nextBeat;
    }
}
