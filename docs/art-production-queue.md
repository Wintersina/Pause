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

1. **Installed and validated — `frost_fighter_4`:** Hailstorm commander; rugged raster strip is live, Preview-opened, and roster-validated.
2. **Installed and validated — `space_fighter_1`:** rugged Needle scout strip is live, Preview-opened, and roster-validated.
3. **Queued — `verdant_fighter_1`**
4. **Queued — `verdant_fighter_2`**
5. **Queued — `verdant_fighter_3`**
6. **Queued — `verdant_fighter_4`**
7. **Queued — `ember_fighter_1`**
8. **Queued — `ember_fighter_2`**
9. **Queued — `ember_fighter_3`**
10. **Queued — `ember_fighter_4`**
11. **Queued — `verdant_alien`**
12. **Queued — `ember_alien`**
13. **Queued — `space_chaser`**
14. **Queued — `frost_chaser`**
15. **Queued — `verdant_chaser`**
16. **Queued — `ember_chaser`**
17. **Queued — `space_big`**
18. **Queued — `frost_big`**
19. **Queued — `verdant_big`**
20. **Queued — `ember_big`**
21. **Queued — `space_rock_crater`**
22. **Queued — `space_rock_cluster`**
23. **Queued — `space_rock_dark`**
24. **Queued — `frost_rock_chunk`**
25. **Queued — `frost_rock_rime`**
26. **Queued — `frost_rock_shard`**
27. **Queued — `verdant_rock_spore`**
28. **Queued — `verdant_rock_vine`**
29. **Queued — `verdant_rock_pod`**
30. **Queued — `verdant_rock_knot`**
31. **Queued — `ember_rock_magma`**
32. **Queued — `ember_rock_islet`**
33. **Queued — `ember_rock_cinder`**
34. **Queued — `ember_rock_obsidian`**
35. **Queued — rail-mine audit:** verify all four themed rows retain their approved quality and rail-clamped scroll behavior; rebuild only a failing row.
36. **Queued — impact/destruction VFX family:** enemy impacts, explosions, and debris sprite families.
37. **Queued — projectile-hazard VFX family:** enemy projectile and telegraph hazards.
38. **Queued — Space boss support:** `Space_shots`, `Space_card`, and animation-support audit.
39. **Queued — Frost boss support:** `Frost_shots`, `Frost_card`, and animation-support audit.
40. **Queued — Verdant boss support:** `Verdant_shots`, `Verdant_card`, and animation-support audit.
41. **Queued — Ember boss support:** `Ember_shots`, `Ember_card`, and animation-support audit.
42. **Queued — Frost world integration:** promote the staged sky/far/mid/flow/anim/fx layers, preserve parallax and spinning landmark behavior, validate.
43. **Queued — Verdant world rebuild:** sky, far, mid, flow, anim, fx, walls, parallax, and spinning landmark as one coherent world commit.
44. **Queued — Ember world rebuild:** sky, far, mid, flow, anim, fx, walls, parallax, and spinning landmark as one coherent world commit.
45. **Queued — Space world audit:** retain the live backdrop and industrial rails; fix only any visual/scrolling gap found in review.
46. **Queued — legacy cleanup audit:** remove a legacy SVG/source family only after its exact live raster replacement and tests are verified.

## Already approved / installed

- Space Fighters 1–4 and Frost Fighters 1–4 have been rebuilt against the rugged industrial reference and are live as the current quality benchmarks.
- Frost alien is live and validated alongside the Space Bile Mite.
- The four approved boss body atlases are installed and validated in game.
- Existing rail/mines are explicitly considered an approved visual quality bar.
