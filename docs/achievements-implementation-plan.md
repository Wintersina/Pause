# Achievements implementation plan

For the implementation agents. Read `achievements-audit.md` (what exists), `achievements-redesign.md` (the 60 achievements, hooks, art briefs) and
`achievements-store-setup.md` (store ids and art specs) first. Work on a worktree branch; no merge to master without the owner's go-ahead.

## 1. Architecture

```
gameplay hooks --> Achievements (static facade) --> AchievementStore (progress + claimed, PlayerPrefs)
                         |                                  |
                         |-- AchievementToast (in run)      |-- Claim(): StarDustLedger.Grant + claimed flag, one save
                         |-- IAchievementReporter ----------> StoreReporter (SocialBridge) | NullReporter
Codex ACHIEVEMENTS tab <-- reads Achievements.All / state / progress
```

### 1.1 Data model (`Pause/Assets/Scripts/Achievements/`, new folder, asmdef not needed)

* `AchievementDef` (sealed class): `id`, `title`, `description`, `group` (enum `AchievementGroup`: Meta, World, Boss, Elite, Enemy, Codex, Collection, Dust, Skill, Pause), `tier` (`AchievementTier` Bronze..Platinum), `points`, `target` (0 = one-shot; for the dynamic "all" ones a `Func<int>` resolver), `hidden`, `requiresTide`, `androidId`, `iosId`, `display section` (7 sections, see 3.1).
* `AchievementCatalog` (static): `All` array in C# (not JSON: one reviewable table that the CSV exporter and tests read; mirror of the redesign table, ids are the keys). `Find(id)`, `Active` (excludes `requiresTide && !WorldManager.TideEnabled`).
* `AchievementStore` (static, PlayerPrefs; keys per id so cloud merge works):
  * progress: `ach_p_<id>` int (counters; one-shots use 0/1)
  * unlocked: `ach_u_<id>` int 0/1 (set when progress >= target)
  * claimed: `ach_c_<id>` int 0/1
  * lifetime counters that feed several achievements (`ach_n_kills`, `ach_n_elites`, `ach_n_rocks`, `ach_n_mines`, `ach_n_blinks`, `ach_n_stars`, `ach_n_deaths`, `ach_n_spent`, `ach_set_elites` (comma list of killed elite codex ids), `ach_set_bosses` (bitmask), `ach_best_loop`, `ach_best_chain`) so thresholds can be retuned without losing data.
  * migration stamp `ach_schema`.
  * `Unlock(id)`: idempotent, sets unlocked, raises `static event Action<AchievementDef> Unlocked`, queues a store report, `PrefsSaver.MarkDirty()`.
  * `Claim(id)`: requires unlocked && !claimed. Sets claimed, calls `StarDustLedger.Grant(25)`, then ONE `PrefsSaver.SaveNow()`; both prefs are set before the single save, so a kill mid-way cannot give dust without the flag (or the flag without dust beyond the disk write granularity). `ClaimAll()` loops, one save at the end. Returns the dust paid.
  * `RewardDust = 25` const in the catalog.
* `StarDustLedger.Grant(float)` (new, in `Core/StarDustLedger.cs`): if `!IsActive` adds to `PlayerPrefs[CurrencyKey]`; if a run is active adds to `baseline` too (so the next `Commit` keeps it). Claiming is only reachable from menus, so `IsActive` is normally false, but the guard keeps the ledger correct.
* Offline-safe: all state is local; the store is only informed. Unlock/claim never wait on sign-in.

### 1.2 Tracking hooks

One facade, `Achievements` (static): `Report(id)`, `Add(counterId, n)`, `SetAtLeast(counterId, v)`, all guarded by `AchievementGuard.Real` (not tutorial, not `DeveloperUnlocks.Enabled`, `score.paysRealDust`). Each achievement family is wired in ONE place; prefer subscribing in a single `AchievementTracker` (`[RuntimeInitializeOnLoadMethod]`, like `CodexToast.Init`) to the events that already exist, and add the minimum new call sites:

| Family | Wiring (file) |
|---|---|
| Codex counts, field guide, `codex_complete` | `Codex.Discovered` event (Codex/Codex.cs) |
| Elites (first/10/50, per-world sets, blink) | `EliteShip.Died` static `Action<EliteShip, EliteDamage, string>` (Gameplay/Elites/EliteShip.cs:98); `Def.codexId`, `Def.world`; ignore `EliteDamage.FriendlyFire` if not caused by the pilot |
| Enemy / rock / mine kills | `collisionDetection.RecordKillAchievement` (Ship/collisionDetection.cs:127): replace `alien_killed()`/`asteroid_destroyed()` with `Achievements.OnKill(target)`; tag `Enimey` -> kills, `Astr` -> rocks, `EnemyIdentity.role == Mine` -> mines |
| Blink kill | same method; `bonusPoints > 0` (teleport kill) |
| `chain_10` | `RunScore.OnKill` after chain update (read `parts.bestChain`) |
| `mega_domino` | `DeathCrashDomino.MegaDominoStarted` |
| Bosses | `BossEncounter.BeginOutro` (Bosses/BossEncounter.cs ~377): if `explode` -> `Achievements.OnBossDestroyed(world)`; `boss_no_hit` needs `AchievementEvents.PlayerHurt` raised where hearts are lost in `collisionDetection` and latched by the encounter fight start |
| Worlds / loops | `WorldManager.Advance(bool)` (Worlds/WorldManager.cs ~517): after `CurrentIndex` changes -> `OnWorldEntered(CurrentIndex)`; loop branch -> `OnLoop(RunLoop.Index)`; also `OnWorldCleared` for `pause_no_pause_world` |
| Ships / skins / spend | `shopingShips.TryPurchase` (Shop/shopingShips.cs:85) and `ShipSkins.TryPurchase` (Ship/ShipSkins.cs:281): after success -> `OnShipBought`, `OnSkinBought`, `Add("spent", cost)` |
| Secret power | `SecretPowerController.Fire` (Gameplay/Weapons/SecretPowers.cs:243) |
| Dust pickups / deaths | existing `achievementAPICalls.star_collected` / `player_died` bodies call `Achievements.Add` |
| Score | `score.SettleCurrentRun` (run end) -> `SetAtLeast` on `score_*` via `RunScore.Total` |
| Speed | `achievementAPICalls.SpeedMilestones.Step`: keep, bodies call `Achievements.Report` |
| Pause | `score.pauseCounterFunction` (Core/score.cs:196) -> `pause_first`, per-world flag; `score.incromentPause` -> hoarder check; `movePlayer` teleport (Ship/movePlayer.cs:140 next to `RunScore.OnTeleport`) -> `Add("blinks")` and the dodge probe |
| Meta | tutorial: `Hints.cs:279` / `TutorialSkip.cs:241`; sign-in: `CloudSync.cs:159` (call `Achievements.Report("meta_logged_on")`; the dev-mode guard does not apply to sign-in) |

Per-frame cost: every hook is O(1) with cached `AchievementDef` lookups, no allocation (cache id strings; `Add` only writes prefs when a threshold boundary or every 10th step is crossed, mirroring `AchievementTiers` batching, with `PrefsSaver.MarkDirty()`).

### 1.3 Store reporting

* `IAchievementReporter { void Unlock(AchievementDef d); void Progress(AchievementDef d, double percent); }`
* `StoreReporter`: delegates to `SocialBridge.ReportProgress(androidIdForPlatform, percent)` (existing); skips placeholder ids (`AchievementStoreIds.IsPlaceholder`).
* `NullReporter`: used in the editor and when `Social.localUser` is unauthenticated.
* Offline queue: the facade keeps `ach_pending` (comma list of ids unlocked but not yet confirmed by the store); flushed on `SocialBridge.SignedIn` (existing event) with confirmation callbacks, fixing the audit finding that signed-out one-shots were lost. Counters re-report percent on sign-in from local progress (replaces `AchievementSync`).
* `SocialBridge.ShowAchievements()` remains as an "ON GOOGLE PLAY / GAME CENTER" link in the Codex tab header (optional).

### 1.4 In-game unlock notice

