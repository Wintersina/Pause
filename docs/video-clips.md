# Gameplay clips: removed (2026-10-12)

The game-media merge (change wyxotxul, "Game media: HapticGate logo ..., title video
background, how-to-play clip") shipped two muted H.264 loops cut from the Space
showcase footage, played by a `VideoPlayer` wrapper (`Scripts/Video/LoopingClip.cs`)
from `StreamingAssets/Video/`:

| file | where | script |
|---|---|---|
| `title_loop.mp4` (2.0 MB) | behind the title menu (startS4) | `TitleVideo` |
| `tutorial_demo.mp4` (0.8 MB) | Options screen, "HOW TO PLAY" frame | `HowToPlayClip` |

The user rejected both: the home backdrop must be the real in-game backdrop layers
(`MenuBackdrop`, rolled per arrival; never recorded footage), and Options has no
how-to-play clip. The scripts, the clips and the `StreamingAssets` folder were deleted;
`GameMediaTest` keeps any video from coming back. The source footage stays in the
marketing folder (HapticGate-brand/marketing/pause/video/raw), outside the game.

`com.unity.modules.video` is still listed in `Packages/manifest.json` (nothing uses it;
removing it is a separate, optional size cleanup).

## Gate-opening sound and the logo
See docs/hapticgate-splash.md ("Opening clip", "Brand mark").
