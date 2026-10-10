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

## 8. Deviations from this plan, and what phases 0a and 0b built (log)

Branch `feature/attacks-foundations` (worktree `attacks-found`), phases 0a + 0b. Later phases append here.

### 8.1 Built

* **0a** `Editor/Tests/DodgeBot.cs` (the bot), `AttackBudgetScenarios.cs` (one fixture per roster attacker, rail mine, elite and boss attack, driven by the real
  `EnemyBrain`/`EnemyVolley`, `RailMineLaser`, `EliteShip`, `BossEncounter`), `AttackBudgetTest.cs` (pins, `WithinBudget`, `Themed` table, `Sweep`).
* **0b** `Gameplay/Enemies/ShotSkins.cs` (`ShotSkin`, `ShotMotion`, `ShotSkins`), `AttackArt.cs`, `Attacks/HostileZone.cs` (`IHostileZone`), `Attacks/AttackShape.cs`,
  `Attacks/AttackPreview.cs`, `Attacks/AttackPools.cs`; `HostileShots` zone registry; `EliteShot.Launch` reads the skin; `EnemyBehaviour.ShotStyle` carries the shooter's world;
  `.claude/skills/add-world/scripts/verify_attack_art.py`; `ShotSkinTest`. Behaviour is unchanged: every skin is today's procedural shot until a phase calls `ShotSkins.Enable(world)`.

### 8.2 Deviations

