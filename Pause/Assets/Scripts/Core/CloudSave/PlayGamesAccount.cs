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
// used Play Games before; Authenticate() just reports that result.
// ManuallyAuthenticate() is the fallback that may show Google's sign-in UI and
// is only used when the player taps a leaderboard/achievement button.
//
// Activating the platform also makes Unity's Social API (which SocialBridge
// uses for achievements and leaderboards) talk to Play Games.
//
// Saved Games must be switched on for the game in the Play Console, or every
// snapshot call fails (sign-in itself is unaffected).
public sealed class PlayGamesAccount : IPlayerAccount
{
    public const string SaveName = "pause_progress";

    public string PlatformName { get { return "Google Play Games"; } }

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

    public void SignIn(bool interactive, Action<bool> done)
    {
        Activate();
        Action<SignInStatus> callback = status =>
        {
            if (status != SignInStatus.Success)
                Debug.Log("[Account] Play Games sign-in: " + status);
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
    public void SignIn(bool interactive, Action<bool> done) { if (done != null) done(false); }
    public void LoadCloudSave(Action<bool, string> done) { done(false, null); }
    public void WriteCloudSave(string json, Action<bool> done) { done(false); }
#endif
}
