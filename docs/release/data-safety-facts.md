# Privacy and data safety facts (Play Console forms)

Derived from the code and plugins at branch `feature/release-rc`, not from memory of the design. Re-run the greps at the bottom if anything is added before release.

## 1. What the game collects and sends

| Item | Where | Evidence |
|---|---|---|
| Game progress in PlayerPrefs (star dust, owned ships and skins, worlds reached, best score, achievement counters, Codex discoveries, sound switch) | Device only, private app storage | `ProgressSnapshot.Capture`, `PlayerPrefs` usage |
| Play Games sign-in: player id and display name | Provided by Google to the game, held in memory | `PlayGamesAccount` (`GetUserId`, `GetUserDisplayName`) |
| Cloud save: one JSON "Pause progress" snapshot | Google Play Games saved games (sent only when signed in) | `PlayGamesAccount.WriteCloudSave`, `ProgressSnapshot` (currency, owned ships, best score, worlds, counters, skins) |
| Leaderboard scores: Top Score, Star Dust (run), Furthest Loop | Google Play Games leaderboards (only when signed in, not in developer mode) | `LeaderboardService`, `LeaderboardBoards` |
| Achievements (60 ids) | Google Play Games (only when signed in) | `AchievementIds`, `SocialBridge` |
| Analytics, crash reports, telemetry | None. No Firebase, Crashlytics, Unity Analytics, GameAnalytics etc. in code or packages that is initialised; `UnityConnectSettings` has Analytics, Ads, Purchasing, Performance and Cloud Diagnostics all `m_Enabled: 0` | grep below |
| Ads | None. No ad SDK in Packages or Assets, no `Advertisement` calls | grep below |
| In-app purchases | None used. `Resources/BillingMode.json` is `{"androidStore":"GooglePlay"}` (Unity IAP configuration only); no script references `UnityEngine.Purchasing` and no product is defined | grep below |
| Device identifiers | None read: no `SystemInfo.deviceUniqueIdentifier`, `Application.RequestAdvertisingIdentifierAsync`, no advertising ID | grep below |
| Network calls of our own | None: no `UnityWebRequest`, `HttpClient`, `WebClient`, sockets or URLs in `Assets/Scripts`. The only `OpenURL` is `app-settings:` (iOS) | grep below |
| Location, contacts, camera, microphone, storage | None used, none requested | permissions section of `google-play-audit.md` |

Result: the only transmission is by Google's Play Games Services SDK, and only after the player signs in.

Third-party code that runs in the player: Unity Engine, Google Play Games plugin for Unity (v2 SDK, `play-services-games-v2`), Google Play services base libraries. Unity Engine itself does not send analytics from a player build when no Unity Gaming Services are initialised; the project initialises none. (Packages `com.unity.purchasing`, `com.unity.analytics` and `com.unity.services.*` are present in `Packages/manifest.json` as dormant dependencies; see the audit for the recommendation to remove them.)

## 2. Data safety form, question by question

Play Console > App content > Data safety. Answers below assume Play Games sign-in is the only data flow. Google's Play Games Services handles that data as Google's own service; Google's guidance for games using it is to declare the data the app causes to be collected. Confirm against the form's current wording at submission.

**Does your app collect or share any of the required user data types?** Yes.

**Is all of the user data collected by your app encrypted in transit?** Yes (Play Games Services uses TLS).

**Do you provide a way for users to request that their data be deleted?** Yes. Data lives on the device (clear storage or uninstall) and in the player's Play Games account (Play Games app > Settings > Delete Play Games account and data, which lists per-game deletion). There is no developer-side account. The deletion link entered is the privacy policy URL, so the hosted policy must contain the deletion instructions (see `privacy-policy.md`). A web "delete data" URL is only required for apps that let users create an account inside the app; Pause does not.

### As entered in Play Console (owner, October 2026)

| Field | Entered |
|---|---|
| Collects user data | Yes |
| Personal info > User IDs | Collected, not shared, optional, purpose App functionality |
| App activity > App interactions | Collected, not shared, optional, purpose App functionality (covers scores, achievements, saved game, progress) |
| Advertising ID | No |
| Encrypted in transit | Yes |
| Deletion | Yes, link = privacy policy URL https://sinaserati.com/hapticgate/privacy/ |
| Ads | None |
| Target age | 13+ |
| IARC / ESRB | Everyone 10+ (E10+) |
| Contact email | hapticgate@gmail.com |
| Category | Games > Arcade |

Differences from the table below, and recommendations:

