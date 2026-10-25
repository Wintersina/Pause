<div align="center">

<img src="docs/pause-title.png" alt="Pause" width="440">

**A one-touch survival game where letting go is the only move you have.**

Hold to fly. Lift your finger to freeze the universe, but you only get so many pauses.

</div>

---

Pause is a Unity game for **Android and iOS** (a paid app, planned at $1.99). You
survive across seven worlds: Space, Frost, Verdant, Ember, Tide, Storm and Desert.

- Package / bundle id: `me.hapticgate.pause`
- Unity version: **6000.3.23f1**

## Repository layout

| Path | What is in it |
| --- | --- |
| `Pause/` | The Unity project (the project root, not the repo root). See [docs/project-layout.md](docs/project-layout.md). |
| `docs/` | Design notes, build guides, store and release docs. |
| `scripts/` | Build, capture and test helper scripts (`unity-batch.sh` runs Unity batch jobs one at a time). |
| `Makefile` | Local build / install / run shortcuts. |

## Running the project

In Unity Hub, `Add` and select the `Pause/` folder, then open it with Unity 6000.3.23f1.

## Building

The root `Makefile` wraps the batch builds (run `make help` for the full list).
Set `UNITY=/path/to/Unity` if Unity is not at the default Unity Hub location.

```bash
make mac-run                 # build and launch the Mac app
make mac-dev-run             # same, PAUSE_DEV build
make android-deploy          # build, install and launch on a connected Android device
make android-dev-deploy      # same, PAUSE_DEV build
make android-release         # signed Play AAB + APK + symbols in Pause/Builds/Android/Release
make android-release-signed  # prompts for the keystore password, then runs android-release
make apk-share               # debug-key-signed APK for friends (see docs/build-and-share.md)
make android-log             # stream Unity logs from the device
make ios-devices             # list connected iPhone/iPad ids
IOS_DEVICE_ID=<udid> make ios-deploy      # build, install and launch on that device
IOS_DEVICE_ID=<udid> make ios-dev-deploy  # same, PAUSE_DEV build
```

Emulators and simulators:

```bash
make devices          # list running Android emulators and booted iOS simulators
make install-android  # build the dev APK, install and launch it on running emulators
make install-ios      # build the simulator app, install and launch it
make install          # both, one after the other
make run-android      # launch the already-installed app (run-ios for iOS)
```

`android-*` targets need `adb` with USB debugging; with several devices pick one with
`ANDROID_SERIAL=<serial>`. `ios-*` targets need Xcode, a trusted device and a signing
setup that permits it. Build output lands in `Pause/Builds/`.

## Tests

Tests run on CI only, in GitHub Actions (`.github/workflows/unity-tests.yml`, see
[docs/ci-unity-tests.md](docs/ci-unity-tests.md)). Push a branch and check the pull
request's checks rather than running the suites locally.

## Docs

- [docs/project-layout.md](docs/project-layout.md): where things live in `Pause/Assets`
- [docs/build-and-share.md](docs/build-and-share.md): building and sharing test builds
- [docs/leaderboards.md](docs/leaderboards.md): leaderboards
- [docs/achievements-export/](docs/achievements-export/README.md): achievements export
- [docs/economy.md](docs/economy.md): star dust and shop prices
