using System;
using System.Collections.Generic;
using UnityEngine;

// Keeps the player's progress in their platform account's cloud save.
//
// Launch: silent sign-in. Failure or cancel -> one log line, nothing else;
// the game keeps playing on local PlayerPrefs exactly as before.
//
// Signed in: the account's cloud save is read and reconciled with local
// progress (Reconcile below), the result is written to PlayerPrefs and
// uploaded. From then on local changes are detected by polling (cheap: the
// progress is ~30 prefs) and uploaded, debounced: at most every
// UploadIntervalMs in general, sooner on a scene change (end of a run, leaving
// the shop) and immediately when the app is backgrounded.
//
// Local PlayerPrefs stay the source of truth: nothing local is touched until
// a cloud save was actually read, and nothing is uploaded before that either,
// so a failed read can never overwrite a better cloud save. A cloud save from
// a newer schema version is left alone (no uploads that session).
//
// Accounts. The id of the last account whose save was merged here is kept in
// cloudSave_lastAccountId.
//   - first sign-in on this device, or the same account again: local and
//     cloud are merged with ProgressMerge (so progress made before ever
//     signing in is kept and attached to the account).
//   - a DIFFERENT account than last time: that account's progress wins.
//     The device's current progress is parked in a local backup under the
//     previous account's id, and the new account gets its cloud save (merged
//     with its own parked backup, if this device has one). An account with no
//     cloud save and no backup starts fresh -- it never inherits someone
//     else's ships or star dust. Switching back restores the parked progress.
public sealed class CloudSync
{
    public const string LastAccountKey = "cloudSave_lastAccountId";
    public const string LocalSavedAtKey = "cloudSave_localSavedAtUtc";
    public const string LocalHashKey = "cloudSave_localHash";
    public const string BackupsKey = "cloudSave_accountBackups";

    public const long UploadIntervalMs = 30000;
    public const long UrgentUploadIntervalMs = 5000;
    public const int MaxBackups = 4;

    public enum State { Idle, SigningIn, Offline, Loading, Ready, ReadOnly }

    public static CloudSync Instance { get; private set; }

    readonly IPlayerAccount account;
    readonly Func<long> clock;

    public State Current { get; private set; }
    public int UploadCount { get; private set; }

    bool hasPending;
    ProgressSnapshot pendingCloud;
    bool dirty;
    bool uploading;
    long lastUploadAt = long.MinValue / 2;

    // Stops reconcile from being applied mid-run (the star dust ledger and
    // WorldManager hold their own copy of those values). Tests override it.
    public Func<bool> CanApplyNow = () => !StarDustLedger.IsActive;

    // Re-reports achievement progress after sign-in; tests substitute a fake.
    public AchievementSync.Reporter AchievementReporter;

