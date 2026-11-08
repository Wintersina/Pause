using System;
using UnityEngine;

// The shared (Firebase) leaderboard's data model. See docs/shared-leaderboard.md.
//
// Two tables, Main and Power-Ups. ONE document, so ONE row, per player per table (id = the player's Firebase
// uid): the player's three personal bests (score, dust, worlds; each may come from a different run) plus the
// loadout/end details of one run (`run`: the best-SCORE run, falling back to the dust run, then the worlds run).
// The SCORE / DUST / WORLDS tabs only change the ORDER of the same rows (orderBy on one top-level number).
// A best of 0 means "none / unknown" and is shown as a dash (worlds 0 is a real, shown 0).
public enum SharedTable { Main, Power }

public enum SharedColumn { Score, Dust, Worlds }

[Serializable]
public struct SharedRun
{
    public long score;       // RunScore.Total, 1 .. 9,999,999,999; 0 = unknown (a native dust/worlds mark with no run score)
    public long dust;        // star dust in hundredths (a Power-Ups run: everything it earned)
    public int worlds;       // total worlds cleared across loops
    public int ship;         // start ship, 0 = unknown
    public int hearts;       // start hearts, 0 = unknown
    public int endHearts;    // -1 = unknown
    public int pauses;       // -1 = unknown
    public int skin;         // 0 = default
    public int powerUps;     // LeaderboardRunTag.PowerDust / PowerClear bits; 0 on the Main table

    // A run that knows nothing yet (every value unknown; score 0 is "unknown", never a fake 1).
    public static SharedRun Blank()
    {
        return new SharedRun { endHearts = -1, pauses = -1 };
    }

    public bool LoadoutKnown { get { return ship != 0; } }

    public long Metric(SharedColumn column)
    {
        return column == SharedColumn.Score ? score : column == SharedColumn.Dust ? dust : worlds;
    }

    // A finished run, in the units the boards store.
    public static SharedRun From(LeaderboardRunStats run)
    {
        bool powered = run.PoweredUp;
        return new SharedRun
        {
            score = run.score,
            dust = (long)Math.Round(Math.Max(0f, powered ? run.dustEarned : run.starDust) * 100.0),
            worlds = Math.Max(0, run.worldsCleared),
            ship = run.startShip,
            hearts = run.startHearts,
            endHearts = run.hasEnd ? run.endHearts : -1,
            pauses = run.hasPauses ? run.endPauses : -1,
            skin = run.startSkin,
            powerUps = powered ? run.powerUps & LeaderboardRunTag.PowerAll : 0,
        };
    }

    // The run behind a native score mark (a board value plus its run tag). What the tag does not say stays unknown.
    // False when the mark can not become a shared run (premium tag, a Power-Ups mark with no power-up bits).
    public static bool TryFromMark(string boardId, long value, string tag, out SharedRun run)
    {
        run = Blank();
        if (LeaderboardRunTag.IsPremium(tag)) return false;
        int ship, hearts, end, pauses, skin, worlds, powerUps;
        long dust, runScore;
        if (!string.IsNullOrEmpty(tag)
            && LeaderboardRunTag.TryParse(tag, out ship, out hearts, out end, out pauses, out skin, out dust, out worlds, out powerUps, out runScore))
        {
            run.ship = ship;
            run.hearts = hearts;
            run.endHearts = end;
            run.pauses = pauses;
            run.skin = skin;
            run.powerUps = powerUps;
            if (dust >= 0) run.dust = dust;
            if (worlds >= 0) run.worlds = worlds;
            if (runScore >= 1) run.score = runScore;
        }
        switch (boardId)
        {
            case LeaderboardBoards.TopScore: run.score = Math.Max(1, value); break;
            case LeaderboardBoards.BoostedRuns:
                run.score = Math.Max(1, value);
                if (run.powerUps == 0) return false;
                break;
            default: return false;
        }
        return true;
    }
}

// A player's document: the three personal bests plus the details of one run. A server document is valid when at
// least one best is above 0. The device's pending and submitted records are the same type (pending = candidate
// bests not yet uploaded; a column is "set" when its best is above 0).
[Serializable]
public sealed class SharedDoc
{
    public string name = "";
    public string platform = "";   // SharedBoardLimits.Android / Ios: shown as a small mark on the row
    public long updatedAtMs;
    public long score;             // best score, 0 = none/unknown
    public long dust;              // best star dust in hundredths, 0 = none
    public int worlds;             // most worlds cleared, 0 = none
    // The details of the best-score run (else the dust run, else the worlds run). Its own score/dust/worlds are
    // not stored on the server; only ship, hearts, endHearts, pauses, skin, powerUps matter.
    public SharedRun run = SharedRun.Blank();
    // Read from an old-shape server document (byScore/byDust/byWorlds). Not on the wire: it only makes the next
    // upload rewrite the document in the new shape.
    public bool legacy;

