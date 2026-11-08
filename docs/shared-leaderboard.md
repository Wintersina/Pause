# Shared leaderboard (Firebase, Spark plan): design note

Status: **phases 1 and 2 implemented, dormant** (backend rules + SDK-free client service + Firebase adapter under `#if PAUSE_FIREBASE`, and the UI: Leaderboard Settings modal, shared panel). A follow-up PR imports the SDK (see "Enabling it").

**The boards are publicly readable (names, platform marks and scores).** Anyone, signed in or not, can `get` a document and `list` up to 25 documents of `main` and `power`; the website shows the top scores this way. Writes and deletes stay owner-only. The privacy policy and store data-safety answers must say that the display name and scores are public. Reading is a change from the first draft below, which required sign-in for reads.

## As built (differences from the first draft of this note)

* **SDK-free first PR.** No Firebase package, EDM bump, Gradle change or config file is in the repo yet. `FirebaseSharedBoard` is compiled only with `PAUSE_FIREBASE`, so **CI has never compiled it**; the import PR is its first build. Things to confirm there: the iOS App Check factory (Firebase's Unity page shows `DeviceCheckProviderFactory`; register that provider in the console, or App Attest if the SDK exposes a factory), `GameCenterAuthProvider.GetCredentialAsync`, the `FirestoreException.ErrorCode` mapping.
* **No `count()` yet.** A player outside the top 10 gets their own row (one document read, so about 11 reads per open as budgeted) with **rank 0, no number**. Add the aggregate when the SDK is confirmed to have it.
* **Denied is one case.** Permission denied (rules or App Check) cannot be told apart client-side: it backs off like other errors and parks the shared board for the session after 8 refusals; `FlushNow` (sync now) tries again.
* **Cache invalidation.** An own upload drops that table's cached pages (all three sorts), not just the rank.
* **Backfill on first sign-in.** Once per install the service signs in to Firebase even with an empty queue and offers the device's bests (native marks plus the normal local best), as the native `BindAccount` does. A different uid re-queues them.
* **Everything is under `Pause/Assets/Scripts/Core/Leaderboards/Shared/`**; tests are `SharedLeaderboardTest` (service, fake, C# rules mirror) and `firebase/rules.test.js` (emulator), both reading `firebase/test-vectors.json`.

## Phase 2: the UI (as built)

* **Options > LEADERBOARD SETTINGS** (`LeaderboardSettingsOptions`, row at y 340 above VIBRATION, every build) opens `LeaderboardSettingsPanel`, a modal on its own overlay canvas in the leaderboard panel's look. Top to bottom: one line on what is shared (display name and scores, public), **SYNC NOW**, its result line, **SHARE SCORES: ON/OFF**, a note that turning sharing off stops uploads but deletes nothing, **DELETE MY SHARED SCORES** (separate; pressing it shows an in-game CANCEL / DELETE step, no OS dialog; then an outcome line), and **PRIVACY POLICY** (opens `ExternalLinks.PrivacyPolicy`, the link the home screen already uses). BACK and the BackNavigator back button close it. The middle scrolls on a short screen; type is 16 units or more, every button is a 100-unit target.
* **Sync now** runs `LeaderboardSync.Now()` (native boards, and the shared board while sharing is on). Results: `Synced` once both queues are empty, `Offline, will retry` if they are still not empty after 12 s (the queues keep retrying on their own), `Sign in to sync` when neither Play Games / Game Center nor the shared board is signed in.
* **Persisted state:** `shared_leaderboard_on` (PlayerPrefs, default on, `SharedLeaderboardService.SharingOn`). The modal adds no other key. Delete turns it off through the service (uploads stay off until the player turns it back on).
* **Home Leaderboard button** (`LeaderboardPanel.OpenHome`): the shared board when `SharedLeaderboardService.BoardUsable` (sharing on, backend available, Firebase user signed in), otherwise the native panel exactly as before. Android builds now use the real Firebase backend (`PAUSE_FIREBASE`, `FirebaseSharedBoard`), so the shared board opens there once the player is signed in to Firebase; the Editor, Mac and any build where Firebase is unavailable still open the native panel. iOS has the same code path but is unverified. App Check is not enforced yet.
* **Shared panel:** the same panel in shared mode. MAIN tab with SCORE / DUST / WORLDS headers and a POWER-UPS tab; each header calls `SharedLeaderboardService.LoadBoard(table, column, ...)` and shows the rows in the order returned (never re-sorted). The player's own row shows even off the page; rank 0 shows `#--` and no unranked note. The service's caching rule applies (5 min, retry bypasses it at most once a minute per board, a failed refresh serves the cached page and shows `UPDATED N MIN AGO`). If the load fails and there is no cache, the error state offers RETRY and **SHOW <PLAY GAMES / GAME CENTER> BOARD**, which turns the panel into the native board in place.
* **Platform mark:** each shared row shows a small generic mark by the name: a robot head (Android) or an apple (iOS). The panel's text stack is the legacy `UnityEngine.UI.Text` with Orbitron, which has no colour emoji (and no TextMeshPro emoji sprite asset is in the project), so the marks are 12 x 12 pixel-art sprites built at runtime (`PlatformMark`), drawn 24 units wide. They are not the Google, Android or Apple logos and never the Apple logo character.
* **Platform field:** every document carries `platform` (`"android"` or `"ios"`). The rules require it to match the sign-in provider: `request.auth.token.firebase.sign_in_provider` is `playgames.google.com` for `android` and `gc.apple.com` for `ios` (`PlayGamesAuthProvider` / `GameCenterAuthProvider` provider ids; Firebase's ID token documentation lists `sign_in_provider` values). The backend writes it (`ISharedBoardBackend.Platform`), `SharedBoardLimits.PlatformOk` mirrors the rule, and `test-vectors.json` covers a wrong provider, a missing and an unknown platform. No migration: nothing is deployed yet.

## SDK import (done in the import PR)

* Firebase Unity SDK 13.17.1 from Google's `dl.google.com/firebase/sdk/unity/dotnet4/` .unitypackages (Auth, Firestore, AppCheck; Google publishes no UPM registry for Firebase). Vendored without the desktop natives, tvOS libs and pdbs, so the Mac editor cannot run Firebase and the adapter fails soft there (shared board off).
* Analytics: the SDK's Android dependencies include `firebase-analytics`; `Assets/Plugins/Android/FirebasePrivacy.androidlib` turns collection off (`firebase_analytics_collection_deactivated`, `google_analytics_adid_collection_enabled`). iOS pulls no Analytics.
* iOS App Check uses `AppAttestProviderFactory`; the debug provider is compiled only for the editor, `DEVELOPMENT_BUILD` and `PAUSE_DEV` (guarded by `SharedLeaderboardTest`).

## Enabling it (follow-up PR, after the console work)

1. Import Firebase Unity SDK 13.17.x (Auth, Firestore, AppCheck only), bump EDM to 1.2.189, resolve, fix the Gradle collisions, strip the desktop native libs.
2. Put `google-services.json` and `GoogleService-Info.plist` in `Pause/Assets/Firebase/Config/` (the Firebase editor tooling finds them anywhere under `Assets/`; done in the import PR), the web client id in `PlayGamesSettings.asset` (`mWebClientId`).
3. Define `PAUSE_FIREBASE` for Android and iOS (Player settings, scripting define symbols).
4. Deploy `firebase/firestore.rules`; build a signed release; check the App Check metrics; only then enforce App Check.

The shared board sits next to the Play Games / Game Center boards in `docs/leaderboards.md`. It never replaces them: native boards keep working exactly as today whatever happens here.

## 1. Client approach: Firebase Unity SDK (Auth + Firestore + App Check)

**Decision: the Firebase Unity SDK, version 13.17.1, not REST.**

Facts checked against official docs (Firebase release notes, Unity setup page, Identity Toolkit and App Check discovery documents, today 2026-11-07):

| Question | Finding |
|---|---|
| Unity 6000.3 | Setup page says "Unity 2021 LTS or later"; 14.0.0 raised the floor to 2022 LTS. **No page mentions Unity 6.** Open issue firebase-unity-sdk #1360 reports a Unity 6.2 Android build failing with SDK 13.5.0 (manifest/application tag). So: expected to work, not guaranteed, and CI cannot prove it. |
| Version pick | 13.17.1 (Oct 23 2026: the Firestore certificate security fix on the 13.x line, Android BoM 34.x, iOS still CocoaPods). **Not 14.x**: 14.0.0 (Oct 15) forces Swift Packages on iOS and is three weeks old. Android minSdk 24 is below ours (25). |
| EDM | 13.17.0 notes list EDM4U **1.2.189**; the project has **1.2.182** (shared by Google Mobile Ads 11.6.0 and Play Games 2.3.0, both plain `Assets/` copies, so the upgrade is a file swap). Needs the EDM bump plus a full resolve. |
| Gradle collisions | `mainTemplate.gradle` already pins `kotlin-bom:1.8.22`, `androidx.fragment:1.7.1`, `play-services-ads:25.5.0`, `play-services-games-v2:22.1.0`. Firebase BoM 34 libraries pull newer Kotlin and play-services-base; the kotlin pin will probably have to move. Only a real Android build shows it. |
| AAB size | ARM64 only (`AndroidTargetArchitectures: 2`). I estimate Auth + Firestore + App Check add on the order of 10 to 20 MB. This is an **estimate, not from docs**. The cap is `AndroidAppBundleSizeToValidate: 200`. Measure on the first release build after import. |
| Install route | Official `.unitypackage` (Auth, Firestore, AppCheck only; no Analytics). Whether a Google scoped registry exists is unconfirmed; I check in step B and prefer it. The package's desktop native libs are large and useless to us: strip them (platform import settings off) so the public repo does not take 100+ MB of binaries. |

**Why not REST** (endpoints verified in Google's discovery documents):

* Firestore REST with an ID token works (`Authorization: Bearer`).
* **Play Games sign-in over REST is undocumented.** `accounts:signInWithIdp` documents `postBody` only for Google, Facebook and Twitter. The `code=<authCode>&providerId=playgames.google.com` form appears only in a community post.
* **Game Center over REST is documented** (`accounts:signInWithGameCenter`: `playerId`, `gamePlayerId`, `teamPlayerId`, `publicKeyUrl`, `signature`, `salt`, `timestamp`, header `x-ios-bundle-identifier`) **but needs native code**: `GKLocalPlayer.fetchItems(forIdentityVerificationSignature:)` added to `PauseGameCenter.mm`.
* **App Check exchange is documented** (`exchangePlayIntegrityToken`, `generatePlayIntegrityChallenge`, `generateAppAttestChallenge`, `exchangeAppAttestAttestation`, `exchangeAppAttestAssertion`, `exchangeDebugToken`), **but the tokens come from native libraries**: the Play Integrity API AAR plus a Java bridge on Android, `DCAppAttestService` plus CBOR handling on iOS. Two native plugins we would own.
* **Unverified and decisive:** I found no official statement that Firestore *REST* calls honour an App Check token header (the header is documented for custom backends and sent by the SDKs). If it does not, App Check cannot be enforced and the board is open to scripted abuse.
* Net: REST means three native pieces of our own and one undocumented body, to save binary size. The SDK has `PlayGamesAuthProvider`, `GameCenterAuthProvider` and the Play Integrity / DeviceCheck / App Attest provider factories in the Unity reference. SDK wins.

**Seam.** All Firebase calls live in one adapter, `FirebaseSharedBoard : ISharedBoardBackend`. Everything else (queue, routing, caching, sync, delete, mapping) is SDK-free and tested against `FakeSharedBoard`. If the SDK proves unworkable, REST replaces only the adapter.

**Landing safely (your call, see Questions).** Proposal: the PR holds the SDK-free core, the rules, tests and docs, and the adapter wrapped in `#if PAUSE_FIREBASE` (define unset, no SDK in the repo). Importing the SDK, the EDM bump and the config files are a separate small PR you cut when the console is ready, so the first PR cannot change release builds or AAB size. Cost: CI does not compile the adapter in the first PR; its first compile is the import PR.

**What CI (GitHub Actions, Linux, Unity tests only) can and cannot cover**

| Covered | Not covered |
|---|---|
| Queue, improve-only, routing (boosted to Power-Ups only, premium nowhere), cache, sync-now, delete, offline fallback, run to doc mapping, a C# mirror of the rules' limits (EditMode, via `FakeSharedBoard`) | Real Auth / Firestore / App Check calls |
| `firestore.rules` itself, in a separate cheap job (section 4) | Android and iOS compilation of the adapter (no AAB or Xcode build on CI), EDM resolution, Gradle collisions, AAB size |
| Parity: one `firebase/test-vectors.json` read by both the C# test and the rules test | Play Games server auth code, Game Center signature, Play Integrity / App Attest: need a signed build on a device |

## 2. Identity

Firebase Auth, one provider per platform, no anonymous or email fallback. **No platform sign-in means no shared board** (no uploads; the panel shows its signed-out state; native boards unaffected).

* **Android: Play Games provider.** After the existing `PlayGamesAccount.SignIn` succeeds, call `PlayGamesPlatform.Instance.RequestServerSideAccess(false, code => ...)` (exists in plugin 2.3.0, `AndroidClient.cs:187`), then `PlayGamesAuthProvider.GetCredential(code)` and `SignInWithCredentialAsync`. Firebase's Unity page still shows the v1 call `RequestServerAuthCode`; the v2 name is what the plugin in the repo ships. Needs:
  * An OAuth **web application** client id in Cloud project 528367766818, written into `PlayGamesSettings.asset` (`mWebClientId`, empty today; `RequestServerSideAccess` refuses to run without it). The id is public. The client **secret** goes only into the Firebase console (Auth, Play Games provider), never the repo.
  * Firebase added to the **existing** Cloud project 528367766818, so the Play Games "game server" credential and the Firebase provider use the same web client.
  * In Play Console, Play Games Services: the Android credential for the signing SHA-1s (upload key and Play app signing) and the game-server credential for the web client. Until Play Games is published, only listed testers can sign in.
  * The auth code is single-use: request a fresh one at each Firebase sign-in, never cache it.
* **iOS: Game Center provider.** After `GameCenterAccount.SignIn` succeeds, `GameCenterAuthProvider.GetCredentialAsync()` then `SignInWithCredentialAsync` (the SDK builds the GameKit identity-verification signature itself). Needs: the Game Center provider enabled in the Firebase console, the iOS app registered with bundle id `me.hapticgate.pause` (Firebase uses it to verify the credential audience), the Game Center capability (already added by `IOSCapabilitiesPostProcess`). Apple's `playerID` is deprecated; the SDK sends `gamePlayerID` and `teamPlayerID`.
* **One identity per platform.** The same human on Android and iOS gets two Firebase uids (no cross-platform linking). "Cross-platform" here means both platforms read and write the same board, not one merged player.
* **Display name:** the platform profile name the panel already uses (`IAccountProfile.DisplayName`: Play Games gamer name, Game Center alias), trimmed, max 24 chars, refreshed on every upload. Both platforms moderate their own names; we collect none of our own. An empty name falls back to `PILOT` plus the last four characters of the uid. Spark Auth: confirm in the console that the Play Games and Game Center providers need no billing (I found no page stating it).
* Pause's own sign-out (`AccountLink.Disconnected`) also signs out of Firebase and stops uploads, same as the native boards.

## 3. Data model

Two top-level collections, **`main`** and **`power`** (Power-Ups). **One doc, so one row, per player per table**, doc id = Firebase uid. The doc holds the player's three personal bests plus the details of one run. The SCORE / DUST / WORLDS tabs only change the order of the same rows (this replaced an older per-column shape, `byScore` / `byDust` / `byWorlds`, whose tabs looked like three different boards).

```
main/{uid}  and  power/{uid}
  name        string 1..24
  platform    "android" | "ios"   (must match the sign-in provider, see Phase 2)
  updatedAt   timestamp   (== request.time, server timestamp)
  score       int  best score         0 = none / unknown (shown as a dash)
  dust        int  best star dust, in hundredths   0 = none (dash)
  worlds      int  most worlds cleared   0 is shown as 0
  run         { ship, hearts, endHearts, pauses, skin, powerUps }   details of the best-SCORE run,
              else the dust run, else the worlds run
```

* At least one of `score`, `dust`, `worlds` is above 0. The three bests may come from different runs (that is intended); ship, hearts and pauses always come from one run, falling back score run -> dust run -> worlds run when the better one does not know them. **Unknown stays unknown:** `ship 0`, `hearts 0`, `endHearts -1`, `pauses -1`, a best of 0; never a made-up 1 (the old model's `SharedRun` defaulted `score` to 1, so a native dust mark became a "score 1" run).
* Units match `LeaderboardRunTag`: `dust` in hundredths, `worlds` = total cleared across loops, `score` = `RunScore.Total`, `run.powerUps` bits 1..7 (`power`: dust 1, clear 2, homing 4 = `LeaderboardRunTag.PowerAll`) or 0 (`main`).
* Each best only improves (`SharedDoc.Offer` on the client, `improves()` in the rules). Equal is allowed, so a tie can gain run details.
* Sort = `orderBy` on one top-level number: `score`, `dust` or `worlds`, descending, `limit 10`, **no filter**, so every player is in every tab. Dust and Worlds add `orderBy score desc` as the tie-break (many players have 0 worlds).
* **Indexes: deploy them with the rules** (`firebase deploy --only firestore:rules,firestore:indexes`). `firestore.indexes.json` holds the four composite indexes (`dust,score` and `worlds,score` on `main` and `power`); the score tab uses the automatic single-field index.
* **Old-shape documents.** A document written before this change is read best-effort (`score` = `byScore.score`, `dust` = `byDust.dust`, `worlds` = `byWorlds.worlds`, details from the first old run that knows its ship) and is rewritten in the new shape by that player's next upload (the rules' `improves()` reads either shape, so the migrating write may not lose a best). It does not appear in the top-N queries until then, because those order on the new top-level fields; **press SYNC NOW** (or finish a run) to migrate it.
* **Power-Ups sorting:** today that tab ranks by score only.
* **Season reset** (dormant, as natively): a new season is new collection names (`main2`, `power2`) plus a rules edit, bumped together with `LeaderboardBoards.CurrentSeason`. No field filter, so no index.
* **Read cost per panel open:** top 10 of one table and sort is **10 reads**; the player's own rank when outside the top 10 is one `count()` aggregate of `field > mine` (1 read per 1,000 index entries, minimum 1). So **11 reads**; switching sort costs another 11. The own doc is not read for display (the upload record is cached). `count()` is not used yet (see "As built"): an off-page own row has no rank number.
* **Upload cost:** a transaction that reads the own doc (1 read) then writes it (1 write), only when a slot improves. A transaction fails offline, which is what we want: the improve-only queue keeps the run.

## 4. Security rules

The rules are `firebase/firestore.rules` (the source of truth, mirrored in C# by `SharedBoardLimits` and checked against `firebase/test-vectors.json` by both the emulator test and `SharedLeaderboardTest`). In short: owner-only writes, `validDoc` (exact key set, name 1..24, platform tied to the sign-in provider, bests 0..max with at least one above 0, `validRun` details with ship != 16 and `powerUps` 0 on `main` / 1..7 on `power`), `improves()` per best (reading the old or the new stored shape), a 15 s write interval, public `get` and `list` with `limit <= 25`.

Notes and limits:

* **Reads are public** (changed in phase 2, so the website can show the top scores without sign-in): `get` is open to anyone and `list` is open to anyone with a limit of 25 or less. The documents hold only the display name, platform and scores. Writes and deletes stay owner-only. Scripted reads can spend the free read quota; the client cache and the 25 limit are the brakes. Note that enforcing App Check on Cloud Firestore also refuses reads that carry no App Check token, which includes a website reading directly; the website should register its own web App Check provider or read a cached static copy (see the Hosting JSON escape hatch in section 5).
* **App Check** is enforced in the console for Cloud Firestore **and** Authentication, not in rules (I found no documented rules field I would trust for it). Roll out: leave unenforced until the metrics show real tokens from both platforms, then enforce. Dev builds use the debug provider with a console-registered token (never committed).
* The 15 s interval applies to updates only (create is free). The client respects it too, so a normal flush is one write per table per run.
* **Honest limit:** rules bound values and block replays, scripts and a Paragon row; they cannot tell a patched client's 9,999,999,999 from a real run. Spark has no Cloud Functions (Blaze required), so there is no server-side verification. Same trust level as the native boards' tamper protection, which only clamps ranges.
* **CI for the rules: yes, cheap.** A separate workflow `firestore-rules.yml`, path-filtered to `firebase/**`: `actions/setup-node`, `npm ci` in `firebase/`, `npx firebase emulators:exec --only firestore "node --test"` with `@firebase/rules-unit-testing`. Ubuntu runners have Java for the emulator; the emulator jar is cached under `~/.cache/firebase/emulators`. About 1 to 2 minutes, only when rules change, no contact with the Unity pipeline. It reads the shared `test-vectors.json`. Making it a required check is your call.

## 5. Free-tier budget (Spark: 50k reads, 20k writes, 20k deletes a day, 1 GiB)

* Storage: a doc is about 0.7 KB; 1 GiB is over a million players. Irrelevant.
* Writes: one transaction write per improved upload; a typical player improves a slot 3 to 5 times a day. 20k writes holds roughly **4,000 daily players**.
* Reads are the limit. A panel open is 11 reads; with the cache below a heavy player costs about 35 reads a day, so 50k reads holds roughly **1,400 daily players** (a typical player costs about half). Each upload adds 1 read.
* Past that, the escape hatch (not built): a scheduled GitHub Action with an admin credential publishes the top 10 per sort as static JSON on Firebase Hosting (Spark, 10 GB/month); clients then read 0 Firestore docs. It needs a service-account secret in GitHub, so only if the game grows.
* **Panel caching rule** (phase 2 uses it; the service implements it now): cache each (table, sort) page in memory for **5 minutes** and in PlayerPrefs, so reopening or going offline paints instantly with an "updated N min ago" age. Opening within the TTL costs 0 reads. The retry button bypasses the cache but at most once per 60 s per (table, sort). An own upload drops only that table's cached rank. No snapshot listeners (each change re-bills reads) and no background auto-refresh.

## 6. Client behaviour

* **Switch (dormant):** `SharedBoardConfig.Enabled` is false unless the backend is usable: `PAUSE_FIREBASE` defined, the platform config file present, and the player's `shared_leaderboard_on` pref true (default **on**; phase 2 adds the toggle). Without all three nothing is created or sent.
* **Auto upload:** `LeaderboardService.SubmitRun` also calls `SharedLeaderboardService.OfferRun(run)` with the same gates (`SubmissionBlocked`: developer mode, tutorial, premium ship) and the **same routing**: boosted runs only to `power`, other runs only to `main`, premium nowhere (the rules reject ship 16 as a second wall). On iOS the shared board carries every column because the run lives in the doc, not in a Game Center score.
* **Queue:** improve-only and offline-safe, in PlayerPrefs (`sharedlb_pending`, `sharedlb_submitted`), device-local and kept out of the cloud save like the native queue. Per table it holds up to three candidate runs (one per slot), debounced 2 s, flushed on sign-in and app resume, retried with `LeaderboardService.BackoffSeconds`. A queued run leaves only after the transaction commits (the server ack is the confirmation, so no read-back like Play Games needs). Bound to the Firebase uid: a different uid drops foreign marks and re-offers the local bests once.
* **Sync now:** (a) native: re-queue every board's best from the submitted marks and `LocalBest`, then `Flush`; (b) shared: rebuild the three slot runs per table from the stored marks' tags (a tag carries ship, hearts, `d`, `w`, `r`) and upload. A mark with no tag (older build) fills only its own metric, other columns unknown.
* **Delete my shared scores:** deletes `main/{uid}` and `power/{uid}` (2 deletes), clears the shared queue and cache, then **deletes the Firebase Auth user** so nothing keeps the link between the uid and the Play Games / Game Center player. If the Auth delete is refused (recent-login requirement), the player is signed out of Firebase instead and a note says so. Native scores are untouched (their own consoles hold those). Uploads stay off until sharing is turned on again; a later upload creates a new uid and doc.
* **Panel read** (phase 2) returns the existing `LeaderboardPage` / `LeaderboardEntry` types (they already carry the run columns), so the row code is reused.
* **Code layout:** `Scripts/Core/Leaderboards/Shared/` with `SharedBoardConfig`, `SharedRun` (model, to/from `LeaderboardRunStats` and the tag), `ISharedBoardBackend`, `SharedLeaderboardService`, `FakeSharedBoard`, `FirebaseSharedBoard` (`#if PAUSE_FIREBASE`); outside Unity: `firebase/` (rules, tests, vectors, `firebase.json`).

## 7. Failure modes

Always: log once, keep the queue, never throw into gameplay, never block a frame (every call is async), and the panel falls back to the native boards.

| Case | Behaviour |
|---|---|
| Offline / timeout | Uploads stay queued with backoff; the panel shows the cached page with its age, else the native boards. |
| No Firebase config file or no SDK | `Enabled` false, nothing initialised. Native only. |
| Firebase init fails (Play services missing or outdated) | Shared off for the session, one log line. |
| Sign-in refused, or Play Games / Game Center not signed in | No upload; the queue waits; retried on resume (at most once a minute, as `RetrySilentOnResume` does). |
| Server auth code refused (web client id or SHA-1 wrong) | Same as refused; logged with the cause so the console checklist can be rechecked. |
| App Check failure (attestation rejected, enforcement on) | Calls return permission denied: shared off for the session, native boards only. No retry storm (backoff caps at 300 s). |
| Rules rejection (`PERMISSION_DENIED` on a write) | After `MaxRejectedSends` (8) drop the run and stop for the session, like the native clamp. |
| Quota exceeded (`RESOURCE_EXHAUSTED`) | Reads: cached page or native boards. Writes: stay queued, retried after the next launch or resume (quotas reset about midnight Pacific). |

## 8. What you must set up (exact checklist)

Firebase / Google Cloud console (I touch none of it):

1. Firebase console: **Add project**, choose the **existing** Google Cloud project 528367766818. Stay on **Spark**: no billing attached. Skip Analytics.
2. Add apps: Android `me.hapticgate.pause` with the SHA-1 of the **upload key** and of the **Play app signing certificate** (Play Console, App integrity), and iOS `me.hapticgate.pause`. Download `google-services.json` and `GoogleService-Info.plist`.
3. Cloud Console, APIs and Services, Credentials: create an **OAuth client of type Web application** ("Pause Play Games server"). Note its id and secret.
4. Firebase, Authentication, Sign-in method: enable **Google** (paste the web client), then **Play Games** (same web client id and secret), and **Game Center**. Confirm no Blaze prompt appears.
5. Play Console, Play Games Services: make sure the Android credential and a **game server** credential (the web client) are attached; add testers.
6. Firestore Database: **Create database**, Native mode, a location (permanent; `nam5` US multi-region unless you want EU), production mode, then deploy `firebase/firestore.rules` (I will give the `firebase deploy --only firestore:rules` command).
7. App Check: register the Android app with **Play Integrity** (enable the Play Integrity API; link the Cloud project in Play Console, App integrity) and the iOS app with **App Attest**; token TTL 1 day (fewer exchanges, stays under the Play Integrity daily request quota). **Do not enforce yet.** After a signed build shows verified requests in the App Check metrics, enforce **Cloud Firestore** and **Authentication**.
8. App Check debug tokens: register one per dev device in the console; keep them in your password manager.
9. Credentials, API keys: restrict the **Android key** to package `me.hapticgate.pause` plus both SHA-1s, and the **iOS key** to bundle id `me.hapticgate.pause`. API restrictions to exactly: Identity Toolkit API, Token Service API, Cloud Firestore API, Firebase Installations API, Firebase App Check API. Delete any unrestricted browser key the wizard creates.
10. Send me: the web client **id**, the Firestore location, and the two config files.

**Files and the public repo**

| File | Where | Commit? |
|---|---|---|
| `google-services.json` | `Pause/Assets/Firebase/Config/` | **Yes, safe.** It holds the API key and project / app ids; Firebase documents these as identifiers, not secrets. Protection is the key restrictions (item 9), the rules and App Check. |
| `GoogleService-Info.plist` | `Pause/Assets/Firebase/Config/` | **Yes, safe**, same reasoning. |
| Web OAuth client **id** | `PlayGamesSettings.asset` `mWebClientId` | Yes (public id). |
| Web OAuth client **secret** | Firebase console only | **Never.** |
| Service-account JSON, `firebase login:ci` token, any `FIREBASE_TOKEN` | not in the repo | **Never** (a GitHub secret only if the optional static-JSON job is ever built). |
| App Check **debug tokens** | password manager and console | **Never** (a leaked one bypasses App Check). |
| Keystores, Play signing keys | as today | Never. |
| `firestore.rules`, `firestore.indexes.json`, `firebase.json`, rules tests | `firebase/` | Yes. |

## Questions for you (defaults in bold)

1. Land the first PR **SDK-free** (adapter under `#if PAUSE_FIREBASE`; SDK, EDM bump and config files in a follow-up), or include the SDK and EDM bump in the same PR?
2. Power-Ups table gets the **same three slots** as Main (sortable later), or score only?
3. App Check enforced from the start (**yes, after a verified signed build**), or left off?
4. Rules job as an optional (**non-required**) check?
5. Firestore location.