1. **Rolls.** 2000 seeded rolls per attack, as planned, but `AttackBudgetTest.Execute` re-measures only five pinned attacks (about a minute); the full 52-attack sweep
   (`scripts/unity-batch.sh -executeMethod AttackBudgetTest.Sweep [-rolls N] [-only substring] [-trace]`) takes about 45 minutes of editor time. A spot check passes within +-1.5 points (a few rolls differ run to run: one brain's drift direction).
2. **The budget has an absolute slack.** `rate(new) <= rate(old) * 1.15 + 0.02`. Most single-shooter baselines are 0-5%, where "1.15 x" alone is meaningless noise; `AttackedShare >= 50%` is also required (an attack that barely shows is broken, not easy).
3. **The bot is not a perfect dodger** (a perfect one scores 0% on everything and pins nothing): it sees the world .25 s late, misjudges each hazard by up to .12 u, takes .175 s to reach 7 u/s, sidesteps 1.2 u when it sees a windup begin
   (before it knows the line; "moving after the tell starts always dodges"), and then plans the nearest clear path over 1.5 s. It knows telegraphs (a laser's aim line, a lunge's tell, a boss beam's scanned arc and sweep, a lob's landing spot).
   A ghost ship that never moves is scored in the same rolls (information only).
4. **What is not measured.** Chasers (no attack object, contact only); rocks; the elites' own tell lines (Rift Lancer's sight line, Orbit Reaver's circle): their rates are high (up to 90-97%) and only mean "no worse than today" when an attack is re-themed in place;
   an elite is rolled for ONE attack (its second is held back); a boss attack for up to three of its own starts.
5. **`ShotSkins.Enable(world)` gate** (not in the plan): art dropped into `Resources/Attacks/<World>/` does nothing until the world's phase enables it, so no skin switches on by accident. Enabling a world whose art covers only some kinds keeps the others procedural.
6. **Landed pools** use the world's `pool` cells (`ShotSkins.PoolSprite`); a lobbed glob in the air wears the world's `slag` cells (the art list's pool cell is the landed look).
7. **Files added** that the plan did not list: `AttackShape.cs` (one footprint object = hitbox + preview outline, FR2), `AttackBudgetScenarios.cs`, `DodgeBot.cs`. Previews are pooled `SpriteRenderer` dots (no `LineRenderer`, no shader dependency).
8. **Zones sit next to beams in `HostileShots`**, not instead of them: `BossBeam` was not converted to `IHostileZone` (untouched behaviour, untouched tests). A later phase may fold it in.
9. **Read-only accessors** added to `BossBeam` (`TellLeft`, `HoldLeft`, `HoldTotal`, `SweepDeg`, `StartDeg`) for the bot; no behaviour change.
10. **Gotchas for later phases.** `TestHarness.Sandbox` snapshots every static readonly collection of the game: a static 2-D array makes it throw `RankException` (use a flat array). Sprites made at run time can be unloaded with a scene: never cache one in a skin without a `== null` re-check (done in `ShotSkins`).
11. **Tide.** `ShotSkins` has five worlds (Tide is index 4); `EnemyRoster.WorldKeys` still has four. Tide's rows wait for its roster (phase 7).
12. `EliteSystem.Clear()` now also calls `AttackPools.ClearAll()` (ends every pooled hazard and preview): the one line in an existing teardown path.

### 8.2b Phases 1c + 1d: the AttackBlast and AttackStrike cores (branch `feature/attacks-cores-blast-strike`, worktree `attacks-cores-b`)

Built (all under `Gameplay/Enemies/Attacks/` unless noted):

* `AttackHazard.cs` -- the shared body of every themed area hazard (a jet, a band and a lash derive from it too): Off -> Tell -> Live -> After -> pool, `IHostileZone`, one trigger
  `PolygonCollider2D` tagged "Enimey" (child `AttackHazardHit`, marker `AttackHazardHitbox`) refilled from the live `AttackShape` every frame, friendly fire once per pulse, `AttackHazard.LiveThreat`
  (blast 2, strike 1 per FR7), `IsHitbox / EraseHitbox / BlinkStrike` for the ship's code. **Steps only through `Step(dt)`** (`AttackPools.StepAll`, called from `EliteSystem.Step`).
* `AttackBlast.cs` + `BlastSpec` -- the expanding ring. `BlastSpec.Standard(world)` = the doc's cold blast (18-bar circle, 70 deg crack, 2.6 u/s from .9 u to 3.4 u, bar half .09);
  `BlastSpec.Wide(world)` = 32 bars to 6 u at 2.7 u/s for a shooter that hovers 5-6 u above the ship. `AttackBlast.Arm(spec, muzzle, target, tell, shooter)`; `.Follow(transform, offset)` rides the
  shooter until it ignites; `.Ignite()` is the brain's Release (a hazard also ignites itself when its tell is up).
* `AttackStrike.cs` + `StrikeSpec` + `StrikeLanes` -- lane glyph, translucent footprint band, column (.36 wide, <= .25 s), ground burst. `StrikeSpec.Standard(world)` picks the style
  (`Icicle` Frost, `Eruption` Ember with a 4.2 u geyser, `Thunder` for the rest); `StrikeLanes.Pick(pilotX, count, spacing, railEdge, hitHalf, buffer)` spreads a pattern.
* `AttackHazardArt.cs` -- procedural pixel art for both (ring bar, gap marker, lane glyph, column tile, spear tip, ground burst), pink keyline / hot core / material ramp per world
  (`RampOf`), a near-black bold ring when `ShotOutline.UseBold`; every look-up tries the `AttackArt` slot first (`AttackArt.RingBar / RingGlyph / RingGapMarker`, `StrikeBody / StrikeGlyph / StrikeBurst`:
  `frost_attack_ring.png` P6, `<w>_attack_strike.png` P3) and caches by an int key, so nothing allocates per take or per frame.
* `EnemyAttack.Blast` / `EnemyAttack.Strike` + builders `EnemyBehaviour.Blast(BlastSpec)` / `.Blast(bars, reach, speed, gapDeg)` / `.Strike(StrikeSpec, lanes, spacing)`; `EnemyBrain` arms the hazard(s)
  with the windup (`EnemyBrain.TellFor`: >= .7 s for an area hazard), ignites them at Release, cancels them if the enemy dies in its tell; `EnemyBehaviour.ThreatCount` reserves the shot budget
  (blast 2, strike one a lane); `EnemyThreat.LiveShots` counts live hazards. **No world's table uses either yet.**
* Tests: `AttackHazardTest` (geometry, life, collider == shape, heart / shield / blink / destroyed hitbox, friendly fire once a pulse, burns shots, pause, pools + zero allocation, cleanup),
  `AttackFairnessTest` (FR1 tells through the real brain, FR3, FR4 corridors, FR7, pixel pink cue, bold keyline on a bright world, hit box vs drawing, art slots, dodge-bot rows),
  `AttackBudgetTest` gains three `themed:` rows; tool `Editor/Tools/Previews/AttackHazardPreview.cs` (`ATTACKHAZ_PREVIEW_DIR`).

Deviations from the plan and decisions a later phase should know:

1. **A shared `AttackHazard` base** (not in the plan's file list) so the next cores (jet, band, lash) are ~150 lines each; it is where the "ship's code" hooks live.
   Touched shared files, one line each: `collisionDetection` (a themed hitbox is not spent on the hull), `EliteShip.ShieldRam / TeleportStrike`, `RamKill.NotAHazardBody`, `DeathCrash.Classify`,
   `EliteSystem.Step` (`AttackPools.StepAll`), `Planetfall.ClearBoard` and `EliteDirector.Update` (world change / player death clear the hazards), `EnemyBrain` (windup / release / threat), `EnemyThreat.LiveShots`.
   Expect trivial merge conflicts with phases 1a / 1b / 1e in those lines.
2. **Pool plumbing.** `IAttackPool.Step`, `IAttackStep`, `IAttackReleased` (the pool tells a taken-back item; Unity runs `OnDisable` only in play mode and the suites run in edit mode, so nothing relies on it),
   `AttackPools.StepAll / Find`, `AttackShape.PolyLength / PolyAt`. `AttackPreview.MaxDots` 420 -> 720 (a ring is ~150 dots, a column to the top of the view ~95).
3. **The standard 3.4 u ring cannot touch a ship 5-6 u below a hovering Golem.** The first fixture measured 0% for the bot AND for a ghost that never moves (the attack was a no-op). Hence `BlastSpec.Wide`
   (32 bars to 6 u so the wall stays closed: a test asserts no ship-wide hole outside the crack at any radius) and the fixture uses it; the Golem's phase must pick Wide, or a lower `stationDepth`.
4. **`gapOffsetDeg`**: a crack aimed straight at the pilot makes standing still safe. The spec turns it off the pilot by this many degrees toward the lane's middle; the fixture uses 40 (5 deg past the 35 deg
   half-crack). `AimGap` also turns the crack (at most 40 deg) until the free chord at the pilot's range, clipped by the rails, is >= 1.65 u, so a pilot hugging a rail still has a corridor.
5. **Fixtures.** `AttackBudgetScenarios.ThemedFixtures` (ids `themed:frost_cold_blast` -> `roster:frost_big`, `themed:ember_eruption` -> `roster:ember_fighter_3`, `themed:frost_icicle_drop` -> `roster:frost_fighter_2`)
   run a real brain on a behaviour injected through `EnemyBehaviours.TestOverride` (cleared by `ClearOverrides`); a per-world phase adds its own row there and in `AttackBudgetTest.Themed`, never edits the cores.
   The bot sees a ring's bars and a column from the first frame of the tell (the preview shows them), with their known expansion / `liveIn`.
6. **Strike geometry choices**: the column spans from just under the impact point (the pilot's y at the tell) up out of the view (eruption: a finite geyser); below the impact point is safe. The glyph and the ground ride the
   board with `ride` (1 for a hazard, 0 for a pilot). One strike is one lane; a pattern is several armed in the same tell (`EnemyBehaviour.Strike(spec, lanes)`; one armed strike per lane, 4 at most per brain).
7. **Art slots, not art**: the ring reads `frost_attack_ring.png` cells 0-3 (bar, two flicker pairs), 4-5 (gapMarker), 6-7 (glyph); the strike reads `<w>_attack_strike.png` row 0 (column, 24 fps), row 1 from y 384 (glyph a,b, burst x3).
   Art cells carry their own stroke; only the procedural fallback has the bold ring (`ShotOutline.For` is not applied to stretched strips).
8. **Not done here (later phases)**: `BossAttackKind.Blast / Strike` and the boss executors (1g), elite attack ids, sounds (1h), the `ReadabilitySweep` and `AtomClarityTest` items for the new strips,
   the per-world rows. `BossBeam` is still not an `IHostileZone`.

### 8.3 Baseline: today's attacks against the dodge bot (2000 rolls, seed 1)

Pinned in `AttackBudgetTest.Pinned`. "bot" = rolls in which the bot was touched; "standing" = the ghost that never moves in the same rolls.

| attack | bot hit rate | standing ghost | shown in | table shape |
| --- | --- | --- | --- | --- |
| `roster:space_mine` | 1.3% (26/2000) | 59.7% | 2000/2000 | Laser x1 volleys 2 cooldown 1.0 tell 0.9 |
| `roster:space_big` | 2.8% (55/2000) | 26.1% | 2000/2000 | Shot x2 volleys 3 cooldown 2.6 tell 0.8 |
| `roster:space_fighter_1` | 0.0% (0/2000) | 0.0% | 2000/2000 | Lunge x1 volleys 0 cooldown 2.0 tell 0.45 |
| `roster:space_fighter_2` | 0.0% (0/2000) | 0.0% | 2000/2000 | Lunge x1 volleys 1 cooldown 1.6 tell 0.55 |
| `roster:space_fighter_3` | 4.2% (83/2000) | 49.4% | 2000/2000 | Shot x2 volleys 3 cooldown 2.2 tell 0.6 |
| `roster:space_fighter_4` | 0.0% (0/2000) | 100.0% | 2000/2000 | Shot x1 volleys 4 cooldown 2.4 tell 0.9 |
| `roster:space_alien` | 0.6% (11/2000) | 15.7% | 2000/2000 | Shot x1 volleys 2 cooldown 3.5 tell 0.55 |
| `roster:frost_mine` | 0.9% (18/2000) | 59.9% | 2000/2000 | Laser x1 volleys 2 cooldown 1.0 tell 0.9 |
| `roster:frost_big` | 4.2% (83/2000) | 54.3% | 2000/2000 | Shot x3 volleys 3 cooldown 3.2 tell 0.9 |
| `roster:frost_fighter_1` | 0.0% (0/2000) | 0.0% | 2000/2000 | Lunge x1 volleys 0 cooldown 2.0 tell 0.55 |
| `roster:frost_fighter_2` | 0.0% (0/2000) | 99.9% | 2000/2000 | Shot x1 volleys 3 cooldown 2.0 tell 0.55 |
| `roster:frost_fighter_3` | 6.9% (138/2000) | 45.6% | 2000/2000 | Shot x2 volleys 3 cooldown 2.2 tell 0.55 |
| `roster:frost_fighter_4` | 2.5% (50/2000) | 27.4% | 2000/2000 | Shot x5 volleys 3 cooldown 3.4 tell 1.0 |
| `roster:verdant_mine` | 1.5% (29/2000) | 59.8% | 2000/2000 | Laser x1 volleys 2 cooldown 1.0 tell 1.0 |
| `roster:verdant_big` | 0.0% (0/2000) | 0.0% | 2000/2000 | Lob x1 volleys 3 cooldown 3.5 tell 0.9 |
| `roster:verdant_fighter_2` | 0.0% (0/2000) | 0.0% | 2000/2000 | Lunge x1 volleys 1 cooldown 2.0 tell 0.5 |
| `roster:verdant_fighter_3` | 0.0% (0/2000) | 0.0% | 2000/2000 | Lunge x1 volleys 3 cooldown 1.3 tell 0.6 |
| `roster:verdant_fighter_4` | 9.2% (184/2000) | 100.0% | 2000/2000 | Shot x3 volleys 4 cooldown 2.6 tell 0.8 |
| `roster:ember_mine` | 3.0% (60/2000) | 59.9% | 2000/2000 | Laser x1 volleys 2 cooldown 1.0 tell 1.1 |
| `roster:ember_big` | 4.1% (82/2000) | 38.5% | 2000/2000 | Shot x2 volleys 3 cooldown 3.6 tell 1.0 |
| `roster:ember_fighter_1` | 0.0% (0/2000) | 0.0% | 2000/2000 | Lunge x1 volleys 0 cooldown 2.0 tell 0.45 |
| `roster:ember_fighter_2` | 1.0% (19/2000) | 42.0% | 2000/2000 | Shot x1 volleys 4 cooldown 1.4 tell 0.5 |
| `roster:ember_fighter_3` | 0.7% (14/2000) | 99.5% | 2000/2000 | Shot x1 volleys 3 cooldown 1.8 tell 0.5 |
| `roster:ember_fighter_4` | 0.3% (6/2000) | 8.7% | 2000/2000 | Ring x8 volleys 3 cooldown 3.8 tell 1.1 |
| `elite:ember_elite_ash_wraith` | 97.0% (1940/2000) | 3.2% | 2000/2000 |  |
| `elite:ember_elite_brass_vulture` | 66.4% (1098/1654) | 66.4% | 1654/2000 |  |
| `elite:ember_elite_cauterizer` | 0.0% (0/2000) | 35.3% | 2000/2000 |  |
| `elite:ember_elite_coalrunner` | 96.9% (1938/2000) | 99.6% | 2000/2000 |  |
| `elite:ember_elite_kilnback` | 0.1% (2/2000) | 60.0% | 2000/2000 |  |
| `elite:ember_elite_sunstoke` | 88.5% (1542/1743) | 88.5% | 1743/2000 |  |
| `elite:frost_elite_cryo_siren` | 4.9% (98/2000) | 42.6% | 2000/2000 |  |
| `elite:frost_elite_floe_harrower` | 77.5% (1550/2000) | 100.0% | 2000/2000 |  |
| `elite:frost_elite_glacier_tender` | 0.0% (0/2000) | 0.0% | 2000/2000 |  |
| `elite:frost_elite_rimebreaker` | 83.3% (1665/2000) | 100.0% | 2000/2000 |  |
| `elite:frost_elite_whiteout_sentinel` | 0.0% (0/2000) | 0.0% | 2000/2000 |  |
| `elite:space_elite_eventide_bastion` | 10.1% (202/2000) | 100.0% | 2000/2000 |  |
| `elite:space_elite_orbit_reaver` | 0.0% (0/2000) | 100.0% | 2000/2000 |  |
| `elite:space_elite_rift_lancer` | 90.1% (1802/2000) | 0.0% | 2000/2000 |  |
| `elite:space_elite_singularity_hauler` | 85.9% (1717/2000) | 49.5% | 2000/2000 |  |
| `elite:verdant_elite_resin_warden` | 1.0% (20/2000) | 25.8% | 2000/2000 |  |
| `boss:Space:chin cannon` | 4.9% (97/2000) | 2.8% | 2000/2000 |  |
| `boss:Space:core burst` | 31.9% (638/2000) | 100.0% | 2000/2000 |  |
| `boss:Space:pod lasers` | 5.7% (113/2000) | 93.7% | 2000/2000 |  |
| `boss:Frost:icicle spray` | 74.1% (1481/2000) | 100.0% | 2000/2000 |  |
| `boss:Frost:glare beams` | 37.6% (751/2000) | 100.0% | 2000/2000 |  |
| `boss:Frost:blowhole hail` | 22.0% (440/2000) | 98.3% | 2000/2000 |  |
| `boss:Verdant:stinger thorns` | 1.6% (31/2000) | 9.5% | 2000/2000 |  |
| `boss:Verdant:spore bloom` | 8.3% (166/2000) | 87.7% | 2000/2000 |  |
| `boss:Verdant:acid cannons` | 23.3% (465/2000) | 100.0% | 2000/2000 |  |
| `boss:Ember:fire breath` | 24.9% (498/2000) | 100.0% | 2000/2000 |  |
| `boss:Ember:furnace slugs` | 58.1% (1161/2000) | 100.0% | 2000/2000 |  |
| `boss:Ember:brow laser` | 59.8% (1195/2000) | 86.1% | 2000/2000 |  |

