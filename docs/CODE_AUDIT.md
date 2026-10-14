# Pause code audit (read-only), master 62aa8726, 2026-10-14

Scope: every runtime C# file under `Pause/Assets/Scripts` that touches saves, cloud
sync, the dust economy, achievements, leaderboards, the shop, the run loop,
elites, bosses and the per-frame hot paths, plus `Assets/Editor/Build`, the
Android/iOS plugin files, `ProjectSettings` and the test registry
(`AllTests.cs`). Every finding below was verified by reading the cited lines;
nothing was run in Unity. Line numbers are from this checkout.

Severity key: **Critical** = loses player data or blocks launch; **High** =
a real bug players will hit, or a launch blocker; **Medium** = bug / gap /
perf cost worth fixing before 1.0; **Low** = hygiene, latent hazards, docs.

Counts: Critical 1, High 4, Medium 12, Low 14.

---

## Critical

### C1. A fresh install (new phone, reinstall, cleared data) overwrites the account's cloud star dust, current world, start world, selected ship and equipped skins with the empty device's values, then uploads that over the cloud

What happens:

1. `CloudSyncRunner.Bootstrap` (`Core/CloudSave/CloudSyncRunner.cs:13-21`) calls
   `CloudSync.Launch` on the first scene, and `CloudSync.Start`
   (`Core/CloudSave/CloudSync.cs:89-91`) calls `RecordLocalChanges()` *before*
   the sign-in.
2. On a device with no prefs, `RecordLocalChanges` (`CloudSync.cs:268-276`)
   compares the content hash with `PlayerPrefs.GetString(LocalHashKey, "")`.
   The stored value is `""`, the hash never is, so it writes
   `cloudSave_localSavedAtUtc = now` for a snapshot that contains no progress.
3. When the cloud save arrives, `TryApplyPending` (`CloudSync.cs:189-195`)
   captures the local snapshot with that "now" stamp and calls `Reconcile`,
   which (not an account switch: `lastAccountId` is empty) goes straight to
   `ProgressMerge.Merge(local, cloud)` (`CloudSync.cs:223-226`).
4. `ProgressMerge.Merge` (`Core/CloudSave/ProgressMerge.cs:30-31, 58-61, 69`)
   picks the side with the newer `savedAtUtc` for `currency`, `spawnShip`,
   `currentWorld`, `startWorld` and `equippedSkins`. The empty device is
   always newer than a cloud save written yesterday, so the result has
   **0 star dust**, ship 0, world 0, no start-world choice and stock skins.
5. `result.Apply()` writes that to PlayerPrefs, `dirty` becomes true
   (`CloudSync.cs:206-207`) and the next `Poll` uploads it
   (`CloudSync.cs:238-265`), so the cloud copy is destroyed too.

Ships, skins owned, best score, highest world, achievements and the codex
survive (union / max), which is why this is easy to miss in testing: the
player gets their ships back but all their dust is gone, and their run starts
in Space on the starter.

Why the tests do not catch it: `AccountCloudSaveTest.SignedInMergeAndUpload`
(`Editor/Tests/AccountCloudSaveTest.cs:427-463`) deliberately stamps the cloud
*newer* (`now + 1000`) and asserts the cloud currency wins; the "first sign-in
ever on a device" case (`:495-500`) gives the device real progress and checks
only ships and `HighestSpeed`. There is no test for "empty device, older cloud".

Suggested fix (either is enough; both is better):

- In `RecordLocalChanges`, when `LocalHashKey` is absent, store the hash but
  leave `LocalSavedAtKey` at 0 (the device has never had progress worth
  dating). The stamp then only starts once something actually changes.
- In `Reconcile` / `Merge`, treat a local snapshot whose `ContentKey()` equals
  `ProgressSnapshot.Fresh(0).ContentKey()` (no progress at all) as
  `savedAtUtc = 0`, so a cloud save with anything in it wins the tie-break.
- Add the test: `ClearProgress()`, `sync.Start()` with a cloud snapshot stamped
  earlier than the clock, assert `PlayerCurrecny`, `spawnShip`, `currentWorld`,
  `playerStartWorld` and the equipped-skin keys come from the cloud and that
  the first upload carries them.

