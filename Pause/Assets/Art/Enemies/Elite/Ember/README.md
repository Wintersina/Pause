# Ember Elite Ship Set

Art-only concept set. These are intentionally not wired into `Resources`, the enemy roster, or player hull systems.

Every final PNG is a transparent `1344 x 192` strip of seven equal `192 x 192` cells:
four idle frames, wind-up/tell, action, then hit/damage. Final silhouettes stay inset in each cell so no neighboring art can bleed into playback.

| Asset | Role | Silhouette note |
| --- | --- | --- |
| `ember_elite_sunstoke.png` | spear interceptor | Tall heat-lance nose, narrow split wings, two offset boiler thrusters. |
| `ember_elite_coalrunner.png` | compact gunship | Broad armored prow, side furnace nacelles, low heavy landing struts. |
| `ember_elite_brass_vulture.png` | salvage striker | Swept, hook-tipped wings, asymmetrical recovery claw and dorsal crane spine. |
| `ember_elite_kilnback.png` | armored hauler | Tall rectangular forge-pack, slab shield wings, hanging chain ballast. |
| `ember_elite_ash_wraith.png` | shadow skirmisher | Slim crescent hull, split exhaust tails, shrouded magenta core. |
| `ember_elite_cauterizer.png` | siege craft | Wide six-vent catamaran, central pressure cannon and layered heat shields. |

Shared Ember materials: soot-black forged steel, basalt armor, weathered copper clamps and ducting, sodium-orange furnace vents, and controlled magenta pressure cores. These are elite player-inspired forgecraft, not enemy recolors and not player-hull edits.

## Redesign 2026-10-16 (current)

Sunstoke, Coalrunner, Kilnback, Ash Wraith and Cauterizer were repainted from scratch with Codex (own palette each, no outline); Brass Vulture is unchanged. Sources, prompts and builder: `Art/Enemies/src~/ember_redesign/`. The old `_candidate` / `_concept` working files of the five repainted ships were removed.

## Refresh 2026-10-11 (superseded for all but Brass Vulture)

All six ships were redrawn in the Tide/Storm/Desert direction (top-down, nose up, restrained light, no pink core). Cells: 0-3 idle, 4 tell, 5 action, 6 hit. The death strips are scripted from each hull (`Art/Enemies/src~/build_ember_enemy_death.py`). The old art (`Art/Enemies/src~/ember_old/`) was removed in the 2026-10-25 art cleanup; it is in the repo history. Sources: `Art/Enemies/src~/ember_elites_refresh/`.