- The form declared **App interactions**; the table below suggested **Other in-app activity**. Both live under "App activity". App interactions ("how a user interacts with the app, e.g. taps") is acceptable and cheaper to leave as is; scores, achievements and a saved game fit "Other in-app activity" more exactly. Optional tidy-up: add Other in-app activity as well. Not blocking.
- The form **omitted Name**. Recommend adding **Personal info > Name** (Play Games display name, shown on public leaderboards): collected, not shared, optional, App functionality. Reason: the game reads and displays the Play Games display name (`GetUserDisplayName`), and an under-declaration is the usual cause of a Data safety rejection while a harmless over-declaration is not. Do this together with the policy paragraph below.
- The live policy at sinaserati.com says it "does not collect your personal information"; that contradicts User IDs being declared. Replace with the paragraph at the top of `privacy-policy.md`.

### Data types (full table, recommended)

| Category / type | Collected | Shared | Optional | Purpose | Notes |
|---|---|---|---|---|---|
| Personal info > User IDs (Play Games player id) | Yes | No | Yes (only if the player signs in) | App functionality | Entered. Account-level id from Google; not sent to the developer |
| Personal info > Name (Play Games display name shown on leaderboards) | Yes | No | Yes | App functionality | NOT entered; recommended to add (see above) |
| App activity > App interactions (scores, achievements, progress counters, saved game) | Yes | No | Yes | App functionality | Entered. Could also tick Other in-app activity |
| App info and performance > any | No | n/a | n/a | n/a | No crash logs, diagnostics or performance data collected |
| Device or other IDs | No | n/a | n/a | n/a | No advertising ID, no Android ID read by the game |
| Location, Contacts, Photos and videos, Audio, Files and docs, Calendar, Messages, Health, Financial info, Web browsing, Search history | No | n/a | n/a | n/a | |

"Shared" is No because the data goes only to Google as the service provider the player chose to sign in with (Google's own Play Games service), not to a third party.

Purposes ticked: App functionality only. Not ticked: Analytics, Developer communications, Advertising or marketing, Fraud prevention/security/compliance, Personalization, Account management.

### Other App content declarations

| Form | Answer |
|---|---|
| Ads | "No, my app does not contain ads" |
| Target audience | 13 and over (see below). Not "designed for children". |
| News app / Government / Health / Financial / COVID | No |
| Data safety: "Is data collection required or optional?" | Optional (sign-in is optional, game works offline) |
| App access (restricted features) | None, all features open without login |
| Advertising ID declaration | The app does not use the advertising ID (answer No; the merged manifest must not contain `com.google.android.gms.permission.AD_ID`; see audit) |

### Target audience and content

- **Target age group: 13+** (choose 13-15, 16-17, 18+; do not choose any under-13 band). Reasoning: the game is an arcade survival game with stylised sci-fi enemy destruction and no user-generated content, but it uses Google Play Games sign-in, which has a minimum age of 13, and it carries no children-specific design (no cartoon characters aimed at children, no rewards loops built for kids). Selecting under-13 audiences would force the Families Policy (certified SDKs, no Play Games sign-in assumptions); avoid it.
- Appeals to children: No.

### Content rating (IARC) questionnaire answers

Category: Game.

| Question | Answer |
|---|---|
| Violence | Yes, mild only: stylised sci-fi ships and aliens destroyed in pixel-art explosions, fantasy/cartoon-style. No blood, no gore, no realistic human violence, no violence toward real-looking people or animals. |
| Blood and gore | No |
| Sexual content or nudity | No |
| Profanity or crude humour | No |
| Controlled substances (drugs, alcohol, tobacco) | No |
| Gambling or simulated gambling | No (no loot boxes, no chance-based purchases; star dust is earned by playing) |
| User-generated content / users can interact or exchange content | No (leaderboards show a name and a score; no chat, no messaging, no sharing) |
| Shares user location with others | No |
| Allows purchases of digital goods | No (paid upfront app, no IAP) |
| Unrestricted web access | No |
| Fear / horror themes | No |

Expected rating: ESRB Everyone 10+ (fantasy violence), PEGI 7, USK 6 or 12; IARC may output slightly different values. If the questionnaire pushes to Everyone 10+ or PEGI 7 for "mild fantasy violence", that is consistent with the 13+ target audience.

## 3. Re-checking the facts

```
cd Pause
grep -rnE "UnityWebRequest|HttpClient|WebClient|https?://" Assets/Scripts --include='*.cs'          # nothing
grep -rlE "Analytics|Crashlytics|Firebase|Advertisement|UnityAds|UnityEngine.Purchasing|IStoreController|advertisingIdentifier|deviceUniqueIdentifier" Assets --include='*.cs'   # nothing
grep -rn "OpenURL" Assets/Scripts --include='*.cs'                                                  # app-settings: only (iOS)
```