---

## High

### H1. An in-flight cloud write, sign-in or leaderboard submit that never calls back disables that feature for the rest of the session (no timeouts)

- `CloudSync.Upload` sets `uploading = true` and only clears it in the
  platform callback (`Core/CloudSave/CloudSync.cs:252-265`); `Poll` returns
  while `uploading` (`:242`). `CloudSyncRunner.OnApplicationPause` forces an
  upload at the exact moment the OS may suspend the process
  (`Core/CloudSave/CloudSyncRunner.cs:39-44`), which is the most likely moment
  for a Play Games / GameKit completion to be lost.
- `GameCenterAccount` keeps static `pendingLoad` / `pendingWrite` and refuses
  any new call while one is set (`Core/CloudSave/GameCenterAccount.cs:40-57,
  110-121`); the native side only clears them by calling back
  (`Plugins/iOS/PauseGameCenter.mm:29-37, 102-105`).
- `AccountLink.SignIn` sets `signingIn = true` and queues later taps into
  `pendingCallbacks` until the platform answers (`Core/CloudSave/AccountLink.cs:154-182`);
  if it never does, the SIGN IN button is dead until the app is restarted, and
  the `Application.logMessageReceived` / `focusChanged` listeners added in
  `PlayGamesAccount.SignIn` (`Core/CloudSave/PlayGamesAccount.cs:69-93`) leak.
- `LeaderboardService.Flush` adds the board to `inFlight` and only removes it
  in `OnSubmitted` (`Core/Leaderboards/LeaderboardService.cs:215-227`).

Impact: the game keeps playing, but cloud saves, sign-in or score submission
silently stop until the next launch; the player sees "saved to your account"
behaviour that is not happening. Fix: stamp each in-flight operation with
`Time.realtimeSinceStartup` and treat it as failed after e.g. 30 s
(`uploading`, `pendingWrite`, `pendingLoad`, `signingIn`, `inFlight`), and
clear the Play Games log listeners on that timeout too.

### H2. The Paragon "COMING SOON" berth ships ON by default with no purchase path

- `FeatureFlags.ParagonShip` defaults to on (`Core/FeatureFlags.cs:29-30`), so
  `PremiumShips.Visible` is true (`Ship/PremiumShips.cs:46-49`) and the dock
  shows a locked ship whose only action is a "SOON" button that does nothing
  (`Shop/DockPopup.cs:788-797, 839-842`; `Shop/SpaceDock.cs:537-538`).
- There is no store integration at all: `PremiumShips.ParagonProductId` is a
  placeholder (`Ship/PremiumShips.cs:37-38`), `Packages/manifest.json` has no
  purchasing package, and the only way to own it is developer mode
  (`Ship/PremiumShips.cs:9-10`).
- `docs/release/google-play-audit.md` (the release audit) still says "four
  worlds", "paid app, no IAP", and does not know about the berth.

A visibly unfinished feature in a paid 1.0 is a review and refund risk
(Apple's "complete and ready" rule, Play's "broken functionality" policy) and a
support question. Either flip the default to off for 1.0 (one constant) or
ship the IAP. Also refresh the release audit: it predates Tide/Storm/Desert
and the premium berth.

### H3. iOS: no achievement is ever reported, and two of the three leaderboards are off

- `AchievementIds.IosIdsConfirmed = false` (`Achievements/AchievementIds.cs:28`)
  makes `IsReportable(def, ios: true)` false for every achievement (`:104-110`),
  so Game Center never receives an unlock; the game's own achievement UI still
  works, so the gap only shows in Game Center.
- `LeaderboardBoards` has empty iOS ids for `run_star_dust` and
  `furthest_world` (`Core/Leaderboards/LeaderboardBoards.cs:121-139`), which
  disables them on iOS by design; only `top_score` is live there.

If iOS is part of v1.0 this is a launch gap (the memory notes the Apple
enrolment is pending, so it may be intentional for now). Track it: create the
60 Game Center achievements and two boards in App Store Connect, flip
`IosIdsConfirmed`, fill the two ids, and extend `ReleaseReadinessTest.StoreIds`
(`Editor/Tests/ReleaseReadinessTest.cs:86-107`) to assert the iOS side when
building for iOS.

