# Local build, launch, and Android-device helpers for Pause.
#
# Override UNITY, ADB, or ANDROID_SERIAL when your local setup differs, e.g.:
#   make android-dev-deploy ANDROID_SERIAL=emulator-5554

UNITY ?= /Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity
ADB ?= adb
PROJECT ?= Pause
ANDROID_PACKAGE ?= me.hapticgate.pause
IOS_BUNDLE_ID ?= me.hapticgate.pause
# Required by ios-deploy. Find it with `make ios-devices`.
IOS_DEVICE_ID ?=
# Sideload with a free personal Apple ID: IOS_NO_GAMECENTER=1 builds with empty
# entitlements (no Game Center / iCloud), IOS_TEAM_ID=<10-char team id> signs
# automatically for that team, e.g.
#   make ios-deploy IOS_DEVICE_ID=<udid> IOS_NO_GAMECENTER=1 IOS_TEAM_ID=ABCDE12345
IOS_NO_GAMECENTER ?=
IOS_TEAM_ID ?=

MAC_APP := $(PROJECT)/Builds/Mac/Pause.app
MAC_DEV_APP := $(PROJECT)/Builds/Mac/Pause-dev.app
ANDROID_APK := $(PROJECT)/Builds/Android/Pause.apk
ANDROID_DEV_APK := $(PROJECT)/Builds/Android/Pause-dev.apk
IOS_XCODE_PROJECT := $(PROJECT)/Builds/iOS/Unity-iPhone.xcodeproj
IOS_DEV_XCODE_PROJECT := $(PROJECT)/Builds/iOS-dev/Unity-iPhone.xcodeproj
IOS_DERIVED_DATA := $(PROJECT)/Builds/iOS/DerivedData
IOS_DEV_DERIVED_DATA := $(PROJECT)/Builds/iOS-dev/DerivedData
IOS_APP := $(IOS_DERIVED_DATA)/Build/Products/Debug-iphoneos/Pause.app
IOS_DEV_APP := $(IOS_DEV_DERIVED_DATA)/Build/Products/Debug-iphoneos/Pause.app

ADB_DEVICE = $(ADB) $(if $(ANDROID_SERIAL),-s $(ANDROID_SERIAL))
# Through scripts/unity-batch.sh: one Unity batch process at a time
# machine-wide (a queue), helpers cleaned up, a unique log per run.
UNITY_CMD = UNITY="$(UNITY)" scripts/unity-batch.sh -projectPath "$(PROJECT)"

.PHONY: test test-fast help mac-build mac-run mac-dev-build mac-dev-run \
	apk-share android-build android-release android-release-signed android-deploy android-run android-dev-build android-dev-deploy android-log \
	ios-build ios-deploy ios-run ios-dev-build ios-dev-deploy ios-dev-run ios-devices \
	devices install install-android install-ios check-android-target check-ios-target run-android run-ios build-ios-sim

help:
	@echo "Pause local commands:"
	@echo "  make test                 Run every editor test suite (AllTests.RunAll)"
	@echo "  make test-fast            Same minus the slow checks (SUITES=A,B to pick suites)"
	@echo "  make mac-run              Build and launch the Mac app"
	@echo "  make mac-dev-run          Build and launch the developer Mac app"
	@echo "  make android-deploy       Build, install, and launch on an Android device"
	@echo "  make android-dev-deploy   Same, with the PAUSE_DEV developer build"
	@echo "  make apk-share            Debug-key-signed release-style APK for friends -> build/share/ (NOT the release key; not for Play)"
	@echo "  make android-release-signed  Prompts (hidden) for the keystore password, then runs android-release"
	@echo "  make android-release      Signed Play AAB + APK + *.symbols.zip in Builds/Android/Release (needs PAUSE_KEYSTORE_PASS, PAUSE_KEYALIAS_PASS)"
	@echo "  make android-log          Stream Unity logs from the Android device"
	@echo "  make ios-deploy           Build, install, and launch on a connected iPhone"
	@echo "  make ios-dev-deploy       Same, with the PAUSE_DEV developer build"
	@echo "  make ios-devices          List iPhone/iPad device IDs for ios-deploy"
	@echo ""
	@echo "  make devices              List running Android emulators and booted iOS simulators"
	@echo "  make install              install-android + install-ios (each builds first, one after the other)"
	@echo "  make install-android      build the dev APK, then adb install -r it on every running emulator (or ANDROID_SERIAL=...) and launch it"
	@echo "  make install-ios          Build the simulator app, then install it on the booted iOS simulator(s) and launch it"
	@echo "  make build-ios-sim        Unity iOS simulator export + xcodebuild -sdk iphonesimulator"
	@echo "  make run-android / run-ios  Launch the already-installed app on the emulators / simulators"
	@echo "Emulator flow: 'make install-android' / 'make install-ios' build the latest dev build first, then install and launch; 'make run-*' only launches."
	@echo "Optional: ANDROID_SERIAL=<serial> selects Android; IOS_DEVICE_ID=<udid> selects iPhone."
	@echo "Free Apple ID sideload: make ios-deploy IOS_NO_GAMECENTER=1 IOS_TEAM_ID=<team id>"
	@echo "          UNITY=<path> overrides Unity."

