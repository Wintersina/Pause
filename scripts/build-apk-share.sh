#!/bin/bash
# build-apk-share.sh -- a sideload-able APK for friends/testers (make apk-share).
#
# "Unsigned" here means NOT signed with the release/upload key. Android will not
# install a truly unsigned APK, so the file is signed with Unity's debug keystore
# and installs by sideloading. It is not for the Play Store.
#
# Runs BuildScript.BuildAndroidShare through scripts/unity-batch.sh (queue,
# unique log) on THIS checkout's Pause/ project. Release-style: no PAUSE_DEV,
# no development flag, IL2CPP as the project is configured. The release keystore
# passwords are scrubbed from the environment and never read. Committed files
# (version, version code) are not touched.
#
# Output: build/share/Pause-<version>-<shortsha>.apk (gitignored).
set -u
cd "$(dirname "$0")/.." || exit 1
ROOT=$PWD
unset PAUSE_KEYSTORE_PASS PAUSE_KEYALIAS_PASS PAUSE_KEYSTORE_PATH
VERSION=$(awk '/^  bundleVersion:/ {print $2; exit}' Pause/ProjectSettings/ProjectSettings.asset)
CODE=$(awk '/^  AndroidBundleVersionCode:/ {print $2; exit}' Pause/ProjectSettings/ProjectSettings.asset)
SHA=$(git rev-parse --short=8 HEAD 2>/dev/null \
  || jj --no-pager log -r '@-' --no-graph -T 'commit_id.short(8)' 2>/dev/null || echo nogit)
OUT="$ROOT/build/share/Pause-$VERSION-$SHA.apk"
PKG=${ANDROID_PACKAGE:-me.hapticgate.pause}
mkdir -p "$ROOT/build/share"
rm -f "$OUT"
PAUSE_SHARE_APK="$OUT" scripts/unity-batch.sh -projectPath "$ROOT/Pause" -buildTarget Android \
  -executeMethod BuildScript.BuildAndroidShare
rc=$?
[ $rc -eq 0 ] || { echo "[apk-share] Unity build failed (exit $rc)" >&2; exit $rc; }
[ -s "$OUT" ] || { echo "[apk-share] no APK at $OUT" >&2; exit 1; }
unzip -l "$OUT" | grep -q AndroidManifest.xml && unzip -l "$OUT" | grep -q libil2cpp.so \
  || { echo "[apk-share] APK structure check failed" >&2; exit 1; }
rm -rf "$ROOT"/build/share/*_BurstDebugInformation_DoNotShip
SIZE=$(ls -l "$OUT" | awk '{print $5}')
echo "[apk-share] version $VERSION (code $CODE), debug-key signed, not for the Play Store"
echo "[apk-share] $OUT ($((SIZE / 1048576)) MB, $SIZE bytes)"
echo "[apk-share] install: adb install -r \"$OUT\""
