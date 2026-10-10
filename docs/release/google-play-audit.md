# Google Play release audit (v1.0.0, branch feature/release-rc)

Package `me.hapticgate.pause`, paid app (USD 1.99), four worlds: Space, Frost, Verdant, Ember. Unity 6000.3.23f1. Checked against an audit build (see section 12) and the code.

## Verdict

**No code blocker, three owner actions block upload**: (1) build the signed AAB with the keystore passwords (only the owner has them), (2) create the Play-App-Signing OAuth client for Play Games (section 9), (3) publish the Play Games achievements and leaderboards in Play Console. Everything else below is fixed in this branch or is a recommendation.

## 1. Version

| Setting | Value | Where |
|---|---|---|
| versionName | `1.0.0` (was `1.1`) | Player Settings > Other > Version = `bundleVersion` in `ProjectSettings/ProjectSettings.asset` |
| versionCode | `10000` (was `1`) | `AndroidBundleVersionCode` in the same file |

Strategy: versionCode = major * 10000 + minor * 100 + patch (1.0.0 = 10000, 1.0.1 = 10001, 1.2.0 = 10200), always increasing, one number per upload. Bump both together before each upload. The phone currently has versionCode 1 builds, so a sideloaded 10000 upgrades cleanly; going back to a lower code needs an uninstall.

## 2. API levels and architecture

| Item | Value | Verdict |
|---|---|---|
| targetSdk | **36** (was `0` = automatic highest installed) | Google Play requires new apps and updates to target Android 15 (API 35) since 31 Aug 2025, and Android 16 (API 36) from 31 Aug 2026, as far as I know. Today is after that date, so 36 is the floor; the project was effectively on the highest installed SDK (also 36) but is now pinned explicitly. Re-check Play Console's "Target API level" notice at upload. Unity 6000.3 supports compileSdk/target 36. |
| minSdk | 25 (Android 7.1) | Fine (Unity 6 minimum is 23, Play Games v2 needs 21+). |
| ABIs | **arm64-v8a only** (was ARMv7 + ARM64) | Done: `AndroidTargetArchitectures: 2`. Built APK `native-code: 'arm64-v8a'`; mainTemplate.gradle and AndroidResolverDependencies.xml updated by the resolver accordingly. Play requires 64-bit; dropping ARMv7 removes pre-2016 devices only. |
| Scripting backend | IL2CPP | Required for arm64. |
| Managed stripping | Low (`managedStrippingLevel: 1`), engine code stripping on | Safe for the Play Games plugin. |
| Output | AAB for Play, APK for sideload | `make android-release` (new) builds both. |
| Bundle size validation | `AndroidValidateAppBundleSize: 1`, limit 200 MB | The AAB is 181 MB: under the limit with little headroom, see section 10. |
| Install location | `auto` (was preferExternal) | Changed; preferExternal is obsolete. |
| Android TV | off (was on) | `AndroidTVCompatibility: 0` removed the `LEANBACK_LAUNCHER` category; a touch-only game should not be offered on TV. |
| Category | Game (`AndroidIsGame`, appCategory game) | OK |

## 3. R8 / minify

`AndroidMinifyRelease: 0`, no custom proguard file, so R8 is OFF for release: nothing to break, and no mapping file to upload. If someone turns minify on later: the Play Games plugin ships `Assets/GooglePlayGames/com.google.play.games/Proguard/games.txt` (keeps `com.google.android.gms.games.**`, `com.google.games.bridge.**`, tasks, common.api, nearby). Unity picks up consumer rules from the plugin folder; verify sign-in, leaderboards, achievements and cloud save on a device after enabling it. Billing is no longer in the app (section 4), so no billing keep rules are needed.

## 4. Permissions

Final merged manifest of the release build (`aapt2 dump badging`):

| Permission | Why | Verdict |
|---|---|---|
| `android.permission.INTERNET` | Play Games Services traffic | Keep |
| `android.permission.ACCESS_NETWORK_STATE` | Google Play services checks connectivity before sign-in and sync | Keep |

