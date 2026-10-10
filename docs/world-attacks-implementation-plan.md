# World attacks: implementation plan

Companion to `world-attacks-design.md` (what), `world-attacks-art.md` and `world-attacks-codex-prompts.md` (art). This is the how:
phases, files, tests, parallelisation, risks. Nothing here is built yet; no code or art was changed on this branch.

Rules inherited from the project: one worktree + branch per agent (`feature/attacks-<name>`), integrate on **`integrate/world-attacks`**, never merge
to master without the user's go-ahead, never edit the main checkout or other sessions' worktrees, Codex works headless
in `codex-<job>` worktrees (max 2 jobs, 3 Claude agents), every Claude agent is spawned with `model: sonnet`, art is opened for the user before it is wired,
audit every Codex commit batch before trusting it, compare any red suite against a control worktree of master before blaming the branch.

## 1. Architecture in one page

```
EnemyBehaviour (roster)         EliteDef (elite)              BossAttack (boss)
   attack: Shot/Lob/Laser/...      attack id + shotKind          kind: Aimed/Fan/Lob/Beam + NEW
   + NEW Jet/Wave/Blast/           + NEW id log_roll             Jet/Wave/Blast/Strike/Lash/Roll
     Strike/Lash                                                 + minPhase
        |                              |                            |
        +--------------+---------------+----------------------------+
                       v
   ShotSkins.For(world, kind) -> ShotSkin { sprite pair, draw scale, trail, motion flags, impact set, sound ids }   (NEW)
                       |
   EliteShot (Launch / Step)         Attack hazards (NEW, pooled, IHostileZone)     RailMineLaser / BossBeam
   motions: Streak Shatter Flutter   AttackJet  AttackWave  AttackBlast              skin from AttackArt.Beam(world)
   Slash Roll Burst (+ existing      AttackStrike  AttackLash  (+ AttackPreview)
   Lob Sling Fuse Glide)
                       |
   HostileShots (shot vs shot; zones burn shots)   FriendlyFire.HostileHit   ShotOutline / HostileGlow (the pink keyline)
                       |
   AttackAudio.Play(world, cue)  (NEW)      AttackArt loader: Resources/Attacks/<World>/*.png with a procedural fallback
```

Design choices that keep this safe:

1. **A skin is data, looked up by the shot's own world** (`EliteDef.world` / `WorldIndex`; the roster's `ShotStyle` gets the enemy's world). The whole existing elite and roster set is therefore re-themed with no behaviour edit.
2. **Every art lookup has a procedural fallback** (the current `EliteFxArt` sprites). Code lands and passes tests *before* Codex art does; art lands later by dropping files into `Resources/Attacks/<World>/` and a test pins them.
3. **New hazards are `IHostileZone`s** (live flag, touch test against a circle, owner id, live age), registered in `HostileShots` next to `BossBeam`. A zone burns light and heavy shots crossing it; players cannot shoot it down.
4. **One preview geometry**: the telegraph outline is drawn from the *same shape object* as the hitbox (FR2), so they cannot disagree.
5. **No ship physics and no new gameplay system** (no ship current, no pull): whirlpool = `Sling` curve for shots only.

## 2. Files

