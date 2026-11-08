#if PAUSE_FIREBASE
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase;
using Firebase.AppCheck;
using Firebase.Auth;
using Firebase.Extensions;
using Firebase.Firestore;
using UnityEngine;
#if UNITY_ANDROID
using GooglePlayGames;
#endif

// The Firebase half of the shared leaderboard: Auth (Play Games on Android, Game Center on iOS), Firestore and
// App Check, behind ISharedBoardBackend. docs/shared-leaderboard.md has the decisions.
//
// Compiled only when PAUSE_FIREBASE is defined, which the SDK-import PR does together with the Firebase Unity
// SDK 13.17.x (Auth, Firestore, AppCheck), its config files and the EDM bump. THIS FILE HAS NOT BEEN COMPILED
// YET (CI has no SDK): it is written against the documented Unity API and the import PR is its first build.
// Android compiled and built in the import PR; iOS (AppAttestProviderFactory, GameCenterAuthProvider.GetCredentialAsync)
// is checked against the SDK's iOS assembly only and still needs an Xcode build.
public sealed class FirebaseSharedBoard : ISharedBoardBackend
{
    FirebaseAuth auth;
    FirebaseFirestore db;
    bool ready, failed;

    public FirebaseSharedBoard() { Init(); }

    void Init()
    {
        try
        {
            // App Check first, before any other Firebase service is used.
#if UNITY_EDITOR || DEVELOPMENT_BUILD || PAUSE_DEV
            // Debug provider: dev builds only (its token is registered in the console, never committed).
            FirebaseAppCheck.SetAppCheckProviderFactory(DebugAppCheckProviderFactory.Instance);
#elif UNITY_ANDROID
            FirebaseAppCheck.SetAppCheckProviderFactory(PlayIntegrityProviderFactory.Instance);
#elif UNITY_IOS
            FirebaseAppCheck.SetAppCheckProviderFactory(AppAttestProviderFactory.Instance);
#endif
            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(t =>
            {
                try
                {
                    if (t.IsFaulted || t.IsCanceled || t.Result != DependencyStatus.Available) { failed = true; return; }
                    var app = FirebaseApp.DefaultInstance;   // throws without the config file
                    auth = FirebaseAuth.GetAuth(app);
                    db = FirebaseFirestore.GetInstance(app);
                    ready = true;
                }
                catch (Exception e) { failed = true; Debug.Log("[SharedBoard] Firebase unavailable: " + e.Message); }
            });
        }
        catch (Exception e) { failed = true; Debug.Log("[SharedBoard] Firebase unavailable: " + e.Message); }
    }

    public string BackendName { get { return "Firebase"; } }
    public string Platform { get { return Application.platform == RuntimePlatform.IPhonePlayer ? SharedBoardLimits.Ios : SharedBoardLimits.Android; } }
    public bool IsAvailable { get { return ready && !failed; } }
    public bool IsSignedIn { get { return IsAvailable && auth.CurrentUser != null; } }
    public string UserId { get { return IsSignedIn ? auth.CurrentUser.UserId : null; } }

    public void SignIn(Action<bool> done)
    {
        if (!IsAvailable) { done(false); return; }
        if (auth.CurrentUser != null) { done(true); return; }
        try
        {
#if UNITY_ANDROID
            // v2 plugin: needs the OAuth web client id in PlayGamesSettings (mWebClientId). The code is single-use.
            PlayGamesPlatform.Instance.RequestServerSideAccess(false, code =>
            {
                if (string.IsNullOrEmpty(code)) { Debug.Log("[SharedBoard] Play Games gave no server auth code; not signed in to Firebase."); done(false); return; }
                Finish(auth.SignInWithCredentialAsync(PlayGamesAuthProvider.GetCredential(code)), done);
            });
#elif UNITY_IOS
            GameCenterAuthProvider.GetCredentialAsync().ContinueWithOnMainThread(t =>
            {
                if (t.IsFaulted || t.IsCanceled) { done(false); return; }
                Finish(auth.SignInWithCredentialAsync(t.Result), done);
            });
#else
            done(false);
#endif
        }
        catch (Exception e) { Debug.Log("[SharedBoard] sign-in failed: " + e.Message); done(false); }
    }

    static void Finish(Task<FirebaseUser> task, Action<bool> done)
    {
        task.ContinueWithOnMainThread(t =>
        {
            if (t.IsFaulted || t.IsCanceled) Debug.Log("[SharedBoard] Firebase sign-in refused: " + (t.Exception != null ? t.Exception.Message : "canceled"));
            else Debug.Log("[SharedBoard] Firebase signed in.");
            done(!t.IsFaulted && !t.IsCanceled);
        });
    }

