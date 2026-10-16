# Unused asset removal (2026-10-16)

Branch `chore/remove-unused-assets`, analysed on master 51a9acfd (after the Desert merge, PR #37), rebased onto
995ae7e5 (Storm backdrops, PR #39; it touches none of the deleted paths, and the Mac build was re-run green on it). Delete-only: no in-use asset,
.meta, import setting, pivot or resolution was touched. The one code change is a test row for the deleted tutorial sprite.
Git history is the undo (`git revert` of the category commit, or `git checkout <commit>^ -- <path>`).

## Totals

| | Before | After | Saved |
|---|---:|---:|---:|
| Files deleted (assets + .meta) | | | **115** |
| On disk, deleted files | | | **24.6 MB** (23.5 MiB) |
| `Pause/Assets` working tree (du) | 2,809,105 KiB | 2,785,068 KiB | 23.5 MiB |
| Mac player build, packed asset bytes (BuildReport) | 1,148,930,435 | 1,148,892,999 | 37,436 B |
| Mac player build, assets packed | 2,520 | 2,519 | 1 |
| Mac player build, total size (`summary.totalSize`) | 1,260,504,384 | 1,260,466,800 | 37,584 B |
| `.git` (count-objects size-pack) | 3.58 GiB | 3.58 GiB | 0 (history keeps every blob) |

Estimated APK/AAB saving: about 37 KB (tut_card, an uncompressed 96x96 RGBA sprite, is the only deleted file that shipped).
Everything else that was deleted never shipped: it was outside every Resources folder and no build scene reached it.

**Why the build saving is tiny.** The shipped set is already clean. Every one of the 1,648 files under the eight
Resources roots was matched to the loader that names it (literal path, or a composed name: `{world}_attack_{file}`,
`BossImpact/attack/{key}_{pose}_stage{n}`, `Boss_{World}` / `{World}_Main` music, `{key}_death`, `shared_{category}_{n}`,
`sky_0{1..4}`, `sun_{palette}`, `flares_{palette}`, `asteroid_{nn}`, `gate_open_{nn}`, `gate_film_{NNN}` (the `Kept`
list), `{Ship}_{Skin}.bytes`, `{Ship}_attack|power`, `ship{n}` prefabs, `tut_mouth_*` / `tut_eye_*`, Desert/Tide ambient
`*_2/_3/_4` layers, Planetfall `{world}_*`, `mask.txt` per variant, ...). All non-Resources assets that a build scene
does not reach are already exempt staging/tooling inputs (`UnusedAssetGuardTest.EveryAssetIsReachable` passes on
master). So the only real lever left is storage: the tilde folders below (1.96 GB, all git-tracked), which this PR does
not touch.

## Method (four independent signals per item)

1. **GUID references**: every `guid:` in every text-serialized file under `Assets` (scenes, prefabs, .asset, .mat,
   .controller, .anim, .overrideController, .playable, .json, .shader, .asmdef, .spriteatlas, .mixer, .lighting, ...),
   every `.meta` (importer cross-references), `ProjectSettings/*` (EditorBuildSettings scene list, icons, cursor,
   splash) and `Packages/*`. No addressables, sprite atlases, TMP assets or audio mixers exist in the project.
2. **String paths**: basename / Resources-relative path searched in runtime C# string literals (comments stripped),
   editor C#, json/txt/csv data and the src~ generators; composed names checked against their builders by hand.
3. **Unity's own dependency data**: a Mac player build (`BuildScript.BuildMac`, `BuildReport.packedAssets`,
   `Builds/size-report.txt`) before any deletion and after the last one; every deleted item was checked against it.
4. **Repo guards**: `UnusedAssetGuardTest` (exemptions), `RosterCleanupTest`, `EliteArtSync` (which source files it
   copies), `ShipOutlineTest` / `EliteTest` (which Elite source strips tests read), `docs/release/unused-assets-audit.md`.

Every deleted path is also absent from the file lists of all open PRs (#24, #28, #35, #38) and in-flight branches
(`art/storm-backdrops`, `art/weather-gust-art`, `art/tide-rocks`, `ios/prep`).

## Verification

- Mac player build before and after: both `Succeeded`, 0 errors; no missing-reference / missing-script / "could not be
  found" line in either Editor.log. The size reports differ by exactly one line (`tut_card.png`).
- `git status` after both editor runs: clean (no import-setting or GUID drift on any remaining asset).
- `AllTests.RunAll` after the last deletion: 249 suites, 248 PASS, including `UnusedAssetGuardTest`, `TutorialRobotTest`,
  `RosterCleanupTest`, `EliteTest`, `ShipOutlineTest`, `WorldBackdropTest`, `SpacePlanetSheetTest`, `PlanetfallTest`,
  `MenuBackdropTest`, the TitleScreen suites (startS4) and the World* suites. The 1 failure, `WeatherOverhaulTest`, is
  already on master and has nothing to do with assets: its reflection call to `BossWaves.Choose` passes 7 arguments,
  but d8e7d6cb gave `Choose` an 8th optional parameter (`TargetParameterCountException`).

## Deleted, by category

1. **Orphan metas** (17): folder `.meta` files whose folder no longer exists (`Art/Atoms`, `Art/Weapons/<Ship>` x15,
   `Art/Weapons/Explosions`). Weapon art lives in `Art/Resources/Weapons`; only tilde folders sit beside these metas.
2. **Enemy art (staging)** (43 + folder meta): `Art/Enemies/Staging/{Space,Frost,Verdant}/*_strip_candidate.png`,
   `*_pixel_candidate.png` and `Staging/Shared/fighters_pixel_candidates_contact.png`. Oct 7 candidates, superseded by
   the re-cut, refreshed strips in `Art/Resources/Enemies` (Oct 9-13); none byte-identical to a shipped strip; the ones
   that were a shipped strip's predecessor are already preserved under `src~/*_fixes/original`. The `*_concept.png`
   sheets stay (recell.py input). `Staging/Ember` is left to PR #28, which deletes it.
3. **Backdrops (staging)** (4 + folder meta): `Art/Worlds/Space/staging/{anim,fx}_pixel_v1.{png,json}`, pixel-v1 drafts
   of the Space planet / fx atlases; the game loads `Backdrop/anim_hires` (+`anim`) and `fx` from
   `Art/Backgrounds/Resources`.
4. **UI / tutorial** (1): `Art/Resources/Tutorial/tut_card.png`. Not loaded since b476c788 (the tutorial-complete card
   wears the `dp_*` plates). It shipped. `TutorialRobotTest`'s kit table drops its row (20 -> 19 sprites).
   `Art/UI/Tutorial/src~/build_kit.py` still draws it, so re-running that generator would bring it back.

No audio, boss art, ship art/skins, prefabs, scenes or materials met the bar: every one is reached by a build scene, a
Resources loader or a test-pinned tool input.

## Uncertain: kept, for the user to decide

| Item | Size | Ships? | Why kept |
|---|---:|---|---|
| `Audio/Music/BoostingMusic.mp3` | 5.56 MB | no | Nothing plays it, but `UnusedAssetGuardTest` exempts it on purpose ("stays until the user decides to delete it or reuse it", restored by c45e714a) and music is the user's domain. To drop it: delete the file + .meta and the exemption line in `UnusedAssetGuardTest.Exempt`. |
| `Art/Backgrounds/Resources/Worlds/Space/Backdrop/anim.png` + `anim.json` | 1.52 MB | **yes** | Only the fallback of `BackdropSet.LoadAnimAtlas` (`anim_hires` exists), but `WorldBackdropTest` requires it and `SpacePlanetSheetTest` compares the 1x and 2x sheets. Removing it means rewriting those tests and the fallback, not a pure delete. |
| `Art/Enemies/Staging/{Space,Frost,Verdant}/*_concept.png` (27) | 30.97 MB | no | Free-layout pose sheets that `src~/recell.py` cuts strips from; may still be the source of today's Space/Frost/Verdant strips. Obsolete once those worlds get the Tide-method repaint (as Ember's do in #28). |
| `Art/Enemies/Elite/*/*_concept.png` (33), `contact_sheet.png` (4), `preview.gif` (4) | 54.4 MB | no | Codex sources / review aids of the current elites. `EliteArtSync` skips them; nothing reads them. |
| `Art/UI/Splash/HapticGate.png` | 14 KB | no | The user's original splash mark; ArtRestyleTest / SplashLayoutTest pin its bytes. |
| Identical frames in `Art/Resources/Pickups/Atoms` (`cooldown_idle_{0,2,4,6,8}` = `_{1,3,5,7,9}`, the four `*_burst_0`), `PauseGlowFx/pausedGlow_{a,b}_2` = `PauseGlow/pausedGlow_{a,b}` | < 0.1 MB | yes | Duplicates by md5, but the loaders address every frame by name; sharing them needs a code change, not a delete. |

## Delete after the in-flight PRs merge

| After | Then delete (if still unreferenced) | Size |
|---|---|---:|
| #38 Space elites (engine burn) | `Art/Enemies/Elite/Space/*_strip_candidate.png` (4, old review candidates described by the manifest #38 rewrites), `Art/Enemies/Elite/Space/*_concept.png` (4) if #38's masters replace them; storage-only: `src~/space_elites_restyle/original/*` (0.44 MB), `src~/ship_scale_border~/original/space_elite_*` (0.81 MB) | ~3.5 MB |
| #28 Ember redesign | #28 itself deletes `Staging/Ember` and the Ember elite candidates. Afterwards check `Art/Enemies/Elite/Ember/*_concept.png` and storage-only `src~/ship_scale_border~/original/ember*` (0.32 MB) | ~2 MB |
| #24 Storm enemies | storage-only: `src~/palette_unify/original/**/storm*` (2.85 MB), `src~/ship_scale_border~/original/*storm*` (1.05 MB), `Art/Enemies/Elite/Storm/contact_sheet.png` + `preview.gif` once the repaint lands | ~4.5 MB |
| Storm backdrops (#39, merged during this work) | #39 itself deleted `Backdrop3/v4` (the Storm variant that left the list). Storage-only now: the old Storm `backdrop_v3~` sources (15.5 MB) if `backdrop_v4~` replaces them. Note: on import, Unity rewrites the merged `Storm/Backdrop3/eye.png.meta` (filterMode 0 -> 1, alphaIsTransparency 0 -> 1); this branch reverted that drift, it belongs to #39's follow-up | ~15.5 MB |
| #35 Cinder Drake follow-ups | nothing extra found (touches 2 files) | 0 |
| Desert (#37, merged) | storage-only, superseded by the merged repaint: `src~/palette_unify/original/**/desert*` (3.16 MB), `src~/desert_enemies/strips/original/*` (0.29 MB), `Art/Worlds/Desert/descent~/original/*` (1.91 MB); `Art/Enemies/Elite/Desert/STAND_IN.md` is out of date | ~5.4 MB |

## Storage-only: tilde folders (not deleted; Unity never imports or ships them)

Unity ignores any folder ending in `~`, so these cost nothing in a build; they are Codex sources, prompts, generator
scripts, review renders and `original/` copies the pipelines need. All 1.96 GB is git-tracked. Candidates for the user
to prune or move out of the repo (e.g. to an art archive): the `original/` copies under `Art/Enemies/src~`
(palette_unify, ship_scale_border~, *_fixes), `Worlds/Space/backdrop_v3_original~` (12.7 MB), `Audio/Legacy~` (9.5 MB),
`Weapons/ExplosionsV1~` (0.5 MB) and per-world `backdrop_v3~` sources once a v4 replaces them.

| Folder | Size on disk | Files | Git-tracked |
|---|---:|---:|---:|
| `Pause/Assets/Art/Enemies/src~` | 370.1 MB | 998 | 370.1 MB (998) |
| `Pause/Assets/Art/Worlds/Tide/backdrop_v3~` | 162.4 MB | 131 | 162.4 MB (131) |
| `Pause/Assets/Art/Worlds/Ember/backdrop_v3~` | 88.8 MB | 66 | 88.8 MB (66) |
| `Pause/Assets/Art/Worlds/Space/hires_planets~` | 81.8 MB | 30 | 81.8 MB (30) |
| `Pause/Assets/Art/Worlds/Desert/backdrop_v3~` | 68.6 MB | 95 | 68.6 MB (95) |
| `Pause/Assets/Art/Worlds/Frost/backdrop_v3/variants/src~` | 67.7 MB | 36 | 67.7 MB (36) |
| `Pause/Assets/Art/Worlds/Verdant/backdrop_v3/src~` | 61.9 MB | 40 | 61.9 MB (40) |
| `Pause/Assets/Art/Worlds/Desert/descent~` | 57.1 MB | 32 | 57.1 MB (32) |
| `Pause/Assets/Art/Bosses/Ember~` | 54.6 MB | 34 | 54.6 MB (34) |
| `Pause/Assets/Art/Bosses~` | 46.0 MB | 233 | 46.0 MB (233) |
| `Pause/Assets/Art/Worlds/Frost/backdrop_v3/src~` | 45.5 MB | 39 | 45.5 MB (39) |
| `Pause/Assets/Art/Worlds/Storm/descent~` | 44.2 MB | 31 | 44.2 MB (31) |
| `Pause/Assets/Art/Bosses/TideDamage~` | 42.5 MB | 28 | 42.5 MB (28) |
| `Pause/Assets/Art/Worlds/Tide/descent~` | 39.1 MB | 23 | 39.1 MB (23) |
| `Pause/Assets/Art/AppIcon/src~` | 38.8 MB | 26 | 38.8 MB (26) |
| `Pause/Assets/Art/Worlds/Desert/worm~` | 38.3 MB | 38 | 38.3 MB (38) |
| `Pause/Assets/Art/Attacks/Verdant~` | 35.1 MB | 58 | 35.1 MB (58) |
| `Pause/Assets/Art/Bosses/StormDamage~` | 34.2 MB | 22 | 34.2 MB (22) |
| `Pause/Assets/Art/Bosses/EmberDamage~` | 33.4 MB | 27 | 33.4 MB (27) |
| `Pause/Assets/Art/Bosses/VerdantDamage~` | 32.1 MB | 27 | 32.1 MB (27) |
| `Pause/Assets/Art/Bosses/Tide~` | 31.4 MB | 18 | 31.4 MB (18) |
| `Pause/Assets/Art/Enemies/Elite/Storm/src~` | 27.4 MB | 22 | 27.4 MB (22) |
| `Pause/Assets/Art/Enemies/Elite/Desert/src~` | 26.7 MB | 25 | 26.7 MB (25) |
| `Pause/Assets/Art/Bosses/src~` | 26.3 MB | 8 | 26.3 MB (8) |
| `Pause/Assets/Art/Bosses/DesertDamage~` | 24.0 MB | 18 | 24.0 MB (18) |
| `Pause/Assets/Art/Bosses/Storm~` | 23.4 MB | 16 | 23.4 MB (16) |
| `Pause/Assets/Art/Worlds/Space/backdrop_v4~` | 23.1 MB | 26 | 23.1 MB (26) |
| `Pause/Assets/Art/Bosses/Desert~` | 21.0 MB | 15 | 21.0 MB (15) |
| `Pause/Assets/Art/Bosses/DeathStrips~` | 20.8 MB | 15 | 20.8 MB (15) |
| `Pause/Assets/Art/Resources/ShipArt/Hulls/src~` | 20.4 MB | 96 | 20.4 MB (96) |
| `Pause/Assets/Art/Bosses/FrostDamage~` | 19.1 MB | 18 | 19.1 MB (18) |
| `Pause/Assets/Art/Worlds/Verdant/descent/src~` | 17.9 MB | 11 | 17.9 MB (11) |
| `Pause/Assets/Art/Worlds/Frost/descent/src~` | 17.0 MB | 12 | 17.0 MB (12) |
| `Pause/Assets/Art/Enemies/Elite/Frost/src~` | 16.8 MB | 12 | 16.8 MB (12) |
| `Pause/Assets/Art/Boulders~` | 16.6 MB | 61 | 16.6 MB (61) |
| `Pause/Assets/Art/Worlds/Storm/backdrop_v3~` | 15.5 MB | 40 | 15.5 MB (40) |
| `Pause/Assets/Art/Worlds/Ember/descent~` | 14.1 MB | 10 | 14.1 MB (10) |
| `Pause/Assets/Audio/EnemyDeathSrc~` | 13.5 MB | 22 | 13.5 MB (22) |
| `Pause/Assets/Art/Worlds/Storm/rails~` | 13.2 MB | 12 | 13.2 MB (12) |
| `Pause/Assets/Art/Worlds/Space/backdrop_v3_original~` | 12.7 MB | 10 | 12.7 MB (10) |
| `Pause/Assets/Art/Weapons/ExplosionsV2~` | 12.2 MB | 11 | 12.2 MB (11) |
| `Pause/Assets/Art/Attacks/Ember~` | 10.2 MB | 20 | 10.2 MB (20) |
| `Pause/Assets/Art/Weather~` | 9.5 MB | 45 | 9.5 MB (45) |
| `Pause/Assets/Audio/Legacy~` | 9.5 MB | 2 | 9.5 MB (2) |
| `Pause/Assets/Art/Enemies/Elite/Tide/src~` | 8.7 MB | 8 | 8.7 MB (8) |
| `Pause/Assets/Art/Worlds/Tide/rails~` | 8.2 MB | 9 | 8.2 MB (9) |
| `Pause/Assets/Art/Attacks/Tide~` | 7.3 MB | 10 | 7.3 MB (10) |
| `Pause/Assets/Art/Attacks/Space~` | 6.2 MB | 12 | 6.2 MB (12) |
| `Pause/Assets/Art/Enemies/Elite/Verdant/new/src~` | 6.1 MB | 7 | 6.1 MB (7) |
| `Pause/Assets/Art/Attacks/Frost~` | 5.8 MB | 12 | 5.8 MB (12) |
| `Pause/Assets/Art/Attacks/MineBeams~` | 4.0 MB | 35 | 4.0 MB (35) |
| `Pause/Assets/Art/Attacks/WorldCues~` | 3.8 MB | 25 | 3.8 MB (25) |
| `Pause/Assets/Art/UI/Tutorial/src~` | 3.8 MB | 26 | 3.8 MB (26) |
| `Pause/Assets/Art/Achievements/src~` | 3.2 MB | 83 | 3.2 MB (83) |
| `Pause/Assets/Art/StoreListing~` | 3.1 MB | 4 | 3.1 MB (4) |
| `Pause/Assets/Art/Bosses/SpaceDamage~` | 1.7 MB | 3 | 1.7 MB (3) |
| `Pause/Assets/Art/Weapons~` | 1.5 MB | 33 | 1.5 MB (33) |
| `Pause/Assets/Art/Enemies/Elite/Storm/hd~` | 1.4 MB | 5 | 1.4 MB (5) |
| `Pause/Assets/Art/Enemies/Elite/Desert/hd~` | 1.4 MB | 5 | 1.4 MB (5) |
| `Pause/Assets/Art/Enemies/Elite/Verdant/hd~` | 1.2 MB | 4 | 1.2 MB (4) |
| `Pause/Assets/Art/Enemies/Elite/Tide/hd~` | 1.2 MB | 4 | 1.2 MB (4) |
| `Pause/Assets/Art/Enemies/Elite/Space/src~` | 1.1 MB | 3 | 1.1 MB (3) |
| `Pause/Assets/Art/UI/Codex/src~` | 1.1 MB | 5 | 1.1 MB (5) |
| `Pause/Assets/Art/Enemies/Elite/Frost/hd~` | 0.8 MB | 3 | 0.8 MB (3) |
| `Pause/Assets/Audio/EnemyShotSrc~` | 0.6 MB | 3 | 0.6 MB (3) |
| `Pause/Assets/Art/HapticGate~` | 0.6 MB | 4 | 0.6 MB (4) |
| `Pause/Assets/Art/Weapons/ExplosionsV1~` | 0.5 MB | 1 | 0.5 MB (1) |
| `Pause/Assets/Art/Enemies/Elite/Tide/thunderfin_hd~` | 0.4 MB | 4 | 0.4 MB (4) |
| `Pause/Assets/Audio/PortalWarpSrc~` | 0.3 MB | 2 | 0.3 MB (2) |
| `Pause/Assets/Art/Worlds/src~` | 0.2 MB | 14 | 0.2 MB (14) |
| `Pause/Assets/Art/Weapons/src~` | 0.1 MB | 5 | 0.1 MB (5) |
| `Pause/Assets/Art/PixelKit/src~` | 0.1 MB | 2 | 0.1 MB (2) |
| `Pause/Assets/Art/Worlds/Space/src~` | 0.0 MB | 7 | 0.0 MB (7) |
| `Pause/Assets/Art/Pickups/src~` | 0.0 MB | 1 | 0.0 MB (1) |
| `Pause/Assets/Art/Enemies/Elite/src~` | 0.0 MB | 5 | 0.0 MB (5) |
| `Pause/Assets/Art/BossAttacks/src~` | 0.0 MB | 2 | 0.0 MB (2) |
| `Pause/Assets/Art/UI/Dock/src~` | 0.0 MB | 24 | 0.0 MB (24) |
| `Pause/Assets/Art/Resources/Vfx/src~` | 0.0 MB | 1 | 0.0 MB (1) |
| `Pause/Assets/Art/Ships/Shield/src~` | 0.0 MB | 5 | 0.0 MB (5) |
| `Pause/Assets/Art/UI/DeathPanel/src~` | 0.0 MB | 10 | 0.0 MB (10) |
| `Pause/Assets/Art/UI~` | 0.0 MB | 1 | 0.0 MB (1) |
| `Pause/Assets/Art/UI/Icons/src~` | 0.0 MB | 2 | 0.0 MB (2) |
| `Pause/Assets/Art/UI/Pause/src~` | 0.0 MB | 1 | 0.0 MB (1) |
| `Pause/Assets/Art/UI/ResumeFx/src~` | 0.0 MB | 1 | 0.0 MB (1) |
| `Pause/Assets/Art/UI/Hud/src~` | 0.0 MB | 3 | 0.0 MB (3) |
| **total (85 folders)** | **1959 MB** | | **1959 MB** |

## Manifest (machine-readable)

Paths are relative to `Pause/`. `files` lists every file removed for the item.

| Category | Path | Bytes (asset + .meta) | Shipped before? | Packed bytes in baseline build |
|---|---|---:|---|---:|
| Orphan metas | `Assets/Art/Atoms.meta` | 172 | no | 0 |
| Orphan metas | `Assets/Art/Weapons/CrimsonHalo.meta` | 172 | no | 0 |
| Orphan metas | `Assets/Art/Weapons/Dove.meta` | 172 | no | 0 |
| Orphan metas | `Assets/Art/Weapons/Explosions.meta` | 172 | no | 0 |
| Orphan metas | `Assets/Art/Weapons/GoldWarden.meta` | 172 | no | 0 |
| Orphan metas | `Assets/Art/Weapons/IonLancer.meta` | 172 | no | 0 |
| Orphan metas | `Assets/Art/Weapons/JadePhantom.meta` | 172 | no | 0 |
| Orphan metas | `Assets/Art/Weapons/Ligher.meta` | 172 | no | 0 |
| Orphan metas | `Assets/Art/Weapons/Lightning.meta` | 172 | no | 0 |
| Orphan metas | `Assets/Art/Weapons/NeonComet.meta` | 172 | no | 0 |
| Orphan metas | `Assets/Art/Weapons/Ninja.meta` | 172 | no | 0 |
| Orphan metas | `Assets/Art/Weapons/Paranoid.meta` | 172 | no | 0 |
| Orphan metas | `Assets/Art/Weapons/Saboteur.meta` | 172 | no | 0 |
| Orphan metas | `Assets/Art/Weapons/SolarFang.meta` | 172 | no | 0 |
| Orphan metas | `Assets/Art/Weapons/Turtle.meta` | 172 | no | 0 |
| Orphan metas | `Assets/Art/Weapons/UFO.meta` | 172 | no | 0 |
| Orphan metas | `Assets/Art/Weapons/VoltViper.meta` | 172 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Frost/frost_alien_pixel_candidate.png` | 353129 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Frost/frost_alien_rugged_tall_v2_strip_candidate.png` | 249551 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Frost/frost_big_rugged_v2_strip_candidate.png` | 706315 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Frost/frost_chaser_rugged_v2_strip_candidate.png` | 222712 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Frost/frost_fighter_1_pixel_candidate.png` | 395427 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Frost/frost_fighter_1_rugged_v2_strip_candidate.png` | 377710 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Frost/frost_fighter_2_pixel_candidate.png` | 354648 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Frost/frost_fighter_2_rugged_v2_strip_candidate.png` | 356744 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Frost/frost_fighter_3_pixel_candidate.png` | 377887 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Frost/frost_fighter_3_rugged_v2_strip_candidate.png` | 286886 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Frost/frost_fighter_4_pixel_candidate.png` | 376114 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Frost/frost_fighter_4_rugged_v2_strip_candidate.png` | 371009 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Frost/frost_rock_shard_rugged_v2_strip_candidate.png` | 317879 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Shared/fighters_pixel_candidates_contact.png` | 5816052 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Space/space_alien_arm_idle_v3_strip_candidate.png` | 192056 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Space/space_alien_pixel_candidate.png` | 377468 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Space/space_alien_rugged_tall_v2_strip_candidate.png` | 368002 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Space/space_chaser_rugged_v2_strip_candidate.png` | 199186 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Space/space_fighter_1_pixel_candidate.png` | 455888 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Space/space_fighter_1_rugged_v2_strip_candidate.png` | 371128 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Space/space_fighter_2_pixel_candidate.png` | 538268 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Space/space_fighter_2_rugged_v2_strip_candidate.png` | 512317 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Space/space_fighter_2_rugged_v3_strip_candidate.png` | 474074 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Space/space_fighter_3_pixel_candidate.png` | 504855 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Space/space_fighter_3_rugged_v2_strip_candidate.png` | 515083 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Space/space_fighter_4_pixel_candidate.png` | 477897 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Space/space_fighter_4_rugged_v2_strip_candidate.png` | 596359 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Space/space_rock_cluster_rugged_v2_strip_candidate.png` | 357224 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Space/space_rock_crater_rugged_v2_strip_candidate.png` | 220025 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Space/space_rock_dark_rugged_v2_strip_candidate.png` | 319957 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Verdant/verdant_alien_rugged_v2_strip_candidate.png` | 305700 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Verdant/verdant_big_rugged_v2_strip_candidate.png` | 430749 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Verdant/verdant_chaser_rugged_v2_strip_candidate.png` | 199617 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Verdant/verdant_fighter_1_pixel_candidate.png` | 348642 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Verdant/verdant_fighter_1_rugged_v2_strip_candidate.png` | 342597 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Verdant/verdant_fighter_2_pixel_candidate.png` | 350035 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Verdant/verdant_fighter_2_rugged_v3_strip_candidate.png` | 265613 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Verdant/verdant_fighter_3_pixel_candidate.png` | 345776 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Verdant/verdant_fighter_3_rugged_v2_strip_candidate.png` | 251255 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Verdant/verdant_fighter_4_pixel_candidate.png` | 341457 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Verdant/verdant_fighter_4_rugged_v2_strip_candidate.png` | 344901 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Verdant/verdant_rock_spore_rugged_v2_strip_candidate.png` | 416407 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Verdant/verdant_rock_vine_rugged_v2_strip_candidate.png` | 287184 | no | 0 |
| Enemy art (staging) | `Assets/Art/Enemies/Staging/Shared.meta` | (deleted) | no | 0 |
| Backdrops (staging) | `Assets/Art/Worlds/Space/staging/anim_pixel_v1.json` | 1234 | no | 0 |
| Backdrops (staging) | `Assets/Art/Worlds/Space/staging/anim_pixel_v1.png` | 1758302 | no | 0 |
| Backdrops (staging) | `Assets/Art/Worlds/Space/staging/fx_pixel_v1.json` | 1465 | no | 0 |
| Backdrops (staging) | `Assets/Art/Worlds/Space/staging/fx_pixel_v1.png` | 1573927 | no | 0 |
| Backdrops (staging) | `Assets/Art/Worlds/Space/staging.meta` | (deleted) | no | 0 |
| UI / tutorial | `Assets/Art/Resources/Tutorial/tut_card.png` | 3922 | yes | 37436 |

```json
{
 "summary": {
  "files_deleted": 115,
  "bytes_deleted_on_disk": 24613557,
  "build_packed_bytes_before": 1148930435,
  "build_packed_bytes_after": 1148892999,
  "build_assets_before": 2520,
  "build_assets_after": 2519
 },
 "items": [
  {
   "category": "orphan-metas",
   "path": "Assets/Art/Atoms.meta",
   "files": [
    "Assets/Art/Atoms.meta"
   ],
   "bytes": 172,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "c7ff5b199b8cd45e2a38d40d58de6177",
   "guid_refs": []
  },
  {
   "category": "orphan-metas",
   "path": "Assets/Art/Weapons/CrimsonHalo.meta",
   "files": [
    "Assets/Art/Weapons/CrimsonHalo.meta"
   ],
   "bytes": 172,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "2055c966f12cd42daaee9c933aa5bdb5",
   "guid_refs": []
  },
  {
   "category": "orphan-metas",
   "path": "Assets/Art/Weapons/Dove.meta",
   "files": [
    "Assets/Art/Weapons/Dove.meta"
   ],
   "bytes": 172,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "b217bd78262f343328e7092473d9c962",
   "guid_refs": []
  },
  {
   "category": "orphan-metas",
   "path": "Assets/Art/Weapons/Explosions.meta",
   "files": [
    "Assets/Art/Weapons/Explosions.meta"
   ],
   "bytes": 172,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "23ac1ecf748d14bfabde902faeb0ca68",
   "guid_refs": []
  },
  {
   "category": "orphan-metas",
   "path": "Assets/Art/Weapons/GoldWarden.meta",
   "files": [
    "Assets/Art/Weapons/GoldWarden.meta"
   ],
   "bytes": 172,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "e73c59a41e5d84ca2afb39afb4fd551b",
   "guid_refs": []
  },
  {
   "category": "orphan-metas",
   "path": "Assets/Art/Weapons/IonLancer.meta",
   "files": [
    "Assets/Art/Weapons/IonLancer.meta"
   ],
   "bytes": 172,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "ad16e4b39fd6340449b90afb42dc9824",
   "guid_refs": []
  },
  {
   "category": "orphan-metas",
   "path": "Assets/Art/Weapons/JadePhantom.meta",
   "files": [
    "Assets/Art/Weapons/JadePhantom.meta"
   ],
   "bytes": 172,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "1a6aa16a420d0481a80033fe0b4912a9",
   "guid_refs": []
  },
  {
   "category": "orphan-metas",
   "path": "Assets/Art/Weapons/Ligher.meta",
   "files": [
    "Assets/Art/Weapons/Ligher.meta"
   ],
   "bytes": 172,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "00547e90ee4e0463e95e54d25a93a8f3",
   "guid_refs": []
  },
  {
   "category": "orphan-metas",
   "path": "Assets/Art/Weapons/Lightning.meta",
   "files": [
    "Assets/Art/Weapons/Lightning.meta"
   ],
   "bytes": 172,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "2e6ae5ac4c9104a8caaccd296f97e4af",
   "guid_refs": []
  },
  {
   "category": "orphan-metas",
   "path": "Assets/Art/Weapons/NeonComet.meta",
   "files": [
    "Assets/Art/Weapons/NeonComet.meta"
   ],
   "bytes": 172,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "8040330a31ea64695a4547e0abfb117f",
   "guid_refs": []
  },
  {
   "category": "orphan-metas",
   "path": "Assets/Art/Weapons/Ninja.meta",
   "files": [
    "Assets/Art/Weapons/Ninja.meta"
   ],
   "bytes": 172,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "359eb7e97885c411aa607f0976499c3f",
   "guid_refs": []
  },
  {
   "category": "orphan-metas",
   "path": "Assets/Art/Weapons/Paranoid.meta",
   "files": [
    "Assets/Art/Weapons/Paranoid.meta"
   ],
   "bytes": 172,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "26ffcbc5ac9df419b8ffbba2c5f556fe",
   "guid_refs": []
  },
  {
   "category": "orphan-metas",
   "path": "Assets/Art/Weapons/Saboteur.meta",
   "files": [
    "Assets/Art/Weapons/Saboteur.meta"
   ],
   "bytes": 172,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "0b57c3741e5714de1bd5202f98ed9abc",
   "guid_refs": []
  },
  {
   "category": "orphan-metas",
   "path": "Assets/Art/Weapons/SolarFang.meta",
   "files": [
    "Assets/Art/Weapons/SolarFang.meta"
   ],
   "bytes": 172,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "1af75eb50c74648e08c6f6288bbbca97",
   "guid_refs": []
  },
  {
   "category": "orphan-metas",
   "path": "Assets/Art/Weapons/Turtle.meta",
   "files": [
    "Assets/Art/Weapons/Turtle.meta"
   ],
   "bytes": 172,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "1bbd32bf6513e4dada995a5ca909505e",
   "guid_refs": []
  },
  {
   "category": "orphan-metas",
   "path": "Assets/Art/Weapons/UFO.meta",
   "files": [
    "Assets/Art/Weapons/UFO.meta"
   ],
   "bytes": 172,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "a841e89f96564451d87024c92f859d5a",
   "guid_refs": []
  },
  {
   "category": "orphan-metas",
   "path": "Assets/Art/Weapons/VoltViper.meta",
   "files": [
    "Assets/Art/Weapons/VoltViper.meta"
   ],
   "bytes": 172,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "4ecb2df3614a646e7b4e0ccbc51098da",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Frost/frost_alien_pixel_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Frost/frost_alien_pixel_candidate.png",
    "Assets/Art/Enemies/Staging/Frost/frost_alien_pixel_candidate.png.meta"
   ],
   "bytes": 353129,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "0af56f156a68c43b09b04fd0858d787c",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Frost/frost_alien_rugged_tall_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Frost/frost_alien_rugged_tall_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Frost/frost_alien_rugged_tall_v2_strip_candidate.png.meta"
   ],
   "bytes": 249551,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "d2fc54cd695cb4b569e0fc8aaf6b4c6b",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Frost/frost_big_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Frost/frost_big_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Frost/frost_big_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 706315,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "331bb6c1a45c4214918318956ee8caf7",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Frost/frost_chaser_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Frost/frost_chaser_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Frost/frost_chaser_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 222712,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "de500d2d7979c4ffc9f4b2ab9070a289",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Frost/frost_fighter_1_pixel_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Frost/frost_fighter_1_pixel_candidate.png",
    "Assets/Art/Enemies/Staging/Frost/frost_fighter_1_pixel_candidate.png.meta"
   ],
   "bytes": 395427,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "05a97f1a20a6d4cf5b0c6dcdb6762d17",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Frost/frost_fighter_1_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Frost/frost_fighter_1_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Frost/frost_fighter_1_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 377710,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "091966b2bac844f35b454215d0ebe989",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Frost/frost_fighter_2_pixel_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Frost/frost_fighter_2_pixel_candidate.png",
    "Assets/Art/Enemies/Staging/Frost/frost_fighter_2_pixel_candidate.png.meta"
   ],
   "bytes": 354648,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "1bae2c34909124c48a8069d874a71d22",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Frost/frost_fighter_2_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Frost/frost_fighter_2_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Frost/frost_fighter_2_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 356744,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "05b80d18471084ac2b3a0a46394cc822",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Frost/frost_fighter_3_pixel_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Frost/frost_fighter_3_pixel_candidate.png",
    "Assets/Art/Enemies/Staging/Frost/frost_fighter_3_pixel_candidate.png.meta"
   ],
   "bytes": 377887,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "7549288091db548008cb8f4ef38358df",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Frost/frost_fighter_3_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Frost/frost_fighter_3_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Frost/frost_fighter_3_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 286886,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "11fbab80475954732b347069ecf4b091",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Frost/frost_fighter_4_pixel_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Frost/frost_fighter_4_pixel_candidate.png",
    "Assets/Art/Enemies/Staging/Frost/frost_fighter_4_pixel_candidate.png.meta"
   ],
   "bytes": 376114,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "7cb867645093d4639bae02b392fe6718",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Frost/frost_fighter_4_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Frost/frost_fighter_4_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Frost/frost_fighter_4_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 371009,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "bb2427d5ec3a94d2e9a93ae609933cbd",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Frost/frost_rock_shard_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Frost/frost_rock_shard_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Frost/frost_rock_shard_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 317879,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "d43241174df5b4d88803ac2be414e39f",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Shared/fighters_pixel_candidates_contact.png",
   "files": [
    "Assets/Art/Enemies/Staging/Shared/fighters_pixel_candidates_contact.png",
    "Assets/Art/Enemies/Staging/Shared/fighters_pixel_candidates_contact.png.meta"
   ],
   "bytes": 5816052,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "b71057d93ac314bd6b4427fac26b5f0c",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Space/space_alien_arm_idle_v3_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Space/space_alien_arm_idle_v3_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Space/space_alien_arm_idle_v3_strip_candidate.png.meta"
   ],
   "bytes": 192056,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "79cfd852a770b4c308422d3229c02c3b",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Space/space_alien_pixel_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Space/space_alien_pixel_candidate.png",
    "Assets/Art/Enemies/Staging/Space/space_alien_pixel_candidate.png.meta"
   ],
   "bytes": 377468,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "49e0f375e0ce14f31bf3587f78d89d3f",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Space/space_alien_rugged_tall_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Space/space_alien_rugged_tall_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Space/space_alien_rugged_tall_v2_strip_candidate.png.meta"
   ],
   "bytes": 368002,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "36844bdca422d440c9105451658a7230",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Space/space_chaser_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Space/space_chaser_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Space/space_chaser_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 199186,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "9dc260ffa81d54f4c887dde827de91ee",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Space/space_fighter_1_pixel_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Space/space_fighter_1_pixel_candidate.png",
    "Assets/Art/Enemies/Staging/Space/space_fighter_1_pixel_candidate.png.meta"
   ],
   "bytes": 455888,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "ee55c1b701df148eda94ea3560c54204",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Space/space_fighter_1_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Space/space_fighter_1_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Space/space_fighter_1_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 371128,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "838990c25807445c297df54f21d2bb66",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Space/space_fighter_2_pixel_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Space/space_fighter_2_pixel_candidate.png",
    "Assets/Art/Enemies/Staging/Space/space_fighter_2_pixel_candidate.png.meta"
   ],
   "bytes": 538268,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "e0c8516999aab4997b8e1ce159257224",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Space/space_fighter_2_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Space/space_fighter_2_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Space/space_fighter_2_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 512317,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "11da299e6b3804320b7a3a406e8ac1ff",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Space/space_fighter_2_rugged_v3_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Space/space_fighter_2_rugged_v3_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Space/space_fighter_2_rugged_v3_strip_candidate.png.meta"
   ],
   "bytes": 474074,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "8660c9e4999864bafaf796451ef23490",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Space/space_fighter_3_pixel_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Space/space_fighter_3_pixel_candidate.png",
    "Assets/Art/Enemies/Staging/Space/space_fighter_3_pixel_candidate.png.meta"
   ],
   "bytes": 504855,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "b7bc304d1256f4255b443dc5ed93cb2f",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Space/space_fighter_3_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Space/space_fighter_3_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Space/space_fighter_3_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 515083,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "a2e8a01ba0a744f3bbdf796452237748",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Space/space_fighter_4_pixel_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Space/space_fighter_4_pixel_candidate.png",
    "Assets/Art/Enemies/Staging/Space/space_fighter_4_pixel_candidate.png.meta"
   ],
   "bytes": 477897,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "fbe951b6854e143c090ca8eb6da27770",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Space/space_fighter_4_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Space/space_fighter_4_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Space/space_fighter_4_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 596359,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "53ad53c3ca9384b7f8e29a96c3bcb79b",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Space/space_rock_cluster_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Space/space_rock_cluster_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Space/space_rock_cluster_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 357224,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "f02dafd9e182840d5830362a7217e1bb",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Space/space_rock_crater_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Space/space_rock_crater_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Space/space_rock_crater_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 220025,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "d1ee8c43ed314438d9a9a11f5012594d",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Space/space_rock_dark_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Space/space_rock_dark_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Space/space_rock_dark_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 319957,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "945cb7e0e17b344ceb05004f51e07299",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Verdant/verdant_alien_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Verdant/verdant_alien_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Verdant/verdant_alien_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 305700,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "e6c7e39b9d2244f9c8f3583ade261516",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Verdant/verdant_big_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Verdant/verdant_big_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Verdant/verdant_big_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 430749,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "b99cc72a572844fe2a579bbcb8300c76",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Verdant/verdant_chaser_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Verdant/verdant_chaser_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Verdant/verdant_chaser_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 199617,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "de76fbd977d3d4f4c9b61d3d044c9206",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_1_pixel_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_1_pixel_candidate.png",
    "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_1_pixel_candidate.png.meta"
   ],
   "bytes": 348642,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "a530de2d751474c979bbcf7771ce14e7",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_1_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_1_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_1_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 342597,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "c437cac03a216485a86a85c1232afb32",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_2_pixel_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_2_pixel_candidate.png",
    "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_2_pixel_candidate.png.meta"
   ],
   "bytes": 350035,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "805911425d7d2403aad160778b88f1c5",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_2_rugged_v3_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_2_rugged_v3_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_2_rugged_v3_strip_candidate.png.meta"
   ],
   "bytes": 265613,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "912cb112aa0e448658c5cf2097d9dc49",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_3_pixel_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_3_pixel_candidate.png",
    "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_3_pixel_candidate.png.meta"
   ],
   "bytes": 345776,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "6ff49b224951f4c5dbfec7971bc607ec",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_3_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_3_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_3_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 251255,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "a38a1197883994769bdf079b33eb8613",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_4_pixel_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_4_pixel_candidate.png",
    "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_4_pixel_candidate.png.meta"
   ],
   "bytes": 341457,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "84874b489fdee48738a11f7933176175",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_4_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_4_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Verdant/verdant_fighter_4_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 344901,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "f0c057486e266434283093756faccd04",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Verdant/verdant_rock_spore_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Verdant/verdant_rock_spore_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Verdant/verdant_rock_spore_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 416407,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "8ef4b7ff9e4c64e74bb6e19a4376f4ea",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Verdant/verdant_rock_vine_rugged_v2_strip_candidate.png",
   "files": [
    "Assets/Art/Enemies/Staging/Verdant/verdant_rock_vine_rugged_v2_strip_candidate.png",
    "Assets/Art/Enemies/Staging/Verdant/verdant_rock_vine_rugged_v2_strip_candidate.png.meta"
   ],
   "bytes": 287184,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "5eb10f9ddc3054baa9f6a799d5fa3dab",
   "guid_refs": []
  },
  {
   "category": "enemy-staging",
   "path": "Assets/Art/Enemies/Staging/Shared.meta",
   "files": [
    "Assets/Art/Enemies/Staging/Shared.meta"
   ],
   "bytes": null,
   "shipped": false,
   "packed_bytes": 0,
   "note": "folder meta of the emptied Shared folder"
  },
  {
   "category": "backdrop-staging",
   "path": "Assets/Art/Worlds/Space/staging/anim_pixel_v1.json",
   "files": [
    "Assets/Art/Worlds/Space/staging/anim_pixel_v1.json",
    "Assets/Art/Worlds/Space/staging/anim_pixel_v1.json.meta"
   ],
   "bytes": 1234,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "4ff3e525906414551b6ac7818006d2b6",
   "guid_refs": []
  },
  {
   "category": "backdrop-staging",
   "path": "Assets/Art/Worlds/Space/staging/anim_pixel_v1.png",
   "files": [
    "Assets/Art/Worlds/Space/staging/anim_pixel_v1.png",
    "Assets/Art/Worlds/Space/staging/anim_pixel_v1.png.meta"
   ],
   "bytes": 1758302,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "ae62007cf24cd434e8989430c57aabbe",
   "guid_refs": []
  },
  {
   "category": "backdrop-staging",
   "path": "Assets/Art/Worlds/Space/staging/fx_pixel_v1.json",
   "files": [
    "Assets/Art/Worlds/Space/staging/fx_pixel_v1.json",
    "Assets/Art/Worlds/Space/staging/fx_pixel_v1.json.meta"
   ],
   "bytes": 1465,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "1bc6a4194678d475780c32b67baab6f9",
   "guid_refs": []
  },
  {
   "category": "backdrop-staging",
   "path": "Assets/Art/Worlds/Space/staging/fx_pixel_v1.png",
   "files": [
    "Assets/Art/Worlds/Space/staging/fx_pixel_v1.png",
    "Assets/Art/Worlds/Space/staging/fx_pixel_v1.png.meta"
   ],
   "bytes": 1573927,
   "shipped": false,
   "packed_bytes": 0,
   "guid": "a313651644da0491fbb0d744b501eed2",
   "guid_refs": []
  },
  {
   "category": "backdrop-staging",
   "path": "Assets/Art/Worlds/Space/staging.meta",
   "files": [
    "Assets/Art/Worlds/Space/staging.meta"
   ],
   "bytes": null,
   "shipped": false,
   "packed_bytes": 0,
   "note": "folder meta of the removed staging folder"
  },
  {
   "category": "ui",
   "path": "Assets/Art/Resources/Tutorial/tut_card.png",
   "files": [
    "Assets/Art/Resources/Tutorial/tut_card.png",
    "Assets/Art/Resources/Tutorial/tut_card.png.meta"
   ],
   "bytes": 3922,
   "shipped": true,
   "packed_bytes": 37436,
   "guid": "4e69f14954d24fd6bfacaf89387df547",
   "guid_refs": []
  }
 ]
}
```
