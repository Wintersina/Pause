using System;
using System.Collections.Generic;

// COLLECT ALL as a short sequence: the list glides to each newly unlocked find in turn and that find's own reward is
// credited as it plays (so the badges drop one by one). Plain C#, no Unity types: the order, the amounts, the skip and
// the cancel are all arithmetic on Tick(dt), so EditMode tests drive it without a frame loop.
//
// Persistence: every item is credited through `collect` (Codex.CollectOne, which saves at once) when its step ends, and
// Finish() credits all the rest immediately, so skipping, leaving the panel or backgrounding the app loses nothing and
// the total always equals what an instant collect-all pays.
public sealed class CodexCollectSequence
{
    public const float MaxStepSeconds = .45f, MinStepSeconds = .1f, ComfortableCount = 6, TotalBudgetSeconds = 2.7f;

    readonly List<string> ids;
    readonly Func<string, int> collect;
    int index;
    float elapsed;
    bool started, finished;

    public Action<string> Focused;          // the list should glide to this id
    public Action<string, int> Paid;        // id, dust credited (fires for the skipped rest too)

    public CodexCollectSequence(IEnumerable<string> pendingIds, Func<string, int> collect)
    {
        ids = new List<string>(pendingIds);
        this.collect = collect;
        StepSeconds = StepSecondsFor(ids.Count);
    }

    // About .45 s per item up to six, then the whole thing is squeezed into ~2.7 s (never below .1 s per item).
    public static float StepSecondsFor(int count)
    {
        if (count <= ComfortableCount) return MaxStepSeconds;
        return Math.Max(MinStepSeconds, TotalBudgetSeconds / count);
    }

    public float StepSeconds { get; private set; }
    public int Count { get { return ids.Count; } }
    public int Index { get { return index; } }
    public bool Active { get { return started && !finished; } }
    public bool Done { get { return finished; } }
    public int TotalPaid { get; private set; }
    public string Current { get { return index < ids.Count ? ids[index] : null; } }

    // Focuses the first item. An empty sequence is done at once.
    public void Begin()
    {
        if (started) return;
        started = true;
        if (ids.Count == 0) { finished = true; return; }
        if (Focused != null) Focused(ids[0]);
    }

    // Advances unscaled seconds: each time a step's time is up its item is credited and the next one is focused.
    public void Tick(float dt)
    {
        if (!Active) return;
        elapsed += Math.Max(0f, dt);
        while (!finished && elapsed >= StepSeconds)
        {
            elapsed -= StepSeconds;
            PayCurrent();
            if (index >= ids.Count) { finished = true; break; }
            if (Focused != null) Focused(ids[index]);
        }
    }

    // Skip / Back / panel closed / app backgrounded: credits every remaining item instantly. Safe any time, any number of times.
    public int Finish()
    {
        if (!started) { started = true; }
        if (finished) return 0;
        int before = TotalPaid;
        while (index < ids.Count) PayCurrent();
        finished = true;
        return TotalPaid - before;
    }

    void PayCurrent()
    {
        string id = ids[index++];
        int dust = collect(id);
        TotalPaid += dust;
        if (Paid != null) Paid(id, dust);
    }
}
