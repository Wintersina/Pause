# Leaderboards

Status: **foundation in place, not live yet.** The code submits, queues and displays scores. Nothing
reaches a store until the boards exist in Play Console and App Store Connect (see "Store setup").

**2026-10: the Top Speed board is retired.** Speed is now capped at HUD 35 (`SpeedRamp.Cap`,
`docs/speed-and-loops.md`), so a best speed ranks nothing. The board is gone from the table, nothing
is submitted to it, and a value an older build left queued under `top_speed` is dropped the next time
the queue is sent. Until Top Score's Play Console id is filled in, **no board is enabled**: the panel
shows its "no boards configured" state and nothing is submitted. See "Retiring Top Speed" below for
the console steps.

## Architecture

All code is in `Pause/Assets/Scripts/Core/Leaderboards/`, and the panel is in `Scripts/UI/Leaderboard/`.

| Piece | Role |
|---|---|
| `ILeaderboardPlatform` | One store: `Submit`, `LoadTopScores(board, timeScope, count)`, `LoadPlayerCentered`, `LoadPlayerScore`, `ShowNativeUI(board or null = all)`, `IsAvailable`, `IsSignedIn`, `SignIn`. Takes platform ids. Each callback fires exactly once. |
| `PlayGamesLeaderboards` | Android. GPGS v2: `ReportScore`, `LoadScores` with `LeaderboardStart.TopScores` / `PlayerCentered`, the `Public` collection and a `LeaderboardTimeSpan`, plus `LoadUsers` for names. |
| `GameCenterLeaderboards` | iOS. `Social.ReportScore`, and `Social.CreateLeaderboard()` with `userScope` / `timeScope` / `range`, then `LoadScores`. It shows the native screen with `GameCenterPlatform.ShowLeaderboardUI`. Game Center has no "around me" query, so player-centered loads read the player's rank first. |
| `NullLeaderboards` | Editor, Mac and everything else. Never available. |
| `FakeLeaderboards` | Tests and previews. Runs in memory, with switches for signed-out, failing and deferred (loading) states. `Demo()` gives sample data. |
| `LeaderboardPlatforms.CreateForPlatform()` | Picks the platform from `Application.platform`, the same way `PlayerAccounts` does. |
| `LeaderboardBoards` | **The board table**: logical id, Android id, iOS id, name, description, sort order, formatter and run metric. |
| `LeaderboardService` | The submission pipeline, the loading done for the panel, and native UI. `Instance` can be swapped out in tests. |
| `LeaderboardRunTracker` | Added at runtime when `gameS1` loads. It tracks the run score (`RunScore.Total`), star dust and furthest world, and calls `SubmitRun` when the run ends. No gameplay script or scene references it. |
| `LeaderboardPanel` | The in-game panel, opened by Options → LeaderBoard (`leaderboard.pull_up_leaderboard`). It's built at runtime on its own overlay canvas, so `leaderboardS3.unity` is unchanged. |

