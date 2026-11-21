# iOS release (TestFlight / App Store)

Mirror of the Android flow (`.claude/skills/release-android`, `docs/build-and-share.md`). Bundle id `me.hapticgate.pause`, iOS 15.0+, portrait, iPhone + iPad (`targetDevice: 2`).

Status: written 2026-11-20, **not yet run end to end** (the pipeline needs the HapticGate Team ID and a Mac signed in to that account). Everything below marked "check" has to be confirmed on the first run.

## What the pipeline does

`make ios-release IOS_TEAM_ID=<HapticGate team id>`

1. `ios-release-build`: Unity batch `BuildScript.BuildIOSRelease` exports `Pause/Builds/iOS-release` (non-dev: no `PAUSE_DEV`, no development flag; its own folder, never mixed with `iOS` / `iOS-dev`). The post-processors add Game Center, iCloud, App Attest, Info.plist compliance keys and the privacy manifest.
2. `ios-archive`: `scripts/ios-release.sh archive` runs `pod install` if the export has a Podfile and no workspace (the EDM iOS resolver is off, `AdsIosPods` writes the Podfile: Firebase, Google Mobile Ads, UMP), then `xcodebuild archive` on `Unity-iPhone.xcworkspace`, Release, automatic signing, team from env.
3. `ios-export`: `xcodebuild -exportArchive` with a generated `ExportOptions.plist` (method `app-store-connect`, automatic signing, team from env, upload symbols) into `Pause/Builds/iOS-release/ipa/Pause.ipa`.
4. Upload (optional): either `IOS_UPLOAD=1 make ios-release` (ExportOptions `destination: upload`, sends straight to App Store Connect) or, after the fact, `make ios-upload` (`xcrun altool --upload-app` with the API key).

Steps can be rerun alone: `make ios-archive`, `make ios-export`, `make ios-upload` (they reuse the Unity export).

No devices on the team: `make ios-archive` cannot create the development profile it wants. Use `PAUSE_IOS_TEAM_ID=... scripts/ios-release.sh manual` (unsigned archive, hand signing with the Apple Distribution identity + the cloud-managed App Store profile, writes `ipa/Pause.ipa`), then `scripts/ios-release.sh upload`.

## Environment variables

| Variable | Used by | Meaning |
|---|---|---|
| `IOS_TEAM_ID` or `PAUSE_IOS_TEAM_ID` | all steps | HapticGate Team ID (10 chars). Required, never guessed. The script refuses the free personal team `6Q8UK2NSZ2`. |
| `ASC_KEY_ID`, `ASC_ISSUER_ID` | archive/export (optional), upload (required) | App Store Connect API key id and issuer id. |
| `ASC_KEY_PATH` | same | The `AuthKey_<KEYID>.p8`; default `~/.appstoreconnect/private_keys/AuthKey_$ASC_KEY_ID.p8`. Keep it outside the repo, never commit it. |
| `IOS_UPLOAD=1` | export | Upload during export. |
| `PAUSE_IOS_BUILD_NUMBER` | Unity step | Override CFBundleVersion (see below). |
| `PAUSE_IOS_VERSION` | Unity step | Override CFBundleShortVersionString (e.g. `1.0` for the first App Store version). |
| `PAUSE_IOS_NO_GAMECENTER` | Unity step | Sideload only (free team). Never for release. |

## Build number

`CFBundleVersion` has to increase on every upload for a given version (`CFBundleShortVersionString` = `bundleVersion`, currently 1.1.3). `BuildIOSRelease` sets the iPhone build number to **`AndroidBundleVersionCode`** (10018 today) at build time, so the Android bump PR (`release-android` step 3) also bumps iOS and nothing else needs editing. The committed `buildNumber: 0` in ProjectSettings is not touched. For an iOS-only re-upload of the same Android code, run with `PAUSE_IOS_BUILD_NUMBER=<n>` (keep it above every earlier upload; mixing the override with Android codes later is fine as long as it only goes up).

## One-time setup

1. **Apple ID in Xcode**: Xcode > Settings > Accounts > + > sign in with the company Apple ID (HapticGate LLC). Select the team, "Manage Certificates", make sure an Apple Distribution certificate exists (or let automatic signing create it).
2. **Team ID**: developer.apple.com/account > Membership details (or the Accounts pane). `export IOS_TEAM_ID=XXXXXXXXXX` (put it in `~/.zshrc`, not in the repo).
3. **App ID**: Certificates, Identifiers & Profiles > Identifiers > `me.hapticgate.pause`, capabilities: Game Center, iCloud (Documents, default container), App Attest. Automatic signing (`-allowProvisioningUpdates`) normally registers these from the entitlements.
4. **App Store Connect app record**: Apps > + > New App, bundle id `me.hapticgate.pause`, SKU e.g. `pause`. Free app (matches Play).
5. **API key**: App Store Connect > Users and Access > Integrations > App Store Connect API > Team Keys > Generate (role App Manager, or Admin for profile creation). Download the `.p8` once, `mkdir -p ~/.appstoreconnect/private_keys && mv AuthKey_<KEYID>.p8 ~/.appstoreconnect/private_keys/ && chmod 600 ...`. Export `ASC_KEY_ID` and `ASC_ISSUER_ID` (Issuer ID is shown at the top of that page).
6. **CocoaPods**: `brew install cocoapods` (1.17 is installed on this Mac). Xcode 27.1 + `pod` need `LANG=en_US.UTF-8`, which the script sets.
7. **Firebase**: `GoogleService-Info.plist` in `Assets/Firebase/Config` must be the one for bundle id `me.hapticgate.pause`. In the Firebase console, App Check > Apps > iOS: register App Attest (no key to upload). Enforcement stays off until metrics look healthy (`docs/shared-leaderboard.md`).