    public CloudSync(IPlayerAccount account, Func<long> clock = null)
    {
        this.account = account;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public static CloudSync Launch(IPlayerAccount account)
    {
        Instance = new CloudSync(account);
        Instance.Start();
        return Instance;
    }

    public void Start()
    {
        RecordLocalChanges();
        Current = State.SigningIn;
        account.SignIn(false, OnSignedIn);
    }

    // Interactive sign-in (leaderboard button); runs the same flow on success.
    public void SignInInteractive(Action<bool> done)
    {
        if (account.IsSignedIn) { if (done != null) done(true); return; }
        account.SignIn(true, ok =>
        {
            OnSignedIn(ok);
            if (done != null) done(ok);
        });
    }

    void OnSignedIn(bool ok)
    {
        if (!ok)
        {
            Current = State.Offline;
            Debug.Log("[CloudSave] not signed in to " + account.PlatformName + "; playing with local saves only.");
            return;
        }
        SocialBridge.NotifySignedIn();
        if (Current == State.Loading || Current == State.Ready) return;
        Debug.Log("[CloudSave] signed in to " + account.PlatformName + ".");
        achievementAPICalls.achievement_logged_on_successfully();
        Current = State.Loading;
        account.LoadCloudSave(OnCloudLoaded);
    }

    void OnCloudLoaded(bool ok, string json)
    {
        if (!ok)
        {
            Current = State.ReadOnly;
            Debug.Log("[CloudSave] cloud save unavailable; keeping local progress, not uploading this session.");
            ResyncAchievements();
            return;
        }
        ProgressSnapshot cloud;
        var parse = ProgressSnapshot.TryParse(json, out cloud);
        if (parse == ProgressSnapshot.ParseResult.Invalid || parse == ProgressSnapshot.ParseResult.NewerSchema)
        {
            Current = State.ReadOnly;
            Debug.Log("[CloudSave] cloud save is " + (parse == ProgressSnapshot.ParseResult.NewerSchema
                ? "from a newer version" : "unreadable") + "; leaving it untouched.");
            ResyncAchievements();
            return;
        }
        pendingCloud = cloud;   // null: the account has no save yet
        hasPending = true;
        TryApplyPending();
    }

    void TryApplyPending()
    {
        if (!hasPending || !CanApplyNow()) return;
        hasPending = false;

        long localAt = LocalSavedAt();
        var local = ProgressSnapshot.Capture(localAt);
        string id = account.PlayerId;
        string last = PlayerPrefs.GetString(LastAccountKey, "");
        var backups = AccountBackups.Load();
        bool switched;
        var result = Reconcile(local, pendingCloud, id, last, backups, clock(), out switched);
        backups.Save();

        result.Apply();
        if (switched) AchievementSync.ClearSyncMarks();
        if (!string.IsNullOrEmpty(id)) PlayerPrefs.SetString(LastAccountKey, id);
        PlayerPrefs.SetString(LocalSavedAtKey, result.savedAtUtc.ToString());
        PlayerPrefs.SetString(LocalHashKey, Hash(ProgressSnapshot.Capture(0).ContentKey()));
        PrefsSaver.SaveNow();

        dirty = pendingCloud == null || pendingCloud.ContentKey() != ProgressSnapshot.Capture(0).ContentKey()
                || pendingCloud.savedAtUtc != result.savedAtUtc;
        pendingCloud = null;
        Current = State.Ready;
        Debug.Log("[CloudSave] " + (switched ? "switched account; loaded its progress." : "merged cloud and local progress."));
        ResyncAchievements();
    }

    void ResyncAchievements()
    {
        AchievementSync.ResyncAll(AchievementReporter);
    }

    // The account rule (see the header). Pure apart from `backups`.
    public static ProgressSnapshot Reconcile(ProgressSnapshot local, ProgressSnapshot cloud,
        string accountId, string lastAccountId, AccountBackups backups, long now, out bool switched)
    {
        switched = !string.IsNullOrEmpty(lastAccountId) && !string.IsNullOrEmpty(accountId)
                   && lastAccountId != accountId;
        if (!switched)
            return cloud == null ? local : ProgressMerge.Merge(local, cloud);

        backups.Put(lastAccountId, local);
        var parked = backups.Take(accountId);
        var result = ProgressMerge.Merge(parked, cloud);
        return result ?? ProgressSnapshot.Fresh(now);
    }

    // ---- change detection and upload ----

    // Called by the runner: regularly (urgent = false), on scene changes and
    // app pause (urgent = true, force = true for pause).
    public void Poll(bool urgent = false, bool force = false)
    {
        if (hasPending) TryApplyPending();
        RecordLocalChanges();
        if (Current != State.Ready || !dirty || uploading) return;
        long now = clock();
        if (!force)
        {
            if (!urgent && !CanApplyNow()) return;   // mid-run: wait for the end of the run or a pause
            if (now - lastUploadAt < (urgent ? UrgentUploadIntervalMs : UploadIntervalMs)) return;
        }
        Upload(now);
    }

    void Upload(long now)
    {
        var snapshot = ProgressSnapshot.Capture(LocalSavedAt());
        string key = snapshot.ContentKey();
        uploading = true;
        lastUploadAt = now;
        UploadCount++;
        account.WriteCloudSave(snapshot.ToJson(), ok =>
        {
            uploading = false;
            if (!ok) { Debug.Log("[CloudSave] upload failed; will retry."); return; }
            if (ProgressSnapshot.Capture(0).ContentKey() == key) dirty = false;
        });
    }

    // Stamps local progress with the time it last changed.
    void RecordLocalChanges()
    {
        string hash = Hash(ProgressSnapshot.Capture(0).ContentKey());
        if (hash == PlayerPrefs.GetString(LocalHashKey, "")) return;
        PlayerPrefs.SetString(LocalHashKey, hash);
        PlayerPrefs.SetString(LocalSavedAtKey, clock().ToString());
        PrefsSaver.MarkDirty();
        dirty = true;
    }

    public static long LocalSavedAt()
    {
        long at;
        return long.TryParse(PlayerPrefs.GetString(LocalSavedAtKey, "0"), out at) ? at : 0;
    }

    // FNV-1a 64; stable across runs and platforms, unlike string.GetHashCode.
    static string Hash(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
        return h.ToString("x16");
    }
}

// Progress parked on this device per account id (see CloudSync header).
[Serializable]
public sealed class AccountBackups
{
    [Serializable]
    public struct Entry
    {
        public string accountId;
        public string json;
    }

    public List<Entry> entries = new List<Entry>();

    public static AccountBackups Load()
    {
        string json = PlayerPrefs.GetString(CloudSync.BackupsKey, "");
        if (string.IsNullOrEmpty(json)) return new AccountBackups();
        try
        {
            var b = JsonUtility.FromJson<AccountBackups>(json);
            if (b != null && b.entries != null) return b;
        }
        catch (Exception) { }
        return new AccountBackups();
    }

    public void Save()
    {
        if (entries.Count == 0) PlayerPrefs.DeleteKey(CloudSync.BackupsKey);
        else PlayerPrefs.SetString(CloudSync.BackupsKey, JsonUtility.ToJson(this));
    }

    public void Put(string accountId, ProgressSnapshot snapshot)
    {
        entries.RemoveAll(e => e.accountId == accountId);
        entries.Add(new Entry { accountId = accountId, json = snapshot.ToJson() });
        while (entries.Count > CloudSync.MaxBackups) entries.RemoveAt(0);
    }

    public ProgressSnapshot Take(string accountId)
    {
        int i = entries.FindIndex(e => e.accountId == accountId);
        if (i < 0) return null;
        string json = entries[i].json;
        entries.RemoveAt(i);
        ProgressSnapshot snapshot;
        return ProgressSnapshot.TryParse(json, out snapshot) == ProgressSnapshot.ParseResult.Ok ? snapshot : null;
    }
}
