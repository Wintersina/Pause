using System;
using UnityEngine;

// The player's link between Pause and their store account (Google Play Games
// on Android, Game Center on iOS), as the Options screen's Account row shows
// it. One place for: the interactive sign-in, the last sign-in result (and
// why it failed), and Pause's own "signed out" switch.
//
// Sign-in
//   - Launch: one SILENT attempt (CloudSync.Start). Play Games v2 answers
//     SIGN_IN_REQUIRED for a player who never signed in to this game and
//     suppresses its own prompt, so the player must be offered a button:
//     the Account row in Options, plus a one-time hint toast on the home
//     screen (AccountHintToast).
//   - SignIn(): the INTERACTIVE attempt (PlayGamesPlatform.ManuallyAuthenticate
//     / Game Center's authenticate sheet). Every "sign in" in the game goes
//     through here: the Account row, the leaderboard panel's SIGN IN and the
//     store screens (SocialBridge.Authenticate).
//
// Signing out
//   Neither store lets an app sign the player out any more: Play Games
//   Services v2 removed SignOut() (the installed plugin, 2.1.0, has none) and
//   Game Center never had one (iOS Settings > Game Center). So SIGN OUT is
//   Pause's own disconnect: DisconnectedKey is stored, and while it is set
//   Pause does not use the account at all -- no silent sign-in at launch, no
//   cloud save reads or writes, no achievement or leaderboard reports. Local
//   progress stays on the device, and the per-account rules in CloudSync
//   (cloudSave_lastAccountId, parked backups) are untouched, so signing in
//   again -- with the same or another account -- follows them as before.
//   Any interactive sign-in clears the switch.
public static class AccountLink
{
    public const string DisconnectedKey = "accountDisconnected";
    // Set once the home screen showed "sign in in Options" (once per install).
    public const string HintShownKey = "account_signInHintShown";

    public enum Status
    {
        Unavailable,    // no store account on this platform (editor, Mac)
        SignedOut,      // never signed in, or the silent attempt failed
        SigningIn,      // an interactive attempt is running
        SignedIn,
        Failed,         // the last interactive attempt failed (LastReport says why)
        Disconnected,   // the player chose SIGN OUT in Pause
    }

    // Raised on every change the Account row should redraw for.
    public static event Action Changed;

    static bool signingIn;
    static SignInReport lastReport;
    static bool silentFailed;
    static IPlayerAccount accountOverride;

    // Interactive attempts actually handed to the platform (tests, diagnostics).
    public static int InteractiveAttempts { get; private set; }

    public static SignInReport LastReport { get { return lastReport; } }

    // The account Pause uses: CloudSync's, else the platform's.
    public static IPlayerAccount Account
    {
        get
        {
            if (accountOverride != null) return accountOverride;
            if (CloudSync.Instance != null) return CloudSync.Instance.Account;
            return PlayerAccounts.Current;
        }
        // Tests that drive AccountLink without a CloudSync.
        set { accountOverride = value; }
    }

    public static bool Available
    {
        get
        {
            var account = Account;
            return account != null && !(account is NullAccount) && account.PlatformName != "none";
        }
    }

    public static bool Disconnected
    {
        get { return PlayerPrefs.GetInt(DisconnectedKey, 0) == 1; }
    }

    // Signed in AND not switched off in Pause: what every account use checks.
    public static bool Connected
    {
        get { return !Disconnected && Account != null && Account.IsSignedIn; }
    }

    public static bool SigningIn { get { return signingIn; } }

    public static Status Current
    {
        get
        {
            if (!Available) return Status.Unavailable;
            if (signingIn) return Status.SigningIn;
            if (Disconnected) return Status.Disconnected;
            if (Account.IsSignedIn) return Status.SignedIn;
            if (lastReport.interactive && lastReport.outcome != SignInOutcome.None &&
                lastReport.outcome != SignInOutcome.Success) return Status.Failed;
            return Status.SignedOut;
        }
    }

    public static string DisplayName
    {
        get
        {
            var profile = Account as IAccountProfile;
            string name = profile != null && Account.IsSignedIn ? profile.DisplayName : null;
            return string.IsNullOrEmpty(name) ? "PILOT" : name;
        }
    }

