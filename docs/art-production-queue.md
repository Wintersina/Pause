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
38. **Queued — impact/destruction VFX family:** enemy impacts, explosions, debris.
39. **Queued — projectile-hazard VFX family:** projectiles and telegraphs.
40. **Queued — Space boss support:** `Space_shots`, `Space_card`, animation-support audit.
41. **Queued — Frost boss support:** `Frost_shots`, `Frost_card`, animation-support audit.
42. **Queued — Verdant boss support:** `Verdant_shots`, `Verdant_card`, animation-support audit.
43. **Queued — Ember boss support:** `Ember_shots`, `Ember_card`, animation-support audit.
44. **Queued — Frost world integration:** staged layers, parallax, spinning landmark, validation.
45. **Queued — Verdant world rebuild:** complete backdrop, walls, parallax, spinning landmark.
46. **Installed and validated — Ember world rebuild:** live parallax stack, ember/ash effects, freshly rebuilt mirrored forge-tower gameplay walls, and a refreshed eight-frame rugged refinery-volcano landmark atlas are wired through the existing Ember backdrop director; Ember-specific backdrop checks pass.
47. **Queued — Space world audit:** live backdrop and industrial rails.
48. **Queued — legacy cleanup audit:** delete a legacy source family only after its live replacement is verified.

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
