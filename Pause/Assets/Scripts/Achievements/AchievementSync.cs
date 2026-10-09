using System;
using System.Collections.Generic;
using UnityEngine;

// Tells the store about unlocks and progress, and catches it up later.
//
// Gameplay never waits for this. An unlock is reported the moment it happens
// when the store is available; if the pilot is signed out (or the report
// fails) nothing is lost: the achievement stays unlocked locally with no
// store mark (ach_s_<id> = 0), and ResyncAll re-reports it once the sign-in
// succeeds (SocialBridge.SignedIn / CloudSync). That fixes the old behaviour
// where one-shots reported while signed out were gone for good.
//
// Counter progress is batched: a counter change only notes the latest
// percent (NoteProgress), and FlushReports sends the queue on PrefsSaver's
// batched save (every ~10 s, and at the end of a run), the way
// the retired AchievementTiers did. Reports never repeat what the store already confirmed.
public static class AchievementSync
{
    static readonly Dictionary<AchievementDef, double> pending = new Dictionary<AchievementDef, double>();
    static readonly List<AchievementDef> flushList = new List<AchievementDef>();

    public static int PendingReports { get { return pending.Count; } }

    // ach_s_<id>: the highest percent the store confirmed (100 = unlocked there).
    public static int Confirmed(AchievementDef def) { return PlayerPrefs.GetInt(def.syncedKey, 0); }

    static void Confirm(AchievementDef def, double percent)
    {
        int p = (int)Math.Min(100.0, Math.Floor(percent + 1e-6));
        if (PlayerPrefs.GetInt(def.syncedKey, 0) >= p) return;
        PlayerPrefs.SetInt(def.syncedKey, p);
        PrefsSaver.MarkDirty();
    }

    // An achievement was just unlocked: report it now if the store can take it.
    public static void OnUnlocked(AchievementDef def)
    {
        pending.Remove(def);
        Send(def, 100.0);
    }

    // A counter moved but did not finish: remember the latest percent for the next flush.
    public static void NoteProgress(AchievementDef def)
    {
        if (def.storeSteps <= 0) return;
        var store = AchievementStores.Current;
        if (!store.CanReport(def)) return;
        pending[def] = AchievementStore.Percent(def);
    }

    // Sends the queued progress (PrefsSaver.SaveNow).
    public static void FlushReports()
    {
        if (pending.Count == 0) return;
        var store = AchievementStores.Current;
        if (!store.Available) return;   // kept: the sign-in resync covers it
        flushList.Clear();
        foreach (var pair in pending) flushList.Add(pair.Key);
        pending.Clear();
        foreach (var def in flushList)
            if (!AchievementStore.IsUnlocked(def)) Send(def, AchievementStore.Percent(def));
    }

    static void Send(AchievementDef def, double percent)
    {
        var store = AchievementStores.Current;
        if (!store.Available || !store.CanReport(def)) return;
        if (Confirmed(def) >= (int)Math.Min(100.0, Math.Floor(percent + 1e-6))) return;
        store.Report(def, percent, ok => { if (ok) Confirm(def, percent); });
    }

    // After a successful sign-in: every unlocked achievement the store has not
    // confirmed, and the progress of every incremental one. Returns what it sent.
    public static List<KeyValuePair<string, double>> ResyncAll()
    {
        var sent = new List<KeyValuePair<string, double>>();
        var store = AchievementStores.Current;
        if (!store.Available) return sent;
        foreach (var def in AchievementCatalog.All)
        {
            if (!AchievementCatalog.IsActive(def) || !store.CanReport(def)) continue;
            if (AchievementStore.IsUnlocked(def))
            {
                if (Confirmed(def) >= 100) continue;
                sent.Add(new KeyValuePair<string, double>(def.id, 100.0));
                Send(def, 100.0);
            }
            else if (def.storeSteps > 0)
            {
                double percent = AchievementStore.Percent(def);
                if (percent <= 0.0 || Confirmed(def) >= (int)Math.Floor(percent + 1e-6)) continue;
                sent.Add(new KeyValuePair<string, double>(def.id, percent));
                Send(def, percent);
            }
        }
        pending.Clear();
        return sent;
    }

    // A different player signed in: nothing is confirmed for them yet.
    public static void ClearSyncMarks()
    {
        pending.Clear();
        foreach (var def in AchievementCatalog.All) PlayerPrefs.DeleteKey(def.syncedKey);
    }
}
