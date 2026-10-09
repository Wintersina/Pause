# Achievements audit (as of master d507a192)

Scope: every place the word "achievement" appears in the repo, plus the neighbouring systems the redesign
depends on (star dust, Codex, saves, store plugins). Nothing here was run; all names were verified by grep.

## 1. Where achievements live

| Area | File | Role |
|---|---|---|
| Store ids (Android) | `Pause/Assets/Scripts/Core/StringHolder.cs` | 28 `achievement_*` consts + 1 stale `leaderboard_highest_speed_reached`. Looks like the old generated `GPGSIds` (class renamed). Ids are real-looking Play Console ids (`CgkI3eXNjrQcEAIQ..`, app id 976061952733). |
| Android/iOS id table | `Core/AchievementIds.cs` | `AchievementIds.All` (28 `Entry`: android id, iOS id = `me.sinaserati.Pause.` + suffix, title, points). iOS ids are a PROPOSED scheme, none exists in App Store Connect. Points sum to 955. `Resolve(androidId, ios)` returns null for unknown ids on iOS. |
| Call sites | `Core/achievementAPICalls.cs` | One static method per one-shot achievement + tiered "event" methods (`star_collected`, `alien_killed`, `asteroid_destroyed`, `player_died`) + `SpeedMilestones`. |
| Tier counters | `Core/AchievementTiers.cs` | Four lifetime counters (`AchievementCategory`: Aliens, Asteroids, Deaths, Stars), thresholds per tier, `Record()`, deferred `FlushReports()`. |
| Re-sync | `Core/AchievementSync.cs` | After sign-in re-reports tiers that are not confirmed (`achv_synced_<cat>`). Called from `CloudSync.cs:219`; marks cleared on account switch (`CloudSync.cs:203`). |
| Reporting | `Core/SocialBridge.cs` | `UnlockAchievement`, `ReportProgress(id, percent)`, `ShowAchievements()` (native UI). Drops reports when signed out. |
| Native bridge | `Plugins/iOS/PauseGameCenter.mm`, `GooglePlayGames/com.google.play.games` (v2.3.0), `Resources/PlayGamesSettings.asset`, `Editor/Tools/PlayGamesSetup.cs` | Platform backends. Unity `Social.ReportProgress` is the only API used. |
| Cloud save | `CloudSave/ProgressSnapshot.cs` (`CounterKeys()`, `Capture`, `Apply`), `ProgressMerge.cs` (counters = max) | Syncs only the four `achv_count_*` counters and the legacy `achv_progress_*` keys. |
| Flush | `Core/PrefsSaver.cs` (`SaveNow` calls `AchievementTiers.FlushReports`) | Batched writes (10 s). |
| Tests | `Editor/Tests/AchievementTiersTest.cs` (registered in `AllTests.cs:47`), plus mentions in `AccountCloudSaveTest`, `AccountSignInTest`, `LeaderboardTest`, `SpeedCapTest`, `ScoringTest` | |
| Docs | `docs/leaderboards.md` (lines 54, 89, 162-166), `docs/speed-and-loops.md` (121, 169, 318), `docs/enemy-behaviours.md` (423) | Speed achievements repurposed when the speed cap landed. |
| UI | none | There is no in-game achievements list or toast. The only UI is the platform's native one via `SocialBridge.ShowAchievements()`. No caller of that exists outside the account code (`grep ShowAchievements` finds only the definition). |

There are no achievements in scenes, Resources, ProjectSettings or `docs/TODO.md`.

## 2. Current achievement list

Persistence: there is NO local "unlocked" flag at all. One-shots are fire-and-forget calls to the store; tiers keep
only a counter (`achv_count_<category>`). "Unlocked" exists only on the store side (and `achv_synced_<cat>` for tiers).

