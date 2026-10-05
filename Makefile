# Local build, launch, and Android-device helpers for Pause.
#
# Override UNITY, ADB, or ANDROID_SERIAL when your local setup differs, e.g.:
#   make android-dev-deploy ANDROID_SERIAL=emulator-5554

UNITY ?= /Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity
ADB ?= adb
PROJECT ?= Pause
ANDROID_PACKAGE ?= me.sinaserati.Pause

MAC_APP := $(PROJECT)/Builds/Mac/Pause.app
MAC_DEV_APP := $(PROJECT)/Builds/Mac/Pause-dev.app
ANDROID_APK := $(PROJECT)/Builds/Android/Pause.apk
ANDROID_DEV_APK := $(PROJECT)/Builds/Android/Pause-dev.apk

ADB_DEVICE = $(ADB) $(if $(ANDROID_SERIAL),-s $(ANDROID_SERIAL))
UNITY_CMD = "$(UNITY)" -batchmode -quit -projectPath "$(PROJECT)"

.PHONY: help mac-build mac-run mac-dev-build mac-dev-run \
	android-build android-deploy android-run android-dev-build android-dev-deploy android-log

help:
	@echo "Pause local commands:"
	@echo "  make mac-run              Build and launch the Mac app"
	@echo "  make mac-dev-run          Build and launch the developer Mac app"
	@echo "  make android-deploy       Build, install, and launch on an Android device"
	@echo "  make android-dev-deploy   Same, with the PAUSE_DEV developer build"
	@echo "  make android-log          Stream Unity logs from the Android device"
	@echo ""
	@echo "Optional: ANDROID_SERIAL=<serial> selects a device; UNITY=<path> overrides Unity."

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
