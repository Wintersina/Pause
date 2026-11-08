# Leaderboards

Status: **live on Android once the Play Games project is published** (the three boards exist as drafts in the `me.hapticgate.pause` project,
Cloud project 528367766818; Review and publish is still to do). **iOS: only Top Score has an id**; the Game Center boards are still to create.

**2026-10: the Top Speed board is retired.** Speed is now capped at HUD 35 (`SpeedRamp.Cap`,
`docs/speed-and-loops.md`), so a best speed ranks nothing. The board is gone from the table, nothing
is submitted to it, and a value an older build left queued under `top_speed` is dropped the next time
the queue is sent. See "Top Speed (retired)" below.

## Shared board

A cross-platform Firebase board (live on Android builds, SDK imported and rules deployed; iOS unverified; dormant in the Editor and Mac) sits next to the store boards: see `docs/shared-leaderboard.md`.

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
| Power-Ups board | See "Power-Ups board" below: boosted runs only, ranked by score, columns in the tag. |
| `LeaderboardPanel` | The in-game panel, opened by Options → LeaderBoard (`leaderboard.pull_up_leaderboard`). It's built at runtime on its own overlay canvas, so `leaderboardS3.unity` is unchanged. |

Sign-in still belongs to `CloudSync`/`IPlayerAccount`. The interactive sign-in is `AccountLink.SignIn`
(this panel's SIGN IN through `SocialBridge.Authenticate`).
`SocialBridge.SignedIn` is raised on every successful sign-in, and the service flushes its queue when it
fires. While the player is signed out in Pause (`AccountLink.Disconnected`, Pause's own disconnect, now without a UI),
`LeaderboardService.SignedIn` is false: nothing is sent, and the panel shows its signed-out state.

### Boards

The table order is the panel's column order (SCORE, DUST, WORLDS; see "Unified panel"): **Top Score** is the primary board and comes first.

| Logical id | Name | Android id | iOS id | Sort | Format | Status |
|---|---|---|---|---|---|---|
| `top_score` | Top Score | `CgkIopqxqbAPEAIQPg` | `me.hapticgate.pause.top_score` | higher is better | integer with thousands separators (`RunScore.Total`, see `Scripts/Core/Scoring/ScoreRules.cs`) | enabled on Android and iOS |
| ~~`top_speed`~~ | ~~Top Speed~~ | (retired) | (retired) | | | **retired** (2026-10, the speed cap): not in the table, `LeaderboardBoards.RetiredSpeedBoard` only names it so old queues drop it |
| `run_star_dust` | Star Dust | `CgkIopqxqbAPEAIQPw` | empty (proposed `me.hapticgate.pause.run_star_dust`) | higher is better | hundredths shown with 2 decimals | enabled on Android; off on iOS until its Game Center id is filled in |
| `furthest_world` | **Worlds** (logical id kept; was "Furthest Loop") | `CgkIopqxqbAPEAIQQA` (console name still "Furthest Loop"; lowest 1, no highest limit, see "Worlds cleared") | empty (proposed `me.hapticgate.pause.furthest_world`) | higher is better | `LOOP L · WORLDS N` | enabled on Android; off on iOS until its Game Center id is filled in |
| `boosted_runs` | **Boosted** (Power-Ups board) | `CgkIopqxqbAPEAIQQg` | empty (Game Center cannot carry the columns) | higher is better | integer with thousands separators (`RunScore.Total`) | **off** on iOS (no Game Center id); live on Android |

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

*Store clamp.* The Play Console board has no highest limit (lowest 1). Should a store board ever carry an upper limit, a value above it is refused: the read-back shows the old value, the client keeps the
value queued and the local best, retries with the usual backoff, and after `MaxRejectedSends` (8) refused sends logs a warning and stops sending that
board for the session (opening the panel or the next launch tries once more). Nothing crashes and no other board is affected.

A board is **enabled on a platform when that platform's id is filled in** (`LeaderboardBoard.EnabledOn`): Android needs only its Play Games id,
iOS only its Game Center id. A board with no id on the running platform gets no header or tab, nothing submitted and nothing queued.

### Submission rules

* **Which boards:** a boosted run feeds only the Power-Ups board, any other run only the other boards (see "Power-Ups board").
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

## Power-Ups board

A fourth board, **Boosted** (logical id `boosted_runs`; this page calls it the Power-Ups board), holds the runs that used a paid **booster**. It is the other half of an exclusive split:

* A run that used a booster is submitted **only** to Power-Ups. Any other run is submitted **only** to Top Score, Star Dust and Worlds. Paragon stays on no board, including this one.
* "Booster" means the three Shop boosters (`Boosters.cs`): **Dust x2**, **Hazard Clear** and **Homing Atoms**. Ship powers, ultimates, in-run pickups and the double-dust ad are not boosters.
* A run counts as boosted when Dust x2 was active on at least one unfrozen frame of real flight (`Boosters.RunDustUsed`, set in `Boosters.Tick`, so it stays set when the time runs out mid-run), or when a Hazard Clear charge fired (`HazardClear.UsedThisRun > 0`), or when Homing Atoms time was spent the same way (`Boosters.RunHomingUsed`). Owning booster time or a held charge does not count. `LeaderboardRunTracker` latches the bits for the run (`powerUps`: 1 = Dust x2, 2 = Clear, 4 = Homing) at every sample, so they cannot reset mid-run.
* Routing lives in `LeaderboardService.SubmitRun`: `board.poweredUpOnly != run.PoweredUp` skips the board. The live "NEW RECORD" popup (`NewRecordTracker.Poll`) applies the same split.
* **Android only:** `LeaderboardBoards` has the Play Console id (`CgkIopqxqbAPEAIQQg`) and an empty iOS id. A board with an empty id is not Usable, so a boosted run on iOS submits nowhere and does **not** fall back to the normal boards (tested). The tab shows on Android only.

**Ranking and columns.** Ranked by run score (`RunScore.Total`), higher is better. Play Games keeps one sort value per board, so the row's other columns ride in the score tag (`LeaderboardRunTag`): `s<ship>h<hearts>e<end>p<pauses>k<skin>` as before, then `d<star dust in hundredths>w<worlds cleared>u<bits>`, e.g. `s7h4e2p3k2d123456w12u3` (every normal-board tag now also carries `d`, `w` and `r`, see "Unified panel"). The columns therefore show the stats of the player's **best-scoring powered-up run**, not separate bests for dust and worlds. Star dust is everything the run earned, booster doubling included (the Star Dust board still ranks dust without the booster's share). Longest tag an in-range run writes: 32 characters (dust clamps at 999,999,999 hundredths, worlds at 9999); the widest string the parser accepts is 42; the Play Games limit is 64 (`PowerUpsLeaderboardTest` pins both). Old tags keep parsing (no columns) and a reader that predates the fields skips them. (Normal-board tags used to carry none of `d`, `w`, `u`; they now carry `d`, `w`, `r` and still never `u`.)

**Panel.** The second tab, **Power-Ups** (behaviour unchanged by the unified panel). Rows show rank, ship, name, `DUST n` over `WORLDS n` (dust reads 999, 1.2K, 12K, 1M; worlds stop at 99), the score (column narrowed to 150 units) and, under it, the power-ups used (`DUST x2`, `CLEAR` or `BOTH`). Hearts and pauses ride on the run line under the name (see "Run line" below); there is no room for side-by-side columns at the narrowest phone (the tab name is short for the same reason). A row with no tag data shows only rank, name and score. Type is 16 units or more, tabs keep their 100-unit tap target, and the screen-fit sheets cover the tab on every test device.

**Local bests, one per route.** `RunScore.SavedBest` banks every ranked run, boosted or not (score rules are unchanged), so the tracker does not use it for leaderboard decisions. `LeaderboardService.LocalBest(false)` (normal, key `leaderboards_bestNormal`, seeded once from the saved best before any boosted run can raise it) and `LocalBest(true)` (key `leaderboards_bestBoosted`, starts at 0) are device-local. A run left early (Menu, Replay, Back, backgrounded) is submitted only when it beats **its own route's** best, so a boosted score never raises the bar for normal runs and a normal best never blocks a boosted run. Death always offers, and the service keeps only improvements. The one-time backfill on account bind offers the normal best to Top Score, never the saved best. A run backgrounded before its first booster is used queues its pre-booster score on the normal boards (an honest unboosted score); once a booster is used, the run's final score goes only to Power-Ups.

*Limits.* After a reinstall, with only the cloud save restored, the normal best is read from `RunScore.SavedBest` (the key is device-local), which could include a boosted score from another device until the first normal run seeds it. Game Center cannot store the columns (see iOS todo in the tracker, section 7): rows of other players are blank there and the player's own row is filled from the device record. The board has no iOS id and stays off on iOS (live on Android).

### Play Console: create the Power-Ups board

Play Console, Pause, Play Games Services, Setup and management, Leaderboards, **Add leaderboard**:

* **Name:** Boosted (the game shows its own name on the tab; keep it short, two tabs share the width)
* **Description:** Best score in a run that used Dust x2 or Hazard Clear.
* **Ordering:** Larger is better
* **Score format:** Numeric, 0 decimal places
* **Limits:** lowest 1, no highest limit (no score cap; the game never offers 0). The console board is still an unpublished draft.
* **Tamper protection:** On
* Save, **Get resources**, paste the id into the Android id of the `BoostedRuns` row in `LeaderboardBoards.cs` (done: `CgkIopqxqbAPEAIQQg`), run `AllTests.RunAll`, and publish the Play Games changes.

## Unified panel (two tabs, sortable columns)

The panel has **two tabs**: **MAIN** (non-boosted runs) and **POWER-UPS** (the Boosted board above). MAIN is one table with three sort headers, **SCORE** (default), **DUST**, **WORLDS**. The store boards are still three (Top Score, Star Dust, Worlds); only the UI merges them.

* **Sorting loads a board.** Tapping a header calls `SelectBoard(<that column's board>)`: the panel loads THAT store board and shows its real ranks. Rows are never re-sorted on the client (a page is only the top 10 of one stat, so that could show a wrong #1). The active header is marked (red plate with an ink shadow, bone type; the others are card-coloured). A newer tap supersedes an older load (`request` token), so a late answer is ignored. The MAIN tab remembers its last sort across a visit to Power-Ups. The native UI ("VIEW ALL") opens the currently selected board.
* **Each row shows three numbers from one run.** The score tag carries them: every normal-board run writes `d<dust hundredths>w<worlds>r<run score>` after `k` (e.g. `s7h4e2p3k2d123456w12r987654`), whichever of the three boards the row is on, so a row on the Dust or Worlds board knows its score. Powered-up runs still write `d`, `w`, `u` and no `r` (their score is the board value). `r` is capped at 9,999,999,999 (`LeaderboardRunTag.MostScore`). Worst case with every field at its real maximum (`u` and `r` together, which a real run never does) is under 50 characters; the widest string the parser accepts is 53; Play Games allows 64 (`UnifiedLeaderboardTest` pins the maxed normal and Power-Ups runs). Old tags parse (no `r`, `HasRunScore` false); a reader that predates `r` skips it.
* **A sorted cell is the board's own value; the other two come from the tag.** No tag (an old row, or a store that keeps none) shows the board's own value and a dash (`-`) in the other cells. **No backfill, no merging rows across boards.** A tie that carries the run columns replaces an older tag without them (`Supersedes`), so a tied personal best gains its columns; the player's own row is also filled from the device-kept record of the same value (`FillOwnTag`).
* **Cells.** SCORE: full number (`9,000`), compact (`1.2M`) only if the full one does not fit at 16 units. DUST: whole dust in the compact form the Power-Ups rows use (`999`, `1.2K`, `12K`, `1M`). WORLDS: a plain number (no `LOOP L . WORLDS N`). Three 100-unit columns, right-aligned, on the right of the row. Row: rank, ship, name, cells. **Hearts and pauses are not side columns:** with three 100-unit columns the name column at 626.7 units is 546.7 - 130 - 14 - 6 - 300 = 96.7, so two more columns would leave the name about 5 units. They sit on a **run line** under the name instead (below). Type is 16 units or more everywhere. Rows are 52 units on a 58 step so the ten rows still fit the design height under the new 100-unit header band.
* **Tap targets.** Tabs: 80 art + 2 x 10 touch margin. Headers: 48 art + 2 x 26 touch margin = 100 units tall, and each is as wide as its 100-unit column. The ScreenFit `leaderboard` sheet stages every board on every test device and checks the headers, the cells (whole, in their column, aligned, 16 units or more) and the rows on each sort and on the Power-Ups tab.
* **Platforms.** A header whose board has no id on the platform is not offered, and cells follow the headers. **iOS:** only Top Score has an id, so MAIN shows just SCORE (one column) and there is no Power-Ups tab; Game Center returns no tag for other players, so their rows have only the score and the own row is filled from the device record. When Dust and Worlds get Game Center ids their headers appear on their own, with dashes for other players.
* **Tests:** `UnifiedLeaderboardTest` (tags, panel, season), plus the updated `LeaderboardTest` / `PowerUpsLeaderboardTest`.

## Season reset (dormant)

A leaderboard "season" is a set of store boards. The plan for a fresh start with no carryover and only the latest build able to see it: **new store boards with new ids**; old builds keep writing to the old boards and cannot see the new ones (there is no force-update). This change builds only the device half, and leaves it dormant.

* `LeaderboardBoards.CurrentSeason = 1` (`FirstSeason = 1`). The device stores `leaderboards_season` (`LeaderboardService.SeasonKey`); **no stored season counts as 1**, so at the shipped value nothing is ever wiped and no key is written.
* `LeaderboardService.MigrateSeason()` runs once at `Install` (like `MigrateWorldsBoard`). When the stored season is behind the constant it: deletes the submitted and pending score tables (every board); **sets** `NormalBestKey` and `BoostedBestKey` to 0 (set, not deleted: a missing normal best is re-seeded from `RunScore.SavedBest`, which would hand the old best back, including after a reinstall that restores only the cloud save); and stores the new season. A stored season ahead of the constant (a downgrade) is left alone.
* **Backfill.** `BindAccount` offers only the submitted marks (now empty) and the normal local best (now 0), so pre-reset bests are never offered to a new board. `BoundPlayerKey` is left alone. Reinstall: the local best key is gone and the season key too, so the migration runs at the next launch and pins the best to 0 before the cloud save lands (the best is device-local, never part of the cloud save). Fresh installs end correct (best 0, season stored).
* **Untouched:** `RunScore.SavedBest` (the personal best). Top Score's NEW RECORD popup keeps using it (`RunScore.BestAtStart`), so after a reset it fires only above the old personal best. The popups for the other boards compare against the submitted / pending tables, which a reset empties: no baseline means no popup (a first run never fires), until the player has a submitted score in the new season.
* **Follow-up PR, once the new boards exist in Play Console / App Store Connect:** replace the Android / iOS ids of the affected boards in `LeaderboardBoards` and **bump `CurrentSeason` to 2 in the same change and the same release** (new ids without the bump would offer old bests to the new boards; the bump without new ids would wipe local state for nothing). Check: the new boards' limits and sort order, `UnifiedLeaderboardTest` still green with the override at the new value, and that an older build's boards are left as they are.

## Run line (ship, hearts, pauses)

Every MAIN and Power-Ups row, shared and native, shows the run's start ship (icon after the rank, in the skin it wore), and under the name a **run line**: the HUD heart + the start hearts (`Hearts` slot) and a two-bar pause glyph + the pauses left at the end (`Pauses` slot), 18 units (16 minimum), two fixed slots of 46 units so they line up row to row. The name takes the top 58 % of the 52-unit row, the run line the rest; the platform mark moves up level with the name. The slots live inside the name column's width (96.7 units at 320 dp), so the numbers and the name lose nothing. An unknown value (no tag, store with no tag, hearts 0, no `p` part) shows a dash (a missing ship shows a dash in the ship slot, `ShipNone`); a real 0 pauses shows `0`. The premium ship is never shown: its row's ship, hearts and pauses read as unknown. The extra 6 units a marked (shared) row leaves before cutting a name replace the old CJK ScreenFit waiver.

## Start ship, start hearts and the premium ship

* **Row layout (the Power-Ups-era layout; the MAIN tab now shows columns instead of the badges, see "Unified panel"):** rank, the start ship's hull art in the skin it wore (`ShipHullArt.Get(ship, skin, 0, 0)`; stock when the tag has no skin), the name, the HUD heart (`Vfx/lifeHeart`) with the start hearts count, a two-bar pause glyph with the pauses the run ended with, then the value (on the Worlds board on two lines: `LOOP L` over `WORLDS N`). Fit at the narrowest panel (320 dp phone, e.g. and-480x854: 626.7 units, Inner 546.7): name column 546.7 - 130 - 14 - 170 - 6 - 2 x 66 = 94.7 units; badge and value type never under 16 units = 7.7 dp (0.48 units per dp there), names 20+ units. An entry with no data (an older score, or a store that keeps no tag) shows nothing in those slots.
* **Capture:** `LeaderboardRunTracker.Start` records `ShipId.Equipped()` and `ShipLives.Max(ship)` at run start (never re-read at the end) in `LeaderboardRunStats.startShip/startHearts`.
* **Carrying it:** `LeaderboardRunTag` encodes `s<ship>h<start hearts>e<end hearts>p<pauses left>k<skin>` (e.g. `s7h4e2p3k2`; `k` is the equipped skin index at run start, left out for the default skin, and read as default when the ship has no such skin; every part after `h` is optional, older `s7h4` / `s7h4e2` tags still read, and a reader skips `<letter><digits>` fields it does not know, so the tag can grow). `p` is `score.pauseCounter` when the run ended (0..99; an older tag has none and its row shows no pauses badge, never a 0). Android sends it as the Play Games score tag and reads it back from every row. Unity's Game Center API has no score context, so iOS rows of other players stay blank; the player's own row is filled from the device-local `submittedScores` record (which now keeps the tag) when the stored value equals the submitted one. `ScoreTable.Entry.tag` is additive: old queues load with an empty tag.
* **Paragon (premium cheat ship) never ranks.** Gates: `RunScore.BeginRun` marks the run `Unranked` at its start (no saved best, no score/loop achievement progress; the dust score bonus is unchanged); `LeaderboardRunTracker` marks the run premium if it starts on, or ever flies, Paragon; `LeaderboardService.SubmitRun`, `SubmissionBlocked` (equipped ship), `OfferUnchecked` and `Flush` (drops queued premium-tagged entries) all refuse it; `SocialBridge.ReportScore` refuses it too. A premium tag read from a board is shown as unknown. The player is told before and after: an amber `PremiumShips.LeaderboardNotice` strip on Paragon's dock card (locked and owned; the IAP flow must show it before payment), "Cheat ship: not on leaderboards" in its codex blurb, and a `CHEAT SHIP - UNRANKED` sub-label on the results score card.

## Adaptive difficulty and score comparability

Spawn density adapts to the pilot (`docs/adaptive-difficulty.md`): enemies x0.75..x1.25 of the old rate. The bound is
what keeps scores comparable: elites, bosses, hit points, speed and per-kill points never adapt, ease can lower the
enemy rate by at most 25% and is not rewarded with extra score, and the premium Paragon ship (unranked) and the
tutorial are off. The developer flag is ignored by boards anyway (developer runs never reach them).

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
   * **Limits:** no lowest and no highest limit (as set in the console today; the game never offers a 0 score)
   * **Tamper protection:** **On**
   * Save, then **Get resources** and copy the generated id (it looks like `CgkI3eXNjrQcEAIQ..`).
     Paste it into the empty Android id of the `TopScore` row in
     `Pause/Assets/Scripts/Core/Leaderboards/LeaderboardBoards.cs`. Don't reuse the
     speed board's id: it is a different metric. The board turns on, and becomes the first tab, as soon
     as that string is filled in. Run `AllTests.RunAll`.
3. For each placeholder you want live, choose **Add leaderboard**:
   * Star Dust: Numeric, **2 decimal places**, larger is better. The game sends hundredths, so 1234
     shows as 12.34.
   * Worlds (the existing `furthest_world` board `CgkIopqxqbAPEAIQQA`, ids are kept): name "Worlds cleared", description "Most worlds cleared in a single run, across loops", Numeric integer (0 decimals), larger is better, **lowest 1, no highest limit** (in the console it is still named "Furthest Loop"; do not recreate it).
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
   * **Score range** (optional): none, to match Google Play (no score cap)
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