### H4. Star dust spent on one device is refunded by a newer device (cross-device merge rule)

`ProgressMerge` unions purchases but takes `currency` from whichever side has
the newer stamp (`Core/CloudSave/ProgressMerge.cs:7-20, 33-37, 58`). Buy a
ship on phone B (dust goes down, ship marked), then play on phone A (newer
stamp, old balance): the merge keeps the ship *and* phone A's un-spent dust.
The reverse order loses dust earned on the older side. Same for codex
rewards and achievement claims paid into `PlayerCurrecny`. Not exploitable at
scale (needs two signed-in devices) but it is an economy hole and a support
question ("my dust disappeared"). Robust fix: sync two monotonic counters,
`dustEarnedTotal` and `dustSpentTotal` (merged by max), and derive the balance;
or at minimum merge `currency` by max when ship/skin sets are equal.

---

## Medium

### M1. `CloudSync.Poll` serialises the whole progress snapshot every 2 s, including mid-run

`CloudSyncRunner.Update` polls every 2 s (`Core/CloudSave/CloudSyncRunner.cs:9, 32-37`);
`Poll` always calls `RecordLocalChanges` (`Core/CloudSave/CloudSync.cs:238-241`),
which does `ProgressSnapshot.Capture(0).ContentKey()` (`:268-270`):
`CounterKeys()` is every achievement's two keys, every counter, every world
flag, every elite flag and the legacy keys (`Core/CloudSave/ProgressSnapshot.cs:90-95`,
`Achievements/AchievementStore.cs:253-263`), i.e. roughly 250 `PlayerPrefs`
reads, several list/array allocations, four CSV splits and a
`JsonUtility.ToJson` of the result, twice when it changed. The `CanApplyNow`
gate only stops the *upload* (`:246`), not the capture. It is a periodic GC
spike in gameS1 for a value that cannot change until the run ends. Suggested:
skip `RecordLocalChanges` while `StarDustLedger.IsActive` (the run end's scene
change already polls urgently), or only hash on scene load / pause / an
explicit "progress changed" hook.

### M2. `movePlayer.Update` scans the scene every frame the finger is down

`movePlayer.cs:113` calls `PauseQuickActions.IsScreenPointOnAction`, which is
`Object.FindFirstObjectByType<PauseQuickActions>()` (`UI/PauseQuickActions.cs:60-64`)
on every pressed frame (that is most of the run). Keep a static instance set
in `PauseQuickActions.Start` / cleared in `OnDestroy`.

### M3. `WorldMusic.Update` does `GameObject.Find("MovingMusic")` every frame

`Worlds/WorldMusic.cs:169-175` calls `FindBackgroundSource()`
(`:151-155`, a `GameObject.Find`) every frame whenever a full-song world is
playing, which is every world now. Cache the `AudioSource` when the runner is
created (it lives in the same scene) and refresh on scene load.

### M4. `collisionDetection.AwardDestroyedTarget` scans the scene per kill

`Ship/collisionDetection.cs:124-125` does `FindFirstObjectByType<collisionDetection>()`
on every kill to read one float (`enemyDustValue`). A DEATH COMBO or shockwave
kills several things in one frame (`Gameplay/Weapons/DeathCombo.cs:249-258`).
Cache the instance (`Start` / `OnDestroy`) or make the payout a static.

### M5. Per-frame string allocations in the HUD

- `score.cs:197` rebuilds `"Pauses Remaining : " + pauseCounter` on every
  running frame (inside `calcScore`), and `score.cs:204` rebuilds the speed
  string every running frame.
- `UI/HudStyler.cs:358` rebuilds `"PAUSES  " + left` every frame.

`Text.text` skips the rebuild when the string is equal, but the concatenation
allocates regardless. `ResumeSpeedRow.SpeedLabel` (`UI/ResumeSpeedRow.cs:51-55`)
already shows the cached-label pattern; the pause label has at most ~20 values.

### M6. `playerIsDead` writes the saved dust unconditionally

