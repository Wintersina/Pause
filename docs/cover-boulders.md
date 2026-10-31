# Cover boulders (Space) and backdrop landforms (every other world)

> **Update (feat/boulders-decorative-only): boulders are decorative only, in every world.** Space's boulder is now a backdrop landform like the others. Everything below that describes
> blocking (the ship, shots, beams, shockwaves and shoves, rail-mine lasers, elite landing sites, the hazard seam), the lane/free-reach rules, the destruction sequence and the
> boulder's own drift is removed. A boulder is pinned to the backdrop's ground, drawn ON the ground plane (`BackdropCatalog.Spec.BoulderOrder`: just above the world's `mid` ground tile, Space: above `planet_near`), under every other backdrop layer and all gameplay, and just fades out when a boss arrives, the world changes or the player dies.


**Two kinds, one flag (`CoverBoulderEntry.interactive`, only Space is true).** Space's asteroids are interactive COVER boulders exactly as in sections 1-11 (you hide
behind them). Every other world's boulder is a decorative backdrop LANDFORM seen from far above the ship (user, 2026-10-14: 'the ship will be much higher, it doesn't
make sense for these to stop you'): it is drawn, drifts and emerges / recedes with the backdrop haze, and affects NOTHING: not the ship, shots, beams, hazards, bodies,
shoves, landing sites or the hazard seam (`CoverBoulders.Alive` / `All` / `Any` / `Solid` count interactive boulders only; `AliveAny` is the pool bookkeeping). No
boss-arrival crumble: when a boss starts a landform just sinks out (1.8 s) and none spawn during the fight. Landforms are bigger (x`DecorScale` 1.5, at most 98 % of
the rail-to-rail width), slower (x`DecorSpeed` .45 of the table's fall) and keep a steady depth haze (`DecorHaze`). A landform never gets a lane-keeping band. Sections
1-11 below describe the interactive kind; section 13 the blending, per-variant rules and landforms.

Branch `feat/cover-boulders`. Code: `Pause/Assets/Scripts/Gameplay/Cover/`. Tests: `CoverBoulderTest*`, `ScreenFit` screens `cover-*`.
Art staging: `Pause/Assets/Art/Boulders~/<World>/` (masters, composites, crumble GIFs; the wired copies live in `Art/Resources/Boulders/<World>/`; see sections 12-13).

## 1. Idea (user request, 2026-10-13)

Rarely a huge boulder drifts slowly down the lane. The player can hide behind it: it blocks **everything** (player shots, enemy shots, beams, blasts, bodies)
and nothing can destroy it. The ship is blocked by it too (solid, slides round it) but takes no damage. When a **boss** arrives the boulder is destroyed
(a short rumble / crack / burst sequence) to clear the field, and none spawn during a boss fight. Every world has its own boulder (own material, own art,
own drift style, own tuning). Pink only in Space.

## 2. What a cover boulder is

* An **object of the board, not of a backdrop layer**: it is drawn *behind* the player, enemies and every shot (sorting order `BackdropCatalog.Spec.BoulderOrder`, just above the ground tile and under every other backdrop layer and the boss's approach order), so a shot that is stopped by it is seen to die on its face and the ship is never half hidden by it.
* **3-5x an enemy ship** wide (2.0 .. 3.0 u; the lane's rails are 2.61 u from the centre, the ship's reach is +-2.2 .. 2.45 u).
* **Solid shape** = one to four circles per variant fitted inside the art's silhouette (`boulder.json`, written by `scripts/verify_boulder_art.py`;
  they sit 6 % inside the art so the ship can tuck against the drawn edge). The shape rotates / sways / bobs with the art.
* **Pooled** (`CoverBoulders.Slots` = 2 boulder objects built once, one on screen at a time), nothing allocates per frame.
* Indestructible: no weapon, ultimate, secret power, mine blast, ramming or shockwave changes its state. Only the boss clear (section 8) and leaving the
  view end it. A shielded / boosted ship does not smash it either (it is not a hazard: no tag `Enimey` / `Astr`, never in `ClearTarget`).

## 3. Per-world design table

Sizes are the drawn art's width in world units (the ship's hull is ~0.8 u wide, a normal enemy ~0.7 u).
Speeds: the boulder's own fall down the screen and its sideways drift, u/s, **independent of the board's scroll** (it is a landmark you hide behind, not a
thing that rushes past). All numbers live in ONE place: `CoverBoulderTable` (section 9).

