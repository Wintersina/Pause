using System;
using UnityEngine;

// The shared (Firebase) leaderboard's data model. See docs/shared-leaderboard.md.
//
// Two tables, Main and Power-Ups. One document per player per table (id = the player's Firebase uid) holding
// the player's best run PER COLUMN, in full: byScore is the best-SCORE run, byDust the best-DUST run, byWorlds
// the best-WORLDS run. Sorting a column is then a plain orderBy on that column's number, and the row shown is
// that column's own best run, whole.
public enum SharedTable { Main, Power }

public enum SharedColumn { Score, Dust, Worlds }

[Serializable]
public struct SharedRun
{
    public long score;       // RunScore.Total, 1 .. 9,999,999,999
    public long dust;        // star dust in hundredths (a Power-Ups run: everything it earned)
    public int worlds;       // total worlds cleared across loops
    public int ship;         // start ship, 0 = unknown
    public int hearts;       // start hearts, 0 = unknown
    public int endHearts;    // -1 = unknown
    public int pauses;       // -1 = unknown
    public int skin;         // 0 = default
    public int powerUps;     // LeaderboardRunTag.PowerDust / PowerClear bits; 0 on the Main table

    // A run that knows nothing yet (every column unknown, score at the minimum).
    public static SharedRun Blank()
    {
        return new SharedRun { score = 1, endHearts = -1, pauses = -1 };
    }

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
            case LeaderboardBoards.RunStarDust: run.dust = Math.Max(0, value); break;
            case LeaderboardBoards.FurthestWorld: run.worlds = (int)Math.Min(Math.Max(0, value), int.MaxValue); break;
            case LeaderboardBoards.BoostedRuns:
                run.score = Math.Max(1, value);
                if (run.powerUps == 0) return false;
                break;
            default: return false;
        }
        return true;
    }

    // Better in this column, or a tie that adds what the stored run lacks (a ship), as the native marks do.
    public bool Supersedes(SharedRun have, SharedColumn column)
    {
        long mine = Metric(column), theirs = have.Metric(column);
        if (mine != theirs) return mine > theirs;
        return have.ship == 0 && ship != 0;
    }
}

// A player's document: three best-run slots. A server document always has all three; the device's pending and
// submitted records are partial (a slot is only set when it holds something).
[Serializable]
public sealed class SharedDoc
{
    public string name = "";
    public string platform = "";   // SharedBoardLimits.Android / Ios: shown as a small mark on the row
    public long updatedAtMs;
    public bool hasScore, hasDust, hasWorlds;
    public SharedRun byScore, byDust, byWorlds;

    public bool Has(SharedColumn c) { return c == SharedColumn.Score ? hasScore : c == SharedColumn.Dust ? hasDust : hasWorlds; }

    public SharedRun Get(SharedColumn c) { return c == SharedColumn.Score ? byScore : c == SharedColumn.Dust ? byDust : byWorlds; }

    public void Set(SharedColumn c, SharedRun run)
    {
        if (c == SharedColumn.Score) { byScore = run; hasScore = true; }
        else if (c == SharedColumn.Dust) { byDust = run; hasDust = true; }
        else { byWorlds = run; hasWorlds = true; }
    }

    public void Clear(SharedColumn c)
    {
        if (c == SharedColumn.Score) { byScore = default(SharedRun); hasScore = false; }
        else if (c == SharedColumn.Dust) { byDust = default(SharedRun); hasDust = false; }
        else { byWorlds = default(SharedRun); hasWorlds = false; }
    }

    public bool IsFull { get { return hasScore && hasDust && hasWorlds; } }
    public bool IsEmpty { get { return !hasScore && !hasDust && !hasWorlds; } }

    public SharedDoc Clone() { return (SharedDoc)MemberwiseClone(); }

    public static readonly SharedColumn[] Columns = { SharedColumn.Score, SharedColumn.Dust, SharedColumn.Worlds };

    // Offers a run to every slot: a slot takes it when empty or when the run beats it in that slot's column.
    // Returns whether anything changed.
    public bool Offer(SharedRun run)
    {
        bool changed = false;
        foreach (var c in Columns)
        {
            if (Has(c) && !run.Supersedes(Get(c), c)) continue;
            Set(c, run);
            changed = true;
        }
        return changed;
    }

    // Drops the pending slots a known server document already beats or ties.
    public void Prune(SharedDoc known)
    {
        foreach (var c in Columns)
            if (Has(c) && known.Has(c) && !Get(c).Supersedes(known.Get(c), c)) Clear(c);
    }
}

// What a write against the server document does: the merge a transaction performs. Pure, used by the Firebase
// adapter and the Fake alike.
public static class SharedBoardMerge
{
    // `server` is null when the player has no document yet. `overlay` holds the device's candidate runs (its
    // partial slots). Returns the document to write; `changed` is false when nothing improved (nothing is written).
    public static SharedDoc Apply(SharedDoc server, SharedDoc overlay, string name, out bool changed, string platform = SharedBoardLimits.Android)
    {
        var doc = server != null ? server.Clone() : new SharedDoc();
        changed = false;
        foreach (var c in SharedDoc.Columns)
            if (overlay.Has(c) && doc.Offer(overlay.Get(c))) changed = true;
        if (changed) { doc.name = name ?? ""; doc.platform = platform ?? ""; }
        return doc;
    }
}
