using System;
using System.Collections.Generic;

// In-memory shared board for tests and previews. It applies SharedBoardLimits (the rules' bounds, including the
// minimum write interval against a settable clock), so a client that would be refused by the real rules is
// refused here too. Everything is synchronous unless DeferLoads is set.
public sealed class FakeSharedBoard : ISharedBoardBackend
{
    public bool Available = true;
    public bool SignedIn = true;
    public bool SignInSucceeds = true;
    public string Uid = "uid-local";
    // When set, uploads and loads fail with this result instead (offline, denied, quota ...).
    public SharedResult? Fail;
    public bool AuthDeleteSucceeds = true;
    public bool DeleteFails;
    public bool DeferLoads;
    // The seconds the fake server's clock shows; the rules' write interval is measured against it.
    public double Now = 1000000;

    public int SignInCalls, UploadCalls, LoadCalls, DeleteCalls;
    public readonly List<SharedDoc> Uploads = new List<SharedDoc>();
    public readonly Dictionary<string, SharedDoc>[] Docs =
    {
        new Dictionary<string, SharedDoc>(), new Dictionary<string, SharedDoc>(),
    };

    readonly List<Action> deferred = new List<Action>();

    public string BackendName { get { return "Fake"; } }
    public bool IsAvailable { get { return Available; } }
    public bool IsSignedIn { get { return Available && SignedIn; } }
    public string UserId { get { return IsSignedIn ? Uid : null; } }

    public void SignIn(Action<bool> done)
    {
        SignInCalls++;
        if (Available && SignInSucceeds) SignedIn = true;
        if (done != null) done(IsSignedIn);
    }

    public void SignOut() { SignedIn = false; }

    // Another player's document, written directly (skips the rules, as a seed).
    public void Seed(SharedTable table, string uid, SharedDoc doc) { Docs[(int)table][uid] = doc.Clone(); }

    public SharedDoc Get(SharedTable table, string uid)
    {
        SharedDoc d;
        return Docs[(int)table].TryGetValue(uid, out d) ? d : null;
    }

    public void Upload(SharedTable table, string name, SharedDoc overlay, Action<SharedResult, SharedDoc> done)
    {
        UploadCalls++;
        if (!IsSignedIn) { done(SharedResult.SignedOut, null); return; }
        if (Fail.HasValue) { done(Fail.Value, null); return; }
        SharedDoc server = Get(table, Uid);
        bool changed;
        var merged = SharedBoardMerge.Apply(server, overlay, name, out changed);
        if (!changed) { done(SharedResult.Ok, merged); return; }
        double since = server == null ? double.MaxValue : Now - server.updatedAtMs / 1000.0;
        bool allowed = server == null ? SharedBoardLimits.AllowCreate(merged, table)
                                      : SharedBoardLimits.AllowUpdate(server, merged, table, since);
        if (!allowed) { done(SharedResult.Denied, null); return; }
        merged.updatedAtMs = (long)(Now * 1000.0);
        Docs[(int)table][Uid] = merged;
        Uploads.Add(merged.Clone());
        done(SharedResult.Ok, merged.Clone());
    }

    public void LoadTop(SharedTable table, SharedColumn column, int count, Action<LeaderboardPage> done)
    {
        LoadCalls++;
        Action run = () => done(Build(table, column, count));
        if (DeferLoads) deferred.Add(run); else run();
    }

    public void CompleteLoads()
    {
        var list = new List<Action>(deferred);
        deferred.Clear();
        foreach (var a in list) a();
    }

    LeaderboardPage Build(SharedTable table, SharedColumn column, int count)
    {
        if (!IsSignedIn) return LeaderboardPage.Failed(LeaderboardStatus.NotSignedIn);
        if (Fail.HasValue) return LeaderboardPage.Failed(Fail.Value == SharedResult.SignedOut ? LeaderboardStatus.NotSignedIn : LeaderboardStatus.Error);
        var rows = new List<KeyValuePair<string, SharedDoc>>();
        foreach (var p in Docs[(int)table])
            if (column == SharedColumn.Score || p.Value.Get(column).Metric(column) >= 1) rows.Add(p);
        rows.Sort((a, b) =>
        {
            int c = b.Value.Get(column).Metric(column).CompareTo(a.Value.Get(column).Metric(column));
            return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
        });
        var page = new LeaderboardPage { status = LeaderboardStatus.Ok };
        for (int i = 0; i < rows.Count && i < count; i++)
            page.entries.Add(SharedBoardRows.ToEntry(i + 1, rows[i].Key, rows[i].Value, column, rows[i].Key == Uid));
        SharedDoc mine = Get(table, Uid);
        if (mine != null)
        {
            bool onPage = false;
            foreach (var e in page.entries) if (e.isLocalPlayer) { page.player = e; onPage = true; }
            if (!onPage) page.player = SharedBoardRows.ToEntry(0, Uid, mine, column, true);
            page.hasPlayer = true;
        }
        return page;
    }

    public void DeleteMine(Action<bool, bool> done)
    {
        DeleteCalls++;
        if (!IsSignedIn || DeleteFails || Fail.HasValue) { done(false, false); return; }
        Docs[0].Remove(Uid);
        Docs[1].Remove(Uid);
        bool auth = AuthDeleteSucceeds;
        SignedIn = false;
        done(true, auth);
    }
}