## What is set automatically (and where)

| Item | Where | Value |
|---|---|---|
| Export compliance | `IOSReleasePostProcess` | `ITSAppUsesNonExemptEncryption = false`. Verified in code: the game only uses HTTPS (Firebase, AdMob, Game Center), OS crypto and a SHA-1 id hash (`AccountLink`), all exempt. Re-check if custom encryption is ever added. |
| ATT text | Google plist step, from `GoogleMobileAdsSettings.userTrackingUsageDescription` | present in the asset; the post-process logs `[BUILD] ERROR` if the key is missing from the output |
| AdMob app id | Google plist step | `GADApplicationIdentifier` from `adMobIOSAppId` (`...~1298571462`) |
| SKAdNetworkItems | Google plist step (`GoogleMobileAdsSKAdNetworkItems.xml`, GMA plugin 11.6.0) | injected by the plugin; refresh the plugin to refresh the list |
| Unused usage keys | `IOSReleasePostProcess` | camera/mic/location/photos/contacts/etc. are removed if something added them |
| Full screen | `IOSReleasePostProcess` | `UIRequiresFullScreen = true` (portrait only; without it iPad validation wants all four orientations) |
| `UIRequiredDeviceCapabilities` | `IOSReleasePostProcess` | `arm64` ensured (Unity adds the rest, e.g. metal) |
| Privacy manifest | `IOSReleasePostProcess` | Unity, Firebase and GMA ship their own `PrivacyInfo.xcprivacy`; the app's own required-reason APIs (UserDefaults `CA92.1` for PlayerPrefs, file timestamps `C617.1` for save files) are merged into Unity's manifest or added as a new one. `NSPrivacyTracking` and empty `NSPrivacyTrackingDomains` are removed (ITMS-91064; the ASC label declares tracking). |
| App Attest | `IOSCapabilitiesPostProcess` | entitlement `com.apple.developer.devicecheck.appattest-environment = production` |
| App icon | ProjectSettings iPhone icon slots | `OrbitalRail_ios_1024.png`, verified no alpha (`sips`: hasAlpha no, 1024x1024) |

App Check: `FirebaseSharedBoard` registers `AppAttestProviderFactory` on iOS release builds (debug provider only in the Editor / `DEVELOPMENT_BUILD` / `PAUSE_DEV`). There is no DeviceCheck fallback in the code; App Attest is available on every iPhone/iPad that can run iOS 15 (the minimum), so none is needed. App Attest does not work in the simulator, which is why the simulator build is a dev build.

## Per-release steps

1. Android bump PR merged to master (`AndroidBundleVersionCode`; also change `bundleVersion` for a new marketing version). The build number follows it.
2. Fresh workspace on master: `make worktree NAME=ios-rel` (never the main checkout).
3. `cd ../Pause-worktrees/ios-rel && IOS_TEAM_ID=... make ios-release` (add `IOS_UPLOAD=1 ASC_KEY_ID=... ASC_ISSUER_ID=...` to upload in the same run), or `make ios-release` then `make ios-upload`.
4. App Store Connect > TestFlight: the build appears after processing (usually 5-30 min), then answers the encryption question automatically (key present).
5. `make worktree-done NAME=ios-rel` when finished.

Upload failure triage: "build number already used" -> raise `PAUSE_IOS_BUILD_NUMBER`; "no profile / capability" -> check step 3 of setup; "ITMS-91053 missing API declaration" -> add the named reason to `IOSReleasePostProcess.ApiReasons`.

## First TestFlight checklist

- [ ] Company Apple ID in Xcode, Team ID exported, API key in `~/.appstoreconnect/private_keys`
- [ ] App record created; App ID has Game Center, iCloud, App Attest
- [ ] Game Center: leaderboards and achievements configured (separate job), same ids as the code
- [ ] `make ios-release` produces `Pause.ipa`; open `Builds/iOS-release/Pause.xcarchive` > Info.plist: check `ITSAppUsesNonExemptEncryption`, `GADApplicationIdentifier`, `SKAdNetworkItems`, `NSUserTrackingUsageDescription`, no stray usage keys; Xcode Organizer > archive > "Generate Privacy Report" lists the merged manifests (no missing-API warnings)
- [ ] Upload processes in TestFlight without warnings emails (ITMS-9xxxx)
- [ ] Internal testing on a real iPhone (and an iPad if iPad stays supported): launch, ATT prompt after the UMP form, ads load, Game Center sign-in, achievements, cloud save, shared leaderboard (App Check with App Attest: check the Firebase App Check metrics show verified iOS requests)
- [ ] App privacy answers entered (`docs/release/app-privacy-ios.md` plus the ads rows), privacy policy URL set
- [ ] Screenshots: iPhone 6.9" (and iPad 13" if iPad is supported); age rating; export compliance answered; submit for review (never automatically)

## Open questions

- iPad: `targetDevice` is iPhone + iPad, which means iPad screenshots and layout checks. Portrait-only plus `UIRequiresFullScreen` is valid; switch to iPhone only if iPad support is not wanted.
- `NSPrivacyTracking` is deliberately absent from the app manifest (ITMS-91064); the App Privacy answers in ASC declare IDFA tracking.
- Optional App Check hardening: none needed for TestFlight; enforce only after the metrics check.
