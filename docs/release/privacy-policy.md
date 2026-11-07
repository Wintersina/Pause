# Privacy Policy for Pause

> **Sync note (2026-11-07).** The public policy at https://hapticgategames.com/privacy/ is the source of truth and now also covers AdMob ads, consent choices, in-app purchases and iOS. This file still carries the older Android text below (written before ads and purchases existed), so its "no ads / no purchases" statements are out of date. The new section "Shared cross-platform leaderboard" below is kept word-for-word in step with the site (site PR: Wintersina/hapticgate-site `feat/privacy-shared-leaderboard`). Where the older text below says the developer runs no server or receives no data, the shared leaderboard section overrides it.

Hosted at https://hapticgategames.com/privacy/ (the URL entered in Play Console). The hosted page currently says the game "does not collect your personal information", which conflicts with the Data safety form (User IDs and App interactions declared). Add the paragraph in the next section to the hosted page; the rest of this file is the full text this repo recommends.

## Replacement paragraph for the hosted policy (owner to paste)

> Pause does not collect your personal information on any server of ours, and HapticGate receives none of your data. If you choose to sign in with Google Play Games, the game uses Google's Play Games Services, and your Play Games player ID, your Play Games display name, one saved game ("Pause progress"), your scores and your achievements are sent to and stored by Google, not by us, under Google's Privacy Policy. This happens only after you sign in, which is optional; without signing in, nothing leaves your device and the game works fully offline. Pause contains no ads, no analytics, no crash reporting and no tracking, and does not use your advertising ID. To delete data held by Google, use Google Play Games > Settings > Delete Play Games account & data, or contact dev@hapticgategames.com for help with the request.

Effective date: October 2026
Developer: Sina Serati (HapticGate)
Contact: dev@hapticgategames.com

This policy explains what the Android game Pause (package `me.hapticgate.pause`) does with your information. Short version: Pause has no ads, no analytics, no crash-reporting service and no in-app purchases, and it never asks for your name, email, location, contacts, microphone or camera. The only data that leaves your phone goes to Google Play Games Services, and only if you sign in.

## What stays on your device

Pause saves your progress on your phone: star dust, ships and skins you own, the worlds you have reached, your best scores, Codex discoveries, achievement progress, and settings such as the sound switch. This is stored in the game's private storage on your device. It is not sent to us. Uninstalling the game deletes it (unless you use cloud save, below).

## Google Play Games Services (optional sign-in)

If you sign in to Google Play Games, the game uses Google's Play Games Services to provide:

- **Sign-in.** Google gives the game your Play Games player ID and display name so it can recognise you and show your name on leaderboards. We do not receive your email address or Google account password.
- **Leaderboards.** After a run, the game sends your score, star dust collected, worlds cleared and pauses left to Google's leaderboards. Other players can see your Play Games name and these scores according to your Play Games profile and visibility settings.
- **Achievements.** The game tells Google which achievements you have unlocked.
- **Cloud save.** The game stores one small save file ("Pause progress", the same progress described above) in your Play Games saved games so you can continue on another device or after reinstalling.

This data is processed by Google under [Google's Privacy Policy](https://policies.google.com/privacy) and the Google Play Games Services terms. The game developer does not run its own servers and does not receive a copy of this data. If you do not sign in, none of it is sent, and the game still works fully offline. Without signing in you will not have leaderboards, achievements or cloud save.

## Shared cross-platform leaderboard

Pause has a leaderboard that Android and iOS players share, shown in the game and on hapticgategames.com/pause/ (top scores). This is the one place where HapticGate holds data about you. It only applies if you are signed in to Google Play Games (Android) or Apple Game Center (iOS). If you are not signed in, nothing is uploaded.

**What is collected and stored.** When you sign in, the game creates a Firebase Authentication account linked to your Google Play Games or Game Center identity. For each player we store:

- the Firebase user ID (a random ID; it is not your name or email)
- a display name copied from your Play Games or Game Center profile (up to 24 characters)
- your platform (Android or iOS) and the time of your last update
- your best runs for a Main table and a Power-Ups table: score, star dust, worlds cleared, ship, hearts, hearts at the end, pauses left, skin and power-ups used

**Why.** Only to run the leaderboard: to show ranked scores, to let you appear on the board from either platform, and to protect it from abuse. We do not use this data for advertising, we do not sell it, and Firebase Analytics collection is turned off. We rely on your choice to take part (sharing is on by default and you can turn it off at any time) and on our legitimate interest in running and securing the leaderboard.

**It is public.** Your display name, platform and scores are visible to everyone, in the game and on hapticgategames.com/pause/. Anyone can read the top entries. Your display name comes from your Play Games or Game Center profile, so do not use a name you do not want shown publicly.

**Who processes it.** The data is stored in Cloud Firestore and Firebase Authentication, services of Google (Google LLC, Firebase and Google Cloud), in a project hosted in the United States (multi-region `nam5`). Google acts as our processor and handles it under the [Firebase privacy information](https://firebase.google.com/support/privacy) and the [Google Privacy Policy](https://policies.google.com/privacy). To keep the service from being abused, the game uses Firebase App Check (Play Integrity on Android, App Attest on iOS), which checks that requests come from the genuine app and sends Google some device and app integrity signals for that purpose only.

**Play Games and Game Center boards.** Your scores are also sent to the Google Play Games or Apple Game Center leaderboards, as described above, under Google's and Apple's own policies. That is separate from the shared leaderboard.

**How long we keep it.** Until you delete it or ask us to. We do not expire entries on a schedule.

**Turn sharing off.** In the game, open Options > Leaderboard Settings and switch off *Share scores across platforms*. This stops new uploads. It does **not** delete what is already stored. *Sync now* in the same screen sends your latest bests when sharing is on.

### Delete your leaderboard data (Pause by HapticGate)

(Site anchor: `#delete-leaderboard-data`.)

- **In the game:** Options > Leaderboard Settings > *Delete my shared scores*. This deletes both of your leaderboard entries (Main and Power-Ups) and your Firebase Authentication account, which removes the link between the Firebase ID and your Play Games / Game Center player. Uploads stay off until you turn sharing on again; if you do, a new, separate entry is created.
- **By email, without the app:** if you no longer have the game installed, email dev@hapticgategames.com with your in-game display name and platform (Android or iOS), and we will delete your entries and account. We may ask a question to be sure the entry is yours.
- Deleting here does not delete your scores on the Google Play Games or Apple Game Center boards; manage those through Google or Apple.

## What Pause does not collect

- No advertising and no advertising ID.
- No analytics or crash-reporting services, and no tracking across apps or websites.
- No in-app purchases and no payment information.
- No location, contacts, photos, files, microphone or camera data.
- No account with us; we do not ask you to register.

## Network use

The game uses the internet only to talk to Google Play Games Services when you are signed in. All of that traffic is encrypted in transit by Google's libraries.

## Sharing

We do not sell your data and we do not share it with anyone. Google (Play Games Services) acts as the service provider described above.

## Deleting your data

- **On the device:** clear the game's storage in Android settings or uninstall it.
- **Play Games data (cloud save, achievements, leaderboard scores):** open the Google Play Games app, go to Settings, and use "Delete Play Games account & data", or delete just this game's data there. You can also contact us at dev@hapticgategames.com and we will help with the request, although the data lives with Google.

## Children

Pause is made for a general audience aged 13 and over and is not directed at children under 13. We do not knowingly collect personal information from children.

## Changes

If this policy changes we will update the effective date above and publish the new text at this address.

## Contact

Questions or requests: dev@hapticgategames.com