Reuse `CodexToast.Announce(heading, name, icon)` (Codex/CodexToast.cs:114): heading `ACHIEVEMENT UNLOCKED`, name = title, icon = badge sprite. It is already non-blocking (no raycaster, unscaled time, queue of 4, built on `gameS1` load) and sits under the HUD stat panel. Subscribe to `AchievementStore.Unlocked`; do not show during the boss intro/outro or while the pause overlay is up (honour the pause feel: toasts run on unscaled time and never write to `Time.timeScale`; if a toast is pending during a boss intro, hold until `BossEncounter.Running` is false). Menu unlocks (for example sign-in) show nothing; the Codex button gets the badge below instead.
`CodexHomeButton`: add an "N TO COLLECT" dot/line when `AchievementStore.ClaimableCount > 0` (refresh on `CodexPanel.Closed` and `Unlocked`).

## 2. Art install plan

* Codex draws 60 masters at 1024 (spec in `achievements-store-setup.md` section 6). Stage in `Pause/Assets/Art/Achievements/src~/<id>_1024.png` (tilde = ignored by Unity, same convention as `Art/Enemies/src~`).
* A tool script (Python or Editor menu `Pause/Achievements/Build icons`) writes `Pause/Assets/Art/Resources/Achievements/<id>.png` (128, nearest-neighbour from a 256 working copy) and `Pause/Assets/Art/Achievements/src~/store/<id>_512.png` for Google. Importer: sprite, point filter, no mipmaps, alpha allowed (add a `TextureImporter` rule in `Editor/Importers`).
* Loader: `AchievementArt.Sprite(id)` -> `Resources.Load<Sprite>("Achievements/" + id)`, cached; missing art falls back to a generic medal and a test fails (below). Locked look: tint the same sprite dark-grey + padlock overlay in code.
* Codex runs headless (`codex exec`) per the art workflow memory: batches by group (World, Boss, Elite, Enemy, Codex, Collection+Dust, Skill+Pause, Meta). The owner vets the first batch before more are generated.

## 3. Codex ACHIEVEMENTS section

### 3.1 Placement
* Add `CodexCategory.Achievements` (CodexCatalogue.cs enum) and append it to `CodexPanel.Tabs` + `CategoryLabel` (`"ACHIEVEMENTS"`, resize-to-fit label already handles width). Seven tabs: change `TabsPerRow` to 4 (rows 4+3; `TabsHeight` unchanged, `tabWidth = (iw - 12*3)/4`). Update `CodexTest` layout expectations.
* The tab is sectioned: add it to `IsSectioned` and branch in `SectionsFor`/`Populate` to build sections from `AchievementCatalog` instead of `CodexEntry`: 7 sections = JOURNEY (Meta+World), BOSSES, ELITES, COMBAT (Enemy+Skill), PAUSE, CODEX, COLLECTION (Collection+Dust). That is exactly `MaxSections = 7`: no change needed (bump it if a section is ever added). Header bars show `found/total` per section; chips jump (existing behaviour); header accent colours from `CodexPalette`/world lights.
* Cards: the existing `Card` (art + name) reused with a different content provider: badge sprite, title, thin progress bar for counters ("37/100"), a state ribbon: LOCKED (greyscale), UNLOCKED (gold glow + `+25` chip, pulsing), CLAIMED (check mark, dimmed). Hidden + locked: `???` and `?` badge. Dormant (Tide): not listed.
* Header strip above the chips (new, inside the Section/list area): `12/58 UNLOCKED   3 TO COLLECT   [COLLECT ALL +75]`. COLLECT ALL is disabled when nothing is claimable.
* Detail view (tap a card -> existing `ShowDetail` path with an achievement branch): big badge, title, description, tier, progress bar, "REWARD: 25 STAR DUST", and a **COLLECT 25** button when claimable, a check and "COLLECTED" when claimed, a hint (LockedHint-style, description only) when locked. Collecting plays the existing dust sound and a count-up of the balance; the home/dock dust label refreshes on `CodexPanel.Closed`.
* Developer mode: show all as unlocked? No: show real state; `AchievementGuard` blocks writes anyway. Matches `Codex` which only reveals.

