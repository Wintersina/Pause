# add-world checklist

Tick as you go. `REF` = Ember (the newest finished world); `W` = the new world.
Regenerate the live list at any time with
`.claude/skills/add-world/scripts/world_audit.sh Ember <World>` (section 2 = hard-coded
counts, section 5 = reference files that do not mention the new world yet).

## A. File-by-file touch list (code under `Pause/Assets/Scripts/`)

| File | Change | Phase |
| --- | --- | --- |
| `Worlds/WorldManager.cs` | `Worlds[]` entry (`speedRampPerSecond`, `enemyRampScale`, `portalColor`, `musicResource ""`); header comments (chain, "last world") | 9, 10 |
| `Worlds/WorldPainter.cs` | `RailTextureName`, `RailBounds` (measured on the PNG), `VisibleRailEdges` world list, `EdgeFor` if needed | 9 |
| `Worlds/Planetfall/PlanetfallDef.cs` | `PlanetfallCatalog.W` + `All` | 10 |
| `Worlds/Planetfall/LiftoffDef.cs` | `LiftoffCatalog.W` (`autoLoop = true`) + `Defs`; **remove `autoLoop` from the old last world** | 10 |
| `Worlds/Planetfall/Liftoff.cs`, `Worlds/WorldTransition.cs`, `WorldManager.cs` | comments that name the last world | 10 |
| `Worlds/Backdrop/BackdropCatalog.cs` | `Spec` for W (layers, rates, tables, cloud knobs) | 11 |
| `Worlds/Backdrop/BackdropSet.cs` | `CreateDirector` case | 9 |
| `Worlds/Backdrop/<W>Backdrop.cs` | `<W>Tuning`, `<W>Director : PlanetDirector` (clone of `EmberBackdrop.cs`) | 11 |
| `Worlds/Backdrop/<W>Ambient.cs` | `<W>AmbientCatalog` (loops, bindings from `points.json`) | 11 |
| `Worlds/Backdrop/LandingSites.cs` | new `LandingKind` values + `KindOf` strings (+ doc comment) | 11, 14 |
| `Worlds/WorldMusic.cs` | **only when the user supplies a track**: `<W>Track` const, `LoopOutSeconds` | -- |
| `Gameplay/Enemies/EnemyRoster.cs` | `WorldKeys`, 12 `Def(...)`, explosion kinds | 9, 12 |
| `Gameplay/Enemies/EnemyBehaviours.cs` | 12 `B("<key>", ...)` (+ `ChaserStyle` decision) | 12 |
| `Gameplay/Enemies/EnemyPalette.cs` + `Art/Enemies/src~/palette.env`, `common.py` | colours + `ThemeFor(N)` | 12 |
| `Gameplay/Enemies/EnemyDensity.cs` | `PilotLoadAtLowSpeed/HighSpeed` entries | 9 |
| `Gameplay/Enemies/RailMineArt.cs` | second atlas routing, rects/pivots/core offsets for the new rows | 12 |
| `Gameplay/Weapons/TargetExplosion.cs` | `Kind`, `KindForWorld` (new `Explosions.png` row) or documented reuse | 12 |
| `Gameplay/Elites/*` | only for genuinely new brains/attacks/shots (`add-elite-ship`) | 14 |
| `Bosses/BossCatalog.cs` | `BossDef` entry (3 attacks, flash, heart colour, `damageKey/deathKey/smokeStrength`) | 9 (stub), 13 |
| `Bosses/BossEmitterTable.cs` | **generated** by `Art/BossAttacks/src~/measure_emitters.py` | 13 |
| `Bosses/BossHearts.cs` | `Body` ellipse case | 13 |
| `Bosses/BossWarning.cs` | `Accents` entry | 13 |
| `Bosses/BossAttackFx.cs` + `Art/BossAttacks/src~/build_boss_attack_fx.py` | a row (`Rows`) | 13 |
| `Codex/CodexCatalogue.cs` | `WorldIds`, world entry | 9, 16 |
| `Codex/CodexPanel.cs` | `MaxSections` | 9 |
| `Core/DeveloperUnlocks.cs` | F-key | 9 |
| (automatic, verify only) | `DeveloperOptions`, `LeaderboardBoards.FormatWorld`, `ScoreRules.WorldClearedPoints`, `MenuBackdrop.Candidates`, `Planetfall/Liftoff` renderers, `EnemyDeathAudio`, `PortalPressure`, `LoopRules` | -- |

## B. Tests and tools that pin the world list (update or extend)

