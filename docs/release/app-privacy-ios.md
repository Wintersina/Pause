# Apple App Privacy (nutrition label) answers: shared leaderboard

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