    public static string Initial(string name)
    {
        if (string.IsNullOrEmpty(name)) return "?";
        foreach (char c in name)
            if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c).ToString();
        return "?";
    }

    // "Google Play Games" / "Game Center".
    public static string StoreName
    {
        get { return Available ? Account.PlatformName : "your game account"; }
    }

    public static bool IsGameCenter
    {
        get { return Account is GameCenterAccount || AccountPlatformOverride == "ios"; }
    }

    // Tests pick which store's wording to check ("android" / "ios").
    public static string AccountPlatformOverride;

    // ---- sign in ----

    // The interactive sign-in. A second tap while one is running is ignored
    // (its callback still gets the answer). Clears the Disconnected switch.
    public static void SignIn(Action<bool> done = null)
    {
        if (!Available)
        {
            if (done != null) done(false);
            return;
        }
        if (signingIn)
        {
            if (done != null) pendingCallbacks += done;
            return;
        }
        SetDisconnected(false);

        var account = Account;
        if (account.IsSignedIn && CloudSync.Instance == null)
        {
            // Already signed in to the platform (e.g. reconnecting after SIGN
            // OUT): just use it again.
            Finish(true, true, account, done);
            return;
        }

        signingIn = true;
        InteractiveAttempts++;
        RaiseChanged();
        Debug.Log("[Account] interactive sign-in to " + account.PlatformName + "...");
        Action<bool> finished = ok =>
        {
            signingIn = false;
            var callbacks = pendingCallbacks;
            pendingCallbacks = null;
            if (done != null) done(ok);
            if (callbacks != null) callbacks(ok);
            RaiseChanged();
        };
        if (CloudSync.Instance != null && CloudSync.Instance.Account == account)
        {
            // CloudSync reports back through OnSignInResult and raises SignedIn.
            CloudSync.Instance.SignInInteractive(finished);
        }
        else
        {
            account.SignIn(true, ok =>
            {
                OnSignInResult(true, ok, account);
                if (ok) SocialBridge.NotifySignedIn();
                finished(ok);
            });
        }
    }

    static Action<bool> pendingCallbacks;

    static void Finish(bool interactive, bool ok, IPlayerAccount account, Action<bool> done)
    {
        OnSignInResult(interactive, ok, account);
        if (ok) SocialBridge.NotifySignedIn();
        if (done != null) done(ok);
        RaiseChanged();
    }

    // Every sign-in result, silent or interactive (CloudSync calls this).
    public static void OnSignInResult(bool interactive, bool ok, IPlayerAccount account)
    {
        var diag = account as IAccountProfile;
        SignInReport report = diag != null ? diag.LastSignIn : default(SignInReport);
        if (report.outcome == SignInOutcome.None)
            report = SignInReport.Of(ok ? SignInOutcome.Success
                : interactive ? SignInOutcome.Failed : SignInOutcome.SignInRequired, interactive,
                ok ? "SUCCESS" : interactive ? "FAILED" : "NOT_SIGNED_IN", "");
        if (ok) report.outcome = SignInOutcome.Success;
        report.interactive = interactive;
        lastReport = report;
        if (!interactive) silentFailed = !ok;
        else if (ok) silentFailed = false;

        Debug.Log("[Account] " + (interactive ? "interactive" : "silent") + " sign-in to "
                  + (account != null ? account.PlatformName : "?") + ": " + report.outcome
                  + " code=" + report.code + (string.IsNullOrEmpty(report.detail) ? "" : " detail=" + report.detail));
        RaiseChanged();
    }

    // ---- sign out (Pause's disconnect) ----

    public static void Disconnect()
    {
        if (CloudSync.Instance != null) CloudSync.Instance.Disconnect();
        SetDisconnected(true);
        silentFailed = false;
        Debug.Log("[Account] signed out of " + StoreName + " in Pause: no cloud save, achievements or "
                  + "leaderboards until the player signs in again. Local progress kept.");
        RaiseChanged();
    }

    static void SetDisconnected(bool on)
    {
        if (on == Disconnected) return;
        if (on) PlayerPrefs.SetInt(DisconnectedKey, 1);
        else PlayerPrefs.DeleteKey(DisconnectedKey);
        PrefsSaver.SaveNow();
    }

    // ---- the home screen hint ----

    // True when the home screen should show "sign in in Options" now: the
    // silent launch sign-in failed this launch, the player did not choose
    // SIGN OUT, and the hint was never shown on this install.
    public static bool ShouldShowLaunchHint
    {
        get
        {
            return silentFailed && Available && !Disconnected && !Account.IsSignedIn
                   && PlayerPrefs.GetInt(HintShownKey, 0) == 0;
        }
    }

    public static void MarkLaunchHintShown()
    {
        PlayerPrefs.SetInt(HintShownKey, 1);
        PrefsSaver.SaveNow();
    }

    public static string LaunchHintText
    {
        get
        {
            return IsGameCenter
                ? "Sign in to Game Center in Options to save progress to your account"
                : "Sign in with Play Games in Options to save progress to your account";
        }
    }

    // ---- wording ----

    public static string SignInLabel
    {
        get { return IsGameCenter ? "SIGN IN TO GAME CENTER" : "SIGN IN WITH GOOGLE PLAY GAMES"; }
    }

    // Short reason for a failed interactive sign-in.
    public static string FailureReason(SignInReport report)
    {
        switch (report.outcome)
        {
            case SignInOutcome.Canceled: return "Sign-in was cancelled.";
            case SignInOutcome.NetworkError: return "No connection. Try again when you're online.";
            case SignInOutcome.ConfigError:
                return IsGameCenter ? "Game Center isn't available for this build."
                                    : "Play Games couldn't sign in to this build.";
            case SignInOutcome.Disabled:
                return "Game Center sign-in is turned off for this app.";
            case SignInOutcome.SignInRequired: return "Play Games didn't sign you in.";
            default: return "Something went wrong. Try again.";
        }
    }

    // The one-line hint for failures the player can't fix by tapping again.
    public static string FailureHint(SignInReport report)
    {
        switch (report.outcome)
        {
            case SignInOutcome.ConfigError:
                return IsGameCenter ? "Game Center isn't set up for this build yet."
                                    : "Play Games isn't set up for this build yet.";
            case SignInOutcome.Disabled:
                return "Sign in from Settings > Game Center, then come back.";
            default: return null;
        }
    }

    // One line for the developer-mode details row.
    public static string DetailsLine
    {
        get
        {
            var r = lastReport;
            if (r.outcome == SignInOutcome.None) return "LAST SIGN-IN: none yet";
            string line = "LAST SIGN-IN: " + (r.interactive ? "interactive " : "silent ") + r.code
                          + " (" + r.outcome + ")";
            if (r.seconds > 0f) line += " " + r.seconds.ToString("0.0") + "s";
            if (r.interactive && !r.promptShown && r.outcome != SignInOutcome.Success) line += " no prompt";
            if (!string.IsNullOrEmpty(r.detail)) line += " | " + Truncate(r.detail, 90);
            string cert = AccountSettingsLink.SigningCertificateSha1();
            if (!string.IsNullOrEmpty(cert)) line += " | cert SHA-1 " + cert;
            return line;
        }
    }

    static string Truncate(string s, int max)
    {
        return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }

    static void RaiseChanged()
    {
        var handler = Changed;
        if (handler != null) handler();
    }

    // Tests: back to "nothing happened this launch".
    public static void ResetSession()
    {
        signingIn = false;
        pendingCallbacks = null;
        lastReport = default(SignInReport);
        silentFailed = false;
        InteractiveAttempts = 0;
        accountOverride = null;
        AccountPlatformOverride = null;
    }
}

