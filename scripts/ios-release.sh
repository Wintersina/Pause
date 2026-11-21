#!/usr/bin/env bash
# App Store / TestFlight pipeline after the Unity export (make ios-release).
#   scripts/ios-release.sh pods|archive|export|upload|manual|all
#   (manual = unsigned archive + hand signing + ipa, for a team with no registered devices; then upload)
#
# Env (nothing is read from or written to the repo; never commit keys):
#   PAUSE_IOS_TEAM_ID  HapticGate Apple Developer Team ID (10 chars). Required.
#   ASC_KEY_ID, ASC_ISSUER_ID   App Store Connect API key (Users and Access > Integrations).
#   ASC_KEY_PATH       the AuthKey_<KEYID>.p8 file, default ~/.appstoreconnect/private_keys/AuthKey_$ASC_KEY_ID.p8
#                      (keep it outside the repo). With the key set, xcodebuild may create/update
#                      profiles and certificates without Xcode being signed in; without it the Apple ID
#                      added in Xcode > Settings > Accounts is used.
#   IOS_UPLOAD=1       export with destination=upload (sends the build to App Store Connect right away)
#                      instead of only writing the .ipa.
#   IOS_EXPORT_DIR     the Unity export (default Pause/Builds/iOS-release).
#
# Output: $IOS_EXPORT_DIR/Pause.xcarchive and $IOS_EXPORT_DIR/ipa/Pause.ipa
set -euo pipefail

cd "$(dirname "$0")/.."
DIR="${IOS_EXPORT_DIR:-Pause/Builds/iOS-release}"
ARCHIVE="$DIR/Pause.xcarchive"
IPA_DIR="$DIR/ipa"
WORKSPACE="$DIR/Unity-iPhone.xcworkspace"
PERSONAL_TEAM="6Q8UK2NSZ2"   # free personal team on the dev Mac: cannot ship to the App Store

die() { echo "ios-release: $*" >&2; exit 2; }

require_team() {
  [ -n "${PAUSE_IOS_TEAM_ID:-}" ] || die "set PAUSE_IOS_TEAM_ID to the HapticGate Team ID (developer.apple.com/account > Membership details)."
  [ "$PAUSE_IOS_TEAM_ID" != "$PERSONAL_TEAM" ] || die "$PERSONAL_TEAM is the free personal team; use the HapticGate organization Team ID."
  [[ "$PAUSE_IOS_TEAM_ID" =~ ^[A-Z0-9]{10}$ ]] || die "PAUSE_IOS_TEAM_ID must be 10 letters/digits."
}

# xcodebuild authentication args when an App Store Connect API key is configured.
auth_args() {
  AUTH=()
  if [ -n "${ASC_KEY_ID:-}" ] || [ -n "${ASC_ISSUER_ID:-}" ]; then
    [ -n "${ASC_KEY_ID:-}" ] && [ -n "${ASC_ISSUER_ID:-}" ] || die "set both ASC_KEY_ID and ASC_ISSUER_ID."
    local key="${ASC_KEY_PATH:-$HOME/.appstoreconnect/private_keys/AuthKey_${ASC_KEY_ID}.p8}"
    [ -f "$key" ] || die "API key file not found: $key"
    AUTH=(-authenticationKeyPath "$key" -authenticationKeyID "$ASC_KEY_ID" -authenticationKeyIssuerID "$ASC_ISSUER_ID")
  fi
}

pods() {
  [ -d "$DIR" ] || die "no Unity export at $DIR (run: make ios-release-build)."
  [ -f "$DIR/Podfile" ] || die "no Podfile in $DIR: the Unity export's post-process (AdsIosPods) did not run. Do NOT retry blindly; check the Unity log for '[ADS] iOS' and '[BUILD] iOS' lines and that the export ran with -buildTarget iOS."
  if [ ! -d "$WORKSPACE" ]; then
    (cd "$DIR" && LANG=en_US.UTF-8 pod install)
  fi
  [ -d "$WORKSPACE" ] || die "no $WORKSPACE (no Podfile in the export? install CocoaPods: brew install cocoapods)."
}

archive() {
  require_team; auth_args; pods
  rm -rf "$ARCHIVE"
  xcodebuild archive \
    -workspace "$WORKSPACE" -scheme Unity-iPhone -configuration Release \
    -destination "generic/platform=iOS" -archivePath "$ARCHIVE" \
    DEVELOPMENT_TEAM="$PAUSE_IOS_TEAM_ID" CODE_SIGN_STYLE=Automatic \
    -allowProvisioningUpdates ${AUTH[@]+"${AUTH[@]}"}
}

write_export_options() {
  local dest="export"; [ "${IOS_UPLOAD:-}" = "1" ] && dest="upload"
  cat > "$DIR/ExportOptions.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>method</key><string>app-store-connect</string>
  <key>destination</key><string>$dest</string>
  <key>teamID</key><string>$PAUSE_IOS_TEAM_ID</string>
  <key>signingStyle</key><string>automatic</string>
  <key>uploadSymbols</key><true/>
  <key>manageAppVersionAndBuildNumber</key><false/>
</dict>
</plist>
PLIST
}

export_ipa() {
  require_team; auth_args
  [ -d "$ARCHIVE" ] || die "no archive at $ARCHIVE (run: make ios-archive)."
  write_export_options
  rm -rf "$IPA_DIR"
  xcodebuild -exportArchive -archivePath "$ARCHIVE" -exportPath "$IPA_DIR" \
    -exportOptionsPlist "$DIR/ExportOptions.plist" \
    -allowProvisioningUpdates ${AUTH[@]+"${AUTH[@]}"}
  echo "ios-release: exported to $IPA_DIR"
}

