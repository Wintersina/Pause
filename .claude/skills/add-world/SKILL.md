---
name: add-world
description: Add a whole new planet world to Pause (the next world after Ember, e.g. World 5 "Tide" the ocean planet, World 6 "Storm" the gas giant) as an ordered, executable playbook for the coordinator -- brief, planet approach + planetfall + lift-off, how the previous last world stops looping straight to Space, atmosphere-level backdrops (4 variants, landmarks, pipes, loops, launch sites), the 11-enemy roster + behaviours + death strips + mines, THEMED ATTACKS (every enemy/elite/boss/mine attacks in its world's material: ice, fire, vines, water, neon; pink-cue rule), boss (atlas, shots, card, damage, death, attacks), rails, elites, authored death sounds, codex/menus/dev/leaderboards, tests and gates. Use when the user says "start World N", "add the ocean / storm world", "what does a world need", or when auditing that an existing world missed a piece. Delegates art to Codex (`codex exec`) and code/tests to Sonnet subagents.
---

# Add a world

A world in Pause is **a re-theme of `gameS1`**, not a scene: a `WorldTheme`
entry, a backdrop spec + director, an enemy roster, a boss, rails, a
planetfall/lift-off pair and a pile of art. Verdant (2) and Ember (3) were
added one at a time on top of Frost (1) by a coordinator (Claude) who gave art
to Codex and code/tests to subagents. This skill is that experience as a
playbook. Companion files (read them when the phase says so):

| File | What it is |
| --- | --- |
| `checklist.md` | The file-by-file touch list, the per-phase definition of done, and the world release gate -- tick it as you go |
| `art-briefs.md` | Reusable Codex prompt templates (planet, backdrop A/B/C, enemy strips, deaths, elites, boss, damage, rails, sounds, fix passes) |
| `world-5-6-briefs.md` | Starter briefs for World 5 *Tide* (ocean) and World 6 *Storm* (gas giant) -- **proposals** for the user to refine |
| `docs/world-attacks-design.md`, `world-attacks-art.md`, `world-attacks-codex-prompts.md`, `world-attacks-implementation-plan.md`, `world-attacks-audit.md` (repo `docs/`) | **THEMED ATTACKS**: the per-world attack for every enemy/elite/boss/mine, the pink-cue and fairness contracts, the attack art list + Codex prompts, the build plan. Read before Phase 12b |
| `scripts/` | `world_audit.sh` (what a world touches), `run_codex.sh`, `contact_sheet.py`, and pre-flight verifiers that reproduce the Unity tests' numbers on Codex output: `verify_enemy_strip.py`, `verify_backdrop_tiles.py`, `verify_planetfall_art.py`, `verify_boss_art.py`, `verify_wavs.py` |

Path note: `scripts/<tool>` in this skill means `.claude/skills/add-world/scripts/<tool>`,
except **`scripts/unity-batch.sh`**, which is the repo's own machine-wide Unity wrapper
(`/Users/sina/Developer/Pause/scripts/unity-batch.sh`, run from any worktree root).

Unity: `/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity`.
Project root is the inner `Pause/`; code is under `Pause/Assets/Scripts/`, tests
under `Pause/Assets/Editor/Tests/`. `<World>` = display name ("Tide"), `<w>` =
lower-case key ("tide"), `N` = world index (Space 0, Frost 1, Verdant 2, Ember 3,
**Tide 4, Storm 5**).

## 0. How the coordinator works (read first)

- **Roles.** The coordinator (you) writes briefs, launches jobs, audits every
  output (numbers *and* by opening the images), integrates, and talks to the
  user. Art = Codex, headless: `scripts/run_codex.sh` (wraps `codex exec -C
  <worktree> -s workspace-write -c model_reasoning_effort='"high"' -i <ref
  images> -o <last.txt> - < prompt.md > log 2>&1`). Code/tests = Claude
  subagents, spawned with `model: sonnet` (never opus/fable), told to use the
  `add-elite-ship` skill for elites. Never do feature work in the main chat.
- **Slots.** At most **2 Codex jobs** and **3 Claude agents** at a time (the
  user's current limits). Codex shares the user's ChatGPT usage limit: a "limit
  reached" line in a log means *wait for the reset*, not retry. Codex cannot be
  messaged mid-run: restructure and relaunch; but **look at the output folder
  before calling a run stuck** (one "stuck" run had already written its files).
- **Codex CLI preflight (before a world starts).** Installed: `codex-cli 0.161.0` at
  `~/.local/bin/codex`, logged in via ChatGPT. Run `codex --version` and `codex
  login status` first. On a usage-limit hit Codex **stops mid-run**; the job log
  (`codex_<job>.log`) says so: wait for the reset, then relaunch the *same prompt* in
  the *same worktree* (it keeps the files already written). `-i` image refs are
  resolved **relative to the shell's cwd**, not the `-C` worktree: pass absolute
  paths. Launching: `run_codex.sh` backgrounds with `nohup ... &`; the tool's
  "completed" notification for that launcher is **not** the Codex run finishing.
  Check `pgrep -f 'codex exec'` and the `-o ..._last.txt` file (written only at the end).
- **One worktree + one branch per job.** Codex: `.claude/worktrees/codex-<job>`,
  branch `art/<job>`. Agents: `.claude/worktrees/<name>`, `feature/<name>`.
  **Never edit or commit in the main checkout** `/Users/sina/Developer/Pause`
  (Codex/the user work there) and never touch other sessions' worktrees.
- **Integration.** Everything for a world lands on one long-lived
  `integrate/world-<w>` branch (merge art branches in *staged*, `~` folders; see
  rule 4 below), run the full suite there, and **fast-forward master only on the
  user's explicit go-ahead** (`master-merge-needs-go-ahead`). Art-only branches
  that add no reachable world may be merged earlier if the user says so.
- **User touchpoints.** (1) Phase 0: the user approves the *direction* (palette,
  hook, boss concept, names). (2) After every Codex output: `open` the PNGs / a
  contact sheet and give an **honest critique** (what is strong, what is weak,
  the measured numbers, what you will fix). Art needs **no approval gate** after
  Phase 0 ("don't wait for my approval on art, I can correct it later") -- keep
  moving, but the user must always have seen it. (3) The final release gate.
  **No phone install and no APK** unless the user says so (the dev APK is
  `BuildScript.BuildAndroidDev`; adb serial is in `pause-build-run` memory).
- **The user authors the world's backdrop/ambient/music sounds himself.** Do
  not generate those. `WorldTheme.musicResource` stays `""` (the scene's track)
  until he drops a track in; then add its `WorldMusic.LoopOutSeconds` entry.
  Enemy/elite *death cues and screams* are ours (Phase 8) and are **mandatory**:
  since 2026-10-09 there is no synthesized fallback, a roster/elite key with no
  WAVs is silent and `EnemyDeathAudioTest` fails.

## 1. Anatomy of a world

### 1a. Runtime pieces (code)

| Piece | File(s) (`Pause/Assets/Scripts/...`) | What it does |
| --- | --- | --- |
| `WorldTheme`, `WorldManager.Worlds[]` | `Worlds/WorldTheme.cs`, `Worlds/WorldManager.cs` | The world list **is the order**. Index = world number; `speedRampPerSecond` / `enemyRampScale` set pace; `portalColor`, `musicResource`. `PortalDestination` = next world, or `RunLoop.StartWorld` after the last. `Advance`, `OpenGateway`, `StartLoop`, `EndLevel` live here |
| Planetfall | `Worlds/Planetfall/Planetfall.cs`, `PlanetfallDef.cs` (`PlanetfallCatalog.All`), `PlanetfallArt.cs`, `PlanetfallTimeline.cs` | The arrival: the planet drifts into the *Space* backdrop, the ship touches its zone, dives through two cloud decks, `WorldManager.Advance(false)` switches the world under the clouds, breakthrough, control returns. `PlanetfallCatalog.For(from,to,loop)` -> only `to == from+1`, never a loop |
| Lift-off | `Worlds/Planetfall/Liftoff.cs`, `LiftoffDef.cs` (`LiftoffCatalog.Defs`), `LiftoffTimeline.cs` | The departure: after the boss the ship climbs through the *departed* world's clouds (its own planetfall art, played backwards), the backdrop becomes `interludeWorld` (Space) unseen, a calm ~9.6 s interlude, then `Gateway()` -> `OpenGateway()` (next planet's planetfall) or, for `autoLoop`, `StartLoop()` |
| Transition guard | `Worlds/WorldTransition.cs` | `InProgress` is *derived* from the live Planetfall/Liftoff/portal; `ShipPowerController` freezes the weapon charge on it. Reusing Planetfall/Liftoff needs no registration; a brand-new kind of transition must read here |
| Loop rules | `Worlds/LoopRules.cs` (`RunLoop`, `LoopDifficulty`) | Everything that scales per loop; independent of world count |
| Painter / rails | `Worlds/WorldPainter.cs` | `RailTextureName`, `RailBounds` (measured inner/outer of the rail art), `EdgeFor` (dark-edge shader), `VisibleRailEdges` (hard-coded world list), rail shader `WorldRailRepeat` |
| Backdrop | `Worlds/Backdrop/BackdropCatalog.cs` (`Spec`, `Layer`, `Role`, `CloudCover`), `BackdropSet.cs` (`CreateDirector` switch), `<World>Backdrop.cs` (`<World>Tuning`, `<World>Director : PlanetDirector`), `<World>Ambient.cs` (`AmbientTable`, `points.json`), `GroundPlacement.cs` (`GroundClass`, `GroundMask`, `GroundPlanner`, `PieceRule`), `LandingSites.cs` (`LandingKind`), `BackdropGrade.cs`, `FrostAmbient.cs` (`BackdropVariants`, shared ambient types), `MenuBackdrop.cs` | Four random ground sets per landing (`Spec.variantSets`, `variantBrightness`, `variantNight`, `brightArt`), pieces pinned to the mid tile (`Layer.PinnedTo`), cluster grammar, measured emitters, cloud ceiling (`cloudDensity/ceilingHold/ceilingClearSeconds`), elite launch sites |
| Roster | `Gameplay/Enemies/EnemyRoster.cs` (`WorldKeys`, `Build()`), `EnemyBehaviours.cs` (`B("<key>", ...)`), `EnemyPalette.cs` (`ThemeFor`), `EnemyArt.cs`/`EnemyFlipbook.cs` (7-cell idle), `EnemyDeathFlipbook.cs` (3-cell death), `RailMineArt.cs` (the mines are a neon atlas, not strips), `EnemyDensity.cs` (per-world pilot load arrays), `HazardSize.cs`, `HostileShotPalette.cs` | The 11 standard enemies; spawners (`Spawning/enmiesOnBoard.cs`, `SpawnLane.cs`) pick **only from the current world** (`EnemyRoster.CurrentWorld`) |
| Explosions / audio | `Gameplay/Weapons/TargetExplosion.cs` (`Kind`, `KindForWorld`), `Gameplay/EnemyDeathAudio.cs` | A world material per world; authored WAVs `Resources/Audio/EnemyDeath/<key>_<n>.wav` + `_scream_<n>` |
| Elites | `Gameplay/Elites/*` (see the `add-elite-ship` skill), `Art/Resources/Elites/Defs/<key>.json` | 5-6 per world: brain + attack + landing sites |
| Boss | `Bosses/BossCatalog.cs` (`BossDef`, `BossAttack`), `BossArt.cs`, `BossEmitterTable.cs` (**generated**), `BossHearts.cs` (`Body` ellipse), `BossWarning.cs` (`Accents`), `BossAttackFx.cs` (a row per boss), `BossEncounter.cs` | Index-aligned with `WorldManager.Worlds` (`BossCatalog.ForWorld(world)`); 5 hearts -> 4 damage stages; deathKey/damageKey |
| Meta | `Codex/CodexCatalogue.cs` (`WorldIds`), `Codex/CodexPanel.cs` (`MaxSections`), `Core/DeveloperUnlocks.cs` (F-keys), `Menu/DeveloperOptions.cs` (picker, automatic), `Core/Leaderboards/LeaderboardBoards.cs` (`FormatWorld`, automatic), `Core/Scoring/ScoreRules.cs` (`WorldClearedPoints`, automatic), `Worlds/Backdrop/MenuBackdrop.cs` (pool of unlocked worlds, automatic) | |

### 1b. Art tree

| Path (under `Pause/Assets/Art/`) | Contents |
| --- | --- |
| `Backgrounds/Resources/Worlds/<World>/Planetfall/` | 7 files: `<w>_planet.png` 1024^2, `_planet_limb.png` 2048x1024, `_cloud_deck.png` + `_cloud_deck_dark.png` 2048x1024 (tile both axes), `_entry_fx.png` 3072x1024 (6 cells 512x1024), `_breakthrough.png` 5120x1024 (5 cells), `_entry_streaks.png` 1024x2048 (tile vertically) |
| `Backgrounds/Resources/Worlds/<World>/Backdrop3/` | `v1..v4/{sky,far,mid,flow}.png` 512x1024 + `mask.txt`; atlases `landmarks, pipes, fires/lavafire-style, weather, sites, smoke, eruption-style, leaks, lights` (1024^2, 256 cells, each with `.json`); `points.json` |
| `Resources/Worlds/<World>/rail_<name>_wide_v1.png` | 725x2170 side rail |
| `Resources/Enemies/<key>.png` | 11 idle strips: 7 square cells (192 px; the big 256). The mine is a row of `Enemies/Mines/rail_mines_neon.png` |
| `Resources/Enemies/Death/<key>.png` | 12 death strips: 3 cells (576x192; big 768x256), the mine included |
| `Resources/Elites/<World>/<key>.png` (+ `_parked/_liftoff/_death`) | 5-6 elite strips; source strips in `Enemies/Elite/<World>/` |
| `Resources/Bosses/<Key>.png` 1920x1536, `_shots` 1024x128, `_card` 1024x288, `_damage` 768x1536, `_damage_fx` 2304x768, `_death` 2304x384 | Boss |
| `Resources/Weapons/Explosions.png` | one row per `TargetExplosion.Kind` |
| `Audio/Resources/Audio/EnemyDeath/` | death cues + screams; scripts in `Audio/EnemyDeathSrc~/` |
| `Worlds/<World>/{descent~,backdrop_v3~}/`, `Enemies/src~/<w>_death/`, `Bosses/<Key>Damage~/` | Codex's **Unity-ignored** (`~`) working/staging folders: sources, prompts, manifests, previews |

### 1c. Where the world order lives (everything that must move together)

`WorldManager.Worlds[]` order; `EnemyRoster.WorldKeys`; `BossCatalog.All` order;
`BossEmitterTable.Worlds`; `BossAttackFx` rows; `RailMineArt` rows;
`CodexCatalogue.WorldIds`; `PlanetfallCatalog.All`; `LiftoffCatalog.Defs`;
`EnemyDensity.PilotLoadAt*Speed`; `BossWarning.Accents`; `TargetExplosion.Kind`
rows. Run `scripts/world_audit.sh Ember <World>` after the scaffold to list
every one still missing (section 2 of its output is the hard-coded-count list).

## 2. Constraints (the rules a world must keep)

1. **Enemies never use the player's red** (`#D8232C/#86121F/#FF5B45`; hue 345..15
   at saturation > .5). Tests: `EnemyRosterTest.PaletteCompliance` (< 2% of
   texels), `palette.env` checked against `EnemyPalette`.
2. **Hostile shots are one hue family in every world**: pink 312..326 degrees
   (`HostileShotPalette`), whatever the world's palette. Pickups own the other
   hues: blue shield atom cyan 178, green repair 82, violet capacitor 259, star
   dust amber 37, red pause atom ~7. A world accent that sits on one of those
   (or on pink) hurts readability -- decide this in Phase 0 (see "Things easy to
   miss" #1).
3. Each world has its **own palette family on a dark base** (Space indigo/magenta,
   Frost white-cyan, Verdant lime/olive, Ember orange/black). A new world must
   not collide with those *or with the pickup hues above*.
4. **Staging**: Codex writes into `~` folders (`Art/Worlds/<World>/<job>~/`,
   `Art/Enemies/src~/...`); Unity ignores them, so `UnusedAssetGuardTest` sees no
   orphans. Anything under a `Resources/` folder is a root (ships). **Delete the
   old art in the same change that wires the new art, never before**, and never
   overwrite a live strip until its replacement is wired and verified.
5. **Never edit Codex's art by hand** (sources in `Art/Enemies/Elite/**`, `~`
   folders); ask for a redo. Exception: fix passes that Codex does on named
   files only (originals saved first).
6. Don't touch the PAUSE logo, HapticGate splash, or other worlds' art/tests
   except to extend lists.
7. **`.meta` churn**: commit `.meta` only for **new** files. Unity rewrites
   others (boss cards, `Backdrop3` importers, `anim_hires.png.meta`): don't
   commit those. The main checkout stays clean.
8. Neon pixel art, 1 px dark outline, nearest-neighbour only, no blur, no
   photoreal; a world's art uses *its* ramps plus ink (`docs/art-style.md`
   sections 1-5).