public enum SignInOutcome
{
    None,
    Success,
    SignInRequired,   // silent attempt: the player has to sign in themselves
    Canceled,         // the player closed the store's sign-in UI
    ConfigError,      // the game isn't set up for this build (SHA-1, Games config, testers)
    NetworkError,
    Disabled,         // Game Center: sign-in turned off for the app (too many cancels)
    Failed,           // anything else
}

// What the platform said about a sign-in attempt, for the Account row and logs.
public struct SignInReport
{
    public SignInOutcome outcome;
    public bool interactive;
    public string code;        // short status, e.g. SIGN_IN_REQUIRED, DEVELOPER_ERROR(10)
    public string detail;      // the platform's full message, if any
    public float seconds;      // how long the attempt took
    public bool promptShown;   // the store's UI took focus during the attempt

    public static SignInReport Of(SignInOutcome outcome, bool interactive, string code, string detail)
    {
        return new SignInReport { outcome = outcome, interactive = interactive, code = code, detail = detail };
    }
}

// Optional extras an IPlayerAccount can offer the Account row.
public interface IAccountProfile
{
    string DisplayName { get; }
    SignInReport LastSignIn { get; }
}

// Turns what the platform plugins report into a SignInReport.
public static class SignInDiagnosis
{
    // Play Games plugin 2.1.0 reports only Success / Canceled / InternalError.
    // The interesting part is in the plugin's own log line, captured while the
    // attempt runs: "Authentication failed - <ApiException ...>" when the
    // task failed (status code inside), "Returning an error code." when Play
    // Games answered "not authenticated" (the silent SIGN_IN_REQUIRED, a
    // cancelled prompt, or a rejected app configuration).
    public static SignInReport FromPlayGames(string status, bool interactive, string pluginLog,
                                             float seconds, bool promptShown)
    {
        string log = pluginLog ?? "";
        var report = new SignInReport
        {
            interactive = interactive,
            detail = (status + (log.Length > 0 ? ": " + log : "")).Trim(),
            seconds = seconds,
            promptShown = promptShown,
        };
        if (status == "Success")
        {
            report.outcome = SignInOutcome.Success;
            report.code = "SUCCESS";
            return report;
        }

        int code = StatusCode(log);
        if (code == 10 || Has(log, "DEVELOPER_ERROR") || Has(log, "APP_MISCONFIGURED") ||
            Has(log, "GAME_NOT_FOUND") || Has(log, "not configured") || Has(log, "misconfigured"))
        {
            report.outcome = SignInOutcome.ConfigError;
            report.code = "DEVELOPER_ERROR" + (code > 0 ? "(" + code + ")" : "");
            return report;
        }
        if (code == 7 || Has(log, "NETWORK_ERROR") || Has(log, "network"))
        {
            report.outcome = SignInOutcome.NetworkError;
            report.code = "NETWORK_ERROR" + (code > 0 ? "(" + code + ")" : "");
            return report;
        }
        if (code == 16 || Has(log, "CANCELED") || Has(log, "CANCELLED"))
        {
            report.outcome = interactive ? SignInOutcome.Canceled : SignInOutcome.SignInRequired;
            report.code = "CANCELED" + (code > 0 ? "(" + code + ")" : "");
            return report;
        }
        if (code == 4 || Has(log, "SIGN_IN_REQUIRED"))
        {
            report.outcome = SignInOutcome.SignInRequired;
            report.code = "SIGN_IN_REQUIRED(4)";
            return report;
        }
        if (code > 0 || status == "InternalError")
        {
            report.outcome = SignInOutcome.Failed;
            report.code = "ERROR" + (code > 0 ? "(" + code + ")" : "_INTERNAL");
            return report;
        }

        // "Not authenticated" with no exception.
        if (!interactive)
        {
            // The launch case on a fresh install: Play Games wants the
            // player to sign in themselves (logcat: suppressed SIGN_IN_REQUIRED).
            report.outcome = SignInOutcome.SignInRequired;
            report.code = "SIGN_IN_REQUIRED";
            return report;
        }
        // Interactive: either the player closed Google's sign-in, or Play
        // Games refused this build before showing anything (unregistered
        // signing key / Games project not set up / not a tester). Without a
        // prompt, or back within a moment, it was not the player.
        if (!promptShown || seconds < QuickRefusalSeconds)
        {
            report.outcome = SignInOutcome.ConfigError;
            report.code = "NOT_AUTHENTICATED_NO_PROMPT";
            return report;
        }
        report.outcome = SignInOutcome.Canceled;
        report.code = "CANCELED";
        return report;
    }