| Area | Files (existing, edit) | Files (new) |
| --- | --- | --- |
| Skins | `Gameplay/Elites/EliteShots.cs` (`EliteShot.Launch/Step/Burst/Land`: skin lookup, motion flags, new `Kind` values `Slash`, `Log`; `EliteShots.KindOf` strings "slash", "log"), `Gameplay/Elites/EliteArt.cs` (`EliteFxArt` stays the fallback), `Gameplay/Enemies/EnemyBehaviours.cs` (`EnemyBehaviour.ShotStyle` carries the world; per-world rows), `Gameplay/Elites/EliteDef.cs` (`shotMotion`) | `Gameplay/Enemies/ShotSkins.cs`, `Gameplay/Enemies/AttackArt.cs` (loader + fallback), `Gameplay/Enemies/ShotTrail.cs`, `Gameplay/Enemies/ShotImpact.cs` |
| Hazards | `Gameplay/Enemies/HostileShots.cs` (zone registry + burn loop generalised), `Gameplay/Enemies/EnemyBrain.cs` (`EnemyThreat` weights, windup preview, Release for new attacks), `Gameplay/Enemies/EnemyBehaviours.cs` (`EnemyAttack` += `Jet, Wave, Blast, Strike, Lash`; builders `.Jet(...)`, `.Wave(...)`, `.Blast(...)`, `.Strike(...)`, `.Lash(...)`; `Shoots`/`Attacks`/`Down`), `Gameplay/Enemies/FriendlyFire.cs` (zone hits once per pulse) | `Gameplay/Enemies/Attacks/HostileZone.cs`, `AttackJet.cs`, `AttackWave.cs`, `AttackBlast.cs`, `AttackStrike.cs`, `AttackLash.cs`, `AttackPreview.cs`, `AttackPools.cs` |
| Mines | `Gameplay/Enemies/RailMineLaser.cs` (`MineLaserArt.For(world)` prefers `AttackArt.Beam(world)`; the magenta-hue conversion stays for the fallback), `Gameplay/Enemies/RailMineArt.cs` untouched (SHA pinned) | |
| Elites | `Gameplay/Elites/EliteAttacks.cs` (`Ids` += `log_roll`, `Create`), `Gameplay/Elites/EliteBrains.cs` (a `hauler` brain variant is reused), `Art/Resources/Elites/Defs/*.json` (`shotKind`; the new `verdant_elite_timber_hauler.json`), `Gameplay/Elites/EliteFx.cs` (`Flipbook` reused) | `Gameplay/Elites/LogRollAttack.cs` (or inside `EliteAttacks.cs`, house style) |
| Bosses | `Bosses/BossCatalog.cs` (`BossAttackKind` += `Jet, Wave, Blast, Strike, Lash, Roll`; `BossAttack.minPhase`, gap/lane/count fields; 2 new attacks per boss; `UnlockedAttacks` filters by `minPhase`), `Bosses/BossActor.cs` / `BossEncounter.cs` (execute the new kinds; reuse `BossEmitters` parts), `Bosses/BossProjectiles.cs` (`BossBeam` skin override, `BossProjectilePool` owns the new zones for boss use), `Bosses/BossAttackFx.cs` (unchanged sheet), `Bosses/BossEmitterTable.cs` (never hand-edited; the new attacks use existing part names) | boss executors in `Bosses/BossHazards.cs` |
| Audio | `EliteShot.Launch`, `EnemyBrain` windup / release, `AttackJet/Wave/...`, `BossProjectile` launch | `Gameplay/Enemies/AttackAudio.cs`; `Audio/AttackSrc~/` (generator), `Audio/Resources/Audio/Attack/<world>/*.wav` |
| Art | | `Art/Attacks/<World>~/` staged, then `Art/Resources/Attacks/<World>/` (+ `.meta` for new files only) |
| Tools | `Editor/Tools/ReadabilitySweep.cs` (items for each new hazard), `Editor/Tools/Previews/HostileProjectilePreview.cs`, `BossAttackPreview.cs` | `Editor/Tools/Previews/AttackHazardPreview.cs`, `.claude/skills/add-world/scripts/verify_attack_art.py` |

Naming check (verified in the repo): `EliteShots.Kind` (Bolt, Shard, Slag, Shell, Glob, Slab, Orb), `EnemyAttack` (None, Lunge, Shot, Ring, Cross, Lob, Laser), `EnemyVolley.Fire`, `EnemyBrain.TellFloorSeconds` /
`MinFireAbove` / `MinFireDistance`, `EnemyThreat.MaxEnemyShots` / `VolleyGap` / `ShotWeight`, `HostileShots.Register`, `RailMineLaser` (`AimSeconds`, `BeamSeconds`, `HitThickness`),
`MineLaserArt`, `BossAttackKind` (Aimed, Fan, Lob, Beam), `BossBeam`, `BossProjectilePool`, `BossCatalog.UnlockedAttacks`, `EliteAttacks.Ids`, `EliteFx.HeatShimmer`, `EliteFxArt`, `ShotOutline.For`, `HostileShotPalette`, `HostileGlow.IsPlayerRed`.

## 3. Phases

Estimates are agent wall-clock hours on a Sonnet agent, as in the add-world skill. `P` = can run in parallel with the listed phases.

