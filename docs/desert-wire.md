# Desert (World 7) wiring: phases 9-14, status and what still stands in

Branch `feature/desert-wire` (jj workspace `.claude/worktrees/desert-wire-jj`), on master 2ac0a577 plus `feature/desert-world`, `art/desert-*`.
`WorldManager.DesertEnabled` is **false** (chained: `DesertLive = StormLive && DesertEnabled`, so Tide AND Storm must be on first); the world exists in
`Worlds[6]`, the developer picker (F7) and tests can start a run on it. All runtime art is under `Resources` (planetfall under
`Backgrounds/Resources/Worlds/Desert/Planetfall/`, enemies, mine atlas, boss, elites). Nothing is merged to master.

## Wired
| Piece | Where | Notes |
| --- | --- | --- |
| Theme, chain, flag | `WorldManager` | ramp .00425 / scale 1.80, pale bone-gold portal colour, `DesertLive`, `LastLiveWorld` chain |
| Planetfall + lift-off | `PlanetfallDef.Desert`, `LiftoffDef.Desert` | R1250 limb: `limbApexPx` 355, `limbEdgePx` 887; disc (553, 544.5) r 387 |
| Codex, dev picker, sections | `CodexCatalogue`, `DeveloperUnlocks` (F7), `CodexPanel.MaxSections` 8 | |
| Roster (12) | `EnemyRoster` (keys `desert_*`), `EnemyBehaviours`, `EnemyPalette` (6 new tokens in `palette.env` / `common.py`), `EnemyDensity` (5.0 / 4.0) | explosion = Rock (no Dust/Grit row: plan question 4) |
| Strips | `Resources/Enemies/desert_*.png` (11), `Death/desert_*.png` (12) | the Scarab keeps the thin pale border (`add_border.py`) |
| Mirage Mine | `rail_mines_neon_desert.png`, `RailMineArt.DesertWorld` | pivot (215, 158) in every 313/314 px panel, core offset (0, 0); clamp reach .409 u (`ClampReachFor`, `MountX(inner, world)`) |
| Scarab Drone | `ChaserStyle.Burrow` | run, dive (hidden, collider off), resurface beside the pilot, lunge |
| Themed attacks | section 10 of the plan | Sandcrawler shard fan, Hawk glass triple, Gunner heat-lance, Tank `Blast` ring (Desert ramp, Eruption strike style), Wisp bolt |
| Boss: Dune-Eater | `BossCatalog` (artKey `Desert`), `BossEmitterTable` (Maw / Boom / Lens, from the manifest), hearts ellipse 1.66 x 1.66, warning accent, attack-fx row 6, lore | atlas, shots, card, damage stages, damage fx, death strip all installed; themed table = burrower strike + sandstorm wall (not fighting themed, like Storm) |
| Elites (6) | `Resources/Elites/Defs/desert_elite_*.json`, `Resources/Elites/Desert/` | five from Codex (art/desert-elites), muzzles snapped on the hover cell; Glass Cannon is a stand-in |
| Sounds | `EnemyDeathAudio.DesertAlias` + borrow rows, `AttackAudio` fallback | donors below; NO new audio file, no beep / coin / ambience |

## Readability border (thin pale bone, never a repaint)
`Art/Enemies/src~/desert_enemies/add_border_big.py` (originals kept in `original_big/`): the Dune-Eater atlas, its damage hull and death strip, and the three darkest
elites (Dune Stalker, Caravan Hauler, Scorpion Walker). Offline median-luminance contrast against the four Desert mid tiles was only ~1.0-1.4:1 for them.
`ReadabilitySweep` cannot measure Desert yet (its backdrop is Tide's stand-in): re-run it, and the border decision, when the Desert director lands.

## Sound donors (docs only; `DesertSoundTest` pins them)
rocks: frost_rock_chunk .85 / frost_rock_shard 1.0 / ember_rock_obsidian .9 / ember_rock_islet .88; mine ember_mine .9; Sandcrawler ember_big .85; Skimmer space_fighter_1 .95;
Hawk frost_fighter_2 1.15; Gunner frost_fighter_3 .9; Tank ember_fighter_4 .8; Scarab verdant_chaser 1.1; Wisp ember_alien 1.2; elites: coalrunner, singularity hauler,
ash wraith, cauterizer, kilnback, eventide bastion; boss boss_space .8. Screams: Skimmer ember_fighter_1 1.3, Hawk verdant_fighter_2 1.15, Gunner ember_fighter_3, Scarab
verdant_fighter_3 1.3, Wisp ember_alien 1.45, elites as listed in `BorrowTable`. Attacks: Desert strike / gust / hail play Storm's (strike: Tide's thunder).

## Still standing in for Desert
- Backdrop: Tide's director and spec (`BackdropCatalog.DesertStandIn`, `BackdropSet`); tiles `backdrop_v3~` A/B/C are staged, not installed or specced.
- Worm ambient (atlas `worm~`, not installed), landmark/pipe/ground masks, `LandingKind`s and launch sites for the elites (no `launchFrom`: any pad of the stand-in).
- Rails: Storm's (`WorldPainter.RailArtWorld`). Music: none (the user supplies it).
- Attack art: no Desert atlases (shots clamp to Tide's skins, hazards use the procedural Desert ramp); no sand-puff effect for the Scarab's dive.
- Glass Cannon elite art + death strip (recolour of the Storm Anvil Cloud, points are the donor's).
- Elite muzzles are Codex's approximate points snapped onto the hull: check at game scale.
- Authored sand / grit / glass sound layers (plan section 8): none yet, donors only.
