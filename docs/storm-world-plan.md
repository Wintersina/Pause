# World 6 "Storm" (gas giant): build-out plan

Branch `feature/storm-world` (worktree `.claude/worktrees/storm-world`). Playbook: `.claude/skills/add-world/SKILL.md`,
brief `.claude/skills/add-world/world-5-6-briefs.md` (World 6 section), model = the Tide implementation.
World order: Space -> Frost -> Verdant -> Ember -> Tide -> **Storm** -> Desert -> Space. Release switch `StormEnabled`
(WorldManager-style) stays **false**; Google Play v1.0 ships 4 worlds. No switch is flipped by this work. Not merged to master.

## Scope of this run
Roster (12 units incl. the rod mine), boss, 5 elites, rails and mine atlas, themed attacks in Storm's own material, death
sounds. Backdrop (4 sets, pieces, loops), planetfall wiring (art already merged) and the owner steps (leaderboards,
Codex chips, desert) come afterwards. Sounds for backdrop/ambience/music are the user's.

## Palette (avoid collisions)
Slate/graphite base hue 212-230 sat <= .30; lightning storm-white #F2F6FF with pale-yellow edge #FFF3A0 (hue 55-62, >= 18
degrees from amber dust 37); cold white-blue arc hue ~205 (sat <= .5, clear of cyan pickup 178); muted teal-grey 195 (sat
<= .25). Never violet/magenta 250-300 (pickup 259, Space), mint (Tide), orange/amber glow (Ember), lime, red.
Hostile attacks keep the pink core cue (hue 312-326 core/fringe) with Storm material around it (lightning, arcs, hail, gusts).

## Roster (12)
| key | name | role / behaviour (brief) |
| --- | --- | --- |
| storm_rock_hail | Hailstone | pitted ball, Drift |
| storm_rock_ore | Ore Shard | crystal blade, Glide slice |
| storm_rock_slag | Anvil Chunk | dark iron chunk, Sink + spin |
| storm_rock_islet | Cloud Islet (floating) | platform fragment + antenna, upright Sway/Bob |
| storm_mine | Rod Mine | rail mine, second-style 3rd atlas `rail_mines_neon_storm.png`, Laser x2 + arcs |
| storm_big | Cloud Whale | holds a column; gills open then fan of 3 arc shots |
| storm_fighter_1 | Stormray Pup | Drift + straight Lunge (wing-flap gust dash) |
| storm_fighter_2 | Stormray | Track + aimed arc bolt |
| storm_fighter_3 | Thunderhead | Brake hover, 0.9 s charge flare, cloud-seed shell (Lob + Strike) |
| storm_fighter_4 | Tempest Anvil | Brake + slow Track, ring of 8 (chain lightning) |
| storm_chaser | Cyclone Drone | rotor drone, Spiral hail pellets (needs a chaser style; Tide added `Slither`) |
| storm_alien | Static Mote | ball lightning in a cage, Pulse + Sway, arcing spit |

## Boss: TEMPEST ENGINE, "STORM-MAKING TURBINE"
Floating turbine engine. Parts: 1 core drum (arc fan), 2 lightning-rod crown (lane strike, crown flares first), 3 twin coil
towers (coil beams). Signature chain-lightning sweep (`Wave`). Flash white-yellow, heart pale yellow.
STATUS (branch feature/storm-boss-wire): installed. Storm.png / Storm_shots.png / Storm_card.png live in
`Art/Resources/Bosses/` (staged originals stay in `Art/Bosses/Storm~/`); BossCatalog entry (artKey "Storm", 20-cell base atlas,
not expanded); emitters Core / Rods / CoilL / CoilR measured by `measure_emitters.py` into BossEmitterTable row 5; BossHearts
ellipse, BossAttackFx row 5 (storm-white / pale yellow / slate), codex part words. Default table: core arc fan (Fan, tell 0),
crown hail (Lob, tell 1), coil beams (Beam, tell 2). Themed table (BossThemed, off until Storm is live): + crown strike (Strike,
tell 1, phase 1) and chain-lightning sweep (Wave Scan band, tell 0, phase 3). PENDING: Storm_damage / _damage_fx / _death art
(art/storm-boss-damage); until delivered `BossCatalog.StormDamageKey` / `StormDeathKey` = "Tide" (one-line swap to "Storm";
BossEncounterTest / BossDamageTest require the swap as soon as the files exist). The 35-cell expanded combat atlas is optional later.

## Elites (5)
Skyhook Harrier (interceptor, hook lance), Refinery Tender (hauler, sinking canisters), Rod Dancer (skirmisher, blink + arc fan),
Anvil Cloud (siege, lane-telegraphed lightning shell), Static Bastion (tank, `ward_curtain` of arcs). Vortex Reaver from the
brief is dropped for this pass (5 as agreed); can be added later.

## Ordered job list (Codex, one worktree + branch each, art/storm-<piece>, `.claude/worktrees/codex-storm-<piece>`)
Prompts: `docs/storm-codex-prompts/` (`build.sh <job>` prints common preamble + job). Max 2 Codex jobs machine-wide.
1. `storm-enemies`  11 idle strips (7 cells; 1344x192, big 1792x256) -> staged `Art/Enemies/src~/storm_enemies/`
2. `storm-death-mine`  11 death strips + mine atlas (1254x314, 4 frames) + mine death strip
3. `storm-rails`  725x2170 seamless rail -> `Resources/Worlds/Storm/rail_storm_wide_v1.png`
4. `storm-boss`  Storm.png 1920x1536, shots 1024x128, card 1024x288 (staged `Art/Bosses/Storm~/`)
5. `storm-boss-damage`  damage 768x1536, fx 2304x768, death 2304x384 (staged `Art/Bosses/StormDamage~/`)
6. `storm-elites`  5 flight strips + concepts + death strips + manifest
7. `storm-sounds`  17 keys x3 death cues, screams for pilots/elites (no beeps/coins, no ambience)
Optional later: boss expanded combat atlas (35 cells, as Tide), themed attack art (docs/world-attacks-codex-prompts.md J12).

## Code/test wiring (Claude, per landed art; each tested in a Unity batch run)
Scaffold (Phase 9: Worlds[5], StormEnabled false, loop rewiring), roster defs + behaviours + palette + mine third atlas +
explosion material, themed attacks (Storm material, pink cue), BossDef + BossCatalog + damage/death keys, elites via
`add-elite-ship`, rails (WorldPainter, RailBounds, RailMatchTest), audio keys (`EnemyDeathAudioTest`), Codex entries (hidden
until live), tests: RosterTest, EnemyBehaviourTest, EliteTest, boss tests, ReadabilitySweep, AttackBudgetTest, WorldLeakTest.
