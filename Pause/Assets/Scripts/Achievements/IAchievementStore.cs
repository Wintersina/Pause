using System;
using UnityEngine;

// One place achievements are reported to (Google Play Games, Game Center, or
// nowhere). The game's own state is always local (AchievementStore); a store
// is only told.
public interface IAchievementStore
{
    string Name { get; }
    // Signed in and able to take a report right now.
    bool Available { get; }
    // This achievement has a real (non-placeholder) id on this store.
    bool CanReport(AchievementDef def);
    // `percent` 0..100 (100 unlocks). `done(true)` once the store accepted it.
    void Report(AchievementDef def, double percent, Action<bool> done);
}

// The editor, other platforms, and the fallback while ids are placeholders.
public sealed class NullAchievementStore : IAchievementStore
{
    public string Name { get { return "none"; } }
    public bool Available { get { return false; } }
    public bool CanReport(AchievementDef def) { return false; }
    public void Report(AchievementDef def, double percent, Action<bool> done) { if (done != null) done(false); }
}

// Play Games (Android) and Game Center (iOS) both go through Unity's Social
// API (SocialBridge); they differ only in which id they send.
public abstract class SocialAchievementStore : IAchievementStore
{
    protected abstract bool IsIos { get; }
    public abstract string Name { get; }

    public bool Available { get { return SocialBridge.IsAuthenticated; } }

    public bool CanReport(AchievementDef def) { return AchievementIds.IsReportable(def, IsIos); }

    public void Report(AchievementDef def, double percent, Action<bool> done)
    {
        if (!CanReport(def)) { if (done != null) done(false); return; }
        string id = IsIos ? AchievementIds.IosId(def) : AchievementIds.AndroidId(def);
        SocialBridge.ReportProgress(id, percent, ok => { if (done != null) done(ok); });
    }
}

public sealed class PlayGamesAchievementStore : SocialAchievementStore
{
    protected override bool IsIos { get { return false; } }
    public override string Name { get { return "Google Play Games"; } }
}

public sealed class GameCenterAchievementStore : SocialAchievementStore
{
    protected override bool IsIos { get { return true; } }
    public override string Name { get { return "Game Center"; } }
}

public static class AchievementStores
{
    static IAchievementStore current;

    // The store for this platform; the editor (and any other platform) gets the no-op.
    // Tests substitute a fake.
    public static IAchievementStore Current
    {
        get
        {
            if (current != null) return current;
#if UNITY_EDITOR
            return current = new NullAchievementStore();
#elif UNITY_ANDROID
            return current = new PlayGamesAchievementStore();
#elif UNITY_IOS
            return current = new GameCenterAchievementStore();
#else
            return current = new NullAchievementStore();
#endif
        }
        set { current = value; }
    }
}
