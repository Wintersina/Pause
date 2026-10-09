# Verdant backdrop v3 painted sources

All sixteen source PNGs in this folder were generated with the built-in image generation tool. The two `terrain` images per variant are alternate painted candidates, both used in the final ground layers. Two transparent flow candidates were also made per variant; the selected one supplies the overlay marks. The generator in `build.py` scales with nearest-neighbor sampling, indexes the palette, adjusts depth and value, and blends the tile edges. No existing Verdant backdrop art was copied.

## Shared constraints in each prompt

Portrait 1:2 composition, top-down aerial view from just below the spore-cloud ceiling, terrain filling every edge, no horizon or sky. Moody but clearly lit 16-bit game pixel art with crisp square clusters and a restrained palette; low enough contrast for ship, enemies, and bullets. No space, stars, planets, stations, ships, ice, red, photorealism, text, blur, or smooth gradients.

## Terrain candidates

| Source | Painted subject and reason selected |
| --- | --- |
| `v1_terrain_a.png` | Vast emerald canopy sea, winding lime-gold rivers, cliff-step waterfalls, copper pipe-trellis towers, vine-choked relay stacks. Strong forest and river read for `mid`. |
| `v1_terrain_b.png` | Alternate canopy mass and waterfall layout with denser rusted industrial structures. Used strongly in `far`. |
| `v2_terrain_a.png` | Braided wetland channels, lily-pad basins, mangrove roots, stilt refinery compounds and vine-swallowed tank farms. Island silhouette separates this place from v1. |
| `v2_terrain_b.png` | Alternate delta with broad waterways, marsh islands, corroded boardwalks and circular tanks. Used strongly in `far`. |
| `v3_terrain_a.png` | Collapsed orthogonal industrial city grid, cracked diagonal highways and rail viaducts, roof vines and canals. Strong road geometry for `mid`. |
| `v3_terrain_b.png` | Alternate ruined grid with overgrown rooftops, vertical gardens and corroded pipe runs. Used strongly in `far`. |
| `v4_terrain_a.png` | Deep night canopy, branching luminous lime roots and rivers, tiny cyan and magenta fungus and sparse industrial lamps. Strong night forest read for `mid`. |
| `v4_terrain_b.png` | Alternate dark teal canopy and glowing root network with small copper lamps. Used strongly in `far`. |

Each terrain prompt requested evenly distributed edge-to-edge texture without a central focal object or perspective convergence. The second candidate for each variant retained the same setting and palette while changing the arrangement of its dominant shapes. The selected `a` paintings anchor the nearer layer; `b` contributes more to the distant layer. Both contribute to `sky`, which is faded toward teal-green spore mist.

## Transparent flow prompts

| Source | Painted overlay request |
| --- | --- |
| `v1_flow_source.png` | Sparse floating spore haze and broken lime-gold river glints from above; narrow pollen streaks, stepped green-gold patches, single spores and leaf flecks with large transparent gaps. |
| `v2_flow_source.png` | Sparse slow swamp current lines and floating mats; tiny lily leaves, curved teal and muted lime water sheens, pollen flecks with large transparent gaps. |
| `v3_flow_source.png` | Sparse drifting jungle leaves and delicate pollen streams above ruined city; short diagonal green leaf trails and occasional magenta spores with large transparent gaps. |
| `v4_flow_source.png` | Sparse rising night spores; sinuous dotted lime, teal and tiny magenta streams with much transparent space. |

Each flow prompt explicitly requested isolated crisp 16-bit pixel-art marks on a fully transparent background, with no ground, horizon, white, or lettering. The static overlay artwork carries no baked animation or smoke plume.

The second flow candidates (`v*_flow_candidate_b.png`) requested still sparser versions of the same marks: scattered river glints for v1, isolated lily mats for v2, smaller windblown leaves for v3, and broken upward-curving luminescent spore trails for v4. Visual review favored the first candidate for v1 and v2 because its river and current motion read more clearly. For v3 and v4, the smaller second candidate is layered with the first to keep drifting leaves and spore trails visible at phone size without crowding the ground. `build.py` records those choices in `FLOW_SOURCE` and `FLOW_SECONDARY`.

## Review and finish

The selected paintings were visually checked together in a contact sheet before use. `build.py` retains the painted structure, limits each final layer to a compact indexed palette, gives `sky` the most haze, and uses paired edge blending for a repeatable two-axis wrap. `wrap_mid_vertical.png` is a repeated-tile visual check; numeric wrap and brightness checks are recorded in `manifest.json`.