    public long Metric(SharedColumn c) { return c == SharedColumn.Score ? score : c == SharedColumn.Dust ? dust : worlds; }

    public bool Has(SharedColumn c) { return Metric(c) > 0; }

    public void Clear(SharedColumn c)
    {
        if (c == SharedColumn.Score) score = 0; else if (c == SharedColumn.Dust) dust = 0; else worlds = 0;
        if (IsEmpty) run = SharedRun.Blank();
    }

    public bool IsEmpty { get { return score <= 0 && dust <= 0 && worlds <= 0; } }
    public bool IsFull { get { return !IsEmpty; } }

    public SharedDoc Clone() { return (SharedDoc)MemberwiseClone(); }

    public static readonly SharedColumn[] Columns = { SharedColumn.Score, SharedColumn.Dust, SharedColumn.Worlds };

    // Offers a run: each best takes it when the run beats it. The run details follow the best-score run (score
    // known), else the best-dust run, else the best-worlds run; a tie only fills what the details lack, and a
    // best run that does not know its loadout / end state keeps the details another best run supplied (the
    // fallback "dust run, then worlds run"). Returns whether any best or the details changed.
    public bool Offer(SharedRun r)
    {
        long oldScore = score, oldDust = dust; int oldWorlds = worlds;
        bool changed = false;
        if (r.score > score) { score = r.score; changed = true; }
        if (r.dust > dust) { dust = r.dust; changed = true; }
        if (r.worlds > worlds) { worlds = r.worlds; changed = true; }

        // Is this run the one the details should come from, and did it strictly improve that ranking?
        bool lead, strict;
        if (r.score > 0) { lead = r.score >= score; strict = r.score > oldScore; }
        else if (score == 0 && r.dust > 0) { lead = r.dust >= dust; strict = r.dust > oldDust; }
        else if (score == 0 && dust == 0) { lead = r.worlds >= worlds; strict = r.worlds > oldWorlds; }
        else { lead = false; strict = false; }
        bool fallback = !lead && ((r.dust > 0 && r.dust == dust) || (r.worlds > 0 && r.worlds == worlds));

        if (lead || fallback)
        {
            var d = run;
            bool replaceLoadout = lead && strict;
            bool replaceEnd = lead && strict;
            if (r.ship != 0 && (replaceLoadout || d.ship == 0)) { d.ship = r.ship; d.hearts = r.hearts; d.skin = r.skin; }
            if (r.endHearts >= 0 && (replaceEnd || d.endHearts < 0)) d.endHearts = r.endHearts;
            if (r.pauses >= 0 && (replaceEnd || d.pauses < 0)) d.pauses = r.pauses;
            if (r.powerUps != 0 && (replaceLoadout || d.powerUps == 0)) d.powerUps = r.powerUps;
            if (!SameDetails(d, run)) { run = d; changed = true; }
        }
        if (legacy) changed = true;   // an old-shape document is rewritten on the next upload
        return changed;
    }

    static bool SameDetails(SharedRun a, SharedRun b)
    {
        return a.ship == b.ship && a.hearts == b.hearts && a.endHearts == b.endHearts && a.pauses == b.pauses
            && a.skin == b.skin && a.powerUps == b.powerUps;
    }

    // Drops the pending bests a known server document already holds (equal or better). Pending details with
    // nothing left to say go with them.
    public void Prune(SharedDoc known)
    {
        foreach (var c in Columns)
            if (Has(c) && known.Has(c) && Metric(c) <= known.Metric(c)) Clear(c);
    }
}

// What a write against the server document does: the merge a transaction performs. Pure, used by the Firebase
// adapter and the Fake alike.
public static class SharedBoardMerge
{
    // `server` is null when the player has no document yet. `overlay` holds the device's candidate bests, with
    // the details of the run(s) behind them. Returns the document to write; `changed` is false when nothing
    // improved (nothing is written).
    public static SharedDoc Apply(SharedDoc server, SharedDoc overlay, string name, out bool changed, string platform = SharedBoardLimits.Android)
    {
        var doc = server != null ? server.Clone() : new SharedDoc();
        // Candidate runs rebuilt from the overlay: its bests, with its details attached to whichever it leads.
        changed = false;
        var cand = overlay.run;
        cand.score = overlay.score; cand.dust = overlay.dust; cand.worlds = overlay.worlds;
        if (doc.Offer(cand)) changed = true;
        if (changed) { doc.name = name ?? ""; doc.platform = platform ?? ""; doc.legacy = false; }
        return doc;
    }
}