    public void SignOut()
    {
        try { if (IsAvailable) auth.SignOut(); } catch (Exception) { }
    }

    // ---- mapping ----

    static Dictionary<string, object> RunToDict(SharedRun r)
    {
        return new Dictionary<string, object>
        {
            { "ship", (long)r.ship }, { "hearts", (long)r.hearts }, { "endHearts", (long)r.endHearts },
            { "pauses", (long)r.pauses }, { "skin", (long)r.skin }, { "powerUps", (long)r.powerUps },
        };
    }

    static Dictionary<string, object> DocToDict(SharedDoc d)
    {
        return new Dictionary<string, object>
        {
            { "name", d.name },
            { "platform", d.platform },
            { "updatedAt", FieldValue.ServerTimestamp },
            { "score", d.score }, { "dust", d.dust }, { "worlds", (long)d.worlds },
            { "run", RunToDict(d.run) },
        };
    }

    static long L(Dictionary<string, object> d, string key)
    {
        object v;
        return d.TryGetValue(key, out v) && v != null ? Convert.ToInt64(v) : 0;
    }

    static bool Has(Dictionary<string, object> d, string key) { object v; return d.TryGetValue(key, out v) && v != null; }

    // Details from a `run` map; a missing field stays unknown.
    static SharedRun DetailsFromDict(Dictionary<string, object> d)
    {
        var run = SharedRun.Blank();
        if (d == null) return run;
        run.ship = (int)L(d, "ship"); run.hearts = (int)L(d, "hearts"); run.skin = (int)L(d, "skin"); run.powerUps = (int)L(d, "powerUps");
        if (Has(d, "endHearts")) run.endHearts = (int)L(d, "endHearts");
        if (Has(d, "pauses")) run.pauses = (int)L(d, "pauses");
        return run;
    }

    static SharedDoc DocFromSnapshot(DocumentSnapshot snap)
    {
        if (snap == null || !snap.Exists) return null;
        var d = snap.ToDictionary();
        var doc = new SharedDoc();
        object name, ts;
        object plat;
        doc.platform = d.TryGetValue("platform", out plat) && plat != null ? plat.ToString() : "";
        doc.name = d.TryGetValue("name", out name) && name != null ? name.ToString() : "";
        if (d.TryGetValue("updatedAt", out ts) && ts is Timestamp) doc.updatedAtMs = new DateTimeOffset(((Timestamp)ts).ToDateTime()).ToUnixTimeMilliseconds();
        object o;
        if (d.ContainsKey("byScore") || d.ContainsKey("byDust") || d.ContainsKey("byWorlds"))
        {
            // Old shape: a best run per column. Read best-effort; the next upload rewrites it in the new shape.
            doc.legacy = true;
            Dictionary<string, object> rs = d.TryGetValue("byScore", out o) ? o as Dictionary<string, object> : null;
            Dictionary<string, object> rd = d.TryGetValue("byDust", out o) ? o as Dictionary<string, object> : null;
            Dictionary<string, object> rw = d.TryGetValue("byWorlds", out o) ? o as Dictionary<string, object> : null;
            if (rs != null) doc.score = L(rs, "score");
            if (rd != null) doc.dust = L(rd, "dust");
            if (rw != null) doc.worlds = (int)L(rw, "worlds");
            // Details: the first old run that knows its ship (score run, then dust run, then worlds run).
            var src = rs != null && L(rs, "ship") != 0 ? rs : rd != null && L(rd, "ship") != 0 ? rd : rw != null && L(rw, "ship") != 0 ? rw : rs;
            doc.run = DetailsFromDict(src);
        }
        else
        {
            doc.score = L(d, "score"); doc.dust = L(d, "dust"); doc.worlds = (int)L(d, "worlds");
            doc.run = DetailsFromDict(d.TryGetValue("run", out o) ? o as Dictionary<string, object> : null);
        }
        return doc.IsFull ? doc : null;
    }

    // ---- upload ----

    public void Upload(SharedTable table, string name, SharedDoc overlay, Action<SharedResult, SharedDoc> done)
    {
        if (!IsAvailable) { done(SharedResult.Error, null); return; }
        if (!IsSignedIn) { done(SharedResult.SignedOut, null); return; }
        try
        {
            var reference = db.Collection(SharedBoardLimits.Collection(table)).Document(UserId);
            db.RunTransactionAsync<SharedDoc>(tx => tx.GetSnapshotAsync(reference).ContinueWith(t =>
            {
                SharedDoc server = DocFromSnapshot(t.Result);
                bool changed;
                var merged = SharedBoardMerge.Apply(server, overlay, name, out changed, Platform);
                if (changed) tx.Set(reference, DocToDict(merged));
                return merged;
            })).ContinueWithOnMainThread(t =>
            {
                if (t.IsFaulted || t.IsCanceled) done(Classify(t.Exception), null);
                else done(SharedResult.Ok, t.Result);
            });
        }
        catch (Exception e) { done(Classify(e), null); }
    }

