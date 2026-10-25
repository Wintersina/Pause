# Leaderboards

Status: **live on Android once the Play Games project is published** (the three boards exist as drafts in the `me.hapticgate.pause` project,
Cloud project 528367766818; Review and publish is still to do). **iOS: only Top Score has an id**; the Game Center boards are still to create.

**2026-10: the Top Speed board is retired.** Speed is now capped at HUD 35 (`SpeedRamp.Cap`,
`docs/speed-and-loops.md`), so a best speed ranks nothing. The board is gone from the table, nothing
is submitted to it, and a value an older build left queued under `top_speed` is dropped the next time
the queue is sent. See "Top Speed (retired)" below.

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
| `LeaderboardRunTracker` | Added at runtime when `gameS1` loads. It tracks the run score (`RunScore.Total`), star dust, the worlds cleared (`RunScore.Parts.worldCount`) and the pauses left (`score.pauseCounter`), and calls `SubmitRun` when the run ends. No gameplay script or scene references it. |
| `LeaderboardPanel` | The in-game panel, opened by Options → LeaderBoard (`leaderboard.pull_up_leaderboard`). It's built at runtime on its own overlay canvas, so `leaderboardS3.unity` is unchanged. |

Sign-in still belongs to `CloudSync`/`IPlayerAccount`. The interactive sign-in is `AccountLink.SignIn`
(this panel's SIGN IN through `SocialBridge.Authenticate`).
`SocialBridge.SignedIn` is raised on every successful sign-in, and the service flushes its queue when it
fires. While the player is signed out in Pause (`AccountLink.Disconnected`, Pause's own disconnect, now without a UI),
`LeaderboardService.SignedIn` is false: nothing is sent, and the panel shows its signed-out state.

### Boards

The table order is the panel's tab order: **Top Score** is the primary board and comes first.

| Logical id | Name | Android id | iOS id | Sort | Format | Status |
|---|---|---|---|---|---|---|
| `top_score` | Top Score | `CgkIopqxqbAPEAIQPg` | `me.hapticgate.pause.top_score` | higher is better | integer with thousands separators (`RunScore.Total`, see `Scripts/Core/Scoring/ScoreRules.cs`) | enabled on Android and iOS |
| ~~`top_speed`~~ | ~~Top Speed~~ | (retired) | (retired) | | | **retired** (2026-10, the speed cap): not in the table, `LeaderboardBoards.RetiredSpeedBoard` only names it so old queues drop it |
| `run_star_dust` | Star Dust | `CgkIopqxqbAPEAIQPw` | empty (proposed `me.hapticgate.pause.run_star_dust`) | higher is better | hundredths shown with 2 decimals | enabled on Android; off on iOS until its Game Center id is filled in |
| `furthest_world` | **Worlds** (logical id kept; was "Furthest Loop") | `CgkIopqxqbAPEAIQQA` (**upper limit must be raised**, see "Worlds cleared") | empty (proposed `me.hapticgate.pause.furthest_world`) | higher is better | `LOOP L · WORLDS N` | enabled on Android; off on iOS until its Game Center id is filled in |

**Worlds cleared (value and label).** The submitted number is the TOTAL worlds the run cleared, across loops (one loop = the 7-world cycle).
A world is *cleared* when its portal / planetfall / the last world's lift-off is flown, the same event that pays the world-cleared points
(`WorldManager.Advance` -> `RunScore.OnWorldCleared`; counted in `RunScore.Parts.worldCount`, only while the run is scoring). Entering a
world is not clearing it, so the achievement counters (`OnWorldEntered`, `OnWorldCleared`, `OnLoop`) are untouched. A run that cleared 0 is
never offered (`LeaderboardBoard.minValue` = 1, the store board's lowest score).

The label is derived from the stored number alone (`LeaderboardBoards.FormatWorlds`): `LOOP L · WORLDS N` with
`L = max(1, ceil(N / 7))` (`WorldsPerLoop` = 7, fixed, pinned to the live world count by a test so a stored score keeps its meaning) and the word
`WORLD` when N == 1, `WORLDS` otherwise: 1 -> `LOOP 1 · WORLD 1`, 2 -> `LOOP 1 · WORLDS 2`, 5 -> `LOOP 1 · WORLDS 5`,
7 -> `LOOP 1 · WORLDS 7` (loop 1 complete), 8 -> `LOOP 2 · WORLDS 8`, 14 -> `LOOP 2 · WORLDS 14`, 15 -> `LOOP 3 · WORLDS 15`. The panel
draws the two halves on two lines (no dot glyph needed). Rank is the number itself (higher is better; ties are the store's order).

*Local migration.* The board used to hold the loop reached (1, 2, 3 ...). `LeaderboardService.MigrateWorldsBoard` (run once from `Install`) drops
the device's queued / submitted marks for `furthest_world`, because a mark is only a "this device already sent it" note and an old loop number
would read as a worlds count. Nothing real is lost (the board was an unpublished draft, and there is no other local "best loop").

*Store clamp.* If the store board still has its upper limit of 7, a value above 7 is refused: the read-back shows the old value, the client keeps the
value queued and the local best, retries with the usual backoff, and after `MaxRejectedSends` (8) refused sends logs a warning and stops sending that
board for the session (opening the panel or the next launch tries once more). Nothing crashes and no other board is affected.

A board is **enabled on a platform when that platform's id is filled in** (`LeaderboardBoard.EnabledOn`): Android needs only its Play Games id,
iOS only its Game Center id. A board with no id on the running platform gets no tab, nothing submitted and nothing queued.

### Submission rules

* **When:** at run end. That covers death, and also leaving a run early (Menu/Replay/Back) or
  backgrounding the app, but in those cases only when the run beat the local best score
  (`BestScore`). (The best speed and the old `leaderboard_highest_speed_reached`
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
* **Confirm before done:** the Play Games plugin answers "ok" to a submit at once, even when the server
  later refuses the score. So after every send the player's own entry is read back
  (`LeaderboardService.Judge`) and the value only moves from the queue to `SubmittedKey` once the board
  holds it or better. A refused or unreadable result stays queued and is retried with growing delays
  (`BackoffSeconds`: 3, 10, 30, 90, then 300 s).
* **Account binding and backfill:** `BoundPlayerKey` records which store player the submitted marks
  belong to. On the first Flush for a new player, marks of another player are dropped, marks of an
  unknown owner (written by older builds) go back into the queue, and the saved best score
  (`RunScore.SavedBest`) is offered once. Blocked while developer mode, the premium ship or the
  tutorial is active (nothing sent, binding left for later). Star dust and Worlds have no saved
  best besides the marks, so only marks cover them.
* **Silent sign-in on resume:** `LeaderboardRunner.OnApplicationFocus` retries a failed launch sign-in
  (`AccountLink.RetrySilentOnResume`, at most once a minute, never after SIGN OUT) and flushes the queue.
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

## Start ship, start hearts and the premium ship

* **Row layout:** rank, the start ship's hull art (`ShipHullArt.StockRest`), the name, the HUD heart (`Vfx/lifeHeart`) with the start hearts count, a two-bar pause glyph with the pauses the run ended with, then the value (on the Worlds board on two lines: `LOOP L` over `WORLDS N`). Fit at the narrowest panel (320 dp phone, e.g. and-480x854: 626.7 units, Inner 546.7): name column 546.7 - 130 - 14 - 170 - 6 - 2 x 66 = 94.7 units; badge and value type never under 16 units = 7.7 dp (0.48 units per dp there), names 20+ units. An entry with no data (an older score, or a store that keeps no tag) shows nothing in those slots.
* **Capture:** `LeaderboardRunTracker.Start` records `ShipId.Equipped()` and `ShipLives.Max(ship)` at run start (never re-read at the end) in `LeaderboardRunStats.startShip/startHearts`.
* **Carrying it:** `LeaderboardRunTag` encodes `s<ship>h<start hearts>e<end hearts>p<pauses left>` (e.g. `s7h4e2p3`; every part after `h` is optional, older `s7h4` / `s7h4e2` tags still read, and a reader skips `<letter><digits>` fields it does not know, so the tag can grow). `p` is `score.pauseCounter` when the run ended (0..99; an older tag has none and its row shows no pauses badge, never a 0). Android sends it as the Play Games score tag and reads it back from every row. Unity's Game Center API has no score context, so iOS rows of other players stay blank; the player's own row is filled from the device-local `submittedScores` record (which now keeps the tag) when the stored value equals the submitted one. `ScoreTable.Entry.tag` is additive: old queues load with an empty tag.
* **Paragon (premium cheat ship) never ranks.** Gates: `RunScore.BeginRun` marks the run `Unranked` at its start (no saved best, no score/loop achievement progress; the dust score bonus is unchanged); `LeaderboardRunTracker` marks the run premium if it starts on, or ever flies, Paragon; `LeaderboardService.SubmitRun`, `SubmissionBlocked` (equipped ship), `OfferUnchecked` and `Flush` (drops queued premium-tagged entries) all refuse it; `SocialBridge.ReportScore` refuses it too. A premium tag read from a board is shown as unknown. The player is told before and after: an amber `PremiumShips.LeaderboardNotice` strip on Paragon's dock card (locked and owned; the IAP flow must show it before payment), "Cheat ship: not on leaderboards" in its codex blurb, and a `CHEAT SHIP - UNRANKED` sub-label on the results score card.

## Adding a board

1. Create the leaderboard in **both** consoles (see below).
2. In Play Console, use *Get resources* to copy the generated id, then add or refresh it in
   the `LeaderboardBoards` row (the generated `StringHolder` was removed with the achievements rewrite).
3. Add one `LeaderboardBoard` row to `LeaderboardBoards.All`, giving it a logical id constant, both ids,
   a name, a description, the sort order, a formatter and the metric taken from
   `LeaderboardRunStats`. If the metric needs a new number, add the field and fill it in
   `LeaderboardRunTracker.Stats()`.
4. (Leaderboard ids live only in `LeaderboardBoards`; `AchievementIds` is for achievements.)
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
     `Pause/Assets/Scripts/Core/Leaderboards/LeaderboardBoards.cs`. Don't reuse the
     speed board's id: it is a different metric. The board turns on, and becomes the first tab, as soon
     as that string is filled in. Run `AllTests.RunAll`.
3. For each placeholder you want live, choose **Add leaderboard**:
   * Star Dust: Numeric, **2 decimal places**, larger is better. The game sends hundredths, so 1234
     shows as 12.34.
   * Worlds (the existing `furthest_world` board `CgkIopqxqbAPEAIQQA`, ids are kept): name "Worlds cleared", description "Most worlds cleared in a single run, across loops", Numeric integer (0 decimals), larger is better, **lowest 1, highest 9999** (it was created with 1..7: edit the limits, do not recreate it).
   * Turn tamper protection on for each one.
4. Copy each new id (*Get resources*) into `LeaderboardBoards`.
5. **Publish** the Play Games Services changes. Until they're published, only the tester accounts
   listed in *Testers* can see the boards.

### App Store Connect (Game Center)

1. Go to App Store Connect → *Pause* → **Features → Game Center**. Make sure Game Center is enabled
   for the app version (the Xcode capability is added by `IOSCapabilitiesPostProcess`).
2. **Create Top Score** first. Under **Leaderboards**, choose **+** → **Classic leaderboard** →
   *Single leaderboard*:
   * **Reference name:** Top Score
   * **Leaderboard ID:** `me.hapticgate.pause.top_score` (**exactly** this; the id can't be changed later)
   * **Score format type:** Integer
   * **Score submission type:** Best score
   * **Sort order:** High to low
   * **Score range** (optional, works as tamper protection): 0 to 1000000
   * Add an **English localization**: display name "Top Score", format "Integer", suffix " pts" (or none).
   The game already sends to this id; nothing needs changing in code for iOS. (The board stays off in
   the game until the Play Console id is in too, because a board is enabled per platform by that platform's own id.)
3. For each placeholder, create a leaderboard the same way:
   * `me.hapticgate.pause.run_star_dust`: score format *Fixed point, to 2 places*, High to low
   * `me.hapticgate.pause.furthest_world` ("Worlds cleared"): Integer, High to low, score range 1 to 9999 (the value is the total worlds cleared across loops, so it exceeds 7 once a run loops)
   Then copy the ids into `LeaderboardBoards`.
   (Adding a world later changes how many worlds a loop holds in the game but not the stored numbers; `LeaderboardBoards.WorldsPerLoop` and its
   test are the deliberate switch for the `LOOP L` label.)
4. Attach the leaderboards to the app version (the Game Center section of the version page; put Top Score first so it is the default board) and
   submit them with the next build. Sandbox/TestFlight accounts can use them before release.

## Top Speed (retired)

The game no longer has a speed board, and the old Play Games / Game Center projects (`me.sinaserati.Pause`) are gone. Do not create a speed
leaderboard in the new `me.hapticgate.pause` projects; there is nothing to retire.

## Verified vs. not verifiable yet

**Verified here** (editor tests in `LeaderboardTest`, run through `AllTests.RunAll`, plus Mac, Android
and iOS-project builds):

* Registry rules: unique ids, enabled boards have an id on the running platform, placeholders are disabled and skipped,
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
