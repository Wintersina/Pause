# Ember backdrop v3 painted sources

Sixteen source paintings in this folder were generated with the built-in image generation tool: two terrain candidates and two transparent flow candidates for each place. The contact sheet records the visual review. Both terrain candidates contribute to the depth set: `a` anchors `mid`, `b` anchors `far`, and a softened mix supplies `sky`. Both transparent candidates supply `flow`.

## Shared prompt direction

Portrait 1:2 source for a seamless 512×1024 scrolling tile in a vertical phone shooter. Straight-down orthographic aerial view of Ember's surface from just under the ash-cloud ceiling. Terrain fills all edges; there is no horizon or outer space. Intricate hard-edged 16-bit pixel clusters; basalt and obsidian, bronze/copper/brass machinery, amber-gold lava, subdued brown-violet ash. Moody but clearly lit, low enough contrast for action. No crimson/red, green, teal, ice, stars, planets, stations, spacecraft, text, blur, photorealism, or large baked smoke plumes.

| Variant | Terrain candidate A | Terrain candidate B | Flow candidates A and B |
| --- | --- | --- | --- |
| v1 | Broken caldera mesas, winding lava rivers, crater lakes, stepped lava falls and pipe racks. | Alternate plateau and crater layout with more circular rims and stack clusters. | Separated cooling-crust rafts, molten glints and short surface shimmer arcs on transparency. |
| v2 | Orthogonal forge-city blocks, furnaces, rail yards, pipe bridges and lava canals. | Alternate hard-edged roof grid, huge circular furnace mouths and slag channels. | Disconnected slag-stream fragments, sparks and molten reflections on transparency. |
| v3 | Sweeping grey-brown ash dunes, thin ember rivers, buried ruins and fumaroles. | Alternate wind-ridge and half-buried machine arrangement with broad dune ribbons. | Fine diagonal ash streaks and a few tiny warm embers on transparency. |
| v4 | Dark glassy obsidian, partial glowing crater at the left edge, fissures and brass derricks. | Alternate giant crater rim at the top right and fractured glass fields. | Very sparse separated heat-refraction dashes and floating embers on transparency. |

`build.py` uses nearest-neighbor crop, discrete palette indexing, a depth-specific warm haze, hue control, paired two-axis edge blending, and stepped flow alpha. No existing Ember backdrop art is copied. `verify.py` checks exported size, RGBA mode, both exact wrap joins, value ranges, color restrictions and transparency, and creates a 2×2 repeated-tile review sheet.
