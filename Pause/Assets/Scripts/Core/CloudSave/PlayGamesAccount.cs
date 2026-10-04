using System;
using System.Text;
using UnityEngine;
#if UNITY_ANDROID
using GooglePlayGames;
using GooglePlayGames.BasicApi;
using GooglePlayGames.BasicApi.SavedGame;
#endif

// Google Play Games Services v2 (play-games-plugin-for-unity 2.x).
//
// v2 signs the player in automatically and silently at launch when they have
// used Play Games before; Authenticate() just reports that result. A player
// who never signed in to this game gets SIGN_IN_REQUIRED there (v2 suppresses
// its own prompt), so ManuallyAuthenticate() -- which shows Google's sign-in
// UI -- runs when the player taps SIGN IN (AccountLink: the Options Account
// row, the leaderboard panel, the store screens).
//
// v2 has no sign-out API (PlayGamesPlatform.SignOut was removed); see
// AccountLink for Pause's own SIGN OUT.
//
// Activating the platform also makes Unity's Social API (which SocialBridge
// uses for achievements and leaderboards) talk to Play Games.
//
// Saved Games must be switched on for the game in the Play Console, or every
// snapshot call fails (sign-in itself is unaffected).
public sealed class PlayGamesAccount : IPlayerAccount, IAccountProfile
{
    public const string SaveName = "pause_progress";

    public string PlatformName { get { return "Google Play Games"; } }

    public SignInReport LastSignIn { get; private set; }

#if UNITY_ANDROID
    static bool activated;

    static void Activate()
    {
        if (activated) return;
        PlayGamesPlatform.Activate();
        activated = true;
    }

    public bool IsSignedIn
    {
        get { return activated && PlayGamesPlatform.Instance.IsAuthenticated(); }
    }

    public string PlayerId
    {
        get { return IsSignedIn ? PlayGamesPlatform.Instance.GetUserId() : null; }
    }

    public string DisplayName
    {
        get { return IsSignedIn ? PlayGamesPlatform.Instance.GetUserDisplayName() : null; }
    }

    public void SignIn(bool interactive, Action<bool> done)
    {
        Activate();
        // The plugin reports only Success / Canceled / InternalError; the
        // status code is in its own log line, which arrives just before the
        // callback. Listen while the attempt runs (SignInDiagnosis).
        string pluginLog = null;
        bool promptShown = false;
        float startedAt = Time.realtimeSinceStartup;
        Application.LogCallback listen = (message, stack, type) =>
        {
            if (message == null) return;
            int at = message.IndexOf("Authentication failed", StringComparison.Ordinal);
            if (at < 0) at = message.IndexOf("Returning an error code", StringComparison.Ordinal);
            if (at >= 0) pluginLog = message.Substring(at).Trim();
        };
        Action<bool> focus = hasFocus => { if (!hasFocus) promptShown = true; };
        Application.logMessageReceived += listen;
        Application.focusChanged += focus;

        Action<SignInStatus> callback = status =>
        {
            Application.logMessageReceived -= listen;
            Application.focusChanged -= focus;
            LastSignIn = SignInDiagnosis.FromPlayGames(status.ToString(), interactive, pluginLog,
                Time.realtimeSinceStartup - startedAt, promptShown);
            if (status != SignInStatus.Success)
                Debug.Log("[Account] Play Games sign-in (" + (interactive ? "interactive" : "silent") + "): "
                          + status + " -> " + LastSignIn.outcome + " code=" + LastSignIn.code
                          + " prompt=" + promptShown + " detail=" + LastSignIn.detail);
            if (done != null) done(status == SignInStatus.Success);
        };
        if (interactive) PlayGamesPlatform.Instance.ManuallyAuthenticate(callback);
        else PlayGamesPlatform.Instance.Authenticate(callback);
    }

    static void Open(Action<ISavedGameMetadata> onOpen, Action onFail)
    {
        PlayGamesPlatform.Instance.SavedGame.OpenWithAutomaticConflictResolution(
            SaveName, DataSource.ReadCacheOrNetwork, ConflictResolutionStrategy.UseMostRecentlySaved,
            (status, metadata) =>
            {
                if (status == SavedGameRequestStatus.Success && metadata != null) onOpen(metadata);
                else
                {
                    Debug.Log("[CloudSave] Play Games snapshot open failed: " + status);
                    onFail();
                }
            });
    }

    public void LoadCloudSave(Action<bool, string> done)
    {
        if (!IsSignedIn) { done(false, null); return; }
        Open(metadata =>
        {
            PlayGamesPlatform.Instance.SavedGame.ReadBinaryData(metadata, (status, data) =>
            {
                if (status != SavedGameRequestStatus.Success) { done(false, null); return; }
                // A snapshot that was just created reads back empty.
                done(true, data == null || data.Length == 0 ? null : Encoding.UTF8.GetString(data));
            });
        }, () => done(false, null));
    }

    public void WriteCloudSave(string json, Action<bool> done)
    {
        if (!IsSignedIn) { done(false); return; }
        byte[] data = Encoding.UTF8.GetBytes(json);
        Open(metadata =>
        {
            var update = new SavedGameMetadataUpdate.Builder()
                .WithUpdatedDescription("Pause progress")
                .Build();
            PlayGamesPlatform.Instance.SavedGame.CommitUpdate(metadata, update, data,
                (status, _) => done(status == SavedGameRequestStatus.Success));
        }, () => done(false));
    }
#else
    public bool IsSignedIn { get { return false; } }
    public string PlayerId { get { return null; } }
    public string DisplayName { get { return null; } }
    public void SignIn(bool interactive, Action<bool> done) { if (done != null) done(false); }
    public void LoadCloudSave(Action<bool, string> done) { done(false, null); }
    public void WriteCloudSave(string json, Action<bool> done) { done(false); }
#endif
}