    // Faster than a person can read and dismiss a sign-in screen.
    public const float QuickRefusalSeconds = 1.5f;

    // Game Center: Unity hands over the NSError's localized description.
    public static SignInReport FromGameCenter(bool success, string error, bool interactive, float seconds)
    {
        var report = new SignInReport
        {
            interactive = interactive,
            detail = error ?? "",
            seconds = seconds,
            promptShown = true,
        };
        if (success)
        {
            report.outcome = SignInOutcome.Success;
            report.code = "SUCCESS";
            return report;
        }
        string e = error ?? "";
        if (Has(e, "not recognized") || Has(e, "unrecognized") || Has(e, "GKErrorDomain error 15") ||
            Has(e, "not been enabled") || Has(e, "entitlement"))
        {
            report.outcome = SignInOutcome.ConfigError;
            report.code = "GAME_UNRECOGNIZED";
        }
        else if (Has(e, "cancel") || Has(e, "disabled"))
        {
            // GKErrorCancelled ("cancelled or disabled by the user"). After a
            // few dismissals iOS stops showing the sheet and answers this at
            // once: then only Settings > Game Center can sign the player in.
            if (!interactive) report.outcome = SignInOutcome.SignInRequired;
            else if (seconds < QuickRefusalSeconds) report.outcome = SignInOutcome.Disabled;
            else report.outcome = SignInOutcome.Canceled;
            report.promptShown = interactive && seconds >= QuickRefusalSeconds;
            report.code = "CANCELLED";
        }
        else if (Has(e, "network") || Has(e, "offline") || Has(e, "internet"))
        {
            report.outcome = SignInOutcome.NetworkError;
            report.code = "NETWORK";
        }
        else
        {
            report.outcome = interactive ? SignInOutcome.Failed : SignInOutcome.SignInRequired;
            report.code = string.IsNullOrEmpty(e) ? "NOT_AUTHENTICATED" : "ERROR";
        }
        return report;
    }

