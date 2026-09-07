# PAUSE (THE GAME)

## about
Pause is a game where the player uses 'pausing' as a mechanic. The player
'pauses' to dodge obstacles and build a highscore. You pause by simply lifting
your finger off the touch screen.

## history
Pause is built in Unity. It was written near the end of my senior year to show
off some of what I'd learned. The original project was Unity 5.5.1f1 (2016) and
Android-only; it has since been brought up to Unity 6 and made cross-platform.

## requirements
- Unity **6000.3.23f1** (Unity 6)
- iOS builds need Xcode; Android builds need the Android module

## getting started
1. Open Unity Hub, `Add` -> select the `Pause/` folder (that folder is the Unity
   project root, not the repo root).
2. Open the `Assets/spashS7.unity` scene, or just press Play — the build scene
   list is already ordered:
   `spashS7` -> `startS4` -> `leaderboardS3` -> `gameS1` -> `shopS6` -> `creditsS7` -> `tutorialS5`

## platforms
The game runs on iOS, Android and in the editor. Achievements and leaderboards
go through `SocialBridge`, which wraps Unity's built-in Social API — on iOS that
is Game Center with no extra plugin, and on Android it's inert until a social
plugin is installed.

## ads
Ads are currently stubbed out. The 2016 Google Mobile Ads SDK no longer builds
and was removed. `Assets/Scripts/AdMob.cs` keeps the same call surface as before
(`show()` / `hide()` / `isAdsShowwing`) with no-op bodies, so ads can be brought
back by filling in that one file — nothing else needs to change.

## code
All scripts are in C#, under `Pause/Assets/Scripts`.

## install
The release tag has a PRE-ALPHA `.apk`. It hasn't been rebuilt since 2016, so on
a newer phone it will probably look ugly.
