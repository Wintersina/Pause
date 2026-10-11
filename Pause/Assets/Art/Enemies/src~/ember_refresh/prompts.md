# Ember enemy painted sources

The eleven `*_candidates.png` sheets were generated with the built-in image
generation tool. Each contains three distinct complete top-down concepts. The
zero-based selected columns below are sampled by `build.py`; every finished
strip is made from that painted concept, with nearest-neighbour resize and
local pixel motion. The only newly drawn marks are narrow rim wear, hard lamps,
tell rings, discharge marks, hit cracks and chips.

## Shared generation instructions

> Three different complete concepts side by side on a genuinely transparent
> canvas, one isolated top-down orthographic pixel-art sprite in each column,
> all facing down. Finely detailed mechanical silhouettes for a vertical phone
> shooter. One-pixel dark outline; two-pixel pale upper-left rim; matte
> soot-black basalt and gunmetal plating; weathered brass/copper structural
> trim; rich five-to-six-step shading per material; tiny rivets, panel seams,
> scratches and pitting. Small sun-white `#FFF1D0` cores and lenses with a
> narrow forge-orange edge (hue 16–30), restrained glow. No pink, magenta,
> player's red, pickup amber, cyan, green, violet, blue-dominant hull, glossy
> finish, soft orange bloom, scenery, text, labels, grid, shadow or interface.
> Three concepts, not three animation frames.

For rocks the prompt requested mineral facets and industrial repair hardware
instead of ship plating. Three sheets (`ember_big`, `ember_fighter_2`, and
`ember_rock_obsidian`) arrived with a faint semi-opaque gradient despite the
transparent request. Their raw generations are retained as `*_candidates.png`;
the `*_transparent_candidates.png` files are the isolated, transparent review
copies. `build.py` uses the same source-resolution extraction on the selected
columns. No blurred image pixels are used in the final strips.

| Key | Name | Selected column | Subject supplied to image generation |
| --- | --- | ---: | --- |
| `ember_alien` | Ember Imp | 0 | Floating cinder-sprite drone: curved basalt cage fins around a small sun-white core with a narrow orange edge. |
| `ember_chaser` | Cinder Fang | 0 | Compact furnace-hound drone: round riveted hull, two forward curved fang mandibles, one white eye, smoking exhaust stack. |
| `ember_big` | Magma Skull | 2 | Broad horned furnace-fortress skull from above: crown armor, brass jaws around a white maw, slit lamps, side drums. |
| `ember_fighter_1` | Cinder | 0 | Fast small dart-wedge diver: swept basalt hull, brass nose intake, one canopy lens, smoking fins. |
| `ember_fighter_2` | Scorch | 0 | Broad pincer gunship: curved crab-claw arms, heavy downward cannon, brass bands. |
| `ember_fighter_3` | Brand | 0 | Sleek swept raider: two long hooked horn-blades, narrow wing vents, small white lens. |
| `ember_fighter_4` | Pyre | 0 | Round heavy furnace dreadnought: grated eye, smokestack, four pylons, eight vent ports. |
| `ember_rock_magma` | Magma Rock | 0 | Irregular black basalt boulder with thin orange crack veins and brass staples. |
| `ember_rock_cinder` | Cinder Chunk | 2 | Slag-iron block with rusted pipe wraps and a small forge vent. |
| `ember_rock_obsidian` | Obsidian Shard | 1 | Long, narrow volcanic-glass blade with bright edge and fine orange veins. |
| `ember_rock_islet` | Lava Islet | 1 | Upright floating basalt platform fragment with lava drips, brass chimney, pipe stubs and pale smoke. |

`build.py` assembles four idle cells, two tell cells and one hit cell per strip.
`verify.py` prints the metric table and builds `preview.png`; `audit.py` is a
second entry point to the same audit. `verify_report.txt`, `cell_report.txt`
and `audit_report.txt` capture the final checks.
