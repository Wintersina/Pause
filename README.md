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

## Credits

Code and original art by **Sina Serati**.

Additional sprites and particles from [Kenney](https://kenney.nl) — *Space Shooter
(Remastered)*, *Pixel Shmup* and *Particle Pack*, all CC0.