| # | Phase | Owner | Needs | Parallel with | Est. |
| --- | --- | --- | --- | --- | --- |
| 0a | **Baseline capture**: the dodge bot (reaction .25 s) run against the *current* attacks, hit rates pinned as constants; `AttackBudgetTest` skeleton | A1 | -- | J12a, J12c | 3 h |
| 0b | **Foundations**: `ShotSkin`/`ShotSkins`/`AttackArt` (+ fallback), world plumbed to roster `ShotStyle`, `IHostileZone` + `HostileShots` zones, `AttackPreview`, `AttackPools`, `verify_attack_art.py`, `ShotSkinTest` | A1 | 0a | J12a, J12c | 8 h |
| 1a | `AttackJet` core (cone / column, live time, hitbox, pink edge, preview, pools) + `EnemyAttack.Jet` | A2 | 0b | 1b-1e | 6 h |
| 1b | `AttackWave` core (band with gap, falling, preview, markers) | A3 | 0b | | 5 h |
| 1c | `AttackBlast` core (ring of bars with a gap) | A2 | 1a | | 5 h |
| 1d | `AttackStrike` core (lane glyph, column, ground burst) | A3 | 1b | | 5 h |
| 1e | `AttackLash` core (arc sweep, segment chain) | A1 | 0b | | 6 h |
| 1f | Projectile behaviours in `EliteShot`: Streak, Shatter, Flutter, Slash, Roll, Burst | A1 | 0b | | 12 h |
| 1g | Boss infrastructure: `minPhase`, new `BossAttackKind` executors on top of the cores, `BossAttackTest` / `BossEncounterTest` updates | A3 | 1a-1e | | 5 h |
| 1h | `AttackAudio` + generator skeleton + placeholder WAVs | A2 | 0b | | 6 h |
| 2 | **Space** (no art): Lance Jet, Streak, ion arcs, scan-line Wave, Bastion/Warden/Twin Claw rows, Rift Lancer slugs, Archon rows | A | 1a, 1b, 1f, 1g | Codex J12 | 4 h |
| 3 | **Frost**: skins, Frost ray, Blast (Golem), Icicle + Shatter (Icicle/Kite), Hail cloud, icicle drop Strike, Leviathan rows, elites re-skin | A | 1c, 1d, 1f, 1g, art J12a/b | 4 | 6 h |
| 4 | **Ember**: skins, fire laser, Flame Jet (Brand), nova, slag, eruption Strike, Drake rows, elites re-skin | A | 1a, 1d, art J12c/d | 3 | 6 h |
| 5 | **Verdant**: skins, thorn vine mine, Pod/Burst (Maw), Slash (Mantis), Flutter leaf (Sprout), Thorn fan, Lash vine, Roll trunk, `log_roll` + Timber Hauler elite (art via `add-elite-ship`), Queen rows, acid beam skin | A | 1e, 1f, art J12e/f | | 8 h |
| 6 | **Sounds**: generate the 20 base cues, hook, `AttackAudioTest` | A2 | 1h; materials per world | 2-5 | 8 h + the user's ear |
| 7 | **Tide** (when its roster exists, add-world phase 12b): skins, pressure jet, Pearl fan, Spout, Thunder strike, surf Wave, depth charge, net, Kraken rows | A | Tide roster + palette decision, 1b, 1d, art J12g/h | | 8 h |
| 8 | **Integration and release gate**: full `RunAll` vs a master control; `ReadabilitySweep` no new LOW/WEAK; `AtomClarityTest`; soak; Mac + Android dev builds; the user plays each world | A + Y + U | all | | 6 h |

Order for the first pass: 0a, 0b, then the cores (1a-1h) split over the three slots, then Frost and Ember together, Verdant, Space in any gap, Tide last. Codex J12a and J12c can start on **day one**
(they need only the design doc), J12b/d/e/f follow while the cores are written.

Total: about 105 agent-hours (3-4 working days with three agents) plus eight Codex jobs.

### Slot plan

| Day | Claude slot 1 | Claude slot 2 | Claude slot 3 | Codex 1 | Codex 2 |
| --- | --- | --- | --- | --- | --- |
| 1 | 0a, 0b | (waits on 0b; reviews the design doc against code) | -- | J12a Frost shots + fx | J12c Ember shots + fx |
| 2 | 1f projectile behaviours | 1a Jet, then 1c Blast | 1b Wave, then 1d Strike | J12b Frost strike + ring + beam | J12d Ember jet + strike + beam |
| 3 | 1e Lash, 1g boss infra | 1h audio skeleton | Space (2) | J12e Verdant shots + fx + log | J12f Verdant lash + beam |
| 4 | Frost (3) | Ember (4) | Verdant (5) | audits, fix passes | fix passes |
| 5 | sounds (6) | integration (8) | | | |
| later | | Tide (7) | | J12g, J12h | |