9. **No space imagery** in planet backdrops: no planets/stations/comets/stars.
   The ship is at *atmosphere level* looking down.
10. **THEMED ATTACKS (user requirement, applies to every world we create).** The user's words:
    "the attack patterns for each world should match the enemies there, for example, frost world there will be
    ice attacks and blasts etc, for fire world fire attacks, flame throwers, fire lasers etc, for water world, surf
    wave attacks, water guns, thunder etc... for space, laser neon attacks etc. does that make sense, not all enemies
    should be doing same attacks as first world; if you need animations have codex make it, else do it yourself; and
    for forest world (he wrote 'frost' by mistake: Verdant) have vine attacks, leaf attacks, tree trunks etc."
    So a world is **not done** while its shooters fire Space's pink darts. Every enemy, elite, boss and mine of the
    new world gets an attack whose *shape, motion, trail, impact and sound* match its material and name. The
    **pink-cue rule stays**: every hostile thing keeps a pink-leaning core and/or keyline (`HostileShotPalette` 312-326,
    pickups own 178/82/259/37, never the player's red, bold two-ring keyline on bright worlds), so the world supplies
    the material and the pink says "this hurts". Fairness: an instant-hit hazard (cone, column, band, ring, strike)
    needs a >= .7 s tell, a drawn footprint >= .4 s before it is live and a >= 1.4 u safe corridor; budgets are held
    by a dodge-bot hit-rate test. Details, catalogue and contracts: repo `docs/world-attacks-design.md`.

## 3. Phase map

Owners: **C** = Codex job, **A** = Claude subagent, **Y** = you (coordinator),
**U** = user. Hours are wall-clock from the Frost/Verdant/Ember runs with the
slots above; Codex limits can add half a day.

| # | Phase | Owner | Needs | Can run alongside | Est. |
| --- | --- | --- | --- | --- | --- |
| 0 | World brief | Y + U | -- | -- | 0.5 h + user |
| 1 | Planet / planetfall / lift-off art | C | 0 | 2, 9 | 1.5-3 h |
| 2a | Backdrop run A: 4 ground sets (+ redo pass) | C | 0 | 1, 3 | 2-4 h (+1.5) |
| 2b | Backdrop run B: landmarks, pipes, fires, weather, sites | C | 2a | 3 | 2-3 h |
| 2c | Backdrop run C: animated loops | C | 2b | 4 | 2-3 h |
| 3 | Enemy idle strips x11 (+ mine row) (+ fix pass) | C | 0 | 1, 2a | 2-4 h (+1.5) |
| 4 | Enemy death strips x12 | C | 3 | 2c | 1.5-2.5 h |
| 5 | Rails | C | 2a | any | 0.5-1 h |
| 6 | Boss: atlas, shots, card; then damage + fx + death | C | 0 | 7 | 2-4 h; 2-3 h |
| 7 | Elites x5-6 | C | 0, 2b (sites) | 6 | 2-4 h |
| 8 | Death sounds + screams | C | 3, 7 | any | 1-2 h (+1 revision) |
| 9 | Scaffold: the world exists (code) | A | 0 | 1-8 | 3-5 h |
| 10 | Planetfall / lift-off / loop wiring | A | 1, 9 | 11 | 3-5 h |
| 11 | Backdrop wiring (director, ground masks, emitters, sites) | A | 2c, 9 | 12 | 6-10 h |
| 12 | Roster, behaviours, mines, explosion kind, density | A | 3, 4, 8, 9 | 11 | 4-6 h |
| 12b | **THEMED ATTACKS**: every enemy / elite / boss / mine gets an attack that matches the world's theme (skins, new hazards, boss signature attacks, attack sounds) | C (J12 art) + A | 3, 6, 7, 12 and the roster/boss art drafted | 13 | 6-8 h + art |
| 13 | Boss wiring (catalog, emitters, hearts, fx row, damage/death) | A | 6, 9 | 11 | 3-5 h |
| 14 | Elite wiring (`add-elite-ship`, landing kinds) | A | 7, 11 | 13 | 1-2 h each |
| 15 | Audio hookup + tests | A | 8, 12, 14 | -- | 1-2 h |
| 16 | Meta: codex, menus, dev, leaderboards, docs | A | 9-14 | -- | 2-3 h |
| 17 | Integration, soak, readability sweep, release gate | A + Y + U | all | -- | 4-8 h |

**Codex job order** (two slots): J1 planet + J2 backdrop A -> J3 backdrop B + J4
enemies -> J5 backdrop C + J6 boss -> J7 deaths + J8 elites -> J9 boss damage +
J10 sounds -> rails (J11, short, any gap) -> **J12 attack art (shots + fx first, then strike / jet / wave / lash / beam)** -> fix passes. Start Phase 9 on day one.

Work on **one world at a time**. World 6 does not start until World 5 is at the
release gate (the loop rewiring in Phase 10 is done twice; doing it for a
half-built world 5 would strand it).

## 4. The phases

Every phase lists: **Do** (numbered), **Accept** (numbers a test or script
checks), **Show** (what the user sees). Prompts live in `art-briefs.md`; the
file-level touch list is in `checklist.md`.

### Phase 0 -- World brief (you + the user)

Write the brief once (start from `world-5-6-briefs.md`), keep it in the scratchpad
and paste its slots into every job prompt (`art-briefs.md` "Slots"); add the world to
`docs/art-production-queue.md` as the running queue. Get a yes on the **direction** only:

1. **Name, order, setting.** Index N, the planet's look from orbit, what the ship
   flies over at atmosphere level (the "ground"), time of day (4 variants: say
   which one is the night side -- `variantNight`).
2. **Palette.** A dark base + one electric hue + accents, with an explicit
   collision check against Space/Frost/Verdant/Ember, the pickup hues (178, 82,
   259, 37, red) and hostile pink (312-326). Name the world's **enemy light**
   (`EnemyPalette.Theme.light`) and **boss flash/heart/warning colours**.
3. **Hook** (optional mechanic): decide whether it is art-only or needs code. A
   mechanic is a new system: scope it, test it (`HazardSizeTest`/pace/fairness),
   and keep it out of the critical path (it can ship in a later pass).
4. **Roster of 12** (11 idle strips + the mine): alien, big, chaser, fighter_1..4, mine, 4 rocks (one
   floating). For each: name, silhouette, movement pattern, whether it shoots,
   the counter (see `docs/enemy-behaviours.md`).
5. **5-6 elites**: name, role (brain), attack idea, launch-site kind.
6. **Boss**: concept, 3 attack parts, silhouette within the 384 cell.
7. **Difficulty tier**: `speedRampPerSecond`, `enemyRampScale`, pilot load
   (Phase 9).
8. **Sound identity** for deaths/screams (materials, which units scream).
9. **Themed attacks** (user requirement, see constraint 10): for each of the 12 units, each elite, the boss and the mine, one line:
   the attack's name, the primitive it reuses (`Shot`, `Lob`, `Laser`, `Lunge`, `Ring`, `Sling`, `Fuse`...) or the new one it needs (cone/jet, band/wave, ring/blast, lane strike, lash), its tell, its
   counter, and its pink cue. A world whose attack column repeats another world's is not accepted.

**Accept:** the user answered the open questions (or said "your call").
**Show:** the one-page brief + 2-3 reference sentences, not art.

### Phase 1 -- Planet, planetfall and lift-off art (Codex J1)

The same seven files serve **two** sequences: this world's *approach + dive*
(arriving from the previous world's lift-off interlude, over Space's backdrop)
and this world's own *lift-off* later. The planet is seen in space first, then
the horizon (limb), then the cloud decks. Prompt: `art-briefs.md` "Planet".

**Do**

1. Launch J1 (worktree `codex-<w>-planet`, output `Art/Worlds/<World>/descent~/`,
   sources in `descent~/src~/`). Pass Frost's, Verdant's and Ember's finished
   files as `-i` references (`Art/Backgrounds/Resources/Worlds/Ember/Planetfall/*`).
   Ask for **2048 or 4096 masters** kept in `descent~/` (not shipped); ship the
   contract sizes below. The planet must have a **silhouette distinct from the
   others** (Frost ring city, Verdant vine ring, Ember brass radiator ring).
2. Verify: `python3 .claude/skills/add-world/scripts/verify_planetfall_art.py
   <descent~ or Planetfall dir> <w>` -- sizes, tile seams, the entry-fx opening
   (~146x165 at (256,338) per cell, centre x wandering a few px), margins, and it
   prints the numbers `PlanetfallDef` needs.
3. Entry plasma must be a **wide fierce shroud** like Frost's, not a thin S-ribbon
   (Verdant's and Ember's first passes were the weakest piece: ask for a redo
   pass up front if it comes back thin).
4. Cloud decks continue into the world's backdrop: their colour and brightness
   should match the backdrop's `ceiling` weather atlas (Phase 2b), and the
   cloud-break hand-off must not flash a different colour (Verdant's lime flash
   -> ceiling was a known weak point).
5. Open `preview.png`/`preview.gif`; critique; commit on `art/<job>`.

**Accept:** verifier PASS; seven files at the exact sizes; planet disc >= 18 px
inside the canvas; hues >= 22 degrees from the player's red.
**Show:** contact sheet of the seven files + the entry-fx loop gif.

### Phase 2 -- Backdrop art (Codex J2 -> J3 -> J5)

The ship flies at **atmosphere level over the surface**, under a cloud ceiling.
Three runs, in order, then an optional contrast redo.

**2a. Ground sets (J2)** -- `Art/Worlds/<World>/backdrop_v3~/v1..v4/{sky,far,mid,flow}.png`

1. Four sets that read as **different places at a glance**, not recolours; one is
   the **night side** (declare it: `variantNight`). Seamless 512x1024 RGBA on both
   axes. Roles: `sky` = deepest hazy ground plane (lowest contrast), `far`,
   `mid` = most detailed (the layer landmarks are pinned to), `flow` = sparse
   transparent scrolling marks (.5%-25% opaque).
2. **Brightness**: p90 of HSV value over opaque pixels **.40-.50 day, .32-.42
   night** (`WorldBackdropTest` caps; `Spec.BrightLift` 1.2 decides the bold
   outlines). The target look is **Ember's mid layer**: dark base, hot accents,
   high contrast, lively mid-tones -- *not* flat tan haze (Ember's first sky/far
   needed a full redo for that). A bright-painted world (`brightArt`, like
   Verdant) switches on the bold shot/heart outlines everywhere.
3. No baked smoke/animation in tiles; no space imagery; hue rules.
4. Verify: `verify_backdrop_tiles.py <Backdrop3 dir>` (p90, wrap, opacity, flow
   fraction, forbidden hues, masks). Run **before** accepting.
5. Critique every layer; if sky/far are flat while mid is strong, order the
   **redo pass** now (see `art-briefs.md` "Backdrop redo").

**2b. Pieces (J3)** -- `landmarks`, `pipes`, `fires`-style ground features,
`weather`, `sites` atlases + JSON (1024^2, 256 cells, y from the bottom).

1. Landmarks 16 pieces (large structures, p90 <= .55, each on its own bit of
   terrain *with a feathered base*, not a sticker), pipes 16 (end points listed in
   the manifest), fire/feature grounds 16, weather 16 (cloud banks for the
   ceiling that continues the planetfall deck, wisps, mist, gusts, palls,
   particles; low alpha), sites 12 (closed/open pairs for elite launch sites).
2. The user wants **lots of pipes, smoke and fire/life** on every planet world.
3. >= 14 px clear margin round each piece; verify with `verify_backdrop_tiles.py`
   (atlas part).

**2c. Loops (J5)** -- `smoke`, `<fire>`, `<eruption>`, `leaks`, `lights` atlases.

1. Every loop **seamless**, frames genuinely different, ground-attached loops
   anchored **bottom-centre at (128, ~236)** of the 256 cell (the plume base must
   sit exactly on its anchor: Ember's smoke was off by up to 44 px and had to be
   realigned per frame).
2. **Blinking lights**: measured brightest/darkest frame ratio >= 4.0 (window
   flicker >= 2.2, strobe >= 4); verify with alpha-weighted brightness sums.
3. Particles (embers/spores/rain) must be **clearly visible** (2-4 px specks with
   trails); Verdant's and Ember's first embers/fireflies were too faint.
4. Smoke/steam semi-transparent (<= ~70% alpha), never hiding enemies/bullets.

**2d. Install-time measurements (Phase 11, listed here so the art is made for it)**

- `ground_masks.py` (world-specific copy of Ember's): classifies each variant's
  `mid.png` into a 32x64 grid of `W/B/O/C` -> `v<N>/mask.txt`. For an ocean
  world `W` is most of the tile; pick `PieceRule`s accordingly.
- `measure_points.py`: measures on the pixels where smoke belongs (stack mouths,
  crater/vent centres, pipe flange faces, mast lamp knobs) -> `points.json`. The
  manifest's emitter points are *nominal*: never use them.

**Accept (all of 2):** the verifier passes for tiles and atlases; manifests exist
with measured p90; texture budget plausible (~5.5 MB ASTC for 4 sets + atlases;
the world gets 7 MB in `WorldBackdropTest`).
**Show:** `preview_variants.png` (sky/far/mid/flow per variant + a 1080x2400
composite) and, once wired (Phase 11), **real in-game frames** at 2 s / 10 s /
40 s (a collage mock is not evidence: the user once rejected exactly that).

### Phase 3 -- Enemy idle strips (Codex J4)

11 strips: `<w>_alien`, `_big`, `_chaser`, `_fighter_1..4`, four rocks
(`_rock_<a..d>`, one **floating**: a chunk of the world's ground that sways
upright), and the mine (a row of the neon mines atlas, see Phase 12).

**Contract per strip:** 7 square cells in one row, cell side = height (**192 px;
the big 256** -> 1344x192 / 1792x256); cells 0-3 idle (one fixed anchor and scale:
centroid within **3 px**, area within **+-4%** of cell 0; the hull stays put but
the idle must be **visibly alive**: >= 3% of the body's pixels change between
consecutive idle cells at 12 fps -- turbines/claws/core/visor/limbs bobbing 2-3 px,
not hull drift and not a lone glint), 4-5 tell, 6 hit flash (the same hull, white-hot accent; **not** a
blown-out recolour). >= 6 px clear margin in every cell. 1 px dark outline **and
a light rim** so it stands out of the world's backdrop (`ReadabilitySweep`
stand-out >= 10% of the footprint, aliens >= 15%).

**Detail floor (Unity-enforced; do NOT posterise):** key pose (cell 0) has >= 300
distinct colours (aim for 1500+) and >= 3000 colour boundaries (big >= 6000);
no ruler-straight vertical cut >= 29 px (0.15 x height); nothing on a cell's
outer columns. Verdant's first idle strips were quantised to ~22 colours and
were *held back from master* for failing this.

**Do**

1. Pass Ember/Frost strips and the finished backdrop tiles as references. Give
   each enemy its design brief from Phase 0 (`art-briefs.md` "Enemy strips").
2. Verify every strip: `verify_enemy_strip.py <strips...>`; also run
   `Pause/Assets/Art/Enemies/src~/audit_cells.py <strip>` (components,
   straddles, anchor/scale).
3. Render `contact_sheet.py` with `--backdrop <v1..v4/mid.png>` to judge
   readability on every variant; critique.
4. Order a **fix pass** for the usual defects (below) with originals saved to
   `src~/<w>_fixes/original/` and every untouched cell byte-identical.

**Idle must not be mute.** The user called the Steel Hound's animation "too mute":
its strip barely changes (0.3-0.4% of pixels between cells, measured by
`verify_enemy_strip.py`) *and* the game special-cased it to a 2-cell loop
(`EnemyRoster.FlipbookIdleTicks`: `space_chaser` -> `{6, 6}`, used by
`EnemyFlipbook` and `CodexAnimations`), which hid the animation altogether. New
worlds must **not** special-case any chaser (or other enemy) in `IdleTicks` /
`FlipbookIdleTicks`: every strip plays its 4 idle cells at the role's ticks.

**Quality traps (all seen):** near-static idle loops (< 3% change); posterising to ~22 colours; edges cut by cell
borders; identical idle cells (a "rock" that does not move); drift between idle
cells; a hit cell blown out to white or recoloured off-palette; stray colours
from another world; margins under 6 px; bodies too dark for the backdrop; a
thick 6-8 px black halo instead of the 1 px outline; rocks that read as
rectangles.

**Accept:** every strip PASS in `verify_enemy_strip.py` (including the idle-motion rows); no off-palette reds.
**Show:** contact sheet 2x on `#0b0b1a` and on a mid tile.

### Phase 4 -- Death strips (Codex J7)

`Resources/Enemies/Death/<key>.png` for **all 12 including the mine and the big**:
3 cells (576x192; big 768x256), each cell drawn at **exactly the idle cell's scale
and anchor** (cell 0 centroid within 3 px of idle cell 3). Timing is fixed in
`EnemyDeathFlipbook`: 0.08 s flash / 0.11 s rupture / 0.20 s smoke. Cell 0 = the
intact enemy + white-hot starburst in the world's colours; cell 1 = rupture
(cracked, desaturated, radial cracks, chips flying); cell 2 = no hull, drifting
debris/vapour/sparks. Bright parts >= 6 px from the cell edge. Rocks just
shatter; machines die by their anatomy; creatures spill. Palette = the idle
strip's ramps.

Each death strip also powers the **Codex screen's triple-tap death** (works for every
enemy with an `Enemies/Death/<key>.png`: 46 today); an enemy without one cannot be
triple-tapped, so none may be skipped.