### 3.2 Touch handling (the nested-Canvas lesson)
Codex cards are clickable via `Button` on the card frame (`OnCardClicked`), and the detail art uses a nested Canvas with its own `GraphicRaycaster` plus `IPointerClickHandler` (CodexPanel.cs ~688). Rules for the new UI: use real pointer events (`Button.onClick` / `IPointerClickHandler`), never polling; if a badge or the COLLECT button sits inside an `Isolate`d sub-canvas, add a `GraphicRaycaster` to THAT canvas or the graphics receive nothing; give COLLECT / COLLECT ALL >= 48 dp touch targets (use `raycastPadding` like the tabs); disable raycast on decoration (glows, ribbons). Tests drive the real pipeline (EventSystem + `ExecuteEvents.Execute(..., pointerClickHandler)` or `StandaloneInputModule` simulation as the existing codex tests do) rather than invoking the handler directly.

## 4. Tests (`Pause/Assets/Editor/Tests/`, registered in `AllTests.cs`)

| Test | Checks |
|---|---|
| `AchievementCatalogTest` | 60 entries; unique ids and androidIds/iosIds; title <= 24, description <= 100; points multiples of 5, 5..180 (Android) and <= 100 (iOS); total 1000; every non-dormant id has a hook declared; dormant = exactly the Tide ones |
| `AchievementArtTest` | `Resources.Load<Sprite>("Achievements/" + id)` non-null for all 60; size 128x128; staged master present for each id (skip in CI if `src~` absent) |
| `AchievementTrackingTest` | each hook family with a fake `IAchievementReporter`: kill counters (alien vs rock vs mine), `EliteShip.Died` per cause, boss destroyed per world, `Codex.Discovered` counts, `MegaDominoStarted`, purchases (ship/skin/spend), blink counters, pause flags; tutorial and developer-mode guard produce no progress |
| `AchievementClaimTest` | claim once (second call 0), dust +25 exactly, flag and dust persisted in one save (`PrefsSaver.SaveCount` +1), `ClaimAll` pays N*25, claim blocked when locked, grant during an active ledger run survives `Commit` |
| `AchievementMigrationTest` | seeded old prefs (`achv_count_*`, `highestWorld`, `boughtshipN`, `HasDoneTut`, `codexSeen`) produce the mapped unlocked-unclaimed set; running twice changes nothing; cloud snapshot round-trip with new keys and `ProgressMerge` (claimed = max/union) |
| `AchievementReportingTest` | placeholder ids are never sent; offline queue flushes once on `SocialBridge.SignedIn`; platform id mapping |
| `CodexAchievementsTabTest` | tab count 7 fits `ComputeLayout` at the aspect-ratio sweep; sections <= `MaxSections`; locked/unlocked/claimed states; COLLECT button pays through a real pointer click; COLLECT ALL; hidden shows `???`; Tide dormant not listed; scroll/jump chips still work |
| Update | `AchievementTiersTest` removed, `AccountCloudSaveTest` / `AccountSignInTest` / `LeaderboardTest` adjusted for `AchievementTiers`/`AchievementSync` removal, `ProgressSnapshot.CounterKeys()` includes the new keys |

## 5. Phases (2-3 Sonnet agents, one branch, sequential merges; no merge to master without the owner)

| Phase | Agent | Deliverable |
|---|---|---|
| A. Core | agent 1 | `AchievementDef/Catalog/Store/Facade/Guard`, `StarDustLedger.Grant`, reporter interface + null/store impls, migration, cloud keys, remove `AchievementTiers`/`AchievementSync`/old consts, catalog + claim + migration + reporting tests, CSV export tool and `AchievementStoreIds.csv` placeholder |
| B. Hooks | agent 2 (parallel with A after the facade stub exists) | all tracking hooks in 1.2, `PlayerHurt` event, dodge probe, toast + `CodexHomeButton` badge, tracking tests |
| C. UI + art | agent 3 (after A) | Codex tab, cards/detail/COLLECT, art loader + importer + icon build script, tab/art tests; wires Codex's delivered PNGs |

Art (Codex headless) runs alongside A/B and lands before C's final polish; C can use a generated placeholder medal per tier until then.

## 6. Risks

