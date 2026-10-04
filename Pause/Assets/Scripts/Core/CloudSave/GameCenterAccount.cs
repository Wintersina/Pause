using System;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
using AOT;
using UnityEngine.SocialPlatforms.GameCenter;
#endif

// Game Center on iOS.
//
// Sign-in goes through Unity's built-in Game Center support (Social API), so
// SocialBridge's achievements and leaderboard work as soon as it succeeds.
// iOS decides by itself whether to show its Game Center sign-in sheet; if the
// player dismisses it, the callback just reports failure.
//
// Cloud saves use Game Center saved games (GKSavedGame) through a small native
// bridge (Plugins/iOS/PauseGameCenter.mm). They are stored in the player's
// iCloud and need the iCloud (Documents) capability next to Game Center;
// IOSCapabilitiesPostProcess adds both to the generated Xcode project. Chosen
// over the iCloud key-value store because it is scoped to the Game Center
// player the rest of the account logic keys on.
public sealed class GameCenterAccount : IPlayerAccount, IAccountProfile
{
    public const string SaveName = "pause_progress";

    public string PlatformName { get { return "Game Center"; } }

    public SignInReport LastSignIn { get; private set; }

    // Game Center has no sign-out for apps: Settings > Game Center (see AccountLink).

#if UNITY_IOS && !UNITY_EDITOR
    delegate void LoadCallback(int ok, string json);
    delegate void WriteCallback(int ok);

    [DllImport("__Internal")] static extern string PauseGC_PlayerId();
    [DllImport("__Internal")] static extern void PauseGC_LoadSave(string name, LoadCallback callback);
    [DllImport("__Internal")] static extern void PauseGC_WriteSave(string name, string json, WriteCallback callback);

    static Action<bool, string> pendingLoad;
    static Action<bool> pendingWrite;

    [MonoPInvokeCallback(typeof(LoadCallback))]
    static void OnLoaded(int ok, string json)
    {
        var done = pendingLoad;
        pendingLoad = null;
        if (done != null) done(ok != 0, json);
    }

    [MonoPInvokeCallback(typeof(WriteCallback))]
    static void OnWritten(int ok)
    {
        var done = pendingWrite;
        pendingWrite = null;
        if (done != null) done(ok != 0);
    }

    public bool IsSignedIn
    {
        get { return Social.localUser != null && Social.localUser.authenticated; }
    }

    public string PlayerId
    {
        get
        {
            if (!IsSignedIn) return null;
            string id = PauseGC_PlayerId();
            return string.IsNullOrEmpty(id) ? Social.localUser.id : id;
        }
    }

    public string DisplayName
    {
        get { return IsSignedIn ? Social.localUser.userName : null; }
    }

    // Silent and interactive are the same call: iOS shows its sign-in sheet
    // by itself when it is allowed to (not after the player dismissed it a
    // few times -- then only Settings > Game Center signs them in).
    public void SignIn(bool interactive, Action<bool> done)
    {
        if (IsSignedIn)
        {
            OnSignedIn();
            LastSignIn = SignInDiagnosis.FromGameCenter(true, null, interactive, 0f);
            if (done != null) done(true);
            return;
        }
        float startedAt = Time.realtimeSinceStartup;
        Social.localUser.Authenticate((success, error) =>
        {
            LastSignIn = SignInDiagnosis.FromGameCenter(success, error, interactive,
                Time.realtimeSinceStartup - startedAt);
            if (success) OnSignedIn();
            else Debug.Log("[Account] Game Center sign-in (" + (interactive ? "interactive" : "silent") + "): "
                           + (string.IsNullOrEmpty(error) ? "failed" : error)
                           + " -> " + LastSignIn.outcome + " code=" + LastSignIn.code);
            if (done != null) done(success);
        });
    }

    static void OnSignedIn()
    {
        // iOS shows nothing on unlock unless asked to.
        GameCenterPlatform.ShowDefaultAchievementCompletionBanner(true);
    }

    public void LoadCloudSave(Action<bool, string> done)
    {
        if (!IsSignedIn || pendingLoad != null) { done(false, null); return; }
        pendingLoad = done;
        PauseGC_LoadSave(SaveName, OnLoaded);
    }

    public void WriteCloudSave(string json, Action<bool> done)
    {
        if (!IsSignedIn || pendingWrite != null) { done(false); return; }
        pendingWrite = done;
        PauseGC_WriteSave(SaveName, json, OnWritten);
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