## 4. Tests

New tests (`Pause/Assets/Editor/Tests/`, each `scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites <Name>`, registered in `AllTests`):

| Test | What it pins |
| --- | --- |
| `ShotSkinTest` | every (world, kind) resolves a sprite pair (or the fallback); loaded art meets the pink-cue contract on pixels (>= 30% pink-family/white of the opaque area, hue audit vs 178/82/259/37, zero red-band pixels); sizes and cell grids of the atlases; skins differ across worlds for the same kind (the "not all worlds identical" assertion); nothing allocates in `Launch` |
| `AttackHazardTest` | per primitive: hit geometry (cone polygon, column rectangle, band with a gap, ring annulus with a gap, strike column, lash arc) against sample points; a hit costs one heart, shield absorbs, blink erases only when the hull lands on it, at most once per live pulse; friendly fire (`FriendlyFire.HostileHit`) except the shooter; paused world freezes it; pooled, no allocation; death domino and world change clear it (`WorldLeakTest` extended) |
| `AttackFairnessTest` | the fairness contract FR1-FR4 for every themed attack in every world: tell >= floor and >= .7 s for instant hits, preview visible >= .4 s, live-time and speed caps, a reachable safe corridor >= 1.4 u at every sampled moment (reaction .25 s, ship speed), aim locked at the tell, fires only >= `MinFireAbove` / `MinFireDistance` |
| `AttackBudgetTest` | the dodge bot's hit rate over 2000 seeded rolls per attack <= 1.15 x the old attack's pinned rate; cooldown/volleys not higher; `EnemyThreat` weights; prints the table |
| `BossPhaseUnlockTest` | 2 / 3 / 5 attacks by phase with `minPhase`; every boss has 5 attacks; the new kinds have emitters on opaque art (`BossAttackTest` already measures muzzles) |
| `AttackAudioTest` | every cue key has a WAV, no beep (spectral flatness / no pure tone as the main layer), loudness band, pause silence, pooled sources, `verify_wavs.py` rules |
| `AttackArtTest` | the shipped atlases exist at the exact sizes (when the files are present), reserved cells empty, `.meta` hygiene, no orphan (`UnusedAssetGuardTest`) |

Existing tests that must be extended (and which would otherwise silently miss the new shapes):

| Test / tool | Extension |
| --- | --- |
| `HostileProjectileTest` | every skin wears the keyline and keeps its hitbox (the "keeps its size and hitbox" clause: draw size 2x, hit radius unchanged); new zones burn crossing shots; zones are not shot down |
| `AtomClarityTest` | renders each world's skins (it already renders roster shots in every kind through `EliteShot.Launch`, so skins come free) **and** the new strips (jet, band, ring, strike, lash) with the strip variant of the five cues; thresholds `MinShotCues` / `MinBossCues` unchanged |
| `ReadabilitySweep` | the shots column gains every new hazard and skin; flags LOW/WEAK; run on all five backdrops (Space, Frost, Verdant, Ember, Tide) |
| `EnemyBehaviourTest` | new attack primitives in the table; "no two worlds' fighter-N share a skin"; every world's mine uses its own beam skin; rocks never shoot |
| `EnemyDensityTest` | threat weights of the new hazards; `MaxEnemyShots` honoured |
| `BossAttackTest`, `BossEncounterTest`, `BossHeartsTest` | five attacks, new kinds, no unfair first volley, phases |
| `RailMineLaserTest`, `RailMineArtTest` | the beam skin loads and the geometry/hit tests are unchanged; the original atlas SHA still pinned |
| `EliteTest`, `FrostEliteTest`, `SpaceEliteTest` | `log_roll` registered; elites' shot kinds resolve skins; counts per world (Verdant now 2) |
| `WorldLeakTest`, `WorldGatingTest` | no new pooled hazard survives a world change, a portal, a loop or a replay |
| `EnemyDeathAudioTest` | untouched (attack audio is its own layer) |

