# World weather and hazards

Branch `feat/world-weather-hazards`. Code: `Pause/Assets/Scripts/Worlds/Hazards/`. Tests: `WorldHazardsTest`. Art staging (not wired, awaiting
the user's vetting): `Pause/Assets/Art/Weather~/<World>/`. Codex prompts: `docs/weather-codex-prompts/`.

## 1. Idea (user request, 2026-10-13)

"Each world should have its own unique hazardous effects and its own unique weather effect." Decisions taken with the user:

* Hazards **push / pull only, never damage**. They shove or pull the player's ship, enemies, and every projectile.
* Two layers: **ambient weather** (always on, a look; no gameplay effect) and **hazard events** (rare, timed, always telegraphed).
* Hazards and boulders share the screen freely: boulders are decorative backdrop (see `docs/cover-boulders.md`) and the old cover seam is gone.
* Hazards **are allowed during boss fights** and push / pull bullets too (a tornado scatters a bullet hell, a sandstorm bends it sideways).
* Codex draws the effects; the user vets them before they are wired.

## 2. Audit: what already exists

The backdrop layer (`Scripts/Worlds/Backdrop`) already carries a lot of weather, all of it a **picture behind the board** with no gameplay
effect. It stays; the new ambient layer sits on top and reacts to hazards.

| World | Existing backdrop weather (kept as is) |
| --- | --- |
| Space | nebula clouds, twinkling stars, drifting debris / wrecks (`SpaceExtras`) |
| Frost | snow fields at three depths, cloud ceiling, BLIZZARD gusts (`FrostBackdrop`) |
| Verdant | spore-cloud ceiling, POLLEN GUSTS, drifting spores, fireflies (`VerdantBackdrop`) |
| Ember | ash-cloud ceiling, smoke palls, lava fire, eruptions (`EmberBackdrop`, `EmberAmbient`) |
| Tide | rain streaks, mist, spray, wave crests, whirlpool eyes (`TideBackdrop`, `TideAmbient`) |
| Storm | cloud ceiling, wisps, mist, wind-shear gusts, heavy rain sheets, lightning / strikes (`StormBackdrop`, `StormLightning`; atlases `weather`, `lightning`) |
| Desert | dust devils, grit gusts, sand plumes, grit rain, heat shimmer (`DesertBackdrop`, `DesertAmbient`) |

Nothing in the code base moves a thing because of weather. What gameplay already offers as seams (all reused, none changed in behaviour):

* `IHostileShot.ShotShovable / ShotShove(by)` (boss and elite projectiles move "their whole course with it") and `AttackProjectile.Shove(by)`
  (the player's shots): the shield burst's way of displacing a projectile. `HostileShots.All` is the registry of hostile shots.
* `ClearTarget.Live` (every enemy / rock), `EnemyShove` (lane-clamped body displacement), `EliteShip.Velocity`, `EnemyBrain.Base`
  (a hazard's brain only adds its offset, so a shove stays; a pilot flies back).
* The player ship follows the finger absolutely (`movePlayer`); `SecretPowerController.DashOffsetX` already shows the pattern of an offset added to
  the finger target. Hazards use the same: `WorldHazards.PlayerOffset`.

## 3. Layers

### 3.1 Ambient weather (`WeatherLayer`)

A pooled layer of small sprites (one `SpriteRenderer` each, built once, 40 at most, no per-frame allocation) drifting over the backdrop and under
the board (sorting order -8, below enemies, shots and the boulders' -7). It has **no gameplay effect and no visibility limit** (alpha <= .55, tiny
sprites). Particles are *advected by the live hazard field* (the same function the forces use, scaled), so a tornado visibly sucks the rain / sand
into itself.

| World | Ambient | Cue colour | Motion |
| --- | --- | --- | --- |
| Space | ion motes: tiny cool-white / cyan points, a few magenta (Space only) | ion cyan-white, magenta | slow upward drift |
| Frost | snow flakes, small and large | icy white | fall, lean with a slight wind, flutter |
| Verdant | pollen specks and a few leaves | green-white | lazy sine drift |
| Ember | rising embers and ash flakes | amber | rise, flicker |
| Tide | bubbles rising, a few rain streaks | aqua-white | bubbles wobble up, rain slants |
| Storm | rain streaks | storm white-violet | fast slant, wind lean |
| Desert | blown sand grains | sun-white / pale gold | horizontal blow with a gentle fall |

All numbers sit in `WorldWeatherTable.Ambient` (one place).

### 3.2 Hazard events (`WorldHazards`)

One event at a time. Each event has a **warning** (>= 1.5 s, in practice 2 s), a **live** phase and a **fade-out** (so a force never stops
abruptly). A field's strength is `base x envelope`, the envelope easing in over `RampIn` .8 s after the warning and out over `RampOut` 1 s.

## 4. Per-world design

Strengths are the field speed in u/s at the core for a body with multiplier 1. The lane is 5.2 u wide, the view 13 - 18 u tall.

| World | Hazard (id) | Shape | Concept | Warning | Live s |
| --- | --- | --- | --- | --- | --- |
| Space | `gravity_well` | Swirl R2.3 S2.6 | a drifting mass: pulls everything to its core, bends shots into curves | dark lens ring forms | 11 |
| Space | `solar_wind` | Front T5 S2.0 | an ion sheet sweeps down: shoves sideways | ion ribbons stream in from the side | 7 |
| Frost | `blizzard_front` | Front T6 | a white wall sweeps down: sideways drift plus a headwind that stalls (slows) shots and bodies | white edge streaks | 8 |
| Frost | `whiteout_eddy` | Swirl R2.0 S1.8 | an ice-squall eddy: mostly spin, hardly any pull | flurry spiral grows | 10 |
| Verdant | `spore_gust` | Front T4.5 S1.0 | a pollen cloud rolls through, gently bends shots | green-white haze enters | 8 |
| Verdant | `vine_updraft` | Column W1.8 | warm air column: lifts (up-screen), so falling shots hang and bodies rise | leaves spiral up | 9 |
| Ember | `ash_updraft` | Column W2.2 | heat column, pulsing lift | glow at the base, ash rises | 8 |
| Ember | `geyser_plume` | Column W1.2 | a lava geyser: three hard bursts, outward + up | amber glow bulges | 6 |
| Tide | `whirlpool` | Swirl R2.6 S3.0 | strong pull and spin | water dimples, ring of foam | 11 |
| Tide | `current_surge` | Front T5.5 S2.6 | a surge sweeps sideways | foam line rushes in | 6 |
| Storm | `tornado` | Swirl R2.0 S3.2 | a vortex crosses the field: pulls ships; **scatters bullets** outward and round | the vortex SPINS UP: clouds gather, it grows and accelerates | 12 |
| Storm | `gust_front` | Front T3.8 S2.2 | charged gust, fast; crackling edge, no damage | violet crackle on the side | 5 |
| Desert | `sandstorm_front` | Front T7 S3.0 | the big wall of sand: strongest sideways shove, bends bullets | ochre wall on the horizon | 9 |
| Desert | `dust_devil` | Swirl R1.5 S2.2 | small fast devil | dust spiral | 10 |


### 4.1 Reading the wind (clarity pass, user request)

"The wind stuff is good but it's hard to see how it comes together." Every swirl and front now draws what it does (`HazardVisuals`, one generic layer, pooled):

* a faint **radius ring** of dashes at the true edge of the pull (a swirl grows it in during the warning);
* **pull lines**: streaks and debris that spiral INWARD along the force (their direction is the force's direction), fewer and fainter while only warned;
* a front shows its **leading edge** (a row of dashes on the band's lower rim), its push streaks and debris; the warning streams in from the upwind side.
* **Storm tornado = the top-down vortex** (the user dropped the tilted side-view funnel: "keep the tornado top down like we had originally"): the approved Storm sheet's top-down vortex (`wx_hazard` row 0) now turns as a whole with continuous rotation (150 deg/s, a full turn in 2.4 s), a second, faster layer on top (0.72 scale, out of phase), the loop frames at 12 fps, an occasional soft inner-light flicker (under 15 %, once per 4.5 s), pull lines and debris spiralling inward, and the radius ring. The 2 s warning **spins it up** (it grows, brightens and its speed rises from rest; continuous into the live turn), so the warning teaches the mechanic. No extra art, no extra memory (the Storm hazard sheet, 4.7 MB ASTC while an event lives). `WorldHazardsTest.TornadoVortex` holds the spin-up, the constant live step, the frame order / wrap and the release.

Every world differs in **movement**: Space is slow pulls and curved fire; Frost slows and drifts; Verdant gently lifts and bends; Ember
lifts in pulses and bursts; Tide pulls hard and surges; Storm pulls and scatters; Desert shoves hardest.

## 5. Force model (`HazardForce`, pure functions)

A field has a centre `c` (moves at `vel`), an envelope `e` (0..1) and a shape. `Sample(field, p, target)` returns a velocity in u/s.

* **Swirl** (centre `c`, radius R): `d = |c - p|`, `w = (1 - d/R)^1.2`, inward `n = (c - p)/d`, tangent `t = perp(n)`. `v = S e w (pull n' + swirl t)`; the
  pull fades near the core (`n' = n * min(1, d/(.3R))`) so nothing jitters at the centre.
* **Front** (a horizontal band travelling down the screen, full lane wide): thickness T, centre `y` falling at `Speed`. `w = sin(pi * (1 - |p.y - y| / (T/2)))`
  inside the band (smooth edges). `v = S e w (Push)`, `Push` = (sx, sy) with the side `sx` picked at random (the warning shows it).
* **Column** (a vertical stream at x `c.x`, width W, from the bottom of the view up): `w = bell(|p.x - c.x| / (W/2))`.
  `v = S e w (spread * sign(p.x - c.x) * (1 - w), lift)`; pulsed by an optional `PulseHz` (`ash_updraft`) or gated into bursts (`geyser_plume`).

Each hazard carries per-target **multipliers** (`TargetMix`): the same field pushes a body and bends or scatters a bullet differently.

| Target | Rule |
| --- | --- |
| Player ship | `offset += v * mixPlayer * PlayerGain(.55) * dt`; `offset *= exp(-ReturnRate(2.2) dt)`; clamped to `PlayerCap` .85 u; applied as a finger-offset (`FollowTarget` / `moveLeft_Right`), then the ship's own reach clamp. Steady offset = `v * gain / return`, at most .85 u |
| Enemy bodies (ships, rocks, chasers) | displaced `v * mixBody * dt` (cap `BodyMaxSpeed` 2.4 u/s), lane-clamped like `EnemyShove`; a rock's size is its mass (`HazardSize.ShoveScale`); **never crashes or kills** (no `ShoveCrash`) |
| Pilots | engaging / entering pilots, x.5; they fly back to their line on their own (existing `ShoveReturnSpeed`) |
| Elites | `Velocity += v * mixElite(.3) * EliteAccel(3.0) * dt` while their own speed is below `EliteMaxSpeed` 6; their steering eases them back; never damaged |
| Bosses and their bodies | **not moved** (mix 0), the scripted path is untouched |
| Hostile shots (boss / elite `IHostileShot`, `ShotShovable`) | `ShotShove(v * mixShot * dt)` (cap `ShotMaxSpeed` 4 u/s); a tornado's mix has a **negative pull** and extra swirl, so shots fly outward and round (scatter); a front's mix bends them sideways |
| Player shots (`AttackProjectile`) | `Shove(v * mixMine(.5) * dt)`: aim is bent a little, never wrecked |
| Beams, lasers, strike columns, jets, landed pools, rail mines | unaffected (cosmetic or fixed) |
| Pickups / atoms | unaffected |
| Shield-burst throws | a body in a throw keeps its throw (`EnemyShove`); the field adds its own small push on top; both lane-clamped |

**No damage, ever:** the system has no code path that hurts a heart, kills a body or awards score; `WorldHazardsTest` holds it (hearts, kills, score
unchanged over a whole event).

## 6. Spawn rules (all numbers in `WorldWeatherTable.Schedule`)

* gameS1 only (not the tutorial), through `WorldHazardsBootstrap` like `EliteDirector`.
* The first event is never before `First` world seconds (22 - 34 s, so the 8 s calm start is well inside). Between events a random gap in the
  world's range (table 7); each loop shortens it (x 1 / (1 + .08 loop), at most x 1/1.3).
* **Never starts** during: `WorldEntry.Active` (planetfall / portal arrival), a lift-off, `WorldTransition.InProgress`, the open portal, the boss
  run-up (`SecondsLeftInWorld < 14`), a boss intro / outro (a boss that is not in `Fight`), the player dead, an **elite that is Parked / lifting off /
  joining** (their landing sites and entrances stay clean), or within `MinGapAfterElite` of an elite joining.
* **Boss fights allow hazards.** When the Fight phase starts the next event is brought forward to `BossFightFirst`; thereafter the normal gap.
* **Ends early** (fade-out in .6 s) if a hard block begins (transition, portal, death, boss intro); a boss fight starting
  does not end it.
* One event at a time. Frozen world (finger up, `timeScale` 0): nothing advances (the manager runs on `WorldManager.Flying`).
* Developer: `WorldHazards.Force(id)` starts one at once (tests, previews).

## 7. Rate tuning (`WorldWeatherTable`, the one data place)

| World | First (s into world) | Gap (s) | Boss-fight first (s) |
| --- | --- | --- | --- |
| Space | 34 | 70 - 110 | 20 - 32 |
| Frost | 30 | 60 - 95 | 18 - 30 |
| Verdant | 30 | 58 - 92 | 18 - 30 |
| Ember | 28 | 52 - 85 | 16 - 28 |
| Tide | 26 | 50 - 80 | 16 - 28 |
| Storm | 24 | 46 - 75 | 14 - 26 |
| Desert | 22 | 44 - 72 | 14 - 26 |

A world is about 120 s at baseline: one event, sometimes two. The hazard is random among the world's two (the one used last is not repeated).

## 8. Fairness and safety

* Telegraph: the warning is >= 1.5 s (`MinWarning`, held by the data check); fronts show their entry side and direction, swirls and columns show
  their spot, all in the world's cue colour (pink only in Space), a thin pale outline, no line boxes.
* **Never traps:** the player's displacement is a finger-offset capped at .85 u: the ship is always one finger move from anywhere; the return
  spring pulls the offset back as soon as the field is gone; the ship's reach clamp (rails, floor, ceiling) is final.
* Strengths are capped (`PlayerCap`, `BodyMaxSpeed`, `ShotMaxSpeed`, `EliteMaxSpeed`); a Swirl's pull fades at the core so nothing sticks or jitters.
* Hazards never change what spawns, only where things drift. Spawn protection and the boss budgets are untouched.
* Accessibility: the game has no reduced-motion setting. Two feature flags (Options developer list) exist: `WORLD HAZARDS` and `WORLD WEATHER`
  (both default on). `WeatherLayer.Reduced` (density x.4) is the hook a future player option would flip. Nothing flashes: alpha <= .6, no
  modulation faster than 1 Hz.

## 9. Performance

* Ambient: <= 40 pooled `SpriteRenderer`s built once; a step is arithmetic, no allocation.
* Hazard visuals: <= 24 pooled renderers (telegraph streaks, body streaks), built once.
* The force loops are the only per-frame cost that scales: over `HostileShots.All`, `AttackProjectile.Flying` and `ClearTarget.Live`. Each first does a
  **bounding-box cull** against the field's rectangle and only samples the force for those inside. `WorldHazardsTest.Cost` runs 2000 registered
  shots with no allocation and a time bound.
* One field at a time, capacity 2 (the second only during a fade-out), fixed arrays.

## 10. Risks and open balance questions

* **Hazard x boss bullet hell** is the big one: a tornado scattering a fan removes the authored pattern's gaps (easier or harder); a front bending
  it sideways could close one. Mitigations: `ShotMaxSpeed` cap, `BossShotScale` .6 on boss shots, no first event before 14 s of a fight. Needs feel
  testing on device.
* The player offset vs precise dodging: .85 u is small (the hull is .8 u wide). Tune `PlayerCap` / `PlayerGain` first.
* Fronts push enemies toward the rails; the lane clamp holds them in.

## 11. Integration points

* `movePlayer.FollowTarget / moveLeft_Right`: `+ WorldHazards.PlayerOffsetX / Y` next to the dash offset.
* **Boulders: no seam.** Boulders are decorative backdrop and have no effect on hazards (and hazards none on them): `HazardCoverSeam` / `CoverHazardQuery` were removed. `WorldHazardsTest.BoulderSeam` checks neither blocks the other.
* Boss branch: nothing to merge. Any boss shot class implementing `IHostileShot` with `ShotShovable` is pushed automatically; a shot that must
  resist the field returns `ShotShovable = false`.

## 12. Art and sound

**Art (Codex, headless): all wired.**

| Set | Status |
| --- | --- |
| Space sun (both palettes, flares) | APPROVED, wired (below) |
| Storm: top-down tornado vortex, gust front, ambient rain | APPROVED, wired (`Backdrop3/wx_hazard`, `wx_ambient`) |
| Tide: whirlpool, current surge, ambient bubbles / rain | APPROVED, wired (`Backdrop3/wx_hazard`, `wx_ambient`) |
| Space (gravity well, solar wind, motes), Frost (blizzard front, whiteout eddy, snow), Verdant (spore gust, vine updraft, pollen / leaves), Ember (ash updraft, geyser plume, embers / ash), Desert (sandstorm front, dust devil, sand) | APPROVED, wired (`wx_hazard`, `wx_ambient`) |
| **All seven worlds are wired; nothing is staged.** | |

Sheets: `hazard.png` (3072 x 1536, 512 px cells: row 0 hazard A loop, row 1 hazard B loop, row 2 the two warnings) and `ambient.png` (2048 x 512, 256 px cells, 4 kinds x 4
variants). The wired ones are `wx_hazard.png` / `wx_ambient.png` (+ `wx_ambient.json`, each particle's tight bounds) in `Art/Backgrounds/Resources/Worlds/<World>/Backdrop3/`,
loaded by `HazardArt` when an event of the world begins (released when it ends) and `WeatherArt` while the ambient layer shows the world. The Storm / Tide first deliveries were
2x block upscales and were redone by Codex at true resolution. Wiring is data (`WorldWeatherTable`: `artRow` / `artWarnCol` on a hazard, `artKind` on an ambient kind; loader folders from `BackdropCatalog.Folder`). Columns draw as stacked, overlapping tiles of the painted stream gated by the hazard's pulse; fronts as seamless tiles; swirls as one cell scaled to the diameter (the tornado alone also turns continuously, `spinDegrees`). Prompts: `docs/weather-codex-prompts/`; verifier `scripts/verify_weather_art.py` (includes the block-upscale check);
previews `docs/weather-previews/`. Wiring another world = copy its two sheets as `wx_*`, give its hazards `artRow` / `artWarnCol` and its ambient kinds `artKind` in `WorldWeatherTable`.

**Resolution rules (user: "everything very high rez"):** no downscaling, no block upscaling (`WeatherArtTest` runs the BackdropResolutionTest pixel-duplication check on every wired
cell of every world; wired import max size is not clamped, no mip maps). Import: `WorldBackdropImport` (4096 max, bilinear, ASTC 4x4 on phones; the sun disc RGBA32).
Memory per world, identical for all seven (RGBA32 / ASTC 4x4): hazard sheet 12.6 / 4.7 MB (only while an event lives), ambient sheet 4.2 / 1.0 MB (while in the world); the sun, one palette resident: about 19 MB (below). `WorldBackdropTest` budgets them (Storm / Tide weather sheets <= 8 MB, sun <= 20 MB).

**The Space sun: APPROVED and WIRED (both palettes).** `SpaceSunLayer` (`Scripts/Worlds/Backdrop/SpaceSun.cs`), owned by `BackdropSet` for Space, so the
menu's Space backdrop shows it too (dimmed like its tiles). A pure backdrop piece (no collider, no gameplay effect), at the screen's edge on the `planet_deep`
layer's sorting and depth, its disc kept >= 1.9 u off the lane's centre line (a 15 - 60 % edge of it shows on every phone shape).

* **Palette per run:** `SpaceSunPick` hashes the run's seed (`NewRun()` is called when gameS1 loads, before any backdrop is built): warm yellow/orange or violet/magenta, and
  the side (left / right). Stable for the whole run (every set built in it, the menu included, shows the same), varying across runs.
* **Motion:** the whole sun (disc, corona, flares: one root) turns once per `TurnSeconds` 210 on the backdrop's scaled clock (a paused game holds it), the corona
  breathes (scale +-2.5 %, alpha +-6 %, 9 s), the 8-frame flare flipbook loops at 6 fps, and every 28 - 58 s a BIGGER prominence arc (the flipbook again at 1.28x,
  out of phase, random angle) swells and fades over 8 s, one at a time. 4 renderers, 10 sprites, no per-frame allocation.
* **Low contrast (held by BossHeartsTest and AtomClarityTest for every palette x side, `PAUSE_SUN_FORCE=violet:right` pins one):** disc alpha .62, flares .5, arcs <= .5, corona .6, violet x .72, all under the checked art limits (`scripts/verify_sun_art.py`).
* **Memory (only the picked palette is loaded):** disc RGBA32 2.6 MB warm / 3.5 MB violet (the drawn cell cropped to its visible box by `scripts/crop_sun_disc.py`, pixels untouched; point filter, hard pixel art, never compressed), corona ASTC 4x4 2.1 MB, flare flipbook
  ASTC 4x4 8.4 MB: about 19 MB on phones (uncompressed all would be about 50 MB). ASTC 4x4 is used only on the soft additive layers, where it is visually identical
  (8 bits per pixel; the disc's hard pixel edges stay RGBA32). Import: `WorldBackdropImport` (max 4096, no mip maps, no clamping); `AndroidTextureDiet` leaves the disc alone.
* Tests: `SpaceSunTest` (both palettes load and import at full resolution, stable per-run pick varying across seeds, smooth rotation and no per-frame garbage,
  flipbook plays in order, arcs come one at a time, ScreenFit on 6 phone shapes, menu dimmed, teardown), `WeatherArtTest` (no block upscaling on the sheets).
* Preview: `docs/space-sun-in-backdrop.png` (both palettes in the Space backdrop at 0 s, 6 s and 24 s), GIFs of the flares in `Art/Weather~/SpaceSun/`.

**Sounds:** none added. Missing hazard cues (authored environmental sounds, never beeps / coins; the user does ambience): tornado howl, sandstorm hiss,
geyser burst, whirlpool suck, ion-wind swell, blizzard whine.

## 13. Tests (`WorldHazardsTest`)

Data completeness (every world has weather + hazards; warning >= 1.5 s; rates sane; no pink outside Space), force model per shape and per
target (bullets scatter / bend, ship displaced but capped and returning, bosses unmoved, elites eased, lane clamp), no damage, boulder avoidance
(seam both ways), rarity and spawn rules per world, boss-fight allowance, pool / no-alloc (editor noise bounded: see the test comment), performance with 2000 shots, ScreenFit (6 phone shapes: a front spans the
view, fields and particles stay inside it), flag off = nothing. `WeatherArtTest`: art sizes and the no-block-upscale check; `WorldHazardsPreview` renders
`docs/world-weather-hazards-preview.png` (the placeholder look).
