# Frost backdrop v3 painted sources

Generated with the built-in image generation tool. Each candidate is a portrait
pixel-art painting. `build.py` selects one candidate per tile and uses nearest
neighbour scaling, an offset splice, discrete colour quantization, and stepped
alpha to produce the exported layers.

## Shared production brief

Directly overhead view of Frost's planetary surface from atmosphere level:
frozen ocean, cracked ice sheets, dark water leads, pressure ridges, drifting
floes, industrial coast, offshore rigs, rail and pipe causeways, and distant
lights. Rich, deliberately placed game pixels, limited stepped palettes, deep
navy/blue-violet/steel-blue bases and restrained cyan/magenta lights. No blur,
photorealism, white snowfields, text, outer space, stars, planets, nebulae,
comets, asteroids, spacecraft, space stations, or sky horizon. Distribute
features so no single landmark dominates a repeating tile.

## Candidates

| File | Painted prompt direction | Decision |
| --- | --- | --- |
| `sky_a.png` | Distant frozen ocean and glacier plains beneath blue-violet haze; branching dark leads; barely visible far-off industrial lights. | Selected: darkest and most diffuse ground plane. |
| `sky_b.png` | Small fractured ice fields with tiny shoreline lights and stronger coast fragments. | Rejected: distinct mountains compete with the far plane. |
| `far_a.png` | Distant ocean ice sheets, branching water leads, low pressure ridges, scattered floes, a few tiny industrial causeways and beacons. | Selected: restrained, legible middle depth. |
| `far_b.png` | Ice plates, coast fragments, tiny rigs and piped routes with atmospheric haze. | Rejected: large pale snow and cloudlike patches. |
| `mid_a.png` | Arctic industrial shore with offshore extraction platforms, refinery on land, pressure ridges, rail lines and tracks. | Rejected: a continuous shoreline dominates one side. |
| `mid_b.png` | Many small coast and offshore industrial sites amid ridges, dark leads, crawler tracks, rail and pipeline bridges. | Selected: varied detailed terrain. |
| `flow_a.png` | Dense broken ice field with sinuous current streaks and many cracked floes. | Rejected: too much ice to serve as a transparent flow strip. |
| `flow_b.png` | Sparse irregular floes over broad dark water channels, long slow current lines and chipped edges. | Selected: strongest flowing overlay. |

All candidates were generated with `transparent_background=false`; the flow
alpha was authored from the selected painting in `build.py`.