* **Currency overwrite**: `StarDustLedger` writes absolute balances; a grant while a run is active would vanish. Mitigation in `Grant`; claim is menu-only.
* **Cloud merge**: currency merges "newer wins" while claimed flags union (max); an unsynced second device can lose or repeat 25 dust. Acceptable; documented.
* **Calibration**: score and Codex thresholds need real numbers (`ScoreRules`, `Codex.Total`).
* **Store caps**: the published Play game may block the 1000-point set if legacy points remain.
* **Tide**: dormant achievements counted in the store totals; flipping `TideEnabled` without a new build means the store entries exist but are unreachable on old builds (fine).
* **Dev mode / tutorial leakage**: every hook must go through the guard; test it.
* **Rewards inflate the economy**: 1,500 dust total (cheapest ships cost 600-1400); acceptable.
* **Tab layout**: 7 tabs in 4+3 rows on small phones; verify the 48 dp target in `CodexTest`.

## 7. Open questions (defaults proposed, do not block)

1. Dust reward: flat 25 for all (as requested). Default kept; scaling by tier would be a one-line change.
2. Unlock notification: toast only in runs; default yes. In menus a badge on the Codex button.
3. `boss_no_hit` and `pause_perfect_dodge` need new hooks; default: implement; fall back to dropping them if the dodge probe proves noisy.
4. Hidden achievements: 3 + 2 dormant; default as listed.
5. Verdant has only one elite today, so `elite_verdant_all` equals "first Verdant elite"; it grows when more arrive.
6. Tide points: counted now (55 of 1000 are dormant achievements). Alternative: reserve 55 points by creating a 4th dormant-less set of placeholders; default as designed.
7. Legacy Play achievements: see store setup section 5; the owner must check whether the game was ever published.
8. Should score thresholds follow loop count? Default: single-run score as designed.
9. Pause-related ones depend on the owner's definition of "Correct Pause"; default = blink-kill.
10. Names are placeholders for the owner to rename in the redesign table before store entry.

## 8. Implementation notes (as built, branch `feature/achievements-impl`)

What shipped follows sections 1-6. Where the code differed from the plan, this is what was done instead.

**Files** (`Pause/Assets/Scripts/Achievements/`): `AchievementDef`, `AchievementCatalog` (the 60-row table), `AchievementStore`
(prefs state, claim), `Achievements` (facade, guards, `AchievementEvents`, `AchievementRunner`), `AchievementTracker` (every hook),
`AchievementSync` (store reporting, sign-in catch-up), `IAchievementStore` (+ Null / PlayGames / GameCenter stores, `AchievementStores`),
`AchievementIds` (the single id table), `AchievementMigration`, `AchievementArt` (badge loader + placeholder medal), `SpeedMilestones`.
UI: `Codex/CodexAchievementsView.cs`, `CodexPanel` (7th tab), `CodexHomeButton` (collect dot). Editor: `Tools/AchievementStoreExport.cs`
(`Pause > Achievements > Export store CSV`, output in `docs/achievements-export/`), `Importers/AchievementArtImporter.cs`,
`Previews/CodexPreview.RunAchievements`. Removed: `achievementAPICalls`, `AchievementTiers`, the old `AchievementSync`, `StringHolder`,
old `AchievementIds`, `AchievementTiersTest` (and with them the five never-firing achievements).

**Deviations**

1. *State keys.* No `ach_p_<id>`: progress is a set of shared lifetime counters (`ach_n_<name>`), because several achievements read one
   count. One-shots only need `ach_u_<id>`. Sets that feed counters are `ach_e_<eliteCodexId>` and `ach_b_<world>` (ints, so the cloud
   merge needs no new type: counters merge by max, flags by union; `AchievementStore.RecountDerived` reconciles after a restore).
   `ach_s_<id>` (the store-confirmed percent) is device-local and not synced; `ach_schema` is not synced either.
2. *Score thresholds recalibrated* (open question 8 in the redesign): ScoreRules put a first pass Space -> Ember at ~6,600 and a second loop
   at ~8,100, so 10k / 50k / 150k were out of reach. Raised again after the 2x streak (speed-and-loops.md) to **5,000 / 20,000 / 80,000** (`AchievementCatalog.ScoreRookie/Ace/Legend`; first set was 2,500 / 8,000 / 30,000). The ids
   (`score_10k` / `score_50k` / `score_150k`) are unchanged so the badge art names still match; titles are unchanged, descriptions show
   the real numbers. `docs/achievements-export/` is generated from the code and supersedes the CSV in `achievements-store-setup.md`.
