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
	android-build android-deploy android-run android-dev-build android-dev-deploy android-log \
	ios-build ios-deploy ios-run ios-dev-build ios-dev-deploy ios-dev-run ios-devices

help:
	@echo "Pause local commands:"
	@echo "  make test                 Run every editor test suite (AllTests.RunAll)"
	@echo "  make test-fast            Same minus the slow checks (SUITES=A,B to pick suites)"
	@echo "  make mac-run              Build and launch the Mac app"
	@echo "  make mac-dev-run          Build and launch the developer Mac app"
	@echo "  make android-deploy       Build, install, and launch on an Android device"
	@echo "  make android-dev-deploy   Same, with the PAUSE_DEV developer build"
	@echo "  make android-log          Stream Unity logs from the Android device"
	@echo "  make ios-deploy           Build, install, and launch on a connected iPhone"
	@echo "  make ios-dev-deploy       Same, with the PAUSE_DEV developer build"
	@echo "  make ios-devices          List iPhone/iPad device IDs for ios-deploy"
	@echo ""
	@echo "Optional: ANDROID_SERIAL=<serial> selects Android; IOS_DEVICE_ID=<udid> selects iPhone."
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

android-build:
	$(UNITY_CMD) -executeMethod BuildScript.BuildAndroid

android-deploy: android-build
	$(ADB_DEVICE) wait-for-device
	$(ADB_DEVICE) install -r "$(ANDROID_APK)"
	$(ADB_DEVICE) shell monkey -p "$(ANDROID_PACKAGE)" 1

android-run:
	$(ADB_DEVICE) wait-for-device
	$(ADB_DEVICE) shell monkey -p "$(ANDROID_PACKAGE)" 1

android-dev-build:
	$(UNITY_CMD) -executeMethod BuildScript.BuildAndroidDev

android-dev-deploy: android-dev-build
	$(ADB_DEVICE) wait-for-device
	$(ADB_DEVICE) install -r "$(ANDROID_DEV_APK)"
	$(ADB_DEVICE) shell monkey -p "$(ANDROID_PACKAGE)" 1

android-log:
	$(ADB_DEVICE) logcat -s Unity ActivityManager

ios-build:
	$(UNITY_CMD) -executeMethod BuildScript.BuildIOS

ios-deploy: ios-build
	@test -n "$(IOS_DEVICE_ID)" || (echo "Set IOS_DEVICE_ID to a connected device ID; run 'make ios-devices'." >&2; exit 2)
	xcodebuild -project "$(IOS_XCODE_PROJECT)" -scheme Unity-iPhone -configuration Debug -destination "platform=iOS,id=$(IOS_DEVICE_ID)" -derivedDataPath "$(IOS_DERIVED_DATA)" build
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