**Accept:** `verify_enemy_strip.py --death <strip> --idle-png <idle>` PASS (the
mine's idle cell is in the atlas: compare by eye); `EnemyDeathFlipbookTest`.
**Show:** `preview.png` (idle cell 3 + 3 death cells per row) + gif.
(Elite `_death.png` strips come with Phase 7; the boss death strip with Phase 6.)

### Phase 5 -- Rails (Codex J11)

`Resources/Worlds/<World>/rail_<name>_wide_v1.png`, **725x2170**, the same
structure as Space's/Ember's rails (riveted plates, hoses, lamps), world-specific
weathering and lamp colour, **vertically seamless** (top/bottom rows identical,
no end caps), transparent outside the rail. Ember/Frost rails were made with the
built-in image-edit tool from the closest existing rail as the edit target (see
`docs/rail-art-frost-ember.md` for the prompt style). The darker falloff toward the
screen border (`WorldPainter.DefaultEdge.outerDark/outerStart`, same strength in all
worlds, 2026-10-09) is shared shader code (`WorldRailRepeat`), and lane width is
`RailInset` (`BaseShift` .24 u per side widens every portrait lane): both apply to
a new rail automatically. Keep the rail's **visibility equal to Space's**:
`RailMatchTest` renders each world's rails alone through the game camera and
compares inner edge, width and rail-vs-lane contrast with Space's within a
tolerance (add the world to its `{ "Frost", "Verdant", "Ember" }` loops; read the
test for today's numbers, they moved with `RailInset`). Frost's 60% dim was
reverted for exactly this reason. Measure `RailBounds` (the inner/outer opaque
column / 725) on the PNG in Phase 9 -- it also feeds `RailWidthFactor` and the
rail-mine clamp (`RailMineArt.ClampBite`, `RailsVettingTest`).
**Accept:** 725 wide, tile test, `RailMatchTest`/`WorldRailTest` after wiring.
**Show:** the rail over each variant (`WorldRailReview` -> `RAIL_PREVIEW_DIR`).

### Phase 6 -- Boss art (Codex J6, then J9)

The boss is **one 384x384 cell at 3.3 world units**; the hit box is the shared
`BossConfig.BodyHitbox` (2.1 x 1.1), so keep the body's mass comparable and
inside the cell with room for the aura.

**6a. Base set** (all `Art/Resources/Bosses/`, staged first in `Art/Bosses/<Key>~/`):

1. `<Key>.png` **1920x1536** = 5 columns x 4 rows of 384 cells, all 20 non-empty
   (`BossArt`): row 0 idle 0-3 + hit 4; row 1 tell0 a,b / tell1 a,b / fire 9; row 2
   tell2 a,b / death 0-2; row 3 death 3-4 / retreat 0-1 / portrait. Same scale
   and registration in every cell. **No alpha > 24 within 6 px of a cell
   border** (the Space/Frost fix of 2026-10-08: glows chopped flat at the cell
   edge read as hard rectangles). Ember's shipped cells still touch the border
   in a few effect cells: do better.
2. `<Key>_shots.png` **1024x128** = 8 cells of 128: bolt 0-1, shard 2-3, lane
   telegraph 4, beam 5-6, muzzle charge 7.
3. `<Key>_card.png` **1024x288** (intro name card). `warning.png` is shared.
4. Design the boss around **three named firing parts** (jaw/eye/furnace/crown...)
   -- each attack comes out of a part and has a drawn tell pose (tell0/1/2).

**6b. Damage + fx + death (J9)** -- the user's rule: 5 hearts -> 4 damage stages and
**"make the last 2 frames way more damaged"** (late stages must look dramatically
wrecked; do not bake smoke/electricity into the hull cells):

1. `<Key>_damage.png` **768x1536**: 2 columns (idle loop A,B) x 4 rows (stage
   1-4, cumulative), each registered to the pristine idle cell (alpha bbox within
   the pristine bbox grown by 6 px), real cut holes in the silhouette, never
   bigger than the pristine footprint.
2. `<Key>_damage_fx.png` **2304x768**: 6 x 2 cells, row 0 smoke/steam loop
   (**bold, noticeably heavy**: Space's was too faint), row 1 electrical arcs loop.
3. `<Key>_death.png` **2304x384**: 6 cells (flash through breaches -> split ->
   core overload -> fireball peak -> shockwave ring and smoke -> fading embers),
   ~0.12 s each; parts may fill 90% of the cell only at the peak.
4. Set `damageKey`/`deathKey` in the `BossDef` (Phase 13); `smokeStrength` per
   boss.

**Accept:** `verify_boss_art.py <Bosses dir> <Key>` (sizes, 20 non-empty cells,
registration, stage differences; WARN lines for margins are the standing "do
better" list); view the sheet: late stages dramatic.
**Show:** the damage sheet with fx composited, the death gif, the card.

### Phase 7 -- Elite art (Codex J8)

5-6 elites (Frost 5, Ember 6; Verdant has only 1 -- do not copy that). Strip
layout per the `add-elite-ship` skill (default Ember layout idle0-3/tell/action/
hit, or the 7-cell flight layout: landed, grounded idle, lift-off, hover, bank
L, bank R, damaged), 192 cells, hull ~100-120 px wide, >= 16 px margin, same
hull/scale/anchor in every cell, top-down, nose up. Distinct silhouettes **and**
roles (herder, kiter, tender, tank, interceptor...). Optional `_parked`,
`_liftoff`, `_death` strips. Codex also writes a `manifest.md` (cell table, where
the muzzles and nozzles are, approximate) and a concept painting each.
**Accept:** `EnemyRosterTest.CellsHoldOnePoseEach` rules (the verifier script
works on elite strips with `--frames 7`; idle-drift lines there apply to the
flight cells you choose). **Show:** contact sheet + hover/bank gif.

### Phase 8 -- Death sounds and screams (Codex J10)

Authored WAVs for **every roster key and every elite**: `<key>_0/_1/_2.wav` (3
round-robin variants) and, **only for units with a living occupant or creatures**,
`<key>_scream_0/_1/_2.wav`. Scripts live in `Audio/EnemyDeathSrc~/`
(`build_<w>_sounds.py`, notes, audition WAV, sheet); model them on
`build_verdant_ember_sounds.py` + `build_revision.py`.

Rules (the user hit each of these bugs): **not beepy** (no sustained pure tones,
no sine sweeps above 1.5 kHz, no square chirps, never coin/alarm-like), round and
low (centroid < 1.8 kHz, bigs < 900 Hz, < 12% above 4 kHz), soft 2-8 ms attacks,
layered (thump + material body + detail + 80-250 ms room); mono 44.1 kHz 16-bit;
deterministic seeds. **Loudness is perceived, not peak**: A-weighted loudest
50 ms within +-1.5 dB of **-15** (big/elites up to 2 dB louder; the user heard
"random ones scream really loudly" when peak-normalised), no 1-3 kHz ringing
tonal run > 100 ms. Screams: long painful "ahhh" cries **0.7-1.2 s** (elites
1.0-1.3 s), radio-filtered, centroid < 1.5-1.7 kHz, < 8% above 3.5 kHz, peak
-9 dBFS, ~10-14 dB under their death cue in game (`ScreamVolume` .5). Every cue
is a believable physical/organic event for *that* unit; lines like "just
something breaking" get sent back. Do **not** produce backdrop/ambient/music (the user authors every world's backdrop,
ambient and music sounds himself). Remaining sounds never beep or coin. **Gap to
list in the report and phase for:** boss damage/death sounds are not authored for
*any* world yet. The scream-borrow hook (`EnemyDeathAudio.ScreamBorrow`, a
key -> borrowed-scream map) exists but is empty and unapproved: do not fill it
without the user's go.

**Accept:** `verify_wavs.py <EnemyDeath dir> <w>_ --big <big-ish names>` PASS (it
reproduces the A-weighted table, xcorr < .6, lengths +-20%); `EnemyDeathAudioTest`
(after Phase 15 adds the keys). **Show:** the audition WAV and the sheet; say
which cues most need ear-tuning (you cannot listen).

### Phase 9 -- Scaffold: the world exists (Claude agent)

Branch `feature/world-<w>-scaffold` from the integration branch, warm Library
(`cp -cR <warm worktree>/Pause/Library <wt>/Pause/Library`). Goal: the world
compiles, is in every list, tests parameterised by world count pass with
placeholders. Once `Worlds` has the entry the previous last world's end would lead
into a half-built planet, so keep all of this on the integration branch: **nothing
reaches master before Phase 17** (art-only `~` merges excepted).

1. `WorldManager.Worlds` + entry: `displayName`, `resourceFolder`,
   `musicResource = ""`, `progressiveMusic = false`, `portalColor`,
   `speedRampPerSecond`, `enemyRampScale`. Pace rule: each world ramps faster
   (.00315, .00330, .00345, .00365): propose Tide **.00385**, Storm **.00405**,
   `enemyRampScale` 1.50 / 1.65 (Space 1.00, Frost 1.10, Verdant 1.20, Ember
   1.35). Check with `DifficultyRetuneTest` ("a stock run meets its boss between
   HUD 32 and 35"), `WorldPaceTest` (every phase incl. Chaos before the boss at
   starts 0 and 30), `SpeedCapTest`; update the tests that pin the early-ramp
   list. Update the header comments of `WorldManager`.
2. `EnemyRoster.WorldKeys += "<w>"`; `CodexCatalogue.WorldIds += "world_<w>"`;
   `BossEmitterTable.Worlds`/`Parts`/`Points` (empty entry now, regenerated in
   Phase 13); `BackdropSet.CreateDirector` case; `WorldPainter`:
   `RailTextureName`, `RailBounds`, the world list in `VisibleRailEdges`,
   `EdgeFor` if it needs its own edge.
3. `EnemyDensity.PilotLoadAtLowSpeed/HighSpeed`: add entries (the lookup clamps
   to the last, so a missing entry silently reuses Ember's -- there is no
   assertion; add one). Ember: 3.5/2.5; later worlds may go up by .5.
4. `DeveloperUnlocks`: F5/F6 keys (the picker `DeveloperOptions` is automatic).
5. `CodexPanel.MaxSections` = worlds + 1 (**7** with 6 worlds); re-check
   `CodexTest` layout at the tested aspect ratios (a seventh chip may wrap).
6. `BossCatalog`: a placeholder boss entry (index-aligned) reusing an art key so
   `BossEncounterTest` "one boss per world" holds until Phase 13.
7. Make every test that hard-codes the world count/list generic or extend it. Run
   `scripts/world_audit.sh Ember <World>` and clear section 2 and 5 (the list of
   pinned tests is in `checklist.md`).
8. Docs: `docs/leaderboards.md` `furthest_world` range 1..N (see #15 below).

**Accept:** compiles; `AllTests.RunSuites -suites WorldLogicTest,WorldGatingTest,
WorldLeakTest,CodexTest,DeveloperModeTest,EnemyRosterTest,BossEncounterTest,
DifficultyRetuneTest,WorldPaceTest,SpeedCapTest` clean against a plain-master
control (art-dependent checks stay red until their phase).

### Phase 10 -- Planetfall, lift-off and the loop (Claude agent)

This is the Space-return rewiring. Read section 5 first.

**Release-switch pattern (used for Tide; reuse it for Storm).** A world is added to `Worlds[]` and to the
catalogues long before its backdrop/roster/boss exist, so the *production* loop must not reach it yet:
`WorldManager.TideEnabled` (default false) decides `LastLiveWorld` (`HasNext`, `PortalDestination`, boss rush
final all read it), the previous last world's `LiftoffDef` keeps looping while it is last live
(`loopsWhileLastLive` -> `AutoLoopNow`), and the new world's own `LiftoffDef.autoLoop = true`. Tests that walk
"every world" use `LiveWorldCount`/`LastLiveWorld`, so flipping the switch turns the unfinished-world checks red.
`TideLoopSoakTest` walks the chain headless with the switch off and on (and runs begun elsewhere); copy it for
the next world. Unknown-world lookups resolve to the previous world meanwhile (checklist section F).

1. Install the art: `git mv Art/Worlds/<World>/descent~/<7 files>
   Art/Backgrounds/Resources/Worlds/<World>/Planetfall/` (sources/manifest stay
   in `descent~`); `.meta` for new files only. `PlanetfallArtImporter` handles
   the folder.
2. `PlanetfallCatalog.<World>` (`world = N`, `folder`, 7 file names, the measured
   `planetCentrePx/planetDiscPx/limbApexPx/limbEdgePx/entryFrames=6/entryShipPx/
   entryHolePx/entryHoleX[6]/burstFrames=5`, colours `cue/heat/cold/flash/shade`,
   `openBanner "LAND ON <WORLD>"`, `urgeBanner "DIVE INTO <WORLD>"`,
   `chipPrefix "ORBIT  DANGER "`) and add it to `PlanetfallCatalog.All`. Numbers:
   from `verify_planetfall_art.py` + a circle fit on the globe's edge (the
   radius is the middle of the atmosphere rim band; see the Ember comment).
3. `LiftoffCatalog.<World>` (`world = N`, `planet = PlanetfallCatalog.<World>`,
   `interludeWorld = 0`, `banner = "LIFT OFF"`) added to `Defs`, **with
   `autoLoop = true`**; and **remove `autoLoop` from the previous last world's
   def** (Ember for Tide; Tide for Storm). Then its lift-off's `Gateway()` calls
   `OpenGateway()`, which flies this world's planetfall
   (`PlanetfallCatalog.For(prev, N, false)`).
4. Update every comment that says "Ember (the last world)" (`WorldManager`,
   `LiftoffDef`, `Liftoff`, `WorldTransition`).
5. Tests: `PlanetfallTest` (art sizes/pivots/release, descent all the way down,
   charge frozen), `LiftoffTest`, `OpenPortalTest`, `TransitionChargeTest`,
   `LoopTest`, `BossEncounterTest` (they pin "Ember is last / Ember's lift-off
   starts the loop"): move those expectations to the new last world and add
   "previous world's lift-off -> this planetfall -> arrives, control returns".
6. Previews: `PLANETFALL_FROM=<N-1> PLANETFALL_PREVIEW_DIR=... -executeMethod
   PlanetfallPreview.Run` and `LIFTOFF_WORLD=<N> LIFTOFF_PREVIEW_DIR=...
   -executeMethod LiftoffPreview.Run` (device sizes via `*_DEVICES`).

**Accept:** the suites above clean vs the control; both previews open and look
right (entry hole hugs the ship; no colour pop at the cloud break).

### Phase 11 -- Backdrop wiring (Claude agent)

Clone the closest director (Ember for dark/lava, Verdant for bright) rather than
inventing: `<World>Backdrop.cs`, `<World>Ambient.cs`, a `<World>BackdropPreview`,
`<World>BackdropTest`.

1. Install art: `git mv backdrop_v3~/{v1..v4,atlases} -> Art/Backgrounds/Resources/
   Worlds/<World>/Backdrop3/` (`src~` stays). Run the world's `ground_masks.py`
   and `measure_points.py` (copy Ember's; classes/shares per variant), commit
   `mask.txt` x4 and `points.json`.
2. `BackdropCatalog.specs`: a `Spec` (`folder "Worlds/<World>/Backdrop3/"`,
   `keyAtlas "landmarks"`, `variantSets = BackdropVariants.MaxVariants`,
   `brightness/saturation`, `variantBrightness` table (index = variant, 0 none),
   `variantNight`, `brightArt` only if painted bright, cloud knobs) and the layer
   stack (`sky .006, far .014, mid .024, ground .024 PinnedTo mid, flow .025,
   palls .040, mist .060, wisps .090, ceiling .130` + the world's weather
   particles at faster rates). Rates must strictly increase except a pinned layer
   sharing its host's; `MaxGroundRate` .05; pieces <= `MaxLandmarkSize` 1.8.
3. `<World>Tuning` (brightness, variant table, night boost, piece sizes/gaps,
   `PieceRule` per drawing, cluster weights per variant, site arrays,
   `CloudDensity/CeilingHold/CeilingClearSeconds` -- the user said clouds must
   thin to <= ~10% by 10 s and be gone by ~14 s: defaults 4 s hold / 14 s clear,
   `CloudCover`), `<World>Director : PlanetDirector` with the **cluster grammar**
   (a structure with a pipe run to a neighbour and its smoke; a feature front with
   patches and a pall; a vessel on the water with a bridge; lone pieces; then a
   long calm gap), `Pin(piece,x,gy)` / `planner.Find` / `planner.Step` per frame,
   `<World>AmbientCatalog` (loop specs: fps, alpha, scale; bindings from
   `points.json`).
4. **Smoke placement** (user, 2026-10-09: "smokes correctly placed over pipes and
   volcanoes and not to the side"): the loop's anchor (bottom-centre) sits ON the
   measured emitter; the plume is drawn above the piece, scaled with it, leaning
   with the one level `Wind`; the test renders each binding and checks the plume
   base is within 3 px of the emitter and that the emitter lies on opaque art.
5. **Elite launch sites**: new `LandingKind` values + `LandingSite.KindOf`
   strings, the director's `LandingSites` override (pad kinds that match the
   world: trench hatch, rig deck... see briefs), `EmergeScale*` constants. A world
   without `LandingSites` gets **no elites**.
6. **Delete old art in the same commit** (there is none for a brand-new world;
   but delete Codex's superseded candidates under `Resources/`).
7. Tests: `<World>BackdropTest` (mirror `EmberBackdropTest`, 790 lines), and
   in `WorldBackdropTest` (it hard-codes each v3 world; `grep -n Ember` there is
   the recipe): `<World>TextureBudgetBytes` (7 MB), the five value/sky/tile/chroma/
   cloud caps and their `ValueCap/ChromaCap/SkyLumCap/TileLumCap` switches, the
   `V3()` helper, the `<World>Atlases` dictionary (the drawings the director
   relies on, per atlas), `<World>AtlasAsDrawn` + `<World>DrawAlpha`,
   `Check<World>Palette` (distinct hue/value clusters, p5..p95 value range, counts
   of the world's signature-colour pixels in the fire/feature and light atlases),
   and the `{ "Frost", "Verdant", "Ember" }` loops (importer reimport at ~line 275,
   `CheckWalls` at ~899). Also `WorldLogicTest` art path via
   `BackdropCatalog.TileFolder(world, 1)` (not `Backdrop/sky.png`),
   `MenuBackdropTest` (pool includes it once reached), `UnusedAssetGuardTest`.
8. Register `<World>BackdropTest` in `AllTests.Suites`.
9. Real-frame previews: `<WORLD>_PREVIEW_DIR` runs for every variant at 2 s,
   10 s, 40 s; plus `CLOUD_PREVIEW_DIR` (cloud cover) and
   `ReadabilitySweep` (Phase 17).

**Accept:** `<World>BackdropTest`, `WorldBackdropTest`, `WorldLogicTest`,
`MenuBackdropTest`, `UnusedAssetGuardTest`, `BackdropCellClipTest`,
`CodexTest` clean vs the control; the user has seen the real frames.

### Phase 12 -- Roster, behaviours, mines, explosions (Claude agent)

1. `EnemyRoster.Build()`: 12 `Def(...)` entries (ids are stable forever: codex,
   explosions and saved discoveries key on them). Key = art key = strip name;
   `<w>_rock_*` with one `Floating(...)`, `<w>_mine` (role Mine), `<w>_big`,
   `<w>_fighter_1..4` (tiers), `<w>_chaser`, `<w>_alien`; codexId
   `hazard_<w>_rock_x` / `enemy_<w>_x`; display names, `concept`, 2-sentence lore
   (the codex voice: playful, gives a hint), explosion kind and size.
2. `EnemyBehaviours`: one `B("<key>", "<description>")` per enemy from the
   primitives (`Drift/Glide/Sway/Orbit/Track/March`, `Bob/Pulse/Brake/Sink/
   Patrol/Creep`, `Lunge/Shot/Ring/Cross/Lob/Laser`, `Spin`, `Sizes`, `Timing`,
   `Pilot(...)`, `Volleys`, `Chaser(ChaserStyle...)`). Targets from the table in
   `docs/enemy-behaviours.md`: about 40% of a world's enemies shoot, six attack
   with the body (`Lunge`); each world has a **big that holds a column and fires**,
   fighters 2-4 shoot differently (aimed / straight down / full ring / fan),
   rocks never shoot, the mine fires two lasers (`Laser`, `Muzzle`, `Timing`).
   Every behaviour needs a *counter* the player can read (tell cell 4/5 before the
   attack). `HazardSizeTest` (rock sizes, "no two live bodies overlap"),
   `EnemyDensityTest`, `EnemyBehaviourTest` hold the rules.
   **`ChaserStyle` has four values (Hound, Lancer, Weaver, Burner) and
   `EnemyBehaviourTest` asserts "the four worlds' chasers hunt in four different
   styles":** with 5-6 worlds either add new styles in `ChaserEnemy` (new code) or
   change the test to `min(worlds, styles)`; decide in Phase 0.
3. Palette: add the world's colours to `Art/Enemies/src~/palette.env`, the
   matching `EnemyPalette` constants (named in PascalCase; the test compares) and
   a `ThemeFor(N)` case (`hull/hullShadow/hullHighlight/accent/light/lightDim/
   explosion`); also `Art/Enemies/src~/common.py` WORLD_THEMES. No reds.
4. **Explosion material**: `TargetExplosion.Kind` rows follow
   `Weapons/Explosions.png` (Metal, Rock, Mine, Ice, Spore, Magma). A new
   material means a new row in the PNG (Codex, `Art/Weapons/src~` EXPLOSION_ROWS,
   `ExplosionV2Test`), a `Kind` value, `KindForWorld`, `EnemyRoster` constants.
   Or reuse an existing kind and say so.
5. **Rail mines**: `RailMineArt` slices `Enemies/Mines/rail_mines_neon.png`, a
   1254-px, 4-world atlas (a row per world; `Worlds = 4`; hard-coded `Rects`,
   `Pivots`, `CorePx`) whose SHA-1 is **pinned to the user-approved original**
   by `RailMineArtTest`. A world's mine therefore needs a **second atlas file**
   (same 4-column layout Dormant/Waking/Charging/Burst, new rows) and
   `RailMineArt.Frame(world, column)` routed to it, measured rects/pivots/core
   offset (`RailMineLaserTest` measures the core), the original test kept intact.
   Ask Codex for the new rows with the original atlas as the style reference.
   **Shot readability:** the Verdant mine's and boss's shots are WEAK on dark
   worlds, so the new world's mine needs *lit* (bright, saturated core and beam)
   rows in the second atlas, and its boss shots need checking on its own tiles.
   Inherited automatically (do not rebuild): rail mines **lock their row at windup**
   (`RailMineMount.AimLocked`, frozen `LockedRow`), rails get darker toward the
   outer screen edge (`WorldPainter.RailEdge.outerDark`) and the lane width is
   `RailInset.BaseShift` (.24).
6. `EnemyDensity`/`LoopRules` interplay: nothing per world except the pilot-load
   arrays (Phase 9). Spawn weights/phases are shared (`enmiesOnBoard` phases keyed
   on the level clock, `phaseRampScale`).
7. Wrong-world guards: spawners read `WorldManager.CurrentIndex` at every spawn;
   `WorldGatingTest` and `WorldLeakTest` iterate `Worlds` and audit every live
   object across steady state, planetfall, portal, loop and replay -- they must
   pass with no edits (if they need one, you found a leak).
8. Install the idle + death strips (`Art/Enemies/src~/<w>_death` stays as source).

**Accept:** `EnemyRosterTest`, `EnemyBehaviourTest`, `HazardSizeTest`,
`EnemyDensityTest`, `EnemyDeathFlipbookTest`, `RailMineArtTest`, `RailMineLaserTest`,
`WorldGatingTest`, `WorldLeakTest`, `CodexTest`, `ExplosionV2Test` clean vs control.

### Phase 12b -- THEMED ATTACKS (Codex J12 for art, Claude agent for code, **mandatory**)

Run after the roster and boss art are drafted (Phases 3, 6, 7), alongside Phases 12-13. The user's requirement is quoted in
constraint 10; the method is in repo `docs/world-attacks-design.md` (catalogue, contracts) and `docs/world-attacks-implementation-plan.md` (files, tests).

1. **Attack sheet.** For every roster unit (12), elite (5-6), the mine, and the boss's 5 attacks write the themed attack in the
   design-doc table format: name, reused primitive vs new, telegraph (>= the unit's current tell; >= .7 s for instant hits), damage and
   hit box (never harder than today unless noted), counter, **pink cue**, VFX, sound identity (material, no beeps), size S/M/L. Show it to the user with the brief.
2. **Pink-cue contract (PC1-PC5)**: pink keyline (`ShotOutline`), pink-white core flickering at `HostileShotPalette.FlickerHz`, material <= half of the saturated
   area and clear of 178/82/259/37, pointed shapes only (a round pearl/bubble gets a spiked pink rim), stepped hostile motion.
3. **Art (Codex J12)**: `art-briefs.md` section 10 / `docs/world-attacks-codex-prompts.md`: per world `<w>_attack_shots.png` (1024x256, bolt/shard/shell/slag/pool + 3 signature
   cells), `<w>_attack_fx.png` (impact, special, trail, glyph, flash), plus the world's `strike` / `jet` / `wave` / `ring` / `lash` / `beam` / `log` strips as its attacks need
   (the exact list is in `docs/world-attacks-art.md`). Say which attacks are code-only (procedural: neon lines, telegraph previews, cloud puffs, heat shimmer).
4. **Code**: skins (`ShotSkins.For(world, kind)`, procedural fallback so code never waits for art), the world's rows in `EnemyBehaviours`
   (attack primitive + shot kind), elite `shotKind`s and any new elite attack id, the boss's two signature attacks (`BossAttack.minPhase`; phase 1: 2 attacks, phase 2: 3, phase 3: all), the mine's beam skin
   (`MineLaserArt` prefers `AttackArt.Beam(world)`), and the attack sound cues (`AttackAudio`, material-based).
5. **Tests**: `ShotSkinTest` (pink share and hue audit of the world's art, skins differ across worlds), `AttackHazardTest`, `AttackFairnessTest`
   (tells, preview, live time, safe corridor), `AttackBudgetTest` (dodge-bot hit rate <= 1.15x the attack replaced), `AttackAudioTest`; extend `AtomClarityTest`, `HostileProjectileTest`,
   `ReadabilitySweep` (the world's shots + new hazards), `EnemyBehaviourTest` ("no two worlds' same-tier fighters share a skin / attack").
6. **Shows**: contact sheet of every attack with its telegraph frames over the world's real tiles; the pink-cue sweep; the sound audition.

**Accept:** the tests above clean vs control; `ReadabilitySweep` no new LOW/WEAK; the user saw the attack sheet and the previews.
**Do not** reuse the previous world's attack for a unit "because it is the same tier": a different material means a different attack.

### Phase 13 -- Boss wiring (Claude agent)

1. Install `Bosses/<Key>*.png` (importer `BossArtImporter` is by folder:
   max 4096, CompressedHQ, no mips).
2. `BossCatalog` entry (index-aligned): `id = CodexPrefix + "<w>"`, `name`,
   `title`, `artKey`, `damageKey`, `deathKey`, `lore` (the codex entry: 3
   sentences, hint-giving), sway (`swayX/Y`, `freqX/Y`), `flash` colour (never the
   player's red), `heartColor`, `smokeStrength`, and **3 `BossAttack`s** (the themed signature pair of Phase 12b makes it 5)
   (`kind` Aimed/Fan/Lob/Beam, `tell` pose 0..2, `tellSeconds` .6-1.0,
   `emitters` = part names, `fireFrame`, `volleys/count/spreadDeg/speed`,
   `style` Bolt/Shard, `rail` Absorb/Bounce/Pass, beams: `aim/aimDeg/sweepDeg/
   beamWidth/hold`, `cooldown` 1.1-1.4). Patterns unlock in thirds of the fight
   (`UnlockedAttacks`); the last third has shorter cooldowns; loops tighten
   cooldowns (`LoopRules`) and add a pattern. **Fairness**: every attack has a
   telegraph (tell pose + charge ring; beams a sight line), a safe gap or a safe
   side you can read, no unavoidable shot inside the first volley, shots stay
   `HostileShotPalette` pink.
3. Muzzle table: seed the new world in `Art/BossAttacks/src~/measure_emitters.py`
   (a patch of the drawing in one reference frame + offset to the muzzle pixel
   per named part), run it -> regenerates `BossEmitterTable.cs` (never hand-edit);
   `BossAttackTest` checks each muzzle lands on opaque art in every pose.
4. `BossHearts.Body(boss)` ellipse for the new art key (measure: every idle pixel
   with alpha >= .5 inside it; `BossHeartsTest` re-measures and also checks heart
   outline contrast on the world's backdrop).
5. `BossWarning.Accents` (the world's neon, never red), `BossAttackFx` row: extend
   `Art/BossAttacks/src~/build_boss_attack_fx.py` (6x4 cells -> 6x(N+1) rows,
   spark x3, muzzle flash x2, charge ring) and `BossAttackFx.Rows`.
6. Damage/death: the `BossDamageTest` literals name Space/Frost/Ember --
   extend (`b.artKey == ...`, the death-strip check `2304x384`, 6 distinct cells).
7. Rewards need no code: `ScoreRules.WorldClearedPoints(world)` scales by world
   index, boss bonus by `LoopRules.BonusScale`; `Codex.Discover` on meeting it.
8. `BossAttackPreview` (`BOSSATK_PREVIEW_DIR`), `BossDamagePreview`
   (`BOSSDAMAGE_PREVIEW_DIR`), `BossWarningPreview` -> contact sheets.

**Accept:** `BossAttackTest`, `BossEncounterTest`, `BossHeartsTest`,
`BossDamageTest`, `BossIntroTest`, `BossWarningTest`, `CodexTest` clean vs control.
**Show:** attack previews with telegraph frames; damage sheet; hearts overlay.

### Phase 14 -- Elite wiring (Claude agent, `add-elite-ship` skill)

For each elite follow that skill (sync art, `measure_elite_points.py`, def JSON,
brain/attack, landing site kind, tests, previews). World specifics:
`world` = the `EnemyRoster.WorldKeys` entry; `launchFrom` kinds from Phase 11;
`EliteTest` pins counts per world (`six Ember elites`...): add "N <World> elites
are defined", distinct brains/attacks inside the world, and a
`<World>EliteTest` for new brains/attacks (model: `FrostEliteTest`). Elite
hearts/shots: shot colours pass through `HostileShotPalette`; hearts must not be
red (`EliteTest`). Elites do not spawn in the first 20 s, during a boss or the
tutorial.
**Accept:** `EliteTest`, `EliteEvasionTest`, `<World>EliteTest`, `CodexTest`,
`EnemyRosterTest.CellsHoldOnePoseEach`.

### Phase 15 -- Audio hookup (Claude agent)

1. Copy the WAVs into `Audio/Resources/Audio/EnemyDeath/` with metas for new
   files; `EnemyDeathAudioImporter` sets import rules.
2. `EnemyDeathAudioTest`: add `<World>Keys` (all roster keys + all elites) to
   `AllKeys` and the screaming keys to `ScreamingNew`.
3. `EnemyDeathAudition` (Unity menu) lists keys automatically.

**Accept:** `EnemyDeathAudioTest` clean; the user listens (`afplay` audition).

### Phase 16 -- Meta (Claude agent)

1. **Codex**: world entry (`WorldIds`, `Backdrop("<World>")`, lore line in the
   user's voice), enemies/elites/boss list automatically; triple-tap death
   animation works for any enemy with a death strip (not mines/elites/bosses; 46
   today -- each new enemy needs its `Enemies/Death/<key>.png`). The world entry
   uses the world's **real backdrop tile** (the Space entry uses Space `sky_01`,
   `CodexCatalogue.SpaceSkyTexture`); no bespoke art. Test the triple-tap with
   **real pointer events** (see "Testing UI" below).
2. **Menus**: `MenuBackdrop` picks from the worlds up to
   `PrefsHighestWorld` and each installed variant -- automatic: a new world needs
   nothing but its real tiles (the user rejected bespoke menu vistas). Previews
   must never draw mock UI boxes/lines; render them device-like (below). Verify with
   `MenuBackdropTest` and `MENU_PREVIEW_DIR`. Unlock gating is
   `WorldManager.PrefsHighestWorld` (set by `CurrentIndex`'s setter).
3. **Dev**: world picker row (automatic), F-keys (Phase 9), `BossDev` rush-final
   already uses the last index.
4. **Shop/skins/tutorial**: no per-world content (grep confirms); tutorial stays
   Space.
5. **Leaderboards/achievements**: achievements are not per world; the
   `furthest_world` board is, with a range **1..N**. The store-side setup is the
   user's: Play Console / App Store Connect board limits ("Furthest World: Numeric,
   limits 1 to 4" -> 6). Flag it in the final report; see `docs/leaderboards.md`
   lines 45, 119, 144 and the `pause-store-setup` memory. Cloud save stores
   `highestWorld` as a plain int (no change).
6. **Docs**: `docs/enemy-behaviours.md` (a "### <World>" table), `docs/art-style.md`
   (palette table + do/don't), `docs/speed-and-loops.md` (loop chain),
   `docs/art-production-queue.md`, `docs/project-layout.md` if folders changed,
   `docs/rail-art-*.md`.
7. **Strings**: the game has no localisation layer (only `StringHolder` store ids);
   user-visible text is in code: banners (`WorldBanner`), planetfall banners,
   codex lore, boss names.

### Phase 17 -- Integration, gates and release

See `checklist.md` "World release gate". In order:

1. **Integrate**: branch `integrate/world-<w>` from current master; merge the
   art branches (staged `~`), then feature branches; `git merge master` into it
   again right before the run.
2. **Full suite**: `scripts/unity-batch.sh -executeMethod AllTests.RunAll`
   (one Unity at a time, machine-wide lock; wrapper handles the stale
   `Unity.Licensing.Client` helper that makes runs hang or print fake compiler
   errors; for a fresh worktree `cp -cR` a warm worktree's `Pause/Library` first).
   Read `[ALL] RESULT: PASS|FAIL failures=M failed=A,B`.
3. **Baseline**: run the same suites on a **plain-master control worktree** and
   compare. Known failures change daily (2026-10-09: `BossAttackTest` pod lasers,
   `SpawnSpaceTest` x2, `UnusedAssetGuardTest` HapticGate orphan, flaky
   allocation meters); only differences are yours. A hand-picked list is not
   enough for deletions -- a Frost cleanup once broke `WorldLogicTest`.
4. **Soak (new, write it for World 5 if absent)**: `WorldLoopSoakTest` flies
   every world -> next -> ... -> last -> Space (real `Planetfall`, `Liftoff`,
   `StartLoop`) twice, asserting at each hand-off: world index, `RunLoop.Index`,
   score and hearts carried, no portal where none is due, no live object from the
   wrong world (`EnemyIdentity`/`EliteDef.world`), speed reset to
   `ArrivalSpeed(loop)`, music/rails/backdrop of the right world, the 8 s calm
   window re-armed, `WorldTransition.InProgress` false at the end.
5. **Readability sweep**: `scripts/unity-batch.sh -executeMethod
   ReadabilitySweep.Run` with `READABILITY_DIR=...` (add the world to its
   `Worlds` array first); before/after vs master; no new LOW/WEAK rows; open the
   per-world contact sheet.
6. **Builds**: `BuildScript.BuildMac` and `BuildScript.BuildAndroidDev`
   (`grep "\[BUILD\] result="` -> Succeeded; Gradle daemon failures: retry up to 3x).
   No install.
7. **Release gate**: present the checklist, the previews, the known-failure diff
   and the open questions to the user; **real-device pass only on his OK**; merge
   to master only on his explicit go (fast-forward only: `git merge --ff-only
   integrate/world-<w>` in the main checkout while it is clean).

## 5. Seamless return to Space (how the loop continues the Space world)

The chain after the new world exists, with `L` = the last world:

```
... -> World L-1 --boss--> lift-off (L-1) --interlude (Space sky)--> OpenGateway --> planetfall(L) --> World L
World L --boss--> lift-off (L) --interlude (Space sky)--> StartLoop (autoLoop) --> Space, RunLoop.Index + 1
```

What each hand-off does, and the checks that guard it:

- **Boss over** -> `WorldManager.OnBossOver` -> `OpenPortal()` -> `Liftoff.Spawn(
  LiftoffCatalog.For(CurrentIndex, PortalDestination, !HasNext))`. The stage is
  `Portal` (level clock stopped, **no pressure** during the lift-off), spawning
  suspended (`Liftoff.SuspendsSpawning`), `WorldTransition.InProgress` true (weapon
  charge frozen, kept and released afterwards).
- **Rise** swaps the backdrop and rails to `interludeWorld` (Space) *while the
  clouds cover the view*, so no pop. The world index, enemies and music still
  belong to the old world until the gateway.
- **Interlude** ~3.3 s of calm Space (`SpaceDirector.Quiet` holds back stations
  and rocks), then `Liftoff.Gateway()`:
  - if `def.autoLoop && !WorldManager.HasNext && PortalDestination ==
    def.interludeWorld` -> `WorldManager.StartLoop()` -> `Advance(true)`:
    `Planetfall.ClearBoard`, `RunScore.OnWorldCleared(L)`, `PortalPressure.Close`,
    `RunScore.OnLoop(RunLoop.Advance())`, `BossEncounter.ForgetDone()`,
    `CurrentIndex = RunLoop.StartWorld`, `SpeedRamp.SetNatural(ArrivalSpeed(loop))`,
    `distanceLeft` reset, `ApplyDifficulty` (ramp, `phaseRampScale`,
    `LoopDifficulty.DensityScale`), `WorldPainter.Apply`, `WorldMusic.Apply`,
    `WorldBackdrop.Apply`, banner `Space  LOOP n`, codex discover. The Space sky is
    already on screen, so the change is invisible; `enmiesOnBoard` re-arms its
    8 s calm window because its `arrivalKey` (world + loop) changed; elites wait 20 s.
  - otherwise `OpenGateway()` -> next planet's planetfall (or the portal).
- **A run that began elsewhere** (developer start world, replay, "furthest world
  reached" = L): `PortalDestination` is `RunLoop.StartWorld` != the interlude world
  -> the lift-off falls through to `OpenGateway()` -> the loop **portal**. Keep
  that path working (`LoopTest.LoopBackFromAn<L>Start`).
- **Failure safety**: if `StartLoop` throws or changes nothing it returns false and
  the loop portal opens, so the pilot is never stranded.

Adding a world therefore means: the old last world's `autoLoop` goes away and the
new one's is set (Phase 10), and **every test that says "<old last> loops"** moves
(list in `checklist.md`). Existing saves are fine: `highestWorld` is an int, a
player whose best was Ember starts the next run in Ember and simply meets the new
planet at the end of it.

## 6. Things easy to miss (flagged)

0. **THEMED ATTACKS are mandatory (Phase 12b).** The first four worlds' shooters all fired the same pink darts until the user asked for ice / fire / vines / water attacks; do not repeat that. Read constraint 10 and `docs/world-attacks-design.md`.

Items 1-12 are from the coordinator's experience; **items marked NEW were found
auditing the code for this skill.**

1. **Hostile shots are pink in every world** (`HostileShotPalette` 312-326
   degrees), and atoms own cyan 178 / green 82 / violet 259 / amber 37. NEW: a
   world palette built on magenta, cyan or violet fights a pickup or a shot --
   decide it in Phase 0 (`world-5-6-briefs.md` flags both proposals).
2. **NEW -- the rail-mine atlas is SHA-1 pinned** (`RailMineArtTest`) and sliced by
   hard-coded rects for 4 worlds; a world's mine needs a second atlas + routing.
3. **NEW -- `ChaserStyle` has four values** and `EnemyBehaviourTest` demands one
   style per world.
4. **NEW -- `BossAttackFx` is a 6x4 sheet, a row per boss**, built by
   `build_boss_attack_fx.py`; `BossEmitterTable` is generated by
   `measure_emitters.py` (seed the new boss there); `BossHearts.Body` and
   `BossWarning.Accents` are per-world switches/arrays.
5. **NEW -- `TargetExplosion.Kind` rows are tied to `Explosions.png` rows.**
6. **NEW -- `LandingKind` is an enum per world** (+ `KindOf` strings); no
   `LandingSites` override = no elites.
7. **NEW -- `CodexPanel.MaxSections = 5`** (4 worlds + bosses): 6 worlds need 7.
8. **NEW -- `EnemyDensity.PilotLoadAt*Speed` arrays clamp silently.**
9. **NEW -- `docs/leaderboards.md` `furthest_world` range 1..4** is also a store-side
   setting (Play Console / Game Center) the user must change.
10. **NEW -- `WorldBackdropTest` hard-codes per-world constants** (budget, value/
    chroma caps, atlas dictionaries, palette checks, `{ "Frost","Verdant","Ember" }` loops) -- a new
    world needs its own block; same for `HostileProjectileTest`, `BossHeartsTest`,
    `AtomClarityTest`, `ReadabilitySweep` and the preview tools' `Worlds` arrays.
11. **NEW -- `DeveloperModeTest`, `WorldLogicTest`, `ReplayTest`, `LoopTest`,
    `SpeedCapTest`, `DifficultyRebalanceTest` pin world indices/ramp lists.**
12. **NEW -- No synthesized death-sound fallback**: missing WAVs = silent enemy +
    red `EnemyDeathAudioTest`.
13. **NEW -- the shipped Ember boss atlas still has cells within 6 px of its border**
    (row 1 col 4, rows 2-3 effect cells; `verify_boss_art.py` WARNs): don't treat
    "Ember did it" as the bar.
14. The previous last world's lift-off must stop looping (Phase 10) and the new
    world's must start it.
15. Texture budgets per world in `WorldBackdropTest` (Frost 12 MB, Space 10, Verdant 7,
    Ember 7, default 9). The planetfall art is loaded for one sequence and released
    (`PlanetfallArt.Release`) so it is not in that budget.
16. Delete old art in the same change that wires the replacement; stage in `~`
    folders until then; never delete early.
17. For a deletion run the **full** `RunAll` and compare with the control.
18. Frost's rails were dimmed to 60% and the user later reverted it: keep rail
    visibility equal to Space's.
19. Clouds: thin after the first ~10 s (`CloudCover`), ceiling continues the
    planetfall deck; Verdant had a colour pop at the hand-off.
20. Pieces must be **pinned to the ground** (same rate as the mid tile), placed by
    affinity mask, grown as clusters with long calm gaps -- scattered stickers were
    the user's complaint ("randomly placed and not sensible").
21. Two bosses of the existing four have no damage art (Verdant); the new ones must.
22. The Codex screen needs a death strip for every enemy for the triple-tap
    animation; mines/elites/bosses are excluded.
23. Cloud-save/`PrefsHighestWorld`/menu pool: nothing to change, but test a save
    with `highestWorld` = N-1.
24. Copy-paste hazard in prompts: `prompt_ember_death.md` still said "Ember = icebreaker
    steel + glacier ice + cyan" from the Frost template. Search your prompt for
    the other world's nouns before launching.

## 7. Pitfalls

- **Testing UI**: a feature (Codex triple-tap death) passed tests that called its
  hook directly yet failed on a real touch: a nested sub-`Canvas` without a
  `GraphicRaycaster` swallowed the pointer events. Any interactive UI needs a test that
  sends **real pointer events** through the EventSystem/raycast rules (see the
  pointer-event tests added in `b73108f5`), not a direct method call. Preview/batch
  tools must **lay out like a device**: the home-screen "Play touches the logo" false
  alarm came from a fake world-space canvas; use the ScreenFit rig /
  `MenuBackdropPreview` device settings (aspect, safe area, canvas scaler).
- **Art that fails the Unity quality tests is held back, not excused.** Do not loosen
  the floors (detail floor, margins, motion): send it back to Codex with the failing
  numbers (Verdant's quantised strips were redone this way). Codex outputs are
  committed on `art/<x>` branches; integration agents merge them.
- **Stale `Unity.Licensing.Client`** makes batch builds hang or print fake compiler
  errors: kill the helper and rerun (`unity-batch.sh` handles it).
- **Merging into the main checkout** is blocked by Unity-generated untracked `.meta`
  files and importer rewrites of dirty files. Discard only files that are *identical to
  the branch's*; never discard anyone else's edits. Repeated, re-sent subagent
  hand-backs are harmless: ignore the duplicates.
- **The integration loop**: merge `art/*` then `feature/*` into `integrate/world-<w>`,
  run the full `RunAll`, run it on a plain-master control, compare to the known
  baseline; only differences are yours.

- Letting Codex "finish" with a procedural-only script: **image generation is
  mandatory**; procedural-only results get rejected. Put "use image generation"
  in every prompt.
- Believing a manifest's emitter points. Measure on pixels.
- Brightness drift: raising a world's brightness needs a deliberate test-limit
  change *with a reason*, and the lane contrast guard (enemy bodies >= 2.5:1,
  brightest tone >= 7:1 against the rendered lane; lane darker than the enemy
  hull) must still pass.
- Merging art whose wiring is not ready: orphans fail `UnusedAssetGuardTest`
  (keep it in a `~` folder).
- Spawning more than 2 Codex jobs or 3 agents; running two Unity batches at once
  (use `scripts/unity-batch.sh`, it queues).
- Committing Unity's `.meta` churn on existing files, or anything in the main
  checkout.
- Treating a clean local run as proof: flaky meters exist (allocation meters,
  `EliteTest` Frost landing-site checks); re-run a failing suite once and compare
  to the control before blaming your change.
- Skipping the honest critique. The user wants the weak parts named ("entry
  plasma is a thin ribbon", "sky/far are flat haze") before he finds them.
