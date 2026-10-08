# Pixel Art Production Queue

Status is deliberately kept factual: **Queued**, **In progress**, **Staged for review**, **Approved**, or **Installed and validated**. Nothing moves to live art until it is reviewed and approved.

## Direction and safeguards

- Fresh, individual raster redraws only: rugged cyberpunk/steampunk pixel art, dense gunmetal/brass hardware, weathering, rivets, cables, sharp readable silhouettes, and world-specific emissive cores.
- Preserve only gameplay contracts: exact key/file name, canvas/frame grid, alpha, and animation states.
- Do not modify player ships.
- Open every completed family in Preview before installation.
- Delete legacy vector/source art only after every corresponding new live raster is installed and verified.

## Full production queue

Every numbered line is a separate commit/merge unit unless it explicitly says
**review** or **audit**.  The active item is the only art asset being changed.

1. **Installed and validated — `frost_fighter_4`:** Hailstorm commander.
2. **Installed and validated — `space_fighter_1`:** rugged Needle scout.
3. **Installed and validated — `verdant_fighter_1`:** rugged Gnat scout.
4. **Installed and validated — `space_alien`:** Bile Mite rebuilt taller; live strip is Preview-opened and roster-validated.
5. **Installed and validated — `frost_alien`:** Cryo Jelly rebuilt taller; live strip is Preview-opened and roster-validated.
6. **Installed and validated — `verdant_fighter_2`:** Wasp rebuilt as a compact, rugged bio-industrial stinger with an offset turbine, thorn-leaf wing, resin sac, and toxic reactor eye; live strip is Preview-opened and roster-validated.
7. **Installed and validated — `verdant_fighter_3`:** Mantis rebuilt as a rugged pruning mech with an iron boiler head, chartreuse reactor eye, and oversized chipped bone-and-steel scythes; live strip is Preview-opened and roster-validated.
8. **Installed and validated — `verdant_fighter_4`:** Hornet Queen rebuilt as a heavy, weathered bio-industrial matriarch with damaged leaf-metal wings, chipped pruning scythes, a toxic resin abdomen, and a queen stinger; live strip is Preview-opened and roster-validated.
9. **Installed and validated — `ember_fighter_1`:** Cinder rebuilt as a compact scorched forge-dart with jagged heat-shield plates, orange furnace vents, and a magenta furnace jaw; live strip is Preview-opened and roster-validated.
10. **Installed and validated — `ember_fighter_2`:** Scorch rebuilt as a scorched fork-claw with uneven molten vents and a sodium-white furnace core; live strip is Preview-opened and roster-validated.
11. **Installed and validated — `ember_fighter_3`:** Brand rebuilt as a lean scorched furnace flier with hooked ember-edged exhaust fins, a magenta heat slit, and a compact lower boiler; live strip is Preview-opened and roster-validated.
12. **Installed and validated — `ember_fighter_4`:** Pyre rebuilt as a scorched furnace-heavy with shield slabs, an uneven boiler stack, and a grille-contained sodium core; live strip is Preview-opened and roster-validated.
13. **Installed and validated — `verdant_alien`:** Snap Sprout rebuilt as a rugged bio-industrial seed crawler with thorn-leaf armor, a pruning jaw, resin sacs, copper root conduits, and a toxic magenta-green core; native seven-cell QA complete.
14. **Installed and validated — `ember_alien`:** Ember Imp rebuilt as a rugged soot-forge drone with basalt heat shields, copper bracing, orange vents, and a contained magenta furnace core; native seven-cell QA complete.
15. **Installed and validated — `space_chaser`:** Steel Hound rebuilt as a rugged gunmetal pursuit drone with copper conduit, cyan navigation slit, and magenta core; native seven-cell QA complete.
16. **Installed and validated — `frost_chaser`:** Frost Lancer rebuilt as a rime-armored copper pursuit lance with a cyan cryo core; native seven-cell QA complete.
17. **Installed and validated — `verdant_chaser`:** Dragonsting rebuilt as a thorn-leaf steel stinger with copper roots, resin sac, and toxic core; native seven-cell QA complete.
18. **Installed and validated — `ember_chaser`:** rugged forge-hound chaser with contained furnace jaw, copper loopwork, and safe seven-cell motion strip.
19. **Installed — `space_big`:** Bastion rebuilt as a rugged gunmetal armoured block with a caged magenta reactor core, copper conduit, twin antennae, and twin thrusters; seven-cell strip pixel-audited for alpha, cell margins (20 px minimum), and idle continuity, and roster-validated in the source worktree; in-engine review of the installed strip is pending, and the second tell cell and hit cell draw the hull about 9% and 5% smaller than idle.
20. **Installed and validated — `frost_big`:** Glacier Golem rebuilt as a rugged icebreaker-steel and glacier-ice hulk with copper pressure plumbing, a cyan cryo visor, asymmetric frost damage, and a crushing ice-slam tell; seven-cell strip vetted for safe cell margins, idle continuity, and roster validation.
21. **Installed and validated — `verdant_big`:** Bloom Maw in the rugged raster style, re-celled 2026-10-05 from `verdant_big_rugged_v2_concept.png` with `recell.py`: seven whole poses, one scale (0.762 of the sheet), frame 0 212 px = 1.08 u, 4 px clear margin; `EnemyRosterTest` passes (size band, palette, detail floor). The spin Codex asked for in `9db82918` is not in this strip: see the repairs section.
22. **Queued — `ember_big`**
23. **Installed, validated — `space_rock_crater`**
24. **Installed, validated — `space_rock_cluster`**
25. **Installed, validated — `space_rock_dark`**
26. **Installed and validated — `frost_rock_chunk`** — rugged copper-braced frozen industrial chunk with a cyan pressure core; seven-cell strip vetted for safe cell margins and flipbook continuity.
27. **Installed and validated — `frost_rock_rime`** — six-point rime cutter with riveted clamp rails, copper pins, and a pulsing cyan cryo-reactor; seven-cell strip vetted for continuity.
28. **Installed, validated — `frost_rock_shard`** — rugged Frost ice-shard industrial hazard; 7-frame strip vetted for safe cell margins and roster validation.
29. **Installed, validated — `verdant_rock_spore`** — rugged grass-capped bio-industrial Spore Rock; floating 7-frame strip preserves its mandated key silhouette and safe cell margins.
30. **Installed, validated — `verdant_rock_vine`** — rugged floating Verdant terrain wedge with root-cable vines; 7-frame strip vetted for safe cell margins and roster validation.
31. **Installed and validated — `verdant_rock_pod`** — rugged copper-braced thorn pod with a breathing resin seam; seven-cell strip vetted for safe cell margins and flipbook continuity.
32. **Installed and validated — `verdant_rock_knot`** — thorn-root hazard secured by corroded steel clamp collars and copper rivets around a pulsing bile heart; seven-cell strip vetted for continuity.
33. **Installed and validated — `ember_rock_magma`** — rugged basalt pressure boulder with copper braces and pulsing molten seams; seven-cell boundary and flipbook continuity checked.
34. **Installed and validated — `ember_rock_islet`** — rugged floating basalt shelf with slag, copper clamps, and molten seams; seven-cell boundary and flipbook continuity checked.
35. **Installed and validated — `ember_rock_cinder`** — rugged stacked cinder furnace blocks with copper plumbing and a molten core; seven-cell boundary and flipbook continuity checked.
36. **Installed and validated — `ember_rock_obsidian`** — rugged copper-braced violet obsidian blade with a molten internal vein; seven-cell boundary and flipbook continuity checked.
37. **Done (code; art gap open) — rail-mine audit** (`fix/rails-vetting`, `docs/enemy-behaviours.md` "Rails and view"): all 16 cells whole; mines now clamp to the drawn rail (centre 2.436 u, clamp 0.16 u inside the rail art) on both walls and phone shapes; the rail art scrolls at the board's rate so a mine no longer slides along it; slide + shove stay on the rail line. **Open art item:** the Ember row's lava is orange-red (42% of the dormant cell in the player's red band) and the Verdant row's thorns touch it (14%): recolour toward amber / magenta-pink.
38. **Queued, no art exists — impact/destruction VFX family:** enemy impacts, explosions, debris. Audited 2026-10-05: the live family is still the flat-vector `Weapons/Explosions.png` (16 x 6 cells of 128 px from `Art/Weapons/src~/weapons.py`; frames 1-2 of every material row are red-and-white stars), with `Weapons/AttackFx.png`, `ShipArt/DamageFx.png` and `Shield/shield_shards.png` in the same style. No rugged replacement has been generated or staged. The one sheet in Codex's output that resembles it (a 4 x 4 fireball, 2026-09-07) is a single directional red-orange burst, not the six-material ten-frame contract. Brief below.
39. **Queued, no art exists — projectile-hazard VFX family:** projectiles and telegraphs. Audited 2026-10-05: elite shots (and every roster enemy's shots once `feature/enemy-intelligence` merges) are code-drawn in `EliteFxArt` and tinted per world; boss rail sparks, muzzle flashes and tell rings are the generated 48 px `BossAttackFx/boss_attack_fx.png`; the boss laser's sight line and beam are flat-vector cells of `<World>_shots.png`. The only projectile-hazard image Codex generated (a cyan muzzle flash, 2026-10-04) was rejected by Codex because it includes a launcher body. Brief below.
40. **Queued (Codex work in progress) — Space boss support.** `Space_shots`: a finished rugged 8-sprite strip exists in Codex's generated images (2026-10-04) and is not installed from this branch; its bolt, shard and charge sprites fit the 8 x 128 px contract after packing, its telegraph and beam sprites do not (see brief 1). `Space_card`: not needed — the intro draws its name plate in code (`BossNameShapes`; `BossIntroTest` asserts the baked card is not shown), so the flat `Space_card.png` is dead art to retire under item 48.
41. **Queued (Codex work in progress) — Frost boss support.** `Frost_shots`: a finished rugged 8-sprite strip exists in Codex's generated images (2026-10-04) and is not installed from this branch; its bolt, shard and charge sprites fit the 8 x 128 px contract after packing, its telegraph and beam sprites do not (see brief 1). `Frost_card`: not needed — the intro draws its name plate in code (`BossNameShapes`; `BossIntroTest` asserts the baked card is not shown), so the flat `Frost_card.png` is dead art to retire under item 48.
42. **Queued (Codex work in progress) — Verdant boss support.** `Verdant_shots`: a finished rugged 8-sprite strip exists in Codex's generated images (2026-10-04) and is not installed from this branch; its bolt, shard and charge sprites fit the 8 x 128 px contract after packing, its telegraph and beam sprites do not (see brief 1). `Verdant_card`: not needed — the intro draws its name plate in code (`BossNameShapes`; `BossIntroTest` asserts the baked card is not shown), so the flat `Verdant_card.png` is dead art to retire under item 48.
43. **Queued (Codex work in progress) — Ember boss support.** `Ember_shots`: a finished rugged 8-sprite strip exists in Codex's generated images (2026-10-04) and is not installed from this branch; its bolt, shard and charge sprites fit the 8 x 128 px contract after packing, its telegraph and beam sprites do not (see brief 1). `Ember_card`: not needed — the intro draws its name plate in code (`BossNameShapes`; `BossIntroTest` asserts the baked card is not shown), so the flat `Ember_card.png` is dead art to retire under item 48.
44. **Queued — Frost world integration:** staged layers, parallax, spinning landmark, validation.
45. **Queued — Verdant world rebuild:** complete backdrop, walls, parallax, spinning landmark.
46. **Installed and validated — Ember world rebuild:** live parallax stack, ember/ash effects, freshly rebuilt mirrored forge-tower gameplay walls, and a refreshed eight-frame rugged refinery-volcano landmark atlas are wired through the existing Ember backdrop director; Ember-specific backdrop checks pass.
47. **Queued — Space world audit:** live backdrop and industrial rails.
48. **Queued — legacy cleanup audit:** delete a legacy source family only after its live replacement is verified.
49. **Installed (in game; device feel not yet checked) — Space elites ×4:** `space_elite_eventide_bastion`, `space_elite_orbit_reaver`, `space_elite_rift_lancer`, `space_elite_singularity_hauler` (`Art/Enemies/Elite/Space`, Codex's `manifest.md`). Codex delivered only `_concept` / `_motion_candidate` / `_strip_candidate` files; the 7-cell flight strips (engines off, idle glow, ignition, hover, bank L/R, damaged) were promoted unchanged to `space_elite_<name>.png` so `EliteArtSync` installs them into `Art/Resources/Elites/Space`. Muzzles and nozzles measured on the hover cell; `EliteTest` and `SpaceEliteTest` pass. They launch out of the Space backdrop's stations (Bastion, Lancer), planets (Reaver) and big asteroids (Hauler). **Open art items for Codex:** optional `<key>_liftoff.png` strips (the ship emerging from a hangar or surface: today a code fade-in from the ignition cell plus a procedural dock flare); `<key>_death.png` strips (today the generic debris cut from the damaged cell); a dedicated hangar-door / docking-light drawing on the station atlas would make the launch point read before the ship appears. The 4-cell `_motion_candidate` strips (booster engaged) are not used.

## Already approved / installed

- Space Fighters 1–4 and Frost Fighters 1–4 have been rebuilt against the rugged industrial reference and are live as the current quality benchmarks.
- Frost alien is live and validated alongside the Space Bile Mite.
- The four approved boss body atlases are installed and validated in game.
- Existing rail/mines are explicitly considered an approved visual quality bar.

## Cell-integrity re-cell (2026-10-04)

The first rugged installs of the strips below were cut from their free-layout concept sheets at
grid multiples and each cut was fitted to its cell on its own, so cells held slices of the
neighbouring pose, wide poses were clipped, and the body changed size and position through the
idle loop. Each live strip was rebuilt from its concept sheet with
`Pause/Assets/Art/Enemies/src~/recell.py` (one whole pose per cell, 4 px clear margin, idle frames
anchored on the body; no art redrawn) and passes `audit_cells.py` and
`EnemyRosterTest.CellsHoldOnePoseEach`. Not yet opened in Unity or checked on device.

"Frame 0" is the drawn size of the key pose on its longest side, before -> after. Frames 0-5 share
the listed scale of the concept sheet unless a frame is named with its own.

| Strip | Frame 0 | Scale | Own-scale frames | Size limited by |
| --- | --- | --- | --- | --- |
| `verdant_fighter_3` Mantis | 178 -> 177 px | 0.430 | hit 0.378 | nothing (size kept) |
| `ember_fighter_3` Brand | 178 -> 169 px | 0.467 | hit 0.410 | tell frame's flame on the idle baseline |
| `frost_fighter_3` Frost Kite | 178 -> 172 px | 0.404 | hit 0.343 | width of the beam frame (5) |
| `frost_fighter_2` Icicle | 160 -> 160 px | 0.469 | hit 0.461 | nothing (size kept) |
| `ember_rock_magma` | 178 -> 167 px | 0.488 | none | height of the burst frame (5) |
| `frost_alien` Cryo Jelly | 178 -> 177 px | 0.294 | none | nothing (size kept) |
| `frost_fighter_4` Hailstorm | 160 -> 159 px | 0.393 | burst (5) 0.345, hit 0.384 | nothing (size kept) |
| `verdant_fighter_2` Wasp | 178 -> 174 px | 0.408 | hit 0.368 | height of idle frame 3 |
| `verdant_fighter_4` Hornet Queen | 176 -> 168 px | 0.499 | tell (4) 0.453, strike (5) 0.424, hit 0.438 | idle wings on the shared baseline |
| `verdant_rock_pod` | 176 -> 172 px | 0.497 | none | height of idle frame 3 |

- `frost_fighter_2` and `frost_fighter_4` had already been through `df9658a1`, which only shrank
  each cell's existing content to a 16 px margin (178 -> 160 px); the neighbour slices and cut
  bodies stayed. They are rebuilt at that 160 px size.
- `frost_fighter_3`: one bolt of the hit debris lies nearer the beam tip of pose 5 than its own
  hull on the sheet and is assigned to the hit frame by hand (`--assign 1874,446:6`).
- `verdant_rock_pod` was rebuilt from `verdant_rock_pod_rugged_v2_concept.png`, which exists only
  uncommitted in the `rework/verdant-rock-pod-rugged` worktree, not in this repository's Staging.
- Still cut, not rebuilt: the elites `verdant_elite_resin_warden` (cell 3 fragment, cell 4 left
  wing), `ember_elite_brass_vulture` (cell 6 left side) and `ember_elite_ash_wraith` (nose in
  cells 4-6). Their source art is Codex's (`Art/Enemies/Elite`, copied over Resources by
  `EliteArtSync`) and their defs hold measured muzzle and nozzle points.

## Art repairs (2026-10-05)

Done with `Pause/Assets/Art/Enemies/src~/recell.py` / `audit_cells.py`; nothing redrawn. Verified by
`EliteTest` and `EnemyRosterTest` in batch mode; not played on device.

- **`verdant_big` Bloom Maw.** History: the first rugged install (`897fa461`) fitted each concept pose to its
  cell with its own x and y scale, which squashed poses 1 and 3 flat (the "flapping"); `9db82918` replaced it
  with a vector flower that spins 18 degrees a frame, drawn at 1.30 u with its petal tips cut by the cell in all
  seven frames. The live strip is the rugged concept again, every pose whole and round at one scale. Still
  owed by Codex if the spin is wanted: the concept's seven poses are all drawn at the same rotation, so a
  spinning rugged flower needs new drawings (or a runtime rotation); the concept's idle poses also breathe
  (the petals close about 6% on pose 1 and open about 6% on pose 2).
- **`verdant_elite_resin_warden`.** Rebuilt from `resin_warden_final_concept.png` (the `art/verdant-elite-ships`
  worktree). Hover cell at its old scale (0.451); parked, bank and damaged cells now share it; lift-off at
  0.385. Hull centred on the cell (it sat 10 px left). Cell 3's sliver and cell 4's cut wing are gone. Def:
  muzzles 44,64 / 149,63, nozzles 76,137 / 115,135.
- **Slivers removed** (whole detached components, ships untouched): `ember_elite_cauterizer` cells 4 and 5 at
  x 181-183 (102 px); `ember_elite_brass_vulture` cell 1 at x 8-9 and cell 5 at x 180-183 (43 px).
  `ember_big`: the hit pose's debris chunk that straddled the cell 5/6 boundary moved 5 px right into cell 6.
- **Elite hull radii** re-measured on the reworked art: `ember_elite_ash_wraith` 0.37 -> 0.29,
  `ember_elite_coalrunner` 0.37 -> 0.30, `ember_elite_cauterizer` 0.48 -> 0.43, with `heartOrbit` raised to
  keep the hearts on the orbit they had (1.08, 1.05, 0.73).
- **Still with Codex** (art, not repairable by moving pixels):
  - `ember_elite_coalrunner`: the action cell's (5) left muzzle flame is cut flat at x 8; its tip lies in
    cell 4 (x 168-183) and the right flame's tail in cell 6 (x 8-11).
  - `ember_elite_cauterizer`: the hull's left side in the hit cell (6) is cut flat at x 8 (38 px).
  - `ember_big` Magma Skull: the skull slides left through the idle loop (6, 8, 14 px off frame 0 in frames
    1-3, 18-20 px in 4-5); frame 0 has 2 px of margin on its right.
  - `space_big`: 12 texels on the bottom outline of cell 5.

## Space giant planets + moon rotation frames at high resolution (requested 2026-10-07)

**Owner: Codex. Status: requested; the code is ready for the drop-in.**

Why: the turning planets in Space (`anim.png` cells `giant_00`…`giant_11` and `rocky_00`…`rocky_03`)
are drawn much bigger than they were painted. On a 1170 px wide phone (the run's view is 7.44 u wide,
157 px/u) a near/hero giant is 2.97–4.13 u = 467–649 px across, from a 232–236 px cell: **2.0–2.8x
upscale**. Mid giants are 226–315 px (1.0–1.35x). The BackdropPlanet shader then slides the surface
across the disc, which stretches the art sideways again (1.3x on average now; it was 1.7x and up to 3.8x
before 2026-10-07). Rocky moons / planetoids are only drawn up to 157 px, so they are not upscaled; they
should still be re-rendered so the set matches. Nothing in the repo has these planets at a higher
resolution (`staging/anim_pixel_v1.png` is the same 1024 sheet; `reference_planet.png` is a different
planet), so this is new art.

Deliver:

- `Pause/Assets/Art/Backgrounds/Resources/Worlds/Space/Backdrop/anim_hires.png`: **2048 × 2048** RGBA,
  transparent background, 4 × 4 grid of **512 px cells** (better: 4096 × 4096 with 1024 px cells, but see
  the memory note), same cell order as `anim.png`: rows 1–3 are `giant_00`…`giant_11` left to right, row
  4 is `rocky_00`…`rocky_03`. Do not replace or edit `anim.png` / `anim.json`.
- `anim_hires.json` next to it, the same format as `anim.json` plus three fields:
  ```json
  { "pixelScale": 2, "sheetW": 2048, "sheetH": 2048,
    "sprites": [ {"n":"giant_00","x":20,"y":1568,"w":472,"h":448}, ... 16 entries ... ] }
  ```
  `pixelScale` = sheet pixels per pixel of the old 1x art (2 for 2048, 4 for 4096). Rects use Unity's
  **bottom-left** origin like `anim.json`, are **centred on the disc**, and are the disc's bounding box plus
  `2.5 × pixelScale` px of clear border all round (`build_atlas.py` does this for the 1x sheet).
  Order and names must be exactly `anim.json`'s.
- Each cell is the same planet as now, in the same neon pixel style: the same banded blue/violet gas
  giant with its station girder belt and lamps, the same cratered grey moons with their station, the same
  cyan rim glow on the lit (right) limb. Repaint it with real detail at the new size; do not upscale.
  Width/height ratio of each disc as in the 1x cell (about 1.05:1 for giants).
- The shader turns each cell by sliding its middle ±69° of longitude (`_Band` 1.2 rad) across the globe,
  and keeps the outer 14% of the radius (`_Rim` 0.86: limb glow and halo) as painted. So: put the detail
  in the middle band, keep the limb glow inside the outer 14%, and keep the lighting baked as now (light
  from upper right, ambient about 0.38). The shader cross-fades where the band wraps, so the band's left
  and right ends do not have to match.
- At runtime every cell is a separate **variant** (each planet picks one cell and turns it with the
  shader). The 12 giants do not play as a flipbook. If they are painted as a rotation sequence, close the
  loop (giant_11 → giant_00, rocky_03 → rocky_00) anyway, but this is optional.

Install (no code changes):

1. Copy both files into `Pause/Assets/Art/Backgrounds/Resources/Worlds/Space/Backdrop/` and open Unity
   (or reimport them). `WorldBackdropImport` gives `anim_hires` the planet sheet settings: mipmaps,
   trilinear, max size 4096, ASTC 4x4 on Android/iOS.
2. `BackdropSet.LoadAnimAtlas` picks `anim_hires` over `anim` when both files are there. Bodies keep their
   world size (`SpaceDirector.KindWidth` × tier scale). The sheet's pixel size changes only sharpness.
3. Check: `AllTests.RunSuites -suites SpacePlanetSheetTest,WorldBackdropTest` must pass.
   `SpacePlanetSheetTest.CheckHires` checks the same 16 names and order, `pixelScale >= 2`, cells equal to
   anim's × pixelScale (±4 px of 1x art), discs filling their centred cuts (±2 px of 1x art), and that the
   runtime loads anim_hires. `WorldBackdropTest` checks the per-world texture budget (9 MB). It counts
   anim_hires in place of anim.
4. Review: `Unity -batchmode -quit -projectPath <abs>/Pause -executeMethod SpacePlanetPreview.Run`
   writes each cell at hero size on a 1170 px phone to `Builds/PlanetPreview`. `SpaceBackdropPreview.Run`
   with `BACKDROP_PX=1170 BACKDROP_PY=2532 BACKDROP_FIT=gameplay` renders the whole backdrop.

Memory (phone, ASTC 4x4 with mips; desktop DXT5 is the same size): 1024² 1.3 MB (now), **2048² 5.3 MB**,
4096×2048 10.7 MB, 4096² 21 MB. RGBA32 would be 4× that. A 2048 sheet keeps Space at 9058 KB of its
9216 KB budget. A 4096 sheet needs the budget raised in `WorldBackdropTest.TextureBudgetBytes`, with a
note. Prefer 2048. (Checked 2026-10-08 with a temporary 2048 sheet: 9058 KB. The asteroid drift sheets on
`feature/space-asteroids-drift`, a 1024² `asteroid_drift.png` plus `asteroid_drift_fx.png`, add roughly
0.5 MB more, so once both are on master a 2048 `anim_hires` needs the budget raised a little too.)

Interim option, not installed: `Pause/Assets/Art/Worlds/Space/src~/build_anim_hires_interim.py` writes
a Lanczos 2x + unsharp copy of `anim.png` as `anim_hires.*`. Side-by-side renders showed it is only
slightly crisper than the 1x sheet, for +4 MB, so it was not shipped. Delete it once the real sheet is in.

## Outstanding art briefs (2026-10-05)

Common rules: rugged hand-placed pixel art on a transparent background, 1 px dark outline, painterly metal ramps, saturated neon cores; world accents Space magenta, Frost cyan, Verdant lime, Ember orange; never the player's red (#FF3E4E; keep hues at least 28 degrees away from it). One sprite per cell, centred, at least 6 px clear at every cell edge, same scale and anchor in every frame of a sequence.

1. **Boss laser cells, per world** (`<World>_shots.png` cells 4, 5, 6; 128 x 128 px each). Cell 4 is the lane telegraph (a dim warning band); cells 5 and 6 are two frames of the live beam. `BossProjectiles.Span` stretches each cell over the whole laser, 0.1-0.34 world units wide and up to 14 long, so each cell must be a pure vertical band: every pixel row identical, full cell height, no end caps, emitters, rings, sparks, thorns or rubble (those belong in `boss_attack_fx`). Vary only across the width: dark outline, coloured sheath, bright core. Frost and Ember currently show Space's magenta here.
2. **Enemy destruction atlas** (`Weapons/Explosions.png`, 2048 x 768, 16 columns x 6 rows of 128 px). Rows: Metal, Rock, Mine, Ice, Spore, Magma. Columns 0-9 of each row: one ten-frame burst, radial and undirected: anticipation spark, flash, fireball, then debris of that material flying out and fading. Row 0 columns 10-12 also hold a white flash star in two sizes and a white shockwave ring, which the game tints. No red in any row: Metal sparks white to magenta, Rock amber, Mine the world accent on a dark casing, Ice cyan, Spore lime, Magma orange to yellow.
3. **Hostile shot set** (new sheet, suggested `Elites/hostile_shots.png`, 6 columns x 2 rows of 64 px, drawn in white and greys so the game can tint it per world): bolt, shard, slag blob, shell, ground pool, lob landing ring; two frames each, shots pointing down the screen. Replaces the code-drawn `EliteFxArt` shots used by elites and roster enemies; needs a small loader change when it arrives.
4. **Shield release shockwave** (after `fix/shield-pickup-hitch-skins` merges): one white ring, 256 x 256 px, and one white vertical streak, 64 x 256 px, both tintable; replaces the Kenney soft particles `vfx_circle_05` and `vfx_trace_01`.
5. **Muzzle flash** (replaces Kenney `Prefabs/Vfx/vfx_muzzle_02`, only if `PowerFx` keeps using it): a standalone flash with no launcher body, 128 x 128 px, white core, tintable.