Definition of done per phase = the new/extended tests green **vs a master control** plus a preview contact sheet the coordinator opens for the user
(`AttackHazardPreview`, `HostileProjectilePreview`, `BossAttackPreview` with telegraph frames).

## 5. Parallelisation notes

* The three hot files are `EliteShots.cs`, `HostileShots.cs`, `EnemyBehaviours.cs`/`EnemyBrain.cs`. Phase 0b owns the first two; the cores live in **new** files and only
  add one `case` each to `EnemyBrain`/`EnemyVolley` (one merge each, resolved by the integrator). Per-world phases edit **only their world's rows** of the behaviour table
  (the table is grouped by world) and `BossCatalog`'s own boss entry.
* Art never blocks code (fallbacks) and code never blocks art (the prompts need only the design doc). The only art dependency on code is the wiring commit that moves staged art into `Resources/`.
* Space needs no Codex, so it is the cheapest world to finish first and doubles as the proof of the primitives; Frost and Ember are the most visible wins and should be the first two shown to the user.
* The sound generator (`AttackSrc~`) is independent of everything except the cue key list (design doc 0.3).

## 6. Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Themed bodies drift off pink and become mistakable for atoms (the original "I keep mistaking enemy bullets for atoms") | the pink-cue contract is a pixel test (`ShotSkinTest`) and `AtomClarityTest` runs on every skin; Frost/Tide water and ice are white, not cyan; Verdant leaf is dark, not lime; Ember orange stays 22-30 |
| Ember orange vs star-dust amber 37 and the red band | the orange ramp is the only risk; fallback: shift flames to pink-white -> magenta only (the fringe already carries the cue); `AtomClarityTest` decides |
| Round pearls and bubbles read as atoms | PC4: scalloped / spiked rims; verified by the silhouette cue |
| Instant-hit hazards feel unfair | FR1-FR4 are asserted headless; longer tells than today; a drawn preview; guaranteed corridor; the dodge-bot hit rate |
| Difficulty creep (a ring or band is "bigger" than the fan it replaces) | `AttackBudgetTest` pins the hit rate, volleys are cut where the footprint is larger (Golem 3 -> 2, Hammerhead 3 -> 2, Brand 3 -> 2, Kite 3 -> 2, Warden 4 -> 3) |
| Performance on the phone (60 fps) | pooled zones, sprites resolved at launch, no `GetComponent` or allocations per frame (test), trails capped by `EliteFx` particle budget, one atlas per file |
| Pause / death domino / world change leave a hazard alive | every zone steps only via `Step(dt)`, registers for `DeathCrash` and world teardown (`WorldLeakTest`) |
| Cosmetic trails or glows break the "no round halo" rule | trails are small puffs from the world's art, `ShotOutline` stays the only outline |
| Sounds become beeps or overlap into noise | material-based synthesis, per-cue voice limit, `AttackAudioTest` (no pure tone, loudness band) |
| Hit box and draw size diverge (draw is 2x) and the player feels "robbed" | the hit radius is unchanged and shown by the keyline hugging the *drawing*; if playtest says shots look bigger than they hit, the draw factor drops to 1.5x (`ReadabilitySweep` decides first) |
| Boss changes ripple into pinned tests (`BossDamageTest`, hearts) | no boss art file is edited; the beams use the new beam atlas instead of `Verdant_shots.png` cells 5-6; hearts and damage untouched |
| Tide's palette and roster are not decided | its row is design-only; phase 7 starts after the Phase-0 answers; the stand-in keeps Ember's skins until then |
| Codex style drift across the four worlds | J12a/c/e lock each world's style from its boss shot atlas; one coordinator audit per batch; fix passes on named files only |
| More attack variety makes the screen busier | `EnemyThreat` weights keep the same shot budget; the three-slot rotation of 5 boss attacks keeps 3 distinct per window |

## 7. What the user sees (the order to show things)

1. This design set plus a contact sheet of the *existing* attacks per world (so the "all pink darts" finding is visible).
2. The first Codex sheets: Frost and Ember shots + fx (opened by the coordinator, critique attached).
3. A short gameplay preview per world after its phase (previews, not a build) and the pink-cue sweep.
4. A phone build only at the release gate (phase 8), after the user says yes to the previews.
