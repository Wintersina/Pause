using System;
using UnityEngine;

// The platform's player account: sign-in plus one cloud save slot.
//
//   Android -> Google Play Games Services v2 (PlayGamesAccount)
//   iOS     -> Game Center (GameCenterAccount)
//   others  -> NullAccount (editor, Mac, ...): never signs in
//
// Callbacks arrive on the main thread.
public interface IPlayerAccount
{
    string PlatformName { get; }
    bool IsSignedIn { get; }

    // Stable id of the signed-in player, or null.
    string PlayerId { get; }

    // interactive == false must never show UI of its own.
    void SignIn(bool interactive, Action<bool> done);

    // ok == false: the save could not be read (offline, error).
    // ok == true with a null/empty json: the account has no save yet.
    void LoadCloudSave(Action<bool, string> done);

    void WriteCloudSave(string json, Action<bool> done);
}

public static class PlayerAccounts
{
    static IPlayerAccount current;

    public static IPlayerAccount Current
    {
        get
        {
            if (current == null) current = CreateForPlatform();
            return current;
        }
        // Tests substitute a fake.
        set { current = value; }
    }

    static IPlayerAccount CreateForPlatform()
    {
        switch (Application.platform)
        {
            case RuntimePlatform.Android: return new PlayGamesAccount();
            case RuntimePlatform.IPhonePlayer: return new GameCenterAccount();
            default: return new NullAccount();
        }
    }
}

public sealed class NullAccount : IPlayerAccount
{
    public string PlatformName { get { return "none"; } }
    public bool IsSignedIn { get { return false; } }
    public string PlayerId { get { return null; } }
    public void SignIn(bool interactive, Action<bool> done) { if (done != null) done(false); }
    public void LoadCloudSave(Action<bool, string> done) { if (done != null) done(false, null); }
    public void WriteCloudSave(string json, Action<bool> done) { if (done != null) done(false); }
}
