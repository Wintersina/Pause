# Desert enemy candidate sheets

Each `*_candidates.png` was made with the built-in image generation tool as a
transparent sheet of three complete, isolated, top-down concepts. Shared
instruction: face down; rugged industrial pixel art; 1 px dark indigo outline;
2 px bone rim; detailed sand, glass, and matte copper material ramps; sun-white
lights; no background, labels, red, pink, violet, saturated cyan/green, or amber
glow. The corresponding `*_key.png` is the chosen painted crop at game size.

| Key | Requested concepts | Selected column (zero-based) |
| --- | --- | ---: |
| `desert_alien` | Mirage Wisp, layered bone/glass heat veil, sun-white core and streamers | 1 |
| `desert_chaser` | Scarab Drone, copper elytra, mandibles, legs, single lens | 0 |
| `desert_big` | Sandcrawler, twin treads, cabin, front bucket wheel, exhaust | 2 |
| `desert_fighter_1` | Sand Skimmer, narrow wedge, skid fin, intake and canopy | 1 |
| `desert_fighter_2` | Glint Hawk, mirror-panel wings and hooked prow | 2 |
| `desert_fighter_3` | Lens Gunner, round optical lens and three legs | 0 |
| `desert_fighter_4` | Dust Devil Tank, squat turret and swirling sand bands | 2 |
| `desert_rock_dune` | Dune Boulder, wind-cut sandstone strata | 0 |
| `desert_rock_spire` | Glass Spire, sheared sand-glass facets | 2 |
| `desert_rock_husk` | Fulgurite Husk, twisted hollow fused-sand tube | 0 |
| `desert_rock_slab` | Dune Slab, crescent crust with buried machinery and roots | 1 |

`build.py` crops the selected paintings, resamples only with nearest-neighbour,
cleans forbidden hues, adds localized part motion and small tell/hit marks, and
composes the seven square cells. `verify.py` prints the numeric acceptance table;
`audit.py --preview` rebuilds the art-only preview over the four Desert mid tiles
when that sibling worktree is available.