test:
	$(UNITY_CMD) -executeMethod AllTests.RunAll

test-fast:
	$(UNITY_CMD) -executeMethod AllTests.RunFast $(if $(SUITES),-suites $(SUITES))

mac-build:
	$(UNITY_CMD) -executeMethod BuildScript.BuildMac

mac-run: mac-build
	open "$(MAC_APP)"

mac-dev-build:
	$(UNITY_CMD) -executeMethod BuildScript.BuildMacDev

mac-dev-run: mac-dev-build
	open "$(MAC_DEV_APP)"

apk-share:
	scripts/build-apk-share.sh

android-build:
	$(UNITY_CMD) -buildTarget Android -executeMethod BuildScript.BuildAndroid

android-deploy: android-build
	$(ADB_DEVICE) wait-for-device
	$(ADB_DEVICE) install -r "$(ANDROID_APK)"
	$(ADB_DEVICE) shell monkey -p "$(ANDROID_PACKAGE)" 1

android-run:
	$(ADB_DEVICE) wait-for-device
	$(ADB_DEVICE) shell monkey -p "$(ANDROID_PACKAGE)" 1

android-release:
	$(UNITY_CMD) -executeMethod BuildScript.BuildAndroidRelease

# Prompts silently for the keystore password (skipped if PAUSE_KEYSTORE_PASS is already set),
# exports it for Unity as env vars only (never on a command line), then runs android-release.
android-release-signed:
	@bash -c 'if [ -z "$$PAUSE_KEYSTORE_PASS" ]; then \
		read -r -s -p "Keystore password: " PAUSE_KEYSTORE_PASS; echo >&2; \
	fi; \
	if [ -z "$$PAUSE_KEYSTORE_PASS" ]; then echo "android-release-signed: empty keystore password, aborting" >&2; exit 1; fi; \
	export PAUSE_KEYSTORE_PASS; \
	export PAUSE_KEYALIAS_PASS="$${PAUSE_KEYALIAS_PASS:-$$PAUSE_KEYSTORE_PASS}"; \
	exec $(MAKE) android-release'

android-dev-build:
	$(UNITY_CMD) -buildTarget Android -executeMethod BuildScript.BuildAndroidDev

android-dev-deploy: android-dev-build
	$(ADB_DEVICE) wait-for-device
	$(ADB_DEVICE) install -r "$(ANDROID_DEV_APK)"
	$(ADB_DEVICE) shell monkey -p "$(ANDROID_PACKAGE)" 1

android-log:
	$(ADB_DEVICE) logcat -s Unity ActivityManager

ios-build:
	$(if $(IOS_NO_GAMECENTER),PAUSE_IOS_NO_GAMECENTER=1) $(if $(IOS_TEAM_ID),PAUSE_IOS_TEAM_ID=$(IOS_TEAM_ID)) $(UNITY_CMD) -executeMethod BuildScript.BuildIOS

ios-deploy: ios-build
	@test -n "$(IOS_DEVICE_ID)" || (echo "Set IOS_DEVICE_ID to a connected device ID; run 'make ios-devices'." >&2; exit 2)
	xcodebuild -project "$(IOS_XCODE_PROJECT)" -scheme Unity-iPhone -configuration Debug -destination "platform=iOS,id=$(IOS_DEVICE_ID)" -derivedDataPath "$(IOS_DERIVED_DATA)" $(if $(IOS_TEAM_ID),-allowProvisioningUpdates )build
	xcrun devicectl device install app --device "$(IOS_DEVICE_ID)" "$(IOS_APP)"
	xcrun devicectl device process launch --device "$(IOS_DEVICE_ID)" "$(IOS_BUNDLE_ID)"

ios-run:
	@test -n "$(IOS_DEVICE_ID)" || (echo "Set IOS_DEVICE_ID to a connected device ID; run 'make ios-devices'." >&2; exit 2)
	xcrun devicectl device process launch --device "$(IOS_DEVICE_ID)" "$(IOS_BUNDLE_ID)"

ios-dev-build:
	$(UNITY_CMD) -executeMethod BuildScript.BuildIOSDev

