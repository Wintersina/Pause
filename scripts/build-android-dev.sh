#!/bin/bash
# build-android-dev.sh -- the fast path for a developer APK.
#
#   scripts/build-android-dev.sh            build Pause/Builds/Android/Pause-dev.apk
#   scripts/build-android-dev.sh --install  ... and adb install -r + launch it
#   ANDROID_SERIAL=R5CY70N4N9D scripts/build-android-dev.sh --install
#
# Runs BuildScript.BuildAndroidDev through scripts/unity-batch.sh (queue,
# unique log), always with -buildTarget Android so a fresh Library imports for
# Android once instead of Standalone first and Android second. Afterwards it
# prints the phase timings (Builds/build-timings.txt) and sanity-checks the APK.
# Warm Library: ~30-45 s of Unity time. See docs/build-speed.md.
set -u
cd "$(dirname "$0")/.." || exit 1
ROOT=$PWD
APK=Pause/Builds/Android/Pause-dev.apk
PKG=${ANDROID_PACKAGE:-me.sinaserati.Pause}
rm -f Pause/Builds/build-timings.txt
start=$(date +%s)
scripts/unity-batch.sh -projectPath "$ROOT/Pause" -buildTarget Android -executeMethod BuildScript.BuildAndroidDev
rc=$?
echo "[build-android-dev] wall $(( $(date +%s) - start )) s (includes queue wait), exit $rc"
[ $rc -eq 0 ] || exit $rc
[ -s "$APK" ] || { echo "[build-android-dev] no APK at $APK" >&2; exit 1; }
[ -f Pause/Builds/build-timings.txt ] && grep -vE "Packaging assets|Building scene" Pause/Builds/build-timings.txt | awk '$1+0>=1.0'
ls -l "$APK" | awk '{print "[build-android-dev] " $5 " bytes " $NF}'
unzip -l "$APK" | grep -qE "lib/arm64-v8a/libil2cpp.so" && unzip -l "$APK" | grep -q AndroidManifest.xml \
  || { echo "[build-android-dev] APK structure check failed" >&2; exit 1; }
if [ "${1:-}" = "--install" ]; then
  ADB=(adb ${ANDROID_SERIAL:+-s "$ANDROID_SERIAL"})
  "${ADB[@]}" install -r "$APK" && "${ADB[@]}" shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1
fi