3. *Progress bars for max-style achievements.* `chain_10`, `loop_1/2/5` and the score ones are counters (`best_chain`, `best_loop`,
   `best_score`) with `storeSteps = 0`, so the Codex shows "7/10" while the stores see a standard (non-incremental) achievement.
4. *Codex tab is not a `CodexCategory`.* Its cards are achievements, not entries, so `CodexPanel.Tabs` stays the six entry categories and
   `CodexPanel.TabCount = 7` / `AchievementsTab = 6` adds the seventh. 4 + 3 tabs (the second row's three are wider). The tab is its own
   view (`CodexAchievementsView`, own ScrollRect, sections, jump chips, pinned header, detail) rather than a branch of `Populate`, which
   left the 2500-line `CodexTest` untouched. `MaxSections` (7) was enough, no bump.
5. *Buttons.* COLLECT, COLLECT ALL and the detail COLLECT each sit on their own sub-canvas with their own `GraphicRaycaster` (the
   nested-canvas lesson), which also keeps the pulse from rebuilding the list's canvas. Pulse = scale of the visible chips only.
   No dust sound: the menus have no shared UI sound to reuse. The balance counts up in the strip.
6. *Toast.* Via `CodexToast.Announce` as planned, only in `gameS1`. It is not held back during a boss intro: the toast already drops
   under the boss/portal banners (`TopOffset`), so a separate hold queue was not added.
7. *Codex counts.* `codex_complete` needs every entry in `Codex.Entries` (bosses and all ships included); `codex_field_guide` is one
   world's non-elite, non-boss enemies and hazards (elites and bosses have their own achievements). `codex_10` is already true on a
   fresh profile (17 entries are free: Log, atoms, ...), which is how the codex counts it.
8. *Elite credit* goes to the pilot's own kills only (`PlayerWeapon`, `Teleport`, `ShieldRam`, `PlayerContact`, `Combo`), not crash /
   rail / friendly fire / domino.
9. *Store ids.* 13 legacy Play ids that map one-to-one onto a new achievement are kept in `AchievementIds` (tutorial, logged on, the three
   speed ones, paused, correct pause, buy first / all ships, stars 150 / 1000, deaths 10 / 100); the other 47 are `TODO_android_<id>`
   placeholders. Real ids go in `Assets/Resources/AchievementStoreIds.csv` (`internal_id,CgkI...`), no code change. iOS ids follow
   `me.hapticgate.pause.ach_<id>` but reporting stays off until `AchievementIds.IosIdsConfirmed` is set (App Store Connect rows exist).
   `SocialBridge.ReportProgress/ReportScore` now take platform-ready ids.
10. *Blink-dodge probe* (`pause_perfect_dodge`) ships ON behind `AchievementTracker.DodgeProbeEnabled`: a hostile shot within 0.6 of the
    blink origin that the blink left behind (landing > 0.9 from it), and no heart lost for 1 s of world time. Not play-tested on device.
11. *Sign-in.* `meta_logged_on` fires from `SocialBridge.SignedIn` (via the tracker) and ignores developer mode, as planned; the tutorial
    achievement ignores `paysRealDust` (it happens in the tutorial scene) but not developer mode.
12. *Cloud.* The legacy `achv_count_*` / `achv_progress_*` keys stay in the snapshot's counter list for one release (migration seed);
    the new keys are added. Currency still merges "newer wins": a claim on one device can be overwritten by a newer save from another
    (the claimed flag survives, so it never pays twice, but the 25 may be lost). Documented risk, unchanged.

13. *Legacy `HighestSpeed` fallback dropped.* The plan's fallback for `speed_flash` was removed: `SpeedCapTest` forbids code outside the save field reading a highest speed, and pre-cap speeds are not comparable to the cap anyway. Those three unlock in play.

**Owner-only steps** are in `docs/achievements-export/README.md`.
