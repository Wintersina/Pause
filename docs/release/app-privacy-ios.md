# Apple App Privacy (nutrition label) answers: shared leaderboard + AdMob ads

**Update 2026-11-21: ads are ON in iOS v1.0 (`AdsConfig.EnabledOnIos = true`).** The label must therefore include the AdMob rows in the next section, merged with the leaderboard rows further down. The leaderboard section's "Data Used to Track You: nothing" no longer holds for the app as a whole: the app tracks (IDFA, with ATT consent) for third-party advertising. The privacy manifest already sets `NSPrivacyTracking = true` (IOSReleasePostProcess); tracking domains come from the Google Mobile Ads SDK's own manifest.

## AdMob ads rows (add to the label)

Based on Google's published "Data disclosure" for the Google Mobile Ads SDK on iOS (https://developers.google.com/admob/ios/privacy/strategies and the Apple data-disclosure page linked there). Re-fetch at submission time; rows marked **CONFIRM** are uncertain.

| Apple data type | Collected | Linked to user | Used for tracking | Purposes | Notes |
|---|---|---|---|---|---|
| Identifiers > Device ID (IDFA) | Yes | Yes | **Yes** | Third-Party Advertising, Analytics (Google lists ad measurement) | Collected when the player allows tracking in the ATT prompt. Google lists Device ID as collected, linked and used for tracking. |
| Usage Data > Product Interaction | Yes | Yes | **Yes** | Third-Party Advertising, Analytics | Ad impressions / taps. **CONFIRM** the linked / tracking flags against Google's current table. |
| Usage Data > Advertising Data | Yes | Yes | **Yes** | Third-Party Advertising | Ad data about what the player saw. **CONFIRM** against Google's current table. |
| Diagnostics > Performance Data | Yes | No | No | App Functionality, Analytics | Google lists SDK performance (e.g. launch time, hang rate). **CONFIRM** linked flag. |
| Diagnostics > Crash Data | **CONFIRM** | **CONFIRM** | No | App Functionality | Google's table has listed crash data for the SDK; confirm at submission. |
| Location > Coarse Location | Yes | Yes | **CONFIRM** | Third-Party Advertising | Derived from the IP address by Google; Google lists it. **CONFIRM** tracking flag. |
| Purchases | No | n/a | n/a | n/a | Remove Ads purchases are handled by Apple; the ad SDK does not read them. |

Tracking answer: **Yes, the app tracks** (the IDFA is used for third-party advertising). Keep the ATT prompt text (`AdsConfig.TrackingUsageDescription`) consistent with this. Players who deny ATT still get (non-personalised) ads, so the declaration stays "used for tracking" for those who allow it.

The sections below are the leaderboard-only answers from 2026-11-07; the final label is the union of both tables.

Drafted 2026-11-07 for App Store Connect > App Privacy. **Draft only, not entered anywhere.** Scope: only the data added by the shared cross-platform leaderboard (Firebase Auth + Cloud Firestore + App Check). The label must cover the whole app: merge these rows with the AdMob/IDFA rows and anything else (see `docs/release/no-ads.md` and the site privacy policy for ads). Anything marked **CONFIRM** is an uncertain mapping, not a decision.

Definitions used (fetched 2026-11-07 from https://developer.apple.com/app-store/app-privacy-details/):

- **Collect**: "transmitting data off the device in a way that allows you and/or your third-party partners to access it for a period longer than what is necessary to service the transmitted request in real time." Stored leaderboard rows are collected.
- **Tracking**: "linking data collected from your app about a particular end-user or device ... with Third-Party Data for targeted advertising or advertising measurement purposes, or sharing data ... with a data broker." The leaderboard does neither.
- **Linked to the user**: data "often linked to the user's identity, unless specific privacy protections are put in place before collection to de-identify or anonymize it." Rows keyed by a Firebase uid tied to a Game Center player are linked.
- Apple makes the developer responsible for the practices of third-party partners whose code is in the app (Firebase SDK), and says nothing about Game Center or about data shown publicly to other users.

## Answers

| Apple data type | Collected | Linked to user | Used for tracking | Purposes | Why / source |
|---|---|---|---|---|---|
| Contact Info > Name | Yes | Yes | No | App Functionality | Game Center alias copied into the public leaderboard row (up to 24 chars). Apple's "Name" example is "first or last name"; a Game Center alias is closer to a handle. **CONFIRM:** Name vs only User ID (User ID lists "screen name, handle"). Declaring both is the safe over-declaration. |
| Identifiers > User ID | Yes | Yes | No | App Functionality | Firebase user id (document id) linked to the Game Center player; Apple lists "screen name, handle, account ID, assigned user ID". |
| User Content > Gameplay Content | Yes | Yes | No | App Functionality | Best runs: score, star dust, worlds cleared, ship, hearts, end hearts, pauses left, skin, power-ups used. Apple's example: "saved games, multiplayer matching or gameplay logic, or user-generated content in-game". **CONFIRM:** Gameplay Content vs Usage Data > Other Usage Data / Product Interaction; Gameplay Content fits best. |
| Usage Data > Product Interaction | Unchanged by this feature | n/a | n/a | n/a | Only relevant if already declared for AdMob. Not added by the leaderboard. |
| Identifiers > Device ID | **CONFIRM** | **CONFIRM** | No | App Functionality (abuse protection) | App Attest / Firebase Installations send an app-instance id and attestation to Google. Apple's Device ID is "advertising identifier, or other device-level ID". Likely Yes (linked, if the install id is tied to the uid) for the abuse-protection purpose. Not confirmed from Firebase docs; check Firebase's "Data collected by Firebase" page for Auth, Firestore, App Check and Installations. |
| Diagnostics (Crash / Performance / Other) | No (from this feature) | n/a | n/a | n/a | No Crashlytics, no Performance Monitoring, no Analytics. Firebase Analytics collection is disabled. **CONFIRM** the SDK really sends none with Analytics absent (it is not imported). |
| Location, Contacts, Photos, Health, Financial, Browsing, Search, Purchases, Sensitive Info | No | n/a | n/a | n/a | Not touched. IP address is received by Google servers but has no Apple category except Coarse Location if used; **CONFIRM** (we do not use it). |

Purposes not selected: Third-Party Advertising, Developer's Advertising or Marketing, Analytics, Product Personalization, Other Purposes.

**Data Used to Track You:** nothing from the leaderboard. (AdMob/IDFA tracking is declared separately.)

**Optional-disclosure exemptions:** none apply. The data is uploaded by default, is part of a headline feature and is shown publicly, so it cannot use Apple's "infrequent, optional, user-provided each time" exemption.

**Privacy Policy URL:** https://hapticgategames.com/privacy/. **User Privacy Choices URL (optional field):** https://hapticgategames.com/privacy/#delete-leaderboard-data

## Other App Store items to remember

- **Account deletion (Guideline 5.1.1(v)):** apps that support account creation must offer in-app deletion. The Firebase account is created automatically from Game Center, and "Delete my shared scores" deletes it. **CONFIRM** with App Review notes whether this counts as an account and is accepted as the deletion path.
- **Privacy manifest / required-reason APIs:** the Firebase SDKs ship their own `PrivacyInfo.xcprivacy`. **CONFIRM** after the SDK import PR that the Xcode build includes them and no extra entries are needed.