| # | Android const (StringHolder) | iOS suffix | Title (AchievementIds) | Condition / tracking | Where triggered | Status |
|---|---|---|---|---|---|---|
| 1 | `logged_on_successfully` | `logged_on` | Logged On Successfully | sign-in succeeded | `CloudSync.cs:159` | works |
| 2 | `tutorial_complete` | `tutorial_complete` | Tutorial Complete | tutorial finished | `Hints.cs:279`, `TutorialSkip.cs:241` | works |
| 3 | `paused` | `paused` | Paused | (intent: use a pause) | `achievementAPICalls.achievement_paused()` | **never called** |
| 4 | `correct_pause` | `correct_pause` | Correct Pause | (intent unclear) | `achievement_correct_pause()` | **never called**, undefined meaning |
| 5 | `buy_your_first_ship` | `buy_first_ship` | Buy Your First Ship | purchase | `achievement_buy_your_first_ship()` | **never called** (shop calls `shopingShips.TryPurchase` only) |
| 6 | `buy_all_ships` | `buy_all_ships` | Buy All Ships | all owned | `achievement_buy_all_ships()` | **never called** |
| 7 | `you_have_unlocked_the_secret_ship` | `secret_ship` | Secret Ship | (no secret ship exists in the roster) | `achievement_you_have_unclocked_the_secret_ship()` | **never called, nothing to unlock** |
| 8 | `flash` | `flash` | Flash | natural speed at cap (HUD 35) | `SpeedMilestones.Step`, `collisionDetection.cs:460` (real runs only) | works |
| 9 | `speedster` | `speedster` | Speedster | first limit break | same | works |
| 10 | `super_sonic` | `super_sonic` | Super Sonic | full limit break | same | works |
| 11-16 | `aliens`, `aliens_2..6` | `aliens_5/25/50/150/1000/3500` | Alien Hunter I-VI | `Aliens` counter thresholds 5/25/50/150/1000/3500 | `RecordKillAchievement`, `collisionDetection.cs:130` (only prefab `alien1`) | works, but only the alien-type enemy counts |
| 17-21 | `destroyer`, `destroy_2`, `destroyer_3..5` | `asteroids_5/25/50/100/1500` | Destroyer I-V | `Asteroids` counter (tag `Astr`) | `collisionDetection.cs:131` | works |
| 22-26 | `first_death`, `death_2..5` | `deaths_1/5/10/50/100` | First Death, Death II-V | `Deaths` counter | `collisionDetection.cs:299` | works |
| 27-28 | `stars`, `stars_2` | `stars_150/1000` | Star Collector I-II | `Stars` counter = star pickups | `collisionDetection.cs:350` | works |

Store-side state of the Play Console is unknown from the repo (see section 8).

### Reporting path

`achievementAPICalls.*` / `AchievementTiers.Record` -> `SocialBridge.UnlockAchievement/ReportProgress(androidId, percent)`
-> `PlatformId()` = `AchievementIds.ForCurrentPlatform` (android id as-is; iOS via table) ->
`Social.ReportProgress(id, percent, cb)`. Android: the Play Games platform is installed as Unity's Social platform by
`PlayGamesAccount`; iOS: Unity's built-in Game Center. Gating: `SocialBridge.IsAuthenticated` (store signed in AND
`!AccountLink.Disconnected`); otherwise the report is dropped (callback false) and later recovered only for tiers
(`AchievementSync.ResyncAll`). **One-shots reported while signed out are lost forever** (no queue, no local flag).
A nudge `PlatformPercent` keeps Play Games' truncating percent->steps conversion exact. Editor: `Social.localUser`
is not authenticated, so everything no-ops.

## 3. Star dust (the currency)