`Gameplay/playerIsDead.cs:38-40` writes `PlayerPrefs.SetFloat("PlayerCurrecny",
score.totalCurrency)` on every death. In a run where `score.paysRealDust` is
false, `totalCurrency` is 0 (`Core/score.cs:82-89`), so a death in gameS1 would
zero the saved balance. Today every path into gameS1 clears
`youAreInTutorial` and sets `HasDoneTut` first (`Tutorial/TutorialLiftOff.cs:85`,
`Tutorial/TutorialSkip.cs:230-236`, `Menu/startMenu.cs:190-196`), so I found no
live route, and `StarDustPersistenceTest` ("a non-paying run leaves the saved
dust alone", `Editor/Tests/StarDustPersistenceTest.cs:116`) only covers the
ledger, not this write. The ledger already banks the run (`score.cs:99-104`),
so the write is redundant: guard it with `if (score.paysRealDust)` or delete it.

### M7. Sign-in resync sends every achievement report in one frame

`AchievementSync.ResyncAll` (`Achievements/AchievementSync.cs:75-99`) is called
from every sign-in and every cloud load outcome (`CloudSync.cs:166, 176, 211`,
`Achievements/AchievementTracker.cs:358-362`) and sends up to 60
`Social.ReportProgress` calls at once for a player with many unlocks. Play
Games applies per-user quotas; a throttled call just fails and is retried on
the next sign-in, so nothing is lost, but the first signed-in session of a
returning player can take a while to show Play Games toasts. Consider sending
a few per frame from `PrefsSaver`'s batched flush instead.

### M8. The `developerBackup_*` / live-key split can lose the tutorial flag in developer builds

With developer mode on, `Hints.BeginEnding` (`Tutorial/Hints.cs:487`) and
`TutorialSkip.FinishBySkipping` (`Tutorial/TutorialSkip.cs:230`) write
`HasDoneTut` to the live key, which developer mode owns; switching the mode
off restores the backup (absent) and the player is sent back to the tutorial
(`Core/DeveloperUnlocks.cs:149-155, 173-179`). Release builds cannot enter
developer mode (`DeveloperUnlocks.Available`, `:33-43`), so this is dev-only,
but `ProgressSnapshot` has `WriteRealString` for exactly this
(`Core/CloudSave/ProgressSnapshot.cs:265-276`); route the tutorial flag through
it.

### M9. `LeaderboardService` saves PlayerPrefs to disk once per board per offer

`SubmitRun` offers every usable board (`Core/Leaderboards/LeaderboardService.cs:142-146`)
and each accepted offer calls `PrefsSaver.SaveNow()` (`:168`): three
synchronous `PlayerPrefs.Save()` calls at the moment of death (plus the
ledger's and the achievement flush's). `OnApplicationPause` in
`LeaderboardRunTracker` (`Core/Leaderboards/LeaderboardRunTracker.cs:92-97`)
repeats it. Stage with `MarkDirty` and save once after the loop.

### M10. `ShipPowerController.Targets` walks every Transform in the scene when the ultimate fires

`Gameplay/Weapons/ShipPowerController.cs:511-516` uses
`FindObjectsByType<Transform>` and a `CompareTag` per object; the backdrop
systems add hundreds of transforms. `ClearTarget.Live` already holds every
hazard (`Gameplay/Weapons/ClearTarget.cs:21-30`); snapshot from it instead.

### M11. `HostileShots.Resolve` is O(n^2) and runs after both pools every frame

`Gameplay/Enemies/HostileShots.cs:89-146` compares every collidable shot pair
(up to 128 registered) plus beams and zones, called "at the end of each pool's
step" (boss and elites), so twice per frame in a boss fight with elites. ~8k
pair tests per call is fine on a modern phone, but it is the first thing to
look at if boss fights hitch on low-end Android.

### M12. Test gap: no test for the fresh-device cloud restore (see C1) or for the in-flight timeouts (H1)

`AccountCloudSaveTest` covers merge rules, account switches, load/sign-in
failures and the upload debounce, all with the cloud stamped newer or the
device holding progress. Add: fresh device + older cloud; a `FakeAccount`
that never calls back for `WriteCloudSave` / `SignIn`, asserting the service
recovers after the timeout.

---

## Low

### L1. `ProgressSnapshot` ship / skin loop bounds are inconsistent

Ships are captured for `i <= MaxShipIndex` (`Core/CloudSave/ProgressSnapshot.cs:118, 172`)
and skins for `id < MaxShipIndex` (`:134, 178`). `MaxShipIndex` is
`shopingShips.shipTotal` (17 or 16), one past the last id, so the ship loop
reads a key that can never exist and the skin loop is the correct one.
Harmless; make both `ShipId.First..ShipId.Last`.

### L2. `shopingShips.Start` has a no-op write

`Shop/shopingShips.cs:53-54`: `if (GetInt("spawnShip") == 0) SetInt("spawnShip", 0)`.

### L3. `RunScore.OnKill` uses culture-sensitive `StartsWith` and `GameObject.name` per kill

`Core/Scoring/RunScore.cs:237, 241` (`name.StartsWith("Boss")` without
`StringComparison.Ordinal`; `target.name` allocates). `AchievementTracker.OnKill`
does it the ordinal way (`Achievements/AchievementTracker.cs:83`).

### L4. `WorldManager.CurrentIndex` setter flushes PlayerPrefs synchronously mid-run

`Worlds/WorldManager.cs:154-162` calls `PlayerPrefs.Save()` on every world
change (once per ~2 minutes). `PrefsSaver.SaveNow()` would also flush the
batched achievement reports; `MarkDirty()` would defer to the 10 s batch.

### L5. `PrefsSaver.SaveIfDue` sets `lastSaveAt` twice

`Core/PrefsSaver.cs:42-48` (`SaveNow` already sets it). Cosmetic.

### L6. Two-finger touch reads as "finger up"

`TouchInput.IsPressed` requires exactly one touch (`Core/TouchInput.cs:30-31`),
so a stray second finger freezes the world and the re-press spends a pause
(`Core/score.cs:116-135`). `SecondaryPressed` exists but nothing in the run
uses it any more (the ultimate fires itself:
`Gameplay/Weapons/ShipPowerController.cs:10-14`). If that is not intended,
`touchCount >= 1` is the one-line change; if it is intended, note it in the
tutorial.

### L7. `ShipLivesIndicatorAttach` / `ShipPowerAttach` poll with scene scans until the ship exists

`Ship/ShipLivesIndicator.cs:125-135` does two `FindFirstObjectByType` calls per
frame for up to 6 s; `ShipPowerController.cs:548-556` one per frame for up to
5 s. Bounded, so only noted.

### L8. Achievements cover four worlds; Storm and Desert have no "reached" / boss achievement

`AchievementTracker.WorldReached` / `BossIds` stop at Tide
(`Achievements/AchievementTracker.cs:30-31`), `AchievementCatalog.EliteWorlds = 4`
(`Achievements/AchievementCatalog.cs:49`), and `AchievementStore.RecountDerived`
loops only those (`Achievements/AchievementStore.cs:243-248`). The comments
say this is deliberate for the catalogue of 60; just confirming the store
listing's achievement list matches before publishing.

### L9. `AchievementIds` placeholders are only guarded by a test, not at runtime

`AchievementIds.IsReportable` (`Achievements/AchievementIds.cs:104-110`) does
block placeholders, so this is fine at runtime; the CSV has 60 real ids
(`Assets/Resources/AchievementStoreIds.csv`). Noted only because the Play
Console ids must be re-verified after the "create them fresh" step the file
header describes.

### L10. Stale release documentation

`docs/release/google-play-audit.md` describes v1.0.0 as four worlds, paid, no
IAP, `managedStrippingLevel: 1` (the project is now `Android: 2`,
`ProjectSettings/ProjectSettings.asset:982-984`) and no premium berth. Refresh
it (and `docs/project-layout.md`, which still lists `Resources/BillingMode.json`)
before the store submission so the data-safety and content answers are made
from the current build.

### L11. Editor-tool package in the shipping manifest

`Packages/manifest.json` pulls `com.coplaydev.unity-mcp` from a git URL. It is
editor-only, but it is a third-party dependency fetched at import time in a
release project; pin it to a commit hash or move it to a local package that is
not part of the release checkout.

### L12. `AndroidKeystoreName` in ProjectSettings is a Windows path

`ProjectSettings/ProjectSettings.asset:280`. `BuildScript.ConfigureReleaseSigning`
overrides it (`Editor/Build/BuildScript.cs:115-135`), so builds are fine; an
editor "Build and Run" would not be.

### L13. `DeathCrash.sets` evicts the whole fragment cache at once

`Ship/DeathCrash.cs:865-878`: at 12 cached sets every texture is destroyed and
the next death re-cuts its hull. Bounded and rare (one set per hull drawing);
an LRU would avoid the occasional re-cut.

### L14. Android back vs. predictive back

`BackNavigatorRunner` reads `KeyCode.Escape` (`Core/BackNavigatorRunner.cs:8`).
With `targetSdk 36` (`ProjectSettings.asset:186`) Android 13+ only delivers the
back key to the activity while `enableOnBackInvokedCallback` is false, which is
Unity's default; nothing in the project sets it. Verify on an Android 14/15
device that system back still reaches `BackNavigator` (this could not be
checked from the code alone).

---

## Things checked and found sound (for the record)

- **Star dust ledger**: `StarDustLedger` writes an absolute balance and every
  commit path is idempotent (`Core/StarDustLedger.cs:42-119`); the death
  screen, `OnDestroy`, backgrounding and a replay's early `Awake` are all
  covered by `StarDustPersistenceTest`.
- **Purchases** deduct, mark and save in one step (`Shop/shopingShips.cs:91-104`,
  `Ship/ShipSkins.cs:293-305`); premium ships are refused by dust.
- **Codex rewards and achievement claims** write the mark and the dust before
  the single save (`Codex/Codex.cs:499-520`, `Achievements/AchievementStore.cs:187-202`)
  and `codexPaid` merges by union (`ProgressMerge.cs:79-84`).
- **Account switch** parks the previous account's progress and never lets a
  new account inherit it (`CloudSync.cs:220-232`, tested at
  `AccountCloudSaveTest.cs:465-501`).
- **Static events**: every `+=` has a matching `-=` (AchievementTracker,
  ScoreHud, SpaceDock, CodexHomeButton, CodexToast, PortalPressureHud,
  AccountHintToast, SoundSettings); the `RuntimeInitializeOnLoadMethod`
  bootstraps use `-=` then `+=` so domain reloads cannot double-subscribe.
- **Scene-lifetime singletons** (`DeathCrash`, `BossEncounter`, `EliteDirector`,
  `WorldManager`, `Planetfall`, `Liftoff`, `PortalArrival`, `Portal`) clear
  their `Instance` / `Live` in `OnDestroy`, and `WorldTransition.InProgress`
  derives from them rather than a flag (`Worlds/WorldTransition.cs:28-43`).
- **Pools**: ship attacks, boss projectiles, elite shots, themed hazards,
  explosions, HUD popups, death-crash pieces and hearts are all pre-built and
  reused with hard caps; the only per-spawn `Instantiate` left is pickups
  (`Gameplay/Pickups/spawnGoodStuff.cs:257-267`), which is bounded by the
  spawn timers.
- **Time scale** is reset on every non-gameplay scene (`Core/GameStateReset.cs`),
  the death/replay/menu paths all go through it, and the splash runs on
  unscaled time.
- **iOS native bridge** dispatches every callback to the main queue
  (`Plugins/iOS/PauseGameCenter.mm:29-37, 99, 104`) and returns `malloc`ed
  strings as IL2CPP expects.
- **Release settings**: IL2CPP, ARM64 only, target 36 / min 25, portrait only,
  no `PAUSE_DEV` define in release, only INTERNET / ACCESS_NETWORK_STATE
  (all asserted by `ReleaseReadinessTest`).

## Suggested order of work

1. C1 (cloud restore on a fresh device) and its test.
2. H1 timeouts for cloud write / sign-in / submit.
3. Decide H2 (Paragon flag) and H3 (iOS ids) for the 1.0 scope; refresh the
   release docs (L10).
4. The per-frame scans and allocations (M1-M5) in one small perf pass.
5. H4 if cross-device play is expected at launch; otherwise log it.