Suites in `Pause/Assets/Editor/Tests/` (register new ones in `AllTests.Suites`):

- Worlds/order/loop: `WorldLogicTest` (`Worlds.Length == 4`, `HighestWorld == 3`, art path), `WorldGatingTest`,
  `WorldLeakTest` (comments mention Ember->Space; logic iterates), `LoopTest` (`const Ember = 3`, Ember lift-off
  stands aside), `LiftoffTest`, `OpenPortalTest`, `TransitionChargeTest` (`world == 3`, `LiftoffCatalog.Ember.autoLoop`),
  `PlanetfallTest`, `ReplayTest` (`Ember = 3`), `DeveloperModeTest` (`SelectWorld(3)`), `SpeedCapTest`
  (`Worlds[3]`), `DifficultyRetuneTest` (early ramp list `.315/.330/.345/.365`), `DifficultyRebalanceTest`
  (`Worlds[2] < Worlds[3]`), `WorldPaceTest`, `AccountCloudSaveTest` (no change needed; sanity).
- Backdrop/art: `WorldBackdropTest` (budget, caps, atlases, palette check, loops), `<W>BackdropTest` (new),
  `FrostBackdropTest`/`VerdantBackdropTest` (read for shared helpers), `MenuBackdropTest`, `BackdropCellClipTest`,
  `CloudCoverMeter`, `UnusedAssetGuardTest`, `WorldRailTest`, `RailMatchTest` (world loops), `RailsVettingTest`
  (`ForWorld(3)`, mine clamps in "all four worlds").
- Enemies: `EnemyRosterTest`, `EnemyBehaviourTest` (`styles.Count == 4`), `HazardSizeTest` (rock key lists),
  `EnemyDensityTest`, `EnemyDeathFlipbookTest`, `EnemyDeathAudioTest` (`<W>Keys`, `ScreamingNew`),
  `RailMineArtTest` (SHA pin), `RailMineLaserTest`, `ExplosionV2Test`, `AtomClarityTest` (`Worlds`),
  `HostileProjectileTest` (`worlds` x2), `HostileFireTest`, `CodexTest` (`WorldIds`, hazards list, `Discover(Codex.WorldId(3))`).
- Boss: `BossAttackTest` (`BossCatalog.All[3]`), `BossEncounterTest` (`{ "Space","Frost","Verdant","Ember" }`,
  Ember loop-portal case), `BossHeartsTest` (`worlds` list, `bold` array), `BossDamageTest` (artKey literals, death strip),
  `BossIntroTest`, `BossWarningTest`.
- Elites: `EliteTest` (counts per world), `EliteEvasionTest`, `<W>EliteTest` (new, model `FrostEliteTest`).
- Tools: `ReadabilitySweep.Worlds`, `Previews/{AtomClarity,BossHearts,Explosion,HostileProjectile,MineLaser}Preview`
  `Worlds` arrays, `ShipReachRender`, `BossDamagePreview`, `BossWarningPreview`, `ElitePreview`,
  `LiftoffPreview`/`PlanetfallPreview` (env vars only), a new `<W>BackdropPreview` (clone of `EmberBackdropPreview`).

## C. Definition of done per phase

**0 Brief** -- user said yes to: name/order/hook, palette (collision check written down), 12 enemies (11 strips + mine), 5-6 elites, boss
concept + 3 attack parts, difficulty tier, chaser-style decision, sound identity.

**1 Planet art** -- 7 files at exact sizes; `verify_planetfall_art.py` PASS; entry plasma is wide, not a thin ribbon; cloud
decks match the backdrop ceiling; masters kept in `descent~/`; preview sheet + gif opened for the user; committed on `art/<job>`.

**2 Backdrop art** -- A: 4 sets (one night), `verify_backdrop_tiles.py` PASS, sky/far as strong as mid (redo pass done if
not); B: 5 atlases + JSON, margins >= 14, lots of pipes/smoke/fire sources; C: loops seamless, bases on anchor,
blink ratios >= 4 / 2.2, particles visible; manifests with measured numbers; user saw the sheets.

**3 Enemy strips** -- 11 strips (+ mine rows) PASS `verify_enemy_strip.py` + `audit_cells.py`; stand-out on the world's
tiles checked on a contact sheet; fix pass done; floating rock present; no off-palette reds.

**4 Deaths** -- 12 strips (mine, big included) PASS `--death`; timing/anchor rules; user saw the preview gif.

