<div align="center">

<img src="docs/pause-title.png" alt="Pause" width="440">

**A one-touch arcade runner where letting go is the only move you have.**

Hold to fly. Lift your finger to freeze the universe — but you only get so many pauses.

</div>

---

A Unity game for **Android and iOS**.

## Builds

Download the latest build from [Releases](https://github.com/Wintersina/Pause/releases).

## Running the project

Requires **Unity 6000.3.23f1**. In Unity Hub, `Add` → select the `Pause/` folder
(the inner one — that's the project root, not the repo root).

```bash
UNITY=/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity

$UNITY -batchmode -quit -projectPath Pause -executeMethod BuildScript.BuildAndroid
$UNITY -batchmode -quit -projectPath Pause -executeMethod BuildScript.BuildIOS
$UNITY -batchmode -quit -projectPath Pause -executeMethod BuildScript.BuildMac
```

Output lands in `Pause/Builds/`.

### Local shortcuts

The root `Makefile` wraps the common local workflows:

```bash
make mac-run              # build and launch the Mac app
make mac-dev-run          # build and launch the PAUSE_DEV Mac app
make android-deploy       # build, install, and launch on a connected Android device
make android-dev-deploy   # same, using the PAUSE_DEV build
make android-log          # stream Unity logs from the device
make ios-devices          # list connected iPhone/iPad device IDs
IOS_DEVICE_ID=<udid> make ios-deploy  # build, install, and launch on that device
IOS_DEVICE_ID=<udid> make ios-dev-deploy  # same, using the PAUSE_DEV build
```

`android-*` commands require `adb` with USB debugging enabled. If more than one
device is connected, select one with `ANDROID_SERIAL=<serial> make android-deploy`.
`ios-*` commands require Xcode, a trusted iPhone/iPad, and a signing configuration
that permits the selected device. Find its ID with `make ios-devices`.
Set `UNITY=/path/to/Unity` if Unity is installed somewhere other than the default
Unity Hub location.

## Credits

- **Developed by** — Sina Serati
- **Audio engineer** — Josh Morris

Thanks to [Kenney](https://kenney.nl) for the *Pixel Shmup* and *Particle Pack*
sprites and particles, all CC0.
