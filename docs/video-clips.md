# Gameplay clips (title background, how-to-play loop)

Two muted H.264 loops ship with the game. Both are played by `LoopingClip`
(`Scripts/Video/LoopingClip.cs`): a Unity `VideoPlayer` on a `RenderTexture`.

| file | where | use | encode |
|---|---|---|---|
| `StreamingAssets/Video/title_loop.mp4` | title screen (startS4), behind the menu | `TitleVideo` | 540x960, 30 fps, H.264 Main L3.1, ~1.2 Mbps, no audio, 12.5 s, ~2.0 MB |
| `StreamingAssets/Video/tutorial_demo.mp4` | Options screen (leaderboardS3), beside the Tutorial (replay) button | `HowToPlayClip` | 540x960, 30 fps, H.264 Main L3.1, ~1.0 Mbps, no audio, 7.2 s, ~0.8 MB |

Footage: Space only (pause_showcase_space, HapticGate-brand/marketing/pause/video/raw).
No Tide, Storm or Desert may ever be used here.

## Title loop
Cut 9.0 s - 21.5 s of the showcase (two teleports: onto an enemy at 12.3 s and onto
the red ship at 17.1 s), with the first second of the neighbouring 8.0 s - 9.0 s
crossfaded over the last second, so the file's end flows into its start (9.0 s):

    ffmpeg -ss 9.0 -to 21.5 -i showcase.mp4 -ss 8.0 -to 9.0 -i showcase.mp4 \
      -filter_complex "[0:v]fps=30,scale=540:960,setpts=PTS-STARTPTS,format=yuv420p[a];\
                       [1:v]fps=30,scale=540:960,setpts=PTS-STARTPTS,format=yuv420p[b];\
                       [a][b]xfade=transition=fade:duration=1:offset=11.5,format=yuv420p[v]" \
      -map "[v]" -an -c:v libx264 -profile:v main -level 3.1 -b:v 1200k -maxrate 1500k \
      -bufsize 3000k -preset slow -movflags +faststart title_loop.mp4

`TitleVideo` draws it on a quad at sorting order -400 (above the backdrop tiles, below
the title traffic and logo), centre-cropped to the screen, tinted to 50% brightness.
It is created only once the first frame is decoded (`LoopingClip.BecameReady`), fades
in over 0.8 s, and the rolled MenuBackdrop stays underneath as the fallback.

## How-to-play loop
0.4 s - 7.6 s of the showcase: release (freeze) -> hold -> touch -> teleport.
Placed on the Options screen next to the Tutorial button, NOT inside the tutorial:
the tutorial's timed beats (`TutorialScript`, `Hints`, `TutorialPolishTest`) are
unchanged, so nothing can lengthen them.

## Fallbacks (the screen is exactly the old one when any of these hits)
* no file / `VideoPlayer.errorReceived` (unsupported codec or device) -> `Failed`, nothing drawn
* `SystemInfo.systemMemorySize < 2500` MB -> not started (`LoopingClip.MinMemoryMB`)
* batch mode (headless tests) -> off unless `LoopingClip.AllowInBatchMode`
* `LoopingClip.Disabled = true` (tests, previews)

## Why StreamingAssets, not VideoClip assets
* `VideoPlayer.url` plays the file byte for byte: no editor transcode, no Resources
  packing, the encode in git is the encode that ships (a VideoClip import would
  also be re-encoded per platform when "Transcode" is on).
* Android: StreamingAssets live in the base module's `assets/Video/` of the AAB and
  Unity reads them via `jar:file://` through the asset manager. Checked in the built
  AAB: the zip entries are deflated (0%), but `BundleConfig.pb` lists `**.[mM][pP]4` in
  `uncompressedGlob`, so bundletool stores the mp4s uncompressed in the installed
  APKs (which MediaPlayer needs). Size in the AAB: 2.04 MB + 0.82 MB.
* iOS: plain file in the app bundle, `file://` URL, AVFoundation decodes H.264 natively.
* Mac / Windows: plain file next to the player.
* They are outside every `Resources` folder, so `UnusedAssetGuardTest` is unaffected,
  and they are easy to move into a Play Asset Delivery pack if the base module needs room
  (the code only needs the URL).
* The project needs the built-in `com.unity.modules.video` module (added to the manifest).

## Size (debug-signed Android AAB, `BuildScript.BuildAndroidRelease`, PAUSE_SKIP_APK=1)
before (a8619ad1) 174,830,236 B -> after 178,146,342 B: +3.32 MB (videos 2.86 MB,
gate_open 141 KB, lockup 119 KB, mark 104 KB, the video module and code the rest).

## Gate-opening sound and the logo
See docs/hapticgate-splash.md ("Opening clip", "Brand mark").