    static SharedResult Classify(Exception e)
    {
        while (e is AggregateException && ((AggregateException)e).InnerException != null) e = ((AggregateException)e).InnerException;
        var fe = e as FirestoreException;
        if (fe == null) return SharedResult.Error;
        switch (fe.ErrorCode)
        {
            case FirestoreError.PermissionDenied:
            case FirestoreError.Unauthenticated: return SharedResult.Denied;
            case FirestoreError.ResourceExhausted: return SharedResult.Quota;
            case FirestoreError.Unavailable:
            case FirestoreError.DeadlineExceeded: return SharedResult.Offline;
            default: return SharedResult.Error;
        }
    }

    // ---- read ----

    public void LoadTop(SharedTable table, SharedColumn column, int count, Action<LeaderboardPage> done)
    {
        if (!IsAvailable) { done(LeaderboardPage.Failed(LeaderboardStatus.Unavailable)); return; }
        if (!IsSignedIn) { done(LeaderboardPage.Failed(LeaderboardStatus.NotSignedIn)); return; }
        try
        {
            string uid = UserId;
            string collection = SharedBoardLimits.Collection(table);
            string field = SharedBoardLimits.Field(column);
            Query q = db.Collection(collection);
            // Every player is in every tab: no filter, only the order. Ties on dust / worlds fall back to the score,
            // which needs the composite indexes in firebase/firestore.indexes.json.
            q = q.OrderByDescending(field);
            if (column != SharedColumn.Score) q = q.OrderByDescending("score");
            q = q.Limit(count);
            q.GetSnapshotAsync().ContinueWithOnMainThread(t =>
            {
                if (t.IsFaulted || t.IsCanceled) { done(LeaderboardPage.Failed(Status(Classify(t.Exception)))); return; }
                var page = new LeaderboardPage { status = LeaderboardStatus.Ok };
                int rank = 1;
                foreach (var snap in t.Result.Documents)
                {
                    var doc = DocFromSnapshot(snap);
                    if (doc == null) continue;
                    var entry = SharedBoardRows.ToEntry(rank++, snap.Id, doc, column, snap.Id == uid);
                    page.entries.Add(entry);
                    if (entry.isLocalPlayer) { page.player = entry; page.hasPlayer = true; }
                }
                if (page.hasPlayer) { done(page); return; }
                // The own row outside the page: one read of the own document; no rank (no count query yet).
                db.Collection(collection).Document(uid).GetSnapshotAsync().ContinueWithOnMainThread(own =>
                {
                    var mine = own.IsFaulted || own.IsCanceled ? null : DocFromSnapshot(own.Result);
                    if (mine != null) { page.player = SharedBoardRows.ToEntry(0, uid, mine, column, true); page.hasPlayer = true; }
                    done(page);
                });
            });
        }
        catch (Exception) { done(LeaderboardPage.Failed(LeaderboardStatus.Error)); }
    }

    static LeaderboardStatus Status(SharedResult r)
    {
        return r == SharedResult.SignedOut ? LeaderboardStatus.NotSignedIn : LeaderboardStatus.Error;
    }

    // ---- delete ----

    public void DeleteMine(Action<bool, bool> done)
    {
        if (!IsSignedIn) { done(false, false); return; }
        try
        {
            string uid = UserId;
            var user = auth.CurrentUser;
            var tasks = new[]
            {
                db.Collection(SharedBoardLimits.Collection(SharedTable.Main)).Document(uid).DeleteAsync(),
                db.Collection(SharedBoardLimits.Collection(SharedTable.Power)).Document(uid).DeleteAsync(),
            };
            Task.WhenAll(tasks).ContinueWithOnMainThread(t =>
            {
                if (t.IsFaulted || t.IsCanceled) { done(false, false); return; }
                user.DeleteAsync().ContinueWithOnMainThread(d =>
                {
                    bool deleted = !d.IsFaulted && !d.IsCanceled;
                    if (!deleted) SignOut();   // a recent-login requirement can refuse it
                    done(true, deleted);
                });
            });
        }
        catch (Exception) { done(false, false); }
    }
}
#endif