Sign-in still belongs to `CloudSync`/`IPlayerAccount`. The interactive sign-in is `AccountLink.SignIn`
(the Options Account row, and this panel's SIGN IN through `SocialBridge.Authenticate`).
`SocialBridge.SignedIn` is raised on every successful sign-in, and the service flushes its queue when it
fires. While the player is signed out in Pause (`AccountLink.Disconnected`, the Account row's SIGN OUT),
`LeaderboardService.SignedIn` is false: nothing is sent, and the panel shows its signed-out state.

### Boards

The table order is the panel's tab order: **Top Score** is the primary board and comes first.

| Logical id | Name | Android id | iOS id | Sort | Format | Status |
|---|---|---|---|---|---|---|
| `top_score` | Top Score | empty (create it in Play Console, then paste the id) | `me.sinaserati.Pause.top_score` | higher is better | integer with thousands separators (`RunScore.Total`, see `Scripts/Core/Scoring/ScoreRules.cs`) | **disabled until the Play Console id is filled in** |
| ~~`top_speed`~~ | ~~Top Speed~~ | (was `CgkI3eXNjrQcEAIQAA`) | (was `me.sinaserati.Pause.highest_speed`) | | | **retired** (2026-10, the speed cap): not in the table, `LeaderboardBoards.RetiredSpeedBoard` only names it so old queues drop it |
| `run_star_dust` | Star Dust | empty | empty (proposed `me.sinaserati.Pause.run_star_dust`) | higher is better | hundredths shown with 2 decimals | placeholder, disabled |
| `furthest_world` | Furthest World | empty | empty (proposed `me.sinaserati.Pause.furthest_world`) | higher is better | 1 = Space ... 5 = Tide, shown as the world name | placeholder, disabled |

A board is **enabled only when both ids are filled in**. A disabled board gets no tab, nothing
submitted and nothing queued.

### Submission rules

* **When:** at run end. That covers death, and also leaving a run early (Menu/Replay/Back) or
  backgrounding the app, but in those cases only when the run beat the local best score
  (`BestScore`). (The best speed and the old `achievementAPICalls.leaderboard_highest_speed_reached`
  call are gone with the speed board.)
* **Improvement only:** an offer is dropped unless it beats both the last value this device submitted
  to that board and the value already waiting to be sent.
* **Queue:** accepted offers are saved straight away in PlayerPrefs under
  `LeaderboardService.PendingKey` (`leaderboards_pendingScores`). The queue holds one best value per
  board. The last submitted values are kept under `LeaderboardService.SubmittedKey`. Both are
  **device-local** and deliberately left out of the cloud save, because the store already keeps each
  player's best.
* **Debounce:** the queue is sent `DebounceSeconds` (2 s, unscaled) after the last offer. It's also sent
  as soon as sign-in succeeds. A send that fails stays queued for the next sign-in or offer. If a better
  score arrives while a send is in flight, it is kept and sent afterwards.
* **Never from developer mode or the tutorial.** Nothing is offered while `DeveloperUnlocks.Enabled` is
  on, because unlocked ships and a picked start world aren't fair. Nothing is offered from practice runs
  either: the tutorial scene, a tutorial replay, or any run before the tutorial is done. That's the same
  rule as `score.PaysRealDust`. Queued scores from earlier real runs are still sent.
* **Non-blocking:** every store call is async and fire-and-forget. Gameplay never waits on one.

### Panel states

The states are signed out (with a one-tap interactive sign-in), loading, empty, error/offline (with
retry), unavailable (no store on this device), no boards configured, and populated. Populated shows the
top 10, a "your rank" row that appears even outside the top 10 (Kaneda red), and **VIEW ALL**, which
opens the store's own screen. The panel follows `docs/art-style.md`: flat chamfered cel plates,
`INK` contours, one hard offset shadow, the red title slab, and Orbitron with an ink stroke.

## Adding a board

1. Create the leaderboard in **both** consoles (see below).
2. In Play Console, use *Get resources* to copy the generated id, then add or refresh it in
   `StringHolder` (Window → Google Play Games → Setup), or paste the id string straight in.
3. Add one `LeaderboardBoard` row to `LeaderboardBoards.All`, giving it a logical id constant, both ids,
   a name, a description, the sort order, a formatter and the metric taken from
   `LeaderboardRunStats`. If the metric needs a new number, add the field and fill it in
   `LeaderboardRunTracker.Stats()`.
4. If you use the iOS prefix scheme, also add an entry to `AchievementIds.All` with `leaderboard: true`
   for reference.
5. Run `AllTests.RunAll`. `LeaderboardTest` checks the id rules.

To turn on one of the placeholders, just fill in its two empty strings. Top Score only needs its
Play Console id: its iOS id is already in.

## Store setup (what you need to do)

### Google Play Console (Play Games Services)

1. Go to Play Console → *Pause* → **Play Games Services → Setup and management → Leaderboards**.
2. **Create Top Score** (the primary board). Choose **Add leaderboard**:
   * **Name:** Top Score
   * **Score format:** Numeric, **0 decimal places**
   * **Ordering:** Larger is better
   * **Icon:** optional (the game's icon is fine)
   * **Limits:** lower limit 0; upper limit 1000000 (a good run is around a thousand and a long one a few
     thousand, so this only throws away nonsense)
   * **Tamper protection:** **On**
   * Save, then **Get resources** and copy the generated id (it looks like `CgkI3eXNjrQcEAIQ..`).
     Paste it into the empty Android id of the `TopScore` row in
     `Pause/Assets/Scripts/Core/Leaderboards/LeaderboardBoards.cs` (or regenerate `StringHolder` with
     Window -> Google Play Games -> Setup and reference the new constant there). Don't reuse the
     speed board's id: it is a different metric. The board turns on, and becomes the first tab, as soon
     as that string is filled in. Run `AllTests.RunAll`.
3. Retire Top Speed (`CgkI3eXNjrQcEAIQAA`, "Highest Speed Reached"): see "Retiring Top Speed".
4. For each placeholder you want live, choose **Add leaderboard**:
   * Star Dust: Numeric, **2 decimal places**, larger is better. The game sends hundredths, so 1234
     shows as 12.34.
   * Furthest World: Numeric, 0 decimals, larger is better, limits 1 to 4 (or Custom/text units if
     you want "World").
   * Turn tamper protection on for each one.
5. Copy each new id (*Get resources*) into `LeaderboardBoards`.
6. **Publish** the Play Games Services changes. Until they're published, only the tester accounts
   listed in *Testers* can see the boards.

### App Store Connect (Game Center)

1. Go to App Store Connect → *Pause* → **Features → Game Center**. Make sure Game Center is enabled
   for the app version (the Xcode capability is added by `IOSCapabilitiesPostProcess`).
2. **Create Top Score** first. Under **Leaderboards**, choose **+** → **Classic leaderboard** →
   *Single leaderboard*:
   * **Reference name:** Top Score
   * **Leaderboard ID:** `me.sinaserati.Pause.top_score` (**exactly** this; the id can't be changed later)
   * **Score format type:** Integer
   * **Score submission type:** Best score
   * **Sort order:** High to low
   * **Score range** (optional, works as tamper protection): 0 to 1000000
   * Add an **English localization**: display name "Top Score", format "Integer", suffix " pts" (or none).
   The game already sends to this id; nothing needs changing in code for iOS. (The board stays off in
   the game until the Play Console id is in too, because a board is enabled only with both ids.)
3. Retire Top Speed (`me.sinaserati.Pause.highest_speed`): see "Retiring Top Speed".
4. For each placeholder, create a leaderboard the same way:
   * `me.sinaserati.Pause.run_star_dust`: score format *Fixed point, to 2 places*, High to low
   * `me.sinaserati.Pause.furthest_world`: Integer, High to low, range 1 to 5 (Tide is world 5; raise it from 1-4 BEFORE WorldManager.TideEnabled ships; a sixth world needs 1-6)
   Then copy the ids into `LeaderboardBoards`.
5. Attach the leaderboards to the app version (the Game Center section of the version page; put Top Score first so it is the default board) and
   submit them with the next build. Sandbox/TestFlight accounts can use them before release.

## Retiring Top Speed (2026-10)

The game no longer sends anything to the speed board; these steps only tidy the stores.

* **Play Console** (Play Games Services → Leaderboards → "Highest Speed Reached", `CgkI3eXNjrQcEAIQAA`):
  a published leaderboard cannot be deleted. Either leave it (no new scores arrive) or hide it from
  players by moving it out of the published set: edit it, and in the next Play Games Services
  publish leave it unpublished / mark it hidden where the console offers it. Do **not** reuse its id
  for Top Score.
* **App Store Connect** (Game Center → Leaderboards → `me.sinaserati.Pause.highest_speed`): remove it
  from the app version's Game Center section (detach it) so the native Game Center screen stops
  listing it, then submit with the next build. A leaderboard that has been live can't be deleted, only
  detached / archived.
* **Achievements:** "Speedster" (`achievement_speedster`, iOS `speedster`) is repurposed: it now unlocks
  on the first **limit break** (speed past 35 on the blue atom's boost shield). Re-word its
  description in both consoles, e.g. "Break the speed limit: pass 35 on a boost shield". "Flash"
  (`achievement_flash`) now unlocks when natural speed reaches the cap (35): suggested text "Reach
  the speed limit". "Super Sonic" (`achievement_super_sonic`) unlocks on a full limit break (the
  boost at its maximum, HUD 45): suggested text "Push a limit break to the max". None of the three
  was ever unlocked by code before, so no player holds them under the old meaning.
* `StringHolder.leaderboard_highest_speed_reached` stays in the generated resources (it is
  regenerated from the console); nothing in the game uses it.

## Verified vs. not verifiable yet

**Verified here** (editor tests in `LeaderboardTest`, run through `AllTests.RunAll`, plus Mac, Android
and iOS-project builds):

* Registry rules: unique ids, enabled boards have both ids, placeholders are disabled and skipped,
  Top Score is first and becomes the first tab once its Play Console id is in (`ScoringTest`).
* Submission: improvement only, debounce, one best pending value per board, persisted and surviving a
  relaunch, a failed send stays queued, flushed exactly once on sign-in, and nothing sent from
  developer mode or the tutorial.
* Panel: every state rendered from the Fake, the safe-area fit at eight screen shapes, the Options
  LeaderBoard button opens it, VIEW ALL calls `ShowNativeUI` for the selected board, and Back closes
  it.
* The GPGS and Game Center code compiles for Android and iOS.

**Not verifiable without signed store builds and configured consoles:**

* Scores actually landing on Play Games / Game Center, and what the store returns (names, ranks,
  `PlayerScore` for unranked players, public vs. private Play Games profiles).
* Callback threading of GPGS v2 loads on a device. The plugin dispatches them to the game thread.
* The native leaderboard screens.
* How the store treats out-of-range or tampered scores.
* Account switches: the "last submitted" marks are per device, not per account. A second account on
  the same device only submits once it beats the first account's best. That's harmless, because the
  store keeps each account's best anyway, but it's worth knowing.