    static bool Has(string s, string what)
    {
        return s.IndexOf(what, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // "...ApiException: 10: ..." / "statusCode=DEVELOPER_ERROR" -> 10; 0 when none.
    public static int StatusCode(string log)
    {
        if (string.IsNullOrEmpty(log)) return 0;
        int at = log.IndexOf("ApiException:", StringComparison.Ordinal);
        if (at >= 0)
        {
            int i = at + "ApiException:".Length;
            while (i < log.Length && log[i] == ' ') i++;
            int start = i;
            while (i < log.Length && char.IsDigit(log[i])) i++;
            int code;
            if (i > start && int.TryParse(log.Substring(start, i - start), out code)) return code;
        }
        if (Has(log, "DEVELOPER_ERROR")) return 10;
        if (Has(log, "SIGN_IN_REQUIRED")) return 4;
        if (Has(log, "NETWORK_ERROR")) return 7;
        return 0;
    }
}

// Opening the store's own account settings, and the signing-key check for
// the developer details line.
public static class AccountSettingsLink
{
    public const string PlayGamesPackage = "com.google.android.play.games";

    // Can this platform open its account settings at all?
    public static bool CanOpen
    {
        get
        {
            return Application.platform == RuntimePlatform.Android ||
                   Application.platform == RuntimePlatform.IPhonePlayer;
        }
    }

    public static string OpenLabel
    {
        get { return AccountLink.IsGameCenter ? "OPEN SETTINGS" : "OPEN PLAY GAMES"; }
    }

    public static string ManageInstructions
    {
        get
        {
            return AccountLink.IsGameCenter
                ? "Apps can't sign you out of Game Center. To switch or sign out, go to Settings > Game Center."
                : "Apps can't sign you out of Google Play Games. To switch or remove the account, open the Play Games app > Settings.";
        }
    }

    // Tests replace it to see what would be opened.
    public static Func<bool> OpenOverride;

    // Returns false when nothing could be opened (the caller shows text only).
    public static bool Open()
    {
        if (OpenOverride != null) return OpenOverride();
        try
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.MAIN"))
            {
                intent.Call<AndroidJavaObject>("addCategory", "android.intent.category.LAUNCHER");
                intent.Call<AndroidJavaObject>("setPackage", PlayGamesPackage);
                intent.Call<AndroidJavaObject>("addFlags", 0x10000000);   // FLAG_ACTIVITY_NEW_TASK
                activity.Call("startActivity", intent);
            }
            return true;
#elif UNITY_IOS && !UNITY_EDITOR
            // UIApplicationOpenSettingsURLString: the app's page in Settings;
            // Game Center is one level up (Settings > Game Center).
            Application.OpenURL("app-settings:");
            return true;
#else
            return false;
#endif
        }
        catch (Exception e)
        {
            Debug.Log("[Account] couldn't open the account settings: " + e.Message);
            return false;
        }
    }

    static string certSha1;
    static bool certRead;

    // Android: SHA-1 of the APK's signing certificate, as Play Console lists
    // it (AA:BB:...). Null elsewhere. This is the value that must be
    // registered for the Play Games credential of this package.
    public static string SigningCertificateSha1()
    {
        if (certRead) return certSha1;
        certRead = true;
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var pm = activity.Call<AndroidJavaObject>("getPackageManager"))
            {
                string package = activity.Call<string>("getPackageName");
                using (var info = pm.Call<AndroidJavaObject>("getPackageInfo", package, 64))   // GET_SIGNATURES
                {
                    var signatures = info.Get<AndroidJavaObject[]>("signatures");
                    if (signatures != null && signatures.Length > 0)
                    {
                        byte[] cert = signatures[0].Call<byte[]>("toByteArray");
                        using (var sha = System.Security.Cryptography.SHA1.Create())
                            certSha1 = BitConverter.ToString(sha.ComputeHash(cert)).Replace("-", ":");
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.Log("[Account] couldn't read the signing certificate: " + e.Message);
        }
        if (certSha1 != null) Debug.Log("[Account] APK signing certificate SHA-1: " + certSha1);
#endif
        return certSha1;
    }
}
