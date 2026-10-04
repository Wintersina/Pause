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
15. **Queued — `space_chaser`**
16. **Queued — `frost_chaser`**
17. **Queued — `verdant_chaser`**
18. **Queued — `ember_chaser`**
19. **Queued — `space_big`**
20. **Queued — `frost_big`**
21. **Queued — `verdant_big`**
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
37. **Queued — rail-mine audit:** all four rows, art and rail-clamped scrolling behavior.
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