| World | Boulder (3 variants each) | Width u | Drift style | Fall / sideways u/s | Spawn gap s | Crack light / debris | Destruction concept |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Space | purple-lit cratered asteroid (the user's `asteroid_00..02`, magenta cracks, cyan rim) | 2.0 / 2.4 / 2.8 | **Tumble**: slow spin 3-7 deg/s either way | .50-.80 / +-.15 | 45-85 | magenta cracks, rock chips + magenta crumbs | it cracks along its magenta veins, bursts into chunks that keep glowing, crumbs drift off |
| Frost | ice mountain: broad two-peak crag / sheared iceberg block / twin-spire crag with a cave arch | 2.4 / 2.6 / 2.8 | **Slide**: never rotates, flat foot down, very slow, a slight lateral sway | .35-.60 / +-.10 | 50-90 | cold white-cyan light, ice shards + snow dust | the mountain splits down a crevasse, peaks topple off, snow cloud |
| Verdant | giant tree: broken trunk with root crown / floating root-ball with a canopy tuft / hollow trunk arch | 2.3 / 2.6 / 2.9 | **Sway**: pendulum +-3 deg, 9 s period, slow fall | .40-.65 / +-.12 | 45-85 | jade sap light, bark chips + leaves + spore puffs | the trunk splits, bark and roots fly, sap-light burst, leaves flutter |
| Ember | volcanic stone: obsidian spire / lava-veined slab / basalt column cluster | 2.2 / 2.6 / 2.8 | **Rock**: heavy rocking +-6 deg, 7 s period | .45-.75 / +-.10 | 45-85 | amber lava light, obsidian shards + slag drops | lava seams flare, it shatters into glowing slabs, ash cloud |
| Tide | reef: barnacled sea stack / coral rock arch / domed reef mound | 2.2 / 2.9 / 2.7 | **Bob**: vertical bob .12 u (6 s) + sway +-2 deg | .40-.65 / +-.15 | 45-85 | mint-aqua polyp light, coral chips + shells + bubbles | polyps flash, the reef bursts into coral and shell, bubbles rise |
| Storm | floating thunderhead rock: anvil crag with a cloud skirt / storm island with a ruined refinery stump / hail-stone cluster | 2.4 / 2.9 / 2.6 | **Hover**: bob .10 u (5 s) + slow lean +-1.5 deg, flicker | .45-.70 / +-.20 | 40-80 | storm-white / pale-yellow flashes, slate chips + hail + static | lightning runs through it, it blows into slate and hail, vapour puff |
| Desert | sandstone: flat-topped mesa / natural rock arch / strip-mined butte with girders | 2.5 / 2.9 / 2.6 | **Slide** (like Frost) with a faint dust trail | .35-.60 / +-.10 | 50-90 | sun-gold light in the cracks, sand + slabs + dust | strata crack, slabs fall away, a sand cloud rolls off |

A world with no vetted art is **not enabled** (`CoverBoulderTable.Enabled`): nothing spawns there (the system is testable everywhere through
`CoverBoulders.TestAnyWorld`, which borrows Space's art). On this branch only Space is wired (section 12). The fall speeds above are the first
draft; the shipped numbers are the table in `CoverBoulderTable.cs` (.6-1.1 u/s, i.e. 16-30 s on screen).

## 4. Interactions (what blocks, what passes)

"Blocked" = stopped at the boulder's face with a few world-coloured chips and a soft thud (rate limited); the boulder is never damaged.

### Player weapons (`ShipAttackRunner`, `AttackProjectile`)

| Weapon | Rule |
| --- | --- |
| Straight / seeker / homing / ricochet / javelin / fireball shots (comet, feather fan, gatling, fireball, seekers, lances, missiles, javelins) | **blocked**: the shot ends where its path meets a boulder (no burst damage; a fireball fizzles) |
| Rail slug, tractor beam | **truncated**: the beam's length ends at the first boulder in its column; nothing beyond is hit, nothing beyond is pulled |
| Blowtorch cone | **blocked by line of sight**: a target whose line from the nose crosses a boulder is not burned (the drawn cone is cosmetic) |
| Chain lightning | **blocked per hop**: a hop whose segment crosses a boulder is not taken (the chain ends) |
| Orbit disc | **blocked**: while the disc is inside a boulder it hits nothing |
| Ultimate (the cinematic homing volley) and its top-tier volley | **pass**: the volley is a cinematic reward that curves round cover; it never harms the boulder |
| Secret powers (shield pulse, EMP, clap, nova, black hole, tractor, decoy, blink dash, cloak) | **pass**: ring / field / self powers, not projectiles; the boulder is untouched. A blink that lands inside a boulder is pushed out to its face |
| Shield-burst shockwave (`ShieldShockwave`) | the wave is **blocked** (a body whose line from the ship crosses a boulder is not shoved); a shoved body that meets a boulder is a **crash** (below) |
| Hostile shots shot down by a player projectile | unchanged (a player shot stopped by a boulder shoots nothing down beyond it) |

### Hostile fire

| Source | Rule |
| --- | --- |
| Elite and boss projectiles (`IHostileShot`: bolts, shards, shells, slag, ricochets, roots...) | **blocked** by `HostileShots.Resolve` (popped at the face; a ricochet pops too) |
| Lasers / beams (`BossBeam`, `RailMineLaser`) and themed area hazards (jets, wave bands, blast rings, strike columns, lashes: `AttackHazard`) | **line of sight**: a hit on the ship by a hazard whose shooter (or mine) has a boulder between it and the ship does not land (no heart, no shield spent); the drawn beam is cosmetic. Hazards still hurt other enemies as before |
| Mine blasts | same rule (the mine / the blast origin is the shooter) |
| Bodies (enemy ships, rocks, chasers, elites in play, mines that are not on a rail) | **solid**: pushed out of every boulder each frame, they slide round it (their own movers keep running). No damage either way; bodies never pass through a boulder |
| Rail mines | ride their rail, which is never covered (boulders keep clear of the rails, section 7) |
| Shoved / shield-burst bodies and `ShoveCrash` carriers | **crash**: a body shoved into a boulder crashes there by the same roll as the rail crash (`ShoveCrash.RailChance(impact speed, weight)`, at most `RailMaxKillsPerWave` a release; the kill is the pilot's, paid, no death combo); a survivor scrapes (3 sparks) and is pushed out. Elites and bosses never die of it (an elite loses nothing: it is pushed out) |
| Contact with the ship | the ship is **blocked, never damaged**; see section 7 |
| Boss body and its entrance | the boulder is gone before the boss lands (section 8) |

## 5. Spawn rules (`CoverBoulderDirector`, gameS1 only, like `EliteDirector`)

* At most **one** on screen (`MaxAlive` 1). Next one after a random gap in the world's `gap` range (x 1 on loop 0, never shorter than 40 s).
* Never in the **first 20 s of a world** (this includes the 8 s calm start) and never while: `WorldEntry.Active`, a planetfall / lift-off / portal
  arrival (`SuspendsSpawning`), a world transition, an open portal / planet waiting (`PortalIsOpen`), the boss run-up (`SecondsLeftInWorld < 14`),
  `BossEncounter.Running`, the tutorial, the player dead, `ResumeFx` slow-mo, a cinematic ultimate.
* Never while an **elite is parked or lifting off** (their landing sites and entrances stay clear), and `EliteDirector` skips any landing site a
  boulder covers (`CoverBoulders.SiteCovered`).
* It enters from above the view (or, for drift styles with a sideways speed, from the top corner away from the rail it hugs), leaves out of the bottom
  or the side, and is released when fully outside the view with a margin.
* On a world change, a death or a scene change every boulder is removed at once (quick fade, not the destruction).
* Frozen world (finger up with pauses left, `timeScale 0`): nothing moves, same rule as every hazard.

## 6. Art contract (per world, three variants + destruction strip)

* `boulder.png` 2304 x 768 (three 768 cells), `break.png` 3840 x 2304 (row = variant, five 768 cells), transparent, TRUE resolution (see sections 12-13).
* Light from the upper left, **no outline** (section 13), the body at the backdrop's brightness, the foot fading out in a ground skirt, no pink outside Space.
* Frame 0 of each break row is the intact variant with first cracks lit; frame 4 is the last dust (8-35 % of the area).
* `scripts/verify_boulder_art.py <dir> --world <W> --cell 768 --manifest --sheet preview.png` checks all of it and writes the hit-circle manifest `manifest.json`;
  `scripts/tools/boulder_composite.py` composites the art over every backdrop variant at in-game scale, `boulder_crumble_gif.py` plays the break strip,
  `boulder_grade.py` re-grades into the backdrop range, `boulder_wire.sh <W>` copies the sheets to Resources and enables the world.
* Codex prompts: `docs/cover-boulders-codex/` (`common.md` + `worlds/<World>.md`, `run_world.sh <World>`), run headless one at a time.
* Runtime: the sheets are loaded per world on demand and unloaded when the world changes.

## 7. Safety: the ship can never be trapped or crushed

The ship follows the finger absolutely (`movePlayer`), so there is no physics push to pin it. The boulder only **filters the placement**:
`CoverBoulders.ResolveStep(want, prev, prevWant, ...)` moves the target out along the circle normal (or, near the centre, away from where the ship was), re-clamps
to the reach, and if the clamp put it back inside takes the nearest free exit on the same row, then the column. The hull may only **slide** round a boulder
(`SlideSpeed` 18 u/s): a held finger that comes out on the far side of it, or has been held off by it, walks the ship round the face instead of jumping; a
pause-teleport (finger lands more than `TeleportDistance` 3.2 u from where it was) places it at once.

Guarantee (tested by `CoverBoulderTest.Lanes / Safety / DynamicSafety / Screens`): **both sides** of a boulder keep `SideGap` = 1.0 u between its
circles and the drawn rail face, whatever its turn (the band `|cx| <= Edge - SideGap - boundingRadius` uses the rotation-proof bounding radius; the drift
turns round at its edges), so a hull (0.78 u wide) always fits beside it and a ship at a rail is never squeezed between boulder and rail. Boulder width is
shrunk on a narrow lane (`FitScale`: at most .62 of the rail-to-rail width and room for .25 u of sideways travel). For every finger on a grid x boulder
positions x variants x turns x six lane shapes `Resolve` returns a point outside every circle and inside the reach; a boulder falling onto a still finger
at the floor or a wall moves the ship continuously (<= .6 u a frame at the first touch, the rest at the slide speed) and the ship is back under the finger
when it has passed. A boulder still over the field when a boss arrives is destroyed.

## 8. Boss arrival (`CoverBoulders.OnBossBegin()`, one line in `BossEncounter.Setup`)

Every boulder starts a **destruction sequence** (about 1.4 s of world time), then is released:

1. **rumble** (.55 s): it shakes (+-.04 u, rising), a dark crack light flickers through the art (the world's crack colour, additive tint), a low thud
   (`Shockwave/shield_release` at .35) and a small camera kick;
2. **crack** (.35 s): break frames 0-1 play; 6 small chips peel off;
3. **burst** (.5 s): frames 2-4 play (the drawn strip), 18 world-coloured debris pixels fly out from the face, a bigger kick, the existing rock explosion
   sound (`collisionDetection.PlayExplosion`); the collision shape is gone from the first frame of the burst; shots / bodies are never frozen by it.

A world without drawn break frames plays the same sequence with the **procedural fracture**: the sprite is cut into 3 x 3 tile pieces (pooled sprites)
that part along the cracks. Never any beep or coin sound. No boulder is spawned during the fight, the outro or the portal wait.

## 9. Data / tuning (one place)

`CoverBoulderTable` (`Gameplay/Cover/CoverBoulderTable.cs`): one `Entry` per world (`Space` .. `Desert`, in `WorldManager.Worlds` order) with the art
folder, enabled flag, width per variant, drift style + numbers, gap range, crack / debris colours. `CoverBoulderTable.Validate()` (used by the test)
checks every world has an entry, ranges are sane, no pink outside Space. Global constants (first delay, max alive, the free-reach rule, the hide
radius, sound volumes) sit at the top of `CoverBoulders`.

## 10. Layer, cost, screens

* Sorting order `Spec.BoulderOrder` (ground plane + 3; `CoverBoulderTest.Sorting` holds it under every layer and gameplay order); the chips / debris pixels use order 28 (under shots at 30).
* `CoverBoulders.Step(dt)` is O(boulders x circles) with <= 4 circles, hostile shots are tested in `HostileShots.Resolve` (already O(shots)), bodies by
  `ClearTarget.Live`. No allocation after `Prewarm` (measured by `CoverBoulderTest.Cost`). 60 fps budget: well under .05 ms per frame on device-class code.
* ScreenFit: the boulder's width is clamped to `.62 x` the lane's rail-to-rail width, its path and the 1 u of free lane are checked on every device shape
  (`CoverBoulderTest.Screens`, and `cover-*` ScreenFit screens).

## 11. Tests (`CoverBoulderTest`, gate lane)

Blocking of every projectile kind and the beam truncations, indestructibility by every damage source, the ship blocked without damage and never
trapped (grid sweep + corner cases), bodies solid, crash rules, boss arrival destroys boulders and prevents spawns, the spawn rules per world
(rarity, first delay, blocked states), pooling / no allocations, world table completeness, no pink outside Space, ScreenFit device shapes.

## 12. Status

* **All seven worlds are drawn and wired** (Space the user's in-place asteroids; the six others Codex art at 768 px cells, revision 2 = blended into the backdrop,
  section 13). `CoverBoulderTable.enabled` is true for each; a world's backdrop variants that have no fitting boulder are explicitly `none` (section 13).
* Sheets: `boulder.png` 2304 x 768 (three 768 cells) and `break.png` 3840 x 2304 (row = variant, five cells), `manifest.json` (cell 768, hit circles, written by
  `scripts/verify_boulder_art.py <dir> --world <W> --cell 768 --manifest`). Import (`BoulderArtImporter`, `AndroidTextureDiet`): no mipmaps, bilinear, never clamped
  (4096), ASTC 4x4 on Android / iOS. Only the current world's pair loads (about 10.5 MB ASTC per world: 2.3 + 8.8; see the PR for the measured numbers).
* Resolution: 640-700 px wide per boulder for 2.2-2.9 u, i.e. 220-300 px/u, against Space's 150-220 px/u and the backdrops' ~70-100 px/u. Never block-upscaled
  (`verify_boulder_art.py` and `CoverBoulderTest.WorldArt` measure the 2x/3x/4x duplication score, < .97; real art scores ~.27).

## 13. Blending into the backdrop (revision 2, 2026-10-13/14)

The user rejected the first boulders (bright, pale-outlined, pasted on). Rules now:

* **Storm's landform has an animated effects layer** (`CoverStormFx`, pooled children, no per-frame allocations): 8 drifting cloud wisps round the base (front and back), 14 rain streaks + 5 hail pellets falling off the underside, 3 faint static arcs, a cloud shadow; atlas `Resources/Boulders/Storm/fx.png` (`scripts/tools/storm_fx_atlas.py`, 1024 x 1024, dithered pixel-hard alpha, deterministic). Everything is a function of the boulder's age and presence (grows with the emerge, sinks with the recede, gone the instant it fades). Preview: `CoverBoulderPreview.Run` renders the real runtime over the backdrop variants.
* **Storm alone carries a thin pale one-sided outline** (the user: 'the Storm boulder is good, it just needs an outline so it can be visible'): 4 px storm-white (#DEE6F2) on the top and sides of the solid silhouette, fading out along the base and cloud skirt (`scripts/tools/boulder_outline.py`, `CoverBoulderEntry.outline`, `verify_boulder_art.py --outline`).
* **Every other world: no outline, no rim halo.** The silhouette is the art's own edge; the foot of every boulder is ringed by the world's ground cover (floes, foliage, ash, foam,
  vapour, sand) that fades to transparent (a fog skirt); `verify_boulder_art.py` fails a pale rim and a body brighter than median luma 112.
* **Painted in the backdrop's own view, palette and value range.** Codex was given the world's four-variant contact sheet (`docs/cover-boulders-codex/refs/`), its
  landmarks atlas and mid layer; `scripts/tools/boulder_grade.py` (per-world parameters in `Boulders~/<W>/src~/grade.txt`) re-grades the finished sheets into the
  backdrop's range (Frost and Desert needed it most).
* **The four backdrop variants are different places**, so each world's entry has `shapes[v1..v4]` (bit mask of the boulder shapes that blend in; 0 = none),
  `tint[v1..v4]` (night / teal variants), `haze` and a one-line `notes[v]`. `CoverBoulderDirector.BackdropVariant(world)` reads the live backdrop set
  (`WorldBackdrop.Instance.Current.Variant`; 0 = unknown, e.g. a cross-fade: the union applies). `Blocked` returns `"backdrop"` for a `none` variant, `SpawnOne`
  only picks an allowed shape (never the same twice running while another fits). Coverage is not a goal: where nothing fits, nothing spawns.
* **Emerge / recede** (`CoverBoulder`): it spawns above the view and over `EmergeSeconds` (2.6) grows from .62 x to 1 x, from 0 to full opacity, out of a flat haze of
  the world's `haze` colour (the solid circles scale with it, it is still off screen); in the last `RecedeUnits` (1.8 u) above the view bottom it sinks back to .25
  opacity (opacity only: what blocks is never smaller than what is drawn). Smooth (smoothstep), no pop; `CoverBoulderTest.Emerge` measures the per-frame steps.
* No landmark anchoring: the ground layers scroll at <= .05 of the foreground speed and their landmarks are random pieces, so there is nothing to time a spawn to;
  the variant rule is the backdrop awareness.

### Coverage table (shape 0 / 1 / 2 = the three base shapes; none = no spawn)

| World | Backdrop | Shapes | Why |
| --- | --- | --- | --- |
| Space | v1 | 0+1+2 | open space: an asteroid belongs on every sky variant |
| Space | v2 | 0+1+2 | open space: an asteroid belongs on every sky variant |
| Space | v3 | 0+1+2 | open space: an asteroid belongs on every sky variant |
| Space | v4 | 0+1+2 | open space: an asteroid belongs on every sky variant |
| Frost | v1 | 0+1+2 | v1 broken pack ice and floes: the ice mountain drifts out of the floes (all three shapes) |
| Frost | v2 | none | v2 harbour wharf with wrecks and docks: industrial, a mountain would clash: NONE |
| Frost | v3 | none | v3 industrial pipe / rail lattice with refinery towers on frozen ground: NONE (the user: 'doesn't need any boulders') |
| Frost | v4 | 0+1+2 | v4 sheer ice cliffs and crevasses: natural ice mountain terrain (all three shapes) |
| Verdant | v1 | 0+1+2 | v1 jungle canopy with rivers: giant tree masses grow out of the canopy |
| Verdant | v2 | 0+1+2 | v2 mangrove islands and channels: tree masses and root-balls belong |
| Verdant | v3 | none | v3 ruined overgrown city grid: concrete and roads, a tree mass would clash: NONE |
| Verdant | v4 | 0+1+2 | v4 deep bioluminescent teal jungle: tree masses, tinted teal |
| Ember | v1 | 0+1+2 | v1 lava rivers between basalt plateaus: volcanic stone belongs |
| Ember | v2 | none | v2 foundry plant (pipes, vents, machinery): NONE |
| Ember | v3 | 0+1+2 | v3 ash ridges and lava dunes: volcanic stone belongs |
| Ember | v4 | 0+1+2 | v4 cracked caldera floor with a magma vortex: volcanic stone belongs |
| Tide | v1 | 0+1+2 | v1 storm-tossed open sea: a reef stack drifts in the swell |
| Tide | v2 | none | v2 rusted oil-platform field: industrial brown, a reef would clash: NONE |
| Tide | v3 | none | v3 sunken glowing city (buildings, streets, towers): a built-up city, NONE (the user) |
| Tide | v4 | 0+1+2 | v4 whirlpool bioluminescent ocean: reef belongs |
| Storm | v1 | 0+1+2 | v1 sheared cloud banks: a floating thunderhead rock sits in the deck |
| Storm | v2 | 0+1+2 | v2 towering cumulus field: thunderhead rock belongs |
| Storm | v3 | 0+1+2 | v3 cyclone eye over the deck: thunderhead rock belongs |
| Storm | v4 | 0+1+2 | v4 lightning fields: thunderhead rock belongs |
| Desert | v1 | 0+1+2 | v1 sand dunes: a rock outcrop belongs |
| Desert | v2 | none | v2 mesa canyon crossed by giant pipelines and mining rigs: an industrial complex, NONE |
| Desert | v3 | none | v3 cracked flats with mining scaffolds and rails (strip mine): industrial, NONE |
| Desert | v4 | 0+1+2 | v4 night dunes (blue): rock outcrop, tinted blue |