upload() {
  [ -n "${ASC_KEY_ID:-}" ] && [ -n "${ASC_ISSUER_ID:-}" ] || die "upload needs ASC_KEY_ID and ASC_ISSUER_ID (key file in ~/.appstoreconnect/private_keys/AuthKey_<id>.p8)."
  local ipa="$IPA_DIR/Pause.ipa"
  [ -f "$ipa" ] || die "no $ipa (run: make ios-export)."
  # altool reads AuthKey_<id>.p8 from ~/.appstoreconnect/private_keys, ./private_keys or API_PRIVATE_KEYS_DIR.
  if [ -n "${ASC_KEY_PATH:-}" ]; then export API_PRIVATE_KEYS_DIR="$(dirname "$ASC_KEY_PATH")"; fi
  xcrun altool --upload-app -f "$ipa" --type ios --apiKey "$ASC_KEY_ID" --apiIssuer "$ASC_ISSUER_ID"
}

# Fallback for a team with NO registered devices: automatic signing cannot make the development profile the
# archive step wants, and a global manual profile breaks the pod targets. So: archive unsigned, embed the App
# Store profile, codesign frameworks + app by hand with the Apple Distribution identity and the app entitlements,
# zip the ipa. The profile ("iOS Team Store Provisioning Profile: <bundle id>") is cloud-managed: Xcode creates it
# on the first automatic `-exportArchive -allowProvisioningUpdates`, or point IOS_PROFILE at a .mobileprovision.
# Then `upload` (altool). Verified on the first release (build 10018/10019).
manual_sign() {
  require_team; pods
  local bundle="${IOS_BUNDLE_ID:-me.hapticgate.pause}" app="$ARCHIVE/Products/Applications/Pause.app"
  local ident; ident="$(security find-identity -v -p codesigning | sed -n "s/.*\"\(Apple Distribution: .*($PAUSE_IOS_TEAM_ID)\)\".*/\1/p" | head -1)"
  [ -n "$ident" ] || die "no 'Apple Distribution: ... ($PAUSE_IOS_TEAM_ID)' identity in the keychain (Xcode > Settings > Accounts > Manage Certificates)."
  local profile="${IOS_PROFILE:-}"
  if [ -z "$profile" ]; then
    for f in "$HOME/Library/Developer/Xcode/UserData/Provisioning Profiles"/*.mobileprovision "$HOME/Library/MobileDevice/Provisioning Profiles"/*.mobileprovision; do
      [ -f "$f" ] || continue
      if security cms -D -i "$f" 2>/dev/null | grep -q "iOS Team Store Provisioning Profile: $bundle"; then profile="$f"; break; fi
    done
  fi
  [ -f "$profile" ] || die "no App Store profile for $bundle found; set IOS_PROFILE=<file.mobileprovision>."
  rm -rf "$ARCHIVE"; (cd "$DIR" && LANG=en_US.UTF-8 xcodebuild archive -workspace "$(basename "$WORKSPACE")" -scheme Unity-iPhone \
    -configuration Release -destination "generic/platform=iOS" -archivePath "$(basename "$ARCHIVE")" \
    DEVELOPMENT_TEAM="$PAUSE_IOS_TEAM_ID" CODE_SIGNING_ALLOWED=NO CODE_SIGNING_REQUIRED=NO)
  local ent; ent="$(mktemp -t pause-ent).plist"
  cat > "$ent" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>application-identifier</key><string>$PAUSE_IOS_TEAM_ID.$bundle</string>
<key>com.apple.developer.team-identifier</key><string>$PAUSE_IOS_TEAM_ID</string>
<key>beta-reports-active</key><true/>
<key>get-task-allow</key><false/>
<key>com.apple.developer.game-center</key><true/>
<key>com.apple.developer.devicecheck.appattest-environment</key><string>production</string>
<key>com.apple.developer.icloud-container-identifiers</key><array><string>iCloud.$bundle</string></array>
<key>com.apple.developer.icloud-services</key><array><string>CloudDocuments</string></array>
<key>com.apple.developer.ubiquity-container-identifiers</key><array><string>iCloud.$bundle</string></array>
</dict></plist>
PLIST
  cp "$profile" "$app/embedded.mobileprovision"
  for f in "$app"/Frameworks/*.framework; do [ -d "$f" ] && codesign --force --sign "$ident" --timestamp "$f"; done
  codesign --force --sign "$ident" --timestamp --entitlements "$ent" "$app"
  codesign --verify --deep --strict "$app"
  rm -rf "$IPA_DIR"; mkdir -p "$IPA_DIR/stage/Payload"; cp -R "$app" "$IPA_DIR/stage/Payload/"
  (cd "$IPA_DIR/stage" && zip -qry ../Pause.ipa Payload); rm -rf "$IPA_DIR/stage" "$ent"
  echo "ios-release: manually signed $IPA_DIR/Pause.ipa (now: scripts/ios-release.sh upload)"
}

case "${1:-}" in
  pods)    pods ;;
  archive) archive ;;
  export)  export_ipa ;;
  upload)  upload ;;
  manual)  manual_sign ;;
  all)     archive; export_ipa ;;
  *) die "usage: $0 pods|archive|export|upload|manual|all" ;;
esac