| Item | Detail |
|---|---|
| Name in code | "star dust" in comments/UI; class `StarDustLedger`; pref key `"PlayerCurrecny"` (sic, `StarDustLedger.CurrencyKey`, also `ProgressSnapshot.CurrencyKey`), a float |
| Earn in a run | `score.AwardStarDust(amount)` -> `score.totalCurrency` + `StarDustLedger.Earn`. Only when `score.paysRealDust` (not tutorial, tutorial done). `PortalPressure.EarningsClosed` blocks earning. |
| Ledger | `StarDustLedger.BeginRun/Earn/PayScoreBonus/Commit/EndRun/CommitCurrent/Stage`. Each commit writes the ABSOLUTE `baseline + earned` to prefs. **Anything that adds dust to prefs while a ledger run is active is overwritten on the next commit** (baseline is captured at `BeginRun`). |
| Spend | no central API: `shopingShips.TryPurchase(index, cost)` and `ShipSkins.TryPurchase(id, skin)` each read `PlayerPrefs.GetFloat(CurrencyKey)`, subtract, set owned key, `PrefsSaver.SaveNow()` (skin path via `Equip`). |
| HUD | `ScoreHud` / `HudStyler` in the run; dock/shop shows balance; `DeathPanelView` shows the run's dust. |
| Grant API needed | none exists. Proposed `StarDustLedger.Grant(float)` (menu-only, asserts `!IsActive`, or adds to `baseline`). |
| Cloud | `ProgressSnapshot.currency`, merged by "newer savedAtUtc wins" (not max). |
| Dev mode | `DeveloperUnlocks.Enabled` runs never write progress (`Codex.Discover` returns early; `RunScore` savesBest false). Achievement hooks must follow the same guard. |

## 4. Codex structure

* `Codex` (`Codex/Codex.cs`): discovery store. Pref `codexSeen` = comma list of ids. API: `Discover(GameObject|string)`, `IsDiscovered`, `DiscoveredCount`, `Total` (secret bosses only counted once met), `DiscoveredIn(category, out total)`, `static event Action<CodexEntry> Discovered` (the clean achievement hook), `Reload()`. Tutorial and developer mode are ignored.
* `CodexCatalogue` (`Codex/CodexCatalogue.cs`): `CodexEntry` (id, name, `CodexCategory`, sprite resolver, lore, `matches`, `round`, `secret`). Categories: Log, Enemies, Hazards, Atoms, Worlds, Ships. `WorldIds` has 4 entries (Tide missing, as expected while dormant). Bosses are `secret` entries in Enemies; ids `boss_<world>`; elites `elite_<world>_<name>`.
* `CodexPanel` (`Codex/CodexPanel.cs`, 1500 lines): built at runtime on its own overlay canvas. `Tabs` = 6 categories shown as 6 tab buttons in 2 rows x 3 (`TabsPerRow = 3, TabRows = 2`, `BuildTabs`, `TabRect`). `IsSectioned(c)` is true for Enemies and Hazards: those get world sections (`SectionsFor`, header bars `BuildGrid`, jump chips `BuildChips`, sticky header `UpdateSticky`). **`MaxSections = 7`** sizes `sectionTop/sectionStart/chips/headers` arrays (4 worlds + Tide + Storm + BOSSES = 7; adding sections beyond that needs a bump). Locked state: dark silhouette + "???" + `LockedHint`. Detail view (`ShowDetail`) has the art box with its own nested Canvas and an explicit `GraphicRaycaster` (comment at line ~688: graphics on a nested canvas are only seen by a raycaster ON that canvas) plus `CodexArtTap : IPointerClickHandler`. Tabs use `Button` + `raycastPadding`. Layout is the pure `ComputeLayout(safe)`.
* How a section/tab is added: add a value to `CodexCategory`, add it to `CodexPanel.Tabs`, `CategoryLabel`, adjust `TabRows`/`TabsPerRow` (7 tabs would need a 3rd row or 4+3 split; `TabsHeight` and `ComputeLayout` flow from the constants), and decide plain grid vs sectioned. Achievements are not `CodexEntry`s: they need their own list view (see implementation plan), but should reuse `CodexUi`/`CodexPalette` and the tab button chrome.
* Entry point: `CodexHomeButton` (home screen, "N/M DISCOVERED" counter) -> `CodexPanel.Open(font)`; `CodexPanel.Closed` event refreshes the counter.
* Toast: `CodexToast.Announce(heading, name, icon)` is a ready-made non-blocking toast (no raycaster, queue of 4, unscaled time, built on `gameS1` load). Reusable for "ACHIEVEMENT UNLOCKED".
* Tests: `Editor/Tests/CodexTest.cs` plus ~10 codex tests.

