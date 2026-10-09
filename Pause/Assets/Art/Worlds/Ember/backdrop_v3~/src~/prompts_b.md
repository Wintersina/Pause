# Ember backdrop v3, run B painted sources

Seven transparent cutout sheets were generated with the built-in image generation
tool, saved here as `landmarks_a.png`, `pipes_a.png`, `fires_a.png`,
`weather_a.png`, `sites_a.png`, `slagbarge_b.png`, and `flarestacks_b.png`.
`build_b.py` crops these painted subjects,
uses nearest-neighbour scaling and discrete colour ramps, applies the Ember
brightness and hue limits, and packs the Unity atlases. The machinery, lava,
weather, and site forms all originate in the generated paintings.

## Shared prompt direction

Use case `stylized-concept`. Production pixel art for *Pause*, a vertical 2D
shooter. Top-down to slightly oblique view from atmosphere level, beneath the
ash-cloud ceiling, onto a volcanic forge planet. Match Ember `v1`–`v4` ground:
black basalt and obsidian, brass/copper machinery, amber-gold lava, white-gold
furnace light, very sparse magenta warning lamps. Strong black/amber contrast,
crisp hand-painted pixel clusters, separate fully visible sprites on transparent
alpha, broad grid gutters. No labels, blur, smooth gradients, green, ice,
player red, stars, planets, or outer space.

## Family prompts and painted selection

| Family | Painted grid request | Selection |
| --- | --- | --- |
| Landmarks | 4×4 sheet: forge towers, three refineries, two cooling towers, smelter gantry, kiln domes, two lava derricks, slag barges, lavafall pump, rail viaduct, forgewell and lava intake. Each structure on its own basalt/lava terrain island, no baked smoke. | `landmarks_a.png` plus a separately painted second canal barge in `slagbarge_b.png`. The painted forgewell fills the otherwise unnamed sixteenth cell. |
| Pipes | 4×4 sheet: two straight runs, two bends, tee, cross, two valve manifolds, two trestle pipe bridges, pumphouse, burst steam and lava seams, molten metal pipe, two dense furnace tangles. Complete flanges, gauges, rivets, scorch, small status lights. | `pipes_a.png`. |
| Fires | 4×4 sheet: three frozen lava fountains, three eruption scars, two coal beds, two molten pools, two flare stack bases, two scorched industrial ruins, crawler firebreak, branching lava channel. | `fires_a.png` plus `flarestacks_b.png`, which replaces two initial stacks that had baked flames with open, glowing mouths for later animation. The painted lava channel fills the unnamed sixteenth cell. |
| Weather | 4×4 sheet: four ceiling cloud banks, four wisps, three drifting mist bands, three diagonal ash gusts, two smoke palls, ember and ash flake field. Warm amber crests and charcoal-brown shadows; airy low-alpha pixel clusters. | `weather_a.png`. Its final source slot painted smoke and sparks together; the builder separates them into two non-overlapping half-cell sprites to keep all 17 requested names. |
| Sites | 4×3 sheet: matching closed/open foundry hangar, canal bay, idle/active heat shield pad, closed/open tower-foot furnace bay, closed/open round basalt hatch, off/on pad lights. Centered apertures and stable pair silhouettes. | `sites_a.png`. |

The painted source files remain under `src~/` for review and reproducible atlas
packing. The Unity JSON rects measure y upward from the atlas bottom. Manifest
emitter, pipe-end, and emergence coordinates measure from the named rect's
top-left corner.