ios-dev-deploy: ios-dev-build
	@test -n "$(IOS_DEVICE_ID)" || (echo "Set IOS_DEVICE_ID to a connected device ID; run 'make ios-devices'." >&2; exit 2)
	xcodebuild -project "$(IOS_DEV_XCODE_PROJECT)" -scheme Unity-iPhone -configuration Debug -destination "platform=iOS,id=$(IOS_DEVICE_ID)" -derivedDataPath "$(IOS_DEV_DERIVED_DATA)" build
	xcrun devicectl device install app --device "$(IOS_DEVICE_ID)" "$(IOS_DEV_APP)"
	xcrun devicectl device process launch --device "$(IOS_DEVICE_ID)" "$(IOS_BUNDLE_ID)"

ios-dev-run:
	@test -n "$(IOS_DEVICE_ID)" || (echo "Set IOS_DEVICE_ID to a connected device ID; run 'make ios-devices'." >&2; exit 2)
	xcrun devicectl device process launch --device "$(IOS_DEVICE_ID)" "$(IOS_BUNDLE_ID)"

ios-devices:
	xcrun devicectl list devices

# ---- Emulators / simulators --------------------------------------------------
# install-* always build the latest dev app first, then install on whatever is
# running; run-* only launches. Real phones are never
# touched: only emulator-* adb devices and booted simulators are targeted, unless
# you name one yourself with ANDROID_SERIAL=<serial> / IOS_SIM=<udid>.
# The emulator's ABI must be in the APK (project ships arm64-v8a: an arm64
# emulator on Apple Silicon is fine, an x86_64 emulator is not).

ANDROID_TARGETS = $(if $(ANDROID_SERIAL),$(ANDROID_SERIAL),$(shell $(ADB) devices 2>/dev/null | awk '$$1 ~ /^emulator-/ && $$2 == "device" {print $$1}'))
ANDROID_ACTIVITY ?= com.unity3d.player.UnityPlayerActivity
IOS_SIM ?= booted
IOS_SIM_DERIVED_DATA := $(PROJECT)/Builds/iOS-sim/DerivedData
IOS_SIM_APP := $(IOS_SIM_DERIVED_DATA)/Build/Products/Debug-iphonesimulator/Pause.app

devices:
	@echo "Android (adb):"; $(ADB) devices -l | sed '1d;/^$$/d;s/^/  /'
	@echo "iOS simulators (booted):"; xcrun simctl list devices booted | grep -E '\(Booted\)' | sed 's/^/ /' || true

check-android-target:
	@test -n "$(ANDROID_TARGETS)" || { echo "No running Android emulator (adb devices). Start one or pass ANDROID_SERIAL=<serial>." >&2; exit 2; }

check-ios-target:
	@xcrun simctl list devices $(if $(filter booted,$(IOS_SIM)),booted,) | grep -q 'Booted' || { echo "No booted iOS simulator. Boot one or pass IOS_SIM=<udid>." >&2; exit 2; }

# Always rebuilds the dev APK from the current source first (fail fast with no emulator).
install-android: check-android-target android-dev-build
	@test -f "$(ANDROID_DEV_APK)" || { echo "$(ANDROID_DEV_APK) missing after build." >&2; exit 2; }
	@set -e; for d in $(ANDROID_TARGETS); do \
		echo "== $$d: install + launch"; \
		$(ADB) -s $$d install -r "$(ANDROID_DEV_APK)"; \
		$(ADB) -s $$d shell am start -n "$(ANDROID_PACKAGE)/$(ANDROID_ACTIVITY)" >/dev/null; \
	done

run-android:
	@test -n "$(ANDROID_TARGETS)" || { echo "No running Android emulator." >&2; exit 2; }
	@set -e; for d in $(ANDROID_TARGETS); do \
		$(ADB) -s $$d shell am start -n "$(ANDROID_PACKAGE)/$(ANDROID_ACTIVITY)" >/dev/null; \
	done

# Simulator build: Unity exports an Xcode project with the Simulator SDK
# (BuildScript.BuildIOSSim), then xcodebuild compiles it for iphonesimulator.
build-ios-sim:
	$(UNITY_CMD) -executeMethod BuildScript.BuildIOSSim
	xcodebuild -project "$(PROJECT)/Builds/iOS-sim/Unity-iPhone.xcodeproj" -scheme Unity-iPhone -configuration Debug -sdk iphonesimulator -destination "generic/platform=iOS Simulator" -derivedDataPath "$(IOS_SIM_DERIVED_DATA)" build

# Always rebuilds the simulator app from the current source first.
install-ios: check-ios-target build-ios-sim
	@test -d "$(IOS_SIM_APP)" || { echo "$(IOS_SIM_APP) missing after build." >&2; exit 2; }
	xcrun simctl install $(IOS_SIM) "$(IOS_SIM_APP)"
	xcrun simctl launch $(IOS_SIM) "$(IOS_BUNDLE_ID)"

run-ios:
	xcrun simctl launch $(IOS_SIM) "$(IOS_BUNDLE_ID)"

# Sequential sub-makes so two Unity batch builds never overlap, even under make -j.
install:
	@$(MAKE) install-android
	@$(MAKE) install-ios
