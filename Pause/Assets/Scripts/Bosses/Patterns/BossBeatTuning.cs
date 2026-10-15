using UnityEngine;

// BEAT SYNC KNOBS, one row per boss (index-aligned with WorldManager.Worlds / BossPatternTables). While the boss's
// track is playing (BossBeat.Source), the pattern runner fires each stream's volleys on the beat grid and a pattern
// starts on a bar line; with no beat map or no music every boss keeps its old cooldown timers and these numbers do
// nothing. The shapes, colours and per-stream speeds stay in BossPatternTables.
//
//   beatsScale   every stream's beats-between-volleys times this (1 = about the old pace: the old interval rounded to a
//                sixteenth / half beat; 2 = half as many waves). Per-stream override: PatternStream.beats.
//   speedScale   bullet speed times this while beat-synced (1 = the old travel speed)
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
    // Twice the volley cadence before subdivision rounding; dense streams stop at a sixteenth.
    static readonly BeatTuning[] rows =
    {
        new BeatTuning { beatsScale = .5f },   // 0 Space   Void Archon
        new BeatTuning { beatsScale = .5f },   // 1 Frost   Hoarfrost Leviathan
        new BeatTuning { beatsScale = .5f },   // 2 Verdant Bloom Queen
        new BeatTuning { beatsScale = .5f },   // 3 Ember   Cinder Drake
        new BeatTuning { beatsScale = .5f },   // 4 Tide    Iron Kraken
        new BeatTuning { beatsScale = .5f },   // 5 Storm   Tempest Engine
        new BeatTuning { beatsScale = .5f },   // 6 Desert  Dune-Eater
    };

    public static BeatTuning For(int world)
    {
        return rows[Mathf.Clamp(world, 0, rows.Length - 1)];
    }
}

// Where a pattern's first volley lands: the next bar line if it is close enough, else the next beat.
public static class BeatAlign
{
    // The beat position to start at, for a pattern whose telegraph ends `afterSeconds` from now. Never earlier than that.
    public static double Target(IBeatSource b, double afterSeconds, float barWaitMax)
    {
        double spb = b.SecondsPerBeat;
        double earliest = b.BeatNow + afterSeconds / spb;
        double nextBeat = System.Math.Ceiling(earliest);
        int bar = System.Math.Max(1, b.BeatsPerBar);
        double rel = nextBeat - b.DownbeatBeat;
        double nextBar = nextBeat + ((bar - (rel % bar)) % bar + bar) % bar;
        return (nextBar - earliest) * spb <= barWaitMax ? nextBar : nextBeat;
    }
}