## 5. Save / prefs

PlayerPrefs everywhere. Batched writes through `PrefsSaver` (`MarkDirty`, `SaveNow`, `SaveIfDue` every 10 s, runner `PrefsSaverRunner` saves on pause/quit). Cloud: `ProgressSnapshot` captures a whitelist of keys (currency, ships, skins, worlds, tutorial, best score, counters) and `ProgressMerge` merges (union / max / newer-wins for currency). Only whitelisted keys sync: new achievement progress/claimed keys must be added to `CounterKeys()`.

## 6. Existing claim / collect patterns

None. There is no daily reward, streak, or claim UI (`grep -i 'daily|claim|streak'` hits only unrelated words such as "Claimed"-style comments in Backdrop/Planetfall/TitleScreen code and `PlayGamesLeaderboards`). Star dust is only collected by flying into pickups. Closest UI patterns: the Dock popup's BUY/LAUNCH button (`DockPopup`), `CodexHomeButton`, and `CodexToast.Announce` (used for "ALL SKINS: +2 HEARTS").

## 7. Quality of the current set

| Problem | Items |
|---|---|
| Never fire at all | paused, correct_pause, buy_first_ship, buy_all_ships, secret_ship (5 of 28; 18% dead) |
| Impossible / undefined | secret_ship (no secret ship), correct_pause (no definition) |
| No local record | every one-shot: signed-out unlocks are lost; reinstall relies on the store |
| Redundant tiers | Alien Hunter I-VI (6), Destroyer I-V (5), Death I-V (5): 16 of 28 slots are the same three counters; "Death" tiers reward failing |
| Narrow counters | "Aliens" counts only prefab `alien1`; fighters, chasers, bigs, mines and elites never count; per-world rosters ignored |
| World-agnostic | nothing about worlds, loops, bosses, elites, Codex, skins, pauses, mines, hull hearts |
| Trivial / grindy | First Death, Alien Hunter I (5), Death II-III; Alien Hunter VI 3,500 with no world context |
| Titles | Roman-numeral names; "Logged On Successfully" is 22 chars (ok) but flat; no descriptions in repo (only on the console) |
| Sign-in dependence | `IsAuthenticated` also false when the pilot chose "Disconnected" |
| Stale code | `StringHolder.leaderboard_highest_speed_reached`; `achievement_you_have_unclocked_the_secret_ship` typo; docs mention retired speed board |
| Points | 955 total, within 1000; iOS ids not yet created anywhere |

## 8. Store setup state (what the code expects)

* Android: 28 real-looking ids in `StringHolder`. Whether the Play Console game is configured/published with those 28 is not knowable from the repo (memory notes say SHA-1 credentials and Saved Games still had to be configured). **Assume the 28 exist in the console, unpublished or published; see the store setup doc for both cases.**
* iOS: 0 ids exist. `AchievementIds.IosPrefix + suffix` (28 proposed ids); the 3 leaderboards in `LeaderboardBoards` have empty id strings except `TopScore` iOS `me.sinaserati.Pause.top_score`.
* Placeholders: none in the achievement code (ids are concrete strings), but nothing yet checks them against the consoles.
* Plugin: Google Play Games Plugin for Unity v2.3.0 (`Assets/GooglePlayGames`), APP_ID 976061952733; Game Center via `Plugins/iOS/PauseGameCenter.mm` (never compiled for iOS on this Mac).
