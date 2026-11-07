using System;
using UnityEngine;

// What a write or read against the shared board came to. Every failure leaves the native boards untouched.
public enum SharedResult
{
    Ok,
    Offline,      // no network, deadline exceeded, service unavailable
    SignedOut,    // no Firebase user
    Denied,       // permission denied: the rules refused it, or App Check did
    Quota,        // the free daily quota is spent (resets about midnight Pacific)
    Error,        // anything else
}

// One backend for the shared leaderboard: Firebase in a build with the SDK (FirebaseSharedBoard, behind
// PAUSE_FIREBASE), NullSharedBoard otherwise, FakeSharedBoard in tests. Callbacks arrive on the main thread and
// fire exactly once, also on failure. Nothing here may throw into gameplay.
public interface ISharedBoardBackend
{
    string BackendName { get; }

    // The SDK is compiled in, its config file is present and it initialised.
    bool IsAvailable { get; }

    // A Firebase user exists (not the Play Games / Game Center account itself).
    bool IsSignedIn { get; }

    // The Firebase uid, or null.
    string UserId { get; }

    // Signs in to Firebase with the platform account that is already signed in (Play Games server auth code,
    // or the Game Center signature). Never shows UI.
    void SignIn(Action<bool> done);

    void SignOut();

    // A transaction on the player's own document: read it, SharedBoardMerge.Apply the overlay, write when
    // something improved. `doc` is the document as it stands afterwards (null when the call failed).
    void Upload(SharedTable table, string name, SharedDoc overlay, Action<SharedResult, SharedDoc> done);

    // The top `count` rows by one column, plus the signed-in player's own row (page.player) when known.
    // A row outside the page has rank 0 (no count query).
    void LoadTop(SharedTable table, SharedColumn column, int count, Action<LeaderboardPage> done);

    // Deletes the player's documents in both tables, then the Firebase user. docsDeleted is false when the
    // documents could not be removed (then nothing else happens); authDeleted is false when the user could
    // not be deleted (it is signed out instead).
    void DeleteMine(Action<bool, bool> done);
}

public static class SharedBoardBackends
{
    public static ISharedBoardBackend CreateForPlatform()
    {
#if PAUSE_FIREBASE
        if (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer)
            return new FirebaseSharedBoard();
#endif
        return new NullSharedBoard();
    }
}

// Editor, Mac, a build without the SDK or its config file: never available.
public sealed class NullSharedBoard : ISharedBoardBackend
{
    public string BackendName { get { return "none"; } }
    public bool IsAvailable { get { return false; } }
    public bool IsSignedIn { get { return false; } }
    public string UserId { get { return null; } }
    public void SignIn(Action<bool> done) { if (done != null) done(false); }
    public void SignOut() { }
    public void Upload(SharedTable table, string name, SharedDoc overlay, Action<SharedResult, SharedDoc> done) { if (done != null) done(SharedResult.Error, null); }
    public void LoadTop(SharedTable table, SharedColumn column, int count, Action<LeaderboardPage> done)
    {
        if (done != null) done(LeaderboardPage.Failed(LeaderboardStatus.Unavailable));
    }
    public void DeleteMine(Action<bool, bool> done) { if (done != null) done(false, false); }
}

// Turns a stored document into the row type the panel already draws.
public static class SharedBoardRows
{
    public static LeaderboardEntry ToEntry(int rank, string uid, SharedDoc doc, SharedColumn column, bool isLocal)
    {
        var r = doc.Get(column);
        bool premium = PremiumShips.IsPremium(r.ship);   // never shown, as a premium tag read from a native board
        return new LeaderboardEntry
        {
            rank = rank,
            playerId = uid,
            playerName = doc.name,
            value = r.Metric(column),
            isLocalPlayer = isLocal,
            startShip = premium ? 0 : r.ship,
            startHearts = premium ? 0 : r.hearts,
            startSkin = premium ? 0 : r.skin,
            hasEnd = r.endHearts >= 0,
            endHearts = Math.Max(0, r.endHearts),
            hasPauses = r.pauses >= 0,
            pauses = Math.Max(0, r.pauses),
            hasRunStats = true,
            starDustHundredths = r.dust,
            worlds = r.worlds,
            powerUps = r.powerUps,
            hasScore = true,
            runScore = r.score,
        };
    }
}
