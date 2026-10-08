# Frost backdrop variants: painted sources and selections

All 24 candidates were made with the built-in image generation tool, two per
layer. This directory holds those source paintings. `build.py` selects one per
layer and follows run A's portrait crop, nearest-neighbour scale, offset splice,
discrete median-cut palette and stepped alpha workflow. The candidates are source
art, not exports. No candidate was used as a procedural stand-in.

## Shared prompt

Use case: `stylized-concept`. Source painting for a `512x1024` repeating
parallax ground tile in a vertical phone shooter. Orthographic camera directly
over Frost's planetary surface from atmosphere level under clouds. Crisp,
hand-placed-looking pixel clusters, hard material steps, navy/blue-violet
darkness, mid-dark steel-blue ice, weathered icebreaker metal, minimal brass,
and sparse cyan/magenta pin lights. Portrait `2:3` source with distributed
medium features rather than one oversized landmark. No horizon, outer space,
stars, planets, spacecraft, text, UI, white snowfields, blur, photorealism,
smoke or falling weather. The source will be darkened, palette-quantized and
made seamless after painting.

## V2: Coastline and harbour

Prompt additions: a connected ice-cliff shoreline surrounding a working dark
harbour, about half water and half coastal industry. Breakwaters, piers, gantry
cranes, tank farms, moored icebreakers and tiny harbour lights. Flow asks for
water-current contours and sparse detached floes, with no fixed quay.

| Layer | A candidate | B candidate | Chosen and reason |
| --- | --- | --- | --- |
| Sky | `v2_sky_a.png`: subdued mainland coast and bay in blue-violet haze. | `v2_sky_b.png`: broad dark water with coast mainly at right and tiny breakwaters. | A: quieter coast shape with stronger far-ground haze. |
| Far | `v2_far_a.png`: many piers edging a broad central bay. | `v2_far_b.png`: C-shaped harbour, long breakwaters and distant shore facilities. | B: clearer enclosing harbour silhouette. |
| Mid | `v2_mid_a.png`: scattered docks and icebreakers around open water. | `v2_mid_b.png`: working shore with dry dock, piers, ships, tank clusters and cliffs. | B: most legible port geometry at phone scale. |
| Flow | `v2_flow_a.png`: ice and channels with a fixed industrial quay. | `v2_flow_b.png`: separated floes and fine current contours in broad dark water. | B: clear transparent windows and no baked fixed infrastructure. |

## V3: Inland frozen industrial tundra

Prompt additions: continuous inland land rather than ocean. Refineries, round
tank farms, rail sidings, pipelines, tower fields, frozen lakes, access roads,
wind ridges and compound lights. Flow asks for narrow static snowdrift bands
with open gaps, not animated smoke or fog.

| Layer | A candidate | B candidate | Chosen and reason |
| --- | --- | --- | --- |
| Sky | `v3_sky_a.png`: flat hazy tundra, dim lakes and faint roads. | `v3_sky_b.png`: strongly sculpted ridges and broad lake basins. | A: quieter plane better suited to lowest parallax depth. |
| Far | `v3_far_a.png`: lake-heavy industrial plain and roads. | `v3_far_b.png`: land-dominant ridges with diagonal pipe/rail corridors and remote sites. | B: stronger inland silhouette and transport paths. |
| Mid | `v3_mid_a.png`: separated square refinery blocks around lakes. | `v3_mid_b.png`: diagonal railway, switching yards, tanks and pipeline grid. | B: clear graphic contrast with harbour and glacier compositions. |
| Flow | `v3_flow_a.png`: dense painterly snow streaks covering much of the tile. | `v3_flow_b.png`: sparse distinct pixel-art drift ribbons across dark ground. | B: stronger pixel edges and transparent gaps. |

## V4: Night glacier and crevasse country

Prompt additions: long broken crevasses and stepped ice walls, hairline cyan
depth glints, faint reflected blue-green aurora tint, tiny survey platforms,
bridges and ladders. The aurora stays a surface reflection: no visible sky
bands. Flow asks for static creep cracks, shards and low fissure frost-mist.

| Layer | A candidate | B candidate | Chosen and reason |
| --- | --- | --- | --- |
| Sky | `v4_sky_a.png`: vague slabs and cracks beneath violet haze. | `v4_sky_b.png`: more distinct stepped fissures and platforms. | A: lower-detail hazy foundation. |
| Far | `v4_far_a.png`: fragmented diagonal glacier blocks. | `v4_far_b.png`: long central branched fissure with subdued teal glints. | B: distinctive deep-crevasse silhouette. |
| Mid | `v4_mid_a.png`: towering ice walls and several readable ladder/bridge rigs. | `v4_mid_b.png`: two very broad parallel fissures and relatively few structures. | A: more detailed survey infrastructure and layered ice depth. |
| Flow | `v4_flow_a.png`: several thin cracks, shards and low cyan pockets in broad dark ground. | `v4_flow_b.png`: large slab fields filling much of the frame. | A: more open negative space for the flow strip. |
