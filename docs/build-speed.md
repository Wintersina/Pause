# Build speed

Measured 2026-10-13 on this Mac (11 cores, 18 GB), Unity 6000.3.23f1, dev APK
(`BuildScript.BuildAndroidDev`, IL2CPP, arm64, Medium stripping, 220 MB APK).

## Fast path

    scripts/build-android-dev.sh [--install]     # or: make android-dev-build

It runs `scripts/unity-batch.sh -buildTarget Android -executeMethod
BuildScript.BuildAndroidDev`, prints phase timings (also in
`Pause/Builds/build-timings.txt` and as `[STEP]` lines in the log) and checks
the APK has the manifest and arm64 `libil2cpp.so`. `--install` does
`adb install -r` + launch (`ANDROID_SERIAL=...`).

## Where the time goes (Unity process time)

| Scenario | Unity time | Notes |
|---|---|---|
| Truly cold (no Library, `-buildTarget Android`) | ~244 s | 173 s asset import (ASTC), then 67 s build |
| First build in a cloned Library | 135 s | included a 60 s one-off ILPP configuration stall plus a full script recompile and IL2CPP run (27 s) |
| Warm, nothing changed | 30-32 s | startup + refresh ~10 s, Gradle ~14 s |
| Warm, one script changed (incl. new method) | 42-43 s | script compile 1.5 s, IL2CPP incremental ~8 s |
| Warm, one texture changed | ~30 s | texture reimport 2-3 s |

The Unity build itself is NOT slow. The old 25-60 min figures were wall time:
`scripts/unity-batch.sh` runs a build (HEAVY) only when nothing else runs, in
strict FIFO order, so a build waits behind every running and queued test job.
In this session the same 31 s build waited 207 s, 383 s, 543 s and 545 s
(about 90-95% of the wall time) while three test workers ran. Cutting the
build further gains nothing until the queue wait is cut: run builds when test
runs are not occupying the queue (or give builds their own queue dir, at the
cost of RAM: build peak RSS is ~2-2.4 GB plus Gradle (4 GB heap cap) and clang).

## Warm-cache rules

- Keep the workspace Library; never delete it for a "clean" build. A build
  after only script / texture / scene edits is incremental (Bee, IL2CPP output
  and Gradle project are all reused).
- Never open the project (Editor or batch) at another build target (Standalone,
  iOS): switching the Library's active target reimports every texture (ASTC is
  compressed at import for Android and iOS). Always pass `-buildTarget Android`
  for builds (the script and Makefile do). Tests run without it and stay on the
  Library's current target.
- A new workspace should clone the Library from a warm workspace (APFS clone:
  `cp -cR Pause/Library new/Pause/Library`, seconds) instead of starting cold.
- Cold start without a clone costs ~4 min here (import 173 s). Importer results
  come from the Accelerator when available.
- Refresh: if builds start failing oddly, delete only `Library/Bee` and
  `Library/ilpp.pid`, never `Library/Artifacts`.
- The stale-licensing-helper hang (see unity-batch.sh) is separate.

## Tried, no gain

- Gradle `org.gradle.caching` + `configureondemand`: 13.9 s vs 13.6-14.8 s, reverted.
  Unity stops the Gradle daemon after each build, so the 14 s is JVM start +
  packaging 220 MB.
- Already at the cheap settings: arm64 only, IL2CPP "faster (smaller)", Medium
  stripping, minify off, one build target.

## Next levers (if warm time must drop below ~30 s)

Mono backend for dev builds only (skips IL2CPP, ~8 s on script changes, but
changes behaviour and needs armv7/arm64 Mono), Gradle only (`-buildScriptsOnly`
style APK patching), and skipping the 10 s editor startup with a long-lived
editor instead of batch mode. The queue wait is the real remaining lever.
