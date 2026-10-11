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
| Backdrop (phase 11) | `BackdropCatalog` Desert spec, `DesertBackdrop.cs` (`DesertTuning`, `DesertDirector`), `DesertAmbient.cs`, `Backdrop3/` (4 tile sets + masks, 14 atlases, `points.json`, worm sheets) | see "Backdrop" below |
| Sounds | `EnemyDeathAudio.DesertAlias` + borrow rows, `AttackAudio` fallback | donors below; NO new audio file, no beep / coin / ambience |

## Backdrop (add-world phase 11)
Real director, no stand-in: `BackdropCatalog.For("Desert")` is its own spec (`Worlds/Desert/Backdrop3/`, layers sky / far / mid / ground (pinned to mid) / flow / palls / mist / wisps / ceiling / gusts / grit, `variantNight` = v4).
- **Ground sets**: v1 day dunes, v2 canyon and ruins, v3 salt flat with solar skeletons, v4 night dunes. `mask.txt` per set (`Art/Worlds/Desert/backdrop_v3~/src~/ground_masks.py`): W dune sand, B built, O hardpan, C cliff. `DesertTuning.Rule` puts ruins / masts / caravans off the cliffs, wells / vents / glass on soft or flat ground, dune ridges on sand.
- **Clusters**: a ruin with a pipe run to a neighbour, a well / vent field with rock and ribs (a haze pall over heat vents), a dune scene (ridge, drift, caravan), lone pieces, pipelines; weights per variant. Pipe runs lie half buried on sand, as a trestle bridge over cliffs.
- **Loops** (19, `DesertAmbientCatalog`): smoke, dust (sand plume, dust devil, grit gust), heat vents and shimmer, glass glints, steam, beacons / strobes / work lamps / windows. Emitters are measured on the pixels (`measure_points.py` -> `points.json`); free loops (dust devil, grit gust, mirage glint) wander the ground.
- **Cloud ceiling**: dust haze (weather banks tinted toward the planetfall's deck, `CeilingTint`), 4 s hold, clear by 14 s, then a light haze; gusts of grit.
- **Brightness**: the sand is paler than the other worlds, so `VariantBrightness` {1, .93, .95, .8, .85} draws the sets darker until the brightest enemy tone (sun-white) reads >= 7:1 against the rendered lane in every set. `WorldBackdropTest` holds Desert's tile caps (sky .26, tiles .31 relative luminance as drawn) and a 12 MB ASTC budget (measured ~10.7 MB, the two 2048 worm sheets ~3.7 MB of it: `WorldBackdropImport.IsDesertWormSheet`).
- **Elite sites** (`LandingKind.DesertWormBurrow / CrawlerBay / GlassPad / BuriedSilo / RuinDock`; `launchFrom` wormburrow / caravanbay / glasspad / buriedsilo / ruindock): Dune Stalker -> worm burrow, Caravan Hauler -> crawler bay, Lens Matriarch -> glass pad, Glass Cannon -> buried silo, Scorpion Walker and Mirror Bastion -> ruin dock. Shut until the launch tell opens them. The art's `lights_off/on` cells (a lit pylon pad) are not used.
- **Menu**: Desert's four sets join the menu pool only once `HighestWorld` reaches world 6 (a developer unlock or the release switch); a normal player's progress never offers it while `DesertEnabled` is off.

### The giant sand worms (`DesertDirector`, atlases `worm`, `worm_giant`)
A surface ridge trail crawls across the dune sand (ridge loop, moving along the ground, never mirrored: the art is lit from the upper left); then the worm erupts at that spot (emerge, a held arc, a dive, each in a spray), and the ridge resumes and fades. Small (cell 1.25 u), medium (2.1), giant (4.2, rare: at most once a minute and not before 25 s). One at a time, none in the first 12 s, calm gaps of 9-18 s, none while a boss fight runs (`BossEncounter.Running`), pinned to the mid tile (ground rate), drawn behind every piece, wanderer and gameplay, alpha capped (body .85, spray .7). `DesertTuning.WormsEnabled` / `RidgeOnly` are the kill switches (RidgeOnly = the low-quality trail without the eruption). The art's pointed first-pass frames (`emerge_00`, `dive_04`, the `cascade` loop read as a leaf / feather) are not shown (`UseFeatherFrames`): the eruption starts on frame 1 and the spray covers the hand-off.

**Worms and pinned items** (user, 2026-10-11): a worm's ridge trail and eruption keep clear of every pinned item (`PathClear`: the whole planned life against the planner's claimed footprints, margin `WormMargin` .14). An item that scrolls into the trail later is DESTROYED by the ridge (`WormContacts`, at most `MaxRazed` 3 per worm): its loops stop, it fades out over ~1 s (no wrecked art exists) under three small, hazy, dust-tinted blasts (the weapon explosion's Rock row scaled to .45-.8 u at alpha .5, no pink) and a sand plume, with a quiet rumble (`frost_rock_chunk` at .2); it stays gone until the tile re-seeds. A launch site is never razed (an elite may be docked in it): the worm dives instead. An eruption whose spot a late piece has taken is called off (`EruptionsSkipped`). Backdrop only: no collider, nothing in gameplay changes; every roll uses the director's own stream, so a seed replays identically (`RazedHash`). Held by `DesertBackdropTest.PinnedItems`.

Art weaknesses (from the art agent, seen in game): the ridge mound reads as a flat sunlit / indigo-shadow rock rather than a travelling swell, with almost no wake; `emerge_00` / `dive_04` / `cascade` are the feather; the giant's arc is a big flat brown body (fine at distance); the worm passes under landmarks (drawn behind them) when its trail was clear at spawn but a piece appeared later (then it razes it).

## Readability border (thin pale bone, never a repaint)
`Art/Enemies/src~/desert_enemies/add_border_big.py` (originals kept in `original_big/`): the Dune-Eater atlas, its damage hull and death strip, and the three darkest
elites (Dune Stalker, Caravan Hauler, Scorpion Walker). Offline median-luminance contrast against the four Desert mid tiles was only ~1.0-1.4:1 for them.
`ReadabilitySweep` over the real Desert tiles (`READABILITY_WORLDS=Desert`): WEAK enemies were `desert_big` (Sandcrawler, canyon floor) and `desert_mine`; the Sandcrawler now has the thin bone border too (`add_border.py`, originals in `strips/original/`); the mine is rail-mounted (over the rail art, not the ground) and was flagged at one placement only, left as is. No enemy is LOW. Weak shots (gunship Slag/Glob/Shard/Orb/Shell, the Wisp, Gunner and Hawk bolts, elite shards, the hauler's slag) are listed in the sweep's worst.csv; shots are not repainted.

## Sound donors (docs only; `DesertSoundTest` pins them)
rocks: frost_rock_chunk .85 / frost_rock_shard 1.0 / ember_rock_obsidian .9 / ember_rock_islet .88; mine ember_mine .9; Sandcrawler ember_big .85; Skimmer space_fighter_1 .95;
Hawk frost_fighter_2 1.15; Gunner frost_fighter_3 .9; Tank ember_fighter_4 .8; Scarab verdant_chaser 1.1; Wisp ember_alien 1.2; elites: coalrunner, singularity hauler,
ash wraith, cauterizer, kilnback, eventide bastion; boss boss_space .8. Screams: Skimmer ember_fighter_1 1.3, Hawk verdant_fighter_2 1.15, Gunner ember_fighter_3, Scarab
verdant_fighter_3 1.3, Wisp ember_alien 1.45, elites as listed in `BorrowTable`. Attacks: Desert strike / gust / hail play Storm's (strike: Tide's thunder).

## Still standing in for Desert
- (Backdrop, worms, masks, launch sites and `launchFrom` are done: see Backdrop above.)
- Rails: Storm's (`WorldPainter.RailArtWorld`). Music: none (the user supplies it).
- Attack art: no Desert atlases (shots clamp to Tide's skins, hazards use the procedural Desert ramp); no sand-puff effect for the Scarab's dive.
- Glass Cannon elite art + death strip (recolour of the Storm Anvil Cloud, points are the donor's).
- Elite muzzles are Codex's approximate points snapped onto the hull: check at game scale.
- Authored sand / grit / glass sound layers (plan section 8): none yet, donors only.