**5 Rails** -- 725x2170, seamless, visibility ~ Space's; `RailBounds` measured.

**6 Boss art** -- base atlas (20 cells), shots, card; damage (4 dramatic stages), fx (bold smoke), death (6 cells);
`verify_boss_art.py` PASS with the WARN list shrinking, not growing; user saw the damage sheet.

**7 Elite art** -- 5-6 strips + manifest + concepts; distinct silhouettes; margins >= 16.

**8 Sounds** -- WAVs for every roster key + elite (3 variants); screams only for living occupants; `verify_wavs.py` PASS;
audition WAV + sheet delivered; notes list which cues need ear tuning.

**9 Scaffold** -- compiles; in every list; `world_audit.sh` sections 2 and 5 empty; pinned tests generic or extended;
`EnemyDensity` entries; ramp numbers validated by `DifficultyRetuneTest`/`WorldPaceTest`.

**10 Planetfall/lift-off** -- def numbers measured; old last world's `autoLoop` removed, new one set; tests moved;
previews opened (descent and lift-off at two device aspect ratios); `WorldTransition.InProgress` clean at the end.

**11 Backdrop wiring** -- Spec, Tuning, Director, Ambient; masks + points committed; smoke bases within 3 px of
emitters; clouds: <= ~10% by 10 s, gone by ~14 s; launch sites; all `*BackdropTest` + `WorldBackdropTest` green vs
control; real-frame previews for all 4 variants at 2/10/40 s.

**12 Roster** -- 12 defs + behaviours + palette + explosion + mine atlas; spawn tests green; no wrong-world leaks.

**13 Boss** -- catalog, emitters regenerated, hearts ellipse, warning accent, fx row; attack previews show fair telegraphs.

**14 Elites** -- each its own brain/attack; muzzles/nozzles measured; sites per kind; `EliteTest` + `<W>EliteTest` green.

**15 Audio** -- keys registered; `EnemyDeathAudioTest` green; user listened.

**16 Meta** -- codex entry/sections/chips fit, menu pool, dev picker, leaderboard range noted for the user, docs updated.

**17 Release** -- full `RunAll` on the integration branch == control + only intended diffs; soak test green;
`ReadabilitySweep` no new LOW/WEAK; Mac + Android dev builds Succeeded; hygiene below; user go-ahead.

## D. World release gate (present this to the user)

- [ ] World loop plays end to end in the soak test: `... -> prev lift-off -> <W> planetfall -> <W> -> boss -> <W> lift-off -> Space (loop n+1)` and, for a run that began in `<W>`, the loop portal back to `<W>`.
- [ ] Space continues cleanly after `<W>`: score/hearts/pauses/dust carried, speed = `ArrivalSpeed(loop)`, Space enemies only (no `<W>` leftovers), calm window re-armed, no portal shown.
- [ ] Previews seen by the user: planet + planetfall + lift-off, 4 backdrop variants (real frames), every enemy (idle + death), elites, boss attacks + damage + death, rails, menu backdrop pool, codex page.
- [ ] Readability: `ReadabilitySweep` before/after; hearts/shots outlines pass on every variant; lane contrast guard passes.
- [ ] Audio: authored cues for every key; loudness table PASS; user listened.
- [ ] Known-failure diff vs plain-master control = none new.
- [ ] Builds: Mac + Android dev `Succeeded` (no install).
- [ ] Store-side to-dos listed for the user: leaderboard `furthest_world` range, (Game Center / Play Console), anything the user authors (world music, ambient).
- [ ] Docs updated: `docs/enemy-behaviours.md`, `docs/art-style.md`, `docs/speed-and-loops.md`, `docs/art-production-queue.md`, `docs/leaderboards.md`.
- [ ] **Real-device pass only on the user's OK** (dev APK via `BuildScript.BuildAndroidDev`; never install unprompted).
- [ ] Merge: fast-forward only (`git merge --ff-only integrate/world-<w>` in the clean main checkout), only on the user's explicit go.

## E. Hygiene

- One worktree + branch per job; never commit in the main checkout; never touch other sessions' worktrees.
- Commit messages end with the attribution line given by the session; art commits carry the measured numbers.
- `.meta` only for new files; no importer rewrites (boss cards, `Backdrop3`, `anim_hires`).
- Staged art lives in `~` folders until the wiring change moves it (`git mv`); old art is deleted in that same change.
- Re-run a red suite once; compare to a plain-master control worktree before blaming your branch.
- Clean up finished worktrees only after their branch is merged and the user has no pending feedback on them.