Removed in this branch: **`com.android.vending.BILLING`** (plus the billing-client activities and `queries` entries for the billing service). It came from the `com.unity.purchasing` package, which nothing in the game uses (no IAP; the game is a paid download). I removed `com.unity.purchasing` and `com.unity.analytics` from `Packages/manifest.json` and deleted `Assets/Resources/BillingMode.json` (Unity IAP config). Unity regenerates `packages-lock.json` on first open (committed). No `RECORD_AUDIO`, storage, phone-state, location, contacts, camera, Bluetooth, advertising-ID (`AD_ID`) or foreground-service permissions are present. The Google Nearby dependency of the Play Games plugin adds a service (`exposurenotification.WakeUpService`) but no permissions; leave it (the plugin's resolver file owns it).

Ads: the coordinator reports a separate branch (`feature/no-ads`) is deleting `Assets/Scripts/UI/AdMob.cs` and its call sites. At the time of this audit it is a no-op stub with no ad SDK, no ad permission, no AD_ID and no ad meta-data in the manifest. I did not touch it. After that branch merges, the merged manifest should be unchanged.

## 5. Backup, orientation, safe area

- Backup: the manifest has no `allowBackup` attribute, so Android's default (auto-backup ON) applies; PlayerPrefs (progress) get backed up to the user's Google Drive. Decision: leave on, because players who never sign in to Play Games would otherwise lose everything on a new phone; the cloud-save merge (max-merge by counter, `ProgressMerge`) copes with a restored older snapshot. Developer mode cannot be restored into a release (see section 6). If you prefer no backup, add `android:allowBackup="false"` through a custom main manifest.
- Orientation: `screenOrientation=1` (portrait), only portrait autorotate allowed. `resizeableActivity=true` (Android 16 ignores orientation locks on large screens, 600dp+ displays, so tablets/foldables may letterbox or rotate; the ScreenFit tests cover tablet sizes).
- Edge-to-edge: Unity meta-data `unity.render-outside-safearea=true`, `notch.config=portrait|landscape`; UI panels read `Screen.safeArea` (`TopBand.FrameFor`), checked by the ScreenFit device matrix tests. Android 15+ forces edge-to-edge for target 35+, which this already is.

## 6. Debug and developer leftovers

All developer code is compile-gated; none is reachable in the release build.

| Item | Gate |
|---|---|
| `DeveloperUnlocks` (unlock all, world picker, Options developer rows `DeveloperOptions`) | `Available` is only true under `UNITY_EDITOR`, `DEVELOPMENT_BUILD` or `PAUSE_DEV`. **New in this branch:** `Enabled` now also requires `Available`, so a leftover `developerUnlockEverything` pref (a dev build installed over the release, or a restored backup) cannot switch on developer mode, free ships, boss rush or no-leaderboard behaviour in a release build. |
| F-key hotkeys, P (open portal), B/F (boss) | `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD` |
| DEV badge | `PAUSE_DEV` only; `PAUSE_DEV` is passed per build by `BuildScript` (`extraScriptingDefines`), not stored in Player Settings (test asserts this) |
| Shield debug warnings | `UNITY_EDITOR \|\| DEVELOPMENT_BUILD \|\| PAUSE_DEV` |
| Development Build flag | `BuildOptions.None` in `BuildScript.Run`/`Build`; the release method sets none |
| Test scenes in Build Settings | None: exactly 7 shipping scenes (`spashS7`, `startS4`, `leaderboardS3`, `gameS1`, `shopS6`, `creditsS7`, `tutorialS5`), asserted by the new test |
| Logging | About 37 `Debug.Log*` calls in runtime scripts (sign-in, cloud save, leaderboard diagnostics); harmless, no personal data, no secrets. Left as is. |
| Stub ad ids | `AdMob.cs` comment contains a retired AdMob unit id (being removed by `feature/no-ads`) |

Keystore: `Pause/PauseKey.keystore` is git-ignored (good) and Player Settings still point `AndroidKeystoreName` at a Windows path; the new release build method overrides it, and passwords are never stored in the repo.

## 7. Release switches

Confirmed (and asserted by `ReleaseReadinessTest`):

- `WorldManager.TideEnabled = false`; `LiveWorldCount = 4`; the loop is Space, Frost, Verdant, Ember, Space. `Worlds[]` holds Space, Frost, Verdant, Ember, Tide (Tide is reachable only through the developer picker, which is gated). No Storm or Desert world exists in code (only a "Storm" ship skin colour set, and `CodexPanel.MaxSections` text mentions Storm in a comment).
- Dormant Tide achievements are hidden and not claimable until the switch flips.
- Android ids: all 60 achievements in `Assets/Resources/AchievementStoreIds.csv` are `CgkI...` ids, unique, none `TODO_`. All three leaderboards (Top Score `CgkIopqxqbAPEAIQPg`, Star Dust `...PwA`, Furthest Loop `...QA`) have Android ids. iOS ids are empty or unconfirmed (`IosIdsConfirmed=false`) and are compiled out of the Android path, so they do not block Android.
- Play Games app id in `PlayGamesSettings.asset` = `528367766818`, and in the merged manifest `com.google.android.gms.games.APP_ID`.

Owner TODO: the achievements and leaderboards are still drafts in Play Console; they must be "Review and publish"-ed, and linked to the same app (`me.hapticgate.pause`), or sign-in works but nothing reports.

## 8. Tide assets shipped while disabled

Tide art loaded from `Resources/` is packaged even though the world is off: about 47.6 MB uncompressed of 513 MB (roughly 9%; maybe 15 to 17 MB of the AAB). Not a bug, but it ships dead weight and exposes unreleased art in the APK. If size matters, move Tide's Resources folders out of the build until the switch flips. Risky to do blind (tests and the developer picker load them), so not done here.

## 9. Play Games sign-in and SHA-1

Each OAuth client (Cloud project 528367766818, Credentials, Android client for package `me.hapticgate.pause`) is bound to one signing certificate SHA-1:

| Build | SHA-1 | Client |
|---|---|---|
| Debug-signed (Unity debug key, `make android-deploy`) | `A5:D3:CA:F1:3D:A2:33:BD:98:35:D7:DB:E6:DB:7A:FA:CF:43:EF:28` | exists |
| Upload key `PauseKey.keystore`, alias `pausealias` | `E2:57:07:AC:A8:40:B0:41:E1:7C:38:D6:49:BF:3F:A1:50:01:AA:D5` | exists |
| **Play App Signing key** (Google re-signs what you upload, and is the key users' phones see) | read it in Play Console > Setup > App signing after the first upload ("App signing key certificate" SHA-1) | **needed, third client** |

If the third client is missing, the Play-installed app will fail Play Games sign-in (status code like DEVELOPER_ERROR / "sign in failed") even though sideloaded release builds work. Also add testers' Google accounts in the Play Games Services testing list until the game is published. I did not verify the SHA-1s against the keystore (needs the password).

Sign-in flow in code: `PlayGamesAccount.SignIn` (silent `Authenticate` at start, `ManuallyAuthenticate` from the Options Account row); failures are logged with the plugin's status code and surfaced by `SignInDiagnosis`. Play Games saved games must also be enabled for the game in Play Console (cloud save uses `OpenWithAutomaticConflictResolution`).

## 10. Install size, startup, memory, frame rate

- Audit AAB: 180.7 MB (181 MB), APK 183.1 MB, arm64 only (libil2cpp 51.8 MB, libunity 19.8 MB). Over the Play 200 MB base-module AAB limit it is not, but it is big for a 2D game. Uncompressed content is 513 MB, 90% textures (462 MB).
- Biggest: 16 ship hull sheets at 9.0 MB uncompressed each (135 MB together), `exhaust_atlas.png` 15.4 MB, rail sheets 6 MB each (six worlds' worth), `teleport_portal_atlas` 6.2 MB, Tide assets 47.6 MB. Cheapest wins: confirm hulls/rails use ASTC (the logged sizes look like uncompressed RGBA, which suggests the Android texture format override is not applied to these; check their importer "Android" override), drop Tide until it ships, trim the 15 MB exhaust atlas. Each is a content change outside this task. Play also delivers the AAB compressed and split by ABI/density, so the download is roughly the AAB size or a little less.
- Startup: not measured on a device in this task. The splash scene is first; Unity splash screen is off. Measure with `adb shell am start -W me.hapticgate.pause/com.unity3d.player.UnityPlayerActivity` on the phone before launch.
- Memory budgets: `WorldBackdropTest` enforces per-world texture memory in ASTC 6x6 terms: Space 10 MB, Frost 12 MB, Verdant 7 MB, Ember 7 MB, Tide 10 MB (default 9 MB). Those suites pass in the full run (section 12).
- Frame rate: `FrameRateBootstrap` sets `targetFrameRate = 60` and `vSyncCount = 0`; on 90/120 Hz phones the game still caps at 60. A 60 fps claim is a target, not measured in this task; verify on a mid-range device with `adb shell dumpsys gfxinfo me.hapticgate.pause`.

## 11. Changes made in this branch

- `ProjectSettings`: version 1.0.0 / code 10000, target 36, ARM64 only, install location auto, TV compat off.
- `Packages/manifest.json`: removed `com.unity.purchasing`, `com.unity.analytics`; deleted `Resources/BillingMode.json`.
- `DeveloperUnlocks.Enabled` requires `Available`.
- `BuildScript.BuildAndroidRelease` + `make android-release`: builds `Builds/Android/Release/Pause.aab` and `Pause.apk` (no PAUSE_DEV, not development). Needs `PAUSE_KEYSTORE_PASS`, `PAUSE_KEYALIAS_PASS`, and `PAUSE_KEYSTORE_PATH` if the keystore is not at `Pause/PauseKey.keystore` (the keystore lives in the main checkout; this worktree has none). Without them it builds `Pause-DEBUGSIGNED.*` for auditing only.
- `ReleaseReadinessTest` (registered in `AllTests`).

## 12. Build result

Audit build (debug-signed, not uploadable), `Builds/Android/Release/` in the release-rc worktree:

| File | Size |
|---|---|
| `Pause-DEBUGSIGNED.aab` | 180,743,470 bytes |
| `Pause-DEBUGSIGNED.apk` | 183,099,278 bytes |

`aapt2 dump badging` on the APK: package `me.hapticgate.pause`, versionCode 10000, versionName 1.0.0, minSdk 25, targetSdk 36, compileSdk 36, native-code arm64-v8a, permissions INTERNET and ACCESS_NETWORK_STATE only. The signed upload needs the passwords; run `PAUSE_KEYSTORE_PASS=... PAUSE_KEYALIAS_PASS=... PAUSE_KEYSTORE_PATH=/Users/sina/Developer/Pause/Pause/PauseKey.keystore make android-release`.
