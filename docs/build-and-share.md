# Build an APK to share

```bash
make apk-share
```

Builds a release-style Android APK for friends and testers and writes it to
`build/share/Pause-<version>-<shortsha>.apk` (gitignored). It prints the path,
size and an `adb install -r <apk>` line when done. It goes through
`scripts/unity-batch.sh`, so it queues behind other Unity jobs; a cold Library
imports for Android first, which takes a while.

## "Unsigned" means debug-signed

Android refuses to install a truly unsigned APK. This APK is signed with
Unity's **debug keystore**, not the release/upload key, so it installs by
sideloading (enable "install unknown apps" on the phone, or use `adb install`).
It cannot be uploaded to Google Play, and it cannot be updated in place by a
Play build (different signing key; uninstall first).

The build never reads `PAUSE_KEYSTORE_PASS`, `PAUSE_KEYALIAS_PASS` or
`PAUSE_KEYSTORE_PATH` (the script unsets them and `BuildScript.BuildAndroidShare`
forces the debug key), and no Play or Game Center credentials are involved.

## How it differs from other builds

| | `apk-share` | `android-dev-build` | `android-release` (Play) |
|---|---|---|---|
| Signing | debug key | debug key | release keystore (AAB + APK) |
| `PAUSE_DEV` (developer mode) | off | on | off |
| Unity development build flag | off | off | off |
| Output | APK | APK | AAB + APK |

Package id, version and version code come from `ProjectSettings.asset` and are
not modified by the build.
