# Space sky backdrop v4

Four new, opaque sky-only tiles for the Space world. Generated with the built-in image-generation tool using the supplied Space concept and current sky tiles as visual references. Existing planets, stations, comets, and other backdrop assets were not changed.

| File | Composition | Dimensions | Colours | PNG size | HSV value mean | p90 | p95 | Outer edge difference, top/bottom & left/right |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `sky_01.png` | Diagonal indigo/blue river with muted magenta heart and dark lanes | 896×1792 | 124 | 1,817,699 B | 0.1140 | 0.2706 | 0.3137 | 0 / 0 RGB levels |
| `sky_02.png` | Violet cloud wall, dark void, clustered stars | 896×1792 | 112 | 1,562,048 B | 0.1170 | 0.2471 | 0.2824 | 0 / 0 RGB levels |
| `sky_03.png` | Cooler blue band, central dust lane, sparse stars | 896×1792 | 120 | 1,023,269 B | 0.1229 | 0.2627 | 0.3216 | 0 / 0 RGB levels |
| `sky_04.png` | Broken violet-purple spiral dust ring with near-black centre | 896×1792 | 119 | 1,994,895 B | 0.1160 | 0.2471 | 0.3059 | 0 / 0 RGB levels |

All four images are RGB (opaque). Measurements are over all pixels; value is the maximum RGB channel divided by 255. The outermost top and bottom rows are identical, as are the left and right columns. The original generated images were 887×1774; they were resized to 896×1792 with nearest-neighbour sampling. Opposing 64-pixel top/bottom strips and 32-pixel left/right strips were matched for wrap, then all images were mapped without dithering to the same fixed 160-entry palette. The resulting tiles use 112–124 colours each. No art was painted by script.

The 8-row wrap-region mean absolute RGB differences are 0.966, 0.708, 0.675 and 0.845 levels, respectively. All p90 values are below 0.55, all means are within 0.08–0.20, all p95 values are below the art guide's 0.35 background limit, and all PNGs are below 2.5 MB. Pixels above value 0.6 are isolated star points and small four-point twinkles (0.035–0.131% of each tile); the cloud fields remain dim. There are zero saturated pixels above value 0.55 in the enemy-shot pink hue band (312–326°), and zero bright player-red or green pixels. Orange and cyan highlights are pinpoint stars only.

`preview.png` is a contact sheet with the four full tiles at half size, arranged left to right, and a phone mock on the right. Its phone view is a 360×640 crop from `sky_01.png` enlarged exactly 3× with nearest-neighbour sampling to 1080×1920.

The `verify_backdrop_tiles.py` helper was inspected but does not apply: it expects four `v1`–`v4` Backdrop3 folders with 512×1024 tiles, far/mid/flow layers, and masks. These standalone Space sky tiles were checked directly for dimensions, opacity, edge wrap, value distribution, colour count, file size, and reserved bright hues.

## Generation prompt set

1. Dark indigo-black deep-space sky; diagonal indigo and blue river nebula; restrained magenta-purple heart; bold near-black dust lanes; three hand-painted pixel-clustered cloud layers; tiny orange and cyan stars; seamless scrolling edges; sky only.
2. Dark pixel-art sky; violet-magenta cloud wall on one side; near-black void and loose star cluster on the other; curved dark dust trench; tiny stars; seamless scrolling edges; sky only.
3. Calm, cooler teal-blue band with a dark winding central lane, broad clear-space zones, sparse hot stars, crisp painterly pixel clusters, seamless scrolling edges; sky only.
4. Broken, irregular violet-purple spiral nebula with a nearly black centre, layered dust arcs and small orange/cyan stars, seamless scrolling edges; sky only.

All four prompts also required a portrait 1:2 tile, 4–6 dark tone ramps, no smooth blur or procedural fractal appearance, no foreground objects, rails, frames, ships, planets, text, or UI, and no large bright gameplay-colour regions.
